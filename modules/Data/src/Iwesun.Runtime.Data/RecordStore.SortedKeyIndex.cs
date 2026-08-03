namespace Iwesun.Runtime.Data;

public partial class RecordStore<TValue, TPrimaryKey>
    where TValue : IRecordStoreValue
    where TPrimaryKey : notnull
{
    private enum SortedMutationKind : byte
    {
        None,
        Add,
        Remove,
        Replace
    }

    private sealed class InconsistentKeyComparerException(string keyName)
        : InvalidOperationException($"Key '{keyName}' has inconsistent equality and ordering comparers.")
    {
        public string KeyName { get; } = keyName;
    }

    private interface ISortedKeyIndex
    {
        IRecordKeyDefinition<TValue> Definition { get; }
        int SummaryCapacity { get; }
        void PrepareAdd(in Slot slot);
        void PrepareRemove(in Slot slot);
        void PrepareReplace(in Slot oldSlot, in TValue newValue);
        void CommitPrepared();
        void CancelPrepared();
    }

    private sealed class SortedKeyIndex<TKey>(RecordKeyDefinition<TValue, TKey> definition, int capacity)
        : ISortedKeyIndex
    {
        private readonly struct Entry(TKey key, StoreRecordId id)
        {
            public TKey Key { get; } = key;
            public StoreRecordId Id { get; } = id;
        }

        private readonly struct KeySummary(TKey key, int referenceCount)
        {
            public TKey Key { get; } = key;
            public int ReferenceCount { get; } = referenceCount;
        }

        private sealed class EntryComparer(RecordKeyDefinition<TValue, TKey> keyDefinition) : IComparer<Entry>
        {
            public int Compare(Entry left, Entry right)
            {
                var comparison = keyDefinition.CompareKeys(left.Key, right.Key);
                return comparison != 0 ? comparison : left.Id.Value.CompareTo(right.Id.Value);
            }
        }

        private readonly RecordKeyDefinition<TValue, TKey> _definition = definition;
        private readonly EntryComparer _entryComparer = new(definition);
        private readonly bool _requiresConsistencyIndex = RequiresConsistencyIndex(definition);
        private readonly BucketArena _equalityArena = new();
        private readonly StableBucketIndex<KeyToken> _equalityIndex = new(new KeyTokenComparer(definition));
        private Entry[] _entries = capacity == 0 ? [] : new Entry[capacity];
        private KeySummary[] _keySummaries = [];
        private int _count;
        private int _keySummaryCount;
        private SortedMutationKind _pendingKind;
        private Entry _pendingEntry;
        private int _pendingIndex;
        private int _pendingTargetIndex;
        private int _pendingOldSummaryIndex;
        private int _pendingNewSummaryIndex;
        private bool _pendingRemoveOldSummary;
        private bool _pendingInsertNewSummary;
        private bool _pendingEqualityAdd;
        private IndexEntryHandle _pendingEqualityAddHandle;
        private BucketRef _pendingEqualityAddBucket;
        private bool _pendingEqualityRemove;
        private IndexEntryHandle _pendingEqualityRemoveHandle;
        private BucketRef _pendingEqualityRemoveBucket;

        public IRecordKeyDefinition<TValue> Definition => _definition;
        public int Count => _count;
        public int SummaryCapacity => _keySummaries.Length;

        public void AddUnsorted(in Slot slot)
        {
            EnsureCapacity(_count + 1);
            _entries[_count++] = new Entry(_definition.GetTypedKey(slot.Value), slot.Id);
        }

        public void CompleteBuild()
        {
            Array.Sort(_entries, 0, _count, _entryComparer);
            if (_requiresConsistencyIndex)
                BuildEqualityIndex();
            else
                ValidateAdjacentKeySemantics();
            BuildKeySummaries();
        }

        public TKey[] GetKeys()
        {
            if (_keySummaryCount == 0) return [];
            var result = new TKey[_keySummaryCount];
            for (var index = 0; index < _keySummaryCount; index++) result[index] = _keySummaries[index].Key;
            return result;
        }

        public int GetKeyCount() => _keySummaryCount;

        public int GetValueCount(TKey key)
        {
            GetRange(key, out var start, out var end);
            return end - start;
        }

        public StoreRecordId GetRecordId(int index) => _entries[index].Id;

        public void PrepareAdd(in Slot slot)
        {
            EnsureNoPendingMutation();
            var entry = new Entry(_definition.GetTypedKey(slot.Value), slot.Id);
            var insertionIndex = FindEntryInsertionIndex(entry);
            _pendingNewSummaryIndex = FindSummaryIndex(entry.Key, out var summaryExists);
            if (_requiresConsistencyIndex) ValidateCandidateKey(entry.Key, summaryExists);
            _pendingInsertNewSummary = !summaryExists;
            EnsureCapacity(_count + 1);
            if (_pendingInsertNewSummary)
            {
                EnsureSummaryCapacity(_keySummaryCount + 1);
                if (_requiresConsistencyIndex) PrepareEqualityAdd(entry.Key);
            }
            _pendingEntry = entry;
            _pendingIndex = insertionIndex;
            _pendingKind = SortedMutationKind.Add;
        }

        public void PrepareRemove(in Slot slot)
        {
            EnsureNoPendingMutation();
            var entry = new Entry(_definition.GetTypedKey(slot.Value), slot.Id);
            _pendingIndex = FindExactEntry(entry);
            _pendingOldSummaryIndex = FindSummaryIndex(entry.Key, out var summaryExists);
            if (!summaryExists)
                throw new InvalidOperationException($"Sorted index '{_definition.Name}' has no key summary for a stored entry.");
            _pendingRemoveOldSummary = _keySummaries[_pendingOldSummaryIndex].ReferenceCount == 1;
            if (_pendingRemoveOldSummary)
            {
                if (_requiresConsistencyIndex) PrepareEqualityRemove(entry.Key);
            }
            _pendingKind = SortedMutationKind.Remove;
        }

        public void PrepareReplace(in Slot oldSlot, in TValue newValue)
        {
            EnsureNoPendingMutation();
            var oldEntry = new Entry(_definition.GetTypedKey(oldSlot.Value), oldSlot.Id);
            var newEntry = new Entry(_definition.GetTypedKey(newValue), oldSlot.Id);
            if (_definition.KeysEqual(oldEntry.Key, newEntry.Key))
            {
                if (_definition.CompareKeys(oldEntry.Key, newEntry.Key) != 0)
                    throw new InconsistentKeyComparerException(_definition.Name);
                return;
            }

            var oldIndex = FindExactEntry(oldEntry);
            var insertionIndex = FindEntryInsertionIndex(newEntry);
            if (insertionIndex > oldIndex) insertionIndex--;
            _pendingOldSummaryIndex = FindSummaryIndex(oldEntry.Key, out var oldSummaryExists);
            if (!oldSummaryExists)
                throw new InvalidOperationException($"Sorted index '{_definition.Name}' has no key summary for a stored entry.");
            _pendingRemoveOldSummary = _keySummaries[_pendingOldSummaryIndex].ReferenceCount == 1;
            _pendingNewSummaryIndex = FindSummaryIndex(newEntry.Key, out var newSummaryExists);
            if (_requiresConsistencyIndex)
            {
                ValidateCandidateKeyAfterRemoval(
                    newEntry.Key,
                    newSummaryExists,
                    _pendingOldSummaryIndex,
                    _pendingRemoveOldSummary);
            }
            _pendingInsertNewSummary = !newSummaryExists;
            if (_pendingRemoveOldSummary && _pendingOldSummaryIndex < _pendingNewSummaryIndex)
                _pendingNewSummaryIndex--;
            if (_pendingInsertNewSummary) EnsureSummaryCapacity(_keySummaryCount + 1);
            if (_requiresConsistencyIndex)
            {
                if (_pendingRemoveOldSummary) PrepareEqualityRemove(oldEntry.Key);
                if (_pendingInsertNewSummary) PrepareEqualityAdd(newEntry.Key);
            }
            _pendingEntry = newEntry;
            _pendingIndex = oldIndex;
            _pendingTargetIndex = insertionIndex;
            _pendingKind = SortedMutationKind.Replace;
        }

        public void CommitPrepared()
        {
            switch (_pendingKind)
            {
                case SortedMutationKind.None:
                    return;
                case SortedMutationKind.Add:
                    CommitSummaryAdd(_pendingEntry.Key, _pendingNewSummaryIndex, _pendingInsertNewSummary);
                    if (_pendingIndex < _count)
                        Array.Copy(_entries, _pendingIndex, _entries, _pendingIndex + 1, _count - _pendingIndex);
                    _entries[_pendingIndex] = _pendingEntry;
                    _count++;
                    break;
                case SortedMutationKind.Remove:
                    CommitSummaryRemove(_pendingOldSummaryIndex, _pendingRemoveOldSummary);
                    if (_pendingIndex + 1 < _count)
                        Array.Copy(_entries, _pendingIndex + 1, _entries, _pendingIndex, _count - _pendingIndex - 1);
                    _entries[--_count] = default;
                    break;
                case SortedMutationKind.Replace:
                    CommitSummaryRemove(_pendingOldSummaryIndex, _pendingRemoveOldSummary);
                    CommitSummaryAdd(_pendingEntry.Key, _pendingNewSummaryIndex, _pendingInsertNewSummary);
                    if (_pendingTargetIndex < _pendingIndex)
                    {
                        Array.Copy(
                            _entries,
                            _pendingTargetIndex,
                            _entries,
                            _pendingTargetIndex + 1,
                            _pendingIndex - _pendingTargetIndex);
                    }
                    else if (_pendingTargetIndex > _pendingIndex)
                    {
                        Array.Copy(
                            _entries,
                            _pendingIndex + 1,
                            _entries,
                            _pendingIndex,
                            _pendingTargetIndex - _pendingIndex);
                    }
                    _entries[_pendingTargetIndex] = _pendingEntry;
                    break;
                default:
                    throw new InvalidOperationException("Unknown sorted index mutation.");
            }
            CommitPreparedEqualityChanges();
            CancelPrepared();
        }

        public void CancelPrepared()
        {
            _pendingKind = SortedMutationKind.None;
            _pendingEntry = default;
            _pendingIndex = 0;
            _pendingTargetIndex = 0;
            _pendingOldSummaryIndex = 0;
            _pendingNewSummaryIndex = 0;
            _pendingRemoveOldSummary = false;
            _pendingInsertNewSummary = false;
            CancelPreparedEqualityAdd();
            _pendingEqualityRemove = false;
            _pendingEqualityRemoveHandle = default;
            _pendingEqualityRemoveBucket = default;
        }

        public void GetRange(TKey key, out int start, out int end)
        {
            start = FindLowerBound(key);
            if (_requiresConsistencyIndex) ValidateQueryKey(key, start);
            end = FindUpperBound(key, start);
        }

        private int FindLowerBound(TKey key)
        {
            var low = 0;
            var high = _count;
            while (low < high)
            {
                var middle = low + ((high - low) >> 1);
                if (_definition.CompareKeys(_entries[middle].Key, key) < 0)
                    low = middle + 1;
                else
                    high = middle;
            }
            return low;
        }

        private int FindUpperBound(TKey key, int low)
        {
            var high = _count;
            while (low < high)
            {
                var middle = low + ((high - low) >> 1);
                if (_definition.CompareKeys(_entries[middle].Key, key) <= 0)
                    low = middle + 1;
                else
                    high = middle;
            }
            return low;
        }

        private int FindEntryInsertionIndex(Entry entry)
        {
            var low = 0;
            var high = _count;
            while (low < high)
            {
                var middle = low + ((high - low) >> 1);
                if (_entryComparer.Compare(_entries[middle], entry) < 0)
                    low = middle + 1;
                else
                    high = middle;
            }
            return low;
        }

        private int FindExactEntry(Entry entry)
        {
            var index = FindEntryInsertionIndex(entry);
            if (index >= _count || _entryComparer.Compare(_entries[index], entry) != 0)
                throw new InvalidOperationException(
                    $"Sorted index '{_definition.Name}' does not contain record ID {entry.Id.Value}.");
            return index;
        }

        private int FindSummaryIndex(TKey key, out bool found)
        {
            var low = 0;
            var high = _keySummaryCount;
            while (low < high)
            {
                var middle = low + ((high - low) >> 1);
                if (_definition.CompareKeys(_keySummaries[middle].Key, key) < 0)
                    low = middle + 1;
                else
                    high = middle;
            }
            found = low < _keySummaryCount && _definition.CompareKeys(_keySummaries[low].Key, key) == 0;
            return low;
        }

        private void BuildEqualityIndex()
        {
            for (var index = 0; index < _count; index++)
            {
                var key = _entries[index].Key;
                var token = new KeyToken(key);
                var equalityMatch = _equalityIndex.TryCapture(
                    token,
                    out _,
                    out _,
                    out var hashCode);
                var orderingMatch = index > 0
                    && _definition.CompareKeys(_entries[index - 1].Key, key) == 0;
                if (equalityMatch != orderingMatch)
                    throw new InconsistentKeyComparerException(_definition.Name);
                if (equalityMatch) continue;
                var bucket = _equalityArena.Create();
                var handle = _equalityIndex.PrepareAdd(token, hashCode, bucket);
                _equalityIndex.CommitPrepared(handle);
            }
        }

        private void ValidateAdjacentKeySemantics()
        {
            for (var index = 1; index < _count; index++)
            {
                var compareEqual = _definition.CompareKeys(_entries[index - 1].Key, _entries[index].Key) == 0;
                var equalityEqual = _definition.KeysEqual(_entries[index - 1].Key, _entries[index].Key);
                if (compareEqual != equalityEqual)
                    throw new InconsistentKeyComparerException(_definition.Name);
            }
        }

        private static bool RequiresConsistencyIndex(RecordKeyDefinition<TValue, TKey> keyDefinition)
        {
            if (keyDefinition.EqualityComparer is StringComparer equalityStringComparer
                && keyDefinition.Comparer is StringComparer orderingStringComparer
                && ReferenceEquals(equalityStringComparer, orderingStringComparer))
                return false;

            var type = Nullable.GetUnderlyingType(typeof(TKey)) ?? typeof(TKey);
            var usesDefaults = ReferenceEquals(keyDefinition.EqualityComparer, EqualityComparer<TKey>.Default)
                && ReferenceEquals(keyDefinition.Comparer, Comparer<TKey>.Default);
            if (!usesDefaults) return true;
            return !(type.IsPrimitive
                || type.IsEnum
                || type == typeof(decimal)
                || type == typeof(DateTime)
                || type == typeof(DateTimeOffset)
                || type == typeof(TimeSpan)
                || type == typeof(Guid));
        }

        private void PrepareEqualityAdd(TKey key)
        {
            if (_pendingEqualityAdd)
                throw new InvalidOperationException("An equality index addition is already prepared.");
            var token = new KeyToken(key);
            if (_equalityIndex.TryCapture(token, out _, out _, out var hashCode))
                throw new InconsistentKeyComparerException(_definition.Name);
            var bucket = _equalityArena.Create();
            try
            {
                _pendingEqualityAddHandle = _equalityIndex.PrepareAdd(token, hashCode, bucket);
                _pendingEqualityAddBucket = bucket;
                _pendingEqualityAdd = true;
            }
            catch
            {
                _equalityArena.Release(bucket);
                throw;
            }
        }

        private void PrepareEqualityRemove(TKey key)
        {
            if (_pendingEqualityRemove)
                throw new InvalidOperationException("An equality index removal is already prepared.");
            if (!_equalityIndex.TryCapture(
                    new KeyToken(key),
                    out var bucket,
                    out var handle,
                    out _))
                throw new InvalidOperationException("The equality index does not contain the key being removed.");
            _pendingEqualityRemove = true;
            _pendingEqualityRemoveHandle = handle;
            _pendingEqualityRemoveBucket = bucket;
        }

        private void CommitPreparedEqualityChanges()
        {
            if (_pendingEqualityRemove)
            {
                if (!_equalityIndex.Remove(_pendingEqualityRemoveHandle))
                    throw new InvalidOperationException("The prepared equality index removal failed.");
                _equalityArena.Release(_pendingEqualityRemoveBucket);
            }
            if (_pendingEqualityAdd)
                _equalityIndex.CommitPrepared(_pendingEqualityAddHandle);
            _pendingEqualityAdd = false;
            _pendingEqualityRemove = false;
        }

        private void CancelPreparedEqualityAdd()
        {
            if (!_pendingEqualityAdd) return;
            _equalityIndex.ReleasePrepared(_pendingEqualityAddHandle);
            _equalityArena.Release(_pendingEqualityAddBucket);
            _pendingEqualityAdd = false;
            _pendingEqualityAddHandle = default;
            _pendingEqualityAddBucket = default;
        }

        private void ValidateCandidateKey(TKey key, bool orderingMatch)
        {
            var equalityMatch = _equalityIndex.TryGetValue(new KeyToken(key), out _);
            if (equalityMatch != orderingMatch)
                throw new InconsistentKeyComparerException(_definition.Name);
        }

        private void ValidateCandidateKeyAfterRemoval(
            TKey key,
            bool orderingMatch,
            int removedSummaryIndex,
            bool removeOldSummary)
        {
            var equalityMatch = _equalityIndex.TryGetValue(new KeyToken(key), out _);
            if (removeOldSummary && equalityMatch
                && _definition.KeysEqual(_keySummaries[removedSummaryIndex].Key, key))
                equalityMatch = false;
            if (removeOldSummary && orderingMatch && removedSummaryIndex == _pendingNewSummaryIndex)
                orderingMatch = false;
            if (equalityMatch != orderingMatch)
                throw new InconsistentKeyComparerException(_definition.Name);
        }

        private void ValidateQueryKey(TKey key, int lowerBound)
        {
            var orderingMatch = lowerBound < _count
                && _definition.CompareKeys(_entries[lowerBound].Key, key) == 0;
            var equalityMatch = _equalityIndex.TryGetValue(new KeyToken(key), out _);
            if (orderingMatch != equalityMatch)
                throw new InconsistentKeyComparerException(_definition.Name);
        }

        private void BuildKeySummaries()
        {
            _keySummaryCount = 0;
            for (var index = 0; index < _count; index++)
            {
                var entry = _entries[index];
                if (_keySummaryCount > 0
                    && _definition.KeysEqual(_keySummaries[_keySummaryCount - 1].Key, entry.Key))
                {
                    var summary = _keySummaries[_keySummaryCount - 1];
                    _keySummaries[_keySummaryCount - 1] = new KeySummary(summary.Key, summary.ReferenceCount + 1);
                    continue;
                }
                EnsureSummaryCapacity(_keySummaryCount + 1);
                _keySummaries[_keySummaryCount++] = new KeySummary(entry.Key, 1);
            }
        }

        private void CommitSummaryAdd(TKey key, int index, bool insert)
        {
            if (insert)
            {
                if (index < _keySummaryCount)
                    Array.Copy(_keySummaries, index, _keySummaries, index + 1, _keySummaryCount - index);
                _keySummaries[index] = new KeySummary(key, 1);
                _keySummaryCount++;
                return;
            }

            var summary = _keySummaries[index];
            _keySummaries[index] = new KeySummary(summary.Key, summary.ReferenceCount + 1);
        }

        private void CommitSummaryRemove(int index, bool remove)
        {
            if (remove)
            {
                if (index + 1 < _keySummaryCount)
                    Array.Copy(_keySummaries, index + 1, _keySummaries, index, _keySummaryCount - index - 1);
                _keySummaries[--_keySummaryCount] = default;
                return;
            }

            var summary = _keySummaries[index];
            _keySummaries[index] = new KeySummary(summary.Key, summary.ReferenceCount - 1);
        }

        private void EnsureCapacity(int required)
        {
            if (_entries.Length >= required) return;
            var capacity = Math.Max(required, _entries.Length == 0 ? 4 : checked(_entries.Length * 2));
            Array.Resize(ref _entries, capacity);
        }

        private void EnsureSummaryCapacity(int required)
        {
            if (_keySummaries.Length >= required) return;
            var capacity = Math.Max(required, _keySummaries.Length == 0 ? 4 : checked(_keySummaries.Length * 2));
            Array.Resize(ref _keySummaries, capacity);
        }

        private void EnsureNoPendingMutation()
        {
            if (_pendingKind != SortedMutationKind.None)
                throw new InvalidOperationException($"Sorted index '{_definition.Name}' already has a prepared mutation.");
        }

    }

    private readonly Dictionary<string, ISortedKeyIndex> _sortedKeyIndexes = new(StringComparer.Ordinal);

    private SortedKeyIndex<TKey> EnsureSortedKeyIndex<TKey>(RecordKeyDefinition<TValue, TKey> key)
    {
        if (_sortedKeyIndexes.TryGetValue(key.Name, out var existing))
            return existing as SortedKeyIndex<TKey>
                ?? throw new InvalidOperationException($"Sorted index '{key.Name}' has an incompatible key type.");

        var index = new SortedKeyIndex<TKey>(key, Count);
        foreach (var slot in _slots)
            if (slot.IsActive) index.AddUnsorted(slot);
        index.CompleteBuild();
        _sortedKeyIndexes.Add(key.Name, index);
        return index;
    }

    private void PrepareSortedAdd(in Slot slot)
    {
        while (true)
        {
            try
            {
                foreach (var index in _sortedKeyIndexes.Values.ToArray()) index.PrepareAdd(slot);
                return;
            }
            catch (InconsistentKeyComparerException exception)
            {
                CancelPreparedSortedMutations();
                if (!TryFallbackSortedIndex(exception.KeyName)) throw;
            }
            catch
            {
                CancelPreparedSortedMutations();
                throw;
            }
        }
    }

    private void PrepareSortedRemove(in Slot slot)
    {
        while (true)
        {
            try
            {
                foreach (var index in _sortedKeyIndexes.Values.ToArray()) index.PrepareRemove(slot);
                return;
            }
            catch (InconsistentKeyComparerException exception)
            {
                CancelPreparedSortedMutations();
                if (!TryFallbackSortedIndex(exception.KeyName)) throw;
            }
            catch
            {
                CancelPreparedSortedMutations();
                throw;
            }
        }
    }

    private void PrepareSortedReplace(in Slot oldSlot, in TValue newValue)
    {
        while (true)
        {
            try
            {
                foreach (var index in _sortedKeyIndexes.Values.ToArray()) index.PrepareReplace(oldSlot, newValue);
                return;
            }
            catch (InconsistentKeyComparerException exception)
            {
                CancelPreparedSortedMutations();
                if (!TryFallbackSortedIndex(exception.KeyName)) throw;
            }
            catch
            {
                CancelPreparedSortedMutations();
                throw;
            }
        }
    }

    private bool TryFallbackSortedIndex(string keyName)
    {
        if (KeyIndexMode != RecordStoreKeyIndexMode.Automatic) return false;
        if (!_sortedKeyIndexes.Remove(keyName, out var sortedIndex)) return false;
        EnsureKeyIndex(sortedIndex.Definition);
        return true;
    }

    private void CommitPreparedSortedMutations()
    {
        foreach (var index in _sortedKeyIndexes.Values) index.CommitPrepared();
    }

    private void CancelPreparedSortedMutations()
    {
        foreach (var index in _sortedKeyIndexes.Values) index.CancelPrepared();
    }
}

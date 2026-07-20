using System.Collections;

namespace Iwesun.Runtime.Data;

public sealed partial class RecordStoreV2<TValue, TPrimaryKey> : IEnumerable<TValue>, IReadOnlyCollection<TValue>
    where TValue : struct, IRecordStoreValue
    where TPrimaryKey : notnull
{
    private struct Slot(StoreRecordId id, TValue value)
    {
        public StoreRecordId Id { get; } = id;
        public SlotHandle Handle => new(Id);
        public TValue Value = value;
        public bool IsActive = true;
    }

    private readonly record struct SlotHandle(StoreRecordId Id);

    private struct HandleBucket
    {
        private SlotHandle _first;
        private List<SlotHandle>? _additional;

        public int Count => _first.Id.Value == 0 ? 0 : 1 + (_additional?.Count ?? 0);
        public IEnumerable<SlotHandle> Handles
        {
            get
            {
                if (_first.Id.Value == 0) yield break;
                yield return _first;
                if (_additional is null) yield break;
                foreach (var handle in _additional) yield return handle;
            }
        }

        public SlotHandle First => _first;

        public void PrepareAdd()
        {
            if (_first.Id.Value == 0) return;
            (_additional ??= []).EnsureCapacity((_additional?.Count ?? 0) + 1);
        }

        public void AddStable(SlotHandle handle)
        {
            if (_first.Id.Value == 0)
            {
                _first = handle;
                return;
            }
            if (handle.Id.Value < _first.Id.Value)
            {
                _additional!.Insert(0, _first);
                _first = handle;
                return;
            }
            var index = _additional!.BinarySearch(handle, SlotHandleComparer.Instance);
            _additional.Insert(index < 0 ? ~index : index, handle);
        }

        public void Remove(SlotHandle handle)
        {
            if (_first == handle)
            {
                if (_additional is { Count: > 0 })
                {
                    _first = _additional[0];
                    _additional.RemoveAt(0);
                    if (_additional.Count == 0) _additional = null;
                }
                else
                {
                    _first = default;
                }
                return;
            }
            if (_additional is null) return;
            _additional.Remove(handle);
            if (_additional.Count == 0) _additional = null;
        }
    }

    private sealed class BucketArena
    {
        private readonly SegmentedAppendStore<HandleBucket> _buckets = new(256);
        private readonly Stack<int> _freeIndexes = [];

        public int ReservedCount => _buckets.Count - _freeIndexes.Count;

        public BucketRef Create()
        {
            if (_freeIndexes.TryPop(out var freeIndex))
            {
                _buckets[freeIndex] = default;
                return new BucketRef(this, freeIndex);
            }
            _buckets.EnsureAdditionalCapacity(1);
            return new BucketRef(this, _buckets.Add(default));
        }

        public void Release(BucketRef bucket)
        {
            if (!ReferenceEquals(bucket.Arena, this))
                throw new ArgumentException("The bucket does not belong to this arena.", nameof(bucket));
            if (_buckets[bucket.Index].Count != 0)
                throw new InvalidOperationException("Only empty buckets can be released.");
            _buckets[bucket.Index] = default;
            _freeIndexes.Push(bucket.Index);
        }

        public ref HandleBucket GetReference(int index) => ref _buckets.GetReference(index);
        public HandleBucket Get(int index) => _buckets[index];
    }

    private readonly record struct BucketRef(BucketArena Arena, int Index)
    {
        public int Count => Arena.Get(Index).Count;
        public IEnumerable<SlotHandle> Handles => Arena.Get(Index).Handles;
        public SlotHandle First => Arena.Get(Index).First;
        public void PrepareAdd() => Arena.GetReference(Index).PrepareAdd();
        public void AddStable(SlotHandle handle) => Arena.GetReference(Index).AddStable(handle);
        public void Remove(SlotHandle handle) => Arena.GetReference(Index).Remove(handle);
    }

    private readonly record struct IndexEntryHandle(int Index, long Generation);

    private interface IStableBucketIndex
    {
        void ReleasePrepared(IndexEntryHandle handle);
    }

    private sealed class StableBucketIndex<TToken>(IEqualityComparer<TToken> comparer)
        : IStableBucketIndex, IEnumerable<KeyValuePair<TToken, BucketRef>>
        where TToken : notnull
    {
        private struct Entry
        {
            public TToken Token;
            public BucketRef Bucket;
            public int HashCode;
            public int Next;
            public long Generation;
            public bool IsActive;
            public bool IsPrepared;
        }

        private readonly IEqualityComparer<TToken> _comparer = comparer;
        private readonly List<Entry> _entries = [];
        private readonly Stack<int> _freeIndexes = [];
        private int[] _buckets = new int[8];

        public int Count { get; private set; }
        public IEnumerable<BucketRef> Values
        {
            get
            {
                foreach (var entry in _entries)
                    if (entry.IsActive) yield return entry.Bucket;
            }
        }

        public BucketRef this[TToken token] => TryGetValue(token, out var bucket)
            ? bucket
            : throw new KeyNotFoundException();

        public bool TryGetValue(TToken token, out BucketRef bucket) =>
            TryCapture(token, out bucket, out _, out _);

        public bool TryCapture(
            TToken token,
            out BucketRef bucket,
            out IndexEntryHandle handle,
            out int hashCode)
        {
            hashCode = _comparer.GetHashCode(token) & 0x7FFFFFFF;
            for (var link = _buckets[hashCode % _buckets.Length]; link != 0; link = _entries[link - 1].Next)
            {
                var index = link - 1;
                var entry = _entries[index];
                if (entry.HashCode != hashCode || !_comparer.Equals(entry.Token, token)) continue;
                bucket = entry.Bucket;
                handle = new IndexEntryHandle(index, entry.Generation);
                return true;
            }
            bucket = default;
            handle = default;
            return false;
        }

        public IndexEntryHandle PrepareAdd(TToken token, int hashCode, BucketRef bucket)
        {
            EnsureCapacityForOne();
            int index;
            Entry entry;
            if (_freeIndexes.TryPop(out index))
            {
                entry = _entries[index];
            }
            else
            {
                index = _entries.Count;
                entry = new Entry { Generation = 1 };
                _entries.Add(entry);
            }
            entry.Token = token;
            entry.Bucket = bucket;
            entry.HashCode = hashCode;
            entry.Next = 0;
            entry.IsActive = false;
            entry.IsPrepared = true;
            _entries[index] = entry;
            return new IndexEntryHandle(index, entry.Generation);
        }

        public void CommitPrepared(IndexEntryHandle handle)
        {
            var entry = GetEntry(handle);
            if (!entry.IsPrepared || entry.IsActive)
                throw new InvalidOperationException("The index entry is not prepared for registration.");
            var bucketIndex = entry.HashCode % _buckets.Length;
            entry.Next = _buckets[bucketIndex];
            entry.IsPrepared = false;
            entry.IsActive = true;
            _entries[handle.Index] = entry;
            _buckets[bucketIndex] = handle.Index + 1;
            Count++;
        }

        public void ReleasePrepared(IndexEntryHandle handle)
        {
            var entry = GetEntry(handle);
            if (!entry.IsPrepared || entry.IsActive) return;
            ReleaseEntry(handle.Index, entry);
        }

        public bool Remove(IndexEntryHandle handle)
        {
            var entry = GetEntry(handle);
            if (!entry.IsActive) return false;
            var bucketIndex = entry.HashCode % _buckets.Length;
            var previous = 0;
            for (var link = _buckets[bucketIndex]; link != 0; link = _entries[link - 1].Next)
            {
                if (link - 1 != handle.Index)
                {
                    previous = link;
                    continue;
                }
                if (previous == 0)
                    _buckets[bucketIndex] = entry.Next;
                else
                {
                    var previousEntry = _entries[previous - 1];
                    previousEntry.Next = entry.Next;
                    _entries[previous - 1] = previousEntry;
                }
                Count--;
                ReleaseEntry(handle.Index, entry);
                return true;
            }
            throw new InvalidOperationException("The active index entry is not linked from its hash bucket.");
        }

        public void Add(TToken token, BucketRef bucket)
        {
            if (TryCapture(token, out _, out _, out var hashCode))
                throw new ArgumentException("An index entry with the same token already exists.", nameof(token));
            var handle = PrepareAdd(token, hashCode, bucket);
            CommitPrepared(handle);
        }

        public IEnumerator<KeyValuePair<TToken, BucketRef>> GetEnumerator()
        {
            foreach (var entry in _entries)
                if (entry.IsActive) yield return new KeyValuePair<TToken, BucketRef>(entry.Token, entry.Bucket);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private Entry GetEntry(IndexEntryHandle handle)
        {
            if ((uint)handle.Index >= (uint)_entries.Count)
                throw new ArgumentOutOfRangeException(nameof(handle));
            var entry = _entries[handle.Index];
            if (entry.Generation != handle.Generation)
                throw new InvalidOperationException("The index entry handle is stale.");
            return entry;
        }

        private void ReleaseEntry(int index, Entry entry)
        {
            entry.Token = default!;
            entry.Bucket = default;
            entry.HashCode = 0;
            entry.Next = 0;
            entry.IsActive = false;
            entry.IsPrepared = false;
            entry.Generation = checked(entry.Generation + 1);
            _entries[index] = entry;
            _freeIndexes.Push(index);
        }

        private void EnsureCapacityForOne()
        {
            if (Count + 1 <= _buckets.Length * 3 / 4) return;
            var resized = new int[checked(_buckets.Length * 2)];
            for (var index = 0; index < _entries.Count; index++)
            {
                var entry = _entries[index];
                if (!entry.IsActive) continue;
                var bucketIndex = entry.HashCode % resized.Length;
                entry.Next = resized[bucketIndex];
                _entries[index] = entry;
                resized[bucketIndex] = index + 1;
            }
            _buckets = resized;
        }
    }

    private sealed class SlotHandleComparer : IComparer<SlotHandle>
    {
        public static SlotHandleComparer Instance { get; } = new();
        public int Compare(SlotHandle left, SlotHandle right) => left.Id.Value.CompareTo(right.Id.Value);
    }

    private readonly record struct KeyToken(object? Value);

    private sealed class KeyTokenComparer(IRecordKeyDefinition<TValue> definition) : IEqualityComparer<KeyToken>
    {
        public bool Equals(KeyToken left, KeyToken right) => definition.KeysEqual(left.Value, right.Value);
        public int GetHashCode(KeyToken token) => definition.GetKeyHashCode(token.Value);
    }

    private sealed class KeyIndex(IRecordKeyDefinition<TValue> definition)
    {
        private KeyToken[]? _sortedKeys;

        public IRecordKeyDefinition<TValue> Definition { get; } = definition;
        public BucketArena Arena { get; } = new();
        public StableBucketIndex<KeyToken> Buckets { get; } = new(new KeyTokenComparer(definition));

        public KeyToken[] GetSortedKeys()
        {
            if (_sortedKeys is not null) return _sortedKeys;
            var keys = Buckets.Select(static pair => pair.Key).ToArray();
            Array.Sort(keys, (left, right) => Definition.CompareKeys(left.Value, right.Value));
            _sortedKeys = keys;
            return keys;
        }

        public void InvalidateSortedKeys() => _sortedKeys = null;
    }

    private readonly record struct ConstraintToken(object? Value);

    private sealed class ConstraintTokenComparer(IUniqueConstraintDefinition<TValue> definition)
        : IEqualityComparer<ConstraintToken>
    {
        public bool Equals(ConstraintToken left, ConstraintToken right) => definition.KeysEqual(left.Value, right.Value);
        public int GetHashCode(ConstraintToken token) => definition.GetKeyHashCode(token.Value);
    }

    private sealed class ConstraintIndex(IUniqueConstraintDefinition<TValue> definition)
    {
        public IUniqueConstraintDefinition<TValue> Definition { get; } = definition;
        public BucketArena Arena { get; } = new();
        public StableBucketIndex<ConstraintToken> Buckets { get; } = new(new ConstraintTokenComparer(definition));
    }

    private readonly record struct CapturedPrimaryToken(
        TPrimaryKey Token,
        BucketRef Bucket,
        IndexEntryHandle EntryHandle,
        bool NeedsRegistration);
    private readonly record struct CapturedKeyToken(
        KeyIndex Index,
        KeyToken Token,
        BucketRef Bucket,
        IndexEntryHandle EntryHandle,
        bool NeedsRegistration);
    private readonly record struct CapturedConstraintToken(
        ConstraintIndex Index,
        ConstraintToken Token,
        BucketRef Bucket,
        IndexEntryHandle EntryHandle,
        bool NeedsRegistration);
    private sealed record CapturedIndexTokens(
        CapturedPrimaryToken Primary,
        CapturedKeyToken[] Keys,
        CapturedConstraintToken[] Constraints);

    private readonly RecordStoreDefinition<TValue, TPrimaryKey> _definition;
    private readonly SegmentedAppendStore<Slot> _slots = new(256);
    private readonly Dictionary<StoreRecordId, int> _recordsById = [];
    private readonly BucketArena _primaryArena = new();
    private readonly StableBucketIndex<TPrimaryKey> _primaryIndex;
    private readonly Dictionary<string, KeyIndex> _keyIndexes = new(StringComparer.Ordinal);
    private IUniqueConstraintDefinition<TValue>[] _uniqueConstraints = [];
    private ConstraintIndex[] _uniqueIndexes = [];
    private long _nextRecordId;
    private RecordStoreV2<TValue, TPrimaryKey>? _snapshot;
    private bool _allowPrimaryKeyDuplicate;
    private bool _allowUniqueConstraintViolation;
    private string _schemaId = "";
    private IDeepCloneStrategy<TValue> _cloneStrategy = ValueCopyCloneStrategy<TValue>.Instance;
    private int _indexThreshold = 1024;
    private RecordStoreKeyIndexMode _keyIndexMode;
    private int _runtimeProfileVersion = 2;
    private ITableValueCodec<TValue>? _codec;
    private bool _autoMerge;
    private ValueFormatter<TValue>? _valueFormatter;
    private MergePredicate<TValue>? _mergePredicate;
    private MergeResolver<TValue>? _mergeResolver;
    private RecordFilter<TValue>? _recordFilter;
    private LimitPredicate<TValue>? _limit;
    private RecordStorePublishFormat<TValue> _defaultPublishFormat = new();
    private int _publicationState;
    private int _activeCount;

    public RecordStoreV2(RecordStoreDefinition<TValue, TPrimaryKey> definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _definition = definition;
        _primaryIndex = new StableBucketIndex<TPrimaryKey>(definition.PrimaryKey.EqualityComparer);
    }

    public RecordStoreDefinition<TValue, TPrimaryKey> Definition => _definition;
    public string SchemaId
    {
        get => _schemaId;
        set
        {
            EnsureConfigurationWritable();
            ArgumentNullException.ThrowIfNull(value);
            _schemaId = value;
        }
    }
    public IDeepCloneStrategy<TValue> CloneStrategy
    {
        get => _cloneStrategy;
        set
        {
            EnsureConfigurationWritable();
            _cloneStrategy = value ?? throw new ArgumentNullException(nameof(value));
        }
    }
    public ITableValueCodec<TValue>? Codec
    {
        get => _codec;
        set
        {
            EnsureConfigurationWritable();
            _codec = value;
        }
    }
    public int RuntimeProfileVersion
    {
        get => _runtimeProfileVersion;
        set
        {
            EnsureConfigurationWritable();
            if (value < 1) throw new ArgumentOutOfRangeException(nameof(value));
            _runtimeProfileVersion = value;
        }
    }
    public int IndexThreshold
    {
        get => _indexThreshold;
        set
        {
            EnsureConfigurationWritable();
            if (value < 1) throw new ArgumentOutOfRangeException(nameof(value));
            _indexThreshold = value;
        }
    }
    public RecordStoreKeyIndexMode KeyIndexMode
    {
        get => _keyIndexMode;
        set
        {
            EnsureConfigurationWritable();
            if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
            if (_keyIndexMode == value) return;
            _keyIndexes.Clear();
            _sortedKeyIndexes.Clear();
            _keyIndexMode = value;
        }
    }
    public IReadOnlyList<IUniqueConstraintDefinition<TValue>> UniqueConstraints => _uniqueConstraints;
    public bool AllowPrimaryKeyDuplicate
    {
        get => _allowPrimaryKeyDuplicate;
        set
        {
            EnsureConfigurationWritable();
            if (!value && _primaryIndex.Values.Any(static bucket => bucket.Count > 1))
                throw new InvalidOperationException("Existing records contain duplicate primary keys.");
            _allowPrimaryKeyDuplicate = value;
        }
    }
    public bool AutoMerge
    {
        get => _autoMerge;
        set
        {
            EnsureConfigurationWritable();
            _autoMerge = value;
        }
    }
    public bool AllowUniqueConstraintViolation
    {
        get => _allowUniqueConstraintViolation;
        set
        {
            EnsureConfigurationWritable();
            if (!value && _uniqueIndexes.Any(static index =>
                    index.Buckets.Values.Any(static bucket => bucket.Count > 1)))
                throw new InvalidOperationException("Existing records violate an active unique constraint.");
            _allowUniqueConstraintViolation = value;
        }
    }
    public ValueFormatter<TValue>? ValueFormatter
    {
        get => _valueFormatter;
        set { EnsureConfigurationWritable(); _valueFormatter = value; }
    }
    public MergePredicate<TValue>? MergePredicate
    {
        get => _mergePredicate;
        set { EnsureConfigurationWritable(); _mergePredicate = value; }
    }
    public MergeResolver<TValue>? MergeResolver
    {
        get => _mergeResolver;
        set { EnsureConfigurationWritable(); _mergeResolver = value; }
    }
    public RecordFilter<TValue>? RecordFilter
    {
        get => _recordFilter;
        set { EnsureConfigurationWritable(); _recordFilter = value; }
    }
    public LimitPredicate<TValue>? Limit
    {
        get => _limit;
        set { EnsureConfigurationWritable(); _limit = value; }
    }
    public RecordStorePublishFormat<TValue> DefaultPublishFormat
    {
        get => _defaultPublishFormat;
        set
        {
            EnsureConfigurationWritable();
            _defaultPublishFormat = value ?? throw new ArgumentNullException(nameof(value));
        }
    }
    public Guid TableId { get; } = Guid.NewGuid();
    public RecordStoreV2Origin Origin { get; private set; } = RecordStoreV2Origin.Source;
    public bool IsReadOnly { get; private set; }
    public int Count => _activeCount;
    public int PhysicalCount => _slots.Count;
    public int DeprecatedCount => PhysicalCount - Count;
    public double TombstoneRatio => PhysicalCount == 0 ? 0d : (double)DeprecatedCount / PhysicalCount;
    public long DataVersion { get; private set; }
    public long PublicationVersion { get; private set; }
    public long? LastPublishedDataVersion { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public bool HasUnpublishedChanges => Origin == RecordStoreV2Origin.Source
        && DataVersion != 0
        && LastPublishedDataVersion != DataVersion;
    public bool IsPublishing => Volatile.Read(ref _publicationState) != 0;
    public RecordStoreAccessGate AccessGate { get; } = new();
    public RecordStoreV2<TValue, TPrimaryKey>? Snapshot => Volatile.Read(ref _snapshot);
    internal int ReservedIndexBucketCount => _primaryArena.ReservedCount
        + _keyIndexes.Values.Sum(static index => index.Arena.ReservedCount)
        + _uniqueIndexes.Sum(static index => index.Arena.ReservedCount);
    internal RecordStoreKeyIndexMode GetActiveKeyIndexMode(string keyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyName);
        if (_keyIndexes.ContainsKey(keyName)) return RecordStoreKeyIndexMode.Hash;
        if (_sortedKeyIndexes.ContainsKey(keyName)) return RecordStoreKeyIndexMode.SortedTable;
        return RecordStoreKeyIndexMode.Automatic;
    }

    internal int GetSortedKeySummaryCapacity(string keyName) =>
        _sortedKeyIndexes.TryGetValue(keyName, out var index) ? index.SummaryCapacity : 0;

    public void ApplyOptions(RecordStoreOptions options)
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(options);
        if (!options.AllowPrimaryKeyDuplicate && _primaryIndex.Values.Any(static bucket => bucket.Count > 1))
            throw new InvalidOperationException("Existing records contain duplicate primary keys.");
        if (!options.AllowUniqueConstraintViolation && _uniqueIndexes.Any(static index =>
                index.Buckets.Values.Any(static bucket => bucket.Count > 1)))
            throw new InvalidOperationException("Existing records violate an active unique constraint.");

        // Validation is complete. These field assignments cannot invoke user code or leave partial options.
        _allowPrimaryKeyDuplicate = options.AllowPrimaryKeyDuplicate;
        _autoMerge = options.AutoMerge;
        _allowUniqueConstraintViolation = options.AllowUniqueConstraintViolation;
    }

    public StoreRecordId Add(in TValue value)
    {
        if (TryAdd(value, out var id)) return id;
        throw new InvalidOperationException("The record was rejected by the RecordStore definition.");
    }

    public bool TryAdd(in TValue value, out StoreRecordId id)
    {
        id = default;
        if (!CanWrite()) return false;
        var candidate = PrepareInput(value);
        if (!IsCandidateValid(candidate, replacedSlot: null, allowPrimaryKeyChange: true, out var primaryKey))
            return false;

        BucketRef? primaryMatches = _primaryIndex.TryGetValue(primaryKey, out var primaryBucket)
            ? primaryBucket
            : null;
        if (AutoMerge && MergePredicate is not null && MergeResolver is not null)
        {
            foreach (var existing in GetMergeCandidates(candidate, primaryMatches))
            {
                if (!MergePredicate(Clone(existing.Value), Clone(candidate))) continue;
                if (TryMerge(existing, candidate, out id)) return true;
            }
        }

        if (primaryMatches is { Count: > 0 } && !AllowPrimaryKeyDuplicate)
            return false;
        if (HasUniqueConflict(candidate, replacedSlot: null) || !PassesLimit(candidate, replacedSlot: null))
            return false;

        id = new StoreRecordId(checked(_nextRecordId + 1));
        candidate.StoreRecordId = id;
        var slot = new Slot(id, candidate);
        PrepareSortedAdd(slot);
        CapturedIndexTokens indexTokens;
        try
        {
            indexTokens = CaptureIndexTokens(candidate);
        }
        catch
        {
            CancelPreparedSortedMutations();
            throw;
        }
        PrepareAddedBucketUpdates(indexTokens);
        _slots.EnsureAdditionalCapacity(1);
        _recordsById.EnsureCapacity(_recordsById.Count + 1);
        RegisterPreparedBuckets(indexTokens);
        _nextRecordId = id.Value;
        var location = _slots.Add(slot);
        _recordsById.Add(id, location);
        _activeCount++;
        CommitAddedBucketUpdates(indexTokens, slot.Handle);
        CommitPreparedSortedMutations();
        DataVersion++;
        return true;
    }

    public int AddRange(IEnumerable<TValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var accepted = 0;
        foreach (var value in values)
            if (TryAdd(value, out _)) accepted++;
        return accepted;
    }

    public int AddRange(ReadOnlySpan<TValue> values)
    {
        var accepted = 0;
        foreach (ref readonly var value in values)
            if (TryAdd(value, out _)) accepted++;
        return accepted;
    }

    public void SetUniqueConstraints(IEnumerable<IUniqueConstraintDefinition<TValue>> constraints)
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(constraints);
        var definitions = constraints.ToArray();
        if (definitions.Any(static constraint => constraint is null))
            throw new ArgumentException("Unique constraint definitions cannot contain null.", nameof(constraints));
        if (definitions.Select(static constraint => constraint.Name).Distinct(StringComparer.Ordinal).Count()
            != definitions.Length)
            throw new ArgumentException("Unique constraint names must be unique.", nameof(constraints));
        foreach (var definition in definitions)
            _definition.EnsureKnownComponents(definition.ComponentKeyNames, nameof(constraints));

        var indexes = definitions.Select(static definition => new ConstraintIndex(definition)).ToArray();
        foreach (var slot in _slots)
            if (slot.IsActive) AddToUniqueIndexes(indexes, slot);
        if (!AllowUniqueConstraintViolation
            && indexes.Any(static index => index.Buckets.Values.Any(static bucket => bucket.Count > 1)))
            throw new InvalidOperationException("Existing records violate the requested unique constraints.");

        _uniqueConstraints = definitions;
        _uniqueIndexes = indexes;
    }

    public void AddUniqueConstraint(IUniqueConstraintDefinition<TValue> constraint)
    {
        ArgumentNullException.ThrowIfNull(constraint);
        SetUniqueConstraints(_uniqueConstraints.Append(constraint));
    }

    public bool RemoveUniqueConstraint(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var remaining = _uniqueConstraints.Where(constraint =>
            !StringComparer.Ordinal.Equals(constraint.Name, name)).ToArray();
        if (remaining.Length == _uniqueConstraints.Length) return false;
        SetUniqueConstraints(remaining);
        return true;
    }

    public void ReplaceUniqueConstraint(IUniqueConstraintDefinition<TValue> constraint)
    {
        ArgumentNullException.ThrowIfNull(constraint);
        var found = false;
        var replacements = _uniqueConstraints.Select(existing =>
        {
            if (!StringComparer.Ordinal.Equals(existing.Name, constraint.Name)) return existing;
            found = true;
            return constraint;
        }).ToArray();
        if (!found)
            throw new KeyNotFoundException($"Unique constraint '{constraint.Name}' is not registered.");
        SetUniqueConstraints(replacements);
    }

    public bool TryGetRecord(StoreRecordId id, out TValue value)
    {
        if (_recordsById.TryGetValue(id, out var location))
        {
            var slot = _slots[location];
            value = Clone(slot.Value);
            return true;
        }

        value = default;
        return false;
    }

    public RecordUpdateResult TryUpdate(in TValue value)
    {
        if (IsPublishing) return RecordUpdateResult.Busy;
        EnsureWritable();
        if (!_recordsById.TryGetValue(value.StoreRecordId, out var location))
            return RecordUpdateResult.NotFound;
        var slot = _slots[location];

        var candidate = Clone(value);
        candidate.StoreRecordId = slot.Id;
        var oldPrimaryKey = _definition.PrimaryKey.GetPrimaryKey(slot.Value);
        var newPrimaryKey = _definition.PrimaryKey.GetPrimaryKey(candidate);
        if (!_definition.PrimaryKey.EqualityComparer.Equals(oldPrimaryKey, newPrimaryKey))
            return RecordUpdateResult.PrimaryKeyChanged;

        if (!IsCandidateValid(candidate, slot, allowPrimaryKeyChange: false, out _)
            || HasUniqueConflict(candidate, slot)
            || !PassesLimit(candidate, slot))
            return RecordUpdateResult.Rejected;

        ReplaceIndexedValue(slot, candidate);
        DataVersion++;
        return RecordUpdateResult.Updated;
    }

    public bool TryDeprecate(StoreRecordId id)
    {
        if (!CanWrite()) return false;
        if (!_recordsById.TryGetValue(id, out var location)) return false;
        var slot = _slots[location];
        PrepareSortedRemove(slot);
        CapturedIndexTokens indexTokens;
        try
        {
            indexTokens = CaptureIndexTokens(slot.Value);
        }
        catch
        {
            CancelPreparedSortedMutations();
            throw;
        }
        CommitRemovedBucketUpdates(indexTokens, slot.Handle);
        slot.IsActive = false;
        slot.Value = default;
        _slots[location] = slot;
        _recordsById.Remove(id);
        CommitPreparedSortedMutations();
        _activeCount--;
        DataVersion++;
        return true;
    }

    public TKey[] GetKeys<TKey>(RecordKeyDefinition<TValue, TKey> key)
    {
        EnsureOwnedKey(key);
        var index = GetAdaptiveKeyIndex(key, out var sortedIndex);
        if (sortedIndex is not null) return sortedIndex.GetKeys();
        if (index is not null)
        {
            var tokens = index.GetSortedKeys();
            var result = new TKey[tokens.Length];
            for (var position = 0; position < tokens.Length; position++)
                result[position] = tokens[position].Value is null ? default! : (TKey)tokens[position].Value!;
            return result;
        }
        var values = GetDistinctKeyTokens(key)
            .Select(static token => token.Value is null ? default! : (TKey)token.Value).ToList();
        values.Sort(key.CompareKeys);
        return values.ToArray();
    }

    public int GetKeyCount<TKey>(RecordKeyDefinition<TValue, TKey> key)
    {
        EnsureOwnedKey(key);
        var index = GetAdaptiveKeyIndex(key, out var sortedIndex);
        if (sortedIndex is not null) return sortedIndex.GetKeyCount();
        return index?.Buckets.Count ?? GetDistinctKeyTokens(key).Count;
    }

    public TValue[] GetValues<TKey>(RecordKeyDefinition<TValue, TKey> key, TKey keyValue)
    {
        EnsureOwnedKey(key);
        var index = GetAdaptiveKeyIndex(key, out var sortedIndex);
        if (sortedIndex is not null)
        {
            int start;
            int end;
            try
            {
                sortedIndex.GetRange(keyValue, out start, out end);
            }
            catch (InconsistentKeyComparerException exception)
            {
                if (!TryFallbackSortedIndex(exception.KeyName)) throw;
                return GetValues(key, keyValue);
            }
            if (start == end) return [];
            var values = new TValue[end - start];
            for (var indexPosition = start; indexPosition < end; indexPosition++)
                values[indexPosition - start] = Clone(Resolve(new SlotHandle(sortedIndex.GetRecordId(indexPosition))).Value);
            return values;
        }
        if (index is not null)
        {
            return index.Buckets.TryGetValue(new KeyToken(keyValue), out var slots)
                ? slots.Handles.Select(handle => Clone(Resolve(handle).Value)).ToArray()
                : [];
        }

        return _slots
            .Where(slot => slot.IsActive && key.KeysEqual(key.GetTypedKey(slot.Value), keyValue))
            .Select(slot => Clone(slot.Value))
            .ToArray();
    }

    public int GetValueCount<TKey>(RecordKeyDefinition<TValue, TKey> key, TKey keyValue)
    {
        EnsureOwnedKey(key);
        var index = GetAdaptiveKeyIndex(key, out var sortedIndex);
        if (sortedIndex is not null)
        {
            try
            {
                return sortedIndex.GetValueCount(keyValue);
            }
            catch (InconsistentKeyComparerException exception)
            {
                if (!TryFallbackSortedIndex(exception.KeyName)) throw;
                return GetValueCount(key, keyValue);
            }
        }
        if (index is not null)
            return index.Buckets.TryGetValue(new KeyToken(keyValue), out var slots) ? slots.Count : 0;

        var count = 0;
        foreach (var slot in _slots)
        {
            if (!slot.IsActive) continue;
            if (key.KeysEqual(key.GetTypedKey(slot.Value), keyValue)) count++;
        }
        return count;
    }

    public TPrimaryKey[] GetPrimaryKeys()
    {
        var keys = _primaryIndex.Where(static pair => pair.Value.Count > 0)
            .Select(static pair => pair.Key).ToArray();
        Array.Sort(keys, _definition.PrimaryKey.Comparer);
        return keys;
    }

    public int GetPrimaryKeyCount() => _primaryIndex.Count(static pair => pair.Value.Count > 0);

    public TValue[] GetValues(TPrimaryKey primaryKey) =>
        _primaryIndex.TryGetValue(primaryKey, out var slots)
            ? slots.Handles.Select(handle => Clone(Resolve(handle).Value)).ToArray()
            : [];

    public int GetValueCount(TPrimaryKey primaryKey) =>
        _primaryIndex.TryGetValue(primaryKey, out var slots) ? slots.Count : 0;

    public IReadOnlyList<RecordConstraintConflict> ValidateConstraints(
        CancellationToken cancellationToken = default)
    {
        var conflicts = new List<RecordConstraintConflict>();
        var primaryKeys = _primaryIndex.Where(static pair => pair.Value.Count > 0)
            .Select(static pair => pair.Key).ToArray();
        Array.Sort(primaryKeys, _definition.PrimaryKey.Comparer);
        foreach (var primaryKey in primaryKeys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bucket = _primaryIndex[primaryKey];
            if (bucket.Count > 1)
                conflicts.Add(new RecordConstraintConflict("$primary", bucket.Handles.Select(static handle => handle.Id).ToArray()));
        }

        foreach (var index in _uniqueIndexes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var bucket in index.Buckets.Values.Where(static bucket => bucket.Count > 1)
                         .OrderBy(bucket => _recordsById[bucket.First.Id]))
            {
                conflicts.Add(new RecordConstraintConflict(
                    index.Definition.Name,
                    bucket.Handles.Select(static handle => handle.Id).ToArray()));
            }
        }

        if (Limit is not null)
        {
            foreach (var slot in _slots)
            {
                if (!slot.IsActive) continue;
                cancellationToken.ThrowIfCancellationRequested();
                var current = _slots.Where(candidate => candidate.IsActive && candidate.Id != slot.Id)
                    .Select(candidate => Clone(candidate.Value))
                    .ToArray();
                if (!Limit(Clone(slot.Value), current))
                {
                    conflicts.Add(new RecordConstraintConflict("$limit", [slot.Id]));
                }
            }
        }

        return conflicts;
    }

    public IEnumerator<TValue> GetEnumerator()
    {
        foreach (var slot in _slots)
        {
            if (!slot.IsActive) continue;
            yield return Clone(slot.Value);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public RecordStoreV2<TValue, TPrimaryKey> CreateCompactedStore(
        CancellationToken cancellationToken = default)
    {
        var compacted = new RecordStoreV2<TValue, TPrimaryKey>(_definition)
        {
            Origin = RecordStoreV2Origin.Clone,
            IsReadOnly = false
        };
        CopyMutableConfigurationTo(compacted);
        foreach (var slot in _slots)
        {
            if (!slot.IsActive) continue;
            cancellationToken.ThrowIfCancellationRequested();
            compacted.AppendClonedValue(slot.Value);
        }
        // Compaction must not reuse identities that belonged to removed records.
        compacted._nextRecordId = _nextRecordId;
        compacted.DataVersion = DataVersion;
        return compacted;
    }

    private TValue PrepareInput(in TValue value)
    {
        var working = Clone(value);
        working.StoreRecordId = default;
        if (ValueFormatter is null) return working;
        var formatted = ValueFormatter(working);
        formatted.StoreRecordId = default;
        return Clone(formatted);
    }

    private bool TryMerge(Slot existing, TValue incoming, out StoreRecordId id)
    {
        id = default;
        if (MergeResolver is null) return false;
        var merged = MergeResolver(Clone(existing.Value), Clone(incoming));
        merged.StoreRecordId = existing.Id;
        if (!IsCandidateValid(merged, existing, allowPrimaryKeyChange: true, out _)
            || HasPrimaryConflict(merged, existing, enforceRegardlessOfOptions: true)
            || HasUniqueConflict(merged, existing, enforceRegardlessOfOptions: true)
            || !PassesLimit(merged, existing))
            return false;

        ReplaceIndexedValue(existing, Clone(merged));
        DataVersion++;
        id = existing.Id;
        return true;
    }

    private bool IsCandidateValid(
        TValue candidate,
        Slot? replacedSlot,
        bool allowPrimaryKeyChange,
        out TPrimaryKey primaryKey)
    {
        primaryKey = _definition.PrimaryKey.GetPrimaryKey(candidate);
        if (IsPrimaryKeyAllNull(candidate)) return false;
        if (RecordFilter is not null && !RecordFilter(candidate)) return false;
        if (!allowPrimaryKeyChange && replacedSlot is not null)
        {
            var oldPrimary = _definition.PrimaryKey.GetPrimaryKey(replacedSlot.Value.Value);
            if (!_definition.PrimaryKey.EqualityComparer.Equals(oldPrimary, primaryKey)) return false;
        }
        return true;
    }

    private bool IsPrimaryKeyAllNull(in TValue value)
    {
        foreach (var componentName in _definition.PrimaryKey.ComponentKeyNames)
        {
            _definition.TryGetKey(componentName, out var key);
            if (key!.GetKey(value) is not null) return false;
        }
        return true;
    }

    private List<Slot> GetMergeCandidates(in TValue candidate, BucketRef? primaryMatches)
    {
        var result = new List<Slot>();
        var seen = new HashSet<SlotHandle>();
        if (primaryMatches is not null)
        {
            foreach (var handle in primaryMatches.Value.Handles)
            {
                var slot = Resolve(handle);
                if (seen.Add(handle)) result.Add(slot);
            }
        }

        foreach (var index in _uniqueIndexes)
        {
            var token = new ConstraintToken(index.Definition.GetConstraintKey(candidate));
            if (!index.Buckets.TryGetValue(token, out var bucket)) continue;
            foreach (var handle in bucket.Handles)
            {
                var slot = Resolve(handle);
                if (seen.Add(handle)) result.Add(slot);
            }
        }
        return result;
    }

    private bool HasPrimaryConflict(
        in TValue candidate,
        Slot replacedSlot,
        bool enforceRegardlessOfOptions = false)
    {
        if (!enforceRegardlessOfOptions && AllowPrimaryKeyDuplicate) return false;
        var primaryKey = _definition.PrimaryKey.GetPrimaryKey(candidate);
        return _primaryIndex.TryGetValue(primaryKey, out var matches)
            && matches.Handles.Any(match => match != replacedSlot.Handle);
    }

    private bool HasUniqueConflict(
        in TValue candidate,
        Slot? replacedSlot,
        bool enforceRegardlessOfOptions = false)
    {
        if (!enforceRegardlessOfOptions && AllowUniqueConstraintViolation) return false;
        foreach (var index in _uniqueIndexes)
        {
            var token = new ConstraintToken(index.Definition.GetConstraintKey(candidate));
            if (index.Buckets.TryGetValue(token, out var bucket)
                && bucket.Handles.Any(handle => replacedSlot is null || handle != replacedSlot.Value.Handle)) return true;
        }
        return false;
    }

    private bool PassesLimit(in TValue candidate, Slot? replacedSlot)
    {
        if (Limit is null) return true;
        var current = _slots.Where(slot => slot.IsActive && (replacedSlot is null || slot.Id != replacedSlot.Value.Id))
            .Select(slot => Clone(slot.Value)).ToArray();
        return Limit(candidate, current);
    }

    private KeyIndex EnsureKeyIndex(IRecordKeyDefinition<TValue> key)
    {
        if (_keyIndexes.TryGetValue(key.Name, out var index)) return index;
        index = new KeyIndex(key);
        foreach (var slot in _slots)
            if (slot.IsActive) AddToKeyIndex(index, slot);
        _keyIndexes.Add(key.Name, index);
        _sortedKeyIndexes.Remove(key.Name);
        return index;
    }

    private KeyIndex? GetAdaptiveKeyIndex<TKey>(
        RecordKeyDefinition<TValue, TKey> key,
        out SortedKeyIndex<TKey>? sortedIndex)
    {
        sortedIndex = null;
        if (_keyIndexes.TryGetValue(key.Name, out var index)) return index;
        var useHash = KeyIndexMode == RecordStoreKeyIndexMode.Hash
            || KeyIndexMode == RecordStoreKeyIndexMode.Automatic && Count >= IndexThreshold;
        if (useHash) return EnsureKeyIndex(key);
        try
        {
            sortedIndex = EnsureSortedKeyIndex(key);
        }
        catch (InconsistentKeyComparerException) when (KeyIndexMode == RecordStoreKeyIndexMode.Automatic)
        {
            return EnsureKeyIndex(key);
        }
        return null;
    }

    private HashSet<KeyToken> GetDistinctKeyTokens<TKey>(RecordKeyDefinition<TValue, TKey> key)
    {
        var tokens = new HashSet<KeyToken>(new KeyTokenComparer(key));
        foreach (var slot in _slots)
            if (slot.IsActive) tokens.Add(new KeyToken(key.GetTypedKey(slot.Value)));
        return tokens;
    }

    private static void AddToKeyIndex(KeyIndex index, Slot slot)
    {
        var token = new KeyToken(index.Definition.GetKey(slot.Value));
        if (!index.Buckets.TryGetValue(token, out var bucket))
        {
            bucket = index.Arena.Create();
            index.Buckets.Add(token, bucket);
        }
        bucket.PrepareAdd();
        bucket.AddStable(slot.Handle);
    }

    private static void AddToUniqueIndexes(ConstraintIndex[] indexes, Slot slot)
    {
        foreach (var index in indexes)
        {
            var token = new ConstraintToken(index.Definition.GetConstraintKey(slot.Value));
            if (!index.Buckets.TryGetValue(token, out var bucket))
            {
                bucket = index.Arena.Create();
                index.Buckets.Add(token, bucket);
            }
            bucket.PrepareAdd();
            bucket.AddStable(slot.Handle);
        }
    }

    private Slot Resolve(SlotHandle handle) => _recordsById.TryGetValue(handle.Id, out var location)
        ? _slots[location]
        : throw new InvalidOperationException($"Slot handle '{handle.Id.Value}' is no longer active.");

    private void ReplaceIndexedValue(Slot slot, in TValue candidate)
    {
        PrepareSortedReplace(slot, candidate);
        CapturedIndexTokens oldTokens;
        CapturedIndexTokens newTokens;
        try
        {
            oldTokens = CaptureIndexTokens(slot.Value);
            newTokens = CaptureIndexTokens(candidate);
        }
        catch
        {
            CancelPreparedSortedMutations();
            throw;
        }
        PrepareReplacedBucketUpdates(oldTokens, newTokens);
        RegisterPreparedBuckets(newTokens);
        CommitReplacedBucketUpdates(oldTokens, newTokens, slot.Handle);
        slot.Value = candidate;
        _slots[_recordsById[slot.Id]] = slot;
        CommitPreparedSortedMutations();
    }

    private CapturedIndexTokens CaptureIndexTokens(in TValue value)
    {
        var capturedValue = value;
        var createdBuckets = new List<BucketRef>();
        var preparedEntries = new List<(IStableBucketIndex Index, IndexEntryHandle Handle)>();
        try
        {
            var primaryKey = _definition.PrimaryKey.GetPrimaryKey(capturedValue);
            var primaryRegistered = _primaryIndex.TryCapture(
                primaryKey, out var primaryBucket, out var primaryHandle, out var primaryHashCode);
            if (!primaryRegistered)
            {
                primaryBucket = _primaryArena.Create();
                createdBuckets.Add(primaryBucket);
                primaryHandle = _primaryIndex.PrepareAdd(primaryKey, primaryHashCode, primaryBucket);
                preparedEntries.Add((_primaryIndex, primaryHandle));
            }

            var keys = new CapturedKeyToken[_keyIndexes.Count];
            var keyIndex = 0;
            foreach (var index in _keyIndexes.Values)
            {
                var token = new KeyToken(index.Definition.GetKey(capturedValue));
                var registered = index.Buckets.TryCapture(
                    token, out var bucket, out var entryHandle, out var hashCode);
                if (!registered)
                {
                    bucket = index.Arena.Create();
                    createdBuckets.Add(bucket);
                    entryHandle = index.Buckets.PrepareAdd(token, hashCode, bucket);
                    preparedEntries.Add((index.Buckets, entryHandle));
                }
                keys[keyIndex++] = new CapturedKeyToken(index, token, bucket, entryHandle, !registered);
            }

            var constraints = new CapturedConstraintToken[_uniqueIndexes.Length];
            for (var index = 0; index < _uniqueIndexes.Length; index++)
            {
                var constraintIndex = _uniqueIndexes[index];
                var token = new ConstraintToken(constraintIndex.Definition.GetConstraintKey(capturedValue));
                var registered = constraintIndex.Buckets.TryCapture(
                    token, out var bucket, out var entryHandle, out var hashCode);
                if (!registered)
                {
                    bucket = constraintIndex.Arena.Create();
                    createdBuckets.Add(bucket);
                    entryHandle = constraintIndex.Buckets.PrepareAdd(token, hashCode, bucket);
                    preparedEntries.Add((constraintIndex.Buckets, entryHandle));
                }
                constraints[index] = new CapturedConstraintToken(
                    constraintIndex, token, bucket, entryHandle, !registered);
            }

            return new CapturedIndexTokens(
                new CapturedPrimaryToken(
                    primaryKey, primaryBucket, primaryHandle, !primaryRegistered),
                keys,
                constraints);
        }
        catch
        {
            foreach (var prepared in preparedEntries)
                prepared.Index.ReleasePrepared(prepared.Handle);
            foreach (var bucket in createdBuckets)
                bucket.Arena.Release(bucket);
            throw;
        }
    }

    private static void PrepareAddedBucketUpdates(CapturedIndexTokens tokens)
    {
        tokens.Primary.Bucket.PrepareAdd();
        foreach (var captured in tokens.Keys) captured.Bucket.PrepareAdd();
        foreach (var captured in tokens.Constraints) captured.Bucket.PrepareAdd();
    }

    private static void CommitAddedBucketUpdates(
        CapturedIndexTokens tokens,
        SlotHandle handle)
    {
        tokens.Primary.Bucket.AddStable(handle);
        foreach (var captured in tokens.Keys) captured.Bucket.AddStable(handle);
        foreach (var captured in tokens.Constraints) captured.Bucket.AddStable(handle);
    }

    private void CommitRemovedBucketUpdates(
        CapturedIndexTokens tokens,
        SlotHandle handle)
    {
        tokens.Primary.Bucket.Remove(handle);
        ReleaseEmptyBucket(_primaryIndex, tokens.Primary.EntryHandle, tokens.Primary.Bucket);
        foreach (var captured in tokens.Keys)
        {
            captured.Bucket.Remove(handle);
            ReleaseEmptyBucket(captured.Index.Buckets, captured.EntryHandle, captured.Bucket);
            if (captured.Bucket.Count == 0) captured.Index.InvalidateSortedKeys();
        }
        foreach (var captured in tokens.Constraints)
        {
            captured.Bucket.Remove(handle);
            ReleaseEmptyBucket(captured.Index.Buckets, captured.EntryHandle, captured.Bucket);
        }
    }

    private static void PrepareReplacedBucketUpdates(
        CapturedIndexTokens oldTokens,
        CapturedIndexTokens newTokens)
    {
        PrepareMovedBucket(oldTokens.Primary.Bucket, newTokens.Primary.Bucket);
        for (var index = 0; index < oldTokens.Keys.Length; index++)
            PrepareMovedBucket(oldTokens.Keys[index].Bucket, newTokens.Keys[index].Bucket);
        for (var index = 0; index < oldTokens.Constraints.Length; index++)
            PrepareMovedBucket(oldTokens.Constraints[index].Bucket, newTokens.Constraints[index].Bucket);
    }

    private void RegisterPreparedBuckets(CapturedIndexTokens tokens)
    {
        if (tokens.Primary.NeedsRegistration)
            _primaryIndex.CommitPrepared(tokens.Primary.EntryHandle);

        foreach (var captured in tokens.Keys)
            if (captured.NeedsRegistration)
            {
                captured.Index.Buckets.CommitPrepared(captured.EntryHandle);
                captured.Index.InvalidateSortedKeys();
            }

        foreach (var captured in tokens.Constraints)
            if (captured.NeedsRegistration)
                captured.Index.Buckets.CommitPrepared(captured.EntryHandle);
    }

    private static void ReleaseEmptyBucket<TToken>(
        StableBucketIndex<TToken> index,
        IndexEntryHandle entryHandle,
        BucketRef bucket)
        where TToken : notnull
    {
        if (bucket.Count != 0) return;
        if (!index.Remove(entryHandle))
            throw new InvalidOperationException("The empty bucket index entry could not be removed.");
        bucket.Arena.Release(bucket);
    }

    private static void PrepareMovedBucket(
        BucketRef oldBucket,
        BucketRef newBucket)
    {
        if (oldBucket == newBucket) return;
        newBucket.PrepareAdd();
    }

    private void CommitReplacedBucketUpdates(
        CapturedIndexTokens oldTokens,
        CapturedIndexTokens newTokens,
        SlotHandle handle)
    {
        CommitMovedBucket(
            _primaryIndex,
            oldTokens.Primary.EntryHandle,
            oldTokens.Primary.Bucket,
            newTokens.Primary.Bucket,
            handle);
        for (var index = 0; index < oldTokens.Keys.Length; index++)
        {
            CommitMovedBucket(
                oldTokens.Keys[index].Index.Buckets,
                oldTokens.Keys[index].EntryHandle,
                oldTokens.Keys[index].Bucket,
                newTokens.Keys[index].Bucket,
                handle);
            if (oldTokens.Keys[index].Bucket != newTokens.Keys[index].Bucket)
                oldTokens.Keys[index].Index.InvalidateSortedKeys();
        }
        for (var index = 0; index < oldTokens.Constraints.Length; index++)
            CommitMovedBucket(
                oldTokens.Constraints[index].Index.Buckets,
                oldTokens.Constraints[index].EntryHandle,
                oldTokens.Constraints[index].Bucket,
                newTokens.Constraints[index].Bucket,
                handle);
    }

    private static void CommitMovedBucket<TToken>(
        StableBucketIndex<TToken> index,
        IndexEntryHandle oldEntryHandle,
        BucketRef oldBucket,
        BucketRef newBucket,
        SlotHandle handle)
        where TToken : notnull
    {
        if (oldBucket == newBucket) return;
        oldBucket.Remove(handle);
        newBucket.AddStable(handle);
        ReleaseEmptyBucket(index, oldEntryHandle, oldBucket);
    }

    private void EnsureOwnedKey<TKey>(RecordKeyDefinition<TValue, TKey> key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!_definition.TryGetKey(key.Name, out var registered) || !ReferenceEquals(registered, key))
            throw new ArgumentException("The key definition does not belong to this RecordStore.", nameof(key));
    }

    private TValue Clone(in TValue value) => CloneStrategy.Clone(value);

    private void CopyMutableConfigurationTo(RecordStoreV2<TValue, TPrimaryKey> target)
    {
        target.SchemaId = SchemaId;
        CaptureRuntimeProfile().ApplyTo(target);
    }

    public RecordStoreRuntimeProfile<TValue, TPrimaryKey> CaptureRuntimeProfile() => new()
    {
        ProfileVersion = RuntimeProfileVersion,
        CloneStrategy = CloneStrategy,
        Codec = Codec,
        IndexThreshold = IndexThreshold,
        KeyIndexMode = KeyIndexMode,
        UniqueConstraints = _uniqueConstraints.ToArray(),
        AllowPrimaryKeyDuplicate = AllowPrimaryKeyDuplicate,
        AutoMerge = AutoMerge,
        AllowUniqueConstraintViolation = AllowUniqueConstraintViolation,
        ValueFormatter = ValueFormatter,
        MergePredicate = MergePredicate,
        MergeResolver = MergeResolver,
        RecordFilter = RecordFilter,
        Limit = Limit,
        DefaultPublishFormat = DefaultPublishFormat with { }
    };

    private void EnsureWritable()
    {
        if (IsReadOnly) throw new NotSupportedException("The RecordStore is read-only.");
        if (IsPublishing) throw new InvalidOperationException("The RecordStore is publishing and cannot be modified.");
    }

    private bool CanWrite() => !IsReadOnly && !IsPublishing;

    private void EnsureConfigurationWritable() => EnsureWritable();

    private void AppendClonedValue(in TValue value)
    {
        var candidate = Clone(value);
        var id = candidate.StoreRecordId;
        if (id.Value <= 0 || _recordsById.ContainsKey(id))
            throw new InvalidOperationException("Clone input contains an invalid or duplicate StoreRecordId.");
        var slot = new Slot(id, candidate);
        var indexTokens = CaptureIndexTokens(candidate);
        PrepareAddedBucketUpdates(indexTokens);
        _slots.EnsureAdditionalCapacity(1);
        _recordsById.EnsureCapacity(_recordsById.Count + 1);
        RegisterPreparedBuckets(indexTokens);
        var location = _slots.Add(slot);
        _recordsById.Add(id, location);
        _activeCount++;
        CommitAddedBucketUpdates(indexTokens, slot.Handle);
        _nextRecordId = Math.Max(_nextRecordId, id.Value);
    }

    private bool TryAppendPublished(in TValue value)
    {
        var candidate = Clone(value);
        candidate.StoreRecordId = default;
        if (!IsCandidateValid(candidate, replacedSlot: null, allowPrimaryKeyChange: true, out var primaryKey)
            || (_primaryIndex.TryGetValue(primaryKey, out var primaryBucket) && primaryBucket.Count > 0)
            || HasAnyUniqueConflict(candidate)
            || !PassesLimit(candidate, replacedSlot: null))
            return false;

        var id = new StoreRecordId(checked(_nextRecordId + 1));
        candidate.StoreRecordId = id;
        var slot = new Slot(id, candidate);
        var indexTokens = CaptureIndexTokens(candidate);
        PrepareAddedBucketUpdates(indexTokens);
        _slots.EnsureAdditionalCapacity(1);
        _recordsById.EnsureCapacity(_recordsById.Count + 1);
        RegisterPreparedBuckets(indexTokens);
        _nextRecordId = id.Value;
        var location = _slots.Add(slot);
        _recordsById.Add(id, location);
        _activeCount++;
        CommitAddedBucketUpdates(indexTokens, slot.Handle);
        DataVersion++;
        return true;
    }

    private bool HasAnyUniqueConflict(in TValue candidate)
    {
        foreach (var index in _uniqueIndexes)
        {
            var token = new ConstraintToken(index.Definition.GetConstraintKey(candidate));
            if (index.Buckets.TryGetValue(token, out var bucket) && bucket.Count > 0) return true;
        }
        return false;
    }
}

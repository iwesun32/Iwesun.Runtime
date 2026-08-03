namespace Iwesun.Runtime.Data;

public partial class RecordStore<TValue, TPrimaryKey>
    where TValue : IRecordStoreValue
    where TPrimaryKey : notnull
{
    private readonly object _publicationSync = new();
    private readonly HashSet<string> _subscribers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _expectedSubscribers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _takenSubscribers = new(StringComparer.Ordinal);
    private long _snapshotObserverFailureCount;

    public event SnapshotPublishedEventHandler<TValue, TPrimaryKey>? SnapshotPublished;
    public long SnapshotObserverFailureCount => Interlocked.Read(ref _snapshotObserverFailureCount);
    public int PendingSnapshotSubscriberCount
    {
        get
        {
            lock (_publicationSync)
                return _expectedSubscribers.Count(subscriber => !_takenSubscribers.ContainsKey(subscriber));
        }
    }
    public bool AreAllExpectedSnapshotSubscribersTaken => PendingSnapshotSubscriberCount == 0;

    public RecordStore<TValue, TPrimaryKey> Publish(CancellationToken cancellationToken = default) =>
        Publish(RecordStorePublishTarget.Standalone, DefaultPublishFormat, cancellationToken);

    public RecordStore<TValue, TPrimaryKey> Publish(
        RecordStorePublishFormat<TValue> format,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(format);
        return Publish(RecordStorePublishTarget.Standalone, format, cancellationToken);
    }

    public RecordStore<TValue, TPrimaryKey> Publish(
        IReadOnlyList<TValue> values,
        Aggregate<TValue> aggregate,
        CancellationToken cancellationToken = default) =>
        Publish(RecordStorePublishTarget.Standalone, values, aggregate, cancellationToken);

    public RecordStore<TValue, TPrimaryKey> Publish(
        RecordStorePublishTarget target,
        RecordStorePublishFormat<TValue>? format = null,
        CancellationToken cancellationToken = default)
    {
        EnsurePublishSource(target);
        var sourceDataVersion = BeginPublish();
        RecordStore<TValue, TPrimaryKey> output;
        SnapshotPublishedEventArgs<TValue, TPrimaryKey>? publishedArgs = null;
        try
        {
            var origin = target == RecordStorePublishTarget.Snapshot
                ? RecordStoreOrigin.Snapshot
                : RecordStoreOrigin.Published;
            output = BuildPublishedOutput(format ?? DefaultPublishFormat, origin, cancellationToken);
            if (target == RecordStorePublishTarget.Snapshot)
            {
                PrepareSnapshot(output);
                publishedArgs = CommitSnapshot(output, sourceDataVersion);
            }
        }
        finally
        {
            EndPublish();
        }

        if (publishedArgs is not null) RaiseSnapshotPublished(publishedArgs);
        return output;
    }

    public RecordStore<TValue, TPrimaryKey> Publish(
        RecordStorePublishTarget target,
        IReadOnlyList<TValue> values,
        Aggregate<TValue> aggregate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(aggregate);
        EnsurePublishSource(target);
        if (values.Count == 0)
            throw new ArgumentException("Aggregate publication requires at least one value.", nameof(values));

        var sourceDataVersion = BeginPublish();
        RecordStore<TValue, TPrimaryKey> output;
        SnapshotPublishedEventArgs<TValue, TPrimaryKey>? publishedArgs = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var detached = values.Select(value => Clone(value)).ToArray();
            var aggregated = aggregate.Aggregate(detached);
            cancellationToken.ThrowIfCancellationRequested();
            output = CreateOutput(
                [aggregated],
                target == RecordStorePublishTarget.Snapshot
                    ? RecordStoreOrigin.Snapshot
                    : RecordStoreOrigin.Published,
                cancellationToken);
            if (target == RecordStorePublishTarget.Snapshot)
            {
                PrepareSnapshot(output);
                publishedArgs = CommitSnapshot(output, sourceDataVersion);
            }
        }
        finally
        {
            EndPublish();
        }

        if (publishedArgs is not null) RaiseSnapshotPublished(publishedArgs);
        return output;
    }

    public void RegisterSnapshotSubscriber(string subscriberId)
    {
        EnsureSource();
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriberId);
        lock (_publicationSync) _subscribers.Add(subscriberId);
    }

    public bool UnregisterSnapshotSubscriber(string subscriberId)
    {
        EnsureSource();
        if (string.IsNullOrWhiteSpace(subscriberId)) return false;
        lock (_publicationSync)
        {
            var removed = _subscribers.Remove(subscriberId);
            _expectedSubscribers.Remove(subscriberId);
            return removed;
        }
    }

    public bool TryTakeSnapshot(string subscriberId, out RecordStore<TValue, TPrimaryKey>? snapshot)
    {
        snapshot = null;
        if (string.IsNullOrWhiteSpace(subscriberId)) return false;
        lock (_publicationSync)
        {
            if (_snapshot is null || !_expectedSubscribers.Contains(subscriberId)
                || _takenSubscribers.ContainsKey(subscriberId)) return false;
            _takenSubscribers.Add(subscriberId, DateTimeOffset.UtcNow);
            snapshot = _snapshot;
            return true;
        }
    }

    public bool IsSnapshotTaken(string subscriberId) => GetSnapshotTakeState(subscriberId) == SnapshotTakeState.Taken;

    public SnapshotTakeState GetSnapshotTakeState(string subscriberId)
    {
        if (string.IsNullOrWhiteSpace(subscriberId)) return SnapshotTakeState.NotSubscribed;
        lock (_publicationSync)
        {
            if (_snapshot is null) return SnapshotTakeState.NoSnapshot;
            if (!_expectedSubscribers.Contains(subscriberId)) return SnapshotTakeState.NotSubscribed;
            return _takenSubscribers.ContainsKey(subscriberId) ? SnapshotTakeState.Taken : SnapshotTakeState.Pending;
        }
    }

    public bool TryGetSnapshotTakenAt(string subscriberId, out DateTimeOffset takenAt)
    {
        lock (_publicationSync) return _takenSubscribers.TryGetValue(subscriberId, out takenAt);
    }

    /// <summary>
    /// Creates a detached mutable Source copy while preserving the current installed
    /// Snapshot as a detached read-only Snapshot. Subscriber registrations and take
    /// state intentionally remain owned by the original Source.
    /// </summary>
    public RecordStore<TValue, TPrimaryKey> CreateDetachedSourceCopy(
        CancellationToken cancellationToken = default)
    {
        if (Origin != RecordStoreOrigin.Source)
            throw new NotSupportedException("Only a Source can create a detached Source copy.");

        var sourceCopy = CreateOutput(
            _slots.Where(static slot => slot.IsActive).Select(static slot => slot.Value),
            RecordStoreOrigin.Source,
            cancellationToken);
        sourceCopy.DataVersion = DataVersion;
        sourceCopy.PublicationVersion = PublicationVersion;
        sourceCopy.LastPublishedDataVersion = LastPublishedDataVersion;
        sourceCopy.PublishedAt = PublishedAt;

        var snapshot = Volatile.Read(ref _snapshot);
        if (snapshot is not null)
        {
            var snapshotCopy = snapshot.CreateOutput(
                snapshot._slots
                    .Where(static slot => slot.IsActive)
                    .Select(static slot => slot.Value),
                RecordStoreOrigin.Snapshot,
                cancellationToken);
            snapshotCopy.DataVersion = snapshot.DataVersion;
            snapshotCopy.PublicationVersion = snapshot.PublicationVersion;
            snapshotCopy.LastPublishedDataVersion = snapshot.LastPublishedDataVersion;
            snapshotCopy.PublishedAt = snapshot.PublishedAt;
            Volatile.Write(ref sourceCopy._snapshot, snapshotCopy);
        }

        return sourceCopy;
    }

    private RecordStore<TValue, TPrimaryKey> BuildPublishedOutput(
        RecordStorePublishFormat<TValue> format,
        RecordStoreOrigin origin,
        CancellationToken cancellationToken)
    {
        var values = new List<TValue>(Count);
        foreach (var slot in _slots)
        {
            if (!slot.IsActive) continue;
            cancellationToken.ThrowIfCancellationRequested();
            var value = Clone(slot.Value);
            if (format.SourceFilter is null || format.SourceFilter.Accept(value))
                values.Add(value);
        }

        var consolidated = ConsolidateConflicts(values, format.ConflictAggregator, cancellationToken);
        if (format.ResultFilter is not null)
            consolidated.RemoveAll(value => !format.ResultFilter.Accept(value.Value));
        if (format.ResultComparison is not null)
            consolidated.Sort((left, right) =>
            {
                var order = format.ResultComparison.Compare(left.Value, right.Value);
                return order != 0 ? order : left.SourceIndex.CompareTo(right.SourceIndex);
            });
        return CreateOutput(consolidated.Select(static value => value.Value), origin, cancellationToken);
    }

    private List<PublishedValue> ConsolidateConflicts(
        IReadOnlyList<TValue> values,
        Aggregate<TValue>? aggregate,
        CancellationToken cancellationToken)
    {
        var current = values.Select((value, index) => new PublishedValue(Clone(value), index)).ToList();
        for (var round = 0; round <= values.Count; round++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var next = ConsolidateConflictRound(current, aggregate, cancellationToken, out var hadConflicts);
            if (!hadConflicts) return next;
            if (next.Count >= current.Count)
                throw new InvalidOperationException("Publication aggregation did not make progress.");
            current = next;
        }
        throw new InvalidOperationException("Publication aggregation exceeded its convergence bound.");
    }

    private List<PublishedValue> ConsolidateConflictRound(
        IReadOnlyList<PublishedValue> values,
        Aggregate<TValue>? aggregate,
        CancellationToken cancellationToken,
        out bool hadConflicts)
    {
        var union = new UnionSet(values.Count);
        var primaryBuckets = new Dictionary<TPrimaryKey, int>(_definition.PrimaryKey.EqualityComparer);
        var uniqueBuckets = _uniqueConstraints
            .Select(static constraint => new Dictionary<ConstraintToken, int>(new ConstraintTokenComparer(constraint)))
            .ToArray();

        for (var index = 0; index < values.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = values[index].Value;
            var primaryKey = _definition.PrimaryKey.GetPrimaryKey(value);
            if (primaryBuckets.TryGetValue(primaryKey, out var primaryFirst)) union.Join(primaryFirst, index);
            else primaryBuckets.Add(primaryKey, index);

            for (var constraintIndex = 0; constraintIndex < _uniqueConstraints.Length; constraintIndex++)
            {
                var definition = _uniqueConstraints[constraintIndex];
                var token = new ConstraintToken(definition.GetConstraintKey(value));
                if (uniqueBuckets[constraintIndex].TryGetValue(token, out var uniqueFirst)) union.Join(uniqueFirst, index);
                else uniqueBuckets[constraintIndex].Add(token, index);
            }
        }

        var components = Enumerable.Range(0, values.Count)
            .GroupBy(union.Find)
            .OrderBy(static group => group.Min())
            .ToArray();
        var result = new List<PublishedValue>(components.Length);
        hadConflicts = false;
        foreach (var component in components)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var indices = component.Order().ToArray();
            if (indices.Length == 1)
            {
                var single = values[indices[0]];
                result.Add(new PublishedValue(Clone(single.Value), single.SourceIndex));
                continue;
            }
            hadConflicts = true;
            if (aggregate is null)
                throw new InvalidOperationException("Publication conflicts require a business Aggregate delegate.");
            var group = indices.Select(index => Clone(values[index].Value)).ToArray();
            var aggregated = aggregate.Aggregate(group);
            cancellationToken.ThrowIfCancellationRequested();
            if (IsPrimaryKeyAllNull(aggregated) || RecordFilter is not null && !RecordFilter.Accept(aggregated))
                throw new InvalidOperationException("Publication aggregate returned an invalid record.");
            result.Add(new PublishedValue(Clone(aggregated), indices.Min(index => values[index].SourceIndex)));
        }
        return result;
    }

    private RecordStore<TValue, TPrimaryKey> CreateOutput(
        IEnumerable<TValue> values,
        RecordStoreOrigin origin,
        CancellationToken cancellationToken)
    {
        var output = CreateEmptyOutput(origin);
        CopyMutableConfigurationTo(output);
        ConfigureDerivedOutputStore(output, origin);
        foreach (var value in values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!output.TryAppendPublished(value))
                throw new InvalidOperationException("The publication result violates the RecordStore definition.");
        }
        output.IsReadOnly = origin == RecordStoreOrigin.Snapshot;
        return output;
    }

    private RecordStore<TValue, TPrimaryKey> CreateEmptyOutput(RecordStoreOrigin origin)
    {
        var output = CreateDerivedOutputStore(origin)
            ?? throw new InvalidOperationException("The derived RecordStore output factory returned null.");
        if (ReferenceEquals(output, this))
            throw new InvalidOperationException("The derived RecordStore output factory returned the Source instance.");
        if (!ReferenceEquals(output.Definition, _definition))
            throw new InvalidOperationException(
                "The derived RecordStore output must use the Source definition instance.");
        if (output.Count != 0)
            throw new InvalidOperationException("The derived RecordStore output must be empty.");

        output.Origin = origin;
        output.IsReadOnly = false;
        return output;
    }

    internal RecordStore<TValue, TPrimaryKey> CreateRegisteredRestoreOutput() =>
        CreateEmptyOutput(RecordStoreOrigin.Restored);

    /// <summary>
    /// Creates an empty output instance for Publish, Snapshot, View, Clone, and compaction.
    /// Derived business tables override this method to preserve their runtime type and
    /// copy detached table-header state.
    /// </summary>
    protected virtual RecordStore<TValue, TPrimaryKey> CreateDerivedOutputStore(RecordStoreOrigin origin) =>
        new(_definition);

    /// <summary>
    /// Applies output-specific derived configuration after the Source runtime profile
    /// has been copied and before records are cloned into the output.
    /// </summary>
    protected virtual void ConfigureDerivedOutputStore(
        RecordStore<TValue, TPrimaryKey> output,
        RecordStoreOrigin origin)
    {
    }

    /// <summary>
    /// Marks detached business header state as changed without adding a collection record.
    /// </summary>
    protected void MarkAdditionalStateChanged()
    {
        if (IsPublishing)
            throw new InvalidOperationException("RecordStore cannot change additional state while publishing.");
        EnsureSource();
        DataVersion++;
    }

    private void PrepareSnapshot(RecordStore<TValue, TPrimaryKey> output)
    {
        output.Origin = RecordStoreOrigin.Snapshot;
        output.IsReadOnly = true;
    }

    private SnapshotPublishedEventArgs<TValue, TPrimaryKey> CommitSnapshot(
        RecordStore<TValue, TPrimaryKey> output,
        long sourceDataVersion)
    {
        lock (_publicationSync)
        {
            var publishedAt = DateTimeOffset.UtcNow;
            PublicationVersion++;
            LastPublishedDataVersion = sourceDataVersion;
            PublishedAt = publishedAt;
            output.PublicationVersion = PublicationVersion;
            output.LastPublishedDataVersion = sourceDataVersion;
            output.PublishedAt = publishedAt;
            _expectedSubscribers.Clear();
            _expectedSubscribers.UnionWith(_subscribers);
            _takenSubscribers.Clear();
            Volatile.Write(ref _snapshot, output);
            return new SnapshotPublishedEventArgs<TValue, TPrimaryKey>(
                TableId, PublicationVersion, sourceDataVersion, publishedAt, output);
        }
    }

    private void RaiseSnapshotPublished(SnapshotPublishedEventArgs<TValue, TPrimaryKey> args)
    {
        var handlers = SnapshotPublished?.GetInvocationList();
        if (handlers is null) return;
        foreach (var handler in handlers)
        {
            try
            {
                ((SnapshotPublishedEventHandler<TValue, TPrimaryKey>)handler)(this, args);
            }
            catch
            {
                Interlocked.Increment(ref _snapshotObserverFailureCount);
            }
        }
    }

    private void EnsurePublishSource(RecordStorePublishTarget target)
    {
        if (IsReadOnly || Origin == RecordStoreOrigin.Snapshot)
            throw new NotSupportedException("A read-only Snapshot cannot publish again.");
        if (target == RecordStorePublishTarget.Snapshot) EnsureSource();
    }

    private void EnsureSource()
    {
        if (Origin != RecordStoreOrigin.Source || IsReadOnly)
            throw new NotSupportedException("Only a mutable Source supports Snapshot publication management.");
    }

    private long BeginPublish()
    {
        if (Interlocked.CompareExchange(ref _publicationState, 1, 0) != 0)
            throw new InvalidOperationException("A publication is already in progress.");
        return DataVersion;
    }

    private void EndPublish() => Volatile.Write(ref _publicationState, 0);

    private readonly record struct PublishedValue(TValue Value, int SourceIndex);

    private sealed class UnionSet(int count)
    {
        private readonly int[] _parents = Enumerable.Range(0, count).ToArray();

        public int Find(int value)
        {
            while (_parents[value] != value)
            {
                _parents[value] = _parents[_parents[value]];
                value = _parents[value];
            }
            return value;
        }

        public void Join(int left, int right)
        {
            left = Find(left);
            right = Find(right);
            if (left != right) _parents[right] = left;
        }
    }
}

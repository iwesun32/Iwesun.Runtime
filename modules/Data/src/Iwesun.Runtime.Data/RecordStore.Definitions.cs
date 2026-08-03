using System.Runtime.CompilerServices;

namespace Iwesun.Runtime.Data;

public interface IRecordStoreValue
{
    StoreRecordId StoreRecordId { get; set; }
}

public delegate TValue ValueFormatter<TValue>(TValue value);
public delegate bool RecordFilter<TValue>(TValue value);
public delegate TValue MergeResolver<TValue>(TValue existing, TValue incoming);
public delegate bool MergeAdd<TValue>(TValue incoming, out StoreRecordId recordId);
public delegate TValue Aggregate<TValue>(IReadOnlyList<TValue> values);
public delegate TKey RecordKeySelector<TValue, TKey>(TValue value);
public delegate TPrimaryKey PrimaryKeySelector<TValue, TPrimaryKey>(TValue value);
public delegate TConstraintKey UniqueConstraintKeySelector<TValue, TConstraintKey>(TValue value);
public delegate bool PublishSourceFilter<TValue>(TValue value);
public delegate bool PublishResultFilter<TValue>(TValue value);
public delegate int PublishResultComparison<TValue>(TValue left, TValue right);
public delegate bool ViewSourceFilter<TValue>(TValue value);
public delegate int ViewGroupComparison<TValue>(TValue left, TValue right);
public delegate bool ViewResultFilter<TValue>(TValue value);
public delegate int ViewResultComparison<TValue>(TValue left, TValue right);
public delegate TResult ViewProjector<TValue, TResult>(TValue value)
    where TResult : struct;
public delegate TResult ViewAggregate<TValue, TResult>(IReadOnlyList<TValue> values)
    where TResult : struct;
public delegate bool LimitPredicate<TValue>(TValue value, IReadOnlyList<TValue> current);
public delegate void SnapshotPublishedEventHandler<TValue, TPrimaryKey>(
    object? sender,
    SnapshotPublishedEventArgs<TValue, TPrimaryKey> args)
    where TValue : IRecordStoreValue
    where TPrimaryKey : notnull;
internal delegate bool SegmentedItemPredicate<TValue>(TValue value);
internal delegate int RecordStoreSortComparison<TValue>(TValue left, TValue right);

public enum RecordStoreOrigin : byte
{
    Source,
    Published,
    Snapshot,
    View,
    Clone,
    Restored
}

public enum RecordStorePublishTarget : byte
{
    Standalone,
    Snapshot
}

public enum SnapshotTakeState : byte
{
    NoSnapshot,
    NotSubscribed,
    Pending,
    Taken
}

public enum RecordUpdateResult : byte
{
    Updated,
    NotFound,
    Rejected,
    PrimaryKeyChanged,
    Busy
}

public enum RecordStoreKeyIndexMode : byte
{
    Automatic,
    SortedTable,
    Hash
}

public sealed record RecordStoreOptions(
    bool AllowPrimaryKeyDuplicate = false,
    bool AutoMerge = false,
    bool AllowUniqueConstraintViolation = false);

public sealed class RecordStoreRuntimeProfile<TValue, TPrimaryKey>
    where TValue : IRecordStoreValue
    where TPrimaryKey : notnull
{
    public int ProfileVersion { get; init; } = 2;
    public IDeepCloneStrategy<TValue> CloneStrategy { get; init; } = ValueCopyCloneStrategy<TValue>.Instance;
    public ITableValueCodec<TValue>? Codec { get; init; }
    public int IndexThreshold { get; init; } = 1024;
    public RecordStoreKeyIndexMode KeyIndexMode { get; init; }
    public IReadOnlyList<IUniqueConstraintDefinition<TValue>> UniqueConstraints { get; init; } = [];
    public bool AllowPrimaryKeyDuplicate { get; init; }
    public bool AutoMerge { get; init; }
    public bool AllowUniqueConstraintViolation { get; init; }
    public ValueFormatter<TValue>? ValueFormatter { get; init; }
    public MergeResolver<TValue>? MergeResolver { get; init; }
    public MergeAdd<TValue>? MergeAdd { get; init; }
    public RecordFilter<TValue>? RecordFilter { get; init; }
    public LimitPredicate<TValue>? Limit { get; init; }
    public RecordStorePublishFormat<TValue> DefaultPublishFormat { get; init; } = new();

    internal RecordStoreRuntimeProfile<TValue, TPrimaryKey> Copy() => new()
    {
        ProfileVersion = ProfileVersion,
        CloneStrategy = CloneStrategy,
        Codec = Codec,
        IndexThreshold = IndexThreshold,
        KeyIndexMode = KeyIndexMode,
        UniqueConstraints = UniqueConstraints.ToArray(),
        AllowPrimaryKeyDuplicate = AllowPrimaryKeyDuplicate,
        AutoMerge = AutoMerge,
        AllowUniqueConstraintViolation = AllowUniqueConstraintViolation,
        ValueFormatter = ValueFormatter,
        MergeResolver = MergeResolver,
        MergeAdd = MergeAdd,
        RecordFilter = RecordFilter,
        Limit = Limit,
        DefaultPublishFormat = DefaultPublishFormat with { }
    };

    internal void ApplyTo(RecordStore<TValue, TPrimaryKey> store)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (ProfileVersion < 1) throw new ArgumentOutOfRangeException(nameof(ProfileVersion));
        store.RuntimeProfileVersion = ProfileVersion;
        store.CloneStrategy = CloneStrategy;
        store.Codec = Codec;
        store.IndexThreshold = IndexThreshold;
        store.KeyIndexMode = KeyIndexMode;
        store.AllowPrimaryKeyDuplicate = AllowPrimaryKeyDuplicate;
        store.AutoMerge = AutoMerge;
        store.AllowUniqueConstraintViolation = AllowUniqueConstraintViolation;
        store.ValueFormatter = ValueFormatter;
        store.MergeResolver = MergeResolver;
        store.MergeAdd = MergeAdd;
        store.RecordFilter = RecordFilter;
        store.Limit = Limit;
        store.DefaultPublishFormat = DefaultPublishFormat with { };
        store.SetUniqueConstraints(UniqueConstraints.ToArray());
    }
}

public sealed record RecordStorePublishFormat<TValue>(
    PublishSourceFilter<TValue>? SourceFilter = null,
    PublishResultFilter<TValue>? ResultFilter = null,
    PublishResultComparison<TValue>? ResultComparison = null,
    Aggregate<TValue>? ConflictAggregator = null);

public sealed record RecordStoreViewDefinition<TValue>(
    ViewSourceFilter<TValue>? SourceFilter = null,
    ViewGroupComparison<TValue>? GroupComparison = null,
    Aggregate<TValue>? GroupAggregate = null,
    ViewResultFilter<TValue>? ResultFilter = null,
    ViewResultComparison<TValue>? ResultComparison = null)
{
    public RecordStoreViewDefinition<TValue> Validate()
    {
        if ((GroupComparison is null) != (GroupAggregate is null))
            throw new ArgumentException("GroupComparison and GroupAggregate must be provided together.");
        return this;
    }
}

public sealed record RecordStoreResultViewDefinition<TValue, TResult>(
    ViewSourceFilter<TValue>? SourceFilter = null,
    ViewProjector<TValue, TResult>? Projector = null,
    ViewGroupComparison<TValue>? GroupComparison = null,
    ViewAggregate<TValue, TResult>? GroupAggregate = null,
    ViewResultFilter<TResult>? ResultFilter = null,
    ViewResultComparison<TResult>? ResultComparison = null,
    IDeepCloneStrategy<TResult>? ResultCloneStrategy = null)
    where TResult : struct
{
    public RecordStoreResultViewDefinition<TValue, TResult> Validate()
    {
        var projectionMode = Projector is not null && GroupComparison is null && GroupAggregate is null;
        var aggregationMode = Projector is null && GroupComparison is not null && GroupAggregate is not null;
        if (!projectionMode && !aggregationMode)
            throw new ArgumentException(
                "A result View requires either Projector, or GroupComparison and GroupAggregate, but not both modes.");
        if (Projector is not null)
            RecordStoreDelegateChains.EnsureSingleSelector(Projector, nameof(Projector));
        if (GroupAggregate is not null)
            RecordStoreDelegateChains.EnsureSingleSelector(GroupAggregate, nameof(GroupAggregate));
        if (RuntimeHelpers.IsReferenceOrContainsReferences<TResult>() && ResultCloneStrategy is null)
            throw new ArgumentException("A TResult containing references requires ResultCloneStrategy.");
        return this;
    }
}

public sealed record RecordConstraintConflict(
    string ConstraintName,
    IReadOnlyList<StoreRecordId> RecordIds);

public sealed class SnapshotPublishedEventArgs<TValue, TPrimaryKey>(
    Guid sourceTableId,
    long publicationVersion,
    long sourceDataVersion,
    DateTimeOffset publishedAt,
    RecordStore<TValue, TPrimaryKey> snapshot) : EventArgs
    where TValue : IRecordStoreValue
    where TPrimaryKey : notnull
{
    public Guid SourceTableId { get; } = sourceTableId;
    public long PublicationVersion { get; } = publicationVersion;
    public long SourceDataVersion { get; } = sourceDataVersion;
    public DateTimeOffset PublishedAt { get; } = publishedAt;
    public RecordStore<TValue, TPrimaryKey> Snapshot { get; } = snapshot;
}

public interface IRecordKeyDefinition<TValue>
{
    string Name { get; }
    Type KeyType { get; }
    object? GetKey(in TValue value);
    bool KeysEqual(object? left, object? right);
    int GetKeyHashCode(object? value);
    int CompareKeys(object? left, object? right);
}

public sealed class RecordKeyDefinition<TValue, TKey> : IRecordKeyDefinition<TValue>
{
    private readonly RecordKeySelector<TValue, TKey> _selector;
    private readonly IEqualityComparer<TKey> _equalityComparer;
    private readonly IComparer<TKey> _comparer;

    public RecordKeyDefinition(
        string name,
        RecordKeySelector<TValue, TKey> selector,
        IEqualityComparer<TKey>? equalityComparer = null,
        IComparer<TKey>? comparer = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(selector);
        RecordStoreDelegateChains.EnsureSingleSelector(selector, nameof(selector));
        EnsureSupportedKeyType();

        Name = name;
        _selector = selector;
        _equalityComparer = equalityComparer ?? EqualityComparer<TKey>.Default;
        _comparer = comparer ?? Comparer<TKey>.Default;
    }

    public string Name { get; }
    public Type KeyType => typeof(TKey);
    public object? GetKey(in TValue value) => _selector(value);
    public int GetKeyHashCode(object? value) => value is null
        ? 0
        : value is TKey typed
            ? _equalityComparer.GetHashCode(typed)
            : throw new ArgumentException($"Key value must be assignable to {typeof(TKey).FullName}.", nameof(value));
    public TKey GetTypedKey(in TValue value) => _selector(value);
    public bool KeysEqual(TKey left, TKey right) => _equalityComparer.Equals(left, right);
    public int CompareKeys(TKey left, TKey right) => _comparer.Compare(left, right);
    internal IEqualityComparer<TKey> EqualityComparer => _equalityComparer;
    internal IComparer<TKey> Comparer => _comparer;

    public bool KeysEqual(object? left, object? right)
    {
        if (left is null || right is null)
            return left is null && right is null;
        return left is TKey typedLeft && right is TKey typedRight
            && _equalityComparer.Equals(typedLeft, typedRight);
    }

    public int CompareKeys(object? left, object? right)
    {
        if (left is null) return right is null ? 0 : -1;
        if (right is null) return 1;
        if (left is not TKey typedLeft || right is not TKey typedRight)
            throw new ArgumentException($"Key values must be assignable to {typeof(TKey).FullName}.");
        return _comparer.Compare(typedLeft, typedRight);
    }

    private static void EnsureSupportedKeyType()
    {
        var type = typeof(TKey);
        if (!type.IsValueType && type != typeof(string))
            throw new NotSupportedException($"RecordStore keys must be value types or string; '{type.FullName}' is not supported.");
    }
}

public sealed class PrimaryKeyDefinition<TValue, TPrimaryKey>
    where TPrimaryKey : notnull
{
    private readonly PrimaryKeySelector<TValue, TPrimaryKey> _selector;

    public PrimaryKeyDefinition(
        IReadOnlyList<string> componentKeyNames,
        PrimaryKeySelector<TValue, TPrimaryKey> selector,
        IEqualityComparer<TPrimaryKey>? equalityComparer = null,
        IComparer<TPrimaryKey>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(componentKeyNames);
        ArgumentNullException.ThrowIfNull(selector);
        RecordStoreDelegateChains.EnsureSingleSelector(selector, nameof(selector));
        if (componentKeyNames.Count == 0)
            throw new ArgumentException("A business primary key requires at least one component key.", nameof(componentKeyNames));

        ComponentKeyNames = componentKeyNames.Select(ValidateName).ToArray();
        if (ComponentKeyNames.Distinct(StringComparer.Ordinal).Count() != ComponentKeyNames.Count)
            throw new ArgumentException("Business primary key component names must be unique.", nameof(componentKeyNames));

        _selector = selector;
        EqualityComparer = equalityComparer ?? EqualityComparer<TPrimaryKey>.Default;
        Comparer = comparer ?? Comparer<TPrimaryKey>.Default;
        HasExplicitComparer = comparer is not null;
    }

    public IReadOnlyList<string> ComponentKeyNames { get; }
    public IEqualityComparer<TPrimaryKey> EqualityComparer { get; }
    public IComparer<TPrimaryKey> Comparer { get; }
    internal bool HasExplicitComparer { get; }
    public TPrimaryKey GetPrimaryKey(in TValue value) => _selector(value);

    private static string ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name;
    }
}

public interface IUniqueConstraintDefinition<TValue>
{
    string Name { get; }
    IReadOnlyList<string> ComponentKeyNames { get; }
    object? GetConstraintKey(in TValue value);
    bool KeysEqual(object? left, object? right);
    int GetKeyHashCode(object? value);
}

public sealed class UniqueConstraintDefinition<TValue, TConstraintKey> : IUniqueConstraintDefinition<TValue>
{
    private readonly UniqueConstraintKeySelector<TValue, TConstraintKey> _selector;
    private readonly IEqualityComparer<TConstraintKey> _equalityComparer;

    public UniqueConstraintDefinition(
        string name,
        IReadOnlyList<string> componentKeyNames,
        UniqueConstraintKeySelector<TValue, TConstraintKey> selector,
        IEqualityComparer<TConstraintKey>? equalityComparer = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(componentKeyNames);
        ArgumentNullException.ThrowIfNull(selector);
        RecordStoreDelegateChains.EnsureSingleSelector(selector, nameof(selector));
        if (componentKeyNames.Count == 0)
            throw new ArgumentException("A unique constraint requires at least one component key.", nameof(componentKeyNames));

        Name = name;
        ComponentKeyNames = componentKeyNames.Select(ValidateName).ToArray();
        if (ComponentKeyNames.Distinct(StringComparer.Ordinal).Count() != ComponentKeyNames.Count)
            throw new ArgumentException("Unique constraint component names must be unique.", nameof(componentKeyNames));

        _selector = selector;
        _equalityComparer = equalityComparer ?? EqualityComparer<TConstraintKey>.Default;
    }

    public string Name { get; }
    public IReadOnlyList<string> ComponentKeyNames { get; }
    public object? GetConstraintKey(in TValue value) => _selector(value);
    public int GetKeyHashCode(object? value) => value is null
        ? 0
        : value is TConstraintKey typed
            ? _equalityComparer.GetHashCode(typed)
            : throw new ArgumentException($"Constraint key must be assignable to {typeof(TConstraintKey).FullName}.", nameof(value));

    public bool KeysEqual(object? left, object? right)
    {
        if (left is null || right is null)
            return left is null && right is null;
        return left is TConstraintKey typedLeft && right is TConstraintKey typedRight
            && _equalityComparer.Equals(typedLeft, typedRight);
    }

    private static string ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name;
    }
}

public sealed class RecordStoreDefinition<TValue, TPrimaryKey>
    where TValue : IRecordStoreValue
    where TPrimaryKey : notnull
{
    private readonly Dictionary<string, IRecordKeyDefinition<TValue>> _keysByName;

    public RecordStoreDefinition(
        IReadOnlyList<IRecordKeyDefinition<TValue>> keys,
        PrimaryKeyDefinition<TValue, TPrimaryKey> primaryKey)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(primaryKey);
        if (keys.Count == 0)
            throw new ArgumentException("RecordStore requires at least one key.", nameof(keys));

        Keys = keys.ToArray();
        if (Keys.Any(static key => key is null))
            throw new ArgumentException("Key definitions cannot contain null.", nameof(keys));

        _keysByName = new Dictionary<string, IRecordKeyDefinition<TValue>>(StringComparer.Ordinal);
        foreach (var key in Keys)
        {
            if (!_keysByName.TryAdd(key.Name, key))
                throw new ArgumentException($"Duplicate key name '{key.Name}'.", nameof(keys));
        }

        PrimaryKey = primaryKey;
        EnsureKnownComponents(primaryKey.ComponentKeyNames, nameof(primaryKey));
    }

    public IReadOnlyList<IRecordKeyDefinition<TValue>> Keys { get; }
    public PrimaryKeyDefinition<TValue, TPrimaryKey> PrimaryKey { get; }

    internal int ComparePrimaryKeyComponents(in TValue left, in TValue right)
    {
        foreach (var componentName in PrimaryKey.ComponentKeyNames)
        {
            var key = _keysByName[componentName];
            var comparison = key.CompareKeys(key.GetKey(left), key.GetKey(right));
            if (comparison != 0)
                return comparison;
        }

        return 0;
    }

    public bool TryGetKey(string name, out IRecordKeyDefinition<TValue>? key) =>
        _keysByName.TryGetValue(name, out key);

    internal void EnsureKnownComponents(IEnumerable<string> componentNames, string parameterName)
    {
        foreach (var componentName in componentNames)
        {
            if (!_keysByName.ContainsKey(componentName))
                throw new ArgumentException($"Unknown key component '{componentName}'.", parameterName);
        }
    }
}

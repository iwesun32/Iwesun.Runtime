using Xunit;

namespace Iwesun.Runtime.Data.Tests;

public sealed class RecordStoreComparerConsistencyTests
{
    [Fact]
    public void AutomaticBuild_ShouldFallBackForNonAdjacentEqualityGroup()
    {
        var (store, key) = CreateStore(
            new NonAdjacentEqualityComparer(),
            StringComparer.Ordinal,
            RecordStoreKeyIndexMode.Automatic);
        store.AddRange([
            new Row { Id = 1, Group = "A" },
            new Row { Id = 2, Group = "B" },
            new Row { Id = 3, Group = "C" }
        ]);

        Assert.Equal(2, store.GetValueCount(key, "A"));
        Assert.Equal(RecordStoreKeyIndexMode.Hash, store.GetActiveKeyIndexMode(key.Name));
    }

    [Fact]
    public void AutomaticAdd_ShouldFallBackWhenOrderingMergesDifferentEqualityKeys()
    {
        var (store, key) = CreateStore(
            StringComparer.Ordinal,
            FirstCharacterComparer.Instance,
            RecordStoreKeyIndexMode.Automatic);
        store.Add(new Row { Id = 1, Group = "a1" });
        Assert.Single(store.GetValues(key, "a1"));

        store.Add(new Row { Id = 2, Group = "a2" });

        Assert.Equal(RecordStoreKeyIndexMode.Hash, store.GetActiveKeyIndexMode(key.Name));
        Assert.Single(store.GetValues(key, "a1"));
        Assert.Single(store.GetValues(key, "a2"));
    }

    [Fact]
    public void AutomaticAdd_ShouldFallBackWhenEqualityMergesDifferentOrderingKeys()
    {
        var (store, key) = CreateStore(
            StringComparer.OrdinalIgnoreCase,
            StringComparer.Ordinal,
            RecordStoreKeyIndexMode.Automatic);
        store.Add(new Row { Id = 1, Group = "a" });
        Assert.Single(store.GetValues(key, "a"));

        store.Add(new Row { Id = 2, Group = "A" });

        Assert.Equal(RecordStoreKeyIndexMode.Hash, store.GetActiveKeyIndexMode(key.Name));
        Assert.Equal(2, store.GetValueCount(key, "a"));
    }

    [Fact]
    public void AutomaticUpdate_ShouldFallBackBeforeChangingAnInconsistentKey()
    {
        var (store, key) = CreateStore(
            StringComparer.OrdinalIgnoreCase,
            StringComparer.Ordinal,
            RecordStoreKeyIndexMode.Automatic);
        var id = store.Add(new Row { Id = 1, Group = "a" });
        Assert.Single(store.GetValues(key, "a"));
        Assert.True(store.TryGetRecord(id, out var value));

        Assert.Equal(RecordUpdateResult.Updated, store.TryUpdate(value with { Group = "A" }));

        Assert.Equal(RecordStoreKeyIndexMode.Hash, store.GetActiveKeyIndexMode(key.Name));
        Assert.Single(store.GetValues(key, "a"));
    }

    [Fact]
    public void AutomaticQuery_ShouldFallBackWhenQueryOrderingConflictsWithEquality()
    {
        var (store, key) = CreateStore(
            StringComparer.Ordinal,
            FirstCharacterComparer.Instance,
            RecordStoreKeyIndexMode.Automatic);
        store.Add(new Row { Id = 1, Group = "a1" });
        Assert.Single(store.GetValues(key, "a1"));

        Assert.Empty(store.GetValues(key, "a2"));
        Assert.Equal(RecordStoreKeyIndexMode.Hash, store.GetActiveKeyIndexMode(key.Name));
    }

    [Fact]
    public void ForcedSortedConflict_ShouldLeaveSourceVersionIdentityAndPreparedStateUnchanged()
    {
        var (store, key) = CreateStore(
            StringComparer.Ordinal,
            FirstCharacterComparer.Instance,
            RecordStoreKeyIndexMode.SortedTable);
        var first = store.Add(new Row { Id = 1, Group = "a1", Secondary = "x" });
        Assert.Single(store.GetValues(key, "a1"));
        var version = store.DataVersion;

        Assert.ThrowsAny<InvalidOperationException>(() => store.Add(new Row { Id = 2, Group = "a2" }));
        Assert.Equal(version, store.DataVersion);
        Assert.Single(store);
        Assert.Single(store.GetValues(key, "a1"));

        var second = store.Add(new Row { Id = 2, Group = "b1" });
        Assert.Equal(new StoreRecordId(2), second);
        Assert.Equal(new[] { first }, store.GetValues(key, "a1").Select(static value => value.StoreRecordId));
    }

    [Fact]
    public void ForcedSortedUpdateConflict_ShouldLeaveSourceVersionIdentitySummaryAndPreparedStateUnchanged()
    {
        var (store, key) = CreateStore(
            StringComparer.OrdinalIgnoreCase,
            StringComparer.Ordinal,
            RecordStoreKeyIndexMode.SortedTable);
        var id = store.Add(new Row { Id = 1, Group = "a" });
        Assert.Single(store.GetValues(key, "a"));
        Assert.True(store.TryGetRecord(id, out var original));
        var version = store.DataVersion;
        var keys = store.GetKeys(key);

        Assert.ThrowsAny<InvalidOperationException>(() => store.TryUpdate(original with { Group = "A" }));

        Assert.Equal(version, store.DataVersion);
        Assert.Single(store);
        Assert.True(store.TryGetRecord(id, out var unchanged));
        Assert.Equal(original, unchanged);
        Assert.Equal(keys, store.GetKeys(key));
        Assert.Equal(new[] { id }, store.GetValues(key, "a").Select(static value => value.StoreRecordId));

        Assert.Equal(RecordUpdateResult.Updated, store.TryUpdate(original with { Group = "b" }));
        Assert.Empty(store.GetValues(key, "a"));
        Assert.Equal(new[] { id }, store.GetValues(key, "b").Select(static value => value.StoreRecordId));
        Assert.Equal(new StoreRecordId(2), store.Add(new Row { Id = 2, Group = "c" }));
    }

    [Fact]
    public void LaterSortedIndexConflict_ShouldCancelEarlierPreparedMutation()
    {
        var idKey = new RecordKeyDefinition<Row, int>("id", static value => value.Id);
        var firstKey = new RecordKeyDefinition<Row, string?>("first", static value => value.Group);
        var secondKey = new RecordKeyDefinition<Row, string?>(
            "second",
            static value => value.Secondary,
            StringComparer.Ordinal,
            FirstCharacterComparer.Instance);
        var store = new RecordStore<Row, int>(new RecordStoreDefinition<Row, int>(
            [idKey, firstKey, secondKey],
            new PrimaryKeyDefinition<Row, int>(["id"], static value => value.Id)))
        {
            KeyIndexMode = RecordStoreKeyIndexMode.SortedTable
        };
        store.Add(new Row { Id = 1, Group = "g1", Secondary = "a1" });
        Assert.Single(store.GetValues(firstKey, "g1"));
        Assert.Single(store.GetValues(secondKey, "a1"));

        Assert.ThrowsAny<InvalidOperationException>(() =>
            store.Add(new Row { Id = 2, Group = "g2", Secondary = "a2" }));

        store.Add(new Row { Id = 2, Group = "g2", Secondary = "b1" });
        Assert.Single(store.GetValues(firstKey, "g2"));
        Assert.Single(store.GetValues(secondKey, "b1"));
    }

    [Fact]
    public void HighDuplicateSortedIndex_ShouldSizeSummaryByUniqueKeys()
    {
        var (store, key) = CreateStore(
            StringComparer.Ordinal,
            StringComparer.Ordinal,
            RecordStoreKeyIndexMode.SortedTable);
        for (var index = 0; index < 1023; index++)
            store.Add(new Row { Id = index + 1, Group = "same" });

        Assert.Equal(1, store.GetKeyCount(key));
        Assert.InRange(store.GetSortedKeySummaryCapacity(key.Name), 1, 4);
    }

    private static (RecordStore<Row, int> Store, RecordKeyDefinition<Row, string?> Key) CreateStore(
        IEqualityComparer<string?> equalityComparer,
        IComparer<string?> comparer,
        RecordStoreKeyIndexMode mode)
    {
        var idKey = new RecordKeyDefinition<Row, int>("id", static value => value.Id);
        var key = new RecordKeyDefinition<Row, string?>(
            "group", static value => value.Group, equalityComparer, comparer);
        var store = new RecordStore<Row, int>(new RecordStoreDefinition<Row, int>(
            [idKey, key],
            new PrimaryKeyDefinition<Row, int>(["id"], static value => value.Id)))
        {
            KeyIndexMode = mode,
            IndexThreshold = 1024
        };
        return (store, key);
    }

    private sealed class NonAdjacentEqualityComparer : IEqualityComparer<string?>
    {
        public bool Equals(string? left, string? right) =>
            StringComparer.Ordinal.Equals(Normalize(left), Normalize(right));

        public int GetHashCode(string? value) => StringComparer.Ordinal.GetHashCode(Normalize(value));

        private static string Normalize(string? value) => value is "A" or "C" ? "A" : value ?? "<null>";
    }

    private sealed class FirstCharacterComparer : IComparer<string?>
    {
        public static FirstCharacterComparer Instance { get; } = new();

        public int Compare(string? left, string? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return -1;
            if (right is null) return 1;
            return left[0].CompareTo(right[0]);
        }
    }

    private record struct Row : IRecordStoreValue
    {
        public StoreRecordId StoreRecordId { get; set; }
        public int Id { get; init; }
        public string? Group { get; init; }
        public string? Secondary { get; init; }
    }
}

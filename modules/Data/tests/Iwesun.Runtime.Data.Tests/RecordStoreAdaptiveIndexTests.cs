using Xunit;

namespace Iwesun.Runtime.Data.Tests;

public sealed class RecordStoreAdaptiveIndexTests
{
    [Theory]
    [InlineData(1023, RecordStoreKeyIndexMode.SortedTable)]
    [InlineData(1024, RecordStoreKeyIndexMode.Hash)]
    public void DefaultThresholdBoundary_ShouldUseMeasuredAutomaticPolicy(
        int count,
        RecordStoreKeyIndexMode expectedMode)
    {
        var idKey = new RecordKeyDefinition<AdaptiveRecord, int>("id", static value => value.Id);
        var groupKey = new RecordKeyDefinition<AdaptiveRecord, int>("group", static value => value.Id % 5);
        var store = new RecordStore<AdaptiveRecord, int>(
            new RecordStoreDefinition<AdaptiveRecord, int>(
                [idKey, groupKey],
                new PrimaryKeyDefinition<AdaptiveRecord, int>(["id"], static value => value.Id)));
        for (var index = 0; index < count; index++) store.Add(new AdaptiveRecord { Id = index + 1 });

        _ = store.GetValueCount(groupKey, 1);
        Assert.Equal(expectedMode, store.GetActiveKeyIndexMode(groupKey.Name));
    }

    [Theory]
    [InlineData(64, RecordStoreKeyIndexMode.SortedTable)]
    [InlineData(127, RecordStoreKeyIndexMode.SortedTable)]
    [InlineData(128, RecordStoreKeyIndexMode.Hash)]
    [InlineData(129, RecordStoreKeyIndexMode.Hash)]
    public void ThresholdBoundary_ShouldSelectSortedTableOrHashIndex(
        int count,
        RecordStoreKeyIndexMode expectedMode)
    {
        var selectorCalls = 0;
        var idKey = new RecordKeyDefinition<AdaptiveRecord, int>("id", static value => value.Id);
        var groupKey = new RecordKeyDefinition<AdaptiveRecord, string?>("group", value =>
        {
            selectorCalls++;
            return value.Group;
        });
        var store = new RecordStore<AdaptiveRecord, int>(
            new RecordStoreDefinition<AdaptiveRecord, int>(
                [idKey, groupKey],
                new PrimaryKeyDefinition<AdaptiveRecord, int>(["id"], static value => value.Id)))
        {
            IndexThreshold = 128
        };
        for (var index = 0; index < count; index++)
            store.Add(new AdaptiveRecord { Id = index + 1, Group = "same" });

        Assert.Equal(count, store.GetValueCount(groupKey, "same"));
        Assert.Equal(expectedMode, store.GetActiveKeyIndexMode(groupKey.Name));
        selectorCalls = 0;
        Assert.Equal(count, store.GetValueCount(groupKey, "same"));
        Assert.Equal(0, selectorCalls);
    }

    [Fact]
    public void OrdinaryKeyQueries_ShouldUseSortedTableThenPromoteAndRetainHashIndex()
    {
        var selectorCalls = 0;
        var idKey = new RecordKeyDefinition<AdaptiveRecord, int>("id", static value => value.Id);
        var groupKey = new RecordKeyDefinition<AdaptiveRecord, string?>(
            "group",
            value =>
            {
                selectorCalls++;
                return value.Group;
            });
        var store = new RecordStore<AdaptiveRecord, int>(
            new RecordStoreDefinition<AdaptiveRecord, int>(
                [idKey, groupKey],
                new PrimaryKeyDefinition<AdaptiveRecord, int>(["id"], static value => value.Id)))
        {
            IndexThreshold = 4
        };

        store.AddRange([
            new AdaptiveRecord { Id = 1, Group = "a" },
            new AdaptiveRecord { Id = 2, Group = "a" },
            new AdaptiveRecord { Id = 3, Group = null }
        ]);

        Assert.Equal(2, store.GetValueCount(groupKey, "a"));
        Assert.Equal(3, selectorCalls);
        Assert.Equal(RecordStoreKeyIndexMode.SortedTable, store.GetActiveKeyIndexMode(groupKey.Name));
        Assert.Equal(new string?[] { null, "a" }, store.GetKeys(groupKey));
        Assert.Equal(3, selectorCalls);

        store.IndexThreshold = 3;
        Assert.Equal(2, store.GetKeyCount(groupKey));
        Assert.Equal(6, selectorCalls);
        Assert.Equal(RecordStoreKeyIndexMode.Hash, store.GetActiveKeyIndexMode(groupKey.Name));

        Assert.Equal(2, store.GetValues(groupKey, "a").Length);
        Assert.Equal(6, selectorCalls);

        store.IndexThreshold = 100;
        store.Add(new AdaptiveRecord { Id = 4, Group = "b" });
        Assert.Equal(7, selectorCalls);
        Assert.Equal(3, store.GetKeyCount(groupKey));
        Assert.Equal(7, selectorCalls);
    }

    [Fact]
    public void DifferentOrdinaryKeys_ShouldActivateIndependently()
    {
        var firstCalls = 0;
        var secondCalls = 0;
        var idKey = new RecordKeyDefinition<AdaptiveRecord, int>("id", static value => value.Id);
        var firstKey = new RecordKeyDefinition<AdaptiveRecord, string?>("first", value =>
        {
            firstCalls++;
            return value.Group;
        });
        var secondKey = new RecordKeyDefinition<AdaptiveRecord, string?>("second", value =>
        {
            secondCalls++;
            return value.Secondary;
        });
        var store = new RecordStore<AdaptiveRecord, int>(
            new RecordStoreDefinition<AdaptiveRecord, int>(
                [idKey, firstKey, secondKey],
                new PrimaryKeyDefinition<AdaptiveRecord, int>(["id"], static value => value.Id)))
        {
            IndexThreshold = 2
        };

        store.AddRange([
            new AdaptiveRecord { Id = 1, Group = "a", Secondary = "x" },
            new AdaptiveRecord { Id = 2, Group = "b", Secondary = "x" }
        ]);

        Assert.Equal(2, store.GetKeyCount(firstKey));
        Assert.Equal(2, firstCalls);
        Assert.Equal(0, secondCalls);

        Assert.Equal(2, store.GetValueCount(secondKey, "x"));
        Assert.Equal(2, secondCalls);
        Assert.Equal(2, firstCalls);
    }

    [Fact]
    public void IndexModeSwitch_ShouldForceSortedOrHashAndResetExistingOrdinaryIndexes()
    {
        var idKey = new RecordKeyDefinition<AdaptiveRecord, int>("id", static value => value.Id);
        var groupKey = new RecordKeyDefinition<AdaptiveRecord, string?>("group", static value => value.Group);
        var store = new RecordStore<AdaptiveRecord, int>(
            new RecordStoreDefinition<AdaptiveRecord, int>(
                [idKey, groupKey],
                new PrimaryKeyDefinition<AdaptiveRecord, int>(["id"], static value => value.Id)))
        {
            IndexThreshold = 1,
            KeyIndexMode = RecordStoreKeyIndexMode.SortedTable
        };
        store.AddRange([
            new AdaptiveRecord { Id = 1, Group = "a" },
            new AdaptiveRecord { Id = 2, Group = "b" }
        ]);

        Assert.Equal(2, store.GetKeyCount(groupKey));
        Assert.Equal(RecordStoreKeyIndexMode.SortedTable, store.GetActiveKeyIndexMode(groupKey.Name));

        store.KeyIndexMode = RecordStoreKeyIndexMode.Hash;
        Assert.Equal(RecordStoreKeyIndexMode.Automatic, store.GetActiveKeyIndexMode(groupKey.Name));
        Assert.Equal(2, store.GetKeyCount(groupKey));
        Assert.Equal(RecordStoreKeyIndexMode.Hash, store.GetActiveKeyIndexMode(groupKey.Name));

        store.IndexThreshold = 100;
        store.KeyIndexMode = RecordStoreKeyIndexMode.Automatic;
        Assert.Equal(2, store.GetKeyCount(groupKey));
        Assert.Equal(RecordStoreKeyIndexMode.SortedTable, store.GetActiveKeyIndexMode(groupKey.Name));
    }

    [Theory]
    [InlineData(RecordStoreKeyIndexMode.SortedTable)]
    [InlineData(RecordStoreKeyIndexMode.Hash)]
    public void BothIndexModes_ShouldMaintainStableResultsAcrossAddUpdateAndDeprecate(
        RecordStoreKeyIndexMode mode)
    {
        var idKey = new RecordKeyDefinition<AdaptiveRecord, int>("id", static value => value.Id);
        var groupKey = new RecordKeyDefinition<AdaptiveRecord, string?>("group", static value => value.Group);
        var store = new RecordStore<AdaptiveRecord, int>(
            new RecordStoreDefinition<AdaptiveRecord, int>(
                [idKey, groupKey],
                new PrimaryKeyDefinition<AdaptiveRecord, int>(["id"], static value => value.Id)))
        {
            KeyIndexMode = mode
        };
        var first = store.Add(new AdaptiveRecord { Id = 1, Group = "b" });
        var second = store.Add(new AdaptiveRecord { Id = 2, Group = "a" });
        var third = store.Add(new AdaptiveRecord { Id = 3, Group = "a" });

        Assert.Equal(new[] { second, third },
            store.GetValues(groupKey, "a").Select(static value => value.StoreRecordId));
        var fourth = store.Add(new AdaptiveRecord { Id = 4, Group = "a" });
        Assert.True(store.TryGetRecord(first, out var moving));
        Assert.Equal(RecordUpdateResult.Updated, store.TryUpdate(moving with { Group = "a" }));
        Assert.True(store.TryDeprecate(third));

        Assert.Equal(new[] { first, second, fourth },
            store.GetValues(groupKey, "a").Select(static value => value.StoreRecordId));
        Assert.Equal(3, store.GetValueCount(groupKey, "a"));
        Assert.Equal(new string?[] { "a" }, store.GetKeys(groupKey));
        Assert.Equal(1, store.GetKeyCount(groupKey));
    }

    [Theory]
    [InlineData(RecordStoreKeyIndexMode.SortedTable)]
    [InlineData(RecordStoreKeyIndexMode.Hash)]
    public void KeySummary_ShouldServeRepeatedKeyListAndCountWithoutRescanningOrResorting(
        RecordStoreKeyIndexMode mode)
    {
        var comparer = new CountingStringComparer();
        var idKey = new RecordKeyDefinition<AdaptiveRecord, int>("id", static value => value.Id);
        var groupKey = new RecordKeyDefinition<AdaptiveRecord, string?>(
            "group", static value => value.Group, comparer, comparer);
        var store = new RecordStore<AdaptiveRecord, int>(
            new RecordStoreDefinition<AdaptiveRecord, int>(
                [idKey, groupKey],
                new PrimaryKeyDefinition<AdaptiveRecord, int>(["id"], static value => value.Id)))
        {
            KeyIndexMode = mode
        };
        store.AddRange([
            new AdaptiveRecord { Id = 1, Group = "b" },
            new AdaptiveRecord { Id = 2, Group = "a" },
            new AdaptiveRecord { Id = 3, Group = "a" }
        ]);

        Assert.Equal(new string?[] { "a", "b" }, store.GetKeys(groupKey));
        comparer.CompareCalls = 0;
        Assert.Equal(2, store.GetKeyCount(groupKey));
        Assert.Equal(new string?[] { "a", "b" }, store.GetKeys(groupKey));
        Assert.Equal(0, comparer.CompareCalls);

        store.Add(new AdaptiveRecord { Id = 4, Group = "c" });
        Assert.Equal(new string?[] { "a", "b", "c" }, store.GetKeys(groupKey));
        Assert.Equal(3, store.GetKeyCount(groupKey));
    }

    [Fact]
    public void AutomaticMode_ShouldFallBackToHashWhenKeyEqualityAndOrderingAreInconsistent()
    {
        var idKey = new RecordKeyDefinition<AdaptiveRecord, int>("id", static value => value.Id);
        var groupKey = new RecordKeyDefinition<AdaptiveRecord, string?>(
            "group",
            static value => value.Group,
            StringComparer.OrdinalIgnoreCase,
            StringComparer.Ordinal);
        var store = new RecordStore<AdaptiveRecord, int>(
            new RecordStoreDefinition<AdaptiveRecord, int>(
                [idKey, groupKey],
                new PrimaryKeyDefinition<AdaptiveRecord, int>(["id"], static value => value.Id)))
        {
            IndexThreshold = 100
        };
        store.AddRange([
            new AdaptiveRecord { Id = 1, Group = "a" },
            new AdaptiveRecord { Id = 2, Group = "A" }
        ]);

        Assert.Equal(2, store.GetValueCount(groupKey, "a"));
        Assert.Equal(RecordStoreKeyIndexMode.Hash, store.GetActiveKeyIndexMode(groupKey.Name));
    }

    [Fact]
    public void ForcedSortedMode_ShouldRejectInconsistentKeyEqualityAndOrdering()
    {
        var idKey = new RecordKeyDefinition<AdaptiveRecord, int>("id", static value => value.Id);
        var groupKey = new RecordKeyDefinition<AdaptiveRecord, string?>(
            "group",
            static value => value.Group,
            StringComparer.OrdinalIgnoreCase,
            StringComparer.Ordinal);
        var store = new RecordStore<AdaptiveRecord, int>(
            new RecordStoreDefinition<AdaptiveRecord, int>(
                [idKey, groupKey],
                new PrimaryKeyDefinition<AdaptiveRecord, int>(["id"], static value => value.Id)))
        {
            KeyIndexMode = RecordStoreKeyIndexMode.SortedTable
        };
        store.AddRange([
            new AdaptiveRecord { Id = 1, Group = "a" },
            new AdaptiveRecord { Id = 2, Group = "A" }
        ]);

        Assert.ThrowsAny<InvalidOperationException>(() => store.GetValueCount(groupKey, "a"));
        Assert.Equal(RecordStoreKeyIndexMode.Automatic, store.GetActiveKeyIndexMode(groupKey.Name));
    }

    private sealed class CountingStringComparer : IEqualityComparer<string?>, IComparer<string?>
    {
        public int CompareCalls { get; set; }

        public bool Equals(string? left, string? right) => StringComparer.Ordinal.Equals(left, right);

        public int GetHashCode(string? value) => value is null ? 0 : StringComparer.Ordinal.GetHashCode(value);

        public int Compare(string? left, string? right)
        {
            CompareCalls++;
            return StringComparer.Ordinal.Compare(left, right);
        }
    }

    private record struct AdaptiveRecord : IRecordStoreValue
    {
        public StoreRecordId StoreRecordId { get; set; }
        public int Id { get; init; }
        public string? Group { get; init; }
        public string? Secondary { get; init; }
    }
}

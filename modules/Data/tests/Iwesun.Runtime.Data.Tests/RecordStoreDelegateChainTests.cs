using Xunit;

namespace Iwesun.Runtime.Data.Tests;

public sealed class RecordStoreDelegateChainTests
{
    [Fact]
    public void FormatterAndMergeResolver_ShouldPassPreviousResultToNextDelegate()
    {
        ValueFormatter<Row> formatter = static value => value with { Value = value.Value + 1 };
        formatter += static value => value with { Value = value.Value * 2 };

        MergeResolver<Row> resolver =
            static (existing, incoming) => existing with { Value = existing.Value + incoming.Value };
        resolver += static (existing, incoming) => existing with { Value = existing.Value * incoming.Value };

        Assert.Equal(6, formatter.Format(new Row { Value = 2 }).Value);
        Assert.Equal(15, resolver.Resolve(new Row { Value = 2 }, new Row { Value = 3 }).Value);
    }

    [Fact]
    public void FiltersAndLimits_ShouldRequireEveryDelegateToAccept()
    {
        RecordFilter<Row> filter = static value => value.Value > 0;
        filter += static value => value.Value < 10;
        LimitPredicate<Row> limit = static (value, _) => value.Value != 4;
        limit += static (_, current) => current.Count < 2;

        Assert.True(filter.Accept(new Row { Value = 5 }));
        Assert.False(filter.Accept(new Row { Value = 10 }));
        Assert.True(limit.Accept(new Row { Value = 3 }, []));
        Assert.False(limit.Accept(new Row { Value = 4 }, []));
        Assert.False(limit.Accept(new Row { Value = 3 }, [new(), new()]));
    }

    [Fact]
    public void Comparisons_ShouldUseFirstNonZeroResult()
    {
        PublishResultComparison<Row> comparison =
            static (left, right) => left.Group.CompareTo(right.Group);
        comparison += static (left, right) => left.Value.CompareTo(right.Value);

        Assert.True(comparison.Compare(
            new Row { Group = 1, Value = 9 },
            new Row { Group = 2, Value = 0 }) < 0);
        Assert.True(comparison.Compare(
            new Row { Group = 1, Value = 9 },
            new Row { Group = 1, Value = 10 }) < 0);
    }

    [Fact]
    public void MergeAdd_ShouldStopAtFirstSuccessfulDelegate()
    {
        var calls = 0;
        MergeAdd<Row> merge = (Row _, out StoreRecordId id) =>
        {
            calls++;
            id = default;
            return false;
        };
        merge += (Row _, out StoreRecordId id) =>
        {
            calls++;
            id = new StoreRecordId(7);
            return true;
        };
        merge += (Row _, out StoreRecordId id) =>
        {
            calls++;
            id = new StoreRecordId(9);
            return true;
        };

        Assert.True(merge.TryMerge(new Row(), out var recordId));
        Assert.Equal(new StoreRecordId(7), recordId);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void StructuralSelector_ShouldRejectMulticastDelegate()
    {
        RecordKeySelector<Row, int> selector = static value => value.Group;
        selector += static value => value.Value;

        Assert.Throws<ArgumentException>(() =>
            new RecordKeyDefinition<Row, int>("invalid", selector));
    }

    [Fact]
    public void SnapshotPublished_ShouldUseExplicitEventAndNotifyEveryObserver()
    {
        var key = new RecordKeyDefinition<Row, int>("group", static value => value.Group);
        var definition = new RecordStoreDefinition<Row, int>(
            [key],
            new PrimaryKeyDefinition<Row, int>(["group"], static value => value.Group));
        var store = new RecordStore<Row, int>(definition);
        var calls = 0;
        SnapshotPublishedEventHandler<Row, int> first = (_, _) => calls++;
        SnapshotPublishedEventHandler<Row, int> second = (_, _) => calls++;
        store.SnapshotPublished += first;
        store.SnapshotPublished += second;

        store.Publish(RecordStorePublishTarget.Snapshot);

        Assert.Equal(2, calls);
    }

    private record struct Row : IRecordStoreValue
    {
        public StoreRecordId StoreRecordId { get; set; }
        public int Group { get; init; }
        public int Value { get; init; }
    }
}

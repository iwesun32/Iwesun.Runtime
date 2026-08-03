using Xunit;

namespace Iwesun.Runtime.Data.Tests;

public sealed class RecordStoreResultViewTests
{
    [Fact]
    public async Task ResultView_ShouldSupportExclusiveProjectionAndAggregationModes()
    {
        var store = CreateStore();
        store.AddRange([
            new Row { Id = 1, Group = "a", Value = 2 },
            new Row { Id = 2, Group = "a", Value = 3 },
            new Row { Id = 3, Group = "b", Value = 4 }
        ]);

        var projected = store.CreateView(new RecordStoreResultViewDefinition<Row, int>(
            SourceFilter: static value => value.Value >= 3,
            Projector: static value => value.Value * 10,
            ResultComparison: static (left, right) => right.CompareTo(left)));
        var aggregated = await store.CreateViewAsync(new RecordStoreResultViewDefinition<Row, Summary>(
            GroupComparison: static (left, right) => StringComparer.Ordinal.Compare(left.Group, right.Group),
            GroupAggregate: static values => new Summary(values[0].Group, values.Sum(static value => value.Value)),
            ResultComparison: static (left, right) => StringComparer.Ordinal.Compare(left.Group, right.Group),
            ResultCloneStrategy: ValueCopyCloneStrategy<Summary>.Instance));

        Assert.Equal(new int[] { 40, 30 }, projected);
        Assert.Equal(new Summary[] { new("a", 5), new("b", 4) }, aggregated);
        Assert.Empty(store.CreateView(new RecordStoreResultViewDefinition<Row, int>(
            SourceFilter: static _ => false,
            Projector: static value => value.Value)));
        Assert.Throws<ArgumentException>(() => store.CreateView(
            new RecordStoreResultViewDefinition<Row, int>()));
        Assert.Throws<ArgumentException>(() => store.CreateView(
            new RecordStoreResultViewDefinition<Row, int>(
                Projector: static value => value.Value,
                GroupComparison: static (left, right) => left.Id.CompareTo(right.Id),
                GroupAggregate: static values => values.Sum(static value => value.Value))));
    }

    [Fact]
    public void ResultContainingReferences_ShouldRequireAndUseCloneStrategy()
    {
        var store = CreateStore();
        store.Add(new Row { Id = 1, Group = "a", Value = 2 });
        var definitionWithoutClone = new RecordStoreResultViewDefinition<Row, ReferenceSummary>(
            Projector: static value => new ReferenceSummary([value.Value]));

        Assert.Throws<ArgumentException>(() => store.CreateView(definitionWithoutClone));

        var result = store.CreateView(definitionWithoutClone with
        {
            ResultCloneStrategy = new ReferenceSummaryCloneStrategy()
        });
        result[0].Values[0] = 99;
        var second = store.CreateView(definitionWithoutClone with
        {
            ResultCloneStrategy = new ReferenceSummaryCloneStrategy()
        });
        Assert.Equal(2, second[0].Values[0]);
    }

    private static RecordStore<Row, int> CreateStore()
    {
        var id = new RecordKeyDefinition<Row, int>("id", static value => value.Id);
        var group = new RecordKeyDefinition<Row, string?>("group", static value => value.Group);
        return new RecordStore<Row, int>(new RecordStoreDefinition<Row, int>(
            [id, group],
            new PrimaryKeyDefinition<Row, int>(["id"], static value => value.Id)));
    }

    private readonly record struct Summary(string? Group, int Total);
    private readonly record struct ReferenceSummary(int[] Values);

    private sealed class ReferenceSummaryCloneStrategy : IDeepCloneStrategy<ReferenceSummary>
    {
        public ReferenceSummary Clone(in ReferenceSummary value) => new((int[])value.Values.Clone());
    }

    private record struct Row : IRecordStoreValue
    {
        public StoreRecordId StoreRecordId { get; set; }
        public int Id { get; init; }
        public string? Group { get; init; }
        public int Value { get; init; }
    }
}

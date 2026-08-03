using Xunit;

namespace Iwesun.Runtime.Data.Tests;

public sealed class RecordStoreIncrementalIndexTests
{
    [Fact]
    public void UpdateAndDeprecate_ShouldTouchOnlyTheAffectedIndexEntries()
    {
        var idCalls = 0;
        var groupCalls = 0;
        var aliasCalls = 0;
        var idKey = new RecordKeyDefinition<Row, int>("id", value =>
        {
            idCalls++;
            return value.Id;
        });
        var groupKey = new RecordKeyDefinition<Row, string?>("group", value =>
        {
            groupCalls++;
            return value.Group;
        });
        var aliasKey = new RecordKeyDefinition<Row, string?>("alias", value =>
        {
            aliasCalls++;
            return value.Alias;
        });
        var store = new RecordStore<Row, int>(
            new RecordStoreDefinition<Row, int>(
                [idKey, groupKey, aliasKey],
                new PrimaryKeyDefinition<Row, int>(["id"], value =>
                {
                    idCalls++;
                    return value.Id;
                })))
        {
            IndexThreshold = 128
        };
        store.SetUniqueConstraints([
            new UniqueConstraintDefinition<Row, string?>("alias-unique", ["alias"], value =>
            {
                aliasCalls++;
                return value.Alias;
            })
        ]);
        for (var index = 0; index < 1_000; index++)
            store.Add(new Row { Id = index + 1, Group = $"g-{index % 10}", Alias = $"a-{index}" });
        _ = store.GetKeyCount(groupKey);

        idCalls = groupCalls = aliasCalls = 0;
        Assert.True(store.TryGetRecord(new StoreRecordId(500), out var value));
        value = value with { Group = "changed", Alias = "changed-alias" };
        Assert.Equal(RecordUpdateResult.Updated, store.TryUpdate(value));

        Assert.InRange(idCalls, 1, 20);
        Assert.InRange(groupCalls, 1, 20);
        Assert.InRange(aliasCalls, 1, 20);
        Assert.Single(store.GetValues(groupKey, "changed"));

        idCalls = groupCalls = aliasCalls = 0;
        Assert.True(store.TryDeprecate(new StoreRecordId(500)));
        Assert.InRange(idCalls, 1, 10);
        Assert.InRange(groupCalls, 1, 10);
        Assert.InRange(aliasCalls, 1, 10);
        Assert.Empty(store.GetValues(groupKey, "changed"));
        Assert.Equal(999, store.Count);
    }

    private record struct Row : IRecordStoreValue
    {
        public StoreRecordId StoreRecordId { get; set; }
        public int Id { get; init; }
        public string? Group { get; init; }
        public string? Alias { get; init; }
    }
}

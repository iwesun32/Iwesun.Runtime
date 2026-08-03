using Xunit;

namespace Iwesun.Runtime.Data.Tests;

public sealed class RecordStoreViewCloneTests
{
    [Fact]
    public async Task View_ShouldFilterGroupSortAndSupportAsyncEntry()
    {
        var store = CreateStore(new RecordStoreOptions(AllowPrimaryKeyDuplicate: true));
        store.Add(new Row { Group = "b", Code = "1", Value = 2 });
        store.Add(new Row { Group = "a", Code = "1", Value = 3 });
        store.Add(new Row { Group = "a", Code = "1", Value = 4 });
        var definition = new RecordStoreViewDefinition<Row>(
            SourceFilter: static value => value.Value >= 3,
            GroupComparison: static (left, right) => StringComparer.Ordinal.Compare(left.Group, right.Group),
            GroupAggregate: static values => values[0] with { Value = values.Sum(static value => value.Value) },
            ResultComparison: static (left, right) => right.Value.CompareTo(left.Value));

        var view = store.CreateView(definition);
        var asyncView = await store.CreateViewAsync(definition);

        Assert.Equal(RecordStoreOrigin.View, view.Origin);
        Assert.False(view.IsReadOnly);
        Assert.Equal(7, Assert.Single(view).Value);
        Assert.Equal(7, Assert.Single(asyncView).Value);
    }

    [Fact]
    public void SnapshotClone_ShouldBeWritableAndPreserveIds()
    {
        var store = CreateStore();
        var id = store.Add(new Row { Group = "a", Code = "1", Value = 2 });
        var snapshot = store.Publish(RecordStorePublishTarget.Snapshot);

        var clone = snapshot.DeepClone();

        Assert.Equal(RecordStoreOrigin.Clone, clone.Origin);
        Assert.False(clone.IsReadOnly);
        Assert.True(clone.TryGetRecord(id, out _));
        Assert.Equal(new StoreRecordId(2), clone.Add(new Row { Group = "b", Code = "2", Value = 3 }));
    }

    [Fact]
    public async Task AccessGate_ShouldExposeLeaseStateAndReleaseOnce()
    {
        var store = CreateStore();
        Assert.False(store.AccessGate.IsEntered);

        await using (var lease = await store.AccessGate.EnterAsync())
        {
            Assert.True(store.AccessGate.IsEntered);
            await lease.DisposeAsync();
            Assert.False(store.AccessGate.IsEntered);
        }

        using var syncLease = store.AccessGate.Enter();
        Assert.True(store.AccessGate.IsEntered);
    }

    private static RecordStore<Row, PrimaryKey> CreateStore(RecordStoreOptions? options = null)
    {
        var group = new RecordKeyDefinition<Row, string?>("group", static value => value.Group);
        var code = new RecordKeyDefinition<Row, string?>("code", static value => value.Code);
        var store = new RecordStore<Row, PrimaryKey>(new RecordStoreDefinition<Row, PrimaryKey>(
            [group, code],
            new PrimaryKeyDefinition<Row, PrimaryKey>(
                ["group", "code"], static value => new PrimaryKey(value.Group, value.Code))));
        if (options is not null) store.ApplyOptions(options);
        return store;
    }

    private readonly record struct PrimaryKey(string? Group, string? Code) : IComparable<PrimaryKey>
    {
        public int CompareTo(PrimaryKey other)
        {
            var group = StringComparer.Ordinal.Compare(Group, other.Group);
            return group != 0 ? group : StringComparer.Ordinal.Compare(Code, other.Code);
        }
    }

    private record struct Row : IRecordStoreValue
    {
        public StoreRecordId StoreRecordId { get; set; }
        public string? Group { get; init; }
        public string? Code { get; init; }
        public int Value { get; init; }
    }
}

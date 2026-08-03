using System.Text.Json;
using Xunit;

namespace Iwesun.Runtime.Data.Tests;

public sealed class RecordStoreSafetyTests
{
    [Fact]
    public void SelectorExceptions_ShouldLeaveAddUpdateAndDeprecateAtomic()
    {
        var throwFromGroup = false;
        var idKey = new RecordKeyDefinition<Row, int>("id", static value => value.Id);
        var groupKey = new RecordKeyDefinition<Row, string?>("group", value =>
        {
            if (throwFromGroup) throw new InvalidOperationException("selector failure");
            return value.Group;
        });
        var store = new RecordStore<Row, int>(new RecordStoreDefinition<Row, int>(
            [idKey, groupKey],
            new PrimaryKeyDefinition<Row, int>(["id"], static value => value.Id)))
        {
            IndexThreshold = 1
        };
        var firstId = store.Add(new Row { Id = 1, Group = "original", Value = 1 });
        _ = store.GetKeyCount(groupKey);
        var version = store.DataVersion;

        throwFromGroup = true;
        Assert.Throws<InvalidOperationException>(() =>
            store.Add(new Row { Id = 2, Group = "add", Value = 2 }));
        Assert.Single(store);
        Assert.Equal(version, store.DataVersion);

        Assert.True(store.TryGetRecord(firstId, out var value));
        Assert.Throws<InvalidOperationException>(() => store.TryUpdate(value with { Group = "updated" }));
        Assert.Throws<InvalidOperationException>(() => store.TryDeprecate(firstId));
        Assert.Single(store);
        Assert.Equal(version, store.DataVersion);

        throwFromGroup = false;
        Assert.Single(store.GetValues(groupKey, "original"));
        Assert.Empty(store.GetValues(groupKey, "updated"));
        Assert.Equal(new StoreRecordId(2), store.Add(new Row { Id = 2, Group = "second", Value = 2 }));
    }

    [Fact]
    public void SortedComparerExceptions_ShouldLeaveAddUpdateAndDeprecateAtomic()
    {
        var comparer = new ThrowingStringComparer();
        var idKey = new RecordKeyDefinition<Row, int>("id", static value => value.Id);
        var groupKey = new RecordKeyDefinition<Row, string?>(
            "group",
            static value => value.Group,
            comparer,
            comparer);
        var store = new RecordStore<Row, int>(new RecordStoreDefinition<Row, int>(
            [idKey, groupKey],
            new PrimaryKeyDefinition<Row, int>(["id"], static value => value.Id)))
        {
            KeyIndexMode = RecordStoreKeyIndexMode.SortedTable
        };
        var firstId = store.Add(new Row { Id = 1, Group = "a", Value = 1 });
        store.Add(new Row { Id = 2, Group = "b", Value = 2 });
        Assert.Equal(2, store.GetKeyCount(groupKey));
        var version = store.DataVersion;

        comparer.Throw = true;
        Assert.Throws<InvalidOperationException>(() =>
            store.Add(new Row { Id = 3, Group = "c", Value = 3 }));
        Assert.True(store.TryGetRecord(firstId, out var first));
        Assert.Throws<InvalidOperationException>(() => store.TryUpdate(first with { Group = "z" }));
        Assert.Throws<InvalidOperationException>(() => store.TryDeprecate(firstId));

        comparer.Throw = false;
        Assert.Equal(2, store.Count);
        Assert.Equal(version, store.DataVersion);
        Assert.Single(store.GetValues(groupKey, "a"));
        Assert.Single(store.GetValues(groupKey, "b"));
        Assert.Empty(store.GetValues(groupKey, "c"));
        Assert.Empty(store.GetValues(groupKey, "z"));
        Assert.Equal(new StoreRecordId(3), store.Add(new Row { Id = 3, Group = "c", Value = 3 }));
    }

    [Fact]
    public void Publish_ShouldFreezeEveryMutableRuntimePolicy()
    {
        var store = CreateStore($"safety.freeze-{Guid.NewGuid():N}");
        store.Add(new Row { Id = 1, Group = "a", Value = 1 });

        store.Publish(new RecordStorePublishFormat<Row>(SourceFilter: value =>
        {
            Assert.Throws<InvalidOperationException>(() => store.SchemaId = "blocked");
            Assert.Throws<InvalidOperationException>(() => store.CloneStrategy = ValueCopyCloneStrategy<Row>.Instance);
            Assert.Throws<InvalidOperationException>(() => store.Codec = new RowCodec());
            Assert.Throws<InvalidOperationException>(() => store.RuntimeProfileVersion++);
            Assert.Throws<InvalidOperationException>(() => store.IndexThreshold++);
            Assert.Throws<InvalidOperationException>(() => store.KeyIndexMode = RecordStoreKeyIndexMode.Hash);
            Assert.Throws<InvalidOperationException>(() => store.AllowPrimaryKeyDuplicate = true);
            Assert.Throws<InvalidOperationException>(() => store.AutoMerge = true);
            Assert.Throws<InvalidOperationException>(() => store.AllowUniqueConstraintViolation = true);
            Assert.Throws<InvalidOperationException>(() => store.ValueFormatter = static item => item);
            Assert.Throws<InvalidOperationException>(() => store.MergeResolver = static (left, _) => left);
            Assert.Throws<InvalidOperationException>(() =>
                store.MergeAdd = static (Row _, out StoreRecordId id) =>
                {
                    id = default;
                    return false;
                });
            Assert.Throws<InvalidOperationException>(() => store.RecordFilter = static _ => true);
            Assert.Throws<InvalidOperationException>(() => store.Limit = static (_, _) => true);
            Assert.Throws<InvalidOperationException>(() => store.DefaultPublishFormat = new());
            Assert.Throws<InvalidOperationException>(() => store.ApplyOptions(new RecordStoreOptions()));
            Assert.Throws<InvalidOperationException>(() => store.SetUniqueConstraints([]));
            Assert.Throws<InvalidOperationException>(() => store.Publish());
            return true;
        }));

        Assert.False(store.IsPublishing);
        Assert.Single(store);
    }

    [Fact]
    public void RegisteredRuntimeProfileChanges_ShouldRefreshBeforePersistence()
    {
        var store = CreateStore($"safety.profile-{Guid.NewGuid():N}");
        store.Add(new Row { Id = 1, Group = "a", Value = 1 });
        RecordStoreSchemaRegistry<Row, int>.Register(store);
        _ = store.ToJson();

        RecordFilter<Row> filter = static value => value.Value > 0;
        store.RecordFilter = filter;
        store.RuntimeProfileVersion++;
        RecordStoreSchemaRegistry<Row, int>.Register(store);
        var restored = RecordStore<Row, int>.RestoreJson(store.ToJson());
        Assert.Same(filter, restored.RecordFilter);
        Assert.Equal(store.RuntimeProfileVersion, restored.RuntimeProfileVersion);
    }

    [Fact]
    public void CancelledStableAggregation_ShouldRetainPreviousSnapshotAndUnfreezeSource()
    {
        var store = CreateStore($"safety.cancel-{Guid.NewGuid():N}");
        store.AllowPrimaryKeyDuplicate = true;
        store.Add(new Row { Id = 1, Group = "a", Value = 1 });
        var previous = store.Publish(RecordStorePublishTarget.Snapshot);
        store.Add(new Row { Id = 1, Group = "b", Value = 2 });
        using var cancellation = new CancellationTokenSource();

        Assert.Throws<OperationCanceledException>(() => store.Publish(
            RecordStorePublishTarget.Snapshot,
            new RecordStorePublishFormat<Row>(ConflictAggregator: values =>
            {
                cancellation.Cancel();
                return values[0];
            }),
            cancellation.Token));

        Assert.Same(previous, store.Snapshot);
        Assert.False(store.IsPublishing);
        Assert.Equal(2, store.Count);
    }

    [Fact]
    public void MalformedPersistenceEnvelope_ShouldFailWithoutProducingStore()
    {
        var store = CreateStore($"safety.json-{Guid.NewGuid():N}");
        store.Add(new Row { Id = 1, Group = "a", Value = 1 });
        RecordStoreSchemaRegistry<Row, int>.Register(store);
        var json = store.ToJson();

        Assert.Throws<JsonException>(() => RecordStore<Row, int>.RestoreJson(
            json.Replace("\"formatVersion\":3", "\"formatVersion\":999", StringComparison.Ordinal)));
        Assert.Throws<JsonException>(() => RecordStore<Row, int>.RestoreJson(
            json.Replace("\"valueEncoding\":\"json\"", "\"valueEncoding\":\"raw\"", StringComparison.Ordinal)));
        Assert.Throws<JsonException>(() => RecordStore<Row, int>.RestoreJson(
            json.Replace("\"nextRecordId\":2", "\"nextRecordId\":1", StringComparison.Ordinal)));
        Assert.ThrowsAny<JsonException>(() => RecordStore<Row, int>.RestoreJson("{not-json}"));
    }

    private static RecordStore<Row, int> CreateStore(string schemaId)
    {
        var id = new RecordKeyDefinition<Row, int>("id", static value => value.Id);
        var group = new RecordKeyDefinition<Row, string?>("group", static value => value.Group);
        return new RecordStore<Row, int>(new RecordStoreDefinition<Row, int>(
            [id, group],
            new PrimaryKeyDefinition<Row, int>(["id"], static value => value.Id)))
        {
            SchemaId = schemaId
        };
    }

    private sealed class RowCodec : ITableValueCodec<Row>
    {
        public byte[] Encode(in Row value) => BitConverter.GetBytes(value.Id);
        public Row Decode(ReadOnlySpan<byte> data) => new() { Id = BitConverter.ToInt32(data) };
    }

    private sealed class ThrowingStringComparer : IEqualityComparer<string?>, IComparer<string?>
    {
        public bool Throw { get; set; }

        public bool Equals(string? left, string? right) => StringComparer.Ordinal.Equals(left, right);
        public int GetHashCode(string? value) => value is null ? 0 : StringComparer.Ordinal.GetHashCode(value);

        public int Compare(string? left, string? right)
        {
            if (Throw) throw new InvalidOperationException("comparison failure");
            return StringComparer.Ordinal.Compare(left, right);
        }
    }

    private record struct Row : IRecordStoreValue
    {
        public StoreRecordId StoreRecordId { get; set; }
        public int Id { get; init; }
        public string? Group { get; init; }
        public int Value { get; init; }
    }
}

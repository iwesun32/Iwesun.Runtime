using System.Text.Json;
using Xunit;

namespace Iwesun.Runtime.Data.Tests;

public sealed class RecordStoreSerializationTests
{
    [Fact]
    public void JsonRoundTrip_ShouldPreserveIdsOrderAndWritableState()
    {
        var definition = CreateDefinition();
        var store = new RecordStore<Row, PrimaryKey>(definition) { SchemaId = "serialization.roundtrip.v2" };
        RecordStoreSchemaRegistry<Row, PrimaryKey>.Register(store);
        var first = store.Add(new Row { Key = "a", Value = 1 });
        var second = store.Add(new Row { Key = "b", Value = 2 });

        var restored = RecordStore<Row, PrimaryKey>.RestoreJson(store.ToJson());

        Assert.Equal(RecordStoreOrigin.Restored, restored.Origin);
        Assert.False(restored.IsReadOnly);
        Assert.Equal([first, second], restored.Select(static value => value.StoreRecordId));
        Assert.Equal(new StoreRecordId(3), restored.Add(new Row { Key = "c", Value = 3 }));
        Assert.Null(restored.Snapshot);
    }

    [Fact]
    public void Restore_ShouldRejectUnknownSchemaAndMismatchedEmbeddedId()
    {
        var definition = CreateDefinition();
        var store = new RecordStore<Row, PrimaryKey>(definition) { SchemaId = "serialization.invalid.v2" };
        RecordStoreSchemaRegistry<Row, PrimaryKey>.Register(store);
        store.Add(new Row { Key = "a", Value = 1 });
        var json = store.ToJson();

        Assert.Throws<JsonException>(() => RecordStore<Row, PrimaryKey>.RestoreJson(
            json.Replace("serialization.invalid.v2", "serialization.missing.v2", StringComparison.Ordinal)));

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var malformed = json.Replace("\"recordId\":1", "\"recordId\":2", StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => RecordStore<Row, PrimaryKey>.RestoreJson(malformed));
        Assert.Equal(1, root.GetProperty("records")[0].GetProperty("recordId").GetInt64());
    }

    [Fact]
    public void RuntimeProfile_ShouldRoundTripPoliciesAndRefreshAfterRuntimeChanges()
    {
        var definition = CreateDefinition();
        RecordFilter<Row> filter = static value => value.Value > 0;
        LimitPredicate<Row> limit = static (value, _) => value.Value < 10;
        MergeResolver<Row> mergeResolver = static (left, right) => left with { Value = left.Value + right.Value };
        MergeAdd<Row> mergeAdd = static (Row _, out StoreRecordId id) =>
        {
            id = default;
            return false;
        };
        var schemaId = $"serialization.profile-{Guid.NewGuid():N}.v2";
        var store = new RecordStore<Row, PrimaryKey>(definition)
        {
            SchemaId = schemaId,
            RuntimeProfileVersion = 7,
            IndexThreshold = 5,
            KeyIndexMode = RecordStoreKeyIndexMode.SortedTable,
            AllowPrimaryKeyDuplicate = true,
            AllowUniqueConstraintViolation = true,
            AutoMerge = true,
            RecordFilter = filter,
            Limit = limit,
            MergeResolver = mergeResolver,
            MergeAdd = mergeAdd,
            DefaultPublishFormat = new RecordStorePublishFormat<Row>(ResultComparison: static (left, right) => left.Value.CompareTo(right.Value))
        };
        store.SetUniqueConstraints([
            new UniqueConstraintDefinition<Row, string?>("key-unique", ["key"], static value => value.Key)
        ]);
        store.Add(new Row { Key = "a", Value = 1 });
        RecordStoreSchemaRegistry<Row, PrimaryKey>.Register(store);

        var json = store.ToJson();
        var restored = RecordStore<Row, PrimaryKey>.RestoreJson(json);

        Assert.Equal(7, restored.RuntimeProfileVersion);
        Assert.Equal(5, restored.IndexThreshold);
        Assert.Equal(RecordStoreKeyIndexMode.SortedTable, restored.KeyIndexMode);
        Assert.True(restored.AllowPrimaryKeyDuplicate);
        Assert.True(restored.AllowUniqueConstraintViolation);
        Assert.True(restored.AutoMerge);
        Assert.Same(filter, restored.RecordFilter);
        Assert.Same(limit, restored.Limit);
        Assert.Same(mergeResolver, restored.MergeResolver);
        Assert.Same(mergeAdd, restored.MergeAdd);
        Assert.Single(restored.UniqueConstraints);
        Assert.Throws<JsonException>(() => RecordStore<Row, PrimaryKey>.RestoreJson(
            json.Replace("\"profileVersion\":7", "\"profileVersion\":8", StringComparison.Ordinal)));

        RecordFilter<Row> replacementFilter = static value => value.Value >= 1;
        store.RuntimeProfileVersion = 8;
        store.RecordFilter = replacementFilter;
        store.AutoMerge = false;
        RecordStoreSchemaRegistry<Row, PrimaryKey>.Register(store);

        var refreshed = RecordStore<Row, PrimaryKey>.RestoreJson(store.ToJson());
        Assert.Equal(8, refreshed.RuntimeProfileVersion);
        Assert.Same(replacementFilter, refreshed.RecordFilter);
        Assert.False(refreshed.AutoMerge);
    }

    private static RecordStoreDefinition<Row, PrimaryKey> CreateDefinition()
    {
        var key = new RecordKeyDefinition<Row, string?>("key", static value => value.Key);
        return new RecordStoreDefinition<Row, PrimaryKey>(
            [key],
            new PrimaryKeyDefinition<Row, PrimaryKey>(["key"], static value => new PrimaryKey(value.Key)));
    }

    private readonly record struct PrimaryKey(string? Key) : IComparable<PrimaryKey>
    {
        public int CompareTo(PrimaryKey other) => StringComparer.Ordinal.Compare(Key, other.Key);
    }

    private record struct Row : IRecordStoreValue
    {
        public StoreRecordId StoreRecordId { get; set; }
        public string? Key { get; init; }
        public int Value { get; init; }
    }
}

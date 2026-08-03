using System.Text;
using System.Text.Json;
using Xunit;

namespace Iwesun.Runtime.Data.Tests;

public sealed class RecordStoreRobustnessTests
{
    [Fact]
    public void Publish_ShouldCloseOverlappingPrimaryAndUniqueConflicts()
    {
        var store = CreateConflictStore("robustness.closure.v2");
        store.Add(new Row { Group = "a", Code = "1", Alias = "x", Value = 1 });
        store.Add(new Row { Group = "a", Code = "1", Alias = "y", Value = 2 });
        store.Add(new Row { Group = "b", Code = "2", Alias = "y", Value = 3 });
        var calls = 0;

        var published = store.Publish(new RecordStorePublishFormat<Row>(
            ConflictAggregator: values =>
            {
                calls++;
                Assert.Equal(3, values.Count);
                return values[0] with { Value = values.Sum(static value => value.Value) };
            }));

        Assert.Equal(1, calls);
        Assert.Equal(6, Assert.Single(published).Value);
    }

    [Fact]
    public void AggregateResultCreatingNewConflict_ShouldRepeatUntilStable()
    {
        var store = CreateConflictStore("robustness.atomic.v2");
        store.Add(new Row { Group = "base", Code = "0", Alias = "base", Value = 1 });
        var previous = store.Publish(RecordStorePublishTarget.Snapshot);
        var publicationVersion = store.PublicationVersion;

        store.Add(new Row { Group = "a", Code = "1", Alias = "x", Value = 2 });
        store.Add(new Row { Group = "a", Code = "1", Alias = "y", Value = 3 });
        store.Add(new Row { Group = "b", Code = "2", Alias = "base", Value = 4 });

        var snapshot = store.Publish(
            RecordStorePublishTarget.Snapshot,
            new RecordStorePublishFormat<Row>(
                ConflictAggregator: static values => values[0] with
                {
                    Alias = "base",
                    Value = values.Sum(static value => value.Value)
                }));

        Assert.NotSame(previous, snapshot);
        Assert.Equal(publicationVersion + 1, store.PublicationVersion);
        Assert.Equal(10, Assert.Single(snapshot).Value);
        Assert.False(store.IsPublishing);
    }

    [Fact]
    public void InvalidAggregateResult_ShouldFailWithoutReplacingSnapshot()
    {
        var store = CreateConflictStore("robustness.invalid-aggregate.v2");
        store.Add(new Row { Group = "base", Code = "0", Alias = "base", Value = 1 });
        var previous = store.Publish(RecordStorePublishTarget.Snapshot);
        var publicationVersion = store.PublicationVersion;
        store.Add(new Row { Group = "a", Code = "1", Alias = "x", Value = 2 });
        store.Add(new Row { Group = "a", Code = "1", Alias = "y", Value = 3 });

        Assert.Throws<InvalidOperationException>(() => store.Publish(
            RecordStorePublishTarget.Snapshot,
            new RecordStorePublishFormat<Row>(
                ConflictAggregator: static _ => default)));

        Assert.Same(previous, store.Snapshot);
        Assert.Equal(publicationVersion, store.PublicationVersion);
        Assert.False(store.IsPublishing);
    }

    [Fact]
    public void PublishCallbacks_ShouldObserveFrozenSourceAndEventsShouldObserveUnfrozenSource()
    {
        var store = CreateConflictStore("robustness.freeze.v2");
        store.Add(new Row { Group = "a", Code = "1", Alias = "x", Value = 1 });
        var sourceDataVersion = store.DataVersion;
        var callbackObservedPublishing = false;
        var eventObservedPublishing = true;
        store.SnapshotPublished += (_, args) =>
        {
            eventObservedPublishing = store.IsPublishing;
            Assert.Equal(sourceDataVersion, args.SourceDataVersion);
        };

        var snapshot = store.Publish(
            RecordStorePublishTarget.Snapshot,
            new RecordStorePublishFormat<Row>(SourceFilter: value =>
            {
                callbackObservedPublishing = store.IsPublishing;
                Assert.False(store.TryAdd(value with { Group = "blocked" }, out _));
                Assert.Equal(RecordUpdateResult.Busy, store.TryUpdate(value));
                Assert.False(store.TryDeprecate(value.StoreRecordId));
                Assert.Throws<InvalidOperationException>(() => store.AutoMerge = true);
                return true;
            }));

        Assert.True(callbackObservedPublishing);
        Assert.False(eventObservedPublishing);
        Assert.Equal(sourceDataVersion, store.DataVersion);
        Assert.Single(snapshot);
    }

    [Fact]
    public void SubscriberRegisteredAfterPublish_ShouldWaitForNextSnapshot()
    {
        var store = CreateConflictStore("robustness.subscriber.v2");
        store.Add(new Row { Group = "a", Code = "1", Alias = "x", Value = 1 });
        store.Publish(RecordStorePublishTarget.Snapshot);

        store.RegisterSnapshotSubscriber("late");
        Assert.Equal(SnapshotTakeState.NotSubscribed, store.GetSnapshotTakeState("late"));
        Assert.False(store.TryTakeSnapshot("late", out _));

        store.Publish(RecordStorePublishTarget.Snapshot);
        Assert.Equal(SnapshotTakeState.Pending, store.GetSnapshotTakeState("late"));
        Assert.True(store.TryTakeSnapshot("late", out _));
    }

    [Fact]
    public async Task AccessGateCancellation_ShouldNotLeakEnteredState()
    {
        var gate = new RecordStoreAccessGate();
        await using var held = await gate.EnterAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await gate.EnterAsync(cancellation.Token));
        Assert.True(gate.IsEntered);

        await held.DisposeAsync();
        Assert.False(gate.IsEntered);
        using var reacquired = gate.Enter();
        Assert.True(gate.IsEntered);
    }

    [Fact]
    public void CodecJsonRoundTrip_ShouldPreserveEnvelopeAndRejectDuplicateIds()
    {
        var store = CreateCodecStore("robustness.codec.v2");
        RecordStoreSchemaRegistry<Row, PrimaryKey>.Register(store);
        store.Add(new Row { Group = "a", Code = "1", Alias = "x", Value = 7 });
        store.Add(new Row { Group = "b", Code = "2", Alias = "y", Value = 8 });
        var json = store.ToJson();

        Assert.Contains("\"valueEncoding\":\"codec-base64\"", json, StringComparison.Ordinal);
        var restored = RecordStore<Row, PrimaryKey>.RestoreJson(json);
        Assert.Equal([7, 8], restored.Select(static value => value.Value));

        var duplicateId = json.Replace("\"recordId\":2", "\"recordId\":1", StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => RecordStore<Row, PrimaryKey>.RestoreJson(duplicateId));
    }

    private static RecordStore<Row, PrimaryKey> CreateConflictStore(string schemaId)
    {
        var group = new RecordKeyDefinition<Row, string?>("group", static value => value.Group);
        var code = new RecordKeyDefinition<Row, string?>("code", static value => value.Code);
        var alias = new RecordKeyDefinition<Row, string?>("alias", static value => value.Alias);
        var definition = new RecordStoreDefinition<Row, PrimaryKey>(
            [group, code, alias],
            new PrimaryKeyDefinition<Row, PrimaryKey>(
                ["group", "code"], static value => new PrimaryKey(value.Group, value.Code)));
        var store = new RecordStore<Row, PrimaryKey>(definition) { SchemaId = schemaId };
        store.ApplyOptions(new RecordStoreOptions(
            AllowPrimaryKeyDuplicate: true,
            AllowUniqueConstraintViolation: true));
        store.SetUniqueConstraints(
            [new UniqueConstraintDefinition<Row, string?>("alias-unique", ["alias"], static value => value.Alias)]);
        return store;
    }

    private static RecordStore<Row, PrimaryKey> CreateCodecStore(string schemaId)
    {
        var group = new RecordKeyDefinition<Row, string?>("group", static value => value.Group);
        var code = new RecordKeyDefinition<Row, string?>("code", static value => value.Code);
        var definition = new RecordStoreDefinition<Row, PrimaryKey>(
            [group, code],
            new PrimaryKeyDefinition<Row, PrimaryKey>(
                ["group", "code"], static value => new PrimaryKey(value.Group, value.Code)));
        return new RecordStore<Row, PrimaryKey>(definition)
        {
            SchemaId = schemaId,
            Codec = new RowCodec()
        };
    }

    private sealed class RowCodec : ITableValueCodec<Row>
    {
        public byte[] Encode(in Row value) => Encoding.UTF8.GetBytes(
            $"{value.StoreRecordId.Value}|{value.Group}|{value.Code}|{value.Alias}|{value.Value}");

        public Row Decode(ReadOnlySpan<byte> payload)
        {
            var parts = Encoding.UTF8.GetString(payload).Split('|');
            return new Row
            {
                StoreRecordId = new StoreRecordId(long.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture)),
                Group = parts[1],
                Code = parts[2],
                Alias = parts[3],
                Value = int.Parse(parts[4], System.Globalization.CultureInfo.InvariantCulture)
            };
        }
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
        public string? Alias { get; init; }
        public int Value { get; init; }
    }
}

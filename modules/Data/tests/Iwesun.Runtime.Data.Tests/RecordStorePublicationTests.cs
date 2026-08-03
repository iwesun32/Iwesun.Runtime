using Xunit;

namespace Iwesun.Runtime.Data.Tests;

public sealed class RecordStorePublicationTests
{
    [Fact]
    public void StandalonePublish_ShouldReturnWritableIndependentTable()
    {
        var store = CreateStore();
        store.Add(new Row { Group = "a", Code = "1", Value = 10 });

        var published = store.Publish();
        var publishedValue = Assert.Single(published);

        Assert.Equal(RecordStoreOrigin.Published, published.Origin);
        Assert.False(published.IsReadOnly);
        Assert.NotEqual(store.TableId, published.TableId);
        Assert.Equal(new StoreRecordId(1), publishedValue.StoreRecordId);
        published.Add(new Row { Group = "b", Code = "2", Value = 20 });
        Assert.Single(store);
        Assert.Equal(2, published.Count);
    }

    [Fact]
    public void ConflictPublish_ShouldRequireBusinessAggregateAndAssignNewId()
    {
        var store = CreateStore(new RecordStoreOptions(AllowPrimaryKeyDuplicate: true));
        store.Add(new Row { Group = "a", Code = "1", Value = 10 });
        store.Add(new Row { Group = "a", Code = "1", Value = 20 });

        Assert.Throws<InvalidOperationException>(() => store.Publish());

        var format = new RecordStorePublishFormat<Row>(
            ConflictAggregator: static values => values[0] with { Value = values.Sum(static value => value.Value) });
        var published = store.Publish(format);
        var value = Assert.Single(published);
        Assert.Equal(30, value.Value);
        Assert.Equal(new StoreRecordId(1), value.StoreRecordId);
        Assert.Equal(2, store.Count);
    }

    [Fact]
    public void ArrayAggregatePublish_ShouldCallBusinessDelegateOnce()
    {
        var store = CreateStore();
        var calls = 0;
        var published = store.Publish(
            new[]
            {
                new Row { Group = "a", Code = "1", Value = 2 },
                new Row { Group = "a", Code = "1", Value = 3 }
            },
            values =>
            {
                calls++;
                return values[0] with { Value = values.Sum(static value => value.Value) };
            });

        Assert.Equal(1, calls);
        Assert.Equal(5, Assert.Single(published).Value);
    }

    [Fact]
    public void SnapshotPublish_ShouldTrackSubscriberStateAndIsolateObserverFailures()
    {
        var store = CreateStore();
        store.RegisterSnapshotSubscriber("worker");
        store.Add(new Row { Group = "a", Code = "1", Value = 10 });
        var successfulObservers = 0;
        store.SnapshotPublished += static (_, _) => throw new InvalidOperationException("observer failure");
        store.SnapshotPublished += (_, _) => successfulObservers++;

        var snapshot = store.Publish(RecordStorePublishTarget.Snapshot);

        Assert.True(snapshot.IsReadOnly);
        Assert.Same(snapshot, store.Snapshot);
        Assert.Equal(SnapshotTakeState.Pending, store.GetSnapshotTakeState("worker"));
        Assert.Equal(1, successfulObservers);
        Assert.Equal(1, store.SnapshotObserverFailureCount);
        Assert.True(store.TryTakeSnapshot("worker", out var taken));
        Assert.Same(snapshot, taken);
        Assert.Equal(SnapshotTakeState.Taken, store.GetSnapshotTakeState("worker"));
        Assert.True(store.TryGetSnapshotTakenAt("worker", out _));
        Assert.False(store.TryTakeSnapshot("worker", out _));
    }

    [Fact]
    public void CancelledSnapshotPublish_ShouldKeepPreviousSnapshotAndVersion()
    {
        var store = CreateStore();
        store.Add(new Row { Group = "a", Code = "1", Value = 10 });
        var previous = store.Publish(RecordStorePublishTarget.Snapshot);
        var previousVersion = store.PublicationVersion;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            store.Publish(RecordStorePublishTarget.Snapshot, cancellationToken: cancellation.Token));
        Assert.Same(previous, store.Snapshot);
        Assert.Equal(previousVersion, store.PublicationVersion);
    }

    [Fact]
    public void Unregister_ShouldUpdateCurrentExpectedSetAndExposeCompletionState()
    {
        var store = CreateStore();
        store.RegisterSnapshotSubscriber("first");
        store.RegisterSnapshotSubscriber("second");
        store.Add(new Row { Group = "a", Code = "1", Value = 10 });
        store.Publish(RecordStorePublishTarget.Snapshot);

        Assert.Equal(2, store.PendingSnapshotSubscriberCount);
        Assert.False(store.AreAllExpectedSnapshotSubscribersTaken);
        Assert.True(store.TryTakeSnapshot("first", out _));
        Assert.Equal(1, store.PendingSnapshotSubscriberCount);
        Assert.True(store.UnregisterSnapshotSubscriber("second"));
        Assert.Equal(0, store.PendingSnapshotSubscriberCount);
        Assert.True(store.AreAllExpectedSnapshotSubscribersTaken);
        Assert.Equal(SnapshotTakeState.NotSubscribed, store.GetSnapshotTakeState("second"));
    }

    private static RecordStore<Row, PrimaryKey> CreateStore(RecordStoreOptions? options = null)
    {
        var group = new RecordKeyDefinition<Row, string?>("group", static value => value.Group);
        var code = new RecordKeyDefinition<Row, string?>("code", static value => value.Code);
        var definition = new RecordStoreDefinition<Row, PrimaryKey>(
            [group, code],
            new PrimaryKeyDefinition<Row, PrimaryKey>(
                ["group", "code"], static value => new PrimaryKey(value.Group, value.Code)));
        var store = new RecordStore<Row, PrimaryKey>(definition);
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

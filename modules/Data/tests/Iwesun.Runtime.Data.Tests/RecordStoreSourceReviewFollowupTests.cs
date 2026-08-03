using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Iwesun.Runtime.Data.Tests;

public sealed class RecordStoreSourceReviewFollowupTests
{
    [Fact]
    public void UpdateAndMerge_ShouldPreserveStableRegistrationOrderInsideBuckets()
    {
        var fixture = CreateFixture(indexThreshold: 1);
        fixture.Store.AllowPrimaryKeyDuplicate = true;
        var first = fixture.Store.Add(new Row { Id = 1, Group = "same", Value = 1 });
        var second = fixture.Store.Add(new Row { Id = 1, Group = "same", Value = 2 });
        var third = fixture.Store.Add(new Row { Id = 1, Group = "same", Value = 3 });

        Assert.True(fixture.Store.TryGetRecord(first, out var firstValue));
        Assert.Equal(RecordUpdateResult.Updated, fixture.Store.TryUpdate(firstValue with { Value = 10 }));
        Assert.Equal([first, second, third],
            fixture.Store.GetValues(fixture.GroupKey, "same").Select(static value => value.StoreRecordId));
        Assert.Equal([first, second, third],
            fixture.Store.GetValues(1).Select(static value => value.StoreRecordId));

        var movingFixture = CreateFixture(indexThreshold: 1);
        var b1 = movingFixture.Store.Add(new Row { Id = 1, Group = "b", Value = 1 });
        var a2 = movingFixture.Store.Add(new Row { Id = 2, Group = "a", Value = 2 });
        var b3 = movingFixture.Store.Add(new Row { Id = 3, Group = "b", Value = 3 });
        Assert.True(movingFixture.Store.TryGetRecord(a2, out var moving));
        Assert.Equal(RecordUpdateResult.Updated, movingFixture.Store.TryUpdate(moving with { Group = "b" }));
        Assert.Equal([b1, a2, b3],
            movingFixture.Store.GetValues(movingFixture.GroupKey, "b").Select(static value => value.StoreRecordId));
    }

    [Fact]
    public void ComparerExceptions_ShouldLeaveAllObservableStateUnchanged()
    {
        var throwingComparer = new ThrowingStringComparer();
        var fixture = CreateFixture(indexThreshold: 1, groupComparer: throwingComparer);
        var id = fixture.Store.Add(new Row { Id = 1, Group = "old", Value = 1 });
        _ = fixture.Store.GetKeyCount(fixture.GroupKey);
        var version = fixture.Store.DataVersion;
        Assert.True(fixture.Store.TryGetRecord(id, out var current));

        throwingComparer.ThrowOnNextHash();
        Assert.Throws<InvalidOperationException>(() => fixture.Store.TryUpdate(current with { Group = "new" }));
        Assert.Equal(version, fixture.Store.DataVersion);
        Assert.Single(fixture.Store);
        Assert.Equal(id, Assert.Single(fixture.Store.GetValues(fixture.GroupKey, "old")).StoreRecordId);
        Assert.Empty(fixture.Store.GetValues(fixture.GroupKey, "new"));

        throwingComparer.ThrowOnNextHash();
        Assert.Throws<InvalidOperationException>(() => fixture.Store.TryDeprecate(id));
        Assert.Equal(version, fixture.Store.DataVersion);
        Assert.True(fixture.Store.TryGetRecord(id, out _));

        throwingComparer.ThrowOnNextHash();
        Assert.Throws<InvalidOperationException>(() =>
            fixture.Store.Add(new Row { Id = 2, Group = "add", Value = 2 }));
        Assert.Equal(version, fixture.Store.DataVersion);
        Assert.Single(fixture.Store);
        Assert.Equal(new StoreRecordId(2), fixture.Store.Add(new Row { Id = 2, Group = "add", Value = 2 }));
    }

    [Fact]
    public void PreparedBucketRegistration_ShouldNotReinvokeTheUserComparer()
    {
        var throwingComparer = new ThrowingStringComparer();
        var fixture = CreateFixture(indexThreshold: 1, groupComparer: throwingComparer);
        var first = fixture.Store.Add(new Row { Id = 1, Group = "old", Value = 1 });
        _ = fixture.Store.GetKeyCount(fixture.GroupKey);
        var version = fixture.Store.DataVersion;

        throwingComparer.ThrowOnHashCall(2);
        Assert.Equal(new StoreRecordId(2),
            fixture.Store.Add(new Row { Id = 2, Group = "new", Value = 2 }));
        throwingComparer.StopThrowing();
        Assert.Equal(version + 1, fixture.Store.DataVersion);
        Assert.Equal(2, fixture.Store.Count);

        Assert.True(fixture.Store.TryGetRecord(first, out var current));
        throwingComparer.ThrowOnHashCall(3);
        Assert.Equal(RecordUpdateResult.Updated, fixture.Store.TryUpdate(current with { Group = "moved" }));
        throwingComparer.StopThrowing();
        Assert.Empty(fixture.Store.GetValues(fixture.GroupKey, "old"));
        Assert.Equal(first, Assert.Single(fixture.Store.GetValues(fixture.GroupKey, "moved")).StoreRecordId);
    }

    [Fact]
    public void RepeatedComparerCaptureFailures_ShouldReusePreparedArenaSlots()
    {
        var throwingComparer = new ThrowingStringComparer();
        var fixture = CreateFixture(indexThreshold: 1, groupComparer: throwingComparer);
        fixture.Store.Add(new Row { Id = 1, Group = "existing", Value = 1 });
        _ = fixture.Store.GetKeyCount(fixture.GroupKey);
        var reservedBuckets = fixture.Store.ReservedIndexBucketCount;
        var version = fixture.Store.DataVersion;

        for (var index = 0; index < 1_000; index++)
        {
            throwingComparer.ThrowOnNextHash();
            Assert.Throws<InvalidOperationException>(() =>
                fixture.Store.Add(new Row { Id = index + 2, Group = $"failed-{index}", Value = index }));
        }

        Assert.Equal(reservedBuckets, fixture.Store.ReservedIndexBucketCount);
        Assert.Equal(version, fixture.Store.DataVersion);
        Assert.Single(fixture.Store);
        throwingComparer.StopThrowing();
        Assert.Equal(new StoreRecordId(2),
            fixture.Store.Add(new Row { Id = 2, Group = "successful", Value = 2 }));
    }

    [Fact]
    public void SuccessfulKeyMigration_ShouldKeepIndexBucketCapacityBounded()
    {
        var fixture = CreateFixture(indexThreshold: 1);
        var id = fixture.Store.Add(new Row { Id = 1, Group = "group-0", Value = 0 });
        _ = fixture.Store.GetKeyCount(fixture.GroupKey);
        var reservedBuckets = fixture.Store.ReservedIndexBucketCount;

        for (var index = 1; index <= 100_000; index++)
        {
            Assert.True(fixture.Store.TryGetRecord(id, out var current));
            Assert.Equal(
                RecordUpdateResult.Updated,
                fixture.Store.TryUpdate(current with { Group = $"group-{index}", Value = index }));
        }

        Assert.Single(fixture.Store);
        Assert.Equal(1, fixture.Store.PhysicalCount);
        Assert.Equal(0, fixture.Store.DeprecatedCount);
        Assert.Empty(fixture.Store.GetValues(fixture.GroupKey, "group-0"));
        Assert.Equal(id, Assert.Single(fixture.Store.GetValues(fixture.GroupKey, "group-100000")).StoreRecordId);
        Assert.Equal(reservedBuckets, fixture.Store.ReservedIndexBucketCount);
    }

    [Fact]
    public void SuccessfulDeprecation_ShouldReleasePrimaryKeyAndConstraintBuckets()
    {
        const int records = 10_000;
        var fixture = CreateFixture(indexThreshold: 1);
        fixture.Store.SetUniqueConstraints([
            new UniqueConstraintDefinition<Row, string?>(
                "group-unique", ["group"], static value => value.Group)
        ]);
        var ids = new StoreRecordId[records];
        for (var index = 0; index < records; index++)
            ids[index] = fixture.Store.Add(new Row { Id = index, Group = $"group-{index}", Value = index });
        _ = fixture.Store.GetKeyCount(fixture.GroupKey);
        Assert.True(fixture.Store.ReservedIndexBucketCount >= records * 3);

        foreach (var id in ids) Assert.True(fixture.Store.TryDeprecate(id));

        Assert.Empty(fixture.Store);
        Assert.Equal(records, fixture.Store.PhysicalCount);
        Assert.Equal(records, fixture.Store.DeprecatedCount);
        Assert.Equal(0, fixture.Store.ReservedIndexBucketCount);
        Assert.Equal(new StoreRecordId(records + 1L),
            fixture.Store.Add(new Row { Id = records + 1, Group = "reused", Value = 1 }));
        Assert.Equal(3, fixture.Store.ReservedIndexBucketCount);
    }

    [Fact]
    public void MixedMutationCleanup_ShouldRemainBoundedAcrossAllIndexKinds()
    {
        var throwingComparer = new ThrowingStringComparer();
        var idKey = new RecordKeyDefinition<Row, int>("id", static value => value.Id);
        var groupKey = new RecordKeyDefinition<Row, string?>(
            "group", static value => value.Group, throwingComparer);
        var valueKey = new RecordKeyDefinition<Row, int>("value", static value => value.Value);
        var store = new RecordStore<Row, int>(new RecordStoreDefinition<Row, int>(
            [idKey, groupKey, valueKey],
            new PrimaryKeyDefinition<Row, int>(["id"], static value => value.Id)))
        {
            IndexThreshold = 1,
            AllowPrimaryKeyDuplicate = true
        };
        store.SetUniqueConstraints([
            new UniqueConstraintDefinition<Row, string?>(
                "group-unique", ["group"], static value => value.Group, throwingComparer)
        ]);
        var movingId = store.Add(new Row { Id = 1, Group = "moving-0", Value = 10 });
        store.Add(new Row { Id = 1, Group = "stable", Value = 20 });
        _ = store.GetKeyCount(groupKey);
        _ = store.GetKeyCount(valueKey);
        var reservedBuckets = store.ReservedIndexBucketCount;

        for (var index = 1; index <= 10_000; index++)
        {
            Assert.True(store.TryGetRecord(movingId, out var current));
            Assert.Equal(
                RecordUpdateResult.Updated,
                store.TryUpdate(current with { Group = $"moving-{index}" }));

            var temporaryId = store.Add(new Row
            {
                Id = index + 1,
                Group = $"temporary-{index}",
                Value = index + 100
            });
            Assert.True(store.TryDeprecate(temporaryId));
        }

        Assert.Equal(2, store.Count);
        Assert.Equal(10_002, store.PhysicalCount);
        Assert.Equal(10_000, store.DeprecatedCount);
        Assert.Equal(reservedBuckets, store.ReservedIndexBucketCount);
        Assert.Empty(store.GetValues(groupKey, "moving-0"));
        Assert.Equal(movingId, Assert.Single(store.GetValues(groupKey, "moving-10000")).StoreRecordId);
    }

    [Fact]
    public void ApplyOptions_ShouldRejectAtomicallyWhenAnyTargetPolicyConflicts()
    {
        var fixture = CreateFixture();
        fixture.Store.ApplyOptions(new RecordStoreOptions(
            AllowPrimaryKeyDuplicate: true,
            AllowUniqueConstraintViolation: true));
        fixture.Store.SetUniqueConstraints([
            new UniqueConstraintDefinition<Row, string?>(
                "group-unique", ["group"], static value => value.Group)
        ]);
        fixture.Store.Add(new Row { Id = 1, Group = "same", Value = 1 });
        fixture.Store.Add(new Row { Id = 1, Group = "same", Value = 2 });

        Assert.Throws<InvalidOperationException>(() => fixture.Store.ApplyOptions(new RecordStoreOptions(
            AllowPrimaryKeyDuplicate: true,
            AutoMerge: true,
            AllowUniqueConstraintViolation: false)));
        Assert.True(fixture.Store.AllowPrimaryKeyDuplicate);
        Assert.False(fixture.Store.AutoMerge);
        Assert.True(fixture.Store.AllowUniqueConstraintViolation);

        Assert.Throws<InvalidOperationException>(() => fixture.Store.ApplyOptions(new RecordStoreOptions(
            AllowPrimaryKeyDuplicate: false,
            AutoMerge: true,
            AllowUniqueConstraintViolation: true)));
        Assert.True(fixture.Store.AllowPrimaryKeyDuplicate);
        Assert.False(fixture.Store.AutoMerge);
        Assert.True(fixture.Store.AllowUniqueConstraintViolation);
    }

    [Fact]
    public void Restore_ShouldAlwaysRejectLimitViolationsEvenWhenUniqueViolationsAreAllowed()
    {
        var fixture = CreateFixture();
        fixture.Store.SchemaId = $"followup.limit-{Guid.NewGuid():N}";
        fixture.Store.AllowUniqueConstraintViolation = true;
        fixture.Store.Limit = static (_, current) => current.Count < 1;
        fixture.Store.Add(new Row { Id = 1, Group = "a", Value = 1 });
        RecordStoreSchemaRegistry<Row, int>.Register(fixture.Store);

        var root = JsonNode.Parse(fixture.Store.ToJson())!.AsObject();
        var records = root["records"]!.AsArray();
        var duplicate = records[0]!.DeepClone().AsObject();
        duplicate["recordId"] = 2;
        duplicate["value"]!["StoreRecordId"]!["Value"] = 2;
        duplicate["value"]!["Id"] = 2;
        records.Add(duplicate);
        root["nextRecordId"] = 3;

        Assert.Throws<JsonException>(() =>
            RecordStore<Row, int>.RestoreJson(root.ToJsonString()));
    }

    [Fact]
    public void Tombstones_ShouldExposePhysicalStateAndCompactionShouldReturnDenseWritableStore()
    {
        var fixture = CreateFixture();
        var ids = Enumerable.Range(1, 100)
            .Select(value => fixture.Store.Add(new Row { Id = value, Group = $"g-{value}", Value = value }))
            .ToArray();
        foreach (var id in ids.Where((_, index) => index % 2 == 0))
            Assert.True(fixture.Store.TryDeprecate(id));

        Assert.Equal(50, fixture.Store.Count);
        Assert.Equal(100, fixture.Store.PhysicalCount);
        Assert.Equal(50, fixture.Store.DeprecatedCount);
        Assert.Equal(0.5d, fixture.Store.TombstoneRatio);

        var compacted = fixture.Store.CreateCompactedStore();
        Assert.Equal(50, compacted.Count);
        Assert.Equal(50, compacted.PhysicalCount);
        Assert.Equal(0, compacted.DeprecatedCount);
        Assert.Equal(0d, compacted.TombstoneRatio);
        Assert.Equal(fixture.Store.Select(static value => value.StoreRecordId),
            compacted.Select(static value => value.StoreRecordId));
        Assert.Null(compacted.Snapshot);
        Assert.Equal(new StoreRecordId(101), compacted.Add(new Row { Id = 101, Group = "new", Value = 101 }));

        Assert.True(fixture.Store.TryDeprecate(ids[^1]));
        var withoutHighestId = fixture.Store.CreateCompactedStore();
        Assert.Equal(new StoreRecordId(101),
            withoutHighestId.Add(new Row { Id = 102, Group = "newer", Value = 102 }));
    }

    [Fact]
    public void ResultViewComparison_ShouldBeIsolatedAndSortingShouldObserveCancellation()
    {
        var fixture = CreateFixture();
        fixture.Store.AddRange(Enumerable.Range(1, 1_000)
            .Select(value => new Row { Id = value, Group = "g", Value = value }));
        var cloneStrategy = new MutableResultCloneStrategy();
        var result = fixture.Store.CreateView(new RecordStoreResultViewDefinition<Row, MutableResult>(
            Projector: static value => new MutableResult([value.Value]),
            ResultComparison: static (left, right) =>
            {
                left.Values[0] = int.MaxValue;
                right.Values[0] = int.MinValue;
                return 0;
            },
            ResultCloneStrategy: cloneStrategy));
        Assert.Equal(1, result[0].Values[0]);
        Assert.Equal(1_000, result[^1].Values[0]);

        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        Assert.Throws<OperationCanceledException>(() => fixture.Store.CreateView(
            new RecordStoreResultViewDefinition<Row, int>(
                Projector: static value => value.Value,
                ResultComparison: (left, right) =>
                {
                    if (++calls == 1) cancellation.Cancel();
                    return right.CompareTo(left);
                }),
            cancellation.Token));
    }

    private static Fixture CreateFixture(
        int indexThreshold = 128,
        IEqualityComparer<string?>? groupComparer = null)
    {
        var id = new RecordKeyDefinition<Row, int>("id", static value => value.Id);
        var group = new RecordKeyDefinition<Row, string?>(
            "group", static value => value.Group, groupComparer);
        var store = new RecordStore<Row, int>(new RecordStoreDefinition<Row, int>(
            [id, group],
            new PrimaryKeyDefinition<Row, int>(["id"], static value => value.Id)))
        {
            IndexThreshold = indexThreshold
        };
        return new Fixture(store, group);
    }

    private sealed record Fixture(
        RecordStore<Row, int> Store,
        RecordKeyDefinition<Row, string?> GroupKey);

    private sealed class ThrowingStringComparer : IEqualityComparer<string?>
    {
        private int _hashCallsUntilThrow;
        public void ThrowOnNextHash() => ThrowOnHashCall(1);
        public void ThrowOnHashCall(int call)
        {
            if (call < 1) throw new ArgumentOutOfRangeException(nameof(call));
            _hashCallsUntilThrow = call;
        }
        public void StopThrowing()
        {
            _hashCallsUntilThrow = 0;
        }
        public bool Equals(string? left, string? right) => StringComparer.Ordinal.Equals(left, right);
        public int GetHashCode(string? value)
        {
            if (_hashCallsUntilThrow > 0 && --_hashCallsUntilThrow == 0)
            {
                throw new InvalidOperationException("injected comparer failure");
            }
            return value is null ? 0 : StringComparer.Ordinal.GetHashCode(value);
        }
    }

    private readonly record struct MutableResult(int[] Values);

    private sealed class MutableResultCloneStrategy : IDeepCloneStrategy<MutableResult>
    {
        public MutableResult Clone(in MutableResult value) => new((int[])value.Values.Clone());
    }

    private record struct Row : IRecordStoreValue
    {
        public StoreRecordId StoreRecordId { get; set; }
        public int Id { get; init; }
        public string? Group { get; init; }
        public int Value { get; init; }
    }
}

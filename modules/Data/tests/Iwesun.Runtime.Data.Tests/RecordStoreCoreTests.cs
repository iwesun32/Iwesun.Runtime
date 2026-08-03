using Xunit;

namespace Iwesun.Runtime.Data.Tests;

public sealed class RecordStoreCoreTests
{
    [Fact]
    public void Add_ShouldFormatAssignIdAndSupportEightQueries()
    {
        var fixture = CreateFixture();
        var store = fixture.Store;

        var firstId = store.Add(new TestRecord { Machine = " host ", Ip = "10.0.0.1", Mac = "aa" });
        var secondId = store.Add(new TestRecord { Machine = "host", Ip = "10.0.0.2", Mac = "bb" });

        Assert.Equal(new StoreRecordId(1), firstId);
        Assert.Equal(new StoreRecordId(2), secondId);
        Assert.Equal("host", Assert.Single(store.GetKeys(fixture.MachineKey)));
        Assert.Equal(1, store.GetKeyCount(fixture.MachineKey));
        Assert.Equal(2, store.GetValueCount(fixture.MachineKey, "host"));
        Assert.Equal(2, store.GetValues(fixture.MachineKey, "host").Length);
        Assert.Equal(2, store.GetPrimaryKeyCount());
        Assert.Equal(1, store.GetValueCount(new TestPrimaryKey("host", "10.0.0.1")));
        Assert.Single(store.GetValues(new TestPrimaryKey("host", "10.0.0.2")));
        Assert.Equal(2, store.GetPrimaryKeys().Length);
    }

    [Fact]
    public void Add_ShouldRejectAllNullAndDuplicatePrimaryKeys()
    {
        var store = CreateFixture().Store;

        Assert.False(store.TryAdd(default, out _));
        Assert.True(store.TryAdd(new TestRecord { Machine = "host", Ip = "10.0.0.1", Mac = "aa" }, out _));
        Assert.False(store.TryAdd(new TestRecord { Machine = "host", Ip = "10.0.0.1", Mac = "bb" }, out _));
        Assert.Single(store);
    }

    [Fact]
    public void AutoMerge_ShouldRetainExistingIdAndAllowPrimaryKeyCompletion()
    {
        var fixture = CreateFixture(
            options: new RecordStoreOptions(AutoMerge: true),
            merge: static (existing, incoming) => existing with
            {
                Ip = existing.Ip ?? incoming.Ip,
                Mac = incoming.Mac ?? existing.Mac
            },
            configureMergeAdd: true,
            primaryKey: static value => new TestPrimaryKey(value.Machine, null));

        var first = fixture.Store.Add(new TestRecord { Machine = "host", Mac = "aa" });
        var merged = fixture.Store.Add(new TestRecord { Machine = "host", Ip = "10.0.0.1", Mac = "bb" });

        Assert.Equal(first, merged);
        Assert.Single(fixture.Store);
        Assert.True(fixture.Store.TryGetRecord(first, out var value));
        Assert.Equal("10.0.0.1", value.Ip);
        Assert.Equal("bb", value.Mac);
    }

    [Fact]
    public void UpdateAndDelete_ShouldUseEmbeddedRecordId()
    {
        var fixture = CreateFixture();
        var id = fixture.Store.Add(new TestRecord { Machine = "host", Ip = "10.0.0.1", Mac = "aa" });
        Assert.True(fixture.Store.TryGetRecord(id, out var value));

        value = value with { Mac = "bb" };
        Assert.Equal(RecordUpdateResult.Updated, fixture.Store.TryUpdate(value));
        Assert.Equal("bb", fixture.Store.GetValues(new TestPrimaryKey("host", "10.0.0.1"))[0].Mac);

        value = value with { Ip = "10.0.0.2" };
        Assert.Equal(RecordUpdateResult.PrimaryKeyChanged, fixture.Store.TryUpdate(value));
        Assert.True(fixture.Store.TryDeprecate(id));
        Assert.False(fixture.Store.TryGetRecord(id, out _));
        Assert.Empty(fixture.Store);
    }

    [Fact]
    public void UniqueConstraint_ShouldRejectConflictingValue()
    {
        var machineKey = new RecordKeyDefinition<TestRecord, string?>("machine", static value => value.Machine);
        var ipKey = new RecordKeyDefinition<TestRecord, string?>("ip", static value => value.Ip);
        var macKey = new RecordKeyDefinition<TestRecord, string?>("mac", static value => value.Mac);
        var definition = new RecordStoreDefinition<TestRecord, TestPrimaryKey>(
            [machineKey, ipKey, macKey],
            new PrimaryKeyDefinition<TestRecord, TestPrimaryKey>(
                ["machine", "ip"], static value => new TestPrimaryKey(value.Machine, value.Ip)));
        var store = new RecordStore<TestRecord, TestPrimaryKey>(definition);
        store.SetUniqueConstraints(
            [new UniqueConstraintDefinition<TestRecord, string?>("mac-unique", ["mac"], static value => value.Mac)]);

        Assert.True(store.TryAdd(new TestRecord { Machine = "a", Ip = "1", Mac = "same" }, out _));
        Assert.False(store.TryAdd(new TestRecord { Machine = "b", Ip = "2", Mac = "same" }, out _));
    }

    [Fact]
    public void RelaxedConstraints_ShouldBeReportedInStableOrder()
    {
        var machineKey = new RecordKeyDefinition<TestRecord, string?>("machine", static value => value.Machine);
        var ipKey = new RecordKeyDefinition<TestRecord, string?>("ip", static value => value.Ip);
        var macKey = new RecordKeyDefinition<TestRecord, string?>("mac", static value => value.Mac);
        var definition = new RecordStoreDefinition<TestRecord, TestPrimaryKey>(
            [machineKey, ipKey, macKey],
            new PrimaryKeyDefinition<TestRecord, TestPrimaryKey>(
                ["machine", "ip"], static value => new TestPrimaryKey(value.Machine, value.Ip)));
        var store = new RecordStore<TestRecord, TestPrimaryKey>(definition);
        store.ApplyOptions(new RecordStoreOptions(
            AllowPrimaryKeyDuplicate: true,
            AllowUniqueConstraintViolation: true));
        store.SetUniqueConstraints(
            [new UniqueConstraintDefinition<TestRecord, string?>("mac-unique", ["mac"], static value => value.Mac)]);

        store.Add(new TestRecord { Machine = "a", Ip = "1", Mac = "same" });
        store.Add(new TestRecord { Machine = "a", Ip = "1", Mac = "same" });

        var conflicts = store.ValidateConstraints();
        Assert.Equal(2, conflicts.Count);
        Assert.Equal("$primary", conflicts[0].ConstraintName);
        Assert.Equal("mac-unique", conflicts[1].ConstraintName);
        Assert.Equal([new StoreRecordId(1), new StoreRecordId(2)], conflicts[0].RecordIds);
    }

    [Fact]
    public void ActiveKeyIndex_ShouldTrackUpdateAndDelete()
    {
        var fixture = CreateFixture();
        var id = fixture.Store.Add(new TestRecord { Machine = "a", Ip = "1", Mac = "x" });
        Assert.Single(fixture.Store.GetValues(fixture.MacKey, "x"));
        Assert.True(fixture.Store.TryGetRecord(id, out var value));

        value = value with { Mac = "y" };
        Assert.Equal(RecordUpdateResult.Updated, fixture.Store.TryUpdate(value));
        Assert.Empty(fixture.Store.GetValues(fixture.MacKey, "x"));
        Assert.Single(fixture.Store.GetValues(fixture.MacKey, "y"));
        Assert.True(fixture.Store.TryDeprecate(id));
        Assert.Empty(fixture.Store.GetValues(fixture.MacKey, "y"));
        Assert.Equal(0, fixture.Store.GetKeyCount(fixture.MacKey));
    }

    [Fact]
    public void AddRange_ShouldFormatEachCandidateExactlyOnce()
    {
        var machineKey = new RecordKeyDefinition<TestRecord, string?>("machine", static value => value.Machine);
        var ipKey = new RecordKeyDefinition<TestRecord, string?>("ip", static value => value.Ip);
        var calls = 0;
        var definition = new RecordStoreDefinition<TestRecord, TestPrimaryKey>(
            [machineKey, ipKey],
            new PrimaryKeyDefinition<TestRecord, TestPrimaryKey>(
                ["machine", "ip"], static value => new TestPrimaryKey(value.Machine, value.Ip)));
        var store = new RecordStore<TestRecord, TestPrimaryKey>(definition)
        {
            ValueFormatter = value =>
            {
                calls++;
                return value with { Machine = value.Machine?.Trim() };
            }
        };

        var accepted = store.AddRange([
            new TestRecord { Machine = " a ", Ip = "1" },
            new TestRecord { Machine = " b ", Ip = "2" }
        ]);

        Assert.Equal(2, accepted);
        Assert.Equal(2, calls);
        Assert.Equal(new string?[] { "a", "b" }, store.GetKeys(machineKey));
        _ = store.DeepClone();
        _ = store.Publish();
        Assert.Equal(2, calls);
    }

    [Fact]
    public void PartialNullPrimaryKey_ShouldTreatNullAsAnOrdinaryComparableValue()
    {
        var fixture = CreateFixture();

        Assert.True(fixture.Store.TryAdd(new TestRecord { Machine = "host", Ip = null }, out _));
        Assert.False(fixture.Store.TryAdd(new TestRecord { Machine = "host", Ip = null }, out _));
        Assert.Equal(1, fixture.Store.GetValueCount(new TestPrimaryKey("host", null)));
        Assert.Equal(new string?[] { "host" }, fixture.Store.GetKeys(fixture.MachineKey));
    }

    private static Fixture CreateFixture(
        RecordStoreOptions? options = null,
        MergeResolver<TestRecord>? merge = null,
        bool configureMergeAdd = false,
        PrimaryKeySelector<TestRecord, TestPrimaryKey>? primaryKey = null)
    {
        var machineKey = new RecordKeyDefinition<TestRecord, string?>("machine", static value => value.Machine);
        var ipKey = new RecordKeyDefinition<TestRecord, string?>("ip", static value => value.Ip);
        var macKey = new RecordKeyDefinition<TestRecord, string?>("mac", static value => value.Mac);
        var definition = new RecordStoreDefinition<TestRecord, TestPrimaryKey>(
            [machineKey, ipKey, macKey],
            new PrimaryKeyDefinition<TestRecord, TestPrimaryKey>(
                ["machine", "ip"], primaryKey ?? (static value => new TestPrimaryKey(value.Machine, value.Ip))));
        var store = new MergeTestStore(definition)
        {
            ValueFormatter = static value => value with { Machine = value.Machine?.Trim() },
            MergeResolver = merge
        };
        if (configureMergeAdd)
            store.MergeAdd = store.TryMergeIncoming;
        if (options is not null) store.ApplyOptions(options);
        return new Fixture(store, machineKey, macKey);
    }

    private sealed class MergeTestStore(RecordStoreDefinition<TestRecord, TestPrimaryKey> definition)
        : RecordStore<TestRecord, TestPrimaryKey>(definition)
    {
        public bool TryMergeIncoming(TestRecord incoming, out StoreRecordId recordId)
        {
            recordId = default;
            if (MergeResolver is null) return false;

            var primaryKey = Definition.PrimaryKey.GetPrimaryKey(incoming);
            foreach (var existing in GetValues(primaryKey))
            {
                var merged = MergeResolver.Resolve(existing, incoming);
                if (TryCommitMergedRecord(existing.StoreRecordId, merged, out recordId))
                    return true;
            }
            return false;
        }
    }

    private sealed record Fixture(
        RecordStore<TestRecord, TestPrimaryKey> Store,
        RecordKeyDefinition<TestRecord, string?> MachineKey,
        RecordKeyDefinition<TestRecord, string?> MacKey);

    private readonly record struct TestPrimaryKey(string? Machine, string? Ip) : IComparable<TestPrimaryKey>
    {
        public int CompareTo(TestPrimaryKey other)
        {
            var machine = StringComparer.Ordinal.Compare(Machine, other.Machine);
            return machine != 0 ? machine : StringComparer.Ordinal.Compare(Ip, other.Ip);
        }
    }

    private record struct TestRecord : IRecordStoreValue
    {
        public StoreRecordId StoreRecordId { get; set; }
        public string? Machine { get; init; }
        public string? Ip { get; init; }
        public string? Mac { get; init; }
    }
}

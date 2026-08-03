using Xunit;

namespace Iwesun.Runtime.Data.Tests;

public sealed class RecordStoreDefinitionTests
{
    [Fact]
    public void Definition_ShouldFreezeKeysWhileStoreOwnsMutableFormatter()
    {
        var keys = new List<IRecordKeyDefinition<TestRecord>>
        {
            new RecordKeyDefinition<TestRecord, string?>("machine", static value => value.Machine),
            new RecordKeyDefinition<TestRecord, string?>("ip", static value => value.Ip)
        };

        var definition = new RecordStoreDefinition<TestRecord, TestPrimaryKey>(
            keys,
            new PrimaryKeyDefinition<TestRecord, TestPrimaryKey>(
                ["machine", "ip"],
                static value => new TestPrimaryKey(value.Machine, value.Ip)));
        var store = new RecordStore<TestRecord, TestPrimaryKey>(definition)
        {
            ValueFormatter = static value => value with { Machine = value.Machine?.Trim() }
        };

        keys.Clear();

        Assert.Equal(2, definition.Keys.Count);
        Assert.NotNull(store.ValueFormatter);
        Assert.Equal("host", store.ValueFormatter!(new TestRecord { Machine = " host " }).Machine);
        Assert.True(definition.TryGetKey("ip", out _));
    }

    [Fact]
    public void DefinitionConstructor_ShouldOnlyAcceptKeysAndPrimaryKey()
    {
        var constructor = Assert.Single(typeof(RecordStoreDefinition<TestRecord, TestPrimaryKey>).GetConstructors());
        var parameters = constructor.GetParameters();

        Assert.Equal(2, parameters.Length);
        Assert.Equal("keys", parameters[0].Name);
        Assert.Equal("primaryKey", parameters[1].Name);
    }

    [Fact]
    public void StoreConfiguration_ShouldRemainRuntimeMutable()
    {
        var definition = new RecordStoreDefinition<TestRecord, TestPrimaryKey>(
            [
                new RecordKeyDefinition<TestRecord, string?>("machine", static value => value.Machine),
                new RecordKeyDefinition<TestRecord, string?>("ip", static value => value.Ip)
            ],
            new PrimaryKeyDefinition<TestRecord, TestPrimaryKey>(
                ["machine", "ip"], static value => new TestPrimaryKey(value.Machine, value.Ip)));
        var store = new RecordStore<TestRecord, TestPrimaryKey>(definition);
        var cloneStrategy = new TestCloneStrategy();
        var codec = new TestCodec();

        Assert.Equal(1024, store.IndexThreshold);
        Assert.Equal(RecordStoreKeyIndexMode.Automatic, store.KeyIndexMode);
        Assert.Equal(2, store.RuntimeProfileVersion);

        store.SchemaId = "mutable.schema";
        store.CloneStrategy = cloneStrategy;
        store.Codec = codec;
        store.IndexThreshold = 32;
        store.RecordFilter = static value => value.Machine is not null;
        store.Limit = static (_, current) => current.Count < 10;
        store.AutoMerge = true;

        Assert.Equal("mutable.schema", store.SchemaId);
        Assert.Same(cloneStrategy, store.CloneStrategy);
        Assert.Same(codec, store.Codec);
        Assert.Equal(32, store.IndexThreshold);
        Assert.NotNull(store.RecordFilter);
        Assert.NotNull(store.Limit);
        Assert.True(store.AutoMerge);
        Assert.Throws<ArgumentOutOfRangeException>(() => store.IndexThreshold = 0);
    }

    [Fact]
    public void Definition_ShouldRejectUnknownPrimaryKeyComponent()
    {
        var keys = new IRecordKeyDefinition<TestRecord>[]
        {
            new RecordKeyDefinition<TestRecord, string?>("machine", static value => value.Machine)
        };

        var exception = Assert.Throws<ArgumentException>(() =>
            new RecordStoreDefinition<TestRecord, TestPrimaryKey>(
                keys,
                new PrimaryKeyDefinition<TestRecord, TestPrimaryKey>(
                    ["machine", "ip"],
                    static value => new TestPrimaryKey(value.Machine, value.Ip))));

        Assert.Contains("Unknown key component 'ip'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void KeyDefinition_ShouldTreatNullAsOneComparableValue()
    {
        var key = new RecordKeyDefinition<TestRecord, string?>("machine", static value => value.Machine);

        Assert.True(key.KeysEqual(null, null));
        Assert.False(key.KeysEqual(null, "host"));
        Assert.True(key.CompareKeys(null, "host") < 0);
    }

    [Fact]
    public void CompositePrimaryKey_ShouldSortByRegisteredComponentKeyComparers()
    {
        var definition = new RecordStoreDefinition<TestRecord, TestPrimaryKey>(
            [
                new RecordKeyDefinition<TestRecord, string?>(
                    "machine",
                    static value => value.Machine,
                    StringComparer.OrdinalIgnoreCase,
                    StringComparer.OrdinalIgnoreCase),
                new RecordKeyDefinition<TestRecord, string?>(
                    "ip",
                    static value => value.Ip,
                    StringComparer.Ordinal,
                    StringComparer.Ordinal)
            ],
            new PrimaryKeyDefinition<TestRecord, TestPrimaryKey>(
                ["machine", "ip"],
                static value => new TestPrimaryKey(value.Machine, value.Ip)));
        var store = new RecordStore<TestRecord, TestPrimaryKey>(definition);

        Assert.True(store.TryAdd(new TestRecord { Machine = "selene", Ip = "192.168.32.10" }, out _));
        Assert.True(store.TryAdd(new TestRecord { Machine = "Atlas", Ip = "192.168.32.12" }, out _));

        var keys = store.GetPrimaryKeys();

        Assert.Equal(["Atlas", "selene"], keys.Select(static key => key.Machine));
        Assert.Empty(store.ValidateConstraints());
    }

    [Fact]
    public void PublishFormat_ShouldOnlyHoldBusinessAggregateDelegate()
    {
        Aggregate<TestRecord> aggregate = static values => values[0];
        var format = new RecordStorePublishFormat<TestRecord>(ConflictAggregator: aggregate);

        Assert.Same(aggregate, format.ConflictAggregator);
    }

    private readonly record struct TestPrimaryKey(string? Machine, string? Ip);

    private record struct TestRecord : IRecordStoreValue
    {
        public StoreRecordId StoreRecordId { get; set; }
        public string? Machine { get; init; }
        public string? Ip { get; init; }
    }

    private sealed class TestCloneStrategy : IDeepCloneStrategy<TestRecord>
    {
        public TestRecord Clone(in TestRecord value) => value;
    }

    private sealed class TestCodec : ITableValueCodec<TestRecord>
    {
        public byte[] Encode(in TestRecord value) => [];
        public TestRecord Decode(ReadOnlySpan<byte> payload) => default;
    }
}

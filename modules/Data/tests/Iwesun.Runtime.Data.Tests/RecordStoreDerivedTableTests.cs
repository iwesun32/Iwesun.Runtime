using Xunit;

namespace Iwesun.Runtime.Data.Tests;

public sealed class RecordStoreDerivedTableTests
{
    [Fact]
    public void ReferenceRecord_ShouldRequireExplicitCloneStrategy()
    {
        var store = new RecordStore<DeviceRecord, string>(CreateDefinition());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            store.Add(new DeviceRecord { Name = "Atlas" }));

        Assert.Contains("requires an explicit deep clone strategy", exception.Message);
        Assert.Empty(store);
    }

    [Fact]
    public void ReferenceRecord_ShouldKeepInputAndQueryOwnershipDetached()
    {
        var store = new RecordStore<DeviceRecord, string>(CreateDefinition())
        {
            CloneStrategy = DeviceRecordCloneStrategy.Instance
        };
        var input = new DeviceRecord { Name = "Atlas" };

        store.Add(input);
        input.Name = "ChangedInput";
        var firstRead = Assert.Single(store);
        firstRead.Name = "ChangedRead";

        Assert.Equal("Atlas", Assert.Single(store).Name);
    }

    [Fact]
    public void ReferenceRecord_NullUpdate_ShouldReturnNotFound()
    {
        var store = new RecordStore<DeviceRecord, string>(CreateDefinition())
        {
            CloneStrategy = DeviceRecordCloneStrategy.Instance
        };
        DeviceRecord value = null!;

        Assert.Equal(RecordUpdateResult.NotFound, store.TryUpdate(value));
        Assert.Empty(store);
    }

    [Fact]
    public void DerivedTable_ShouldPreserveTypeAndDetachHeaderAcrossOutputs()
    {
        var source = new DeviceTable();
        var initialVersion = source.DataVersion;
        source.SetLocalName("Apollo");
        Assert.Equal(initialVersion + 1, source.DataVersion);
        source.Add(new DeviceRecord { Name = "Atlas" });

        var snapshot = Assert.IsType<DeviceTable>(
            source.Publish(RecordStorePublishTarget.Snapshot));
        source.RegisterSnapshotSubscriber("consumer");
        snapshot = Assert.IsType<DeviceTable>(
            source.Publish(RecordStorePublishTarget.Snapshot));
        Assert.True(source.TryTakeSnapshot("consumer", out var taken));
        Assert.IsType<DeviceTable>(taken);
        var view = Assert.IsType<DeviceTable>(
            source.CreateView(new RecordStoreViewDefinition<DeviceRecord>()));
        var clone = Assert.IsType<DeviceTable>(source.DeepClone());
        var compacted = Assert.IsType<DeviceTable>(source.CreateCompactedStore());

        Assert.Equal("Apollo", snapshot.Local.Name);
        Assert.Equal("Apollo", view.Local.Name);
        Assert.Equal("Apollo", clone.Local.Name);
        Assert.Equal("Apollo", compacted.Local.Name);
        Assert.NotSame(source.Local, snapshot.Local);
        Assert.NotSame(source.Local, view.Local);
        Assert.NotSame(source.Local, clone.Local);
        Assert.NotSame(source.Local, compacted.Local);

        source.SetLocalName("Changed");
        Assert.Equal("Apollo", snapshot.Local.Name);
        Assert.Throws<InvalidOperationException>(() =>
            snapshot.Add(new DeviceRecord { Name = "Selene" }));
    }

    [Fact]
    public void DetachedSourceCopy_ShouldPreserveDifferentSourceAndSnapshotStates()
    {
        var source = new DeviceTable();
        source.SetLocalName("Apollo");
        source.Add(new DeviceRecord { Name = "Atlas" });
        source.RegisterSnapshotSubscriber("original-consumer");
        source.Publish(RecordStorePublishTarget.Snapshot);
        source.Add(new DeviceRecord { Name = "Selene" });

        var detached = Assert.IsType<DeviceTable>(source.CreateDetachedSourceCopy());
        var detachedSnapshot = Assert.IsType<DeviceTable>(detached.Snapshot);

        Assert.Equal(2, detached.Count);
        Assert.Single(detachedSnapshot);
        Assert.Equal("Apollo", detached.Local.Name);
        Assert.Equal("Apollo", detachedSnapshot.Local.Name);
        Assert.Equal(
            SnapshotTakeState.NotSubscribed,
            detached.GetSnapshotTakeState("original-consumer"));

        detached.Add(new DeviceRecord { Name = "Hades" });
        Assert.IsType<DeviceTable>(
            detached.Publish(RecordStorePublishTarget.Snapshot));
        Assert.Equal(3, detached.Snapshot!.Count);
        Assert.Equal(2, source.Count);
        Assert.Single(source.Snapshot!);
    }

    [Fact]
    public void Clear_ShouldRemoveSourceValuesAndDerivedHeaderButKeepSnapshot()
    {
        var source = new DeviceTable();
        source.SetLocalName("Apollo");
        source.Add(new DeviceRecord { Name = "Atlas" });
        source.RegisterSnapshotSubscriber("consumer");
        source.Publish(RecordStorePublishTarget.Snapshot);
        var publicationVersion = source.PublicationVersion;

        source.Clear();

        Assert.Empty(source);
        Assert.Equal(string.Empty, source.Local.Name);
        Assert.Single(source.Snapshot!);
        Assert.Equal("Apollo", Assert.IsType<DeviceTable>(source.Snapshot).Local.Name);
        Assert.Equal(publicationVersion, source.PublicationVersion);
        Assert.Equal(
            SnapshotTakeState.Pending,
            source.GetSnapshotTakeState("consumer"));
    }

    [Fact]
    public void JsonRestore_ShouldUseRegisteredDerivedFactory()
    {
        var source = new DeviceTable
        {
            SchemaId = $"derived.restore-{Guid.NewGuid():N}.v1"
        };
        source.Add(new DeviceRecord { Name = "Atlas" });
        RecordStoreSchemaRegistry<DeviceRecord, string>.Register(source);

        var restored = Assert.IsType<DeviceTable>(
            RecordStore<DeviceRecord, string>.RestoreJson(source.ToJson()));

        Assert.Equal(RecordStoreOrigin.Restored, restored.Origin);
        Assert.Equal("Atlas", Assert.Single(restored).Name);
        Assert.Equal(string.Empty, restored.Local.Name);
    }

    private static RecordStoreDefinition<DeviceRecord, string> CreateDefinition()
    {
        var name = new RecordKeyDefinition<DeviceRecord, string>(
            "name",
            static value => value.Name,
            StringComparer.OrdinalIgnoreCase,
            StringComparer.OrdinalIgnoreCase);
        return new RecordStoreDefinition<DeviceRecord, string>(
            [name],
            new PrimaryKeyDefinition<DeviceRecord, string>(
                ["name"],
                static value => value.Name,
                StringComparer.OrdinalIgnoreCase,
                StringComparer.OrdinalIgnoreCase));
    }

    private sealed class DeviceTable : RecordStore<DeviceRecord, string>
    {
        public DeviceTable()
            : this(CreateDefinition(), new DeviceRecord())
        {
        }

        private DeviceTable(
            RecordStoreDefinition<DeviceRecord, string> definition,
            DeviceRecord local)
            : base(definition)
        {
            Local = local;
            CloneStrategy = DeviceRecordCloneStrategy.Instance;
        }

        public DeviceRecord Local { get; }

        public void SetLocalName(string name)
        {
            Local.Name = name;
            MarkAdditionalStateChanged();
        }

        public override void Clear()
        {
            base.Clear();
            if (string.IsNullOrEmpty(Local.Name))
                return;
            Local.Name = string.Empty;
            MarkAdditionalStateChanged();
        }

        protected override RecordStore<DeviceRecord, string> CreateDerivedOutputStore(
            RecordStoreOrigin origin) =>
            new DeviceTable(Definition, DeviceRecordCloneStrategy.Instance.Clone(Local));
    }

    private sealed class DeviceRecord : IRecordStoreValue
    {
        public StoreRecordId StoreRecordId { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class DeviceRecordCloneStrategy : IDeepCloneStrategy<DeviceRecord>
    {
        public static DeviceRecordCloneStrategy Instance { get; } = new();

        public DeviceRecord Clone(in DeviceRecord value) => new()
        {
            StoreRecordId = value.StoreRecordId,
            Name = value.Name
        };
    }
}

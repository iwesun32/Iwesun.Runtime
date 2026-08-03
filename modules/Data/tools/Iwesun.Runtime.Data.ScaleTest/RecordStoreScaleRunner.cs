using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using Iwesun.Runtime.Data;


internal static class RecordStoreScaleRunner
{
    public static void Run(int targetMb, int batchSize, int recordTarget, string mode)
    {
        if (mode == "churn")
        {
            RunChurn();
            return;
        }
        var groupKey = new RecordKeyDefinition<ScaleRecord, int>("group", static value => value.GroupKey);
        var sequenceKey = new RecordKeyDefinition<ScaleRecord, long>("sequence", static value => value.Sequence);
        IReadOnlyList<IUniqueConstraintDefinition<ScaleRecord>> constraints = mode == "aggregate"
            ? [new UniqueConstraintDefinition<ScaleRecord, int>("group-unique", ["group"], static value => value.GroupKey)]
            : [];
        var definition = new RecordStoreDefinition<ScaleRecord, ScalePrimaryKey>(
            [groupKey, sequenceKey],
            new PrimaryKeyDefinition<ScaleRecord, ScalePrimaryKey>(
                ["group", "sequence"], static value => new ScalePrimaryKey(value.GroupKey, value.Sequence)));
        var table = new RecordStore<ScaleRecord, ScalePrimaryKey>(definition);
        table.AllowUniqueConstraintViolation = mode == "aggregate";
        table.SetUniqueConstraints(constraints);

        ForceGc();
        var baseline = GC.GetTotalMemory(true);
        var appendAllocatedStart = GC.GetTotalAllocatedBytes(true);
        var appendGen0Start = GC.CollectionCount(0);
        var appendGen1Start = GC.CollectionCount(1);
        var appendGen2Start = GC.CollectionCount(2);
        var appendTimer = Stopwatch.StartNew();
        var sequence = 0;
        long retained;
        do
        {
            var currentBatch = recordTarget > 0 ? Math.Min(batchSize, recordTarget - sequence) : batchSize;
            for (var index = 0; index < currentBatch; index++)
            {
                var current = sequence++;
                table.Add(new ScaleRecord
                {
                    GroupKey = current % 100_000,
                    Sequence = current,
                    Payload1 = current * 17L,
                    Payload2 = current * 31L
                });
            }
            ForceGc();
            retained = GC.GetTotalMemory(true) - baseline;
        } while ((recordTarget > 0 ? sequence < recordTarget : retained < targetMb * 1024L * 1024L)
                 && sequence < 10_000_000);
        appendTimer.Stop();
        var appendAllocated = GC.GetTotalAllocatedBytes(true) - appendAllocatedStart;
        var appendGen0 = GC.CollectionCount(0) - appendGen0Start;
        var appendGen1 = GC.CollectionCount(1) - appendGen1Start;
        var appendGen2 = GC.CollectionCount(2) - appendGen2Start;

        if (recordTarget == 0 && retained < targetMb * 1024L * 1024L)
            throw new InvalidOperationException($"Unable to retain the requested {targetMb} MB within the record safety limit.");

        if (mode == "hotpath")
        {
            RunHotPath(table);
            return;
        }

        var lookupTimer = Stopwatch.StartNew();
        long checksum = 0;
        var lookupHits = 0;
        for (var key = 0; key < 100_000; key += 997)
        {
            foreach (var value in table.GetValues(groupKey, key))
            {
                checksum += value.Sequence;
                lookupHits++;
            }
        }
        lookupTimer.Stop();

        var beforePublication = GC.GetTotalMemory(true);
        var publishAllocatedStart = GC.GetTotalAllocatedBytes(true);
        var publishGen0Start = GC.CollectionCount(0);
        var publishGen1Start = GC.CollectionCount(1);
        var publishGen2Start = GC.CollectionCount(2);
        var publishTimer = Stopwatch.StartNew();
        var snapshot = mode == "aggregate"
            ? table.Publish(
                RecordStorePublishTarget.Snapshot,
                new RecordStorePublishFormat<ScaleRecord>(ConflictAggregator: AggregateGroup))
            : table.Publish(RecordStorePublishTarget.Snapshot);
        publishTimer.Stop();
        var publicationIncrease = GC.GetTotalMemory(true) - beforePublication;
        var publishAllocated = GC.GetTotalAllocatedBytes(true) - publishAllocatedStart;

        var expectedSnapshotCount = mode == "aggregate" ? Math.Min(table.Count, 100_000) : table.Count;
        if (snapshot.Count != expectedSnapshotCount)
            throw new InvalidOperationException("Snapshot count differs from expected publication result.");
        if (!snapshot.IsReadOnly || snapshot.Origin != RecordStoreOrigin.Snapshot)
            throw new InvalidOperationException("Published table is not a read-only snapshot.");
        if (lookupHits == 0)
            throw new InvalidOperationException("Indexed lookup did not produce any records.");

        Console.WriteLine("engine=record-store");
        Console.WriteLine($"runtime={Environment.Version}");
        Console.WriteLine($"process_architecture={RuntimeInformation.ProcessArchitecture}");
        Console.WriteLine($"process_64_bit={Environment.Is64BitProcess}");
        Console.WriteLine($"gc_server={GCSettings.IsServerGC}");
        Console.WriteLine($"gc_latency_mode={GCSettings.LatencyMode}");
        Console.WriteLine($"target_mb={targetMb}");
        Console.WriteLine($"requested_records={recordTarget:N0}");
        Console.WriteLine($"mode={mode}");
        Console.WriteLine($"records={table.Count:N0}");
        Console.WriteLine($"source_retained_mb={ToMb(retained):F1}");
        Console.WriteLine($"append_seconds={appendTimer.Elapsed.TotalSeconds:F3}");
        Console.WriteLine($"append_records_per_second={table.Count / appendTimer.Elapsed.TotalSeconds:N0}");
        Console.WriteLine($"append_allocated_mb={ToMb(appendAllocated):F1}");
        Console.WriteLine($"append_gc_collections={appendGen0}/{appendGen1}/{appendGen2}");
        Console.WriteLine($"indexed_lookup_milliseconds={lookupTimer.Elapsed.TotalMilliseconds:F3}");
        Console.WriteLine($"indexed_lookup_hits={lookupHits:N0}");
        Console.WriteLine($"indexed_lookup_checksum={checksum}");
        Console.WriteLine($"publication_seconds={publishTimer.Elapsed.TotalSeconds:F3}");
        Console.WriteLine($"publication_increase_mb={ToMb(publicationIncrease):F1}");
        Console.WriteLine($"publication_allocated_mb={ToMb(publishAllocated):F1}");
        Console.WriteLine($"publication_gc_collections={GC.CollectionCount(0) - publishGen0Start}/{GC.CollectionCount(1) - publishGen1Start}/{GC.CollectionCount(2) - publishGen2Start}");
        Console.WriteLine($"snapshot_records={snapshot.Count:N0}");
        Console.WriteLine("record-store-scale: PASS");
    }

    private static void RunHotPath(RecordStore<ScaleRecord, ScalePrimaryKey> table)
    {
        const int operations = 100_000;
        if (table.Count < operations)
            throw new InvalidOperationException("Hot-path validation requires at least 100,000 records.");
        var initialCount = table.Count;

        var updateTimer = Stopwatch.StartNew();
        var allocatedStart = GC.GetTotalAllocatedBytes(true);
        var gen0Start = GC.CollectionCount(0);
        var gen1Start = GC.CollectionCount(1);
        var gen2Start = GC.CollectionCount(2);
        for (var index = 0; index < operations; index++)
        {
            var id = new StoreRecordId(index + 1L);
            if (!table.TryGetRecord(id, out var value))
                throw new InvalidOperationException($"Record {id.Value} was not found for update.");
            value = value with { Payload1 = value.Payload1 + 1 };
            if (table.TryUpdate(value) != RecordUpdateResult.Updated)
                throw new InvalidOperationException($"Record {id.Value} update failed.");
        }
        updateTimer.Stop();

        var deprecateTimer = Stopwatch.StartNew();
        for (var index = 0; index < operations / 2; index++)
        {
            if (!table.TryDeprecate(new StoreRecordId(index * 2L + 1)))
                throw new InvalidOperationException("Hot-path deprecation failed.");
        }
        deprecateTimer.Stop();

        if (table.Count != initialCount - operations / 2)
            throw new InvalidOperationException("Hot-path active count is incorrect after deprecation.");
        Console.WriteLine("engine=record-store");
        Console.WriteLine("mode=hotpath");
        Console.WriteLine($"update_operations={operations:N0}");
        Console.WriteLine($"update_seconds={updateTimer.Elapsed.TotalSeconds:F3}");
        Console.WriteLine($"deprecate_operations={operations / 2:N0}");
        Console.WriteLine($"deprecate_seconds={deprecateTimer.Elapsed.TotalSeconds:F3}");
        Console.WriteLine($"hotpath_allocated_mb={ToMb(GC.GetTotalAllocatedBytes(true) - allocatedStart):F1}");
        Console.WriteLine($"hotpath_gc_collections={GC.CollectionCount(0) - gen0Start}/{GC.CollectionCount(1) - gen1Start}/{GC.CollectionCount(2) - gen2Start}");
        Console.WriteLine($"remaining_records={table.Count:N0}");
        Console.WriteLine("record-store-hotpath: PASS");
    }

    private static void RunChurn()
    {
        const int activeRecords = 10_000;
        const int replacements = 1_000_000;
        var groupKey = new RecordKeyDefinition<ScaleRecord, int>("group", static value => value.GroupKey);
        var sequenceKey = new RecordKeyDefinition<ScaleRecord, long>("sequence", static value => value.Sequence);
        var definition = new RecordStoreDefinition<ScaleRecord, ScalePrimaryKey>(
            [groupKey, sequenceKey],
            new PrimaryKeyDefinition<ScaleRecord, ScalePrimaryKey>(
                ["group", "sequence"], static value => new ScalePrimaryKey(value.GroupKey, value.Sequence)));
        var table = new RecordStore<ScaleRecord, ScalePrimaryKey>(definition);
        var activeIds = new StoreRecordId[activeRecords];
        for (var index = 0; index < activeRecords; index++)
            activeIds[index] = table.Add(new ScaleRecord { GroupKey = index, Sequence = index });

        var timer = Stopwatch.StartNew();
        for (var index = 0; index < replacements; index++)
        {
            var slot = index % activeRecords;
            if (!table.TryDeprecate(activeIds[slot]))
                throw new InvalidOperationException("Churn deprecation failed.");
            var sequence = activeRecords + index;
            activeIds[slot] = table.Add(new ScaleRecord { GroupKey = slot, Sequence = sequence });
        }
        timer.Stop();

        if (table.Count != activeRecords || table.PhysicalCount != activeRecords + replacements)
            throw new InvalidOperationException("Churn physical or active count is incorrect.");
        var compacted = table.CreateCompactedStore();
        if (compacted.Count != activeRecords || compacted.PhysicalCount != activeRecords
            || compacted.DeprecatedCount != 0)
            throw new InvalidOperationException("Compaction did not return a dense active store.");
        foreach (var id in activeIds)
            if (!compacted.TryGetRecord(id, out _))
                throw new InvalidOperationException("Compaction did not preserve an active StoreRecordId.");

        Console.WriteLine("engine=record-store");
        Console.WriteLine("mode=churn");
        Console.WriteLine($"active_records={table.Count:N0}");
        Console.WriteLine($"replacement_operations={replacements:N0}");
        Console.WriteLine($"physical_records_before={table.PhysicalCount:N0}");
        Console.WriteLine($"deprecated_records_before={table.DeprecatedCount:N0}");
        Console.WriteLine($"physical_records_after={compacted.PhysicalCount:N0}");
        Console.WriteLine($"churn_seconds={timer.Elapsed.TotalSeconds:F3}");
        Console.WriteLine("record-store-churn: PASS");
    }

    private static ScaleRecord AggregateGroup(IReadOnlyList<ScaleRecord> values)
    {
        var result = values[0];
        for (var index = 1; index < values.Count; index++)
        {
            result = result with
            {
                Sequence = result.Sequence + values[index].Sequence,
                Payload1 = result.Payload1 + values[index].Payload1,
                Payload2 = result.Payload2 + values[index].Payload2
            };
        }
        return result;
    }

    private static void ForceGc()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
    }

    private static double ToMb(long bytes) => bytes / 1024d / 1024d;
}

internal readonly record struct ScalePrimaryKey(int GroupKey, long Sequence) : IComparable<ScalePrimaryKey>
{
    public int CompareTo(ScalePrimaryKey other)
    {
        var group = GroupKey.CompareTo(other.GroupKey);
        return group != 0 ? group : Sequence.CompareTo(other.Sequence);
    }
}

internal record struct ScaleRecord : IRecordStoreValue
{
    public StoreRecordId StoreRecordId { get; set; }
    public int GroupKey { get; init; }
    public long Sequence { get; init; }
    public long Payload1 { get; init; }
    public long Payload2 { get; init; }
}

using System.Diagnostics;
using Iwesun.Runtime.Data;


internal static class RecordStoreIndexBenchmarkRunner
{
    private static readonly int[] RecordCounts = [64, 128, 256, 512, 768, 1024, 2048, 4096, 16384];

    public static void Run(int repetitions, int queryOperations)
    {
        if (repetitions < 3) throw new ArgumentOutOfRangeException(nameof(repetitions), "At least three repetitions are required.");
        if (queryOperations < 1_000) throw new ArgumentOutOfRangeException(nameof(queryOperations));

        WarmUp();
        Console.WriteLine("engine=record-store-index-benchmark");
        Console.WriteLine($"repetitions={repetitions}");
        Console.WriteLine($"query_operations={queryOperations}");
        Console.WriteLine(
            "key_type,duplicates,records,mode," +
            "build_us_p50,build_us_p90,index_bytes_p50," +
            "count_hit_ns_p50,count_hit_ns_p90,count_miss_ns_p50," +
            "values_hit_ns_p50,keys_ns_p50,key_count_ns_p50," +
            "add_ns_p50,add_ns_p90,nonkey_update_ns_p50,key_update_ns_p50,deprecate_ns_p50," +
            "count_alloc_b,values_alloc_b,add_alloc_b,checksum");

        foreach (var duplicates in new[] { 1, 5 })
        {
            foreach (var count in RecordCounts)
            {
                RunMode(
                    "int",
                    count,
                    duplicates,
                    RecordStoreKeyIndexMode.SortedTable,
                    repetitions,
                    queryOperations,
                    static value => value);
                RunMode(
                    "int",
                    count,
                    duplicates,
                    RecordStoreKeyIndexMode.Hash,
                    repetitions,
                    queryOperations,
                    static value => value);
                RunMode(
                    "string-ordinal",
                    count,
                    duplicates,
                    RecordStoreKeyIndexMode.SortedTable,
                    repetitions,
                    queryOperations,
                    static value => $"g{value:D8}",
                    StringComparer.Ordinal,
                    StringComparer.Ordinal);
                RunMode(
                    "string-ordinal",
                    count,
                    duplicates,
                    RecordStoreKeyIndexMode.Hash,
                    repetitions,
                    queryOperations,
                    static value => $"g{value:D8}",
                    StringComparer.Ordinal,
                    StringComparer.Ordinal);
            }
        }
        RunMixedWorkloads(repetitions, Math.Min(queryOperations, 50_000));
        RunEarlyActivationGrowth(repetitions);
        Console.WriteLine("record-store-index-benchmark: PASS");
    }

    private static void RunEarlyActivationGrowth(int repetitions)
    {
        Console.WriteLine("early_key_type,duplicates,target_records,mode,growth_add_ns_p50,growth_add_ns_p90,checksum");
        foreach (var duplicates in new[] { 1, 5 })
        {
            foreach (var count in new[] { 256, 512, 1024 })
            {
                RunEarlyMode("int", count, duplicates, RecordStoreKeyIndexMode.SortedTable, repetitions,
                    static value => value);
                RunEarlyMode("int", count, duplicates, RecordStoreKeyIndexMode.Hash, repetitions,
                    static value => value);
                RunEarlyMode("string-ordinal", count, duplicates, RecordStoreKeyIndexMode.SortedTable,
                    repetitions, static value => $"g{value:D8}", StringComparer.Ordinal, StringComparer.Ordinal);
                RunEarlyMode("string-ordinal", count, duplicates, RecordStoreKeyIndexMode.Hash,
                    repetitions, static value => $"g{value:D8}", StringComparer.Ordinal, StringComparer.Ordinal);
            }
        }
    }

    private static void RunEarlyMode<TKey>(
        string keyType,
        int count,
        int duplicates,
        RecordStoreKeyIndexMode mode,
        int repetitions,
        Func<int, TKey> keyFactory,
        IEqualityComparer<TKey>? equalityComparer = null,
        IComparer<TKey>? comparer = null)
    {
        var samples = new (double Nanoseconds, long Checksum)[repetitions];
        for (var repetition = 0; repetition < repetitions; repetition++)
            samples[repetition] = RunEarlyCase(
                count, duplicates, mode, keyFactory, equalityComparer, comparer);
        var checksums = samples.Select(static sample => sample.Checksum).Distinct().ToArray();
        if (checksums.Length != 1)
            throw new InvalidOperationException("Repeated early activation runs returned different results.");
        var values = samples.Select(static sample => sample.Nanoseconds).Order().ToArray();
        Console.WriteLine(string.Join(',',
            keyType,
            duplicates,
            count,
            mode,
            values[(values.Length - 1) / 2].ToString("F3"),
            values[(int)Math.Ceiling((values.Length - 1) * 0.90)].ToString("F3"),
            checksums[0]));
    }

    private static (double Nanoseconds, long Checksum) RunEarlyCase<TKey>(
        int count,
        int duplicates,
        RecordStoreKeyIndexMode mode,
        Func<int, TKey> keyFactory,
        IEqualityComparer<TKey>? equalityComparer,
        IComparer<TKey>? comparer)
    {
        var groupKey = new RecordKeyDefinition<IndexRecord<TKey>, TKey>(
            "group", static value => value.Group, equalityComparer, comparer);
        var idKey = new RecordKeyDefinition<IndexRecord<TKey>, int>("id", static value => value.BusinessId);
        var definition = new RecordStoreDefinition<IndexRecord<TKey>, int>(
            [idKey, groupKey],
            new PrimaryKeyDefinition<IndexRecord<TKey>, int>(["id"], static value => value.BusinessId));
        var store = new RecordStore<IndexRecord<TKey>, int>(definition) { KeyIndexMode = mode };
        var groupCount = Math.Max(1, count / duplicates);
        var groupKeys = Enumerable.Range(0, groupCount).Select(keyFactory).ToArray();
        const int activationCount = 16;
        for (var index = 0; index < activationCount; index++)
        {
            store.Add(new IndexRecord<TKey>
            {
                BusinessId = index + 1,
                Group = groupKeys[index % groupCount],
                Payload = index
            });
        }
        _ = store.GetValueCount(groupKey, groupKeys[0]);

        var timer = Stopwatch.StartNew();
        for (var index = activationCount; index < count; index++)
        {
            store.Add(new IndexRecord<TKey>
            {
                BusinessId = index + 1,
                Group = groupKeys[index % groupCount],
                Payload = index
            });
        }
        timer.Stop();
        long checksum = store.Count + store.GetKeyCount(groupKey) + store.GetValueCount(groupKey, groupKeys[0]);
        return (timer.Elapsed.TotalNanoseconds / (count - activationCount), checksum);
    }

    private static void RunMixedWorkloads(int repetitions, int operations)
    {
        Console.WriteLine("workload_key_type,records,write_percent,mode,operation_ns_p50,operation_ns_p90,checksum");
        foreach (var count in new[] { 512, 1024, 2048 })
        {
            foreach (var writePercent in new[] { 1, 10, 50, 90 })
            {
                RunMixedMode("int", count, writePercent, RecordStoreKeyIndexMode.SortedTable, repetitions, operations,
                    static value => value);
                RunMixedMode("int", count, writePercent, RecordStoreKeyIndexMode.Hash, repetitions, operations,
                    static value => value);
                RunMixedMode("string-ordinal", count, writePercent, RecordStoreKeyIndexMode.SortedTable,
                    repetitions, operations, static value => $"g{value:D8}", StringComparer.Ordinal, StringComparer.Ordinal);
                RunMixedMode("string-ordinal", count, writePercent, RecordStoreKeyIndexMode.Hash,
                    repetitions, operations, static value => $"g{value:D8}", StringComparer.Ordinal, StringComparer.Ordinal);
            }
        }
    }

    private static void RunMixedMode<TKey>(
        string keyType,
        int count,
        int writePercent,
        RecordStoreKeyIndexMode mode,
        int repetitions,
        int operations,
        Func<int, TKey> keyFactory,
        IEqualityComparer<TKey>? equalityComparer = null,
        IComparer<TKey>? comparer = null)
    {
        var samples = new (double Nanoseconds, long Checksum)[repetitions];
        for (var repetition = 0; repetition < repetitions; repetition++)
            samples[repetition] = RunMixedCase(
                count, writePercent, mode, operations, keyFactory, equalityComparer, comparer);
        var checksums = samples.Select(static sample => sample.Checksum).Distinct().ToArray();
        if (checksums.Length != 1)
            throw new InvalidOperationException("Repeated mixed benchmark runs returned different results.");
        var values = samples.Select(static sample => sample.Nanoseconds).Order().ToArray();
        Console.WriteLine(string.Join(',',
            keyType,
            count,
            writePercent,
            mode,
            values[(values.Length - 1) / 2].ToString("F3"),
            values[(int)Math.Ceiling((values.Length - 1) * 0.90)].ToString("F3"),
            checksums[0]));
    }

    private static (double Nanoseconds, long Checksum) RunMixedCase<TKey>(
        int count,
        int writePercent,
        RecordStoreKeyIndexMode mode,
        int operations,
        Func<int, TKey> keyFactory,
        IEqualityComparer<TKey>? equalityComparer,
        IComparer<TKey>? comparer)
    {
        var groupKey = new RecordKeyDefinition<IndexRecord<TKey>, TKey>(
            "group", static value => value.Group, equalityComparer, comparer);
        var idKey = new RecordKeyDefinition<IndexRecord<TKey>, int>("id", static value => value.BusinessId);
        var definition = new RecordStoreDefinition<IndexRecord<TKey>, int>(
            [idKey, groupKey],
            new PrimaryKeyDefinition<IndexRecord<TKey>, int>(["id"], static value => value.BusinessId));
        var store = new RecordStore<IndexRecord<TKey>, int>(definition) { KeyIndexMode = mode };
        const int duplicates = 5;
        var groupCount = Math.Max(1, count / duplicates);
        var groupKeys = Enumerable.Range(0, groupCount).Select(keyFactory).ToArray();
        for (var index = 0; index < count; index++)
        {
            store.Add(new IndexRecord<TKey>
            {
                BusinessId = index + 1,
                Group = groupKeys[index % groupCount],
                Payload = index
            });
        }
        _ = store.GetValueCount(groupKey, groupKeys[groupCount / 2]);

        var added = new Queue<StoreRecordId>();
        var nextBusinessId = count + 1;
        var writeOrdinal = 0;
        long checksum = 0;
        var timer = Stopwatch.StartNew();
        for (var operation = 0; operation < operations; operation++)
        {
            if (operation % 100 >= writePercent)
            {
                checksum += store.GetValueCount(groupKey, groupKeys[(operation * 997) % groupCount]);
                continue;
            }

            switch (writeOrdinal++ & 3)
            {
                case 0:
                    {
                        var id = new StoreRecordId(operation % count + 1L);
                        if (!store.TryGetRecord(id, out var value)
                            || store.TryUpdate(value with { Payload = value.Payload + 1 }) != RecordUpdateResult.Updated)
                            throw new InvalidOperationException("Mixed benchmark non-key update failed.");
                        checksum++;
                        break;
                    }
                case 1:
                    {
                        var id = new StoreRecordId(operation % count + 1L);
                        if (!store.TryGetRecord(id, out var value)
                            || store.TryUpdate(value with { Group = groupKeys[(operation * 31) % groupCount] })
                            != RecordUpdateResult.Updated)
                            throw new InvalidOperationException("Mixed benchmark key update failed.");
                        checksum++;
                        break;
                    }
                case 2:
                    {
                        var value = new IndexRecord<TKey>
                        {
                            BusinessId = nextBusinessId,
                            Group = groupKeys[(nextBusinessId * 31) % groupCount],
                            Payload = nextBusinessId
                        };
                        nextBusinessId++;
                        if (!store.TryAdd(value, out var id))
                            throw new InvalidOperationException("Mixed benchmark add failed.");
                        added.Enqueue(id);
                        checksum++;
                        break;
                    }
                default:
                    if (added.Count == 0 || !store.TryDeprecate(added.Dequeue()))
                        throw new InvalidOperationException("Mixed benchmark deprecate failed.");
                    checksum++;
                    break;
            }
        }
        timer.Stop();
        checksum += store.Count;
        return (timer.Elapsed.TotalNanoseconds / operations, checksum);
    }

    private static void RunMode<TKey>(
        string keyType,
        int count,
        int duplicates,
        RecordStoreKeyIndexMode mode,
        int repetitions,
        int queryOperations,
        Func<int, TKey> keyFactory,
        IEqualityComparer<TKey>? equalityComparer = null,
        IComparer<TKey>? comparer = null)
    {
        var results = new BenchmarkResult[repetitions];
        for (var repetition = 0; repetition < repetitions; repetition++)
        {
            results[repetition] = RunCase(
                count,
                duplicates,
                mode,
                queryOperations,
                keyFactory,
                equalityComparer,
                comparer);
        }

        var checksums = results.Select(static result => result.Checksum).Distinct().ToArray();
        if (checksums.Length != 1)
            throw new InvalidOperationException("Repeated benchmark runs returned different results.");

        Console.WriteLine(string.Join(',',
            keyType,
            duplicates,
            count,
            mode,
            P50(results, static result => result.BuildMicroseconds).ToString("F3"),
            P90(results, static result => result.BuildMicroseconds).ToString("F3"),
            (long)P50(results, static result => result.IndexBytes),
            P50(results, static result => result.CountHitNanoseconds).ToString("F3"),
            P90(results, static result => result.CountHitNanoseconds).ToString("F3"),
            P50(results, static result => result.CountMissNanoseconds).ToString("F3"),
            P50(results, static result => result.ValuesHitNanoseconds).ToString("F3"),
            P50(results, static result => result.KeysNanoseconds).ToString("F3"),
            P50(results, static result => result.KeyCountNanoseconds).ToString("F3"),
            P50(results, static result => result.AddNanoseconds).ToString("F3"),
            P90(results, static result => result.AddNanoseconds).ToString("F3"),
            P50(results, static result => result.NonKeyUpdateNanoseconds).ToString("F3"),
            P50(results, static result => result.KeyUpdateNanoseconds).ToString("F3"),
            P50(results, static result => result.DeprecateNanoseconds).ToString("F3"),
            P50(results, static result => result.CountAllocatedBytes).ToString("F3"),
            P50(results, static result => result.ValuesAllocatedBytes).ToString("F3"),
            P50(results, static result => result.AddAllocatedBytes).ToString("F3"),
            checksums[0]));
    }

    private static BenchmarkResult RunCase<TKey>(
        int count,
        int duplicates,
        RecordStoreKeyIndexMode mode,
        int queryOperations,
        Func<int, TKey> keyFactory,
        IEqualityComparer<TKey>? equalityComparer,
        IComparer<TKey>? comparer)
    {
        var groupKey = new RecordKeyDefinition<IndexRecord<TKey>, TKey>(
            "group",
            static value => value.Group,
            equalityComparer,
            comparer);
        var idKey = new RecordKeyDefinition<IndexRecord<TKey>, int>("id", static value => value.BusinessId);
        var definition = new RecordStoreDefinition<IndexRecord<TKey>, int>(
            [idKey, groupKey],
            new PrimaryKeyDefinition<IndexRecord<TKey>, int>(["id"], static value => value.BusinessId));
        var store = new RecordStore<IndexRecord<TKey>, int>(definition) { KeyIndexMode = mode };
        var groupCount = Math.Max(1, count / duplicates);
        var groupKeys = Enumerable.Range(0, groupCount).Select(keyFactory).ToArray();
        for (var index = 0; index < count; index++)
        {
            store.Add(new IndexRecord<TKey>
            {
                BusinessId = index + 1,
                Group = groupKeys[index % groupCount],
                Payload = index
            });
        }

        ForceGc();
        var beforeIndex = GC.GetTotalMemory(true);
        var buildTimer = Stopwatch.StartNew();
        var initialCount = store.GetValueCount(groupKey, groupKeys[groupCount / 2]);
        buildTimer.Stop();
        ForceGc();
        var indexBytes = Math.Max(0, GC.GetTotalMemory(true) - beforeIndex);

        long checksum = initialCount;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var countHitTimer = Stopwatch.StartNew();
        for (var operation = 0; operation < queryOperations; operation++)
            checksum += store.GetValueCount(groupKey, groupKeys[(operation * 997) % groupCount]);
        countHitTimer.Stop();
        var countAllocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        var missingKey = keyFactory(groupCount + count + 1);
        var countMissTimer = Stopwatch.StartNew();
        for (var operation = 0; operation < queryOperations; operation++)
            checksum += store.GetValueCount(groupKey, missingKey);
        countMissTimer.Stop();

        var valuesOperations = Math.Clamp(queryOperations / 20, 1_000, 10_000);
        allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var valuesTimer = Stopwatch.StartNew();
        for (var operation = 0; operation < valuesOperations; operation++)
            checksum += store.GetValues(groupKey, groupKeys[(operation * 997) % groupCount]).Length;
        valuesTimer.Stop();
        var valuesAllocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        var keyOperations = Math.Clamp(2_000_000 / Math.Max(1, groupCount), 16, 2_000);
        var keysTimer = Stopwatch.StartNew();
        for (var operation = 0; operation < keyOperations; operation++)
            checksum += store.GetKeys(groupKey).Length;
        keysTimer.Stop();

        var keyCountTimer = Stopwatch.StartNew();
        for (var operation = 0; operation < keyOperations; operation++)
            checksum += store.GetKeyCount(groupKey);
        keyCountTimer.Stop();

        var mutationOperations = Math.Min(256, Math.Max(32, count / 8));
        allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var addTimer = Stopwatch.StartNew();
        for (var operation = 0; operation < mutationOperations; operation++)
        {
            var businessId = count + operation + 1;
            store.Add(new IndexRecord<TKey>
            {
                BusinessId = businessId,
                Group = groupKeys[(businessId * 31) % groupCount],
                Payload = businessId
            });
        }
        addTimer.Stop();
        var addAllocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        var nonKeyUpdateTimer = Stopwatch.StartNew();
        for (var operation = 0; operation < mutationOperations; operation++)
        {
            var id = new StoreRecordId(operation + 1L);
            if (!store.TryGetRecord(id, out var value)) throw new InvalidOperationException("Benchmark update record missing.");
            if (store.TryUpdate(value with { Payload = value.Payload + 1 }) != RecordUpdateResult.Updated)
                throw new InvalidOperationException("Benchmark non-key update failed.");
        }
        nonKeyUpdateTimer.Stop();

        var keyUpdateTimer = Stopwatch.StartNew();
        for (var operation = 0; operation < mutationOperations; operation++)
        {
            var id = new StoreRecordId(operation + 1L);
            if (!store.TryGetRecord(id, out var value)) throw new InvalidOperationException("Benchmark update record missing.");
            var updated = value with { Group = groupKeys[(operation + groupCount / 2 + 1) % groupCount] };
            if (store.TryUpdate(updated) != RecordUpdateResult.Updated)
                throw new InvalidOperationException("Benchmark key update failed.");
        }
        keyUpdateTimer.Stop();

        var deprecateTimer = Stopwatch.StartNew();
        for (var operation = 0; operation < mutationOperations; operation++)
            if (!store.TryDeprecate(new StoreRecordId(operation + 1L)))
                throw new InvalidOperationException("Benchmark deprecate failed.");
        deprecateTimer.Stop();

        checksum += store.GetKeyCount(groupKey);
        return new BenchmarkResult(
            buildTimer.Elapsed.TotalMicroseconds,
            countHitTimer.Elapsed.TotalNanoseconds / queryOperations,
            countMissTimer.Elapsed.TotalNanoseconds / queryOperations,
            valuesTimer.Elapsed.TotalNanoseconds / valuesOperations,
            keysTimer.Elapsed.TotalNanoseconds / keyOperations,
            keyCountTimer.Elapsed.TotalNanoseconds / keyOperations,
            addTimer.Elapsed.TotalNanoseconds / mutationOperations,
            nonKeyUpdateTimer.Elapsed.TotalNanoseconds / mutationOperations,
            keyUpdateTimer.Elapsed.TotalNanoseconds / mutationOperations,
            deprecateTimer.Elapsed.TotalNanoseconds / mutationOperations,
            indexBytes,
            (double)countAllocated / queryOperations,
            (double)valuesAllocated / valuesOperations,
            (double)addAllocated / mutationOperations,
            checksum);
    }

    private static void WarmUp()
    {
        _ = RunCase(32, 5, RecordStoreKeyIndexMode.SortedTable, 1_000, static value => value, null, null);
        _ = RunCase(32, 5, RecordStoreKeyIndexMode.Hash, 1_000, static value => value, null, null);
    }

    private static double P50(IEnumerable<BenchmarkResult> results, Func<BenchmarkResult, double> selector) =>
        Percentile(results, selector, 0.50);

    private static double P90(IEnumerable<BenchmarkResult> results, Func<BenchmarkResult, double> selector) =>
        Percentile(results, selector, 0.90);

    private static double Percentile(
        IEnumerable<BenchmarkResult> results,
        Func<BenchmarkResult, double> selector,
        double percentile)
    {
        var values = results.Select(selector).Order().ToArray();
        var index = (int)Math.Ceiling((values.Length - 1) * percentile);
        return values[index];
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private readonly record struct BenchmarkResult(
        double BuildMicroseconds,
        double CountHitNanoseconds,
        double CountMissNanoseconds,
        double ValuesHitNanoseconds,
        double KeysNanoseconds,
        double KeyCountNanoseconds,
        double AddNanoseconds,
        double NonKeyUpdateNanoseconds,
        double KeyUpdateNanoseconds,
        double DeprecateNanoseconds,
        long IndexBytes,
        double CountAllocatedBytes,
        double ValuesAllocatedBytes,
        double AddAllocatedBytes,
        long Checksum);

    private record struct IndexRecord<TKey> : IRecordStoreValue
    {
        public StoreRecordId StoreRecordId { get; set; }
        public int BusinessId { get; init; }
        public TKey Group { get; init; }
        public long Payload { get; init; }
    }
}

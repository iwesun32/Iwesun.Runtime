using Iwesun.Runtime.Data;


var targetMb = ReadIntArgument(args, "--target-mb", 128);
var batchSize = ReadIntArgument(args, "--batch-size", 25_000);
var recordTarget = ReadIntArgument(args, "--records", 0);
var mode = ReadStringArgument(args, "--mode", "publish");
var repetitions = ReadIntArgument(args, "--repetitions", 3);
var queryOperations = ReadIntArgument(args, "--queries", 50_000);
if (targetMb < 16) throw new ArgumentOutOfRangeException(nameof(targetMb), "target-mb must be at least 16.");
if (batchSize < 1_000) throw new ArgumentOutOfRangeException(nameof(batchSize), "batch-size must be at least 1000.");
if (mode is not ("publish" or "aggregate" or "hotpath" or "churn" or "index-benchmark"))
    throw new ArgumentOutOfRangeException(
        nameof(mode),
        "mode must be publish, aggregate, hotpath, churn, or index-benchmark.");

if (mode == "index-benchmark")
    RecordStoreIndexBenchmarkRunner.Run(repetitions, queryOperations);
else
    RecordStoreScaleRunner.Run(targetMb, batchSize, recordTarget, mode);

static int ReadIntArgument(string[] args, string name, int fallback)
{
    var index = Array.FindIndex(args, value => value.Equals(name, StringComparison.OrdinalIgnoreCase));
    return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var value) ? value : fallback;
}

static string ReadStringArgument(string[] args, string name, string fallback)
{
    var index = Array.FindIndex(args, value => value.Equals(name, StringComparison.OrdinalIgnoreCase));
    return index >= 0 && index + 1 < args.Length ? args[index + 1].ToLowerInvariant() : fallback;
}

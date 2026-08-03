namespace Iwesun.Runtime.SampleHost;

public sealed record SampleHostProfile
{
	public string Name { get; init; } = "Iwesun Runtime Sample Host";
	public string Scenario { get; init; } = "diagnostics-and-execution-sample";
	public TimeSpan CoordinatorInterval { get; init; } = TimeSpan.FromSeconds(3);
	public TimeSpan WorkerInterval { get; init; } = TimeSpan.FromMilliseconds(900);
	public TimeSpan MonitorInterval { get; init; } = TimeSpan.FromSeconds(2);
	public int BatchGate { get; init; } = 3;
	public int MaxBatches { get; init; } = 8;
	public bool EnableFailureInjection { get; init; } = true;

	public static SampleHostProfile CreateDefault() => new();
}

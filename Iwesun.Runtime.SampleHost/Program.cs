using System.Reflection;
using Iwesun.Runtime.Diagnostics;
using Iwesun.Runtime.SampleHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

[assembly: DiagnosticPipePrefix("Iwesun.SampleHost")]
[assembly: DiagnosticFileOutput(
	"logs/sample-host-diag.jsonl",
	FileWriteMode.CreateNew,
	Format = DiagnosticFileFormat.PlainText)]
[assembly: DiagnosticWatchPoint(
	"sample.host.profile",
	"sample-host",
	"profile",
	"Static host profile data.",
	"Iwesun.Runtime.SampleHost/SampleHostProfile.cs")]
[assembly: DiagnosticWatchPoint(
	"sample.host.session",
	"sample-host",
	"session",
	"Dynamic session data.",
	"Iwesun.Runtime.SampleHost/SampleHostState.cs")]
[assembly: DiagnosticWatchPoint(
	"sample.host.execution",
	"sample-host",
	"execution",
	"Execution manager snapshot.",
	"Iwesun.Runtime.SampleHost/SampleHostWorker.cs")]
[assembly: DiagnosticWatchPoint(
	"sample.host.loop",
	"sample-host",
	"tick",
	"Sample host loop heartbeat.",
	"Iwesun.Runtime.SampleHost/Program.cs")]
[assembly: DiagnosticWatchPoint(
	"sample.host.random",
	"sample-host",
	"random",
	"Periodic random sample.",
	"Iwesun.Runtime.SampleHost/SampleHostRandomState.cs")]
[assembly: DiagnosticBreakpoint(
	"sample.host.pause",
	"sample-host",
	"Pause before the simulated work step.",
	"Iwesun.Runtime.SampleHost/SampleHostWorker.cs")]
[assembly: DiagnosticBreakpoint(
	"sample.host.batch-gate",
	"sample-host",
	"Pause before a gated batch step.",
	"Iwesun.Runtime.SampleHost/SampleHostWorker.cs")]
[assembly: DiagnosticBreakpoint(
	"sample.host.random.initial-enabled",
	"sample-host",
	"Compiled enabled random breakpoint.",
	"Iwesun.Runtime.SampleHost/SampleHostWorker.cs",
	Enabled = true)]
[assembly: DiagnosticBreakpoint(
	"sample.host.random.dynamic",
	"sample-host",
	"Runtime-controlled random breakpoint.",
	"Iwesun.Runtime.SampleHost/SampleHostWorker.cs",
	Enabled = false)]
[assembly: DiagnosticBreakpoint(
	"sample.host.random.numeric",
	"sample-host",
	"Runtime-controlled numeric breakpoint.",
	"Iwesun.Runtime.SampleHost/SampleHostWorker.cs",
	Enabled = false)]
[assembly: DiagnosticNumericBreakpoint("sample.host.random.numeric", "gt", 90)]
[assembly: DiagnosticHookableEvent(
	"sample.host.state-changed",
	typeof(SampleHostState),
	nameof(SampleHostState.StateChanged))]
[assembly: DiagnosticHookableEvent(
	"sample.host.random-updated",
	typeof(SampleHostRandomState),
	nameof(SampleHostRandomState.Updated))]

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddRuntimeDiagnostics();
var runtimeDirectory = Path.Combine(AppContext.BaseDirectory, "runtime");
var runAsWindowsService = args.Contains("--windows-service", StringComparer.OrdinalIgnoreCase);
if (runAsWindowsService)
{
	builder.Services.StartWindowsService(
		serviceOptions: new RuntimeWindowsServiceOptions
		{
			ServiceName = "Iwesun.Runtime.SampleHost",
			DisplayName = "Iwesun Runtime Sample Host",
			Description = "Demonstrates Runtime-managed Windows Service startup and coordinated shutdown.",
			ShutdownTimeout = TimeSpan.FromSeconds(30)
		},
		runtimeDirectory: runtimeDirectory,
		startupRuntimeDiagnosticsPipeName: "Iwesun.SampleHost.RuntimeDiagnostics");
}
else
{
	builder.Services.Start(runtimeDirectory, startupRuntimeDiagnosticsPipeName: "Iwesun.SampleHost.RuntimeDiagnostics");
}
builder.Services.AddSingleton(SampleHostProfile.CreateDefault());
builder.Services.AddSingleton<SampleHostState>();
builder.Services.AddSingleton<SampleHostRandomState>();
builder.Services.AddHostedService<SampleHostWorker>();

using var host = builder.Build();

host.Services.Activate(Assembly.GetExecutingAssembly());

var hub = host.Services.GetRequiredService<RuntimeDiagnosticHub>();
var execution = host.Services.GetRequiredService<RuntimeExecutionManager>();
var profile = host.Services.GetRequiredService<SampleHostProfile>();
var state = host.Services.GetRequiredService<SampleHostState>();
var randomState = host.Services.GetRequiredService<SampleHostRandomState>();

RuntimeInjector.Thread(
	execution,
	"sample-host.coordinator",
	"Sample Host Coordinator",
	RuntimeExecutionLifetime.Static,
	RuntimeThreadKind.Coordinator,
	owner: nameof(Iwesun.Runtime.SampleHost),
	sourceLocation: "Iwesun.Runtime.SampleHost/SampleHostWorker.cs");

RuntimeInjector.Thread(
	execution,
	"sample-host.worker",
	"Sample Host Worker",
	RuntimeExecutionLifetime.Static,
	RuntimeThreadKind.Worker,
	owner: nameof(Iwesun.Runtime.SampleHost),
	sourceLocation: "Iwesun.Runtime.SampleHost/SampleHostWorker.cs");

RuntimeInjector.Thread(
	execution,
	"sample-host.monitor",
	"Sample Host Monitor",
	RuntimeExecutionLifetime.Static,
	RuntimeThreadKind.Monitor,
	owner: nameof(Iwesun.Runtime.SampleHost),
	sourceLocation: "Iwesun.Runtime.SampleHost/SampleHostWorker.cs");

RuntimeInjector.Task(
	execution,
	"sample-host.coordinator.loop",
	"Sample Host Coordinator Loop",
	RuntimeExecutionLifetime.Static,
	category: "lifecycle",
	threadId: "sample-host.coordinator",
	sourceLocation: "Iwesun.Runtime.SampleHost/SampleHostWorker.cs",
	step: "registered");

RuntimeInjector.Task(
	execution,
	"sample-host.worker.loop",
	"Sample Host Worker Loop",
	RuntimeExecutionLifetime.Static,
	category: "sample",
	threadId: "sample-host.worker",
	sourceLocation: "Iwesun.Runtime.SampleHost/SampleHostWorker.cs",
	step: "registered");

RuntimeInjector.Task(
	execution,
	"sample-host.monitor.loop",
	"Sample Host Monitor Loop",
	RuntimeExecutionLifetime.Static,
	category: "monitor",
	threadId: "sample-host.monitor",
	sourceLocation: "Iwesun.Runtime.SampleHost/SampleHostWorker.cs",
	step: "registered");

RuntimeInjector.Data(hub, "sample.host.profile", profile, new RuntimeDiagnosticObjectAccess
{
	AllowReadAllPublic = false,
	ReadableMembers = ["Name", "Scenario", "CoordinatorInterval", "WorkerInterval", "MonitorInterval", "BatchGate", "MaxBatches", "EnableFailureInjection"]
});

RuntimeInjector.Data(hub, "sample.host", state, new RuntimeDiagnosticObjectAccess
{
	AllowReadAllPublic = false,
	ReadableMembers = ["CurrentPhase", "Iteration", "IsPaused", "FaultRequested", "UpdatedAt", "Snapshot"],
	WritableMembers = ["IsPaused", "FaultRequested"],
	InvokableMembers = ["Advance", "Pause", "Resume", "Reset", "RequestFault", "ClearFault", "Snapshot"]
});

RuntimeInjector.Data(hub, "sample.host.session", state, new RuntimeDiagnosticObjectAccess
{
	AllowReadAllPublic = false,
	ReadableMembers = ["CurrentPhase", "Iteration", "IsPaused", "FaultRequested", "UpdatedAt", "Snapshot"],
	WritableMembers = ["IsPaused", "FaultRequested"],
	InvokableMembers = ["Advance", "Pause", "Resume", "Reset", "RequestFault", "ClearFault", "Snapshot"]
});

RuntimeInjector.Data(hub, "sample.host.random", randomState, new RuntimeDiagnosticObjectAccess
{
	AllowReadAllPublic = false,
	ReadableMembers = ["CurrentValue", "PreviousValue", "MinimumValue", "MaximumValue", "SampleCount", "UpdatedAt"],
	InvokableMembers = ["Snapshot", "RecordValidationSample"]
});

await host.RunAsync();

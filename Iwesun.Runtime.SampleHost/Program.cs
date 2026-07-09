using System.Reflection;
using Iwesun.Runtime.Diagnostics;
using Iwesun.Runtime.SampleHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

[assembly: DiagnosticPipePrefix("Iwesun.SampleHost")]
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
[assembly: DiagnosticHookableEvent(
	"sample.host.state-changed",
	typeof(SampleHostState),
	nameof(SampleHostState.StateChanged))]

var builder = Host.CreateApplicationBuilder(args);
var runtimeDirectory = Path.Combine(AppContext.BaseDirectory, "runtime");

builder.Services.Start(runtimeDirectory);
builder.Services.AddSingleton(SampleHostProfile.CreateDefault());
builder.Services.AddSingleton<SampleHostState>();
builder.Services.AddHostedService<SampleHostWorker>();

using var host = builder.Build();

host.Services.Activate(Assembly.GetExecutingAssembly());

var hub = host.Services.GetRequiredService<RuntimeDiagnosticHub>();
var execution = host.Services.GetRequiredService<RuntimeExecutionManager>();
var profile = host.Services.GetRequiredService<SampleHostProfile>();
var state = host.Services.GetRequiredService<SampleHostState>();

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

await host.RunAsync();
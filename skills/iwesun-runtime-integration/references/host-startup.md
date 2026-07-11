# Host startup

## Standard entry point

Use `Iwesun.Runtime.SampleHost/Program.cs` as the canonical executable example.

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddRuntimeDiagnostics();
builder.Services.Start(
    runtimeDirectory,
    startupRuntimeDiagnosticsPipeName: startupPipeName);

// Register business services here.

using var host = builder.Build();
host.Services.Activate(Assembly.GetExecutingAssembly());

// Register explicit reflection targets and static execution records here.

await host.RunAsync();
```

## Boundaries

- Fixed template: logging bridge, `Start`, `Build`, `Activate`, `RunAsync`.
- Host configuration: runtime directory, startup pipe name, startup file path.
- Business area: DI registrations, hosted workers, explicit reflection targets.
- Resolve startup pipe and file settings once. Do not mutate their names during normal runtime.

## Migration audit

Remove scattered host calls to `AddRuntimeDiagnostics`, `UseRuntimeDiagnostics`, and `BuildDiagnosticRegistries`. Their implementation remains internal to `Start` and `Activate`; they are not the recommended business entry points.

Register `RuntimeDiagnosticObjectAccess` after `Activate`, with explicit member lists. Never enable broad public invocation to shorten integration work.

## Shutdown ownership

The host owns `RuntimeShutdownCoordinator`. A shutdown command announces global Stop, sends FIFO Stop/Wakeup to each registered unit, waits for deregistration, and returns exit code 0 or timeout code 124. Do not terminate the host immediately after accepting the command.

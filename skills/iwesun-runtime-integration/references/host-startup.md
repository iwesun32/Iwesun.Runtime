# Host startup

## Standard entry point

Use `modules/Diagnostics/samples/Iwesun.Runtime.SampleHost/Program.cs` as the canonical executable example.

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddRuntimeDiagnostics();
var pipeAccess = new RuntimeNamedPipeAccessOptions
{
    AllowLocalInteractiveUsers = true,
    AllowAuthenticatedUsers = false,
    AllowedWindowsPrincipals = [$@"{Environment.MachineName}\IwesunAiDiag"]
};
builder.Services.Start(
    runtimeDirectory,
    startupRuntimeDiagnosticsPipeName: "Product.RuntimeDiagnostics",
    pipeAccessOptions: pipeAccess);

// Register business services here.

using var host = builder.Build();
host.Services.Activate(Assembly.GetExecutingAssembly());

// Register explicit reflection targets and static execution records here.

await host.RunAsync();
```

For a Windows Service, replace only `Start(...)` with the platform-owned service entry:

```csharp
builder.Services.StartWindowsService(
    serviceOptions: new RuntimeWindowsServiceOptions
    {
        ServiceName = "Company.ProductService",
        DisplayName = "Product Service",
        Description = "Business-owned service description.",
        ShutdownTimeout = TimeSpan.FromSeconds(30)
    },
    runtimeDirectory: runtimeDirectory,
    startupRuntimeDiagnosticsPipeName: "Product.RuntimeDiagnostics",
    pipeAccessOptions: pipeAccess);
```

Business services remain standard `IHostedService` or `BackgroundService`. Runtime owns SCM Stop/Shutdown, coordinated cleanup, exit codes, and deregistration. Do not call `AddWindowsService()` separately.

If service identity belongs in a business DI module, call `StartConfiguredWindowsService(...)` in the fixed Program.cs and `ConfigureRuntimeWindowsService(...)` anywhere before `Build()`. Runtime never supplies a default business ServiceName.

## Boundaries

The current model permits one Runtime host per process. Repeating `Start` with identical settings on the same `IServiceCollection` is idempotent; conflicting settings throw `RuntimeHostConfigurationException`. Repeating `Activate` on the same provider is idempotent, while a different provider fails fast without replacing the first static context.

- Fixed template: logging bridge, `Start`, `Build`, `Activate`, `RunAsync`.
- Host configuration: runtime directory, startup pipe name, startup file path.
- Business area: DI registrations, hosted workers, explicit reflection targets.
- Declare fixed startup pipe and file settings in source. `--diag-pipe` and `--diag-file` are the only temporary startup overrides.
- Runtime ignores legacy `diagnostic-switchboard.json` files and never writes them.
- Declare remote Windows accounts or groups through `RuntimeNamedPipeAccessOptions` in Program.cs. Keep `AllowLocalInteractiveUsers=true` for local CLI, disable `AllowAuthenticatedUsers` for strict remote access, and never put credentials in host source or Runtime JSON.

## Migration audit

Remove scattered host calls to `AddRuntimeDiagnostics`, `UseRuntimeDiagnostics`, and `BuildDiagnosticRegistries`. Their implementation remains internal to `Start` and `Activate`; they are not the recommended business entry points.

Register `RuntimeDiagnosticObjectAccess` after `Activate`, with explicit member lists. Never enable broad public invocation to shorten integration work.

## Shutdown ownership

The host owns `RuntimeShutdownCoordinator`. A shutdown command announces global Stop, sends FIFO Stop/Wakeup to each registered unit, waits for deregistration, and returns exit code 0 or timeout code 124. Do not terminate the host immediately after accepting the command.

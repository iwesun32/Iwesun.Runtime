# Iwesun Runtime - AI Agent Instructions

This file is the entry point for AI coding agents working in this repository.

## Full Instructions

The complete repository-wide instructions and safety boundaries live at:

- [copilot-instructions.md](copilot-instructions.md)
- [.github/instructions/copilot-access-rules.instructions.md](.github/instructions/copilot-access-rules.instructions.md)

The ignore boundary for Copilot is defined by:

- [.copilotignore](.copilotignore)

## Quick Start

- **Build**: `dotnet build Iwesun.Runtime.slnx -c Release`
- **Language**: C# (.NET 10), `LangVersion=latest`, `Nullable=enable`, `ImplicitUsings=enable`
- **Private fields**: `_camelCase` with underscore prefix
- **Logging**: `Microsoft.Extensions.Logging` structured templates (`_logger.LogInformation("...{PipeName}", _pipeName)`)
- **DI**: `Microsoft.Extensions.DependencyInjection` via extension methods (`AddRuntimeDiagnostics()`, `UseRuntimeDiagnostics()`)
- **Language policy**:
  - Code identifiers, comments, XML docs -> **English**
  - Design docs (`docs/`) -> **中文 (Chinese)**
  - AI instructions (`AGENTS.md`) -> **English**

## Critical Rules

0. **Never write or overwrite files from the terminal.** Use editor tools for every file edit or file creation.
1. **Never add `Console.WriteLine` or temporary log files** for diagnostics. Use `RuntimeOutput.TracePoint()` / `DiagnosticSwitchboard` exclusively. This is the core design principle.
2. **Default state must remain silent** - all sections, points, and outputs default to `false`. Always restore with `quiet` after use.
3. **Reflection targets must be explicitly registered** with `RuntimeDiagnosticObjectAccess` whitelists. Never expose broad `InvokableMembers` - security constraint.
4. **Schema versioning**: When adding/changing output points in `DiagnosticSwitchboardCompiledConfig`, bump `SchemaVersion`. Otherwise existing configs will be silently reset by `MergeWithCompiledDefaults`.
5. **Pipe protocol**: 4-byte little-endian length-prefixed JSON. The CLI handles framing - do not hand-roll pipe clients.
6. **Defensive execution** - skip invalid input gracefully; don't crash the whole process.
7. **Do not hardcode pipe names** - use `DiagnosticPipePrefix.Resolve(channel)` or `DiagnosticSwitchboardCompiledConfig.DefaultRuntimeDiagnosticsPipeName`.
8. **Breakpoints are collaborative** - `RuntimeOutput.BreakIf()` uses `await` (not thread suspension). Only the calling call-chain pauses; other threads run freely. CLI disconnect auto-resumes all breakpoints.
9. **Event hooks use weak references** - instance event hooks use `WeakReference<T>` to avoid blocking GC. Static events must be explicitly detached.
10. **Use `#if DEBUG` for breakpoint/watch injection** - `BreakIf` and `Watch` calls should be wrapped in `#if DEBUG` for production builds. `TracePoint`/`Log` are production-safe.

## Five Diagnostic Modules

| Module            | API                                     | Status        | Production    |
| ----------------- | --------------------------------------- | ------------- | ------------- |
| Data output       | `RuntimeOutput.TracePoint()` / `Log()`  | ✅ Implemented | ✅ Yes         |
| Breakpoints       | `RuntimeOutput.BreakIf()`               | ✅ Implemented | ❌ `#if DEBUG` |
| Object reflection | `navigate` action on reflection targets | ✅ Implemented | ✅ Read-only   |
| Event hooks       | `RuntimeDiagnosticHooks.Attach()`       | ✅ Implemented | ❌ `#if DEBUG` |
| Program shutdown  | `shutdown` action on switchboard target | ✅ Implemented | ✅ Yes         |

## Four Core Registries

| Registry      | Built from                            | Query via                               |
| ------------- | ------------------------------------- | --------------------------------------- |
| Watch points  | `[assembly: DiagnosticWatchPoint]`    | `diagnostics.registry` action           |
| Breakpoints   | `[assembly: DiagnosticBreakpoint]`    | `diagnostics.breakpoints` target        |
| Event hooks   | `[assembly: DiagnosticHookableEvent]` | `diagnostics.hooks` target              |
| Pipe channels | `DiagnosticPipePrefix` at runtime     | `DiagnosticPipePrefix.ControlPipe` etc. |

## Host Integration (standardized template)

`RuntimeHostTemplate` (in `RuntimeHostTemplate.cs`) provides the fixed host-side startup/shutdown flow as extension methods. Prefer this over calling `AddRuntimeDiagnostics`/`UseRuntimeDiagnostics`/`BuildDiagnosticRegistries` directly. See `Iwesun.Runtime.SampleHost/Program.cs` for the canonical example.

```csharp
// Program.cs
var builder = Host.CreateApplicationBuilder(args);
builder.Services.Start(runtimeDirectory);          // registers diagnostics DI
using var host = builder.Build();
host.Services.Activate(Assembly.GetExecutingAssembly()); // starts pipe + builds registries
host.Run();
// On shutdown: RuntimeHostTemplate.Stop(stateManager, execution, threadId, taskId, graceful: true);
```

**Two-layer model**:
- **Fixed host template layer** — `RuntimeHostTemplate.Start` / `Activate` / `Stop` (owned by the host, boilerplate).
- **Business annotation/injection layer** — `RuntimeInjector.Output` / `Watch` / `Break` / `Data` / `Thread` / `Task` (thin static facade over `RuntimeOutput` + `RuntimeExecutionManager`; low-intrusion for business code).

## Managed Execution Wrappers

`RProcess` / `RThread` / `RTask` (in `Iwesun.Runtime.Diagnostics`) wrap the standard runtime primitives and auto-register with `RuntimeExecutionManager`. They give framework-supplied coarse-grained state (`Working`/`Stop`) plus business-added fine-grained state via `IRManagedState` (`RManagedState`): `SetDetail` / `TryGetDetail` / `TransitionTo` / `TryTransitionTo`.

| Wrapper    | Base / composition                                                                       | Notes                                            |
| ---------- | ---------------------------------------------------------------------------------------- | ------------------------------------------------ |
| `RProcess` | inherits `System.Diagnostics.Process`                                                    | Pushes task-state on `Start()` / `Exited`.       |
| `RTask`    | inherits `System.Threading.Tasks.Task`                                                   | Registers with category / threadId / lifetime.   |
| `RThread`  | `sealed class`, wraps inner `System.Threading.Thread` (composition — `Thread` is sealed) | Registers with lifetime / kind / owner metadata. |

> All three live in namespace `Iwesun.Runtime.Diagnostics` (not `System.*`). State foundations: `RuntimeState`, `RuntimeStateCatalog`, `RuntimeStateManager`; execution catalog exposed via the `runtime.execution` reflection target.

## Project Structure

```text
Iwesun.Runtime.Diagnostics/  -> Core diagnostics library + host template + managed wrappers (referenced by DDNS Snap)
Iwesun.Runtime.SampleHost/   -> Standalone Exe demonstrating the standard startup/shutdown injection template
Iwesun.Runtime.WebView2/     -> WebRuntime pipe client models (referenced only by Cli)
Iwesun.Runtime.Cli/          -> Standalone CLI tool (not referenced by DDNS Snap)
```

**Dependency graph**:

```text
Iwesun.Runtime.Diagnostics  (no internal deps)
Iwesun.Runtime.WebView2     (no internal deps)
Iwesun.Runtime.SampleHost   -> Diagnostics
Iwesun.Runtime.Cli          -> Diagnostics + WebView2
```

> No unit test project exists in the solution. Validate changes with focused `dotnet build`.

## Architecture

**Iwesun.Runtime.Diagnostics** is the core library. Data flow:

```text
Business code (RuntimeOutput.TracePoint)
  -> RuntimeOutputSwitch (volatile bool fast-path gate)
  -> DiagnosticSwitchboard (static, config-driven router)
  -> FIFO string queue
  -> Background pump (PumpAsync)
  -> RuntimeDiagnosticHub (event/command center)
  -> RuntimeDiagnosticsMonitor (BackgroundService, named pipe server)
```

Key classes:

- `RuntimeOutput` - static facade: `Log`, `Trace`, `TracePoint`, `Error`. Business code entry point.
- `DiagnosticSwitchboard` - static config-driven router with FIFO queue, section/point filtering, snapshots.
- `DiagnosticSwitchboardConfig` / `DiagnosticSwitchboardCompiledConfig` - config POCO + compiled defaults (schema v4, 15 sections).
- `RuntimeDiagnosticHub` - central registry for targets, events, commands, host scanning.
- `RuntimeDiagnosticsMonitor` - `BackgroundService` hosting the named pipe server.
- `RuntimeDiagnosticLoggerProvider` - bridges `ILogger` events to the switchboard.
- `IRuntimeDiagnosticTarget` / `ReflectionRuntimeDiagnosticTarget` - diagnostic target pattern for reflective access.

**Iwesun.Runtime.Cli** is a standalone command-line tool (top-level statements in `Program.cs`). It parses short verbs into JSON commands via regex patterns from `Iwesun.Runtime.Cli.commands.v2.json`, sends them over the named pipe, and returns JSON results. Dispatch order: shell -> configured (regex) -> webview2 -> composite -> diagnostics.

**Iwesun.Runtime.WebView2** provides shared WebRuntime control models and a named pipe client (`AIGateway.WebRuntime` pipe). Only consumed by the CLI; DDNS Snap does not host WebView2 sessions.

## Main APIs Consumed by Hosts

Hosts (like DDNS Snap) consume Diagnostics via:

```csharp
// DI registration
services.AddRuntimeDiagnostics(runtimeDirectory);
provider.UseRuntimeDiagnostics();
loggingBuilder.AddRuntimeDiagnostics();

// Business code tracing
RuntimeOutput.TracePoint("pipeline.stage", "DnsUpdateStage", "message", new { Domain, TargetIp });
```

## Coding Conventions

- **Models**: `sealed record` for snapshots; `sealed class` with `init` properties and `= ""` defaults for commands/results/events.
- **Static state**: `ConcurrentDictionary`, `Interlocked`, `volatile`, `SemaphoreSlim`.
- **Null handling**: `ArgumentNullException.ThrowIfNull()` for constructor dependencies.
- **Async**: `Task`/`async` everywhere; accept `CancellationToken`. Pipe I/O uses `PipeOptions.Asynchronous`.

## Documentation

All docs are in Chinese under `docs/`:

- [docs/README.md](docs/README.md) - 文档索引
- [RUNTIME_DIAGNOSTICS.md](docs/RUNTIME_DIAGNOSTICS.md) - 诊断框架核心组件、数据流、配置
- [IWESUN_RUNTIME_CLI.md](docs/IWESUN_RUNTIME_CLI.md) - CLI 命令参考

## Relationship to DDNS Snap

This repository is referenced by the DDNS Snap project (at `D:\Git Space\Ddns Snap`) via cross-repo `ProjectReference`. DDNS Snap passes `C:\ProgramData\DdnsSnap` as the `runtimeDirectory` and uses the pipe name `DdnsSnap.RuntimeDiagnostics` (compiled default). The DDNS Snap repo contains a [runtime-diagnostics.instructions.md](../Ddns%20Snap/.github/instructions/runtime-diagnostics.instructions.md) that covers usage from the consumer side.

DDNS Snap may keep usage guidance for Runtime, but Runtime implementation rules and AI instruction boundaries belong in this repository.

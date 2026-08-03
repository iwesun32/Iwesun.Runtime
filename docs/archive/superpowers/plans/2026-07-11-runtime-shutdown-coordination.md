# Runtime Coordinated Shutdown Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a unified shutdown coordinator that combines global lifecycle state polling with per-unit FIFO Stop commands, waits for all process/thread/task registrations to drain, and returns exit code 0 or timeout code 124.

**Architecture:** `RuntimeShutdownCoordinator` owns the single-flight shutdown request, global state transition, targeted FIFO broadcast, countdown, and registry drain check. `RuntimeUnitGuardian` is composed into managed process/thread/task wrappers and normalizes global-state polling plus FIFO commands into one idempotent cleanup/unregister flow. CLI and host shutdown routes call the coordinator; `StopApplication()` runs only after coordination completes.

**Tech Stack:** C# 14, .NET 10 Generic Host, Microsoft.Extensions.DependencyInjection, RuntimeManagedRegistry, RuntimeStateManager, RuntimeDList, existing named-pipe CLI protocol.

---

## File map

- Create `Iwesun.Runtime.Diagnostics/RuntimeShutdownModels.cs`: shutdown command enum, request, snapshot, unit residue, result and exit-code constants.
- Create `Iwesun.Runtime.Diagnostics/RuntimeUnitGuardian.cs`: per-unit dual-channel stop detection and idempotent cleanup lifecycle.
- Create `Iwesun.Runtime.Diagnostics/RuntimeShutdownCoordinator.cs`: global shutdown orchestration, FIFO fan-out, countdown and registry drain.
- Modify `Iwesun.Runtime.Diagnostics/RuntimeManagedRegistry.cs`: reject or immediately stop registrations after shutdown begins; expose active-unit snapshot and change notification.
- Modify `Iwesun.Runtime.Diagnostics/RuntimeManagedCommandTarget.cs`: route lifecycle shutdown/status/broadcast to the coordinator.
- Modify `Iwesun.Runtime.Diagnostics/RuntimeDiagnosticsServiceCollectionExtensions.cs`: register and wire coordinator/guardian dependencies.
- Modify `Iwesun.Runtime.Diagnostics/DiagnosticSwitchboardTarget.cs`: replace direct StopApplication shutdown path with coordinator request.
- Modify `Iwesun.Runtime.Diagnostics/RProcess.cs`, `RThread.cs`, `RTask.cs`: use the Guardian contract for stop polling, cleanup state and unregister.
- Modify `Iwesun.Runtime.Diagnostics/RuntimeHostTemplate.cs`: expose coordinated stop and map result to process exit code.
- Modify `Iwesun.Runtime.SampleHost/Program.cs`, `SampleHostWorker.cs`: use coordinated host shutdown and demonstrate stage states.
- Modify `Iwesun.Runtime.FunctionalTests/Program.cs`: add focused in-process guardian/coordinator scenarios.
- Create `Iwesun.Runtime.FunctionalTests/ShutdownCoordinationScenario.cs`: real SampleHost + CLI normal and timeout exit scenarios.
- Modify `docs/05-runtime-tooling/RUNTIME_INTEGRATION_GUIDE.md`, `docs/IWESUN_RUNTIME_CLI.md`, `docs/REQUIREMENTS_ACTIVE.md`: document the final protocol and evidence.

### Task 1: Shutdown protocol models

**Files:**
- Create: `Iwesun.Runtime.Diagnostics/RuntimeShutdownModels.cs`
- Modify: `Iwesun.Runtime.FunctionalTests/Program.cs`
- Test: `Iwesun.Runtime.FunctionalTests/Program.cs` scenario `shutdown-models`

- [ ] **Step 1: Add the failing model scenario**

Register `shutdown-models` and assert the stable system contract:

```csharp
var request = RuntimeShutdownRequest.Create(TimeSpan.FromSeconds(10));
Assert(request.RequestId != Guid.Empty, "shutdown-request-id");
Assert(request.Deadline > request.RequestedAt, "shutdown-deadline");
Assert(RuntimeShutdownExitCodes.Success == 0, "shutdown-success-code");
Assert(RuntimeShutdownExitCodes.Timeout == 124, "shutdown-timeout-code");
Assert(Enum.GetNames<RuntimeShutdownCommand>().SequenceEqual(["None", "Wake", "Stop"]), "shutdown-command-contract");
```

- [ ] **Step 2: Run RED verification**

Run:

```powershell
dotnet build Iwesun.Runtime.FunctionalTests/Iwesun.Runtime.FunctionalTests.csproj -c Debug --no-restore
```

Expected: compile failure because `RuntimeShutdownRequest`, `RuntimeShutdownExitCodes`, and `RuntimeShutdownCommand` do not exist.

- [ ] **Step 3: Add the protocol models**

Create these public contracts:

```csharp
namespace Iwesun.Runtime.Diagnostics;

public enum RuntimeShutdownCommand { None = 0, Wake = 1, Stop = 2 }

public static class RuntimeShutdownExitCodes
{
    public const int Success = 0;
    public const int Timeout = 124;
}

public sealed record RuntimeShutdownRequest(
    Guid RequestId, DateTimeOffset RequestedAt, DateTimeOffset Deadline)
{
    public static RuntimeShutdownRequest Create(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        var now = DateTimeOffset.UtcNow;
        return new(Guid.NewGuid(), now, now.Add(timeout));
    }
}

public sealed record RuntimeShutdownUnitResidue(
    string UnitId, RuntimeManagedUnitKind Kind, string State,
    IReadOnlyList<RuntimeState> StageStates, DateTimeOffset LastHeartbeat,
    string? CleanupError, TimeSpan Waited);

public sealed record RuntimeShutdownSnapshot(
    Guid? RequestId, string GlobalState, DateTimeOffset? Deadline,
    TimeSpan Remaining, int ActiveProcesses, int ActiveThreads, int ActiveTasks,
    IReadOnlyList<RuntimeShutdownUnitResidue> Residues);

public sealed record RuntimeShutdownResult(
    Guid RequestId, int ExitCode, bool TimedOut, TimeSpan Elapsed,
    IReadOnlyList<RuntimeShutdownUnitResidue> Residues);
```

Use the existing managed-unit kind type if its current name differs; do not introduce a duplicate enum.

- [ ] **Step 4: Run GREEN verification**

Run the `shutdown-models` child scenario. Expected: success with all five checks.

- [ ] **Step 5: Commit**

```powershell
git add Iwesun.Runtime.Diagnostics/RuntimeShutdownModels.cs Iwesun.Runtime.FunctionalTests/Program.cs
git commit -m "feat: define runtime shutdown protocol"
```

### Task 2: Registry drain contract and shutdown registration gate

**Files:**
- Modify: `Iwesun.Runtime.Diagnostics/RuntimeManagedRegistry.cs`
- Modify: `Iwesun.Runtime.FunctionalTests/Program.cs`
- Test: scenario `shutdown-registry`

- [ ] **Step 1: Add failing registry tests**

Test four behaviors:

```csharp
var registry = new RuntimeManagedRegistry();
var changes = 0;
registry.Changed += (_, _) => changes++;
Assert(registry.TryRegister(registration), "register-before-shutdown");
registry.BeginShutdown(request.RequestId);
Assert(!registry.TryRegister(lateRegistration), "reject-register-after-shutdown");
Assert(registry.SnapshotActive().Count == 1, "active-snapshot");
Assert(registry.Unregister(registration.UnitId), "shutdown-unregister");
Assert(registry.SnapshotActive().Count == 0 && changes >= 2, "registry-drained-event");
```

- [ ] **Step 2: Run RED verification**

Expected: compile failure for `Changed`, `BeginShutdown`, `TryRegister`, and `SnapshotActive`.

- [ ] **Step 3: Implement the gate**

Add an atomic shutdown request ID, a `Changed` event, and these APIs:

```csharp
public bool IsShutdownRequested => Volatile.Read(ref _shutdownRequested) != 0;
public Guid? ShutdownRequestId { get; private set; }
public event EventHandler? Changed;

public bool BeginShutdown(Guid requestId)
{
    if (Interlocked.Exchange(ref _shutdownRequested, 1) != 0) return false;
    ShutdownRequestId = requestId;
    Changed?.Invoke(this, EventArgs.Empty);
    return true;
}

public bool TryRegister(RuntimeManagedRegistration registration)
{
    if (IsShutdownRequested) return false;
    var added = RegisterCore(registration);
    if (added) Changed?.Invoke(this, EventArgs.Empty);
    return added;
}

public IReadOnlyList<RuntimeManagedRegistration> SnapshotActive() =>
    SnapshotRegistrations().Where(x => x.IsActive).ToArray();
```

Adapt `RegisterCore` and active detection to the existing registration model. Existing registration callers must receive a deterministic rejection rather than silently creating a late unit.

- [ ] **Step 4: Run GREEN verification and existing managed scenario**

Expected: both `shutdown-registry` and `managed` succeed.

- [ ] **Step 5: Commit**

```powershell
git add Iwesun.Runtime.Diagnostics/RuntimeManagedRegistry.cs Iwesun.Runtime.FunctionalTests/Program.cs
git commit -m "feat: gate managed registration during shutdown"
```

### Task 3: Dual-channel unit Guardian

**Files:**
- Create: `Iwesun.Runtime.Diagnostics/RuntimeUnitGuardian.cs`
- Modify: `Iwesun.Runtime.Diagnostics/RProcess.cs`
- Modify: `Iwesun.Runtime.Diagnostics/RThread.cs`
- Modify: `Iwesun.Runtime.Diagnostics/RTask.cs`
- Modify: `Iwesun.Runtime.FunctionalTests/Program.cs`
- Test: scenario `shutdown-guardian`

- [ ] **Step 1: Add failing Guardian tests**

Build two Guardians over fake cleanup callbacks:

```csharp
var cleanupCount = 0;
var guardian = new RuntimeUnitGuardian(
    unitId, stateManager, managedRegistry,
    () => Volatile.Read(ref globalStop),
    () => managedRegistry.TryDequeueCommand(unitId, out var frame) ? frame : null,
    _ => { cleanupCount++; return ValueTask.CompletedTask; });

globalStop = true;
var first = await guardian.CheckAndStopAsync();
var second = await guardian.CheckAndStopAsync();
Assert(first.StopRequested && second.StopRequested, "global-stop-observed");
Assert(cleanupCount == 1, "cleanup-idempotent");
Assert(!managedRegistry.SnapshotActive().Any(x => x.UnitId == unitId), "guardian-unregistered");
```

Repeat with global state false and one FIFO `Stop`; assert FIFO-driven cleanup. Send duplicate Stop and assert cleanup remains one.

- [ ] **Step 2: Run RED verification**

Expected: compile failure because `RuntimeUnitGuardian` does not exist.

- [ ] **Step 3: Implement Guardian state flow**

Create a focused class with an atomic single-flight cleanup task:

```csharp
public sealed class RuntimeUnitGuardian
{
    public ValueTask<RuntimeUnitStopObservation> CheckAndStopAsync(CancellationToken ct = default);
    public Task<RuntimeUnitStopResult> RequestStopAsync(Guid requestId, CancellationToken ct = default);
}

public sealed record RuntimeUnitStopObservation(bool StopRequested, RuntimeShutdownCommand Command);
public sealed record RuntimeUnitStopResult(bool Completed, string? Error);
```

`CheckAndStopAsync` checks the global Stop anchor first, then drains this unit's FIFO until it finds Stop or Wake. `RequestStopAsync` uses `Interlocked.CompareExchange` around one cleanup task and performs exactly:

```text
Transition StopRequested
Transition StopDraining
invoke cleanup
Transition StopCompleted (or record cleanup error)
unregister reflection target and managed registration
return result
```

Expose the same terminal operation through `Dispose`/`DisposeAsync`. Normal completion, coordinated Stop, and explicit destruction must share one atomic terminal task and one unregister gate. A never-started wrapper must still undo constructor-time registration. Do not run asynchronous cleanup from a finalizer; a finalizer may only emit the existing leak diagnostic signal.

Unknown commands are skipped defensively. Cancellation before cleanup begins is honored; once cleanup begins, the unregister `finally` path always runs.

- [ ] **Step 4: Compose Guardian into wrappers**

- `RThread` polls Guardian in its existing command/guardian loop and maps completed stop to exit code 0.
- `RProcess` guardian polling sends the configured graceful process stop signal, waits for exit, then unregisters.
- `RTask` checks Guardian at managed task boundaries and uses its completion continuation for final unregister.
- Preserve existing `ExitHandlingRequested` and `ExitCompleted` events by raising them from the Guardian cleanup path.
- Implement `IDisposable`/`IAsyncDisposable` consistently on RProcess, RThread, and RTask where the base type permits it; repeated calls are no-ops after the shared terminal task completes.

- [ ] **Step 5: Run focused and existing wrapper tests**

Run `shutdown-guardian`, `thread`, `task`, and `process`. Add never-started dispose, running dispose, and repeated dispose cases for all three wrappers. Expected: all pass; cleanup counters and unregister counters are one; no registrations or reflection targets remain.

- [ ] **Step 6: Commit**

```powershell
git add Iwesun.Runtime.Diagnostics/RuntimeUnitGuardian.cs Iwesun.Runtime.Diagnostics/RProcess.cs Iwesun.Runtime.Diagnostics/RThread.cs Iwesun.Runtime.Diagnostics/RTask.cs Iwesun.Runtime.FunctionalTests/Program.cs
git commit -m "feat: add dual-channel runtime guardians"
```

### Task 4: Shutdown coordinator

**Files:**
- Create: `Iwesun.Runtime.Diagnostics/RuntimeShutdownCoordinator.cs`
- Modify: `Iwesun.Runtime.Diagnostics/RuntimeDiagnosticsServiceCollectionExtensions.cs`
- Modify: `Iwesun.Runtime.FunctionalTests/Program.cs`
- Test: scenarios `shutdown-coordinator-success`, `shutdown-coordinator-timeout`

- [ ] **Step 1: Add failing success scenario**

Register three fake managed units, attach cleanup Guardians, call:

```csharp
var result = await coordinator.ShutdownAsync(TimeSpan.FromSeconds(5));
Assert(result.ExitCode == 0 && !result.TimedOut, "coordinator-success-code");
Assert(registry.SnapshotActive().Count == 0, "coordinator-registry-empty");
Assert(stateManager.CurrentState.Key == RuntimeStateKey.StopCompleted, "global-stop-completed");
Assert(cleanupCounts.Values.All(x => x == 1), "all-cleanups-once");
```

- [ ] **Step 2: Add failing timeout scenario**

Register one residue that never unregisters and call with 250 ms. Assert exit code 124, `TimedOut=true`, global `StopTimeout`, and residue identity/state/heartbeat.

- [ ] **Step 3: Run RED verification**

Expected: compile failure because `RuntimeShutdownCoordinator` does not exist.

- [ ] **Step 4: Implement coordinator single-flight orchestration**

Public surface:

```csharp
public sealed class RuntimeShutdownCoordinator
{
    public Task<RuntimeShutdownResult> ShutdownAsync(TimeSpan timeout, CancellationToken ct = default);
    public RuntimeShutdownSnapshot Snapshot();
}
```

Implementation requirements:

- Protect `_shutdownTask` with a lock; concurrent callers share it.
- Create request and call `registry.BeginShutdown` before taking the first active snapshot.
- Set global state by stable keys: `StopRequested`, later `StopCompleted` or `StopTimeout`.
- For every active registration enqueue `RuntimeShutdownCommand.Stop` with request ID; enqueue `Wake` when the unit declares a wake capability.
- Repeat fan-out only for still-active units, at a bounded 100 ms interval.
- Subscribe to `registry.Changed` and use `TaskCompletionSource` to wake immediately on unregister.
- Build residues from active registration and unit state snapshots.
- Return 0 only when all process/thread/task registrations are empty.
- Return 124 at the deadline without killing OS processes.

- [ ] **Step 5: Register coordinator in DI**

Add singleton registration after Registry and state services. Ensure no dependency cycle: coordinator depends on Registry/StateManager; targets depend on coordinator.

- [ ] **Step 6: Run GREEN verification**

Run both coordinator scenarios three consecutive times to detect timing races. Expected: success case always 0; timeout always 124 with one residue.

- [ ] **Step 7: Commit**

```powershell
git add Iwesun.Runtime.Diagnostics/RuntimeShutdownCoordinator.cs Iwesun.Runtime.Diagnostics/RuntimeDiagnosticsServiceCollectionExtensions.cs Iwesun.Runtime.FunctionalTests/Program.cs
git commit -m "feat: coordinate managed runtime shutdown"
```

### Task 5: Lifecycle target, switchboard and CLI integration

**Files:**
- Modify: `Iwesun.Runtime.Diagnostics/RuntimeManagedCommandTarget.cs`
- Modify: `Iwesun.Runtime.Diagnostics/DiagnosticSwitchboardTarget.cs`
- Modify: `Iwesun.Runtime.Diagnostics/RuntimeDiagnosticHub.cs`
- Modify: `Iwesun.Runtime.Cli/Iwesun.Runtime.Cli.commands.v2.json`
- Modify: `Iwesun.Runtime.FunctionalTests/Program.cs`
- Test: scenario `shutdown-command-routing`

- [ ] **Step 1: Add failing command-routing tests**

Assert:

- `lifecycle.shutdown 5000` starts the coordinator and returns request ID/deadline.
- `lifecycle.status` returns active counts and remaining time.
- `lifecycle.broadcast Stop` sends targeted commands but does not create a second shutdown request.
- switchboard `shutdown` delegates to the same coordinator.
- unknown lifecycle command fails defensively.

- [ ] **Step 2: Run RED verification**

Expected: current shutdown returns before registry drain or directly calls application lifetime.

- [ ] **Step 3: Route all shutdown commands to the coordinator**

Inject `RuntimeShutdownCoordinator` into both command targets. Return a structured accepted response immediately only if protocol semantics require pipe response before the process closes; otherwise await the coordinator result. Use one shared request ID and expose final result through status.

Remove static shutdown event/delegate as the required execution path. Keep a compatibility event only as a post-acceptance notification if existing consumers need it; it must not decide whether the process exits.

- [ ] **Step 4: Update CLI definitions**

Keep canonical commands:

```text
lifecycle.shutdown [countdownMs]
lifecycle.status
lifecycle.broadcast <Stop|Wake>
safe-shutdown [countdownMs]
```

`safe-shutdown` maps to `lifecycle.shutdown`; it must not append breakpoint resume commands or rely on CLI disconnect behavior.

- [ ] **Step 5: Run GREEN routing verification**

Expected: all command routes share one coordinator request and status snapshot.

- [ ] **Step 6: Commit**

```powershell
git add Iwesun.Runtime.Diagnostics/RuntimeManagedCommandTarget.cs Iwesun.Runtime.Diagnostics/DiagnosticSwitchboardTarget.cs Iwesun.Runtime.Diagnostics/RuntimeDiagnosticHub.cs Iwesun.Runtime.Cli/Iwesun.Runtime.Cli.commands.v2.json Iwesun.Runtime.FunctionalTests/Program.cs
git commit -m "feat: route shutdown through runtime coordinator"
```

### Task 6: Host completion and process exit-code mapping

**Files:**
- Modify: `Iwesun.Runtime.Diagnostics/RuntimeHostTemplate.cs`
- Modify: `Iwesun.Runtime.SampleHost/Program.cs`
- Modify: `Iwesun.Runtime.SampleHost/SampleHostWorker.cs`
- Test: `Iwesun.Runtime.FunctionalTests/ShutdownCoordinationScenario.cs`

- [ ] **Step 1: Add real-host normal-exit test**

Start Debug SampleHost with a unique pipe, issue `safe-shutdown 10000`, and assert:

- global state reaches StopRequested;
- coordinator/worker/monitor registrations drain;
- SampleHost exits within ten seconds;
- OS exit code is 0;
- no process remains.

- [ ] **Step 2: Run RED verification**

Expected: current host lifetime may exit before registration drain or bypass the coordinator.

- [ ] **Step 3: Implement host completion mapping**

Add a coordinated stop API:

```csharp
public static async Task<int> StopAsync(
    IServiceProvider services,
    TimeSpan timeout,
    CancellationToken ct = default)
{
    var coordinator = services.GetRequiredService<RuntimeShutdownCoordinator>();
    var result = await coordinator.ShutdownAsync(timeout, ct);
    services.GetRequiredService<IHostApplicationLifetime>().StopApplication();
    return result.ExitCode;
}
```

SampleHost top-level program captures this result and assigns `Environment.ExitCode`. Do not call `StopApplication()` before coordinator completion.

- [ ] **Step 4: Add SampleHost Guardian cleanup stages**

In each of the three loops, use one continuous Guardian block to append a meaningful draining stage, stop the loop, complete cleanup and unregister. Preserve the existing periodic random behavior before shutdown.

- [ ] **Step 5: Run GREEN normal-exit test**

Expected: command acknowledged, registrations drain, process exits 0.

- [ ] **Step 6: Commit**

```powershell
git add Iwesun.Runtime.Diagnostics/RuntimeHostTemplate.cs Iwesun.Runtime.SampleHost/Program.cs Iwesun.Runtime.SampleHost/SampleHostWorker.cs Iwesun.Runtime.FunctionalTests/ShutdownCoordinationScenario.cs Iwesun.Runtime.FunctionalTests/Program.cs
git commit -m "feat: complete coordinated host shutdown"
```

### Task 7: Real timeout and race validation

**Files:**
- Modify: `Iwesun.Runtime.SampleHost/Program.cs`
- Modify: `Iwesun.Runtime.FunctionalTests/ShutdownCoordinationScenario.cs`
- Modify: `Iwesun.Runtime.FunctionalTests/Program.cs`

- [ ] **Step 1: Add an explicit test-only residue switch**

Parse `--shutdown-test-residue=true` only in SampleHost. Register a unit that records heartbeat but deliberately does not unregister. Do not enable this behavior by default.

- [ ] **Step 2: Add real timeout scenario**

Start SampleHost with the residue switch, issue `safe-shutdown 500`, and assert:

- lifecycle result is timeout;
- residue report contains the test UnitId and last state;
- process exit code is 124;
- test cleanup forcibly terminates only if the process fails to honor its own timeout result.

- [ ] **Step 3: Add duplicate and lost-command scenarios**

- Duplicate: send two concurrent shutdown commands and assert one request ID and cleanup count one.
- Lost FIFO: suppress Stop delivery for one Guardian and assert global state polling still drains it.
- Late registration: attempt registration after StopRequested and assert rejection.

- [ ] **Step 4: Run all shutdown scenarios repeatedly**

Run each five times. Expected: stable 0/124 codes, no hangs, no registrations or OS processes left after test cleanup.

- [ ] **Step 5: Commit**

```powershell
git add Iwesun.Runtime.SampleHost/Program.cs Iwesun.Runtime.FunctionalTests/ShutdownCoordinationScenario.cs Iwesun.Runtime.FunctionalTests/Program.cs
git commit -m "test: validate shutdown timeout and races"
```

### Task 8: Full regression and documentation

**Files:**
- Modify: `docs/05-runtime-tooling/RUNTIME_INTEGRATION_GUIDE.md`
- Modify: `docs/IWESUN_RUNTIME_CLI.md`
- Modify: `docs/REQUIREMENTS_ACTIVE.md`

- [ ] **Step 1: Run Debug solution build**

```powershell
dotnet build Iwesun.Runtime.slnx -c Debug --no-restore -t:Rebuild
```

Expected: 0 errors. Record warning count exactly.

- [ ] **Step 2: Run Release solution build**

```powershell
dotnet build Iwesun.Runtime.slnx -c Release --no-restore -t:Rebuild
```

Expected: 0 errors. Confirm shutdown types exist while Debug-only breakpoint implementation types remain absent.

- [ ] **Step 3: Run complete FunctionalTests**

```powershell
dotnet Iwesun.Runtime.FunctionalTests/bin/Debug/net10.0/Iwesun.Runtime.FunctionalTests.dll
```

Expected: every scenario succeeds and report failures are empty.

- [ ] **Step 4: Verify process and diff hygiene**

Confirm no live SampleHost/CLI/FunctionalTests validation process remains. Run `git diff --check`; only line-ending notices are acceptable.

- [ ] **Step 5: Update documentation**

Document:

- global-state + FIFO dual channel;
- Guardian cleanup/state/unregister sequence;
- registration rejection after StopRequested;
- lifecycle CLI response fields;
- exit codes 0 and 124;
- timeout residue report;
- exact build/test report paths.

- [ ] **Step 6: Commit**

```powershell
git add docs/05-runtime-tooling/RUNTIME_INTEGRATION_GUIDE.md docs/IWESUN_RUNTIME_CLI.md docs/REQUIREMENTS_ACTIVE.md
git commit -m "docs: record coordinated shutdown protocol"
```

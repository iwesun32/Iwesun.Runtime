# SampleHost CLI Full Validation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend the real SampleHost with periodic random-number diagnostics and add a FunctionalTests scenario that drives the complete supported Runtime surface through the compiled CLI.

**Architecture:** `SampleHostRandomState` owns thread-safe random samples and deterministic validation samples. `SampleHostWorker` updates it from the existing worker loop and publishes output/watch/event/breakpoint surfaces. A new `sample-host-cli-full` functional scenario launches the real SampleHost, discovers its pipe, runs CLI v2 commands, asserts every supported capability, and always attempts `safe-shutdown`.

**Tech Stack:** C# 14, .NET 10 Generic Host, `System.Diagnostics.Process`, named-pipe Runtime protocol, `Iwesun.Runtime.Cli`, JSON reports.

---

## File map

- Create `Iwesun.Runtime.SampleHost/SampleHostRandomState.cs`: thread-safe random state, snapshots, update event, deterministic validation sample injection.
- Modify `Iwesun.Runtime.SampleHost/Program.cs`: assembly declarations, DI registration, reflection whitelist, startup pipe announcement.
- Modify `Iwesun.Runtime.SampleHost/SampleHostWorker.cs`: periodic random generation and Runtime injection points.
- Modify `Iwesun.Runtime.FunctionalTests/Program.cs`: scenario routing and top-level result aggregation only.
- Create `Iwesun.Runtime.FunctionalTests/SampleHostCliFullScenario.cs`: process orchestration, CLI driver, checklist assertions, safe shutdown, report data.
- Modify `docs/REQUIREMENTS_ACTIVE.md`: record coverage and final verification.

### Task 1: Random state contract

**Files:**
- Create: `Iwesun.Runtime.SampleHost/SampleHostRandomState.cs`
- Modify: `Iwesun.Runtime.FunctionalTests/Program.cs`
- Test: `Iwesun.Runtime.FunctionalTests/Program.cs` (`sample-host-random-state` scenario)

- [ ] **Step 1: Add a failing random-state scenario**

Add routing for `sample-host-random-state` and a scenario that constructs the state, calls `Record(17)` then `Record(83)`, and asserts:

```csharp
var state = new SampleHostRandomState();
state.Record(17);
state.Record(83);
var snapshot = state.Snapshot();

Assert(snapshot.CurrentValue == 83, "random-current-value");
Assert(snapshot.PreviousValue == 17, "random-previous-value");
Assert(snapshot.MinimumValue == 17, "random-minimum-value");
Assert(snapshot.MaximumValue == 83, "random-maximum-value");
Assert(snapshot.SampleCount == 2, "random-sample-count");
Assert(snapshot.UpdatedAt > DateTimeOffset.MinValue, "random-updated-at");
```

- [ ] **Step 2: Run RED verification**

Run:

```powershell
dotnet build Iwesun.Runtime.FunctionalTests/Iwesun.Runtime.FunctionalTests.csproj -c Debug
```

Expected: FAIL because `SampleHostRandomState` does not exist.

- [ ] **Step 3: Implement the state model**

Create:

```csharp
namespace Iwesun.Runtime.SampleHost;

public sealed class SampleHostRandomState
{
    private readonly object _gate = new();
    private int _currentValue;
    private int _previousValue;
    private int _minimumValue = int.MaxValue;
    private int _maximumValue = int.MinValue;
    private long _sampleCount;
    private DateTimeOffset _updatedAt = DateTimeOffset.UtcNow;

    public event EventHandler<SampleHostRandomUpdatedEventArgs>? Updated;

    public int CurrentValue => Snapshot().CurrentValue;
    public int PreviousValue => Snapshot().PreviousValue;
    public int MinimumValue => Snapshot().MinimumValue;
    public int MaximumValue => Snapshot().MaximumValue;
    public long SampleCount => Snapshot().SampleCount;
    public DateTimeOffset UpdatedAt => Snapshot().UpdatedAt;

    public void Record(int value)
    {
        SampleHostRandomSnapshot snapshot;
        lock (_gate)
        {
            _previousValue = _sampleCount == 0 ? value : _currentValue;
            _currentValue = value;
            _minimumValue = Math.Min(_minimumValue, value);
            _maximumValue = Math.Max(_maximumValue, value);
            _sampleCount++;
            _updatedAt = DateTimeOffset.UtcNow;
            snapshot = CreateSnapshot();
        }
        Updated?.Invoke(this, new SampleHostRandomUpdatedEventArgs(snapshot));
    }

    public SampleHostRandomSnapshot Snapshot()
    {
        lock (_gate) return CreateSnapshot();
    }

    public SampleHostRandomSnapshot RecordValidationSample(int value)
    {
        Record(value);
        return Snapshot();
    }

    private SampleHostRandomSnapshot CreateSnapshot() => new(
        _currentValue, _previousValue,
        _sampleCount == 0 ? 0 : _minimumValue,
        _sampleCount == 0 ? 0 : _maximumValue,
        _sampleCount, _updatedAt);
}

public sealed record SampleHostRandomSnapshot(
    int CurrentValue, int PreviousValue, int MinimumValue, int MaximumValue,
    long SampleCount, DateTimeOffset UpdatedAt);

public sealed record SampleHostRandomUpdatedEventArgs(SampleHostRandomSnapshot Snapshot);
```

- [ ] **Step 4: Reference SampleHost from FunctionalTests**

Add to `Iwesun.Runtime.FunctionalTests.csproj`:

```xml
<ProjectReference Include="..\Iwesun.Runtime.SampleHost\Iwesun.Runtime.SampleHost.csproj" />
```

The executable project is referenced only for its public state contract; FunctionalTests retains its own entry point and does not invoke SampleHost top-level statements in-process.

- [ ] **Step 5: Run GREEN verification**

Run the focused scenario. Expected: JSON contains `"Success":true` and all six random-state checks.

- [ ] **Step 6: Commit**

```powershell
git add Iwesun.Runtime.SampleHost/SampleHostRandomState.cs Iwesun.Runtime.FunctionalTests/Program.cs Iwesun.Runtime.FunctionalTests/Iwesun.Runtime.FunctionalTests.csproj
git commit -m "feat: add sample host random state"
```

### Task 2: Inject random functionality into SampleHost

**Files:**
- Modify: `Iwesun.Runtime.SampleHost/Program.cs`
- Modify: `Iwesun.Runtime.SampleHost/SampleHostWorker.cs`
- Test: `Iwesun.Runtime.FunctionalTests/SampleHostCliFullScenario.cs`

- [ ] **Step 1: Write a failing host smoke scenario**

Create a scenario helper that starts the built SampleHost with a unique pipe override, waits for `host.info`, reads `sample.host.random`, waits one worker interval, reads it again, and asserts `SampleCount` increased.

Expected checklist keys:

```csharp
"sample-host-started",
"sample-host-pipe-ready",
"random-sample-count-increased",
"random-value-in-range"
```

- [ ] **Step 2: Run RED verification**

Expected: FAIL because target `sample.host.random` and its watch/output declarations are absent.

- [ ] **Step 3: Register declarations and DI**

In `Program.cs`, add:

```csharp
[assembly: DiagnosticWatchPoint(
    "sample.host.random", "sample-host", "random",
    "Periodic random sample.",
    "Iwesun.Runtime.SampleHost/SampleHostRandomState.cs")]
[assembly: DiagnosticBreakpoint(
    "sample.host.random.initial-enabled", "sample-host",
    "Compiled enabled random breakpoint.",
    "Iwesun.Runtime.SampleHost/SampleHostWorker.cs", Enabled = true)]
[assembly: DiagnosticBreakpoint(
    "sample.host.random.dynamic", "sample-host",
    "Runtime-controlled random breakpoint.",
    "Iwesun.Runtime.SampleHost/SampleHostWorker.cs", Enabled = false)]
[assembly: DiagnosticBreakpoint(
    "sample.host.random.numeric", "sample-host",
    "Runtime-controlled numeric breakpoint.",
    "Iwesun.Runtime.SampleHost/SampleHostWorker.cs", Enabled = false)]
[assembly: DiagnosticNumericBreakpoint("sample.host.random.numeric", "gt", 90)]
[assembly: DiagnosticHookableEvent(
    "sample.host.random-updated",
    typeof(SampleHostRandomState),
    nameof(SampleHostRandomState.Updated))]
```

Register the singleton and reflection target:

```csharp
builder.Services.AddSingleton<SampleHostRandomState>();

var randomState = host.Services.GetRequiredService<SampleHostRandomState>();
RuntimeInjector.Data(hub, "sample.host.random", randomState,
    new RuntimeDiagnosticObjectAccess
    {
        AllowReadAllPublic = false,
        ReadableMembers = ["CurrentValue", "PreviousValue", "MinimumValue", "MaximumValue", "SampleCount", "UpdatedAt", "Snapshot"],
        InvokableMembers = ["Snapshot", "RecordValidationSample"]
    });
```

Whitelist `RecordValidationSample` only for the SampleHost validation surface; document that it is a deterministic diagnostic stimulus.

- [ ] **Step 4: Publish periodic samples**

Inject `SampleHostRandomState` into `SampleHostWorker`. In the worker loop:

```csharp
var randomValue = Random.Shared.Next(0, 101);
_randomState.Record(randomValue);
var randomSnapshot = _randomState.Snapshot();

RuntimeInjector.Output(
    "sample.host.random", "sample-host", "random",
    "Periodic random sample.", randomSnapshot);
RuntimeInjector.Watch(
    "sample.host.random", randomSnapshot,
    nameof(SampleHostRandomSnapshot));

#if DEBUG
await RuntimeInjector.Break(
    "sample.host.random.initial-enabled",
    () => randomSnapshot.SampleCount == 1,
    randomSnapshot);
await RuntimeInjector.Break(
    "sample.host.random.dynamic",
    () => true,
    randomSnapshot);
await RuntimeOutput.BreakIfNumbers(
    "sample.host.random.numeric",
    randomSnapshot.CurrentValue,
    randomSnapshot.PreviousValue,
    0,
    randomSnapshot);
#endif
```

- [ ] **Step 5: Supply a deterministic pipe name from the parent**

FunctionalTests generates a unique name and starts SampleHost with:

```powershell
dotnet Iwesun.Runtime.SampleHost.dll --runtime-diagnostics-pipe=sample.host.validation.<guid>
```

In `Program.cs`, parse the same startup argument and pass it to the host template:

```csharp
const string pipeArgument = "--runtime-diagnostics-pipe=";
var startupPipeName = args
    .FirstOrDefault(x => x.StartsWith(pipeArgument, StringComparison.OrdinalIgnoreCase))?
    [pipeArgument.Length..];

builder.Services.Start(
    runtimeDirectory,
    startupRuntimeDiagnosticsPipeName: startupPipeName);
```

The parent already knows the name and polls `host.info`; SampleHost must not announce diagnostics through console output.

- [ ] **Step 6: Run GREEN smoke verification and commit**

Expected: SampleCount increases across at least two snapshots; safe shutdown exits code 0.

```powershell
git add Iwesun.Runtime.SampleHost/Program.cs Iwesun.Runtime.SampleHost/SampleHostWorker.cs Iwesun.Runtime.FunctionalTests/SampleHostCliFullScenario.cs
git commit -m "feat: inject random diagnostics into sample host"
```

### Task 3: Build the reusable CLI process driver

**Files:**
- Create: `Iwesun.Runtime.FunctionalTests/SampleHostCliFullScenario.cs`
- Modify: `Iwesun.Runtime.FunctionalTests/Program.cs`

- [ ] **Step 1: Add failing CLI driver assertions**

The driver must reject nonzero exits, invalid JSON, and `status.ok=false` with command-specific diagnostics.

```csharp
var response = await driver.RunAsync("host.info", cancellationToken);
checks.Add(response.Frame.Status?.Ok == true ? "cli-host-info-ok" : "");
```

- [ ] **Step 2: Run RED verification**

Expected: FAIL because `SampleHostCliDriver` does not exist.

- [ ] **Step 3: Implement driver and process capture**

Implement these focused types in the new file:

```csharp
internal sealed record CliExecution(
    string Command, int ExitCode, string Stdout, string Stderr,
    RuntimeDiagnosticFrame? Frame, long DurationMs);

internal sealed class SampleHostCliDriver
{
    public SampleHostCliDriver(string cliDll, string configPath, string pipeName, string workingDirectory) { }
    public Task<CliExecution> RunAsync(string command, CancellationToken cancellationToken) { }
}
```

Use `ProcessStartInfo("dotnet", ...)`, redirect stdout/stderr, quote paths, await `WaitForExitAsync`, and deserialize the last complete JSON object. Never construct a pipe client in this scenario.

- [ ] **Step 4: Implement host lifecycle wrapper**

```csharp
internal sealed class SampleHostProcess : IAsyncDisposable
{
    public Process Process { get; }
    public string PipeName { get; }
    public Task<string> ReadStdoutAsync();
    public Task<string> ReadStderrAsync();
    public Task<bool> WaitForExitAsync(TimeSpan timeout);
    public Task RequestSafeShutdownAsync(SampleHostCliDriver cli, CancellationToken ct);
}
```

`RequestSafeShutdownAsync` runs `safe-shutdown`, tolerates connection cancellation only when the process exits, then asserts exit code 0. The fallback process termination is allowed only after CLI is unreachable and the graceful timeout expires.

- [ ] **Step 5: Verify focused lifecycle test and commit**

Expected checks: `host-started`, `host-info-ok`, `safe-shutdown-requested`, `host-exit-code-zero`, `no-host-process-remains`.

### Task 4: Implement the full CLI checklist

**Files:**
- Modify: `Iwesun.Runtime.FunctionalTests/SampleHostCliFullScenario.cs`
- Modify: `Iwesun.Runtime.FunctionalTests/Program.cs`

- [ ] **Step 1: Add the complete expected checklist as a failing assertion**

Define stable keys grouped by capability:

```csharp
string[] RequiredChecks =
[
    "host-info", "host-list", "host-events",
    "switchboard-list", "switchboard-global", "switchboard-pipe", "switchboard-file", "switchboard-point",
    "root-paths", "registry-all", "object-read", "object-invoke", "object-denied",
    "breakpoint-compiled-enabled", "breakpoint-compiled-disabled", "breakpoint-enable", "breakpoint-disconnect-stable", "breakpoint-resume", "breakpoint-disable",
    "numeric-threshold-hit", "numeric-threshold-miss", "numeric-static-hit", "numeric-rule-switch", "numeric-clear",
    "hook-enable", "hook-event", "hook-disable",
    "thread-list", "task-list", "unit-state", "unit-history", "state-invalid-transition",
    "file-status", "file-on", "file-created", "file-off",
    "safe-shutdown", "exit-code-zero", "no-process-remains"
];
```

The scenario fails when any key is missing.

- [ ] **Step 2: Run RED verification**

Expected: FAIL listing all unimplemented checklist keys.

- [ ] **Step 3: Implement read-only and switchboard commands**

Execute and assert `host.list`, `host.info`, `host.events`, `sw.list`, `sw.enable`, `sw.pipe true`, `sw.file true`, `sw.points`, point enable/disable, `root.paths`, and `reg.list`.

Each command records `CliExecution`; success requires exit 0 and protocol success. Restore `quiet` or equivalent explicit switches before shutdown.

- [ ] **Step 4: Implement object and hook checks**

Use `ref.get sample.host.random Snapshot`, `ref.exec sample.host.random Snapshot`, and `ref.nav sample.host.random Snapshot.CurrentValue`. Read and invoke only whitelisted random-state members. Execute `ref.exec sample.host.random ToString` and assert protocol failure. Enable `sample.host.random-updated`, wait for a new sample/event, then disable it and verify no new hook observation is attributed after the disable timestamp.

- [ ] **Step 5: Implement breakpoint checks**

For compiled enabled breakpoint, do not send `bp.enable`; poll `bp.snapshot` for waiting, query again to prove disconnect stability, then `bp.resume`.

For compiled disabled breakpoint, verify multiple iterations advance without waiting; run `bp.enable`, observe waiting, `bp.resume`, run `bp.disable`, then verify iterations advance without waiting.

- [ ] **Step 6: Implement numeric data-breakpoint checks**

Use `bp.setNumericThreshold sample.host.random.numeric gt 90` and `RecordValidationSample(95)` to force a hit. Resume explicitly. Switch to `lt 10`, inject 50 for a miss and 5 for a hit. Use `bp.setNumeric ... eq2` with equal current/previous validation samples for static predicate coverage. End with `bp.clearNumeric` and `bp.disable`.

- [ ] **Step 7: Implement execution, state and file checks**

Assert `thread.list` contains coordinator, worker and monitor. Assert `task.list` contains static loops and at least one completed dynamic batch. Query unit state/history and assert an invalid transition fails without killing the host. Enable file recording, generate a marked sample, verify the resolved file exists and contains the mark, then disable file recording.

- [ ] **Step 8: Verify the scenario and commit**

Run:

```powershell
dotnet Iwesun.Runtime.FunctionalTests/bin/Debug/net10.0/Iwesun.Runtime.FunctionalTests.dll --child --scenario sample-host-cli-full
```

Expected: `Success=true`, every `RequiredChecks` key present, failures empty, SampleHost exit code 0.

### Task 5: Failure evidence and safe cleanup

**Files:**
- Modify: `Iwesun.Runtime.FunctionalTests/SampleHostCliFullScenario.cs`

- [ ] **Step 1: Add a failing cleanup test path**

Inject a deliberately unknown CLI command midway, assert the scenario records command/exit/stdout/stderr, and verify SampleHost still receives `safe-shutdown` in `finally`.

- [ ] **Step 2: Implement structured evidence**

```csharp
internal sealed record SampleHostCliValidationReport(
    bool Success,
    IReadOnlyList<string> Checks,
    IReadOnlyList<string> Failures,
    IReadOnlyList<CliExecution> Commands,
    string HostStdout,
    string HostStderr,
    int? HostExitCode,
    DateTimeOffset CheckedAt);
```

Write reports under `runtime-functional-reports` using the existing reporting pattern. Do not create ad-hoc diagnostic log files.

- [ ] **Step 3: Implement condition-based cleanup**

Before safe shutdown: disable all test breakpoints, poll until no breakpoint is waiting, issue `bp.resume-all` if necessary, then run `safe-shutdown`. Confirm the process exits within 10 seconds.

- [ ] **Step 4: Run positive and injected-failure paths**

Expected positive path: exit 0. Expected injected failure: scenario failure contains the unknown command evidence while host exit remains 0 and no process remains.

- [ ] **Step 5: Commit**

```powershell
git add Iwesun.Runtime.FunctionalTests/SampleHostCliFullScenario.cs
git commit -m "test: capture sample host CLI validation evidence"
```

### Task 6: Full regression and documentation

**Files:**
- Modify: `docs/REQUIREMENTS_ACTIVE.md`

- [ ] **Step 1: Build the entire solution**

```powershell
dotnet build Iwesun.Runtime.slnx -c Debug
dotnet build Iwesun.Runtime.slnx -c Release
```

Expected: 0 errors. Record warning counts separately; do not claim warning-free if CA1416 work remains.

- [ ] **Step 2: Run the focused SampleHost validation**

Expected: all checklist keys present, failure list empty, safe exit code 0.

- [ ] **Step 3: Run the complete FunctionalTests suite**

```powershell
dotnet Iwesun.Runtime.FunctionalTests/bin/Debug/net10.0/Iwesun.Runtime.FunctionalTests.dll
```

Expected: every scenario true, failures empty, report path emitted.

- [ ] **Step 4: Verify process cleanup and diff hygiene**

Confirm no command line contains live SampleHost, CLI, or FunctionalTests validation processes. Run `git diff --check`. Inspect only the files in this plan and preserve unrelated user changes.

- [ ] **Step 5: Update active requirements**

Record the exact build results, full checklist result, report path, and any known warning debt in Chinese.

- [ ] **Step 6: Final commit**

```powershell
git add docs/REQUIREMENTS_ACTIVE.md
git commit -m "docs: record sample host CLI validation"
```

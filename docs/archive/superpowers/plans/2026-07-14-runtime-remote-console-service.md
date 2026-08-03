# Runtime Remote Console Service Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build and debug a Windows RemoteConsole service that accepts approved CLI Shell commands, uploads workspace files, executes commands under the installed service account, and returns sequenced stdout, stderr, and exit codes.

**Architecture:** Add a protocol assembly shared by the service and CLI, then host an independent framed named-pipe server in a Windows Service executable. The service owns Windows identity authorization, approval state, workspace boundaries, process execution, and output buffers; CLI remains the AI-facing shell. The WPF manager is deliberately excluded from this plan and will consume the completed approval API in a later plan.

**Tech Stack:** C#/.NET 10, Microsoft.Extensions.Hosting, Microsoft.Extensions.Hosting.WindowsServices 10.0.9, Windows named pipes with ACL and client impersonation, RuntimeDiagnosticFrame v2, SHA-256, Iwesun.Runtime.Cli v3, Iwesun.Runtime.FunctionalTests.

---

## File map

Create these focused units:

- `Iwesun.Runtime.Diagnostics/RuntimeFramePipeCodec.cs` — shared length-prefixed JSON frame reader/writer.
- `Iwesun.Runtime.RemoteConsole.Protocol/` — immutable command, state, workspace, output and response models.
- `Iwesun.Runtime.RemoteConsole/Program.cs` — console/SCM host entry point and source-owned identities.
- `Iwesun.Runtime.RemoteConsole/RemoteConsoleOptions.cs` — pipe, principals, workspace and quota settings.
- `Iwesun.Runtime.RemoteConsole/RemoteConsolePipeServer.cs` — pipe accept loop, identity capture and framed dispatch.
- `Iwesun.Runtime.RemoteConsole/RemoteConsoleAuthorization.cs` — submitter/approver action checks.
- `Iwesun.Runtime.RemoteConsole/RemoteConsoleJobStore.cs` — idempotent in-memory jobs and atomic transitions.
- `Iwesun.Runtime.RemoteConsole/RemoteConsoleApprovalPolicy.cs` — Manual/Guarded/Automatic decision.
- `Iwesun.Runtime.RemoteConsole/RemoteConsoleCommandExecutor.cs` — original command launch and output capture.
- `Iwesun.Runtime.RemoteConsole/RemoteConsoleWorkspaceStore.cs` — workspace, path containment, quota and upload sessions.
- `Iwesun.Runtime.RemoteConsole/RemoteConsoleCommandRouter.cs` — maps Frame actions to focused services.
- `Iwesun.Runtime.Cli/CliRemoteConsoleClient.cs` — submit/follow/workspace multi-request operations.
- `Iwesun.Runtime.Cli/CliRemoteConsoleShell.cs` — local CLI grammar and current console target.
- `Iwesun.Runtime.FunctionalTests/RemoteConsoleScenario.cs` — focused in-process and child-process scenarios.
- `docs/IWESUN_RUNTIME_REMOTE_CONSOLE.md` — installation, source identities, CLI and debugging manual.
- `skills/iwesun-runtime-integration/references/remote-console.md` — reusable AI operating instructions.

Existing files modified together:

- `Iwesun.Runtime.Diagnostics/RuntimeFramePipeClient.cs`
- `Iwesun.Runtime.Diagnostics/RuntimeDiagnosticsMonitor.cs`
- `Iwesun.Runtime.Cli/CliApplication.cs`
- `Iwesun.Runtime.Cli/CliInteractiveShell.cs`
- `Iwesun.Runtime.Cli/RuntimeCliSystemConfig.json`
- `Iwesun.Runtime.Cli/RuntimeCliSystemMetadata.json`
- `Iwesun.Runtime.FunctionalTests/Program.cs`
- `Iwesun.Runtime.FunctionalTests/Iwesun.Runtime.FunctionalTests.csproj`
- `Iwesun.Runtime.slnx`
- `Iwesun.Runtime.Release/Iwesun.Runtime.Release.csproj`
- `Iwesun.Runtime.Setup/Iwesun.Runtime.Setup.wixproj`
- `scripts/release/verify-runtime-install.ps1`
- `docs/README.md`
- `docs/REQUIREMENTS_ACTIVE.md`
- `skills/iwesun-runtime-integration/SKILL.md`

## Task 1: Extract the shared Runtime frame codec

**Files:**
- Create: `Iwesun.Runtime.Diagnostics/RuntimeFramePipeCodec.cs`
- Modify: `Iwesun.Runtime.Diagnostics/RuntimeFramePipeClient.cs`
- Modify: `Iwesun.Runtime.Diagnostics/RuntimeDiagnosticsMonitor.cs`
- Test: `Iwesun.Runtime.FunctionalTests/RemoteConsoleScenario.cs`

- [ ] **Step 1: Add a failing fragmented-frame scenario**

Add `RunFrameCodecAsync()` that writes the four-byte header and JSON body in deliberately fragmented chunks, then asserts the decoded `RequestId` and rejects payloads over the configured maximum.

```csharp
public static async Task<FunctionalScenarioResult> RunFrameCodecAsync()
{
    var frame = new RuntimeDiagnosticFrame
    {
        Header = new RuntimeDiagnosticFrameHeader
        {
            Schema = RuntimeDiagnosticProtocol.V2Schema,
            FrameType = "request",
            RequestId = "codec-fragmented"
        }
    };
    await using var stream = new FragmentingDuplexStream([1, 2, 3, 5]);
    await RuntimeFramePipeCodec.WriteAsync(stream, frame, CancellationToken.None);
    stream.Rewind();
    var decoded = await RuntimeFramePipeCodec.ReadAsync(stream, 1024 * 1024, CancellationToken.None);
    return decoded.Header.RequestId == "codec-fragmented"
        ? FunctionalScenarioResult.Pass("remote-console-frame-codec", "fragmented frame round-trip")
        : FunctionalScenarioResult.Fail("remote-console-frame-codec", [], ["requestId mismatch"]);
}
```

Define `FragmentingDuplexStream` in the same test file as a memory-backed stream that limits each read/write to the next configured fragment length.

- [ ] **Step 2: Run the scenario and verify it fails**

Run: `dotnet run --project Iwesun.Runtime.FunctionalTests -c Debug -- --child --scenario remote-console-frame-codec`

Expected: build fails because `RuntimeFramePipeCodec` does not exist.

- [ ] **Step 3: Implement the public codec**

```csharp
public static class RuntimeFramePipeCodec
{
    public static async Task WriteAsync(Stream stream, RuntimeDiagnosticFrame frame, CancellationToken cancellationToken);
    public static async Task<RuntimeDiagnosticFrame> ReadAsync(Stream stream, int maxPayloadBytes, CancellationToken cancellationToken);
}
```

Use `BinaryPrimitives.WriteInt32LittleEndian`, loop until the complete header/body is read, reject `length <= 0 || length > maxPayloadBytes`, and deserialize with `JsonSerializerDefaults.Web`.

- [ ] **Step 4: Replace duplicate client and monitor framing**

Make `RuntimeFramePipeClient` and `RuntimeDiagnosticsMonitor` delegate all frame reads/writes to `RuntimeFramePipeCodec`; preserve existing response limits and error Frame behavior.

- [ ] **Step 5: Run regression scenarios**

Run:

```powershell
dotnet run --project Iwesun.Runtime.FunctionalTests -c Debug -- --child --scenario remote-console-frame-codec
dotnet run --project Iwesun.Runtime.FunctionalTests -c Debug -- --child --scenario cli-transport-failure
dotnet build Iwesun.Runtime.Diagnostics/Iwesun.Runtime.Diagnostics.csproj -c Release
```

Expected: scenarios pass; Release build has 0 errors.

- [ ] **Step 6: Commit**

```powershell
git add Iwesun.Runtime.Diagnostics/RuntimeFramePipeCodec.cs Iwesun.Runtime.Diagnostics/RuntimeFramePipeClient.cs Iwesun.Runtime.Diagnostics/RuntimeDiagnosticsMonitor.cs Iwesun.Runtime.FunctionalTests/RemoteConsoleScenario.cs Iwesun.Runtime.FunctionalTests/Program.cs
git commit -m "refactor: share runtime frame pipe codec"
```

## Task 2: Add the RemoteConsole protocol assembly

**Files:**
- Create: `Iwesun.Runtime.RemoteConsole.Protocol/Iwesun.Runtime.RemoteConsole.Protocol.csproj`
- Create: `Iwesun.Runtime.RemoteConsole.Protocol/RemoteConsoleModels.cs`
- Create: `Iwesun.Runtime.RemoteConsole.Protocol/RemoteConsoleProtocol.cs`
- Modify: `Iwesun.Runtime.slnx`
- Modify: `Iwesun.Runtime.FunctionalTests/Iwesun.Runtime.FunctionalTests.csproj`

- [ ] **Step 1: Write protocol validation tests**

Test that unknown states/actions fail, submit requires shell/command/workspace, follow requires non-negative `afterSequence`, and upload chunks remain within the compiled chunk limit.

```csharp
var invalid = new RemoteConsoleSubmitRequest("", "", "", "", new Dictionary<string, string>(), "");
var validation = RemoteConsoleProtocol.Validate(invalid);
AssertFalse(validation.Ok, "empty submit must fail");
```

- [ ] **Step 2: Create the project and verify the tests fail**

The project references `Iwesun.Runtime.Diagnostics` for `RuntimeDiagnosticFrame` only. Add it to `Iwesun.Runtime.slnx` and the functional test project.

Run: `dotnet build Iwesun.Runtime.FunctionalTests/Iwesun.Runtime.FunctionalTests.csproj -c Debug`

Expected: fails until the protocol types are implemented.

- [ ] **Step 3: Implement exact protocol types**

```csharp
public enum RemoteConsoleApprovalMode { Manual, Guarded, Automatic }
public enum RemoteConsoleJobState { Submitted, AwaitingApproval, Starting, Running, Completed, Failed, Rejected, Cancelled, Interrupted }
public enum RemoteConsoleOutputStream { Stdout, Stderr }

public sealed record RemoteConsoleSubmitRequest(
    string RequestId,
    string Shell,
    string Command,
    string WorkspaceId,
    IReadOnlyDictionary<string, string> Environment,
    string RiskHint);

public sealed record RemoteConsoleOutputChunk(
    long Sequence,
    RemoteConsoleOutputStream Stream,
    DateTimeOffset Timestamp,
    string Text);
```

Also define workspace create/list/remove, upload begin/chunk/commit, job status/follow, approve/reject/cancel, policy get/set, stable error codes, and Frame factory/parser methods under domain `remote.console` and target `remote.console.server`.

- [ ] **Step 4: Run protocol tests and build**

Run: `dotnet build Iwesun.Runtime.RemoteConsole.Protocol/Iwesun.Runtime.RemoteConsole.Protocol.csproj -c Release`

Expected: 0 errors and 0 warnings.

- [ ] **Step 5: Commit**

```powershell
git add Iwesun.Runtime.RemoteConsole.Protocol Iwesun.Runtime.slnx Iwesun.Runtime.FunctionalTests/Iwesun.Runtime.FunctionalTests.csproj Iwesun.Runtime.FunctionalTests/RemoteConsoleScenario.cs
git commit -m "feat: add remote console protocol"
```

## Task 3: Build the Windows Service host and identity boundary

**Files:**
- Create: `Iwesun.Runtime.RemoteConsole/Iwesun.Runtime.RemoteConsole.csproj`
- Create: `Iwesun.Runtime.RemoteConsole/Program.cs`
- Create: `Iwesun.Runtime.RemoteConsole/RemoteConsoleOptions.cs`
- Create: `Iwesun.Runtime.RemoteConsole/RemoteConsoleAuthorization.cs`
- Create: `Iwesun.Runtime.RemoteConsole/RemoteConsolePipeServer.cs`
- Modify: `Iwesun.Runtime.slnx`

- [ ] **Step 1: Add authorization failure tests**

Test Submitter can submit/read own jobs, Approver can approve/policy-set, unlisted identities receive `RC_ACCESS_DENIED`, and identical submitter/approver identity receives `RC_SELF_APPROVAL_DENIED`.

- [ ] **Step 2: Create the service project**

Reference Protocol, Diagnostics, `Microsoft.Extensions.Hosting` 10.0.9, `Microsoft.Extensions.Hosting.WindowsServices` 10.0.9, and `System.IO.Pipes.AccessControl` 6.0.0. Add `--console` behavior for developer runs; otherwise call `AddWindowsService`.

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "Iwesun.Runtime.RemoteConsole");
builder.Services.AddSingleton(new RemoteConsoleOptions
{
    PipeName = "Iwesun.Runtime.RemoteConsole",
    SubmitterPrincipals = [$@"{Environment.MachineName}\IwesunAiDiag"],
    ApproverPrincipals = ["S-1-5-32-544"],
    ApprovalMode = RemoteConsoleApprovalMode.Manual
});
builder.Services.AddHostedService<RemoteConsolePipeServer>();
await builder.Build().RunAsync();
```

- [ ] **Step 3: Resolve principals once at startup**

Accept either an account/group name or a SID string, and resolve every configured principal before the pipe accept loop. Missing names and invalid SIDs throw a dedicated configuration exception and stop service startup. Build the pipe ACL from SYSTEM, service identity, Administrators, Submitters and Approvers.

- [ ] **Step 4: Capture actual client identity**

After connection, use `NamedPipeServerStream.RunAsClient` and `WindowsIdentity.GetCurrent(TokenAccessLevels.Query)` to capture the authenticated SID. Never accept identity, role or approval claims from Frame data.

- [ ] **Step 5: Run console-mode smoke test**

Run: `dotnet run --project Iwesun.Runtime.RemoteConsole -c Debug -- --console`

Expected: the service logs structured startup through `ILogger`, creates `Iwesun.Runtime.RemoteConsole`, and stops cleanly on Ctrl+C without `Console.WriteLine` diagnostics.

- [ ] **Step 6: Commit**

```powershell
git add Iwesun.Runtime.RemoteConsole Iwesun.Runtime.slnx Iwesun.Runtime.FunctionalTests/RemoteConsoleScenario.cs
git commit -m "feat: host remote console windows service"
```

## Task 4: Implement idempotent jobs and approval transitions

**Files:**
- Create: `Iwesun.Runtime.RemoteConsole/RemoteConsoleJobStore.cs`
- Create: `Iwesun.Runtime.RemoteConsole/RemoteConsoleApprovalPolicy.cs`
- Create: `Iwesun.Runtime.RemoteConsole/RemoteConsoleCommandRouter.cs`
- Test: `Iwesun.Runtime.FunctionalTests/RemoteConsoleScenario.cs`

- [ ] **Step 1: Add failing state-machine tests**

Cover all three modes, duplicate RequestId, concurrent approve/reject, self-approval, cancel Pending, reject Running cancel, and immutable approved content hash.

```csharp
var first = store.Submit(request, submitterSid);
var duplicate = store.Submit(request, submitterSid);
AssertEqual(first.JobId, duplicate.JobId, "same requestId must be idempotent");
AssertFalse(store.Cancel(first.JobId, submitterSid).Ok && first.State == RemoteConsoleJobState.Running,
    "running job must not be reported cancelled");
```

- [ ] **Step 2: Implement atomic transitions**

Use `ConcurrentDictionary<string, RemoteConsoleJob>` plus a per-job lock. Store SHA-256 over shell, command, workspace, working directory and environment. Legal transitions are exactly those in the approved design; return `RC_JOB_STATE_CONFLICT` with current state for losing races.

- [ ] **Step 3: Implement simple source-owned rules**

Guarded mode uses anchored compiled regular expressions from `AutoApprovePatterns`. Automatic mode rejects anchored `DenyPatterns`. Empty or invalid patterns fail startup; no shell semantic parser is introduced.

- [ ] **Step 4: Run tests**

Run: `dotnet run --project Iwesun.Runtime.FunctionalTests -c Debug -- --child --scenario remote-console-approval`

Expected: all identity, idempotency and transition checks pass.

- [ ] **Step 5: Commit**

```powershell
git add Iwesun.Runtime.RemoteConsole/RemoteConsoleJobStore.cs Iwesun.Runtime.RemoteConsole/RemoteConsoleApprovalPolicy.cs Iwesun.Runtime.RemoteConsole/RemoteConsoleCommandRouter.cs Iwesun.Runtime.FunctionalTests/RemoteConsoleScenario.cs
git commit -m "feat: add remote console approval state machine"
```

## Task 5: Execute original commands and stream bounded output

**Files:**
- Create: `Iwesun.Runtime.RemoteConsole/RemoteConsoleCommandExecutor.cs`
- Create: `Iwesun.Runtime.RemoteConsole/RemoteConsoleOutputBuffer.cs`
- Modify: `Iwesun.Runtime.RemoteConsole/RemoteConsoleJobStore.cs`
- Test: `Iwesun.Runtime.FunctionalTests/RemoteConsoleScenario.cs`

- [ ] **Step 1: Add child-process tests**

Submit a PowerShell command that writes distinct stdout/stderr lines and exits 7. Assert ordered sequences, correct stream labels, final Failed state and ExitCode 7. Add a small output budget test that returns `Truncated=true` and `EarliestAvailableSequence`.

- [ ] **Step 2: Implement process launch**

Use `ProcessStartInfo` with `UseShellExecute=false`, `CreateNoWindow=true`, redirects enabled and the workspace path as `WorkingDirectory`. Permit only configured shell executable paths; pass the approved command unchanged as one shell argument.

```csharp
var startInfo = new ProcessStartInfo(options.PowerShellPath)
{
    UseShellExecute = false,
    CreateNoWindow = true,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    WorkingDirectory = workspacePath
};
startInfo.ArgumentList.Add("-NoLogo");
startInfo.ArgumentList.Add("-NonInteractive");
startInfo.ArgumentList.Add("-Command");
startInfo.ArgumentList.Add(job.Command);
```

- [ ] **Step 3: Implement output capture**

Read stdout and stderr asynchronously, assign each accepted line/chunk one `Interlocked.Increment` sequence, and store up to `MaxOutputBytesPerJob`. `Follow(afterSequence, limit, waitMs)` uses a per-job async signal and never blocks longer than the compiled maximum wait.

- [ ] **Step 4: Handle service stop**

Stop accepting new jobs, mark Running jobs Interrupted, stop output pumps, and leave OS child processes untouched in first version. Document this limitation in the returned final event; never report Completed or Cancelled.

- [ ] **Step 5: Run command/output tests**

Run: `dotnet run --project Iwesun.Runtime.FunctionalTests -c Debug -- --child --scenario remote-console-command`

Expected: stdout/stderr/exit 7 and truncation checks pass; no child test process remains.

- [ ] **Step 6: Commit**

```powershell
git add Iwesun.Runtime.RemoteConsole/RemoteConsoleCommandExecutor.cs Iwesun.Runtime.RemoteConsole/RemoteConsoleOutputBuffer.cs Iwesun.Runtime.RemoteConsole/RemoteConsoleJobStore.cs Iwesun.Runtime.FunctionalTests/RemoteConsoleScenario.cs
git commit -m "feat: execute remote console commands"
```

## Task 6: Add isolated workspaces and chunk uploads

**Files:**
- Create: `Iwesun.Runtime.RemoteConsole/RemoteConsoleWorkspaceStore.cs`
- Create: `Iwesun.Runtime.RemoteConsole/RemoteConsoleUploadSession.cs`
- Modify: `Iwesun.Runtime.RemoteConsole/RemoteConsoleCommandRouter.cs`
- Test: `Iwesun.Runtime.FunctionalTests/RemoteConsoleScenario.cs`

- [ ] **Step 1: Add workspace boundary tests**

Reject `..\escape`, absolute paths, drive paths, UNC paths, alternate data streams, symlink/reparse escape, chunk overflow, quota overflow and hash mismatch. Verify commit atomically exposes the final relative file only after SHA-256 matches.

- [ ] **Step 2: Implement canonical path containment**

Resolve the workspace root and candidate with `Path.GetFullPath`, require candidate to start with `root + DirectorySeparatorChar` using `OrdinalIgnoreCase`, and reject any existing component with `FileAttributes.ReparsePoint`.

- [ ] **Step 3: Implement upload sessions**

`Begin` creates a server-named `.upload` file and expected length/hash record. `Chunk` requires the next exact sequence and enforces 64 KiB decoded bytes. `Commit` flushes, checks length and SHA-256, then `File.Move(temp, final, overwrite:false)` within the same workspace.

- [ ] **Step 4: Implement cleanup rules**

Use a periodic hosted cleanup pass. Delete workspaces older than 24 hours only when they have no active upload and no Starting/Running job. Explicit remove follows the same guard.

- [ ] **Step 5: Run upload tests**

Run: `dotnet run --project Iwesun.Runtime.FunctionalTests -c Debug -- --child --scenario remote-console-workspace`

Expected: valid upload passes; every escape/quota/hash case returns its stable RC code without files outside the root.

- [ ] **Step 6: Commit**

```powershell
git add Iwesun.Runtime.RemoteConsole/RemoteConsoleWorkspaceStore.cs Iwesun.Runtime.RemoteConsole/RemoteConsoleUploadSession.cs Iwesun.Runtime.RemoteConsole/RemoteConsoleCommandRouter.cs Iwesun.Runtime.FunctionalTests/RemoteConsoleScenario.cs
git commit -m "feat: add remote console workspaces"
```

## Task 7: Connect the existing CLI Shell

**Files:**
- Create: `Iwesun.Runtime.Cli/CliRemoteConsoleClient.cs`
- Create: `Iwesun.Runtime.Cli/CliRemoteConsoleShell.cs`
- Modify: `Iwesun.Runtime.Cli/CliApplication.cs`
- Modify: `Iwesun.Runtime.Cli/CliInteractiveShell.cs`
- Modify: `Iwesun.Runtime.Cli/RuntimeCliSystemConfig.json`
- Modify: `Iwesun.Runtime.Cli/RuntimeCliSystemMetadata.json`
- Test: `Iwesun.Runtime.FunctionalTests/RemoteConsoleScenario.cs`

- [ ] **Step 1: Add CLI grammar tests**

Cover console target add/use/current, workspace create/list/remove, upload, submit after `--`, status, follow, pending, approve/reject, policy get/set, and ensure `exit/quit` only leaves the local Shell.

- [ ] **Step 2: Add a `remote-console` endpoint**

Use pipe `Iwesun.Runtime.RemoteConsole`, 5-second connect timeout, 60-second request timeout and 16 MiB response ceiling. Add catalog entries for server actions; keep file.upload as a client command because it reads a local file and sends begin/chunk/commit requests.

- [ ] **Step 3: Reuse target resolution**

Extend Node/Target with endpoint `remote-console`; do not add a second server/pipe parser. A Diagnostics target and Console target may point at the same Node but must remain distinct aliases and current contexts.

- [ ] **Step 4: Implement upload and follow loops**

Upload reads 64 KiB blocks, computes SHA-256 locally, sends monotonically numbered chunks and commits. Follow repeatedly calls the finite server action, writes stdout to `Console.Out`, stderr to `Console.Error`, advances `afterSequence`, and stops only on a terminal Job state.

- [ ] **Step 5: Run end-to-end console-mode tests**

Start the service child in `--console` mode, then use the real Debug CLI to create a workspace, upload a text file, submit a PowerShell hash/read command, approve through an Approver test client, follow output and assert ExitCode 0.

Run: `dotnet run --project Iwesun.Runtime.FunctionalTests -c Debug -- --child --scenario remote-console-cli`

Expected: full CLI Frame round-trip passes and diagnostics targets remain unchanged.

- [ ] **Step 6: Commit**

```powershell
git add Iwesun.Runtime.Cli/CliRemoteConsoleClient.cs Iwesun.Runtime.Cli/CliRemoteConsoleShell.cs Iwesun.Runtime.Cli/CliApplication.cs Iwesun.Runtime.Cli/CliInteractiveShell.cs Iwesun.Runtime.Cli/RuntimeCliSystemConfig.json Iwesun.Runtime.Cli/RuntimeCliSystemMetadata.json Iwesun.Runtime.FunctionalTests/RemoteConsoleScenario.cs
git commit -m "feat: connect cli shell to remote console"
```

## Task 8: Debug as an actual Windows Service

**Files:**
- Create: `docs/IWESUN_RUNTIME_REMOTE_CONSOLE.md`
- Modify: `Iwesun.Runtime.RemoteConsole/Program.cs`
- Modify: `Iwesun.Runtime.RemoteConsole/RemoteConsoleOptions.cs`

- [ ] **Step 1: Publish the service**

Run:

```powershell
dotnet publish Iwesun.Runtime.RemoteConsole/Iwesun.Runtime.RemoteConsole.csproj -c Debug -r win-x64 --self-contained false
```

Expected: publish succeeds and contains the service executable, Protocol, Diagnostics and WindowsServices dependency.

- [ ] **Step 2: Document manual SCM installation**

The manual must instruct an administrator to create the real AI account first, grant `Log on as a service`, then install the service with Windows service tools using an interactively supplied credential. Do not place a password in source, JSON, command history or a checked-in script.

- [ ] **Step 3: Install and start under the designated account**

From an elevated interactive PowerShell, install the published executable as `Iwesun.Runtime.RemoteConsole`, assign the prepared AI account, start it, and verify SCM reports Running. This is an environment-gated manual step; if administrator authority or the account is absent, record it as not executed rather than passing it.

- [ ] **Step 4: Run real service smoke commands**

From a different approved client identity:

```text
node auth atlas --user Atlas\IwesunAiDiag
console target add atlas-console Atlas Iwesun.Runtime.RemoteConsole
console target use atlas-console
workspace.create
console.submit --workspace <id> --shell powershell -- whoami
console.follow <jobId>
```

Expected: `whoami` reports the configured service account, output arrives in sequence, and the service remains Running after command completion.

- [ ] **Step 5: Verify stop/start semantics**

Stop the service while an intentionally long command is Running. Expected: service stop completes; Job is not reported Completed/Cancelled; restarting the service does not replay it. Confirm any surviving child process limitation is reported honestly.

- [ ] **Step 6: Uninstall the debug service**

Stop and delete only the manually created `Iwesun.Runtime.RemoteConsole` service. Keep the prepared Windows account unchanged.

- [ ] **Step 7: Commit the manual**

```powershell
git add docs/IWESUN_RUNTIME_REMOTE_CONSOLE.md Iwesun.Runtime.RemoteConsole/Program.cs Iwesun.Runtime.RemoteConsole/RemoteConsoleOptions.cs
git commit -m "docs: add remote console service debugging"
```

## Task 9: Synchronize skill, release and installer payload

**Files:**
- Create: `skills/iwesun-runtime-integration/references/remote-console.md`
- Modify: `skills/iwesun-runtime-integration/SKILL.md`
- Modify: `docs/README.md`
- Modify: `docs/REQUIREMENTS_ACTIVE.md`
- Modify: `Iwesun.Runtime.Release/Iwesun.Runtime.Release.csproj`
- Modify: `Iwesun.Runtime.Setup/Iwesun.Runtime.Setup.wixproj`
- Modify: `scripts/release/verify-runtime-install.ps1`

- [ ] **Step 1: Write the operating skill reference**

Route RemoteConsole tasks from `SKILL.md` to a concise reference covering service identity, console targets, upload, submit/follow, approval separation, output truncation, `exit/quit`, and restore/cleanup steps.

- [ ] **Step 2: Add release payloads**

Publish Protocol and Service under `app/bin/Iwesun.Runtime.RemoteConsole`, copy the manual and skill, and keep Manager absent until its later plan. The MSI installs files only; it must not create accounts, assign passwords or automatically register/start the service.

- [ ] **Step 3: Strengthen install verification**

Assert service EXE/runtimeconfig, Protocol DLL, remote console manual, CLI catalog commands and skill reference exist. Assert their FileVersion/ProductVersion match Diagnostics and WebView2.

- [ ] **Step 4: Validate the skill**

Run:

```powershell
python C:\Users\LYH\.codex\skills\.system\skill-creator\scripts\quick_validate.py D:\Git Space\Runtime\skills\iwesun-runtime-integration
```

Expected: `Skill is valid!`

- [ ] **Step 5: Commit**

```powershell
git add skills/iwesun-runtime-integration docs/README.md docs/REQUIREMENTS_ACTIVE.md Iwesun.Runtime.Release/Iwesun.Runtime.Release.csproj Iwesun.Runtime.Setup/Iwesun.Runtime.Setup.wixproj scripts/release/verify-runtime-install.ps1
git commit -m "release: include remote console service"
```

## Task 10: Final regression and release gate

**Files:**
- Modify only files required by failures found in this task.

- [ ] **Step 1: Run focused Debug scenarios**

```powershell
dotnet run --project Iwesun.Runtime.FunctionalTests -c Debug -- --child --scenario remote-console-frame-codec
dotnet run --project Iwesun.Runtime.FunctionalTests -c Debug -- --child --scenario remote-console-approval
dotnet run --project Iwesun.Runtime.FunctionalTests -c Debug -- --child --scenario remote-console-command
dotnet run --project Iwesun.Runtime.FunctionalTests -c Debug -- --child --scenario remote-console-workspace
dotnet run --project Iwesun.Runtime.FunctionalTests -c Debug -- --child --scenario remote-console-cli
```

Expected: all five scenarios pass and leave no dotnet child process.

- [ ] **Step 2: Run existing regression scenarios**

```powershell
dotnet run --project Iwesun.Runtime.FunctionalTests -c Debug -- --child --scenario cli-context-shell
dotnet run --project Iwesun.Runtime.FunctionalTests -c Debug -- --child --scenario cli-transport-failure
dotnet run --project Iwesun.Runtime.FunctionalTests -c Debug -- --child --scenario sample-host-cli-full
```

Expected: all pass; existing Diagnostics/SampleHost behavior is unchanged.

- [ ] **Step 3: Build Debug and Release**

```powershell
dotnet build Iwesun.Runtime.slnx -c Debug
dotnet build Iwesun.Runtime.slnx -c Release
```

Expected: 0 errors; Release 0 warnings; only the already documented Debug breakpoint CA1416 warnings may remain.

- [ ] **Step 4: Audit logical safety**

Verify no password fields, no Console.WriteLine diagnostics, no broad reflection invocation, no path traversal, no self-approval, no duplicate RequestId launch, no automatic replay, no running-job false cancellation, and no RemoteConsole action routed to a business Diagnostics pipe.

- [ ] **Step 5: Build the full package only after SCM smoke passes or is marked environment-blocked**

Run the unique release script with the next approved numeric version. Expected: staging verification and MSI succeed, and the final report distinguishes automated tests from the real Windows Service account test.

- [ ] **Step 6: Commit any final verification fixes**

Stage only RemoteConsole-related fixes; do not include unrelated WebView2 or consumer worktree changes.

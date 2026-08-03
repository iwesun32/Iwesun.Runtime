# Runtime CLI Shell and Remote Node Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build one authoritative CLI context/target resolver and use it for local and remote Windows Runtime named-pipe diagnostics.

**Architecture:** Introduce focused Node, Target and resolved-target types, then route both one-shot commands and the Shell through the same resolver. Keep transport responsible only for framed I/O and stable connection errors; keep Windows authentication/ACL and multi-node fan-out in separate bounded components.

**Tech Stack:** C#/.NET 10, `NamedPipeClientStream`, `System.Text.Json`, existing Runtime Frame protocol and FunctionalTests executable.

---

### Task 1: Freeze context and target behavior with tests

**Files:**
- Modify: `Iwesun.Runtime.FunctionalTests/CliContextShellScenario.cs`
- Modify: `Iwesun.Runtime.FunctionalTests/CliTransportFailureScenario.cs`

- [ ] Add Shell cases for variable expansion in `target add`, `cd`, remote arguments and `@${target}`.
- [ ] Assert an undefined variable returns `CLI_CONTEXT_VARIABLE_NOT_FOUND` without a server request.
- [ ] Assert `target use` affects `get`, `ls` and ordinary commands while `@target` remains one-shot.
- [ ] Add transport cases proving server name and pipe name are separate and local `.` remains compatible.
- [ ] Run focused context and transport scenarios and confirm the new assertions fail before implementation.

### Task 2: Add Node/Target configuration and immutable resolution

**Files:**
- Create: `Iwesun.Runtime.Cli/CliRuntimeTargetResolver.cs`
- Modify: `Iwesun.Runtime.Cli/CliV3.cs`
- Modify: `Iwesun.Runtime.Cli/RuntimeCliSystemConfig.json`
- Modify: `Iwesun.Runtime.Cli/RuntimeCliUserConfig.example.json`

- [ ] Define `CliNode`, extended `CliTarget` and `ResolvedRuntimeTarget` with non-sensitive connection fields.
- [ ] Merge and validate `nodes`; inject built-in `local` and bind legacy targets without `node` to it.
- [ ] Reject UNC pipe names, missing nodes, incomplete `--server/--pipe`, and conflicting `--target` overrides.
- [ ] Resolve timeout precedence as Target, Node, Endpoint, then built-in default.
- [ ] Support `--target=alias` and `--server=name --pipe=name` in one-shot mode.
- [ ] Run focused configuration and transport scenarios.

### Task 3: Replace Shell branch-specific context handling

**Files:**
- Create: `Iwesun.Runtime.Cli/CliShellContext.cs`
- Modify: `Iwesun.Runtime.Cli/CliInteractiveShell.cs`
- Modify: `Iwesun.Runtime.Cli/CliTargetContext.cs`
- Modify: `Iwesun.Runtime.Cli/RuntimeVirtualPathRouter.cs`

- [ ] Preserve token quote metadata and expand all eligible tokens immediately after tokenization.
- [ ] Keep original input in history and exclude future secret-input commands from history.
- [ ] Resolve `@target`, current Target and startup Target in one method.
- [ ] Pass the same resolved Target to ordinary commands and virtual-path commands.
- [ ] Implement `node add/list/show/test/remove` and enhanced `target add/show/test/use/current/remove` without positional ambiguity.
- [ ] Make `clear` restore the startup target and path without deleting configured nodes or targets.
- [ ] Run all Shell context scenarios.

### Task 4: Remote transport and stable errors

**Files:**
- Modify: `Iwesun.Runtime.Cli/CliTransport.cs`
- Create: `Iwesun.Runtime.Cli/CliRemoteErrorClassifier.cs`

- [ ] Construct `NamedPipeClientStream` with separate `ServerName` and `PipeName`.
- [ ] Preserve exact reads for header and payload and include resolved target data in failures.
- [ ] Map Windows access denied, credential conflict, node unreachable, missing pipe, connect timeout and protocol truncation to stable CLI codes.
- [ ] Never include credentials or command payloads in transport errors.
- [ ] Add `node test` and `target test` read-only probes using the same resolver and transport.
- [ ] Run local failure tests and remote tests when Atlas is reachable.

### Task 5: Large-frame correctness

**Files:**
- Modify: `Iwesun.Runtime.Diagnostics/RuntimeDiagnosticsMonitor.cs`
- Modify: `Iwesun.Runtime.FunctionalTests/Program.cs`
- Create: `Iwesun.Runtime.FunctionalTests/RemotePipeFrameScenario.cs`

- [ ] Add deterministic responses above 64 KiB and 1 MiB in the functional host path.
- [ ] Verify client reads exactly the declared length and reports truncation distinctly.
- [ ] Verify server completes header/payload writes and Flush before releasing the pipe.
- [ ] Run local large-frame tests; run the same client against a remote Windows node when available.

### Task 6: Windows authentication and ACL boundary

**Files:**
- Create: `Iwesun.Runtime.Cli/CliWindowsNodeSession.cs`
- Modify: `Iwesun.Runtime.Diagnostics/RuntimeNamedPipeServerFactory.cs`
- Modify: `scripts/release/verify-runtime-install.ps1`

- [ ] Implement Windows-only session inspection and explicit auth/logout entry points without a password option.
- [ ] Refuse secret input when standard input is redirected; never record auth input in history.
- [ ] Require explicit confirmation for logout and never disconnect sessions automatically.
- [ ] Resolve the local `Iwesun Runtime Operators` group SID when present and grant read/write connection rights.
- [ ] Preserve SYSTEM, NetworkService and Administrators full control; keep migration compatibility explicit.
- [ ] Validate locally and document which remote account/host checks require an administrator-prepared environment.

### Task 7: Bounded multi-node read-only coordination

**Files:**
- Create: `Iwesun.Runtime.Cli/CliMultiTargetCoordinator.cs`
- Modify: `Iwesun.Runtime.Cli/CliV3.cs`
- Modify: `Iwesun.Runtime.Cli/RuntimeCliSystemConfig.json`
- Modify: `Iwesun.Runtime.Cli/RuntimeCliSystemMetadata.json`

- [ ] Add a read-only coordination command accepting explicit target aliases and concurrency 1-16.
- [ ] Reject destructive/state-changing commands before any connection begins.
- [ ] Execute each target with isolated timeout/cancellation and preserve each complete Runtime Frame.
- [ ] Return one JSON result with target-level status and aggregate counts.
- [ ] Test mixed success, timeout and missing-target outcomes without cancelling successful targets.

### Task 8: Documentation, skill and release synchronization

**Files:**
- Modify: `docs/IWESUN_RUNTIME_CLI.md`
- Modify: `docs/IWESUN_RUNTIME_USER_GUIDE.md`
- Modify: `docs/REQUIREMENTS_ACTIVE.md`
- Modify: `skills/iwesun-runtime-integration/SKILL.md`
- Modify: `skills/iwesun-runtime-integration/references/json-cli.md`
- Modify: `Iwesun.Runtime.Release/Iwesun.Runtime.Release.csproj`

- [ ] Document Node/Target resolution, Shell expansion order, remote error codes and credential boundaries.
- [ ] Add complete local, remote and multi-node examples, including `exit/quit` and explicit shutdown separation.
- [ ] Synchronize repository and installed skill copies and validate byte equality.
- [ ] Build Debug and Release; run context, transport, large-frame, SampleHost and install-verification scenarios.
- [ ] Restore diagnostics to quiet and record unavailable environment-dependent remote checks honestly.

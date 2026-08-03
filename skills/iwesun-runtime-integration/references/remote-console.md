# RemoteConsole service and CLI

## Boundary

Use `Iwesun.Runtime.RemoteConsole` when an AI or operator must submit approved shell commands to another Windows machine. It is a separate Windows service and pipe. It does not replace a host Diagnostics pipe, create Windows accounts, store credentials, install packages, or stop a business host when the CLI Shell exits.

The current fast-adaptation release provides the service and CLI. A management UI may observe or approve through the same protocol later, but is not part of the current operating path.

## Source declarations

Before publishing the service, set `PipeName`, `SubmitterPrincipals`, `ApproverPrincipals`, and `ApprovalMode` together in `modules/RemoteConsole/src/Iwesun.Runtime.RemoteConsole/Program.cs`.

Use Windows account names or SID strings. The service resolves them during startup and refuses invalid or empty role sets. Production submitters and approvers should be different identities. Runtime creates only pipe ACLs; an administrator must create the real account, assign “Log on as a service,” and install the service with an interactively supplied credential.

Never put a password in source, JSON, arguments, environment variables, Frames, logs, skills, or command history.

## Connect a node and console target

```text
iwrt --interactive
node add atlas Atlas
node auth atlas --user Atlas\IwesunAiDiag
console target add atlas-console atlas Iwesun.Runtime.RemoteConsole
console target use atlas-console
console target current
console.info
```

`console target` maintains the current RemoteConsole target independently of ordinary Diagnostics `target`. `node auth` must remain interactive and excluded from history. `exit` and `quit` close only the local Shell; they do not stop the service, submitted job, or business host.

## Workspace and upload

```text
workspace.create deployment
workspace.list
file.upload <workspaceId> C:\staging\package.zip package.zip
console.file.list <workspaceId>
workspace.show <workspaceId>
```

`file.upload` hashes the local file and sends bounded `begin/chunk/commit` requests. The server validates sequence, declared length, quota and SHA-256 before atomic placement. Do not bypass the workspace boundary with absolute paths, UNC paths, ADS, `..`, or reparse points.

This is a one-way local-to-remote workspace upload. There is no `file.download` command in the current release. To copy or expand an uploaded file elsewhere on the remote machine, submit an approved PowerShell command such as `Copy-Item` or `Expand-Archive`; the service account's Windows ACL remains authoritative.

## Submit, approve, and follow

```text
console.submit --workspace <workspaceId> --shell powershell -- whoami
console.status <jobId>
console.pending
console.approve <jobId>
console.reject <jobId> <reason>
console.follow <jobId>
console.cancel <jobId>
```

Supported shells are `powershell` and `cmd`. The service authenticates the Windows SID captured from the pipe connection; JSON identity fields are not trusted. Request IDs are idempotent, submitted command hashes are immutable, and an identity without the approver role cannot decide a job.

Approval modes:

- `Manual`: every valid submit waits;
- `Guarded`: deny rules win, allow rules may auto-approve, all other jobs wait;
- `Automatic`: valid jobs auto-approve after deny checks.

Use `console.policy.get` and approver-only `console.policy.set` to inspect or change the current process policy.

`console.follow` returns sequenced stdout/stderr. If `truncated=true`, output is incomplete; use `earliestAvailableSequence` and do not claim a complete transcript.

## Stop and cleanup

- Service stop stops admission and marks unfinished jobs `Interrupted`; it does not report false `Completed` or `Cancelled`.
- Restart does not replay old jobs.
- Remove temporary workspaces with `workspace.remove <workspaceId>` when the job is complete; the service also expires old workspaces.
- Close the Windows IPC session only when all tools using that logon session are finished: `node logout <alias> --confirm`.
- Restore approval mode to the deployment default after temporary testing.

The fast-adaptation release does not guarantee termination of an external child process that has escaped the service lifetime. Report that limitation instead of claiming process-tree cleanup.

## Verification

```powershell
dotnet run --project modules/Diagnostics/tests/Iwesun.Runtime.FunctionalTests/Iwesun.Runtime.FunctionalTests.csproj -c Debug -- --child --scenario remote-console-cli
```

For a real SCM smoke test, install under the designated service account, run `whoami` through `console.submit`, verify the returned identity, then stop/start the service and confirm SCM state. If administrator authority or interactive credentials are unavailable, mark this test environment-blocked.

See `docs/IWESUN_RUNTIME_REMOTE_CONSOLE.md` for the complete administrator procedure.

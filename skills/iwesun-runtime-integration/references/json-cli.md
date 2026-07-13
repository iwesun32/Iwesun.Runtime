# JSON and CLI

## Wire format

Use exactly:

```text
[4-byte little-endian signed int32 payload length][UTF-8 JSON]
```

Read until the complete 4-byte header and payload length are satisfied. Never assume one `ReadAsync` returns a complete frame. Prefer `Iwesun.Runtime.Cli` instead of hand-written clients.

## Protocols

- `rtdiag/2.0`: one request command and one response frame.
- `rtdiag/3.0`: one structured batch with 1-128 steps, conditions, bindings, delays, deadline, and per-step results.

The frame header owns schema, frame type, category, operation, request/correlation IDs, timestamp, source, and destination. Commands own domain, target, action, member, and typed JSON arguments. Responses own status, data, extended status, and metadata.

## Pipe names

Keep the requested pipe name and resolved pipe name separate:

- `RequestedPipeName`: caller intent.
- `ResolvedPipeName`: actual collision-free name.

Resolved duplicates use `_001`, `_002`, and so on. Do not claim uniqueness unless the pipe host atomically holds the exclusive server instance and registers the lease.

## CLI v3 user extensions

Use `RuntimeCliSystemConfig.json` for routes, `RuntimeCliSystemMetadata.json` for help metadata, and `RuntimeCliUserConfig.json` for current-directory user extensions. Explicit `--user-config` replaces the default user file. Use `iwrt shell` for client-only context, variables, and virtual paths. `exit` and `quit` only close the shell and never stop the host. Composite output remains one complete JSON Runtime Frame.

For multiple hosts, use `target add/list/use/current/remove` or `@name command`. Never guess a similar pipe. Connection, write, response, and local-cancellation failures have distinct codes. Prefer `host.summary` and bounded list commands for routine inspection; use detailed snapshots only explicitly.

CLI v3 is current. The standard catalog remains canonical and contains no product-specific aliases. Load process-local user extensions explicitly:

```powershell
iwrt --user-config="C:\ProgramData\Product\runtime-cli.user.json" diagnostics.status
```

The user file supports `extensions.add`, `extend`, `replace`, and `disable`. Put aliases in `commands[].aliases` for complete catalogs or in `extensions.extend[].aliases` for incremental files. Missing targets and alias collisions are configuration errors.

## Composite commands

Use `composites` to package existing commands into one `rtdiag/3.0` batch request:

```json
{
  "name": "diagnostics.quick-check",
  "aliases": ["diag.quick"],
  "stopOnError": false,
  "steps": [
    { "command": "host.info" },
    { "command": "switchboard.get" },
    { "command": "host.events", "arguments": ["20"] }
  ]
}
```

All steps must use one endpoint. Composites do not provide workflow branches, loops, result bindings, or script strings.

## Current focused commands

- `reflection.get <target> [member]` routes directly to an explicitly registered target.
- Debug-only `reflection.invoke <target> <member>` calls only a parameterless method explicitly listed in `InvokableMembers`; Release Diagnostics does not compile the invoke route.
- `switchboard.point.list [id]` queries compiled and runtime-discovered points.
- `switchboard.point.enable <id>` and `switchboard.point.disable <id>` control one point in the current process.
- `breakpoint.list` returns `LastContext` in Debug when a breakpoint has captured context.

## Live-debug sequence

1. Query status and registries.
2. Enable the smallest required section or point set.
3. Trigger one focused business action.
4. Read structured events and frames.
5. Explicitly resume waiting breakpoints.
6. Detach hooks.
7. Run quiet or restore the previous switchboard state.
8. Send safe shutdown only when host termination is intended.

CLI disconnect neither resumes breakpoints nor stops the host.

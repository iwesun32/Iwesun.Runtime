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

## CLI version gate

CLI v3 is approved but remains migration-only until the source contains `Iwesun.Runtime.Cli.commands.json` with schema `iwesun.runtime.cli/3.0`. Until then:

- Do not present v3 commands as executable.
- Treat v2 command and configuration documentation as deprecated migration reference.
- Do not add new v2 aliases, string workflow steps, or compatibility code.

## Live-debug workflow

1. Query status and registries.
2. Enable the smallest required section or point set.
3. Trigger one focused business action.
4. Read structured events and frames.
5. Explicitly resume waiting breakpoints.
6. Detach hooks.
7. Run quiet or restore the previous switchboard state.
8. Send safe shutdown only when host termination is intended.

CLI disconnect neither resumes breakpoints nor stops the host.

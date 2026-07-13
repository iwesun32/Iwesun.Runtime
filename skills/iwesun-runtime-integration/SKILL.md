---
name: iwesun-runtime-integration
description: Integrate or migrate .NET hosts to Iwesun Runtime. Use when replacing Program.cs startup, converting Process/Thread/Task creation to RProcess/RThread/RTask, adding RuntimeInjector output/watch/break/data points, implementing managed state/events/coordinated shutdown, or operating Runtime pipes and CLI v3 commands.
---

# Iwesun Runtime Integration

Follow the repository's `AGENTS.md`, active requirements, and access rules before changing a host. Treat the Runtime source and tested SampleHost as authoritative over historical documentation.

## Route the task

Read only the references needed for the request:

- Replace or audit `Program.cs`: [references/host-startup.md](references/host-startup.md)
- Replace Process, Thread, or Task creation: [references/managed-execution.md](references/managed-execution.md)
- Add output, watch, breakpoint, numeric breakpoint, event, or reflection injection: [references/diagnostic-injection.md](references/diagnostic-injection.md)
- Implement state, events, guardians, or shutdown: [references/state-shutdown-events.md](references/state-shutdown-events.md)
- Work with frames, named pipes, or CLI: [references/json-cli.md](references/json-cli.md)

For a full host migration, read the references in the order listed above.

## Workflow

1. Inspect the host entry point and direct Process/Thread/Task creation sites.
2. Record requirements before implementation when the repository requires it.
3. Replace startup with `Services.Start(...)` and `Services.Activate(...)`.
4. Replace eligible execution primitives with `RProcess`, `RThread`, and `RTask`.
5. Add diagnostics as continuous single-point blocks at the business location.
6. Keep reflection access explicitly whitelisted.
7. Implement both global-state polling and FIFO Stop/Wakeup handling.
8. Verify deregistration occurs only after actual process, thread, or task completion.
9. Build Debug and Release, then run focused and end-to-end tests.
10. Restore diagnostics to quiet after live inspection.

## Safety boundaries

- Never add diagnostic `Console.WriteLine` calls or temporary log files.
- Keep diagnostics silent by default unless the host has an explicit startup recording requirement.
- Put breakpoint and numeric-breakpoint declarations and calls behind `#if DEBUG`.
- Do not expose broad reflection invocation; list readable, writable, and invokable members explicitly.
- Send fixed-width simple state and control values through FIFO; send complex data through framed JSON pipes.
- Do not unregister a live process, thread, or task merely because Dispose or a timeout occurred.
- Treat CLI disconnect, breakpoint resume, and host shutdown as independent actions.
- Use 4-byte little-endian length-prefixed UTF-8 JSON; do not hand-roll clients when the Runtime CLI is available.
- Treat CLI v3, user aliases, incremental extensions, and composite batch commands as current supported features.
- Runtime does not read or write diagnostic-switchboard.json; source declarations are fixed and CLI controls are process-local.

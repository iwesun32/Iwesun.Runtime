# Diagnostic injection

## Assembly declarations

```csharp
[assembly: DiagnosticPipePrefix("Product")]
[assembly: DiagnosticFileOutput(
    "logs/product-diagnostics.jsonl",
    FileWriteMode.CreateNew,
    Format = DiagnosticFileFormat.CompactJson)]
[assembly: DiagnosticWatchPoint(
    "product.worker.snapshot", "worker", "snapshot",
    "Worker state snapshot.", "Workers/Worker.cs")]

#if DEBUG
[assembly: DiagnosticBreakpoint(
    "product.worker.pause", "worker",
    "Pause before commit.", "Workers/Worker.cs",
    Enabled = false)]
[assembly: DiagnosticNumericBreakpoint(
    "product.worker.depth", "gt", 100)]
#endif

[assembly: DiagnosticHookableEvent(
    "product.worker.completed",
    typeof(WorkerState),
    nameof(WorkerState.Completed))]
```

## Single-point runtime block

Keep the ID, condition, context, and calls together at the business location.

```csharp
var snapshot = state.Snapshot();

RuntimeInjector.Output(
    "product.worker.tick",
    "worker",
    "tick",
    "Worker cycle completed.",
    snapshot);

RuntimeInjector.Watch(
    "product.worker.snapshot",
    snapshot,
    nameof(WorkerSnapshot));

#if DEBUG
var breakpointContext = new { snapshot.BatchId, snapshot.QueueDepth };
await RuntimeInjector.Break(
    "product.worker.pause",
    () => snapshot.RequiresInspection,
    breakpointContext);
await RuntimeOutput.BreakIfNumbers(
    "product.worker.depth",
    snapshot.QueueDepth,
    100,
    breakpointContext);
#endif
```

## Reflection whitelist

```csharp
RuntimeInjector.Data(hub, "product.worker", state, new RuntimeDiagnosticObjectAccess
{
    AllowReadAllPublic = false,
    ReadableMembers = ["Phase", "QueueDepth", "UpdatedAt", "Snapshot"],
    WritableMembers = ["PauseRequested"],
    InvokableMembers = ["Snapshot"]
});
```

## Build behavior

- Debug: output, watch, hooks, reflection, pipes, files, shutdown, breakpoints, numeric breakpoints.
- Release: output, logs, hooks, reflection whitelist, pipes, files, state, execution registries, shutdown.
- Release does not compile or assemble breakpoint behavior.

CLI disconnect does not resume a breakpoint. Resume is explicit. Restore enabled points, sections, hooks, pipe/file switches, and global state after focused debugging.

Dynamic watch/output points enter the switchboard catalog on first observation and remain disabled by default. Use `switchboard.point.list`, `switchboard.point.enable`, and `switchboard.point.disable`; an unknown ID must return `OUTPUT_POINT_NOT_FOUND`.

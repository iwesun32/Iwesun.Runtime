# State, events, and shutdown

## State layers

- Global lifecycle: `RuntimeManagedRegistry.GlobalLifecycleState` and `RuntimeStateManager`.
- Managed unit state: `IRManagedState.TransitionTo` / `TryTransitionTo`.
- Business detail: `SetDetail`, `TryGetDetail`, and `RuntimeStateHistory` backed by RecordStore.

```csharp
unit.State.SetDetail("scanPhase", "enumerating");
if (!unit.State.TryTransitionTo("Working"))
{
    // Handle an invalid transition without corrupting the state machine.
}
```

A successful managed transition syncs the state and publishes a simple numeric state instruction to the controller FIFO. Use framed JSON pipes for large or structured context.

## Events

Declare normal .NET events at the business object, then register hookable events at assembly level:

```csharp
[assembly: DiagnosticHookableEvent(
    "product.worker.completed",
    typeof(WorkerState),
    nameof(WorkerState.Completed))]
```

Instance hooks use weak references. Explicitly detach static events during shutdown because static publishers outlive ordinary instances.

## Guardian behavior

Every process, thread, or task guardian must support both:

1. Polling the global Stop/Exit/Completed/Timeout shutdown domain as a fallback.
2. Receiving FIFO Stop/Wakeup for prompt event-driven response.

Make cleanup idempotent because both paths may trigger nearly simultaneously.

## Coordinated shutdown

1. Publish global Stop and an exit deadline.
2. Enumerate registrations and send FIFO Stop plus Wakeup to each unit.
3. Each flat Root registration transitions through Requested and Draining while all CleanupRequested handlers run; no handlers means immediate completion.
4. Transition to Completed and release the lightweight exit signal after cleanup, or transition to Timeout at the single controller deadline.
5. The managed entry point reads the shared state, returns 0 or 124, and deregisters only after actual completion.
6. Wait until process/thread/task registrations and relevant Runtime state histories are empty; return 0. If the deadline expires with pending units, return 124.

Only registrations with `BlocksShutdown=true` participate in the exit barrier. Shutdown watchers and dispatch infrastructure must register with `blocksShutdown: false`; they remain observable but cannot create a wait-for-self cycle.

Active `UnitId` values are unique. A duplicate registration or a repeated/concurrent `Start` on the same wrapper is rejected before it can replace or unregister the current owner. `RThread` and `RProcess` expose an init-only `BlocksShutdown` property for observer infrastructure.

Caller cancellation only cancels that caller's wait. Once accepted, coordinated shutdown continues independently to the first frozen deadline. Non-graceful termination is not part of this contract.

Use the same async cleanup hook on `RProcess`, `RThread`, and `RTask`:

```csharp
unit.CleanupRequested += async (_, _, cancellationToken) =>
    await FlushBusinessStateAsync(cancellationToken);
```

The hook returns `ValueTask`; never use `async void`. With no handlers, cleanup completes immediately. The shared RuntimeState code is authoritative and the lightweight exit signal only wakes the managed entry point.

```csharp
var coordinator = services.GetRequiredService<RuntimeShutdownCoordinator>();
var result = await coordinator.ShutdownAsync(
    TimeSpan.FromSeconds(15),
    payload: "operator-request",
    cancellationToken);
Environment.ExitCode = result.ExitCode;
```

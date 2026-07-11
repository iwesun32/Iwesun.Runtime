# State, events, and shutdown

## State layers

- Global lifecycle: `RuntimeManagedRegistry.GlobalLifecycleState` and `RuntimeStateManager`.
- Managed unit state: `IRManagedState.TransitionTo` / `TryTransitionTo`.
- Business detail: `SetDetail`, `TryGetDetail`, and subtask-state DLIST history.

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

1. Polling the global Stop/Exit state as a fallback.
2. Receiving FIFO Stop/Wakeup for prompt event-driven response.

Make cleanup idempotent because both paths may trigger nearly simultaneously.

## Coordinated shutdown

1. Publish global Stop and an exit deadline.
2. Enumerate registrations and send FIFO Stop plus Wakeup to each unit.
3. Let each guardian clean business resources and transition itself to Stop.
4. Deregister handlers, leases, reflection targets, and the unit only after actual completion.
5. Wait until process/thread/task registrations and relevant DLIST containers are empty; return 0.
6. If the deadline expires with pending units, return 124.

```csharp
var coordinator = services.GetRequiredService<RuntimeShutdownCoordinator>();
var result = await coordinator.ShutdownAsync(
    TimeSpan.FromSeconds(15),
    payload: "operator-request",
    cancellationToken);
Environment.ExitCode = result.ExitCode;
```

# Managed execution

## Replacement matrix

`RuntimeInjector.Thread` and `RuntimeInjector.Task` only create descriptive records. They do not create or own execution. `RThread`/`RTask` and the `CreateThread`/`CreateTask` factories are managed execution. `BackgroundService.ExecuteAsync` remains Generic Host-owned.

| Existing creation | Runtime creation | Returned type |
| --- | --- | --- |
| `Process.Start(...)` | `RuntimeInjector.CreateProcess(...)` | `RProcess` |
| `new Process()` | `new RProcess(unitId)` | `RProcess` |
| `new Thread(...)` | `RuntimeInjector.CreateThread(...)` | `RThread` |
| `Task.Run(...)` / `new Task(...)` | `RuntimeInjector.CreateTask(...)` | `RTask` |

Use stable, unique unit IDs such as `product.process.scanner`, `product.thread.worker`, and `product.task.refresh`.

## RProcess

```csharp
using RProcess process = RuntimeInjector.CreateProcess(
    "worker.exe",
    "--scan",
    unitId: "product.process.scanner",
    startImmediately: true);

await process.WaitForExitAsync(cancellationToken);
```

- Start failure rolls back registrations, reflection targets, and branch-pipe leases.
- Normal exit deregisters after the OS process exits.
- Dispose requests Stop and waits before force termination; it must not hide a live OS process.

## RThread

```csharp
using RThread thread = RuntimeInjector.CreateThread(
    WorkerLoop,
    unitId: "product.thread.worker",
    name: "Product Worker",
    lifetime: RuntimeExecutionLifetime.Static,
    kind: RuntimeThreadKind.Worker,
    startImmediately: false);

thread.Start();
thread.Join(TimeSpan.FromSeconds(10));
```

`RThread` uses composition because `Thread` is sealed. Stop requests wake the guardian. A timeout keeps the registration until the thread actually exits.

## RTask

```csharp
using RTask task = RuntimeInjector.CreateTask(
    token => RunLoop(token),
    unitId: "product.task.loop",
    cancellationToken: cancellationToken,
    startImmediately: true);

await task;
```

`RTask` wraps a Task and supports await through `GetAwaiter`; it is not a Task subclass or `Task<T>`. Prefer the token-aware overload so FIFO Stop can request cancellation. Dispose while running defers cleanup and deregistration until completion.

Use `WaitForWakeupAsync` for long waits that must react immediately to FIFO Wakeup. A Wakeup ends only the current wait; it does not request Stop.

Infrastructure tasks that coordinate shutdown but must not wait for themselves use:

```csharp
using RTask shutdownWatch = RuntimeInjector.CreateTask(
    token => WatchShutdown(token),
    unitId: "product.task.shutdown-watch",
    cancellationToken: cancellationToken,
    blocksShutdown: false);
```

Business tasks keep the default `blocksShutdown: true`.

## Do not mechanically replace

Review `Task<T>` result flows, UI/STA threads, custom schedulers, native handles, and code that casts objects to native Task or Process types. Refactor ownership explicitly before converting these sites.

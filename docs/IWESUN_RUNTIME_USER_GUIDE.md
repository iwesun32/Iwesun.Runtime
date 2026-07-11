# Iwesun Runtime 用户手册

> **状态**：CURRENT  
> **适用**：.NET 10 业务宿主  
> **CLI v3**：迁移中，本文不将未实施命令标记为当前可用  
> **源码样例**：`Iwesun.Runtime.SampleHost`

本手册按实际接入顺序说明如何把现有业务程序改造为 Iwesun Runtime 宿主。架构原理参见 [IWESUN_RUNTIME_DESIGN.md](IWESUN_RUNTIME_DESIGN.md)，诊断内核参见 [RUNTIME_DIAGNOSTICS.md](RUNTIME_DIAGNOSTICS.md)。

## 1. 替换程序启动主程序

### 1.1 标准 Program.cs

新宿主统一使用 `RuntimeHostTemplate.Start` 和 `Activate`。不再在业务主程序里分散调用 `AddRuntimeDiagnostics`、`UseRuntimeDiagnostics` 和 `BuildDiagnosticRegistries`。

```csharp
using System.Reflection;
using Iwesun.Runtime.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Fixed diagnostics declarations.
[assembly: DiagnosticPipePrefix("MyProduct")]
[assembly: DiagnosticFileOutput(
    "logs/my-product-diagnostics.jsonl",
    FileWriteMode.CreateNew,
    Format = DiagnosticFileFormat.CompactJson)]

var builder = Host.CreateApplicationBuilder(args);

// Fixed Runtime host template.
builder.Logging.AddRuntimeDiagnostics();

var runtimeDirectory = Path.Combine(AppContext.BaseDirectory, "runtime");
const string pipeArgument = "--runtime-diagnostics-pipe=";
var startupPipeName = args
    .FirstOrDefault(x => x.StartsWith(pipeArgument, StringComparison.OrdinalIgnoreCase))?
    [pipeArgument.Length..];

builder.Services.Start(
    runtimeDirectory,
    startupRuntimeDiagnosticsPipeName: startupPipeName);

// Business registration area.
builder.Services.AddSingleton<MyBusinessState>();
builder.Services.AddHostedService<MyBusinessWorker>();

using var host = builder.Build();

// Fixed activation point: starts diagnostics and builds the host registries.
host.Services.Activate(Assembly.GetExecutingAssembly());

// Optional business reflection registration. Keep an explicit whitelist.
var hub = host.Services.GetRequiredService<RuntimeDiagnosticHub>();
var businessState = host.Services.GetRequiredService<MyBusinessState>();
RuntimeInjector.Data(hub, "my-product.state", businessState, new RuntimeDiagnosticObjectAccess
{
    AllowReadAllPublic = false,
    ReadableMembers = ["CurrentPhase", "UpdatedAt", "Snapshot"],
    WritableMembers = ["PauseRequested"],
    InvokableMembers = ["Snapshot"]
});

await host.RunAsync();
```

### 1.2 固定区与业务区

| 内容 | 位置 | 是否按业务修改 |
| --- | --- | --- |
| `Logging.AddRuntimeDiagnostics()` | Builder 创建后 | 否 |
| `Services.Start(...)` | DI 构建期 | 只改运行目录和启动管道参数 |
| 业务 DI | `Start` 之后、`Build` 之前 | 是 |
| `Activate(hostAssembly)` | `Build` 之后 | 否 |
| `RuntimeInjector.Data` | `Activate` 之后 | 是，必须白名单 |
| `RunAsync()` | 最后 | 否 |

### 1.3 启动配置优先级

诊断主管道名和文件路径在启动期一次性决议：

1. 命令行启动参数。
2. Runtime JSON 配置。
3. 程序源码/程序集属性默认值。

路径和管道名不应在业务运行期随意修改。输出开关可以运行期调整。

## 2. 替换进程、线程和任务创建

### 2.1 统一替换表

| 原创建方式 | 标准方式 | 返回对象 |
| --- | --- | --- |
| `Process.Start(...)` | `RuntimeInjector.CreateProcess(...)` | `RProcess` |
| `new Process()` | `new RProcess(unitId)` 或 `CreateProcess` | `RProcess` |
| `new Thread(...)` | `RuntimeInjector.CreateThread(...)` | `RThread` |
| `Task.Run(...)` / `new Task(...)` | `RuntimeInjector.CreateTask(...)` | `RTask` |

`RProcess`、`RThread`、`RTask` 会自动登记执行状态、接收简单 FIFO 指令，并在真实结束后反登记。

### 2.2 进程

```csharp
// Before:
using var oldProcess = Process.Start("worker.exe", "--scan");

// After:
using RProcess process = RuntimeInjector.CreateProcess(
    fileName: "worker.exe",
    arguments: "--scan",
    unitId: "my-product.process.scanner",
    startImmediately: true);

await process.WaitForExitAsync(cancellationToken);
if (process.ExitCode != 0)
{
    throw new InvalidOperationException($"Scanner exited with {process.ExitCode}.");
}
```

如果需要先设置 `ProcessStartInfo`：

```csharp
using var process = RuntimeInjector.CreateProcess(
    new ProcessStartInfo("worker.exe", "--scan")
    {
        UseShellExecute = false,
        RedirectStandardOutput = true
    },
    unitId: "my-product.process.scanner",
    startImmediately: false);

process.Start();
```

行为要点：

- `Start()` 失败会回滚登记、反射目标和分支管道租约。
- 正常退出事件触发后才反登记。
- `Dispose()` 先请求 Stop 并等待，必要时才强制结束；不会把仍存活的 OS 进程伪装成已注销。
- 子进程使用自己的分支管道和 FIFO，不与父进程竞争同一消费句柄。

### 2.3 线程

```csharp
using RThread worker = RuntimeInjector.CreateThread(
    start: WorkerLoop,
    unitId: "my-product.thread.worker",
    name: "My Product Worker",
    lifetime: RuntimeExecutionLifetime.Static,
    kind: RuntimeThreadKind.Worker,
    owner: "my-product",
    sourceLocation: "Workers/MyWorker.cs",
    stopTimeoutMilliseconds: 5000,
    startImmediately: false);

worker.ExitRequested += (_, e) =>
{
    // Start business cleanup. Do not block this callback indefinitely.
};
worker.ExitCompleted += (_, e) =>
{
    // e.ExitCode == 0 means normal completion; 124 means timeout.
};

worker.Start();
worker.Join(TimeSpan.FromSeconds(10));
```

`RThread` 是对 `System.Threading.Thread` 的组合包装，不是继承类。Stop 会唤醒守护程序并请求线程停止。超时时保留存活登记，直到线程真正退出。

### 2.4 任务

```csharp
using RTask task = RuntimeInjector.CreateTask(
    action: asyncToken =>
    {
        while (!asyncToken.IsCancellationRequested)
        {
            RunOneBatch();
            asyncToken.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(250));
        }
    },
    unitId: "my-product.task.batch",
    category: "batch",
    threadId: "my-product.thread.worker",
    lifetime: RuntimeExecutionLifetime.Dynamic,
    sourceLocation: "Workers/BatchWorker.cs",
    cancellationToken: cancellationToken,
    startImmediately: true);

await task;
```

`RTask` 是包含内部 `Task` 的组合类。它通过 `GetAwaiter()` 支持 `await`，但不能当作 `Task<T>` 使用。带 `Action<CancellationToken>` 的构造路径能在收到 FIFO Stop 时取消。非 token-aware 任务只能等待业务自然完成。

运行中调用 `Dispose()` 不会提前反登记；释放将延迟到真实任务完成。

### 2.5 不可机械替换的场景

- 需要 `Task<T>` 返回值的调用链必须先重构结果传递。
- UI 主线程、COM STA 线程或自定义消息泵不能直接机械替换。
- 明确依赖特定 `TaskScheduler` 的代码应手动传入 scheduler。
- 依赖将对象转型为原生 `Task` 的旧代码不能直接使用 `RTask`。

## 3. 注入器、监视记录器与断点

### 3.1 单点注入定义

一个功能的 ID、条件、上下文和调用应集中在业务现场的一个连续代码段中。程序集属性负责静态目录和初始状态，运行代码负责在真实业务位置发布数据。

### 3.2 程序集属性

```csharp
using Iwesun.Runtime.Diagnostics;

[assembly: DiagnosticPipePrefix("MyProduct")]
[assembly: DiagnosticFileOutput(
    "logs/my-product-diagnostics.jsonl",
    FileWriteMode.CreateNew,
    Format = DiagnosticFileFormat.CompactJson)]

[assembly: DiagnosticWatchPoint(
    "my-product.worker.snapshot",
    "worker",
    "snapshot",
    "Worker state snapshot.",
    "Workers/MyWorker.cs")]

#if DEBUG
[assembly: DiagnosticBreakpoint(
    "my-product.worker.pause",
    "worker",
    "Pause before committing a batch.",
    "Workers/MyWorker.cs",
    Enabled = false,
    HitCountTarget = 1)]
[assembly: DiagnosticNumericBreakpoint(
    "my-product.worker.queue-depth",
    "gt",
    100)]
#endif

[assembly: DiagnosticHookableEvent(
    "my-product.worker.completed",
    typeof(MyWorkerState),
    nameof(MyWorkerState.Completed))]
```

| 属性 | 用途 | 默认行为 |
| --- | --- | --- |
| `DiagnosticPipePrefix` | 宿主管道前缀 | 每程序集一个 |
| `DiagnosticFileOutput` | 文件模板、写模式、格式 | `CreateNew` + `CompactJson` |
| `DiagnosticWatchPoint` | 静态监视点目录 | 默认静默 |
| `DiagnosticBreakpoint` | 逻辑断点 | `Enabled=false`；仅 Debug 注入 |
| `DiagnosticNumericBreakpoint` | 断点默认数值谓词 | 仅 Debug 注入 |
| `DiagnosticHookableEvent` | 可动态挂接事件目录 | 挂接状态由运行配置决定 |

Release 保留输出、日志、反射白名单、事件钩子、管道、状态、文件记录和安全退出。断点和数值断点调用必须由 `#if DEBUG` 隔离。

### 3.3 输出与 Watch

```csharp
var snapshot = state.Snapshot();

RuntimeInjector.Output(
    outputPointId: "my-product.worker.tick",
    section: "worker",
    kind: "tick",
    message: "Worker cycle completed.",
    payload: new
    {
        snapshot.CurrentPhase,
        snapshot.ProcessedCount,
        snapshot.UpdatedAt
    });

RuntimeInjector.Watch(
    watchPointId: "my-product.worker.snapshot",
    target: snapshot,
    typeName: nameof(MyWorkerSnapshot));
```

这段代码可以放在业务计算完成后的任意位置，不要为了输出而修改业务分支或返回值。

### 3.4 逻辑断点与数值断点

```csharp
#if DEBUG
var context = new
{
    batch.Id,
    QueueDepth = queue.Count,
    Limit = options.QueueLimit
};

await RuntimeInjector.Break(
    breakpointId: "my-product.worker.pause",
    condition: () => batch.RequiresInspection,
    context: context);

await RuntimeOutput.BreakIfNumbers(
    breakpointId: "my-product.worker.queue-depth",
    value1: queue.Count,
    value2: options.QueueLimit,
    context: context);
#endif
```

协作断点只暂停命中的异步调用链，不挂起整个进程。CLI 连接与断点生命周期无关；客户端断开不会自动恢复断点，只有显式 Resume 才恢复。

### 3.5 反射对象注入

```csharp
RuntimeInjector.Data(hub, "my-product.worker", workerState, new RuntimeDiagnosticObjectAccess
{
    AllowReadAllPublic = false,
    ReadableMembers = ["CurrentPhase", "QueueDepth", "UpdatedAt", "Snapshot"],
    WritableMembers = ["PauseRequested"],
    InvokableMembers = ["Snapshot", "RequestScan"]
});
```

不要把业务对象的所有公开方法暴露给诊断管道。写和调用权限必须按成员显式白名单。

## 4. 业务状态、事件与安全退出

### 4.1 状态分层

| 层级 | API | 用途 |
| --- | --- | --- |
| 全局生命周期 | `RuntimeManagedRegistry.GlobalLifecycleState` / `RuntimeStateManager` | Start、Working、Stop、Exit 等全程序状态 |
| 单元主状态 | `IRManagedState.TransitionTo` | 进程、线程、任务粗粒度状态 |
| 业务细分状态 | `SetDetail` / 子任务状态 DLIST | 扫描阶段、队列深度、清理进度等 |

```csharp
worker.State.SetDetail("scanPhase", "enumerating");
worker.State.SetDetail("currentPath", currentPath);

if (!worker.State.TryTransitionTo("Working"))
{
    // The requested transition is invalid from the current state.
}

worker.State.AppendSubTaskState("Working");
```

`TransitionTo` 在转换无效时抛异常；`TryTransitionTo` 返回 `false`。成功转换后 `RManagedState` 自动 Sync，并向主控 FIFO 发送简单数值状态指令。大量上下文数据应继续通过 JSON 管道传输。

### 4.2 业务事件

```csharp
public sealed class MyWorkerState
{
    public event EventHandler<MyWorkerCompletedEventArgs>? Completed;

    public void Complete(string batchId)
    {
        Completed?.Invoke(this, new MyWorkerCompletedEventArgs(batchId));
    }
}

public sealed record MyWorkerCompletedEventArgs(string BatchId);
```

程序集登记：

```csharp
[assembly: DiagnosticHookableEvent(
    "my-product.worker.completed",
    typeof(MyWorkerState),
    nameof(MyWorkerState.Completed))]
```

实例事件钩子使用弱引用，不应阻止业务对象回收。静态事件没有实例生命周期，必须在停机时显式 Detach。

### 4.3 标准退出流程

1. 主控发布全局“准备关机/Stop”状态和退出 deadline。
2. 主控遍历当前注册表，逐个向进程、线程和任务 FIFO 发送 `Stop` 和 `Wakeup`。
3. 每个守护程序执行业务清理，将自身状态转换到 Stop。
4. 对象在真实结束时销户自己的登记、FIFO handler、管道和反射目标。
5. 主控持续检查进程/线程/任务登记和相关 DLIST；全部清空后返回 0。
6. deadline 到期仍有待退出单元时返回 124。

```csharp
var shutdown = host.Services.GetRequiredService<RuntimeShutdownCoordinator>();
var result = await shutdown.ShutdownAsync(
    timeout: TimeSpan.FromSeconds(15),
    payload: "operator-request",
    cancellationToken: cancellationToken);

Environment.ExitCode = result.ExitCode;
```

### 4.4 守护程序：轮询保底 + FIFO 唤醒

```csharp
while (!cancellationToken.IsCancellationRequested)
{
    if (managedRegistry.IsGlobalStopOrExitRequested)
    {
        await CleanupAsync(cancellationToken);
        return;
    }

    await RunOneCycleAsync(cancellationToken);

    // Keep this wait interruptible. FIFO Wakeup/Stop is the fast path;
    // polling the global state remains the fallback.
    await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
}
```

实际 `RProcess`、`RThread`、`RTask` 会注册内部命令 handler。业务守护程序必须保持清理可重入，因为全局状态轮询和 FIFO Stop 可能几乎同时触发。

## 5. JSON 与 CLI 清单

### 5.1 Wire format

所有 Runtime 命名管道 JSON 使用：

```text
[4-byte little-endian signed int32 payload length][N-byte UTF-8 JSON]
```

必须循环读取直到满 4 字节头部和完整 payload，不能假设一次 `ReadAsync` 就获得完整帧。CLI 已封装 framing，业务代码不应手写管道客户端。

### 5.2 rtdiag/2.0 单命令

```json
{
  "header": {
    "schema": "rtdiag/2.0",
    "frameType": "request",
    "category": "query",
    "operation": "get",
    "requestId": "2a6f1fa9f6774f11a5af862ea6c9741b",
    "timestamp": "2026-07-12T10:00:00Z",
    "source": "cli",
    "destination": "runtime"
  },
  "command": {
    "domain": "pipes",
    "target": "diagnostics.pipes",
    "action": "list",
    "args": {
      "includeInactive": false
    }
  }
}
```

响应：

```json
{
  "header": {
    "schema": "rtdiag/2.0",
    "frameType": "response",
    "correlationId": "2a6f1fa9f6774f11a5af862ea6c9741b",
    "source": "runtime",
    "destination": "cli"
  },
  "status": {
    "ok": true,
    "code": "OK",
    "message": "",
    "retryable": false
  },
  "data": [],
  "meta": {
    "durationMs": 1
  }
}
```

### 5.3 rtdiag/3.0 结构化 batch

```json
{
  "header": {
    "schema": "rtdiag/3.0",
    "frameType": "request",
    "category": "batch",
    "operation": "execute",
    "requestId": "59b5599bb2384a8394f7d43b142c05d7",
    "source": "cli",
    "destination": "runtime"
  },
  "batch": {
    "options": {
      "stopOnError": true,
      "deadlineMs": 30000
    },
    "steps": [
      {
        "id": "list-pipes",
        "command": {
          "domain": "pipes",
          "target": "diagnostics.pipes",
          "action": "list"
        }
      },
      {
        "id": "get-runtime-state",
        "command": {
          "domain": "runtime",
          "target": "runtime.managed",
          "action": "lifecycle"
        },
        "continueOnError": false,
        "delayMs": 0
      }
    ]
  }
}
```

### 5.4 CLI v3（迁移中）

CLI v3 已完成设计批准，但当前源码尚未完成迁移。以下是新配置的权威结构，不代表当前 v2 CLI 已能加载：

```json
{
  "schema": "iwesun.runtime.cli/3.0",
  "application": {
    "name": "Iwesun Runtime CLI"
  },
  "endpoints": {
    "diagnostics": {
      "transport": "namedPipe",
      "pipeName": "DdnsSnap.RuntimeDiagnostics"
    }
  },
  "commands": [],
  "workflows": [],
  "extensions": {}
}
```

v3 将使用小写 dot-style 规范命令，例如 `switchboard.get`、`breakpoint.list`、`pipe.acquire`。别名、自定义命令和结构化 workflow 继续保留；旧 schema、旧命令名和携带 CLI 文本的 workflow 步骤将被删除。

详细规格参见 [2026-07-12-cli-v3-redesign.md](superpowers/specs/2026-07-12-cli-v3-redesign.md)。CLI v3 实施后，本节将从新 `Iwesun.Runtime.Cli.commands.json` 生成完整命令表。

### 5.5 CLI 操作标准

1. 先查询宿主状态和目标目录。
2. 只启用必要的 section、输出点、断点或钩子。
3. 触发最小真实业务动作。
4. 读取 frame、事件、文件或状态快照。
5. 显式 Resume 所有等待断点。
6. 卸载动态钩子。
7. 运行 quiet 或恢复之前的开关状态。
8. 最后才发送安全退出命令。

CLI 客户端退出不改变服务端断点状态，也不触发宿主退出。

## 附录 A：最小接入检查表

- [ ] 主程序使用 `Services.Start` 和 `Activate`。
- [ ] 配置 Runtime 目录和管道名来源。
- [ ] 进程、线程、任务创建点已检查。
- [ ] 反射对象只暴露显式白名单。
- [ ] 断点调用使用 `#if DEBUG`。
- [ ] 关机通过 `RuntimeShutdownCoordinator`。
- [ ] 诊断默认保持静默。

## 附录 B：完整接入检查表

- [ ] 所有受管单元有稳定且唯一的 unitId。
- [ ] 所有动态单元在真实结束后反登记。
- [ ] 线程和任务有可中断等待或 CancellationToken。
- [ ] 状态转换使用统一 RuntimeState Code。
- [ ] 简单状态指令走 FIFO，复杂数据走管道。
- [ ] 守护程序同时支持全局状态轮询和 FIFO 唤醒。
- [ ] 事件登记和卸载与对象生命周期一致。
- [ ] 正常退出码为 0，退出超时码为 124。
- [ ] 真实 CLI 场景结束后恢复 quiet。

## 附录 C：Debug / Release 能力矩阵

| 能力 | Debug | Release |
| --- | --- | --- |
| 输出 / TracePoint / Log | 保留 | 保留 |
| 文件记录 | 保留 | 保留 |
| 命名管道 | 保留 | 保留 |
| 反射白名单 | 保留 | 保留 |
| 事件钩子 | 保留 | 保留 |
| 状态/执行登记 | 保留 | 保留 |
| 安全退出 | 保留 | 保留 |
| 逻辑断点 | 编译 | 不编译/不装配 |
| 数值断点 | 编译 | 不编译/不装配 |

## 附录 D：旧痕迹删除清单

- [ ] 删除业务代码中分散的 `UseRuntimeDiagnostics` / `BuildDiagnosticRegistries`。
- [ ] 删除用于诊断的 `Console.WriteLine` 和临时文件。
- [ ] 删除未受管的 `Process.Start`、`new Thread`、`Task.Run` 创建点，或在审核后明确保留理由。
- [ ] 删除宽泛反射 `InvokableMembers`。
- [ ] 删除 Release 中未隔离的 Break/Watch 调试注入。
- [ ] 删除“CLI 断开自动恢复断点”的旧逻辑和文档。
- [ ] CLI v3 完成后删除 v2 配置、旧命令名和文本 workflow 步骤。

## 附录 E：Codex 接入技能

Runtime 仓库包含可复制技能：

`skills/iwesun-runtime-integration/`

将该目录整体复制到其他 Codex 环境的技能目录后，可以通过 `$iwesun-runtime-integration` 触发主程序替换、受管对象替换、单点诊断注入、状态退出和 JSON/CLI 流程。

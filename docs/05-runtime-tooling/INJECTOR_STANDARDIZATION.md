# 注入器标准化

> **状态**: DRAFT | **最后更新**: 2026-07-09
> **源码参考**: `Iwesun.Runtime.Diagnostics/RuntimeDiagnosticsServiceCollectionExtensions.cs`, `Iwesun.Runtime.Diagnostics/RuntimeOutput.cs`, `Iwesun.Runtime.Diagnostics/RuntimeDiagnosticHub.cs`, `Iwesun.Runtime.Diagnostics/RuntimeExecutionManagement.cs`, `Iwesun.Runtime.Diagnostics/RuntimeStateManager.cs`, `Iwesun.Runtime.Diagnostics/DiagnosticAssemblyAttributes.cs`, `Iwesun.Runtime.SampleHost/Program.cs`

本文定义 Runtime.Diagnostics 的六大类标准化注入器，以及如何把“启动/退出”收敛成一个任何程序都直接使用的固定模板，把“输出/断点/数据/线程任务”收敛成业务源代码里的标准标注化注入。

这里的“注入器”不是字节码层面的真正宏定义，而是两层模型：

1. **标准模板层**：启动与退出使用统一宿主模板，任何程序直接套用，不需要改模板结构。
2. **标注化层**：输出、断点、数据、线程和任务使用源代码里的标准注入标注和固定插入片段。

目标是：

- 不干扰业务逻辑。
- 插入位置固定。
- 语义固定。
- 调用尽量短。
- 关闭时近似无成本。

## 设计目标

- 把启动、输出、断点、数据、线程/任务、退出统一成一套命名。
- 启动和退出不进业务逻辑，只走固定模板。
- 输出、断点、数据、线程/任务只在业务边界处做标注化插入，不把控制逻辑打散。
- 支持“像宏一样简单”的调用体验，但保持 C# 正常可读性和可调试性。
- 所有注入点都应能被诊断系统查询、回放和验证。

## 两层模型

### A. 标准模板层

这一层只负责：启动、初始化、停止、收尾。

要求：

- 所有程序都用同一个模板结构。
- 不要求业务程序改模板内部结构。
- 只需要填入业务宿主类型和业务循环入口。
- 启动/退出逻辑保持稳定，不散落在业务代码里。

最简写法：

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddRuntimeDiagnostics(runtimeDirectory);
using var host = builder.Build();
host.Services.UseRuntimeDiagnostics();
host.Services.BuildDiagnosticRegistries(Assembly.GetExecutingAssembly());
await host.RunAsync();
```

退出模板对应的是：

- `SetStop()`。
- `StopAsync()`。
- `Draining / Completed / Faulted / Timeout` 收尾状态。

### B. 标注化层

这一层只负责：输出、断点、数据、线程和任务。

要求：

- 注入点写在业务源代码里。
- 注入点名称稳定。
- 注入点表达尽量短。
- 注入点不改变业务判断结构。

## 六大类注入器

### 1. 启动注入器

用途：在宿主启动最早阶段完成诊断内核初始化、注册表构建、状态入口登记。

归属：标准模板层。

建议职责：

- 初始化诊断开关板。
- 注册 Hub 目标。
- 初始化状态目录、执行目录、反射目录。
- 完成宿主程序集特性扫描。

### 2. 输出注入器

用途：把业务输出变成统一的 TracePoint 或 Watch 事件。

归属：标注化层。

最简写法：

```csharp
RuntimeOutput.TracePoint("sample.host.loop", "sample-host", "tick", "Loop tick.", payload);
RuntimeOutput.Watch("sample.host.session", snapshot, nameof(SampleHostStateSnapshot));
```

建议职责：

- 输出监视点。
- 结构化日志输出。
- 业务状态快照。

### 3. 断点注入器

用途：在特定条件成立时暂停当前调用链，给 CLI 或监控端观察窗口。

归属：标注化层。

最简写法：

```csharp
await RuntimeOutput.BreakIf("sample.host.pause", () => state.IsPaused, state.Snapshot());
```

建议职责：

- 条件断点。
- 断点上下文快照。
- 恢复后继续执行。

### 4. 数据注入器

用途：把静态数据类、动态数据类、运行时快照暴露给诊断 Hub。

归属：标注化层。

最简写法：

```csharp
hub.RegisterObject("sample.host.profile", profile, access);
hub.RegisterObject("sample.host.session", state, access);
```

建议职责：

- 暴露只读配置数据。
- 暴露可变会话数据。
- 暴露快照对象和有限白名单成员。

### 5. 线程和任务注入器

用途：把静态线程表、动态线程表、静态任务表、动态任务表统一登记到执行目录。

归属：标注化层。

最简写法：

```csharp
execution.RegisterThread("sample-host.worker", "Sample Host Worker", RuntimeExecutionLifetime.Static, RuntimeThreadKind.Worker);
execution.RegisterTask("sample-host.worker.loop", "Sample Host Worker Loop", RuntimeExecutionLifetime.Static, threadId: "sample-host.worker");
```

建议职责：

- 注册线程。
- 注册任务。
- 更新心跳。
- 推进状态。
- 记录完成、故障、取消和收尾。

### 6. 退出注入器

用途：在宿主停止时统一落状态、进入 draining、写完成态、准备超时兜底。

归属：标准模板层。

最简写法：

```csharp
_runtimeStateManager.SetStop();
execution.SetThreadState(threadId, RuntimeThreadState.Draining, currentTaskId: taskId);
execution.SetTaskState(taskId, RuntimeTaskState.Draining, step: "cancelling", threadId: threadId);
```

建议职责：

- 发出停止信号。
- 任务进入 draining。
- 线程进入 draining。
- 完成态、错误态、超时态落盘。

## 标注化边界

C# 没有真正的编译期宏，因此这里采用的是“**标注化 + 模板化**”，不是“宏语义”。

推荐方式是：

1. 启动与退出只走固定模板。
2. 输出、断点、数据、线程/任务只在业务边界处做标注化插入。
3. 复杂逻辑只存在于诊断库内部，不散落在业务代码里。

这样可以达到类似宏的写法密度，但仍保留：

- 可读。
- 可调试。
- 可搜索。
- 可重构。

## 推荐最小写法

宿主代码建议保持在以下形态：

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddRuntimeDiagnostics(runtimeDirectory);
builder.Services.AddSingleton<SampleHostProfile>();
builder.Services.AddSingleton<SampleHostState>();
builder.Services.AddHostedService<SampleHostWorker>();

using var host = builder.Build();
host.Services.UseRuntimeDiagnostics();
host.Services.BuildDiagnosticRegistries(Assembly.GetExecutingAssembly());
await host.RunAsync();
```

业务循环中只保留：

- 状态推进。
- 监视点输出。
- 条件断点。
- 线程/任务状态更新。

不建议把业务分支逻辑、诊断逻辑和收尾逻辑混在一个大方法里。

## 当前状态

标准模板层和标注化层已经有了可用的代码落点：

- `RuntimeHostTemplate` 负责启动、激活和退出模板。
- `RuntimeInjector` 负责输出、断点、数据、线程和任务的标注化注入门面。
- `Iwesun.Runtime.SampleHost` 已经用这两层结构跑通样板宿主。

本文仍然保留为标准说明文档，后续如果需要可以继续细化命名或扩展模板能力。

## 相关文档

- 技术方案 → [INJECTOR_STANDARDIZATION_PLAN.md](INJECTOR_STANDARDIZATION_PLAN.md)
- 任务书 → [INJECTOR_STANDARDIZATION_TASKS.md](INJECTOR_STANDARDIZATION_TASKS.md)
- 运行时诊断 → [../RUNTIME_DIAGNOSTICS.md](../RUNTIME_DIAGNOSTICS.md)
- 样板宿主 → [SAMPLE_HOST.md](SAMPLE_HOST.md)
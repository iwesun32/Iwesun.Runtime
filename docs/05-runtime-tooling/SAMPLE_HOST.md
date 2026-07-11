# 独立样板宿主

> **状态**: CURRENT | **最后更新**: 2026-07-09
> **源码参考**: `Iwesun.Runtime.SampleHost/Program.cs`, `Iwesun.Runtime.SampleHost/SampleHostProfile.cs`, `Iwesun.Runtime.SampleHost/SampleHostState.cs`, `Iwesun.Runtime.SampleHost/SampleHostWorker.cs`, `Iwesun.Runtime.Diagnostics/RuntimeDiagnosticsServiceCollectionExtensions.cs`

本文描述一个与 DDNS Snap 解耦的独立样板宿主。它只依赖 `Iwesun.Runtime.Diagnostics`，用于验证运行时诊断接入、代码注入、静态/动态数据类、状态切换、线程/任务登记、输出监视点、断点和反射目标暴露方式，并可作为后续发布模板。

## 目标

- 提供一个更单纯的测试进程，不绑定业务应用。
- 演示诊断框架的标准接入方式。
- 演示程序集特性注册、反射目标注册和后台循环输出。
- 作为以后可发布的样板宿主。

## 当前结构

样板宿主包含四部分：

1. `Program.cs`：宿主启动、诊断注册、程序集特性声明。
2. `SampleHostProfile.cs`：静态数据类，描述宿主配置和测试节奏。
3. `SampleHostState.cs`：动态数据类，可被注入和查询的状态对象。
4. `SampleHostWorker.cs`：后台并行循环，负责状态推进、线程/任务登记和诊断输出。

## 演示能力

### 1. 代码注入

样板宿主通过程序集特性把诊断点挂到宿主启动阶段：

- `DiagnosticPipePrefix`
- `DiagnosticWatchPoint`
- `DiagnosticBreakpoint`
- `DiagnosticHookableEvent`

这套入口用于验证宿主如何在代码层声明可观测点。

这里需要区分两层：程序集特性是 SampleHost 为完整功能测试保留的“预编译目录与初始状态”样例；真正的业务注入应遵守单点注入，即一个功能的条件、上下文和输出/断点调用集中在业务现场的一个连续代码段。普通监视或临时跟踪不应因为需要输出一次数据，就被迫同时修改 `Program.cs`、启动注册和业务文件。只有测试编译初始断点、CLI 预发现、事件登记或反射白名单时，才使用这些额外声明。

### 2. 静态/动态数据

样板宿主暴露两类数据：

- `SampleHostProfile`：静态配置数据，作为只读样板元数据。
- `SampleHostState`：动态运行数据，作为可变会话状态。

### 3. 运行时状态

样板宿主通过 `RuntimeStateManager` 切换 `Start / Working / Stop`，并暴露给诊断 Hub 查询。

### 4. 线程与任务

样板宿主在 `runtime.execution` 下注册静态线程表、静态任务表和动态批次任务，用三路并行循环模拟协调、工作和监控场景。

### 5. 反射目标

样板宿主将 `SampleHostProfile`、`SampleHostState` 注册为反射目标，允许按白名单读取、写入和调用成员。

### 6. 循环观测

后台 worker 以协调器、工作器和监视器三路并行循环推进状态，发出 `RuntimeOutput.TracePoint()`，并可在调试构建下触发断点和观察点。

## 运行方式

```powershell
dotnet run --project Iwesun.Runtime.SampleHost/Iwesun.Runtime.SampleHost.csproj -c Release
```

宿主启动后可通过 `Iwesun.Runtime.Cli` 连接运行时诊断管道，观察 `sample.host` 和 `runtime.state`。

## 发布目录附带模板

`Iwesun.Runtime.SampleHost` 发布目录会附带以下集成资产：

- `templates/RuntimeHost.Startup.Template.cs.txt`
- `templates/RuntimeHost.Shutdown.Template.cs.txt`
- `templates/RuntimeIntegration.Interface.Template.json`
- `templates/Iwesun.Runtime.Cli.commands.custom.sample.json`
- `docs/RUNTIME_INTEGRATION_GUIDE.md`
- `docs/IWESUN_RUNTIME_CLI.md`
- `cli/Iwesun.Runtime.Cli.commands.v2.json`

## 当前完成情况

- 已完成：独立进程、诊断接入、静态/动态数据类、状态推进、线程/任务登记、反射目标暴露。
- 已完成：可作为后续发布模板的基础。
- 待扩展：更完整的统一收尾编排、自动化测试脚本、正式发布说明。

## 相关文档

- 运行时诊断 → [../RUNTIME_DIAGNOSTICS.md](../RUNTIME_DIAGNOSTICS.md)
- 状态分类基础类型 → [STATE_CLASSIFICATION.md](STATE_CLASSIFICATION.md)
- 状态分类在线业务实现 → [STATE_CLASSIFICATION_ONLINE.md](STATE_CLASSIFICATION_ONLINE.md)
- 线程与任务管理 → [THREAD_TASK_MANAGEMENT.md](THREAD_TASK_MANAGEMENT.md)

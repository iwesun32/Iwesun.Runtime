# 进程与线程标准注入

> **状态**: CURRENT | **最后更新**: 2026-07-09
> **源码参考**: `modules/Diagnostics/src/Iwesun.Runtime.Diagnostics/RThread.cs`, `modules/Diagnostics/src/Iwesun.Runtime.Diagnostics/RProcess.cs`, `modules/Diagnostics/src/Iwesun.Runtime.Diagnostics/RuntimeHostTemplate.cs`

本文定义 Runtime 的进程/线程标准注入方式，目标是通过“前缀类 + 全文替换”实现低成本接入。

## 目标

- 提供与系统类接近的调用入口，便于从 `Thread`/`Process` 迁移。
- 在调用路径中自动注入 `RuntimeExecutionManager` 线程/任务状态。
- 保持业务代码改动最小，优先支持全文替换。

## 命名约定

- 线程注入类：`RThread`（对应 `Thread`）
- 进程注入类：`RProcess`（对应 `Process`）

前缀采用 `R`（Runtime），避免与 BCL 类型冲突，同时保留可读性。

## 使用模式

### 线程

把常见创建调用替换为：

- `new Thread(start)` → `RThread.Create(...).Start()`
- 需要等待时：`RThread.Join(...)`

### 进程

把常见启动调用替换为：

- `Process.Start(startInfo)` → `RProcess.Start(...)`
- 需要等待时：`RProcess.WaitForExit(...)`

## 生命周期注入

- 创建时登记：`Registered`
- 启动后登记：`Running`
- 结束时登记：`Completed` / `Cancelled` / `Faulted`
- 异常路径必须记录 `Faulted`

## 模板初始化要求

宿主启动后必须先激活 Runtime 模板，再使用注入类：

1. `services.Start(runtimeDirectory)`
2. `provider.Activate(hostAssembly)`（内部完成注入上下文初始化）

## 全文替换建议

建议按“调用模式”分批替换，而不是一次性替换所有 `Thread`/`Process` 语义：

1. 优先替换 `new Thread(...)` 和 `Process.Start(...)`
2. 再替换等待与状态读取调用
3. 每批替换后执行构建与回归验证

## 当前范围

本轮先覆盖“常用创建、启动、等待、状态快照”路径；复杂 API 按实际使用点增量扩展。

## 相关文档

- 线程与任务管理 → [THREAD_TASK_MANAGEMENT.md](THREAD_TASK_MANAGEMENT.md)
- 独立样板宿主 → [SAMPLE_HOST.md](SAMPLE_HOST.md)
- 运行时诊断总览 → [../RUNTIME_DIAGNOSTICS.md](../RUNTIME_DIAGNOSTICS.md)

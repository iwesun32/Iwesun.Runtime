# 注入器标准化任务书

> **状态**: DRAFT | **最后更新**: 2026-07-09
> **源码参考**: `Iwesun.Runtime.Diagnostics/RuntimeDiagnosticsServiceCollectionExtensions.cs`, `Iwesun.Runtime.Diagnostics/RuntimeOutput.cs`, `Iwesun.Runtime.Diagnostics/RuntimeDiagnosticHub.cs`, `Iwesun.Runtime.Diagnostics/RuntimeExecutionManagement.cs`, `Iwesun.Runtime.Diagnostics/RuntimeStateManager.cs`, `Iwesun.Runtime.SampleHost/Program.cs`

本文把注入器标准化拆成可执行任务，用于后续实施。当前拆分以“固定启动/退出模板 + 源码标注化注入”为总原则。

## 任务 1：定义标准模板

- 定义统一的启动模板。
- 定义统一的退出模板。
- 定义所有程序共用的主入口/停止入口结构。

### 完成标准

- 任意程序都能直接套用，不需要改模板结构。
- 启动与退出逻辑不散落在业务代码里。

## 任务 2：定义标注化注入命名

- 统一六大类注入器名称。
- 统一门面命名。
- 统一输入输出命名。

### 完成标准

- 启动、输出、断点、数据、线程/任务、退出的入口名称一致。
- 样板宿主和正式宿主共享同一套术语。

## 任务 3：定义最小插入片段

- 规定 `Program.cs` 的最小启动片段。
- 规定 Worker 的最小运行片段。
- 规定 `finally` / `StopAsync` 的最小退出片段。

### 完成标准

- 业务代码只需插入少量固定行。
- 不改变业务流程结构。

## 任务 4：补齐六大类调用边界

- 启动：固定模板。
- 输出：TracePoint / Watch。
- 断点：BreakIf。
- 数据：RegisterObject / Snapshot。
- 线程/任务：Register / State / Heartbeat。
- 退出：固定模板。

### 完成标准

- 六类注入器都能被样板宿主覆盖。
- 六类注入器都能被 CLI / Hub 查询。

## 任务 5：建立样板宿主验收程序

- 静态数据类。
- 动态数据类。
- 多线程 / 多循环。
- 动态任务登记。
- 输出监视点。
- 条件断点。
- 退出收尾。

### 完成标准

- 样板宿主可作为发布前的固定验收入口。

## 任务 6：补文档和测试

- 更新文档索引。
- 写最小操作说明。
- 补自动化验证脚本或测试入口。

### 完成标准

- 文档和代码保持一致。
- 新宿主不需要依赖 DDNS Snap。

## 当前状态

本任务书中的核心路径已经落地出可运行原型：启动/退出模板和标注化注入门面均已实现并在样板宿主中使用。

当前更偏向后续完善项：

- 更严格的命名收口。
- 更完整的自动化测试。
- 更明确的发布模板说明。

## 相关文档

- 设计说明 → [INJECTOR_STANDARDIZATION.md](INJECTOR_STANDARDIZATION.md)
- 技术方案 → [INJECTOR_STANDARDIZATION_PLAN.md](INJECTOR_STANDARDIZATION_PLAN.md)
- 样板宿主 → [SAMPLE_HOST.md](SAMPLE_HOST.md)
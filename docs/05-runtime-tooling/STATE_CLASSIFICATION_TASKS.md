# 状态分类任务书

> **状态**: DRAFT | **最后更新**: 2026-07-09
> **源码参考**: `Iwesun.Runtime.Diagnostics/RuntimeOutput.cs`, `Iwesun.Runtime.Diagnostics/RuntimeDiagnosticHub.cs`, `Iwesun.Runtime.Diagnostics/RuntimeDiagnosticsMonitor.cs`, `Iwesun.Runtime.Diagnostics/RuntimeDiagnosticsServiceCollectionExtensions.cs`

本文是状态分类能力的任务书，按可执行颗粒度拆分为实现任务。

## 任务 1：定义基础状态值类型

- 建立一个可继承、可扩展的状态基础值类型。
- 支持状态码、父状态码和层级信息。
- 保证类型可序列化、可快照。

### 完成标准

- 能表达 `Start / Working / Stop`。
- 能表达 `Working.*` 子状态。

## 任务 2：定义两个判断接口

- 完成精确相等接口。
- 完成 `is 属于` 接口。
- 明确两个接口的职责边界。

### 完成标准

- `ExactEqual` 只判断本体。
- `Is(...)` 只判断归属链。

## 任务 3：补在线业务状态注册

- 在宿主启动时注册初始状态。
- 在工作流程中切换状态。
- 在停止流程中收尾状态。

### 完成标准

- 业务运行时能看到状态变化。
- 监控侧能查询当前状态。

## 任务 4：补线程/任务联动

- 把状态类型接入线程登记。
- 把状态类型接入任务登记。
- 让静态/动态表复用统一状态语义。

### 完成标准

- 线程表和任务表的状态语义一致。
- 粗判断和精判断都能工作。

## 任务 5：补文档与测试

- 为基础类型补文档示例。
- 为在线业务补落地说明。
- 为关键语义补测试。

### 完成标准

- 文档结构完整。
- 语义描述与实现方向一致。

## 当前状态

本任务书对应的能力已经完成前两项、在线闭环验证，以及线程/任务管理的第一版基础实现。当前拆分结果如下：

- 已完成：任务 1、任务 2、任务 3 的基础闭环。
- 已完成：任务 4 的基础管理器和样板宿主验证。
- 待完成：任务 4 的正式生命周期编排，以及任务 5。

它继续描述拆分后的待办，但不再把已完成内容归入未实现范围。

## 相关文档

- 基础类型设计 → [STATE_CLASSIFICATION.md](STATE_CLASSIFICATION.md)
- 在线业务实现 → [STATE_CLASSIFICATION_ONLINE.md](STATE_CLASSIFICATION_ONLINE.md)
- 实施计划 → [STATE_CLASSIFICATION_PLAN.md](STATE_CLASSIFICATION_PLAN.md)

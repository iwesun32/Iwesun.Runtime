# 状态分类实施计划

> **状态**: DRAFT | **最后更新**: 2026-07-09
> **源码参考**: `Iwesun.Runtime.Diagnostics/RuntimeOutput.cs`, `Iwesun.Runtime.Diagnostics/RuntimeDiagnosticHub.cs`, `Iwesun.Runtime.Diagnostics/RuntimeDiagnosticsMonitor.cs`, `Iwesun.Runtime.Diagnostics/RuntimeDiagnosticsServiceCollectionExtensions.cs`

本文是 [状态分类基础类型设计](STATE_CLASSIFICATION.md) 与 [状态分类在线业务实现技术文档](STATE_CLASSIFICATION_ONLINE.md) 的实施计划。

## 目标拆分

1. 建立可继承、可扩展的状态值类型。
2. 建立精确相等与 `is 属于` 两类判断接口。
3. 建立在线业务登记、切换、查询与收尾流程。
4. 建立状态分类在线程/任务/生命周期中的统一使用方式。

## 实施原则

- 先把状态语义定死，再写业务接入。
- 先支持最小三态，再逐步细分。
- 先保证父状态判断稳定，再增加子状态。
- 先让查询面可用，再扩大控制面。

## 阶段 1：基础类型定义

### 内容

- 定义状态值对象。
- 定义状态码、父码、层级信息。
- 定义精确相等接口。
- 定义 `is 属于` 接口。

### 验收

- 能表达 `Start / Working / Stop`。
- 能表达 `Working.*` 的子状态。
- 能同时满足精确相等与包含判断。

## 阶段 2：在线业务接入

### 内容

- 把状态切换接入宿主启动流程。
- 把状态切换接入工作流程。
- 把状态收尾接入停止流程。

### 验收

- 在线业务可写入状态。
- 监控侧可查询当前状态。
- 粗判断与精判断结果一致且稳定。

## 阶段 3：线程/任务联动

### 内容

- 线程表使用状态分类。
- 任务表使用状态分类。
- 线程/任务生命周期都能映射到状态树。

### 验收

- 静态/动态表都能复用状态类型。
- 线程和任务在查询面上显示一致。

## 阶段 4：文档与测试

### 内容

- 补状态分类的单元测试。
- 补在线接入的生命周期测试。
- 补文档索引和交叉链接。

### 验收

- 文档与实现约束一致。
- 新增状态不会破坏旧父状态判断。

## 风险点

1. 如果只做平铺枚举，父子语义会丢失。
2. 如果只做字符串判断，后续扩展会脆弱。
3. 如果不分精确相等与包含判断，业务判断会混乱。
4. 如果在线接入太晚，启动阶段的状态会丢失。

## 当前状态

本计划对应的能力已经完成基础层和在线闭环，并且线程/任务联动已经有了第一版执行管理器。当前进度如下：

- 已完成：基础状态值类型、精确相等接口、`is 属于` 接口、状态目录、在线状态管理器。
- 已完成：独立样板宿主，用于验证诊断接入和代码注入。
- 已完成：线程/任务管理基础管理器和 `runtime.execution` 查询面。
- 待完成：统一停止收尾、完整测试覆盖、正式生命周期编排。

本文继续作为后续实施顺序的计划文档。

## 相关文档

- 基础类型设计 → [STATE_CLASSIFICATION.md](STATE_CLASSIFICATION.md)
- 在线业务实现 → [STATE_CLASSIFICATION_ONLINE.md](STATE_CLASSIFICATION_ONLINE.md)
- 任务书 → [STATE_CLASSIFICATION_TASKS.md](STATE_CLASSIFICATION_TASKS.md)

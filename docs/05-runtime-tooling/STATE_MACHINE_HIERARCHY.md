# 分层状态机基础设计

> **状态**: CURRENT | **最后更新**: 2026-07-09
> **源码参考**: `modules/Diagnostics/src/Iwesun.Runtime.Diagnostics/`

本文定义“分层状态枚举 + 专门状态机类”的基础设计，用于统一进程、线程、任务三类执行单元的状态表达与跃迁约束。

## 目标

- 将粗粒度状态与细粒度状态分层，避免同一平面混用。
- 由专门状态机类统一处理主状态跃迁。
- 保留 RuntimeState 的可扩展值类型能力。
- 为后续进程/线程/任务统一管理提供基础。

## 分层模型

### 第 1 层：主状态（粗粒度）

主状态用于流程控制与合法跃迁校验，建议聚焦生命周期主线：

- `Start`
- `Working`
- `Stop`

### 第 2 层：子状态（细粒度）

子状态用于描述阶段细节，不替代主状态。

示例：

- `WorkingIdle`
- `WorkingCollecting`
- `WorkingAnalyzing`
- `WorkingNetworkAccess`
- `StopRequested`
- `StopDraining`
- `StopCompleted`
- `StopTimeout`

## 专门状态机类职责

新增 `RuntimeStateTransitionEngine`，职责如下：

1. 识别主状态锚点（Start/Working/Stop）。
2. 校验主状态跃迁是否合法。
3. 提供统一的 Try/Force 迁移接口。
4. 保持扩展状态兼容，不阻断业务新增子状态。

## 跃迁规则（基础版）

主状态允许的最小跃迁：

- `Start -> Working`
- `Start -> Stop`
- `Working -> Stop`
- `Working -> Start`（允许重置/重启语义）
- `Stop -> Start`
- 同主状态内的子状态切换允许直接通过

不允许的典型路径：

- `Stop -> Working`（必须先回到 Start，再进入 Working）

## Manager 接入策略

在 `RuntimeStateManager` 保留现有直接设置接口（兼容），并新增：

- `TryTransitionTo(...)`
- `TransitionTo(...)`

约束策略：

- `TryTransitionTo`：失败返回 `false`，不抛异常。
- `TransitionTo`：失败抛 `InvalidOperationException`。

## 与后续扩展的关系

- 本文只定义“基础状态机规则”，不绑定共享内存传输结构。
- 下一阶段可在不破坏主状态规则前提下，增加原因码、命令幂等与分布式同步策略。

## 相关文档

- [STATE_CLASSIFICATION.md](STATE_CLASSIFICATION.md)
- [THREAD_TASK_MANAGEMENT.md](THREAD_TASK_MANAGEMENT.md)
- [PROCESS_THREAD_INTERCEPTION_DECISION.md](PROCESS_THREAD_INTERCEPTION_DECISION.md)

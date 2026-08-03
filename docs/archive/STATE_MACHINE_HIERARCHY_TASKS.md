# 分层状态机基础任务书

> **状态**: CURRENT | **最后更新**: 2026-07-09
> **关联设计**: [STATE_MACHINE_HIERARCHY.md](STATE_MACHINE_HIERARCHY.md)

本文将“分层状态枚举 + 专门状态机类”拆解为可执行任务，作为基础阶段实施清单。

## 范围

- 覆盖 RuntimeState 的主状态跃迁规则。
- 新增状态机引擎类，集中管理合法跃迁。
- 在 RuntimeStateManager 提供受控跃迁入口。
- 保持现有调用兼容，不做破坏性迁移。

## 任务拆解

### T1. 状态机引擎类

- 新增 `RuntimeStateTransitionEngine`。
- 实现主状态识别（Start/Working/Stop）。
- 实现基础合法跃迁判定。

验收：

- 可调用 `CanTransition(from, to, catalog)` 得到稳定结果。

### T2. RuntimeStateManager 受控入口

- 新增 `TryTransitionToByCode(int code)`。
- 新增 `TryTransitionToByName(string name)`。
- 新增 `TransitionToByCode(int code)`、`TransitionToByName(string name)`。

验收：

- 合法跃迁成功更新状态。
- 非法跃迁在 Try 接口返回 false，在强制接口抛异常。

### T3. 文档索引接入

- 在 `docs/README.md` 增加两份新文档入口。

验收：

- 文档索引可直达设计文档与任务文档。

### T4. 构建回归

- 执行解决方案构建验证。

验收：

- 构建成功。

## 备注

- 本任务书为基础阶段，不包含共享内存协议、命令总线、跨进程一致性细节。

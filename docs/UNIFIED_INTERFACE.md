# 统一界面规范

> **状态**: CURRENT | **最后更新**: 2026-07-09

本文档用于统一 Runtime 文档、CLI 命令界面和诊断协议的表达方式，减少入口分散与术语不一致。

## 1. 统一目标

- **入口统一**：所有活跃文档从 `docs/README.md` 进入
- **术语统一**：同一概念在不同文档中使用同一命名
- **协议统一**：CLI 与运行时交互统一到 `RuntimeDiagnosticFrame`
- **状态统一**：所有开关默认 `false`（静默启动）

## 2. 术语与命名约定

| 概念 | 统一写法 | 说明 |
| --- | --- | --- |
| 运行时诊断总线 | `RuntimeDiagnosticHub` | 命名管道服务与目标分发中心 |
| 诊断路由器 | `DiagnosticSwitchboard` | section/point 级别开关控制 |
| 业务追踪入口 | `RuntimeOutput.TracePoint()` | 业务侧统一追踪 API |
| 管道协议帧 | `RuntimeDiagnosticFrame` | 4 字节长度前缀 + JSON 帧 |
| 命令行工具 | `Iwesun.Runtime.Cli` / `iwrt` | 统一命令入口 |

## 3. 文档界面统一规则

- 所有当前有效文档放在 `docs/` 活跃区
- 历史资料只放在 `docs/archive/`，不作为当前权威入口
- 交叉引用优先使用 `docs/` 内相对路径
- 不再引用仓库外路径或不存在目录（例如旧的 `04-interface`）

## 4. 命令界面统一规则

- CLI 文档命令示例统一使用 `iwrt` 前缀
- 诊断命令与 WebRuntime 命令分开描述，避免混用语义
- 参数覆盖顺序保持一致：显式参数 > 环境变量/配置 > 默认值

## 5. 输出与行为统一规则

- 默认静默：`globalEnabled=false`，section/point 默认关闭
- 调试输出走 `RuntimeOutput` 与 `DiagnosticSwitchboard`，不使用 `Console.WriteLine`
- 运行时观察与业务决策分离：诊断只观测，不改写业务流程

## 6. 关联文档

- 项目总览：`PROJECT_SUMMARY.md`
- 运行时诊断：`RUNTIME_DIAGNOSTICS.md`
- CLI 手册：`IWESUN_RUNTIME_CLI.md`
- AI 规则复核：`AI_ACCESS_RECHECK.md`

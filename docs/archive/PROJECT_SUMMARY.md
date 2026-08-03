# Iwesun Runtime 项目总览

> **状态**: CURRENT | **最后更新**: 2026-07-09

## 项目定位

Iwesun Runtime 是面向宿主应用（如 DDNS Snap）的运行时诊断与命令控制工具集，核心目标是：

- 提供默认静默、可按需启用的运行时观测能力
- 通过统一 CLI 协议进行诊断查询与控制操作
- 将诊断内核与具体业务宿主解耦

## 解决方案结构

解决方案：`Iwesun.Runtime.slnx`

| 项目 | Target Framework | 角色 |
| --- | --- | --- |
| `Iwesun.Runtime.Diagnostics` | `net10.0` | 运行时诊断内核（Switchboard、Hub、管道监控、反射目标） |
| `Iwesun.Runtime.WebView2` | `net10.0` | WebRuntime 控制模型与管道客户端 |
| `Iwesun.Runtime.Cli` | `net10.0` | 独立 CLI 工具（命令解析、协议封装、管道通信） |
| `Iwesun.Runtime.SampleHost` | `net10.0` | 最小样板宿主（用于本地集成与演示） |

依赖关系：

- `Iwesun.Runtime.Cli` → `Iwesun.Runtime.Diagnostics` + `Iwesun.Runtime.WebView2`
- `Iwesun.Runtime.SampleHost` → `Iwesun.Runtime.Diagnostics`

## 核心运行链路

```text
业务代码 / ILogger
  → RuntimeOutput.TracePoint()
  → DiagnosticSwitchboard（按 section/point 开关路由）
  → FIFO + 后台泵
  → RuntimeDiagnosticHub（命名管道）
  → CLI / 诊断客户端
```

## 当前文档基线

- 核心诊断：`RUNTIME_DIAGNOSTICS.md`
- CLI 手册：`IWESUN_RUNTIME_CLI.md`
- 统一界面规范：`UNIFIED_INTERFACE.md`
- AI 访问与操作复核：`AI_ACCESS_RECHECK.md`

## 本仓库边界

- Runtime 仓库负责运行时能力与实现规则
- 业务仓库（如 DDNS Snap）通过 `ProjectReference` 接入 Runtime
- 归档资料位于 `docs/archive/`，不作为当前权威入口

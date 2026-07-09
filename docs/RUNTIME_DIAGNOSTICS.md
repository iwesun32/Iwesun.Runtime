# 运行时诊断

> **状态**: CURRENT | **最后更新**: 2026-07-08
> **源码参考**: `Iwesun.Runtime.Diagnostics/`, `Iwesun.Runtime.Cli/`

宿主应用（如 DDNS Snap）通过 `Iwesun.Runtime.Diagnostics` 路由运行时诊断输出，替代直接的控制台/文件调试日志。正常运行时保持静默，直到监控器显式启用输出。

## 核心组件

| 组件                              | 职责                                  |
| --------------------------------- | ------------------------------------- |
| `RuntimeOutput`                   | 静态 TracePoint API，业务代码调用入口 |
| `DiagnosticSwitchboard`           | 静态配置驱动的输出路由器              |
| `RuntimeDiagnosticHub`            | 命名管道服务器，供实时监控            |
| `RuntimeDiagnosticLoggerProvider` | Serilog 提供者集成                    |

## 数据流

```text
ILogger / RuntimeOutput.TracePoint()
  → RuntimeOutputSwitch.Enabled（一个可变静态布尔检查）
  → DiagnosticSwitchboard FIFO<string>
  → 后台泵
  → RuntimeDiagnosticHub 事件
  → 可选诊断文件
```

当管道输出和文件输出都禁用时，诊断调用在格式化或序列化载荷之前返回，性能开销最小。

## 开关配置

配置文件：`diagnostic-switchboard.json`

- DDNS Snap 中位于 `C:\ProgramData\DdnsSnap\diagnostic-switchboard.json`
- 存储开关和输出点元数据，不是日志文件
- 首次启动时从编译的监控目录生成，后续启动时修复（添加新监控点）
- Schema 版本 4 默认静默启动：`globalEnabled`、管道输出、文件输出、每个 section、每个编译输出点均生成为 `false`
- 保留用户对已知点 ID 的 `enabled` 更改，同时自动添加新编译的点 ID

每个 `outputPoints[]` 条目是一个稳定监控点：

```json
{
  "id": "log.pipeline",
  "category": "logging",
  "purpose": "DDNS pipeline stage ILogger events.",
  "section": "pipeline",
  "enabled": false
}
```

配置还包含 `runtimeDiagnosticsPipeName`，同一机器上运行多个 DDNS Snap 构建或服务时保持唯一。

## TracePoint API

业务代码中使用静态 `RuntimeOutput.TracePoint()` 调用：

```csharp
RuntimeOutput.TracePoint("pipeline.stage", "DnsUpdateStage",
    new { Domain = domain, TargetIp = targetIp, Updated = true });
```

- 所有运行时追踪通过此 API，不直接使用 `Console.WriteLine` 或文件写入
- 由 `DiagnosticSwitchboard` 根据配置路由到输出点（管道、文件、日志）
- 支持结构化 JSON 载荷

## 诊断管道

Service 和 Agent 主机从 `diagnostic-switchboard.json` 启动专用运行时诊断管道（默认名：`DdnsSnap.Runtime.Diagnostics`）。

管道接受 4 字节小端长度前缀的 `RuntimeDiagnosticFrame` JSON 消息（`schema=rtdiag/2.0`，`frameType=request`），并返回 `RuntimeDiagnosticFrame` 响应（`frameType=response`）。

统一协议约束：
- 管道协议以 JSON Frame 为唯一入口，不再支持旧命令对象直连。
- CLI 命令语法仅作为人机接口，最终全部包装为 Frame 下发到运行时。
- 新增字段应优先扩展 `header`、`status`、`extStatus`、`data`，避免回退到旧结构。

不要实现自定义诊断客户端，使用 `Iwesun.Runtime.Cli` 连接。

## Serilog 集成

`RuntimeDiagnosticLoggerProvider` 将 Serilog 事件路由到诊断开关板。日志事件通过 `log.*` 监控点输出，可按 section（如 `pipeline`、`agent-sync`、`peer-sync`）控制。

## 诊断目标

开关板支持多个输出目标：
- **命名管道**：实时监控（CLI 连接）
- **文件**：持久化诊断输出
- **日志**：Serilog 集成

输出目标可独立启用/禁用。

## 设计原则

- 默认不输出
- 通过 switchboard/配置控制监视点
- 输出点与业务配置分离
- 不改变主程序逻辑
- 只能观察和取样，不能代替业务决策

## 相关文档

- CLI 使用 → [IWESUN_RUNTIME_CLI.md](IWESUN_RUNTIME_CLI.md)
- 统一界面原则 → [../04-interface/UNIFIED_INTERFACE.md](../04-interface/UNIFIED_INTERFACE.md)
- 诊断开发规范 → [../../.github/instructions/runtime-diagnostics.instructions.md](../../.github/instructions/runtime-diagnostics.instructions.md)

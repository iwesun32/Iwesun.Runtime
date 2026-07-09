# Runtime Diagnostics 升级迁移对照表

> 状态: CURRENT
> 更新时间: 2026-07-09

本文用于说明 Runtime Diagnostics 从旧协议到统一 Frame 协议的迁移方式，覆盖 Runtime 仓库与 Ddns Snap 引用点。

## 1. 升级目标

- 管道协议统一为 JSON Frame (`schema=rtdiag/2.0`)。
- CLI 语法仍保留可读性，但只作为人机接口，最终全部包装为 Frame。
- 旧命令入口不再由 `RuntimeDiagnosticsMonitor` 接收。

## 2. 核心接口迁移

| 旧接口 | 新接口 | 说明 |
| --- | --- | --- |
| RuntimeDiagnosticsMonitor 解析 RuntimeDiagnosticCommand | RuntimeDiagnosticsMonitor 解析 RuntimeDiagnosticFrame | 监控器仅接收 Frame 请求并返回 Frame 响应 |
| RuntimeDiagnosticHub.ExecuteAsync(RuntimeDiagnosticCommand) 外部直调 | RuntimeDiagnosticHub.ExecuteFrameAsync(RuntimeDiagnosticFrame) | 对外统一入口，内部仍可复用旧执行链 |
| UI Pipe payload 使用 RuntimeDiagnosticCommand | UI Pipe payload 使用 RuntimeDiagnosticFrame | Service / Agent 的 runtime.diagnostics 已切换 |
| CLI 直接发送 RuntimeDiagnosticCommand | CLI 发送 RuntimeDiagnosticFrame | 由 ProtocolBoundary + FrameFactory 统一封装 |

## 3. CLI 命令层迁移

| 层级 | 旧方式 | 新方式 |
| --- | --- | --- |
| 启动示例 | dotnet run --project ... | iwrt ...（发行版 exe） |
| 配置文件 | Iwesun.Runtime.Cli.commands.json | Iwesun.Runtime.Cli.commands.v2.json |
| 边界模型 | RuntimeDiagnosticCommand | RuntimeDiagnosticFrameCommand -> RuntimeDiagnosticFrame |
| 传输语义 | 命令对象即协议 | 命令语义与协议封装分离 |

## 4. 请求映射对照

| 语义字段 | Frame 字段 |
| --- | --- |
| namespace / category | header.category |
| operation | header.operation |
| targetId | command.target |
| action | command.action |
| member | command.member |
| args | command.args |
| value | command.args.value |

## 5. 响应映射对照

| 旧结果概念 | 新 Frame 位置 |
| --- | --- |
| success | status.ok |
| error code/message | status.code / status.message |
| target/action 补充信息 | extStatus.target / extStatus.action |
| value | data |

## 6. Ddns Snap 已完成改造点

- src/DdnsSnap.Service/Agent/UiPipeHostedService.cs
  - runtime.diagnostics 改为反序列化 RuntimeDiagnosticFrame 并调用 ExecuteFrameAsync。
- src/DdnsSnap.Agent/Services/AgentPipeHostedService.cs
  - runtime.diagnostics 改为 Frame 调用链。
- tests/DdnsSnap.Tests/Diagnostics/RuntimeDiagnosticsTests.cs
  - 诊断调用改为构造 Frame 请求。

## 7. 兼容性说明

- 破坏性变更: RuntimeDiagnosticsMonitor 不再接收旧命令对象 JSON。
- 建议回滚策略:
  1. 如需临时兼容，优先在调用侧封装 Frame，而不是恢复 Monitor 旧分支。
  2. 禁止新增任何直接依赖旧命令模型的外部入口。

## 8. 验证清单

- Runtime: dotnet build Iwesun.Runtime.slnx -c Release --nologo
- Ddns Snap: dotnet build DdnsSnap.slnx -c Release --nologo
- CLI: 使用 iwrt status / iwrt monitor 验证 Frame 请求链路

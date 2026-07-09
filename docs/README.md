# Iwesun Runtime 文档索引

Iwesun Runtime 是独立的运行时工具库，提供诊断框架、CLI 客户端和 WebView2 控制接口。

## 项目结构

| 项目 | 说明 |
|------|------|
| `Iwesun.Runtime.Diagnostics` | 运行时诊断内核：开关板、FIFO、Hub、反射、监控管道 |
| `Iwesun.Runtime.WebView2` | WebRuntime 客户端模型和管道客户端 |
| `Iwesun.Runtime.Cli` | 命令行工具：诊断、快照查询、事件消费、WebRuntime 控制 |

解决方案：`Iwesun.Runtime.slnx`

## 文档列表

| 文档 | 说明 |
|------|------|
| [RUNTIME_DIAGNOSTICS.md](RUNTIME_DIAGNOSTICS.md) | 运行时诊断框架、Switchboard、FIFO、管道命令 |
| [IWESUN_RUNTIME_CLI.md](IWESUN_RUNTIME_CLI.md) | CLI 命令参考 |

## 归档

以下内容已移入 `docs/archive/`，作为历史资料保留，不再作为当前权威入口：

- 迁移期对照文档
- `design/` 下的阶段性设计与实施计划文档
- 旧 AI / Skill 文档与 AIGateway 语境资料

当前 Runtime.Diagnostics 升级任务交接见 [archive/HANDOFF_2026-07-09_RUNTIME_DIAGNOSTICS.md](archive/HANDOFF_2026-07-09_RUNTIME_DIAGNOSTICS.md)。

归档索引见 [archive/README.md](archive/README.md)。

## 构建

```bash
dotnet build Iwesun.Runtime.slnx -c Release
```

## 宿主集成

宿主应用（如 DDNS Snap）通过跨仓库 `ProjectReference` 引用 `Iwesun.Runtime.Diagnostics`。CLI 作为独立工具运行，不被宿主引用。

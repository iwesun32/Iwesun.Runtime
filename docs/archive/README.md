# Iwesun Runtime 归档索引

本目录保存已经退出活跃入口的历史资料，仅供追溯参考，不作为当前设计、实现或操作说明的权威来源。

## 归档内容

### 交接笔记

| 文档 | 说明 |
|------|------|
| [HANDOFF_2026-07-09_RUNTIME_DIAGNOSTICS.md](HANDOFF_2026-07-09_RUNTIME_DIAGNOSTICS.md) | Runtime.Diagnostics 全面升级任务的阶段性交接日志，供账号切换后续接 |

### 迁移资料

| 文档 | 说明 |
|------|------|
| [DIAGNOSTICS_MIGRATION_GUIDE.md](DIAGNOSTICS_MIGRATION_GUIDE.md) | Runtime Diagnostics 从旧协议迁移到统一 Frame 协议的对照表 |

### 旧 AI / Skill 文档

| 文档 | 说明 |
|------|------|
| [AI_RUNTIME_TOOLING.md](AI_RUNTIME_TOOLING.md) | 旧版 AI 运行时工具整体说明 |
| [AI_RUNTIME_TOOLING_SKILL.md](AI_RUNTIME_TOOLING_SKILL.md) | 旧版 AI 运行时工具技能文档 |
| [RUNTIME_DIAGNOSTICS_SKILL.md](RUNTIME_DIAGNOSTICS_SKILL.md) | 旧版运行时诊断技能文档 |
| [IWESUN_RUNTIME_CLI_SKILL.md](IWESUN_RUNTIME_CLI_SKILL.md) | 旧版 CLI 技能文档 |

### 阶段性设计资料

位于 [design/](design/)：

- [design/AI_DEBUG_TOOL_DESIGN.md](design/AI_DEBUG_TOOL_DESIGN.md)
- [design/CLI_COMMAND_PROTOCOL.md](design/CLI_COMMAND_PROTOCOL.md)
- [design/IMPLEMENTATION_PLAN.md](design/IMPLEMENTATION_PLAN.md)

## 使用规则

1. 活跃文档入口以上级 [../README.md](../README.md) 为准。
2. 归档文档允许保留旧命名、旧示例和阶段性语境，不要求与当前实现持续同步。
3. 若历史资料重新变成现行规则，应重新整理后回到活跃目录，而不是直接从归档引用。
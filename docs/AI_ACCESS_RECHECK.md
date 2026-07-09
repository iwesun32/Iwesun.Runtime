# AI 访问规则复核（忽略/禁止/推荐）

> **状态**: CURRENT | **最后更新**: 2026-07-09

本文档用于复核当前仓库 AI 操作边界，统一“哪些可以读、哪些禁止读、哪些推荐优先读”。

## 1. 忽略与禁止（必须遵守）

依据 `.github/instructions/copilot-access-rules.instructions.md` 与 `.copilotignore`：

### 1.1 硬性禁止读取/枚举

- `ai-ignore/`
- `archive/`
- `docs/archive/`
- `**/bin/`、`**/obj/`、`**/Debug/`、`**/Release/`、`**/publish/`、`**/net*/`

说明：以上目录属于归档、生成产物或噪声区域，不作为当前有效实现依据。

### 1.2 软限制区域（可列出，不自动读取）

- `release/`：可列目录，不读二进制内容
- `tools/`：仅在明确请求时读取
- `UpgradeLog*.htm`：可列出，不读取

## 2. 推荐优先读取区域（Active Zones）

- `Iwesun.Runtime.Diagnostics/`
- `Iwesun.Runtime.Cli/`
- `Iwesun.Runtime.WebView2/`
- `docs/`（活跃文档）
- `.github/`（规则与配置）
- `AGENTS.md`
- `Directory.Build.props`
- `Iwesun.Runtime.slnx`

## 3. 推荐工作流（最小上下文）

1. 先搜索再读取（Search first, read second）
2. 默认只读 2-5 个文件，必要时再扩展
3. 优先读取最小行范围，避免大范围扫读
4. 编辑时只改与目标直接相关内容，避免连带重构

## 4. 仓库内补充约束（来自 copilot-instructions.md）

- 终端命令优先检查语法；耗时命令优先后台执行并轮询
- 调试结论前需核实运行时状态，不以“代码存在”替代“实际已启动”
- 文档与代码修改应一次给出完整可执行结果，非重大分叉不反复确认

## 5. 执行前检查清单

- [ ] 是否避开了硬性禁止目录
- [ ] 是否在 Active Zones 内完成主要分析
- [ ] 是否先搜索再读取目标文件
- [ ] 是否只做与当前任务直接相关的修改
- [ ] 是否更新了 `docs/README.md` 的入口索引（如新增文档）

# CLI Context Shell Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 Runtime CLI 增加规范化三文件配置、当前目录缺省用户配置、交互 Shell、进程内变量/记忆和统一 Runtime 虚拟路径，同时保持单次模式及协议兼容。

**Architecture:** 配置加载、单命令执行、上下文、虚拟路径和 REPL 拆成独立文件；所有远程操作最终复用同一 `CliCommandExecutor`。上下文只在客户端内存中，`exit/quit` 只结束 Shell，不发送 Frame。

**Tech Stack:** C#/.NET 10、System.Text.Json、RuntimeDiagnosticFrame、FunctionalTests、PowerShell 全量发布。

---

### Task 1: 配置命名与加载优先级

**Files:**
- Create: `Iwesun.Runtime.Cli/CliConfigurationLoader.cs`
- Modify: `Iwesun.Runtime.Cli/CliV3.cs`
- Rename content: `RuntimeCliSystemConfig.json`, `RuntimeCliSystemMetadata.json`, `RuntimeCliUserConfig.example.json`

- [ ] 新增配置加载测试：缺省当前目录用户文件、显式文件优先、缺省不存在、显式不存在。
- [ ] 把系统/metadata 加载从 `CliV3` 移到 `CliConfigurationLoader`。
- [ ] 保持 v3 merge、alias、composite 和 schema 校验。
- [ ] 构建并运行配置场景。

### Task 2: 提取单命令执行器和 tokenizer

**Files:**
- Create: `Iwesun.Runtime.Cli/CliCommandExecutor.cs`
- Create: `Iwesun.Runtime.Cli/CliTokenizer.cs`
- Modify: `Iwesun.Runtime.Cli/CliV3.cs`

- [ ] 用现有单次命令回归固定 Frame 和退出码。
- [ ] 提取 Bind、Frame 构造、pipe 发送和响应判定。
- [ ] tokenizer 支持引号、反斜杠转义和空白分隔。
- [ ] 验证单次模式输出仍为一个 JSON。

### Task 3: 上下文和虚拟路径

**Files:**
- Create: `Iwesun.Runtime.Cli/CliContext.cs`
- Create: `Iwesun.Runtime.Cli/RuntimeVirtualPathRouter.cs`

- [ ] 测试变量、未定义变量、命令参数记忆、敏感/危险参数排除。
- [ ] 实现 `$name`、`${name}` 展开和命令级记忆。
- [ ] 实现 `/host`、lifecycle、registry、execution、switchboard、pipes、files、reflection 路径映射。
- [ ] 测试 `pwd/cd/../ls/get/root` 与 reflection 长路径。

### Task 4: 交互 Shell

**Files:**
- Create: `Iwesun.Runtime.Cli/CliInteractiveShell.cs`
- Modify: `Iwesun.Runtime.Cli/CliV3.cs`
- Modify: `Iwesun.Runtime.Cli/Program.cs`

- [ ] 写入 stdin 脚本场景，断言 Shell 连续执行、多命令失败继续。
- [ ] 实现 `shell` 与 `--interactive` 入口和本地命令。
- [ ] 测试 `exit/quit/EOF` 不产生管道请求且宿主继续运行。
- [ ] 测试显式 `lifecycle.shutdown` 才停止宿主。

### Task 5: 文档、技能、版本与发布

**Files:**
- Modify: `docs/IWESUN_RUNTIME_CLI.md`
- Modify: `docs/IWESUN_RUNTIME_QUICK_START.md`
- Modify: `docs/IWESUN_RUNTIME_USER_GUIDE.md`
- Modify: `skills/iwesun-runtime-integration/references/json-cli.md`
- Modify: `Iwesun.Runtime.Release/Iwesun.Runtime.Release.csproj`
- Modify: `scripts/release/verify-runtime-install.ps1`

- [ ] 同步三文件名、优先级、Shell、本地命令、变量和虚拟路径。
- [ ] 同步仓库技能、本机技能、SampleHost 和发布自检。
- [ ] Debug/Release 全构建并运行 CLI/SampleHost/退出回归。
- [ ] 版本升级到 1.0.18，执行唯一全量打包并核对 MSI 哈希。

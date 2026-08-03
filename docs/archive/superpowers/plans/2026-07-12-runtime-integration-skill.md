# Runtime Integration Skill Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 创建可自动触发的 `iwesun-runtime-integration` 技能，指导 Codex 在宿主项目中安全执行 Runtime 主程序替换、受管对象替换、单点注入、状态退出和 JSON/CLI 操作。

**Architecture:** SKILL.md 只保留任务路由、执行顺序和安全边界；五个专题 reference 按需加载详细步骤。技能在 Codex 自动发现目录和 Runtime 仓库各保留一份字节级一致的副本。

**Tech Stack:** Codex Skills, Markdown references, YAML agent metadata, skill-creator validation scripts.

---

### Task 1: 初始化技能主副本

**Files:**
- Create: `C:/Users/LYH/.codex/skills/iwesun-runtime-integration/SKILL.md`
- Create: `C:/Users/LYH/.codex/skills/iwesun-runtime-integration/agents/openai.yaml`
- Create: `D:/Git Space/Runtime/skills/iwesun-runtime-integration/SKILL.md`
- Create: `D:/Git Space/Runtime/skills/iwesun-runtime-integration/agents/openai.yaml`

- [ ] **Step 1: 用 skill-creator 初始化主副本**

Run `init_skill.py` twice with skill name `iwesun-runtime-integration`, resources `references`, and interface values:

```text
display_name=Iwesun Runtime Integration
short_description=Integrate hosts with Iwesun Runtime safely
default_prompt=Integrate this .NET host with Iwesun Runtime using the standard startup, managed execution, diagnostics, state, shutdown, and CLI patterns.
```

Expected: 两个目录均生成 SKILL.md、agents/openai.yaml 和 references/。

- [ ] **Step 2: 删除初始化占位内容**

不保留模板占位标记、示例占位文件或多余 README。

### Task 2: 编写精简 SKILL.md

**Files:**
- Modify: `C:/Users/LYH/.codex/skills/iwesun-runtime-integration/SKILL.md`
- Mirror: `D:/Git Space/Runtime/skills/iwesun-runtime-integration/SKILL.md`

- [ ] **Step 1: 写入触发元数据**

```yaml
---
name: iwesun-runtime-integration
description: Integrate or migrate .NET hosts to Iwesun Runtime. Use when replacing Program.cs startup, converting Process/Thread/Task creation to RProcess/RThread/RTask, adding RuntimeInjector output/watch/break/data points, implementing managed state/events/coordinated shutdown, or operating Runtime JSON and CLI commands.
---
```

- [ ] **Step 2: 写入任务路由**

SKILL.md 要求先读仓库 AGENTS.md 和活跃需求，然后根据任务只读对应 reference：host-startup、managed-execution、diagnostic-injection、state-shutdown-events、json-cli。

- [ ] **Step 3: 写入核心安全约束**

包含：不用 Console.WriteLine 诊断、默认静默、反射白名单、Debug 断点隔离、复杂信息走管道、简单状态走 FIFO、销毁时反登记、安全退出后恢复 quiet。

### Task 3: 编写五个按需 reference

**Files:**
- Create: `C:/Users/LYH/.codex/skills/iwesun-runtime-integration/references/host-startup.md`
- Create: `C:/Users/LYH/.codex/skills/iwesun-runtime-integration/references/managed-execution.md`
- Create: `C:/Users/LYH/.codex/skills/iwesun-runtime-integration/references/diagnostic-injection.md`
- Create: `C:/Users/LYH/.codex/skills/iwesun-runtime-integration/references/state-shutdown-events.md`
- Create: `C:/Users/LYH/.codex/skills/iwesun-runtime-integration/references/json-cli.md`
- Mirror: `D:/Git Space/Runtime/skills/iwesun-runtime-integration/references/*.md`

- [ ] **Step 1: 编写 host-startup.md**

只放标准 Program.cs 流程、配置点、固定/业务区分和旧启动痕迹删除规则。

- [ ] **Step 2: 编写 managed-execution.md**

包含 Process/Thread/Task 替换矩阵、返回类型、unitId、启停、等待、Dispose、回滚和反登记。

- [ ] **Step 3: 编写 diagnostic-injection.md**

包含编译属性、Output/Watch/Break/BreakIfNumbers/Data 单点样例、Debug/Release 矩阵和反射白名单。

- [ ] **Step 4: 编写 state-shutdown-events.md**

包含全局/业务状态分层、FIFO 状态指令、事件、守护程序与六步协调退出。

- [ ] **Step 5: 编写 json-cli.md**

包含 rtdiag/2.0、rtdiag/3.0、little-endian wire format、CLI v3 状态门、命令调试后 quiet 流程。

### Task 4: 生成 UI 元数据并验证技能

**Files:**
- Modify: `C:/Users/LYH/.codex/skills/iwesun-runtime-integration/agents/openai.yaml`
- Mirror: `D:/Git Space/Runtime/skills/iwesun-runtime-integration/agents/openai.yaml`

- [ ] **Step 1: 按 skill-creator 规则生成 openai.yaml**

Run: `generate_openai_yaml.py` with the three approved interface values from Task 1.

- [ ] **Step 2: 验证主副本**

Run `quick_validate.py` separately against both skill directories.

Expected: both validations succeed.

- [ ] **Step 3: 比较主副本**

Run: `git diff --no-index -- C:/Users/LYH/.codex/skills/iwesun-runtime-integration D:/Git Space/Runtime/skills/iwesun-runtime-integration`

Expected: exit code 0 and no differences.

### Task 5: 前向测试技能路由

**Files:**
- Modify if needed: both skill copies

- [ ] **Step 1: 测试主程序替换路由**

使用任务：`Use $iwesun-runtime-integration to explain which files and APIs must change when replacing an existing .NET host Program.cs.`

Expected: 只加载 host-startup reference，不伪造 CLI v3 已实施。

- [ ] **Step 2: 测试单点注入路由**

使用任务：`Use $iwesun-runtime-integration to add one watch point, one numeric breakpoint, and a reflection whitelist to a host.`

Expected: 加载 diagnostic-injection，断点用 `#if DEBUG`，反射列出显式成员。

- [ ] **Step 3: 测试退出路由**

使用任务：`Use $iwesun-runtime-integration to audit a process/thread/task coordinated shutdown flow.`

Expected: 加载 state-shutdown-events，同时检查轮询与 FIFO Stop、反登记和超时退出。

### Task 6: 记录、提交和安装复核

**Files:**
- Modify: `docs/REQUIREMENTS_ACTIVE.md`
- Add: `skills/iwesun-runtime-integration/**`

- [ ] **Step 1: 记录技能主副本路径和验证结果**

- [ ] **Step 2: 确认自动发现主副本存在**

Run: `Test-Path C:/Users/LYH/.codex/skills/iwesun-runtime-integration/SKILL.md`

Expected: `True`.

- [ ] **Step 3: 提交仓库副本**

```powershell
git add skills/iwesun-runtime-integration docs/REQUIREMENTS_ACTIVE.md
git commit -m "feat: add Runtime integration skill"
```

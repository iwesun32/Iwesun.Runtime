# Runtime User Guide Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立一份从宿主替换到 JSON/CLI 操作的 Runtime 业务接入权威手册，并清理现有文档中的冲突入口。

**Architecture:** 新手册按改造时序组织为五章和附录；代码样例以 SampleHost 和 Diagnostics 当前签名为唯一事实源。CLI v3 未实施前明确标记为迁移中，实施后再从新 JSON 目录生成最终清单。

**Tech Stack:** Markdown, C#/.NET 10 API examples, RuntimeDiagnosticFrame JSON, CLI v3 specification.

---

### Task 1: 建立手册骨架与版本状态

**Files:**
- Create: `docs/IWESUN_RUNTIME_USER_GUIDE.md`
- Modify: `docs/README.md`

- [ ] **Step 1: 创建五章与附录目录**

写入固定顶部信息：

```markdown
# Iwesun Runtime 用户手册

> 状态：CURRENT  
> 适用：.NET 10 业务宿主  
> CLI v3：迁移中，本文不将未实施命令标记为当前可用
```

- [ ] **Step 2: 把手册加入文档快速入口**

在 `docs/README.md` 快速入口表中增加 `IWESUN_RUNTIME_USER_GUIDE.md`，说明它是业务接入权威手册。

- [ ] **Step 3: 检查目录和链接**

Run: `rg -n "^#|IWESUN_RUNTIME_USER_GUIDE" docs/IWESUN_RUNTIME_USER_GUIDE.md docs/README.md`

Expected: 手册和快速入口均可检索。

- [ ] **Step 4: 提交骨架**

```powershell
git add docs/IWESUN_RUNTIME_USER_GUIDE.md docs/README.md
git commit -m "docs: establish Runtime user guide"
```

### Task 2: 编写标准主程序章节

**Files:**
- Modify: `docs/IWESUN_RUNTIME_USER_GUIDE.md`
- Reference: `Iwesun.Runtime.SampleHost/Program.cs`
- Reference: `Iwesun.Runtime.Diagnostics/RuntimeHostTemplate.cs`
- Reference: `Iwesun.Runtime.Diagnostics/RuntimeShutdownCoordinator.cs`

- [ ] **Step 1: 核对真实宿主 API**

Run: `rg -n "AddRuntimeDiagnostics|\.Start\(|Activate\(|RunAsync|ShutdownAsync" Iwesun.Runtime.SampleHost/Program.cs Iwesun.Runtime.Diagnostics/RuntimeHostTemplate.cs Iwesun.Runtime.Diagnostics/RuntimeShutdownCoordinator.cs`

Expected: 确认推荐入口是 `Services.Start` 和 `Services.Activate`。

- [ ] **Step 2: 写入可编译的 Program.cs 样例**

样例必须包含 `Host.CreateApplicationBuilder`、`Logging.AddRuntimeDiagnostics`、`Services.Start`、业务 DI、`Build`、`Activate`、`RunAsync` 和协调退出说明，并用注释标记固定模板与业务填写区。

- [ ] **Step 3: 补充启动配置表**

列出 runtimeDirectory、startupRuntimeDiagnosticsPipeName、程序名、业务服务和静态登记点的定义位置。

- [ ] **Step 4: 验证不再推荐旧分散启动**

Run: `rg -n "UseRuntimeDiagnostics|BuildDiagnosticRegistries" docs/IWESUN_RUNTIME_USER_GUIDE.md`

Expected: 只出现在迁移删除说明中。

### Task 3: 编写进程、线程、任务替换章节

**Files:**
- Modify: `docs/IWESUN_RUNTIME_USER_GUIDE.md`
- Reference: `Iwesun.Runtime.Diagnostics/RProcess.cs`
- Reference: `Iwesun.Runtime.Diagnostics/RThread.cs`
- Reference: `Iwesun.Runtime.Diagnostics/RTask.cs`
- Reference: `Iwesun.Runtime.Diagnostics/RuntimeHostTemplate.cs`

- [ ] **Step 1: 核对创建、启动、等待、停止和 Dispose 签名**

Run: `rg -n "public .*Start|public .*Join|public .*Wait|public .*Dispose|CreateProcess|CreateThread|CreateTask" Iwesun.Runtime.Diagnostics/RProcess.cs Iwesun.Runtime.Diagnostics/RThread.cs Iwesun.Runtime.Diagnostics/RTask.cs Iwesun.Runtime.Diagnostics/RuntimeHostTemplate.cs`

- [ ] **Step 2: 写入四组旧代码/新代码对照**

覆盖 `Process.Start`、`new Process`、`new Thread`、`Task.Run/new Task`，并明确返回类型为 `RProcess`、`RThread`、`RTask`。

- [ ] **Step 3: 写入所有权和反登记规则**

每个对象说明 unitId、lifetime、CancellationToken、启动失败回滚、真实结束后反登记和运行中 Dispose 的行为。

- [ ] **Step 4: 写入不可机械替换的边界**

包括依赖 `Task<T>` 返回值、特定 TaskScheduler、UI 线程、必须继承 Process 的旧调用方。

### Task 4: 编写单点诊断注入章节

**Files:**
- Modify: `docs/IWESUN_RUNTIME_USER_GUIDE.md`
- Reference: `Iwesun.Runtime.SampleHost/Program.cs`
- Reference: `Iwesun.Runtime.SampleHost/SampleHostWorker.cs`
- Reference: `Iwesun.Runtime.Diagnostics/RuntimeHostTemplate.cs`
- Reference: `Iwesun.Runtime.Diagnostics/RuntimeOutput.cs`

- [ ] **Step 1: 写入编译期属性对照表**

表格覆盖 PipePrefix、FileOutput、WatchPoint、Breakpoint、NumericBreakpoint 和 HookableEvent 的参数、缺省状态与 Debug/Release 行为。

- [ ] **Step 2: 为 Output、Watch、Break、BreakIfNumbers、Data 写完整代码段**

每段包含稳定 ID、条件、上下文和调用，断点代码用 `#if DEBUG` 包装。

- [ ] **Step 3: 写入反射白名单样例**

使用 `RuntimeDiagnosticObjectAccess` 显式列出 ReadableMembers、WritableMembers 和 InvokableMembers，禁止宽泛暴露。

- [ ] **Step 4: 验证所有 API 名可在源码找到**

Run: `rg -n "RuntimeInjector\.(Output|Watch|Break|Data)|BreakIfNumbers|Diagnostic(HookableEvent|NumericBreakpoint|Breakpoint|WatchPoint|FileOutput|PipePrefix)" Iwesun.Runtime.Diagnostics Iwesun.Runtime.SampleHost docs/IWESUN_RUNTIME_USER_GUIDE.md`

Expected: 手册的每类 API 都有真实定义或样例。

### Task 5: 编写业务状态、事件与退出章节

**Files:**
- Modify: `docs/IWESUN_RUNTIME_USER_GUIDE.md`
- Reference: `Iwesun.Runtime.Diagnostics/RManagedState.cs`
- Reference: `Iwesun.Runtime.Diagnostics/RuntimeStateManager.cs`
- Reference: `Iwesun.Runtime.Diagnostics/RuntimeShutdownCoordinator.cs`
- Reference: `Iwesun.Runtime.Diagnostics/RuntimeManagedRegistry.cs`

- [ ] **Step 1: 写入粗粒度与业务细分状态对照**

区分 RuntimeStateManager 全局状态和 RManagedState 的 SetDetail、TryGetDetail、TransitionTo、TryTransitionTo。

- [ ] **Step 2: 写入事件样例**

展示实例事件静态登记、触发和弱引用钩子，同时说明静态事件必须显式卸载。

- [ ] **Step 3: 写入六步协调退出流程**

包含全局准备关机、逐个 FIFO Stop、业务清理、状态更新、反登记、DLIST 清空检查和超时退出。

- [ ] **Step 4: 同时展示轮询和 FIFO 事件驱动守护程序**

轮询全局状态位作为保底，FIFO 命令作为快速唤醒通道，复杂内容使用管道。

### Task 6: 编写 JSON/CLI 章节与附录

**Files:**
- Modify: `docs/IWESUN_RUNTIME_USER_GUIDE.md`
- Reference: `Iwesun.Runtime.Diagnostics/RuntimeDiagnosticModels.cs`
- Reference: `docs/superpowers/specs/2026-07-12-cli-v3-redesign.md`

- [ ] **Step 1: 写入 rtdiag/2.0 和 rtdiag/3.0 完整样例**

单命令使用 header + command，batch 使用 header + batch，响应展示 status + data + meta。

- [ ] **Step 2: 写入长度前缀规范**

明确为 `[4-byte little-endian int32][UTF-8 JSON]`，不保留 big-endian 旧说明。

- [ ] **Step 3: 写入 CLI v3 迁移中清单**

列出 application、endpoints、commands、workflows、extensions、别名和退出码，但在 CLI v3 代码完成前不标记为当前可执行。

- [ ] **Step 4: 写入四个附录表**

完成最小接入、完整接入、Debug/Release 能力和旧痕迹删除清单。

### Task 7: 清理权威文档冲突

**Files:**
- Modify: `docs/IWESUN_RUNTIME_DESIGN.md`
- Modify: `docs/RUNTIME_DIAGNOSTICS.md`
- Modify: `docs/UNIFIED_INTERFACE.md`
- Modify: `docs/IWESUN_RUNTIME_CLI.md`

- [ ] **Step 1: 把设计文档的详细接入步骤替换为手册链接**

- [ ] **Step 2: 删除 big-endian、v2 配置和旧命令的权威性陈述**

- [ ] **Step 3: 保留必要历史说明时显式标记为已废弃**

- [ ] **Step 4: 扫描冲突**

Run: `rg -n "big-endian|commands\.v2|baseCommands|compositeCommands|sw\.|bp\.|reg\." docs -g '*.md' -g '!archive/**'`

Expected: 活动文档中只剩明确的已废弃/迁移说明。

### Task 8: 最终校验与提交

**Files:**
- Modify: `docs/REQUIREMENTS_ACTIVE.md`

- [ ] **Step 1: 逐项回读用户手册设计规格**

Run: `Get-Content -Raw docs/superpowers/specs/2026-07-12-runtime-user-guide-design.md`

- [ ] **Step 2: 检查 Markdown 差异**

Run: `git diff --check`

Expected: 无 whitespace 错误。

- [ ] **Step 3: 记录验收状态**

在 `docs/REQUIREMENTS_ACTIVE.md` 勾选手册五章、附录、源码核对和文档冲突清理。

- [ ] **Step 4: 提交完整手册**

```powershell
git add docs
git commit -m "docs: publish comprehensive Runtime user guide"
```

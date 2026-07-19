# Breakpoint Probe Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在保留现有 MMF、Semaphore 和历史注释的前提下，试探性验证断点并发、上下文、重复登记和释放缺陷的最小修复是否可行。

**Architecture:** 不重写断点协议，也不删除旧结构。新增一个聚焦功能场景；生产实现增加每断点局部状态锁、等待/恢复计数、可取消等待、安全上下文捕获、重复登记拒绝和标准 `IDisposable` 生命周期。宿主协调退出联动本轮仅记录测试结果，不修改公共退出协议。

**Tech Stack:** C# 14、.NET 10、System.Threading、System.Text.Json、现有 FunctionalTests 场景运行器。

---

### Task 1: 固化失败行为

**Files:**
- Modify: `Iwesun.Runtime.FunctionalTests/Program.cs`
- Create: `Iwesun.Runtime.FunctionalTests/BreakpointSafetyScenario.cs`

- [x] **Step 1: 新增 `breakpoint-safety` 场景**

场景必须使用真实 `RuntimeDiagnosticBreakpoints`，依次验证：两个并发等待者可分别恢复、输出门关闭不屏蔽断点、循环 context 不向业务抛异常、重复 ID 明确拒绝、Dispose 恢复等待者。

实施中追加压力边界：32 路突发恢复不得遗留许可、Disable 释放全部等待者、等待开始后的 CancellationToken 能退出且不污染下一次命中。

```csharp
var first = breakpoints.WaitAsync(id);
var second = breakpoints.WaitAsync(id);
breakpoints.Resume(id);
breakpoints.Resume(id);
await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(2));
```

- [x] **Step 2: 运行红测**

Run: `dotnet run --project Iwesun.Runtime.FunctionalTests -c Debug -- --child --scenario breakpoint-safety`

Expected: FAIL，至少报告并发恢复、输出门独立、循环 context、重复登记或 Dispose 行为缺失。

### Task 2: 最小试探实现

**Files:**
- Modify: `Iwesun.Runtime.Diagnostics/RuntimeDiagnosticBreakpoints.cs`

- [x] **Step 1: 保留旧结构并增加等待计数**

在 `BreakpointState` 中增加本地 `WaitingCount`；保留 `SharedState.IsWaiting`、MMF、Semaphore 和原注释。Semaphore 容量扩大，使每次 `Resume` 只释放一个实际等待者，`ResumeAll`/`Disable` 释放当前全部等待者。

```csharp
var waiting = Interlocked.Increment(ref bp.WaitingCount);
bp.SharedState.IsWaiting = waiting > 0 ? 1 : 0;
try { await Task.Run(() => bp.Signal.WaitOne(), ct); }
finally { bp.SharedState.IsWaiting = Interlocked.Decrement(ref bp.WaitingCount) > 0 ? 1 : 0; }
```

- [x] **Step 2: 隔离上下文序列化失败**

先捕获可安全序列化的 `JsonElement`；失败时保存有限错误描述，不允许诊断异常进入业务调用链。`LastContext` 与命中事件共用该安全快照。

- [x] **Step 3: 拒绝重复登记并接入 IDisposable**

`Register` 使用 `TryAdd`；重复 ID 释放新对象并抛出明确配置异常。`RuntimeDiagnosticBreakpoints` 实现 `IDisposable`，保留原 `Dispose()` 方法体并使其幂等。

- [x] **Step 4: 运行绿测**

Run: `dotnet run --project Iwesun.Runtime.FunctionalTests -c Debug -- --child --scenario breakpoint-safety`

Expected: PASS，且无等待 Task 残留。

### Task 3: CLI 结果与退出链观察

**Files:**
- Modify only if a failing test confirms: `Iwesun.Runtime.Diagnostics/RuntimeDiagnosticHub.cs`
- Modify: `Iwesun.Runtime.FunctionalTests/BreakpointSafetyScenario.cs`

- [x] **Step 1: 验证未知 ID**

未知 breakpoint enable/disable/resume 必须返回失败状态，而不是 `OK + false`。先写 Frame 级失败测试；只在测试确认后修改 Hub。

- [x] **Step 2: 记录停止期间行为**

验证 Dispose 能释放等待者；本轮不把断点接入 `RuntimeShutdownCoordinator`，避免在缺少真实服务退出证据时修改公共退出协议。

### Task 4: 回归与提交边界

**Files:**
- Modify: `docs/REQUIREMENTS_ACTIVE.md`

- [x] **Step 1: 聚焦回归**

Run: `breakpoint-safety`、`bp-process-cli`、`numeric-breakpoint`；`bp-process-cli` 连续运行三轮。

- [x] **Step 2: 全构建**

Run: `dotnet build Iwesun.Runtime.slnx -c Debug --nologo` and Release equivalent.

- [x] **Step 3: 记录试探结论**

文档明确区分“已由回归确认的试探修复”和“等待更多服务退出数据后再决定的协调退出改造”。本轮不打包、不发布。

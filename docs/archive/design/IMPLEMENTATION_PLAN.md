# AI 运行时调试工具 — 分步实施计划

> 基于设计文档 [AI_DEBUG_TOOL_DESIGN.md](./AI_DEBUG_TOOL_DESIGN.md)

---

## Phase 1：核心基础设施 — 断点系统

### 目标
实现协作式逻辑断点，CLI 可通过管道控制断点启停。

### 任务清单

| # | 任务 | 文件 | 工作内容 |
|---|------|------|---------|
| 1.1 | 新增 `RuntimeDiagnosticBreakpoints` 类 | `RuntimeDiagnosticBreakpoints.cs` | 断点注册表（ConcurrentDictionary）、Register/WaitAsync/Resume/Snapshot、超时机制、每个断点独立的 SemaphoreSlim 信号 |
| 1.2 | 扩展 `RuntimeOutput` 增加 `BreakIf` 方法 | `RuntimeOutput.cs` | 三个重载：无条件 / 条件 / 条件+上下文，全部先检查 `RuntimeOutputSwitch.Enabled` |
| 1.3 | 新增断点事件模型 | `RuntimeDiagnosticModels.cs` | `BreakpointHitEvent`、`BreakpointResumeCommand` |
| 1.4 | 扩展 `RuntimeDiagnosticHub` 增加断点 action | `RuntimeDiagnosticHub.cs` | `breakpoint.list`、`breakpoint.enable`、`breakpoint.disable`、`breakpoint.resume`、`breakpoint.resumeAll` |
| 1.5 | 新增 `DiagnosticPipePrefix` 类 | `DiagnosticPipePrefix.cs` | 管道名前缀注入：`[assembly: DiagnosticPipePrefix]` 特性 + `InitializeFromAssembly` + `Resolve` |
| 1.6 | 扩展 DI 注册 | `RuntimeDiagnosticsServiceCollectionExtensions.cs` | 注册 `RuntimeDiagnosticBreakpoints` 为 singleton，`UseRuntimeDiagnostics` 中初始化 |
| 1.7 | 断点自测目标 | 扩展 `RuntimeDiagnosticsSelfTestState` | 增加 `BreakpointTest` 属性 |

### 验证标准
- 编译通过：`dotnet build Iwesun.Runtime.slnx -c Release`
- 断点注册表可查询
- 断点启用/禁用可切换
- `BreakIf` 在 `Enabled=false` 时零开销（不阻塞）
- 超时自动恢复

### 预计时间
2-3 天

---

## Phase 2：注册表构建 + 事件钩子系统

### 目标
运行时扫描程序集特性构建三大注册表，实现事件钩子 Attach/Detach。

### 任务清单

| # | 任务 | 文件 | 工作内容 |
|---|------|------|---------|
| 2.1 | 新增程序集特性类 | `DiagnosticAssemblyAttributes.cs` | `DiagnosticWatchPointAttribute`、`DiagnosticBreakpointAttribute`、`DiagnosticHookableEventAttribute`、`DiagnosticPipePrefixAttribute` |
| 2.2 | 新增 `RegistryBuilder` 类 | `RegistryBuilder.cs` | 扫描宿主程序集特性，构建三大注册表到内存 ConcurrentDictionary |
| 2.3 | 新增 `RuntimeDiagnosticHooks` 类 | `RuntimeDiagnosticHooks.cs` | 事件钩子：Attach/Detach、弱引用包装、GC 清理定时器、表达式树构建通用 handler |
| 2.4 | 新增钩子事件模型 | `RuntimeDiagnosticModels.cs` | `HookFiredEvent`、`HookAttachCommand`、`HookDetachCommand` |
| 2.5 | 扩展 `RuntimeDiagnosticHub` hook action | `RuntimeDiagnosticHub.cs` | `hook.list`、`hook.attach`、`hook.detach` |
| 2.6 | 新增 `registry` action | `RuntimeDiagnosticHub.cs` | `registry` 查询所有/分类注册表，返回 RegistrySnapshot |
| 2.7 | 注册表导出 | `RegistryBuilder.cs` | 导出 `diagnostic-registry.json` |
| 2.8 | 扩展 DI 注册 | `RuntimeDiagnosticsServiceCollectionExtensions.cs` | 注册 `RuntimeDiagnosticHooks`、`RegistryBuilder`，在 `UseRuntimeDiagnostics` 中调用构建 |

### 验证标准
- 编译通过
- 程序集特性正确扫描
- 注册表可通过管道查询
- 事件钩子可 Attach/Detach
- 弱引用目标 GC 回收后钩子自动清理
- 注册表可导出为 JSON

### 预计时间
2-3 天

---

## Phase 3：反射增强 + 多通道管道 + 终止控制

### 目标
增强反射路径导航，支持多通道管道，实现程序终止控制。

### 任务清单

| # | 任务 | 文件 | 工作内容 |
|---|------|------|---------|
| 3.1 | 扩展 `ReflectionRuntimeDiagnosticTarget` 路径导航 | `ReflectionRuntimeDiagnosticTarget.cs` | 新增 `navigate` action：dot 路径解析（属性/字段/索引器），逐级反射访问 |
| 3.2 | 扩展 `RuntimeOutput` 增加 `Watch` 方法 | `RuntimeOutput.cs` | 对象监视点：`Watch(id, obj, typeName)`，捕获对象快照到 FIFO |
| 3.3 | 新增监视点注册表 | `WatchPointRegistry.cs` | 独立注册表，关联 `DiagnosticSwitchboard` 输出点 |
| 3.4 | 多通道管道 | `RuntimeDiagnosticsMonitor.cs` 扩展 | 解析 `{host}.Control`、`{host}.Data`、`{host}.Events` |
| 3.5 | 新增 `shutdown` action | `DiagnosticSwitchboardTarget.cs` | 优雅关闭（`IHostApplicationLifetime`）/ 强制退出（`Environment.Exit`） |
| 3.6 | 扩展 CLI 断点控制命令 | `Iwesun.Runtime.Cli/Program.cs` | 新增 CLI 命令：`breakpoint`、`hook`、`registry`、`navigate`、`shutdown` |
| 3.7 | 断开自动清理 | `RuntimeDiagnosticsMonitor.cs` | CLI 断开 → Detach 所有钩子 → Resume 所有断点 |

### 验证标准
- 编译通过
- 路径导航正确（含索引器）
- 多通道管道各自独立工作
- 断点断开后自动恢复，不阻塞宿主进程
- 优雅关闭正常工作

### 预计时间
2-3 天

---

## Phase 4：完善 & 文档 & 集成测试

### 目标
完善边界情况，写文档，确保 DDNS Snap 宿主可正常集成。

### 任务清单

| # | 任务 | 工作内容 |
|---|------|---------|
| 4.1 | 更新 `RUNTIME_DIAGNOSTICS.md` | 补充新功能文档 |
| 4.2 | 更新 `AGENTS.md` | 更新 Runtime 仓库 AI 指令 |
| 4.3 | 更新 `runtime-diagnostics.instructions.md` | 更新 DDNS Snap 消费者说明 |
| 4.4 | 实现 `RuntimeDiagnosticsSelfTestState` 完整自测 | 增加断点/钩子自测 |
| 4.5 | 边界情况处理 | 并发安全、异常恢复、空值处理 |
| 4.6 | 验证 DDNS Snap 构建 | `dotnet build DdnsSnap.slnx -c Release` 通过 |
| 4.7 | 写快速测试指南 | 自测步骤文档 |

### 预计时间
1 天

---

## 总进度估时

| Phase | 内容 | 预计 |
|-------|------|------|
| Phase 1 | 断点系统 | 2-3 天 |
| Phase 2 | 注册表 + 事件钩子 | 2-3 天 |
| Phase 3 | 反射增强 + 多通道 + 终止 | 2-3 天 |
| Phase 4 | 完善 & 文档 | 1 天 |
| **总计** | | **7-10 天** |

---

## 立即开始：Phase 1

### 下一步

1. 创建 `RuntimeDiagnosticBreakpoints.cs`
2. 扩展 `RuntimeOutput.cs` 增加 `BreakIf`
3. 扩展 `RuntimeDiagnosticModels.cs` 增加断点模型
4. 扩展 `RuntimeDiagnosticHub.cs` 增加断点 action
5. 创建 `DiagnosticPipePrefix.cs`
6. 扩展 DI 注册
7. 编译验证 → 自测

---

> 现在开始 Phase 1 实施

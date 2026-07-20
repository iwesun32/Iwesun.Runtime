# Iwesun Runtime 完整设计文档

> **状态**: CURRENT | **最后更新**: 2026-07-20
> **源码参考**: `Iwesun.Runtime.Diagnostics/`, `Iwesun.Runtime.Cli/`, `Iwesun.Runtime.Data/`, `Iwesun.Runtime.WebView2/`
> **定位**: 本文是整个 Runtime 框架的权威设计入口，其他文档均为专题展开。

---

## 一、设计定位

Iwesun Runtime 是一套**寄生在业务程序里的非侵入性监控调试框架**，以 DLL 的形式发布，业务程序做最小注入接入，无需改造业务逻辑结构。

核心三原则：

1. **不干扰业务**：诊断关闭时开销接近零；任何诊断路径都不修改业务判断流程。
2. **统一入口**：所有业务侧调用归一到 `RuntimeInjector` 静态门面；所有管理侧命令归一到 CLI。
3. **可观测可控**：运行时所有状态可通过管道、文件、CLI 观测；可通过 CLI 或命令帧动态开关和调整。

---

## 二、功能分类与基础类构建

### 2.1 功能分类树

```
Iwesun.Runtime.Diagnostics
├── A. 输出监视（OutputMonitor）
│   ├── A1. TracePoint 追踪点
│   ├── A2. Watch 快照监视
│   └── A3. BreakIf / BreakIfNumbers 协作断点
│
├── B. 执行管理（ExecutionManagement）
│   ├── B1. 线程登记与状态（RThread）
│   ├── B2. 任务登记与状态（RTask）
│   ├── B3. 进程登记与状态（RProcess）
│   └── B4. 心跳与快照
│
├── C. 诊断路由（DiagnosticSwitchboard）
│   ├── C1. Section 过滤（粒度控制）
│   ├── C2. FIFO 队列（异步解耦）
│   ├── C3. Pipe 输出（实时管道）
│   └── C4. File 输出（独立写线程）
│
├── D. 注册表（Registry）
│   ├── D1. WatchPoint / Breakpoint / HookEvent（编译期静态注册）
│   ├── D2. RuntimeManagedRegistry（运行期动态登记）
│   └── D3. RuntimeFileRegistry（文件路径登记）
│
├── E. 状态管理（StateManager）
│   ├── E1. RuntimeStateManager（生命周期状态机）
│   └── E2. IRManagedState（业务细粒度状态扩展）
│
├── F. 管道服务（HubMonitor）
│   ├── F1. RuntimeDiagnosticHub（命名管道服务端）
│   └── F2. ReflectionRuntimeDiagnosticTarget（反射目标访问）
│
└── G. WebView2 运行时证据（WebRuntimeEvidence）
    ├── G1. WebRuntimeDomSnapshot（可恢复 DOM 真快照）
    ├── G2. WebRuntimeNetworkEvidenceSession（导航前 HTTP 输入记录）
    └── G3. WebRuntimePageEvidenceCapture（DOM/CSS/脚本/事件/CDP/MHTML 聚合证据）
```

### 2.3 WebView2 证据基础层

`Iwesun.Runtime.WebView2` 负责站点无关的浏览器运行时事实采集。HTTP 会话在导航前绑定现有 `CoreWebView2`；页面采集器随后按 checkpoint 输出完整页面证据包。宿主只提供浏览器、`IWebRuntimeScriptSession`、本机输出目录和受限选项，不复制事件订阅、固定脚本或 CDP 调用。

外部文档、JSON 和其他 HTTP 输入采用公共内容寻址管理：正文按 SHA-256 去重，响应记录具有稳定 ID；消费快照保存正向引用，公共目录保存反向引用。页面/UI 主锚点由业务宿主依据 DOM 制作流程决定，网络批次不构成新 UI 锚点。

公开面不接收任意 JavaScript 或 CDP 方法名，不包含豆包 XPath、页面版本、快捷键、XAML 或业务正文解释。API、输出文件和失败语义统一见 [FULL_PAGE_EVIDENCE_API.md](../Iwesun.Runtime.WebView2/docs/FULL_PAGE_EVIDENCE_API.md)，源码候选发布边界见 [WEBVIEW2_1.0.30_EVIDENCE_RELEASE.md](../Iwesun.Runtime.WebView2/docs/WEBVIEW2_1.0.30_EVIDENCE_RELEASE.md)。

### 2.2 功能基础类对照

线程管理是主控管理程序的精简版，去掉了 WatchPoint/Breakpoint（已由框架统一管理），保留执行状态核心：

| 主控管理程序 | 业务侧线程管理（精简版） |
|-------------|------------------------|
| `RuntimeExecutionManager` | `RThread` / `RTask` / `RProcess` |
| 统一登记注册 | 自动向 `RuntimeExecutionManager` 登记 |
| 全局心跳/快照 | 自身状态维护，通过管道可观测 |
| WatchPoint/Breakpoint 管理 | **省略**（由 `RuntimeOutput` + `DiagnosticSwitchboard` 统一处理） |
| 生命周期状态机 | 相同，使用 `IRManagedState` 粗粒度/细粒度分层 |

---

## 三、诊断三层架构（OutputMonitor 详解）

### 3.1 架构图

```
业务线程（发送端）
	│
	├─ RuntimeInjector.Output.TracePoint(section, kind, msg, payload)
	│         ↓
	│   [反射/布尔门：RuntimeOutputSwitch.Enabled]
	│   关闭 → 立即返回（接近零开销）
	│   开启 ↓
	│   [Section 过滤：Sections[section].Enabled]
	│   禁用 → 统计 Suppressed，返回
	│   启用 ↓
	│   PutFifo(json)
	│   FIFO 满（>= fifoDepth） → _fifoDropped++, return（不阻塞）
	│   FIFO 有空间 → Enqueue + FifoSignal.Release()
	│
	└─ 业务线程结束，不等待
─────────────────────────────────────────────────────────
主监控线程（FIFO 消费端）
	│
	├─ WaitAsync(FifoSignal)
	├─ TryDequeue → ProcessEnvelope()
	│   ├─ PipeOutputEnabled → _hub.Publish(diagnosticEvent)   // 实时推管道
	│   └─ FileOutputEnabled → EnqueueFileWrite(line)           // 投入文件队列，不阻塞
	│
─────────────────────────────────────────────────────────
文件写线程（专用后台线程）
	│
	├─ Wait(FileSignal)
	├─ TryDequeue from FileQueue
	│   └─ WriteFileLine()                                      // 含 retry，IO 隔离
	│
	└─ 队满策略：写 [DROPPED] +N messages 标记到文件，业务不等待
```

### 3.2 开关含义

| 开关 | 含义 | 关联逻辑 |
|------|------|---------|
| `GlobalEnabled` | 总开关 | 控制 UpdateInputEnabled |
| `InputEnabled` | 接收开关（派生）| `= PipeOutputEnabled \|\| FileOutputEnabled \|\| GlobalEnabled` |
| `PipeOutputEnabled` | 实时管道输出 | 发到 RuntimeDiagnosticHub → 命名管道 |
| `FileOutputEnabled` | 文件输出 | 发到文件写线程队列 |
| Section.Enabled | 分组过滤 | 每个 section 独立启用/禁用 |

### 3.3 FIFO 设计

- **深度**：默认 1024，可通过 `SetFifoDepth()` 调整
- **满队策略**：直接跳过（`_fifoDropped++`, `return false`），**不阻塞业务线程**
- **文件队列**：独立深度 512，满时写 `[DROPPED]` 标记到文件
- **退出**：`ShutdownAsync()` 先停文件线程（`Join 2s` 排空），再停 pump

### 3.4 文件输出

| 属性 | 选项 | 说明 |
|------|------|------|
| `FileWriteMode` | `Append / Overwrite / CreateNew` | CreateNew 自动添加时间戳后缀 |
| `DiagnosticFileFormat` | `CompactJson / PrettyJson / PlainText` | 格式化由 `DiagnosticFileOutputFilter` 处理 |
| 路径登记 | `RuntimeFileRegistry` | 模板路径 → 解析路径，可通过 `diagnostics.files` 查询 |

---

## 四、逻辑安全（注入无害性保障）

### 4.1 调用方无害性

所有注入路径遵循以下保障：

```
1. 关闭时立即返回       - RuntimeOutputSwitch.Enabled 是一个 volatile bool
2. 不抛异常             - try/catch 包裹所有诊断路径；异常内部静默消化
3. 不修改业务变量       - 注入点只读取 payload，不写回业务对象
4. 不阻塞业务线程       - FIFO 满直接 return，不 sleep，不 wait
5. 不影响业务返回值     - 所有注入点均为 void 调用，无 out/ref 参数
```

### 4.2 框架内部安全

```
文件 IO         → 独立后台线程，retry 在文件线程内，不影响 pump
管道 IO         → pump 线程（Task.Run），不占用业务线程池
FIFO 并发       → ConcurrentQueue + Interlocked 计数，无显式锁
Section 并发    → ConcurrentDictionary，读写安全
文件写并发      → FileWriteLock（静态 object lock），单文件串行写
Shutdown        → CancellationTokenSource + Join，有序清理
```

### 4.3 状态枚举分层

粗粒度由框架统一，细粒度由业务扩展：

```
粗粒度（框架提供）        细粒度（业务扩展）
Working                → IRManagedState.SetDetail("status", "downloading")
Stopping               → IRManagedState.TransitionTo(BizState.Flushing)
Stopped                → IRManagedState.TransitionTo(BizState.Done)
```

---

## 五、注入界面（快速接入指南）

> **已迁移**：业务接入的当前权威步骤见 [IWESUN_RUNTIME_USER_GUIDE.md](IWESUN_RUNTIME_USER_GUIDE.md)。本章下方的旧片段只作为设计演进背景，不得作为新宿主的复制模板。

### 5.1 宿主启动（当前标准）

```csharp
// Program.cs 启动入口
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddRuntimeDiagnostics();
builder.Services.Start(runtimeDirectory);
using var host = builder.Build();
host.Services.Activate(Assembly.GetExecutingAssembly());
await host.RunAsync();
```

### 5.2 编译期标注（AssemblyInfo.cs）

在宿主程序集的 `AssemblyInfo.cs` 声明：

```csharp
// 管道名称前缀（区分多实例部署）
[assembly: DiagnosticPipePrefix("MyApp")]

// 追踪点（静态注册到 WatchPoint 表）
[assembly: DiagnosticWatchPoint("myapp.worker.tick", "worker", "trace",
	"Worker loop tick.", "Worker.cs:DoWork")]

// 断点（逻辑断点，可通过 CLI 激活）
[assembly: DiagnosticBreakpoint("myapp.worker.pause", "worker",
	"Pause worker before processing.", "Worker.cs:DoWork")]

// 数值断点（用于阈值监控）
[assembly: DiagnosticNumericBreakpoint("myapp.queue.depth", "worker",
	"Queue depth numeric breakpoint.", "Worker.cs:EnqueueItem")]

// 可挂钩事件
[assembly: DiagnosticHookableEvent("myapp.worker.completed", "worker",
	"Raised when worker completes a cycle.", "Worker.cs:DoWork")]

// 文件输出默认配置（可被 JSON 配置覆盖）
[assembly: DiagnosticFileOutput("logs/myapp-diag.log",
	FileWriteMode.CreateNew, DiagnosticFileFormat.PlainText)]
```

### 5.3 输出注入（业务代码内）

`RuntimeInjector` 提供平坦静态方法，不用嵌套类前缀：

```csharp
// 追踪点（最常用）
RuntimeInjector.Output(
	outputPointId: "myapp.worker.tick",   // 与编译期 WatchPoint id 对应
	section:       "worker",
	kind:          "trace",
	message:       "Worker tick.",
	payload:       new { Elapsed = elapsed, QueueDepth = queue.Count });

// Watch 快照（观测对象整体状态）
RuntimeInjector.Watch(
	watchPointId: "myapp.worker.session",
	target:       stateSnapshot,
	typeName:     nameof(WorkerStateSnapshot));

// 协作断点（命中后挂起，等待 CLI resume）
await RuntimeInjector.Break(
	breakpointId: "myapp.worker.pause",
	condition:    () => queue.Count > MaxQueueDepth);

// 数值断点（两个数值，通过配置绑定比较条件）
await RuntimeOutput.BreakIfNumbers(
	breakpointId: "myapp.queue.depth",
	value1:       queue.Count,
	value2:       MaxQueueDepth);
```

### 5.4 线程/任务注入

`RThread`/`RTask`/`RProcess` 从 `RuntimeInjectionContext` 自动取得 `RuntimeExecutionManager`，无需手动传入：

```csharp
// 使用 RThread 包装（自动登记到 RuntimeExecutionManager）
// 方式一：通过 RuntimeInjector.CreateThread 工厂（不立即启动）
var worker = RuntimeInjector.CreateThread(
	start:           WorkerLoop,
	unitId:          "myapp.worker",
	name:            "MyApp Worker",
	lifetime:        RuntimeExecutionLifetime.Static,
	kind:            RuntimeThreadKind.Worker,
	startImmediately: false);
worker.Start();

// 方式二：直接 new
var worker2 = new RThread(
	start:    WorkerLoop,
	unitId:   "myapp.worker",
	name:     "MyApp Worker",
	lifetime: RuntimeExecutionLifetime.Static,
	kind:     RuntimeThreadKind.Worker);
worker2.Start();

void WorkerLoop()
{
	// 业务循环（通过 State 属性观测粗粒度状态）
}

// 使用 RTask 包装
var task = RuntimeInjector.CreateTask(
	action:           () => DoStartupWork(),
	unitId:           "myapp.startup-task",
	lifetime:         RuntimeExecutionLifetime.Dynamic,
	startImmediately: false);
task.Start(TaskScheduler.Default);
```

### 5.5 完整宿主模板（标准主程序模板）

```csharp
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddRuntimeDiagnostics(runtimeDirectory);
using var host = builder.Build();

var execution = host.Services.GetRequiredService<RuntimeExecutionManager>();
var stateManager = host.Services.GetRequiredService<RuntimeStateManager>();

host.Services.UseRuntimeDiagnostics();
host.Services.BuildDiagnosticRegistries(Assembly.GetExecutingAssembly());

// 注册主线程（使用 RuntimeInjector.Thread 便捷注册）
var mainThread = RuntimeInjector.Thread(
	execution:  execution,
	id:         "myapp.main",
	name:       "Main",
	lifetime:   RuntimeExecutionLifetime.Static,
	kind:       RuntimeThreadKind.Main);

stateManager.SetState(RuntimeLifecycleState.Running);

// 启动业务
await RunBusinessAsync(host, execution, cts.Token);

// 主控协调退出；业务守护程序负责清理并从 RuntimeManagedRegistry 反登记。
var shutdown = host.Services.GetRequiredService<RuntimeShutdownCoordinator>();
var result = await shutdown.ShutdownAsync(TimeSpan.FromSeconds(10));
Environment.ExitCode = result.ExitCode;
await DiagnosticSwitchboard.ShutdownAsync();
```

---

## 六、JSON 指令格式规范

> 用户可复制的当前帧样例和长度前缀规范见 [IWESUN_RUNTIME_USER_GUIDE.md](IWESUN_RUNTIME_USER_GUIDE.md#5-json-与-cli-清单)。

### 6.1 设计原则

- JSON 帧是**指令帧**，数据是指令的附加；不是纯数据传输格式。
- 每帧都有完整的元信息（类型、目标、时间戳、进程/线程上下文）。
- 帧格式版本化，解释器按 `v` 字段分发处理。

### 6.2 基础帧结构（RuntimeDiagnosticFrame）

实际协议版本为 `rtdiag/2.0`，帧采用嵌套结构分离 Header / Command / Status / Data / Meta：

```json
{
  "header": {
    "schema":      "rtdiag/2.0",
    "frameType":   "request | response | event | error",
    "category":    "query | instruction | stream | system",
    "operation":   "get | set | invoke | subscribe",
    "requestId":   "uuid",
    "correlationId": "uuid",
    "timestamp":   "2026-07-11T08:00:00.000Z",
    "source":      "cli",
    "destination": "runtime"
  },
  "command": {
    "domain":  "switchboard | breakpoints | registry | pipes | runtime",
    "target":  "diagnostics.switchboard",
    "action":  "GetSnapshot",
    "member":  null,
    "args":    { }
  },
  "status": {
    "ok":       true,
    "code":     "OK",
    "message":  "",
    "retryable": false
  },
  "data": { },
  "meta": {
    "durationMs": 0,
    "page":       { },
    "traceId":    null
  }
}
```

| 节 | 说明 |
|----|------|
| `header` | 路由与元信息：schema 版本、帧类型、分类、操作语义、ID、时间戳 |
| `command` | 指令路由：domain（分类）、target（Hub 目标 ID）、action、成员名、参数 |
| `status` | 回复状态：ok、code、message、是否可重试 |
| `data` | 回复数据体（`JsonElement`，按 action 定义） |
| `meta` | 附加元：耗时、分页信息、traceId |

> CLI 发送方只填 `header` + `command`；`status`/`data`/`meta` 由服务端回复填充。

### 6.3 常用指令示例

**查询快照（CLI → Runtime）**

```json
{
  "header": { "schema": "rtdiag/2.0", "frameType": "request",
			   "category": "query", "operation": "get",
			   "requestId": "req-001", "source": "cli", "destination": "runtime" },
  "command": { "domain": "switchboard", "target": "diagnostics.switchboard",
				"action": "GetSnapshot" }
}
```

**开关 Section（CLI → Runtime）**

```json
{
  "header": { "schema": "rtdiag/2.0", "frameType": "request",
			   "category": "instruction", "operation": "set", "source": "cli" },
  "command": { "domain": "switchboard", "target": "diagnostics.switchboard",
				"action": "SetSection",
				"args": { "section": "worker", "enabled": true } }
}
```

**激活断点（CLI → Runtime）**

```json
{
  "header": { "schema": "rtdiag/2.0", "frameType": "request",
			   "category": "instruction", "operation": "invoke", "source": "cli" },
  "command": { "domain": "breakpoints", "target": "diagnostics.breakpoints",
				"action": "Enable",
				"args": { "id": "myapp.worker.pause" } }
}
```

**回复帧（Runtime → CLI）**

```json
{
  "header": { "schema": "rtdiag/2.0", "frameType": "response",
			   "category": "query", "operation": "get",
			   "requestId": "req-001", "source": "runtime", "destination": "cli" },
  "command": { "domain": "switchboard", "target": "diagnostics.switchboard",
				"action": "GetSnapshot" },
  "status": { "ok": true, "code": "OK", "message": "" },
  "data": { "globalEnabled": true, "sections": [ ] },
  "meta": { "durationMs": 3 }
}
```

### 6.4 管道传输格式（Wire Format）

命名管道上使用带长度前缀的帧：

```
[4 bytes: payload length (little-endian int32)] [N bytes: UTF-8 JSON]
```

CLI 负责帧的组装与发送；业务侧不直接操作管道。

### 6.5 目标 ID 注册表

| 目标 ID | 类型 | 说明 |
|---------|------|------|
| `diagnostics.switchboard` | 反射/直接 | 开关板：GetSnapshot / SetGlobal / SetSection / SetFileOutput |
| `diagnostics.pipes` | 注册表 | 管道注册表：list / get / register / release |
| `diagnostics.files` | 注册表 | 文件注册表：list / get / register / release / purge |
| `diagnostics.breakpoints` | 断点 | Enable / Disable / Resume / List |
| `runtime.execution` | 反射 | Snapshot / RegisterThread / SetThreadState / Heartbeat |
| `runtime.managed` | 反射 | processes / threads / tasks 列表 |
| `runtime.root` | 根容器 | snapshot / refresh / table / get / query / prefix |
| `service.root-snapshot` | 宿主 | GetSnapshot |

---

## 七、CLI 命令格式规范

> **v2 已废弃，v3 已生效**：当前路由权威文件为 `RuntimeCliSystemConfig.json`，命令帮助元数据位于 `RuntimeCliSystemMetadata.json`；旧 `sw.*`、`bp.*`、`reg.*` 不再是有效命令。

### 7.1 设计定位

CLI（`iwrt`）是一个**通用命令解释器**：

- 内置指令格式由框架发布（基础 canonical 指令）
- 用户通过 `--user-config=PATH` 加载 v3 增量配置，扩展别名、命令和 `composites`
- CLI 不含业务逻辑，职责是"发令"，不是"解释业务"

```
用户输入 CLI 命令
	→ CommandParser（仿 PowerShell 语法解析）
	→ 语义命令（target + action + params）
	→ RuntimeDiagnosticFrame（JSON）
	→ 命名管道 → RuntimeDiagnosticHub
	→ 反射目标/适配器动作/开关板
```

### 7.2 命令语法格式（仿 PowerShell）

```
iwrt <动词>-<名词> [-参数名 <值>] [-开关]
iwrt <dot.style.command> [参数...]
```

基本规则：

| 元素 | 格式 | 示例 |
|------|------|------|
| 命令名 | `动词-名词` 或 `dot.style` | `Get-Snapshot` / `sw.status` |
| 具名参数 | `-Name Value` | `-Section worker` |
| 开关参数 | `-Switch` | `-Enabled` |
| 管道符 | `\|` | `Get-Threads \| Format-Table` |
| 变量 | `$name` | `$pipe = "MyApp.Diag"` |
| 别名 | JSON 配置定义 | `status` → `Get-Status` |

### 7.3 内置基础指令集

#### 内置指令以 dot-style 为主，Verb-Noun 为可配置别名

命令名格式：`namespace.verb` 或直接 `verb`，参数用 `-param value` 或 `param=value`：

```powershell
# 帮助 / 状态
iwrt help
iwrt host.info              # 宿主信息
iwrt status                 # 连接状态（别名，JSON 配置定义）
iwrt monitor                # 实时监控流（别名）

# 开关板
iwrt switchboard.get
iwrt switchboard.enable
iwrt switchboard.enable worker
iwrt switchboard.point.list

# 注册表
iwrt registry.list
iwrt switchboard.point.list "" worker
iwrt breakpoint.list
iwrt hook.list

# 断点
iwrt breakpoint.list
iwrt breakpoint.enable myapp.worker.pause
iwrt breakpoint.disable myapp.worker.pause
iwrt breakpoint.resume myapp.worker.pause
iwrt breakpoint.list-numeric
iwrt breakpoint.set-numeric-threshold numeric.default.threshold gt 7

# 执行管理
iwrt thread.list
iwrt task.list
iwrt process.list

# 进程反射读写
iwrt process.mem.get  process.abc123 UnitId
iwrt process.mem.nav  process.abc123 StartInfo.FileName
iwrt process.mem.set  process.abc123 StateName Running

# 生命周期
iwrt lifecycle.status
iwrt lifecycle.set -state Pause
iwrt lifecycle.shutdown

# 管道注册表
iwrt pipe.list
iwrt pipe.get  -name MyApp.Worker
iwrt pipe.register -name MyApp.Worker -module worker
iwrt pipe.release  -name MyApp.Worker

# 文件注册表
iwrt file.list
iwrt file.get    -id runtime.diagnostics.output
iwrt file.release -id runtime.diagnostics.output
iwrt file.purge

# WebRuntime
iwrt web.status
iwrt web.invoke -module web.runtime -command reload
```

> 以上为 canonical 形式；用户可在 v3 用户增量配置的 `extensions.extend` 中追加别名。

### 7.4 JSON 配置扩展（CLI v3）

配置文件路径优先级：
1. `--config=<path>`
2. `<config-dir>/RuntimeCliSystemConfig.json`
3. `<exe-dir>/RuntimeCliSystemConfig.json`
4. 当前目录
5. 内嵌默认配置

```json
{
  "version": 2,
  "pipes": {
	"diagnostics": "MyApp.RuntimeDiagnostics"
  },
  "aliases": [
	{ "alias": "status",   "command": "Get-Status" },
	{ "alias": "monitor",  "command": "Watch-Output" },
	{ "alias": "sw",       "command": "Get-SwitchboardSnapshot" },
	{ "alias": "threads",  "command": "Get-Threads" }
  ],
  "compositeCommands": [
	{
	  "name": "diag-on",
	  "description": "快速开启诊断（全局+worker section）",
	  "steps": [
		"Set-Global -Enabled",
		"Set-Section -Name worker -Enabled",
		"Get-SwitchboardSnapshot"
	  ]
	}
  ],
  "customCommands": [
	{
	  "name": "My-WorkerStatus",
	  "description": "查看 worker 线程状态",
	  "frame": {
		"target": "runtime.execution",
		"action": "Snapshot"
	  }
	}
  ]
}
```

### 7.5 命令参数覆盖优先级

```
显式命令行参数 > JSON 配置 > 内置默认值
```

---

## 八、关联文档索引

| 文档 | 说明 |
|------|------|
| `docs/RUNTIME_DIAGNOSTICS.md` | 诊断系统详细 API 参考 |
| `docs/RUNTIME_ROOT_DATA_STRUCTURE.md` | RuntimeRoot 数据结构与 T01~T10 表定义 |
| `docs/IWESUN_RUNTIME_CLI.md` | CLI 命令完整列表与示例 |
| `docs/UNIFIED_INTERFACE.md` | 术语与命名约定 |
| `docs/05-runtime-tooling/INJECTOR_STANDARDIZATION.md` | 注入器六大类详细说明 |
| `docs/05-runtime-tooling/THREAD_TASK_MANAGEMENT.md` | RThread/RTask/RProcess 详细用法 |
| `Iwesun.Runtime.SampleHost/Program.cs` | 完整宿主接入示例 |

---

> 本文档是活文档，随实现演进持续更新。修改时同步更新顶部最后更新日期。

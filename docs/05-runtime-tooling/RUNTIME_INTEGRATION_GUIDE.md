# Runtime 注入与扩展接口使用说明（发布版）

> 状态：CURRENT  
> 适用版本：`Iwesun.Runtime.Diagnostics` + `Iwesun.Runtime.Cli`（Unified V2）

本文对应以下交付目标：

1. 用户主程序启动页/结束页替换模板  
2. 进程/线程/任务创建方式与返回类型统一  
3. 监视输出注入器格式  
4. 断点注入器格式  
5. 用户业务状态管理接口（关机准备、细分状态、子任务状态）  
6. 完整接口 JSON 样板  
7. 完整 CLI 使用与配置扩展（别名、复合指令）  
8. 发布前全量检查清单

---

## 1) 启动页与结束页替换模板

发布目录自带模板文件（由 `Iwesun.Runtime.SampleHost` 打包）：

- `templates/RuntimeHost.Startup.Minimal.Template.cs.txt`
- `templates/RuntimeHost.DiagnosticsExamples.Template.cs.txt`
- `templates/RuntimeHost.ManagedWorker.Template.cs.txt`
- `templates/RuntimeHost.Shutdown.Template.cs.txt`

集成规则：

- 启动页最小接入顺序：
  1. `builder.Services.Start(runtimeDirectory)`
  2. `host.Services.Activate(Assembly.GetExecutingAssembly())`
  3. 注册静态线程/任务和反射目标
- 结束页统一收口：
  - 守护循环同时查询全局 Stop/Exit 状态并消费本单元 FIFO 的 Stop 命令
  - 在 `finally` 中执行持久化/清理、更新本单元状态并调用 `RuntimeManagedRegistry.Unregister(...)`
  - 主控统一调用 `RuntimeShutdownCoordinator.ShutdownAsync(...)`；登记表清空返回 `0`，倒计时超时返回 `124`

---

## 2) 进程/线程/任务创建方式与返回类型（统一）

统一使用 `RuntimeInjector` 的创建 API：

- `RuntimeInjector.CreateProcess(...)` → 返回 `RProcess`
- `RuntimeInjector.CreateThread(...)` → 返回 `RThread`
- `RuntimeInjector.CreateTask(...)` → 返回 `RTask`

说明：

- `RProcess / RThread / RTask` 都内置了受管状态 `IRManagedState State`
- 都可使用：
  - `TransitionTo(...) / TryTransitionTo(...)`
  - `SetDetail(...) / TryGetDetail(...)`
- `RThread` 支持 guardian 停机事件：
  - `ExitHandlingRequested`
  - `ExitCompleted`

---

## 3) 监视输出注入器书写格式

### 单点注入定义

“单点注入”不是“整个程序只允许一个诊断点”，也不是把声明统一搬到 `Program.cs`。它指一个诊断功能在业务现场以一个连续代码段完成：诊断标识、启用条件、采集内容和输出/断点调用放在一起，不要求在程序集特性、启动注册、业务实现等多个位置分别补代码。

它应像就地写一条 `print` 一样独立：需要观察哪段业务，就在那段业务附近直接加入完整注入块；删除该块即可移除这个诊断功能，不影响业务结构。不同功能可以各有自己的单点注入块。

程序集特性和启动期注册属于可选的“预编译目录/反射暴露”能力，不应成为普通输出、监视记录或断点跟踪的强制第二修改点。只有确实需要编译初始值、CLI 启动前发现、反射白名单或事件类型登记时，才增加相应元数据。

标准格式：

```csharp
RuntimeInjector.Output(
    outputPointId: "your.output.point",
    section: "your-section",
    kind: "trace",
    message: "your message",
    payload: yourPayload);
```

建议：

- `outputPointId` 使用稳定、可检索命名（dot-style）
- `section` 固定到业务域，便于 CLI 过滤
- `payload` 使用结构化对象
- 同一功能所需的判断、上下文构造和 `RuntimeInjector.Output` 调用保持在一个连续代码段内

---

## 4) 断点注入器书写格式

断点子系统只在 `DEBUG` 构建中编译和装配。Release 不包含断点实现类型，不注册断点服务、不扫描断点与数值断点特性、不暴露 `diagnostics.breakpoints` 命令目标，也不生成 RuntimeRoot 的断点登记表和断点运行表。业务代码中的断点调用本身由 `#if DEBUG` 隔离。

Release 继续保留可供业务逻辑使用的诊断能力：结构化输出、`ILogger` 桥接、反射白名单、事件钩子、命名管道、运行状态/执行表、文件记录和安全退出。

标准格式：

```csharp
await RuntimeInjector.Break(
    breakpointId: "your.breakpoint.id",
    condition: () => shouldBreak,
    context: yourContext);
```

建议：

- `breakpointId` 固定且可回放
- `condition` 避免副作用
- `context` 传递最小必要上下文
- 同一断点的条件和调用就地集中，不把条件计算、登记和触发散布到多个业务文件

---

## 5) 用户业务状态管理接口

### 固定值指令 FIFO

- 全局主控收件 FIFO 固定深度为 128；每个登记进程、线程、任务或业务单元的收件 FIFO 固定深度为 64。
- 状态转换成功后发送固定 48 字节指令，`Value` 为 `RuntimeState.Code`；主控发向单元时，`Value` 为 `RuntimeManagedCommandKind` 整数值。
- FIFO 只携带整数、标志和 ticks。字符串、异常、状态详情和业务对象通过命名管道读取。
- FIFO 使用匿名共享映射和匿名唤醒事件。`RProcess` 通过 `DuplicateHandle` 把句柄复制到受管子进程，再经子进程诊断管道完成附着；不使用命名 MMF。
- 指令投递结果为 `Sent`、`TargetNotFound`、`QueueFull` 或 `TargetDisposed`，调用方不得把后三种状态视为成功。

### 全局状态（主控）

- `lifecycle.get`
- `lifecycle.set <name> [countdownMs]`
- `lifecycle.shutdown [countdownMs]`
- `lifecycle.status`
- `lifecycle.broadcast <command>`

### 单元状态（进程/线程/任务）

- `unit.state.get <unitId>`
- `unit.state.history <unitId> [count]`
- `unit.state.transition <unitId> <stateName>`
- `unit.state.trytransition <unitId> <stateName>`
- `unit.state.subtask.append <unitId> <stateName>`
- `unit.state.subtask.tryappend <unitId> <stateName>`

### 业务编排建议

- 关机准备：
  1. 协调器发布全局 Stop（含倒计时）
  2. 协调器向每个登记单元发送 Stop/Wakeup
  3. 各守护程序主动查询总状态或消费 FIFO，执行清理、更新状态并反登记
  4. 协调器等待进程、线程、任务登记表清空；正常返回 `0`，超时返回 `124`
- 细分状态：
  - 使用 `TransitionTo/TryTransitionTo` 写入业务状态
- 子任务状态：
  - 使用 `AppendSubTaskState/TryAppendSubTaskState` 维护有序状态轨迹

---

## 6) 完整接口 JSON 样板

发布目录包含：

- `templates/RuntimeIntegration.Interface.Template.json`

该样板覆盖：

- host 启动配置
- 静态线程/任务注册结构
- managed 状态与停机参数
- 输出/断点注入器格式
- 反射目标访问白名单
- 统一创建 API 映射

---

## 7) CLI 使用说明、配置 JSON、别名与复合指令扩展

发布目录包含：

- `docs/IWESUN_RUNTIME_CLI.md`
- `config/RuntimeCliSystemConfig.json`
- `config/RuntimeCliSystemMetadata.json`
- `config/RuntimeCliUserConfig.example.json`
- `templates/Iwesun.Runtime.Cli.commands.custom.sample.json`

扩展规则：

- 系统 canonical 命令使用 dot-style（如 `unit.state.transition`）
- 用户可在配置中新增：
  - `aliases`（别名）
  - `compositeCommands`（复合指令）
  - 自定义 `baseCommands`

---

## 8) 发布前全方位检查清单

- 代码构建：
  - `dotnet build Iwesun.Runtime.slnx -c Release -p:UseSharedCompilation=false`
- 单元/功能检查：
  - `dotnet test Iwesun.Runtime.slnx -c Release --no-restore`
  - `dotnet run --project Iwesun.Runtime.FunctionalTests/Iwesun.Runtime.FunctionalTests.csproj -- --scenario cli`
- 发布产物检查：
  - 模板文件是否进入发布目录（`templates/`）
  - CLI 文档与配置是否进入发布目录（`docs/` + `cli/`）
  - 样板命令是否可运行（`host.info`, `lifecycle.status`, `unit.state.get`, `pipe.list`）

满足以上项后，可判定进入工程发布状态。

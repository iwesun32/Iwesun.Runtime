# Iwesun Runtime CLI Server 标准框架审计意见书

日期：2026-07-12  
审计对象：Iwesun Runtime CLI v3、Runtime Host 标准启动框架、Windows Service 生命周期、SampleHost 模板及发布文档  
审计性质：架构合理性与交付可用性审查，不包含本轮源码整改

正式答复与整改计划：[`CLI_Server标准框架审计综合答复与整改方案.md`](CLI_Server标准框架审计综合答复与整改方案.md)

## 一、总体结论

本轮重大更新的总体方向合理，可以作为业务 Server 接入 Runtime 的标准基础。

新版已经把过去分散的诊断初始化、CLI 命令解析、管道寻址和退出处理收敛为四个明确边界：

1. 宿主统一通过 `RuntimeHostTemplate.Start` 和 `Activate` 接入。
2. CLI 文本命令由 v3 catalog 编译为标准 `RuntimeDiagnosticFrame`。
3. RuntimeDiagnostics 与 Management、WebRuntime 等业务管道物理隔离，但统一登记、寻址和代理。
4. SCM Stop、系统 Shutdown 和 CLI shutdown 汇聚到 Runtime 协调退出链。

这些边界符合公共 Runtime 平台定位，也适合后续 AIGateway、DDNS Snap 和其他 Windows Server 宿主复用。

当前主要问题不在总体架构，而在“标准框架是否能被业务方安全、准确地直接采用”。发布模板、帮助信息、Wakeup 示例和重复初始化语义仍需补强。建议在下一次正式发布前完成本文 P0/P1 项。

## 二、审计依据与已验证事实

本轮针对以下权威材料进行了核对：

- `Iwesun.Runtime.Diagnostics/RuntimeHostTemplate.cs`
- `Iwesun.Runtime.SampleHost/templates/RuntimeHost.Startup.Template.cs.txt`
- `Iwesun.Runtime.SampleHost/templates/RuntimeHost.Shutdown.Template.cs.txt`
- 安装版 `IWESUN_RUNTIME_USER_GUIDE.md`
- 安装版 `IWESUN_RUNTIME_CLI.md`
- 安装版 `Iwesun.Runtime.Cli.exe --help`

实际验证结果：

- `dotnet build Iwesun.Runtime.slnx -c Debug --no-restore` 成功。
- Debug 构建为 0 警告、0 错误。
- 安装版 CLI 能读取 v3 catalog 并生成命令帮助。
- 安装版已公开 `runtime.inspect` composite。
- CLI 帮助实际包含 `--user-config=PATH`。
- `RuntimeHostTemplate` 已提供普通 Host、显式 Windows Service 和配置式 Windows Service 三类入口。
- Windows Service Lifetime 仅在真实 Windows Service 上下文中替换。
- CLI v3 已提供明确的本地错误结构和退出码分类。

说明：按场景类名执行 FunctionalTests 过滤时没有产生标准测试摘要，因此本意见书不把该次命令退出视为功能场景测试通过。功能场景覆盖情况仍以 Runtime 项目正式测试入口和已有审计记录为准。

## 三、合理且应保留的设计

### 3.1 固定宿主入口

`Start` 负责 DI 注册，`Activate` 负责启动 Diagnostics 和建立宿主登记表，这一拆分符合 Generic Host 生命周期。业务代码不应再直接组合：

- `AddRuntimeDiagnostics`
- `UseRuntimeDiagnostics`
- `BuildDiagnosticRegistries`

建议继续把 `Start/Activate` 作为唯一推荐入口。

### 3.2 Windows Service 平台边界

Runtime 提供 `StartWindowsService` 和 `StartConfiguredWindowsService`，但不预设业务 ServiceName，这是正确边界。

SCM Stop、系统关机和 CLI shutdown 共用协调退出任务，也避免业务宿主重复实现第二套 Stop 状态机。

### 3.3 CLI catalog 权威性

CLI 文本不是协议，`RuntimeCliSystemConfig.json` 才是权威路由配置，这一原则合理。每条命令显式声明 endpoint、category、operation、domain、target、action 和 typed parameters，可避免根据命令名猜协议。

### 3.4 管道登记与代理

RuntimeDiagnostics、Management、WebRuntime 使用不同物理管道，同时由 `RuntimePipeRegistry` 统一管理 `Name / RequestedPipeName / ResolvedPipeName`，再由代理按租约转发标准 Frame。该设计同时满足隔离和统一管理要求，应保持不变。

### 3.5 Composite 边界

Composite 被编译为单个 `rtdiag/3.0` batch，一次连接执行多个已登记命令，同时禁止脚本文本、循环和任意代码执行。这个能力足以覆盖运维速查，不应重新扩张为脚本工作流引擎。

### 3.6 反射安全边界

`reflection.get` 只读白名单成员；`reflection.invoke` 仅在 Debug Diagnostics 中调用宿主明确登记的无参数方法；Release 不编译 invoke 执行路由。该安全模型合理。

## 四、必须整改事项

## P0-1 发布 Startup 模板必须能够直接编译

### 当前问题

发布模板仍包含业务占位符：

- `YourHostedWorker`
- `yourStateObject`
- `YourCompany.YourApp`
- 固定示例 target ID

其中 `yourStateObject` 在模板内没有定义。用户手册要求用模板整体替换 Program.cs，并强调只迁移业务 DI 注册；严格照做会导致编译失败，或迫使用户同时猜测模板其他替换点。

### 整改建议

将模板拆为两个文件：

1. `RuntimeHost.Startup.Minimal.Template.cs.txt`：复制后只需修改明确标记的 ServiceName、管道前缀和业务注册区，并且能够编译。
2. `RuntimeHost.DiagnosticsExamples.Template.cs.txt`：保存可选的 Data、Thread、Task 和 Reflection 示例。

所有占位符必须采用一致、可搜索的标记，例如 `__PRODUCT_NAME__`，并在文件顶部列出替换清单。未定义业务变量不得出现在最小模板的可编译语句中。

### 验收

- 将最小模板复制到空白 .NET 10 Worker 项目。
- 只按模板顶部替换清单修改。
- Debug 和 Release 均能构建。
- 启动后 `host.info`、`lifecycle.status` 可用。

## P0-2 区分真实执行对象与描述性登记

### 当前问题

Startup 模板通过 `RuntimeInjector.Thread/Task` 注册静态记录，但 HostedWorker 的实际执行仍来自 `BackgroundService`。接入者可能把“登记了一条 Thread/Task 描述”误认为实际执行链已经由 `RThread/RTask` 管理。

### 整改建议

文档和示例必须明确区分：

- `RuntimeInjector.Thread/Task`：描述性或外部对象登记，不会创建执行对象。
- `RuntimeInjector.CreateThread/CreateTask`、`RThread/RTask`：实际受管执行对象。
- `BackgroundService.ExecuteAsync`：Generic Host 管理的实际任务，应使用专门的 HostedService 状态登记方式，不应伪装成独立 RTask。

三套 Server 示例中应至少有一套真实使用 `RTask`，并验证 Stop、完成、异常和反登记。

### 验收

- CLI `thread.list/task.list` 返回内容能够区分 descriptive 与 managed execution。
- 真实任务结束前不得反登记。
- Dispose 或超时不得把仍在运行的对象报告为已停止。

## P0-3 Server Worker 模板必须完整展示 Stop/Wakeup

### 当前问题

Shutdown 模板只消费 `RuntimeManagedCommandKind.Stop`，没有处理 Wakeup。示例使用固定 `Task.Delay(1s)`，不能体现长等待 Server 如何立即响应 Wakeup 或 Stop。

### 整改建议

标准 Worker 示例应同时覆盖：

- `IsGlobalStopOrExitRequested`
- FIFO `Stop`
- FIFO `Wakeup`
- 可取消、可唤醒等待
- `CleanupRequested`
- 实际结束后的状态更新与反登记

Wakeup 只唤醒当前等待，不等同于 Stop；CLI断开也不得改变 Stop/Wakeup 状态。

### 验收

- 进入长等待后发送 Wakeup，Worker 立即继续下一轮且不退出。
- 发送 Stop 后 Worker 退出。
- SCM Stop 与 CLI shutdown 走同一幂等协调链。
- 退出完成后才从 Registry 移除。

## P1-1 CLI 帮助必须包含摘要和关键参数

### 当前问题

安装版 `iwrt --help` 中大多数命令只显示名字，没有摘要。当前只有少数 composite 显示清晰说明。

对于 Server 运维工具，仅有命令名不足以支持现场使用，也不能证明 catalog 定义完整。

### 整改建议

标准 catalog 中每条命令必须填写：

- `summary`
- 参数名、类型、是否必需
- 危险级别或是否会修改状态
- Debug/Release 可用性

`iwrt <command> --help` 应显示该命令的 endpoint、参数和简短示例。

### 验收

- `iwrt --help` 中所有内置命令均有非空摘要。
- `iwrt lifecycle.shutdown --help` 明确显示 destructive/confirmation 参数。
- `reflection.invoke` 明确提示 Debug-only 和白名单限制。

## P1-2 明确 Start/Activate 重复调用与多 Host 语义

### 当前问题

`RuntimeHostTemplate.Start` 和 `Activate` 当前公开接口没有在签名或手册中说明重复调用行为。`RuntimeInjectionContext` 使用进程静态槽位保存 Execution、Managed 和 Hub，第二个 Host 激活时会覆盖前一个 Host 的上下文。

### 整改建议

必须明确选择并实现一种策略：

1. 单进程单 Runtime Host：重复 Activate 明确抛出结构化异常，并记录首次 Host 身份。
2. 支持多 Host：把静态上下文改为 Host/Scope 隔离。

不建议保留“后激活者静默覆盖前者”的隐式语义。

### 验收

- 重复 `Start` 的行为有单元或功能验证。
- 同一 provider 重复 `Activate` 的行为有验证。
- 不同 provider 依次 `Activate` 不得静默污染已有 Host。

## P1-3 用户手册调用格式补齐 `--user-config`

### 当前问题

安装版 CLI 帮助包含：

```text
--user-config=PATH
```

但 CLI 手册“调用格式”主语法行只列出 `--pipe/--config/--timeout-ms`，与后文和实际帮助不完全一致。

### 整改建议

统一为：

```text
iwrt [--pipe=NAME] [--config=PATH] [--user-config=PATH] [--timeout-ms=15000] <command> [arguments]
```

并在全局参数表加入 `--user-config`。

## P1-4 提供明确、可发现的官方验证入口

### 当前问题

按场景名称执行 `dotnet test --filter` 没有产生标准测试摘要。仓库说明又指出没有专用单元测试项目，容易让维护者误判验证状态。

### 整改建议

在文档中明确区分：

- 普通解决方案构建。
- FunctionalTests 的真实启动方式和场景参数。
- 安装版验证脚本。
- 真实 SampleHost + CLI 端到端命令。

所有验证入口必须返回明确退出码和最终检查数量。

## 五、建议增强事项

### P2-1 Catalog 增加命令风险元数据

建议为命令增加只读/状态修改/破坏性分类。CLI 对 shutdown、删除、清理、反射调用等命令可输出显式确认信息，自动化调用则通过明确参数跳过交互。

### P2-2 Composite 输出增加步骤摘要

`rtdiag/3.0` 已返回逐步结果。CLI 可额外输出：总步骤、成功、失败、跳过、首个失败代码和总耗时，方便 Server 健康检查系统消费。

### P2-3 标准 Server 健康检查 Composite

可在默认 catalog 保留一个只读 composite，例如：

```text
runtime.inspect
```

其步骤只包含 host、lifecycle、registry、process、thread、task 和 pipe 状态，不自动启用开关、不恢复断点、不发送 shutdown。

当前安装版已经提供该命令，建议把字段与兼容性正式纳入 catalog 测试。

### P2-4 明确 Release CLI 与 Release Host 的能力协商

CLI 可以保留 `reflection.invoke` 命令，但 Release Host 会拒绝。建议响应中携带 capability code，避免运维系统把“Release不支持”误判为网络或配置错误。

## 六、建议的标准 Server 目录

```text
YourServer/
├─ Program.cs
├─ Hosting/
│  ├─ RuntimeHostRegistration.cs
│  └─ RuntimeWindowsServiceOptions.cs
├─ Workers/
│  └─ BusinessWorker.cs
├─ Diagnostics/
│  └─ BusinessDiagnosticRegistration.cs
└─ _migration/
   └─ Program.before-runtime.cs.txt
```

固定 Runtime 启动代码留在 Program.cs；业务 DI、Worker 和诊断白名单分文件维护。这样既保持标准模板稳定，也避免 Program.cs 再次膨胀为业务容器。

## 七、发布前验收清单

- [ ] 最小 Startup 模板复制后可编译。
- [ ] 所有模板占位符有统一替换清单。
- [ ] 普通 Console Host 启动、退出通过。
- [ ] Windows Service SCM Stop/Shutdown 通过。
- [ ] CLI shutdown 与 SCM 共用同一幂等任务。
- [ ] Wakeup 与 Stop 语义分别验证。
- [ ] 真实 RProcess/RThread/RTask 完成后再反登记。
- [ ] 重复 Start/Activate 行为明确并验证。
- [ ] CLI 所有内置命令具有非空摘要。
- [ ] 命令级帮助显示参数、风险和 Debug/Release 能力。
- [ ] `--user-config` 在帮助与手册中一致。
- [ ] Composite 单连接 batch 与逐步结果通过验证。
- [ ] RuntimeDiagnostics 与业务管道保持物理隔离。
- [ ] 管道租约正确返回 Requested/Resolved 名称。
- [ ] Debug/Release Diagnostics 变体分别验证。
- [ ] 安装版 SampleHost 使用安装 DLL 完成真实 CLI 验证。
- [ ] 发布文档、catalog、模板和安装文件版本一致。

## 八、整改优先级结论

建议发布阻断顺序：

1. P0-1 可编译模板。
2. P0-2 真实执行对象边界。
3. P0-3 Stop/Wakeup 完整示例。
4. P1-1 CLI 帮助摘要。
5. P1-2 重复初始化与多 Host 语义。
6. P1-3 文档参数一致性。
7. P1-4 官方验证入口。

完成 P0/P1 后，当前 CLI v3 与 Server Host 框架可以作为正式公共接入标准。P2 项可在不破坏现有协议的前提下逐步增强。

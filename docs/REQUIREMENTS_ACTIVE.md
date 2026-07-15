# 活跃需求文档

> 状态：ACTIVE  
> 最后更新：2026-07-12

## 2026-07-15 RemoteConsole 快速适配发布要求

- RemoteConsole 必须作为独立 Windows 服务和独立管道发布，不得复用业务宿主 Diagnostics 管道。
- 服务端授权由 `Program.cs` 集中声明提交者、审批者、默认管道和审批模式；Runtime 不创建账号、不保存密码。
- CLI 必须分别维护 Diagnostics 当前目标与 RemoteConsole 当前目标；`exit`/`quit` 只退出 Shell。
- 命令提交、审批、执行、stdout/stderr 序列、退出码、工作区和分块上传必须使用完整 Runtime Frame。
- 现有诊断 `file.list` 不得被远程工作区命令覆盖；远程清单使用 `console.file.list`。
- MSI 只安装 RemoteConsole 文件、文档和技能，不得注册/启动服务或变更服务账号。
- 真实 SCM 指定账号测试必须交互执行；权限或凭据不具备时标记环境受限，不得伪报通过。

## 2026-07-12 安装目录修订

- 完整只读套件必须形成在 `C:\Program Files\Iwesun\Runtime`：`bin`、`lib`、`docs`、`samples`、`scripts`、`skills`。
- `C:\ProgramData\Iwesun\Runtime` 仅保留运行期可修改的 `config`。
- MSI 版本升级到 1.0.2，必须从 1.0.1 正常迁移并移除旧 ProgramData 文档/样例布局。

## 2026-07-12 Runtime 业务管道管理边界

- Runtime 不只管理 RuntimeDiagnostics 管道；Management、WebRuntime 等业务专用管道也必须通过 `RuntimePipeRegistry` 统一申请、登记、解析、转发和释放。
- 每类业务使用自己的专用命名管道实例，不与 RuntimeDiagnostics/CLI 控制通道混发业务消息。
- Runtime 统一的是管道治理、代理路由和 `RuntimeDiagnosticFrame` JSON 命令格式，不是把所有流量合并到一个物理管道。
- 业务宿主持有实际服务端实例并执行具体业务命令；Runtime 保存 `Name / RequestedPipeName / ResolvedPipeName` 租约和路由关系。
- 完成 CLI v3 手册重写后统一生成 1.0.4 MSI，禁止继续分发包含旧 v2 手册的 1.0.3 构建。
- WebView2 专属技术文档统一归档到 `Iwesun.Runtime.WebView2/docs`；Runtime 总体用户手册、CLI 手册和 Quick Start 只保留跨模块入口与链接，不在根 `docs` 重复维护 WebView2 细节。
- AIGateway 不保留公共 WebView2 DTO、虚拟输入、管道协议或 Program Registry 的本地副本；业务项目只保留 WebView2 会话适配、站点选择器、登录状态机和业务动作实现。
- 完整迁移后进入业务调试前，必须审计公共 WebView2 的并发登记、取消/超时竞争、Dispose/停止顺序、Frame 关联标识、管道重连、输入坐标与会话生命周期，并以构建和相关测试确认。
- DeepSeek Web 真实调试确认 CLI v3 的位置参数绑定不得在 LINQ 谓词中递增位置游标；`web.programs.execute` 等多位置参数命令必须稳定绑定后通过 RuntimeDiagnostics/Program 链路执行。
- DeepSeek 完成等待必须只接受能够解析出非空 DeepSeek SSE 内容的当前完成消息，不能用“任意非调试 WebMessage”提前结束，否则上游已返回内容时网关仍可能输出空字符串。
- WebView2 安全审计确认需要补齐 Ambient 可重启状态机、幂等释放和任务故障状态；Program 多实例初始化失败反向回滚、逐项可靠停止、停止期间拒绝新执行；以及 CommandTarget 生命周期命令统一经过 Host 锁和状态机。
- 不恢复旧 `IWebRuntimeControl` 或旧线协议兼容层；该建议与已经批准的一次性破坏性迁移冲突，唯一兼容面仍仅限 CLI v3 用户 JSON 别名、扩展和 `composites`。

## 2026-07-12 发布版 Debug/Release 与 CLI 路由修订

- Diagnostics 必须同时发布 Debug 与 Release DLL；Debug 宿主必须引用 Debug 变体才能获得完整断点功能。
- `lib/Iwesun.Runtime.Diagnostics` 根目录继续保留 Release 兼容副本，并新增明确的 `Debug`、`Release` 子目录。
- CLI v3 catalog 的 host、lifecycle、process、thread、task 路由必须逐项匹配实际注册 target/action，不得使用设计占位 target。
- Release 断点命令返回不可用是编译策略，不得误报为运行时配置问题。
- 用户手册必须提供按 `$(Configuration)` 条件引用 Diagnostics Debug/Release DLL 的完整 `.csproj` 示例，并明确安装器不替换宿主私有输出副本。
- Management、WebRuntime 及其客户端只允许使用 4 字节小端长度前缀的 `RuntimeDiagnosticFrame`；不得保留 `ManagementPipeMessage`、私有 `PipeMessage` 或无长度前缀 JSON 的线协议兼容分支。
- 旧 `Iwesun.Runtime.WebView2.WebRuntimePipeClient` 必须由统一帧客户端完全取代，AIGateway Service、Desktop、Tester 同步一次性迁移。
- 唯一允许的兼容面位于 CLI v3 用户 JSON 配置：用户可定义别名、自定义命令和结构化组合命令；线协议和公共客户端不提供旧格式兼容。
- `Iwesun.Runtime.WebView2` 必须提供与业务无关的脚本会话、输入会话、虚拟鼠标和虚拟键盘公共能力；业务宿主只保留站点选择器、业务流程和具体 WebView2 会话适配，不得复制一套私有虚拟输入框架。
- WebRuntime 控制面不得接受或回退执行调用方提供的 JavaScript；所有业务扩展必须注册为编译期 C# 程序，由 Runtime 负责程序登记、统一 JSON 命令转接、超时、执行状态监控和结果返回。页面自身脚本不属于该控制面限制。
- WebRuntime 不得维护私有 `schema/module/success/error` Envelope；请求与响应必须完整纳入 `RuntimeDiagnosticFrame` 的 Header/Command/Status/Data/Meta，公共请求模型不得包含 `Script`，客户端必须保留 RequestId/CorrelationId、错误 Code 和 Retryable。

## 当前任务（WebView2 JSON 协议与 CLI 重规划）

1. `Iwesun.Runtime.WebView2` 需借用 Runtime 的命名管道与 JSON 转发能力。
2. 第一阶段不改变 WebView2 业务功能；先统一 JSON 命名、请求/响应包装、错误结构、请求标识和类型化 payload 风格。
3. JSON 协议是权威层；CLI 只负责把人类命令编译为协议 JSON，不得反向让协议迎合 CLI 文本。
4. 命名管道申请必须同时检查进程内活动租约与操作系统实际占用。
5. 申请名已占用时，依次使用 `_001`、`_002`、`_003` 后缀；首个未占用名作为最终管道名返回。
6. 名称检查、排他创建和租约登记必须是同一个原子流程；不允许“试创建后立即释放，仅返回字符串”的竞争窗口。
7. 管道服务端或专用租约对象必须持有排他管道实例，直到正式释放租约。
8. 在完成 JSON 风格统一和管道所有权边界设计后，再统一重规划 CLI 命令、组合命令、批处理和 WebRuntime 转发。
9. 管道登记模型仿照文件登记：`Name` 是登记键，`RequestedPipeName` 是原始申请名，`ResolvedPipeName` 是避让冲突后真正落地的管道名。
10. `RequestedPipeName` 与 `ResolvedPipeName` 必须在 JSON 中作为两个独立字段返回，不再使用 `BranchId` / `PipeName` 承担多重含义。
11. CLI 全面重构为 v3，彻底删除旧 schema、旧命令名、旧命令字符串组合步骤和 v2 配置。
12. CLI v3 保留并规范化用户别名、自定义命令、结构化 workflow、endpoint 覆盖、扩展、替换和禁用能力。
13. CLI v3 正式设计见 `docs/superpowers/specs/2026-07-12-cli-v3-redesign.md`。
14. 新建 `docs/IWESUN_RUNTIME_USER_GUIDE.md` 作为业务接入权威手册，依次覆盖主程序替换、进程/线程/任务替换、诊断单点注入、业务状态与退出、JSON 与 CLI 清单。
15. 用户手册设计见 `docs/superpowers/specs/2026-07-12-runtime-user-guide-design.md`；CLI v3 实施前必须将其标记为迁移中，不得冒充当前可用功能。
16. 用户手册已落地为 `docs/IWESUN_RUNTIME_USER_GUIDE.md`，包含五个主章、Debug/Release 矩阵和完整接入/迁移检查表。
17. Runtime 接入技能已安装到 `C:/Users/LYH/.codex/skills/iwesun-runtime-integration`，并保留字节级一致的仓库副本 `skills/iwesun-runtime-integration`。
18. 技能主副本已分别通过 `skill-creator/quick_validate.py`，且目录对比无差异。
19. 完整发布需包含 CLI、Data、Diagnostics、WebView2、SampleHost 的 Release x64 产物，以及 CLI v3 JSON、权威文档、技能和标准源码。
20. 新增 WiX SDK `Iwesun.Runtime.Setup` 生成 framework-dependent Windows x64 MSI，安装到 `C:/Program Files/Iwesun/Runtime`，并使用 `C:/ProgramData/Iwesun/Runtime` 保存可写用户数据。
21. Setup 必须检查 .NET 10 Runtime、注册 `iwrt.exe` 系统 PATH、保留 ProgramData 用户数据，并支持升级/修复/卸载。
22. 发布和 Setup 正式完成以 CLI v3 先实施为前置；任何 v2 CLI JSON 都不得进入新发布包。
23. Release/Setup 正式设计见 `docs/superpowers/specs/2026-07-12-runtime-release-setup-design.md`。

### 当前阶段执行状态

- [x] 管道租约快照已统一为 `Name / RequestedPipeName / ResolvedPipeName`。
- [x] 重名避让后缀已统一为 `_001`、`_002` 顺序。
- [x] CLI `pipe.acquire` 参数已改为 `requestedPipeName`。
- [x] RProcess 跨进程公告已使用 `requestedPipeName / resolvedPipeName`。
- [x] Debug / Release 构建通过；`pipe-registry`、`process`、`tree-process` 真实场景通过。
- [ ] 操作系统排他占位句柄与多进程同名竞争尚待下一阶段实施。

## 当前任务（临时追加）

1. 将 CA1416 平台兼容性修复策略在本次涉及代码范围内统一应用（“选整个”）。
2. 目标是消除与 Windows 限定 API 相关的编译/分析警告，不扩展到无关注警（如 CS1591）。
3. 采用一致方案：对使用 `MemoryMappedFile.CreateOrOpen`、命名 `Semaphore/Mutex` 的类型添加 `[SupportedOSPlatform("windows")]`。
4. 使用真实 CLI 动态控制跨进程逻辑断点，验证启用、等待快照与恢复链路。
5. 使用真实 CLI 动态修改数值数据断点规则，验证命中、未命中、规则切换与恢复链路。
6. 断点初始化与动态控制必须分别验证：程序集反射 `Enabled=true` 无需 CLI 即生效，`Enabled=false` 默认不生效，运行期可通过 CLI 启用、禁止与恢复。
7. 扩展真实 SampleHost：保留三条定时循环，Worker 周期生成随机数并注入输出、监视、对象、普通断点、数值断点、事件、线程/任务与状态能力。
8. FunctionalTests 启动真实 SampleHost 后，必须通过编译后的 CLI v3 完整验证宿主、开关、登记表、反射、断点、数据断点、执行状态和 `lifecycle.shutdown`。

## 需求来源（用户对话整理）

1. 先整理核心项目功能清单，明确“有哪些功能、如何测试”。
2. 基于功能清单编写测试规划文档，再进入具体测试实现。
3. 编写并接入一个独立的功能测试程序，覆盖 Runtime.Diagnostics 的关键链路。
4. 测试范围至少包含：诊断宿主初始化、`runtime.managed` 命令目标、线程/进程/任务生命周期、统一状态同步、监控管道帧协议与 RProcess 管道通信。
5. 这是要落地执行的测试程序，不只是口头说明。
6. 将前面汇总的 RuntimeRoot 单根数据结构落实到代码：先用 DLIST 作为容器包装条目，并引入基础条目 `struct`（含 `Id`、主键、辅助键、时间戳、`object` 负载），后续按性能需要再扩展辅助结构。
7. Runtime 已进入持续开发阶段，DDNS Snap 作为宿主/消费项目纳入当前构建与验证范围，相关说明只做引用，不重复改设计文档。
8. 现有用于调试的文件输出与控制台输出需要移除；如后续需要调试，再按新的诊断接入方式补回。
9. 程序启动与进程/线程创建需要切换到新的接入方式，替换旧的继承式/直接调用式用法。
10. 调试程序时优先使用新的 CLI，并按新的命令格式操作，不再依赖旧命令路径。
11. Runtime CLI 命令体系统一到新规范（v2），清理旧命令规范带来的混用与歧义。
12. CLI 必须保持配置可扩展：允许用户通过 JSON 定义别名与组合命令并叠加到默认命令集。
13. 新标准命令输入语法采用 PowerShell 风格参数格式（如 `-count 2` / `-count:2`），并确认内部执行链路完整。
14. 别名与组合命令由用户自行在 JSON 中配置实现，运行时只负责加载与执行。
15. 需要将 CLI 编译为可分发 exe，并同时提供新版 JSON 配置文件后再进行程序调试验证。
16. 需要对当前自有程序执行快速实机调试，优先输出可用性结论（状态、事件、线程/进程快照）。
17. 监视开关异常（尤其 `fileOutputEnabled` 运行态错误）归属 Runtime 项目处理：需要在 `Iwesun.Runtime.Diagnostics` 中定位并修正，不在 DDNS Snap 项目层面规避。
18. 编译默认值策略需要调整：`GlobalEnabled`/`PipeOutputEnabled`/`FileOutputEnabled` 仅在缺省时为 `false`，不得因编译配置版本门控被固定为 `false`；用户已保存的开关值必须可保留并生效。
19. 诊断主管道名与文件输出路径采用统一启动期优先级：命令参数 > 配置 JSON > 启动源码默认值；该策略在启动时一次性决议，不依赖运行期动态设置。
20. 在基础静态 RuntimeRoot 中新增文件路径 DLIST：`DLIST<文件路径说明符>`；说明符至少包含文件路径名与配套说明字段，主记录文件路径必须排在第一条；该 Root 属于全局共享内存语义，主监控程序可写，其他程序只读。
21. 文件路径名不允许运行期动态修改；文件输出监视开关允许运行期动态开关，并且状态变化需要可从 CLI 追踪到 RuntimeRoot 的最终路径描述数据。
22. Runtime 相关能力的代码修改必须落地在 Runtime 自身仓库中（`D:\Git Space\Runtime`），不得把 Runtime 实现改动落在 DDNS Snap 仓库；DDNS Snap 仅作为使用方，遵循“尽量少改动、以引用和接入为主”的原则。
23. 诊断文件记录需要具备“无需 CLI 命令即可自动启动记录”的默认行为：编译默认开关与最小点位应支持启动即落盘，避免运行期手动启用。

## 核心功能清单（当前整理结果）

1. 运行时诊断宿主初始化与 DI 注入。
2. `runtime.managed` 命令路由与注册/事件/FIFO 队列。
3. 线程包装与守护退出链路（RThread）。
4. 任务包装与完成/取消/故障链路（RTask）。
5. 进程包装、退出回收与管道交换链路（RProcess）。
6. 统一状态模型与同步链路（RManagedState / RuntimeStateManager）。
7. 诊断帧协议与 Hub 路由链路（RuntimeDiagnosticHub / RuntimeDiagnosticsMonitor）。
8. 监控开关、断点与钩子等辅助诊断能力。
9. CLI 协议边界与命令解析（Iwesun.Runtime.Cli）。
10. SampleHost 启动/退出与宿主接入样例（Iwesun.Runtime.SampleHost）。

## 当前测试覆盖统计（已知状态）

### 已完成的真实功能测试
- 诊断宿主初始化 + 诊断管道帧交换：已覆盖。
- `runtime.managed` 注册/事件/FIFO/列表/出队：已覆盖。
- `RThread` 启动、停止、退出事件与注册回收：已覆盖。
- `RTask` 完成态与注册回收：已覆盖。
- `RProcess` 进程启动、管道交换、退出与回收：已覆盖。

### 仅做基础验证、尚未形成严格功能测试
- SampleHost 启动/退出链路：当前作为宿主样例存在，尚未纳入端到端场景验证。
- 统一状态模型的独立回归：功能已在其他场景间接验证，但尚未单独作为一条专门回归场景固定。
- 监控开关、断点与钩子：已通过树场景补齐基础验证，但仍需继续扩展成独立回归场景。
- CLI 的更多命令边界：已完成 `host.info` 端到端可用性验证，但 `sw.*`、`bp.*`、`hook.*` 等命令族仍需继续补充独立边界场景。
- 树状对象与登记表暴露：已新增树场景与登记表驱动的可观察对象，当前可用于监控和回放，但还需要纳入更完整的边界与失败路径。

### 已知未覆盖或需补强
- `runtime.diagnostics` 之外的诊断控制命令场景。
- 失败路径与边界条件：如超时、无效输入、重复注册、异常退出、管道中断。
- 解决方案级集成：功能测试项目尚需加入 `.slnx`。
- 新 CLI 命令族与旧调试入口的替换验证。
- 启动/进程/线程新接入方式在宿主中的端到端验证。
- 调试输出静默化后的回归验证（不再产生控制台和文件监视输出）。

## 真实功能测试规划（当前阶段）

- 采用“父进程驱动 + 子进程真实执行”的方式，不再只做接口直调。
- 子进程必须实际创建并运行 `RThread`、`RTask`、`RProcess`、诊断宿主与管道通信。
- 父进程通过标准输出 JSON、诊断管道响应、进程退出码、事件/状态快照进行断言。
- 每个功能点至少包含：真实启动、真实运行、真实可观测结果、真实收尾四层断言。
- 后续再把规划拆成正式测试文档，补充更严格失败报告与边界场景。
- 按优先级补齐 CLI 其他命令族、SampleHost、状态模型和辅助诊断能力的独立回归场景。
- 为每个场景定义正向路径与至少一条负向路径，确保不是只验证“能跑起来”。

### 功能测试矩阵（每个功能怎么测）

| 功能 | 测试方式 | 真实断言 | 当前状态 |
|---|---|---|---|
| 诊断宿主初始化与 DI 注入 | 子进程启动真实 Host，读取 `RuntimeDiagnosticsMonitor`、`RuntimeDiagnosticHub`、`RuntimeManagedRegistry` 等服务 | 宿主可启动、监控可运行、管道名可用、Hub 可返回快照 | 已覆盖 |
| `runtime.managed` 命令路由 | 通过真实命令目标执行 `snapshot`、`registrations`、`events`、`enqueue`、`dequeue` | 注册表变化可见、事件队列可见、出队结果正确、注销后清理正确 | 已覆盖 |
| `RThread` 线程生命周期 | 真实创建线程、等待运行、发送 stop、观察 join 与退出 | 线程注册可见、运行态可见、退出请求/完成事件出现、注销后不存在 | 已覆盖 |
| `RTask` 任务生命周期 | 真实执行任务并等待完成 | 任务完成态可见、任务注销完成、无残留记录 | 已覆盖 |
| `RProcess` 进程与管道交换 | 真实启动子进程并做管道帧交换 | 管道请求/响应成功、进程退出码为 0、进程注销完成 | 已覆盖 |
| 统一状态模型 | 通过线程/任务/进程场景间接验证，并建议增加单独状态回归 | 状态迁移正确、快照与运行态一致、停止后归零/收尾正确 | 部分覆盖 |
| 诊断帧协议与 Hub 路由 | 发送真实诊断帧到 monitor pipe，由 Hub 返回响应 | 响应 JSON 可解析、状态码成功、返回数据包含预期快照 | 已覆盖 |
| 监控开关、断点、钩子 | 设计独立场景触发开关和钩子路径 | 开关变更可观测、断点命中可观测、钩子结果可见 | 树场景已覆盖基础路径，仍需独立回归 |
| 树状对象与登记表暴露 | 通过树对象、watch/break/hook/output 点和 registry 查询做观测 | 树快照、登记表、断点、输出点、钩子都可被监控程序读到 | 已新增 |
| CLI 协议边界与命令解析 | 运行 CLI 子进程或调用实际命令入口 | 解析结果、退出码、协议边界错误信息符合预期 | 已覆盖 `host.info` 端到端入口，其他命令族待补强 |
| SampleHost 启动/退出样例 | 运行 SampleHost 真实启动并退出 | 宿主能初始化、能收尾、日志/状态不异常 | 未形成严格场景 |

### 场景级断言规则

- 启动断言：必须是实际进程/宿主启动成功，而不是对象实例化成功。
- 运行断言：必须出现可观测行为，例如事件、状态变化、管道响应、输出 JSON。
- 失败断言：必须验证无效输入、超时、异常退出、重复注册等边界路径。
- 收尾断言：必须检查注销、退出码、资源释放、残留记录清理。

### 测试分层

1. 冒烟层
- 目标：确认宿主、CLI 和测试程序能启动。
- 断言：进程能起来、最小命令能返回、无初始化错误。

2. 真实功能层
- 目标：验证核心链路的外部行为。
- 覆盖：诊断宿主、`runtime.managed`、RThread、RTask、RProcess、CLI host.info、管道协议。
- 断言：真实状态变化、事件、快照、退出码、响应内容。

3. 回归层
- 目标：防止边界问题和历史故障回退。
- 覆盖：超时、无效输入、重复注册、管道中断、异常退出、资源释放。
- 断言：错误码、错误消息、清理后无残留。

4. 扩展层
- 目标：补齐目前未独立验证的功能域。
- 覆盖：CLI 更多命令族边界、SampleHost、统一状态模型独立回归、监控开关/断点/钩子、树状对象与登记表。
- 断言：可观测输出与状态一致，且有正向/负向各一条。
- 当前新增树场景只作为基础层验证，后续应补齐边界与失败路径。

## 执行前一致性检查

- [x] 已更新当前任务到活跃需求文档
- [x] 已确认本轮目标是梳理功能清单与测试规划
- [x] 已读取解决方案结构与现有宿主入口

## 执行后复核

- [x] 已创建独立功能测试程序
- [ ] 已把测试程序加入解决方案
- [x] 已实现关键链路功能测试用例
- [x] 已完成构建/运行验证
- [x] 已回读本需求文档并核对一致性
- [x] 已形成功能清单、覆盖统计、测试矩阵与分层规划
- [x] 已将 CLI 命令入口统一到 v2 规范，并对旧 schema 给出明确阻断提示
- [x] 已支持用户通过 JSON 覆盖/扩展命令（别名 + 组合命令）
- [x] 已支持 PowerShell 风格参数解析（`-name value` 与 `-name:value`）并通过运行验证
- [x] 已完成 CLI 发布产物（exe + 新版 JSON）并通过发布目录下调试命令验证
- [ ] 已为 CLI、SampleHost、统一状态模型、监控开关/断点/钩子、树状对象与登记表补齐独立回归场景
- [x] 树状对象与登记表的基础可观测场景已落地
- [x] 已落地 RuntimeRoot 容器基础能力（DLIST + 基础条目 struct + 主键/辅助键包装）
- [x] 已接入 `runtime.root` 诊断目标并可通过命令查询根容器数据
- [x] 已落地 DLIST 辅助索引扩展（`IRuntimeRootAuxIndex` + `sorted-primary` 前缀查询）
- [x] 已同步输出 RuntimeRoot 容器技术文档
- [x] 已将基础类下沉到 Data 公共项目（`Iwesun.Runtime.Data`）并补齐 Data 项目技术文档
- [x] 已完成 DLIST 一次性改造（归一判定、可重复开关、过滤委托、批量新增、委托排序、重复合并）
- [x] 已在 `Iwesun.Runtime.Diagnostics` 修复监视开关误输入兼容问题：`enable/disable` 遇到 `section=true/false` 时按全局开关处理，避免错误创建同名 section 导致状态误判
- [x] 已调整编译默认值合并策略：三大开关改为“缺省 false、用户配置可保留”，不再因编译配置版本门控固定为 false
- [x] 已移除初始化合并中的 schema 门控读取限制：`Sections` 与 `OutputPoints.Enabled` 也按用户持久化值读取（缺省仍由编译默认值补齐）
- [x] 已落地启动期统一优先级：诊断主管道名/文件路径按“命令参数 > 配置 JSON > 启动源码默认值”决议并生效
- [x] 已在静态 RuntimeRoot 中新增文件路径 DLIST（文件路径说明符），并保证主记录路径排在第一条
- [x] 已固定文件路径名为启动期静态策略（禁用运行期 setfilepath），并保留 `sw.file` 运行期动态开关
- [x] 已新增 CLI `root.paths` 命令，可追踪 RuntimeRoot 中的最终文件路径描述数据
- [x] 已补充 Runtime 改动边界约束：Runtime 实现改动只落地 Runtime 仓库，DDNS Snap 仓库保持使用方最小改动策略
- [x] 已新增“Runtime 改动边界与修改记录”文档，供后续协作与审计追踪
- [x] 跨进程断点 CLI 场景：已移除客户端断开时的自动 `ResumeAllBreakpoints`；10 个断点均验证连续快照与 CLI 断开不改变等待状态，仅显式 `bp.resume` 才恢复
- [x] CLI 动态数值数据断点：已实测 `gt 10` 命中/恢复、切换 `lt 5` 后未命中与再次命中/恢复，`listNumeric` 可确认最终规则
- [x] 数值断点静态谓词登记：阈值运算符登记失败时回退到静态谓词目录，`eq2`/`ne2` 已在真实跨进程 CLI 场景中命中并恢复
- [x] 断点初始值与动态控制：程序集初始启用断点可直接进入等待；初始禁止断点不阻塞；CLI 动态启用、规则切换、禁止和显式恢复链路已覆盖
- [x] SampleHost 完整 CLI 场景：真实宿主与真实 CLI 子进程完成 64 项检查，覆盖随机定时循环、反射白名单、编译初始断点、动态断点、数值断点、CLI 断开状态稳定、事件钩子、线程/任务查询、文件记录和安全退出
- [x] SampleHost 监视记录器：已接入 `AddRuntimeDiagnostics()`；默认仍保持静默，测试结束显式关闭文件、管道、hooks、sample-host section 和全局开关
- [x] SampleHost 文件策略：程序集配置为 `logs/sample-host-diag.jsonl`、`CreateNew`、`PlainText`；运行时解析为带时间戳的新文件，已验证文件存在且非空
- [x] 完整功能回归：21/21 场景成功、失败列表为空；报告：`Iwesun.Runtime.FunctionalTests/bin/Debug/net10.0/runtime-functional-reports/full-report-20260711-145648.json`
- [x] 解决方案构建：Debug 0 错误/61 警告，Release 0 错误/62 警告；警告债务为既有 CA1416 平台标注和 CLI CS1591 XML 注释
- [x] Release 构建阻断修复：`BreakpointHelper.SetInstance` 仅在 `DEBUG` 编译，Release 不再引用仅调试期存在的辅助类型
- [ ] shutdown 与宿主业务启动方式的结构性调整后续单独处理；本轮仅验证 `safe-shutdown` 命令收到成功响应后 SampleHost 能以退出码 0 安全结束
- [x] 明确“单点注入”定义：每个诊断功能的标识、条件、上下文和调用集中在业务现场一个连续代码段；不是“只声明一个点”，程序集特性/启动注册只用于确有需要的预编译目录、初始状态、事件登记或反射白名单
- [x] Release 断点隔离：不编译断点实现类型、共享状态、等待信号、数值谓词和命令处理；不做 DI 注册、Hub 路由、程序集断点扫描及 RuntimeRoot 断点表刷新；业务调用由 `#if DEBUG` 隔离
- [x] Release 业务诊断保留：输出、日志、反射白名单、事件钩子、管道、状态/执行表、文件记录和安全退出继续开放给业务逻辑使用
# 2026-07-12 仓库全面清理与发布边界

- 保留源码、活动文档、脚本、技能和安装项目源码。
- 每个工程仅保留 `bin/Debug` 与 `bin/Release` 编译结果；删除所有 `obj`、`publish`、RID、测试结果和其他中间输出。
- 删除仓库级 `.vs`、`artifacts`、聚合 `bin`、`logs`、缓存及系统依赖准备产物。
- 删除被根级 `Iwesun.Runtime.Setup` 取代的旧 `setup/Iwesun.Runtime.Setup`。
- 更新 `.gitignore`、`.copilotignore` 并新增 `.codexignore`，统一屏蔽生成物、依赖缓存、本机配置和归档。
- 活动文档、CLI v3、Setup 源码与清理规则完成验证后提交并推送仓库。

# 2026-07-12 安装版接入速查手册

- 技术手册保留完整设计和接口解释；另设一份很短的速查手册，作为安装后的首要入口。
- 速查手册必须按可执行顺序说明：备份旧 `Program.cs`、按 Debug/Release 引用对应 DLL、全工程分步字符串替换、填写主管道名、填写主记录文件路径/格式/写入模式、编译验证。
- 进程、线程、轻量任务及其声明类型和返回类型必须给出明确的“查找 -> 替换”样例。
- 必须明确不能全局替换的类型和场景，尤其是 `Task<T>`、普通异步 `Task`、UI/STA/COM/message-pump 线程。
- 发布包必须携带速查手册、启动模板、替换清单和可编译的安装版 SampleHost 项目。
- 安装版 SampleHost 必须分别引用 Debug/Release Diagnostics DLL，并在两种配置下完成编译和真实 CLI 验证。
- 速查手册必须完整纳入当前 Runtime 新架构，除宿主启动和受管执行外，还必须覆盖：统一 `RuntimeDiagnosticFrame`、4 字节小端帧、RuntimeDiagnostics 与业务专用管道边界、`RuntimePipeRegistry` 租约与名称解析、CLI v3 用户配置和 `composites`、WebRuntime 预编译 C# Program 截获、诊断/断点安全规则、Stop/Wakeup 与实际完成后释放、分层验证和迁移完成清单。
- `WebRuntimeControlRequest` 只能描述 Command typed Args，不得重新成为私有线协议 Envelope；速查手册不得出现旧 `schema/module/success/error` WebRuntime DTO 或调用方 JavaScript 执行入口。
- 源码 Quick Start 与 `C:\Program Files\Iwesun\Runtime\docs` 发布副本必须保持字节级一致。

# 2026-07-12 CLI 用户增量配置

- 标准 v3 catalog 保持规范化且不预置用户别名或组合指令。
- 新增 `--user-config=PATH`，在标准 catalog 之上加载用户增量 JSON，而不是要求用户复制整份标准配置。
- `extensions.add/extend/replace/disable` 必须按确定顺序合并；`extend` 当前用于追加别名，不能静默覆盖标准路由。
- 用户配置中的 endpoint 可新增或显式覆盖，以便新增命令引用用户管道；合并后统一执行完整 schema、命令、别名和 endpoint 校验。
- 未知的 extend/replace/disable 目标、重复命令、重复别名和缺失文件必须返回明确 CLI 错误。
- 结构化 workflow 执行不混入本轮；配置字段保留，未实现时不得宣称可执行。

# 2026-07-12 CLI 复合命令

- 复合命令不是 workflow；只把若干现有标准命令展开为一个协议 batch，一次发送并统一返回。
- 配置根字段统一为 `composites`，删除容易造成错误预期的 `workflows`。
- 每个复合步骤引用已有命令名并携带固定参数；不提供条件、循环、结果绑定或脚本文本。
- 一个复合命令内的所有步骤必须使用同一个 endpoint，步骤数限制为 1~128。
- 标准 catalog 提供一个常用的大命令，用户增量配置也可以添加自己的复合命令和别名。

# 2026-07-12 Runtime 配置无文件化整改

- 取消 `diagnostic-switchboard.json` 的运行时读取、合并、回写和兼容迁移，不再让路径相关配置文件参与程序身份或诊断状态决议。
- 源码/程序集声明提供可靠固定值；启动命令行参数是唯一启动期覆盖入口。
- CLI 控制命令只修改当前进程内存状态，进程退出后自然恢复源码默认值。
- 主管道名由源码固定，`--diag-pipe` 仅用于测试、调试和临时实例覆盖。
- 主记录文件路径由源码固定，`--diag-file` 是唯一临时覆盖入口。
- 删除或明确拒绝所有 `persist` 语义，禁止 CLI 把动态状态写入配置文件。
- 快照继续只读显示最终管道名、文件路径和开关状态，供 CLI 审计实际生效值。
- 整改方案与 WebView2 安全审计答复报告同步归档，实施和测试完成前不得重新发布。

# 2026-07-12 整改所有权与变更冻结

- Runtime、CLI、Diagnostics、Data、WebView2 公共库及发布工程的最终整改只在 `D:\Git Space\Runtime` 实施。
- WebView2 使用方和其他宿主仓库以后只提交问题、复现步骤、日志、预期行为和审查意见，不再直接修改 Runtime 仓库源码。
- 整改期间冻结外部并行改码；任何建议先进入审计答复和整改清单，由 Runtime 侧统一评估、实现、测试和发布。
- 四宿主中已加入的强制写 JSON 仅作为临时联调措施；Runtime 无配置文件化完成后，由宿主侧按 Runtime 发布接口做最小删除/替换，不复制 Runtime 内部逻辑。
- 全部整改必须按阶段提交证据，未通过阶段验收不得打包发布。

# 2026-07-12 唯一全量打包入口

- 新增 `scripts/release/build-runtime-setup.ps1` 作为唯一正式打包程序。
- 每次固定执行 Debug/Release 全构建、完整 staging、自检和 MSI 强制 Rebuild。
- 禁止把 WiX 增量复用产生的旧 MSI 当作新发布包。
- `ProductVersion` 由打包程序统一传入 Setup，不再手工同步多个版本位置。
- 每次全量收集 DLL、CLI JSON、文档、技能、样例和脚本；发布目录不得包含样例 bin/obj 或 diagnostic-switchboard.json。
- Runtime 宿主最低 Microsoft.Extensions 依赖版本为 10.0.9，用户手册必须明确提示旧 preview 的运行时崩溃风险。
- DLL AssemblyVersion/FileVersion 必须与 MSI ProductVersion 可比较地同步，不得继续固定为 1.0.0.0。
- 发布文档中的所有相对链接必须在安装目录结构中有效，WebRuntime 控制文档必须实际进入 docs 目录。

# 2026-07-12 零业务钩子安全退出

- `RProcess`、`RThread`、`RTask` 的安全退出必须由 Runtime 托管层自行驱动，业务代码不需要挂载退出事件才能完成标准退出。
- 收到全局 Stop 或单元 FIFO Stop 后，Runtime 先发布退出事件；没有订阅者时事件发布立即完成，并继续执行托管清理流程。
- Runtime 必须监视退出期限，并且只在底层进程、线程或任务真实完成后转换停止状态、清理资源并反登记。
- 支持取消令牌的 `RTask` 由 Runtime 发出取消；不响应取消且仍在运行的任务不得被伪反登记，主控应在期限到达后返回超时退出码 124。
- 零业务退出钩子的标准路径必须由 SampleHost 或聚焦测试覆盖，正常退出应返回 0，登记表和阻塞退出的 DLIST 应排空。

# 2026-07-12 扁平托管正常出口整改

- Runtime 创建的每个进程、线程和小任务都必须拥有自己的守护处理，并采用相同的 Stop、退出事件、正常出口、完成确认和自动反登记流程。
- 所有进程、线程和任务扁平登记在主控注册表；Stop 的枚举和广播统一由主控完成，单元之间不得建立父通知子或逐级转发命令链。
- 进程、线程和任务不得拥有互相独立的退出倒计时；唯一截止时间由总控发布并随 Stop 指令传递。
- 单元退出不得调用 `Process.Kill`、`Environment.Exit`、伪造完成或提前反登记；Runtime 只负责推动其受控执行入口从正常出口返回。
- 三种单元提供统一的可选业务清理事件和清理完成标志；事件委托全部返回后标志成立。
- 退出事件无人订阅时，清理完成标志必须在 Stop 到达时立即成立，守护程序不得增加空等待。
- 退出事件存在订阅者时，守护程序同时监视清理完成标志和主控下发的唯一截止时间；委托完成则立即继续，截止时间到达仍未完成也必须继续退出。
- 重复 Stop、全局状态轮询和 FIFO Stop 同时到达必须幂等，同一单元只发布一次退出周期、只完成一次清理、只反登记一次。
- 只有总控在全局截止时间到达且登记仍未排空时返回 124，并负责处理不属于 Runtime 创建或无法沿托管出口退出的外部对象。
- 高频退出必须覆盖零钩子、有钩子、清理完成标志、清理超时、重复 Stop、扁平广播及最终登记排空测试。

# 2026-07-12 退出整改发布

- 退出清理公共 API、共享状态、轻量信号量、退出码和自动反登记规则必须同步到用户手册、速查手册及仓库/本机技能。
- Debug/Release 全构建和 thread/task/process 聚焦回归通过后，使用唯一全量打包程序发布 1.0.11。

# 2026-07-12 Windows Service 平台宿主

- Windows Service 接口由 Runtime 平台层实现，业务宿主不得重复拼接 SCM 生命周期、协调退出、退出码和幂等控制。
- 保留现有 `Start(...)` 作为普通控制台、桌面和 Generic Host 入口，行为必须完全不变。
- 新增显式、可选的 `StartWindowsService(...)`；只有调用该入口时才引入 Windows Service Lifetime 和 SCM Stop/Shutdown 处理。
- 服务入口至少接收服务名、Runtime 目录、诊断管道/文件声明和唯一总退出期限，并复用现有 `Start/Activate` 与 `RuntimeShutdownCoordinator`。
- SCM Stop、系统关机和 CLI shutdown 必须汇聚到同一个幂等协调退出任务；Root 扁平广播、CleanupRequested、共享状态、退出信号和反登记不得形成第二套实现。
- Runtime 必须等待协调退出结果并设置 0/124；不得调用 Process.Kill 或为每个单元创建私有退出倒计时。
- Windows Service 集成必须保留业务标准 `IHostedService`/`BackgroundService` 接口，不要求继承 Runtime 私有服务基类。
- 必须测试普通 `Start()` 不注册 Windows Service Lifetime，以及服务模式 Stop 能触发 Runtime 协调退出。
- Windows Service 公共入口作为 1.0.12 发布；安装目录必须携带 10.0.9 WindowsServices 运行依赖、更新后的 SampleHost、文档和技能。
- 新增独立 `IWESUN_RUNTIME_WINDOWS_SERVICE.md`，明确 Runtime 主控负责服务系统接口，业务只实现标准 HostedService、可选清理与状态汇报；文档必须进入索引和全量发布包。
- Windows Service 身份完全由业务提供：支持 Program.cs 直接传 `RuntimeWindowsServiceOptions`，也支持业务 DI 模块通过 `ConfigureRuntimeWindowsService` API 配置、固定主程序调用 `StartConfiguredWindowsService`；Runtime 不得预设 ServiceName。
- 最终携带业务 API 配置入口和独立 Server 手册的全量发布版本为 1.0.13，禁止覆盖已生成的 1.0.12。

# 2026-07-12 CLI 反射调用补口

- CLI v3 新增 `reflection.invoke <target> <member>`，用于调用宿主已在 `InvokableMembers` 白名单登记的无参数方法。
- `reflection.get` 继续只读，不得通过读取命令隐式调用方法；invoke 不得绕过白名单或支持任意带参数调用。
- `GetPeerStatuses`、`GetAgentSnapshots`、`GetSnapshot` 等业务方法是否可调用完全取决于宿主显式登记，Runtime CLI 只提供通用协议入口。
- `domains/peerServers`、公共 DNS 与服务商密钥解耦、`/api/darp/wake` 属于业务宿主整改，不进入 Runtime 公共实现。
- 携带 Server 业务 API 配置、独立 Server 手册和 CLI reflection.invoke 的最终全量发布版本为 1.0.14。
- `reflection.invoke` 与断点采用相同编译边界：仅 DEBUG 编译；Release 必须删除 invoke 路由和执行代码，保留 `reflection.get` 只读能力。
- 宿主的 `InvokableMembers` 调试登记建议放入 `#if DEBUG`；CLI catalog 可保留命令定义，以便 Debug/Release 使用同一客户端并由 Release 宿主明确拒绝。
- 独立 Windows Service 手册必须同步记录 Debug-only reflection.invoke、Release reflection.get 和业务方法白名单示例。
- 文档、JSON、CLI、技能和 Server 专项手册全部同步后的最终全量发布版本为 1.0.15。
- Server 专项手册必须同时提供完整 Program.cs 直配例子、固定 Program.cs + 业务 DI API 配置例子，以及 HostedService/RTask/CleanupRequested/状态汇报组合例子。
- 包含三套完整 Server 示例的最终全量发布版本为 1.0.16。

# 2026-07-12 CLI Server 标准框架审计意见

- 对当前 CLI v3、`RuntimeHostTemplate`、Windows Service 生命周期、发布模板和安装版 catalog 形成正式书面审计意见。
- 意见书必须区分已验证事实、必须整改、建议增强和验收标准，不把文档推断写成已验证功能。
- 优先整改可复制模板的可编译性、真实执行对象与描述性登记的边界、Stop/Wakeup 完整示例、CLI 帮助摘要和重复 Start/Activate 语义。
- 正式意见书放在仓库根目录 `CLI_Server标准框架审计意见书.md`，供后续 Runtime 公共模块整改和发布审查使用。

# 2026-07-12 CLI Server 标准框架整改基线

> 来源：仓库根目录 `CLI_Server标准框架审计意见书.md`。以下 P0、P1 为下一正式发布的阻断条件；P2 为兼容性增强，不得反向扩大为脚本工作流或任意反射执行。

正式逐项答复、实施阶段、测试矩阵和责任边界见仓库根目录 `CLI_Server标准框架审计综合答复与整改方案.md`。

## P0：发布前必须完成

1. 发布两个职责分离的 Server 模板：
   - `RuntimeHost.Startup.Minimal.Template.cs.txt` 必须能够复制到空白 .NET 10 Worker 项目，仅按文件顶部清单替换统一的 `__PLACEHOLDER__` 后，在 Debug、Release 下直接编译；业务注册区为空时也必须启动，不得依赖 `YourHostedWorker` 等预先创建的业务类型。
   - `RuntimeHost.DiagnosticsExamples.Template.cs.txt` 只承载可选 Data、Thread、Task、Reflection 示例，不得让未定义业务变量进入最小启动模板。
2. 文档、模板、快照和 CLI 必须明确区分：
   - `RuntimeInjector.Thread/Task` 是描述性或外部对象登记，不创建、不托管执行对象。
   - `RuntimeInjector.CreateThread/CreateTask`、`RThread/RTask` 才是实际受管执行对象。
   - `BackgroundService.ExecuteAsync` 属于 Generic Host 执行链，不得仅因增加描述记录就宣称已转换为 `RTask`。
3. 至少提供一个真实 `RTask` Server 示例，并验证 Stop、正常完成、异常和实际完成后反登记；仍在运行的对象不得因 Dispose 或超时被伪报停止。
4. 标准 Worker 示例必须同时展示全局 Stop 状态轮询、FIFO `Stop`、FIFO `Wakeup`、可取消且可唤醒等待、`CleanupRequested`、完成状态和实际反登记。
5. `Wakeup` 只打断当前等待并继续工作，不等同于 Stop；CLI 断开不得改变 Stop、Wakeup 或断点状态。

## P1：正式发布阻断项

1. CLI 标准 catalog 的每条内置命令必须具有非空 `summary`，并声明参数名称、类型、必填性、状态修改/危险等级以及 Debug/Release 可用性。
   - 历史 CLI 使用严格未知字段拒绝，因此路由与元数据分离；新名称统一为 `RuntimeCliSystemConfig.json` 与 `RuntimeCliSystemMetadata.json`。
2. `iwrt <command> --help` 必须显示 endpoint、参数、简短示例和能力限制；`lifecycle.shutdown` 必须说明破坏性/确认参数，`reflection.invoke` 必须说明 Debug-only、无参数和白名单限制。
3. Runtime 采用“单进程单 Runtime Host”作为当前标准语义：
   - 重复 `Start`、同一 provider 重复 `Activate` 的行为必须明确、幂等或结构化拒绝，并有测试。
   - 同一 `IServiceCollection`、相同参数重复 `Start` 必须幂等；参数冲突抛出专用 .NET `RuntimeHostConfigurationException`。DI 注册阶段没有 Frame 通道，CLI 结构化错误只适用于宿主启动后的命令阶段。
   - 不同 provider 再次 `Activate` 必须 fail-fast，保留首次 Host 身份，不得静默覆盖进程静态 `RuntimeInjectionContext`。
   - 在真正实现 Host/Scope 隔离前，不得宣称支持同进程多 Runtime Host。
4. CLI 手册主调用格式统一为：
   `iwrt [--pipe=NAME] [--config=PATH] [--user-config=PATH] [--timeout-ms=15000] <command> [arguments]`。
5. 文档必须给出可发现且返回明确退出码/检查总数的官方验证入口，分别覆盖解决方案构建、FunctionalTests 场景运行、安装版验证脚本以及 SampleHost + CLI 端到端验证；不得把无测试摘要的过滤命令记为通过。
6. 安装版 SampleHost 不得使用 Runtime 源码 `ProjectReference` 或仓库内手工 DLL；引用来源必须是 `C:\Program Files\Iwesun\Runtime`。允许 `Private=true` 复制到输出目录，但副本哈希必须与对应 Debug/Release 安装 DLL 一致。

## P2：保持兼容的增强项

- Catalog 可增加只读、状态修改、破坏性风险元数据；自动化跳过交互必须使用显式参数。
- Composite 结果可增加总步骤、成功、失败、跳过、首个失败代码和总耗时摘要，但摘要必须位于完整 Frame 的稳定 `Data/Meta` 字段中；CLI 默认仍只输出一个 JSON，不额外打印非 JSON 文字。Composite 仍是同 endpoint 的单次 batch，不引入条件、循环、结果绑定或脚本文本。
- `runtime.inspect` 固定为只读 Server 健康检查并纳入 catalog 回归，不得启用开关、恢复断点或发送 shutdown。
- Release Host 拒绝 Debug-only `reflection.invoke` 时应返回明确 capability code，避免被误判为网络或配置故障。

## 1.0.17 发布验收门槛

- [x] 最小 Startup 模板按替换清单复制后，Debug/Release 均可编译。
- [x] 最小模板业务注册区为空时可启动，无需预先创建 Worker/RTask/Reflection 类型。
- [x] `host.info`、`lifecycle.status` 在模板宿主启动后可用。
- [x] descriptive 与 managed execution 在模型、快照、CLI 输出和文档中可区分。
- [x] 长等待 Worker 的 Wakeup 立即继续且不退出，Stop 正常退出。
- [ ] SCM Stop、系统 Shutdown、CLI shutdown 共用同一幂等协调任务。
- [x] RProcess/RThread/RTask 仅在真实完成后反登记。
- [x] 重复 Start/Activate 与不同 provider 激活均有明确测试结果，无静默上下文覆盖。
- [x] Start 参数冲突抛出 `RuntimeHostConfigurationException`，且启动前异常与启动后 CLI Frame 错误边界清楚。
- [x] 全部内置 CLI 命令具有摘要，命令级帮助覆盖参数、风险、示例和构建能力。
- [x] Catalog 新旧解析器双向兼容通过，或完成明确的 schema 升级与版本错误验证。
- [x] `--user-config` 在 CLI 帮助、用户手册、速查手册和技能中一致。
- [x] `runtime.inspect` 单连接 batch、逐步结果和只读边界通过回归。
- [x] Composite 汇总保留完整 Frame 和单 JSON 输出边界。
- [x] Debug/Release Diagnostics 变体和 Release capability 拒绝分别验证。
- [x] 安装版 SampleHost 使用安装 DLL 完成真实 CLI 验证。
- [x] SampleHost 无源码 ProjectReference/手工 DLL，输出副本哈希与安装目录对应 DLL 一致。
- [x] 文档、catalog、JSON、模板、技能和 MSI 内容及版本一致。
- [ ] P0、P1 全部通过后才允许生成并发布 1.0.17 全量安装包。

# 2026-07-13 CLI 缺省用户配置与上下文 Shell

- 系统路由配置统一命名为 `RuntimeCliSystemConfig.json`，系统帮助元数据统一命名为 `RuntimeCliSystemMetadata.json`，用户增量配置统一命名为 `RuntimeCliUserConfig.json`。
- 未指定 `--user-config` 时，CLI 自动加载当前工作目录的 `RuntimeCliUserConfig.json`；显式 `--user-config` 优先且取代缺省用户文件。
- 单次模式保持现有执行一次后退出的行为；新增 `iwrt shell` 和 `iwrt --interactive` 上下文模式。
- Shell 的 `exit`、`quit` 和 EOF 只退出本地 CLI 进程，不发送 Runtime Frame，不停止被控制设备；宿主停止必须显式执行 `lifecycle.shutdown`。
- Shell 上下文仅存在于当前 CLI 进程，包括当前 Runtime 虚拟路径、显式变量、命令级非敏感参数记忆和输入历史；退出后不持久化。
- 显式变量使用 `set/unset/vars` 管理，通过 `$name` 或 `${name}` 引用；未定义变量不得发送管道请求。
- 统一虚拟路径覆盖 host、lifecycle、registry、execution、switchboard、pipes、files 和 reflection；通过 `pwd/cd/ls/get/root` 探索，并最终映射为现有 catalog 规范命令。
- destructive 命令参数、敏感名称参数和 JSON 对象/数组不得进入隐含记忆。
- 用户 alias 和 composites 继续来自用户配置，不得覆盖 Shell 保留命令，也不得扩张为 workflow 或脚本引擎。
- 单次远程命令继续保持单 JSON Frame；Shell 每个远程命令仍各自输出一个完整 Frame，本地提示和上下文命令不属于协议输出。
- 详细设计和验收标准见 `docs/superpowers/specs/2026-07-13-cli-context-shell-design.md`。

# 2026-07-13 DDNS Snap 四宿主联调意见

- 根据 DDNS Snap 的 Service、Agent、Service UI、Agent UI 四宿主真实联调结果，形成一份 Runtime/CLI 改进意见书。
- 意见书必须区分已验证事实、待确认推断、建议整改和验收标准，不直接把宿主现象写成 Runtime 根因。
- 优先覆盖 Agent 诊断端点持续返回 `CLI_CANCELLED`、单宿主退出影响其他宿主控制面、停止期间状态查询能力、生命周期残留隔离、CLI 大响应以及 v2 技能与 v3 实现漂移。
- Root、runtime.inspect、switchboard 等树状数据必须支持分层回送：当前层只返回本层字段、直属子节点目录、计数和继续读取标识，不得默认递归序列化整棵树；调用方显式指定路径、深度或分页后才展开下一层。
- 本任务只提交问题、复现步骤和建议，不修改 Runtime/CLI 实现源码。
- 正式意见书存放于 `docs/DDNS_Snap四宿主运行时联调改进意见书.md`。

# 2026-07-13 CLI_CANCELLED 错误分析

- 单独形成 `CLI_CANCELLED错误分析报告.md`，记录 DDNS Agent 管道名漂移导致的错误诊断、CLI 取消语义合并问题及完整证据链。
- 报告必须明确区分已证实根因、CLI 次生缺陷、被推翻的假设和仍待使用正确管道验证的安全退出行为。
- 报告需提供源码定位、时序、复现命令、修复建议、错误码设计、测试矩阵、文档/技能同步清单和验收标准，帮助 Runtime/CLI 组直接实施。
- 本任务仅生成分析报告，不修改 Runtime/CLI 或 DDNS Snap 业务源码。

# 2026-07-13 WebView2 同步发布要求

- `Iwesun.Runtime.WebView2` 必须作为 Runtime 全量安装包的固定组成部分，每次统一发布均从当前源码重新构建，不得复用旧 staging 或本地副本。
- WebView2 DLL 的 `AssemblyVersion/FileVersion/InformationalVersion` 必须由统一打包入口注入，并与同一安装包中的 Runtime DLL 发布版本一致。
- 安装目录必须同时包含 WebView2 DLL、`WEB_RUNTIME_CONTROL.md`、`WEBVIEW2_RUNTIME_CAPABILITIES.md` 和 `WEBVIEW2_JSON_PIPE_CLI_PLAN.md`，使用户能够确认当前接口和能力状态。
- 安装验证脚本必须校验 WebView2 DLL 存在、版本同步及三份正式文档完整；任一项失败则禁止生成正式 MSI。

# 2026-07-13 CLI 多宿主止血实施

- [x] 不存在的管道返回 `CLI_CONNECT_TIMEOUT`，错误包含 pipe、phase、timeoutMs、elapsedMs 和 retryable。
- [x] 本地取消与连接/请求超时分离，只有本地取消使用退出码 130。
- [x] Shell 支持 `target add/list/use/current/remove` 和 `@name command`，`exit/quit` 仍只退出 Shell。
- [x] 用户配置支持 targets，Agent 示例使用 `DdnsSnap.Agent.Server.RuntimeDiagnostics`。
- [x] 新增轻量 `host.summary`，标准 `runtime.inspect` 不再默认展开程序集类型。
- [x] process/thread/task/reflection/pipe/file/output-point 等高频目录采用默认 100、最大 500 的分页结果。
- [x] Switchboard 全局、Section、输入、管道、文件和 FIFO 修改返回轻量 Mutation，不再返回完整快照。
- [x] Switchboard 查询区分 configured/effective 状态，并提供 SnapshotVersion/GeneratedAt。
- [x] Host 快照提供 InstanceId、ProcessStartTimeUtc 和实际诊断管道。
- [x] 四宿主意见书已撤回由旧 Agent 管道产生的控制面失效结论。
- [x] 下一阶段有界查询、服务端预算、业务适配器和 WebView2 共同模型已写入独立设计。
- [x] Debug/Release 构建及 CLI context、transport、SampleHost 完整场景通过。
- [x] 止血实现阶段未提前生成 MSI；完成发布前审计、修复 4 个阻断问题并回归后，统一生成 1.0.19 全量 MSI。

## 1.0.19 发布审计结果

- [x] 修复 Switchboard set/output-point 仍返回完整快照的问题。
- [x] 修复 SetInput 错写 PipeOutputEnabled 的状态语义。
- [x] 多目标 Shell 对未明确目标的 destructive 命令返回 CLI_TARGET_REQUIRED。
- [x] 目标别名进入 CLI 传输错误上下文。
- [x] CLI context、transport、数值断点、跨进程断点和 SampleHost 完整场景通过。
- [x] Debug/Release 全解决方案构建通过。
- [x] WebView2 与 Diagnostics 文件版本同步为 1.0.19.0，三份 WebView2 文档齐全。
- [x] 安装版 SampleHost Debug/Release 引用边界和 DLL 哈希验证通过。

# 2026-07-13 CLI Shell 与远程节点统一改造

- 采用 `Node -> Target -> ResolvedRuntimeTarget` 分层，公共名称使用 `node`，不与远端 Runtime `host.*` 命令域冲突。
- 单次 CLI、Shell、虚拟路径、alias 和 composite 共用一个上下文与目标解析器。
- 所有允许展开的 token 在本地分派和目标解析前统一展开；未定义变量不得发送请求。
- 虚拟路径和普通远程命令必须继承同一个当前 Target；`@target` 仅覆盖当前命令。
- 远程 Named Pipe 必须分别传入 `serverName` 与纯 `pipeName`，禁止用 UNC 代替管道名。
- CLI 不保存密码、不实现密码协议、不自动注销 Windows IPC 会话；账户和组部署归安装器或管理员脚本。
- 单 Target composite 保持不变；跨节点协调使用独立的只读 `MultiTargetCoordinator`。
- 分阶段实施：Shell/远程只读、Windows 认证与 ACL、多节点只读协调。
- 权威设计为 `docs/superpowers/specs/2026-07-13-runtime-cli-shell-remote-node-design.md`。

## 实施状态

- [x] Shell 变量在本地分派、路径和 `@target` 解析前统一展开。
- [x] 虚拟路径与普通命令共享当前 Target，一次性 `@target` 不污染当前值。
- [x] 配置支持 `nodes` 与 `targets[].node`，旧 Target 缺省绑定 `local`。
- [x] 单次模式支持 `--target` 和 `--server/--pipe`，传输层分别传入服务器和管道。
- [x] 远程错误包含稳定代码、Windows 错误码和非敏感目标上下文。
- [x] Node/Target Shell 登记、查看、测试、认证和显式注销入口已接入。
- [x] Runtime Operators ACL 规则已进入统一管道安全工厂；Authenticated Users 兼容规则暂时保留。
- [x] 大于 1 MiB 的完整 Frame 收发通过功能测试。
- [x] `multi.query` 提供有界并发只读多 Target 查询，并在连接前拒绝非只读命令。
- [ ] Atlas 的 NetworkService、Runtime Operators 账户、Service/UI 四管道 ACL 和大 Frame 仍需真实远程环境验收。

## 1.0.21 发布状态

- [x] Shell 上下文、远程 Node/Target、稳定错误码、Windows IPC 辅助和多 Target 只读协调完成本地回归。
- [x] Atlas UI 完成远程 Frame 往返，Atlas Service 的拒绝访问准确分类为 `CLI_REMOTE_ACCESS_DENIED`。
- [x] 版本统一升级到 1.0.21，进入 Debug/Release、全量 staging、内容验证和 MSI 构建。

## 远程 AI 账号与管道 ACL 源码声明

- 远端宿主必须在 `Program.cs` 的 Runtime 启动调用中固定声明允许访问主管道的 Windows 账号或组；该声明随业务程序编译、发布并在管道创建前生效。
- Runtime 提供统一的管道访问策略参数，普通程序、Windows Service 和配置式 Windows Service 使用同一策略，不允许各宿主自行创建管道 ACL。
- 严格部署必须能够关闭 `Authenticated Users` 兼容授权，只向 SYSTEM、NetworkService、Administrators 和业务声明的账号/组授权。
- 已声明的 Windows principal 无法解析时必须使宿主启动失败并给出配置错误，禁止静默跳过后形成不可访问管道。
- Windows 用户或域账号必须预先存在；Runtime 不创建操作系统账号，也不把密码、令牌或可逆凭据编译进业务程序。
- 远端 CLI 使用对应 AI 账号凭据建立 Windows IPC 会话；服务端源码只负责身份授权，客户端只负责凭据登录。
- SampleHost、主程序模板、CLI 手册、Server 手册和集成技能必须提供可照搬的严格授权示例。

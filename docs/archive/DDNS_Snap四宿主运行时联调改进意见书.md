# DDNS Snap 四宿主运行时联调改进意见书

> 日期：2026-07-13  
> 状态：提交 Runtime/CLI 组排查  
> 范围：Iwesun.Runtime.Diagnostics、Iwesun.Runtime.Cli、运行时接入技能与发布文档

## 一、结论摘要

DDNS Snap 的 Service、Agent、Service UI、Agent UI 已分别使用独立诊断管道接入 Runtime CLI v3。真实联调证明四个管道均可建立连接，Service 与两个 UI 可以执行状态查询和协调退出；Agent 的业务心跳也持续正常。

原 Agent `CLI_CANCELLED` 已确认由旧管道名触发连接超时，并非 Agent 控制面失效。CLI 又把连接超时统一归类为取消，放大了误判。跨宿主退出耦合目前没有本次故障证据支持，只保留为独立联调课题。

建议把“单宿主控制面隔离”和“退出期间控制面仍可查询”列为下一版 Runtime 的发布阻断项。

本轮需要集中处理的七项问题：

1. Agent 的控制端点会持续返回 `CLI_CANCELLED`，导致独立查询和安全退出不可靠。
2. `runtime.inspect`、Root 快照和开关响应有时过大，容易在 CLI 或调用端截断重要字段。
3. 已安装技能仍有部分 CLI v2 描述，与当前 CLI v3 的命令和宿主能力不一致。
4. 大型对象树需要路径选择、服务器端过滤、当前层摘要、分层展开和稳定分页，不能在查询当前层时一次返回整棵树。
5. 四宿主同时运行时，CLI 缺少目标目录、当前目标和快速切换命令；反复输入完整 `--pipe` 容易误操作宿主。
6. 大响应不是单纯的终端显示问题：当前服务端会先构造并序列化完整结果，再由客户端检查响应长度，无法避免服务端内存、CPU 和管道占用。
7. 联调中出现过状态字段看似不一致的样本，但尚未证明是 Runtime 状态损坏、非原子快照，还是宿主业务状态写入问题；必须保留原始 Frame 和同周期复测证据后再归因。

## 二、联调环境

四个 Debug 宿主及诊断管道如下：

| 宿主 | 诊断管道 |
|---|---|
| DdnsSnap.Service | `DdnsSnap.Service.RuntimeDiagnostics` |
| DdnsSnap.Agent | `DdnsSnap.Agent.Server.RuntimeDiagnostics` |
| DdnsSnap.UI | `DdnsSnap.UI.RuntimeDiagnostics` |
| DdnsSnap.Agent.UI | `DdnsSnap.Agent.UI.RuntimeDiagnostics` |

客户端为安装版 Iwesun Runtime CLI v3，命令入口为 `iwrt.exe`。联调使用的主要命令：

- `runtime.inspect`
- `lifecycle.status`
- `lifecycle.shutdown`
- `reflection.list`
- `reflection.invoke`
- `switchboard.disable`

## 三、已验证事实

### 3.1 四个诊断管道可以独立连接

四个宿主均曾成功响应 CLI 请求，返回各自的进程、生命周期、线程、任务和反射登记信息。管道命名本身没有发生冲突。

### 3.2 Service 与两个 UI 的生命周期命令正常

以下三个宿主能够正常响应 `lifecycle.status`，并能在收到 `lifecycle.shutdown` 后返回成功响应：

- DdnsSnap.Service
- DdnsSnap.UI
- DdnsSnap.Agent.UI

响应明确包含 `Coordinated shutdown requested.`，随后进程正常结束。

### 3.3 原 Agent CLI_CANCELLED 结论已纠偏

当时命令连接的是已废止的 `DdnsSnap.Agent.RuntimeDiagnostics`，而当前 Agent 实际监听 `DdnsSnap.Agent.Server.RuntimeDiagnostics`。不存在的旧管道在 5000ms 后触发连接超时，CLI 将其折叠为：

```json
{
  "schema": "iwesun.runtime.cli.result/1.0",
  "ok": false,
  "code": "CLI_CANCELLED",
  "message": "Operation cancelled."
}
```

已复现的命令包括：

- `lifecycle.status`
- `lifecycle.shutdown`
- `switchboard.disable`

使用正确管道后 `host.info` 和 `lifecycle.status` 已成功。本次现象不能作为 Agent Monitor 失效、生命周期分裂或跨宿主停止耦合的证据。

### 3.4 Agent 安全退出需要按正确管道重新验收

旧端点上的退出命令从未到达 Agent，因此不能据此判断安全退出失败。后续必须使用正确管道独立验证，并保留响应 Frame 和进程正常退出码。

### 3.5 CLI v3 与已安装技能存在版本漂移

当前 `iwrt --help` 显示的是 CLI v3 命令，例如：

- `lifecycle.shutdown`
- `reflection.get`
- `reflection.invoke`
- `switchboard.point.enable`
- `runtime.inspect`

但已安装的 DDNS Snap 多进程调试技能仍以 CLI v2 为主，并声称 UI 尚未完整托管、槽位和用户配置能力不可用。真实联调中两个 UI 均已具备 v3 诊断端点。技能内容会误导调试步骤，应随发布版同步更新。

### 3.6 部分成功响应过大

`runtime.inspect` 的 `host.info` 会展开大量程序集类型信息，单次响应达到数万字符。`reflection.invoke service.root-snapshot GetSnapshot` 可超过八万字符。`switchboard.disable` 虽是一个简单状态修改命令，也会返回完整开关、语句和输出点快照，CLI 随即提示输出被截断。

这会增加管道占用、JSON 解析和 AI 调试上下文成本，并掩盖真正需要检查的字段。

### 3.7 CLI 尚无四宿主目标上下文

当前 CLI 能用 `--pipe=<PIPE>` 精确连接四条独立管道，进入 Shell 时也会保留启动参数中的管道；但 Shell 没有目标登记表、当前目标和运行时切换命令。调试者若在四个宿主之间往返，只能退出后重新指定管道，或为完整命令自行维护外部脚本。

因此“管道可独立访问”不等于“多目标操作已经可用”。缺失的是 CLI 本地控制上下文，不应通过让四个宿主共享一条主管道解决，否则会重新引入目标歧义和生命周期耦合。

### 3.8 服务端没有响应预算

当前 Monitor 对请求帧设置了 1 MiB 上限，但回复路径会先调用目标 `Snapshot()`、构造完整对象、完整 JSON 序列化，再一次性写入管道。CLI 的 `maxResponseBytes` 只在读到四字节响应长度后执行校验；它可以保护客户端分配，却不能阻止服务端已经发生的大对象遍历、内存分配、序列化和管道独占。

因此仅给 CLI 增加截断显示、`head` 或更大的 `maxResponseBytes` 不能解决根因。分页、字段选择、深度和字节预算必须在 Runtime 目标执行阶段生效。

### 3.9 状态不一致样本尚未完成归因

联调中观察到过顶层状态与子状态看似不一致的返回。目前至少存在三种可能：

1. Runtime 返回的是不同语义的字段，例如全局门控关闭而局部配置仍保持启用；
2. 宿主业务在一次更新过程中被非原子读取；
3. 宿主异常路径没有同步收尾顶层与阶段状态。

在取得同一请求的完整 Frame、`requestId`、宿主 `InstanceId/epoch`、生成时刻和连续复测结果前，本意见书不把该样本认定为 Runtime 或宿主的确定缺陷。

## 四、尚待 Runtime 组确认的推断

以下内容是根据外部行为形成的排查方向，不作为已经证实的源码根因：

1. Agent 的诊断请求 CancellationToken 可能错误绑定了全局 Stop、共享退出截止时间或旧退出周期，而不是只绑定当前请求和当前宿主生命周期。
2. 不同宿主虽然使用独立诊断管道，但可能共享了未按宿主身份隔离的命名同步对象、共享内存状态或全局退出 epoch。
3. 发起 `lifecycle.shutdown` 后，诊断 Monitor 可能过早取消自身接收/响应任务，导致成功响应尚未发送或后续只读查询无法完成。
4. 新启动宿主可能读取到上一退出周期的残留 Stop/Deadline，而缺少进程所有者、启动时间或 epoch 校验。
5. CLI 将多种取消来源统一折叠成 `CLI_CANCELLED`，使调用方无法区分本地超时、用户取消、目标停止、管道关闭和服务端请求取消。

## 五、必须整改建议

### P0-1：生命周期状态按宿主实例隔离

每个 Runtime Host 应拥有稳定的实例身份，至少包含：

- ProcessId
- ProcessStartTime 或随机 InstanceId
- Resolved diagnostics pipe
- Lifecycle epoch

Stop、Deadline、Cleanup、Wakeup 和请求取消必须验证宿主实例身份。不同进程、不同管道或不同 epoch 的状态不得互相触发。

### P0-2：请求取消与宿主退出解耦

CLI 请求应分别处理：

- 客户端取消
- 命令超时
- 管道断开
- 当前宿主停止
- Runtime 全局广播停止

只读命令 `lifecycle.status` 在宿主 Draining 阶段仍应可用。不能因为宿主收到 Stop 就把所有新请求直接映射成客户端 `CLI_CANCELLED`。

### P0-3：先回复 shutdown，再停止 Monitor

`lifecycle.shutdown` 的推荐顺序：

1. 接收并登记幂等退出请求。
2. 生成并发送结构化成功响应。
3. 确认响应帧写入完成。
4. 启动业务清理和登记表排空。
5. 保留最小生命周期查询通道直至退出或截止时间到达。
6. 最后关闭诊断 Monitor 和管道。

### P0-4：明确单宿主退出与广播退出

默认 `lifecycle.shutdown` 只能停止命令所连接的宿主。若确实需要跨宿主广播，应提供名称明确、权限更高的独立命令，并在响应中列出目标宿主和逐项结果。

### P0-5：结构化区分取消原因

建议至少提供以下错误码：

- `CLI_LOCAL_CANCELLED`
- `CLI_TIMEOUT`
- `PIPE_DISCONNECTED`
- `HOST_STOPPING`
- `HOST_INSTANCE_CHANGED`
- `REQUEST_CANCELLED_BY_HOST`

返回中应保留 `requestId`、`correlationId`、目标管道和 `retryable`，避免使用不带 Frame 上下文的本地结果替代服务端响应。

## 六、建议增强

### P1-0：增加 CLI 多目标上下文

CLI Shell 应提供本地目标目录，不改变现有一宿主一管道原则：

- `target add <name> <pipe>`：登记本次 Shell 使用的目标；
- `target list`：列出名称、管道和可选的最近一次身份探测结果；
- `target use <name>`：切换当前目标；
- `target current`：显示当前目标、解析后的管道和最近确认的宿主身份；
- `target remove <name>`：删除本地登记；
- 单条命令允许 `@name <command>` 临时选择目标，但不改变当前目标。

`RuntimeCliUserConfig.json` 可声明静态目标别名；Shell 中的临时目标只存在于当前 CLI 进程。每个响应必须回显目标别名、实际管道、ProcessId 和 InstanceId，破坏性命令执行前不得只凭别名确认目标。

四宿主推荐名称为 `service`、`agent`、`service-ui`、`agent-ui`。禁止用“自动选择第一个可连接管道”作为缺省策略。

### P1-1：为大对象提供摘要和分页

- `host.info` 默认只返回宿主身份、版本、程序集名称和类型数量；详细类型清单使用显式参数或分页命令。
- `runtime.inspect` 保持轻量健康检查，不默认展开所有程序集类型。
- Root、执行登记表、Switchboard 和反射对象采用分层回送协议。读取当前层时，只返回本层标量字段、直属子节点名称/类型、元素计数、是否还有下层以及继续读取所需路径，不得自动递归序列化整棵对象树。
- 调用方只有显式提供 `path`、`depth`、`offset`、`limit` 或字段选择后，服务端才展开指定下一层；默认 `depth` 应为 0 或 1，并设置服务端最大深度与最大响应字节数。
- `reflection.get`/`reflection.invoke` 对大快照支持路径、深度、分页、字段选择和服务器端摘要。完整快照必须是显式操作，不能由普通当前层查询隐式触发。
- 分页和分层响应应返回稳定的节点路径、总数、本页范围、截断标志和继续令牌，便于 CLI、人工和 AI 逐层追踪同一对象。
- 状态修改命令默认只返回变更项和最终有效状态。
- 响应预算必须进入目标查询接口，例如通过统一的 `RuntimeQueryOptions` 传递 `path/depth/offset/limit/fields/maxBytes/continuationToken`；目标在枚举和投影期间停止，不得先生成完整快照再裁剪。
- 普通修改命令使用轻量 `MutationResult`，只返回变更对象、旧值、新值、有效值和版本；需要完整快照时由调用方另发查询。
- 对无法分页的业务方法返回值，Runtime 应在调用前要求业务提供摘要/分页适配器，或明确拒绝超过预算的“完整快照”能力，不能依赖序列化后截断出一个无效 JSON。

建议的分层读取形态：

```text
root
  fields: generatedAt, publishVersion
  children: configuration, status, device, secrets

root.device
  children: history, discovery, registration, subdomains

root.device.subdomains.published.machines
  count: 4
  page: 0..3
```

上述查询不应在读取 `root` 时同时携带所有 Machine、MAC、IP、DomainStatus 和历史叶子。

### P1-2：区分配置状态与有效状态

`switchboard.disable` 后观察到 `GlobalEnabled=false`，但部分 Section 仍显示 `Enabled=true`。建议响应同时提供：

- ConfiguredEnabled
- GlobalEnabled
- EffectiveEnabled

这样调用方无需推断全局门控与局部开关的组合结果。

该项目前属于字段语义缺口，不等同于已证明的状态损坏。后续状态一致性复现必须使用单次锁定或版本化快照，并在响应中返回 `SnapshotVersion`、`GeneratedAt` 和宿主 `InstanceId/epoch`；若顶层和阶段表来自宿主业务对象，还应由宿主提供一次性快照委托，Runtime 不应分别反射读取多个会变化的属性。

### P1-3：发布时同步技能与 CLI 元数据

Runtime MSI 的发布验收应增加：

1. `iwrt --help` 命令集与安装技能中的命令一致。
2. 技能声明的宿主能力与 SampleHost/真实宿主验证一致。
3. v2 示例不得进入只发布 v3 CLI 的安装包。
4. 文档、技能和 CLI metadata 使用同一版本号或生成标识。

## 七、建议复现步骤

1. 启动四个使用独立诊断管道的 Debug Host。
2. 分别执行 `iwrt --pipe=<PIPE> lifecycle.status`，确认四个端点均成功。
3. 仅向 Service 执行 `lifecycle.shutdown`。
4. 在 Service 退出期间及退出后，分别查询 Agent 和两个 UI 的 `lifecycle.status`。
5. 单独向 Agent 执行 `lifecycle.shutdown`。
6. 观察 Agent 是否先返回成功响应、是否正常退出，以及端点是否错误返回 `CLI_CANCELLED`。
7. 完全退出四个宿主后立即重启 Agent，再次执行 `lifecycle.status`，检查旧 epoch 是否影响新进程。

## 八、验收标准

### 生命周期隔离

- Service、Agent、两个 UI 同时运行时，停止任一宿主不会取消其他三个宿主的 CLI 请求。
- 单宿主 `lifecycle.shutdown` 只停止目标宿主。
- 四宿主并发 shutdown 时，每个端点均返回自己的结构化结果。

### Agent 安全退出

- Agent 正常运行时，`lifecycle.status` 连续调用 100 次均成功。
- Agent 收到 shutdown 后先返回成功响应，再从正常出口退出，退出码为 0。
- Agent 的清理委托、任务反登记和管道释放均完成。
- 不需要 `Stop-Process` 或 `Process.Kill`。

### 残留状态

- 上一进程异常结束后，新进程不会继承旧 Stop、Deadline、Cancellation 或 Cleanup 状态。
- 旧进程残留对象即使未及时清理，也因 InstanceId/epoch 不匹配而被忽略。

### CLI 结果

- 每种取消来源具有可区分的错误码。
- `runtime.inspect` 默认响应保持为轻量摘要。
- 简单开关命令不会返回完整数万字符快照。
- CLI 不因正常大响应静默截断关键状态。
- 查询 Root 或其他树状对象的当前层时，响应不得包含孙级及更深层数据；逐层指定路径后才能读取下一层。
- 分层响应必须提供直属子节点目录、元素总数、截断状态和继续读取参数；同一页重复读取的顺序应稳定。
- 服务端达到最大响应字节数时必须返回结构化 `truncated/continuation` 信息，不得先生成完整大树再由 CLI 静默截断输出。
- Shell 可登记四宿主目标并用 `target use` 或 `@name` 快速选择；每个结果回显实际宿主身份，`exit/quit` 仍只退出 Shell。
- 大数据验收必须同时测量目标枚举数、服务端序列化前分配、响应字节数和管道占用时间；仅验证 CLI 没有打印全部内容不算通过。
- 对状态不一致样本，在无法稳定复现并定位前标记为 `Unconfirmed`，不得把一次瞬时读取直接写成 Runtime 或宿主已确认缺陷。

### 发布一致性

- 安装版 CLI、文档、技能和 SampleHost 使用同一 v3 命令体系。
- 安装技能不再声明已经实现能力为“未实现”，也不再推荐已废止命令。

## 九、责任边界

本意见书只记录 Runtime/CLI 公共控制面的现象和建议。DDNS Snap 继续负责：

- 为四个宿主提供独立固定管道名。
- 使用 Runtime 标准 Host 接入和协调退出 API。
- 只登记必要的反射白名单。
- 调试结束后恢复全局静默。

Runtime/CLI 负责：

- 多宿主生命周期与取消状态隔离。
- 退出期间诊断控制面的可靠响应。
- CLI Frame、错误码、超时与取消语义。
- 安装版 CLI、文档和技能的一致发布。

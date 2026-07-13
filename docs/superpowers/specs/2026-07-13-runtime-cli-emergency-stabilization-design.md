# Runtime CLI 多宿主止血与大数据架构规划

> 日期：2026-07-13  
> 边界：本轮只实施止血项；大数据查询架构只形成下一阶段设计，不进入本轮代码，也不发布 MSI。

## 目标

1. 四宿主可明确、快速选择，不再反复手写管道名。
2. 管道不存在、连接超时、请求超时和本地取消返回不同错误。
3. 常用 list 具有安全默认上限、稳定排序和分页摘要。
4. Switchboard 修改命令不再附带完整大快照。
5. 状态响应携带生成时间、快照版本和宿主身份。
6. 修正旧 Agent 管道名以及由此产生的错误结论。
7. 下一阶段再统一设计服务端路径查询、响应预算和大对象分页。

## 不做

- 不升级 rtdiag/2.0 或 rtdiag/3.0 schema。
- 不实现流式 JSON、多帧响应、continuation token 或任意业务对象递归裁剪。
- 不自动猜测相似管道，不自动选择第一个可连接宿主。
- 不把一次状态不一致样本定性为 Runtime 或业务故障。

## 多宿主目标上下文

Shell 增加 target add/list/use/current/remove；“@name command”临时选择目标且不改变当前目标。RuntimeCliUserConfig.json 可以声明 targets。优先级固定为：命令显式 --pipe、@name、Shell 当前目标、命令 endpoint。破坏性命令在多目标环境中不得隐式选择目标。

目标别名只是客户端选择器。结果必须关联目标别名、实际管道和服务端进程身份。系统 catalog 不内置 DDNS Snap 私有宿主。

## 传输错误

传输阶段划分 connect、write、read-header、read-body：

| 条件 | Code | Exit |
|---|---|---:|
| 外部 token/Ctrl+C | CLI_LOCAL_CANCELLED | 130 |
| 连接期限到达 | CLI_CONNECT_TIMEOUT | 4 |
| 写请求期限到达 | CLI_WRITE_TIMEOUT | 4 |
| 等待或读取响应超时 | CLI_RESPONSE_TIMEOUT | 4 |
| 响应中途断开 | CLI_PROTOCOL_TRUNCATED | 5 |
| 其他管道 I/O | CLI_TRANSPORT_FAILURE | 4 |

错误数据包含 endpoint、pipeName、targetAlias、phase、timeoutMs、elapsedMs、retryable，不包含用户配置全文或敏感参数。本次 CLI_CANCELLED 已确认由旧管道 DdnsSnap.Agent.RuntimeDiagnostics 触发连接超时；正确名称是 DdnsSnap.Agent.Server.RuntimeDiagnostics。

## 有限列表

host.events、switchboard.point.list、breakpoint.list、hook.list、registry.list、process.list、thread.list、task.list、pipe.list、file.list、reflection.list 增加 offset/limit。默认 limit=100，硬上限 500。服务端稳定排序后分页，返回 items、total、offset、limit、returned、hasMore。

reflection.get、reflection.invoke 和业务大快照本轮不做伪分页，进入下一阶段架构。

新增轻量 host.summary，只返回宿主身份、Runtime/产品版本、程序集名称与数量、登记目标数量，不展开程序集类型清单。标准 runtime.inspect 改用 host.summary；原 host.info 保留为显式详细兼容入口。这样先消除日常健康检查的大响应，但不假装已经解决任意对象的大快照。

## 轻量修改与状态取证

Switchboard 修改命令返回 RuntimeMutationResult：TargetId、Action、Subject、PreviousValue、RequestedValue、EffectiveValue、Changed、SnapshotVersion、GeneratedAt，不再调用完整 Snapshot。

查询响应区分 ConfiguredEnabled、GlobalEnabled、EffectiveEnabled，并增加 GeneratedAt、SnapshotVersion、InstanceId、ProcessId、ProcessStartTimeUtc 和实际诊断管道。本轮只增强取证，不修改共享内存 epoch 或业务状态机。

## 资料纠偏

同步 CLI_CANCELLED 报告、四宿主意见书、无配置文件方案、CLI 手册/速查、JSON 示例、仓库技能和安装技能。撤回“本次错误证明 Agent 控制面失效或跨宿主退出耦合”的结论，将其保留为独立待测课题。

## 下一阶段架构

单独设计 RuntimeQueryOptions(path, depth, offset, limit, fields, maxBytes, continuationToken)、枚举期预算、业务摘要/分页适配器、稳定 continuation token、原子快照委托，以及单帧分页/多帧/流式协议取舍。Diagnostics、WebView2 和代理命令最终采用同一模型。

## 验收

- 一个 Shell 内切换四目标，@name 不改变当前目标。
- 错管道返回 CLI_CONNECT_TIMEOUT，只有外部取消返回 130。
- list 默认最多 100、硬上限 500，且有分页摘要。
- runtime.inspect 使用 host.summary，不默认展开程序集类型。
- Switchboard 修改响应不包含 Statements/OutputPoints。
- 状态可识别宿主实例、实际管道和快照版本。
- Agent 管道名在源码、配置、文档、技能中一致。
- Debug/Release 和相关功能场景通过；本轮不打包。

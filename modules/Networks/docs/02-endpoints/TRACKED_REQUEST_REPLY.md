# 请求—响应跟踪与可靠重试基类

> **状态**: CURRENT | **最后更新**: 2026-07-22
>
> **源码参考**: `modules/Networks/src/Iwesun.Runtime.Networks/TrackedRequestReplyEndpointBase.cs`
>
> **上游**: [ENDPOINT_BASE.md](ENDPOINT_BASE.md)
>
> **下游**: [TCP_UDP_PING.md](TCP_UDP_PING.md)、HTTP/DoH适配器

调用方如果需要在DNS/DoH访问中保留原服务域名，同时指定底层目标IP、网卡或代理，请直接阅读
[DoH精确底层路由使用指南](DOH_PRECISION_ROUTING_GUIDE.md)，其中包含完整接口表和可运行示例。

## 一、目的

本设计是Networks 3.0唯一正式的请求—响应跟踪基类，自身完整实现FIFO、容量背压、Pending、重试和四级GUID生命周期。
所有具有明确请求—响应关系的端点必须采用本基类；不得再新增并行旧版端点或兼容别名。

`TrackedRequestReplyEndpointBase<TRequest,TResponse,TKey>`为具有明确请求—响应关系的网络功能统一提供：

- 原请求与响应的关联；
- 发送后等待确认的Pending表；
- 超时和错误后的自动重试；
- 每条请求最多执行三次网络尝试；
- 三次尝试可分别配置超时预算，并报告每次实际耗时；
- 成功完成、重试和最终失败事件；
- 迟到或重复响应抑制；
- 跟随请求传递的非空精确访问计划；
- 批量发送、批量接收；允许具备请求级隔离能力的端点真实并发启动整批网络操作。

新的UDP广播、持续监听等不存在一一响应关系的功能应使用独立的多响应或持续接收合同；本请求—响应基类不强行覆盖它们。

## 一点一、统一原则

- HTTP、DoH、TCP、Ping、精确UDP、PTR和NBNS只允许无版本后缀的`Network*Endpoint<TKey>`公共端点；
- 不提供旧端点别名、包装器或兼容程序集；残留引用必须通过编译错误强制升级；
- 3.0协议请求强制携带非空`RequestedAccessPlan`；跟踪基类不保存、解释或兼容旧Route字段；
- PowerShell、Process和本地快照属于非重复特殊模型，可继续使用`NetworkAsyncEndpointBase`，但不得复制已存在的协议能力。

## 二、三队列模型

```text
发送FIFO ──> Pending等待表 ──> 接收FIFO
   ^              |
   |              +── 超时/错误 ──> 重试FIFO（复用发送FIFO）
   |              |
   |              +── 超过重试上限 ──> RequestFailed
   +──────────────+
```

`SendCompleted`只表示一次网络尝试已提交到底层，不表示请求闭环完成。只有收到并验证匹配响应后，
才触发`RequestAcknowledged`。调用方通过该事件同时取得原请求和响应，从而把结果转发给原始调用者。

发送FIFO、Pending等待表和接收FIFO分别具有独立容量。`MaxPendingCount`对已经离开发送FIFO但尚未收到确认的
请求实施背压，防止上游长期无响应时Pending表无限增长。

### 二点一、批量启动语义

`Send(ReadOnlySpan<TRequest>)`仍负责整批登记Pending并进入发送FIFO。后台发送线程保持唯一出队者，但端点可以通过
`StartAttemptsConcurrently`声明其访问计划解析和执行状态已按Request隔离。启用后，出队者把同一批次的尝试立即分派到
线程池，各请求独立规划并启动I/O，不能等待前一请求的网络闭环后才处理后一请求。

`NetworkPingEndpoint<TKey>`、`NetworkDnsReverseLookupEndpoint<TKey>`、`NetworkNetBiosNameEndpoint<TKey>`、
`NetworkUdpDatagramEndpoint<TKey>`、`NetworkTcpConnectEndpoint<TKey>`、`NetworkHttpGetEndpoint<TKey>`和
`NetworkDohEndpoint<TKey>`均启用该能力。这些端点的请求状态、解析结果、Socket执行和终态均按Request隔离；
HTTP/DoH连接池和UDP数据面本身使用并发安全的池实现。Windows访问计划解析同时使用请求级不可变接口目录与路由快照，
不能依赖共享Provider中最后一次写入的“当前快照”，因此多网卡环境下并发解析不会把A请求解析成B请求的接口或路由。
同一Windows解析器在一秒内复用一次只读网卡目录采样，但每个目标仍单独执行
`GetBestRoute2`并形成独立路由快照；网卡目录复用只消除批次内重复枚举，不复用目标路由结论。
接口和路由Provider保留有界的不可变快照版本集合。执行器验证本请求引用的版本仍在对应集合中，
不能要求它等于Provider最后写入的版本；否则同批后解析的目标会把先解析目标错误标记为
`resolved-plan-stale`。只有引用版本已从有界集合淘汰时才判定Stale；实际发送事实仍由Actual证据回答，
不得用后一次路由查询覆盖前一请求已经冻结的Resolved事实。

## 三、标识与关联

`Guid RequestId`是唯一Pending主键。调用方或便捷构造函数必须在调用发送入口之前生成最终非空GUID；
`TrySend`收到`Guid.Empty`时同步返回false和`request-id-empty`，并异步触发`RequestRejected`，不会启动端点、写入发送FIFO
或创建Pending。重复Pending身份和当前有界终态缓存内已经完成的身份同样拒绝，基类不再自动换号。

每次执行固定形成：

```text
RequestId
└─ AttemptId
   └─ BranchId
      └─ ResponseId
```

首次尝试及每次外层重试都生成新的`AttemptId`；每个已开始Attempt至少创建一个Branch，单路径请求也不例外。
`GetAttemptBranchCount`和`OnStartBranch`允许PreferredSet竞速端点在同一Attempt创建多个Branch。每条完整响应进入边界时生成新的
`ResponseId`；多Branch端点必须使用`PublishResponse(response, branchId)`明确归属。成功赢家关闭其他Branch为`Superseded`。

`TKey`是调用方定义的业务查询键，例如DNS客户端会话、DNS TransactionId或内部作业编号。它可以重复，公共库在
Pending快照、完成和失败记录中原样携带，并提供`GetPendingByKey`和`CountPendingByKey`只读查询，绝不作为内部唯一主键。
按键查询只观察当前Pending集合，不参与响应匹配、重试、完成或移除。响应同时提供`RequestId`和业务键；无法按
`RequestId`匹配、已经最终完成或已失败请求的响应属于迟到/重复响应，触发`LateResponseReceived`，不得再次发布为成功结果。

多响应端点必须显式覆盖`GetResponseCollectionPolicy`。`FirstValidResponse`保持单响应行为；
`CollectUntilWindowEnds`要求正数窗口和正数响应上限。窗口内每条有效响应通过`ResponseObserved`即时发布独立ResponseId，
但只有数量上限或窗口结束生成唯一终态。无有效响应的窗口进入超时/重试，闭环后的响应仍只进入迟到事件。

响应中的业务键仅用于迟到响应证据。正常完成记录的`Key`始终取自原请求，即使响应携带另一个键，也只能完成其
`RequestId`精确命中的请求，不能重定向到同键或异键的其他请求。自定义引用类型键在Pending期间必须保持相等性和哈希稳定。

内置端点允许调用方选择自己的`TKey`合同；不需要业务查询键的消费者可以使用任意稳定值，但唯一跟踪始终使用非空`RequestId`。
需要字符串、整数或复合键的扩展端点可以直接从泛型基类派生并选择自己的`TKey`，也可传入自定义
`IEqualityComparer<TKey>`控制只读查询的相等语义。无论具体键类型如何，内部主表始终是
`ConcurrentDictionary<Guid,...>`，其余操作只能使用`RequestId`。

## 三点一、三级尝试超时

内置端点公开三个可读写属性：

```csharp
endpoint.FirstAttemptTimeoutMs = 2000;
endpoint.SecondAttemptTimeoutMs = 3000;
endpoint.ThirdAttemptTimeoutMs = 4000;
```

它们分别对应首次发送、第一次重试和第二次重试。具体请求继续使用`int? TimeoutMs = null`：

- `null`：依次读取上述T1、T2、T3；
- 正整数：该请求的全部尝试统一使用请求值；
- 0或负数：明确拒绝。

`DefaultTimeoutMs`为兼容属性。读取时返回T1；设置时同时把T1、T2、T3设为同一值。旧代码行为不变，新代码可以分别调整
三次预算。一次尝试开始后会锁定本次预算；运行期间修改端点属性只影响后续尚未开始的尝试。

## 四、重试语义

- `MaxAttemptCount=3`表示包含首次发送在内最多执行三次；公共基类硬性限制范围为1到3；
- 首次尝试的`RetryCount=0`，第二、三次分别为1和2；不再使用“最大重试次数”命名，避免把总次数误解成4次；
- 成功响应立即完成，不再重试；
- 可重试错误响应立即进入重试；
- 完全无响应由Pending截止时间触发重试；
- 过滤失败、不可重试请求和超过上限直接发布最终失败；
- 自动重试只适用于幂等或由调用方明确允许重试的请求；
- 重试提供至少一次尝试保证，不承诺网络对端只处理一次。

## 四点一、尝试结果上报

`RequestRetrying`、`RequestAcknowledged`和`RequestFailed`均携带固定长`TrackedAttemptHistory`。最多三个
`TrackedAttemptReport`分别报告：

- `AttemptNumber`：1、2、3；
- `RetryCount`：0、1、2；
- `TimeoutMs`：本次实际采用的超时预算；
- `StartedAtUnixMs`、`FinishedAtUnixMs`；
- `ElapsedMs`：本次墙钟耗时；
- `FailureKind`、`FailureCode`。
- `Identity`：本次RequestId/AttemptId父链；
- `BranchCount`：本次Attempt实际创建的Branch数量。

`RequestRetrying`事件还直接提供`NextRetryCount`和`NextAttemptNumber`，调用方不需要自行推算当前正在排入的是第一次
还是第二次重试。

成功和最终失败结果另有`TotalElapsedMs`，从首次进入发送流程到闭环完成。完整失败原因仍由最终`NetworkFailure`提供；
尝试历史只保存固定长错误类别和代码，避免字符串引用破坏值类型、栈分配和批量读取能力。

## 五、逐请求访问计划

`RequestedAccessPlan`作为请求值的一部分进入发送FIFO，并且必须是非空、可规范化的显式值。
精确请求把协议访问对象与路径控制分开，正交表达PathProvider、目标地址、源地址、物理接口、下一跳、
路由范围、IP包政策和RouteAdapter。协议端点可在此基础上增加Resolver、Host、TLS SNI、证书校验名等
业务维度，但不得把它们塞回路径Flags。

`Automatic`明确授权系统选择未约束维度；`Direct`逐维执行Required/Preferred/Allowed/Excluded约束；
`SystemProxy`和`RouteAdapter`分别具有独立合同。系统选择不等于精确证明，无法满足Required维度时必须
明确拒绝，不能静默回退。

每个新Attempt可以在冻结的规范请求计划内重新解析接口和路由快照，并为实际候选创建新的Branch；重试不得
改变PathProvider、选择器类型、Required约束或规范候选集合。Actual证据直接归属于Branch，并分别报告
实际目标、源地址、接口、下一跳、路径类型及各维证明等级。

## 六、完成顺序

成功响应采用以下顺序：

1. 只根据响应`RequestId`定位Pending记录；
2. 在该记录内串行验证响应；
3. 唯一关闭赢家Branch、Attempt和Request，并异步发布三级终态事件；
4. 成功写入接收FIFO；
5. 将Pending状态标记为完成并放入有界终态身份缓存；
6. 触发`RequestAcknowledged`，完成记录携带完整四级身份。

如果接收FIFO已满，不能把网络成功误记为完成。该响应保留为待发布完成项，等待接收FIFO恢复；不得重新执行网络请求。

`BranchTerminated`、`AttemptTerminated`和`RequestTerminated`分别保证每层唯一终态。迟到或重复响应通过有界终态缓存恢复原
Request/Attempt/Branch父链，并为本次迟到观测创建新的ResponseId。`ResponseObserved`逐条发布具有完整父链的正常及迟到
响应；迟到项同时触发`LateResponseReceived`，但不能重开Request。

## 七、生命周期

- `Stop`：取消截止时间检查，将仍在等待的请求报告为`endpoint-stopped`；
- `ResetHardware`：保留允许重试的Pending请求并重新进入发送FIFO；
- 最终完成或失败后才从Pending表移除；
- 网络层资源的取消和释放仍由具体适配器负责；
- 所有请求、响应、访问计划和状态快照均使用值类型；内部同步对象不进入公共数据契约。

## 八、适用端点

| 端点 | 是否使用跟踪基类 | 说明 |
| --- | --- | --- |
| `NetworkHttpGetEndpoint<TKey>` | 已实现 | HTTP协议身份、稳定池键、Direct/SystemProxy/RouteAdapter证据 |
| `NetworkDohEndpoint<TKey>` | 已实现 | DNS查询身份、Resolver访问计划和HTTP安全边界分离 |
| `NetworkTcpConnectEndpoint<TKey>` | 已实现 | 显式访问计划、单Branch连接及Actual证据 |
| `NetworkUdpDatagramEndpoint<TKey>` | 已实现 | 每次尝试使用独立socket/NAT映射并按RequestId闭环 |
| `NetworkPingEndpoint<TKey>` | 已实现 | 统一IPv4/IPv6，支持单响应及有界广播/组播窗口 |
| `NetworkDnsReverseLookupEndpoint<TKey>` | 已实现 | 原生PTR、显式Resolver、事务号校验及UDP→TCP回退 |
| `NetworkNetBiosNameEndpoint<TKey>` | 已实现 | 原生NBNS单播及IPv4广播多响应窗口 |
| PowerShell | 保留旧端点 | 单命令输出无法自然拆分为独立请求与响应协议 |

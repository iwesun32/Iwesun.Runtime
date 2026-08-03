# TCP、UDP与Ping端点

> **状态**: CURRENT | **最后更新**: 2026-07-22

## 唯一公共端点

- `NetworkTcpConnectEndpoint<TKey>`：TCP连接探测；
- `NetworkUdpDatagramEndpoint<TKey>`：每个Branch分配`NetworkFlowSerial`，通过可替换`INetworkDatagramDataPlane`执行；
  默认System Socket后端使用长期Socket池、端口租约、SocketGeneration和常驻接收循环；
- `NetworkPingEndpoint<TKey>`：统一IPv4/IPv6单播及受控多响应。

三个端点都要求非空`RequestedAccessPlan`和非空RequestId。`TKey`可以重复，只用于调用方查询；唯一跟踪链固定为RequestId → AttemptId → BranchId → ResponseId。

UDP的`DatagramDispatched`事件在实际发送后发布Flow流水号、SocketGeneration、实际本地IP/端口、预期远端和接口。
无协议Token的并发请求不会共享同一本地端口；顺序请求在迟到隔离结束后可以复用长期槽。连接式UDP由系统先过滤其他远端，
接收循环仍校验实际远端IP和端口；无法匹配的数据报不会完成、失败或污染当前请求。

新增部分的职责边界：

- 四级GUID链仍是调用方查询和跨线程传播的正式身份；`NetworkFlowSerial`只是进程内热路径索引；
- `TrySend`成功只表示请求进入FIFO，实际本地端口必须等待`DatagramDispatched`；
- `SocketGeneration`区分Socket重建前后的传输实例，不能用本地端口单独判断复用关系；
- 数据面在创建Flow前再次验证Requested/Resolved稳定语义及快照时效，直接调用接口也不能绕过精确访问合同；
- 到期回收使用流水号主键索引和隔离到期索引，不扫描全部活动Flow；
- 每个稳定访问计划与远端端口的池默认最多64个活动/隔离槽，达到上限后以
  `network-flow-capacity-reached`拒绝新Flow，不提前清除隔离；
- `DataPlaneCounters`公开池数、槽位总数、活动槽、隔离槽、单池峰值和容量拒绝数，用于β实测决定正式容量；
- 接收循环永久失败时销毁槽；用户事件处理器异常被隔离，不会改变请求结果。

面向消费方的升级说明、观察字段和验证矩阵见
[Networks 3.0 UDP数据面β测试指南](../03-reference/NETWORKS_3_0_UDP_DATA_PLANE_BETA_TEST_GUIDE.md)。

## 路径能力

| 能力 | TCP/UDP | Ping |
| --- | --- | --- |
| Automatic | 支持 | 支持 |
| Direct精确源/接口 | Socket绑定 | Windows ICMP API |
| Direct OnLink | 支持 | 支持平台参数时执行 |
| ExactNextHop | Windows WFP | 明确拒绝 |
| ExactCompartment | 明确拒绝 | 明确拒绝 |
| 多响应 | UDP协议按端点定义 | IPv4广播/IPv6组播窗口 |

Ping/ICMP没有可由库独占分配的本地端口匹配维度，因此不能安全使用当前WFP连接策略隔离ExactNextHop。此情况返回固定能力错误，不回退系统路由。

Windows IPv6精确接口使用`Icmp6SendEcho2`的SourceAddress与带ScopeId目标共同形成API绑定证据。对于无Scope的链路本地或
组播目标，执行器按已解析的Exact Interface补入ScopeId；接口冲突仍在发送前拒绝。只有原生API成功提交且目标、源地址、
接口和ScopeId证据完整时，访问轴才允许为`Satisfied`。

全局IPv6单播不使用ScopeId。原生API绑定Exact Source且Windows接口快照确认该Source属于Exact Interface时，接口证据为
`AdapterReported + Satisfied`；Source与Interface不匹配时返回`source-interface-conflict`。给全局地址附加ScopeId返回
`ipv6-scope-not-applicable`，不得把Scope当作通用接口字段。

## 多响应

IPv4广播和IPv6组播必须使用`TrackedResponseCollectionPolicy.CollectUntilWindowEnds`。Ping原生执行器拥有并主动关闭收集窗口，
避免基类截止计时与原生API返回争抢终态。窗口内有效响应即时发布，每条创建唯一ResponseId；数量上限或窗口结束只生成一次
Branch、Attempt和Request终态。普通单播使用`FirstValidResponse`。

`NetworkPingExecutionResult.Evidence.Destination`始终记录实际提交的组播目标；每条回包的单播来源记录在
`ResponderAddress`。有响应且绑定完整时闭环为`Succeeded + Satisfied`；零响应但绑定完整时闭环为
`TimedOut + Satisfied`，不得误报Transport。Windows响应方解析遵循SDK中1字节压缩的`IPV6_ADDRESS_EX`布局。

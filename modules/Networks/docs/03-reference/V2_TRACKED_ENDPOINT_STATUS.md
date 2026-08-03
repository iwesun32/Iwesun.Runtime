# V2请求跟踪端点历史状态

> **状态**: ARCHIVED | **最后更新**: 2026-07-22
>
> 本文仅保存2.0历史事实。V2公共类型已在3.0候选中物理移除，不得作为当前接入说明。
>
> **设计**: [TRACKED_REQUEST_REPLY.md](../02-endpoints/TRACKED_REQUEST_REPLY.md)

## 当前完成

- 独立`TrackedRequestReplyEndpointBase<TRequest,TResponse,TKey>`；
- `Guid RequestId`作为库管理的唯一请求身份；`TKey`仅作为允许重复的调用方业务查询键，并可通过
  `GetPendingByKey`、`CountPendingByKey`观察，绝不参与完成选择；
- 发送FIFO、Pending等待表、完成FIFO分别限容；
- 原请求与响应组成的完成记录；
- 每条请求包含首次发送在内最多执行三次网络尝试；
- `FirstAttemptTimeoutMs`、`SecondAttemptTimeoutMs`、`ThirdAttemptTimeoutMs`独立预算；
- 完成、重试和失败上报固定长三次尝试历史及总耗时；
- 超时、错误响应、最终失败和迟到响应处理；
- 旧尝试迟到失败不干扰当前尝试；
- 接收FIFO满时保留完成响应，不重复执行网络请求；
- 批量发送、批量接收；
- 异步事件分发；
- `NetworkRouteDirective`逐请求路由；
- 每条FIFO请求可独立指定目标IP、源IP、物理网卡、路由适配器和标志；全部缺省时使用系统选择；
- `INetworkRouteAdapter`与稳定ID注册表；
- `HttpConnectNetworkRouteAdapter`固定CONNECT目标IP；
- `PrecisionRouteHttpMessageHandler`保持Host/SNI并执行该条请求自己的路由；
- `TrackedHttpGetEndpointV2`；
- `TrackedDohEndpointV2`；
- DoH响应独立物化全部解析地址（当前wire解析上限32），不截断为少数固定槽；
- `TrackedTcpConnectEndpointV2`；
- `TrackedUdpDatagramEndpointV2`，每次尝试使用独立UDP socket/NAT映射并回传实际本地端点；
- `TrackedDnsReverseLookupEndpointV2`，原生PTR wire查询、显式解析器、事务号校验及UDP截断后TCP回退；
- `TrackedNetBiosNameEndpointV2`，原生NBNS UDP/137 Node Status查询，不再依赖`nbtstat`进程；
- `TrackedPingIpv4EndpointV2`与`TrackedPingIpv6EndpointV2`，支持系统选路、指定源地址、指定物理接口和三次总尝试；
- Ping在指定`InterfaceIndex`时解析并绑定该物理网卡的源IP，避免Clash Verge、VPN或其他TUN虚拟网卡抢占默认路由；
- Ping响应和最终路由同时记录实际目标IP、实际源IP和接口索引；
- 所有V2端点统一`Route = null`、`TimeoutMs = null`、T1/T2/T3、兼容`DefaultTimeoutMs`和`MaxAttemptCount <= 3`契约；
- 独立测试项目。
- 独立`Iwesun.Runtime.Networks.Examples`控制台示例，覆盖DoH系统选择、固定IPv4/IPv6、源地址/网卡、IPv6源地址竞速、
  系统代理、固定最终IP的HTTP CONNECT路径，以及按物理接口执行IPv4/IPv6 Ping；
- 完整[DoH精确底层路由使用指南](../02-endpoints/DOH_PRECISION_ROUTING_GUIDE.md)。

## 2.0统一边界

Networks 2.0已删除被取代的旧HTTP、DoH、TCP、IPv4 Ping和IPv6 Ping公开端点、请求、结果及内部helper，
不提供别名或兼容程序集。`NetworkAsyncEndpointBase<TSend,TReceive>`只为PowerShell、Process、ARP和IPv6邻居
快照等非重复特殊模型保留，不得再承载已有V2对应的协议。

Aether的`HttpGetService`已于2026-07-19切换到
`TrackedHttpGetEndpointV2`：普通缺省、固定目标直连、显式HTTP CONNECT适配器和IPv6 Preferred源地址竞速均由V2执行；
Aether不再自行实现HTTP精确路由socket。Aether的`NetworkProbeService`已经迁移到V2 IPv4/IPv6 Ping和TCP Connect，
其公共方法保留旧式省略调用，并在末尾增加可空逐请求路由参数。
兼容批量DoH路径也已强制迁移到`TrackedDohEndpointV2`。DDNS Snap的活动名称扫描、连接探测、IPv4广播Ping和
IPv6组播Ping均已切换到V2。

真实网络复验已确认：固定阿里IPv4和IPv6均在保留`dns.alidns.com` Host/SNI的情况下返回合法DNS答案；
显式HTTP CONNECT适配器访问固定Google `8.8.8.8`也返回合法答案，CONNECT目标和TLS SNI/Host已分别保持。

## 后续扩展

- 本地ARP/IPv6邻居快照的统一完成格式；
- SOCKS及其他`INetworkRouteAdapter`实现；
- 大规模Pending时间轮优化；
- PowerShell、Process与本地快照模型的公共格式继续收敛，但不得复制请求—响应V2能力。

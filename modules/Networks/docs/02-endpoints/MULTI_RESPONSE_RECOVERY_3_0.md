# Networks 3.0 多响应与Windows恢复原语

> **状态**: M7_COMPLETE | **最后更新**: 2026-07-22  
> **源码参考**: `TrackedRequestReplyEndpointBase.cs`、`NetworkPingEndpoint.cs`、`NetworkNetBiosNameEndpoint.cs`、`WindowsNetworkRecoveryPrimitives.cs`  
> **上游**: [精确网络访问控制最终设计](../01-design/PRECISION_NETWORK_ACCESS_CONTROL_FINAL_DESIGN.md)

## 一、多响应窗口

`TrackedResponseCollectionPolicy`只提供两种非模糊模式：

- `FirstValidResponse`：首个有效响应关闭Branch；
- `CollectUntilWindowEnds`：在正数窗口和正数上限内逐条发布响应，达到上限或窗口结束后关闭Branch。

窗口中的每条有效响应都通过`ResponseObserved`即时发布，具有共同的RequestId/AttemptId/BranchId和独立ResponseId。窗口终止只产生一次Branch、Attempt和Request终态；完成FIFO携带最后一条有效响应。无有效响应的窗口使用`response-collection-window-empty`进入既有重试/失败管线，不伪造响应。闭环后的报文只进入`LateResponseReceived`。

`NetworkPingEndpoint<TKey>`在Windows使用`IcmpSendEcho2Ex`或`Icmp6SendEcho2`的多回复缓冲支持IPv4广播和IPv6组播；最多收集256条，未显式提供窗口时发送前拒绝。`NetworkNetBiosNameEndpoint<TKey>`使用单个UDP socket与Transaction ID收集广播Node Status响应，并为每个实际发送者生成Observed目标证据。

IPv6多回复缓冲必须遵守Windows SDK `IPExport.h`的两层对齐：内部
`IPV6_ADDRESS_EX`按1字节压缩且大小为26，外层`ICMPV6_ECHO_REPLY`
恢复默认ULONG对齐，因此响应地址从偏移6开始，`Status`位于28，
`RoundTripTime`位于32，结构步长为36。不得把压缩子结构的26字节末尾
直接当作外层`Status`，否则第二条及后续响应会按每条2字节持续错位。

## 二、恢复原语

`WindowsNetworkRecoveryPrimitives`只提供原子动作，不包含DDNS业务恢复顺序：

- ICMPv6 Router Solicitation；
- `ipconfig /release6 <adapter>`；
- 独立等待；
- `ipconfig /renew6 <adapter>`；
- 网卡disable/enable重启；
- 动作前后地址快照。

每个结果分开记录`RequestAccepted`、`PlatformActionSucceeded`、`AddressChanged`和`ConnectivityRecheck`。平台动作成功不会自动把地址变化或连通性标为成功；可选复检委托的结果也不会反向覆盖平台动作。网卡重启在disable后的取消或异常路径上使用不受原取消令牌影响的enable补偿。

能力查询在非Windows平台返回`platform-not-supported`；需要系统权限的RS、DHCPv6和网卡重启在默认Windows执行器下接受管理员，或属于内置`Network Configuration Operators`（SID `S-1-5-32-556`）的服务身份，权限不足返回`platform-permission-missing`。测试通过注入`INetworkRecoveryPlatformExecutor`验证合同，不执行真实系统恢复动作；真实网卡验收留在M11明确授权环境执行。

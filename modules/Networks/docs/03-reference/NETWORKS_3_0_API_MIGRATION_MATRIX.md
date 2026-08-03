# Networks 3.0 API 迁移矩阵

状态：M0/M1 实施基线  
盘点日期：2026-07-22  
当前发布基线：Iwesun.Runtime.Networks 2.0.1

> 本文保留2.0.1到3.0的原始强制迁移基线。当前`3.0.0-beta.4`源码、架构闭环和
> beta.3增量迁移结论见
> [beta.4升级迁移报告](NETWORKS_3_0_BETA4_UPGRADE_MIGRATION_REPORT.md)。

> `IpAddressValue`的当前4/16/17字节边界和显式地址族API迁移，见
> [Networks 3.0 IpAddressValue迁移手册](NETWORKS_3_0_IP_ADDRESS_VALUE_MIGRATION_GUIDE.md)。

## 一、工作区基线

| 仓库 | 分支 | 提交 | 迁移职责 |
| --- | --- | --- | --- |
| Networks | `master` | `63509a6ccdfc4d06f18a3ac11537b472c43c8c28` | 3.0合同、基类和协议端点 |
| Runtime | `main` | `367c674971af14e8af6f8dc8f90dcc7261f4ad23` | 诊断投影、发布聚合、安装包 |
| DDNS Snap | `main` | `4754024b611d05442da66ea893ce2aeab90aed4e` | Ping、PTR、UDP、NBNS及地址守护迁移 |
| Aether | `master` | `387e68b2d8b5cde4d15ace5f93f7dd2e96778659` | HTTP、DoH、TCP及连接池迁移 |

四仓均存在用户未提交工作。本轮只在Networks新增M0/M1产物，不覆盖、回退或整理其他改动。

## 二、2.0.1可重复基线

| 配置 | 命令 | 结果 |
| --- | --- | --- |
| Debug | `dotnet test Networks.slnx -c Debug --nologo` | 29通过，0失败，0跳过 |
| Release | `dotnet test Networks.slnx -c Release --nologo` | 29通过，0失败，0跳过 |

程序集版本由`Directory.Build.props`统一为`2.0.1`。本阶段不改版本、不生成或覆盖发布包。

## 三、2.0.1公共面归属

### 3.1 导出类型基线

- 基础FIFO：`NetworkAsyncEndpointBase<TSend,TReceive>`、`NetworkSendResult`、三个发送/硬件/错误枚举及六类事件参数。
- 跟踪基类：`TrackedRequestReplyEndpointBase<TRequest,TResponse,TKey>`、`TrackedPendingSnapshot`、`TrackedAttemptHistory`、`TrackedAttemptReport`、`TrackedRequestCompletion`、`TrackedRequestFailure`、`NetworkFailure`及五类跟踪事件参数。
- 路由：`NetworkRouteDirective`、`NetworkRouteKind`、`NetworkRouteFlags`、`INetworkRouteAdapter`、`NetworkRouteAdapterRegistry`、`NetworkRouteConnectionContext`、`HttpConnectNetworkRouteAdapter`、`PrecisionRouteHttpMessageHandler`、`RouteBoundHttpMessageHandler`。
- 协议：HTTP、DoH、TCP、UDP、IPv4 Ping、IPv6 Ping、PTR、NBNS共八组`EndpointV2 + RequestV2 + ResponseV2`；另有`TrackedDnsQueryTypeV2`和`NetBiosNameRecordV2`。
- 非重复能力：PowerShell两组二进制端点、进程端点、ARP表、IPv6邻居快照、固定字节缓冲及DNS Wire Codec。
- 固定值基础：`IpAddressValue`、`MacAddressValue`、`BinaryNetworkError`；旧
  `BinaryIpAddress` 已删除并通过编译错误强制迁移。

八个V2端点的公共构造函数均暴露容量、尝试数、默认超时等参数；HTTP/DoH/TCP额外接收`NetworkRouteAdapterRegistry`，UDP/PTR/NBNS额外接收同步上下文和接收缓冲参数。所有V2请求同时暴露显式`RequestId`构造函数与尾随`Guid? requestId`便捷构造函数。跟踪基类统一公开`RequestWaiting`、`RequestRetrying`、`RequestAcknowledged`、`RequestFailed`、`LateResponseReceived`和`SendQueueAvailable`事件；基础FIFO统一公开`SendStarted`、`SendCompleted`、`ReceiveCompleted`、`HardwareStateChanged`、`HardwareError`、`Error`和`SendQueueAvailable`事件。

以上清单以2026-07-22首次M1源码加入前的2.0.1导出面为准；3.0新增合同不属于2.0.1基线。

| 2.0.1公共面 | 现状 | 3.0归属 | 实施里程碑 |
| --- | --- | --- | --- |
| `TrackedRequestReplyEndpointBase<TRequest,TResponse,TKey>` | RequestId单层Pending；Attempt使用序号 | 四级GUID树；TKey只读查询 | M2 |
| `TrackedRequestCompletion`、Pending、AttemptHistory和事件 | 闭环记录没有Branch/Response身份 | Branch/Attempt/Request分层终态 | M2、M3 |
| `NetworkRouteDirective?`、`Route = null` | null兼作系统选路 | 非空`RequestedAccessPlan` | M4、M9 |
| `NetworkRouteKind`、`NetworkRouteFlags` | 路径和逐维约束混合 | `NetworkPathProvider`与正交强类型选择器 | M1、M4 |
| `TrackedHttpGetEndpointV2` | HTTP V2 | 唯一3.0 HTTP端点、稳定池键 | M6 |
| `TrackedDohEndpointV2` | DoH V2 | DNS身份/Resolver/HTTP安全边界分离 | M6、M7 |
| `TrackedTcpConnectEndpointV2` | TCP V2 | 3.0单Branch/多Branch执行 | M6 |
| `TrackedUdpDatagramEndpointV2` | UDP V2 | NAT映射和生命周期显式化 | M6 |
| `TrackedPingIpv4EndpointV2`、`TrackedPingIpv6EndpointV2` | 精确源/接口由Flags表达 | 3.0路径计划、IPv4广播/IPv6组播窗口 | M6、M8 |
| `TrackedDnsReverseLookupEndpointV2` | PTR目标和Resolver路径仍耦合 | 查询对象与Resolver访问计划分离 | M7 |
| `TrackedNetBiosNameEndpointV2` | 单窗口响应数组 | 多响应四级身份链和唯一窗口终态 | M8 |
| `INetworkRouteAdapter`与Registry | 适配器能力表达有限 | 能力版本、ClientLeg/EgressLeg证据 | M5 |
| PowerShell、进程、本地快照端点 | 非重复请求—响应能力 | 保留独立模型并统一格式 | M9 |

公开类型、构造函数和事件的源码基线位于`modules/Networks/src/Iwesun.Runtime.Networks/*.cs`；M9删除V2公共面前必须再次以反射清单比对，确保不存在混合语义正式程序集。

## 四、消费者调用点

### Runtime

- `modules/Packaging/release/Iwesun.Runtime.Release/Iwesun.Runtime.Release.csproj`构建并复制Networks程序集、文档和示例。
- 不直接调用协议端点；M10只迁移诊断快照、文档和发布版本。

### DDNS Snap

- `DdnsSnap.Core`当前引用NuGet `Iwesun.Runtime.Networks` 2.0.1。
- `EventDrivenNetworkScanPipelineTask`使用Ping、PTR、NBNS及可空Route。
- `Ipv6ConnectivityDaemon`使用`NetworkRouteFlags.BindSourceAddress/BindInterface`。
- `ConnectProbeService`使用TCP和PTR端点。
- 功能测试直接覆盖UDP、PTR、NBNS和精确接口路由。
- M10迁移到显式计划，并验证地址守护的真实IPv6路径，不保留null兼容层。

### Aether

- `Aether.Service`当前以跨仓`ProjectReference`引用Networks。
- `HttpGetService`使用HTTP V2、Flags、代理/适配器和Preferred IPv6源。
- `BatchDohRefresher`使用DoH V2及`route = null`/SystemProxy二义表达。
- `NetworkProbeService`使用TCP V2及可空Route。
- M10必须把Host/SNI/证书名、Resolver和稳定连接池键映射到3.0合同。

## 五、固定迁移规则

1. 对外绝对跟踪身份统一为`RequestId → AttemptId → BranchId → ResponseId`四级GUID链。
2. `Guid.Empty`在低层发送入口、FIFO和Pending之前拒绝；`TKey`不参与唯一性或完成匹配。
3. 所有路径请求显式提供非空`RequestedAccessPlan`；不以null、default、Flags或哨兵值表达选择。
4. `ProtocolOutcome`与`AccessCompliance`分别聚合，Actual证据只归属于Branch。
5. HTTP/DoH连接池使用稳定访问语义键，排除跟踪GUID、时间、超时、重试和Actual观测。
6. M9一次性移除V2和可空Route公共面；M10三方消费者同步强制迁移。
7. `IpAddressValue.FromAddressBytes`不得恢复；裸地址必须在来源处明确选择
   `FromIPv4Bytes`或`FromIPv6Bytes`，17字节固定格式只通过二进制读写API处理。

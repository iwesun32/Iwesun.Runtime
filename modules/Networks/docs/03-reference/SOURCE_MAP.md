# 源码映射

> **状态**: CURRENT | **最后更新**: 2026-07-22
> **源码参考**: `modules/Networks/src/Iwesun.Runtime.Networks/` 目录
> **上游**: [../README.md](../README.md)（文档索引）
> **下游**: 无

---

## 一、源码文件索引

| 文件路径                                 | 类型     | 说明                                           | 对应文档                                                                                                           |
| ---------------------------------------- | -------- | ---------------------------------------------- | ------------------------------------------------------------------------------------------------------------------ |
| `Iwesun.Runtime.Networks.csproj`                 | 项目文件 | 类库项目配置                                   | -                                                                                                                  |
| `NetworkExecutionIdentity.cs`            | 3.0合同  | Request/Attempt/Branch/Response四级GUID身份链  | [最终设计](../01-design/PRECISION_NETWORK_ACCESS_CONTROL_FINAL_DESIGN.md)                                          |
| `NetworkAccessSelectors.cs`              | 3.0合同  | 路径提供者、强类型选择器及稳定规范集合         | [最终设计](../01-design/PRECISION_NETWORK_ACCESS_CONTROL_FINAL_DESIGN.md)                                          |
| `NetworkAccessPlan.cs`                   | 3.0合同  | Requested计划、校验和稳定访问语义Key           | [最终设计](../01-design/PRECISION_NETWORK_ACCESS_CONTROL_FINAL_DESIGN.md)                                          |
| `NetworkAccessEvidence.cs`               | 3.0合同  | Resolved/Actual、证明等级及分层终态             | [最终设计](../01-design/PRECISION_NETWORK_ACCESS_CONTROL_FINAL_DESIGN.md)                                          |
| `NetworkAccessCapabilities.cs`           | 3.0合同  | 能力快照、端点合法性矩阵                       | [最终设计](../01-design/PRECISION_NETWORK_ACCESS_CONTROL_FINAL_DESIGN.md)                                          |
| `NetworkAttemptPolicy.cs`                | 3.0合同  | 不进入稳定Key的超时、重试与竞速政策            | [最终设计](../01-design/PRECISION_NETWORK_ACCESS_CONTROL_FINAL_DESIGN.md)                                          |
| `NetworkAccessFailureCodes.cs`           | 3.0合同  | 固定失败原因码与平台错误载荷                   | [最终设计](../01-design/PRECISION_NETWORK_ACCESS_CONTROL_FINAL_DESIGN.md)                                          |
| `NetworkAccessResolution.cs`             | 3.0求解层 | 接口身份规范化、Derived求解、快照失效和可解释能力查询 | [最终设计](../01-design/PRECISION_NETWORK_ACCESS_CONTROL_FINAL_DESIGN.md)                                  |
| `NetworkSocketExecution.cs`              | 3.0执行层 | Automatic/Direct TCP、UDP Socket绑定、执行和逐维Actual证据 | [Socket执行后端](../02-endpoints/SOCKET_EXECUTION_3_0.md)                                         |
| `NetworkFlowTracking.cs`                 | 数据面核心 | uint流水号、活动生命周期、迟到隔离及身份/传输反查 | [内部流跟踪设计](../01-design/NETWORK_FLOW_SERIAL_AND_FORWARDING_DATA_PLANE_DESIGN.md) |
| `NetworkDataPlanes.cs`                   | 数据面合同 | Datagram/Stream/Packet能力、发送证据和可替换后端接口 | [内部流跟踪设计](../01-design/NETWORK_FLOW_SERIAL_AND_FORWARDING_DATA_PLANE_DESIGN.md) |
| `SystemSocketDatagramDataPlane.cs`        | UDP数据面 | 长期Socket池、端口租约、Generation、常驻接收与严格分派 | [TCP/UDP/Ping](../02-endpoints/TCP_UDP_PING.md) |
| `NetworkForwardingContracts.cs`           | 转发合同 | DNS、NAT、Socket、HTTP代理及三层数据面独立能力边界 | [内部流跟踪设计](../01-design/NETWORK_FLOW_SERIAL_AND_FORWARDING_DATA_PLANE_DESIGN.md) |
| `WindowsNetworkRouteSnapshotProvider.cs` | 3.0平台层 | Windows GetBestRoute2只读候选路径及接口快照采集 | [Socket执行后端](../02-endpoints/SOCKET_EXECUTION_3_0.md)                                               |
| `NetworkAccessExecutionContext.cs`       | 3.0运行层 | 平台快照刷新、计划求解与Socket执行器的端点级组合上下文 | [Socket执行后端](../02-endpoints/SOCKET_EXECUTION_3_0.md)                                      |
| `NetworkSocketEndpoints.cs`              | 3.0端点层 | 泛型TKey TCP/UDP端点、四级响应身份和Branch级路径证据 | [Socket执行后端](../02-endpoints/SOCKET_EXECUTION_3_0.md)                                          |
| `NetworkPingEndpoint.cs`                 | 3.0端点层 | 统一IPv4/IPv6 ICMP、精确源绑定及广播/组播多回复窗口 | [TCP/UDP/Ping](../02-endpoints/TCP_UDP_PING.md)                                               |
| `NetworkDnsReverseLookupEndpoint.cs`     | 3.0端点层 | PTR查询对象/Resolver路径分离及同Branch UDP→TCP阶段证据 | [名称解析](../02-endpoints/NAME_RESOLUTION.md)                                                |
| `NetworkNetBiosNameEndpoint.cs`          | 3.0端点层 | 单播/广播NBNS节点状态查询、多响应窗口及发送者证据 | [名称解析](../02-endpoints/NAME_RESOLUTION.md)                                                       |
| `WindowsNetworkRecoveryPrimitives.cs`    | 3.0恢复层 | RS、DHCPv6、等待、网卡重启、地址快照及分轴结果 | [多响应与恢复](../02-endpoints/MULTI_RESPONSE_RECOVERY_3_0.md)                                      |
| `NetworkHttpContracts.cs`                | 3.0合同层 | HTTP Host/SNI/证书身份、复用政策与稳定连接池Key | [HTTP/DoH 3.0](../02-endpoints/HTTP_DOH_3_0.md)                                                   |
| `NetworkHttpEndpoint.cs`                 | 3.0端点层 | 固定传输IP HTTP GET、稳定语义池和连接路径证据 | [HTTP/DoH 3.0](../02-endpoints/HTTP_DOH_3_0.md)                                                     |
| `NetworkDohEndpoint.cs`                  | 3.0端点层 | JSON/WireMessage DoH、固定IP TLS身份及四级跟踪 | [HTTP/DoH 3.0](../02-endpoints/HTTP_DOH_3_0.md)                                                     |
| `NetworkRouteAdapters.cs`                | 3.0适配层 | 显式适配器能力版本、传输类型与Client/Egress双Leg合同 | [HTTP/DoH 3.0](../02-endpoints/HTTP_DOH_3_0.md)                                               |
| `HttpConnectNetworkRouteAdapter.cs`      | 3.0适配层 | HTTP CONNECT ByteStream适配器及不跨段的双Leg证据 | [HTTP/DoH 3.0](../02-endpoints/HTTP_DOH_3_0.md)                                               |
| `NetworkAsyncEndpointBase.cs`            | 核心类   | 异步网络端点抽象基类（FIFO、状态机、线程循环） | [ENDPOINT_BASE.md](../02-endpoints/ENDPOINT_BASE.md)                                                               |
| `TrackedRequestReplyEndpointBase.cs`     | 3.0核心类 | 四级GUID请求跟踪、多Branch、三队列、确认、超时与重试基类 | [TRACKED_REQUEST_REPLY.md](../02-endpoints/TRACKED_REQUEST_REPLY.md)                                      |
| `BinaryNetworkPrimitives.cs`             | 值类型   | BinaryNetworkError                             | [BINARY_PRIMITIVES.md](../02-endpoints/BINARY_PRIMITIVES.md)                                                       |
| `IpAddressValue.cs`                      | 值类型   | 17字节可空纯IP数值、比较、分类、二进制和JSON   | [ADDRESS_VALUE_TYPES.md](../02-endpoints/ADDRESS_VALUE_TYPES.md)                                                   |
| `MacAddressValue.cs`                     | 值类型   | 7字节可空 EUI-48 数值、比较、二进制和JSON       | [ADDRESS_VALUE_TYPES.md](../02-endpoints/ADDRESS_VALUE_TYPES.md)                                                   |
| `IpAddressTraits.cs` / `IpAddressType.cs` | 值分类  | IP 固有正交特征与简化地址分类                   | [ADDRESS_VALUE_TYPES.md](../02-endpoints/ADDRESS_VALUE_TYPES.md)                                                   |
| `MacAddressTraits.cs`                    | 值分类   | EUI-48 固有特征                                 | [ADDRESS_VALUE_TYPES.md](../02-endpoints/ADDRESS_VALUE_TYPES.md)                                                   |
| `WindowsArpTableEndpoint.cs`             | 端点     | Windows ARP 表快照                             | [LOCAL_TABLES.md](../02-endpoints/LOCAL_TABLES.md)                                                                 |
| `WindowsIpv6NeighborSnapshotEndpoint.cs` | 端点     | Windows IPv6 邻居表快照（P/Invoke）            | [LOCAL_TABLES.md](../02-endpoints/LOCAL_TABLES.md)                                                                 |
| `ProcessCommandEndpoint.cs`              | 端点     | 外部进程执行                                   | [PROCESS_POWERSHELL.md](../02-endpoints/PROCESS_POWERSHELL.md)                                                     |
| `PowerShellSingleCommandEndpoints.cs`    | 端点     | PowerShell 命令执行 + IPv6 邻居表（PS 方式）   | [PROCESS_POWERSHELL.md](../02-endpoints/PROCESS_POWERSHELL.md)、[LOCAL_TABLES.md](../02-endpoints/LOCAL_TABLES.md) |

---

## 二、枚举类型位置

| 枚举类型                   | 所在文件                                 | 对应文档                                                            |
| -------------------------- | ---------------------------------------- | ------------------------------------------------------------------- |
| `NetworkHardwareStatus`    | `NetworkAsyncEndpointBase.cs`            | [HARDWARE_STATE_MACHINE.md](../01-design/HARDWARE_STATE_MACHINE.md) |
| `NetworkSendResult`        | `NetworkAsyncEndpointBase.cs`            | [HARDWARE_STATE_MACHINE.md](../01-design/HARDWARE_STATE_MACHINE.md) |
| `NetworkEndpointErrorKind` | `NetworkAsyncEndpointBase.cs`            | [HARDWARE_STATE_MACHINE.md](../01-design/HARDWARE_STATE_MACHINE.md) |
| `NetworkSendStatus`        | `NetworkAsyncEndpointBase.cs`            | [HARDWARE_STATE_MACHINE.md](../01-design/HARDWARE_STATE_MACHINE.md) |
| `ArpEntryType`             | `WindowsArpTableEndpoint.cs`             | [LOCAL_TABLES.md](../02-endpoints/LOCAL_TABLES.md)                  |
| `IPv6NeighborState`        | `WindowsIpv6NeighborSnapshotEndpoint.cs` | [LOCAL_TABLES.md](../02-endpoints/LOCAL_TABLES.md)                  |
| `ExecutionPolicy`          | `PowerShellSingleCommandEndpoints.cs`    | [PROCESS_POWERSHELL.md](../02-endpoints/PROCESS_POWERSHELL.md)      |

---

## 三、事件参数类型位置

| 事件参数类型                           | 所在文件                      |
| -------------------------------------- | ----------------------------- |
| `NetworkSendEventArgs<TSend>`          | `NetworkAsyncEndpointBase.cs` |
| `NetworkReceiveEventArgs<TReceive>`    | `NetworkAsyncEndpointBase.cs` |
| `NetworkHardwareStateChangedEventArgs` | `NetworkAsyncEndpointBase.cs` |
| `NetworkHardwareErrorEventArgs`        | `NetworkAsyncEndpointBase.cs` |
| `NetworkEndpointErrorEventArgs`        | `NetworkAsyncEndpointBase.cs` |
| `NetworkQueueAvailableEventArgs`       | `NetworkAsyncEndpointBase.cs` |

---

## 四、项目结构

```text
Networks/
├── Directory.Build.props     -> 仓库全局构建设置
├── Networks.slnx             -> 解决方案入口
├── docs/                     -> 设计文档（中文）
│   ├── README.md             -> 文档索引
│   ├── 01-design/            -> 设计层（核心抽象、状态机、FIFO）
│   ├── 02-endpoints/         -> 端点层（基类、各具体端点）
│   └── 03-reference/         -> 参考层（源码映射、逻辑约定）
└── modules/Networks/src/Iwesun.Runtime.Networks/          -> 库源码
    ├── Iwesun.Runtime.Networks.csproj
    ├── NetworkAsyncEndpointBase.cs  -> 核心基类（~1000 行）
    ├── BinaryNetworkPrimitives.cs   -> 二进制原语
    └── *Endpoint*.cs                -> 10 个具体端点实现
```

---

## 五、相关文档

- 文档索引 → [../README.md](../README.md)
- 逻辑约定 → [LOGICAL_CONTRACTS.md](LOGICAL_CONTRACTS.md)

# Iwesun.Runtime.Networks 文档索引

> **状态**: CURRENT | **最后更新**: 2026-09-22
> 本文档是 Iwesun.Runtime.Networks 设计文档的唯一入口。所有设计文档以中文撰写，按自上而下的逻辑架构组织。

## 语言约定

| 内容类型                                            | 语言    | 说明                                                   |
| --------------------------------------------------- | ------- | ------------------------------------------------------ |
| 程序代码、标识符、注释                              | English | 命名空间、类名、方法名、变量名、XML 文档注释、行内注释 |
| 设计文档（`docs/`）                                 | 中文    | 所有设计文档、指南、面向人的文档                       |
| AI 指令文件（`.github/instructions/`、`AGENTS.md`） | English | AI 编码代理消费的指令文件                              |

## 设计主线

Iwesun.Runtime.Networks 3.0围绕**一个显式访问计划请求—响应抽象 + 专用特殊模型**组织：

- **正式请求—响应抽象**：`TrackedRequestReplyEndpointBase<TRequest,TResponse,TKey>`统一四级GUID身份、Pending、重试、Branch和逐请求访问计划。
- **特殊模型**：PowerShell、Process、ARP和IPv6邻居快照继续使用`NetworkAsyncEndpointBase`，因为它们不属于请求—响应协议端点。
- **值类型FIFO**：所有请求、响应和快照合同保持值类型；发送、Pending和完成队列均有明确容量边界。

**被替代的版本化协议端点、可空Route、旧路由Flags和兼容处理器已经物理移除，不得恢复。**

## 阅读顺序（自上而下）

### 01 — 设计（宏观层）

| 文档                                                             | 内容                                              |
| ---------------------------------------------------------------- | ------------------------------------------------- |
| [DESIGN_OVERVIEW.md](01-design/DESIGN_OVERVIEW.md)               | **设计总纲**：核心抽象、FIFO 模型、状态机、数据流 |
| [HARDWARE_STATE_MACHINE.md](01-design/HARDWARE_STATE_MACHINE.md) | 硬件状态机、状态转换规则、错误模型                |
| [FIFO_MODEL.md](01-design/FIFO_MODEL.md)                         | 发送/接收 FIFO、背压机制、批量读写、容量配置      |
| [ROUTING_SEMANTICS_OPTIMIZATION.md](01-design/ROUTING_SEMANTICS_OPTIMIZATION.md) | **历史RFC**：保留路由语义问题、消费者意见和设计理由 |
| [PRECISION_NETWORK_ACCESS_CONTROL_MODEL.md](01-design/PRECISION_NETWORK_ACCESS_CONTROL_MODEL.md) | **已完成总体规划**：保留GUID、选择器、Windows后端和协议扩展规划依据 |
| [PRECISION_NETWORK_ACCESS_CONTROL_FINAL_DESIGN.md](01-design/PRECISION_NETWORK_ACCESS_CONTROL_FINAL_DESIGN.md) | **已接受的Networks 3.0最终设计**：唯一权威实施合同 |
| [NETWORKS_3_0_IMPLEMENTATION_PLAN.md](01-design/NETWORKS_3_0_IMPLEMENTATION_PLAN.md) | **实施计划**：M0～M12里程碑、停线条件、三方迁移与发布门禁 |
| [NETWORKS_3_0_EXACT_INTERFACE_PING_FEEDBACK.md](01-design/NETWORKS_3_0_EXACT_INTERFACE_PING_FEEDBACK.md) | **已确认反馈**：精确接口IPv6组播成功却被误判为传输失败的证据、修正意见与验收矩阵 |
| [NETWORKS_3_0_PROTOCOL_ACCESS_TERMINAL_CORRECTION_PLAN.md](01-design/NETWORKS_3_0_PROTOCOL_ACCESS_TERMINAL_CORRECTION_PLAN.md) | **本地更正已通过**：三轴终态、真实Ping及DDNS隔离DLL消费通过，完整Service恢复链未运行 |
| [PROXY_ROUTER_APPLICATION_REQUIREMENTS.md](01-design/PROXY_ROUTER_APPLICATION_REQUIREMENTS.md) | **代理与路由数据面需求**：DNS代理、UDP端口租约、NAT、透明代理和三层转发边界 |
| [NETWORK_FLOW_SERIAL_AND_FORWARDING_DATA_PLANE_DESIGN.md](01-design/NETWORK_FLOW_SERIAL_AND_FORWARDING_DATA_PLANE_DESIGN.md) | **内部流跟踪设计**：uint流水号、UDP端口租约、DNS/NAT/代理及虚拟网卡数据面抽象 |
| [NETWORK_FLOW_SERIAL_IMPLEMENTATION_PLAN.md](01-design/NETWORK_FLOW_SERIAL_IMPLEMENTATION_PLAN.md) | **基础批次已验证**：流水号、数据面合同、System Socket UDP池、端点迁移和验证门禁 |

### 02 — 端点（实现层）

| 文档                                                        | 内容                                      |
| ----------------------------------------------------------- | ----------------------------------------- |
| [ENDPOINT_BASE.md](02-endpoints/ENDPOINT_BASE.md)           | 基类 API 参考、虚方法钩子、事件模型       |
| [TRACKED_REQUEST_REPLY.md](02-endpoints/TRACKED_REQUEST_REPLY.md) | 请求跟踪、四级GUID身份、三次重试与多响应窗口 |
| [SOCKET_EXECUTION_3_0.md](02-endpoints/SOCKET_EXECUTION_3_0.md) | Networks 3.0 Automatic/Direct Socket执行、Windows路由快照与逐维证据 |
| [HTTP_DOH_3_0.md](02-endpoints/HTTP_DOH_3_0.md) | Networks 3.0 HTTP/DoH协议身份、连接池与RouteAdapter实施状态 |
| [MULTI_RESPONSE_RECOVERY_3_0.md](02-endpoints/MULTI_RESPONSE_RECOVERY_3_0.md) | Networks 3.0多响应窗口、迟到响应与Windows恢复原语 |
| [WFP_EXACT_NEXT_HOP_3_0.md](02-endpoints/WFP_EXACT_NEXT_HOP_3_0.md) | Networks 3.0 WFP请求级精确下一跳、隔离键与清理合同 |
| [WFP_WINDOWS_VALIDATION_RUNBOOK.md](02-endpoints/WFP_WINDOWS_VALIDATION_RUNBOOK.md) | 管理员真实WFP加入、并发网关与崩溃清理验收手册 |
| [DOH_PRECISION_ROUTING_GUIDE.md](02-endpoints/DOH_PRECISION_ROUTING_GUIDE.md) | DoH保留服务域名并指定底层IP、网卡和代理的完整用法 |
| [BINARY_PRIMITIVES.md](02-endpoints/BINARY_PRIMITIVES.md)   | 固定 IP、MAC 与错误值原语                 |
| [ADDRESS_VALUE_TYPES.md](02-endpoints/ADDRESS_VALUE_TYPES.md) | IP/MAC 可空数值、分类、比较、二进制与 JSON 合同 |
| [TCP_UDP_PING.md](02-endpoints/TCP_UDP_PING.md)             | TcpConnect、UdpDatagram、Ping 端点        |
| [NETWORKS_3_0_PING_TERMINAL_BETA_TEST_GUIDE.md](03-reference/NETWORKS_3_0_PING_TERMINAL_BETA_TEST_GUIDE.md) | 精确接口IPv6组播有响应/零响应三轴β验证 |
| [NAME_RESOLUTION.md](02-endpoints/NAME_RESOLUTION.md)       | DnsReverseLookup、NetBiosName 端点        |
| [LOCAL_TABLES.md](02-endpoints/LOCAL_TABLES.md)             | WindowsArpTable、WindowsIpv6Neighbor 端点 |
| [PROCESS_POWERSHELL.md](02-endpoints/PROCESS_POWERSHELL.md) | ProcessCommand、PowerShell 端点           |

### 03 — 参考

| 文档                                                      | 内容                                     |
| --------------------------------------------------------- | ---------------------------------------- |
| [SOURCE_MAP.md](03-reference/SOURCE_MAP.md)               | 源码索引                                 |
| [LOGICAL_CONTRACTS.md](03-reference/LOGICAL_CONTRACTS.md) | 逻辑约定总表（命名、错误处理、线程安全） |
| [NETWORKS_3_0_API_MIGRATION_MATRIX.md](03-reference/NETWORKS_3_0_API_MIGRATION_MATRIX.md) | Networks 2.0.1公共面、三方调用点与3.0强制迁移归属 |
| [NETWORKS_3_0_IP_ADDRESS_VALUE_MIGRATION_GUIDE.md](03-reference/NETWORKS_3_0_IP_ADDRESS_VALUE_MIGRATION_GUIDE.md) | **IpAddressValue迁移手册**：4/16/17字节边界、显式地址族入口、原生结构与验收清单 |
| [NETWORKS_3_0_BETA4_UPGRADE_MIGRATION_REPORT.md](03-reference/NETWORKS_3_0_BETA4_UPGRADE_MIGRATION_REPORT.md) | **beta.4升级迁移报告**：完整源码审计、破坏性变化、消费者迁移与发布门禁 |
| [NETWORKS_3_0_BETA5_UPGRADE_MIGRATION_REPORT.md](03-reference/NETWORKS_3_0_BETA5_UPGRADE_MIGRATION_REPORT.md) | **beta.5升级迁移报告**：长期TCP策略生命周期、数据面转发边界与测试版升级方式 |
| [NETWORKS_3_0_UDP_DATA_PLANE_BETA_TEST_GUIDE.md](03-reference/NETWORKS_3_0_UDP_DATA_PLANE_BETA_TEST_GUIDE.md) | UDP长期池、流水号、端口证据与数据面替换的工程β测试清单 |
| [IWESUN_NETWORKS_1_1_0_RELEASE_NOTES.md](03-reference/IWESUN_NETWORKS_1_1_0_RELEASE_NOTES.md) | 1.1.0功能、兼容性、验证和交付说明 |
| [IWESUN_NETWORKS_1_2_0_RELEASE_NOTES.md](03-reference/IWESUN_NETWORKS_1_2_0_RELEASE_NOTES.md) | 1.2.0三级超时、尝试历史和总耗时上报 |
| [IWESUN_NETWORKS_1_3_0_RELEASE_NOTES.md](03-reference/IWESUN_NETWORKS_1_3_0_RELEASE_NOTES.md) | 1.3.0 UDP、PTR、NBNS跟踪升级与PowerShell合同统一 |
| [IWESUN_NETWORKS_2_0_0_RELEASE_NOTES.md](03-reference/IWESUN_NETWORKS_2_0_0_RELEASE_NOTES.md) | 2.0.0单一请求—响应规范与V1强制移除 |

当前候选状态见[RELEASE_STATUS.md](RELEASE_STATUS.md)，面向版本的变化记录见[CHANGELOG](../src/Iwesun.Runtime.Networks/CHANGELOG.md)。

## 跨库关系

Iwesun.Runtime.Networks 是独立的通用网络库，被DDNS Snap通过版本化NuGet包引用。本库**零业务耦合**，仅依赖`System.*`。

3.0迁移范围见[API迁移矩阵](03-reference/NETWORKS_3_0_API_MIGRATION_MATRIX.md)。HTTP、DoH、TCP、Ping、UDP、PTR和NBNS只保留`Network*Endpoint<TKey>`规范。

可运行的公共示例位于[`Iwesun.Runtime.Networks.Examples`](../samples/Iwesun.Runtime.Networks.Examples/README.md)。新消费者如果需要在DNS/DoH请求中
保留服务域名，同时指定实际目标IP、源地址、物理网卡或代理，应先阅读
[DoH精确底层路由使用指南](02-endpoints/DOH_PRECISION_ROUTING_GUIDE.md)，不要把HTTPS URL中的域名替换成IP。

| 消费者    | 仓库路径                 | 使用方式                       |
| --------- | ------------------------ | ------------------------------ |
| DDNS Snap | `D:\Git Space\Ddns Snap` | 网络扫描、连通性探测、地址验证 |
| Aether | `D:\Git Space\Aether` | 3.0 HTTP、DoH评分、精确直连与显式HTTP CONNECT适配器 |

## 文档状态约定

每个设计文档头部包含：

- **状态**：`CURRENT`（与代码一致）/ `ACCEPTED`（已批准合同）/ `READY_FOR_EXECUTION`（已批准实施计划）/
  `FOUNDATION_IMPLEMENTED_BETA_VALIDATION`（基础批次完成，等待工程β验证）/
  `SUPERSEDED_BY_ACCEPTED_FINAL_DESIGN`（已被最终设计取代）/ `DRAFT`（设计中）/ `ARCHIVED`（历史参考）
- **最后更新**：日期
- **源码参考**：对应的源文件路径
- **上游/下游**：文档导航链接

# Networks代理与路由数据面应用需求

> 状态：REQUIREMENTS_BASELINE  
> 最后更新：2026-07-22  
> 范围：通用UDP、DNS代理、NAT代理、透明代理与三层转发
> 技术实现：[内部流水号与转发数据面技术设计](NETWORK_FLOW_SERIAL_AND_FORWARDING_DATA_PLANE_DESIGN.md)

## 一、统一身份和内部传输键

对外仍只使用固定GUID父链：

```text
RequestId → AttemptId → BranchId → ResponseId
```

端口、Socket句柄、DNS ID、ICMP Token和五元组均为内部传输键，不得成为第二套公共请求身份。

内部高速索引统一使用`NetworkFlowSerial : uint`，由非泛型静态
`NetworkFlowSerialAllocator.GetNext()`在进程活动及迟到隔离可见窗口内原子分配。流水号不进入UDP报文，
不能代替端口、实际远端端点、协议Token或五元组完成回包匹配。

通用数据面内部至少区分：

```text
TransportPoolId
SocketGeneration
NetworkFlowSerial
LocalAddress + LocalPort
RemoteAddress + RemotePort
ProtocolToken
RequestId + AttemptId + BranchId
```

## 二、通用UDP端口租约

### 必须能力

- 长期保存预绑定UDP Socket，禁止正常请求每次创建和销毁Socket。
- 池按地址族、源地址、接口、路径提供者、路由作用域和包策略隔离。
- 端口为`0`时由系统分配，绑定完成后立即读取`LocalEndPoint`。
- 一个无协议Token的池槽同一时刻只归属一个Branch。
- 单播响应严格核对实际远端IP和端口。
- 错误来源数据报只计入受限诊断计数，继续等待有效响应。
- 终态后进入迟到隔离；隔离期内不得重新归属同一远端的新Branch。
- 路由快照过期、接口消失、源地址失效或池关闭时才销毁Socket。

### 发送证据

`TrySend`只表示请求进入FIFO。端口租约建立后必须发布可等待的发送证据：

```text
RequestId / AttemptId / BranchId
SocketGeneration
ActualLocalAddress / ActualLocalPort
ExpectedRemoteAddress / ExpectedRemotePort
ActualInterface
DispatchedAtUnixMs
```

## 三、DNS代理

DNS代理是第一个使用UDP端口池的正式应用，但不能把通用UDP的端口租约直接冒充DNS协议匹配。

### 客户端侧

- 支持IPv4/IPv6 UDP 53监听和TCP 53监听。
- 保存客户端地址、客户端端口、原始DNS Transaction ID、查询名、类型、类和请求标志。
- 同一客户端允许并发复用相同原始DNS ID；内部身份必须仍由RequestId区分。
- 响应时恢复客户端原始DNS ID，并返回原客户端端点。

### 上游侧

- 从上游Socket池取得端口租约。
- 为每个在途查询分配当前SocketGeneration范围内唯一的内部DNS ID。
- 匹配键固定为`SocketGeneration + UpstreamEndPoint + InternalDnsId`。
- 校验响应QR、Question、Opcode、源端点和最小报文结构，拒绝伪造或错配响应。
- UDP响应TC置位时，在同一Branch内执行TCP回退；不得新建RequestId。
- 支持普通DNS、DoT/DoH适配入口时保持相同Request身份，但传输证据分别记录。

### 运行要求

- 有界Pending、ID耗尽背压、超时、取消、迟到响应和上游健康状态。
- 正缓存、负缓存和TTL边界；不得缓存格式错误或身份不匹配响应。
- 分流策略可按域名、查询类型、客户端、接口和上游组选择，但不得改变已冻结Branch计划。
- 支持EDNS0大小限制、截断、TCP回退和DNSSEC载荷透传；首版不承担递归解析器职责。

## 四、四层NAT与代理

### UDP NAT/转发

- 内部连接跟踪键为协议、源/目的地址端口、接口/作用域和映射Generation。
- 支持端点独立、地址依赖或地址端口依赖映射策略时必须显式声明。
- 映射超时、端口池耗尽、迟到包、Hairpin、广播/组播和ICMP错误分别建模。
- 对外RequestId可以代表一次映射或一次业务操作，但不能替代连接跟踪五元组。

### TCP代理

- 区分TCP连接探测与长期双向字节流代理。
- 保存客户端腿和出口腿两个独立端点及证据。
- 明确半关闭、RST、空闲超时、背压、零拷贝边界和连接池复用规则。
- HTTP CONNECT、SOCKS和透明TCP代理不得共享含义不清的RouteAdapter能力。

## 五、透明IP代理与三层转发

普通Socket只能代理明确发送到本程序地址/端口的流量。若要保留原始目的地址、透明截获任意进程流量或转发完整IP包，
必须进入Windows包过滤/注入层，不能伪装成普通UDP Endpoint。

### 透明代理需要

- WFP重定向或Callout能力，取得原始源/目的端点和进程/隔离身份。
- 请求级策略拥有者、并发隔离、崩溃清理和恢复审计。
- 回注包必须避免重复捕获和代理循环。

### 三层转发需要

- 明确入口/出口接口、VRF或Compartment、路由表和下一跳。
- IPv4 TTL/IPv6 HopLimit递减及校验和更新。
- MTU、分片、PMTU和ICMP错误生成/转发。
- ARP与IPv6 ND、邻居状态、代理ARP/ND和链路本地Scope处理。
- ACL、防火墙、QoS、统计、速率限制和有界流表。
- IPv4 NAT44、IPv6路由、NPTv6/NAT66、NAT64/DNS64必须作为独立能力声明，不默认互相替代。
- 真正的L2帧转发、VLAN和MAC学习属于二层交换，不进入本三层合同。

## 六、应用场景清单

| 应用 | 最低数据面 | 普通Socket是否足够 |
| --- | --- | --- |
| 本机DNS转发器 | UDP端口池＋DNS ID映射＋TCP回退 | 是 |
| 指定上游DNS分流 | DNS代理＋精确访问计划 | 是 |
| 显式UDP端口转发 | UDP NAT映射表 | 是，客户端必须访问代理端点 |
| 显式TCP代理/SOCKS/CONNECT | 双腿TCP会话 | 是 |
| 多出口策略代理 | Socket池＋源/接口/下一跳策略 | 部分；精确下一跳可能需要WFP |
| 透明进程代理 | 原始目的地址截获与重定向 | 否，需要WFP |
| NAT路由器 | 完整连接跟踪与包重写 | 否，通常需要WFP/虚拟接口/系统NAT |
| 三层交换/子网转发 | 完整IP包转发、邻居和路由控制面 | 否，需要包层后端 |
| VPN/TUN出口 | 虚拟接口＋加解封装＋路由 | 否，需要虚拟接口后端 |

## 七、实施顺序

1. 修复现有通用UDP单播实际远端严格匹配。
2. 建立长期UDP Socket池、端口租约、发送证据与迟到隔离。
3. 实现DNS UDP监听、内部ID映射、上游分发和TCP回退。
4. 建立通用UDP NAT映射合同和双向转发。
5. 建立TCP长期双腿代理合同。
6. 冻结WFP透明代理后端边界。
7. 最后规划三层包转发、邻居、MTU、ICMP和NAT族能力。

# 精确网络访问控制总体规划

> **状态**: SUPERSEDED_BY_ACCEPTED_FINAL_DESIGN
>
> **最后更新**: 2026-07-22
>
> **目标版本**: Iwesun.Networks 3.0.0
>
> **关系**: 本文扩展[精确路由语义优化 RFC](ROUTING_SEMANTICS_OPTIMIZATION.md)，规划完整的出站精确访问表达、执行与证据合同

> **最终合同候选**: [Iwesun.Networks 3.0精确网络访问控制最终设计](PRECISION_NETWORK_ACCESS_CONTROL_FINAL_DESIGN.md)

> **说明**: 征求意见已结束；本文仅保留总体规划依据，不再接收或定义实施语义

## 已提交的正式回复

- [DDNS Snap正式回复](rfc-responses/DDNS_SNAP_PRECISION_NETWORK_ACCESS_CONTROL_RESPONSE.md)：有条件接受；确认总体规划覆盖前序意见，并要求冻结完整的四级GUID父子链。
- [Aether正式回复](../../../../../Aether/docs/Networks精确网络访问控制总体规划征求意见回复.md)：确认覆盖，原则接受进入Networks 3.0合同冻结阶段。
- [Aether最终设计确认书](../../../../../Aether/docs/Networks3.0精确网络访问控制最终设计确认书.md)：接受最终设计候选并确认每个已创建Attempt至少一个Branch。

## 一、结论先行

精确访问不是一个`Route`枚举，也不是“目标IP + 源IP + 网卡”三个可空字段。完整合同必须同时解决：

1. 谁发起请求、如何绝对跟踪；
2. 访问哪个逻辑服务和传输目标；
3. 对源地址、接口、下一跳、端口和路径提供者施加哪些约束；
4. Windows用哪种能力执行这些约束；
5. 协议专属字段如何叠加；
6. 实际执行后能证明什么；
7. 无法执行或无法证明时如何明确失败。

总体方案采用“**统一GUID跟踪 + 分层访问计划 + 正交选择器 + 能力驱动执行 + 双轴结果证据**”。
不为每种组合创建独立端点或重载，也不使用`null`、空地址、接口索引`0`、默认枚举值或布尔标志组合推断意图。

## 二、统一GUID绝对跟踪合同

### 2.1 对外唯一根身份

- 所有公开请求、响应、失败、取消、超时、完成和迟到响应统一携带非空`Guid RequestId`。
- `RequestId`是一次公开操作的绝对根身份，也是Pending、重试、完成选择、移除和等待的唯一键。
- 推荐调用方在最靠近业务源头的位置创建`Guid`，使请求进入任何队列之前就可被日志、Runtime诊断和上层状态关联。
- 便捷工厂可以接收`Guid? requestId = null`并立即生成新值，但构造完成后的公开请求属性必须是非空、非`Guid.Empty`的`Guid`。
- 低层`Send`/`TrySend`收到`Guid.Empty`必须在入队前同步拒绝，禁止入队后异步生成或更换`RequestId`。调用方可能已用该GUID
  建立等待表、诊断关联和取消关系，底层换号会破坏绝对跟踪。
- `TKey`只保留为调用方自定义查询/关联数据，允许重复，永远不能代替`RequestId`。

### 2.2 子过程也使用GUID并显式建立父子关系

| 身份 | 用途 | 父身份 |
| --- | --- | --- |
| `RequestId` | 一次公开请求或恢复动作 | 无 |
| `AttemptId` | T1/T2/T3中的一次外层尝试 | `RequestId` |
| `BranchId` | 一条实际执行路径；每个Attempt至少创建一个，竞速时创建多个 | `AttemptId` |
| `ResponseId` | 一条独立网络响应或观测 | `BranchId` |

权威父链固定为：

```text
RequestId
└─ AttemptId
   └─ BranchId
      └─ ResponseId
```

普通请求也必须为每次Attempt创建唯一`BranchId`，不存在“无分支响应”。每个子对象可以为了查询和序列化冗余携带
全部祖先GUID，但直接父关系必须唯一且非空。序号仍可用于显示顺序和紧凑存储，不能替代GUID；不允许使用`null`、
`Guid.Empty`、源/目标地址、DNS TransactionId、ICMP序列号、NAT五元组或响应顺序代替任何一级身份。

Branch级失败和取消携带`BranchId`；Attempt聚合终态携带`AttemptId`并列出Branch；Request最终终态携带`RequestId`
并引用最终Attempt；迟到响应保留原`ResponseId`和完整祖先链。竞速分支失败不能伪装成新的业务请求。

## 三、分层模型

```text
NetworkRequestEnvelope
├─ RequestId / TKey                 绝对跟踪与用户查询
├─ LogicalServiceIdentity           URL、Host、SNI、业务服务器名
├─ ProtocolRequest                  TCP、UDP、ICMP、DNS、HTTP等协议数据
├─ NetworkAccessPlan                通用出站访问控制
│  ├─ PathProvider                  Automatic / Direct / RouteAdapter / SystemProxy
│  ├─ DestinationSelection          传输目标选择
│  ├─ SourceSelection               本地源地址选择
│  ├─ InterfaceSelection            出口接口选择
│  ├─ NextHopSelection              下一跳/网关选择
│  ├─ LocalEndpointSelection        本地端口选择
│  ├─ RouteScopeSelection           路由作用域/compartment约束
│  └─ IpPacketPolicy                IP层发送策略
├─ ProtocolAccessOptions            Resolver、Host/SNI、组播、多响应等协议扩展
└─ AttemptPolicy                    超时、重试、竞速、取消
```

这几层不得互相代替。HTTP的Host/SNI不是路由字段；DNS要反查的IP不是Resolver地址；`RequestId`不是NAT映射键；
指定源地址也不能自动等同于指定物理网卡。

## 四、统一选择器代数

每个维度使用自己的强类型选择器，但遵循同一组选择形态：

| 选择形态 | 含义 | 失败语义 |
| --- | --- | --- |
| `SystemSelected` | 明确授权平台在其他已声明约束内选择 | 可以成功但证据不完整 |
| `Exact<T>` | 必须使用指定值 | 不能执行或证明时拒绝/失败 |
| `AllowedSet<T>` | 只允许集合中的值，由平台或策略选择 | 集合外即违反约束 |
| `PreferredSet<T>` | 在候选集合内按独立竞速/偏好策略选择 | 保存全部已启动分支证据 |
| `ExcludedSet<T>` | 明确禁止一组值，例如虚拟接口 | 无法证明未使用时不得称为精确成功 |
| `Derived` | 从另一已解析维度推导，例如由源地址解析接口 | 推导无结果或多义时拒绝 |
| `NotApplicable` | 当前协议确实不存在该维度 | 只能由协议合法性矩阵允许 |

所有选择器的`Unspecified = 0`永久非法。`SystemSelected`是显式值，不由`null`、空集合、空地址或数字`0`代替。
选择器之间可以组合，但不得形成循环依赖，例如“源地址从接口推导，同时接口从源地址推导”。

## 五、通用访问维度

### 5.1 路径提供者

| 值 | 含义 |
| --- | --- |
| `Automatic` | 使用端点定义的平台默认网络栈政策；不附加精确路径承诺 |
| `Direct` | 禁止系统代理和登记适配器，按逐维约束直接访问 |
| `RouteAdapter` | 使用指定适配器，并要求适配器能力覆盖当前协议 |
| `SystemProxy` | 明确授权系统代理；只适用于具有系统代理语义的应用协议 |

顶层模式只决定路径提供者，不吞并其他维度。`Direct`不是“三项全部固定”，也不是`Automatic`的空值替代品。

路径提供者还决定通用约束作用于哪一段网络路径：

| 提供者 | 通用约束的作用段 | 允许的精确约束 | 不能证明的部分 |
| --- | --- | --- | --- |
| `Automatic` | 本机到请求目标 | 目标来自请求；源、接口、下一跳均为`SystemSelected` | 不承诺指定物理路径 |
| `Direct` | 本机到实际传输目标 | 支持目标、源、接口、下一跳和本地端点的正交组合 | 后端不支持的组合必须拒绝 |
| `RouteAdapter` | 本机到适配器入口，以及适配器声明可证明的出口段 | 本地段与适配器出口段分别建模 | 适配器未报告的出口接口/网关 |
| `SystemProxy` | 本机到系统代理入口 | 只允许平台能观察的本地段约束 | 系统代理后的实际出口物理路径 |

`Automatic`不能同时携带`ExactSource`、`ExactInterface`或`ExactNextHop`；出现这些要求时应使用`Direct`。
本地端口和IP层政策可以按端点能力独立应用，但不会因此产生精确路由证明。

代理和适配器路径必须按Leg保存证据，不能把客户端到代理的接口当成代理到最终目标的接口：

```text
AccessPathEvidence
├─ ClientLeg       本进程 -> 系统代理/登记适配器/最终目标
└─ EgressLeg       代理或适配器 -> 最终目标（仅在适配器能力允许时存在）
```

如果调用方要求精确控制`EgressLeg`，系统代理不满足要求；必须选择能够接收出口约束并返回出口证据的`RouteAdapter`。

### 5.2 传输目标

建议策略：

- `RequestTarget`：使用协议请求携带的字面IP或由端点解析逻辑名称；
- `ExactAddress`：固定实际连接/发送目标IP；
- `AllowedAddressSet`：只允许名称解析结果落入指定集合；
- `PreferredAddressSet`：Happy Eyeballs或候选IP竞速，策略与候选集合分层。

逻辑Host、URL、TLS SNI和证书校验名继续保存在协议请求中。固定传输IP不得改写逻辑服务身份。

### 5.3 本地源地址

建议策略：

- `SystemSelected`；
- `ExactAddress`；
- `AllowedSet` / `PreferredSet`；
- `DerivedFromInterface`：只在已确定接口的地址集合中选择。

源地址必须属于当前地址族并处于可用状态。临时IPv6地址、Preferred/Deprecated状态、地址生命周期和是否允许
隐私地址作为候选过滤条件，而不是另一个模糊布尔标志。

### 5.4 物理/逻辑接口

Windows接口具有Index、LUID、GUID、Alias等不同身份。接口索引在禁用/启用后可能变化，不能作为长期配置身份。

建议支持：

- `SystemSelected`；
- `ExactInterface`，输入可为LUID、GUID、Index或Alias，发送前统一解析成规范接口快照；
- `AllowedSet` / `ExcludedSet`；
- `DerivedFromSource`；
- `DerivedFromNextHop`；
- 接口属性约束，如物理/虚拟、OperationalStatus、地址族能力，但不能用名称猜测虚拟接口。

请求只指定网卡时，其他维度仍须显式写为系统选择：

```text
Direct(
    Destination = RequestTarget,
    Source      = SystemSelectedWithinInterface,
    Interface   = ExactInterface,
    NextHop     = SystemSelectedWithinInterface)
```

### 5.5 下一跳/路由网关

建议策略：

- `SystemSelected`；
- `ExactNextHop`；
- `OnLink`；
- `AllowedSet` / `ExcludedSet`；
- 可选的路由属性约束：目标前缀、路由来源、最大Metric、接口Metric、是否默认路由。

请求只指定网关时：

```text
Direct(
    Destination = RequestTarget,
    Source      = SystemSelectedForResolvedRoute,
    Interface   = DerivedFromNextHop,
    NextHop     = ExactNextHop)
```

Windows普通Socket绑定可以指定源地址或出口接口，但不能把“配置里写了一个网关”当成数据包已经经过该网关。
Windows Filtering Platform的连接策略可对出站连接设置SourceAddress、NextHopInterface和NextHop；这是实现
“只指定网关”的候选后端，但它具有权限、策略生命周期、并发隔离和系统状态影响，必须能力检测并以请求范围安全管理。
如果当前后端不能隔离并证明`ExactNextHop`，请求必须拒绝；Networks不得为单次请求静默添加或修改全局路由表。

WFP后端只有在以下条件全部验证后才能声明支持：同目标不同网关的并发请求不会串线；策略匹配范围不会污染同进程其他连接；
取消、超时、异常、连接池回收和进程崩溃后具有确定清理或恢复机制；权限不足在发送前拒绝；`ProofKind`能区分查询推断与
实际策略强制。若请求级WFP策略与连接复用不能同时保证，相关连接必须标记不可复用。

### 5.6 本地端点与端口

建议策略：

- `Ephemeral`：由系统分配；
- `ExactPort`：必须绑定指定端口；
- `AllowedRange`：从调用方允许的范围分配；
- `ReservedLease`：使用独立端口/地址资源管理器授予的租约。

单次Socket绑定属于本RFC；长期端口预留、NAT映射、保活、专用源IP池占有与释放另立资源管理RFC。
任何内部NAT键或五元组均不得取代对外`RequestId`。

### 5.7 路由作用域与Compartment

建议先建模为能力受限维度：`CurrentScope`或`ExactCompartment`。Windows公开的线程Compartment切换API被官方标记为
保留、不得使用，因此首版不能靠切换共享线程Compartment实现。只有经审查的WFP/平台后端可以声明支持精确Compartment；
其他后端在收到`ExactCompartment`时发送前拒绝。

### 5.8 IP层数据包政策

这些字段影响发送行为，但不属于路径身份：

- IPv4 TTL / IPv6 HopLimit；
- Don't Fragment、用户MTU；
- DSCP/TrafficClass（平台允许时）；
- 广播发送许可；
- 组播出口接口、TTL/Hops和Loopback；
- IPv4/IPv6单栈或双栈政策。

端点按协议公开支持项。未知或不支持的必需选项必须拒绝，不能忽略。

## 六、组合数量与系统表达

组合不是一个可以长期写死的有限清单。假设只取路径提供者、目标、源地址、接口和下一跳五个维度，且后四个维度
各只有三种选择形态，理论结构组合已经达到`4 × 3⁴ = 324`种；加入本地端口、集合选择、接口排除、地址竞速、
DNS Resolver、组播和尝试策略后会迅速增长，具体IP、端口和集合值更使值空间近似无限。

因此正式API应表达“选择器的笛卡尔积 + 合法性规则”，而不是为组合命名：

1. 每个维度必须有显式选择器；
2. 协议矩阵先删除不适用组合；
3. 约束解析器正规化LUID/Index/ScopeId等身份；
4. 检测冲突、循环派生和无解集合；
5. 平台能力匹配决定执行后端；
6. 生成不可变`ResolvedAccessPlan`；
7. 执行时不得改变顶层模式或放宽Required约束。

典型组合：

| 需求 | 目标 | 源地址 | 接口 | 下一跳 |
| --- | --- | --- | --- | --- |
| 全部系统选择 | `RequestTarget` | `SystemSelected` | `SystemSelected` | `SystemSelected` |
| 只固定网卡 | `RequestTarget` | `Derived/SystemWithinInterface` | `ExactInterface` | `SystemWithinInterface` |
| 只固定网关 | `RequestTarget` | `SystemForResolvedRoute` | `DerivedFromNextHop` | `ExactNextHop` |
| 只固定源地址 | `RequestTarget` | `ExactAddress` | `DerivedFromSource` | `SystemForResolvedRoute` |
| 固定目标与网关 | `ExactAddress` | `SystemForResolvedRoute` | `DerivedFromNextHop` | `ExactNextHop` |
| 固定物理路径 | `ExactAddress` | `ExactAddress` | `ExactInterface` | `ExactNextHop`或`OnLink` |
| 排除TUN/VPN | `RequestTarget/Exact` | `SystemSelected` | `ExcludedSet` | `SystemSelected` |
| IPv6源地址竞速 | `ExactAddress` | `PreferredSet` | `DerivedFromSource` | `SystemForEachBranch` |

## 七、Windows执行后端规划

| 后端 | 可以执行/观察 | 不能假定 |
| --- | --- | --- |
| Socket bind + `IP_UNICAST_IF` / `IPV6_UNICAST_IF` | 源地址、出口接口、本地端口、部分IP选项 | 不能单独证明指定下一跳 |
| `GetBestRoute2` / 路由表快照 | 解析候选源地址、接口、下一跳、前缀和Metric | 查询结果不是发送后事实，路由可能变化 |
| `SIO_ROUTING_INTERFACE_QUERY` | 查询给定目标的首选本地接口地址 | 不能持久保证后续路由不变 |
| WFP Connection Policy | 按应用、用户、本地/远端地址、端口、协议等条件设置源地址、出口接口、下一跳 | 需要能力、权限、生命周期和并发隔离设计 |
| `RouteAdapter` | HTTP CONNECT、SOCKS、VPN/隧道等登记路径 | 能力必须声明；字节流不能冒充数据报 |
| `SystemProxy` | 平台代理感知的HTTP/DoH等应用协议 | 不证明代理出口的物理接口或网关 |
| 全局路由表修改 | 可能改变机器选路 | 不作为单次请求的隐式实现；必须另立受控管理合同 |

执行后端是内部能力，不应迫使消费者理解Win32细节；但公开结果必须说明使用的后端和证据来源。

## 八、协议专属访问扩展

### 8.1 DNS与PTR

DNS请求必须分开：

- 查询对象：域名、记录类型或需要反查的IP；
- Resolver选择：`SystemConfigured`、`ExactResolver`、`AllowedResolverSet`、`PreferredResolverSet`；
- Resolver传输：UDP、TCP、UDP截断后保持同路径TCP回退、DoH等；
- Resolver自身的目标、源地址、接口、下一跳约束；
- DNSSEC、EDNS、超时与重试等协议政策。

不指定DNS服务器时必须显式选择`SystemConfigured`，不能用空Resolver地址表示。指定Resolver但不指定网卡时，
接口和源地址按通用访问计划的显式`SystemSelected`处理。PTR查询对象IP不能复用Resolver目标字段。

### 8.2 HTTP与DoH

- URL、Host、SNI、证书校验名属于逻辑/协议请求；
- 传输目标可由名称解析或`ExactAddress`覆盖；
- 连接池和`HttpClient`缓存键必须包含完整ResolvedAccessPlan，不能让不同接口、网关、源地址或适配器共享连接；
- 连接池键使用ResolvedAccessPlan的**稳定访问语义摘要**：包含会改变路径或安全边界的目标、源地址、接口、下一跳、
  路径提供者、适配器身份与能力版本、代理Leg、Resolver结果及Host/SNI隔离要求；排除`RequestId`、`AttemptId`、
  `BranchId`、时间戳、超时、重试次数和Actual观测值等瞬时字段；
- `SystemProxy`、显式`RouteAdapter`和`Direct`证据必须区分。

### 8.3 TCP、UDP与ICMP

- TCP：目标端口、源端口、连接重用、KeepAlive是传输政策；
- UDP：单次交换、NAT租约和多响应是不同生命周期；
- ICMP：标识、序列、TTL/HopLimit和载荷属于协议字段；代理模式通常不适用；
- UDP/ICMP只有平台实际支持的接口和数据包选项才能进入能力矩阵。

### 8.4 广播、组播和NBNS

请求显式选择：

- `FirstValidResponse`；
- `CollectUntilWindowEnds`，带接收窗口和最大响应数；
- 持续发现使用独立Listener/Subscription合同。

每条观测即时发布并携带`RequestId + AttemptId + BranchId + ResponseId`，窗口结束后发布唯一聚合终态。普通单路径
请求与多分支、多响应请求使用同一身份结构。组播成员关系、出口接口、Hops和Loopback均为显式协议/IP层政策。

## 九、请求解析、执行和证据

```text
RequestedAccessPlan
    调用方声明的所有非空选择器
        ↓ 正规化、冲突检查、能力匹配
ResolvedAccessPlan
    唯一执行后端、目标、源、接口、下一跳、端口和协议扩展
        ↓ 每次Attempt/Branch执行
ActualAccessEvidence
    实际观测、API绑定证明、WFP策略证明、适配器证明或不可取得
```

每份`ActualAccessEvidence`归属于具体`BranchId`。Attempt级和Request级只能聚合Branch证据，不能重新制造一个没有
实际执行路径来源的“统一实际路径”。代理的ClientLeg和EgressLeg也归属于产生它们的同一Branch。

结果采用双轴：

| 轴 | 候选值 |
| --- | --- |
| `ProtocolOutcome` | `Succeeded`、`Rejected`、`TimedOut`、`Cancelled`、`TransportFailed`、`ProtocolFailed` |
| `AccessCompliance` | `Satisfied`、`Violated`、`EvidenceIncomplete`、`NotApplicable` |

每个维度分别返回Requested、Resolved、Actual和`ProofKind`：

- `Observed`：从实际Socket、响应或平台观测取得；
- `ApiBinding`：底层API明确接受并执行绑定；
- `PolicyEnforced`：受控平台策略后端确认施加；
- `AdapterReported`：登记适配器按能力合同报告；
- `Inferred`：仅推导，不满足Required精确约束；
- `Unavailable`：无法取得。

协议成功不等于访问约束满足。任何`Exact`或Required排除约束无法执行、违反或无法证明时，都不能标记精确访问成功。

## 十、发送前固定校验顺序

1. 校验非空`RequestId`；`Guid.Empty`在进入队列前同步拒绝，不允许低层正规化换号；
2. 拒绝所有`Unspecified`和旧哨兵表达；
3. 校验协议字段和地址族；
4. 校验路径提供者与端点能力；
5. 正规化接口Index/LUID/GUID/Alias与IPv6 ScopeId；
6. 解析Derived选择器并检测循环；
7. 求解目标、源、接口、下一跳和本地端点约束；
8. 选择能同时执行全部Required约束的后端；
9. 生成不可变ResolvedAccessPlan、`AttemptId`以及该Attempt至少一个非空`BranchId`；每条实际发送路径独占一个Branch；
10. 若无安全且可证明的执行方案，在网络发送前以固定原因拒绝。

任何阶段都不得通过改成`Automatic`、换代理、换网卡、忽略网关或放宽集合来“帮助成功”。需要备用路径时，上层以新的
`RequestId`明确提交新请求，或者使用预先冻结的多路径计划合同。

重试冻结的是RequestedAccessPlan，而不是上一次解析出的具体成员。新的Attempt不得改变PathProvider、选择器类型、
Required约束或规范候选集合，但允许在原冻结计划内重新解析地址、接口和路由。`PreferredSet`可以在同一规范集合内选择
不同成员，每条实际执行路径都创建新的`BranchId`；超出原集合或放宽约束必须创建新的RequestId。

## 十一、公共能力查询

Networks 3.0应允许消费者在构造批量请求前查询：

- 当前平台支持的路径提供者；
- 每个端点支持的选择器和IP层选项；
- 精确源地址、接口、下一跳、本地端口和Compartment的执行/证明能力；
- 适配器的ByteStream、Datagram等能力；
- Windows恢复原语及权限状态；
- 某个具体RequestedAccessPlan能否解析，并返回不可满足原因。

能力查询只是当前快照，真正发送仍须重新校验，防止接口、地址和路由变化。

## 十二、明确移出本RFC的合同

- 入站Listener、Accept和服务器会话；
- 长期UDP NAT映射、保活和端口复用；
- 专用源IP池、端口池的占有、租约和释放；
- 修改、发布或持久化系统路由表；
- 防火墙放行、应用流量阻断和地址独占；
- 持续发现/订阅流；
- DDNS恢复顺序和业务状态机。

这些能力可以复用GUID层级、选择器、能力查询和证据原语，但必须另立生命周期与资源所有权合同。

## 十三、冻结前必须完成

- [ ] Runtime提交正式消费者回复；
- [x] DDNS Snap确认本文覆盖其前序主要意见，并有条件接受；
- [x] Aether确认总体规划覆盖其需求，原则接受进入Networks 3.0合同冻结阶段；
- [x] Aether确认“每个Attempt至少一个Branch”的统一四级链收紧规则；
- [ ] 冻结低层拒绝`Guid.Empty`及Request→Attempt→Branch→Response完整父链的具体类型与序列化合同；
- [ ] 冻结选择器类型、派生规则和冲突矩阵；
- [ ] 冻结集合去重、稳定排序、地址族校验和ResolvedAccessPlan稳定语义摘要；
- [ ] 冻结Windows Socket、路由查询和WFP后端能力边界；
- [ ] 冻结WFP并发隔离、取消/超时/异常/连接池回收清理及崩溃恢复合同；
- [ ] 对“只指定接口”“只指定网关”“只指定源地址”等组合建立固定测试；
- [ ] 冻结DNS/PTR Resolver合同和HTTP连接池隔离键；
- [ ] 冻结每个选择器维度可接受的ProofKind及AccessCompliance最严格聚合规则；
- [ ] 冻结多响应GUID与时间窗合同；
- [ ] 冻结迟到响应不重开已完成请求、不覆盖同TKey新请求的合同；
- [ ] 冻结Requested/Resolved/Actual证据结构和双轴终态；
- [ ] 另立Listener/NAT/专用地址端口资源RFC；
- [ ] 发布Networks 2.x到3.0的三方强制迁移表和真实网络验收矩阵。

完成前状态保持`REQUEST_FOR_COMMENTS`，不得创建半成品公共API。

## 十四、Windows官方能力依据

- [GetBestRoute2](https://learn.microsoft.com/en-us/windows-hardware/drivers/network/getbestroute2)：按目标、可选源地址和接口查询最佳路由及最佳源地址。
- [MIB_IPFORWARD_ROW2](https://learn.microsoft.com/en-us/windows-hardware/drivers/network/mib-ipforward-row2)：路由条目包含接口LUID/Index、目标前缀、下一跳和Metric等事实。
- [IPv4 Socket选项](https://learn.microsoft.com/en-us/windows/win32/winsock/ipproto-ip-socket-options)与[IPv6 Socket选项](https://learn.microsoft.com/en-us/windows/win32/winsock/ipproto-ipv6-socket-options)：出口接口、组播、接收接口、MTU和分片等能力。
- [Winsock Routing Interface Query](https://learn.microsoft.com/en-us/windows/win32/winsock/winsock-ioctls)：查询给定目标的首选本地接口，同时明确路由可能变化。
- [FwpmConnectionPolicyAdd0](https://learn.microsoft.com/en-us/windows/win32/api/fwpmu/nf-fwpmu-fwpmconnectionpolicyadd0)：按连接条件设置源地址、下一跳接口和下一跳。
- [SetCurrentThreadCompartmentId](https://learn.microsoft.com/en-us/windows/win32/api/netioapi/nf-netioapi-setcurrentthreadcompartmentid)：官方标记为保留且不得使用，不能作为公共实现基础。

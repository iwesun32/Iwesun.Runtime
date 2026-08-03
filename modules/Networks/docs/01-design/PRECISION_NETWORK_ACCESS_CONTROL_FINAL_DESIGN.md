# Iwesun.Runtime.Networks 3.0 精确网络访问控制最终设计

> **状态**: ACCEPTED
>
> **最后更新**: 2026-07-22
>
> **目标版本**: Iwesun.Runtime.Networks 3.0.0
>
> **效力**: 本文是精确访问控制的最终合同；旧RFC和总体规划保留为设计依据，不再作为实施规则来源
>
> **实施计划**: [Iwesun.Runtime.Networks 3.0精确网络访问控制实施计划](NETWORKS_3_0_IMPLEMENTATION_PLAN.md)

## 正式确认

- [DDNS Snap总体规划回复](rfc-responses/DDNS_SNAP_PRECISION_NETWORK_ACCESS_CONTROL_RESPONSE.md)：有条件接受，要求固定四级GUID链。
- [DDNS Snap最终设计确认书](rfc-responses/DDNS_SNAP_FINAL_DESIGN_CONFIRMATION.md)：接受最终设计，确认重试、PreferredSet和统一Branch规则。
- [Aether最终设计确认书](../../../../../Aether/docs/Networks3.0精确网络访问控制最终设计确认书.md)：接受最终设计候选，正式确认统一Branch规则。

## 一、最终目标

Networks 3.0提供统一的出站精确访问模型，使调用方可以独立、显式地控制或授权平台选择：

- 传输目标；
- 本地源地址；
- 出口接口；
- 下一跳/网关；
- 本地地址与端口；
- 路由作用域；
- 路径提供者；
- IP层发送政策；
- DNS Resolver、HTTP Host/SNI、多响应等协议专属访问政策。

系统必须回答四个不同问题：谁发起了请求、请求要求什么、平台实际执行了什么、取得的证据能证明什么。

最终架构为：

```text
统一GUID身份树
    + 非空正交访问选择器
    + 协议专属访问扩展
    + 能力驱动的Windows执行后端
    + Requested / Resolved / Actual逐维证据
    + ProtocolOutcome × AccessCompliance双轴终态
```

## 二、不可妥协规则

1. 所有公开跟踪身份使用非空`Guid`，不建立第二套内部身份。
2. 低层`Send`/`TrySend`在入队前拒绝`Guid.Empty`，不得入队后换号。
3. 每个Attempt至少创建一个Branch，普通单路径请求也不例外。
4. 固定父链为`RequestId → AttemptId → BranchId → ResponseId`。
5. `TKey`只用于允许重复的调用方查询，不参与Pending、匹配、重试、完成或移除。
6. 所有选择器非空；`Unspecified = 0`永久非法。
7. 不用`null`、空地址、接口索引`0`、空集合或布尔标志推断访问意图。
8. 任何Required约束无法执行或证明时，不得标记精确访问成功。
9. 请求意图、解析计划和实际证据必须分开，不用请求值冒充实际值。
10. 重试不得改变`PathProvider`、选择器类型、Required约束或规范候选集合；新的Attempt允许在同一冻结计划内重新解析和执行。
11. 单次请求不得静默修改全局路由表。
12. 当前2.0.1不引入半兼容类型；3.0一次性强制迁移全部消费者。

## 三、统一GUID身份树

### 3.1 权威父子关系

```text
RequestId
└─ AttemptId
   └─ BranchId
      └─ ResponseId
```

| 身份 | 直接父身份 | 生命周期 |
| --- | --- | --- |
| `RequestId` | 无 | 一次公开请求、恢复动作或能力执行 |
| `AttemptId` | `RequestId` | 一次外层尝试；每次重试创建新值 |
| `BranchId` | `AttemptId` | 一条实际执行路径；每个Attempt至少一个 |
| `ResponseId` | `BranchId` | 一条独立网络响应或观测 |

对象可冗余携带祖先GUID以便查询和序列化，但直接父身份唯一且非空。序号只用于排序或紧凑存储，不能代替GUID。

### 3.2 生成与拒绝规则

- 推荐调用方在业务源头生成`RequestId`。
- 便捷工厂可以接收`Guid?`，但必须在构造公开请求之前同步生成并返回最终GUID。
- 低层发送入口收到`Guid.Empty`立即返回固定拒绝原因，不得写入FIFO或Pending。
- Attempt开始前创建`AttemptId`；每条实际执行路径开始前创建`BranchId`。
- 单路径Attempt也创建唯一Branch。
- 每条完整响应进入接收边界时创建`ResponseId`。
- 发送前校验或计划求解失败的Request直接按`RequestId`返回Rejected，不创建虚假的AttemptId或BranchId。
- AttemptId一旦创建，就必须在第一条实际执行路径开始前创建至少一个BranchId；不存在零Branch的已开始Attempt。

### 3.3 分层终态

- Branch成功、失败、取消或被赢家终止，均产生Branch终态。
- Attempt聚合其全部Branch，产生唯一Attempt终态。
- Request聚合一次或多次Attempt，产生唯一Request终态。
- 广播、组播和NBNS的每条观测具有独立`ResponseId`，窗口只产生一个聚合终态。
- 迟到响应保留原完整父链，发布迟到事件，但不能重开已完成Request或覆盖同`TKey`的新Request。

## 四、最终请求分层

```text
NetworkRequestEnvelope
├─ RequestId / TKey
├─ LogicalServiceIdentity
├─ ProtocolRequest
├─ NetworkAccessPlan
│  ├─ PathProvider
│  ├─ DestinationSelection
│  ├─ SourceSelection
│  ├─ InterfaceSelection
│  ├─ NextHopSelection
│  ├─ LocalEndpointSelection
│  ├─ RouteScopeSelection
│  └─ IpPacketPolicy
├─ ProtocolAccessOptions
└─ AttemptPolicy
```

- `LogicalServiceIdentity`保存URL、Host、SNI、证书校验名和业务服务器名。
- `ProtocolRequest`保存端口、DNS查询对象、ICMP载荷等协议数据。
- `NetworkAccessPlan`只保存跨协议通用的访问路径控制。
- `ProtocolAccessOptions`保存Resolver、多响应、HTTP连接等协议专属访问控制。
- `AttemptPolicy`保存超时、重试、竞速并发和取消，不影响连接池稳定身份。

## 五、选择器合同

### 5.1 统一选择形态

每个维度使用独立强类型值，但只允许以下形态：

| 形态 | 含义 |
| --- | --- |
| `SystemSelected` | 平台在其他Required约束内选择 |
| `Exact<T>` | 必须使用唯一指定值 |
| `AllowedSet<T>` | 只能从规范集合中选择 |
| `PreferredSet<T>` | 在规范集合内按独立策略选择或竞速 |
| `ExcludedSet<T>` | 禁止使用规范集合中的值 |
| `Derived` | 从另一已解析维度推导 |
| `NotApplicable` | 协议矩阵明确认定不存在该维度 |

### 5.2 规范化

- 空集合、包含默认非法值的集合和地址族冲突在发送前拒绝。
- 集合必须去重并按稳定规则排序。
- `Derived`必须声明来源，静态依赖图不得成环。
- `SystemSelected`仍受Allowed/Excluded及其他Required约束限制。
- `NotApplicable`只能由端点合法性矩阵授权。
- 接口Index、LUID、GUID、Alias统一解析成不可变接口快照；长期配置不以易变Index作为唯一身份。
- IPv6 ScopeId最终规范为接口约束；与`ExactInterface`冲突时拒绝。

## 六、路径提供者

| 值 | 最终含义 | 约束作用段 |
| --- | --- | --- |
| `Automatic` | 明确授权平台默认选路，不提供精确物理路径承诺 | 本机到请求目标 |
| `Direct` | 禁止系统代理和登记适配器，按逐维选择器直连 | 本机到实际传输目标 |
| `RouteAdapter` | 使用指定且能力匹配的登记适配器 | ClientLeg及适配器可证明的EgressLeg |
| `SystemProxy` | 明确授权系统代理 | 只证明可观察的ClientLeg |

规则：

- `Automatic`的源地址、接口和下一跳必须为`SystemSelected`；有精确约束时使用`Direct`。
- `Direct`不要求目标、源、接口三项全部固定，允许任意合法组合。
- `RouteAdapter`必须声明`ByteStream`、`Datagram`等能力；能力不匹配时发送前拒绝。
- `SystemProxy`不能证明代理出口接口、源地址或网关。
- ClientLeg与EgressLeg分别返回证据，禁止跨Leg冒充。

## 七、通用访问维度

| 维度 | 主要策略 | 最终规则 |
| --- | --- | --- |
| 目标 | `RequestTarget`、`ExactAddress`、Allowed/Preferred地址集 | 逻辑Host/SNI不随传输IP改变 |
| 源地址 | System、Exact、Allowed/Preferred、DerivedFromInterface | 校验地址族、状态和生命周期 |
| 接口 | System、Exact、Allowed/Excluded、DerivedFromSource/NextHop | 规范化为稳定接口快照 |
| 下一跳 | System、`ExactNextHop`、`OnLink`、Allowed/Excluded | 无安全执行后端时拒绝 |
| 本地端点 | `Ephemeral`、`ExactPort`、`AllowedRange`、`ReservedLease` | 长期租约另立资源合同 |
| 路由作用域 | `CurrentScope`、`ExactCompartment` | 仅能力明确的后端支持Exact |
| IP政策 | TTL/HopLimit、DF/MTU、广播、组播接口/Hops/Loopback等 | 端点不支持时拒绝，不忽略 |

### 7.1 典型组合

| 需求 | 目标 | 源 | 接口 | 下一跳 |
| --- | --- | --- | --- | --- |
| 全部系统选择 | RequestTarget | System | System | System |
| 只指定网卡 | RequestTarget | DerivedWithinInterface | ExactInterface | SystemWithinInterface |
| 只指定网关 | RequestTarget | DerivedFromRoute | DerivedFromNextHop | ExactNextHop |
| 只指定源地址 | RequestTarget | ExactAddress | DerivedFromSource | SystemForRoute |
| 固定目标与网关 | ExactAddress | DerivedFromRoute | DerivedFromNextHop | ExactNextHop |
| 固定完整物理路径 | ExactAddress | ExactAddress | ExactInterface | ExactNextHop/OnLink |
| 排除TUN/VPN | RequestTarget/Exact | System | ExcludedSet | System |
| IPv6源竞速 | ExactAddress | PreferredSet | DerivedFromSource | SystemForBranch |

实现使用“选择器笛卡尔积 + 合法性矩阵 + 约束解析器”，不为组合创建大量重载。

## 八、协议专属合同

### 8.1 DNS与PTR

必须分离：

- 查询对象；
- Resolver选择：SystemConfigured、ExactResolver、Allowed/PreferredResolverSet；
- Resolver传输：UDP、TCP、同路径UDP→TCP回退、DoH；
- Resolver自身的目标、源、接口和下一跳计划。

PTR查询对象IP与Resolver目标是两个字段。Direct PTR必须明确Resolver；TCP回退保持同一Resolver和访问计划。

### 8.2 HTTP与DoH

- URL、Host、SNI和证书校验名属于协议身份。
- 目标选择器决定实际连接IP。
- 连接池键使用稳定访问语义摘要。
- 摘要必须包含目标、源、接口、下一跳、路径提供者、适配器身份及能力版本、Leg、Resolver结果和Host/SNI隔离要求。
- 摘要必须排除Request/Attempt/Branch/Response GUID、时间戳、超时、重试次数和Actual观测值。
- 请求级WFP策略不能安全复用连接时，该连接标记不可复用。

### 8.3 TCP、UDP与ICMP

- TCP目标/本地端口、KeepAlive和连接复用属于传输政策。
- UDP单次交换、长期NAT映射和持续接收是不同生命周期。
- ICMP标识、序列、载荷和TTL/HopLimit属于协议字段。
- SystemProxy和现有字节流RouteAdapter不适用于ICMP。

### 8.4 广播、组播和NBNS

请求明确选择：

- `FirstValidResponse`；或
- `CollectUntilWindowEnds`，包含窗口和最大响应数。

每条响应即时发布完整四级GUID链。窗口结束或达到上限产生唯一终态。持续发现使用独立Listener/Subscription合同。

## 九、Attempt与Branch执行模型

```text
Request Pending
  └─ Attempt 1
      ├─ Branch A -> responses/outcome
      └─ Branch B -> responses/outcome
          ↓ Attempt aggregate
  └─ Attempt 2（允许重试时）
      └─ new Branch -> responses/outcome
          ↓ Request terminal
```

- PreferredSet只是候选约束；并发数、启动间隔和赢家规则属于AttemptPolicy。
- 一次竞速的多个Branch不增加外层重试计数。
- 赢家产生后取消未完成Branch，但仍保存其取消终态。
- 新的外层重试创建新的AttemptId和全新BranchId。
- 新Attempt继承同一RequestedAccessPlan，不得改变PathProvider、选择器类型、Required约束或规范候选集合。
- 新Attempt可以重新解析已冻结计划，以适应地址状态、接口和路由变化；`PreferredSet`可以在原规范集合内选择与上一Attempt
  不同的成员，并为每条实际执行路径创建全新Branch。
- 需要增加候选、删除候选、换代理、改变精确约束或放宽排除集合时，调用方必须创建新的RequestId和新计划。
- 每个Branch保存独立Resolved计划、开始/结束时间、协议结果和访问证据。

## 十、Windows执行后端

| 后端 | 用途 | 约束 |
| --- | --- | --- |
| Socket bind与IP接口选项 | 源地址、接口、本地端点、部分IP政策 | 不单独证明下一跳 |
| `GetBestRoute2`与路由表 | 解析源、接口、下一跳、前缀和Metric | 只是解析快照，不是发送事实 |
| `SIO_ROUTING_INTERFACE_QUERY` | 查询给定目标的首选本地接口 | 路由变化后必须重验 |
| WFP Connection Policy | 精确源、出口接口和下一跳 | 需要权限、隔离、清理和证明 |
| `RouteAdapter` | 代理、隧道及登记路径 | 按声明能力执行并报告 |
| `SystemProxy` | 系统代理感知协议 | 不推断EgressLeg |

### 10.1 WFP冻结要求

- 同目标不同网关、源或接口的并发请求不能串线。
- 策略匹配范围不能污染同进程其他连接。
- 取消、超时、异常和连接池回收必须撤销请求级策略。
- 必须定义进程崩溃后的清理/恢复机制。
- 权限不足和平台不支持在发送前拒绝。
- 不以临时全局路由模拟单请求下一跳。
- ProofKind区分路由查询推断和WFP实际强制。

## 十一、Requested、Resolved与Actual证据

```text
RequestedAccessPlan
    ↓ 规范化、冲突校验、能力匹配
ResolvedAccessPlan
    ↓ Attempt / Branch执行
ActualAccessEvidence
```

每个维度都必须能逐项比较Requested、Resolved、Actual、差异原因和证明来源。Actual证据归属于Branch；Attempt和Request
只能聚合Branch证据。

### 11.1 ProofKind

| 值 | 含义 | 可否满足Required Exact |
| --- | --- | --- |
| `Observed` | 从实际连接、响应或平台观测取得 | 是 |
| `ApiBinding` | 底层API确认执行绑定 | 按维度矩阵决定 |
| `PolicyEnforced` | 受控平台策略确认强制执行 | 是 |
| `AdapterReported` | 登记适配器按能力合同报告 | 按能力矩阵决定 |
| `Inferred` | 根据查询或环境推导 | 否 |
| `Unavailable` | 无法取得 | 否 |

### 11.2 双轴终态

| 轴 | 值 |
| --- | --- |
| `ProtocolOutcome` | Succeeded、Rejected、TimedOut、Cancelled、TransportFailed、ProtocolFailed |
| `AccessCompliance` | Satisfied、Violated、EvidenceIncomplete、NotApplicable |

聚合规则：

1. 任一Required维度明确违反，AccessCompliance为`Violated`；
2. 没有违反但至少一项Required证据不足，为`EvidenceIncomplete`；
3. 全部Required维度满足，才是`Satisfied`；
4. 协议成功不能覆盖访问不合规；
5. 协议失败也不抹除已经取得的路径执行证据。

## 十二、固定校验流水线

1. 校验RequestId，空GUID入队前拒绝；
2. 拒绝Unspecified、旧哨兵值和非法集合；
3. 校验协议字段、地址族和协议矩阵；
4. 校验PathProvider和端点能力；
5. 规范化接口身份与IPv6 ScopeId；
6. 规范化集合并解析Derived依赖图；
7. 求解目标、源、接口、下一跳和本地端点；
8. 选择能执行全部Required约束的后端；
9. 生成不可变ResolvedAccessPlan、AttemptId及至少一个BranchId；
10. 执行Branch并采集逐维证据；
11. 聚合Branch、Attempt和Request终态。

任何阶段无解都使用固定原因码拒绝或失败，不得隐式降级。

## 十三、公共能力查询

能力查询必须返回：

- 平台和端点支持的PathProvider；
- 每个选择器、IP政策和协议扩展的支持情况；
- 所需执行后端；
- 缺失权限；
- 不满足的具体维度和固定原因码；
- RouteAdapter能力及版本；
- Windows恢复原语能力。

能力快照只用于预筛选；每次发送必须重新规范化和校验。

## 十四、恢复原语

Networks只提供Windows能力优先的公共原语及分步证据：

- Router Solicitation；
- DHCPv6 release；
- 等待；
- DHCPv6 renew；
- 网卡重启；
- 动作后的地址快照。

请求接受、系统动作成功、地址变化和恢复后连通性成功是四类结果。DDNS地址守护程序继续决定动作顺序和复检政策。

## 十五、明确排除

以下能力另立合同，不进入本出站访问计划：

- 入站Listener、Accept和服务器会话；
- 长期UDP NAT映射、保活和端口复用；
- 专用源IP池、端口池租约；
- 持久或临时修改系统路由表；
- 防火墙放行、阻断和地址独占；
- 持续发现/订阅流；
- DDNS业务恢复顺序。

## 十六、Networks 3.0迁移

1. 新建无版本后缀的正式请求、响应、身份、选择器、计划和证据合同。
2. 删除`Route = null`、默认枚举、空地址和接口索引`0`推断。
3. 删除低层自动替换`Guid.Empty`的行为。
4. 将Host/SNI从路由指令移回HTTP/DoH协议请求。
5. 将Preferred IPv6拆为Source PreferredSet与Attempt竞速策略。
6. 将所有结果升级为四级GUID链和双轴终态。
7. 迁移Runtime内嵌载荷、DDNS Snap和Aether。
8. 不提供旧别名、隐式转换或运行时静默兼容。
9. 编译错误用于发现全部遗漏调用点。
10. 固定测试和Windows真实网络验收全部通过后发布3.0.0。

## 十七、验收矩阵

### 17.1 固定测试

- Guid.Empty在发送入口同步拒绝且不进入FIFO/Pending。
- 普通、竞速和多响应请求均具有完整四级GUID链。
- 重复TKey请求只按各自RequestId闭环。
- 默认枚举、空集合、循环Derived、地址族冲突发送前拒绝。
- 只指定接口、源地址、网关及完整物理路径均有成功/失败测试。
- ExcludedSet命中为Violated，无法证明为EvidenceIncomplete。
- PreferredSet每个Branch有独立终态，赢家不改变RequestId。
- 迟到响应不重新完成Request。
- RouteAdapter能力不匹配发送前拒绝。
- DNS/PTR UDP→TCP回退保持Resolver和访问计划。
- HTTP/DoH不同稳定访问摘要不共享连接；不同RequestId但相同稳定摘要可以安全复用。
- WFP权限、隔离和清理错误具有固定原因。

### 17.2 Windows真实网络

- Clash/VPN/TUN环境下分别验证Automatic、排除虚拟接口和指定物理接口。
- 同一目标并发使用不同源、接口和下一跳，确认Socket/WFP/连接池不串线。
- 固定IPv4、IPv6、Preferred IPv6、SystemProxy和RouteAdapter分别验证。
- 运行中改变接口Index、地址状态和路由，旧Resolved计划不得冒充新事实。
- 取消、超时、异常退出和连接池回收后无残留WFP策略或端口资源。
- ClientLeg可见但EgressLeg不可见时只返回对应证据。
- ProtocolOutcome与AccessCompliance的所有关键组合稳定表达。

## 十八、实施与发布门槛

本文已停止征求意见并确认为`ACCEPTED`。以下事项不再是设计意见门槛，而是实施和发布门禁：

- 最终公共类型、二进制布局、合法性矩阵和固定原因码在实施M1冻结；
- WFP并发隔离、清理和崩溃恢复必须通过专项测试后才能声明支持ExactNextHop；
- Listener/NAT/地址端口资源管理建立独立RFC入口；
- Runtime、DDNS Snap、Aether按同一3.0候选包迁移；
- 固定测试、Windows真实网络测试和发布验收全部通过。

具体顺序、停线条件和交付物以[实施计划](NETWORKS_3_0_IMPLEMENTATION_PLAN.md)为准。

## 十九、设计依据

- [精确网络访问控制总体规划](PRECISION_NETWORK_ACCESS_CONTROL_MODEL.md)
- [精确路由语义优化RFC](ROUTING_SEMANTICS_OPTIMIZATION.md)
- [DDNS Snap总体规划正式回复](rfc-responses/DDNS_SNAP_PRECISION_NETWORK_ACCESS_CONTROL_RESPONSE.md)
- [DDNS Snap最终设计确认书](rfc-responses/DDNS_SNAP_FINAL_DESIGN_CONFIRMATION.md)
- [Aether总体规划正式回复](../../../../../Aether/docs/Networks精确网络访问控制总体规划征求意见回复.md)
- [Aether最终设计确认书](../../../../../Aether/docs/Networks3.0精确网络访问控制最终设计确认书.md)
- [GetBestRoute2](https://learn.microsoft.com/en-us/windows-hardware/drivers/network/getbestroute2)
- [MIB_IPFORWARD_ROW2](https://learn.microsoft.com/en-us/windows-hardware/drivers/network/mib-ipforward-row2)
- [IPv4 Socket选项](https://learn.microsoft.com/en-us/windows/win32/winsock/ipproto-ip-socket-options)
- [IPv6 Socket选项](https://learn.microsoft.com/en-us/windows/win32/winsock/ipproto-ipv6-socket-options)
- [Winsock Routing Interface Query](https://learn.microsoft.com/en-us/windows/win32/winsock/winsock-ioctls)
- [WFP Connection Policy](https://learn.microsoft.com/en-us/windows/win32/api/fwpmu/nf-fwpmu-fwpmconnectionpolicyadd0)

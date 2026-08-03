# Networks内部流水号与转发数据面技术设计

> **状态**：FOUNDATION_IMPLEMENTED_BETA_VALIDATION  
> **最后更新**：2026-07-22  
> **范围**：UDP跟踪、DNS代理、NAT/IP路由、HTTP代理、Socket代理、透明转发与三层数据面  
> **上游合同**：[精确网络访问控制最终设计](PRECISION_NETWORK_ACCESS_CONTROL_FINAL_DESIGN.md)  
> **应用需求**：[代理与路由数据面应用需求](PROXY_ROUTER_APPLICATION_REQUIREMENTS.md)  
> **终态合同**：[协议、访问与终态更正方案](NETWORKS_3_0_PROTOCOL_ACCESS_TERMINAL_CORRECTION_PLAN.md)

> **实施状态**：流水号、System Socket UDP池、UDP端点迁移和扩展合同已完成；DNS/NAT/代理/虚拟网卡真实后端仍按能力分批实施。

## 一、设计结论

Networks内部高速跟踪统一采用一个4字节流水号：

```text
NetworkFlowSerial : uint
```

正式发号入口统一为非泛型静态类：

```text
NetworkFlowSerialAllocator.GetNext()
```

`uint`是值类型，C#类或静态类不能继承它。因此使用`readonly record struct NetworkFlowSerial(uint Value)`形成强类型包装，
由静态分配器持有进程级计数器和占用表。禁止在不同泛型类型中各自建立静态计数器，避免`TKey`不同导致重复流水号。

流水号的唯一性边界固定为：

> 在当前Networks运行实例内，从分配成功开始，到活动流、迟到响应隔离和所有数据面引用释放为止，不得重复。

它不承诺跨进程重启、跨计算机或永久历史唯一。公开请求身份仍按当前已冻结合同处理；内部只在建流时建立一次
`Request/Attempt/Branch → NetworkFlowSerial`映射，逐包热路径不携带或生成GUID。

## 二、流水号类型与分配器

### 2.1 强类型值

计划中的类型形态如下，具体代码在实施批次中完成：

```csharp
public readonly record struct NetworkFlowSerial
{
    internal NetworkFlowSerial(uint value) => Value = value;
    public uint Value { get; }
    public bool IsValid => Value != 0;
}
```

规则：

- `0`固定表示未分配或无效；
- 不提供调用方任意写入的公共构造捷径；
- 不允许把Socket句柄、端口、DNS ID或数组下标直接转换成流水号；
- 网络字节序、共享内存和IPC编码统一为无符号32位值；
- 日志显示可以使用十进制，诊断转储可以同时显示8位十六进制。

### 2.2 唯一发号入口

`NetworkFlowSerialAllocator`是非泛型静态类，至少提供：

```text
GetNext()         原子分配并登记一个非零流水号
TryGetNext()      容量受限路径使用，不抛出容量异常
TryGet(flow)      查询活动或隔离中的流记录
BeginQuarantine() 终态后转入迟到隔离
Release()         隔离期和引用计数都归零后释放
```

`GetNext()`不是简单返回`++counter`。它必须完成：

1. 原子递增进程级游标；
2. 跳过`0`；
3. 在活动表和隔离表中原子登记；
4. 回绕后跳过仍被占用的值；
5. 32位空间没有可安全分配值时返回明确容量错误，不覆盖旧流。

当前实现使用“双索引内存流表”：`ConcurrentDictionary<uint, FlowRegistration>`作为流水号主键索引，
`PriorityQueue<QuarantineRegistration, long>`作为隔离到期索引。发号不再扫描全部活动记录；隔离登记和到期回收为
`O(log N)`，按流水号查询接近`O(1)`。游标由`Interlocked.Increment`原子递增，不额外获取全局锁。发号和闭环发现
到期积压时，按到期索引直接清空全部已到期记录，不保留固定批次窗口。

这里不直接使用`Iwesun.Runtime.Data`或RecordStore：Networks必须保持只依赖`System.*`，实时收发也不能承担持久化、
序列化和磁盘索引成本。需要保留的历史统计可以由上层在Flow闭环后异步批量写入Runtime Data，但不得参与在线分派判定。
逐包查询走Socket槽和内存索引，不执行全局表扫描、持久化查询或GUID哈希。

未到期记录不能因数量超过阈值而清零，否则会提前复用流水号或传输端点。流表总容量必须覆盖实际并发Flow；
达到配置容量时返回背压/容量错误，不牺牲匹配正确性。

System Socket UDP池按`StableAccessKey + RemotePort`独立限制槽位，默认每池最多64个活动或隔离槽。第65个Flow在发送前
返回`network-flow-capacity-reached`；不清除未到期隔离、不借用其他访问语义的池，也不复用仍被占用的端口。调用方可以
通过构造参数调整上限，但扩大容量必须同时评估可用本地端口、句柄和响应隔离窗口。

### 2.3 生命周期

统一状态机：

```text
Allocated → Binding → Active → Draining → Quarantine → Released
                  └──────────────→ Failed → Quarantine
```

- `Allocated`：流水号已占用，但尚未取得传输资源；
- `Binding`：正在绑定本地地址、端口、接口或数据面通道；
- `Active`：允许发送和接收；
- `Draining`：不接受新发送，等待已提交操作闭环；
- `Quarantine`：拒绝复用，专门吸收迟到响应和旧包；
- `Released`：占用解除，未来回绕时才允许重新使用。

取消、超时和异常都不得直接从`Active`跳到`Released`。

## 三、三类身份必须分开

### 3.1 业务与公共跟踪身份

用于调用方查询、跨线程事件和持久诊断：

```text
RequestId → AttemptId → BranchId → ResponseId
```

### 3.2 内部流流水号

用于当前进程或当前数据面通道内的高速定位：

```text
NetworkFlowSerial
```

一个Branch通常对应一个活动Flow；重试和竞速的新Branch必须取得新的流水号。一个长期代理监听器不是一条Flow，
每个被接受的TCP连接、UDP映射或DNS查询分别取得流水号。

### 3.3 线上传输匹配键

用于证明收到的数据实际属于哪条流：

```text
LocalAddress + LocalPort
RemoteAddress + RemotePort
SocketGeneration
ProtocolToken / DNS Transaction ID / ICMP Identifier
IP五元组与转发Zone
```

`NetworkFlowSerial`不会自动出现在UDP、IP或ICMP报文中，因此不能单独用于匹配回包。数据面必须先用线上可观察键找到Flow，
然后才使用流水号进入内部状态和公共身份映射。

## 四、统一数据面抽象

业务端点不得直接依赖`Socket`、WFP或未来虚拟网卡。统一抽象分为：

```text
INetworkDataPlane
├─ INetworkDatagramDataPlane
├─ INetworkStreamDataPlane
└─ INetworkPacketDataPlane
```

公共能力包括：

- 数据面身份、能力版本和运行Generation；
- IPv4/IPv6及ByteStream/Datagram/IPPacket能力；
- 源地址、接口、下一跳、本地端口和路由作用域支持等级；
- 流水号登记、发送证据、接收事件和终态清理；
- Ready、Degraded、Draining、Stopped状态；
- 活动流、端口租约、丢弃、迟到包和容量统计。

后端规划：

| 后端 | 当前/未来 | 主要用途 |
| --- | --- | --- |
| `SystemSocketDataPlane` | 首个实现 | DNS、显式UDP/TCP代理、HTTP代理、普通Socket转发 |
| `WfpDataPlane` | 后续 | 透明进程代理、精确下一跳、重定向与包回注 |
| `VirtualAdapterDataPlane` | 预留 | 独立虚拟网卡、VPN/TUN、完整IP包入口与出口 |
| `ExternalForwarderDataPlane` | 预留 | 独立服务、共享内存或命名管道连接的系统外转发程序 |

当前System Socket实现必须只实现这些抽象，不能把Socket对象泄漏给DNS、NAT或代理业务层。未来虚拟网卡后端注册相同能力接口，
上层请求、Flow记录、终态和证据合同不变。

跨进程或虚拟网卡场景中，流水号的作用域由已建立的数据面通道隐含确定；一个通道只有一个发号权威。当前Networks进程的
非泛型静态分配器始终负责本通道发号，外部转发器接收已经登记的流水号并以`Channel + NetworkFlowSerial`定位，不能另起
一套同域计数器。外部数据面发现新的入站流时先上报线上匹配键，由Networks慢路径分配流水号后再激活该流；等待期间必须
有界缓存或背压。这样未来切换虚拟网卡后，公开类型和上层接口都不改变。

## 五、UDP跟踪核心设计

### 5.1 UDP为什么必须依赖端口或协议Token

UDP没有连接握手，报文本身也不携带Networks流水号。收到数据报时，系统只可靠提供：

```text
本地IP、目标本地端口、实际远端IP、实际远端端口、载荷
```

因此通用UDP只能选择以下匹配方式之一：

1. 每条在途流租用独立本地端点；
2. 使用独立且已连接的UDP Socket，并严格限制远端端点；
3. 协议本身提供可改写和校验的Token，例如DNS Transaction ID；
4. NAT/路由层使用完整五元组和映射Generation；
5. 自有隧道或虚拟网卡封装显式携带内部Token。

如果多个无协议Token请求同时使用相同Socket、本地端口和远端端点，则回包无法可靠区分。此组合必须在发送前拒绝，
或为每条流分配不同本地端口，不能依靠发送顺序、FIFO位置或`NetworkFlowSerial`猜测。

### 5.2 长期Socket池

正常UDP请求不得每次创建和销毁Socket。池按以下稳定语义隔离：

```text
AddressFamily
SourceAddressSelection
InterfaceSelection
PathProvider / DataPlaneIdentity
RouteCompartment
IP Packet Policy
Broadcast / Multicast Policy
```

每个Socket槽至少保存：

```text
PoolSerial
SocketGeneration
ActualLocalAddress
ActualLocalPort
ActualInterface
State
ReceiveLoop
LeaseCount
```

Socket重建时必须递增`SocketGeneration`。旧Generation的响应只能进入旧流隔离，不能交给新租约。

池本身同样必须有生命周期边界。System Socket 后端定期清理超过空闲保留期且所有槽均为
`Available`的池；达到总池上限时，先回收完全可用的空闲池，再决定是否拒绝新Flow。
包含`Active`或未到期`Quarantine`槽的池绝不允许回收。这样固定DNS上游可以长期复用，
NAT、三层转发和Socket代理面对不断变化的目标时也不会永久积累池与端口句柄。

### 5.3 UDP Flow记录

每条UDP流至少保存：

```text
NetworkFlowSerial
Request / Attempt / Branch映射
SocketGeneration
ActualLocalEndPoint
ExpectedRemoteEndPoint
MatchMode
ProtocolToken（可选）
Requested / Resolved / Actual路径证据
Created / Dispatched / Deadline / Quarantine时间
TerminalState / ProtocolOutcome / AccessCompliance
```

发送顺序固定为：

1. 分配并登记`NetworkFlowSerial`；
2. 解析冻结的Branch访问计划；
3. 从正确池取得Socket或本地端点租约；
4. 绑定完成后读取实际本地IP和端口；
5. 登记线上匹配键；
6. 执行发送；
7. 系统接受发送后发布可等待的DispatchEvidence；
8. 由常驻接收循环分派响应；
9. 终态后进入迟到隔离，最后释放流水号和传输租约。

`TrySend`只表示进入FIFO，不能假装已经知道系统分配端口。调用方需要端口时等待DispatchEvidence。

### 5.4 接收匹配

单播默认匹配键：

```text
SocketGeneration + ActualLocalEndPoint + ExpectedRemoteEndPoint + ProtocolToken?
```

接收循环必须使用系统返回的实际`RemoteEndPoint`：

- IP或端口不符：丢弃并计入有界诊断，不完成也不失败当前Flow；
- Token不符：丢弃或交给对应Flow；
- 匹配成功：定位`NetworkFlowSerial`并发布响应；
- 已在Quarantine：发布迟到观察，不重开请求；
- 找不到映射：记为Unmatched，不允许猜测最近发送者。

广播和组播不能要求响应源等于组播目标，应使用协议Token、允许响应集合和收集窗口；每个响应方地址必须单独记录。

### 5.5 端口复用与隔离

- 无协议Token的通用单播：同一SocketGeneration和远端端点同一时刻只允许一个活动Flow；
- 需要并发时优先租用不同本地端口；
- DNS等有Token协议可以共享Socket，但仍须同时验证远端端点；
- Flow终态后，本地端点与远端组合进入迟到隔离；
- 端口耗尽必须形成明确背压或容量错误，不得退化为不安全复用；
- 专用源IP池可以扩展可用四元组空间，但不能取消端口和响应匹配规则。

## 六、DNS代理

每个客户端DNS查询创建一个`NetworkFlowSerial`，客户端原始DNS ID只属于客户端腿。上游腿执行：

```text
ClientEndPoint + OriginalDnsId
    → NetworkFlowSerial
    → SocketGeneration + UpstreamEndPoint + InternalDnsId
```

要求：

- 同一客户端可并发复用相同OriginalDnsId，内部仍由流水号区分；
- InternalDnsId在当前SocketGeneration和上游端点内不得重复；
- 回包同时校验QR、Opcode、Question、远端端点、InternalDnsId和最小报文结构；
- 返回客户端前恢复OriginalDnsId；
- TC回退到TCP保持同一业务Flow和Branch，新增传输子阶段，不重新生成根请求；
- 缓存键不含流水号，缓存结果必须在协议校验通过后产生；
- ID耗尽、Pending上限、超时和迟到响应均有独立计数和背压。

## 七、UDP NAT与IP路由

NAT映射的真实查找键不是流水号，而是：

```text
Zone + Protocol + OriginalSource + OriginalDestination + MappingGeneration
```

外部回程还需要：

```text
TranslatedLocalEndPoint + RemoteEndPoint + MappingPolicy + MappingGeneration
```

每个活动映射关联一个`NetworkFlowSerial`，用于内部状态、策略、统计和公共身份映射。必须显式声明：

- Endpoint-Independent、Address-Dependent或Address-And-Port-Dependent映射；
- SNAT、DNAT、双向NAT和Hairpin能力；
- 端口分配、冲突、回收和迟到隔离；
- ICMP错误中的内嵌五元组反查；
- IPv4分片首片建流和后续分片关联；
- NAT44、NAT64、NPTv6和NAT66分别声明，不能用一个布尔能力代替。

## 八、TCP、HTTP与Socket代理

### 8.1 TCP和通用Socket代理

每个接受的TCP连接或主动出口连接取得一个流水号。内部匹配依赖Socket/连接Generation和四元组，保存ClientLeg与
EgressLeg两段证据。必须处理半关闭、RST、空闲超时、背压、取消、连接失败和双腿独立终态。

### 8.2 HTTP代理

- HTTP/1.1独占连接时，请求Flow映射到连接及请求序号；
- HTTP/1.1流水线默认不启用，启用时必须严格按协议顺序关联；
- HTTP/2、HTTP/3使用协议Stream ID匹配，流水号只作内部索引；
- CONNECT隧道建立操作与隧道内长期字节流分别记录；
- 连接池键包含稳定访问与安全语义，不包含流水号、公共请求ID、超时或瞬时Actual证据；
- Host/SNI、实际目标IP和代理入口保持不同字段。

### 8.3 显式代理与透明代理

普通Socket后端只处理明确连接到代理监听端点的客户端。保留原始目的地址、截获其他进程流量或回注包时，必须切换到
WFP或虚拟网卡数据面。上层仍按同一流水号、Flow记录和双腿证据工作。

## 九、三层转发与未来虚拟网卡

三层数据面以完整IP包为输入，内部连接跟踪至少包含：

```text
IngressZone / Interface
AddressFamily / Protocol
SourceAddress / SourcePort
DestinationAddress / DestinationPort
Route / NextHop / EgressInterface
ConntrackGeneration
NetworkFlowSerial
```

必须为以下能力预留独立接口和证据：

- 路由查询、策略路由、VRF或Compartment；
- TTL/HopLimit递减和校验和更新；
- MTU、PMTU、IPv4分片和ICMP错误；
- ARP、IPv6 ND、邻居缓存和链路本地Scope；
- ACL、防火墙、QoS、速率限制和有界Conntrack；
- NAT与非NAT纯路由；
- 包回注循环标记和崩溃清理。

逐包热路径只携带`NetworkFlowSerial`、PacketSequence及必要元数据，不生成GUID、不创建托管对象、不查询公共Pending字典。
首包或未命中包进入慢路径建流，后续包使用流表快路径。

真正的L2帧交换、VLAN、MAC学习和生成树不属于本三层合同；未来如需实现，另建`INetworkFrameDataPlane`，不能把它
塞入IP Flow模型。

## 十、用户选择和适配器边界

业务目标与数据面对象保持正交：

```text
Destination   = 最终DNS/IP/服务目标
PathProvider  = RouteAdapter
RouteAdapter  = Exact(登记的数据面适配器身份和能力版本)
```

用户选择未来`VirtualAdapterDataPlane`或`ExternalForwarderDataPlane`时，Destination不改成适配器地址。适配器负责把冻结的
访问计划翻译为Socket、WFP、共享内存或虚拟网卡操作，并返回真实ClientLeg/EgressLeg证据。能力不匹配时发送前拒绝，
不得静默退回System Socket或Automatic。

## 十一、三轴终态与证据

每个Flow保留：

```text
TerminalState × ProtocolOutcome × AccessCompliance
```

- 错误来源UDP包被丢弃，不把当前Flow标成TransportFailed；
- 发送成功但响应超时，可形成`TimedOut + Satisfied`，前提是所有Required路径绑定证据完整；
- 路径证据不足不得伪装成TransportFailed；
- NAT/代理双腿分别记录Actual，不能用入口证据冒充出口证据；
- 组播目标与响应方地址分开；
- 流水号只负责内部关联，不构成路径或协议证明。

## 十二、资源、安全与可观察性

- 所有池、Pending、流表、隔离表和接收队列必须有界；
- 适配器显式注册、声明能力版本并受白名单控制；
- 外部数据面握手必须校验协议版本、身份和通道所有权；
- 共享内存描述符只保存固定宽度值和有界载荷引用；
- 端口、Flow、DNS ID和Conntrack耗尽分别报告；
- 默认诊断保持静默，只发布聚合计数；逐包证据必须显式开启并采样；
- 进程退出、适配器重启和通道断开必须让全部Flow进入确定终态和隔离清理。

## 十三、实施顺序与门禁

1. 冻结`NetworkFlowSerial`、静态分配器、生命周期和容量错误；
2. 建立`INetworkDataPlane`及System Socket后端，不改变业务端点合同；
3. 实现长期UDP Socket池、端口租约、DispatchEvidence和严格远端匹配；
4. 增加无Token并发拒绝、迟到隔离、回绕和端口耗尽测试；
5. 实现DNS代理双ID映射和TCP回退；
6. 实现显式UDP NAT及TCP/Socket代理；
7. 实现HTTP代理和协议Stream映射；
8. 冻结WFP透明后端并完成真实系统验收；
9. 接入VirtualAdapter/ExternalForwarder后端验证无需修改上层API；
10. 最后实现三层路由、Conntrack、NAT族、邻居、MTU和ICMP合同。

验收必须覆盖Debug/Release、并发、回绕模拟、错误远端、迟到包、Socket重建Generation、端口/ID耗尽、取消、崩溃清理、
真实IPv4/IPv6网络，以及System Socket与模拟VirtualAdapter两个后端的同一上层合同测试。

本设计只冻结技术方案，不授权修改系统路由、安装驱动、创建虚拟网卡、启动服务或恢复β发布状态。

## 十四、2026-07-22新增代码复核结论

本轮对流水号、UDP池、端点转发和直接数据面调用进行了再次复核，并修正以下边界：

- 隔离到期时间先于`Quarantine`状态发布，避免清理线程看到隔离状态却仍读到零到期时间；
- 隔离回收由全表扫描改成到期索引，活动流数量增加时不再让每次发号退化为线性扫描；
- System Socket数据面直接调用与旧Socket执行入口共用同一访问计划校验，拒绝过期或不匹配的Resolved计划；
- 常驻接收循环发生不可恢复异常后将槽标为失效，当前Flow闭环后销毁该Socket，不允许坏槽重新入池；
- 多个`DatagramDispatched`观察者相互隔离，单个用户回调异常不能阻止其他观察者，也不能改变网络终态；
- 重复或已经闭环的同源数据报计入迟到统计，不能再次完成同一Flow。

基础批次的用户验证步骤见[Networks 3.0 UDP数据面β测试指南](../03-reference/NETWORKS_3_0_UDP_DATA_PLANE_BETA_TEST_GUIDE.md)。

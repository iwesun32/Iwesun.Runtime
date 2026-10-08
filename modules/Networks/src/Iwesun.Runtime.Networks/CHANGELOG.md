# Changelog

本文件记录Iwesun.Runtime.Networks面向消费者的版本变化。

## 3.0.0-beta.5 - 2026-09-22

- 新增`NetworkSocketExecutor.OpenTcpAsync`和`NetworkTcpConnection`：调用方可取得长期双向
  TCP流；精确下一跳策略的所有权随连接存续，在连接释放前不会过早撤销。
- 连接建立失败、取消和策略清理失败均保留在`NetworkTcpConnectionOpenResult`中；成功连接同时
  支持`IDisposable`和`IAsyncDisposable`，释放后才结束策略生命周期。
- `INetworkPacketDataPlane`补齐捕获回调与注入入口，捕获记录携带内部`NetworkFlowSerial`、
  四级GUID执行身份、方向、接口和时间戳，为DNS代理、NAT、三层转发及后续独立数据面后端
  保留统一边界；默认Socket数据面不虚假声明Packet能力。
- WFP验收程序新增本地流与HTTPS探针支撑；Networks Debug/Release合同测试均为208项通过。
- 本包替代`3.0.0-beta.4`作为当前普通β。程序集版本继续保持`3.0.0.0`以维持3.0预发布阶段
  的二进制装载身份；消费者以NuGet包版本和产品信息版本识别本批次。

## 3.0.0-beta.4 - 2026-07-28

- `NetworkPingEndpoint<TKey>`启用请求级并发启动；批量登记后不再由唯一发送线程逐项完成昂贵解析，
  每个Request仍按独立GUID身份和终态闭环。
- Windows Automatic/Direct解析改用请求级不可变接口目录和路由快照，避免并发请求读取共享Provider中
  另一请求最后写入的“当前快照”。
- 同一解析器在一秒内复用只读网卡目录，但每个目标仍独立调用`GetBestRoute2`；多网卡DDNS真实扫描中，
  IPv6候选9项由逐项累积约2.5秒收敛为44～47毫秒，0项因内部排队超时。
- 删除按4/16字节长度猜测地址族的`IpAddressValue.FromAddressBytes`，改用严格的
  `FromIPv4Bytes`和`FromIPv6Bytes`；17字节固定合同只通过二进制读写API处理。
- ICMPv6单响应和多响应统一使用36字节Windows原生回复布局及同一解析器，
  固定`Address@6`、`Status@28`、`RoundTripTime@32`。
- HTTP和DoH连接池统一为有界、租约感知、可安全回收的内部池；容量不足且无可回收项时使用瞬态项，
  不驱逐活跃连接。
- `IpAddressValue`和`MacAddressValue`实现.NET 10 UTF-8 Span解析/格式化接口，
  保持字符入口的Null、格式和ScopeId语义。

## 3.0.0-beta.3 - 2026-07-25

- 将 `IpAddressValue`、`MacAddressValue`、`IpAddressTraits`、`MacAddressTraits` 和
  `IpAddressType` 纳入 `Iwesun.Runtime.Networks` 公共命名空间，统一 IP/MAC 数值、Null、
  分类、比较、固定二进制与 JSON 合同。
- `IpAddressValue`基础结构和固定二进制格式统一为17字节，仅保存128位纯地址和
  Null/IPv4/IPv6状态；ScopeId和接口索引改由访问计划、端点或路径证据单独保存。
- 删除`BinaryIpAddress`，网络合同、选择器、稳定键、路径证据、端点、示例和验证程序统一使用
  `IpAddressValue`；旧引用不提供兼容包装，以编译错误强制升级。
- `MacAddressValue`的CLR物理结构和固定二进制格式统一为7字节，仅保存48位EUI-48数值和
  Null/Value状态；OUI、NIC标识、文本和分类标志全部改为按需计算的只读视图。
- ARP与IPv6邻居表公共记录的字符串IP/MAC字段强制迁移为`IpAddressValue`和
  `MacAddressValue`；IPv6作用域继续单独保存在`InterfaceIndex`，缺失MAC使用值类型Null。
- 纯IP解析明确拒绝携带`%zone`的IPv6文本，避免作用域被静默丢弃或混入地址相等性。
- 修正IP类型迁移后字符串隐式转换干扰稳定键重载选择的问题，网卡别名和安全身份继续作为普通
  字符串进入稳定键，不会被误解析为IP。
- 本批次随Runtime 1.0.36-beta.1统一生成Debug/Release DLL、本地NuGet/符号包、完整文档与校验清单；
  不上传公共NuGet源。

## 3.0.0-beta.2 - 2026-07-23

- 增加4字节`NetworkFlowSerial`及非泛型静态分配器，支持进程活动域内非零原子发号、生命周期、迟到隔离、
  回绕碰撞跳过和Request/Attempt/Branch到传输键的反查。
- `NetworkUdpDatagramEndpoint<TKey>`迁移到可替换`INetworkDatagramDataPlane`；默认System Socket后端使用长期Socket池、
  端口租约、SocketGeneration、常驻接收循环和实际远端严格匹配。
- 无协议Token的并发UDP流使用不同本地端口；顺序流在隔离期后复用相同槽。发送证据公开实际源IP、端口、接口、
  远端和Flow流水号，错误来源与迟到包不能完成新流。
- 冻结DNS代理、NAT、Socket代理、HTTP代理和IP Packet数据面独立能力合同；当前System Socket后端只声明Datagram，
  不把未实现代理能力标记为可用。
- 发布前复核把Flow表改为流水号主键与隔离到期双索引，消除发号时扫描全部活动Flow；隔离状态与到期时间按单Flow
  原子发布，直接数据面调用统一执行访问计划校验，坏Socket槽不再复用，发送证据观察者相互隔离。
- 增加不打包的`Iwesun.Runtime.Networks.UdpBetaValidation`回环验证工具，以结构化JSON输出IPv4/IPv6吞吐、延迟、
  活动/隔离槽、单池峰值和容量拒绝；本机矩阵确认64槽保护上限会安全拒绝，不会清除未到期隔离。
- 流水号、UDP池、转发合同与复核守卫测试均纳入Debug/Release全量门禁；三轴Ping更正已完成，剩余M11真实网络
  验收继续阻止正式版，但允许普通β联调。

## 3.0.0-beta.1 - 2026-07-22

- 修正仅提供接口Index等部分身份的ExactInterface请求：稳定Key比较前统一正规化接口身份，避免合法的
  TCP/UDP请求在发送前被错误拒绝为`resolved-plan-mismatch`。
- 将公开适配器合同统一为无版本后缀的`INetworkRouteAdapter`、`NetworkRouteAdapterRegistry`和
  `HttpConnectNetworkRouteAdapter`，对应源码文件同步采用正式名称。
- 不提供旧名称别名、转发类型或兼容程序集；引用旧版本化名称的消费者必须通过编译错误完成升级。
- 协议端点继续统一为`Network*Endpoint<TKey>`，唯一跟踪使用四级非空GUID链，`TKey`仅作调用方查询数据。
- 继承rc.3已完成的WFP、恢复动作、RouteAdapter和SystemProxy真实网络修正；此前本地rc包全部废止。

## 3.0.0-rc.3 - 2026-07-22

- 修正平台默认上下文错误地把`RouteAdapter`和`SystemProxy`外层计划交给仅支持Automatic/Direct的
  `GetBestRoute2`快照器，导致真实代理请求发送前固定失败为`constraint-unresolved`的问题。
- 代理和适配器改为委托解析路径：不再用“本机到最终目标”的直连路由冒充代理路径；精确出口约束交由
  适配器执行，SystemSelected维度等待ClientLeg/EgressLeg实际证据。
- Aether真实验收已通过HTTP CONNECT RouteAdapter固定`8.8.8.8`且保留`dns.google` Host/SNI，以及
  Windows SystemProxy服务解析访问；失败兼容空载荷现在同时记录Networks固定原因和RequestId。
- Networks Debug/Release测试提升为105项，增加无本机路由快照时的两类委托路径回归守卫。

## 3.0.0-rc.2 - 2026-07-22

- 修正WFP引擎打开认证服务：统一使用Windows要求的`RPC_C_AUTHN_WINNT`，消除管理员能力查询通过、
  实际策略加入却返回原生错误50的假阳性。
- 能力查询现在真实打开并关闭WFP引擎，失败时返回原生错误及固定能力分类。
- 管理员验收程序增加恢复能力、RS、DHCPv6 release/wait/renew、网卡重启和地址快照入口。
- 真实Windows验收已通过TCP、UDP、同网关双连接隔离、正常清理、崩溃清理和恢复动作；双网关并发
  因当前机器只有一个真实默认网关仍待验证。
- DDNS Snap地址守护改为要求精确源地址并由业务层校验接口归属，不再要求ICMP API无法证明的
  ExactInterface维度；当前DHCPv6源真实探测通过。

## 3.0.0-rc.1 - 2026-07-22

### 唯一公共请求—响应规范

- 以`RequestId → AttemptId → BranchId → ResponseId`四级非空GUID链作为正式跟踪身份；
  `TKey`只保留为允许重复的调用方查询数据。
- 删除全部`Tracked*V2`协议类型、`NetworkRouteDirective`、可空`Route`、旧路由Flags、
  旧HTTP处理器和旧路由适配器；旧源码引用必须迁移并通过编译错误暴露遗漏。
- TCP、UDP、Ping、PTR、NBNS、HTTP和DoH统一为`Network*Endpoint<TKey>`合同，所有请求携带
  非空`RequestedAccessPlan`，低层发送入口拒绝`Guid.Empty`。

### 精确网络访问控制

- 引入正交目标、源地址、接口、下一跳、路由范围、IP包政策、Resolver和RouteAdapter选择器，
  分离Requested、Resolved与Actual证据，禁止不满足Required约束时静默回退。
- Direct Socket执行支持显式源地址与接口；Windows路由快照通过`GetBestRoute2`提供可失效的
  解析证据；ExactNextHop由请求级动态WFP策略执行并报告`PolicyEnforced`。
- HTTP/DoH稳定连接池键包含访问语义和协议安全身份，但排除请求GUID、超时和重试；
  SystemProxy与RouteAdapter分别报告客户端段和出口段证据。

### 多响应、恢复与验证

- Ping广播/组播和NBNS广播使用显式有界收集窗口；每条响应保留完整身份链，窗口只产生一个终态。
- PTR UDP→TCP回退、NBNS手工Socket和HTTP CONNECT客户端段均纳入相同WFP策略生命周期。
- 提供RS、DHCPv6 release/renew、等待和网卡重启公共恢复原语，但不内置DDNS业务恢复顺序。
- 增加独立管理员WFP验收程序；真实策略加入、并发网关隔离、崩溃清理和系统恢复动作仍须在
  M11授权环境验收，当前版本不得视为正式发布完成。

## 2.0.1 - 2026-07-21

### 精确路由稳定性

- IPv6 Ping按`InterfaceIndex`解析源地址时跳过不支持IPv6属性查询的Windows适配器，兼容
  `NetworkInformationException` 10043，并继续查找目标物理接口。
- IPv4 Ping同步采用逐适配器容错，避免不支持协议的适配器中断精确路由选择。
- 跟踪基类把初始路由和重试路由解析纳入失败闭环；路由解析异常记录为
  `route-resolution-threw`，不得终止端点发送线程或宿主进程。
- 新增回归测试，验证路由解析失败后同一端点仍能继续处理后续请求。

## 2.0.0 - 2026-07-20

### 强制统一

- `TrackedRequestReplyEndpointBase<TRequest,TResponse,TKey>`成为唯一正式请求—响应规范。
- 删除已被V2取代的`HttpGetEndpoint`、`DohJsonEndpoint`、`TcpConnectEndpoint`、
  `PingIpv4AsyncEndpoint`、`PingIpv6AsyncEndpoint`及其旧请求/结果合同。
- V2 Ping内部直接实现自动路由与显式源地址执行，不再借用旧Ping解释器或旧source helper。
- DDNS广播/组播Ping和Aether兼容批量DoH同步迁移，旧类型引用会直接编译失败。
- PowerShell、Process、ARP和IPv6邻居快照没有重复V2实现，继续保留其专用FIFO模型。

### 破坏性变化

- 2.0.0不提供旧端点别名、转发器或兼容包；消费者必须改用对应`Tracked*V2`类型。

## 1.2.0 - 2026-07-19

### 新增

- `FirstAttemptTimeoutMs`、`SecondAttemptTimeoutMs`、`ThirdAttemptTimeoutMs`分别配置T1/T2/T3。
- `TrackedAttemptReport`和固定长`TrackedAttemptHistory`记录最多三次尝试。
- 成功和最终失败结果新增`TotalElapsedMs`。
- 重试事件新增`NextRetryCount`与`NextAttemptNumber`。

### 兼容性

- 请求`TimeoutMs`非空时继续覆盖该请求全部尝试；为`null`时改为按T1/T2/T3读取。
- `DefaultTimeoutMs`继续保留；设置它会同时设置T1/T2/T3，旧调用无需修改。
- 尝试历史只保存固定长错误类型和代码，不引入字符串引用，完成结构继续支持`stackalloc`。

## 1.1.0 - 2026-07-19

### 新增

- V2请求—响应跟踪基类，包含发送FIFO、Pending、完成FIFO、响应确认、超时、重试和最终失败闭环。
- `TrackedHttpGetEndpointV2`、`TrackedDohEndpointV2`、`TrackedTcpConnectEndpointV2`。
- `TrackedPingIpv4EndpointV2`和`TrackedPingIpv6EndpointV2`。
- 逐请求`NetworkRouteDirective`，支持目标IP、源IP、物理接口和明确路由适配器。
- 保留HTTPS服务域名、TLS SNI和HTTP Host，同时固定底层目标IP。
- HTTP CONNECT固定最终目标IP适配器。
- 可编译运行的DoH和Ping示例项目。

### 统一契约

- 所有V2请求的`Route`均为可空参数，`null`表示系统普通路径。
- 所有V2请求的`TimeoutMs`均为可空参数，`null`读取端点`DefaultTimeoutMs`。
- `MaxAttemptCount`包含首次发送，公共基类硬性限制最多总计3次。
- Ping完成结果记录实际目标IP、实际源IP和物理接口索引。

### 兼容性

- 旧V1端点和双FIFO接口继续保留，不自动迁移消费者。
- 1.1.0新增V2类型，不删除现有1.0.0公共类型。

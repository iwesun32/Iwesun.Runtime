# Iwesun.Runtime.Networks 3.0 精确网络访问控制实施计划

> **状态**: GENERAL_BETA_READY_FORMAL_BLOCKED
>
> **最后更新**: 2026-07-23
>
> **权威设计**: [Iwesun.Runtime.Networks 3.0精确网络访问控制最终设计](PRECISION_NETWORK_ACCESS_CONTROL_FINAL_DESIGN.md)
>
> **实施原则**: 分阶段开发、一次性破坏性迁移、最终只保留一种公共规范
>
> **当前边界**: M0～M10完成；beta.1仅作失效基线；三轴终态更正已进入beta.2，M11剩余验收阻止正式版

## 一、交付目标

交付Iwesun.Runtime.Networks 3.0.0，完整实现：

- 固定`RequestId → AttemptId → BranchId → ResponseId`身份树；
- 非空访问选择器和不可变Requested/Resolved计划；
- Automatic、Direct、RouteAdapter、SystemProxy路径提供者；
- 目标、源地址、接口、下一跳、本地端点、路由作用域和IP政策；
- DNS/PTR、HTTP/DoH、TCP、UDP、ICMP、NBNS及多响应协议扩展；
- Requested/Resolved/Actual逐维证据和双轴终态；
- Windows Socket、路由查询、WFP和适配器执行后端；
- Runtime、DDNS Snap、Aether一次性强制迁移；
- 固定测试、Windows真实网络测试、包和Runtime嵌入载荷发布。

3.0正式程序集不得继续公开被替代的V2请求、响应、端点、Route可空重载、旧路由Flags或隐式兼容类型。

## 二、实施总顺序

```text
M0 基线与公共面盘点
 ↓
M1 冻结值类型合同和原因码
 ↓
M2 重建跟踪基类与四级GUID状态机
 ↓
M3 实现选择器规范化、计划求解和能力查询
 ↓
M4 实现Socket直连/自动路径和逐维证据
 ↓
M5 迁移TCP、UDP、Ping、PTR、NBNS
 ↓
M6 迁移HTTP、DoH、RouteAdapter和连接池
 ↓
M7 实现多响应、迟到响应和恢复原语
 ↓
M8 实现并验证WFP精确下一跳
 ↓
M9 删除V2公共面并生成3.0候选包
 ↓
M10 Runtime、DDNS Snap、Aether强制迁移
 ↓
M11 全量与真实网络验收
 ↓
M12 文档、打包和正式发布
```

M1～M8允许在同一开发分支逐步编译，但不得发布混合语义正式包。M9完成后，程序集公共面只剩3.0规范。

## 三、M0：基线与公共面盘点

### 工作

- 记录Networks、Runtime、DDNS Snap、Aether当前分支、提交和未提交文件，不覆盖用户工作区。
- 保存Networks 2.0.1 Debug/Release构建和测试基线。
- 用反射或API清单记录当前公开类型、构造函数、事件和程序集版本。
- 搜索全部`Tracked*V2`、`NetworkRouteDirective?`、`Route = null`、`NetworkRouteFlags`和`Guid.Empty`自动换号调用点。
- 建立三方消费者迁移表，逐项关联到最终设计章节。

### 产物

- `docs/03-reference/NETWORKS_3_0_API_MIGRATION_MATRIX.md`
- 2.0.1公共API基线清单
- 三仓消费者调用点清单

### 退出门禁

- 2.0.1基线测试可重复；
- 所有公共面和消费者调用点都有归属；
- 未修改任何现有发布包。

## 四、M1：冻结基础值类型合同

### 建议源码分组

```text
modules/Networks/src/Iwesun.Runtime.Networks/
├─ NetworkExecutionIdentity.cs
├─ NetworkAccessSelectors.cs
├─ NetworkAccessPlan.cs
├─ NetworkAccessEvidence.cs
├─ NetworkAccessCapabilities.cs
├─ NetworkAttemptPolicy.cs
└─ NetworkAccessFailureCodes.cs
```

### 合同

- `NetworkExecutionIdentity`及Request/Attempt/Branch/Response父链值类型；
- `NetworkPathProvider`，`Unspecified = 0`非法；
- 目标、源、接口、下一跳、本地端点、RouteScope强类型选择器；
- 稳定集合规范化结果，禁止暴露可变集合；
- `RequestedAccessPlan`、`ResolvedAccessPlan`、`ActualAccessEvidence`；
- `ProofKind`、`ProtocolOutcome`、`AccessCompliance`；
- Branch、Attempt、Request终态；
- 固定kebab-case失败原因码和平台错误载荷；
- `NetworkAccessStableKey`，专供HTTP/DoH连接池和消费者稳定身份使用。

### 约束

- 请求、结果、快照继续使用值类型；
- 所有默认结构在发送入口非法；
- 不使用可空字段或哨兵值表达选择；
- 稳定Key不包含任何GUID、时间、超时、重试和Actual观测。

### 退出门禁

- 默认值、空集合、重复集合、地址族冲突和Derived环测试齐全；
- 相同语义生成相同稳定Key；
- 不同路径/安全边界生成不同稳定Key；
- 公共合同审查通过后才能进入基类实施。

## 五、M2：四级GUID跟踪基类

### 工作

- 将`TrackedRequestReplyEndpointBase`升级为3.0唯一请求—响应基类。
- 低层`Send`/`TrySend`在FIFO前拒绝`Guid.Empty`。
- Pending只以`RequestId`为键；`TKey`查询只读且允许重复。
- 发送前拒绝只创建Request终态，不创建Attempt或Branch。
- Attempt开始时创建新`AttemptId`；每个已创建Attempt至少一个Branch。
- 每条实际执行路径创建唯一`BranchId`；每条完整响应创建`ResponseId`。
- 分层实现Branch、Attempt、Request唯一终态和异步事件。
- 迟到响应保留完整身份链，不重新打开终态。
- 重试继承同一Requested计划，创建新Attempt和Branch集合。

### 测试重点

- Guid.Empty不进FIFO/Pending；
- 普通请求恰有一个Branch；
- PreferredSet可有多个Branch；
- 重复回调、取消与超时只完成一次；
- 迟到响应不二次完成；
- 重复TKey请求互不影响；
- 新Attempt可在原PreferredSet内选择不同成员但不能改变集合。

### 退出门禁

- 身份和状态机测试全部通过；
- 基类不包含任何Route=null或自动换RequestId路径；
- Runtime诊断所需的四级快照字段稳定。

## 六、M3：计划规范化、求解和能力查询

### 建议组件

- `NetworkAccessPlanNormalizer`
- `NetworkAccessConstraintResolver`
- `NetworkAccessCapabilityProvider`
- `NetworkInterfaceIdentityResolver`
- `NetworkRouteSnapshotProvider`
- `NetworkAccessStableKeyBuilder`

### 工作

- 规范化接口LUID/GUID/Alias/Index和IPv6 ScopeId。
- 去重并稳定排序Allowed/Preferred/Excluded集合。
- 构建Derived依赖图并拒绝环、无解和多义结果。
- 建立PathProvider × 端点 × 选择器 × 后端合法性矩阵。
- 实现可解释能力查询：不支持维度、所需后端、权限和固定原因码。
- 发送时重新校验能力快照，不能隐式降级。

### 退出门禁

- 只指定接口、网关、源地址及完整路径均能得到唯一Resolved计划或固定拒绝原因；
- 系统路由、地址或接口变化会使旧计划失效并重新解析；
- 不存在循环派生和未声明NotApplicable。

## 七、M4：Socket和自动/直连执行后端

### 工作

- 实现Automatic的系统路径执行与可取得证据。
- 实现Direct的源地址bind、接口选项、本地端点和IP政策。
- 使用`GetBestRoute2`/路由快照解析候选源、接口和下一跳，但标记为Inferred。
- TCP/UDP连接后采集本地/远端实际端点。
- 数据报使用包信息采集接收接口和目标信息（平台支持时）。
- 逐维产生Observed、ApiBinding、Inferred或Unavailable证明。
- 无法执行Required约束时发送前拒绝或Branch失败，不回退。

### 退出门禁

- Automatic和Direct在IPv4/IPv6下均有成功、失败及证据不足测试；
- 只固定接口、只固定源地址可实机证明；
- ExactNextHop尚未由WFP支持时能力明确返回不支持，不修改全局路由。

## 八、M5：基础协议端点迁移

迁移顺序：

1. TCP Connect；
2. UDP Datagram；
3. IPv4 Ping；
4. IPv6 Ping；
5. DNS PTR；
6. NetBIOS Name。

每个端点必须：

- 使用3.0请求/响应合同和四级身份；
- 声明支持的PathProvider、选择器、IP政策和响应基数；
- 生成Branch级协议结果和路径证据；
- 不支持SystemProxy/RouteAdapter时发送前拒绝；
- PTR分离查询对象、Resolver选择和Resolver访问计划；
- UDP→TCP回退保持同一Requested计划并形成可追踪Branch/阶段证据；
- Ping不接受无意义的代理能力。

### 退出门禁

- 每个端点的模式/能力矩阵有固定测试；
- PTR、IPv6 ScopeId、广播/组播等边界测试通过；
- 端点不再依赖旧`NetworkRouteDirective`。

## 九、M6：HTTP、DoH、RouteAdapter和连接池

> **里程碑状态**: COMPLETE（2026-07-22）

### 工作

- 将Host、SNI、证书名从路由Flags移回协议请求。
- 以稳定访问语义Key隔离HttpClient/连接池。
- Direct固定传输IP时仍保持逻辑Host/SNI。
- RouteAdapter注册信息增加能力与能力版本。
- 分离ClientLeg与EgressLeg证据。
- SystemProxy只报告可见ClientLeg，不推断出口。
- HTTP CONNECT仅声明ByteStream；数据报适配器没有实现前不得声明Datagram。
- WFP请求级策略无法安全复用时禁止相关连接复用。

### 退出门禁

- 不同源、接口、下一跳、提供者或适配器不共享错误连接；
- 不同RequestId但稳定语义相同的请求可安全复用；
- DoH固定IP保留Host/SNI并通过证书校验；
- 代理双Leg证据无跨段冒充。

## 十、M7：多响应、迟到响应和恢复原语

> **里程碑状态**: COMPLETE（2026-07-22）

### 多响应

- 实现`FirstValidResponse`和`CollectUntilWindowEnds`。
- 每条观测即时发布四级身份链。
- 数量上限或窗口结束只生成一个Branch/Attempt/Request聚合终态。
- 迟到响应只进入迟到事件。

### 恢复原语

- Router Solicitation；
- DHCPv6 release、等待、renew；
- 网卡重启；
- 动作后地址快照。

分别报告请求接受、系统动作、地址变化和复检结果。Networks不实现DDNS恢复顺序。

### 退出门禁

- IPv4广播、IPv6组播和NBNS多响应测试通过；
- 迟到响应不破坏新请求；
- Windows权限不足和不支持平台返回明确能力结果；
- 恢复动作不把退出码0冒充网络恢复。

## 十一、M8：WFP精确下一跳专项

### 工作包

1. WFP能力与权限探测；
2. 请求/连接范围的策略匹配键设计；
3. 同目标不同网关并发隔离；
4. SourceAddress、NextHopInterface和NextHop执行；
5. PolicyEnforced证明；
6. 正常、取消、超时和异常清理；
7. 连接池回收清理；
8. 进程崩溃后的残留恢复；
9. 权限不足和不支持平台拒绝；
10. 不修改全局路由表的审计。

### 停线条件

以下任一失败，3.0不得声明支持`ExactNextHop`：

- 并发请求可能串线；
- 策略可能污染同进程其他连接；
- 取消/异常后不能确定清理；
- 崩溃后无法发现或清理残留；
- 不能区分查询推断与实际强制。

失败时保留选择器合同和能力拒绝，不以Automatic或全局路由修改代替。

## 十二、M9：移除V2并生成候选包

### 删除/替换

- 所有`Tracked*V2`公开请求、响应和端点；
- `NetworkRouteDirective?`、`Route = null`兼容入口；
- `NetworkRouteFlags`中的Host/SNI、竞速和绑定混合标志；
- 低层Guid.Empty自动换号；
- 旧Precision/RouteBound HTTP处理器中与3.0重复的公共语义；
- 所有V2别名、隐式转换和兼容包装。

### 门禁

- 公共API差异明确显示V2被移除、3.0成为唯一标准；
- `rg`无旧Route=null和V2公开调用残留；
- Debug/Release构建及Networks测试通过；
- 生成`Iwesun.Runtime.Networks.3.0.0-rc.N.nupkg`供三方迁移。

## 十三、M10：三方消费者强制迁移

### Runtime

- 更新独立Networks包/嵌入程序集版本边界；
- 更新Runtime载荷清单、安装文档和验证脚本；
- Runtime诊断可显示Request/Attempt/Branch/Response树和Branch证据；
- 不复制Networks实现。

### DDNS Snap

- 扫描、Ping、NBNS、地址守护和PTR改用显式计划；
- 普通请求和多响应保留四级身份；
- 地址守护只消费满足所需AccessCompliance的证据；
- 恢复顺序继续由DDNS业务层控制。

### Aether

- HTTP/DoH、Ping/TCP探测迁移3.0；
- Preferred IPv6改为PreferredSet + AttemptPolicy；
- DNS内容、协议结果、路径合规和证据不足分别评价；
- Node Path使用稳定访问语义摘要；
- 连接池按最终稳定Key隔离。

### 门禁

- 三仓不再引用V2类型；
- 编译错误清零，不使用临时别名；
- 三仓Debug/Release构建及固定测试通过。

## 十四、M11：真实网络验收

### 固定环境

- Windows物理网卡 + Wi-Fi/以太网；
- IPv4/IPv6双栈；
- Clash/VPN/TUN至少一种虚拟路径；
- 可用系统代理和显式HTTP CONNECT适配器；
- 可控制的DNS Resolver；
- 具有/不具有WFP权限的两类运行身份。

### 必测场景

- Automatic、只指定接口、只指定源、只指定网关、固定完整路径；
- 排除虚拟接口；
- Preferred IPv6多Branch竞速和新Attempt重新解析；
- IPv6链路本地ScopeId；
- 广播/组播/NBNS窗口及迟到响应；
- PTR同路径UDP→TCP；
- DoH固定IP + Host/SNI；
- SystemProxy、RouteAdapter双Leg；
- 同目标不同源/接口/网关并发；
- 运行中接口Index、地址状态和路由变化；
- 取消、超时、异常退出、连接池回收及WFP残留检查；
- Runtime CLI按四级GUID查询实际证据。

### 退出门禁

- Networks、Runtime、DDNS Snap、Aether进程持续存活；
- 不存在路径串线、证据冒充或隐式降级；
- 所有失败都有固定原因和完整身份链；
- 安装/嵌入载荷与候选包哈希一致。

## 十五、M12：正式发布

### 发布前

- 版本统一为3.0.0；
- 更新README、端点文档、API迁移表、CHANGELOG、RELEASE_STATUS和发布说明；
- 归档旧RFC，最终设计保持`ACCEPTED`；
- 生成NuGet包、符号包及校验和；
- 更新Runtime嵌入载荷和安装目录文档；
- 完成干净环境restore/build/test/install验证。

### 发布顺序

1. 发布Networks 3.0.0正式包或确定的本地包源；
2. 更新并发布Runtime载荷；
3. 更新DDNS Snap；
4. 更新Aether；
5. 重复三方真实网络验收；
6. 提交并推送各仓库，确认远端提交一致。

不得先发布消费者再补Networks正式合同，也不得长期混用2.0.1与3.0语义。

## 十六、独立后续RFC

在M12前至少建立入口，但不阻塞本次出站模型实现：

- Listener/Accept/服务器会话；
- 长期UDP NAT映射与保活；
- 专用源IP池和端口池租约；
- 系统路由管理；
- 防火墙与地址独占；
- 持续发现/订阅流。

这些合同复用GUID、选择器和证据原语，但不得扩张3.0单次出站请求的生命周期。

## 十七、执行纪律

- 每个里程碑完成后更新本计划勾选状态和验证证据。
- 不跨过退出门禁进入下一阶段。
- 任何实现差异先修订最终设计书，不在代码中发明新语义。
- 不使用Console或临时文件诊断；Networks测试使用正式事件和结果，Runtime集成使用Runtime Diagnostics。
- 不自动安装、启动服务或修改系统网络配置；真实测试动作必须明确记录并可恢复。
- 不覆盖无关未提交文件，不使用破坏性Git命令。

## 十八、首个执行批次

收到开始实施指令后，第一批只执行M0和M1：

1. 保存四仓工作区和2.0.1验证基线；
2. 建立API迁移矩阵；
3. 创建基础值类型文件和对应测试；
4. 冻结失败原因码、稳定Key和合法性矩阵；
5. Debug/Release构建并提交阶段性证据；
6. 通过M1门禁后再改跟踪基类。

不得在第一批同时迁移协议端点或消费者，以免基础合同未稳定就扩大修改面。

## 十九、实施状态

### M0：已完成（2026-07-22）

- 已记录Networks、Runtime、DDNS Snap、Aether的分支、提交和未提交状态，未覆盖用户工作区。
- Networks 2.0.1基线：Debug与Release均为29通过、0失败、0跳过。
- 已建立[Networks 3.0 API迁移矩阵](../03-reference/NETWORKS_3_0_API_MIGRATION_MATRIX.md)，登记2.0.1公共面及三方消费者调用点。
- 未修改版本、现有协议端点、消费者或发布包。

### M1：已完成（2026-07-22）

- 已实现四级GUID身份、路径提供者、强类型选择器、稳定规范集合、Requested/Resolved/Actual、证明等级、双轴结果及分层终态。
- 已实现固定kebab-case原因码、平台错误载荷、能力查询、端点合法性矩阵和稳定访问语义Key。
- 稳定Key不接收跟踪身份、时间、超时、重试或Actual证据；集合顺序、重复项和主机名大小写完成规范化。
- 固定合同测试覆盖default、空集合、重复集合、地址族冲突、Derived环、NotApplicable授权及稳定Key隔离。
- Debug与Release均为38通过、0失败、0跳过，构建零警告。

M1门禁通过后已进入M2；现有V2协议端点尚未迁移，不得把当前工作区作为3.0发布包。

### M2：已完成（2026-07-22）

- `TrySend`在启动端点、发送FIFO和Pending之前拒绝`Guid.Empty`，不再自动生成或替换RequestId。
- Pending及终态复用保护只按RequestId管理；TKey继续仅用于允许重复的只读观察。
- 每个Attempt生成唯一AttemptId并至少创建一个BranchId；单路径和多Branch执行共用同一状态机。
- 每条响应生成ResponseId；多Branch发布必须显式提供BranchId，赢家关闭其余Branch为Superseded。
- Branch、Attempt、Request分别产生唯一异步终态事件；完成记录携带完整四级身份。
- 有界终态身份缓存使迟到响应保留原Request/Attempt/Branch父链，但不会重新打开Request。
- 外层重试冻结旧V2请求和首次路由，创建新的Attempt和Branch；规范Requested计划的重新求解由M3实现。
- 固定测试覆盖空GUID、单Branch、多Branch、三次重试、重复响应、迟到响应、重复TKey、终态RequestId复用和Stop取消。
- Debug与Release均为43通过、0失败、0跳过；Debug额外连续重复三轮通过，构建零警告。

当前仍是实施工作区：V2协议请求的`NetworkRouteDirective?`和旧端点命名将在M9统一删除，不得发布混合语义正式包。

### M3：已完成（2026-07-22）

- 新增`NetworkAccessPlanNormalizer`，把Interface Index/GUID/LUID/Alias解析为包含稳定身份和当前Index的不可变快照。
- Allowed/Preferred/Excluded集合继续使用M1稳定排序与去重结果；解析值不在规范候选集合内时固定拒绝，不隐式改选。
- Derived来源增加逐维白名单并继续执行静态环检测；无效来源、环、无唯一解分别返回固定原因码。
- 新增`NetworkAccessConstraintResolver`，覆盖只指定接口、只指定网关、只指定源及固定完整路径的唯一求解。
- IPv6 ScopeId在规范化及Resolved结果阶段与实际接口Index交叉验证，冲突固定拒绝。
- 新增接口目录与路由快照Provider；`ResolvedAccessPlan`记录双快照版本，任一变化都会使旧结果失效并要求重新求解。
- 新增`NetworkAccessCapabilityProvider`，返回支持状态、具体不支持维度、所需执行后端、缺失权限、固定原因码和快照版本。
- `NetworkAccessStableKeyBuilder.TryBuild`成为公共稳定Key入口；仍排除跟踪GUID、时间、重试和Actual证据。
- 固定测试覆盖四类路径组合、接口歧义、ScopeId冲突、非法Derived、候选无解、WFP权限解释和快照变化失效。
- M3门禁后Debug与Release均为53通过、0失败、0跳过，构建零警告，差异检查通过。

### M4：已完成（2026-07-22）

- 新增`NetworkSocketExecutor`，支持Automatic/Direct下的TCP连接与单次UDP请求—响应，并保持每次执行归属于完整Request/Attempt/Branch身份。
- Direct按正交选择器分别执行源地址bind、IPv4/IPv6接口选项、本地端口和IP包政策；SystemSelected维度不会被误提升为Exact绑定。
- 新增`WindowsNetworkRouteSnapshotProvider`，使用只读`GetBestRoute2`取得候选源、接口和下一跳，并生成接口/路由快照版本；查询结果只标记为Inferred。
- Windows接口属性读取逐地址族隔离`NetworkInformationException`，不会因网卡不支持IPv4或IPv6属性查询而终止宿主进程。
- TCP/UDP采集实际本地和远端端点；UDP在平台支持时通过PacketInformation记录实际接收接口，不支持时保留Inferred证明。
- `ActualAccessEvidence`补齐PathProvider、IP包政策、RouteAdapter及逐维差异原因，继续直接归属于Branch。
- 发送前复核Requested/SecurityBoundary/Resolved稳定语义和双快照版本，不一致或过期固定拒绝且不回退。
- ExactNextHop在WFP未实施前固定返回`exact-next-hop-backend-unavailable`；ExactCompartment固定返回`route-scope-backend-unavailable`，均不修改全局路由。
- 固定测试覆盖Automatic/Direct、TCP/UDP、IPv4/IPv6、只指定源、只指定接口、Windows真实GetBestRoute2、证据不足、快照失效和能力拒绝。

M4门禁已通过并进入M5。M4完成时尚未迁移协议公共请求；后续迁移状态以下方M5记录为准，工作区不得作为3.0发布包。

### M5：已完成（2026-07-22）

- 已完成TCP、UDP第一批迁移，新增泛型`NetworkTcpConnectEndpoint<TKey>`与`NetworkUdpDatagramEndpoint<TKey>`。
- 新端点只接受非空`RequestedAccessPlan`，不接受`NetworkRouteDirective?`或`Route = null`。
- 每个Attempt重新刷新并求解被冻结的Requested计划；每个Branch调用M4执行后端并发布逐维Actual证据。
- 响应本体创建的ResponseId由跟踪基类原样用于观察事件和完成记录，不再二次生成不同身份。
- 已完成统一`NetworkPingEndpoint<TKey>`，同一非空3.0合同覆盖IPv4和IPv6，不再为两个地址族复制公共语义。
- Automatic使用系统ICMP路径；Direct ExactSource在Windows使用`IcmpSendEcho2Ex`/`Icmp6SendEcho2`原生源绑定。
- Direct ExactInterface目前只能由源地址与路由快照推断，协议成功仍报告EvidenceIncomplete，不把推断冒充ApiBinding。
- Ping明确拒绝ExactNextHop、ExactCompartment、Direct Explicit IP政策及广播/组播多响应，不隐式回退。
- 新增`NetworkDnsReverseLookupEndpoint<TKey>`，分离反查对象、Resolver选择和Resolver访问计划；系统Resolver与精确Resolver均有显式选择合同。
- PTR UDP→TCP回退保持同一Request/Attempt/Branch身份，分别记录UDP/TCP阶段路径证据；TCP发送前重新校验快照。
- 新增`NetworkNetBiosNameEndpoint<TKey>`单播NBNS端点；广播/组播多响应在M7前固定拒绝，不用首响应冒充窗口终态。
- TCP、UDP、Ping、PTR和NBNS均不接受`NetworkRouteDirective?`或`Route = null`，不支持的PathProvider和Required维度明确拒绝。
- Debug与Release全量均为78通过、0失败、0跳过，构建零警告，差异检查通过。

M5门禁已通过并进入M6。V2类型继续暂留到M9统一删除，当前工作区仍不是3.0发布候选包。

### M6：已完成（2026-07-22）

- 已冻结`NetworkHttpProtocolIdentity`，Host、SNI、证书校验名和HTTP协议版本不再塞入路由Flags。
- 已实现`NetworkHttpConnectionPoolKey`，稳定Key包含路径、Resolver和协议安全身份，类型签名不允许传入跟踪GUID、时间、超时、重试或Actual证据。
- 已新增正式`INetworkRouteAdapter`及注册表，严格匹配AdapterId、CapabilityVersion和ByteStream/Datagram能力。
- RouteAdapter连接合同分别返回ClientLeg和EgressLeg证据，未报告的EgressLeg保持Unavailable/NotApplicable。
- 已新增`NetworkHttpGetEndpoint<TKey>`，Direct固定传输IP时保留逻辑URL Host，并按稳定语义复用HttpClient/连接池。
- 相同语义不同RequestId复用同一池；源地址、Host/SNI或适配器版本变化产生不同池键。
- 已将证书SHA-256固定值纳入HTTP安全身份和稳定池键，不同固定值不能共享连接。
- 已新增`NetworkDohEndpoint<TKey>`，支持JSON/WireMessage查询，固定IP HTTPS实测同时证明逻辑Host、SNI和证书固定没有随传输目标变化。
- 已新增`HttpConnectNetworkRouteAdapter`；ClientLeg记录本机到代理的Observed socket，EgressLeg只记录适配器报告的CONNECT目标，不跨Leg冒充源地址、接口或网关。
- HTTP CONNECT适配器只声明ByteStream，无法证明或执行的Egress精确维度固定拒绝。
- SystemProxy已通过连接回调观测ClientLeg；EgressLeg保持Unavailable/NotApplicable，不推断代理出口。
- SystemProxy解析到的代理URI或bypass状态进入稳定池键；映射变化不会命中旧池。
- 当前无法安全执行的SystemProxy精确源、接口、下一跳等物理约束固定拒绝，不隐式降级。
- 当前阶段Debug与Release全量均为87通过、0失败、0跳过，构建零警告，差异检查通过。

M6门禁已通过并进入M7。V2类型继续暂留到M9统一删除，当前工作区仍不是3.0发布候选包。

### M7：已完成（2026-07-22）

- 新增`TrackedResponseCollectionPolicy`，明确区分`FirstValidResponse`和`CollectUntilWindowEnds`，拒绝零窗口、零上限和未指定模式。
- 收集窗口内每条有效响应即时发布共同Request/Attempt/Branch父链及独立ResponseId；数量上限或窗口结束只生成一次Branch/Attempt/Request终态。
- 空窗口使用固定原因码进入重试/失败；存在具体协议或传输失败时保留最后失败，不用空窗口超时覆盖真实原因。
- 规划阶段新增不可被截止扫描器观察的`Planning`状态，消除非法策略被并发扫描错误覆盖为Timeout的Release竞态。
- `NetworkPingEndpoint<TKey>`使用Windows ICMP多回复缓冲支持IPv4广播和IPv6组播，显式窗口最多256条；IPv4/IPv6缓冲解析均有固定双回复测试。
- `NetworkNetBiosNameEndpoint<TKey>`支持IPv4广播Node Status窗口，同一socket和Transaction ID逐条发布响应者Observed证据。
- 新增`WindowsNetworkRecoveryPrimitives`，分别提供RS、DHCPv6 release、等待、renew、网卡重启和动作前后地址快照，不包含DDNS业务顺序。
- 恢复结果把请求接受、平台动作、地址变化和连通性复检分轴报告；网卡disable后的取消/异常路径强制执行enable补偿。
- 默认Windows恢复执行器在权限不足时预检拒绝；非Windows明确返回不支持。测试使用注入平台，不执行真实系统网络动作。
- Debug与Release全量均为101通过、0失败、0跳过，构建零警告，差异检查通过；非法策略Release专项连续5轮通过。

M7门禁已通过并进入M8。真实RS、DHCPv6和网卡重启动作只在M11明确授权的Windows网络环境验收；当前工作区仍不是3.0发布候选包。

### M8：库内实现已完成，真实管理员验收并入M11（2026-07-22）

- 已完成请求级隔离键、动态WFP会话原生后端、TCP/UDP Socket执行接入和`PolicyEnforced`证据。
- HTTP/DoH ExactNextHop强制使用`NoReuseRequestPolicy`，策略Lease由连接流持有至实际释放。
- Acquire异常、键碰撞、TCP成功和UDP取消均验证释放；Ping/ICMP继续明确拒绝ExactNextHop。
- HTTP/DoH已验证禁止共享池、TLS/SNI路径内Lease存活和连接回收；x64 WFP原生结构布局已建立固定测试。
- 全面Socket审计补齐PTR UDP→TCP分阶段回退、NBNS多响应手工Socket及HTTP CONNECT适配器客户端段，禁止任何阶段只绑定Socket却遗漏WFP策略。
- 新增不参与打包的`Iwesun.Runtime.Networks.WfpValidation`管理员验收程序；默认只读，真实加入与故意崩溃必须显式传入`--confirm-system-mutation`。
- 验收程序支持策略残留按PolicyId查询、固定本地端口、并发保持窗口和异常退出，可复用同一入口完成M11真实Windows门禁。
- 修正PTR双协议测试端口分配竞态和NBNS多响应顺序假设；Debug/Release全量均为113通过，专项不稳定测试连续5轮通过。
- 真实管理员WFP加入、同目标不同网关并发和进程崩溃残留检查保留为M11强制验收项；完成前不得声明3.0正式发布门禁通过。

### M9：已完成本地候选收口（2026-07-22）

- 已物理删除全部`Tracked*V2`协议端点、`NetworkRouteDirective`、可空Route、旧路由Flags、
  旧HTTP处理器和旧路由适配器；跟踪基类不再保存、快照或传播旧Route字段。
- TCP、UDP、Ping、PTR、NBNS、HTTP和DoH只保留`Network*Endpoint<TKey>`公开规范；新增反射测试，
  阻止V2标识及已删除兼容类型重新进入导出公共面。
- 公共示例已改为显式`RequestedAccessPlan`及四级GUID/Branch证据输出；版本统一为
  `3.0.0-rc.1`，README、CHANGELOG和活动端点文档已切换至3.0语义。
- 修正重试调度中旧Attempt快照与新Attempt并发覆盖的事件竞态，并改进PTR测试双协议共享端口选择，
  专项Release重复验证稳定。
- 最终全量Debug/Release均为103通过、0失败、0跳过，四项目解决方案构建均为零警告、零错误。
- 已生成并检查`Iwesun.Runtime.Networks.3.0.0-rc.1.nupkg`与符号包；主包包含Release程序集、README、
  CHANGELOG、候选发布状态、活动文档、历史发布说明和示例说明，WFP验收程序不进入包。
- M9收口时Runtime、DDNS Snap和Aether尚未执行M10迁移；该迁移随后已按下节完成。管理员WFP和
  Windows真实网络矩阵仍未执行，因此当前仍禁止对外发布或宣称3.0正式完成。

### M10：已完成三方消费者强制迁移（2026-07-22）

- Runtime嵌入边界初始升级到内部rc，M11真实环境修正和无后缀API收口后重建为`3.0.0-beta.1`，程序集文件版本固定为
  `3.0.0.0`；载荷清单、安装文档、
  验证脚本及全局/仓库集成技能参考均已切换到3.0合同。Runtime解决方案Debug/Release构建均为零警告、
  零错误；仅生成暂存发布目录并执行只读验证，未安装或启动服务。
- Aether的Ping、TCP、HTTP及DoH消费面已迁移为显式`RequestedAccessPlan`、统一GUID身份和3.0端点；
  HTTP/DoH连接池按稳定访问语义隔离。解决方案Debug/Release均为零警告、零错误，服务测试两种配置
  均为114/114通过，未启动服务。
- DDNS Snap的地址守护、网络扫描、Ping、PTR、NBNS、UDP、连接探测及直连HTTP处理器已迁移；普通
  请求、多响应和测试假端点均保留完整四级身份。解决方案Release及`Platform=x64`的Debug构建均为
  零警告、零错误；不指定x64平台的Debug命令会因既有Setup载荷目录约定失败，不是Networks编译错误。
  排除一项与Networks无关的既有安装包PowerShell文本断言后，候选重验Debug为562通过、9跳过，
  Release为561通过、9跳过。迁移后的`TrackedEndpointAwaiter`专项在两种配置均为2/2通过。
- 三仓活动源码和项目文件扫描均无`Tracked*V2`、`NetworkRouteDirective`、`NetworkRouteFlags`、
  `RouteBoundHttpMessageHandler`或`WithRequestId`残留；未引入临时别名或兼容包装。

M10门禁按Networks 3.0消费范围形成历史通过记录。其后真实组播联调发现三轴终态公共合同错误，beta.1候选资格已撤销。
必须先完成[协议、访问与终态更正方案](NETWORKS_3_0_PROTOCOL_ACCESS_TERMINAL_CORRECTION_PLAN.md)，再继续M11明确授权的
管理员WFP、真实Windows网络和恢复动作；更正门禁完成前禁止生成或分发新的私有beta包。

### M11：管理员与真实网络验收进行中（2026-07-22）

- 非管理员只读能力预检正确返回`platform-permission-missing`；用户明确授权后，通过UAC提升的验收子进程
  能真实打开WFP引擎。
- 首次真实加入暴露`FwpmEngineOpen0`认证服务错误使用0，能力查询形成假阳性；现已统一改为
  `RPC_C_AUTHN_WINNT`，且能力查询必须真实打开并关闭引擎。当时Debug/Release均为107/107通过；
  当前全量测试数量与结果以[发布状态](../RELEASE_STATUS.md)为准。
- 接口19、源`192.168.32.16`、下一跳`192.168.32.1`下，TCP 443和UDP 53策略均得到
  `PolicyEnforced`，正常关闭后无残留。
- 两条同目标、同下一跳、不同本地端口和PolicyId的TCP策略并发成功并独立清理；故意`FailFast`后
  独立查询`exists=false`，动态会话崩溃清理通过。
- RS、DHCPv6 release/wait/renew和网卡重启均完成真实动作。release移除DHCPv6地址，renew恢复；
  重启后证实地址刚被枚举时仍可能无法bind，等待IPv4和全局IPv6进入Preferred后精确连接复检通过。
- DDNS Snap地址守护真实DHCPv6源测试发现ExactSource与ExactInterface同时Required会因ICMP接口证据
  不可证明而形成`EvidenceIncomplete`。曾在业务层先验证源地址属于目标接口、底层只要求ExactSource并通过39项专项；
  该做法现仅记录为暴露公共库错误时的临时诊断路径，不是可接受合同或发布绕过。公共库修复后必须恢复完整Required约束，
  并重新执行DHCPv6源与IPv6/Ping真实验收。
- Aether固定阿里DNS IPv4/IPv6 DoH目标成功；固定Google IPv6 DoH目标失败。系统curl固定同一
  `dns.google` IPv6的TCP/443也在10秒连接超时，排除Aether独有实现错误。自动Ping Google IPv6成功，
  但当前DHCPv6源显式Ping Google超时，说明协议及源×目标组合不可互相替代。
- Aether真实代理验收首次暴露平台上下文把RouteAdapter/SystemProxy误送到仅支持Automatic/Direct的
  路由快照器并以`constraint-unresolved`拒绝。现已改为委托解析，不伪造到最终目标的本机直连快照；
  HTTP CONNECT RouteAdapter固定`8.8.8.8`并保留`dns.google` Host/SNI成功，Windows SystemProxy访问成功。
- 当前机器只有一个真实默认网关，无法完成“同目标、不同网关”并发隔离；Runtime CLI四级证据和其余
  路由变化矩阵仍待验收。因此M11保持进行中，不得标记正式发布。

上述真实环境修正及无版本后缀API收口曾生成`3.0.0-beta.1`，该包因精确接口Ping与三轴终态错误失效。
当前更正、全协议审计和真实组播验收已进入`3.0.0-beta.2`普通β；三方消费者迁移和旧名称移除事实继续有效，
剩余M11真实环境矩阵阻止正式版本冻结，但不阻止普通β安装和内部联调。

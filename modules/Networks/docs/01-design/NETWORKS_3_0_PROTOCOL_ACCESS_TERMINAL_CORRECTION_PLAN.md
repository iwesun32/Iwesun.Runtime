# Networks 3.0 协议、访问与终态更正方案

> **状态**：READY_FOR_EXECUTION  
> **日期**：2026-07-22  
> **适用范围**：Networks 3.0 请求—响应端点、Ping 多响应执行与分层终态  
> **反馈输入**：[精确接口 Ping 终态错误反馈意见书](NETWORKS_3_0_EXACT_INTERFACE_PING_FEEDBACK.md)  
> **上游合同**：[精确网络访问控制最终设计](PRECISION_NETWORK_ACCESS_CONTROL_FINAL_DESIGN.md)  
> **源码边界**：`NetworkPingEndpoint.cs`、`TrackedRequestReplyEndpointBase.cs`、`NetworkAccessEvidence.cs`

## 一、正式结论

Networks 接受反馈意见书关于以下问题的判断：

1. 精确接口 Ping 的合规性不能只按 Requested 选择器无条件降级；
2. `ProtocolOutcome.Succeeded + AccessCompliance.EvidenceIncomplete`不能映射为传输失败；
3. Branch、Attempt 和 Request 终态不得固定写入`EvidenceIncomplete`；
4. 组播 Ping 是一发零到多收，窗口内零响应、有效响应和迟到响应必须分别闭环；
5. 修正属于 Networks 公共跟踪合同，DDNS Snap 不承担兼容绕过。

本方案同时更正反馈意见书第7.2节的一项判定条件：**观察到协议响应不是证明本机执行了精确接口绑定的必要条件**。
如果 Windows API 已经用规范化后的源地址、接口作用域和目标 sockaddr 执行调用，则接口维度可以由
`ApiBinding`证明。无人响应只改变协议结果，不得反向抹除已经成立的路径执行证据。

## 二、统一三轴状态模型

所有 Branch、Attempt 和 Request 终态必须同时保存三个正交字段：

| 轴 | 回答的问题 | 允许值 |
| --- | --- | --- |
| `TerminalState` | 本层生命周期如何闭环 | Succeeded、Rejected、Failed、TimedOut、Cancelled、Superseded |
| `ProtocolOutcome` | 协议执行实际发生了什么 | Succeeded、Rejected、TimedOut、Cancelled、TransportFailed、ProtocolFailed |
| `AccessCompliance` | Requested 路径约束是否得到证明 | Satisfied、Violated、EvidenceIncomplete、NotApplicable |

三条强制规则：

- `TerminalState`不得被当作协议事实或路径证据的替代字段；
- `ProtocolOutcome`不得根据`AccessCompliance`反向改写；
- `AccessCompliance`必须从 Actual 各维证据聚合，不得根据协议成功与否推断。

典型组合固定如下：

| 场景 | TerminalState | ProtocolOutcome | AccessCompliance | Failure分类 |
| --- | --- | --- | --- | --- |
| 协议成功且全部Required约束满足 | Succeeded | Succeeded | Satisfied | None |
| 协议成功但Required证据不足 | Failed | Succeeded | EvidenceIncomplete | AccessEvidence |
| 协议成功但Required约束明确违反 | Failed | Succeeded | Violated | AccessViolation |
| 目标、源、接口及ScopeId绑定证据完整但窗口零响应 | TimedOut | TimedOut | Satisfied | Timeout |
| Windows传输调用失败 | Failed | TransportFailed | 按已有Actual聚合 | Transport |
| 对端返回协议错误 | Failed | ProtocolFailed | 按已有Actual聚合 | Protocol |
| 发送前能力或计划校验失败 | Rejected | Rejected | Violated或NotApplicable | Rejected |

`AccessEvidence`和`AccessViolation`表示建议增加的明确失败分类；在公共枚举冻结前，也必须至少使用独立原因码，
不得借用`Transport`。

## 三、Actual证据聚合标准

### 3.1 逐维判定

每个Required维度按以下顺序计算：

1. 实际值与Required约束明确冲突：`Violated`；
2. 实际值未冲突，但只有`Inferred`或`Unavailable`：`EvidenceIncomplete`；
3. 实际值通过该维度能力矩阵允许的`Observed`、`ApiBinding`、`PolicyEnforced`或`AdapterReported`证明：`Satisfied`；
4. 该维度由端点合法性矩阵判定不适用：`NotApplicable`。

Branch总体合规性按以下优先级聚合：

```text
Violated > EvidenceIncomplete > Satisfied > NotApplicable
```

其中`NotApplicable`只能在没有Required访问约束时成为总体结果。

### 3.2 IPv6 scoped multicast接口证明

IPv6组播Ping的接口维度可以标记为`ApiBinding + Satisfied`，必须同时满足：

1. `PathProvider == Direct`；
2. Requested Interface是Exact；
3. Resolved Interface与Requested Interface一致；
4. Resolved Source属于该接口并具有正确地址族；
5. 目标ScopeId为空时由接口规范化得到，非空时必须与接口索引一致；
6. `Icmp6SendEcho2`实际接收该Source和目标sockaddr作为调用参数；
7. 调用没有在提交阶段返回接口、地址或参数错误。

收到Echo Reply不是上述判定的前置条件。响应只决定协议轴是`Succeeded`还是窗口结束后的`TimedOut`。

### 3.3 目标与响应方必须分离

对`ff02::1%19`发送时：

- Actual Destination仍是实际提交的组播目标`ff02::1%19`；
- 每个Echo Reply的源地址是独立的Responder Address；
- 不得因为收到单播响应，就把Actual Destination替换成响应方单播地址。

如果现有响应合同没有独立的Responder字段，实施时必须补齐协议响应证据，不能复用访问计划的Destination维度。

## 四、组播Ping收集窗口标准

IPv6组播Echo Request遵循RFC 4443：接收节点应当回应，但不是强制保证，因此请求天然是一发零到多收。

### 4.1 窗口内收到有效响应

- 每条有效响应即时发布自己的Response身份和Responder证据；
- 第一条响应即证明协议至少成功一次；
- `CollectUntilWindowEnds`继续收集，直到窗口结束或达到`MaxResponses`；
- 聚合终态为`ProtocolOutcome.Succeeded`；
- `AccessCompliance`只按发送Branch的Actual路径证据计算。

### 4.2 窗口内没有有效响应

- 聚合终态为`ProtocolOutcome.TimedOut`；
- 原因码使用`response-collection-window-empty`或等价的明确超时原因；
- 不得使用`TransportFailed`；
- 已取得的Source、Interface、Destination和IP政策证据必须保留；
- 只有目标、源地址、接口及ScopeId四项API绑定证据全部成立时，才允许`TimedOut + Satisfied`；任一项证据不足仍为
  `TimedOut + EvidenceIncomplete`，不得因零响应统一提升为`Satisfied`。

### 4.3 无效与迟到响应

- 标识、序列、载荷或地址族不匹配的响应不得计入有效响应数；
- 窗口关闭后到达的合法响应发布LateResponse，不得重开Request；
- 达到`MaxResponses`后关闭窗口，其后响应同样按迟到处理；
- 每个响应保留完整父链，聚合窗口只产生一个Branch终态。

## 五、跟踪基类更正边界

`TrackedRequestReplyEndpointBase`当前的布尔`IsSuccessfulResponse`不足以承载三轴事实。实施时必须做到：

1. Endpoint向基类提供响应自身的`ProtocolOutcome`、`AccessCompliance`和失败详情；
2. `CreateBranchTerminal`接收真实合规性，不再固定写入`EvidenceIncomplete`；
3. Attempt和Request终态从决定性Branch聚合，不根据布尔成功重新构造协议结果；
4. 只有没有协议结果的基础设施异常，才允许从`NetworkFailure`映射`ProtocolOutcome`；
5. `RequestAcknowledged`只在最终合同满足时发布；
6. `RequestFailed`允许携带`ProtocolOutcome.Succeeded`，调用方必须查看三轴字段判断失败原因；
7. `ResponseObserved`始终保存响应原始事实，终态聚合不得覆盖或篡改它。

聚合采用决定性Branch规则：成功请求使用赢家或有效响应所在Branch的合规性；没有成功Branch时才聚合全部终态Branch，
并按`Violated > EvidenceIncomplete > Satisfied > NotApplicable`计算访问轴。失败Branch的证据不能污染已经满足合同的赢家Branch。

## 六、Ping执行后端能力矩阵

实施前必须逐条登记，不允许用统一猜测替代：

| 后端 | Automatic | Exact Source | Exact Interface | 多响应 | 证明来源 |
| --- | --- | --- | --- | --- | --- |
| 托管`Ping.SendPingAsync` | 支持 | 按平台能力 | 不宣称支持 | 单响应 | Observed/Inferred |
| Windows IPv4 ICMP API | 支持 | 按调用参数验证 | 按后端验证 | 按实现验证 | ApiBinding/Observed |
| Windows `Icmp6SendEcho2`单播 | 支持 | 支持时显式声明 | Scope与Source共同验证 | 单响应 | ApiBinding/Observed |
| Windows `Icmp6SendEcho2`组播 | Direct | 必须解析 | 必须验证Scope | 零到多响应 | ApiBinding＋每响应Observed |

不能执行Required Exact的后端必须发送前拒绝。能够执行但无法达到合同允许证明等级的后端，应发送前拒绝，或在调用方
明确允许证据不完整时返回`EvidenceIncomplete`；不得静默回退到Automatic。

## 七、实施批次

### 批次A：合同与测试先行

- 固化三轴组合矩阵和Failure分类；
- 增加基类终态传播测试；
- 增加`Succeeded + EvidenceIncomplete`不得变成Transport测试；
- 分别增加四项绑定证据完整时的`TimedOut + Satisfied`，以及任一证据不足时的
  `TimedOut + EvidenceIncomplete`测试；
- 增加多响应窗口唯一终态和迟到响应测试。

### 批次B：跟踪基类

- 取消终态合规性的硬编码；
- 让Branch终态继承Endpoint提供的真实协议与访问结果；
- 修正Attempt、Request聚合和Acknowledged/Failed事件条件；
- 保持完整响应与AttemptHistory不丢失。

### 批次C：Ping证据

- 重写Ping逐维证据和总体合规性聚合；
- 分离Actual Destination与Responder Address；
- 按后端能力严格处理Exact Interface；
- 修正组播零响应、有效响应、上限关闭和迟到响应。

### 批次D：全部协议端点审计

- TCP、UDP、HTTP、DoH、PTR和NBNS逐一检查是否也把双轴压成布尔值；
- 任何协议成功但访问不满足的结果均不得映射为Transport；
- 所有Branch、Attempt和Request终态必须携带真实AccessCompliance。

### 批次E：真实环境验收

- Networks Debug/Release聚焦及全量测试；
- `%19` IPv6 scoped multicast真实接口测试；
- 零响应接口测试，只有目标、源地址、接口及ScopeId绑定证据完整时确认`TimedOut + Satisfied`；
- Scope冲突发送前拒绝测试；
- DDNS Snap Debug Server真实扫描和Runtime CLI逐请求终态复核；
- 所有诊断开关和协作断点恢复为关闭。

## 八、2026-07-22实施记录

- 批次A～D已在当前源码完成：基类不再固定合规性，七类协议端点显式提供协议轴与访问轴事实；
- 新增`AccessEvidence`与`AccessViolation`失败分类，二者映射到`ProtocolOutcome.Succeeded`，不再冒充Transport；
- 成功Attempt采用决定性Branch事实，其他分支以`Superseded / Cancelled / NotApplicable`闭环，不污染赢家；
- Ping执行器拥有原生响应收集窗口，避免基类截止时间与`Icmp6SendEcho2`返回时刻争抢终态；
- IPv6精确接口会在提交前把无Scope的链路本地/组播目标规范化到已解析接口，成功提交即形成目标、源和接口
  `ApiBinding`证据；响应与否只改变协议轴；
- IPv6全局单播不允许伪造ScopeId；原生API绑定Exact Source且Windows接口目录确认该Source属于Exact Interface时，
  接口维度使用`AdapterReported + Satisfied`。全局地址携带ScopeId发送前以`ipv6-scope-not-applicable`拒绝；
- Windows接口快照现携带真实UnicastAddresses；Exact Interface与Resolved Source不匹配时以
  `source-interface-conflict`拒绝，调用方意图不得冒充接口归属证据；
- 单响应和多响应Ping均由执行器拥有协议超时，并设置原生完成看门狗，避免基类抢先发布模糊`response-timeout`；
- `Actual Destination`固定为提交目标，新增`ResponderAddress`保存每个实际响应方；
- Windows SDK的`IPV6_ADDRESS_EX`按1字节压缩布局解析，地址、Scope、Status和RoundTripTime偏移已修正；
- Debug/Release合同测试均为126/126；本机`ff02::1%19`得到一个响应并闭环为`Succeeded + Satisfied`，
  `ff02::1%8`零响应闭环为`TimedOut + Satisfied`；验证入口见
  [Ping三轴β验证指南](../03-reference/NETWORKS_3_0_PING_TERMINAL_BETA_TEST_GUIDE.md)；
- DDNS Snap已用隔离DLL消费根通过真实DHCPv6源地址、接口19和公网目标探测；完整Service恢复链未运行，整体发布状态
  继续保持阻断。

## 九、发布门禁

满足以下条件前，Networks 3.0不得恢复β发布通过状态：

- 终态不再固定写入`EvidenceIncomplete`；
- `%19`有效响应得到`Succeeded + Satisfied`；
- 零响应不再误报Transport；
- Actual Destination与Responder语义分离；
- 其他协议端点完成同类布尔压缩审计；
- Debug/Release、真实IPv6接口和DDNS消费验收全部通过。

本方案只冻结更正标准和实施顺序，不授权安装、启动服务、改变系统网络状态或发布包。

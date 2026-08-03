# Networks 3.0 精确接口 Ping 终态错误反馈意见书

> **状态**：FEEDBACK_CONFIRMED  
> **反馈方**：DDNS Snap  
> **反馈日期**：2026-07-22  
> **适用版本**：Iwesun.Runtime.Networks 3.0 当前实现  
> **源码参考**：`NetworkPingEndpoint.cs`、`TrackedRequestReplyEndpointBase.cs`  
> **上游合同**：[精确网络访问控制最终设计](PRECISION_NETWORK_ACCESS_CONTROL_FINAL_DESIGN.md)  
> **正式回复**：[协议、访问与终态更正方案](NETWORKS_3_0_PROTOCOL_ACCESS_TERMINAL_CORRECTION_PLAN.md)  
> **下游验证**：DDNS Snap IPv6 邻居发现与 Hades 在线状态发布

## 一、反馈结论

Networks 3.0 的 `NetworkPingEndpoint<TKey>` 能够把 IPv6 组播请求发送到指定接口并取得实际响应，但当前实现只要请求包含精确接口约束，就无条件把 `AccessCompliance` 设为 `EvidenceIncomplete`。随后终态判定又把“协议成功、访问证据不完整”转换成 `RequestFailed`，并错误归类为 `NetworkFailureKind.Transport`。

本问题不是目标构造错误、接口选择错误、Hades 不响应或 `RequestAcknowledged`/`RequestFailed` 合同过时，而是 Ping 后端的 Actual 证据构造、合规性计算和失败映射没有遵守 Networks 3.0 的双轴终态合同。

## 二、复现环境

- 主机：Apollo
- 消费者：DDNS Snap Debug Snap Server
- 诊断方式：Iwesun Runtime CLI、协作断点、逐 RequestId 终态快照
- 局域网物理接口：InterfaceIndex 19
- 目标设备：Hades
- Hades MAC：`FC:34:97:0D:7E:AA`
- IPv6 组播目标：`ff02::1%19`

DDNS Snap 同轮还枚举了接口 8、16、21。接口 21 为 WSL 虚拟接口；每个组播请求都携带独立 ScopeId，因此这些附加请求不会改变 `%19` 请求的目标接口。

## 三、请求合同

实际请求访问计划如下：

| 维度 | 请求值 |
| --- | --- |
| PathProvider | `Direct` |
| Destination | `Exact(ff02::1%19)` |
| Source | `SystemSelected` |
| Interface | `Exact(InterfaceIndex=19)` |
| NextHop | `SystemSelected` |
| ResponsePolicy | `CollectUntilWindowEnds` |
| AllowRetry | `false` |

解析后得到接口 19 对应的源 IPv6、接口身份和带 ScopeId 19 的目标地址。Windows 后端使用 `Icmp6SendEcho2`，并传入解析后的 `sourceAddress` 与 `targetAddress`。

## 四、真实运行证据

### 4.1 组播逐接口终态

| 目标 | 请求接口 | 真实结果 | Networks 终态 | 原因 |
| --- | ---: | --- | --- | --- |
| `ff02::1%8` | 8 | 无响应 | Failed | `response-collection-window-empty` |
| `ff02::1%16` | 16 | 无响应 | Failed | `response-collection-window-empty` |
| `ff02::1%19` | 19 | 收到响应并激活 Hades NDP | Failed | `access-evidence-incomplete` |
| `ff02::1%21` | 21 | 无有效响应 | Pending/Timeout | 外层观察窗口结束 |

`%19` 请求的实测总耗时为 802 ms。请求完成后，Windows NDP 中出现 Hades 的 MAC 和两个有效公网 IPv6。

### 4.2 Hades 后续证据

| IPv6 | Ping | MAC | 扫描结论 |
| --- | --- | --- | --- |
| `240e:36f:931:2600:5558:ad73:85d:6b68` | Success，约 1 ms | `FC:34:97:0D:7E:AA` | Online |
| `240e:36f:931:2600:fe34:97ff:fe0d:7eaa` | Success，约 1 ms | `FC:34:97:0D:7E:AA` | Online |
| `240e:36f:931:2600:f023:476f:93eb:4ba8` | 无响应 | 同设备旧记录 | Offline |

DDNS Snap 最终扫描发布包含 4 台命名机器，Hades 整机为 Online。以上事实证明 `%19` 组播已经在正确的二层网络生效。

## 五、错误代码链

### 5.1 执行阶段实际成功

`NetworkPingExecutor.SendNativeIpv6Multiple` 调用 `Icmp6SendEcho2`：

```text
Resolved.Source + Destination(ff02::1%19)
    -> Icmp6SendEcho2
    -> 收到 ICMPv6 响应
    -> ProtocolOutcome.Succeeded
```

因此不存在“未发送”“发往其他接口”或“对端未响应”的证据。

### 5.2 合规性被无条件降级

当前 `EvaluateCompliance` 的逻辑是：只要 Interface 或 NextHop 不是 `SystemSelected`/`NotApplicable`，就返回 `EvidenceIncomplete`。它没有检查：

- 解析接口是否与请求接口一致；
- 解析源地址是否属于该接口；
- IPv6 ScopeId 是否与接口一致；
- 底层 API 是否使用该源地址和 ScopeId；
- 是否取得了实际响应。

所以任何 `Exact Interface` Ping 都不可能得到 `AccessCompliance.Satisfied`。

### 5.3 Actual 证据与实际执行不一致

当前 `CreateEvidence` 对接口维度固定发布：

```text
ProofKind.Inferred
AccessCompliance.EvidenceIncomplete
```

但本次执行已经通过源地址和 IPv6 ScopeId对 Windows ICMP API 施加了接口约束。实际执行与证据标记不一致。

### 5.4 双轴终态被压扁成传输失败

当前成功判定要求：

```text
ProtocolOutcome == Succeeded
AND AccessCompliance == Satisfied
```

当结果为 `Succeeded + EvidenceIncomplete` 时，请求进入失败路径。失败类型又根据 `ProtocolOutcome` 映射；`Succeeded` 没有专用分支，最终落入默认 `NetworkFailureKind.Transport`。

因此公共库发布了与事实冲突的结论：协议已经成功，却被标记为传输失败。

## 六、违反的正式语义

Networks 3.0 最终设计明确规定：

1. `ProtocolOutcome` 与 `AccessCompliance` 是两个独立轴；
2. 协议成功不能覆盖访问不合规；
3. 协议失败也不能抹除已经取得的路径证据；
4. Actual 证据必须逐维说明 Requested、Resolved、Actual 和 ProofKind；
5. `ApiBinding` 可以在维度能力矩阵允许时满足 Required Exact。

当前实现同时违反第 1、3、4 项：它没有保留“协议成功、证据不完整”的原始事实，并用 `Transport` 覆盖了已经观察到的成功响应。

## 七、修正意见

### 7.1 先冻结精确接口 Ping 的能力边界

对每一种 Ping 执行路径明确回答：底层是否真正执行并能够证明 Exact Interface。

- 能执行并证明：发送并发布 `Satisfied`；
- 能执行但无法证明：保留 `Succeeded + EvidenceIncomplete`，不得改写为 Transport；
- 根本不能执行 Required Exact：发送前以明确的能力错误拒绝，不得先发送后伪装成传输失败。

### 7.2 IPv6 scoped multicast 的建议判定

只有以下条件全部满足时，接口维度可标为 `ApiBinding + Satisfied`：

1. `PathProvider == Direct`；
2. `Interface.Kind == Exact`；
3. `Resolved.Interface` 与请求接口一致；
4. `Resolved.Source` 属于该接口；
5. 目标是需要作用域的 IPv6 地址，且目标 ScopeId 与接口索引一致；
6. Windows ICMP 调用实际使用该源地址和目标 sockaddr；
7. 实际观察到协议响应。

此规则只适用于能够由 Windows API 和 sockaddr 共同约束的路径，不得推广成“所有 Exact Interface 自动满足”。

### 7.3 重写合规性计算

`EvaluateCompliance` 不应只读取 Requested 选择器。它应根据 ActualAccessEvidence 逐维聚合：

```text
任一 Required 维度明确违反 -> Violated
无违反但至少一项 Required 证据不足 -> EvidenceIncomplete
全部 Required 维度得到允许的证明 -> Satisfied
没有适用约束 -> NotApplicable
```

### 7.4 修正失败分类

不得再把 `ProtocolOutcome.Succeeded + AccessCompliance.EvidenceIncomplete` 转成 `NetworkFailureKind.Transport`。

建议优先保持完整双轴 RequestTerminal；如果跟踪基类必须使用失败类型，应增加明确的访问约束/证据类型，而不是复用 Transport。

### 7.5 不建议的临时修法

以下修改均不可接受：

- 看到 ICMP Success 就无条件把全部访问约束设为 Satisfied；
- 删除 AccessCompliance 检查；
- 把 Interface Exact 改成 SystemSelected 来隐藏公共库错误；
- 在 DDNS Snap 中把 `access-evidence-incomplete` 当普通成功长期兼容；
- 仅增加超时而不修正证据与终态语义。

## 八、验收矩阵

| 场景 | 预期 ProtocolOutcome | 预期 AccessCompliance | 预期终态 |
| --- | --- | --- | --- |
| `ff02::1%19`，Exact Interface 19，收到响应且路径可证明 | Succeeded | Satisfied | Acknowledged |
| `ff02::1%19`，协议成功但平台无法证明接口 | Succeeded | EvidenceIncomplete | 保留双轴事实，不得为 Transport |
| Exact Interface 不受当前后端支持 | Rejected | EvidenceIncomplete/NotApplicable | 发送前明确拒绝 |
| `%8/%16/%21` 无响应 | TimedOut | 按已取得路径证据计算 | Failed/TimedOut |
| 普通 Automatic IPv6 Ping 成功 | Succeeded | Satisfied | Acknowledged |
| Exact Interface 与 IPv6 ScopeId 冲突 | Rejected | Violated | 发送前拒绝 |

## 九、回归要求

修正后至少完成：

1. Networks Debug/Release 聚焦测试；
2. IPv6 scoped multicast 真实接口测试；
3. Actual Source、Interface、Destination 证据断言；
4. `Succeeded + EvidenceIncomplete` 双轴终态测试；
5. DDNS Snap Debug Server 真实扫描；
6. Runtime CLI 逐 RequestId 终态复核；
7. Hades 两个有效 IPv6 进入 Online，旧地址保持 Offline；
8. 所有诊断开关和协作断点恢复为关闭。

## 十、与 DDNS Snap 消费端修正的边界

DDNS Snap 同时发现并修正了独立的批量观察窗口问题：原窗口只按单请求尝试次数计算，没有考虑一批请求进入 V3 端点后的调度时间，导致 `%19` 请求尚未闭环时消费者已经停止观察。

该问题属于消费者适配；本意见书所述 `Succeeded + EvidenceIncomplete -> Transport Failed` 属于 Networks 公共库实现错误。两者必须分别修正，不能用延长 DDNS 超时掩盖公共库的证据和终态错误。

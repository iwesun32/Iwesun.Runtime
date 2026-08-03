# DDNS Snap 对精确网络访问控制总体规划的正式回复

> **状态**: SUBMITTED
>
> **回复方**: DDNS Snap
>
> **回复日期**: 2026-07-21
>
> **目标规划**: [精确网络访问控制总体规划](../PRECISION_NETWORK_ACCESS_CONTROL_MODEL.md)
>
> **前序回复**: [DDNS Snap对精确路由语义优化RFC的正式回复](DDNS_SNAP_ROUTING_SEMANTICS_RESPONSE.md)
>
> **结论**: 有条件接受

## 一、总体确认

DDNS Snap确认，《精确网络访问控制总体规划》已经承接前序回复提出的主要条件，不再把四种顶层路由模式误作完整访问合同。

总体规划已覆盖：

1. 非空`RequestId`作为公开请求、响应、失败、取消、超时和完成的绝对根身份；
2. `TKey`仅供调用方查询，不参与内部Pending、重试、完成、移除或等待；
3. `AttemptId`、`BranchId`和`ResponseId`采用GUID并显式建立父子关系；
4. 四种顶层模式只表示路径提供者；
5. 目标、源地址、接口、下一跳、本地端点、路由作用域和IP层政策使用正交选择器表达；
6. `Direct`允许只固定网卡、网关、源地址或其他维度组合，不强迫三项全部固定；
7. DNS/PTR的查询对象、Resolver及Resolver传输路径分别建模；
8. HTTP/DoH的URL、Host、SNI、证书身份与传输目标分别建模；
9. 代理客户端段与代理出口段分别提供证据；
10. `RequestedAccessPlan`、`ResolvedAccessPlan`和`ActualAccessEvidence`三层分离；
11. 协议结果和访问约束合规采用双轴状态；
12. IPv4广播、IPv6组播和NBNS支持一发多收及时间窗闭环；
13. 入站Listener、NAT映射、专用地址/端口池和系统路由管理移出本合同；
14. Windows Socket、路由查询、WFP策略和RouteAdapter按能力选择执行后端；
15. RFC冻结前不创建半成品公共API，冻结后以Networks 3.0强制迁移所有消费者。

因此，DDNS Snap前序回复中的逐维选择、能力矩阵、ScopeId、Resolver、多响应、路径证据、恢复原语和迁移条件，均已进入总体规划。

## 二、必须补充的GUID父子链

### 2.1 当前缺口

总体规划已经定义：

```text
RequestId
└─ AttemptId
   └─ BranchId
```

但当前`ResponseId`只明确关联`RequestId`和`AttemptId`。当一次尝试包含Preferred源地址竞速、Happy Eyeballs、候选Resolver或其他并发分支时，响应还必须能够证明自己来自哪个`BranchId`。

如果`ResponseId`跳过`BranchId`，将出现以下问题：

- 多个分支均收到有效响应时，无法确定每条响应的真实执行路径；
- 迟到响应只能定位到Attempt，无法定位到已完成或已取消的分支；
- 路径证据可能被错误归并到同一Attempt；
- 一发多收与分支竞速叠加时，响应身份不再绝对；
- Runtime诊断无法构造无歧义的父子执行树。

### 2.2 正式建议

DDNS Snap建议所有Attempt至少创建一个Branch，包括没有竞速的普通请求：

```text
RequestId
└─ AttemptId
   └─ BranchId
      └─ ResponseId
```

父子关系固定为：

| 身份 | 直接父身份 | 说明 |
| --- | --- | --- |
| `RequestId` | 无 | 一次公开操作的绝对根身份 |
| `AttemptId` | `RequestId` | T1/T2/T3中的一次外层尝试 |
| `BranchId` | `AttemptId` | 一条实际执行路径；普通Attempt也创建唯一Branch |
| `ResponseId` | `BranchId` | 一条独立网络响应或观测 |

每个子对象可以冗余携带祖先ID以便查询和序列化，但直接父关系必须唯一且非空。

建议公共结果至少携带：

```text
RequestId
AttemptId
BranchId
ResponseId
```

失败、取消和完成也应归属于正确层级：

- Branch级失败携带`BranchId`；
- Attempt聚合失败携带`AttemptId`并列出其Branch；
- Request最终失败或完成携带`RequestId`并引用最终Attempt；
- 迟到响应保留原`ResponseId`及完整祖先链；
- 一发多收的每条观测使用独立`ResponseId`，但共享产生它们的`BranchId`。

### 2.3 不接受的替代方案

DDNS Snap不接受以下表达：

- `BranchId = Guid.Empty`表示普通请求；
- `Guid? BranchId = null`表示没有竞速；
- 使用分支序号代替`BranchId`；
- 只根据源地址、接口、目标地址或响应顺序反推分支；
- 让`ResponseId`直接挂在Attempt下，同时另用可空字段补充Branch；
- 把`TKey`、DNS TransactionId、ICMP序列号或NAT五元组当作父身份。

统一创建Branch的额外成本可控，并能消除普通请求与竞速请求之间的两套身份结构。

## 三、事件驱动和多响应确认

DDNS Snap确认总体规划的事件语义：

- 每条完整响应到达后立即发布，不等待同批其他分支或请求；
- `ResponseId`在响应进入接收边界时即确定；
- 多响应窗口结束只发布唯一聚合终态，不延迟窗口内单条观测；
- Branch竞速的赢家不会抹除其他已产生响应或失败证据；
- 取消未完成分支时仍发布分支终态；
- 新数据覆盖旧业务数据不影响网络请求自身的GUID闭环。

这与DDNS Snap松散数据模型一致，不建立跨设备、跨服务器或跨管线轮次的事务依赖。

## 四、路径证据确认

DDNS Snap接受总体规划的三层证据和双轴结果：

```text
RequestedAccessPlan
    ↓
ResolvedAccessPlan
    ↓
ActualAccessEvidence
```

```text
ProtocolOutcome × AccessCompliance
```

每份`ActualAccessEvidence`必须归属于具体`BranchId`。Attempt级和Request级结果只能聚合Branch证据，不得重新制造一个无法对应实际发送路径的“统一实际路径”。

对于代理和RouteAdapter：

- `ClientLeg`和`EgressLeg`分别归属同一Branch；
- 适配器不能证明的出口事实标记为证据不完整；
- 客户端接口、源地址或网关不能冒充代理出口事实；
- Required精确约束无法执行或证明时，不能标记`AccessCompliance=Satisfied`。

## 五、Windows执行边界确认

DDNS Snap接受以下边界：

- Socket绑定和接口选项用于可执行的源地址、接口、本地端点及IP层约束；
- `GetBestRoute2`和路由快照用于解析候选路径，不能冒充发送事实；
- WFP连接策略只作为精确下一跳的候选执行后端；
- WFP能力必须经过权限、生命周期、并发隔离和请求范围清理验证；
- 单次请求不得静默修改全局路由表；
- 后端无法安全隔离或证明约束时，发送前明确拒绝。

DDNS Snap地址守护程序继续负责RS、DHCPv6 release/renew、复检和网卡重启的业务顺序；Networks只提供分步恢复原语和证据。

## 六、冻结条件

DDNS Snap将总体规划维持为“有条件接受”，直至以下事项完成：

- [ ] 将所有Attempt至少一个Branch的规则写入总体规划；
- [ ] 将`ResponseId -> BranchId -> AttemptId -> RequestId`固定为权威父子链；
- [ ] 冻结选择器类型、派生规则、冲突规则和合法性矩阵；
- [ ] 冻结路径证据结构以及各层GUID字段；
- [ ] 冻结多响应窗口、Branch竞速和迟到响应的组合状态机；
- [ ] 冻结Windows Socket、WFP和路由查询的能力边界；
- [ ] 冻结DNS/PTR Resolver和HTTP连接池隔离合同；
- [ ] Runtime和Aether确认总体规划；
- [ ] 完成Networks 2.x到3.0的三方迁移及真实网络验收矩阵。

上述条件满足后，DDNS Snap同意将总体规划提升为`ACCEPTED`并进入Networks 3.0实施。

## 七、最终结论

DDNS Snap确认总体规划已经覆盖前序路由RFC回复中的主要诉求。当前唯一新增的结构性意见是：公开执行身份必须形成无空值、无双轨、可完整追溯的四级GUID链。

在该身份链及其他冻结条件定稿前，结论为**有条件接受**；不得提前修改Networks公共API。

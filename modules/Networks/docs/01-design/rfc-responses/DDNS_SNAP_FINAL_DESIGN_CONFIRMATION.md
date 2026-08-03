# DDNS Snap 对 Iwesun.Runtime.Networks 3.0 最终设计的正式确认书

> **状态**: ACCEPTED
>
> **确认方**: DDNS Snap
>
> **确认日期**: 2026-07-22
>
> **目标文档**: [Iwesun.Runtime.Networks 3.0精确网络访问控制最终设计](../PRECISION_NETWORK_ACCESS_CONTROL_FINAL_DESIGN.md)
>
> **前序回复**:
> [路由语义RFC回复](DDNS_SNAP_ROUTING_SEMANTICS_RESPONSE.md)、
> [总体规划回复](DDNS_SNAP_PRECISION_NETWORK_ACCESS_CONTROL_RESPONSE.md)

## 一、确认结论

DDNS Snap确认，最终设计已完整承接前序回复中的架构要求，同意其作为Networks 3.0唯一实施合同。

DDNS Snap不再要求保留旧`Route = null`、空地址、接口索引`0`、默认枚举或其他哨兵兼容语义；同意通过编译错误发现并强制迁移全部旧调用点。

本确认表示设计评审完成，不表示未经实施计划、测试和真实网络验收即可发布Networks 3.0。

## 二、确认的权威合同

### 2.1 统一身份树

```text
RequestId
└─ AttemptId
   └─ BranchId
      └─ ResponseId
```

- 所有公开跟踪身份均为非空`Guid`；
- 每个Attempt至少创建一个Branch，普通请求也不例外；
- `TKey`只用于允许重复的调用方查询；
- Pending、匹配、重试、完成、移除和迟到响应均按GUID身份闭环；
- 一发多收的每条观测具有独立`ResponseId`并保留完整父链。

### 2.2 正交访问合同

- 四种PathProvider只决定路径提供者，不代替完整访问计划；
- 目标、源地址、接口、下一跳、本地端点、路由作用域和IP政策分别使用非空强类型选择器；
- `Unspecified = 0`永久非法；
- `Direct`支持只指定接口、只指定源地址、只指定网关及完整物理路径等合法组合；
- Required约束无法安全执行或证明时必须拒绝或失败，不得静默降级。

### 2.3 重试与Branch

DDNS Snap确认以下规则：

> 重试不得改变PathProvider、选择器类型、Required约束或规范候选集合；新的Attempt可以在同一冻结RequestedAccessPlan内重新解析和执行，并为每条实际路径创建全新Branch。

因此，`PreferredSet`可以在新Attempt内重新竞速并由原集合中的不同成员获胜；接口Index、地址状态或路由发生变化时可以生成新的`ResolvedAccessPlan`，但不得改变稳定身份约束或放宽请求计划。需要改变候选集合、代理、接口身份或其他Required约束时，调用方必须创建新的`RequestId`。

### 2.4 协议与证据

- DNS查询对象与Resolver及其传输路径分离；
- HTTP/DoH的URL、Host、SNI和证书身份与实际传输目标分离；
- ClientLeg与EgressLeg分别提供证据；
- 广播、组播和NBNS逐条即时发布响应，窗口只发布唯一聚合终态；
- `RequestedAccessPlan`、`ResolvedAccessPlan`和`ActualAccessEvidence`不得混用；
- Actual证据归属于Branch，Attempt和Request只聚合Branch证据；
- `ProtocolOutcome`与`AccessCompliance`分别得出结论；
- 请求值或路由查询结果不能冒充实际发送事实。

### 2.5 Windows执行与恢复

- Socket绑定和接口选项只声明其实际支持的约束；
- `GetBestRoute2`和路由快照用于解析候选，不是发送事实；
- WFP只在权限、并发隔离、撤销和崩溃清理合同满足后承担请求级精确下一跳；
- 单次请求不得静默修改全局路由表；
- Networks提供RS、DHCPv6 release/renew、等待、网卡重启和地址快照原语；
- DDNS Snap地址守护程序继续独立决定恢复顺序和复检政策；
- 请求接受、系统动作成功、地址变化和复检成功必须分别报告。

## 三、DDNS Snap迁移范围

Networks 3.0实施后，DDNS Snap将一次性迁移：

- IPv4/IPv6局域网扫描；
- IPv6组播、链路本地ScopeId及NBNS发现；
- 地址守护的逐源精确Ping；
- 非代理物理路由守护及其消费者；
- 权威DNS、PTR、Echo和DNS供应商HTTP访问；
- 伙伴服务器地址解析、Push和Pull；
- Agent向两台服务器的数据发送；
- RS、DHCPv6 release/renew和网卡重启恢复动作；
- Runtime诊断中的Request、Attempt、Branch、Response证据投影。

迁移不得改变DDNS业务语义。设备Online、地址公网连通性、Echo验证、DNS注册状态、设备合并和发布订阅仍由DDNS核心数据模型决定。

## 四、实施与发布门禁

以下事项属于实施和发布门禁，不再是设计意见：

- [ ] Networks 3.0公开类型和合法性矩阵按最终设计实现；
- [ ] WFP并发隔离、撤销、异常清理和崩溃恢复专项验证通过；
- [ ] Listener、NAT及地址/端口资源管理保持独立合同；
- [ ] Runtime、DDNS Snap和Aether迁移清单全部完成；
- [ ] 固定测试、三方构建和Windows真实网络验收矩阵全部通过；
- [ ] Clash/VPN/TUN环境中的Automatic、Direct、SystemProxy和RouteAdapter行为均取得真实证据；
- [ ] 旧2.x类型、隐式兼容和模糊哨兵表达从消费者代码与发布载荷中完全清除。

## 五、最终意见

DDNS Snap正式接受《Iwesun.Runtime.Networks 3.0精确网络访问控制最终设计》。后续实施必须以该文档为唯一合同，旧RFC和总体规划只保留为设计依据。

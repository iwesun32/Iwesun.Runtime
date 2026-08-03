# Iwesun.Runtime.Networks 3.0.0-beta.4 升级迁移报告

> **状态**: BETA4_GENERAL_BETA_INSTALLED_VALIDATED  
> **日期**: 2026-07-28  
> **上一普通β**: 3.0.0-beta.3  
> **目标普通β**: 3.0.0-beta.4  
> **程序集/文件版本**: 3.0.0.0

## 一、审计范围

本轮以当前源码为事实来源，审阅 `src/Iwesun.Runtime.Networks` 全部43个活动C#文件、
12,436行源码，并交叉核对最终设计、请求跟踪合同、HTTP/DoH合同、API迁移矩阵、测试、
样例、验证工具与发布状态。

完整架构分层如下：

| 层 | 责任 | 结论 |
| --- | --- | --- |
| 值与编码 | IP/MAC、DNS wire、固定错误 | 4/16字节裸IP、17字节IP值、7字节MAC值边界明确 |
| 请求跟踪 | FIFO、Pending、重试、完成、多响应 | 四级GUID为唯一身份，TKey只读查询 |
| 精确访问 | 选择器、解析、能力、证据、终态 | Requested/Resolved/Actual与协议/合规双轴分离 |
| HTTP/DoH | 协议身份、TLS、连接池、代理 | Host/SNI不随传输IP变化；池键包含稳定访问语义 |
| Socket/UDP | TCP、UDP、流水号、长期池 | 活动、隔离、迟到与容量边界闭环 |
| 探测/解析 | Ping、PTR、NBNS | 单响应、多响应和Resolver访问计划统一 |
| Windows | 路由快照、WFP、ARP/NDP、恢复 | 原生ABI与公共值类型分层，Actual证据不由请求伪造 |
| 特殊模型 | Process、PowerShell、本地快照 | 不复制协议请求—响应公共抽象 |

活动C#源码不存在 `BinaryIpAddress`、`NetworkRouteDirective`、`Tracked*V2/V3`兼容实现，
也不存在TODO、FIXME或`NotImplementedException`占位。

## 二、beta.4相对beta.3的变化

### 2.1 地址族显式化

删除按长度猜测地址族的：

```text
IpAddressValue.FromAddressBytes
```

替换规则：

| 来源 | beta.4入口 |
| --- | --- |
| DNS A、ICMPv4、IPv4 SOCKADDR裸地址 | `FromIPv4Bytes`，严格4字节 |
| DNS AAAA、ICMPv6、IPv6 SOCKADDR裸地址 | `FromIPv6Bytes`，严格16字节 |
| 固定持久化/IPC | `TryWriteBinary`/`TryReadBinary`，17字节 |
| `System.Net.IPAddress` | `FromIPAddress`，先读`AddressFamily` |

带ScopeId的IPv6继续拒绝进入纯地址值。接口Scope保存在选择器、传输端点或Actual证据。

### 2.2 ICMPv6原生边界

单响应和多响应统一复用36字节 `ICMPV6_ECHO_REPLY` 布局：

```text
Address@6 / Status@28 / RoundTripTime@32
```

不再以17字节公共IP值、旧34字节步长或不同单双响应偏移解释Windows原生缓冲区。

### 2.3 请求级并发与不可变快照

- 七类协议端点均可请求级并发启动；
- 唯一发送线程只负责有序出队，不串行等待昂贵路由解析和I/O；
- 每个Request保存自己的接口目录、路由快照、Attempt与Branch；
- 同一解析器一秒内复用只读接口目录，但每个目标仍独立执行`GetBestRoute2`；
- 执行器接受仍处于有界历史集合内的本请求快照，不要求等于“最后一次全局快照”。

这消除了批量扫描中A请求读取B请求路由事实，以及逐项解析累积到外层超时的问题。

### 2.4 HTTP/DoH连接池资源边界

- HTTP和DoH共用独立的有界连接池实现；
- 池项以稳定访问计划和协议/TLS身份为键；
- 活跃租约不能被空闲清理；
- 路由/接口快照失效时淘汰旧项；
- 达到容量时优先回收安全空闲项，否则使用不进入共享池的瞬态项；
- 机会清扫与独立定时器共同回收空闲项，即使后续不再有流量也能闭合资源生命周期；
- Stop、失效替换与容量淘汰只把条目标记为Retired，最后一个活动租约释放后才真正释放；
- GUID、时间、超时、重试和Actual观测不进入池键。

### 2.5 UTF-8泛型值接口

`IpAddressValue`和`MacAddressValue`新增：

```text
IUtf8SpanFormattable
IUtf8SpanParsable<TSelf>
```

UTF-8入口与字符入口保持相同Null、格式、ScopeId拒绝和规范化语义；格式化直接写调用方缓冲区。

## 三、公共API迁移

唯一破坏性调用迁移是 `IpAddressValue.FromAddressBytes`。不得增加以下兼容代码：

- 重新用4/16长度猜地址族；
- 把17字节固定格式当作裸IPv6；
- 删除`%zone`但不保存接口身份；
- 直接复制CLR结构内存作为固定协议；
- 恢复V2/V3端点、可空Route或旧Flags。

新增UTF-8接口是源兼容能力。请求并发、快照隔离和连接池治理属于内部修正，不改变协议请求、
响应或业务键合同。

## 四、消费者迁移

| 消费者 | 迁移方法 | 当前结果 |
| --- | --- | --- |
| Runtime | 聚合beta.4 Debug/Release DLL、文档、包和校验清单 | 1.0.38-beta.1已安装并通过自检 |
| DDNS Snap | 按数据来源替换模糊IP字节入口，重跑真实IPv6接口场景 | 上游已有隔离DLL证据，安装版可供复验 |
| Aether | 从默认Program Files引用，不建立跨仓ProjectReference | 242/242；DNS/DoH精确匹配36/36 |

Aether消费面没有 `IpAddressValue.FromAddressBytes`，公共转换使用`FromIPAddress`，因此生产代码
无需兼容修改；已新增IPv4/IPv6、Exact目标、访问计划校验和ScopeId拒绝测试。

## 五、验证

| 门禁 | 结果 |
| --- | --- |
| Networks Debug测试 | 202/202 |
| Networks Release测试 | 202/202 |
| Aether Release测试（候选staging） | 242/242 |
| Aether DNS/DoH E2E | 15/15，mismatch=0，failure=0 |
| Runtime 1.0.38-beta.1 MSI安装与官方自检 | PASS，Web载荷为零 |
| Aether Release测试（Program Files安装版） | 242/242 |
| Aether DNS/DoH E2E（Program Files安装版） | 冷/热UDP与TCP各12项，精确匹配36/36，mismatch=0，failure=0 |
| 精确AliDNS IPv4 | PASS |
| 精确AliDNS IPv6 | PASS |
| Google IPv6目标 | 当前链路三次超时，不覆盖已有IPv6成功证据 |

Runtime根解决方案Debug/Release均以0警告、0错误通过；Publish staging和安装载荷自检通过。
候选目录已经生成MSI、便携ZIP、Networks nupkg/snupkg、说明、清单和SHA-256清单。
完整发布入口在最后写入阶段发现同名候选目录已由并发发布流程生成，按不可覆盖规则主动停止；
现有目录7项交付物齐全，因此没有删除、覆盖或复用该候选。

## 六、剩余正式版阻塞

- Selene安装新载荷后的精确接口IPv6复验；
- 两个真实下一跳环境的WFP并发隔离矩阵；
- 完整恢复动作与长期UDP工程流量；
- 干净环境restore/build/test/install及3.0.0正式冻结。

这些项目阻止正式3.0.0，但不阻止beta.4普通β候选生成。beta.4不得冒充正式发布候选，
不得上传公共NuGet源，也不得自动安装。

Packet、NAT、透明代理和系统外虚拟网卡目前冻结的是可替换数据面能力合同，不代表这些后端
已经全部实现。当前普通Socket后端实现Datagram基础能力；后续后端应通过现有数据面/适配器边界
接入，不得反向修改公共请求身份、访问计划或Branch证据合同。

## 七、迁移结论

```text
ARCHITECTURE_AUDIT_PASS
BREAKING_API_MAPPED
CONSUMER_SOURCE_MIGRATION_PASS
BETA4_RELEASE_PAYLOAD_READY
BETA4_GENERAL_BETA_INSTALLED_VALIDATED
FORMAL_RELEASE_BLOCKED
```

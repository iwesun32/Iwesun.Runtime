# Networks 3.0 HTTP、DoH与连接池

> **状态**: BETA_4_LIFECYCLE_CLOSED_M11_PARTIAL | **最后更新**: 2026-07-28  
> **源码参考**: `NetworkHttpContracts.cs`、`NetworkHttpConnectionPool.cs`、`NetworkHttpEndpoint.cs`、`NetworkDohEndpoint.cs`、`NetworkRouteAdapters.cs`、`HttpConnectNetworkRouteAdapter.cs`  
> **上游**: [精确网络访问控制最终设计](../01-design/PRECISION_NETWORK_ACCESS_CONTROL_FINAL_DESIGN.md)

## 一、当前已完成

- `NetworkHttpProtocolIdentity`把Scheme、逻辑Host、SNI、证书校验名、证书SHA-256固定值、端口和HTTP主版本从路径选择器中分离；
- `NetworkHttpConnectionPoolKey`组合稳定访问Key和协议/TLS身份，不接收任何跟踪GUID、时间、超时、重试或Actual证据；
- 源地址、接口、下一跳、PathProvider、适配器ID/能力版本、Resolver身份或Host/SNI变化都会产生不同池键；
- `NetworkHttpGetEndpoint<TKey>`支持固定传输IP的Automatic/Direct HTTP发送，并保留逻辑URL Host；
- 池内连接保存连接建立时的路径证据，复用给新Branch时只重设身份父链，不伪造新连接观测；
- 路由/接口快照或Resolved值变化时淘汰旧池；
- `NoReuseRequestPolicy`为请求级策略提供禁止复用入口。
- HTTP与DoH共用有界池管理器；池条目租约覆盖请求发送与响应读取阶段，失效替换、Stop或
  容量淘汰只标记Retired，最后一个活动租约释放后才真正释放`HttpClient`。
- 默认空闲保留2分钟、最大1024个稳定键、每64次租用进行机会清扫；独立定时器保证停止流量后
  仍能回收空闲条目。容量已满且全部条目活动时，本次请求使用不入池瞬态连接。
- HTTP与DoH构造函数尾部新增可选`idleConnectionRetention`、`maxConnectionPoolCount`和
  `connectionPoolSweepInterval`，既有位置参数调用保持兼容。
- `NetworkDohEndpoint<TKey>`支持JSON与WireMessage查询；固定传输IP不改变URL Host、TLS SNI和证书校验身份；
- 证书固定值进入稳定池键，不同信任边界绝不共池；固定IP HTTPS回环验收同时验证了Host、SNI、证书固定和Branch证据。

## 二、RouteAdapter合同

`INetworkRouteAdapter`使用字符串AdapterId、严格CapabilityVersion和显式ByteStream/Datagram能力。注册表按ID、版本和传输类型三项匹配。适配器连接结果分别返回ClientLeg和EgressLeg，禁止把客户端到代理的socket证据冒充代理出口证据。

`HttpConnectNetworkRouteAdapter`已经实现：代理ClientLeg由Networks socket实际观测，CONNECT目标EgressLeg只使用`AdapterReported`证明；不能证明的代理出口源地址、接口、网关和端口均保持`NotApplicable`，绝不复制ClientLeg证据。该适配器只声明ByteStream能力；当前不支持的Egress精确约束以`route-adapter-egress-constraint-unsupported`失败。旧整数ID适配器已经删除。

平台默认上下文对`RouteAdapter`和`SystemProxy`使用委托解析：只冻结外层稳定访问语义和明确给出的精确值，
不调用`GetBestRoute2`伪造一条“本机到最终目标”的直连路由。SystemSelected源、接口和下一跳保持未决，
直到连接阶段由ClientLeg/EgressLeg分别报告。M11真实环境已通过HTTP CONNECT到固定`8.8.8.8`并保留
`dns.google` Host/SNI，以及Windows SystemProxy服务解析访问。

## 三、当前明确拒绝

- SystemProxy仅接受当前可安全执行的SystemSelected物理维度；要求精确源、接口、下一跳等约束时返回`system-proxy-constraint-unsupported`；
- Direct ExactNextHop在M8前返回`exact-next-hop-backend-unavailable`；
- ExactCompartment无请求级后端时返回`route-scope-backend-unavailable`；
- 协议Host/SNI/证书名与URL身份不一致时返回`http-request-invalid`。

## 四、SystemProxy证据边界

SystemProxy连接通过`SocketsHttpHandler.ConnectCallback`观测实际ClientLeg目标、源地址、本地端口和可定位的物理接口；EgressLeg始终保持Unavailable/NotApplicable，不推断代理出口。每次请求解析到的代理URI或bypass状态进入稳定池键，系统代理映射变化后不会命中旧池。

M6库内实现已完成；M11真实代理验证修正了委托路径被错误送入直连快照器的问题。beta.4
连接池生命周期合同已纳入当前Debug/Release各202项全量门禁；剩余真实路由变化矩阵仍按M11执行。

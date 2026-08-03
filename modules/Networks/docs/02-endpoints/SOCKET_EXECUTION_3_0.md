# Networks 3.0 Socket 执行后端

> **状态**: CURRENT | **最后更新**: 2026-07-22  
> **源码参考**: `modules/Networks/src/Iwesun.Runtime.Networks/NetworkSocketExecution.cs`、`modules/Networks/src/Iwesun.Runtime.Networks/WindowsNetworkRouteSnapshotProvider.cs`  
> **上游**: [精确网络访问控制最终设计](../01-design/PRECISION_NETWORK_ACCESS_CONTROL_FINAL_DESIGN.md)  
> **下游**: M5 TCP、UDP、Ping、PTR和NBNS协议端点

## 一、定位

Socket后端执行已经完成规范化和求解的`RequestedAccessPlan`与`ResolvedAccessPlan`。它不是最终协议端点，也不保留旧版本兼容入口。协议端点为每个Attempt/Branch调用该后端，并把结果聚合到固定四级GUID身份链。

当前支持：

- `Automatic`：不施加源地址、接口或下一跳强制条件，允许系统选路；
- `Direct`：按非系统选择器分别执行源地址bind、接口socket option、本地端口和IP包政策；
- TCP连接与单次UDP请求—响应；
- IPv4和IPv6；
- Windows `GetBestRoute2`只读候选路由快照。

## 二、部分约束执行

Direct不是“全部字段都必须指定”的固定模板，各选择器正交执行：

| Requested条件 | Socket动作 | 证明 |
| --- | --- | --- |
| Source为Exact/集合/Derived | bind已解析源地址 | 连接后本地端点为`Observed` |
| Source为SystemSelected | 不预先bind源地址 | 连接后本地端点为`Observed` |
| Interface为Exact/集合/Derived | 设置IPv4/IPv6单播接口选项 | `ApiBinding`；UDP包信息可升级为`Observed` |
| Interface为SystemSelected | 不设置接口选项 | 路由快照仅为`Inferred`；UDP包信息可为`Observed` |
| LocalEndpoint为ExactPort/AllowedRange/Ephemeral | 在发送前bind解析端口 | 发送后本地端口为`Observed` |
| NextHop为SystemSelected或OnLink | 使用正常socket路径 | 路由快照为`Inferred` |

只指定网卡和只指定源地址均是独立受测场景。没有显式要求的维度不会被转换成Exact约束。

## 三、路由快照不是发送证明

`WindowsNetworkRouteSnapshotProvider`调用`GetBestRoute2`，输入精确目标以及可选源地址和接口Index，输出候选源、接口和下一跳。它同时生成不可变接口目录与路由快照版本。

- 查询只读，不修改全局路由表；
- `GetBestRoute2`结果只允许标记为`Inferred`；
- 发送前路由或接口快照版本变化时，旧`ResolvedAccessPlan`固定以`resolved-plan-stale`拒绝；
- 单个网卡不支持IPv4或IPv6属性查询时跳过该属性，不允许`NetworkInformationException`终止进程；
- 原生ABI布局在调用前检查，平台、入口点或布局不支持均返回结构化失败。

## 四、Actual逐维证据

`ActualAccessEvidence`归属于Branch，并包含PathProvider、目标、源地址、接口、下一跳、本地端口、路由域、IP包政策和RouteAdapter。每一维包含：

- 实际值；
- `ProofKind`；
- `AccessCompliance`；
- 可选差异原因。

TCP/UDP连接后的本地与远端端点为`Observed`。UDP优先使用包信息取得接收接口；平台不支持时回退到普通接收，并保持接口证明为`Inferred`，不得冒充观测结果。

## 五、明确拒绝

以下能力在M4不作隐式降级：

- 非OnLink的ExactNextHop需要M8 WFP后端，当前返回`exact-next-hop-backend-unavailable`；
- ExactCompartment尚无请求级执行后端，返回`route-scope-backend-unavailable`；
- Requested、SecurityBoundary与Resolved稳定Key不一致，返回`resolved-plan-mismatch`；
- 过期快照返回`resolved-plan-stale`；
- 不修改全局路由、不临时添加路由，也不退回Automatic。

## 六、当前验证

固定测试覆盖Automatic/Direct、TCP/UDP、IPv4/IPv6、只指定源、只指定接口、UDP包信息、实际Windows `GetBestRoute2`、快照失效、稳定语义不匹配、ExactNextHop/ExactCompartment拒绝及传输失败。

M5已新增`NetworkTcpConnectEndpoint<TKey>`和`NetworkUdpDatagramEndpoint<TKey>`：

- `TKey`只作为允许重复的用户查询数据；
- 请求必须由调用方在入队前提供非空`RequestId`及非空精确访问计划；
- 每个Attempt重新刷新并求解同一Requested计划；
- 响应本体、ResponseObserved事件和完成记录共享同一个ResponseId；
- UDP每个Branch独占socket及NAT映射。

TCP、UDP、Ping、PTR和NBNS现已统一使用正式无后缀端点；被替代的公共面已物理删除，旧源码引用必须编译失败。

接口选择器在比较稳定Key前必须先正规化。调用方只提供Index、LUID、GUID或Alias中的任一合法接口身份时，
Runtime先解析成目录中的完整接口身份，再分别生成Requested与Resolved稳定Key；不得用未正规化的调用方引用
直接比较已正规化的Resolved计划，否则合法的ExactInterface请求会被错误拒绝为`resolved-plan-mismatch`。

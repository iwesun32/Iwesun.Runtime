# Iwesun.Runtime.Networks 2.0.0发布说明

> **状态**: CURRENT | **发布日期**: 2026-07-20

## 发布定位

2.0.0是请求—响应公共接口的强制统一版本。库内只保留
`TrackedRequestReplyEndpointBase<TRequest,TResponse,TKey>`及对应`Tracked*V2`端点作为正式请求—响应规范，
不再提供被替代V1类型的别名、包装器或兼容程序集。

## 已移除公共类型

- `HttpGetEndpoint`、`HttpGetRequest`、`HttpGetResult`；
- `DohJsonEndpoint`、`DohResult`、`DnsQueryType`；
- `TcpConnectEndpoint`、`TcpConnectRequest`、`TcpConnectResult`；
- `PingIpv4AsyncEndpoint`、`PingIpv6AsyncEndpoint`；
- `PingIpv4BinaryRequest`、`PingIpv4BinaryResult`、`PingIpv6BinaryRequest`、`PingIpv6BinaryResult`；
- `PingIpv6SourceAsyncEndpoint`及旧Ping解释器。

对应功能分别由`TrackedHttpGetEndpointV2`、`TrackedDohEndpointV2`、`TrackedTcpConnectEndpointV2`、
`TrackedPingIpv4EndpointV2`和`TrackedPingIpv6EndpointV2`唯一承载。IPv4广播和IPv6组播在`Route = null`时同样
使用系统自动选路；显式源地址和物理接口直接由V2 Ping执行。

## 保留的特殊模型

PowerShell、Process、ARP和IPv6邻居快照没有重复V2实现，继续使用专用值类型FIFO端点。这些类型不是旧协议的
并行替代版本，不得据此恢复已删除的V1请求—响应端点。

## 消费方升级

- DDNS Snap的普通、广播和组播Ping全部使用V2；
- Aether兼容批量DoH路径使用`TrackedDohEndpointV2`，系统代理通过逐请求`NetworkRouteDirective`选择；
- DDNS Snap包引用升级到`Iwesun.Runtime.Networks` 2.0.0；
- Runtime发布载荷嵌入Networks 2.0.0，但Runtime自身版本边界保持独立。

## 兼容性

本版本包含有意的编译期破坏性变化。旧端点引用必须直接迁移，不提供静默兼容层。

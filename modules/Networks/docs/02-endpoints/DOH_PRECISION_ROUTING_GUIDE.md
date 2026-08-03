# DoH精确访问使用指南

> **状态**: CURRENT | **最后更新**: 2026-07-22
> **源码参考**: `NetworkDohEndpoint.cs`、`NetworkHttpContracts.cs`、`NetworkRouteAdapters.cs`

## 核心合同

`NetworkDohEndpoint<TKey>`始终接收非空`RequestedAccessPlan`。URL、Host、SNI、证书固定和Resolver身份属于协议安全边界；目标IP、源地址、接口、下一跳和路径提供者属于访问计划。两者分别表达，不能用IP替换HTTPS URL中的服务域名。

每个请求必须预先生成非空RequestId。端点为每次尝试和执行分支生成AttemptId、BranchId，每条响应生成ResponseId。`TKey`只用于调用方查询。

## 路径选择

- `Automatic`：明确允许系统选择源、接口和下一跳；
- `Direct`：使用正交选择器约束目标、源、接口、下一跳和本地端点；
- `RouteAdapter`：使用`INetworkRouteAdapter`及其显式能力版本，分别报告ClientLeg和EgressLeg；
- `SystemProxy`：明确允许系统代理，只证明实际观察到的客户端段。

Direct ExactNextHop在Windows使用请求级WFP策略，必须选择`NoReuseRequestPolicy`。共享池请求会在连接前返回`wfp-policy-connection-reuse-unsupported`。

## 最小结构

```csharp
var plan = new RequestedAccessPlan(
    NetworkPathProvider.Direct,
    NetworkDestinationSelection.Exact(IpAddressValue.Parse("223.5.5.5")),
    NetworkSourceSelection.SystemSelected(),
    NetworkInterfaceSelection.SystemSelected(),
    NetworkNextHopSelection.SystemSelected(),
    NetworkLocalEndpointSelection.Ephemeral(),
    NetworkRouteScopeSelection.Current(),
    NetworkIpPacketPolicy.SystemDefault(),
    NetworkRouteAdapterIdentity.NotApplicable());

NetworkHttpProtocolIdentity.TryCreate(
    new Uri("https://dns.alidns.com/resolve"),
    null, null, null, 2,
    NetworkHttpConnectionReusePolicy.Reusable,
    out var protocol,
    out _);

using var endpoint = new NetworkDohEndpoint<string>();
endpoint.TrySend(new NetworkDohRequest<string>(
    Guid.NewGuid(),
    "dns-query",
    "https://dns.alidns.com/resolve",
    "example.com",
    NetworkDnsQueryType.A,
    NetworkDohResponseFormat.Json,
    protocol,
    plan,
    "alidns"));
```

连接池键包含稳定访问语义和安全身份，不包含RequestId、AttemptId、超时、重试次数或时间戳。

# Iwesun.Runtime.Networks 1.1.0发布说明

> **状态**: READY_FOR_DISTRIBUTION（本地正式包） | **发布日期**: 2026-07-19

## 一、版本定位

1.1.0是公共网络库的第一个V2请求—响应跟踪版本。旧V1端点继续保留；新消费者可按功能逐项迁移，不需要一次性替换。

## 二、主要能力

- 原请求、业务关联标识和响应形成闭环；
- 发送FIFO、Pending等待表和完成FIFO分别限容；
- 超时或错误后重新进入发送FIFO；
- 包含首次发送在内最多总计3次网络尝试；
- 成功确认、最终失败、重试和迟到响应事件；
- 每条请求独立携带目标IP、源IP、物理网卡和路由适配器；
- DoH/HTTPS保持服务域名、证书、TLS SNI和HTTP Host，同时固定底层TCP目标；
- IPv4/IPv6 Ping可绑定指定物理网卡实际源地址，避免Clash Verge、VPN或TUN虚拟网卡改变默认路径；
- 响应和最终路由保留实际路径证据。

## 三、统一API约定

```csharp
NetworkRouteDirective? Route = null;
int? TimeoutMs = null;
```

`Route = null`表示使用系统普通路径。`TimeoutMs = null`读取端点可读写的`DefaultTimeoutMs`；显式正整数只覆盖该条请求。

```csharp
using var endpoint = new TrackedDohEndpointV2(
    maxAttemptCount: 3,
    defaultTimeoutMs: 5000);
```

`MaxAttemptCount`范围为1到3，包含首次发送。首次的`RetryCount=0`，后续两次分别为1和2。

## 四、安装与引用

```xml
<PackageReference Include="Iwesun.Runtime.Networks" Version="1.1.0" />
```

本地包默认生成到`artifacts/packages`。尚未配置公共NuGet源时，可把该目录登记为本地包源：

```powershell
dotnet nuget add source "D:\Git Space\Runtime\artifacts\packages" --name IwesunLocal
```

## 五、验证结果

- Networks Debug/Release测试：16/16通过；
- Release解决方案：0警告、0错误；
- Aether Debug/Release构建：0警告、0错误；
- Aether非RealNetwork业务回归Debug/Release：26/26通过；
- 固定目标IP的DoH真实请求通过；
- 指定物理接口的IPv4/IPv6 Ping真实请求通过，并返回实际源IP和接口索引。
- 独立消费者仅通过`PackageReference Iwesun.Runtime.Networks 1.1.0`恢复、Release编译和运行通过，程序集版本为`1.1.0.0`。

## 六、已知边界

- 当前包目标框架为`net10.0`；消费者必须使用兼容的.NET 10运行时/SDK。
- Windows原生源地址绑定Ping在Windows上提供完整能力；其他平台的系统普通Ping仍可使用。
- ICMP不通过HTTP CONNECT或系统HTTP代理；Ping精确路径通过源地址和物理网卡控制。
- SOCKS和其他自定义路径需要实现后续`INetworkRouteAdapter`。
- 公共NuGet推送和Git标签属于分发动作，不影响本地1.1.0包使用。

# Networks 3.0 Ping三轴终态β验证指南

> 状态：源码工程β验证；不代表恢复包发布资格。

## 验证目标

验证精确接口IPv6组播请求同时满足以下合同：

- Request、Attempt、Branch、Response保持完整GUID父链；
- Actual Destination始终是`ff02::1%InterfaceIndex`；
- 每个实际回包的单播来源只写入`ResponderAddress`；
- 原生API成功提交并完成目标、源地址、接口、ScopeId绑定时，有响应为
  `Succeeded + Satisfied`，零响应为`TimedOut + Satisfied`；
- API未建立完整绑定时不得把零响应统一标记为`Satisfied`。

## 执行方式

自动选择一个已启用、支持组播且存在IPv6地址的接口：

```powershell
dotnet run --project 'modules\Networks\validation\Iwesun.Runtime.Networks.PingBetaValidation\Iwesun.Runtime.Networks.PingBetaValidation.csproj' -c Release
```

指定接口索引：

```powershell
dotnet run --project 'modules\Networks\validation\Iwesun.Runtime.Networks.PingBetaValidation\Iwesun.Runtime.Networks.PingBetaValidation.csproj' -c Release -- --interface-index=19
```

指定接口与全局单播目标：

```powershell
dotnet run --project 'modules\Networks\validation\Iwesun.Runtime.Networks.PingBetaValidation\Iwesun.Runtime.Networks.PingBetaValidation.csproj' -c Release -- --interface-index=19 --target=2001:4860:4860::8888
```

程序只向标准输出写入一个JSON对象，不修改接口、路由、地址或系统服务。

## 结果判定

有响应时必须同时看到：

- `ProtocolOutcome = 1`；
- `AccessCompliance = 1`；
- `DestinationProof = 2`、`InterfaceProof = 2`；
- `Destination`仍为组播目标；
- `Responder`为非空实际单播地址。

零响应时必须同时看到：

- `State = 4`；
- `ProtocolOutcome = 3`；
- `AccessCompliance = 1`；
- `Failure.Kind = 2`且Reason为`ping-response-window-empty`。

## 当前本机基线

- 接口19：收到1个响应，`Succeeded + Satisfied`，目标和Responder正确分离；
- 接口19全局单播：`Succeeded + Satisfied`，接口证据为`AdapterReported`；
- 接口8：收到0个响应，`TimedOut + Satisfied`，未误报Transport；
- 全局IPv6错误附加ScopeId：发送前以`ipv6-scope-not-applicable`拒绝；
- DDNS Snap隔离DLL消费：真实DHCPv6源地址、接口19和公网目标测试通过；
- Debug/Release合同测试：126/126。

完整DDNS Service恢复链可能触发RS、DHCPv6 renew和网卡重启，必须在单独授权后运行；隔离DLL消费通过不自动解除
Networks的剩余M11发布阻断。

# WFP Windows 真实网络验收手册

## 1. 安全边界

`Iwesun.Runtime.Networks.WfpValidation`是独立、不可打包的管理员验收程序。默认 `capability` 与 `check` 只读；`run`和`crash`必须显式提供`--confirm-system-mutation`，否则拒绝执行。

验收程序只创建请求级动态WFP连接策略，不创建持久Provider/Filter，不修改全局路由表。`crash`会故意调用`Environment.FailFast`验证进程异常退出后的动态会话清理，只能在专用验收终端执行。

## 2. 前置条件

- Windows 11 22H2或当前系统实际导出`FwpmConnectionPolicyAdd0`；
- 管理员PowerShell；
- 已知源地址、接口索引、可达下一跳、目标地址和目标TCP/UDP端口；
- 若验证不同网关并发，两个下一跳必须真实可承担各自测试流量；
- 先保存当前路由和WFP状态快照，验收后再次比较。

只读能力检查：

```powershell
dotnet run --project .\modules\Networks\validation\Iwesun.Runtime.Networks.WfpValidation\Iwesun.Runtime.Networks.WfpValidation.csproj -c Release -- capability
```

能力查询必须真实使用`RPC_C_AUTHN_WINNT`打开并关闭WFP引擎；只检查DLL导出和管理员身份会产生假阳性。
若引擎打开失败，必须返回原生错误和`wfp-engine-open-capability`分类，不得声明支持。

## 3. 单连接执行与清理

以下占位值必须替换为验收环境真实值：

```powershell
dotnet run --project .\modules\Networks\validation\Iwesun.Runtime.Networks.WfpValidation\Iwesun.Runtime.Networks.WfpValidation.csproj -c Release -- run `
  --confirm-system-mutation `
  --source 192.0.2.10 `
  --interface-index 12 `
  --next-hop 192.0.2.1 `
  --target 198.51.100.20 `
  --target-port 443 `
  --protocol tcp `
  --policy-id 11111111-1111-1111-1111-111111111111
```

成功必须依次输出连接的`PolicyEnforced`记录和`remains=false`清理记录。随后可单独复核：

```powershell
dotnet run --project .\modules\Networks\validation\Iwesun.Runtime.Networks.WfpValidation\Iwesun.Runtime.Networks.WfpValidation.csproj -c Release -- check `
  --policy-id 11111111-1111-1111-1111-111111111111
```

## 4. 同目标不同网关并发

在两个管理员终端同时执行`run`，使用相同目标地址/端口和应用程序，但使用不同下一跳、不同固定本地端口、不同PolicyId，并设置`--hold-ms 15000`。两条连接必须同时成功，各自输出正确下一跳，期间不得影响同进程或系统中的第三条普通连接。两条进程结束后分别执行`check`，都必须返回`remains=false`。

## 5. 崩溃清理

```powershell
dotnet run --project .\modules\Networks\validation\Iwesun.Runtime.Networks.WfpValidation\Iwesun.Runtime.Networks.WfpValidation.csproj -c Release -- crash `
  --confirm-system-mutation `
  --source 192.0.2.10 `
  --interface-index 12 `
  --next-hop 192.0.2.1 `
  --target 198.51.100.20 `
  --target-port 443 `
  --policy-id 22222222-2222-2222-2222-222222222222
```

程序输出`acquired=true`后故意异常终止。进程完全退出后执行：

```powershell
dotnet run --project .\modules\Networks\validation\Iwesun.Runtime.Networks.WfpValidation\Iwesun.Runtime.Networks.WfpValidation.csproj -c Release -- check `
  --policy-id 22222222-2222-2222-2222-222222222222
```

只有查询成功且`exists=false`时，崩溃清理项通过。

## 6. 验收记录

必须保存：系统版本、进程位数、管理员身份、接口索引/LUID、源地址、两个下一跳、目标、PolicyId、程序JSON输出、验收前后路由快照和WFP残留查询。任何策略串线、普通连接受影响、关闭后仍存在或无法明确查询时，M8立即停线，不得进入3.0发布候选。

## 7. 恢复原语真实验收

同一不可打包验收程序提供恢复能力和动作入口：

```powershell
dotnet run --project .\modules\Networks\validation\Iwesun.Runtime.Networks.WfpValidation\Iwesun.Runtime.Networks.WfpValidation.csproj -c Release -- `
  recovery-capability --interface-index 19 --interface-alias '以太网'

dotnet run --project .\modules\Networks\validation\Iwesun.Runtime.Networks.WfpValidation\Iwesun.Runtime.Networks.WfpValidation.csproj -c Release -- `
  recovery --confirm-system-mutation --action rs `
  --interface-index 19 --interface-alias '以太网'
```

`--action`还支持`release6`、`wait`、`renew6`、`restart`和`snapshot`。DHCPv6必须按
`release6 → wait → renew6`执行，即使release失败也必须尝试renew；网卡重启后不得以“地址已枚举”作为
可用证据，应等待所需地址进入Preferred并完成真实bind/连接复检。恢复动作结果分别报告平台动作、
地址变化和连通性，不把退出码0等同于业务恢复。

## 8. 2026-07-22当前机器实测

- 接口19、源`192.168.32.16`、下一跳`192.168.32.1`；提升后能力查询通过。
- TCP `223.5.5.5:443`和UDP `223.5.5.5:53`均得到`PolicyEnforced`，关闭后`remains=false`。
- 两条同目标、同下一跳、不同本地端口和PolicyId的TCP策略并发成功，独立清理且二次查询无残留。
- 故意`FailFast`后独立查询`exists=false`，动态会话崩溃清理通过。
- RS、DHCPv6 release/wait/renew和网卡重启均执行成功；release移除DHCPv6地址，renew恢复该地址。
- 网卡重启后地址首次出现时立即bind曾返回10049；等待IPv4和全局IPv6进入Preferred后，精确WFP TCP
  复检成功且无残留。
- 当前机器只有一个真实默认网关，尚不能完成“同目标、不同网关”并发门禁；不得以虚假下一跳替代。

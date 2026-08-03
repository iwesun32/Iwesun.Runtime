# Networks 3.0 UDP数据面β测试指南

> **状态**：ENGINEERING_BETA_VALIDATION  
> **适用范围**：内部流水号、System Socket长期UDP池、端口租约、严格远端匹配和可替换数据面合同  
> **不代表**：Networks 3.0整体恢复发布候选资格

## 一、这次升级新增了什么

`NetworkUdpDatagramEndpoint<TKey>`不再为每个请求临时创建并销毁UDP Socket。默认后端现在维护长期Socket槽，
在发送前绑定本地地址和端口，发送后发布实际分派证据，并由常驻接收循环完成响应分派。

新增的三组身份不能混用：

```text
公共身份：RequestId → AttemptId → BranchId → ResponseId
内部索引：NetworkFlowSerial（进程内uint流水号）
线上匹配：SocketGeneration + LocalEndPoint + RemoteEndPoint + ProtocolToken?
```

调用方仍以GUID链完成正式查询和归因。`TKey`仍可重复，只是用户自己的查询数据。流水号不会写入UDP报文，
不能替代端口、远端地址或DNS Transaction ID。

## 二、性能与资源模型

- 流表使用流水号主键索引，按Flow查询接近`O(1)`；
- 隔离记录使用到期时间索引，插入和回收为`O(log N)`，发号不扫描全部活动Flow；发现到期积压时直接清空
  全部已到期记录，但不会强制清除尚未到期的安全隔离；
- 无协议Token的同远端并发请求使用不同Socket槽和本地端口；
- 每个稳定访问计划与远端端口的池默认最多64个活动或隔离槽；容量达到上限时拒绝新Flow，绝不清除未到期隔离；
- 顺序请求在迟到隔离结束后可以复用同一SocketGeneration和本地端口；
- 请求级WFP策略槽不复用，防止策略释放后连接语义残留；
- Socket接收循环永久失败后销毁槽，不把坏Socket返回池中。
- 完全空闲的池按保留时间定期回收；达到总池上限时优先回收不含活动或隔离槽的可用池，
  只有所有池均不可安全回收时才拒绝新Flow。

Networks实时数据面不引用`Iwesun.Runtime.Data`。历史统计如需持久化，应由消费方在Flow终态后异步批量写入，
不能让RecordStore参与实时包分派。

## 三、消费方可观察的新字段

订阅`NetworkUdpDatagramEndpoint<TKey>.DatagramDispatched`可以取得：

- `Identity`：当前Request/Attempt/Branch；
- `FlowSerial`：内部流水号；
- `SocketGeneration`：本次Socket实例代数；
- `ActualLocalAddress`与`ActualLocalPort`：系统实际使用的源端点；
- `ExpectedRemoteAddress`与`ExpectedRemotePort`：当前Branch冻结的远端；
- `ActualInterface`：解析并执行的接口身份；
- `DispatchedAtUnixMs`：系统接受发送后的时间。

最终`NetworkSocketExecutionResult`同时保存`FlowSerial`、`SocketGeneration`和`DispatchEvidence`。这些字段用于诊断和
高性能内部关联；跨组件业务查询仍使用GUID链。

`DataPlaneCounters`新增容量测量字段：

- `PoolCount`与`SocketSlotCount`：当前池和Socket槽总量；
- `ActiveSlotCount`与`QuarantinedSlotCount`：当前正在等待响应及尚未允许复用的槽；
- `PeakSlotsPerPool`：本进程生命周期内单池达到的最高槽位数；
- `CapacityRejections`：因池或Flow容量不足而在发送前拒绝的次数。

## 四、β测试准备

`3.0.0-beta.1`和`3.0.0-beta.2`是旧回归基线，不能验证本轮IP/MAC与池回收新增代码。测试者应使用
`3.0.0-beta.4`普通β或同一源码构建DLL；
允许由Runtime MSI安装和内部文件源分发，但不得上传公共NuGet源或描述为正式发布候选。

基础构建：

```powershell
dotnet build modules\Networks\Iwesun.Runtime.Networks.slnx -c Debug
dotnet build modules\Networks\Iwesun.Runtime.Networks.slnx -c Release
```

自动测试：

```powershell
dotnet test modules\Networks\tests\Iwesun.Runtime.Networks.Tests\Iwesun.Runtime.Networks.Tests.csproj -c Debug
dotnet test modules\Networks\tests\Iwesun.Runtime.Networks.Tests\Iwesun.Runtime.Networks.Tests.csproj -c Release
```

可重复的回环容量验证工具：

```powershell
dotnet run --project modules\Networks\validation\Iwesun.Runtime.Networks.UdpBetaValidation\Iwesun.Runtime.Networks.UdpBetaValidation.csproj `
  -c Release -- `
  --duration=30 --concurrency=16 --delay=5 --quarantine=1000 --max-slots=64 --timeout=3000
```

增加`--ipv6=true`切换到IPv6回环。工具只输出一个结构化JSON对象，不写测试日志或修改系统配置；
`CancelledAtEnd`表示测试窗口关闭时主动取消的在途请求，不计作协议超时。

普通UDP专项不需要管理员权限。ExactNextHop/WFP、系统恢复动作、虚拟网卡和路由变更不属于本指南，未经单独授权不得执行。

## 五、用户必须完成的β场景

### 1. IPv4和IPv6单播闭环

分别使用IPv4与IPv6可控UDP应答服务，确认：

- 请求和响应四级GUID父链完整；
- `DatagramDispatched.IsValid`为真；
- 实际本地地址、端口和接口符合访问计划；
- 响应Payload来自预期服务端；
- `ProtocolOutcome`和`AccessCompliance`分别反映协议与访问事实。

### 2. 顺序复用

对同一远端顺序发送至少1000次请求，等待每次完成并跨过配置的隔离期。确认：

- 多次请求可以复用相同`SocketGeneration`和本地端口；
- 每个Branch取得不同`FlowSerial`；
- 没有响应串入后一请求；
- 句柄和端口数量不会随请求次数线性增长。

### 3. 无Token并发

对同一远端同时保持多条未完成请求。确认每条在途请求使用不同本地端口或在容量不足时被明确拒绝，
不得让两个无Token Flow在同一SocketGeneration、同一四元组上并发等待。

### 4. 错误来源与迟到响应

让其他UDP端点向已知本地端口发送伪响应，再由正确服务端回复；随后制造超时并在隔离窗口内发送迟到包。确认：

- 错误来源不完成也不失败当前请求；
- 正确来源仍能完成请求；
- 迟到包不会完成下一请求；
- `UnexpectedRemoteDatagrams`、`UnmatchedDatagrams`或`LateDatagrams`按系统可观察路径增长。

连接式UDP可能由内核先过滤错误来源，此时库级`UnexpectedRemoteDatagrams`可以不增长，但请求结果仍只能来自预期远端。

### 5. 超时、取消与Stop

分别执行无响应超时、调用方取消和端点Stop。确认Flow进入隔离并最终释放，Stop后没有活动接收循环、端口租约或继续发布的响应。

### 6. 多目标池回收

使用超过`maxPoolCount`的不同稳定访问键或远端端口顺序完成请求，并将并发保持在槽上限以内。确认：

- 已完成且越过隔离期的空闲池可以被回收；
- 新目标不会仅因为历史空闲池仍在字典中而被拒绝；
- 活动池和未到期隔离池从不被回收；
- `PoolCount`保持有界，Socket句柄不会随历史目标数永久线性增长。

### 6. 容量与压力

使用受控的较小`maxSocketsPerPool`和`maxPoolCount`制造容量上限，确认返回明确容量/传输失败，不能静默共享不安全槽。
再以实际目标容量运行至少30分钟，记录吞吐、P50/P95/P99、活动Socket数、端口数、GC分配和三类丢弃计数。

默认64只是工程β保护值，正式值由全流量实测决定。单池所需槽位可先按下式估算：

```text
所需槽位 ≈ 峰值同时在途请求数 + 峰值每秒完成数 × 迟到隔离秒数
正式配置 = 上式实测P99/峰值再加安全余量
```

测试期间必须连续采集`PeakSlotsPerPool`和`CapacityRejections`。若容量拒绝非零或峰值长期接近64，扩大后重测；
若峰值显著低于64，仍需覆盖突发流量后才能下调。无论实测结果如何，都不得通过清除未到期隔离来提高吞吐。

### 6.1 本机短基线（2026-07-22）

以下结果只验证工具、容量公式和安全拒绝，不替代用户环境持续β：

| 地址族 | 并发 | 服务延迟 | 隔离 | 上限 | 成功/秒 | 峰值槽 | 容量拒绝 | 结论 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| IPv4 | 4 | 5ms | 50ms | 64 | 64.3 | 9 | 0 | 低并发通过 |
| IPv6 | 4 | 5ms | 50ms | 64 | 63.4 | 8 | 0 | IPv6低并发通过 |
| IPv4 | 16 | 5ms | 1000ms | 64 | 62.3 | 64 | 1205 | 安全拒绝生效，64不足以吸收1秒隔离 |
| IPv4 | 16 | 5ms | 1000ms | 128 | 63.4 | 81 | 0 | 对照验证公式成立 |
| IPv4 | 32 | 0ms | 100ms | 64 | 607.8 | 64 | 10146 | 高速无Token流达到保护上限 |
| IPv4 | 32 | 0ms | 100ms | 128 | 1189.3 | 128 | 8295 | 增大容量提高吞吐后仍会扩大隔离需求 |

因此64保留为通用无Token UDP的工程β安全保护值，而不是“保证全流量零拒绝”的承诺。DNS等高速协议必须利用协议Token
共享Socket和端口，不能通过无限扩大通用无Token池解决。

### 7. 数据面替换

注入测试用`INetworkDatagramDataPlane`，确认同一个`NetworkUdpDatagramEndpoint<TKey>`无需访问Socket即可完成请求。
此项用于证明未来VirtualAdapter或ExternalForwarder不要求修改业务端点API。

## 六、结果提交模板

```text
测试者/主机：
Windows版本：
.NET SDK版本：
Networks提交或构建标识：
Debug/Release构建：
Debug/Release测试数量：
IPv4/IPv6目标：
Automatic/Direct访问计划：
并发数/持续时间：
Socket与端口峰值：
PoolCount/SocketSlotCount：
Active/Quarantined/PeakSlotsPerPool：
CapacityRejections：
Unexpected/Unmatched/Late计数：
失败RequestId：
失败FlowSerial/SocketGeneration：
ActualLocal/ExpectedRemote：
ProtocolOutcome/AccessCompliance/ReasonCode：
是否可稳定复现：
```

报告中优先提供结构化文本、RequestId和固定字段，不上传敏感载荷或完整网络抓包。

## 七、已知限制与通过标准

DNS代理双ID映射、NAT、HTTP代理、Socket代理、Packet后端和虚拟网卡目前只有能力合同，不属于已实现功能。
IPv6组播Ping三轴终态仍是Networks整体发布阻断项，与本轮UDP基础数据面通过与否分别记录。

本专项β通过要求：Debug/Release自动测试全通过；上述七类场景无串流、无不安全复用、无资源线性泄漏；所有失败都能由
GUID链、FlowSerial、SocketGeneration、实际端点及三轴状态闭环解释。专项通过后仍不能自动解除Networks整体发布阻断。

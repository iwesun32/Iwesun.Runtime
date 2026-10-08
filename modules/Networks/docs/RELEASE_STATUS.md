# Iwesun.Runtime.Networks发布状态

## 2026-10-09 当前公开候选：3.0.0-beta.6

本次随 Runtime `1.0.47-beta.1` 完整公开交付，含 HTTP 请求级取消、接口身份和当前验证工具修正。
总体仍为 β，真实 WFP/下一跳和长期压力不由合同测试替代。当前验收以根版本发布清单为准。
以下内容为各历史版本的过程记录；其中“当前”“仅独立发布”等措辞只适用于对应日期。

> **当前版本**: 3.0.0-beta.5
>
> **状态**: GENERAL_BETA_READY_FORMAL_BLOCKED
>
> **最后更新**: 2026-09-22

## 2026-09-22 beta.5 网络用户反馈同步发布

- 已确认安装目录、Runtime 便携包和本地NuGet源中原有的`3.0.0-beta.4`均来自较早源码快照；
  它们不包含本轮TCP长期连接及数据面扩展，不能再作为“当前Networks”向用户交付。
- `NetworkSocketExecutor.OpenTcpAsync`返回`NetworkTcpConnection`。精确下一跳策略由连接对象
  持有，贯穿整个读写生命周期；仅在连接释放后清理。连接前取消、打开失败和清理异常各自保留，
  不伪造成功或提前释放策略。
- `INetworkPacketDataPlane`新增捕获和注入合同。记录保留内部流水号和四级GUID执行身份；这是一层
  面向DNS代理、NAT、Socket/HTTP代理和三层转发的公共后端边界，不等同于已提供虚拟网卡或Packet
  后端实现。
- Debug与Release合同测试均通过208项。此包是独立Networks普通β交付，不重打当前混有未完成
  Web/WebView2改动的全量Runtime MSI或便携包。
- 迁移、交付物和仍需真实网络验证的边界见
  [beta.5升级迁移报告](03-reference/NETWORKS_3_0_BETA5_UPGRADE_MIGRATION_REPORT.md)。

## 2026-07-28 beta.4完整架构审计与升级

- 删除按4/16字节长度猜测地址族的`IpAddressValue.FromAddressBytes`，改为
  `FromIPv4Bytes`与`FromIPv6Bytes`；公共数值构造分别明确IPv4和IPv6。
- Networks源码中的系统地址、ICMPv6回复和`SOCKADDR_INET`转换已迁移到显式地址族入口。
- 发现并修正ICMPv6单响应仍读取旧`Status=26`、`RTT=30`偏移的遗漏；单响应和多响应
  现在共同使用36字节原生回复布局和同一个解析器。
- 17字节`IpAddressValue`只作为公共纯地址值与固定序列化合同，不直接覆盖4字节IPv4、
  16字节IPv6或Windows原生回复缓冲区。
- 已提供独立的[IpAddressValue迁移手册](03-reference/NETWORKS_3_0_IP_ADDRESS_VALUE_MIGRATION_GUIDE.md)，
  消费项目必须按数据来源迁移编译错误，不得恢复长度推断兼容层。
- HTTP/DoH连接池已统一为有界租约池；协议端点使用请求级并发启动和请求私有路由快照；
  IP/MAC值补齐.NET 10 UTF-8 Span解析与格式化合同。
- 完整审计、破坏性变化和消费者迁移见
  [beta.4升级迁移报告](03-reference/NETWORKS_3_0_BETA4_UPGRADE_MIGRATION_REPORT.md)。
- Networks Debug/Release各202项通过；物理以太网接口19上的精确IPv6单播
  `2400:3200::1`及`ff02::1%19`组播请求均得到`Succeeded + Satisfied`，
  DDNS Snap使用本轮Debug DLL完成全解决方案编译，0警告、0错误。

## 当前结论

2026-07-24 已在源码中加入17字节纯IP数值、7字节纯MAC数值、特殊端点空GUID同步拒绝、
UDP空闲池安全回收及配套测试和文档；所有公共网络合同已从被删除的旧IP类型统一到
`IpAddressValue`。IP不再保存ScopeId，MAC不再缓存OUI、NIC标识或分类，带`%zone`的IPv6文本明确
拒绝，接口作用域继续归访问计划和路径证据；ARP和IPv6邻居公共记录也已从字符串强制迁移为
这两个值类型。该能力首先进入`3.0.0-beta.3`普通β；当前源码进一步冻结为
`3.0.0-beta.4`候选，并随修正后的Runtime `1.0.38-beta.1`统一生成Debug/Release DLL、本地包、
文档与SHA-256校验清单，并已安装到本机Program Files通过官方自检。

Iwesun.Runtime.Networks 3.0已完成库内唯一公共请求—响应规范的源码收口：协议端点统一使用
`Network*Endpoint<TKey>`、非空`RequestedAccessPlan`及
`RequestId → AttemptId → BranchId → ResponseId`四级GUID身份链；被替代的版本化端点、可空Route和旧路由兼容类型
已从源码与项目中物理移除。DDNS Snap真实IPv6组播联调曾确认精确接口Ping终态错误；当前源码已完成三轴终态、
精确接口API绑定证据、组播目标与响应方分离及零响应闭环。2026-07-28 Selene真实DDNS断点发现
`ICMPV6_ECHO_REPLY`外层对齐仍按错误的34字节步长解析；源码现已改为Windows ABI规定的36字节、
`Status@28`及`RoundTripTime@32`。Networks Debug/Release各202项通过，Apollo DDNS Debug
真实`ff02::1`回归已在首个NDP快照得到11条Reachable邻居并重新激活Hades；Selene安装新载荷后的
同机回归仍为发布前门禁。DDNS已通过隔离DLL根完成真实DHCPv6源地址、接口19和公网目标探测；完整Service恢复链因可能执行
RS、DHCPv6 renew及网卡重启而未运行。当前源码作为`3.0.0-beta.5`普通β候选准备，
允许生成Runtime MSI、便携载荷和内部文件源候选；完成统一候选门禁前不安装，
剩余M11门禁仍阻止正式版本冻结，且不允许上传公共NuGet源。

Runtime、DDNS Snap和Aether的M10强制迁移现已完成，活动消费面不再引用被替代的版本化名称或可空Route合同。
当前新增的首要阻塞阶段是协议、访问与生命周期三轴终态更正；修复完成后仍须继续M11管理员WFP/真实网络验收和
M12正式版本冻结。此前构建、迁移和局部真实网络通过记录继续作为历史证据，但不能覆盖当前发布阻断。

## 当前质量门

| 项目 | 要求 | 当前状态 |
| --- | --- | --- |
| Networks Debug/Release构建 | 0警告、0错误 | PASS |
| Networks Debug/Release测试 | 全部通过 | 当前源码Debug/Release 208/208；beta.4基线202/202 |
| 版本后缀、可空Route及旧处理器公共面 | 不得进入程序集 | PASS（反射守卫同时禁止V2/V3导出名） |
| beta.5普通β包 | 内容、版本、符号包及哈希可复核 | PASS（NuGet/符号包、Debug/Release DLL及SHA-256清单已生成） |
| Runtime消费者迁移 | 全部改用3.0正式合同 | PASS（Debug/Release构建及暂存载荷验证通过） |
| DDNS Snap消费者迁移 | 全部改用3.0正式合同 | PASS（x64 Debug/Release构建；Networks范围测试通过） |
| Aether消费者迁移 | 全部改用3.0正式合同 | INSTALLED BETA PASS（Program Files Debug/Release构建；测试242/242；DNS/DoH精确匹配36/36） |
| 三轴终态合同 | TerminalState、ProtocolOutcome、AccessCompliance独立传播 | LOCAL PASS（基类、七类协议端点与决定性Branch聚合已更正） |
| 精确接口IPv6 Ping | 组播及全局单播按真实协议和绑定证据闭环 | LOCAL DDNS PASS / SELENE RETEST REQUIRED（Apollo首个NDP快照11条Reachable） |
| 内部流水号与UDP长期数据面 | 流水号、端口租约、严格分派、迟到隔离 | PASS（Debug/Release合同测试通过） |
| UDP工程β回环基线 | IPv4/IPv6、容量拒绝、隔离成本与池指标 | LOCAL PASS / USER FULL-TRAFFIC PENDING（64槽保护值待用户持续实测） |
| UDP历史目标池回收 | 只回收完全可用池，不回收Active/Quarantine | CONTRACT PASS / USER LONG-RUN PENDING |
| 管理员真实WFP验收 | 加入、并发隔离、正常/异常清理通过 | PARTIAL PASS（TCP/UDP、同网关并发、正常/崩溃清理通过；双网关待测） |
| Windows真实网络与恢复验收 | 固定矩阵及授权恢复动作通过 | PARTIAL PASS（RS、DHCPv6、重启、RouteAdapter、SystemProxy通过；完整矩阵待测） |

## 普通β载荷与历史回归输入

UDP长期池、长期TCP和内部流水号基础批次开放普通β验证，测试者必须使用`3.0.0-beta.5`或同一源码构建结果，并按
[UDP数据面β测试指南](03-reference/NETWORKS_3_0_UDP_DATA_PLANE_BETA_TEST_GUIDE.md)提交结构化结果。

- `Iwesun.Runtime.Networks.dll` Debug/Release普通β程序集；
- `Iwesun.Runtime.Networks.3.0.0-beta.5.nupkg`和`Iwesun.Runtime.Networks.3.0.0-beta.5.snupkg`；
- 根README、CHANGELOG、完整3.0接口文档和可运行示例；
- 不参与NuGet打包的`Iwesun.Runtime.Networks.WfpValidation`管理员验收程序。

`3.0.0-rc.1`、`rc.2`、`rc.3`、`beta.1`、`beta.2`和`beta.3`均只作为历史基线或回归输入。
`beta.5`包的大小与SHA-256登记在包目录外部校验清单中；包内状态文件不嵌入包自身哈希，避免自引用。

## 发布阻塞项

1. DDNS Snap隔离DLL消费已通过；若要运行完整Service地址守护链，必须单独授权其可能触发的RS、DHCPv6 renew和网卡重启；
2. 保留本机合同测试和`ff02::1%InterfaceIndex`真实网络证据；当前`%19`为`Succeeded + Satisfied`，`%8`为
   `TimedOut + Satisfied`，后者仅因目标、源地址、接口及ScopeId的API绑定证据全部成立；
3. 在具备两个真实下一跳的管理员Windows环境完成双网关并发，并完成M11剩余真实网络矩阵；
4. 处理或正式豁免DDNS Snap既有安装包PowerShell文本断言，完成消费者全仓无排除测试记录；
5. 完成M12干净环境restore/build/test/install、正式`3.0.0`版本冻结和最终包重建。

当前允许生成`beta.5`普通β候选；统一候选门禁完成后才允许内部分发和安装。在全部正式门禁完成前，
禁止创建正式标签、推送公共包，或把普通β描述为正式发布候选。

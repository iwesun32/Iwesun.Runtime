# Iwesun Runtime 1.0.37-beta.1 普通 β 发布说明

> 状态：`BETA_CANDIDATE_PREPARATION_FORMAL_BLOCKED`  
> Runtime产品版本：`1.0.37`  
> Runtime程序集/文件版本：`1.0.37.0`  
> Runtime产品信息版本：`1.0.37-beta.1`  
> Networks产品信息版本：`3.0.0-beta.4`  
> Networks程序集/文件版本：`3.0.0.0`

本版用于先向DDNS Snap、Aether、DNS代理、NAT和路由器原型提供统一测试DLL。它是普通β，
不是正式版；不上传公共NuGet源，也不覆盖`1.0.36-beta.1`历史候选。

> **撤回说明（2026-07-28）**：首次生成的1.0.37候选因WebView2传递携带仍在调试的
> `Iwesun.Runtime.Web.dll`而撤回，只可作为本地失效构建证据，不得分发或安装。

## 1. Networks升级

- `IpAddressValue`裸字节入口显式分为`FromIPv4Bytes`和`FromIPv6Bytes`；
  被删除的`FromAddressBytes`通过编译错误强制消费者按数据来源迁移。
- ICMPv6单响应和多响应统一为36字节Windows原生布局、`Status@28`和
  `RoundTripTime@32`。
- `IpAddressValue`与`MacAddressValue`补齐.NET 10 UTF-8 Span泛型解析和格式化接口，
  17/7字节布局与现有字符、JSON合同保持不变。
- 协议端点请求可并发启动，Windows Automatic/Direct解析使用请求私有的不可变接口和路由快照。
- HTTP/DoH使用共享有界租约池；空闲项有定时回收，失效替换不会提前释放仍被并发请求使用的连接。
- UDP继续使用长期Socket池、端口租约、严格实际远端匹配、迟到隔离和64槽保护基线。

完整变化、架构分层和迁移表见
[Networks beta.4升级迁移报告](../modules/Networks/docs/03-reference/NETWORKS_3_0_BETA4_UPGRADE_MIGRATION_REPORT.md)。

## 2. 标准DLL布局

```text
lib\Iwesun.Runtime.Diagnostics\Debug\Iwesun.Runtime.Diagnostics.dll
lib\Iwesun.Runtime.Diagnostics\Release\Iwesun.Runtime.Diagnostics.dll
lib\Iwesun.Runtime.Data\Debug\Iwesun.Runtime.Data.dll
lib\Iwesun.Runtime.Data\Release\Iwesun.Runtime.Data.dll
lib\Iwesun.Runtime.Networks\Debug\Iwesun.Runtime.Networks.dll
lib\Iwesun.Runtime.Networks\Release\Iwesun.Runtime.Networks.dll
lib\Iwesun.Runtime.WebView2\Debug\Iwesun.Runtime.WebView2.dll
lib\Iwesun.Runtime.WebView2\Release\Iwesun.Runtime.WebView2.dll
```

各库顶层同名DLL是Release兼容入口。消费项目必须引用安装目录或便携包同配置DLL，
不得使用跨仓库`ProjectReference`，也不得混用Debug和Release。

## 3. 强制迁移

1. 删除消费项目旧beta.3 DLL和本地缓存。
2. 对所有`FromAddressBytes`编译错误按IPv4、IPv6或17字节固定格式来源迁移。
3. 保持调用源生成非空RequestId；内部`NetworkFlowSerial`不替代公共GUID身份链。
4. 不恢复V2/V3端点、可空Route、旧Flags或`BinaryIpAddress`兼容包装。
5. 重新编译全部消费者，不允许只替换DLL而跳过源码编译验证。

## 4. 普通β测试重点

1. DNS/UDP并发：端口租约、实际远端、迟到包和容量拒绝。
2. HTTP/DoH并发：稳定池键、活动租约、路由变化替换和无后续流量空闲回收。
3. IPv6 Ping：精确接口单播、组播多响应和零响应三轴终态。
4. 地址值：UTF-8泛型入口、IPv4嵌入IPv6文本、ScopeId分离和持久化升级。
5. DDNS Snap、Aether分别从本发布目录完成Debug/Release构建和核心端到端测试。

## 5. 当前验证

- Networks Debug：202/202通过。
- Networks Release：202/202通过。
- 17字节IP、7字节MAC及ICMPv6原生布局合同通过。
- HTTP空闲池无后续流量回收合同通过。
- Aether使用最终staging发布DLL复验：242/242，DNS/DoH E2E 15/15。

Runtime根解决方案、Publish staging、MSI和安装载荷结果以本次统一打包入口最终输出为准。

## 6. 统一交付目录

```text
artifacts\packages\Iwesun.Runtime.1.0.37-beta.1\
```

应包含：

- `Iwesun.Runtime.1.0.37-beta.1.msi`
- `Iwesun.Runtime.1.0.37-beta.1.zip`
- `Iwesun.Runtime.Networks.3.0.0-beta.4.nupkg`
- `Iwesun.Runtime.Networks.3.0.0-beta.4.snupkg`
- `IWESUN_RUNTIME_1.0.37_BETA_RELEASE_GUIDE.md`
- `RELEASE_MANIFEST.md`
- `SHA256SUMS.txt`

## 7. 正式版阻塞项

- WFP双真实网关并发隔离；
- Selene安装新载荷后的精确IPv6同机复验；
- 用户全流量复核UDP 64槽保护值；
- 经单独授权执行完整RS、DHCPv6和网卡恢复链；
- 干净环境restore/build/test/install/uninstall与最终`3.0.0`冻结。

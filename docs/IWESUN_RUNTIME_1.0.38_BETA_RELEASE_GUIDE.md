# Iwesun Runtime 1.0.38-beta.1 普通 β 发布说明

> 状态：`GENERAL_BETA_READY_FORMAL_BLOCKED`  
> Runtime产品版本：`1.0.38`  
> Runtime程序集/文件版本：`1.0.38.0`  
> Runtime产品信息版本：`1.0.38-beta.1`  
> Networks产品信息版本：`3.0.0-beta.4`  
> Networks程序集/文件版本：`3.0.0.0`

本版用于更新DDNS Snap、Aether及其他现有消费者的本地编译依赖。它是普通β，不是正式版；
不上传公共NuGet源，也不覆盖已撤回的`1.0.37-beta.1`。

## 1. 发布边界

本版完整发布：

- `Iwesun.Runtime.Diagnostics`
- `Iwesun.Runtime.Data`
- `Iwesun.Runtime.Networks`
- `Iwesun.Runtime.WebView2`
- CLI、RemoteConsole、SampleHost、配置、技能、样例和文档

`modules/Web`仍在架构调试，明确不发布。载荷中不得出现`Iwesun.Runtime.Web.dll`、
`Iwesun.Runtime.Web.pdb`、独立Web库目录或Web项目源文件。原位于WebView2的`DomElement`
属性填充适配器已经回归Web调试组件，使WebView2和CLI不再反向依赖Web。

## 2. Networks beta.4升级

- 模糊`IpAddressValue.FromAddressBytes`删除，按数据来源改用`FromIPv4Bytes`、
  `FromIPv6Bytes`或17字节`TryReadBinary`。
- ICMPv6单双响应统一为36字节Windows原生布局、`Status@28`和`RoundTripTime@32`。
- IP/MAC补齐.NET 10 UTF-8 Span泛型接口，17/7字节二进制布局保持不变。
- 协议端点请求并发启动并使用请求私有不可变路由快照。
- HTTP/DoH使用有界租约池，活动连接延迟释放，空闲连接定时回收。
- UDP保持长期Socket、端口租约、严格实际远端匹配、迟到隔离和容量保护。

完整迁移见
[Networks beta.4升级迁移报告](../modules/Networks/docs/03-reference/NETWORKS_3_0_BETA4_UPGRADE_MIGRATION_REPORT.md)。

## 3. 标准DLL布局

```text
lib\Iwesun.Runtime.Diagnostics\Debug|Release\Iwesun.Runtime.Diagnostics.dll
lib\Iwesun.Runtime.Data\Debug|Release\Iwesun.Runtime.Data.dll
lib\Iwesun.Runtime.Networks\Debug|Release\Iwesun.Runtime.Networks.dll
lib\Iwesun.Runtime.WebView2\Debug|Release\Iwesun.Runtime.WebView2.dll
```

各库顶层同名DLL是Release兼容入口。Debug与Release具有相同文件名、命名空间、类型和接口，
但消费项目必须引用同配置目录，不得混用。

## 4. 强制迁移

1. 删除消费项目中的旧Runtime DLL和beta.3 Networks缓存。
2. 按来源修复全部`FromAddressBytes`编译错误，不恢复长度猜测包装。
3. RequestId继续由调用源生成非空GUID；内部流水号不替代外部身份链。
4. 不恢复V2/V3端点、可空Route、旧Flags、DList、RecordStore V1或旧程序集。
5. 从本版统一目录重新编译Debug/Release全部消费者。

## 5. β测试重点

- UDP/DNS代理的端口租约、实际远端、迟到隔离和容量拒绝；
- HTTP/DoH稳定池键、并发活动租约、路由变化和空闲回收；
- IPv6精确接口单播/组播和零响应三轴终态；
- UTF-8 IP/MAC接口、ScopeId分离和旧持久化迁移；
- DDNS Snap与Aether只引用本版DLL的双配置编译和核心端到端回归。

## 6. 交付目录

```text
artifacts\packages\Iwesun.Runtime.1.0.38-beta.1\
```

目录应包含MSI、便携ZIP、Networks nupkg/snupkg、发布说明、清单和SHA-256清单。

## 7. 正式版阻塞项

- WFP双真实网关并发隔离；
- Selene安装新载荷后的精确IPv6复验；
- UDP 64槽保护值的用户全流量数据；
- 经授权执行完整RS、DHCPv6和网卡恢复链；
- 干净环境安装、升级、卸载与最终版本冻结。


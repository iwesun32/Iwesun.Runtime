# Networks 3.0 IpAddressValue 迁移手册

> **状态**: CURRENT  
> **最后更新**: 2026-07-28  
> **适用版本**: Iwesun.Runtime.Networks 3.0  
> **源码参考**: `IpAddressValue.cs`、`NetworkIpAddressInterop.cs`、`NetworkPingEndpoint.cs`、`WindowsNetworkRouteSnapshotProvider.cs`  
> **上游合同**: [IP与MAC数值类型](../02-endpoints/ADDRESS_VALUE_TYPES.md)  
> **发布状态**: [Networks发布状态](../RELEASE_STATUS.md)

## 一、迁移目标

`IpAddressValue`是Networks唯一公共IP数值合同。迁移必须同时消除以下混用：

- 4字节IPv4裸地址；
- 16字节IPv6裸地址；
- 17字节`IpAddressValue`固定二进制格式；
- 带端口、FlowInfo和ScopeId的平台原生地址结构。

这些数据长度和语义不同。调用方必须在数据来源处明确地址族，不得依靠数组长度、默认值或
原生结构偏移推断公共值类型。

## 二、固定布局

| 数据 | 长度 | 正式入口 | 是否包含地址族 |
| --- | ---: | --- | --- |
| IPv4裸地址 | 4字节 | `FromIPv4Bytes` | 否，入口已明确IPv4 |
| IPv6裸地址 | 16字节 | `FromIPv6Bytes` | 否，入口已明确IPv6 |
| `IpAddressValue`固定二进制 | 17字节 | `TryWriteBinary` / `TryReadBinary` | 是，`family + high + low` |
| `MacAddressValue`固定二进制 | 7字节 | MAC专用二进制入口 | 不适用于IP |

17字节格式不是Windows Socket、DNS、ICMP、WFP或路由API的原生地址格式。禁止把
`IpAddressValue`直接传给`Marshal.StructureToPtr`、`MemoryMarshal.AsBytes`或Socket。

## 三、破坏性API替换

### 3.1 裸字节输入

旧代码：

```csharp
var value = IpAddressValue.FromAddressBytes(bytes);
```

新代码必须根据数据来源明确选择：

```csharp
var ipv4 = IpAddressValue.FromIPv4Bytes(ipv4Bytes); // 必须正好4字节
var ipv6 = IpAddressValue.FromIPv6Bytes(ipv6Bytes); // 必须正好16字节
```

`FromAddressBytes`已删除，不提供兼容包装。错误长度抛出`ArgumentException`。

### 3.2 数值构造

```csharp
var ipv4 = new IpAddressValue(ipv4NetworkOrderUInt32);
var ipv6 = new IpAddressValue(ipv6HighNetworkOrderUInt64, ipv6LowNetworkOrderUInt64);
```

兼容的具名工厂仍可使用：

```csharp
var ipv4 = IpAddressValue.FromIPv4(ipv4NetworkOrderUInt32);
var ipv6 = IpAddressValue.FromIPv6(ipv6HighNetworkOrderUInt64, ipv6LowNetworkOrderUInt64);
```

IPv4数值、高64位和低64位均按网络字节序解释。

### 3.3 System.Net.IPAddress

```csharp
var value = new IpAddressValue(systemAddress);
// 或
var value = IpAddressValue.FromIPAddress(systemAddress);
```

该入口读取`IPAddress.AddressFamily`后分派，不依据`GetAddressBytes().Length`猜测。
公共纯地址值拒绝带ScopeId的IPv6；调用方必须把接口索引单独保存。

系统邻居、Socket和路由执行层可以在已经单独取得接口证据后，提取16字节纯IPv6地址；
不得把被剥离的ScopeId丢失为无来源信息。

## 四、按数据来源迁移

| 数据来源 | 地址入口 | 额外要求 |
| --- | --- | --- |
| DNS A记录 | `FromIPv4`或`FromIPv4Bytes` | 读取4字节网络序 |
| DNS AAAA记录 | `FromIPv6`或`FromIPv6Bytes` | 读取16字节网络序 |
| ARP表 | `FromIPv4Bytes`或明确IPv4的`IPAddress`转换 | MAC使用`MacAddressValue` |
| IPv6邻居表 | `FromIPv6Bytes` | `InterfaceIndex`独立保存 |
| ICMPv4回复 | `FromIPv4Bytes` | 不读取17字节格式 |
| ICMPv6回复 | `FromIPv6Bytes` | 原生回复布局与公共值分层 |
| `SOCKADDR_INET` IPv4 | `FromIPv4Bytes(bytes[4..8])` | 先确认原生Family |
| `SOCKADDR_INET` IPv6 | `FromIPv6Bytes(bytes[8..24])` | ScopeId位于独立字段 |
| WFP IPv4条件 | 32位IPv4网络序值 | 不传17字节结构 |
| WFP IPv6条件 | 16字节地址数组 | 不含family与ScopeId |
| JSON | 公共JSON转换器 | 字符串或JSON `null` |
| 固定持久化/IPC | `TryWriteBinary` / `TryReadBinary` | 缓冲区固定17字节 |

## 五、原生结构边界

平台API返回的结构必须先按平台ABI解析，再转换为`IpAddressValue`。以ICMPv6为例：

```text
Icmp6SendEcho2原生回复
  → 按ICMPV6_ECHO_REPLY布局取得16字节Responder地址
  → FromIPv6Bytes
  → 17字节语义完整的IpAddressValue
```

不得把17字节`IpAddressValue`嵌入原生回复结构，也不得用公共值类型的字段布局计算
平台结构的`Status`、RTT或下一条记录步长。ICMPv6单响应和多响应必须复用同一原生解析器。

## 六、禁止的兼容写法

以下方式不得进入消费项目或公共库：

```csharp
// 禁止：重新按长度猜地址族
bytes.Length == 4
    ? IpAddressValue.FromIPv4Bytes(bytes)
    : IpAddressValue.FromIPv6Bytes(bytes);

// 禁止：把17字节固定格式当作裸IPv6
IpAddressValue.FromIPv6Bytes(serializedIpAddressValue);

// 禁止：静默删除%zone后不保存接口
IpAddressValue.Parse(scopedText.Split('%')[0]);

// 禁止：直接复制值类型内存作为固定二进制协议
MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref value, 1));
```

如果调用处不知道地址族，应先修正上游合同，让上游提供`AddressFamily`或强类型来源；不能在
`IpAddressValue`入口恢复模糊推断。

## 七、消费者迁移步骤

1. 升级到包含显式地址族入口的Networks DLL或包。
2. 全局查找`FromAddressBytes`，按数据来源改为IPv4或IPv6入口。
3. 查找`GetAddressBytes`、`Marshal`、`MemoryMarshal`和硬编码4/16/17偏移。
4. 确认ScopeId、接口索引、端口和网关保存在访问计划或路径证据中。
5. 确认固定持久化和IPC只通过`TryWriteBinary`/`TryReadBinary`。
6. 全量重新编译；不得用兼容扩展方法隐藏编译错误。
7. 运行值类型、DNS、Ping、路由、Socket、ARP/NDP和WFP定向测试。
8. 在真实物理接口上验证IPv4、IPv6单播及IPv6组播。

## 八、推荐搜索

```powershell
rg -n "FromAddressBytes|GetAddressBytes|IpAddressValue.BinarySize" src tests
rg -n "Marshal|MemoryMarshal|FieldOffset|StructLayout" src
rg -n "ScopeId|InterfaceIndex|AddressFamily" src
```

期望结果：

- `IpAddressValue.FromAddressBytes`为零；
- 不存在直接Marshal `IpAddressValue`；
- 原生边界先确认地址族，再调用显式裸字节入口；
- ScopeId不进入纯地址值。

## 九、验收门禁

- `IpAddressValue`的CLR大小与固定二进制长度均为17字节；
- IPv4工厂拒绝非4字节输入；
- IPv6工厂拒绝非16字节输入；
- IPv4映射IPv6仍保持IPv6地址族；
- 带ScopeId的公共构造和文本入口被拒绝；
- ICMPv6单双响应使用同一36字节原生回复布局；
- Debug与Release测试全部通过；
- 消费项目使用目标DLL完成全量编译；
- 真实网络结果同时报告协议结果与访问约束结果。

## 十、DDNS Snap迁移示例

DDNS Snap必须继续从正式Runtime安装目录或明确的隔离验证DLL引用Networks，不建立跨仓
`ProjectReference`。调试新Networks源码时，通过构建属性指向本轮DLL；验证完成后再恢复正式
安装目录引用。

扫描业务只接收转换完成的`IpAddressValue`。设备在线状态、地址生命周期、公网连通性、
Echo验证、注册地址优选和注册状态继续保存在`IpRecord`业务字段，不得写入纯IP值类型。

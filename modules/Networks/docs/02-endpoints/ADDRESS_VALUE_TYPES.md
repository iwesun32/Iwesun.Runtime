# IP 与 MAC 数值类型

> **状态**: CURRENT（进入Networks 3.0.0-beta.4 / Runtime 1.0.38-beta.1）
>
> **最后更新**: 2026-07-28
>
> **源码参考**: `IpAddressValue.cs`、`MacAddressValue.cs`、`IpAddressTraits.cs`、`MacAddressTraits.cs`、`IpAddressType.cs`

## 公共边界

`IpAddressValue` 与 `MacAddressValue` 位于统一命名空间 `Iwesun.Runtime.Networks`，仅依赖
`System.*`。它们提供网络身份的规范数值表示、分类、比较、固定二进制表示和 JSON
字符串转换，不承载设备在线、地址生命周期、公网连通性、Echo 验证或 DDNS 注册状态。

## IpAddressValue

- `default` 与 `Null` 表示未填写；`0.0.0.0`、`::` 是明确的 Unspecified 地址。
- 基础结构固定为17字节：128位纯地址数值加1字节状态；不保存文本、ScopeId、接口或分类缓存。
- 状态只允许 Null、IPv4、IPv6；IPv4 使用低32位网络序数值，其余96位固定为零。
- IPv6 `%zone`/`ScopeId`属于接口和路径约束，不属于IP数值。带作用域文本在本类型解析入口明确拒绝，
  调用方必须把接口索引保存到访问计划、传输端点或实际路径证据。
- `IpAddressTraits` 提供 IPv4/IPv6、Loopback、Private、LinkLocal、Multicast、
  Broadcast、Shared、Documentation、Benchmark、Reserved、GlobalUnicast、
  Transition、IPv4Mapped、UniqueLocal 和 SiteLocal 正交标志。
- `IpAddressType` 是由固有地址特征产生的简化分类，不表示地址是否在线或是否可访问公网。
- CLR物理结构和固定二进制格式均为17字节；二进制顺序为
  `family(1) + high(8) + low(8)`。
- 裸地址构造严格区分地址族：`FromIPv4Bytes`只接受4字节，
  `FromIPv6Bytes`只接受16字节；不再提供按长度猜测地址族的
  `FromAddressBytes`。
- 数值构造同样显式区分：单个32位网络序数值构造IPv4，两个64位网络序
  高低段构造IPv6。`IPAddress`入口依据其`AddressFamily`分派，而不是依据
  返回字节数组长度猜测。
- 17字节固定二进制格式只通过`TryWriteBinary`/`TryReadBinary`读写；不得把
  CLR字段内存、4/16字节裸地址或Windows原生结构直接当作该格式。
- JSON 使用规范 IP 字符串；Null 写为 JSON `null`。
- 实现`IParsable<IpAddressValue>`、`ISpanParsable<IpAddressValue>`、
  `ISpanFormattable`、`IUtf8SpanParsable<IpAddressValue>`和`IUtf8SpanFormattable`。
  UTF-16与UTF-8入口共享纯地址、Null和ScopeId拒绝规则，格式化直接写入调用方缓冲区，
  不先创建格式化字符串。

### IpAddressType

| 值 | 固有地址分类 |
| --- | --- |
| `Null` | 数值未填写，分类未执行 |
| `Unset` | 已分类，但没有适用的简化类型 |
| `Public` | 全局单播地址 |
| `Private` | IPv4 私网或 IPv6 ULA/站点本地地址 |
| `LinkLocal` | 链路本地地址 |
| `Loopback` | 回环地址 |
| `Temp` | 保留给消费者的其他临时简化分类；数值分类器不据此判断系统生命周期 |
| `Multicast` | 组播地址 |
| `Transition` | IPv4 映射或协议过渡地址 |

`IpAddressType` 只由地址数值的固有范围产生。系统报告的 Temporary、Deprecated、DHCP、
公网可达或 Echo 一致性必须使用消费者自己的独立状态，不能写回本枚举。

### IpAddressTraits

| 标志 | 含义 |
| --- | --- |
| `None` | Null 或没有固有标志 |
| `IPv4` / `IPv6` | 地址族 |
| `Unspecified` | `0.0.0.0` 或 `::` |
| `Loopback` | 回环范围 |
| `Private` | RFC1918、ULA 或旧站点本地范围 |
| `LinkLocal` | IPv4/IPv6 链路本地 |
| `Multicast` | 组播范围 |
| `Broadcast` | IPv4 有限广播 |
| `Shared` | IPv4 运营商共享地址空间 |
| `Documentation` | 文档示例保留范围 |
| `Benchmark` | 基准测试保留范围 |
| `Reserved` | 不能按普通全局单播使用的保留范围 |
| `GlobalUnicast` | 数值范围属于全局单播 |
| `Transition` | 协议过渡地址 |
| `IPv4Mapped` | IPv4 映射 IPv6 |
| `UniqueLocal` | IPv6 ULA |
| `SiteLocal` | 已废弃 IPv6 站点本地范围 |

## MacAddressValue

- `default` 与 `Null` 表示未填写；全零 EUI-48 是明确值，但不能作为有效设备身份。
- 基础结构固定为7字节：48位地址数值加1字节 Null/Value 状态，不保存文本、OUI、NIC标识或分类缓存。
- CLR物理结构和固定二进制格式均为7字节；二进制顺序为 `state(1) + address(6)`。
- OUI、NIC标识以及全部 `MacAddressTraits` 均由48位地址按需计算，是只读属性而非存储字段。
- 接受冒号、连字符、连续十六进制和 Cisco 点分格式。
- 空字符串和纯空白输入按“未填写”解析为 Null，用于接收旧版数据；其他非法格式仍返回解析失败。
- 规范输出为大写冒号格式。
- `MacAddressTraits` 提供 Eui48、Unspecified、Broadcast、Unicast、Multicast、
  Individual、Group、UniversallyAdministered 和 LocallyAdministered 标志。
- JSON 使用规范 MAC 字符串；Null 固定写为 JSON `null`。读取兼容旧版空字符串和纯空白字符串，
  并统一转换为 Null，不再原样传播旧格式。
- 实现字符和UTF-8两组泛型解析/格式化接口；UTF-8输出完整支持既有`G`、`N`、`D`、`C`
  四种格式，不改变规范大写冒号输出。

### MacAddressTraits

| 标志 | 含义 |
| --- | --- |
| `None` | Null 或没有固有标志 |
| `Eui48` | 明确的48位 MAC 值 |
| `Unspecified` | 全零地址 |
| `Broadcast` | 全 `FF` 广播地址 |
| `Unicast` | I/G 位为0的单播地址 |
| `Multicast` | 非广播且 I/G 位为1的组播地址 |
| `Individual` | IEEE Individual 地址 |
| `Group` | IEEE Group 地址 |
| `UniversallyAdministered` | U/L 位为0 |
| `LocallyAdministered` | U/L 位为1 |

## 相等与排序

两个类型均实现泛型与非泛型相等/比较合同。Null 只有一个相等值，升序排序时位于所有实际值之后。
地址值相等只比较状态和128位纯地址数值。IPv6接口作用域、端口和路径约束必须由上层组合类型
分别参与端点或访问身份比较，不能写入本值类型。

## 迁移与发布

类型从 DDNS Snap 业务程序集迁入 Networks 后，消费者必须直接引用
`Iwesun.Runtime.Networks` 命名空间；公共库不提供旧命名空间转发包装。消费者始终从 Runtime
统一发布目录引用 DLL，不得跨仓库使用 `ProjectReference`。IP 与 MAC 的物理布局变化属于强制
升级边界，消费者必须重新编译，并以程序集/包版本判断合同版本。

`FromAddressBytes`已经删除。编译错误必须按数据来源迁移：A记录、IPv4原生字段和
4字节Socket地址使用`FromIPv4Bytes`；AAAA记录、ICMPv6回复、IPv6邻居与16字节
Socket地址使用`FromIPv6Bytes`。不得为消除编译错误重新加入长度推断包装。

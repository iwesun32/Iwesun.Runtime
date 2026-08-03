# 固定网络原语

> **状态**: CURRENT | **最后更新**: 2026-07-28
>
> **源码参考**: `IpAddressValue.cs`、`MacAddressValue.cs`、`BinaryNetworkPrimitives.cs`
>
> **详细地址合同**: [IP 与 MAC 数值类型](ADDRESS_VALUE_TYPES.md)

## IpAddressValue

Networks 只有一个公共 IP 数值类型：`IpAddressValue`。它是17字节只读值结构，只保存
Null/IPv4/IPv6状态和128位纯地址。旧 `BinaryIpAddress` 已删除，不提供别名、包装或类型转发。

IPv6 `ScopeId` 不属于 IP 数值。接口选择保存在访问计划及实际路径证据中，只在调用
`System.Net.IPAddress`、Socket 或 Windows 原生 API 的边界临时组合。由此，同一个纯地址不会因
操作系统接口编号不同而产生不同的相等性、哈希、集合项或连接池稳定键。

固定二进制格式为 `family(1) + high(8) + low(8)`，共17字节。IPv4要求高96位为零；
非法地址族和非规范IPv4载荷在 `TryReadBinary` 中被拒绝。

裸网络地址不是该固定格式。IPv4裸地址必须通过`FromIPv4Bytes`按4字节读取，
IPv6裸地址必须通过`FromIPv6Bytes`按16字节读取；已删除按长度自动判定地址族的
入口。数值构造分别使用32位IPv4网络序值和两个64位IPv6网络序高低段。

## MacAddressValue

`MacAddressValue` 是7字节只读值结构：`state(1) + EUI-48(6)`。OUI、NIC标识和分类标志均
按需计算，不占用额外存储。详见[IP 与 MAC 数值类型](ADDRESS_VALUE_TYPES.md)。

## BinaryNetworkError

`BinaryNetworkError` 保留为固定错误值，包含：

| 字段 | 类型 | 含义 |
| --- | --- | --- |
| `Kind` | `byte` | 错误类型 |
| `Code` | `int` | 平台错误码 |
| `HResult` | `int` | HRESULT |

## 使用原则

1. 网络合同、选择器、证据和端点统一使用 `IpAddressValue`/`MacAddressValue`。
2. 地址值只表达地址本身；接口、端口、网关、协议及路径证据使用独立字段。
3. 通过 `Span<byte>` 和固定长度格式进行无歧义序列化。
4. 旧类型已被强制移除，升级依靠编译错误定位全部残留引用。

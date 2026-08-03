# 本地系统表端点

> **状态**: CURRENT | **最后更新**: 2026-07-25
>
> **源码参考**: `WindowsArpTableEndpoint.cs`、`WindowsIpv6NeighborSnapshotEndpoint.cs`、
> `PowerShellSingleCommandEndpoints.cs`
>
> **地址合同**: [IP 与 MAC 数值类型](ADDRESS_VALUE_TYPES.md)

## WindowsArpTableEndpoint

该端点执行 Windows `arp -a`，把一份输出转换为多条强类型记录：

```csharp
public readonly record struct WindowsArpTableRequest(
    Guid RequestId = default,
    int TimeoutMs = 3000);

public readonly record struct WindowsArpTableRecord(
    Guid RequestId,
    MacAddressValue Mac,
    IpAddressValue Ip,
    string Type,
    DateTime ObservedAtUtc);
```

- `Guid.Empty` 在发送队列之前拒绝。
- 只发布合法IPv4及有效单播EUI-48；回环、链路本地、组播、广播、全零MAC和广播MAC被过滤。
- IP与MAC从系统文本立即正规化为 `IpAddressValue` 和 `MacAddressValue`，不会把文本格式传播给消费者。

## WindowsIpv6NeighborSnapshotEndpoint

该端点通过 PowerShell `Get-NetNeighbor -AddressFamily IPv6` 获取CSV，再发布强类型记录：

```csharp
public readonly record struct WindowsIpv6NeighborSnapshotRequest(
    Guid RequestId = default,
    int TimeoutMs = 3000);

public readonly record struct WindowsIpv6NeighborRecord(
    Guid RequestId,
    MacAddressValue Mac,
    IpAddressValue Ip,
    string State,
    int InterfaceIndex,
    DateTime ObservedAtUtc);
```

- IPv6地址值不保存 `%zone`/`ScopeId`；接口作用域只保存在 `InterfaceIndex`。
- 系统没有报告有效MAC时，`Mac` 为 `MacAddressValue.Null`，而不是 `null`、空字符串或特殊文本。
- IPv6组播地址不作为邻居项发布；`33:33:*`、全零、广播及其他无效MAC统一转换为Null。
- 原始PowerShell二进制/CSV合同只属于命令适配层；业务消费者应读取上述强类型端点。

## 使用示例

```csharp
using var arp = new WindowsArpTableEndpoint();
arp.Start();
arp.Send(new WindowsArpTableRequest(Guid.NewGuid()));
while (arp.TryReadReceived(out var record))
{
    Console.WriteLine($"{record.Ip} -> {record.Mac} ({record.Type})");
}

using var neighbors = new WindowsIpv6NeighborSnapshotEndpoint();
neighbors.Start();
neighbors.Send(new WindowsIpv6NeighborSnapshotRequest(Guid.NewGuid()));
while (neighbors.TryReadReceived(out var record))
{
    Console.WriteLine($"{record.Ip}%{record.InterfaceIndex} -> {record.Mac} ({record.State})");
}
```

## 相关文档

- [端点基类](ENDPOINT_BASE.md)
- [固定网络原语](BINARY_PRIMITIVES.md)
- [进程与PowerShell端点](PROCESS_POWERSHELL.md)

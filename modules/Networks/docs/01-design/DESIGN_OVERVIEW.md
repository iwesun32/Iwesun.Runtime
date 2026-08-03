# 设计总纲

> **状态**: CURRENT | **最后更新**: 2026-07-09
> **源码参考**: `modules/Networks/src/Iwesun.Runtime.Networks/NetworkAsyncEndpointBase.cs`
> **上游**: [README.md](../README.md)（文档索引）
> **下游**: [HARDWARE_STATE_MACHINE.md](HARDWARE_STATE_MACHINE.md)（状态机）、[FIFO_MODEL.md](FIFO_MODEL.md)（FIFO 模型）、[ENDPOINT_BASE.md](../02-endpoints/ENDPOINT_BASE.md)（基类 API）

---

## 一、核心命题

Iwesun.Runtime.Networks 提供一套**事件驱动、值类型 FIFO** 的异步网络端点模型，解决 .NET 中网络 I/O 的统一抽象问题：

1. **统一抽象**：所有网络操作（TCP 连接、UDP 收发、ICMP Ping、DNS 查询、ARP 表读取、进程启动等）都通过同一个基类 `NetworkAsyncEndpointBase<TSend, TReceive>` 暴露一致的 API。
2. **发送/接收解耦**：调用方通过 `Send()` 向内存 FIFO 写入请求，端点内部后台线程执行网络 I/O，结果通过接收 FIFO 或事件返回，调用方永不阻塞。
3. **硬件状态机**：统一建模网络硬件的就绪/阻塞/故障状态，调用方可以通过状态变化感知网络可用性，无需关心底层差异。
4. **零业务耦合**：仅依赖 `System.*`，不引用任何业务类型，可被任意 .NET 项目引用。

---

## 二、核心抽象

### 2.1 NetworkAsyncEndpointBase<TSend, TReceive>

```csharp
public abstract class NetworkAsyncEndpointBase<TSend, TReceive> : IDisposable
    where TSend : struct
    where TReceive : struct
{
    // 发送请求
    public NetworkSendResult Send(TSend request);
    public NetworkSendResult Send(ReadOnlySpan<TSend> requests);

    // 读取结果
    public bool TryReadReceived(out TReceive result);
    public int TryReadReceived(Span<TReceive> results);

    // 生命周期
    public void Start();
    public void Stop();
    public void Dispose();

    // 硬件状态信号
    public void SignalHardwareReady();
    public void SignalHardwareBlocked(NetworkEndpointErrorKind errorKind, string? message = null);
    public void ResetHardware();

    // 子类实现
    protected abstract Task OnSend(TSend request, CancellationToken ct);

    // 子类钩子（虚方法，可选重写）
    protected virtual bool OnFilterSend(ref TSend request);
    protected virtual void OnAnalyzeSend(ref TSend request);
    protected virtual bool OnFilterReceive(ref TReceive result);
    protected virtual void OnAnalyzeReceive(ref TReceive result);

    // 子类发布结果
    protected void PublishReceived(TReceive result);
    protected void PublishReceivedRaw(ReadOnlySpan<TReceive> results);

    // 事件
    public event EventHandler<NetworkSendStartedEventArgs>? SendStarted;
    public event EventHandler<NetworkSendCompletedEventArgs>? SendCompleted;
    public event EventHandler<ReceiveCompletedEventArgs>? ReceiveCompleted;
    public event EventHandler<HardwareStateChangedEventArgs>? HardwareStateChanged;
    public event EventHandler<HardwareErrorEventArgs>? HardwareError;
    public event EventHandler<EndpointErrorEventArgs>? Error;
    public event EventHandler? SendQueueAvailable;
}
```

### 2.2 值类型约束

`TSend` 和 `TReceive` 必须是值类型（`struct`），包括 `readonly record struct`。这确保：

- FIFO 存储为值类型数组，无 GC 压力
- 发送/接收无装箱开销
- 线程间传递无需额外同步

---

## 三、数据流

```text
调用方线程                     端点后台发送线程                    网络/系统
    │                               │                               │
    │  Send(request)                │                               │
    │ ─────────────────────────────>│                               │
    │  (写入发送FIFO, 唤醒线程)     │                               │
    │                               │  OnFilterSend()               │
    │                               │  OnAnalyzeSend()              │
    │                               │  OnSend() ───────────────────>│
    │                               │                               │ 网络I/O
    │                               │ <─────────────────────────────│
    │                               │  OnFilterReceive()            │
    │                               │  OnAnalyzeReceive()           │
    │                               │  PublishReceived()            │
    │                               │  (写入接收FIFO)               │
    │  TryReadReceived()            │                               │
    │ <─────────────────────────────│                               │
    │  (从接收FIFO读取)             │  ReceiveCompleted 事件        │
    │                               │                               │
```

---

## 四、端点清单

| 端点类                                   | TSend                      | TReceive                    | 用途                           |
| ---------------------------------------- | -------------------------- | --------------------------- | ------------------------------ |
| `NetworkTcpConnectEndpoint<TKey>`        | `NetworkTcpConnectRequest<TKey>` | `NetworkTcpConnectResponse<TKey>` | TCP精确访问探测 |
| `NetworkUdpDatagramEndpoint<TKey>`       | `NetworkUdpDatagramRequest<TKey>` | `NetworkUdpDatagramResponse<TKey>` | UDP请求响应与NAT映射 |
| `NetworkPingEndpoint<TKey>`              | `NetworkPingRequest<TKey>` | `NetworkPingResponse<TKey>` | IPv4/IPv6及多响应Ping |
| `NetworkDnsReverseLookupEndpoint<TKey>`  | `NetworkDnsReverseLookupRequest<TKey>` | `NetworkDnsReverseLookupResponse<TKey>` | PTR及UDP→TCP回退 |
| `NetworkNetBiosNameEndpoint<TKey>`       | `NetworkNetBiosNameRequest<TKey>` | `NetworkNetBiosNameResponse<TKey>` | NBNS单播/广播查询 |
| `NetworkHttpGetEndpoint<TKey>`           | `NetworkHttpGetRequest<TKey>` | `NetworkHttpGetResponse<TKey>` | HTTP稳定语义连接池 |
| `NetworkDohEndpoint<TKey>`               | `NetworkDohRequest<TKey>` | `NetworkDohResponse<TKey>` | DoH Wire/JSON查询 |
| `WindowsArpTableEndpoint`                | `WindowsArpTableRequest`   | `WindowsArpTableRecord`     | 读取本地 ARP 缓存              |
| `WindowsIpv6NeighborSnapshotEndpoint`    | `WindowsIpv6NeighborSnapshotRequest` | `WindowsIpv6NeighborRecord` | 读取 IPv6 邻居缓存      |
| `ProcessCommandEndpoint`                 | `ProcessCommandRequest`    | `ProcessCommandResult`      | 启动子进程并获取输出           |
| `PowerShellWriteOutputEndpoint`          | `PowerShellCommandRequest` | `PowerShellCommandResult`   | 执行单条 PowerShell 命令       |
| `PowerShellIpv6NeighborSnapshotEndpoint` | `PowerShellIpv6NeighborSnapshotBinaryRequest` | 原始CSV二进制结果 | 底层PowerShell命令适配 |

---

## 五、设计栈阅读顺序

```text
DESIGN_OVERVIEW.md（本文）
  ├── HARDWARE_STATE_MACHINE.md  → 硬件状态机
  ├── FIFO_MODEL.md              → FIFO 模型与背压
  └── ENDPOINT_BASE.md           → 基类 API 参考
        ├── BINARY_PRIMITIVES.md → IpAddressValue / MacAddressValue
        ├── TCP_UDP_PING.md      → TCP/UDP/Ping 端点
        ├── NAME_RESOLUTION.md   → DNS/NetBIOS 端点
        ├── LOCAL_TABLES.md      → ARP/IPv6 邻居端点
        └── PROCESS_POWERSHELL.md → 进程/PowerShell 端点
```

---

## 六、相关文档

- 仓库入口（AI 指令）→ [../../../../AGENTS.md](../../../../AGENTS.md)
- 源码索引 → [../03-reference/SOURCE_MAP.md](../03-reference/SOURCE_MAP.md)
- 逻辑约定 → [../03-reference/LOGICAL_CONTRACTS.md](../03-reference/LOGICAL_CONTRACTS.md)

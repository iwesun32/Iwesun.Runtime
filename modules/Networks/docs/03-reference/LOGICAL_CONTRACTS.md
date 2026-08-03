# 逻辑约定

> **状态**: CURRENT | **最后更新**: 2026-07-19
> **源码参考**: 整个 `modules/Networks/src/Iwesun.Runtime.Networks/` 目录
> **上游**: [../README.md](../README.md)（文档索引）
> **下游**: 无

---

## 一、类型约束

### 1.1 值类型约束

- **`TSend` 和 `TReceive` 必须是值类型（`struct`）**。基类声明为 `where TSend : struct where TReceive : struct`。
- 所有请求/结果类型使用 `readonly record struct`，确保不可变性和值语义。
- 禁止使用引用类型（`class`）作为 `TSend`/`TReceive`，避免 GC 压力和共享引用导致的并发问题。

### 1.2 零业务耦合

- 本库仅依赖 `System.*` 命名空间，不引用任何第三方库或业务项目。
- 不包含任何 DDNS Snap 或其他消费者的业务逻辑。
- 所有网络操作都是通用的，不绑定特定业务场景。

---

## 二、命名约定

| 元素     | 约定                           | 示例                                        |
| -------- | ------------------------------ | ------------------------------------------- |
| 跟踪端点类名 | `Network*Endpoint<TKey>`    | `NetworkTcpConnectEndpoint<string>`         |
| 请求类型 | `*Request`                     | `TcpConnectRequest`                         |
| 结果类型 | `*Result` / `*Record`          | `TcpConnectResult`、`WindowsArpTableRecord` |
| 私有字段 | `_camelCase`（下划线前缀）     | `_sendFifo`、`_sendSignal`                  |
| 公共方法 | `PascalCase`                   | `Send`、`TryReadReceived`                   |
| 公共属性 | `PascalCase`                   | `IsRunning`、`HardwareStatus`               |
| 枚举值   | `PascalCase`                   | `Ready`、`BufferOverflow`                   |
| 命名空间 | 扁平命名空间 `Iwesun.Runtime.Networks` | 所有类型都在此命名空间下                    |

---

## 三、错误处理约定

### 3.1 防御性执行

- **不跨线程抛异常**：后台发送线程中的异常被捕获并通过 `Error` 或 `HardwareError` 事件报告，不直接抛出到调用方线程。
- **无效请求跳过**：`OnFilterSend` 返回 `false` 的请求被丢弃，不影响后续请求处理。
- **单个请求失败不终止端点**：一个请求的异常不会导致整个端点停止，错误被记录后继续处理下一个请求。

### 3.2 错误报告路径

| 错误类型     | 事件                             | 说明                                        |
| ------------ | -------------------------------- | ------------------------------------------- |
| 发送过滤拒绝 | `Error`                          | `SendFiltered`                              |
| 发送分析异常 | `Error`                          | `SendAnalyzeFailed`                         |
| 发送过程异常 | `Error`                          | `SendFailed`                                |
| 接收过滤拒绝 | `Error`                          | `ReceiveFiltered`                           |
| 接收分析异常 | `Error`                          | `ReceiveAnalyzeFailed`                      |
| 缓冲区溢出   | `Error` + `HardwareStateChanged` | `BufferOverflow`，状态切换到 BufferOverflow |
| 硬件级故障   | `HardwareError`                  | 网络适配器故障、Socket 错误等               |

### 3.3 参数验证

- 公共方法入口使用 `ArgumentNullException.ThrowIfNull()` 验证引用类型参数。
- 子类在 `OnFilterSend` 中验证请求字段的有效性（如端口范围、IP 地址族）。

---

## 四、线程安全约定

### 4.1 线程安全保证

- `Send()` 和 `TryReadReceived()` 可从任意线程并发调用，无需外部同步。
- 后台发送线程是唯一的消费者（单线程出队），避免并发 I/O 竞争。
- 所有共享状态通过 `ConcurrentQueue`、`Volatile`、`Interlocked` 和 `WaitHandle` 保护。

### 4.2 线程所有权

| 资源/操作           | 所有线程                          | 说明                              |
| ------------------- | --------------------------------- | --------------------------------- |
| `Send()` 入队       | 多线程（调用方）                  | ConcurrentQueue 原生支持          |
| `_sendFifo` 出队    | 后台发送线程                      | 单线程消费                        |
| `PublishReceived()` | 后台发送线程                      | 从 OnSend 中调用，单线程生产      |
| `TryReadReceived()` | 多线程（调用方）                  | ConcurrentQueue 原生支持          |
| 硬件状态转换        | 后台发送线程                      | 仅后台线程修改 `_hardwareStatus`  |
| 事件触发            | 后台线程或 SynchronizationContext | 若提供 eventContext 则 marshaling |

### 4.3 禁止事项

- 子类 `OnSend` 实现中不要阻塞后台线程过长时间（应使用 `async/await` 等待 I/O）。
- 不要在事件处理程序中调用 `Stop()` 或 `Dispose()`，可能导致死锁。
- 不要在 `OnFilterSend`/`OnAnalyzeSend`/`OnFilterReceive`/`OnAnalyzeReceive` 中执行耗时操作。

---

## 五、生命周期约定

### 5.1 状态转换

```text
Uninitialized ──Start()──> Ready ──Send()──> Busy ──完成──> Ready
                              │                       │
                              │                       └──错误──> Faulted
                              │                       └──队列满──> BufferOverflow
                              └──Stop()/Dispose()──> Stopped
```

### 5.2 Start/Stop 幂等性

- `Start()` 可多次调用，仅首次生效。
- `Stop()` 可多次调用，仅首次生效。
- `Dispose()` 调用后不可再使用端点，任何公共方法调用将抛 `ObjectDisposedException`。

### 5.3 自动启动

- 首次调用 `Send()` 时若端点未启动，会自动启动（`EnsureStarted(waitForStart: false)`），无需显式调用 `Start()`。
- 若需要确保端点完全启动后再发送，可显式调用 `Start()`（`waitForStart: true`）。

---

## 六、资源管理约定

- 端点实现 `IDisposable`，使用后必须 `Dispose()`（推荐 `using` 声明）。
- `OnSend` 中创建的 `TcpClient`、`UdpClient`、`Ping`、`Process` 等对象必须在 `OnSend` 返回前 `Dispose()`，使用 `using` 语句确保释放。
- 后台线程在 `Stop()`/`Dispose()` 时通过 `CancellationToken` 协作取消，不强制 `Abort()`。
- `WaitHandle`（`_sendSignal`、`_resetSignal`、`_hardwareReadySignal`）在 `Dispose()` 时释放。

---

## 七、平台兼容性

- 大部分端点（TCP/UDP/Ping/DNS/Process/PowerShell）跨平台（Windows/Linux/macOS）。
- Windows 专用端点（`WindowsArpTableEndpoint`、`WindowsIpv6NeighborSnapshotEndpoint`）使用 P/Invoke 调用 Windows API，仅在 Windows 平台可用；`NetworkNetBiosNameEndpoint<TKey>`使用UDP/NBNS协议实现。
- 跨平台端点不使用 Windows 专用 API。

---

## 八、相关文档

Networks 2.0只保留V2请求—响应端点。完成语义以`RequestAcknowledged`为准；默认对每条请求执行最多三次网络尝试
（包含首次发送），并通过原请求与响应组成的完成FIFO保留调用方关联。PowerShell、Process和本地快照的旧式FIFO事件
仅属于各自特殊模型，不得作为新请求—响应端点范式。

V2路由指令属于请求数据本身，必须和业务数据一起入FIFO。相邻记录可以使用不同目标IP、源IP、物理网卡和
`RouteAdapterId`；Pending、重试及完成记录保留各自指令。字段缺省时使用系统选择，显式字段不得被端点级可变状态覆盖。

- 文档索引 → [../README.md](../README.md)
- 源码映射 → [SOURCE_MAP.md](SOURCE_MAP.md)
- 端点基类 API → [../02-endpoints/ENDPOINT_BASE.md](../02-endpoints/ENDPOINT_BASE.md)

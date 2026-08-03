# FIFO 模型

> **状态**: CURRENT | **最后更新**: 2026-07-09
> **源码参考**: `modules/Networks/src/Iwesun.Runtime.Networks/NetworkAsyncEndpointBase.cs`（`_sendFifo`、`_receiveFifo`、容量控制、背压）
> **上游**: [DESIGN_OVERVIEW.md](DESIGN_OVERVIEW.md)（设计总纲）、[HARDWARE_STATE_MACHINE.md](HARDWARE_STATE_MACHINE.md)（状态机）
> **下游**: [ENDPOINT_BASE.md](../02-endpoints/ENDPOINT_BASE.md)（基类 API）

---

## 一、双 FIFO 架构

每个端点维护两个独立的 FIFO 队列：

```text
┌─────────────────┐         ┌──────────────────────┐         ┌──────────────────┐
│  调用方线程      │  Send() │  发送 FIFO            │  后台   │  网络/系统 I/O   │
│  (多生产者)      │ ──────> │  ConcurrentQueue<TSend>│ ──────> │  (OnSend)        │
└─────────────────┘         └──────────────────────┘  线程    └──────────────────┘
                                                              │
                                                              ▼
┌─────────────────┐         ┌──────────────────────┐         ┌──────────────────┐
│  调用方线程      │ TryRead │  接收 FIFO            │  Publish │  网络回调/结果   │
│  (多消费者)      │ <────── │ ConcurrentQueue<TReceive>│ <───── │  (OnSend 返回)   │
└─────────────────┘         └──────────────────────┘         └──────────────────┘
```

- **发送 FIFO**：`ConcurrentQueue<TSend>`，多生产者单消费者模型。调用方通过 `Send()` 入队，后台发送线程单线程出队执行。
- **接收 FIFO**：`ConcurrentQueue<TReceive>`，单生产者（后台线程）多消费者模型。网络回调通过 `PublishReceived()` 入队，调用方通过 `TryReadReceived()` 出队。

---

## 二、容量控制与背压

### 2.1 构造参数

| 参数                          | 默认值 | 说明                                                         |
| ----------------------------- | ------ | ------------------------------------------------------------ |
| `maxSendQueueLength`          | 4096   | 发送 FIFO 最大容量，≤0 表示无限制                            |
| `maxReceiveQueueLength`       | 4096   | 接收 FIFO 最大容量，≤0 表示无限制                            |
| `sendQueueAvailableThreshold` | 1      | 发送队列可用阈值（低于此容量触发 `SendQueueAvailable` 事件） |

### 2.2 背压机制

1. **发送时检查**：`Send()` 在入队前调用 `TryReserveSendSlot()`，若发送队列已满（`IsSendQueueFull`），则：
   - 不将请求入队
   - 调用 `ReportBufferOverflow()` 触发 `BufferOverflow` 状态
   - 返回 `NetworkSendResult.HardwareBlocked`

2. **接收时检查**：`PublishReceived()` 在入队前检查接收队列容量，若已满则丢弃最旧结果（FIFO 滑动窗口），触发 `Error` 事件。

3. **恢复通知**：后台发送线程处理完积压请求后，若发送队列长度回落到 `sendQueueAvailableThreshold` 以下，触发 `SendQueueAvailable` 事件，通知调用方可以继续发送。

### 2.3 队列状态属性

| 属性                    | 类型   | 说明                           |
| ----------------------- | ------ | ------------------------------ |
| `SendQueueLength`       | `int`  | 当前发送 FIFO 中的项目数       |
| `ReceiveQueueLength`    | `int`  | 当前接收 FIFO 中的项目数       |
| `SendQueueIdle`         | `int`  | 发送 FIFO 剩余容量             |
| `ReceiveQueueIdle`      | `int`  | 接收 FIFO 剩余容量             |
| `IsSendQueueFull`       | `bool` | 发送 FIFO 是否已满             |
| `IsReceiveQueueFull`    | `bool` | 接收 FIFO 是否已满             |
| `MaxSendQueueLength`    | `int`  | 发送 FIFO 最大容量（构造参数） |
| `MaxReceiveQueueLength` | `int`  | 接收 FIFO 最大容量（构造参数） |

---

## 三、发送流程

```text
Send(item)
  │
  ├─ 检查是否已 Disposed → 抛异常
  ├─ 检查是否已 Start → 若未启动则自动启动（waitForStart=false）
  ├─ TryReserveSendSlot() → 队列满？
  │    ├─ 是 → ReportBufferOverflow() → 返回 HardwareBlocked
  │    └─ 否 → 入队 _sendFifo
  ├─ _sendSignal.Set() → 唤醒后台发送线程
  └─ 返回 Sent
```

批量发送 `Send(ReadOnlySpan<TSend>)` 流程相同，但一次性预留多个槽位，减少信号触发次数。

---

## 四、后台发送线程循环

```text
WaitAny(_sendSignal, _resetSignal, _hardwareReadySignal)
  │
  ├─ _resetSignal 收到 → 执行重置（清空 FIFO、重置状态）→ 继续等待
  ├─ _hardwareReadySignal 收到 → 设置硬件就绪 → 继续等待
  └─ _sendSignal 收到 → 处理发送队列
       │
       ├─ while _sendFifo.TryDequeue(out item):
       │    ├─ OnFilterSend(ref item) → 返回 false？→ 报告 SendFiltered 错误 → continue
       │    ├─ OnAnalyzeSend(ref item) → 抛异常？→ 报告 SendAnalyzeFailed 错误 → continue
       │    ├─ 触发 SendStarted 事件
       │    ├─ OnSend(item, ct) → 抛异常？→ 报告 SendFailed 错误 → continue
       │    └─ 触发 SendCompleted 事件
       │
       └─ 检查队列容量 → 若低于阈值 → 触发 SendQueueAvailable 事件
```

---

## 五、接收流程

子类在 `OnSend` 中收到网络响应后，调用 `PublishReceived()` 发布结果：

```text
PublishReceived(result)
  │
  ├─ OnFilterReceive(ref result) → 返回 false？→ 报告 ReceiveFiltered 错误 → return
  ├─ OnAnalyzeReceive(ref result) → 抛异常？→ 报告 ReceiveAnalyzeFailed 错误 → return
  ├─ 接收队列满？→ 丢弃最旧结果（Dequeue）→ 报告 BufferOverflow 错误
  ├─ _receiveFifo.Enqueue(result)
  └─ 触发 ReceiveCompleted 事件
```

批量发布 `PublishReceivedRaw(ReadOnlySpan<TReceive>)` 流程相同，减少事件触发次数。

调用方读取结果：

```text
TryReadReceived(out result) → bool
  └─ 从 _receiveFifo.TryDequeue(out result)

TryReadReceived(Span<TReceive> results) → int
  └─ 批量出队，返回实际读取数量
```

---

## 六、线程安全保证

- **发送 FIFO**：`ConcurrentQueue` 支持多线程并发 `Send()`，无需外部锁。
- **接收 FIFO**：`ConcurrentQueue` 支持多线程并发 `TryReadReceived()`。
- **队列长度计数**：使用 `Volatile.Read`/`Interlocked.Increment`/`Interlocked.Decrement` 保证无锁原子更新。
- **状态字段**：使用 `volatile` 修饰布尔状态字段，保证跨线程可见性。
- **事件触发**：若构造时提供了 `SynchronizationContext`（如 UI 线程），事件通过 `_eventContext.Post()`  marshaling 到指定上下文；否则在后台线程直接触发。

---

## 七、FIFO 清空

以下情况会清空 FIFO：

1. **ResetHardware()**：清空发送和接收 FIFO，重置硬件状态为 Ready。
2. **Stop()**：清空发送 FIFO（接收 FIFO 中剩余结果仍可读取）。
3. **Dispose()**：清空两个 FIFO，释放 WaitHandle 资源。

---

## 八、相关文档

- 设计总纲 → [DESIGN_OVERVIEW.md](DESIGN_OVERVIEW.md)
- 硬件状态机 → [HARDWARE_STATE_MACHINE.md](HARDWARE_STATE_MACHINE.md)
- 基类 API → [../02-endpoints/ENDPOINT_BASE.md](../02-endpoints/ENDPOINT_BASE.md)

# 硬件状态机

> **状态**: CURRENT | **最后更新**: 2026-07-09
> **源码参考**: `modules/Networks/src/Iwesun.Runtime.Networks/NetworkAsyncEndpointBase.cs`（`NetworkHardwareStatus`、`NetworkSendResult`、`NetworkEndpointErrorKind`）
> **上游**: [DESIGN_OVERVIEW.md](DESIGN_OVERVIEW.md)（设计总纲）
> **下游**: [FIFO_MODEL.md](FIFO_MODEL.md)（FIFO 模型）、[ENDPOINT_BASE.md](../02-endpoints/ENDPOINT_BASE.md)（基类 API）

---

## 一、状态枚举

`NetworkHardwareStatus` 描述端点底层网络硬件的运行状态：

| 状态值           | 值  | 说明                                                                  |
| ---------------- | --- | --------------------------------------------------------------------- |
| `Uninitialized`  | 0   | 初始状态，端点已创建但未调用 `Start()`                                |
| `Ready`          | 1   | 硬件就绪，可以发送请求                                                |
| `Busy`           | 2   | 硬件繁忙（发送队列有积压），仍可接受请求但可能延迟                    |
| `BufferOverflow` | 3   | 发送/接收 FIFO 溢出，新请求被拒绝，需要等待 `SendQueueAvailable` 事件 |
| `Resetting`      | 4   | 硬件正在重置中（调用 `ResetHardware()` 后）                           |
| `Faulted`        | 5   | 硬件故障，端点停止工作，需要重新 `Start()` 或外部干预                 |
| `Stopped`        | 6   | 端点已停止（调用 `Stop()` 或 `Dispose()` 后）                         |

---

## 二、状态转换图

```text
         Start()
Uninitialized ──────────► Ready
     ▲                      │  ◄──────────────────────┐
     │                      │                         │
     │               SignalHardwareBlocked    SignalHardwareReady
     │                      │                         │
     │                      ▼                         │
     │                    Busy ◄──── ResetHardware()  │
     │                      │            │            │
     │               BufferOverflow      ▼            │
     │                      │       Resetting ────────┘
     │                      │            │
     │                      │     重置失败/致命错误
     │                      │            ▼
     │                      └────────► Faulted
     │                                   │
     └───────────────────────────────────┘
                    Stop()/Dispose()
                              │
                              ▼
                          Stopped
```

### 转换规则

1. **Uninitialized → Ready**：调用 `Start()` 后，端点启动后台发送线程，进入 Ready 状态。
2. **Ready → Busy**：发送队列积压超过阈值，或底层硬件报告繁忙。
3. **Busy → Ready**：发送队列回落到阈值以下，触发 `SendQueueAvailable` 事件。
4. **Ready/Busy → BufferOverflow**：发送 FIFO 达到 `maxSendQueueLength` 容量上限，新请求返回 `HardwareBlocked`。
5. **BufferOverflow → Ready**：后台线程处理完积压请求，FIFO 有可用空间，触发 `SendQueueAvailable`。
6. **任意状态 → Resetting**：调用 `ResetHardware()`，清空 FIFO，重置内部状态。
7. **Resetting → Ready**：重置完成，硬件恢复就绪。
8. **任意状态 → Faulted**：发生不可恢复错误（如网络适配器断开、Socket 异常）。
9. **任意状态 → Stopped**：调用 `Stop()` 或 `Dispose()`，后台线程退出。
10. **Stopped/Faulted → Ready**：重新调用 `Start()`。

---

## 三、发送结果枚举

`NetworkSendResult` 表示单次 `Send()` 调用的结果：

| 值                | 值  | 说明                                                   |
| ----------------- | --- | ------------------------------------------------------ |
| `Sent`            | 0   | 请求已成功写入发送 FIFO                                |
| `HardwareBlocked` | 1   | 硬件阻塞（BufferOverflow/Faulted/Stopped），请求未入队 |
| `Failed`          | 2   | 发送失败（过滤拒绝、分析异常等），请求未入队           |

---

## 四、错误类型枚举

`NetworkEndpointErrorKind` 分类端点运行中可能发生的错误：

| 值                     | 值  | 说明                                     |
| ---------------------- | --- | ---------------------------------------- |
| `SendFiltered`         | 0   | `OnFilterSend` 返回 false，请求被过滤    |
| `SendAnalyzeFailed`    | 1   | `OnAnalyzeSend` 抛出异常                 |
| `SendFailed`           | 2   | `OnSend` 抛出异常                        |
| `ReceiveFiltered`      | 3   | `OnFilterReceive` 返回 false，结果被过滤 |
| `ReceiveAnalyzeFailed` | 4   | `OnAnalyzeReceive` 抛出异常              |
| `BufferOverflow`       | 5   | FIFO 溢出                                |

---

## 五、状态信号方法

| 方法                      | 触发转换                         | 说明                                     |
| ------------------------- | -------------------------------- | ---------------------------------------- |
| `SignalHardwareReady()`   | Busy/Resetting → Ready           | 子类在硬件恢复时调用                     |
| `SignalHardwareBlocked()` | Ready/Busy → Busy/BufferOverflow | 子类在硬件阻塞时调用，附带错误类型和消息 |
| `ResetHardware()`         | 任意 → Resetting → Ready         | 清空 FIFO，重置内部状态，恢复就绪        |

---

## 六、事件

| 事件                   | 触发时机                                                |
| ---------------------- | ------------------------------------------------------- |
| `HardwareStateChanged` | 硬件状态发生转换时                                      |
| `HardwareError`        | 发生硬件级错误（BufferOverflow、Faulted）时             |
| `Error`                | 发生端点级错误（过滤失败、分析异常、发送失败）时        |
| `SendQueueAvailable`   | 发送 FIFO 从满变为可用时（BufferOverflow → Ready/Busy） |

---

## 七、相关文档

- 设计总纲 → [DESIGN_OVERVIEW.md](DESIGN_OVERVIEW.md)
- FIFO 模型 → [FIFO_MODEL.md](FIFO_MODEL.md)
- 基类 API → [../02-endpoints/ENDPOINT_BASE.md](../02-endpoints/ENDPOINT_BASE.md)

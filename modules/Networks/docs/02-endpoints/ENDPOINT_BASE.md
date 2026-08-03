# 端点基类 API 参考

> **状态**: CURRENT | **最后更新**: 2026-07-09
> **源码参考**: `modules/Networks/src/Iwesun.Runtime.Networks/NetworkAsyncEndpointBase.cs`
> **上游**: [DESIGN_OVERVIEW.md](../01-design/DESIGN_OVERVIEW.md)（设计总纲）、[HARDWARE_STATE_MACHINE.md](../01-design/HARDWARE_STATE_MACHINE.md)（状态机）、[FIFO_MODEL.md](../01-design/FIFO_MODEL.md)（FIFO 模型）
> **下游**: 各具体端点文档

---

## 一、构造函数

```csharp
protected NetworkAsyncEndpointBase(
    SynchronizationContext? eventContext = null,
    int maxSendQueueLength = 4096,
    int maxReceiveQueueLength = 4096,
    int sendQueueAvailableThreshold = 1)
```

| 参数                          | 默认值 | 说明                                                                                                                     |
| ----------------------------- | ------ | ------------------------------------------------------------------------------------------------------------------------ |
| `eventContext`                | `null` | 事件 marshaling 上下文；`null` 则在后台线程触发事件，UI 线程传入 `SynchronizationContext.Current` 可使事件在 UI 线程触发 |
| `maxSendQueueLength`          | 4096   | 发送 FIFO 最大容量                                                                                                       |
| `maxReceiveQueueLength`       | 4096   | 接收 FIFO 最大容量                                                                                                       |
| `sendQueueAvailableThreshold` | 1      | 发送队列可用阈值                                                                                                         |

---

## 二、公共方法

### 发送

| 方法                                   | 说明                                      |
| -------------------------------------- | ----------------------------------------- |
| `void Send(TSend item)`                | 发送单个请求，满队列时触发 BufferOverflow |
| `int Send(TSend[] items)`              | 批量发送，返回成功入队的数量              |
| `void Send(ReadOnlySpan<TSend> items)` | 批量发送（Span 重载）                     |

### 接收

| 方法                                         | 说明                           |
| -------------------------------------------- | ------------------------------ |
| `bool TryReadReceived(out TReceive item)`    | 尝试读取单个结果，返回是否成功 |
| `int TryReadReceived(Span<TReceive> buffer)` | 批量读取，返回实际读取数量     |

### 生命周期

| 方法             | 说明                                                                            |
| ---------------- | ------------------------------------------------------------------------------- |
| `void Start()`   | 启动端点，创建后台发送线程。首次 `Send()` 会自动调用（`waitForStart: false`）。 |
| `void Stop()`    | 停止端点，终止后台线程，清空发送 FIFO。                                         |
| `void Dispose()` | 释放所有资源（WaitHandle、线程等）。调用后不可再使用。                          |

### 硬件状态信号

| 方法                                                         | 说明                    |
| ------------------------------------------------------------ | ----------------------- |
| `void SignalHardwareReady(string reason = "hardware-ready")` | 通知硬件恢复就绪        |
| `void SignalHardwareBlocked(string reason, Exception?)`      | 通知硬件阻塞/繁忙       |
| `void ResetHardware(string reason = "hardware-reset")`       | 请求重置硬件，清空 FIFO |

---

## 三、子类可重写方法

### 抽象方法（必须实现）

```csharp
protected abstract Task OnSend(TSend request, CancellationToken ct);
```

子类在此方法中实现真正的网络发送逻辑。当收到网络响应时，调用 `PublishReceived()` 发布结果。

### 虚方法（可选重写）

| 方法                                         | 说明                                      | 默认行为              |
| -------------------------------------------- | ----------------------------------------- | --------------------- |
| `bool OnFilterSend(ref TSend request)`       | 发送前过滤请求，返回 `false` 则丢弃该请求 | 返回 `true`（不过滤） |
| `void OnAnalyzeSend(ref TSend request)`      | 发送前分析/修改请求                       | 空操作                |
| `bool OnFilterReceive(ref TReceive result)`  | 接收前过滤结果，返回 `false` 则丢弃该结果 | 返回 `true`（不过滤） |
| `void OnAnalyzeReceive(ref TReceive result)` | 接收前分析/修改结果                       | 空操作                |

---

## 四、子类发布方法

| 方法                                                      | 说明                 |
| --------------------------------------------------------- | -------------------- |
| `void PublishReceived(TReceive result)`                   | 发布单个接收到的结果 |
| `void PublishReceivedRaw(ReadOnlySpan<TReceive> results)` | 批量发布结果         |

---

## 五、公共属性

| 属性                    | 类型                    | 说明                     |
| ----------------------- | ----------------------- | ------------------------ |
| `IsRunning`             | `bool`                  | 端点是否正在运行         |
| `IsInitialized`         | `bool`                  | 端点是否已初始化         |
| `IsHardwareReady`       | `bool`                  | 硬件是否就绪             |
| `IsNetworkBusy`         | `bool`                  | 网络是否繁忙             |
| `IsBufferOverflow`      | `bool`                  | 是否处于缓冲区溢出状态   |
| `HardwareStatus`        | `NetworkHardwareStatus` | 当前硬件状态             |
| `LastHardwareException` | `Exception?`            | 最近一次硬件异常         |
| `LastHardwareReason`    | `string?`               | 最近一次硬件状态变化原因 |
| `SendQueueLength`       | `int`                   | 当前发送队列长度         |
| `ReceiveQueueLength`    | `int`                   | 当前接收队列长度         |
| `SendQueueIdle`         | `int`                   | 发送队列剩余容量         |
| `ReceiveQueueIdle`      | `int`                   | 接收队列剩余容量         |
| `IsSendQueueFull`       | `bool`                  | 发送队列是否已满         |
| `IsReceiveQueueFull`    | `bool`                  | 接收队列是否已满         |

---

## 六、事件

| 事件                   | 事件参数类型                           | 触发时机                             |
| ---------------------- | -------------------------------------- | ------------------------------------ |
| `SendStarted`          | `NetworkSendEventArgs<TSend>`          | 开始处理一个发送请求                 |
| `SendCompleted`        | `NetworkSendEventArgs<TSend>`          | 一个发送请求处理完成                 |
| `ReceiveCompleted`     | `NetworkReceiveEventArgs<TReceive>`    | 接收到一个结果并发布到 FIFO          |
| `HardwareStateChanged` | `NetworkHardwareStateChangedEventArgs` | 硬件状态发生转换                     |
| `HardwareError`        | `NetworkHardwareErrorEventArgs`        | 发生硬件级错误                       |
| `Error`                | `NetworkEndpointErrorEventArgs`        | 发生端点级错误（过滤/分析/发送失败） |
| `SendQueueAvailable`   | `NetworkQueueAvailableEventArgs`       | 发送队列从满变为可用                 |

---

## 七、实现端点的标准模式

```csharp
public sealed class MyEndpoint : NetworkAsyncEndpointBase<MyRequest, MyResult>
{
    public MyEndpoint(SynchronizationContext? eventContext = null)
        : base(eventContext, maxSendQueueLength: 4096, maxReceiveQueueLength: 4096)
    {
    }

    protected override async Task OnSend(MyRequest request, CancellationToken ct)
    {
        // 1. 执行网络操作
        // 2. 处理响应
        // 3. 发布结果
        var result = new MyResult { ... };
        PublishReceived(result);
    }

    protected override bool OnFilterSend(ref MyRequest request)
    {
        // 可选：验证请求，拒绝无效请求
        return request.IsValid;
    }
}
```

使用：

```csharp
using var endpoint = new MyEndpoint();
endpoint.Start();

// 发送请求
endpoint.Send(new MyRequest { ... });

// 读取结果
while (endpoint.TryReadReceived(out var result))
{
    ProcessResult(result);
}

// 或通过事件
endpoint.ReceiveCompleted += (s, e) => ProcessResult(e.Result);
```

---

## 八、相关文档

- 设计总纲 → [../01-design/DESIGN_OVERVIEW.md](../01-design/DESIGN_OVERVIEW.md)
- 硬件状态机 → [../01-design/HARDWARE_STATE_MACHINE.md](../01-design/HARDWARE_STATE_MACHINE.md)
- FIFO 模型 → [../01-design/FIFO_MODEL.md](../01-design/FIFO_MODEL.md)
- 逻辑约定 → [../03-reference/LOGICAL_CONTRACTS.md](../03-reference/LOGICAL_CONTRACTS.md)

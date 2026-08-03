# Iwesun.Runtime.Networks 1.2.0发布说明

> **状态**: READY_FOR_DISTRIBUTION（本地正式包） | **发布日期**: 2026-07-19

## 一、版本定位

1.2.0在1.1.0三次总尝试闭环上增加分级超时和完整尝试证据。所有V2 HTTP、DoH、TCP、IPv4 Ping与IPv6 Ping
统一实现，不改变旧V1端点。

## 二、三级超时

```csharp
endpoint.FirstAttemptTimeoutMs = 2000;
endpoint.SecondAttemptTimeoutMs = 3000;
endpoint.ThirdAttemptTimeoutMs = 4000;
```

- T1用于首次发送；
- T2用于第一次重试；
- T3用于第二次重试；
- 请求`TimeoutMs = null`时使用三级属性；
- 请求显式`TimeoutMs`时，该值覆盖本请求全部尝试；
- 设置兼容`DefaultTimeoutMs`会同时重设三级属性。

## 三、结果上报

成功完成和最终失败均报告：

- `AttemptCount`和`RetryCount`；
- `AttemptHistory`中的每次预算、起止时间、实际耗时、错误类型和错误代码；
- `TotalElapsedMs`总耗时。

`RequestRetrying`事件额外报告`NextRetryCount`和`NextAttemptNumber`。历史使用固定三个值槽，不使用`List`、数组或
错误字符串，保持值类型批量读取和条件允许时的栈分配能力。

## 四、使用示例

```csharp
endpoint.RequestAcknowledged += (_, args) =>
{
    var result = args.Completion;
    Console.WriteLine($"attempts={result.AttemptCount}, retries={result.RetryCount}, total={result.TotalElapsedMs}ms");
    for (var number = 1; number <= result.AttemptHistory.Count; number++)
    {
        var attempt = result.AttemptHistory.GetAttempt(number);
        Console.WriteLine($"#{attempt.AttemptNumber}: timeout={attempt.TimeoutMs}ms, elapsed={attempt.ElapsedMs}ms, result={attempt.FailureKind}");
    }
};
```

## 五、验证状态

- Networks Debug/Release测试：18/18通过；
- Networks Release构建：0警告、0错误；
- Aether Debug/Release构建：0警告、0错误；
- Aether非RealNetwork回归：Debug/Release均27/27通过；
- 本地Ping示例验证T1=200ms并上报实际尝试与总耗时；
- 不可达Ping验证三次预算100/150/200ms，上报实际147/150/206ms，总耗时507ms；
- 隔离消费者仅从本地NuGet源引用1.2.0，恢复、编译和运行通过，程序集版本为1.2.0.0。

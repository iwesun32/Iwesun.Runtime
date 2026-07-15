# Iwesun.Runtime.WebView2 公共控制与监控平台

`Iwesun.Runtime.WebView2` 为业务宿主提供统一 WebRuntime 控制、预编译 C# 程序截获转接、虚拟输入和执行监控基础。它不启动第二套浏览器，也不建立脱离 Runtime 的管道登记体系。

## 边界

- 页面自身 JavaScript 继续由 WebView2 页面运行。
- Runtime/WebRuntime 控制面仅在显式 `script.evaluate`/`eval` 动作下接受脚本文本；普通 DOM 和输入动作不会隐式回退到脚本。
- 业务方可实现 `IWebRuntimeBusinessProgram`，随宿主编译为二进制后注册。
- 管理层负责登记、JSON 命令转接、超时、取消、状态、历史和指标，不理解业务动作。
- C# 截获可以逐动作启用；强制截获时，未登记程序或未声明动作必须失败。

## 管道与线协议

WebRuntime 使用 Runtime 管道注册表申请专用物理管道。它与 RuntimeDiagnostics、Management 管道物理隔离，但统一使用：

```text
[4-byte little-endian int32 length][UTF-8 RuntimeDiagnosticFrame JSON]
```

唯一 Envelope 是 `RuntimeDiagnosticFrame`。禁止增加 WebRuntime 私有的 `schema / module / success / error` Envelope。

标准请求：

```json
{
  "header": {
    "schema": "rtdiag/2.0",
    "frameType": "request",
    "category": "instruction",
    "operation": "invoke",
    "requestId": "req-1",
    "correlationId": "workflow-1",
    "source": "runtime-cli",
    "destination": "AIGateway.WebRuntime"
  },
  "command": {
    "domain": "web.runtime",
    "target": "openai-web",
    "action": "input.keyboard.press",
    "args": {
      "programId": "aigateway.webview2",
      "key": "Enter"
    }
  }
}
```

标准响应使用 Frame 的：

- `status.ok`
- `status.code`
- `status.message`
- `status.retryable`
- `data`
- `meta.durationMs / traceId / page`

`WebRuntimeControlRequest` 只是 Command 参数对象，不是 Envelope。使用 `WebRuntimeProtocol.CreateRequestFrame` 构造 Frame，使用 `WebRuntimeProtocol.ParseRequestFrame` 验证和解析。`WebRuntimePipeClient.ExecuteAsync` 返回完整响应 Frame；只需要数据时使用 `ExecuteDataAsync`。

## 预编译 C# 程序接口

业务程序实现：

```csharp
public sealed class MyWebProgram : IWebRuntimeBusinessProgram
{
    public WebRuntimeProgramDescriptor Descriptor { get; } = new(
        "my-product.webview2",
        "My Product Web Program",
        "1.0.0",
        [new("session.snapshot", "Read session state.")]);

    public Task<WebRuntimeProgramResult> ExecuteAsync(
        WebRuntimeProgramContext context,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(
            WebRuntimeProgramResult.Ok(context.Request.Action, new { ready = true }));
    }
}
```

程序是宿主编译产物，不上传源码、不使用 Roslyn、不动态编译、不解释执行 C# 文本。

公共 DI 注册：

```csharp
services.AddSingleton<IWebRuntimeBusinessProgram, MyWebProgram>();
services.AddWebRuntimeProgramMonitoring();
```

`WebRuntimeProgramHost` 统一负责注册、初始化、停止、注销和失败回滚。`WebRuntimeProgramRegistry` 负责动作白名单、执行、超时、取消、活跃执行、最近 128 条历史和每动作指标。

## 监控目标

Runtime Hub 目标：`webruntime.programs`。

| action | 作用 |
| --- | --- |
| `capabilities` | 返回平台协议、截获模式和功能目录 |
| `snapshot / list / status` | 返回程序、动作、执行、历史和指标 |
| `execute` | 调用已登记的 C# 程序动作 |
| `cancel` | 按 ExecutionId 主动取消 |
| `initialize` | 初始化所有生命周期程序 |
| `stop` | 停止程序并取消活跃执行 |

执行状态通过 `ExecutionChanged` 发布，宿主可登记 Runtime Hook。Watch 快照包含：

- 平台版本、`rtdiag/2.0` 和 `web.runtime` Domain
- Program Descriptor 与动作目录
- 活跃执行
- 最近执行历史
- Started、Completed、Failed、TimedOut、Canceled 总计及每动作计数

## 公共输入动作

| action | 参数 |
| --- | --- |
| `input.mouse.move` | `x / y` |
| `input.mouse.click` | `x / y / button` |
| `input.mouse.doubleClick` | `x / y / button` |
| `input.mouse.wheel` | `x / y / deltaX / deltaY` |
| `input.keyboard.type` | `text / slowPrefixChars` |
| `input.keyboard.press` | `key` |
| `input.keyboard.shortcut` | `key / code / windowsVirtualKeyCode / ctrl / shift / alt / meta` |
| `input.ambient.start` | 环境鼠标与键盘托管任务启动 |
| `input.ambient.stop` | 停止并等待托管任务退出 |
| `input.ambient.status` | 返回环境输入状态 |

环境输入由 `WebAmbientInputController` 使用 `RTask` 管理。虚拟鼠标和键盘分别由 `WebVirtualMouse`、`WebVirtualKeyboard` 提供。

## 结构化事件

`WebRuntimeEventEnvelope` 使用 `EventId / CorrelationId / ProgramId / BackendId / Source / Kind / Data / Timestamp`。`Data` 是 `JsonElement`，不得把 JSON 再包装成字符串 Payload。

## 禁止事项

- 禁止 WebRuntime 私有 Envelope。
- 禁止未声明动作隐式回退到 JavaScript；脚本只允许通过 `script.evaluate`/`eval` 入口，并受长度、审计和专用管道约束。
- 禁止未声明动作回退到 JavaScript。
- 禁止由业务宿主自行维护另一套程序注册表或管道租约。
- 禁止字符串 JSON 隐藏 Status、Data 或事件数据。
- 禁止运行期上传或编译 C# 源码。

# WebRuntime 公共能力与 CLI 配置总说明

本文是 `Iwesun.Runtime.WebView2` 当前版本的统一说明，覆盖公共 API、脚本调试、网络接管、监控过滤、XPath 证据和 CLI 配置。

## 一、项目边界

- 公共代码位于 `Iwesun.Runtime.WebView2`。
- CLI 位于 `Iwesun.Runtime.Cli`，命令由 `RuntimeCliSystemConfig.json` 驱动。
- RuntimeDiagnostics、Management 和 WebRuntime 使用独立管道，但遵循统一 RuntimeDiagnosticFrame JSON 协议。
- 页面自身 JavaScript 默认继续运行；只有显式调用 `script.evaluate`/`eval` 才执行受控脚本。
- 公共模块不保存业务宿主状态，不替业务维护另一套管道注册表。

## 二、脚本反射

### 请求动作

| 动作 | 作用 |
| --- | --- |
| `script.evaluate` | 显式执行页面脚本 |
| `eval` | `script.evaluate` 的短别名 |

脚本来源按优先级读取：`Script` → `args.script` → `Text`。

限制：单次脚本最多 256 KiB；空脚本返回 `MISSING_SCRIPT`；超长返回 `SCRIPT_TOO_LARGE`；执行异常返回 `SCRIPT_EVALUATION_FAILED`。

### C# 调用

```csharp
var result = await WebRuntimeScriptDispatcher.TryExecuteAsync(
    session,
    new WebRuntimeControlRequest
    {
        Action = "script.evaluate",
        Script = "JSON.stringify({ url: location.href, title: document.title })"
    },
    cancellationToken,
    auditLog);
```

## 三、脚本审计

`WebRuntimeScriptAuditLog` 保存时间、动作、脚本长度、成功状态和错误码，不保存脚本文本、Cookie 值或令牌。

```csharp
var records = auditLog.Snapshot();
auditLog.Clear();
```

默认最多保存 256 条，可配置范围为 1–4096 条。

## 四、网络阻断和替换

`WebRuntimeNetworkRuleRegistry` 支持：

- `Block`：阻断匹配请求。
- `Replace`：返回指定状态码、Content-Type 和响应体。
- 按 `BackendId`、HTTP 方法和 URL 通配模式匹配。
- 规则新增、删除、清空和快照。

```csharp
rules.Add(new WebRuntimeNetworkRule(
    "block-completion",
    WebRuntimeNetworkRuleKind.Block,
    "*/chat/completion*",
    BackendId: "doubao-web",
    Method: "POST"));

var decision = rules.Decide("doubao-web", "POST", url);
```

规则注册表只产生决策。宿主必须在 WebView2 的网络请求事件中调用 `WebRuntimeHostController.HandleNetworkRequestAsync`，并将 `Blocked` 或替换响应应用到原生请求。

## 五、监控过滤

`WebRuntimeMonitorFilterRegistry` 支持按事件类型和 URL 过滤：

```csharp
filters.Add(new WebRuntimeMonitorFilter("chat-xhr", "XHR", "/chat/"));
var accepted = filters.Accept("XHR", requestUrl);
```

支持 `Add`、`Remove`、`Clear`、`Snapshot`。没有过滤器时默认接收全部事件。

## 六、XPath 证据和高亮

```csharp
var script = WebRuntimeEvidenceScripts.HighlightXPath("//button[@id='send']");
var clear = WebRuntimeEvidenceScripts.ClearHighlights();
```

`IWebRuntimeHostAdapter.CaptureXPathAsync` 由宿主负责解析 XPath 矩形和截图路径。

## 七、宿主接线

宿主实现 `IWebRuntimeHostAdapter`：

```csharp
public interface IWebRuntimeHostAdapter
{
    Task ApplyNetworkDecisionAsync(
        WebRuntimeNetworkRequest request,
        WebRuntimeNetworkDecision decision,
        CancellationToken ct);

    Task<WebRuntimeElementEvidence?> CaptureXPathAsync(
        string xpath,
        CancellationToken ct);
}
```

然后创建：

```csharp
var controller = new WebRuntimeHostController(hostAdapter);
await controller.HandleNetworkRequestAsync(request, cancellationToken);
```

WebView2 宿主应在 `WebResourceRequested` 中完成网络决策应用，在页面所属 STA 线程执行脚本和截图。

## 八、CLI 快捷命令

CLI 命令全部来自：

`Iwesun.Runtime.Cli/RuntimeCliSystemConfig.json`

当前命令：

| 命令 | 用途 |
| --- | --- |
| `web.script.evaluate` | 执行页面脚本 |
| `web.script.audit` | 查询脚本审计 |
| `web.network.rule.add` | 添加阻断/替换规则 |
| `web.network.rule.clear` | 清空网络规则 |
| `web.monitor.filter.add` | 添加监控过滤器 |
| `web.monitor.filter.clear` | 清空监控过滤器 |
| `web.highlight` | 高亮 XPath 目标 |
| `web.capabilities` | 查询 WebRuntime 能力 |
| `web.snapshot` | 获取页面快照 |
| `web.navigate` | 页面导航 |
| `web.mouse.click` | 虚拟鼠标点击 |
| `web.keyboard.type` | 虚拟键盘输入 |

### 自定义配置

用户可以复制系统配置为 `RuntimeCliUserConfig.json`，增加或覆盖命令，不需要修改 CLI 程序。自定义配置用于实验和业务别名；稳定后再合并到系统配置并发布。

## 九、验证结果

已通过：

- 全量解决方案编译：0 警告、0 错误。
- `web-runtime-script` 功能测试。
- 脚本成功、别名、缺参、超长和审计测试。
- 网络阻断、响应替换和宿主决策转交测试。
- 监控过滤和 XPath 高亮脚本生成测试。
- CLI 帮助列表检查。

## 十、尚未自动化的部分

以下必须由具体 WebView2 宿主实现，公共库不能伪造完成：

1. 将网络决策接入真实 `CoreWebView2.WebResourceRequested`。
2. 使用真实页面执行 XPath 区域截图。
3. 将过滤器接入实时事件缓冲和推送管道。
4. 使用真实豆包账号完成端到端验证。

## 十一、发布约束

- 当前只完成源码、配置和测试，不自动发布新版。
- CLI 优先使用用户自定义 JSON 配置验证。
- 规则、脚本和 Cookie 属于高权限调试能力，生产环境必须使用专用 Runtime 管道并记录审计摘要。

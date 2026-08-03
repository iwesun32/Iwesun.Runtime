# Iwesun.Runtime.WebView2.SampleHost 工程样例

`Iwesun.Runtime.WebView2.SampleHost` 是随 Runtime 发布的可编译 WPF/WebView2 示例，用于验证公共 WebView2 API，不属于业务适配器，也不修改 AIGateway。

## 工程结构

```text
Iwesun.Runtime.WebView2.SampleHost/
├─ Iwesun.Runtime.WebView2.SampleHost.csproj
├─ App.xaml / App.xaml.cs
└─ MainWindow.xaml / MainWindow.xaml.cs
```

工程引用 `Iwesun.Runtime.WebView2` 公共程序集和 `Microsoft.Web.WebView2`。

## 启动流程

1. 创建 WPF 窗口和 WebView2。
2. `EnsureCoreWebView2Async` 完成后注册 `WebResourceRequested`。
3. 创建 `WebRuntimeHostController` 和 `WebRuntimeScriptAuditLog`。
4. 导航到豆包页面。
5. 页面导航完成后调用 `script.evaluate`，读取 URL 和标题。
6. 网络规则通过 `WebRuntimeNetworkRuleRegistry` 产生决策，由宿主应用到 WebView2 原生请求。

## 脚本调用示例

```csharp
var result = await WebRuntimeScriptDispatcher.TryExecuteAsync(
    session,
    new WebRuntimeControlRequest
    {
        Action = WebRuntimeScriptActions.Evaluate,
        Script = "JSON.stringify({ url: location.href, title: document.title })"
    },
    cancellationToken,
    auditLog);
```

## 网络规则示例

```csharp
controller.NetworkRules.Add(new WebRuntimeNetworkRule(
    "block-completion",
    WebRuntimeNetworkRuleKind.Block,
    "*/chat/completion*",
    BackendId: "doubao-web",
    Method: "POST"));
```

宿主在 `CoreWebView2.WebResourceRequested` 中调用 `Decide`，对 `Blocked` 或替换响应设置 `CoreWebView2WebResourceResponse`。

## 运行和验证

```powershell
dotnet build modules\WebView2\samples\Iwesun.Runtime.WebView2.SampleHost\Iwesun.Runtime.WebView2.SampleHost.csproj -c Release
dotnet run --project modules\WebView2\samples\Iwesun.Runtime.WebView2.SampleHost\Iwesun.Runtime.WebView2.SampleHost.csproj -c Release
```

运行前需要安装 WebView2 Runtime。示例不会保存 Cookie，也不会把脚本正文写入审计日志。

`DataStreamRecorderSample.cs` 是独立可编译的数据记录器接线样例，展示命名委托、请求/响应交换、请求 sidecar、manifest、元数据快速门和生命周期释放。示例方法不会由窗口自动调用，避免默认产生记录文件。

完整页面证据同样不得在样例启动时默认落盘。设置 `IWESUN_WEBVIEW2_EVIDENCE_DIRECTORY` 后，样例会在 `Navigate` 前调用 `WebRuntimeNetworkEvidenceSession.StartAsync`，启用 `PageReconstruction` 正文策略，并把会话注册到 `WebRuntimeHostController`；未设置时完全不写证据正文。

```powershell
$env:IWESUN_WEBVIEW2_EVIDENCE_DIRECTORY = 'C:\Evidence\IwesunWebView2Sample'
dotnet run --project modules\WebView2\samples\Iwesun.Runtime.WebView2.SampleHost\Iwesun.Runtime.WebView2.SampleHost.csproj -c Release
```

业务宿主在用户明确选择本机证据目录后调用 `WebRuntimePageEvidenceCapture.CaptureAsync`，最后释放网络会话。CLI 宿主还应把 `network.evidence.status / policy / export` 交给 `WebRuntimeHostController.TryExecuteNetworkEvidenceAsync`。完整代码顺序、JSON、Shell 命令、输出清单和隐私边界见 [FULL_PAGE_EVIDENCE_API.md](FULL_PAGE_EVIDENCE_API.md)。

多快照宿主应设置 `WebRuntimePageEvidenceOptions.NetworkExport`，把外部文档/JSON 注册到 `WebRuntimeSharedHttpEvidenceStore`。每个快照使用稳定引用 ID，公共资源池负责 SHA-256 去重和反向引用；样例不得把同一正文复制到每个快照目录。

## 发布位置

- `bin\Iwesun.Runtime.WebView2.SampleHost\`：可运行示例输出。
- `samples\source\Iwesun.Runtime.WebView2.SampleHost\`：完整工程源码。
- 本文和 `skills\iwesun-runtime-integration\references\webview2-runtime.md`：配套说明与技能。

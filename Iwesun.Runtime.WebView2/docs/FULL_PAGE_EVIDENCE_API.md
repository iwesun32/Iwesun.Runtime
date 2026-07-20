# WebView2 完整页面与 HTTP 输入证据 API

本接口是 `Iwesun.Runtime.WebView2` 中面向已初始化 `CoreWebView2` 的通用证据层。它只保存浏览器运行时事实，不包含豆包规则、快捷键、页面版本、XAML 转换或业务数据解释。

## 生命周期

HTTP 证据会话必须在首次 `Navigate` 之前启动，否则导航早期请求无法补录：

```csharp
await using var network = await WebRuntimeNetworkEvidenceSession.StartAsync(
    webView.CoreWebView2,
    localStagingDirectory,
    new WebRuntimeNetworkEvidenceOptions
    {
        SharedStoreDirectory = sharedHttpStoreDirectory
    });

await WebRuntimeResourceApplicationTracker.InstallAsync(webView.CoreWebView2);

var checkpoint = network.CreateCheckpoint();
webView.CoreWebView2.Navigate(targetUrl);

await WebRuntimePageEvidenceCapture.CaptureAsync(
    webView.CoreWebView2,
    scriptSession,
    evidenceDirectory,
    network,
    checkpoint,
    cancellationToken);
```

`scriptSession` 使用既有 `IWebRuntimeScriptSession`，以便 DOM 真快照按稳定 DOM 顺序处理可访问 frame。宿主不能向本 API 传入 JavaScript 或 CDP 方法名；公开入口仅执行库内固定脚本和固定协议方法。

## 页面证据包

聚合入口一次生成：

- `live-dom-tree.json`：元素、文本、注释、attributes、安全 primitive property、开放 Shadow DOM 和可访问 iframe；
- `complete-dom-properties.json`：元素几何、可见性、完整计算样式及伪元素样式；
- `document.html`、`frames.json`、`frame-*.html`：运行时克隆 HTML；
- `cdp-dom-tree.json`、`dom-snapshot.json`：固定 CDP DOM 证据；
- `maximum-template.css`：当前 document 与可访问 iframe 的 CSSOM 静态规则；
- `scripts/`、`event-registry.json`：已解析脚本源码和 window/document 事件监听接口；
- `resource-container-map.json`：外部资源 URL 与直接 DOM 容器；
- `request-dom-application-map.json`：fetch/XHR 申请模块、调用栈摘要、完成状态，以及请求活动期或完成后 2.5 秒关联窗口内发生变化的呈现容器；
- `request-dom-assignment-tasks.json`：仅收集 `unknown` 请求，状态为 `pending-user-assignment`，留待下一阶段由用户指定文档或 JavaScript 控件容器；
- `page.mhtml`：固定 `Page.captureSnapshot` 页面归档；
- `runtime-evidence-manifest.json`：证据包版本、生成时间和文件清单。

任一必需步骤失败时，聚合 API 直接抛出异常，不生成可供宿主误标记的成功结果。

`WebRuntimeResourceApplicationTracker` 必须在首次导航之前安装。它使用程序集内固定脚本包装 fetch/XHR 并观察 DOM 变化，单个 frame 最多保留 2048 个请求、每个请求最多保留 128 个容器，URL、栈和属性字符串也有长度上限。输出是可审计的时间关联线索，不等价于业务层绝对因果；同一时间窗中的并发请求可能指向同一个容器。DOM `src`/`href` 等直接引用标记为 `linked`，时间邻近容器只标记 `candidate`；没有找到时明确标记 `unknown`，并由 `CreateManualAssignmentTasks` 放进下一阶段任务清单，不令页面证据采集失败。后续宿主由 C# 加载和校验用户选择，XAML 不直接读取任务文件。开放 Shadow DOM 和可访问 iframe 分别记录，跨域 frame 只标记不可访问。

## HTTP 输入证据

`WebRuntimeNetworkEvidenceSession` 记录全部响应元数据以及固定 CDP `requestWillBeSent`、`responseReceived` 事件。设置 `WebRuntimeNetworkEvidenceOptions.SharedStoreDirectory` 后，响应从会话启动时即写入独立公共库，与是否抓取快照无关；匹配 JSON、文本、Markdown、HTML、XML、PDF、Word/Office 的响应保存完整正文、长度和 SHA-256。单项正文读取失败记录在公共/快照清单的 `bodyFailures` 中，仍保存该响应元数据，不使 DOM/UI 快照失败。

多个页面快照应设置 `WebRuntimePageEvidenceOptions.NetworkExport`。快照阶段不重新抓取正文，而是从当前 document、Performance/DOM 资源 URL 和 fetch/XHR 跟踪记录判定页面使用的公共资源，写入正向引用并更新公共 `catalog.json` 的反向引用。`response-manifest.json:applications[]` 为每个引用输出 `linked / candidate / unknown`、容器和说明。快照的完整性由引用集合表达，不要求在每个快照目录重复复制正文。未设置公共库时仍保留独立快照目录导出模式，供单次取证使用。

默认清单会删除 Cookie、Set-Cookie、Authorization、Token、Secret、API Key 和 WebSocket Key 等敏感头。正文仍可能包含账号隐私，因此输出目录必须是调用方明确选择的本机证据目录，不得直接进入源码或发布目录。

## 边界

- 会话只能绑定一个 `CoreWebView2`，释放时解除事件订阅并关闭固定 Network domain。
- 使用公共资源库时 checkpoint 不切分公共正文；页面引用由当前页面实际出现的 URL 集合判定。checkpoint 仅保留给未启用公共库的独立导出模式。
- 页面分组、主/辅助关系、业务清单、DOM→XAML 和 WinUI 页面目录由调用项目负责。
- 需要业务匹配器、CLI 生命周期或 WebSocket/WebMessage 记录时，继续使用 [DATA_STREAM_MONITOR_RECORDER.md](DATA_STREAM_MONITOR_RECORDER.md)。

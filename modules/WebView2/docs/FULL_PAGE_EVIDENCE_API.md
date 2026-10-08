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
        SharedStoreDirectory = sharedHttpStoreDirectory,
        MaximumBodyBytes = 128 * 1024 * 1024,
        ClearHttpCachesOnStart = true,
        DisableHttpCacheDuringSession = true,
        BypassServiceWorkerDuringSession = true,
        PreserveSharedStoreDuringSession = false,
        BodyCapturePolicy = new()
        {
            Kinds = WebRuntimeNetworkBodyKinds.PageReconstruction,
            AdditionalContentTypes = Array.Empty<string>(),
            AdditionalExtensions = Array.Empty<string>()
        }
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

要求单次导航重新取得全部 HTTP 应用响应正文时，宿主同时启用 `ClearHttpCachesOnStart`、
`DisableHttpCacheDuringSession` 与 `BypassServiceWorkerDuringSession`。Runtime 只清理 WebView2 `DiskCache` 和
`CacheStorage`，不会清除 Cookie、登录状态、LocalStorage、IndexedDB 或 Service Worker 注册；缓存和 Service Worker
绕过开关在会话释放时恢复。高并发采集可设置 `PreserveSharedStoreDuringSession=false`：响应正文仍从会话启动起进入
本机暂存区，导出时再一次批量写入内容寻址公共库，避免数百个响应逐项争用公共目录锁。上述行为开关默认不改变普通宿主。

宿主若需要在 Runtime 证据之外同步生成自己的结构化输入，可先调用固定的 `WebRuntimePageEvidenceCapture.StabilizePresentationAsync`，紧接着生成业务输入，再用 `WaitForDocumentFonts=false`、`StabilizationDelay=Zero` 调用聚合采集，避免在两个证据源之间重复等待字体和固定延迟。动态页面仍须分别保留各文件的采集时间与动画时间点，不能伪称所有分块文件来自同一原子帧。

## 页面证据包

聚合入口一次生成：

- `live-dom-tree.json`：元素、文本、注释、attributes、安全 primitive property、开放 Shadow DOM 和可访问 iframe；
- `complete-dom-properties.json`：元素几何、可见性、完整计算样式及伪元素样式；
- `layout-state.json`：页面样式环境、媒体/支持条件、逐元素 parent/offsetParent/Shadow host/slot 关系、Flex/Grid/定位/尺寸/对齐等结构化语义、活动伪类、表单状态、滚动、client rect/box quad、动画 timing 与 keyframes；
- `style-provenance.json`：`1.2` 紧凑架构以与计算值相同的元素路径和 CDP `nodeId/backendNodeId` 建立连接；共享匹配规则、inline/attributes style、伪元素规则和关键帧进入全局 `definitionCatalog`，元素仅保存有序定义 ID 和继承层级。规则定义通过 `conditionIds` 连接全局 `conditionCatalog`，保留 media/container/supports/scope 条件、当前活动结果、stylesheet 与 source range；目录仍完整保留 selector、声明值、`!important` 与 origin；无法解析的节点写入 `resolutionStatus=unresolved` 与错误原因；
- `document.html`、`frames.json`、`frame-*.html`：运行时克隆 HTML；
- `cdp-dom-tree.json`、`dom-snapshot.json`：固定 CDP DOM 证据；
- `maximum-template.css`：当前 document 与可访问 iframe 的 CSSOM 静态规则；
- `scripts/`、`event-registry.json`：已解析脚本源码和 window/document 事件监听接口；
- `resource-container-map.json`：外部资源的原始值、解析 URL、来源类型、应用属性、元素 XPath 与直接 DOM 容器；覆盖 `currentSrc/srcset`、SVG 引用、CSS background/mask/content/cursor/list-style/border-image、伪元素、开放 Shadow DOM 和可访问 iframe；
- `request-dom-application-map.json`：fetch/XHR 申请模块、调用栈摘要、完成状态，以及请求活动期或完成后 2.5 秒关联窗口内发生变化的呈现容器；
- `request-dom-assignment-tasks.json`：仅收集 `unknown` 请求，状态为 `pending-user-assignment`，留待下一阶段由用户指定文档或 JavaScript 控件容器；
- `page.mhtml`：固定 `Page.captureSnapshot` 页面归档；
- `runtime-evidence-manifest.json`：证据包版本、生成时间和文件清单。

任一必需步骤失败时，聚合 API 直接抛出异常，不生成可供宿主误标记的成功结果。

### CSS 双源状态恢复证据

页面克隆不得只使用其中一个来源。Runtime 分别保存两源，消费方必须按元素路径综合：

- 运行态结果源由 `complete-dom-properties.json` 与 `layout-state.json` 组成：前者是浏览器完成级联和布局后的最终事实，后者保存父布局、容器、viewport、媒体条件、活动伪类与动画时间点；
- 原始定义源由 `style-provenance.json` 与 `maximum-template.css` 组成：前者逐元素连接匹配规则、inline、继承、伪元素和关键帧，后者保存规则原文并由 `styleSheetId` 与 source range 定位；
- `auto/%/fr/minmax()/calc()/var()` 等原始语义来自定义源，其本次生效条件和最终结果来自运行态结果源；消费方必须形成统一的逐元素模型，不能分别择一使用；
- CDP 返回的匹配规则不是 Runtime 推测出的“唯一赢家”。转换器应结合规则顺序、origin、specificity、`!important`、inline/attributes、继承、媒体/容器条件、布局关系和最终计算值确定 XAML 映射；应优先保留自动布局语义，绝对值只用于校准。无法对等翻译的属性必须进入转换统计，不能静默丢弃。

如仅需补采逐元素来源，可调用固定入口 `WebRuntimePageEvidenceCapture.CaptureStyleProvenanceAsync`。该入口不接受 JavaScript 或 CDP 方法名，仅执行程序集内固定的 `DOM.getDocument` 与 `CSS.getMatchedStylesForNode` 流程。

真实动态页面的 CDP DOM 和逐节点匹配样式可能超过 `System.Text.Json` 默认的 64 层深度。固定证据入口对这两类浏览器原始 JSON 使用 2048 层只读上限；这是合法深层 DOM 的解析容量，不改变证据内容，也不对宿主开放通用 JSON、JavaScript 或 CDP 执行入口。消费方读取 `style-provenance.json` 时必须采用不低于证据端的对应上限。

来源采集按 4 个节点一批并发请求固定 CDP 接口，并在每批后写入进度。相同规则或样式按内容哈希只在 `definitionCatalog` 保存一次；元素引用保持浏览器返回顺序，因此去重不改变级联分析顺序。此结构避免继承链和公共规则随元素数重复膨胀，消费方必须先解析目录，再按 ID 还原逐元素来源。

`WebRuntimeResourceApplicationTracker` 必须在首次导航之前安装。它使用程序集内固定脚本包装 fetch/XHR 并观察 DOM 变化，单个 frame 最多保留 2048 个请求、每个请求最多保留 128 个容器，URL、栈和属性字符串也有长度上限。输出是可审计的时间关联线索，不等价于业务层绝对因果；同一时间窗中的并发请求可能指向同一个容器。DOM `src`/`href` 等直接引用标记为 `linked`，时间邻近容器只标记 `candidate`；没有找到时明确标记 `unknown`，并由 `CreateManualAssignmentTasks` 放进下一阶段任务清单，不令页面证据采集失败。后续宿主由 C# 加载和校验用户选择，XAML 不直接读取任务文件。开放 Shadow DOM 和可访问 iframe 分别记录，跨域 frame 只标记不可访问。

## HTTP 输入证据

`WebRuntimeNetworkEvidenceSession` 记录全部响应元数据以及固定 CDP `requestWillBeSent`、`responseReceived` 事件。默认 `PageReconstruction` 策略保存文本、CSS/JavaScript、JSON/XML、PDF/Office、`image/*`、SVG、PNG、JPEG、GIF、WebP、AVIF、ICO、BMP、WOFF/WOFF2、TTF、OTF 和 EOT。`AdditionalContentTypes` 与 `AdditionalExtensions` 用于增加站点专用类型；传入 `Kinds=None` 才会关闭默认正文保存。

正文来自 `CoreWebView2WebResourceResponseView.GetContentAsync()`，是浏览器交付给应用层的响应正文，不是 TLS 线上压缩字节。复制过程在写入期间执行 `MaximumBodyBytes` 硬上限，超限正文会删除临时文件并记为 `CaptureFailed`，不会先无界写满磁盘。每个快照响应引用明确写出方法、URL、状态、状态文本、去敏响应头、MIME、`bodyDisposition=Captured / SkippedByPolicy / CaptureFailed`、长度、SHA-256 和共享对象路径。

若 `GetContentAsync()` 对已观察响应返回不可用错误，Runtime 使用 `Network.responseReceived` 中同一 URL 的固定
`requestId` 调用内部 `Network.getResponseBody` 后备入口。该入口不接受宿主提供的方法名，仍受同一字节上限、SHA-256
和失败清单约束；`bodyCaptureSource` 明确区分 `web-resource-response` 与 `cdp-network.getResponseBody`。两者都是浏览器
应用响应正文，均不得描述为 TLS 线上压缩字节。

设置 `WebRuntimeNetworkEvidenceOptions.SharedStoreDirectory` 后，响应从会话启动时即写入独立公共库，与是否抓取快照无关；正文按 SHA-256 内容寻址并保存一次。单项正文读取失败记录在公共/快照清单的 `bodyFailures` 中，仍保存该响应元数据，不使 DOM/UI 快照失败；需要把正文失败升级为业务采集失败时，由宿主检查 `GetStatus().FailureCount` 或导出清单。

### WinUI 内存重建资源源

WinUI 的 WebView2 SDK 使用 `Microsoft.Web.WebView2.Core.Projection`，不得把它的 `CoreWebView2` 实例强制传给基于桌面 Core 程序集编译的会话。WinUI 宿主应在导航前创建 `WebRuntimeWinUiNetworkEvidenceSession`。该适配器直接订阅 WinUI 投影的 `WebResourceResponseReceived`，把页面重建需要的图像、字体、文本、CSS、脚本和结构化数据保存为不可变内存正文，并实现公共 `IWebRuntimeCapturedHttpBodySource`：

```csharp
await using var network =
    await WebRuntimeWinUiNetworkEvidenceSession.StartAsync(coreWebView2);

// 导航、点击和页面稳定检查。
await network.WaitForPendingBodiesAsync(TimeSpan.FromSeconds(30));

IWebRuntimeCapturedHttpBodySource resourceSource = network;
```

`TryReadCapturedBody(url, out body)` 只读取本次会话已经捕获的原始应用响应正文，按完整 URL（忽略 fragment）匹配，绝不发起第二次网络请求。DOM→XAML 重建应把该接口挂在 HTML 根资源连接上；`img`、字体和其他 UI 资源从这里创建 WinUI 对象。这样即使原 URL 带短期签名或随后失效，克隆仍使用页面实际显示时获得的同一份数据。URL、MIME、SHA-256 和捕获来源继续作为审计身份，运行时像素尺寸由真实 WinUI 解码结果读取。

多个页面快照应设置 `WebRuntimePageEvidenceOptions.NetworkExport`。快照阶段不重新抓取正文，而是从当前 document、Performance/DOM/CSS/伪元素资源 URL 和 fetch/XHR 跟踪记录判定页面使用的公共资源，写入正向引用并更新公共 `catalog.json` 的反向引用。`resource-container-map.json` 使用 `1.2` 架构，区分 `dom-attribute / responsive-image / responsive-image-candidate / css-computed / css-pseudo`，同时区分 `http / inline-data / blob / other`；HTTP URL 关联时忽略仅用于 SVG 符号选择的 fragment。`response-manifest.json:applications[]` 为每个引用输出 `linked / candidate / unknown`、容器和说明。Runtime 不以 Canvas、截图或重新编码替代 HTTP 正文。快照的完整性由引用集合表达，不要求在每个快照目录重复复制正文。未设置公共库时仍保留独立快照目录导出模式，供单次取证使用。

默认清单会删除 Cookie、Set-Cookie、Authorization、Token、Secret、API Key 和 WebSocket Key 等敏感头。正文仍可能包含账号隐私，因此输出目录必须是调用方明确选择的本机证据目录，不得直接进入源码或发布目录。

## 业务 API 与 CLI 共用命令面

业务程序直接持有 `WebRuntimeNetworkEvidenceSession`，使用 `GetStatus()`、`CreateCheckpoint()`、`ExportAsync()` 和 `DisposeAsync()`。如宿主同时提供 WebRuntime CLI 控制，把会话注册到现有控制器：

```csharp
controller.AttachNetworkEvidenceSession(network);
var result = await controller.TryExecuteNetworkEvidenceAsync(request, cancellationToken);
```

受控动作只有：

- `network.evidence.status`：返回响应数、正文数、字节数、跳过数、失败数、活动复制数和策略；
- `network.evidence.policy`：返回当前 `Kinds`、附加 MIME 与扩展名；
- `network.evidence.export`：使用 typed args `outputDirectory` 与可选 `afterSequence` 导出到宿主本机目录。

CLI v3 示例：

```powershell
iwrt web.network.evidence.status doubao-web doubao-web
iwrt web.network.evidence.policy doubao-web doubao-web
iwrt web.network.evidence.export doubao-web doubao-web C:\Evidence\doubao-http 0
```

这些命令经 Diagnostics Proxy 转发标准 Runtime Frame；CLI 不直接接触 `CoreWebView2`，也不能修改正文策略或提交任意 CDP/JavaScript。

## 边界

- 会话只能绑定一个 `CoreWebView2`，释放时解除事件订阅并关闭固定 Network domain。
- 使用公共资源库时 checkpoint 不切分公共正文；页面引用由当前页面实际出现的 URL 集合判定。checkpoint 仅保留给未启用公共库的独立导出模式。
- 页面分组、主/辅助关系、业务清单、DOM→XAML 和 WinUI 页面目录由调用项目负责。
- 需要业务匹配器、CLI 生命周期或 WebSocket/WebMessage 记录时，继续使用 [DATA_STREAM_MONITOR_RECORDER.md](DATA_STREAM_MONITOR_RECORDER.md)。

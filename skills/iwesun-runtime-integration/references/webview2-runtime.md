# WebView2 Runtime 能力

公共程序集 `Iwesun.Runtime.WebView2` 提供：

- `script.evaluate` / `eval`：受控页面脚本执行。
- `WebRuntimeScriptAuditLog`：脚本长度、结果和错误码审计，不记录脚本文本和 Cookie 值。
- `WebRuntimeNetworkRuleRegistry`：按后端、方法和 URL 规则阻断或替换响应。
- `WebRuntimeNetworkEvidenceSession`：在首次导航前记录 HTTP 元数据，并按公共正文策略保存 CSS/脚本、文档、图像、图标和字体的原始应用响应正文。
- `WebRuntimeNetworkEvidenceCommandDispatcher`：让业务宿主和 CLI 共用 `network.evidence.status / policy / export`。
- `WebRuntimeMonitorFilterRegistry`：按事件类型和 URL 过滤监控事件。
- `WebRuntimeTrackedCdpDomTreeSession`：以 CDP 事件维护 XPath、
  `nodeId/backendNodeId` 原子节点树。
- `WebRuntimeCdpDomAccess`：通过节点身份执行固定 CDP 读取、属性、聚焦、点击和高亮。
- `WebRuntimeHostController`：把网络决策和 XPath 证据请求转交给具体 WebView2 宿主。

可编译工程样例位于 `Iwesun.Runtime.WebView2.SampleHost`，完整说明见 `WEBVIEW2_SAMPLE_HOST.md`。样例演示 WebView2 初始化、`script.evaluate`、脚本审计和 `WebResourceRequested` 网络规则接线。

CLI 由 `RuntimeCliSystemConfig.json` 驱动。HTTP 证据命令为 `web.network.evidence.status <targetId> <backendId>`、`web.network.evidence.policy <targetId> <backendId>` 和 `web.network.evidence.export <targetId> <backendId> <host-local-directory> [afterSequence]`；它们通过 Diagnostics Proxy 转发标准 Runtime Frame。

接入顺序固定：在 `Navigate` 前调用 `WebRuntimeNetworkEvidenceSession.StartAsync`，设置明确的仓库外证据目录；再调用 `controller.AttachNetworkEvidenceSession(session)`。业务程序直接使用 `GetStatus/ExportAsync`，WebRuntime 命令处理器调用 `controller.TryExecuteNetworkEvidenceAsync(request)`。不要在消费项目重复订阅 `WebResourceResponseReceived` 保存同一批正文。

`StartAsync`按事务处理事件订阅、CDP Network启用和缓存策略；启动失败会自动反向清理，调用方不会得到半初始化Session。
释放Session会先停止新事件、等待在途正文复制，再在WebView2所属STA上下文恢复浏览器设置。
`WebRuntimeProgramHost.StopAsync`遇到单项停止失败或取消时保留失败项及其注册，后续调用继续补偿清理；
成功停止的项目不会重复停止。

默认 `WebRuntimeNetworkBodyKinds.PageReconstruction` 覆盖文本、结构化数据、文档、图像和字体。正文来自 WebView2 `GetContentAsync()`，属于浏览器应用响应正文，不是 TLS 线上压缩字节；快照响应引用保留状态文本和去敏响应头，清单必须检查 `bodyDisposition`、哈希和失败计数。

页面证据的 `resource-container-map.json` 1.2 同时保存 `currentSrc/srcset`、SVG 引用、CSS 与伪元素 URL、原始值、解析 URL、应用属性、XPath 和容器。`inline-data` 与 `blob` 不得冒充 HTTP 正文；禁止以 Canvas、截图或图片重新编码替代共享 HTTP 原字节。

业务宿主需要紧邻 Runtime 证据生成自己的结构化输入时，先调用固定的 `WebRuntimePageEvidenceCapture.StabilizePresentationAsync`；随后聚合采集可把字体等待和固定延迟设为零，避免两个来源之间重复等待。

宿主必须在 WebView2 所属 STA 线程调用脚本，在 `WebResourceRequested` 中应用网络决策；公共库不会自行创建浏览器窗口或保存业务 Cookie。

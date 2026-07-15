# WebView2 Runtime 能力

公共程序集 `Iwesun.Runtime.WebView2` 提供：

- `script.evaluate` / `eval`：受控页面脚本执行。
- `WebRuntimeScriptAuditLog`：脚本长度、结果和错误码审计，不记录脚本文本和 Cookie 值。
- `WebRuntimeNetworkRuleRegistry`：按后端、方法和 URL 规则阻断或替换响应。
- `WebRuntimeMonitorFilterRegistry`：按事件类型和 URL 过滤监控事件。
- `WebRuntimeEvidenceScripts`：XPath 高亮和清除高亮。
- `WebRuntimeHostController`：把网络决策和 XPath 证据请求转交给具体 WebView2 宿主。

可编译工程样例位于 `Iwesun.Runtime.WebView2.SampleHost`，完整说明见 `WEBVIEW2_SAMPLE_HOST.md`。样例演示 WebView2 初始化、`script.evaluate`、脚本审计和 `WebResourceRequested` 网络规则接线。

CLI 由 `RuntimeCliSystemConfig.json` 驱动，相关命令包括 `web.script.evaluate`、`web.script.audit`、`web.network.rule.add`、`web.network.rule.clear`、`web.monitor.filter.add`、`web.monitor.filter.clear` 和 `web.highlight`。

宿主必须在 WebView2 所属 STA 线程调用脚本，在 `WebResourceRequested` 中应用网络决策；公共库不会自行创建浏览器窗口或保存业务 Cookie。

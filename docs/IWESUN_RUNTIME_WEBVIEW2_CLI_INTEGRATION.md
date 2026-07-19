# Iwesun Runtime WebView2 / CLI 集成与发布说明

## 1. 权威配置

CLI 的命令路由唯一由 `Iwesun.Runtime.Cli/RuntimeCliSystemConfig.json` 定义，格式为 `iwesun.runtime.cli/3.0`。CLI 文本只是命令名，真正发送的是 `RuntimeDiagnosticFrame`：

```json
{
  "header": {
    "schema": "rtdiag/2.0",
    "frameType": "request",
    "category": "instruction",
    "operation": "invoke",
    "requestId": "req-1",
    "source": "runtime-cli",
    "destination": "Product.RuntimeDiagnostics"
  },
  "command": {
    "domain": "proxy",
    "target": "diagnostics.proxy",
    "action": "invoke",
    "args": {
      "targetId": "doubao-web",
      "proxyAction": "script.evaluate",
      "script": "JSON.stringify({url:location.href})",
      "module": "WebRuntime",
      "pipe": "WebRuntime"
    }
  }
}
```

所有 JSON 通过 4 字节 little-endian 长度前缀传输，正文为 UTF-8。禁止重新引入旧的 `schema/module/success/error` 私有 Envelope。

## 2. 新增 CLI 指令

系统配置已包含：

| CLI 命令 | Runtime 动作 |
| --- | --- |
| `web.script.evaluate` | `script.evaluate` |
| `web.script.audit` | `script.audit.list` |
| `web.network.rule.add` | `network.rule.add` |
| `web.network.rule.clear` | `network.rule.clear` |
| `web.monitor.filter.add` | `monitor.filter.add` |
| `web.monitor.filter.clear` | `monitor.filter.clear` |
| `web.highlight` | `highlightXPath` |
| `web.data-recorder.create` | `data.recorder.create` |
| `web.data-recorder.start` | `data.recorder.start` |
| `web.data-recorder.status/list` | `data.recorder.status/list` |
| `web.data-recorder.update` | `data.recorder.update` |
| `web.data-recorder.stop/delete` | `data.recorder.stop/delete` |
| `web.data-recorder.events` | `data.recorder.events` |

用户可以复制 `RuntimeCliUserConfig.example.json` 为 `RuntimeCliUserConfig.json`，增加别名或组合命令；系统配置和用户增量配置都必须保持 `iwesun.runtime.cli/3.0`。

## 3. 公共源码 API

公共程序集为 `Iwesun.Runtime.WebView2.dll`：

- `WebRuntimeScriptDispatcher`：受控脚本执行，最大 256 KiB。
- `WebRuntimeScriptAuditLog`：记录动作、长度、结果和错误码，不记录脚本正文或 Cookie 值。
- `WebRuntimeNetworkRuleRegistry`：阻断/固定响应规则。
- `WebRuntimeMonitorFilterRegistry`：监控过滤器。
- `WebRuntimeEvidenceScripts`：XPath 高亮和清理。
- `WebRuntimeHostController` / `IWebRuntimeHostAdapter`：宿主接线。
- `IDataStreamRecorderManager` / `DataStreamRecorderManager`：原始请求/响应数据记录、业务委托、状态和事件。

宿主在 WebView2 STA 线程调用脚本，在 `WebResourceRequested` 事件中应用网络决策。公共库不创建窗口、不保存业务 Cookie、不自行维护业务管道。

## 4. 源码示例项目

参考：

- `Iwesun.Runtime.SampleHost`：Runtime 启动、管道和 CLI 示例。
- `Iwesun.Runtime.WebView2`：公共 WebView2 契约和实现。
- `Iwesun.Runtime.FunctionalTests/WebRuntimeScriptScenario.cs`：脚本、审计、网络规则、监控过滤和宿主决策测试。
- `skills/iwesun-runtime-integration/references/webview2-runtime.md`：技能使用说明。

## 5. 发布包含关系

`Iwesun.Runtime.Release` 从当前源码重新生成：

- CLI 可执行文件和系统/用户配置样例。
- Diagnostics、Data、WebView2 公共 DLL。
- 使用手册、JSON/CLI/WebView2 文档。
- SampleHost 模板和源码。
- `iwesun-runtime-integration` 技能目录。

WiX `Iwesun.Runtime.Setup` 只消费 staging：

- `artifacts/release/Iwesun.Runtime/app` → `C:\Program Files\Iwesun\Runtime`
- `artifacts/release/Iwesun.Runtime/data` → `C:\ProgramData\Iwesun\Runtime`

安装包注册 `iwrt.exe` 所在目录到系统 PATH，并使用 MajorUpgrade 支持版本升级。

## 6. 统一发布命令

版本步进后只执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\release\build-runtime-setup.ps1 -ProductVersion 1.0.26
```

该脚本依次执行 Debug/Release 全量编译、完整 staging、安装前自检、WiX Rebuild，并输出 MSI 路径和 SHA-256。

1.0.26 在构建前先执行 Debug、Release 和 Setup clean；正式 MSI 只在明确发布时调用该唯一入口。数据记录器完整接口见 `Iwesun.Runtime.WebView2/docs/DATA_STREAM_MONITOR_RECORDER.md`，升级和回退见 `WEBVIEW2_1.0.26_UPGRADE.md`。

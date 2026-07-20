# Iwesun.Runtime.WebView2 发布状态

> 当前安装版仍为 Runtime 1.0.29。2026-07-20 已形成 1.0.30 页面与 HTTP 输入证据 API 源码候选，但 staging 与 MSI 尚未重建。候选说明见 [WEBVIEW2_1.0.30_EVIDENCE_RELEASE.md](WEBVIEW2_1.0.30_EVIDENCE_RELEASE.md)。

## 1.0.30 源码候选（未进入安装包）

- 新增 `WebRuntimeNetworkEvidenceSession`，在导航前记录全部请求/响应元数据和固定 CDP Network 事件；
- 对 JSON、文本、Markdown、HTML、XML、PDF 和 Office 响应保存正文、长度和 SHA-256；
- 新增 `WebRuntimePageEvidenceCapture`，统一生成 DOM 真快照、完整计算样式、CSS、脚本、事件、资源容器、CDP DOM、DOMSnapshot 和 MHTML；
- 敏感头默认清除，正文失败或等待超时明确失败；
- DoubaoUIClone 已迁移为公共 API 消费方并删除重复抓取器；
- 外部文档/JSON 采用 `WebRuntimeSharedHttpEvidenceStore` 公共资源池，正文按 SHA-256 去重，快照与资源维护双向引用；
- 新增导航前固定安装的 `WebRuntimeResourceApplicationTracker`，用有界时间窗口记录 fetch/XHR 申请模块与后续 DOM 呈现容器关联；
- 当前只宣称源码候选，1.0.29 安装包中不包含这些 API 和新文档。

## 1.0.26 功能线（已纳入 1.0.29）

- 完整 DOM 真快照 `CaptureAsync`、`RestoreAsync`、`RestoreAndLinkAsync`；
- 公共数据流记录器 `IDataStreamRecorderManager` 和 `DataStreamRecorderManager`；
- 通用复合条件、命名/直接委托、请求响应交换和安全文件输出；
- 八条 `web.data-recorder.*` CLI v3 命令及显式帮助元数据；
- `DATA_STREAM_MONITOR_RECORDER.md`、`DOM_SNAPSHOT_API.md` 和 `WEBVIEW2_1.0.26_UPGRADE.md`；
- SampleHost 的 `DataStreamRecorderSample.cs` 可编译业务接线样例。

## 既有 WebView2 能力

- Iwesun.Runtime.WebView2.dll；当前统一包要求 FileVersion 为 1.0.29.0；
- 与 Diagnostics 共用 RuntimeDiagnosticFrame；
- WebRuntime 公共会话、程序登记、虚拟鼠标、虚拟键盘和输入分发接口；
- CLI v3 的 WebRuntime capabilities、status、execute、cancel、snapshot、navigate、虚拟输入、受控脚本、网络规则、监控过滤和 XPath 高亮命令；
- 受控脚本长度限制与内存审计、网络决策注册表、监控过滤注册表和宿主适配接口；
- 可编译的 WPF WebView2 SampleHost 源码与发布程序；
- WebView2 DLL 与 Diagnostics、Data、CLI 使用同一 ProductVersion 和 Git 构建标识；
- WEB_RUNTIME_CONTROL.md、WEBVIEW2_RUNTIME_CAPABILITIES.md、WEBVIEW2_JSON_PIPE_CLI_PLAN.md 和 SCRIPT_REFLECTION_PLAN.md。

## 发布前必须验证

- Debug/Release 全解决方案构建成功；
- WebView2 DLL 进入全新 staging，且文件版本与 Diagnostics 一致；
- CLI catalog 与 metadata 命令数量一致，八条数据记录器命令均可显示帮助；
- 数据记录器生命周期、停止后不再写入和安全路径行为通过；
- `web-runtime-script` 和完整功能场景通过；
- 安装目录自检包含数据记录器文档、升级说明和样例源码。

## 本次验证结果

- Runtime 全解决方案 Debug/Release 均为 0 警告、0 错误；
- Debug 26 个、Release 21 个功能场景全部通过；
- CLI catalog 与 metadata 均为 73 项，八条数据记录器命令的源码配置和 staging 帮助查询均通过；
- 数据记录器创建、启动、匹配、请求/响应 sidecar、manifest、停止及删除生命周期通过；
- 1.0.29 干净 staging 的版本、程序集哈希、Debug/Release SampleHost、文档、配置和命令自检通过；
- Runtime Debug 26/26、Release 21/21 完整功能场景通过，其中包含数据流记录器与 WebRuntime 脚本场景；
- WebView2 不单独生成 MSI，已随 Runtime 1.0.29 全量安装包完成 Rebuild。

## 尚未宣称完成的实机验证

- 具体消费宿主中的 WebView2 初始化失败反向回滚；
- 单个业务 Program 停止失败时其余 Program 的继续清理；
- WebView2 执行过程中 Stop、取消和超时组合；
- Ambient 鼠标/键盘任务反复启停及 pulse 异常注入；
- 四宿主加载 1.0.24 后的联合生命周期测试。

这些项目需要消费宿主、真实 WebView2 会话和可控故障注入环境。当前发布准备不把它们伪写为已通过；发现问题后由使用方提交证据，Runtime 仓库统一修正公共库。

## 文档阅读顺序

1. 本文：确认当前发布和验证状态；
2. WEB_RUNTIME_CONTROL.md：公共 API 和宿主接入；
3. WEBVIEW2_RUNTIME_CAPABILITIES.md：能力边界；
4. WEBVIEW2_JSON_PIPE_CLI_PLAN.md：JSON、管道和 CLI 协议；
5. SCRIPT_REFLECTION_PLAN.md：受控脚本、反射边界及相关实现状态；
6. DOM_SNAPSHOT_API.md：完整 DOM 快照；
7. DATA_STREAM_MONITOR_RECORDER.md：数据流记录器 API、JSON 和 CLI；
8. WEBVIEW2_1.0.26_UPGRADE.md：升级、配置兼容与回退；
9. FULL_PAGE_EVIDENCE_API.md：完整页面与 HTTP 输入证据公共 API；
10. WEBVIEW2_1.0.30_EVIDENCE_RELEASE.md：源码候选发布范围、兼容边界和正式发布清单；
11. 安全审计报告、答复报告、完整整改方案：历史问题、设计依据及后续测试清单。

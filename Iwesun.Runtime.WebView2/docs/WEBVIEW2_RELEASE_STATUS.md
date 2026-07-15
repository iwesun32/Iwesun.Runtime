# Iwesun.Runtime.WebView2 1.0.22 发布状态

> 本文是安装包中 WebView2 当前发布状态的权威入口。安全审计报告、答复报告和完整整改方案中的未勾选清单保留为历史审计与后续测试项目，不代表所有条目都属于 1.0.22 发布阻断项。

## 已随 1.0.22 交付

- Iwesun.Runtime.WebView2.dll，FileVersion 1.0.22.0；
- 与 Diagnostics 共用 RuntimeDiagnosticFrame；
- WebRuntime 公共会话、程序登记、虚拟鼠标、虚拟键盘和输入分发接口；
- CLI v3 的 WebRuntime capabilities、status、execute、cancel、snapshot、navigate、虚拟输入、受控脚本、网络规则、监控过滤和 XPath 高亮命令；
- 受控脚本长度限制与内存审计、网络决策注册表、监控过滤注册表和宿主适配接口；
- 可编译的 WPF WebView2 SampleHost 源码与发布程序；
- WebView2 DLL 与 Diagnostics、Data、CLI 使用同一 ProductVersion 和 Git 构建标识；
- WEB_RUNTIME_CONTROL.md、WEBVIEW2_RUNTIME_CAPABILITIES.md 和 WEBVIEW2_JSON_PIPE_CLI_PLAN.md。

## 本次已经验证

- Debug/Release 全解决方案构建成功；
- WebView2 DLL 已进入统一发布树和 MSI；
- WebView2 DLL 文件版本与 Diagnostics 一致；
- WebView2 正式使用文档完整；
- CLI catalog 中 WebRuntime 命令能够加载并显示帮助；
- `web-runtime-script` 功能场景通过脚本、审计、网络规则、过滤、高亮及宿主适配检查；
- 安装目录自检通过。

## 尚未宣称完成的实机验证

- 具体消费宿主中的 WebView2 初始化失败反向回滚；
- 单个业务 Program 停止失败时其余 Program 的继续清理；
- WebView2 执行过程中 Stop、取消和超时组合；
- Ambient 鼠标/键盘任务反复启停及 pulse 异常注入；
- 四宿主加载 1.0.22 后的联合生命周期测试。

这些项目需要消费宿主、真实 WebView2 会话和可控故障注入环境。1.0.22 不把它们伪写为已通过；发现问题后由使用方提交证据，Runtime 仓库统一修正公共库。

## 文档阅读顺序

1. 本文：确认当前发布和验证状态；
2. WEB_RUNTIME_CONTROL.md：公共 API 和宿主接入；
3. WEBVIEW2_RUNTIME_CAPABILITIES.md：能力边界；
4. WEBVIEW2_JSON_PIPE_CLI_PLAN.md：JSON、管道和 CLI 协议；
5. 安全审计报告、答复报告、完整整改方案：历史问题、设计依据及后续测试清单。

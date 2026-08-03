# Iwesun.Runtime.WebView2 1.0.30 页面证据 API 源码候选说明

> 状态：源码候选已就绪；Runtime 1.0.30 staging 与 MSI 尚未重建。当前已发布安装包仍为 1.0.29，本文件不得被解释为安装版已经升级。

## 发布目标

本候选版把完整 DOM 快照、页面静态证据和 HTTP 输入正文的 `CoreWebView2` 接线统一放入 `Iwesun.Runtime.WebView2`。消费宿主不再复制网络响应监听、CDP DOM、CSS、脚本和 MHTML 抓取实现。

## 新增公共 API

- `WebRuntimeNetworkEvidenceSession`：在导航前订阅响应和固定 Network CDP 事件，按 checkpoint 导出请求/响应清单与结构化正文；
- `WebRuntimeNetworkEvidenceOptions`：正文大小、等待超时和启动即写入的公共资源库目录；
- `WebRuntimeNetworkEvidenceCheckpoint`：连续浏览会话中的证据切片点；
- `WebRuntimePageEvidenceCapture`：一次生成 DOM、计算样式、CSS、脚本、事件、资源容器、CDP DOM、MHTML 和 HTTP 证据包；
- `WebRuntimePageEvidenceResult`：成功完成后的输出目录和采集时间。
- `WebRuntimeSharedHttpEvidenceStore`：按正文哈希去重并维护快照正向引用、资源反向引用的公共 HTTP 输入资源管理器；
- `WebRuntimeSharedHttpResourceInput` / `WebRuntimeSharedHttpReference`：共享资源注册和引用契约。
- `WebRuntimeResourceApplicationTracker`：导航前安装固定 fetch/XHR 跟踪器，导出申请模块、完成状态与响应后 DOM 呈现容器的有界时间关联证据。

详细契约见 [FULL_PAGE_EVIDENCE_API.md](FULL_PAGE_EVIDENCE_API.md)。

## 安全与兼容

- 公共入口不接收 JavaScript 表达式或 CDP 方法名，仅执行程序集内固定、可审计脚本；
- Cookie、Set-Cookie、Authorization、Token、Secret、API Key 和 WebSocket Key 默认不进入证据清单；
- 单项正文保存失败或超限会记录 `bodyFailures` 并保留响应元数据，不再中止已经成功的 DOM/UI 快照；
- 项目继续以 `net10.0` 为目标，避免破坏 Runtime CLI、功能测试和发布器引用链；
- WebView2 Core 编译引用由 NuGet 固定版本提供，但不把 WPF 传递资产扩散给非 Windows Runtime 项目；实际宿主仍需正常引用 WebView2 包。

## 消费项目迁移

DoubaoUIClone 已删除本地 HTTP 证据记录器和完整页面采集实现。业务项目只保留快捷键、页面归组、主/辅助版本、DOM→XAML、状态分析和 WinUI 页面目录。

外部文档/JSON 从宿主启动时持续进入公共资源库，不再按主快照复制。每个主/辅助证据包仅根据本页 URL 写资源引用和 `linked / candidate / unknown` 容器映射；公共目录保存唯一正文和资源到快照的反向引用，网络批次不参与 UI 主锚点判定。

## 源码验证

- Runtime WebView2 Debug：0 警告、0 错误；
- Runtime 全解决方案 Debug/Release：0 警告、0 错误；
- DoubaoUIClone Debug/Release：0 警告、0 错误；
- DoubaoUIClone 测试：34/34 通过；
- DoubaoUIClone App Release `win-x64`：0 警告、0 错误；
- Runtime 发布项目已包含两份新文档，安装自检已把两份文档列为必需项。
- Runtime `shared-http-evidence` Debug/Release 功能场景通过启动即保存且零引用、稳定记录 ID、SHA-256 正文去重和快照双向引用验证。
- Runtime `request-dom-application` Debug/Release 功能场景验证请求—容器证据契约、`unknown` 转交用户指定任务以及错误 schema 拒绝行为。
- Runtime 全功能场景 Debug 28/28、Release 23/23 通过。

上述源码级发布检查均已完成。未经用户明确发布指令，仍不重建 staging、MSI，不覆盖 1.0.29 安装包。

## 进入正式发布的清单

1. Runtime Debug/Release 全解决方案均为 0 警告、0 错误；
2. DoubaoUIClone Debug/Release、测试和 WinUI `win-x64` 构建通过；
3. 新文档进入 `ReleaseDocs`，安装自检要求其存在；
4. 从空 staging 构建 1.0.30，核验 DLL 文件版本、源码样例和相对链接；
5. 仅在明确发布授权后执行 MSI Rebuild，并更新哈希和安装实测记录。

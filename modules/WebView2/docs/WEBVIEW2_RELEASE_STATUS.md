# Iwesun.Runtime.WebView2 发布状态

> 2026-07-30 导航后树恢复修正正在源码验证：实时单节点操作改为
> `DOM.performSearch/getSearchResults/describeNode` 直查浏览器当前 DOM，不再把辅助树缓存
> 当作实时节点来源；完整树继续服务批量采集。跟踪会话新增
> `Page.frameNavigated/Page.navigatedWithinDocument` 导航失效、显式刷新，以及失效 nodeId
> 刷新后单次重试。失效树在新 revision 原子发布前不再通过 `Current` 暴露；批量证据读取
> 同时拒绝跨 revision 拼接。WebView2 合同 Debug/Release 各 34/34。本轮尚未生成、安装或
> 声明新的发布候选。
>
> 2026-07-30 未发布源码已进入 CDP DOM 节点树迁移：XPath 仅作树定位，正式元素访问统一
> 使用当前 revision 的 `nodeId/backendNodeId` 和固定 CDP 方法。该改造尚未进入既有安装包，
> 详见 [CDP_DOM_NODE_TREE.md](CDP_DOM_NODE_TREE.md)。
> WebView2 平台合同 Debug/Release 各 25/25；拆回 Web 的 HTML/XAML 组合合同
> Debug/Release 各 305/305，均零失败。WebView2 领域 `.slnx`、项目和测试均不再引用
> Web 调试项目；依赖方向固定为 Web 单向使用 WebView2。本轮源码仍未生成新发布候选。
>
> 2026-07-30 二次实机修正：单纯滚动后读取 box 中心仍可能命中滚动裁剪区或覆盖层。
> `WebRuntimeCdpDomAccess.ClickAsync` 现改为可见 content quad 视口裁剪、中心/内缩点采样、
> 鼠标移动前后两次 `DOM.getNodeForLocation` 命中证明，再派发按下/释放；命中祖先或其他
> 节点时明确失败，不再返回伪成功。调用方继续使用原始精确 XPath，禁止改写成父节点或
> 回退脚本点击。WinUI 3 Projection 宿主使用薄
> `IWebRuntimeDevToolsSession` 适配器，不能同时引用 Core 与 Core.Projection 两套程序集。
>
> Runtime 1.0.40-beta.1 已安装，但不包含本次二次实机修正；当前修正冻结到新的不可覆盖
> Runtime 1.0.41-beta.1 候选。Web 项目和程序集仍不进入发布载荷。

## 1.0.36-beta.1：CSS 双源、HTTP 页面资源证据与生命周期加固

- `WebRuntimePageEvidenceCapture` 已在源码中增加 `style-provenance.json`，逐元素保存 CDP 匹配规则、stylesheet header/source URL、source range、inline/attributes style、继承、伪元素和关键帧；
- 新增 `layout-state.json`，保存页面媒体环境、条件规则结果、布局父级/Shadow/slot 关系、自动布局语义、活动伪类、滚动与动画时间点；
- `complete-dom-properties.json` + `layout-state.json` 形成运行态结果源，`style-provenance.json` + `maximum-template.css` 形成原始定义源；两源通过相同 DOM 路径及 CDP 节点标识连接，由消费方综合使用；
- 页面证据 manifest 源码 schema 提升到 `iwesun.webview2.page-evidence/1.2`；
- 新增固定公共入口 `CaptureStyleProvenanceAsync`，不开放任意 JavaScript 或任意 CDP 转发；
- 修复真实动态页面 DOM 深度超过 `System.Text.Json` 默认 64 层时原始样式来源采集失败的问题；CDP DOM 与匹配样式采用明确的 2048 层只读解析上限，并要求消费方保持对应读取能力；
- `style-provenance.json` 提升到紧凑 `1.2` 架构：共享规则、样式、伪元素和关键帧写入全局定义目录，元素保存有序引用；规则定义以稳定 `conditionIds` 连接 media/container/supports/scope 条件目录；固定 CDP 请求按 4 节点批处理，避免数千元素页面产生 GB 级重复文件和长时间串行等待；
- `WebRuntimeNetworkEvidenceSession` 新增公共正文策略，默认保存页面复现需要的 CSS/脚本、结构化数据、文档、图像/图标和字体；清单区分 `Captured / SkippedByPolicy / CaptureFailed`，复制期间执行单项字节硬上限；
- 新增 `network.evidence.status / policy / export` 公共命令分发器、CLI v3 路由、帮助元数据和 SampleHost 接线；
- 当前候选要求Runtime全解决方案Debug/Release、WebView2/CLI构建和完整FunctionalTests回归通过；
  `shared-http-evidence`覆盖CSS、SVG、WebP、WOFF2、附加MIME、状态文本/去敏响应头、内容寻址去重和
  `bodyDisposition`。真实WebView2页面采集仍是正式版门禁，不阻止普通β联调。
- `WebRuntimeNetworkEvidenceSession.StartAsync`现在把订阅、CDP启用和缓存策略视为一个启动事务；
  任一阶段失败都会反向解除事件并恢复已经启用的浏览器设置。释放会先停止新事件、等待在途正文复制，
  再在WebView2所属上下文恢复设置。
- `WebRuntimeProgramHost.StopAsync`只移除已经成功停止的生命周期对象；失败或取消对象保留注册，
  后续调用可以执行补偿停止，成功后再统一释放注册。

## 当前正式能力

- `WebRuntimeNetworkEvidenceSession` 在导航前记录请求/响应元数据和固定 CDP Network 事件；
- `WebRuntimePageEvidenceCapture` 正式版生成 DOM 真快照、计算样式、CSS、脚本、事件、资源容器、CDP DOM、DOMSnapshot 和 MHTML；上述 CSS 双源状态恢复证据属于当前未发布源码扩展；
- `WebRuntimeSharedHttpEvidenceStore` 按 SHA-256 去重保存允许落盘的 HTTP 正文，并维护快照正向引用和资源反向引用；
- `WebRuntimeResourceApplicationTracker` 有界记录 fetch/XHR 与后续 DOM 呈现容器的 `linked`、`candidate`、`unknown` 关系；
- `IDataStreamRecorderManager` 提供复合条件、命名/直接委托、请求响应交换、安全文件输出和完整生命周期；
- WebRuntime 公共会话、程序登记、虚拟鼠标、虚拟键盘、输入分发、受控脚本、网络规则、监控过滤和 XPath 证据能力保持可用；
- CLI v3 提供对应配置、帮助元数据和 Frame 命令入口；
- 敏感头默认清除，正文读取失败、超限或等待超时均返回明确失败信息，不产生伪成功。

## 1.0.31 发布验证

- Runtime 全解决方案 Debug/Release 构建均为 0 警告、0 错误；
- Debug 与 Release 完整功能场景全部通过，其中包含 WebRuntime 脚本、共享 HTTP 证据、请求到 DOM 应用跟踪和数据流记录器；
- WebView2 DLL、Diagnostics、Data、CLI 和 RemoteConsole 的 Runtime 文件版本统一为 `1.0.31.0`；
- 全新 staging、自检、安装版 SampleHost Debug/Release 引用与 DLL 哈希验证通过；
- Runtime 1.0.31 MSI 已完成强制 Rebuild；安装树不包含旧 `Iwesun.Data.dll`、DList 或 RecordStore V1；
- 历史候选说明、升级说明和安全审计材料已移入 `docs/archive/`，不再进入活动发布载荷。

## 仍需真实宿主验证的边界

- 真实 WebView2 STA/UI 线程上的完整页面证据连续采集与导航成功确认；
- 具体消费宿主中的初始化失败回滚、Stop/取消/超时组合和重复启停；
- 业务 Program 单项停止失败时其余 Program 的继续清理；
- 长期运行下的证据资源池容量、正文复制失败和浏览器退出竞态。

这些项目需要真实 WebView2 会话和可控故障注入环境。当前发布不把未执行的实机项目描述为已通过。

## 当前文档阅读顺序

1. `WEB_RUNTIME_CONTROL.md`：公共 API 和宿主接入；
2. `WEBVIEW2_RUNTIME_CAPABILITIES.md`：能力边界；
3. `WEBVIEW2_JSON_PIPE_CLI_PLAN.md`：JSON、管道和 CLI 协议；
4. `SCRIPT_REFLECTION_PLAN.md`：受控脚本与反射边界；
5. `DOM_SNAPSHOT_API.md`：完整 DOM 快照；
6. `DATA_STREAM_MONITOR_RECORDER.md`：数据流记录器；
7. `FULL_PAGE_EVIDENCE_API.md`：完整页面与 HTTP 输入证据；
8. `CDP_DOM_NODE_TREE.md`：节点树、事件刷新和 NodeId 访问迁移；
9. `WEBVIEW2_SAMPLE_HOST.md`：可编译宿主样例。

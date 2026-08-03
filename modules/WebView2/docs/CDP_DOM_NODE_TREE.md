# WebView2 CDP DOM 节点树

## 1. 目标

WebView2 正式 DOM 访问分为实时单节点和批量树快照两条链路：

```text
实时操作：调用方 XPath
  -> DOM.performSearch / DOM.getSearchResults
  -> 浏览器当前 DOM 的 nodeId/backendNodeId
  -> 与当前树 revision 和 document scope 核对
  -> 固定 CDP 方法
  -> 强类型结果

批量采集：DOM.getDocument(depth=-1, pierce=true)
  -> Runtime 内存节点树
  -> XPath/nodeId/backendNodeId 辅助索引
  -> 一次性批量证据读取
```

XPath 是稳定的公共定位条件，不交给页面 JavaScript 执行。实时操作每次从浏览器当前 DOM
取得节点，不依赖辅助树是否已经收到全部结构事件。`nodeId` 和
`backendNodeId` 是浏览器当前文档 revision 内的执行身份，不能跨导航或整树重建复用。

受控 `script.evaluate` 继续作为用户明确请求的独立调试能力保留，但 Runtime 的正式 DOM
读取、属性修改、聚焦、点击和高亮不会把它作为隐式实现或后备路径。

## 2. 对象模型

`WebRuntimeTrackedCdpDomTreeSession` 持有当前页面的原子树版本。每个
`WebRuntimeCdpDomTreeNode` 包含：

- `DocumentScope` 与绝对 `XPath`；
- `NodeId` 与 `BackendNodeId`；
- 标签名称和属性；
- 父 XPath、有序子 XPath。

Runtime 首先建立带直接 `Parent/Children` 对象引用的节点树，完整验证父子双向关系、顺序和
节点身份；只有树验证通过后，`WebRuntimeCdpDomTreeIndex` 才从树派生 XPath、`nodeId`、
`backendNodeId` 三个辅助索引。树是主体，字典可以重建，不能反过来用字典冒充树。三个索引
只允许与同一个树 revision 一次发布，外部不会看到部分更新状态。

批量树收到 XPath 时，`ResolveXPathAsync` 从当前文档根开始沿有序子树逐段导航。不存在、歧义、
文档作用域错误或 revision 已失效都返回明确失败，不调用 `document.evaluate`。
规范绝对 XPath 直接通过不可变字典 O(1) 返回节点；兼容 XPath 首次树导航成功后写入当前
revision 的并发查询缓存，后续同一“文档作用域 + XPath”也直接返回节点和 `nodeId`。整树
刷新会替换整个索引对象，因此旧 revision 的查询缓存不会进入新页面。

当前确定性 XPath 子集包括绝对子路径、起始后代轴 `//`、元素名或 `*`、正整数位置谓词、
属性存在谓词和属性等值谓词，例如 `/html/body/main`、`//button[2]`、
`//button[@id='send']`。函数、联合、父轴和任意中段后代轴会返回 `NotSupportedException`；
多节点结果会返回歧义错误，不会擅自选择第一个节点或退回脚本求值。

## 3. 初始化与刷新

正式宿主使用 `CoreWebView2DevToolsSession` 包装当前 `CoreWebView2`。初始化顺序固定为：

1. 在 WebView2 所属上下文建立 DOM 结构与属性事件接收器；
2. 启用 CDP `DOM` domain；
3. 调用 `DOM.getDocument(depth=-1, pierce=true)`；
4. 完整验证节点身份和关系；
5. 原子发布首个 revision。

初始化同时启用 CDP `Page` domain，并订阅 `Page.frameNavigated` 与
`Page.navigatedWithinDocument`。整页、iframe 和 SPA History API 导航都会立即使当前
revision 失效；失效状态下 `Current` 不再暴露旧树。属性增加、修改和删除可以安全映射到
现有节点时，Runtime 直接生成一个新 revision。节点
插入、删除、文本变化、Shadow DOM 变化、伪元素变化和 `documentUpdated` 会使当前树失效，
并触发整树刷新；下一个访问也会强制等待刷新完成。

如果事件恰好发生在整树读取期间，失效标志会保留并继续刷新，旧读取结果不能覆盖更新事件。
刷新失败保留最后异常并让树保持失效；后续访问会重新尝试，而不是继续使用陈旧 `nodeId`。
宿主和测试还可以显式调用 `Invalidate()` 与 `RefreshAsync()`；它们是事件缺口和外部导航
边界的补偿入口，不要求销毁整个会话。

## 4. 固定 CDP 操作

`WebRuntimeCdpDomAccess` 当前提供：

| 能力 | CDP 方法 |
| ---- | -------- |
| 外层 HTML | `DOM.getOuterHTML` |
| 属性读取 | `DOM.getAttributes` |
| 属性设置/删除 | `DOM.setAttributeValue` / `DOM.removeAttribute` |
| 聚焦 | `DOM.focus` |
| 高亮/清除 | `Overlay.highlightNode` / `Overlay.hideHighlight` |
| 点击 | `Page.bringToFront` + `DOM.scrollIntoViewIfNeeded` + `DOM.getContentQuads` + `Page.getLayoutMetrics` + `DOM.getNodeForLocation` + `Input.dispatchMouseEvent` |

每项实时操作先调用 `DOM.performSearch` 在当前浏览器 DOM 中解析 XPath。结果必须唯一，
随后通过 `DOM.getSearchResults` 取得 `nodeId`、通过 `DOM.describeNode` 取得
`backendNodeId`，并在 `finally` 中调用 `DOM.discardSearchResults`。Runtime 再核对
document scope、当前 URL 和树 revision；URL 或节点身份不一致时自动重建辅助树。

固定 CDP 操作如果明确报告 `nodeId` 已失效，Runtime 会强制刷新整树、重新执行同一 XPath
实时查询并只重试一次。第二次仍失败时返回原始终态，不无限重试，也不把其他 CDP、传输或
取消错误误判为节点失效。

点击把 content quad 裁剪到 CSS 视口，
只对 `DOM.getNodeForLocation` 证明命中目标节点或其后代的点派发鼠标输入；不在页面内构造
MouseEvent，也不调用元素的 JavaScript `click()`。

点击必须严格执行以下顺序：

1. 使用调用方提供的原始绝对 XPath，在浏览器当前 DOM 中直接解析唯一节点，并与当前树
   revision 核对；
2. 调用 `Page.bringToFront`，确保输入发送到当前页面 target；
3. 调用 `DOM.scrollIntoViewIfNeeded`，让滚动容器内的目标进入可命中区域；
4. 调用 `DOM.getContentQuads` 和 `Page.getLayoutMetrics`，把每个 quad 裁剪到 CSS 视口；
5. 从裁剪多边形中心和内缩采样点中选择候选，以 `DOM.getNodeForLocation` 验证实际命中；
6. 发送 `mouseMoved` 后再次命中验证，防止 hover 产生的覆盖层改变目标；
7. 只有两次命中均属于目标节点或其后代时，才发送带正确 `buttons` 状态的
   `mousePressed` 和 `mouseReleased`。

不得为了让点击“看起来成功”而把记录 XPath 改成父元素 XPath、选择相邻节点、选择首个
匹配节点或退回 JavaScript `click()`。节点不存在、XPath 歧义、滚动失败、无可见 quad、
命中祖先/兄弟/覆盖层都必须明确失败。结果同时返回输入坐标及命中的 nodeId/backendNodeId，
记录身份、实际 DOM 节点和交互证据始终指向同一个元素。

宿主截图桥接也不再接收裸 XPath。`WebRuntimeHostController` 先解析树，再把包含 revision、
XPath、`nodeId` 和 `backendNodeId` 的 `WebRuntimeDomNodeReference` 交给宿主。

## 5. STA 与资源生命周期

`CoreWebView2DevToolsSession` 记录创建时的 WebView2 所属同步上下文。事件刷新即使从异步
续体发起，CDP 方法仍回到该上下文执行。事件订阅必须在所属上下文创建。

`HtmlRuntimeWebView2Context.CreateCdp` 默认建立持续跟踪树，并公开 `CdpDomAccess`。
上下文实现 `IAsyncDisposable`；释放时取消后台刷新并反向解除所有 CDP DOM 事件订阅。
初始化中任一步失败也会反向清理已经建立的订阅。

## 6. 迁移

- 宿主把 `CoreWebView2` 包装为 `CoreWebView2DevToolsSession`。
- 使用 `HtmlRuntimeWebView2Context.CreateCdp`，不要再构造脚本 DOM reader。
- 原 `WebRuntimeEvidenceScripts.HighlightXPath/ClearHighlights` 已删除；改用
  `CdpDomAccess.HighlightAsync/HideHighlightAsync`。
- `IWebRuntimeHostAdapter.CaptureXPathAsync` 已替换为 `CaptureNodeAsync`。
- 自定义 `IWebRuntimeDevToolsSession` 必须实现 `SubscribeDevToolsProtocolEvent`；缺少事件
  能力时正式跟踪会话拒绝初始化，不降级为一次性快照。

### WinUI 3 Projection 边界

Runtime 自带的 `CoreWebView2DevToolsSession` 使用
`Microsoft.Web.WebView2.Core.dll` 中的 `CoreWebView2`。WinUI 3 宿主得到的对象可能来自
`Microsoft.Web.WebView2.Core.Projection.dll`；二者虽然类型全名相同，但程序集身份不同，
不能互相传参。

此类 WinUI 3 宿主只实现一个薄的 `IWebRuntimeDevToolsSession` 投影适配器：

- `CallDevToolsProtocolMethodAsync` 转发到 WinUI 所属的 `CoreWebView2`；
- `SubscribeDevToolsProtocolEvent` 在 WebView2 所属 UI 上下文建立和释放订阅；
- 适配器不得构建 DOM 树、解析 XPath、缓存 nodeId 或自行实现点击；
- 适配器传给 `HtmlRuntimeWebView2Context.CreateCdp` 后，节点树、revision、属性刷新和
  固定 DOM 操作全部由 Runtime 实现。

禁止为了调用 Runtime 自带适配器而同时显式引用 Core 与 Projection 两套程序集；这会产生
`CS0433` 同名类型冲突。

## 7. 当前边界

本模型负责元素定位和 DOM 操作。用户显式提交的脚本、页面语义插桩和证据采集是不同能力；
它们不得被 DOM 访问入口隐式调用。后续若某项正式元素 API 仍依赖脚本，应迁移到本节点树，
或明确从正式元素访问合同中移除。

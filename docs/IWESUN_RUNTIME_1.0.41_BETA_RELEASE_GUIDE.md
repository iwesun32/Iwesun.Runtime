# Iwesun Runtime 1.0.41-beta.1 普通 β 发布说明

> 状态：`GENERAL_BETA_READY_FORMAL_BLOCKED`  
> Runtime 产品版本：`1.0.41`  
> Runtime 程序集/文件版本：`1.0.41.0`  
> Runtime 产品信息版本：`1.0.41-beta.1`  
> Networks 产品信息版本：`3.0.0-beta.4`

本版修正 1.0.40 中“CDP 鼠标事件已发送，但滚动容器内的精确节点没有形成有效点击”的
实机问题。它是新的不可覆盖候选，不上传公共 NuGet 源。

## 1. 精确点击合同

- 调用方记录 XPath 只用于解析当前 revision 的唯一节点，不改写为父 `<a>` 或业务 URL。
- 点击依次激活页面、滚入视区、读取 content quads，并按 CSS 视口裁剪。
- 对裁剪多边形中心和内缩点逐项调用 `DOM.getNodeForLocation`。
- 只有实际命中目标节点或其后代的点才能进入鼠标移动；移动后再次验证命中，防止 hover
  覆盖层改变目标。
- 两次命中均成立后才派发 `mousePressed` 和 `mouseReleased`。
- 无可见 quad、命中祖先/兄弟/覆盖层、节点失效或滚动失败均明确失败，不返回伪成功，
  不使用 JavaScript `click()` 后备。
- 点击结果增加输入坐标和实际命中的 nodeId/backendNodeId，便于宿主记录交互证据。

WebView2 Debug/Release 合同各 25/25，覆盖顺序、滚动失败、视口裁剪、祖先误命中拒绝和
中心覆盖后的同一节点内采样。

## 2. 发布边界

发布 Diagnostics、Data、Networks、WebView2、CLI、RemoteConsole、SampleHost、配置、技能、
样例和文档。`modules/Web`、`Iwesun.Runtime.Web.dll`、PDB 和源码不进入安装载荷。

WebView2 不引用 Web；HTML/XAML 组合和 Web 对象映射继续由 Web 单向使用 WebView2。
WinUI 3 宿主继续通过薄 `IWebRuntimeDevToolsSession` 适配器接入。

## 3. 标准 DLL 布局

```text
lib\Iwesun.Runtime.Diagnostics\Debug|Release\Iwesun.Runtime.Diagnostics.dll
lib\Iwesun.Runtime.Data\Debug|Release\Iwesun.Runtime.Data.dll
lib\Iwesun.Runtime.Networks\Debug|Release\Iwesun.Runtime.Networks.dll
lib\Iwesun.Runtime.WebView2\Debug|Release\Iwesun.Runtime.WebView2.dll
```

各库顶层同名 DLL 为 Release 兼容入口。

## 4. β 实机重点

- 豆包历史会话内部精确 `div` 点击后，确认 `CoreWebView2.Source` 进入预期 `/chat/<id>`；
- 记录请求 XPath、解析 nodeId、实际 HitNodeId、输入坐标和页面结果；
- 覆盖嵌套滚动容器、部分裁剪、局部覆盖层和 hover 后 DOM 变化；
- 后续附件 XPath 只在会话导航成功后执行，避免把连锁失败误判为附件定位错误。

## 5. 交付目录

```text
artifacts\packages\Iwesun.Runtime.1.0.41-beta.1\
```

目录包含 MSI、便携 ZIP、Networks nupkg/snupkg、说明、清单和 SHA-256 清单。

## 6. 正式版阻塞项

- 豆包真实页面精确会话点击和附件链复验；
- WebView2 长时间 DOM 事件与 STA 压力；
- WFP 双真实网关并发隔离和完整网络恢复矩阵；
- 干净环境安装、升级、卸载和最终版本冻结。

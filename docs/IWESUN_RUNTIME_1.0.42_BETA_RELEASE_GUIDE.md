# Iwesun Runtime 1.0.42-beta.1 普通 β 发布说明

> 状态：`GENERAL_BETA_READY_FORMAL_BLOCKED`  
> Runtime 产品版本：`1.0.42`  
> Runtime 程序集/文件版本：`1.0.42.0`  
> Runtime 产品信息版本：`1.0.42-beta.1`  
> Networks 产品信息版本：`3.0.0-beta.4`

本版发布 1.0.41 之后完成的 WebView2 实时节点定位和导航恢复修正。它是新的不可覆盖
候选，不上传公共 NuGet 源。

## 1. WebView2 实时节点与导航恢复

- 点击、属性、聚焦和高亮等实时单节点操作通过 CDP `DOM.performSearch` 在浏览器当前
  DOM 中重新解析精确 XPath，不使用辅助树中的旧 nodeId，也不注入 JavaScript。
- 搜索结果必须唯一，并通过 `DOM.getSearchResults` 和 `DOM.describeNode` 核对当前
  document scope、URL、nodeId/backendNodeId 与树 revision。
- `DOM.discardSearchResults` 在成功和失败路径都执行，避免浏览器搜索结果泄漏。
- 跟踪树订阅 `Page.frameNavigated` 与 `Page.navigatedWithinDocument`，覆盖整页、
  iframe 和 SPA History API 导航。
- 导航或结构事件使旧树立即失效；新 revision 原子发布前不再通过 `Current` 暴露旧树。
- CDP 明确报告 nodeId 失效时，Runtime 强制刷新整树、按原 XPath 重新定位并只重试一次。
- 批量证据读取拒绝跨 revision 拼接，避免把导航前树和导航后 DOMSnapshot 混合。

WebView2 Debug/Release 合同各 34/34，覆盖实时搜索、搜索结果释放、导航失效、刷新竞态、
失效节点单次恢复和跨 revision 拒绝。

## 2. 发布边界

发布 Diagnostics、Data、Networks、WebView2、CLI、RemoteConsole、SampleHost、配置、技能、
样例和文档。`modules/Web`、`Iwesun.Runtime.Web.dll`、PDB 和源码不进入安装载荷。

WebView2 不引用 Web；HTML/XAML 组合和 Web 对象映射继续由 Web 单向使用 WebView2。

## 3. 标准 DLL 布局

```text
lib\Iwesun.Runtime.Diagnostics\Debug|Release\Iwesun.Runtime.Diagnostics.dll
lib\Iwesun.Runtime.Data\Debug|Release\Iwesun.Runtime.Data.dll
lib\Iwesun.Runtime.Networks\Debug|Release\Iwesun.Runtime.Networks.dll
lib\Iwesun.Runtime.WebView2\Debug|Release\Iwesun.Runtime.WebView2.dll
```

各库顶层同名 DLL 为 Release 兼容入口。

## 4. β 实机重点

- 豆包 SPA 会话导航后，旧树不得继续返回旧 nodeId；
- 后续附件 XPath 必须在当前 URL 和当前 document revision 中重新解析；
- 连续前进、后退、iframe 导航和 History API 导航后验证树刷新；
- 人为制造失效 nodeId，确认只恢复一次且传输异常不会被误判为节点失效；
- 长时间页面更新中观察订阅释放、刷新任务和搜索结果是否保持有界。

## 5. 交付目录

```text
artifacts\packages\Iwesun.Runtime.1.0.42-beta.1\
```

目录包含 MSI、便携 ZIP、Networks nupkg/snupkg、说明、清单和 SHA-256 清单。

## 6. 正式版阻塞项

- 豆包真实页面完整会话与附件链连续复验；
- WebView2 长时间 DOM 事件与 STA 压力；
- WFP 双真实网关并发隔离和完整网络恢复矩阵；
- 干净环境安装、升级、卸载和最终版本冻结。

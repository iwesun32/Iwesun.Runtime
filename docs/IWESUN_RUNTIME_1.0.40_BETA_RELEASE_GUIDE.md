# Iwesun Runtime 1.0.40-beta.1 普通 β 发布说明

> 状态：`GENERAL_BETA_READY_FORMAL_BLOCKED`  
> Runtime 产品版本：`1.0.40`  
> Runtime 程序集/文件版本：`1.0.40.0`  
> Runtime 产品信息版本：`1.0.40-beta.1`  
> Networks 产品信息版本：`3.0.0-beta.4`

本版用于现有消费者更新 Runtime 编译依赖和开展普通 β 测试。它不覆盖既有候选，
不上传公共 NuGet 源。

## 1. 发布边界

发布 Diagnostics、Data、Networks、WebView2、CLI、RemoteConsole、SampleHost、配置、技能、
样例和文档。`modules/Web` 仍处于界面与 HTML/XAML 对象模型调试期，不发布
`Iwesun.Runtime.Web.dll`、PDB、源码或独立库目录。

WebView2 只保留浏览器平台、CDP、节点树、网络、输入和公共控制能力。HTML/XAML 组合、
`HtmlRuntime*`、Web 对象映射和脚本 DOM 诊断归属 Web；依赖方向固定为 Web 单向使用 WebView2。

## 2. 精确 CDP 点击

- 调用方记录 XPath 只解析到当前 revision 的唯一节点，不替换为父节点、相邻节点或首项匹配。
- 固定顺序为 `DOM.scrollIntoViewIfNeeded`、`DOM.getBoxModel`、
  `mousePressed`、`mouseReleased`。
- 盒模型必须在滚动完成后读取，点击坐标取 content quad 中心。
- 滚动失败、盒模型缺失、XPath 不存在或歧义立即失败，不派发后续鼠标事件。
- 正式点击不使用 JavaScript `click()` 或页面脚本后备。

WebView2 Debug/Release 合同各 22/22，门禁明确验证严格顺序、同一 `nodeId` 和滚动失败短路。

## 3. CDP 节点树

- 先建立并验证直接 `Parent/Children` 对象树，再派生 XPath、nodeId 和 backendNodeId 索引。
- 规范 XPath 通过不可变字典定位；兼容 XPath 首次树导航后进入当前 revision 缓存。
- 属性事件增量发布新 revision；结构、导航和 Shadow DOM 事件使树失效并原子重建。
- WinUI 3 Projection 宿主通过薄 `IWebRuntimeDevToolsSession` 适配器接入，不同时引用两套
  同名 Core 类型。

## 4. 标准 DLL 布局

```text
lib\Iwesun.Runtime.Diagnostics\Debug|Release\Iwesun.Runtime.Diagnostics.dll
lib\Iwesun.Runtime.Data\Debug|Release\Iwesun.Runtime.Data.dll
lib\Iwesun.Runtime.Networks\Debug|Release\Iwesun.Runtime.Networks.dll
lib\Iwesun.Runtime.WebView2\Debug|Release\Iwesun.Runtime.WebView2.dll
```

各库顶层同名 DLL 为 Release 兼容入口。消费项目必须引用对应 Debug 或 Release 目录。

## 5. β 测试重点

- 历史列表、嵌套滚动容器和 SPA 页面中的内部元素精确点击；
- XPath 兼容查询、revision 更新、节点变化和导航重建；
- WinUI 3 薄适配器的 UI 上下文事件订阅与释放；
- 无 `Iwesun.Runtime.Web.dll` 的消费者 Debug/Release 编译；
- Networks UDP/DNS 代理、HTTP/DoH 池和精确 IPv6 路径回归；
- MSI 升级后的递归零 Web 载荷和 ProgramData 数据保持。

## 6. 交付目录

```text
artifacts\packages\Iwesun.Runtime.1.0.40-beta.1\
```

目录包含 MSI、便携 ZIP、Networks nupkg/snupkg、说明、清单和 SHA-256 清单。

## 7. 正式版阻塞项

- WebView2 真实动态页面长时间 DOM 事件与 STA 实机压力；
- WFP 双真实网关并发隔离和完整网络恢复矩阵；
- UDP 容量保护值的用户全流量数据；
- 干净环境安装、升级、卸载和最终版本冻结。

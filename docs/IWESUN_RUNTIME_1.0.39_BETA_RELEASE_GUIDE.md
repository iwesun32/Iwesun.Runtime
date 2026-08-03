# Iwesun Runtime 1.0.39-beta.1 普通 β 发布说明

> 状态：`GENERAL_BETA_READY_FORMAL_BLOCKED`  
> Runtime产品版本：`1.0.39`  
> Runtime程序集/文件版本：`1.0.39.0`  
> Runtime产品信息版本：`1.0.39-beta.1`  
> Networks产品信息版本：`3.0.0-beta.4`

本版用于让现有消费者更新 Runtime 编译依赖。它是新的普通β候选，不覆盖
`1.0.38-beta.1`，不上传公共NuGet源。

## 1. 发布边界

本版发布：

- `Iwesun.Runtime.Diagnostics`
- `Iwesun.Runtime.Data`
- `Iwesun.Runtime.Networks`
- `Iwesun.Runtime.WebView2`
- CLI、RemoteConsole、SampleHost、配置、技能、样例和文档

`modules/Web`仍处于界面和HTML/XAML对象模型调试期，不发布
`Iwesun.Runtime.Web.dll`、PDB、项目源码或独立库目录。

## 2. Web与WebView2边界修正

- WebView2只保留浏览器平台、CDP、节点树、网络、输入和公共控制能力。
- HTML/XAML组合、`HtmlRuntime*`、DOM到Web对象映射、脚本DOM诊断和事件组合适配已归回Web。
- WebView2不再项目引用或程序集引用Web；依赖方向固定为Web调试组件引用WebView2平台。
- WebView2节点树使用自身的`WebRuntimeDomTreeElement`合同，Web在组合边界显式转换为
  `DomElementConstructionNode`。

## 3. CDP节点树

- 先建立并验证直接`Parent/Children`对象树，再从树派生XPath、nodeId和backendNodeId索引。
- 规范XPath通过不可变字典O(1)定位；兼容XPath首次树导航后进入当前revision缓存。
- DOM读取、属性、聚焦、高亮和点击统一使用CDP节点身份，不把JavaScript作为隐式后备。
- 属性事件增量发布新revision；结构、导航和Shadow DOM事件使树失效并原子重建。

## 4. 标准DLL布局

```text
lib\Iwesun.Runtime.Diagnostics\Debug|Release\Iwesun.Runtime.Diagnostics.dll
lib\Iwesun.Runtime.Data\Debug|Release\Iwesun.Runtime.Data.dll
lib\Iwesun.Runtime.Networks\Debug|Release\Iwesun.Runtime.Networks.dll
lib\Iwesun.Runtime.WebView2\Debug|Release\Iwesun.Runtime.WebView2.dll
```

各库顶层同名DLL为Release兼容入口。消费项目必须引用对应Debug或Release目录，禁止混用。

## 5. β测试重点

- 现有消费者在完全没有`Iwesun.Runtime.Web.dll`时完成Debug/Release编译和启动；
- XPath兼容查询、revision更新、节点插入/删除、属性变化和导航重建；
- CDP点击、聚焦、高亮及WebView2 STA生命周期；
- Networks UDP/DNS代理、HTTP/DoH连接池和精确IPv6路径回归；
- MSI升级后Program Files载荷递归零Web，ProgramData用户数据保持。

## 6. 交付目录

```text
artifacts\packages\Iwesun.Runtime.1.0.39-beta.1\
```

目录包含MSI、便携ZIP、Networks nupkg/snupkg、发布说明、清单和SHA-256清单。

## 7. 正式版阻塞项

- WebView2真实动态页面长时间DOM事件与STA实机压力；
- WFP双真实网关并发隔离和完整网络恢复矩阵；
- UDP容量保护值的用户全流量数据；
- 干净环境安装、升级、卸载和最终版本冻结。

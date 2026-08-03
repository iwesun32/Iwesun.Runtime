# Iwesun.Runtime.Web WinUI 投影边界

## 目标

`Iwesun.Runtime.Web` 面向通用 HTML/SVG 协议，`Iwesun.Runtime.Web.WinUI` 面向通用 WinUI 3 运行时。两者都与豆包或任何具体站点无关。

## 职责分配

| 职责 | 唯一所有者 |
| --- | --- |
| HTML/SVG 元素类型、继承、规范属性 | `Iwesun.Runtime.Web` |
| DOM 六槽位填充和强查询身份 | `Iwesun.Runtime.Web` |
| 元素专属 `CreateXaml` 决策与输出属性计划 | `Iwesun.Runtime.Web` |
| CSS 样式、布局、数据源、事件的全局关系 | `Iwesun.Runtime.Web` |
| WinUI 强类型对象创建与属性设置 | `Iwesun.Runtime.Web.WinUI` |
| WinUI 子对象挂载、Binding/事件连接 | `Iwesun.Runtime.Web.WinUI` |
| 已加载 WinUI 控件的 XamlFill 真实读回 | `Iwesun.Runtime.Web.WinUI` |
| DOM/XAML 双向审核算法 | `Iwesun.Runtime.Web` |
| 站点导航、站点顶层策略和展示窗口 | 消费项目 |

## 强制门禁

1. 每个标准 HTML/SVG 最终类必须拥有自己的 `CreateXaml` 判定，不得退回公共字符串猜测器。
2. WinUI 投影对 DOM 背景元素必须走具体 CLR 类型重载；只有无 DOM 源的合成子对象可走合成入口。
3. 属性目标已经由元素类专属判定时，必须使用目标 WinUI 类型的强类型设置器。字符串路由只能处理无特例、一对一的通用属性。
4. `XamlFill` 只能从已加载的 WinUI 对象、Binding、Resource、Composition 和真实事件证据读取。不得从 DOM/CSS 预期值回填。
5. 审核必须以原 DOM 元素集为母集合，检查缺失 XAML 元素、多余 XAML 元素、值差异和链接差异。
6. Runtime Web 两个程序集均不得引用消费项目，也不得出现站点名、站点 XPath 或站点 URL。

## 消费项目可以做的事

消费项目只保留导航到目标页、绑定 Runtime WebView2、调用 Runtime Web 管线、显示两个对比窗口和输出诊断。具体页面可提供大布局配置，但不得重新实现 `div`、`span`、`button` 等通用元素的投影逻辑。

# Iwesun.Runtime.Web 架构与依赖边界

## 分层

```text
元素类型与实例树
    ├── 元素固有分类
    ├── XPath/父子兄弟管理
    └── 事件注册与派发
             ↓
分辨率/尺寸/样式继承服务
             ↓
五类泛型槽位属性
             ↓
属性审核 → 元素审核 → 累计统计/文本报告
             ↑
强类型容器布局合同
```

### Elements

定义 HTML/SVG 元素的固有语义和实例管理。`HtmlSvgElementCatalog` 是唯一标准目录入口，
按标签名创建新的独立实例。公共对象不得保存消费项目的 DOM DTO 或 XAML 对象。

### Properties

定义属性特性、反射读取器、五类槽位和来源/目标阶段。属性对象同时保存源端和 XAML 端槽位，
具体属性负责自己的审核算法。

### Inheritance

`ElementInheritanceService` 发布带 ID 的分辨率、容器尺寸和样式快照。元素只保存
`ElementInheritanceLink`，不复制整套公共数据。子上下文通过 `ParentId` 继承允许继承的样式；
窗口或典型分辨率变化发布新上下文，不直接逐元素手工修改。

### Layout

定义容器布局使用的强类型约束。CSS 原始字符串只作为证据，正式计算使用枚举、数值和结构体。

### Auditing

属性审核返回结构化阶段结果；元素审核只负责遍历；累计器只汇总，不得修改比较结果。
无法映射和数值不一致必须分类报告，不得通过缩小审核范围制造通过。

## 依赖规则

`Iwesun.Runtime.Web`：

- 只依赖 .NET 基础库。
- 不依赖 DoubaoUIClone、DOM 快照合同、WinUI、WebView2 或站点策略。
- 不读取外部文件，不进行网络访问，不输出控制台诊断。

`Iwesun.Runtime.Web.WinUI`：

- 是 `Iwesun.Runtime.Web` 的通用 Windows/WinUI 强类型投影适配层，不是任何站点的业务层。
- 依赖 `Iwesun.Runtime.Web`、`Iwesun.Runtime.WebView2`、WinUI 3 和诊断模块。
- 将具体 HTML/SVG 元素生成的 `XamlElementObjectPlan` 强类型实例化为 WinUI 对象，并从已加载的 WinUI 可视树回填 XAML 运行时槽位。
- 不得包含 DoubaoUIClone、站点 URL、站点 XPath、站点布局常量或站点状态判定。
- 不得在 WinUI 属性读取失败时回退为 DOM/CSS 源值；缺少真实控件证据必须使审核失败。

消费项目：

- 只组装 Runtime Web 提供的导航、DOM Fill、全局关系分析、XAML 对象树构建、XamlFill 和审核管线。
- 可以提供站点导航操作和站点自身的大布局/样式配置，但不得重新实现通用 HTML/SVG 元素投影。
- 不得复制公共源码，不得重新定义同名审核或布局合同。

## 通用 HTML→WinUI 管线

```text
Iwesun.Runtime.WebView2 (CDP/DOM 真实证据)
    ↓
Iwesun.Runtime.Web (113 HTML + 14 SVG 强类型元素)
    DomFill → GlobalRelationships → CreateXaml/BuildXamlObjectPlans
    ↓
Iwesun.Runtime.Web.WinUI (强类型 new + 属性设置 + 可视树挂载)
    ↓
WinUI 已加载可视树
    XamlFill(真实控件值) → Audit(DOM 对 XAML)
```

元素类自身决定 HTML 语义、XAML 目标类型和属性转换；WinUI 适配层只执行该强类型计划。禁止消费项目根据某个页面的现场坐标修改通用元素语义。

## 稳定原则

- 类型分类是只读固有信息。
- 树、XPath 和事件是每个元素实例独有的管理状态。
- 元素目录不返回共享可变单例。
- 相同审核功能只由同一个公共方法实现。
- 审核统计与文本呈现分离，统计结果可累加但不可反向影响判定。

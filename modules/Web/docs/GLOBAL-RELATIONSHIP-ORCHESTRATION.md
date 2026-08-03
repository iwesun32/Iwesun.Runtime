# HTML/XAML 全局关系编排规范

**状态：正式规范**  
**日期：2026-08-01**

## 1. 目的

逐元素 `DomFill` 只负责读取该元素自身的初始化、链接和运行证据。它的
查询方向是“元素属性 → DOM/CSS 证据”。仅有这一步无法恢复 CSS、布局器、
数据源和事件系统对多个元素的反向控制关系。

因此正式内存流程增加 `HtmlRuntimeGlobalRelationshipOrchestrator`。它由
`HtmlRuntimeDocumentRoot` 持有，执行两个全局阶段：

1. `GlobalRelationshipsResolved`：在全部元素 `DomFill` 完成后，从全局
   CSS、布局、数据和事件连接反向解析到具体元素与具体属性。
2. `XamlGlobalRelationshipsComposed`：在全部强类型 XAML 对象创建并串成树后，
   把上述关系连接到实际 WinUI 对象与依赖属性。

任一全局关系引用不存在的 DOM 元素、XAML 对象或不受支持的强类型目标时，
正式路径必须报告失败；不得复制 DOM/CSS 值伪装成 WinUI 运行值。

## 2. 数据源的两个领域

文本和图形数据都必须显式归入以下一个领域：

| 领域 | 枚举 | 含义 | 示例 |
|---|---|---|---|
| UI 资源 | `DomDataSourceDomain.UiResource` | 跟随语言、主题和 UI 样式配置，可由资源包替换 | 按钮标题、占位符、图标、装饰图、主题图片 |
| 业务输入 | `DomDataSourceDomain.BusinessInput` | 由程序、用户或业务服务输入，不属于 UI 资源 | 输入值、勾选状态、会话正文、列表数据、导航目标 |

`Source` 只是 XAML 目标属性名，不能据此判断领域。分类必须在
`HtmlElementDataSourceCatalog` 的强类型定义处完成，并写入
`DomElementDataSource.Domain`。全局编排器分别输出 `UiResources` 和
`BusinessInputs`，禁止在后续绑定阶段重新猜测。

DOM 内容图像、媒体和表单当前值默认属于业务输入；CSS、脚本、主题包等
界面构成资源属于 UI 资源。按钮、标签等固定界面文案属于 UI 资源。通用
`content.ownText` 已作为正式数据源槽位捕获，不再只作为生成期字符串使用。

## 2.1 动态数据源

每个全局数据绑定都持有一个 `HtmlRuntimeDynamicDataValue`。它的身份由
`documentScope + XPath + 数据源名称` 唯一确定，并公开：

- `Value`：最近一次 DOM 运行捕获值；
- `Revision`：每次成功捕获后递增；
- `Domain`：UI 资源或业务输入；
- `ContentKind`：文本、图形、媒体、表单状态、集合或导航；
- `INotifyPropertyChanged`：让既有 WinUI Binding 接收更新。

`RefreshRuntimeAsync` 必须更新同一个动态数据源实例。不得为新值创建无关联
对象，也不得要求重写 XAML 文件才能更新控件。

业务程序通过 `HtmlRuntimeDocumentRoot.UpdateBusinessDataSource(documentScope,
xpath, sourceName, value)` 更新已捕获的业务输入。该入口只接受已经解析且领域为
`BusinessInput` 的强身份；未捕获身份和 `UiResource` 均硬失败，防止业务代码
绕过资源/主题管理器改写 UI 资源。

## 3. DOM 全局关系阶段

固定顺序为：

```text
Navigate
  → Build typed DOM tree
  → DomFill (逐元素、逐反射槽位)
  → Resolve global relationships
```

全局解析至少覆盖：

- 数据绑定：领域、内容类型、当前值、元素身份、XAML 目标属性；
- 样式绑定：CSS 规则/变量到元素和属性的反向连接；
- 布局绑定：视口、媒体状态、Flex/Grid/定位关系到元素的反向连接；
- 事件绑定：事件名称、阶段、处理器证据到元素的连接；
- 分辨率输入：保存当前 WebView2 运行视口，供 XAML 布局器使用，不做独立
  DPI 缩放。

`DomFill` 与全局解析不可合并：前者是元素局部证据采集，后者是跨元素关系
建图。这样可以分别审核“属性是否填对”和“全局控制关系是否完整”。

## 4. XAML 全局关系阶段

固定顺序继续为：

```text
CreateXaml (最终 HTML/SVG 强类型决定具体 WinUI 类型)
  → Fill XAML initialization properties
  → Attach XAML children
  → Compose XAML global relationships
  → Display
  → XamlFill from displayed WinUI objects
  → Audit
```

`IXamlGlobalRelationshipBinder` 接收已解析图，并对实际对象执行：

- 用 WinUI `Binding` 连接文本、内容、占位符、图形源、表单状态和集合；
- 使用强类型转换器把动态字符串转换为布尔、数值、URI 和 `BitmapImage`；
- 把事件证据注册到实际控件和运行时事件证据表；
- 保留样式、布局与动画管理器已经生成的实际 XAML 目标连接；
- 不读取外部文件；文件 I/O、反序列化和校验必须先在 C# 完成；
- 不使用字符串 XAML 或字符串控件工厂替代强类型对象创建。

## 5. 审核门禁

1. `BuildXaml` 只能从 `GlobalRelationshipsResolved` 阶段进入。
2. `DisplayXaml` 只能从 `XamlGlobalRelationshipsComposed` 阶段进入。
3. DOM 全局图引用树外元素时立即失败。
4. 数据绑定保留 `Domain` 和 `ContentKind`，不得在 XAML 阶段降级为无类型文本。
5. `XamlFill` 只从已经显示的真实 WinUI 对象读取；禁止回退到 DOM、CSS、
   控制器缓存或全局关系图中的源值。
6. Audit 对比 Source 与真实 XAML 多槽位；共同遗漏不能判为一致。
7. 动态刷新门禁必须验证数据源实例身份不变、revision 递增，并验证已有
   WinUI Binding 的文本、表单状态或图形属性实际发生变化。

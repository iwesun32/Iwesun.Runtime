# HTML/SVG 元素树与事件模型

## 元素固有属性

`HtmlSvgElementDefinition` 是公共基类。下列属性描述元素类型本身，不随运行状态改变：

- `ElementNamespace`
- `Category`
- `VisualKind`
- `ContentModel`
- `Closure`
- `Syntax`
- `XamlSupport`
- `XamlControlFamily`
- `XamlElementName`
- `IsVisualElement`
- `IsClosedVisualControl`
- `CannotConvertToXaml`

`XamlElementName` 是只读精确名称，例如 `div → Grid`、`a → HyperlinkButton`、
`img → Image`。控件族用于归类，元素名称用于生成和类型审核，两者不得混用。

HTML 家族基类复用共同分类；具体标签使用真实类表达，例如 `HtmlDivElement`、
`HtmlButtonElement`、`HtmlImageElement`、`SvgPathElement`。新增标签必须进入
`HtmlSvgElementCatalog` 并补充分类测试。

当前公共目录包含 112 个独立 HTML 具体类和 14 个 SVG 具体类，超过首阶段 86 种 HTML
元素要求。每个 HTML 类均接受全局属性、ARIA、data、自定义扩展证据，并通过专属属性目录
识别 `href`、`srcset`、`pattern`、`sandbox` 等标签属性；未识别属性仍保留并标记为扩展，
不会在转换前丢失。

126 个具体类全部自行重写 `AuditElementType()`，发布唯一 `RuleId`、检查集合、结论和文本。
每类报告至少包含以本标签命名的类型身份、可视语义、封闭语义、XAML 翻译、儿子封闭、
XPath、允许属性集合和属性槽位所有权检查；文本逐项输出期望值、实际值与 PASS/FAIL，
因此不能只改规则编号后复用同一份结论。公共 `StandardElementTypeAuditor` 只提供可复用
检查原语；标签类选择自己的规则并负责标签特有约束，例如 `img` 的 `alt`、`track` 的
`src`、表格/列表的允许儿子集合和 `input` 类型。测试通过反射强制验证重写方法确实声明在
具体类上，并验证 126 份检查签名和文本输出均不同，不能退回公共基类默认审核。

## Fill、ToXaml、Audit 的方法归属

`FillAsync`、`CreateXaml` 和 `AuditElementType` 必须由每个具体 HTML/SVG 类在类体内重写。
公共基类只实现三种稳定算法：儿子优先递归、逐槽执行、结果汇总；它不得用外部标签分派器
替代具体类的方法。

### 禁止伪强类型 XAML 映射

“每个具体类都声明重写”不等于“每个具体类拥有映射语义”。以下实现明确禁止：

```csharp
protected override XamlElementMappingDecision ResolveXamlElementMapping() =>
    ResolveTypeDefinedXamlElementMapping();
```

只要公共方法返回 `XamlElementMappingDecision`、XAML 控件类型、子项安放方式或布局模式，
决策权就仍然属于公共方法；把同一行转调复制到 113 个类中只是伪强类型。它会丢失标签自身
的内容模型、默认显示、允许儿子、交互语义及专属状态，现场数据变化后还会同时改错其他类型。

每个具体 HTML/SVG 类型必须在自己的 `CreateXaml()` 中完整建立其映射决定，包括：

- 该类型允许生成的 XAML 控件类型集合；
- 默认控件类型和默认子项安放规则；
- 该类型允许由设计链接选择的布局状态；
- 每个状态所需的初始化、CSS 链接或运行状态证据；
- 不满足状态前提时属于不支持、缺证据还是确定的回退语义。

运行时值只能在该具体类型预先声明的有限状态中选择，不能创造元素类型语义。绝对坐标和
运行宽高只用于审核，不能参与控件类型选择或初始化布局。公共根基类不得替全部元素返回
最终映射决定，也不得根据标签名、`display`、当前宽高或子项数量猜测控件类型。

公共层只允许提供不带元素语义的机械原语，例如反射枚举属性、读取指定槽位、数值和单位
换算、创建已由具体类型选定的节点、投影属性、挂载儿子和汇总诊断。真正共有的强类型语义
可以由对应的窄语义族基类继承实现，但该基类只能服务自己的继承族，规则必须是该族恒定
合同，禁止读取标签名或现场数据跨类型猜测。具体类型仍须拥有并能覆盖自己的固有合同与
专属差异。实现门禁必须同时验证：

1. 映射方法的 `DeclaringType` 是当前具体类型；
2. 具体方法没有转调任何公共“返回映射决定/控件类型/布局模式”的方法；
3. 不存在面向全部元素、按标签或现场数据猜测结果的公共决策器；
4. 每个具体类型的映射合同包含唯一的类型身份及完整允许状态；
5. 改变一个类型的映射合同不会改变另一个类型的映射结果。

`FillAsync` 的公共实现枚举当前具体元素的完整属性定义，并将每个初始化值、链接值和运行值
拆成独立 `ElementFillSlotRequest`。每次委托调用只请求一个
`documentScope + XPath + 属性名 + 槽位 + 证据类型`。消费方可以在委托内部缓存同一 DOM
元素的原始证据，但不能把批量缓存结果冒充未逐项请求的槽位。`input` 的选择区、
`select` 的选中项、图片固有尺寸和媒体运行状态等专属需求声明在对应具体元素类中。

`ToXaml` 先投影元素自己的全部槽位，再按元素自己的 `XamlChildPlacement` 递归写儿子：
面板使用直接儿子，内容控件使用单一 `Content`（多个儿子由容器承载），选择器使用
`Items`，文本使用 `Inlines`，叶控件拒绝儿子。事件不得写代码隐藏处理器名称；命令控件
只绑定经过验证的内存命令，其余事件由统一 C# 运行时分派器在 XAML 加载后挂载。

正式对象路径固定调用 `CreateXaml → FillXamlProperties → AttachXamlChildren`。
WinUI 工厂的 `CreateElement` 只能构造目标类型；属性写入必须在独立的
`FillElementProperties` 调用中发生，禁止再次把“创建类型”和“填属性”合并。

## 实例管理属性

每个目录查询都创建独立元素实例。实例包含：

- `Parent`
- `Children`
- `PreviousSibling`
- `NextSibling`
- `XPath`
- `InheritanceLink`

`AppendChild` 建立双向父子关系并自动维护兄弟关系；已有父节点会先安全脱离。
它拒绝叶元素挂载子项，也拒绝任何会形成循环引用的操作。

XPath 是 DOM 证据，不是对象指针。采集器应使用 `AssignXPath` 或
`AppendChild(child, xpath)` 写入真实绝对 XPath。没有显式 XPath 且父 XPath 已知时，
公共类按同标签兄弟序号生成确定性路径。

`InheritanceLink` 同样是不可变符号链接。分辨率、容器尺寸和可继承样式由
`ElementInheritanceService` 集中发布；元素通过 `LinkInheritance` 连接，避免给每个元素重复写入
相同屏幕和基础样式数据。

## 事件列表

`Events` 返回元素当前的公开事件描述，不暴露处理委托。描述包含稳定注册 ID、事件种类和名称。

首个事件种类是 `Click`：

1. `RegisterClickHandler` 注册具名异步处理器并返回订阅对象。
2. `ClickAsync` 从目标元素开始执行。
3. 未停止传播时，事件沿 `Parent` 逐层冒泡。
4. 处理器可阻止默认行为、停止后续父层传播或立即停止同元素剩余处理器。
5. 释放订阅对象后，注册从事件列表移除。

事件调用支持 `CancellationToken`。公共事件层只提供 UI 动作语义，不发送网络消息，也不执行业务策略。

## 类型目录

```csharp
var root = HtmlSvgElementCatalog.GetRequired("html");
var button = HtmlSvgElementCatalog.GetRequired("button");
root.AssignXPath("/html");
root.AppendChild(button, "/html/body/div/button[1]");

using var subscription = button.RegisterClickHandler(
	"open",
	(context, cancellationToken) => ValueTask.CompletedTask);
await button.ClickAsync();
```

`TryGet` 用于非异常查询；标签匹配不区分大小写。未知标签由消费项目决定使用自定义元素还是拒绝。

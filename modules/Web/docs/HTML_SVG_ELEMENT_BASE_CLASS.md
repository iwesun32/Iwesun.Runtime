# HTML/SVG 元素基础类设计

## 状态

本文是 `Iwesun.Runtime.Web` 元素对象模型的实现合同。当前
`HtmlSvgElementDefinition` 及集中式 Fill/Audit/ToXaml 文件不满足本文，不能以
“类型数量”或“构建通过”宣称元素类已经完成。

## 一、基本原则

1. 元素是属性和行为的所有者。
2. 每个具体 HTML/SVG 类型单独一个同名文件。
3. `FillAsync`、`ToXaml`、`Audit` 是具体元素类的方法。
4. 基础类只提供共同状态、反射枚举、树操作和受保护的组合原语。
5. 不设置全局元素属性清单，不根据标签名在中央目录猜测属性。
6. DOM 查询委托只负责查询，不决定某种元素拥有哪些属性。
7. 属性对象负责自己的 Fill 请求、槽位回填、XAML 投影和审核。

## 二、基础类固有只读分类

基础类发布以下不可变类型事实：

- `TagName`
- `ElementNamespace`
- `Category`
- `VisualKind`
- `ContentModel`
- `Closure`
- `Syntax`
- `XamlSupport`
- `XamlControlFamily`
- `XamlElementName`
- `DefaultDisplay`
- `InteractionKind`

以下布尔值只能从上述枚举推导：

- `IsVisualElement`
- `IsClosedVisualControl`
- `CannotConvertToXaml`
- `IsVoidElement`
- `RequiresRuntimeReplacement`
- `CanContainElements`
- `CanContainText`
- `ShouldTraverseChildren`

## 三、实例身份和树关系

基础类拥有：

- `DocumentScope`
- `XPath`
- `Parent`
- `Children`
- `PreviousSibling`
- `NextSibling`
- `NodeKey`
- `ParentNodeKey`
- `OffsetParentXPath`
- `ContainingBlockXPath`
- `ScrollContainerXPath`

这些是管理属性，也必须使用 `[ElementProperty]` 标记。需要证据回填的属性必须
标记 `IsFillRequired=true`。树操作必须维护父子和兄弟一致性并拒绝循环引用。

## 四、真实属性对象

元素属性必须是具体 CLR 属性，不得只保存为字符串请求：

```csharp
[ElementProperty(..., IsFillRequired = true)]
public ButtonDisabledProperty Disabled { get; }
```

具体属性类型继承五类槽位属性之一：

- `ElementSpaceSlottedProperty<TValue>`
- `ElementStyleSlottedProperty<TValue>`
- `ElementEffectSlottedProperty<TValue>`
- `ElementActionSlottedProperty<TValue>`
- `ElementDataOrganizationSlottedProperty<TValue>`

每个属性对象必须发布：

- 不可变 `ElementPropertyTraits`
- 源端初始化、链接、运行槽位及其值来源
- XAML 初始化、链接、运行槽位及其值来源
- `CreateFillRequests()`
- `ApplyFillResponse(...)`
- `CreateXamlProjection()`
- `Audit()`

扩展 HTML 属性也必须创建正式属性对象。缺少特征属于实现缺陷，不能自动归为人工复核。

## 五、反射属性目录

基础类在实例类型上反射公开属性，只收集同时满足以下条件的成员：

1. 带 `ElementPropertyAttribute`；
2. 值实现统一元素属性接口；
3. 声明类型属于当前继承链。

反射结果按 `(运行时元素类型, PropertyInfo)` 缓存。属性实例和值不能进入静态缓存。

`IsFillRequired` 决定是否向委托请求数据。基础类不得通过属性名称、标签名称或中央
字符串数组推断这一决定。

## 六、Fill 方法合同

每个具体元素重写完整的 `FillAsync`。固定顺序为：

1. 递归调用全部 `Children[i].FillAsync`；
2. 反射枚举当前具体元素的真实属性；
3. 调用属性的 `CreateFillRequests()`；
4. 对每条请求调用标准证据委托；
5. 由原属性对象执行 `ApplyFillResponse()`；
6. 汇总缺失、已确认不存在和来源不支持；
7. 返回当前子树 Fill 结果。

基础类只提供 `FillChildrenAsync`、`FillDeclaredPropertiesAsync` 和结果合并原语。
禁止 `ElementFillRequirementCatalog` 代表所有元素生成属性。

## 七、ToXaml 方法合同

`ToXaml` 是严格的三段编排，不得合并职责：

1. `CreateXaml()`：抽象方法，由最终具体 HTML/SVG 类型实现，只创建并返回正确 XAML
   类型的对象空壳/创建合同；
2. `FillXamlProperties()`：基类反射枚举当前元素的全部属性对象，把设计初始化和链接投影
   写入已经创建的对象；不得重新选择控件类型；
3. `AttachXamlChildren()`：依据具体类型公布的 `XamlChildPlacement` 挂载已创建子对象。

每个具体元素的 `CreateXaml()`：

1. 明确知道自己的 HTML/SVG 具体类型；
2. 检查本类型的内容模型和实际子类型构成；
3. 只在本类型允许的 XAML 类型/布局状态集合中选择；
4. 创建不带属性填充副作用的对象空壳；
5. 返回类型身份、布局机制和子项安放合同。

属性反射、属性转换和属性写入统一属于 `FillXamlProperties()`。基类可以实现这套机械遍历，
但不得在填属性时替换对象类型，也不得根据某个属性临时改写另一类型的创建合同。

DOM 运行绝对坐标只能用于运行审核，不能写入 XAML 初始化布局。

### 具体类型拥有完整决策权

具体类型的重写必须直接表达该标签的 XAML 控件选择、允许布局状态和子项安放合同。禁止用
一行代码调用公共 `Resolve*Xaml*Mapping`、`ResolveTypeDefinedXamlElementMapping`
或同义方法返回最终决定；这种写法虽然在反射上显示为“已重写”，实际仍由公共算法统一猜测，
属于未实现。

面向全部元素的根基类不得替具体类型返回以下任一最终结果：

- `XamlElementMappingDecision`；
- XAML 控件类型或控件族；
- `XamlChildPlacement`；
- Flex、Grid、普通流、定位等布局模式的最终选择。

公共层可以接收“具体类已经选定的决定”并完成节点创建、属性遍历、单位换算和儿子挂载。
真正共有的规则允许放入窄的强类型语义族基类并由继承复用，例如同一内容模型的固定儿子
安放机械；但该规则必须是此继承族的恒定合同，不能按标签名或现场数据跨族猜测，也不能
吞掉具体类型的专属差异。CSS/computed/runtime 证据只能驱动具体类型或其强类型语义族
明确声明的状态机；不得以当前页面的宽高、坐标、是否有儿子等现场值反推标签语义。

## 八、Audit 方法合同

每个具体元素重写 `Audit`：

1. 先调用所有子元素的 `Audit`；
2. 审核本元素的类型合同；
3. 反射枚举本元素全部真实属性；
4. 根据属性 `IsAuditRequired` 调用属性自己的 `Audit()`；
5. 汇总结构化统计和文本说明；
6. 子元素或本元素任一失败，当前子树失败。

不得由外部 `ElementTreeAuditor` 重新遍历元素；不得在元素审核器中重新实现属性比较。

## 九、事件

基础类维护结构化事件注册集合。Click 至少支持注册、取消、异步调用、取消令牌、
阻止默认行为、停止传播及父链冒泡。事件也是动作槽位属性，不能只存旁路字符串。

## 十、文件布局

```text
Elements/
  HtmlSvgElement.cs
  Html/
    HtmlButtonElement.cs
    HtmlInputElement.cs
    HtmlDivElement.cs
  Svg/
    SvgPathElement.cs
    SvgRectElement.cs
Properties/
  ElementSlottedProperty.cs
  ButtonDisabledProperty.cs
  RuntimeXCoordinateProperty.cs
```

删除聚合壳文件 `StandardHtmlElements.cs`、`StandardSvgElements.cs` 和
`FormTableMediaElements.cs`。元素业务行为不得放入按 Fill、Audit 或 Converter 命名的文件。

## 十一、实现门禁

实现必须由测试证明：

- 每个具体元素类型只有一个同名源码文件；
- 每个具体类型声明自己的 `FillAsync`、`ToXaml`、`Audit`；
- 每条 Fill 请求能反向定位到一个具体属性对象和槽位；
- 委托返回值写回发起请求的同一属性对象；
- 不存在中央标签到属性请求清单；
- 没有真实属性的壳类型不得计入“已实现元素”；
- 审核遍历的属性数量等于反射得到的需审核属性数量。
- 每个具体类型的 XAML 映射方法由该类型声明，并直接建立该类型专属合同；
- 不存在供全部具体类型一行转调、按标签或现场数据猜测的公共映射决策器；
- 门禁检查方法调用依赖，不能只检查 `MethodInfo.DeclaringType`；
- 单独修改一种标签的映射合同，不得改变其他标签的映射结果。

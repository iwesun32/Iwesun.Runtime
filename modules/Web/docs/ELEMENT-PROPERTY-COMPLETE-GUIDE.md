# Iwesun.Runtime.Web 完整实现文档

## 概述

`Iwesun.Runtime.Web` 是一个完整的 HTML/SVG 元素模型、属性槽位和自动填充框架。本文档覆盖从元素类型定义、属性规范、特性标记、反射遍历、槽位填充到 WebView2 集成的全链路设计和实现状态。

**当前状态（2026-07-28）**：框架层完整，测试覆盖 150 个用例，WebView2 填充器已实现。

---

## 第一部分：元素类型体系

### 1.1 覆盖范围

| 类别      | 数量   | 实现方式                  |
| --------- | ------ | ------------------------- |
| HTML 元素 | 113 个 | 全部有具体 `sealed class` |
| SVG 元素  | 14 个  | 全部有具体 `sealed class` |

### 1.2 继承树

```
DomElement (抽象根类型)
├── HtmlDomElementDefinition    ← 36 个全局属性
│   ├── HtmlLayoutDomElementDefinition       ← 布局容器（Grid/StackPanel/Flex）
│   │   ├── HtmlContainerDomElementDefinition    ← div, main, nav, aside...
│   │   ├── HtmlSectioningDomElementDefinition    ← body, header, footer, form...
│   │   ├── HtmlListDomElementDefinition          ← ul, ol, menu, li, dl
│   │   └── HtmlTableDomElementDefinition         ← table, tr
│   │       └── HtmlTableCellDomElementDefinition  ← th, td  (+ApplyCellSpans)
│   ├── HtmlTextVisualDomElementDefinition     ← 文本元素（字体/颜色/对齐）
│   │   ├── HtmlPhrasingDomElementDefinition   ← span, strong, em, a, label...
│   │   ├── HtmlBlockTextDomElementDefinition  ← p, h1-h6, pre...
│   │   ├── HtmlVoidTextDomElementDefinition   ← br, wbr
│   │   ├── HtmlInteractiveDomElementDefinition ← details, summary, button
│   │   │   └── HtmlHyperlinkDomElementDefinition   ← a, area
│   │   └── HtmlFormControlDomElementDefinition ← input, textarea, select (+ApplyDisabledAttribute)
│   ├── HtmlMediaDomElementDefinition         ← video, audio, img
│   ├── HtmlMetadataDomElementDefinition      ← head, title, meta, link, script...
│   └── HtmlEmbeddedDomElementDefinition      ← iframe, canvas, embed, object (+ApplyEmbeddedDimensions)
└── SvgDomElementDefinition
    ├── SvgContainerDomElementDefinition      ← svg, g, defs
    ├── SvgGeometryDomElementDefinition       ← path, rect, circle, line...
    └── SvgDefinitionDomElementDefinition     ← clipPath, mask
```

### 1.3 元素类型固有设计属性

每个元素类通过 13 个只读属性表达规范固有的语义信息，这些属性不来自运行时网页数据：

| 属性                 | 类型   | 说明                                                                |
| -------------------- | ------ | ------------------------------------------------------------------- |
| `TagName`            | string | 规范标签名称                                                        |
| `ElementNamespace`   | enum   | HTML / SVG / MathML                                                 |
| `Category`           | enum   | Document / Sectioning / Text / FormControl / Media / Table...       |
| `VisualKind`         | enum   | LayoutContainer / TextContent / NativeControl / ReplacedContent...  |
| `ContentModel`       | enum   | 按规范允许承载的内容类型                                            |
| `Closure`            | enum   | OpenContainer / ClosedLeaf / ReplacedControl                        |
| `Syntax`             | enum   | Normal / Void / RawText                                             |
| `XamlSupport`        | enum   | Direct / Composite / NonVisual / RuntimeReplacement                 |
| `XamlControlFamily`  | enum   | Panel / Text / Button / Input / Selector / Image / Media / Shape... |
| `XamlElementName`    | string | 默认翻译成的 XAML 元素名称                                          |
| `DefaultDisplay`     | enum   | Block / Inline / InlineBlock / None...                              |
| `InteractionKind`    | enum   | None / Navigation / Input / Disclosure...                           |
| `XamlChildPlacement` | enum   | DirectChildren / Inlines / Content / Items / None                   |

---

## 第二部分：属性定义 (WhatWG 标准)

### 2.1 全局属性 (36 个)

所有 HTML 元素继承的全局属性（定义在 `HtmlDomElementDefinition` 基类中）：

| 分组       | 属性（HTML 名称 → CLR 属性）                                                                                                                                                                                                                                      |
| ---------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 标识与语言 | `id`→Id, `class`→Class, `title`→Title, `lang`→Language, `dir`→Direction                                                                                                                                                                                           |
| 样式与焦点 | `style`→Style, `hidden`→Hidden, `tabindex`→TabIndex, `accesskey`→AccessKey, `autofocus`→AutoFocus                                                                                                                                                                 |
| 编辑与输入 | `contenteditable`→ContentEditable, `draggable`→Draggable, `spellcheck`→SpellCheck, `translate`→Translate, `inputmode`→InputMode, `enterkeyhint`→EnterKeyHint, `autocapitalize`→AutoCapitalize, `autocorrect`→AutoCorrect, `writingsuggestions`→WritingSuggestions |
| 可访问性   | `role`→Role, `inert`→Inert, `popover`→Popover                                                                                                                                                                                                                     |
| Shadow DOM | `slot`→Slot, `part`→Part, `exportparts`→ExportParts                                                                                                                                                                                                               |
| 标题层级   | `headingoffset`→HeadingOffset, `headingreset`→HeadingReset                                                                                                                                                                                                        |
| 自定义元素 | `is`→Is                                                                                                                                                                                                                                                           |
| CSP        | `nonce`→Nonce                                                                                                                                                                                                                                                     |
| Microdata  | `itemid`→ItemId, `itemprop`→ItemProp, `itemref`→ItemRef, `itemscope`→ItemScope, `itemtype`→ItemType                                                                                                                                                               |

### 2.2 元素专属属性（约 250 个）

45 个有专属属性的元素，在各自的 `sealed class` 中定义了强类型属性。示例：

| 元素       | 专属属性                                                          |
| ---------- | ----------------------------------------------------------------- |
| `<a>`      | Href, Target, Download, Ping, Rel, HrefLang, Type, ReferrerPolicy |
| `<input>`  | Accept, Alpha, Alt, AutoComplete, Checked, ..., Width（共 35 个） |
| `<button>` | Command, CommandFor, Disabled, Form, ..., Value（共 14 个）       |
| `<video>`  | Source, CrossOrigin, Poster, ..., Width, Height（共 12 个）       |
| `<img>`    | Alt, Source, SourceSet, ..., FetchPriority（共 14 个）            |

### 2.3 扩展属性机制

`data-*`、`aria-*` 等开放属性通过 `ExtensionAttributes` 集合兜底：
- `AddExtensionAttribute(name)` 动态添加
- 自动参与 Fill/Audit/ToXaml 流程
- 属性目录通过前缀匹配自动识别（`aria-*`、`data-*`、`on*`）

### 2.4 事件处理器（115 个）

| 类别                          | 数量  | 来源                                                       |
| ----------------------------- | ----- | ---------------------------------------------------------- |
| HTML Living Standard 全局事件 | 61 个 | WHATWG HTML                                                |
| 跨规范补充                    | 32 个 | Pointer/UI/Touch/Animation/Transition/Fullscreen/Selection |
| WebKit 兼容                   | 4 个  | 浏览器兼容                                                 |
| Body 专属 Window 事件         | 18 个 | afterprint, beforeunload, hashchange 等                    |

---

## 第三部分：属性特性标记

### 3.1 特性标记分类

每个属性通过 `[ElementProperty]` 特性声明其身份和行为：

| 特性标志                                  | 含义                 | 标记对象                                                    |
| ----------------------------------------- | -------------------- | ----------------------------------------------------------- |
| `IsHtmlDefinedProperty = true`            | 来自 HTML 规范的属性 | 全局属性 + 元素专属属性                                     |
| `IsElementDesignProperty = true`          | 类型固有设计属性     | TagName, Category, VisualKind...                            |
| `IsManagementProperty = true`             | 管理属性             | DocumentScope, XPath, ParentXPath...                        |
| `IsObjectTreeRelationshipProperty = true` | 树关系属性           | Parent, Children, LeftSibling, RightSibling                 |
| `IsDomMappingProperty = true`             | DOM 映射属性         | ParentXPath, ChildXPaths, LeftSiblingXPath...               |
| `IsRuntimeDerivedProperty = true`         | 运行时派生属性       | Parent, Children, Siblings（由代码设置，不来自网页）        |
| `IsSlottedProperty = true`                | 单值槽位属性         | Id, Class, Style, 各元素专属属性                            |
| `IsSlottedPropertyCollection = true`      | 集合槽位属性         | Events, ExtensionAttributes, RuntimeProperties, DataSources |

### 3.2 填充控制标志

| 特性标志                      | 含义             | 影响                        |
| ----------------------------- | ---------------- | --------------------------- |
| `IsFillRequired = true`       | 需要从 DOM 填充  | 被 `DomFillAsync` 遍历处理  |
| `IsXamlFillRequired = true`   | 需要从 XAML 填充 | 被 `XamlFillAsync` 遍历处理 |
| `IsAuditRequired = true`      | 需要审核         | 被 `Audit()` 遍历处理       |
| `IsXamlOutputProperty = true` | 需要输出到 XAML  | 被 `ToXaml()` 遍历处理      |

### 3.3 标记规则

```
需要填充的属性（HTML 属性）：
  IsHtmlDefinedProperty = true
  IsFillRequired = true
  IsXamlFillRequired = true
  IsAuditRequired = true
  IsSlottedProperty = true (或 IsSlottedPropertyCollection = true)

不需要填充的属性（管理/设计属性）：
  IsFillRequired = false
  使用 IsElementDesignProperty / IsManagementProperty / IsRuntimeDerivedProperty 标识
```

### 3.4 验证结果

- ✅ 113 个 HTML 元素全部遍历验证，每个元素的每个 HTML 属性都有完整标记
- ✅ 管理属性不标记 `IsFillRequired`，不会被错误填充
- ✅ 设计属性不标记 `IsFillRequired`
- ✅ 运行时派生属性不标记 `IsFillRequired`

---

## 第四部分：六槽位模型

### 4.1 槽位结构

每个属性同时保存源端（HTML/DOM）和目标端（XAML/WinUI）各三个槽位：

| 端                    | Initialization       | Link                            | Runtime          |
| --------------------- | -------------------- | ------------------------------- | ---------------- |
| **Source (HTML/DOM)** | HTML 属性/CSS 声明值 | 语义链接（布局绑定/CSS 表达式） | 浏览器计算绝对值 |
| **Xaml (WinUI)**      | XAML 初始属性值      | Binding/资源描述                | WinUI 运行绝对值 |

### 4.2 值来源枚举

| 来源                       | 含义         | 使用场景                                                            |
| -------------------------- | ------------ | ------------------------------------------------------------------- |
| `DirectConstant`           | 直接常量     | HTML 写死值（`color: red`）                                         |
| `LinkedConstant`           | 链接常量     | CSS 变量引用（`var(--primary)`）                                    |
| `LinkedCalculation`        | 链接计算     | Binding、表达式（`calc()`）                                         |
| `ContainerAutomaticLayout` | 容器自动布局 | 百分比（`width: 100%`）、Flex/Grid（必须带 ContainerLayoutBinding） |

### 4.3 链接类型枚举

| 链接类型            | 含义         | 示例                                       |
| ------------------- | ------------ | ------------------------------------------ |
| `None`              | 无链接       | 直接常量                                   |
| `CssExpression`     | CSS 表达式   | `var(--color)`, `calc(50% - 20px)`         |
| `ContainerLayout`   | 布局绑定     | `width: 100%`（带 ContainerLayoutBinding） |
| `XamlBinding`       | XAML Binding | `{Binding Width}`                          |
| `LayoutExpression`  | 布局表达式   | flex, grid                                 |
| `Url`               | URL 链接     | href, src                                  |
| `DomDescription`    | DOM 描述     | 原始属性字符串                             |
| `ConstantReference` | 常量引用     | StaticResource                             |

### 4.4 槽位填充流程

```
DomFillAsync(queryEngine)
  ↓
反射遍历所有 IsFillRequired 属性
  ↓
对每个属性值（IDomFillSlotOwner）：
  ↓
遍历 owner.DomSlots → [Initialization, Link, Runtime]
  ↓
构造强类型查询身份：
documentScope + xpath + reflectedPropertyName + ownerKind
+ propertyName + category + evidenceKind + slot
  ↓
生成并验证 ElementSlotTraversalDescriptor
  ↓
调用引擎 QueryAsync(context) → DomPropertyQueryResult
  ↓
owner.ApplyDomQueryResult(slot, result) → 写入对应槽位

XamlFillAsync 在 XAML 控件生成后执行，填充 XamlInitialization/XamlLink/XamlRuntime
```

### 4.5 查询结果状态

| 状态                | 处理方式                   |
| ------------------- | -------------------------- |
| `Captured`          | 将 Value/Link 写入对应槽位 |
| `ConfirmedAbsent`   | 明确清除该槽位的旧值       |
| `SourceUnsupported` | 不修改槽位，保留未知状态   |

重复填充时，`ConfirmedAbsent` 与 `SourceUnsupported` 的语义不同。前者证明当前状态没有值，
必须清除上一状态留下的值；后者只说明当前查询源不支持该属性，不能用来覆盖已有证据。

### 4.6 属性类型覆盖

| 属性类型                                                                           | DomSlots             | 槽位数 |
| ---------------------------------------------------------------------------------- | -------------------- | ------ |
| `DomElementStringProperty`（HTML 属性）                                            | Init, Link, Runtime  | 3      |
| `DomElementRuntimeProperty`（运行时属性）                                          | Init, Link, Runtime  | 3      |
| `DomElementEvent`（事件属性）                                                      | Init, Link, Runtime  | 3      |
| `DomElementDataSource`（数据源属性）                                               | Init, Link, Runtime  | 3      |
| `List<T>`（集合属性：ExtensionAttributes, RuntimeProperties, Events, DataSources） | 遍历每个元素的三槽位 | 3 × N  |

---

## 第五部分：自动遍历填充机制

### 5.1 反射遍历

`ElementPropertyTraitsReflector.GetAttributes(Type)` 使用：

```csharp
type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
```

- **不使用 `DeclaredOnly`**，遍历整个继承链
- 结果缓存到 `ConcurrentDictionary<Type, ...>`，性能良好
- 子类新增属性自动纳入，无需修改基类代码
- `ValidateTraversalContract()` 反向扫描全部公开槽位属性；公开槽位漏加特性时立即失败
- 每个 owner 必须完整公开 DOM/XAML 各三个槽位，并具有非歧义类别
- `TraversalDeclaration` 明确声明 `Static` 或 `OwnerResolved`
- 静态声明保留在 Traits；每次实际执行生成独立的
  `ElementSlotTraversalDescriptor`，包含元素身份、强属性身份、类别、证据类型和 Traits
- OwnerResolved 属性的静态路由字段使用 `Unspecified` 哨兵不是“漏填”，
  其完整路由必须由运行时描述符提供并通过 `Validate()`

### 5.2 填充接口

```csharp
// DOM 端兼容委托；只适用于没有同名 owner 的简单调用
public delegate ValueTask<DomPropertyQueryResult> DomPropertyQueryDelegate(
    string documentScope, string xpath, string propertyName, DomPropertyDataSlot slot);

// XAML 端查询委托
public delegate ValueTask<XamlPropertyQueryResult> XamlPropertyQueryDelegate(
    string documentScope, string xpath, string propertyName, XamlPropertyDataSlot slot);

// 正式查询引擎接口；上下文携带强类型 owner 身份
public interface IDomPropertyQueryEngine { ... }
public interface IXamlPropertyQueryEngine { ... }
```

### 5.3 三种填充方式

#### 方式 1：设置引擎（推荐）

```csharp
var element = new HtmlButtonDomElement(mapping);
element.DomQueryEngine = new WebView2DomPropertyFiller(webView2);
await element.DomFillAsync();  // 自动使用引擎，递归填充整棵树
```

#### 方式 2：传入委托（兼容入口）

```csharp
await element.DomFillAsync((scope, xpath, name, slot) =>
{
    if (name == "id" && slot == DomPropertyDataSlot.Initialization)
        return ValueTask.FromResult(DomPropertyQueryResult.DirectConstant("my-id"));
    return ValueTask.FromResult(DomPropertyQueryResult.ConfirmedAbsent(""));
});
```

#### 方式 3：子类重写（非密封基类）

```csharp
protected override DomPropertyQueryDelegate? CreateDefaultDomQueryDelegate()
{
    return (scope, xpath, name, slot) => { /* 元素特有逻辑 */ };
}
```

### 5.4 基类 API

| API                                                           | 说明                                        |
| ------------------------------------------------------------- | ------------------------------------------- |
| `DomQueryEngine`                                              | 可设置的 DOM 查询引擎                       |
| `XamlQueryEngine`                                             | 可设置的 XAML 查询引擎                      |
| `XamlElement`                                                 | XAML 控件挂载引用（`object?`，UI 框架中立） |
| `CreateDefaultDomQueryDelegate()`                             | `protected virtual`，子类可重写             |
| `CreateDefaultXamlQueryDelegate()`                            | `protected virtual`，子类可重写             |
| `DomFillAsync(CancellationToken)`                             | 无参数填充，使用默认引擎/委托               |
| `XamlFillAsync(CancellationToken)`                            | 无参数填充，使用默认引擎/委托               |
| `DomFillAsync(DomPropertyQueryDelegate, CancellationToken)`   | 使用外部委托填充                            |
| `DomFillAsync(IDomPropertyQueryEngine, CancellationToken)`    | 使用强类型 DOM 查询上下文填充               |
| `XamlFillAsync(XamlPropertyQueryDelegate, CancellationToken)` | 使用外部委托填充                            |
| `XamlFillAsync(IXamlPropertyQueryEngine, CancellationToken)`  | 使用完整执行描述、六槽证据和控件引用填充    |
| `ValidateTraversalContract()`                                 | 反向验证特性、槽位和 owner 强身份            |
| `Audit()`                                                     | 递归审核整棵树                              |
| `ToXaml(TextWriter)`                                          | 生成 XAML 字符串                            |

---

## 第六部分：WebView2 集成

### 6.1 WebView2DomPropertyFiller

`Iwesun.Runtime.WebView2.WebView2DomPropertyFiller` 实现了 `IDomPropertyQueryEngine`，
作为缺省委托直接使用 WebView2 API：

| 槽位           | 实现方式          | JS API                                                             |
| -------------- | ----------------- | ------------------------------------------------------------------ |
| Initialization | HTML attribute 值 | `element.getAttribute()`, `element.hasAttribute()`                 |
| Link           | 语义链接          | `getComputedStyle()` 检测百分比/vw/vh/flex/grid/var()/calc()       |
| Runtime        | 运行时计算值      | `getBoundingClientRect()`, `getComputedStyle()`, DOM property 访问 |

### 6.2 支持的属性类型

| 属性类别        | 示例                                                             | 槽位           |
| --------------- | ---------------------------------------------------------------- | -------------- |
| HTML attributes | `id`, `class`, `href`, `disabled`                                | Initialization |
| 布尔属性        | `hidden`, `checked`, `readonly`, `required`                      | Initialization |
| 几何属性        | `rect.x`, `rect.y`, `rect.width`, `rect.height`                  | Runtime        |
| 滚动/尺寸       | `scrollLeft`, `scrollTop`, `clientWidth`, `offsetHeight`         | Runtime        |
| 计算样式        | `style.color`, `style.fontSize`, `style.margin`, `style.padding` | Runtime        |
| 表单属性        | `value`, `checked`, `selectedIndex`                              | Runtime        |
| 媒体属性        | `naturalWidth`, `currentTime`, `duration`, `paused`              | Runtime        |
| 布局链接        | 百分比/vw/vh/flex/grid                                           | Link           |
| CSS 表达式      | `var()`, `calc()`, `min()`, `max()`                              | Link           |

### 6.3 使用示例

```csharp
// 绑定 WebView2 填充器到整棵树
root.BindWebView2Filler(webView2.CoreWebView2);

// 一次调用填充所有属性
await root.DomFillAsync();

// 审核填充结果
var auditReport = root.Audit();
Console.WriteLine(auditReport.TextReport);
```

### 6.4 完整流程

```
1. 构建 DomElement 树（从 XPath 映射）
2. 绑定 WebView2DomPropertyFiller
3. 调用 DomFillAsync() → 填充 DOM 端三槽位
4. 调用 ToXaml() → 生成 XAML 字符串/控件树
5. 设置 XamlElement 引用 → 绑定 XAML 控件
6. 调用 XamlFillAsync() → 填充 XAML 端三槽位
7. 调用 Audit() → 审核源端和 XAML 端值是否一致
```

---

## 第七部分：审核系统

### 7.1 审核过程

元素公开 `Audit()` 自动审核整棵子树。后序遍历：容器先递归审核全部子元素，最后审核自身。

```csharp
var report = rootElement.Audit();
// report.Passed: 所有子元素和自身是否通过
// report.Statistics: 结构化统计（通过/失败/缺失映射/人工复核数）
// report.TextReport: 可读文本报告
```

### 7.2 数值属性审核

使用 `NumericElementPropertyAuditor`：

- 源值按 `SourceToXamlScale` 缩放到 XAML 单位
- 误差容忍：`Math.Max(AbsoluteTolerance, Math.Max(expected, actual) × RelativeTolerance)`
- 在误差内通过，超出失败
- 源单位/XAML 单位不匹配则失败
- 缺少值则标记为 MissingSourceValue/MissingXamlValue

### 7.3 Traits 驱动的语义审核

`ElementPropertySemanticComparer` 统一执行属性 Traits 中的比较策略：

- `Exact`：序数精确比较
- `Semantic`：按值类型执行空白、大小写和枚举语义归一化
- `NumericTolerance` / `GeometryTolerance`：解析复合数值并执行逐属性容差
- `NormalizedColor`：统一 CSS RGBA 与 XAML ARGB 表达
- `ResourceIdentity`：统一 URI 转义、分隔符和绝对资源身份
- CSS px 与 DIP 作为逻辑布局单位比较；物理像素不在缺少 DPI 证据时猜测换算
- DOM 无值而 XAML 有值必须报告 `UnexpectedXamlValue`
- `event.*` 使用独立的 XAML 执行描述符；注册、链接、调用次数和路由身份
  均进入类型化事件槽位，未发生调用时不伪造运行证据

### 7.4 审核分类

| 状态                 | 含义                     |
| -------------------- | ------------------------ |
| `Passed`             | 值在允许误差内一致       |
| `ValueMismatch`      | 有值但超出误差           |
| `MissingSourceValue` | 缺少 DOM 源值            |
| `MissingXamlValue`   | 缺少 XAML 值             |
| `SourceUnitMismatch` | 源单位不预期             |
| `XamlUnitMismatch`   | XAML 单位不预期          |
| `ManualReview`       | 无法自动比较，需人工判断 |
| `NotRequired`        | 该阶段未启用审核         |

---

## 第八部分：测试覆盖

### 8.1 测试统计

| 类别          | 测试数  | 覆盖内容                       |
| ------------- | ------- | ------------------------------ |
| 元素类型目录  | 4       | 113 HTML + 14 SVG 注册完整性   |
| 元素架构审核  | 4       | 属性/事件/XAML 投影完整性      |
| 容器布局合同  | 6+      | 百分比/锚点/Flex/对齐验证      |
| DOM 元素树    | 5+      | 父子关系、XPath、循环检测      |
| 槽位属性      | 18      | 五类属性、数值审核、字符串属性 |
| XAML 槽位处理 | 13      | XAML 端三槽位填充              |
| 查询引擎/委托 | 9       | 引擎设置、子类重写、委托传入   |
| 属性特性标记  | 27      | 113 元素 × 完整标记验证        |
| 反射遍历      | 7       | 继承链属性遍历验证             |
| 全面覆盖      | 8       | 所有元素类型端到端验证         |
| XAML 元素引用 | 5       | XamlElement 挂载引用           |
| 继承体系      | 11      | 子类继承、公共属性             |
| 其他          | ~33     | 事件、数据源、缓存等           |
| **总计**      | **167** | Debug + Release 均 0 失败      |

### 8.2 运行命令

```powershell
cd D:\Git Space\Runtime\modules\Web
dotnet test Iwesun.Runtime.Web.slnx -c Debug
dotnet test Iwesun.Runtime.Web.slnx -c Release
```

---

## 第九部分：文件索引

| 文件                                          | 职责                                           |
| --------------------------------------------- | ---------------------------------------------- |
| `Elements/DomElement.cs`                      | 抽象根类型，树管理，Fill/Audit/ToXaml 核心逻辑 |
| `Elements/DomElementFamilies.cs`              | HTML 基类，36 个全局属性                       |
| `Elements/DomElementSemanticFamilies.cs`      | 语义中间基类（布局/文本/表单控制等）           |
| `Elements/StandardHtmlDomTextElements.cs`     | 文档/文本/标题元素                             |
| `Elements/StandardHtmlDomListsAndMetadata.cs` | 列表/超链接/元数据元素                         |
| `Elements/StandardHtmlDomFormsTablesMedia.cs` | 表单/表格/媒体/嵌入元素                        |
| `Elements/StandardSvgDomElements.cs`          | SVG 元素                                       |
| `Elements/HtmlDomElementTypeCatalog.cs`       | 元素工厂和类型目录                             |
| `Elements/HtmlElementAttributeCatalog.cs`     | 属性白名单目录                                 |
| `Elements/HtmlDomAttributeProperty.cs`        | 属性定义和语法分类                             |
| `Elements/HtmlEventHandlerCatalog.cs`         | 事件处理器目录（115 个）                       |
| `Properties/ElementPropertyTraits.cs`         | 特性标记和反射读取器                           |
| `Properties/ElementSlottedProperty.cs`        | 六槽位基类和链接类型                           |
| `Properties/CategorizedSlottedProperties.cs`  | 五类属性分类基类                               |
| `Properties/NumericSlottedProperty.cs`        | 数值属性（带偏差审核）                         |
| `Auditing/NumericPropertyAuditor.cs`          | 数值审核算法                                   |
| `Layout/ContainerLayoutBinding.cs`            | 强类型布局绑定                                 |
| `Layout/ContainerLayoutBindingValidator.cs`   | 布局绑定验证                                   |
| `Inheritance/ElementInheritanceService.cs`    | 样式继承服务                                   |

### WebView2 集成文件

| 文件                                                                | 职责                    |
| ------------------------------------------------------------------- | ----------------------- |
| `modules/WebView2/src/.../WebRuntimeDomPropertyFiller.cs`           | WebView2 DOM 属性填充器 |
| `modules/WebView2/src/.../WebRuntimeDomPropertyFillerExtensions.cs` | 绑定扩展方法            |

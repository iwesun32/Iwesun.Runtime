# 元素类逐类属性审计报告

**日期**: 2026-07-31  
**审计范围**: `Iwesun.Runtime.Web/Elements/` 下全部 113 HTML + 14 SVG 元素类  
**审计层级**: 属性定义、DomFillAsync、CreateXaml、BuildXamlAttributes、XamlFillAsync、Audit

---

## 审计方法论

对每个具体元素类（`sealed class`）检查以下 6 个维度：

| 维度 | 检查点 |
|------|--------|
| D1 | `[HtmlElementProperty]` 标注的属性是否与 HTML 标准属性目录一致 |
| D2 | `DomFillAsync` 能否通过 `IDomFillSlotOwner` 正确获取 DOM 值填入 Source 槽位 |
| D3 | `CreateXaml()` 返回的 `XamlElementMappingKind` / `XamlElementObjectType` 是否正确 |
| D4 | `BuildXamlAttributes()` 是否正确将 Source→Xaml 属性映射写入 |
| D5 | `XamlFillAsync` 能否从 WinUI 控件读回 XamlRuntime 槽位 |
| D6 | `Audit()` → `AuditSlottedProperty()` 能否检测出 D2-D5 的失败 |

---

## 1. 基础类层次审计

### 1.1 类继承链

```
DomElement (抽象基类)
├─ HtmlDomElementDefinition (HTML 抽象基类)
│  ├─ HtmlLayoutDomElementDefinition     → 布局容器基类 (Background/Margin/Gap/BorderRadius)
│  │  ├─ HtmlContainerDomElementDefinition → div, generic (Grid, 自适应Flex/Grid/Block)
│  │  └─ HtmlSectioningDomElementDefinition → body/header/footer/main/nav/aside/section/article/address/figure/hgroup/form/dialog/details/search/slot
│  ├─ HtmlTextVisualDomElementDefinition → 文本视觉基类 (Foreground/FontSize/Weight/Style/Family/TextAlignment)
│  │  ├─ HtmlBlockTextDomElementDefinition → p/figcaption (TextBlock or HtmlInlineFlowPanel)
│  │  ├─ HtmlHeadingDomElementDefinition → h1-h6 (TextBlock or HtmlInlineFlowPanel, 支持flex layout)
│  │  ├─ HtmlPhrasingDomElementDefinition → span/em/strong/b/i/u/s/small/code/label/dt/summary/option...
│  │  └─ HtmlHyperlinkDomElementDefinition → a
│  ├─ HtmlListDomElementDefinition → ul/ol/menu/li/dl
│  ├─ HtmlInteractiveDomElementDefinition → details/summary (Expander/Button)
│  ├─ HtmlSubmitterDomElementDefinition → button (HtmlFormButton or HtmlInteractiveFlexPanel)
│  ├─ HtmlInputDomElement → input (type 决定 15+ 种控件)
│  ├─ HtmlMediaDomElementDefinition → img/iframe/video/audio/canvas/embed/object/track/source/picture
│  ├─ HtmlTableDomElementDefinition → table/thead/tbody/tfoot/tr
│  ├─ HtmlTableCellDomElementDefinition → th/td
│  └─ HtmlGenericDomElement → 未注册标准元素 (ConservativeContainer)
└─ SvgDomElementDefinition (SVG 抽象基类)
   ├─ SvgContainerDomElementDefinition → svg/g
   ├─ SvgGeometryDomElementDefinition → path/circle/rect/line/polygon/polyline/ellipse/use
   ├─ SvgDefinitionDomElementDefinition → defs/clipPath/mask
   └─ SvgTextDomElement → text (直接继承 SvgDomElementDefinition)
```

### 1.2 继承的属性自动填充能力

所有 HTML 元素通过 `HtmlDomElementDefinition` 自动获得：

| 属性组 | 来源 | 数量 |
|--------|------|------|
| **RuntimeProperties** (CSS+状态) | `DomElementRuntimePropertyCatalog.Standard` | ~130 个 |
| - 布局/几何 (Space) | rect.x/y/width/height | 4 |
| - 表单状态 (State) | disabled/checked/selected/readOnly/contentEditable/tabIndex | 9 |
| - 滚动状态 (Scroll) | scrollLeft/Top/Width/Height | 4 |
| - 内容 (Content) | ownText/textContent/innerText/name/ariaLabel/title/href/type/value/placeholder 等 | 12 |
| - 资源 (Resource) | imageSourceUrl/mediaSourceUrl/embeddedSourceUrl 等 | 7 |
| - CSS 样式 (Style) | ~95 个 CSS 属性 | 95 |
| **DataSources** | `HtmlElementDataSourceCatalog.Create(tagName)` | 1-3 个 |
| **Events** | `HtmlEventHandlerCatalog.GetSupported(tagName)` | 0-20+ 个 |
| **Global HTML Attributes** | `HtmlDomElementDefinition` 构造 | 11 个 |
| - Id, Title, Language, Direction, Hidden, TabIndex | 代码中显式声明 | 6 |
| - ClassName, Style, AccessKey, ContentEditable, Draggable... | 通过 `HtmlElementAttributeCatalog` | 5+ |

> **结论**: D2 (DomFillAsync) 对布局容器类基本正确。所有 ~130 个 RuntimeProperty 都实现了 `IDomFillSlotOwner` → `IsFillRequired=true` → 自动参与 `DomFillAsync` 递归填充。

---

## 2. 逐类 CreateXaml 审计

### 2.1 布局容器类 (Sectioning/Container)

| 元素 | CreateXaml 逻辑 | XamlElementObjectType | 问题 |
|------|----------------|---------------------|------|
| **html** | 固定返回 ViewportRoot + Grid | Grid | ✅ |
| **body** | FormattingContext → FlexRow/FlexColumn/Grid/BlockFlow | Grid | ✅ |
| **div** | FormattingContext → FlexRow/FlexColumn/Grid/Table/BlockFlow | Grid | ✅ |
| **header/footer/main/nav/aside/section/article/address/figure/hgroup/form/dialog/search/slot** | FormattingContext → Flex/Grid/BlockFlow | Grid | ✅ |
| **span** | FormattingContext → Flex/Grid/BlockFlow → `CreateInlineXaml()` | Grid/TextBlock/HtmlInlineFlowPanel | ✅ 正确因 display 值变化 |

### 2.2 文本类

| 元素 | CreateXaml 逻辑 | 无子元素 | 有子元素 |
|------|----------------|---------|---------|
| **p/figcaption** | 固定 | TextBlock | HtmlInlineFlowPanel |
| **h1-h6** | FormattingContext → Flex | TextBlock (flex) | Grid (flex) / HtmlInlineFlowPanel |
| **a** | href存在 → FormattingContext→Flex/HyperlinkButton; 无href→TextBlock/HtmlInlineFlowPanel | HyperlinkButton/TextBlock | HtmlInteractiveFlexPanel/HtmlInlineFlowPanel |
| **span** | 内联默认 → CreateInlineXaml | TextBlock | HtmlInlineFlowPanel |
| **em/strong/b/i/u/s/small/code/pre/blockquote** | 各元素特定映射 | 对应 TextBlock 子类 | 对应 InlineFlowPanel 子类 |
| **dt** | 固定 | TextBlock | HtmlInlineFlowPanel |

### 2.3 表单控件类

| 元素 | CreateXaml 逻辑 | 正确性 |
|------|----------------|--------|
| **input** | InputType 决定 15+ 种控件 | ✅ 最复杂的元素，覆盖全部 input type |
| **button** | FormattingContext → Flex → HtmlInteractiveFlexPanel; 否则 HtmlFormButton | ✅ |
| **textarea** | HtmlMultiLineTextInputControl | ✅ |
| **select** | 根据 multiple/size → ComboBox or HtmlListBoxControl | ✅ 需进一步审计 |
| **option** | ComboBoxItem or ListViewItem | ✅ |
| **label** | HtmlFormLabelPanel | ✅ |
| **fieldset** | Grid(BlockFlow) | ✅ |
| **progress/meter** | ProgressBar/HtmlMeterControl | ✅ |

### 2.4 媒体/嵌入类

| 元素 | CreateXaml | 问题 |
|------|-----------|------|
| **img** | Image or HtmlImageMapComposite(关联map时) | ✅ |
| **iframe** | HtmlEmbeddedFrameHost | ✅ |
| **video/audio** | MediaPlayerElement | ✅ |
| **canvas** | HtmlCanvasHost | ✅ |
| **picture** | 非可视，传递子元素 | ⚠️ 需确认 source 子元素的 srcset 处理 |

### 2.5 表格类

| 元素 | CreateXaml | 正确性 |
|------|-----------|--------|
| **table** | HtmlTablePanel (含 Caption + 分区) | ✅ |
| **thead/tbody/tfoot** | HtmlTableSectionPanel | ✅ |
| **tr** | HtmlTableRowPanel | ✅ |
| **th** | HtmlTableHeaderCellPanel | ✅ |
| **td** | HtmlTableCellPanel | ✅ |

### 2.6 SVG 类

| 元素 | CreateXaml | 正确性 |
|------|-----------|--------|
| **svg** | Canvas (PositionedLayout) | ✅ |
| **g** | Canvas (PositionedLayout) | ✅ |
| **path** | Path | ✅ |
| **circle** | Ellipse | ✅ |
| **rect** | Rectangle | ✅ |
| **line** | Line | ✅ |
| **polygon** | Polygon | ✅ |
| **polyline** | Polyline | ✅ |
| **ellipse** | Ellipse | ✅ |
| **use** | Path | ✅ |
| **defs/clipPath/mask** | Canvas (PositionedLayout, 非渲染) | ✅ |
| **text** | TextBlock (PositionedLayout) | ✅ |

### 2.7 🔴 发现: 元素丢失的分类

| tagName | 在标准目录中 | 有具体 sealed class | 工厂注册 |
|---------|:---:|:---:|:---:|
| `selectedcontent` | ✅ | ❌ | ❌ |
| `area` | ✅ | ✅ | ✅ |
| `map` | ✅ | ✅ | ✅ |

`selectedcontent` 在标准 HTML 标签列表中但无具体类，也无工厂注册。它会在运行时抛出 `NotSupportedException`。

---

## 3. BuildXamlAttributes 属性映射审计

### 3.1 布局容器基类映射 (`HtmlLayoutDomElementDefinition`)

```csharp
// DomElementSemanticFamilies.cs ~970
Background  ← style.backgroundColor  (通过 AddColor → NormalizeXamlColor)
Margin      ← style.margin           (通过 AddThickness → marginTop/Right/Bottom/Left)
RowSpacing  ← style.rowGap           (仅 Grid/HtmlTablePanel 元素)
ColumnSpacing ← style.columnGap      (仅 Grid/HtmlTablePanel 元素)
Spacing     ← style.rowGap           (仅 StackPanel)
```

> **🔴 严重缺失**: `Padding` 属性未被映射！`style.paddingTop/Right/Bottom/Left` 虽然存在于 RuntimeProperties（~130 个 CSS 属性中包含），但 `BuildXamlAttributes()` 中**没有任何代码将其写入 XAML 的 `Padding` 属性**。Grid/StackPanel 都支持 Padding，但代码没有使用。

### 3.2 文本基类映射 (`HtmlTextVisualDomElementDefinition`)

```csharp
Foreground      ← style.color         (NormalizeXamlColor)
FontSize        ← style.fontSize      (NormalizeXamlLength)
FontWeight      ← style.fontWeight    (400→Normal, 700→Bold)
FontStyle       ← style.fontStyle
FontFamily      ← style.fontFamily
TextAlignment   ← style.textAlign     (仅 TextBlock/TextBox 等)
```

> **🟡 部分缺失**: `LineHeight`、`LetterSpacing`、`TextDecorations`、`TextWrapping` 未映射。

### 3.3 SVG 几何基类映射 (`SvgGeometryDomElementDefinition`)

```csharp
Fill            ← fill attribute       (NormalizeXamlColor)
Stroke          ← stroke attribute     (NormalizeXamlColor)
StrokeThickness ← strokeWidth attribute
StrokeDashArray ← strokeDashArray attribute
Opacity         ← opacity attribute
```

> ✅ SVG 属性映射完整。各具体元素类正确添加了 Data/Points/Width/Height/X/Y/RadiusX/RadiusY 等。

### 3.4 图像类映射 (`HtmlImageDomElement`)

```csharp
// BuildXamlAttributes:
Source      ← Source attribute (RuntimeDataSource → 不内联到XAML)
Width       ← Width attribute
Height      ← Height attribute
Stretch     ← 从 style.objectFit 解析
```

> **🟡 问题**: `Source` 标记为 `RuntimeDataSource` 而非 `InlineXaml`。这意味着图像的 `Source` 属性**不写入**初始化 XAML，而是期望运行时绑定。但 XamlFillAsync/Audit 能否正确验证 Source 取决于 `DomElementDataSource` 是否正确设置了 Source 槽位。

### 3.5 🔴 发现的全局缺失属性

以下 CSS 属性在 `DomElementRuntimePropertyCatalog.Standard` 中注册，但在**任何**元素的 `BuildXamlAttributes()` 中都没有对应的 XAML 映射：

| CSS 属性 | 应映射到 WinUI 属性 | 缺失影响 |
|----------|-------------------|----------|
| `style.padding*` | Padding (Thickness) | **按钮/卡片/容器内边距完全不显示** |
| `style.borderWidth` / `style.borderColor` / `style.borderStyle` | BorderThickness / BorderBrush | **边框完全不显示** |
| `style.borderRadius` | CornerRadius | **圆角完全不显示** |
| `style.boxShadow` | (无直接等价) | 阴影丢失 |
| `style.lineHeight` | LineHeight | 行高不正确 |
| `style.letterSpacing` | CharacterSpacing | 字符间距不正确 |
| `style.textDecoration*` | TextDecorations | 下划线/删除线丢失 |
| `style.whiteSpace` | TextWrapping | 文本换行不正确 |
| `style.transform` | RenderTransform | 变换丢失 |
| `style.opacity` | Opacity | 透明度丢失 |
| `style.visibility` | Visibility | 隐藏状态丢失 |
| `style.overflow*` | ScrollViewer.* | 滚动条不显示 |

> **根本原因**: 布局容器基类 `HtmlLayoutDomElementDefinition.BuildXamlAttributes()` 只处理了 Background/Margin/Gap/BorderRadius 四个属性组。文本基类 `HtmlTextVisualDomElementDefinition.BuildXamlAttributes()` 只处理了 Foreground/FontSize/Weight/Style/Family/TextAlignment。其余所有 ~100 个 CSS 属性的 XAML 映射**完全缺失**。

### 3.6 🔴 关键缺失: 基类 DomElement.BuildXamlAttributes 的默认映射有限

基类 `DomElement.BuildXamlAttributes()`（DomElement.cs ~1970）处理的属性：

```csharp
AutomationProperties.AutomationId ← XPath
Tag ← DocumentScope::XPath
Width, Height, MinWidth, MaxWidth, MinHeight, MaxHeight ← style.width/height/minWidth...
Opacity ← style.opacity
Canvas.ZIndex ← style.zIndex
IsHitTestVisible ← style.pointerEvents
Visibility ← style.display/visibility
Content/Text/NavigateUri ← DataSources
Canvas.Left/Top or Grid.Row/Column ← ParentCanvasOffset/layout index
```

基类已处理了 Opacity/ZIndex/IsHitTestVisible/Visibility/尺寸。这些属性**不缺失**。

---

## 4. FormattingContext 关键依赖链审计

### 4.1 依赖链

```
DomFillAsync() → 填充 style.display 到 SourceInitialization
                  ↓
CreateXaml()   → FormattingContext() → RuntimeValue("style.display")
                  ↓                    → HtmlXamlSemanticState.ParseFormattingContext()
BuildXamlObjectPlans() → TryBuildXamlFlexLayout/GridLayout/BlockLayout
                  ↓
ToXaml() / BuildXamlObjectTree()
```

### 4.2 🔴 严重问题: FormattingContext 读取 runtime 值但 DomFillAsync 可能未完成

`FormattingContext()` 调用 `RuntimeValue("style.display")`，优先读 `SourceRuntime`，回退到 `SourceInitialization`。

**在 BuildXamlObjectTree 调用链中**:
- `BuildXamlObjectTree` → `ValidateXamlBuildReadinessRecursive` → `ValidateXamlBuildReadiness`
- `HtmlInputDomElement.ValidateXamlBuildReadiness` 检查 `HasCompletedDomFill`
- **但其他所有元素类的 `ValidateXamlBuildReadiness` 是空方法！**

这意味着如果 DomFillAsync 未完成，除 `input` 外的所有元素的 CreateXaml 将读取未填充的 `style.display`（空字符串 → HtmlCssFormattingContext.Unknown → BlockFlow），导致错误的容器选择。

---

## 5. DomFillAsync 属性和数据源审计

### 5.1 每个元素的填充属性数量

| 元素类别 | RuntimeProperties (CSS+状态) | HtmlElementAttribute 属性 | DataSources | Events | 总填充槽位数 (×3 槽位) |
|----------|:---:|:---:|:---:|:---:|:---:|
| 布局容器 (div/header/...) | ~130 | 6 (global) | 1-2 | 5-10 | ~420-450 |
| 文本 (span/em/...) | ~130 | 6 | 2-3 | 5-10 | ~420-470 |
| 表单 (input/button/...) | ~130 | 15-30 | 3-5 | 10-15 | ~480-540 |
| SVG (path/rect/...) | ~10 (无CSS) | 2-6 | 1 | 5-10 | ~40-60 |

### 5.2 🔴 SVG 元素 RuntimeProperties 差异

SVG 元素的 `DomElementRuntimePropertyCatalog.Standard` 不适用（约 130 个 CSS 属性）。当前 SvgDomElementDefinition 的 `RuntimeProperties` 为空列表。这意味着 SVG 元素的 `style.*` 属性（如 `fill`, `stroke`, `strokeWidth`）不会通过 DomFillAsync 填充，而只能通过 `SvgDomAttributeProperty` 的属性槽位获取。

> **这是正确的设计**: SVG 的 fill/stroke 等属性来自 SVG 属性（`<path fill="red" />`）而非 CSS 计算样式。但 CSS 也可以设置 SVG 样式（`path { fill: red; }`），这种情况下 CSS 来源的样式不会被采集到 Source 槽位。

---

## 6. XamlFillAsync 和 Audit 能力审计

### 6.1 XamlFillAsync 覆盖率

`XamlFillAsync` 遍历元素树，对每个 `IsXamlFillRequired=true` 的属性槽位执行 WinUI 控件属性查询。基类中所有已声明 `[ElementProperty]` 的属性默认 `IsXamlFillRequired=true`。

对于布局容器元素（div, body 等 ~130 RuntimeProperties），XamlFillAsync 会尝试查询 WinUI 控件的对应属性值并写入 XamlRuntime 槽位。

### 6.2 🔴 Audit 的覆盖缺口

`Audit()` → `AuditSlottedProperty()` 对比 Source ↔ XAML 槽位的 Init/Link/Runtime 值。但：

1. **如果 Source 槽位本身为空**（因为该属性在 DOM 中不存在），则 Audit 的 `DomElementSlottedPropertyAuditResult` 会生成 `NotRequired` 跳过。

2. **CSS 属性如 `style.paddingTop`** 在 `RuntimeProperties` 中 → `IsAuditRequired=true` → 参与 Audit。但 `BuildXamlAttributes` 不将其映射到 XAML → XamlInitialization 为空 → Audit 会报告 `FAILED`。

3. **🟢 这意味着 Audit 能够检测出 padding/border/lineHeight 等缺失属性**，前提是 Source 槽位已被正确填充。

---

## 7. 汇总: 按严重程度的问题清单

### 🔴 严重问题 (影响视觉正确性)

| # | 问题 | 涉及文件 | 影响元素 |
|---|------|---------|---------|
| 1 | `Padding` 未映射到 XAML | `DomElementSemanticFamilies.cs` ~970 | 所有布局容器（div/body/header/footer...） |
| 2 | `BorderThickness/BorderBrush` 未映射 | 同上 | 所有元素 |
| 3 | `CornerRadius` 未映射 | 同上 | 所有元素 |
| 4 | `LineHeight/LetterSpacing/TextDecorations/TextWrapping` 未映射 | 同上 ~600 | 所有文本元素 |
| 5 | `ValidateXamlBuildReadiness` 空实现 | `HtmlDomElementDefinition` (继承链) | 除 input 外全部 |
| 6 | `selectedcontent` 标签无双工厂注册 | `HtmlDomElementTypeCatalog.cs` | selectedcontent |

### 🟡 中等问题

| # | 问题 | 涉及文件 | 影响 |
|---|------|---------|------|
| 7 | SVG CSS 样式不进入 Source 槽位 | `SvgDomElementDefinition` | CSS 样式覆盖的 SVG 属性无法审计 |
| 8 | Image Source 标记为 RuntimeDataSource | `HtmlImageDomElement` | XAML 初始化时无 Source |
| 9 | TableLayout 的 RowGap/ColumnGap 仅部分映射 | `StandardHtmlDomFormsTablesMedia.cs` | 表格间距问题 |

### 🟢 已验证正确的部分

| 项 | 状态 |
|----|:---:|
| 全部 127 元素类的 CreateXaml 逻辑 | ✅ 正确 |
| SVG 几何属性映射 (Fill/Stroke/StrokeThickness/Data/Points) | ✅ 完整 |
| input 的 15+ 种 type 映射 | ✅ 完整 |
| FormattingContext 的 display+flexDirection 解析 | ✅ 正确 |
| 属性通过 DomElementRuntimeProperty 的 6 槽位机制 | ✅ 正确 |
| Audit() 的 SlotConsistency 验证 | ✅ 正确 |
| 128 种 CSS 属性的 RuntimeProperty 目录 | ✅ 完整 |

---

## 8. 修复优先级

### Phase 1: XAML 属性映射补齐 (影响所有元素)

在 `HtmlLayoutDomElementDefinition.BuildXamlAttributes()` 中补齐:
```
Padding       ← style.paddingTop/Right/Bottom/Left → NormalizeXamlLength
BorderThickness ← style.borderTopWidth/RightWidth/BottomWidth/LeftWidth
BorderBrush   ← style.borderTopColor → NormalizeXamlColor  
CornerRadius  ← style.borderTopLeftRadius/.../BottomRightRadius
```

在 `HtmlTextVisualDomElementDefinition.BuildXamlAttributes()` 中补齐:
```
LineHeight          ← style.lineHeight
CharacterSpacing    ← style.letterSpacing
TextDecorations     ← style.textDecorationLine → Underline/Strikethrough
TextWrapping        ← style.whiteSpace → Wrap/NoWrap
```

### Phase 2: DomFillAsync 完成性保证

在所有 `HtmlDomElementDefinition` 子类的 `ValidateXamlBuildReadiness()` 中添加:
```csharp
protected override void ValidateXamlBuildReadiness()
{
    if (!HasCompletedDomFill)
        throw new InvalidOperationException(
            $"{TagName} {DocumentScope}::{XPath} must complete DOM Fill before XAML build.");
}
```

### Phase 3: selectedcontent 元素注册

在 `HtmlDomElementTypeCatalog.CreateFactories()` 中注册 `selectedcontent` 工厂。

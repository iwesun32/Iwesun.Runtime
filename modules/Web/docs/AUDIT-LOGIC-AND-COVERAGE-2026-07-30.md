# Iwesun.Runtime.Web 全面逻辑审核报告

审核日期：2026-07-30
审核范围：`src/Iwesun.Runtime.Web`（元素、集成、审核、布局、继承）+ `tests/Iwesun.Runtime.Web.Tests`
验证基线：`dotnet test Iwesun.Runtime.Web.slnx -c Debug` → 264/264 通过，0 失败。

---

## 总体结论

项目整体架构严谨，已经建立了相当完整的"双端六槽位"（DOM 初始化/链接/运行 + XAML 初始化/链接/运行）证据驱动模型、强类型元素目录、架构审核门禁和 264 个通过的单元测试。**整体设计没有致命缺陷**，但在"每个元素类自己的专属分析和创建过程"这一核心要求上，存在**明显的深度不均衡**和**若干真实的逻辑/覆盖缺口**，尤其集中在：

1. WebView2 采集端对部分关键属性类别（事件、部分数据源、SVG 专属属性、iframe 嵌套文档）证据不完整；
2. 少数复杂元素的 XAML 投影（`video/audio/track`、`iframe` 嵌套内容、`map/area`、`col/colgroup`、`input[type=hidden]` 的数据回写）存在功能不对等或单向；
3. 审核体系（`HtmlDomElementArchitectureAudit`）只能发现"声明缺失/未注册"，**不能发现"声明了但运行值错位/未回读"**，需要补充运行时对账（reconciliation）层级；
4. 个别基类与派生类之间的职责重叠（`BuildXamlAttributes` 中的 `Attributes.TryAdd` 合并顺序）可能导致**派生类静默覆盖基类已计算的运行时属性**。

下文按两大审核重点分述，并列出按严重级别排序的发现。

---

## 第一项审核：从运行时 WebView2 中获取全面真实的多维度数据

### 1.1 已经做得正确的部分

- **六槽位模型强制约束**：`ValidateOwnTraversalContract` 强制每个可填充属性必须恰好暴露 `Initialization/Link/Runtime` 三个 DOM 槽位和三个 XAML 槽位，错位在构造期就会抛异常 — 这是防错位的第一道闸门，设计正确。
- **证据状态三态**：`Captured / ConfirmedAbsent / SourceUnsupported` 严格区分"有值"、"确实没有"、"采集不到"，禁止把 `SourceUnsupported` 伪造成成功，符合"失败即停"铁律。
- **身份稳定**：`DocumentScope + XPath` 作为查询身份，与 WebView2 `document.evaluate` 一一对应；`queryResults.Count != pending.Count` 直接 `InvalidDataException`，避免结果错位。
- **批量查询**：`FillOwnFromHtmlRootAsync` 通过 `htmlRoot.QueryManyAsync` 一次性提交本元素全部请求，避免 JS 往返错位。
- **递归填充顺序**：`DomFillFromHtmlRootAsync` 先填自己，再按 DOM 顺序递归子元素，符合"先建树后填充、当前先填、随后递归"的规范。
- **运行时类型分类**：`QueryRuntimeAsync` 正确把 `rect.*` 走 `getBoundingClientRect`、`style.*` 走 `getComputedStyle`、表单/媒体/滚动走 DOM property，分派清晰。

### 1.2 发现的逻辑错误与缺失（按严重级别）

#### P0 — 事件槽位采集是空白

`WebView2DomPropertyFiller.QueryAsync` 只按 `DomPropertyDataSlot` 的 `Initialization/Link/Runtime` 分派，完全没有识别 `ElementSlotOwnerKind.Event` / `ElementEvidenceKind.Events` 的上下文。

- 位置：[WebView2DomPropertyFiller.cs](../src/Iwesun.Runtime.Web/Integration/WebView2DomPropertyFiller.cs)
- 影响：`DomElementEvent` 槽位（`Events` 属性被标记为 `IsFillRequired = true`）走 `QueryInitializationAsync` 时，会把 `onclick` 之类的 `on*` attribute 当字符串抓回来；但**通过 `addEventListener` 注册的运行时事件、事件冒泡链、委托监听**完全采集不到。
- 结果：`Events` 在 `IsAuditRequired = true` 下，初始化槽位有值（attribute 字符串），运行槽位永远 `ConfirmedAbsent`，XAML 端无法判断"事件真的挂载成功了没有"。
- 建议：`QueryAsync` 在 `context.OwnerKind == ElementSlotOwnerKind.Event` 时，切换到专用 JS（`getEventListeners` 在 Chromium 只能通过 CDP `DOMDebugger.getEventListeners` 获得；WebView2 需要注入 instrumentation hook 在 `EventTarget.prototype.addEventListener` 上打点）。这是**当前最严重的一项数据缺口**。

#### P0 — `content.ownText` 与 `content.*` 系列运行时槽位的采集路径不可靠

`WebView2DomPropertyFiller` 源码中对 `content.` 前缀**没有任何专门分支**（`grep -i "content\|ownText\|childNodes\|textContent"` 在 filler 内 0 命中）。当 `DomQueryEngine` 路径（`BindWebView2Filler`）触发 `QueryRuntimeAsync` 时，`content.ownText` 走最后的 default 分支 `QueryElementPropertyAsync`：

```js
node['content.ownText']   // 永远是 undefined
```

- 位置：[WebView2DomPropertyFiller.cs](../src/Iwesun.Runtime.Web/Integration/WebView2DomPropertyFiller.cs) 的 `QueryRuntimeAsync` default 分支
- 关键架构事实：`content.ownText` 不是 attribute（`QueryInitializationAsync` 的 `node.hasAttribute('content.ownText')` 也永远 false），也不是 DOM property — 它是**需要通过 `node.childNodes` 过滤 text node 拼接的合成量**。
- 两条填充路径都会失败：
  1. **DomQueryEngine 路径**（filler 直接绑定到元素）：Initialization 走 attribute 查询 → false；Runtime 走 `node[...]` → undefined。两端都 `ConfirmedAbsent`。
  2. **HtmlRoot 路径**：`HtmlDocumentRoot.QueryDomPropertiesAsync` 是抽象方法，由消费方（DoubaoUIClone）实现；本库内的 filler 不经过此路径。消费方是否处理了 `content.*` 需要单独审核 — 本库**没有提供任何辅助**。
- 后果：所有非叶文本元素（`p/span/div/strong/em/...`）的"自有文本"（`ownText`，用于 `HtmlLayoutDomElementDefinition.HasGeneratedXamlContent` 和合成 `TextBlock` 子节点）**在 filler 直连 WebView2 时完全采集不到**。
- 影响面：所有依赖 `RuntimeInitialization("content.ownText")` / `RuntimeValue("content.ownText")` 的 XAML 生成路径（`HtmlLayoutDomElementDefinition`、`HtmlTextVisualDomElementDefinition`、`HtmlFieldSetDomElement.BuildXamlAttributes` 的 legend 文本等）。
- 修复：在 `QueryInitializationAsync` 和 `QueryRuntimeAsync` 入口对 `propName.StartsWith("content.")` 分派到专用 JS：
  ```js
  // content.ownText：仅拼接自有 text node，不含后代元素文本
  Array.from(node.childNodes)
       .filter(n => n.nodeType === Node.TEXT_NODE)
       .map(n => n.data).join('').trim()
  // content.placeholder → node.placeholder；content.value → node.value
  // content.selectedText → node.value.substring(node.selectionStart, node.selectionEnd)
  ```
  这是**当前覆盖面最广的采集错位**（影响每一个非叶文本元素）。

#### P1 — SVG 元素运行时属性未走专用通道

`WebView2DomPropertyFiller` 只识别 `rect.*`、`style.*` 和少数表单/媒体 DOM property。SVG 几何属性（`path.d`、`circle.cx/cy/r`、`text.x/y`、动画值 `animVal` vs `baseVal`）没有专门分支：

- `path.getAttribute('d')` 走 `Initialization` 是对的；但 `pathData` 的运行时归一化值（`getPathData()`）、`getPointAtLength` 等运行时几何无法获得。
- `circle.cx` 在 `QueryElementPropertyAsync` 中拿到的是 `SVGLength` 对象，`String(value)` 会得到 `[object SVGLength]`，**值错位**。
- 影响：SVG 元素（14 个）的运行槽位数据大多不正确。
- 建议：增加 `ElementSlotOwnerKind` / 命名空间分支，SVG 属性的运行时值统一走 `node[propriate].baseVal.valueAsString` 或 `getAttribute` 回退。

#### P1 — `iframe` 嵌套文档作用域没有真正进入

`AttachEmbeddingFrameOwner` 只允许 `iframe` 作为嵌套文档根，但 `WebView2DomPropertyFiller` 的所有 JS 都用 `document.evaluate`，**没有 `contentDocument` 切换**。

- 影响：`iframe` 内部元素用 `DocumentScope = "frame-xxx"` + 自己的 XPath，但查询时父文档的 `document.evaluate` 根本找不到这些节点 — 全部返回 `ConfirmedAbsent`。
- 这是**嵌套 iframe 场景下的完全数据缺失**。`HtmlDocumentRoot` 的多 `DocumentRoots` 机制已经预留，但 `WebView2DomPropertyFiller` 没有对应的 frame routing。

#### P1 — `Link` 槽位语义过于单薄

`QueryLayoutLinkAsync` 的百分比/vw/vh/flex/grid 分派只识别了少数情况，且返回值用 `ElementPropertyLinkKind.CssExpression / LayoutExpression` 这种弱类型包装：

- 对于 `calc(100% - 20px)` 这种真正的链接计算，只返回原始字符串，没有构造**强类型 `ContainerLayoutBinding`**（项目里 `Layout/ContainerLayoutBinding.cs` 定义了完整的强类型契约，但 filler 没用上）。
- `position: absolute/fixed` 时参考基准应该返回 `offsetParent` 链，目前只判断了 `offsetParent` 存在，没有把它序列化成链接。
- `ElementPropertyValueSource.ContainerAutomaticLayout` 的 link 只放了 `"flex"` / `"grid"` 字符串，违反"容器自动布局必须携带强类型 ContainerLayout 链接"的契约（`DomPropertyQueryResult.ValidateValueSource` 只检查 `Kind == ContainerLayout`，但 filler 给的是 `LayoutExpression`，这意味着**flex/grid 的 link 实际上通不过自己的校验**——`requiresLink = true`，`link.Kind` 却是 `LayoutExpression`，在 `ContainerAutomaticLayout` 分支下要求 `link.Kind == ContainerLayout`，会抛 `ArgumentException`）。

  > 这是一个**真实的自检矛盾**：`ValidateValueSource` 要求 `ContainerAutomaticLayout` 必须搭配 `ContainerLayout` 链接，而 filler 在 flex/grid 分支返回的是 `LayoutExpression` 链接。一旦走到这条路径，会在 `DomPropertyQueryResult.Captured` 内部抛异常，被 `QueryAsync` 的 `catch (Exception)` 捕获并降级为 `SourceUnsupported` —— **所有 flex/grid 容器的 Link 槽位被静默吞掉**。

#### P2 — 初始化槽位布尔属性判定错误

```js
if (typeof node[name] === 'boolean' && node[name] === true) return 'true';
```

- 对 `<input disabled>`，attribute 名是 `disabled`，但填充器收到的 `propName` 是 C# 属性名（例如 `Disabled`），`node.hasAttribute('Disabled')` 返回 false（attribute 大小写敏感），`node['Disabled']` 是 undefined，因此 `disabled` 等所有布尔 attribute 在初始化槽位**永远 `ConfirmedAbsent`**。
- 修复：filler 需要访问 `HtmlDomAttributeProperty.AttributeName`（全小写），而不是反射属性名。这意味着 `DomPropertyQueryContext.PropertyName` 必须带 attribute 名而不是 C# 属性名 — 目前 `CreateDomFillRequests` 用的是 `traits.PropertyName`（C# 名）。
- 影响范围：**所有 HTML attribute 的初始化槽位**，大小写不一致（如 `viewBox`、`preserveAspectRatio`、`colSpan`、`rowSpan`、`maxLength`、`autoComplete`）都会落空。这是覆盖面极广的错位。

#### P2 — 没有视口/分辨率上下文

`ElementInheritanceService` 提供了分辨率/容器尺寸发布，但 `WebView2DomPropertyFiller` 没有把 `window.innerWidth/devicePixelRatio` 发布到 `GlobalLayoutService` / `GlobalStyleService`，这两个属性被 `QueryInitializationAsync` 硬编码为 `ConfirmedAbsent("Service property")`。

- 影响：标记为 `IsFillRequired = true` 的两个服务属性永远填不进去，`Audit` 时永远失败（它们没有 `SourceUnsupported` 而是 `ConfirmedAbsent`，不算硬失败，但审计语义被削弱）。

#### P3 — XPath 注入与特殊字符

`EscapeJs` 只处理 `'`、`\\`、换行；如果 XPath 中含 `"`（比如 `//*[@id="foo"]`），在 JS 字符串里没问题，但构造 JS 模板时用 `'` 包裹 XPath，XPath 内部的 `'`（例如 `//*[text()='x']`）会被转义 — 这块处理是对的。但 XPath 里出现的 `${` 在 C# raw string `$$"""` 插值下没问题（用了 `{{}}` 占位）。**没有真实 bug，但建议补一个 XPath 中含单引号的测试**。

---

## 第二项审核：从数据正确创建和转换具有相同功能的 XAML 对象

### 2.1 已经做得正确的部分

- **每个元素强类型 CreateXaml**：`HtmlDomElementArchitectureAudit.OwnsStrongXamlCreation` 强制 `CreateXaml` 必须在最终元素类上重写 — 133 个元素类全部满足，测试 `Inspect_WhenEveryStandardHtmlTypeIsRegistered_HasCompleteAttributes` 全部通过。
- **强类型 XAML 契约目录**：`HtmlXamlStrongTypeContractCatalog` 113 个 HTML 标签每个都登记了允许的 XAML 元素名集合，审核会校验 `plan.Mapping.ElementName ∈ AllowedElementNames`，把"标准元素选 ConservativeContainer"列为错误。
- **`input` 22 态分派**：`HtmlInputDomElement.CreateXaml` 按 `HtmlInputTypeState` 精确分派到 CheckBox/RadioButton/Slider/NumberBox/PasswordBox/TextBox/HtmlDateInputControl 等 13 种 XAML 类型，是项目中最完整的元素专属分析示例。
- **`select/option/optgroup` 投影**：`HtmlSelectDomElement.BuildXamlObjectPlans` 自己构造 `ComboBox/ListView`，把 `optgroup` 拍平成带 SemiBold 标签 + 选项，把 `disabled` 组的 `IsEnabled=False` 透传到每个 option — 这是**真正的元素专属创建过程**，做得到位。
- **表格三态**：`HtmlTableDomElement`/`thead/tbody/tfoot/tr/th/td` 分别投影到 `HtmlTablePanel/HtmlTableSectionPanel/HtmlTableRowPanel/Border`，`th/td` 处理 `colSpan/rowSpan`。
- **文本元素叶/复分派**：`HtmlTextVisualDomElementDefinition` 区分叶文本（`TextBlock`）与复合短语容器（`HtmlInlineFlowPanel`），并在复合时把 `ownText` 生成为合成 `TextBlock` 子节点 — 这种"自有文本与子元素共存"的处理是正确的。
- **非可视元素抑制**：`datalist/optgroup/head/title/script/style/link/meta/template/source/track/map/area/col/colgroup` 都被标记为 `NonVisual`，`HasXamlOutput() == false`，`BuildXamlObjectPlans() == []`，不会污染视觉树。

### 2.2 发现的逻辑错误与缺失（按严重级别）

#### P0 — `input[type=hidden]` 数据完全丢失

`HtmlInputDomElement`：

```csharp
protected override bool HasXamlOutput() => InputType != HtmlInputTypeState.Hidden;
protected internal override IReadOnlyList<XamlElementObjectPlan> BuildXamlObjectPlans() =>
    InputType == HtmlInputTypeState.Hidden ? [] : base.BuildXamlObjectPlans();
```

- hidden input 完全不产生任何 XAML 对象，也不在 `_xamlOwnedObjects` 中登记任何持有者。
- 后果：hidden 的 `name`/`value` 是表单语义的一部分，**XAML 端无法通过 `x:Name` 或 `Tag` 找到它**，`HtmlFormState.InitialValue` 也无法写入。
- 强类型契约里 `input` 的 `AllowedElementNames` 包含 `""`（空名），审核通过，但**功能不对等**：DOM 有数据，XAML 没有。
- 建议：hidden input 应生成一个轻量 `ContentControl { Visibility=Collapsed, Tag=..., HtmlFormState.* }` 持有数据，而不是完全消失。

#### P0 — `track`（video/audio 字幕轨道）功能缺失

`HtmlXamlStrongTypeContractCatalog` 把 `source/track/map/area/col/colgroup` 全部登记为 `""`（非可视）。

- `video`/`audio` 投影到 `HtmlMediaElementControl`，但其 `track` 子元素被静默丢弃，**字幕/章节/描述元数据不会进入 XAML**。
- `HtmlMediaElementControl` 是自定义控件占位，项目内没有它的实现 — 即便生成 XAML，也没有真正可工作的 `MediaPlayerElement` 等价物。这是**功能不对等的最大黑洞**。
- `source` 同理：`video > source[src]` 的备选源没有写入 `HtmlMediaElementControl` 的任何属性。

#### P0 — `iframe` 嵌套文档没有投影

`HtmlIframeDomElement` 投影到 `HtmlEmbeddedDocumentHost`，但其嵌套文档根（`EmbeddingFrameOwner` 反向）的子树：

- `EnumerateDomChildren()` 在 iframe 元素本身上返回空（iframe 元素的 DOM 子节点不是内容文档）；
- 内容文档的 `html` 根是另一个 `HtmlDocumentRoot`，没有作为 iframe 的 XAML 子节点挂载。
- 结果：XAML 端 `HtmlEmbeddedDocumentHost` 是一个空壳，**内部文档不可见**。配合 1.2 P1（iframe 数据采集不到），iframe 是一个端到端断裂。

#### P1 — `BuildResolvedXamlAttributes` 合并顺序允许派生类静默覆盖运行时计算

```csharp
foreach (var runtimeProperty in XamlRuntimeProperties) { ... attributes.TryAdd(...); }
foreach (var attribute in BuildXamlAttributes()) attributes.TryAdd(attribute.Name, attribute);
```

- 顺序：先 `IsXamlOutputProperty` 槽位属性 → 再 `XamlRuntimeProperties` → 最后 `BuildXamlAttributes()`（基类 + 派生类合并后）。
- `TryAdd` 意味着**先注册者赢**。
- 问题：`BuildXamlAttributes()` 里 `AddRuntimeLength(attributes, "style.width", "Width")` 是从 `XamlRuntimeProperties` 读的，但它在第三个循环里被 `TryAdd`，**永远不会覆盖**前两个来源已写入的 `Width`。
- 反过来，如果某个派生类的 `BuildXamlAttributes` 里写了 `SetXamlAttribute(attributes, "Width", ...)`（例如 SVG `SvgRootDomElement` 写 `Width`），它用的是 `SetXamlAttribute`（覆盖赋值）**在自己的字典里**，但这个字典最后被 `TryAdd` 进总表 — 如果总表已有 `Width`（来自运行时属性 `style.width`），派生类的 SVG `width` attribute **被静默丢弃**。
- 具体后果：`SvgRootDomElement.BuildXamlAttributes` 设置 `Canvas.Left/Canvas.Top/Width/Height`，如果 DOM `style.width` 有值，SVG 的 `width` attribute 会被 `style.width` 覆盖。对 SVG 这是**语义错位**（`width` attribute 是视口尺寸，`style.width` 是 CSS 尺寸，二者不等价）。
- 建议：明确优先级顺序并文档化；SVG 专属属性应在总表合并前用 `attributes[name] = ...`（覆盖）而不是 `TryAdd`，或提供 `SetXamlAttribute` 优先级标记。

#### P1 — `HtmlImageMapComposite` / `map` / `area` 完全断裂

`HtmlXamlStrongTypeContractCatalog`：

- `img` → `Image` 或 `HtmlImageMapComposite`
- `map` / `area` → `""`（非可视）

`HtmlImageMapComposite` 在 `CanOwnPlacement` 里被列为可拥有 `DirectChildren`，但 `map`/`area` 非可视、不产生子对象，所以 `HtmlImageMapComposite` 永远拿不到 area 子节点。

- 客户端图像地图的点击区域**完全没有投影**。
- 建议：`map`/`area` 应在 `img` 投影时由 `HtmlImageDomElement` 自己读取（通过 `usemap` attribute 找到关联 `map`），把 `area[shape/coords/href]` 投影为 `HtmlImageMapOverlay` 子节点。这是典型的"需要元素自己专属分析"的场景 — 目前 `HtmlImageDomElement` 没有这样做。

#### P1 — `col` / `colgroup` 的列宽没有传入表格

`col`/`colgroup` 非可视化，但它们的 `span`/`width`/`style` 决定表格列的宽度。

- `HtmlTableDomElement` 没有把 `colgroup > col` 的 `width` 转换为 `HtmlTablePanel` 的 `ColumnDefinitions`。
- 结果：表格列宽完全依赖单元格内容自适应，**`<col style="width:200px">` 被静默忽略**。
- 同样属于"元素自己专属分析"缺失 — `HtmlTableDomElement` 应该遍历 `colgroup` 子节点构造列轨道。

#### P1 — `video/audio` 状态没有回读通道

`HtmlVideoDomElement`/`HtmlAudioDomElement` 的 `BuildXamlAttributes` 写了 `Source/AutoPlay/Controls` 等，但：

- 运行时 `currentTime/duration/paused/ended/volume` 这些**只能运行时读取的状态**，被 `WebView2DomPropertyFiller.QueryRuntimeAsync` 采集到 DOM 端；
- XAML 端 `HtmlMediaElementControl` 是占位类型，没有真实的 `MediaPlayerElement` 后端，`XamlFillAsync` 回读时拿不到对应控件属性，**永远 `TargetUnsupported`**。
- 这是"双端填充"链路在媒体元素上整体断裂。

#### P1 — `details/summary` 的 `open` 状态单向

`HtmlDetailsDomElement` 投影到 `Expander`，`open` attribute → `IsExpanded`。

- DOM → XAML 方向正确；
- 但用户在 XAML 端展开/收起 Expander，**没有回写 DOM `open` attribute** 的机制；`XamlFillAsync` 回读 `IsExpanded` 写到 `TargetRuntime`，但不会同步到 DOM。
- 同样的单向问题出现在：`dialog` 的 `open`、`input` 的 `checked/value`（`HtmlFormState.InitialChecked` 是初始化快照，不是双向同步）。
- 注意：项目规范明确"只复刻 UI 和纯界面行为，不实现登录/订阅/在线业务"，所以**单向可能是设计意图**。但审核需要明确标注"这是有意单向"还是"缺失"。目前没有任何文档/注释标注。

#### P2 — `HtmlFormState.*` 附加属性写在普通 attribute 字典里，不是真正的 XAML 附加属性

```csharp
SetXamlAttribute(attributes, "HtmlFormState.InitialValue", ...);
```

- 生成的 XAML 字符串是 `HtmlFormState.InitialValue="..."`，这要求存在一个叫 `HtmlFormState` 的**真实附加属性类**。
- 项目内 `Iwesun.Runtime.Web` 不引用 WinUI，只生成 XAML 字符串；消费方（DoubaoUIClone.App）需要实现 `HtmlFormState` 附加属性。这是一个**跨项目契约**，但 `Iwesun.Runtime.Web` 内部没有任何机制验证消费方真的注册了这些附加属性 — 如果消费方没注册，`XamlParseException` 在运行时才暴露。
- 建议：`HtmlXamlStrongTypeContractCatalog` 应附带"需要的附加属性清单"，供消费方在启动时自检。

#### P2 — `HtmlRubyAnnotationPanel` 等控件在 `CanOwnPlacement` 中硬编码，新元素需要改两处

`CanOwnPlacement` 是一个大的字符串 switch，列出所有合法 XAML 元素名。新增一个 `Html*Panel` 类型时必须同时：

1. 在 `HtmlXamlStrongTypeContractCatalog` 加 contract；
2. 在 `CanOwnPlacement` 加进对应 placement 分支。

漏掉任何一处，审核才会报错。**这两处没有单一来源（single source of truth）**，违反 DRY。

#### P2 — `HtmlGenericDomElement` 兜底路径与架构门禁的矛盾

`HtmlDomElementTypeCatalog.Create`：

- 标准标签（113+14）没有具体类型时 `NotSupportedException` — 正确；
- 非标准标签（自定义元素）返回 `HtmlGenericDomElement` — 合理；
- 但 `HtmlGenericDomElement` 的 `CreateXaml` 在 `HtmlDomElementArchitectureAuditTests.Inspect_WhenGenericShellIsUsed_FailsArchitectureGate` 中被明确判为**不通过**。

这意味着：页面里只要出现一个未注册的自定义元素，架构审核就失败。对一个真实的豆包页面（大量自定义元素如 `doubao-*`），这个门禁永远不可能通过。

- 建议：区分"标准 HTML 必须强类型"与"自定义元素允许 generic 但需显式登记到白名单"，否则会陷入"要么造假通过、要么永远失败"的两难。

#### P3 — `HtmlDocumentRoot` 的 `XamlElementName` 来自 `CreateXaml().ElementName`

`HtmlDomElementDefinition.XamlElementName => CreateXaml().ElementName`：

- `CreateXaml` 是 `abstract`，每次都重新计算；对 `HtmlInputDomElement`，`CreateXaml` 依赖 `InputType`，而 `InputType` 依赖 `SourceInitialization(Type)`。
- 在 `Type` attribute 还没填进初始化槽位之前调用 `XamlElementName`，会得到 `HtmlInputTypeState.Text` 的默认 `TextBox`。
- 虽然 `XamlElementName` 是 `TypeIntrinsic` 阶段属性，但它的值依赖 `DomRuntime` 阶段的 `Type` attribute — **阶段耦合颠倒**。
- 影响：`ValidateTraversalContract` 在填充前调用，此时 `XamlElementName` 不稳定；不过当前 `XamlElementName` 没有 `IsFillRequired`，不影响填充。但 `BuildXamlObjectPlans → ResolveXamlObjectProjection → CreateXaml` 也依赖 `InputType`，如果在 DOM 填充之前构建 XAML 对象树，会得到错误的控件类型。
- 规范顺序是"先 DomFillAsync 再 BuildXamlObjectTree"，但代码没有强制 — 建议 `BuildXamlObjectTree` 在 `Type` 等关键 attribute 未填时抛出明确异常，而不是用默认值静默生成错误控件。

#### P3 — `HtmlImageMapComposite` 等元素名在 `HtmlXamlStrongTypeContractCatalog` 但无实现类

`HtmlImageMapComposite / HtmlImageMapOverlay / HtmlEmbeddedDocumentHost / HtmlMediaElementControl / HtmlCanvasSurface / HtmlDialogControl / HtmlFormButton / HtmlDateInputControl` 等都是字符串占位，`Iwesun.Runtime.Web` 内没有对应的 WinUI 控件类，需要消费方实现。

- 与 P2 类似，但更深层：**目录登记了"允许的类型"，但没人验证这些类型在消费方存在**。
- 建议：契约目录附带"必需消费方类型清单"，消费方启动时通过反射自检。

---

## 审核体系本身的不足

### A. `HtmlDomElementArchitectureAudit` 只看"声明"，不看"运行"

`Inspect` 检查的是：

- 标准 attribute 是否都有 `DomElementStringProperty` 声明；
- 事件是否都登记；
- `CreateXaml` 是否在自己类重写；
- 契约目录是否覆盖。

但**不检查**：

- 填进 `SourceInitialization` 的值在 XAML 输出里是否真的出现；
- `BuildXamlAttributes` 输出的 attribute 名是否与契约目录的 `AllowedElementNames` 兼容（例如 `TextBlock` 不接受 `Value` attribute）；
- XAML 端 `TargetRuntime` 是否真的被 WinUI 控件接受（需要 WinUI 运行时才能验证，但应至少静态校验 attribute 名在 `CanSetDataTarget` 之类的白名单中）。

`CanSetDataTarget` 已经做了部分（`Content/Text/Value/IsChecked/Source/NavigateUri/PlaceholderText/ItemsSource/SelectedItem/CommandParameter`），但它只在 `AddDataSourceAttributes` 用，**`BuildXamlAttributes` 里 `SetXamlAttribute` 写任意名字时不校验** — 例如 `HtmlInputDomElement` 在 `HtmlInputTypeState.Image` 分支写 `Source` 给 `HtmlImageSubmitButton`，`CanSetDataTarget` 的 `Source` 分支只允许 `Image/MediaPlayerElement`，**这条路径绕过了校验**。

### B. 审核结果 `Passed` 的语义过于宽容

`ownPassed = ownResults.Count > 0 && ownResults.All(...)`：一个元素如果**没有任何 IsAuditRequired 属性**，`ownResults.Count == 0`，直接判 FAILED — 但 `HtmlMetadataDomElementDefinition` 等非可视元素本来就不该有任何槽位，这会被误判。需要检查非可视元素是否被豁免。

### C. 缺一层"运行时对账（reconciliation）"

最严格的端到端验证应该是：

1. `DomFillAsync` 抓 DOM 值；
2. `BuildXamlObjectTree` 建 XAML；
3. `XamlFillAsync` 从 XAML 控件读回 `TargetRuntime`；
4. 逐属性比较 `SourceInitialization` ↔ `TargetInitialization`、`SourceRuntime` ↔ `TargetRuntime`。

`SlottedElementAuditor` 与 `NumericPropertyAuditor` 提供了比较器，但`DomElement.Audit` 只调用 `owner.AuditSlots()` — 单个属性内部比较，**不做跨元素聚合和跨端到端一致性**。缺一个独立的 `EndToEndReconciliationAuditor`。

---

## 修复优先级建议

| 优先级 | 问题                                              | 建议动作                                                                                               |
| ------ | ------------------------------------------------- | ------------------------------------------------------------------------------------------------------ |
| P0     | 事件槽位采集空白                                  | `QueryAsync` 按 `OwnerKind == Event` 分派到 CDP `DOMDebugger.getEventListeners` 或注入 instrumentation |
| P0     | `content.ownText` 运行时采集路径错误              | `QueryInitializationAsync`/`QueryRuntimeAsync` 对 `content.*` 前缀走 childNodes text 拼接              |
| P0     | hidden input 数据丢失                             | 生成 `Visibility=Collapsed` 的 `ContentControl` 持有 name/value                                        |
| P0     | iframe 嵌套文档采集/投影断裂                      | filler 支持 `contentDocument` 路由；`HtmlEmbeddedDocumentHost` 挂载嵌套根                              |
| P0     | flex/grid Link 自检矛盾                           | 修正 `ValidateValueSource` 或 filler 返回 `ContainerLayout` 链接                                       |
| P0     | track/video/audio 媒体状态无 XAML 后端            | 实现真实 `MediaPlayerElement` 包装或显式声明"不支持"                                                   |
| P1     | attribute 名大小写错位                            | `DomPropertyQueryContext` 携带 attribute 名而非 C# 属性名                                              |
| P1     | SVG 运行时属性类型错误                            | SVG 分支走 `baseVal.valueAsString`                                                                     |
| P1     | 属性合并顺序导致 SVG width 被 CSS 覆盖            | 明确 `SetXamlAttribute` 覆盖优先级，或 SVG 走专属通道                                                  |
| P1     | map/area 无投影                                   | `HtmlImageDomElement` 通过 `usemap` 解析 map，生成 overlay                                             |
| P1     | col/colgroup 列宽丢失                             | `HtmlTableDomElement` 构造列轨道                                                                       |
| P2     | `HtmlFormState.*` 附加属性跨项目契约无验证        | 契约目录附带附加属性清单，消费方启动自检                                                               |
| P2     | `CanOwnPlacement` 两处硬编码                      | 单一来源化                                                                                             |
| P2     | `HtmlGenericDomElement` 审核两难                  | 自定义元素白名单机制                                                                                   |
| P3     | `input.CreateXaml` 依赖未填 attribute 的默认 Type | `BuildXamlObjectTree` 前强制校验关键 attribute 已填                                                    |
| A      | 审核只看声明不看运行                              | 增加 `BuildXamlAttributes` 输出白名单校验                                                              |
| B      | 审核对非可视元素误判                              | 非可视元素豁免 `ownResults.Count > 0`                                                                  |
| C      | 缺端到端对账                                      | 新增 `EndToEndReconciliationAuditor`                                                                   |

---

## 结论

**架构层面**：项目已经建立了一个罕见严谨的、证据驱动的、带自检的 DOM→XAML 转换框架，133 个元素类全部有自己的 `CreateXaml` 强类型实现，264 个测试通过。这一项**符合设计要求**。

**第一项（WebView2 采集）**：框架正确，但 `WebView2DomPropertyFiller` 在**事件、`content.*` 自有文本、SVG 运行时值、iframe 嵌套文档、attribute 名大小写**五个方面有真实的数据缺口或错位，其中 flex/grid 的 Link 槽位存在自检矛盾会被静默吞掉。需要补 5 个采集通道。

---

## 后续实施附录（2026-07-30）

本报告列出的采集与覆盖缺口已按当前源码重新核验并实施：

- 专用 `content.*`、SVG animated/base value、嵌套 iframe scope
  和强类型父 flex/grid 链接通道已加入。
- attribute 规范查询名已由 `owner.Name` 提供，反射名仅用于身份；
  HTML 与 SVG 大小写已有门禁。
- `addEventListener/removeEventListener` 在 document-created 阶段插桩，
  默认事件连接读取全部活动监听器，随后由单属性 API 填充事件
  Initialization/Link/Runtime 三槽。
- hidden input 改为 collapsed 数据对象；input 类型选择要求先完成
  DOM Fill。
- map/area、col/colgroup、media、iframe XAML 挂载在报告生成前后的
  当前源码中均已有专属实现，不再按“空白”处理。
- 新增正式端到端对账器，并接入 `HtmlRuntimeDocumentRoot.AuditXaml()`；
  对账未通过时禁止写 XAML 文件。
- 自定义元素白名单、消费方类型/附加属性清单和 placement 能力已集中
  到正式契约入口。

验证基线：Runtime Web 286/286、Runtime WebView2 23/23、
DoubaoUIClone 120/120；WinUI 对象工厂自检 PASS。

这些结果证明代码契约和内存对象创建链通过，不等同于真实页面的最终
视觉通过。真实页面必须继续输出 DOM/XAML 运行值并由端到端对账报告
逐元素判定。

**第二项（XAML 创建）**：元素专属分析深度严重不均衡 — `input/select/textarea/table/fieldset` 做到了教科书级的专属创建，但 **`iframe/video/audio/track/map/area/col/hidden input` 这一组"嵌入与资源类"元素存在端到端断裂**。此外 `BuildResolvedXamlAttributes` 的 `TryAdd` 合并顺序会让 SVG 等专属 attribute 被 CSS 同名属性静默覆盖，是一个真实的错位风险。建议按上表 P0→P1→P2 顺序修复。

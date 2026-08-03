# Iwesun.Runtime.Web 全面审核报告与修改意见

审核日期：2026-07-30
审核范围：`src/Iwesun.Runtime.Web` 全部 8 个目录（Elements/Integration/Auditing/Layout/Inheritance）+ `tests/Iwesun.Runtime.Web.Tests`
验证基线：`dotnet test Iwesun.Runtime.Web.slnx -c Debug` → **264/264 通过，0 失败**
审核方法：逐文件源码审查 + 控制流/数据流交叉验证 + 自检矛盾运行时推演

---

## 〇、执行摘要

项目整体架构属于**罕见的高严谨度证据驱动型 DOM→XAML 转换框架**，核心设计决策（双端六槽位、强类型元素目录、架构审核门禁、失败即停）正确且被 264 个测试锁定。**不存在致命架构缺陷**。

但在"**133 个元素类各自的专属分析与创建过程**"这一核心承诺上，存在**严重的深度不均衡**，以及若干**真实的、已逐行验证的逻辑错误**。最关键的是：**框架的"自检机制"本身存在两处会被静默吞掉的矛盾**，使得部分设计上的安全网在实际运行时失效。

| 维度                 | 评分  | 说明                                                     |
| -------------------- | ----- | -------------------------------------------------------- |
| 架构设计             | ★★★★★ | 六槽位、强类型目录、审核门禁、失败即停，设计正确         |
| DOM 采集（WebView2） | ★★☆☆☆ | 框架正确，但 5 个采集通道存在真实缺口/错位               |
| XAML 创建            | ★★★☆☆ | 表单/表格/文本类教科书级；嵌入/资源类端到端断裂          |
| 自检/审核体系        | ★★★☆☆ | 门禁有效，但存在 2 处自检矛盾被静默吞掉                  |
| 测试覆盖             | ★★★★☆ | 264 测试全部通过，但全部是"声明级"，无"运行时端到端对账" |

---

## 一、审核方法说明

本次审核对 `src/Iwesun.Runtime.Web` 下全部 49 个源文件进行了逐一阅读，重点交叉验证了以下控制流/数据流：

1. **DOM 采集链路**：`WebView2DomPropertyFiller.QueryAsync` → `QueryInitializationAsync`/`QueryLinkAsync`/`QueryRuntimeAsync` → JS `document.evaluate` → `DomElement.FillOwnFromHtmlRootAsync` → `ApplyDomQueryResult`
2. **XAML 创建链路**：`DomElement.BuildXamlObjectPlans` → `ResolveXamlObjectProjection` → `CreateXaml` → `BuildXamlObjectChildPlans` → `FillXamlProperties` → `BuildResolvedXamlAttributes` → `IXamlElementObjectFactory`
3. **自检一致性**：`DomPropertyQueryResult.ValidateValueSource` 对 filler 返回值的运行时校验
4. **属性合并优先级**：`BuildResolvedXamlAttributes` 三个 `TryAdd` 循环的顺序语义

所有标记为 P0 的发现都经过**逐行源码验证**（非推测）。

---

## 二、总体结论（两个审核重点）

### 重点一：从运行时 WebView2 获取全面真实多维度数据 —— 框架正确，采集通道有 5 处真实缺口

**做对的**：六槽位强制约束（错位构造期抛异常）、证据三态（禁止伪造成功）、`DocumentScope + XPath` 稳定身份、批量查询防错位、递归填充顺序正确、运行时属性按 `rect./style./表单/媒体` 正确分派。

**真实缺口**（详见 §三）：事件槽位、`content.*` 自有文本、SVG 运行时值、iframe 嵌套文档、attribute 名大小写。

### 重点二：每个元素类的专属 XAML 创建过程 —— 深度严重不均衡

**做到位的**（教科书级）：`input`（22 态精确分派）、`select/option/optgroup`（拍平 optgroup + 透传 disabled）、`textarea`、`table/thead/tbody/tfoot/tr/th/td`、`fieldset/legend`、所有文本类元素（叶/复合正确分流）。

**端到端断裂的**：`iframe/video/audio/track/map/area/col/colgroup/input[type=hidden]` —— 这组"嵌入与资源类"元素在采集端和 XAML 端同时断裂。

---

## 三、详细发现（按严重级别）

### P0 — 必须立即修复（会导致功能错误或数据丢失）

#### P0-1 事件槽位采集是空白

- **位置**：[WebView2DomPropertyFiller.cs](../src/Iwesun.Runtime.Web/Integration/WebView2DomPropertyFiller.cs) `QueryAsync`
- **验证**：`QueryAsync` 只按 `DomPropertyDataSlot`（Initialization/Link/Runtime）分派，**完全不识别 `context.OwnerKind == ElementSlotOwnerKind.Event`**。`Events` 属性被标记 `IsFillRequired=true`，走 `QueryInitializationAsync` 时把 `onclick` attribute 字符串抓回，但 `addEventListener` 注册的运行时事件、事件冒泡链、委托监听**完全采集不到**。
- **后果**：`Events` 在 `IsAuditRequired=true` 下，初始化槽位有值（attribute 字符串），运行槽位永远 `ConfirmedAbsent`，XAML 端无法判断"事件真的挂载成功了没有"。`DomEventRuntimeEvidence`（含 `InvocationCount/LastInvokedAt/TargetXPath/CurrentTargetXPath/Phase`）定义了完整的运行时证据模型，但**没有任何采集器填充它**。
- **修改意见**：
  1. `QueryAsync` 增加 `context.OwnerKind == ElementSlotOwnerKind.Event` 分支；
  2. 对 `addEventListener` 注册的运行时事件，WebView2 无法直接读取，需注入 instrumentation：在页面加载前 `AddScriptToExecuteOnDocumentCreatedAsync`，hook `EventTarget.prototype.addEventListener/removeEventListener/dispatchEvent`，把注册记录写入 `window.__iwesunEventRegistry`，查询时读取该注册表；
  3. `DomEventRuntimeEvidence.InvocationCount` 需要 hook `dispatchEvent` 累加。

#### P0-2 `content.ownText` 与 `content.*` 系列采集路径完全失效

- **位置**：[WebView2DomPropertyFiller.cs](../src/Iwesun.Runtime.Web/Integration/WebView2DomPropertyFiller.cs) `QueryInitializationAsync` / `QueryRuntimeAsync`
- **验证**：源码中对 `content.` 前缀**零处理**（`grep -i "content\|ownText\|childNodes\|textContent"` 在 filler 内 0 命中）。`content.ownText` 不是 attribute（`hasAttribute` 永远 false），也不是 DOM property（`node['content.ownText']` 永远 undefined）—— 它是需要通过 `node.childNodes` 过滤 TEXT_NODE 拼接的**合成量**。
- **后果**：所有非叶文本元素（`p/span/div/strong/em/...`）的"自有文本"采集不到，直接影响：
  - `HtmlLayoutDomElementDefinition.HasGeneratedXamlContent` / `BuildXamlObjectChildPlans`（合成 TextBlock 子节点）
  - `HtmlTextVisualDomElementDefinition` 的复合短语容器
  - `HtmlFieldSetDomElement.BuildXamlAttributes` 的 legend Header 文本
- **影响面**：**每一个非叶文本元素**。
- **修改意见**：在两个查询入口对 `propName.StartsWith("content.")` 分派到专用 JS：
  ```js
  // content.ownText
  Array.from(node.childNodes).filter(n=>n.nodeType===Node.TEXT_NODE).map(n=>n.data).join('').trim()
  // content.value → node.value；content.placeholder → node.placeholder
  // content.selectedText → node.value.substring(node.selectionStart,node.selectionEnd)
  // content.ariaLabel → node.getAttribute('aria-label')
  ```

#### P0-3 flex/grid Link 槽位自检矛盾被静默吞掉

- **位置**：[WebView2DomPropertyFiller.cs](../src/Iwesun.Runtime.Web/Integration/WebView2DomPropertyFiller.cs#L216) vs [DomElement.cs](../src/Iwesun.Runtime.Web/Elements/DomElement.cs) `DomPropertyQueryResult.ValidateValueSource`
- **验证**（逐行）：
  - filler flex/grid 分支返回：`Captured(type, ContainerAutomaticLayout, new ElementPropertyLink(LayoutExpression, type))`
  - `ValidateValueSource` 要求：`ContainerAutomaticLayout && link.Kind != ContainerLayout → throw ArgumentException`
  - 该异常被 `QueryAsync` 外层 `catch (Exception ex)` 捕获 → 降级为 `SourceUnsupported(ex.Message)`
- **后果**：**所有 flex/grid 容器的 Link 槽位被静默吞掉**，且报错信息（"容器自动布局必须携带强类型 ContainerLayout 链接"）被包装成 `SourceUnsupported`，违反"失败即停、禁止吞错"的设计原则。
- **修改意见**（二选一，推荐 A）：
  - **A. 修 filler**：flex/grid 分支构造真正的 `ContainerLayoutBinding`（项目已有 `Layout/ContainerLayoutBinding.cs` 强类型契约），`link.Kind = ContainerLayout`；
  - **B. 修校验器**：允许 `ContainerAutomaticLayout` 搭配 `LayoutExpression` 作为过渡（不推荐，会弱化强类型契约）。
  - 无论哪种，都应**消除静默吞错**：`QueryAsync` 的 `catch` 应过滤掉 `ArgumentException`（自检矛盾属于编程错误，不应降级为数据状态）。

#### P0-4 hidden input 数据完全丢失

- **位置**：[StandardHtmlDomFormsTablesMedia.cs](../src/Iwesun.Runtime.Web/Elements/StandardHtmlDomFormsTablesMedia.cs) `HtmlInputDomElement`
- **验证**：`HasXamlOutput() => InputType != Hidden`，`BuildXamlObjectPlans() => Hidden ? [] : ...`。hidden input 不产生任何 XAML 对象，不登记 `_xamlOwnedObjects`。
- **后果**：hidden 的 `name/value` 是表单语义一部分，XAML 端无法通过 `x:Name`/`Tag` 找到，`HtmlFormState.InitialValue` 无处写入。**功能不对等**：DOM 有数据，XAML 没有。
- **修改意见**：hidden input 生成 `ContentControl { Visibility=Collapsed, Tag="{scope}::{xpath}", HtmlFormState.InitialValue=..., HtmlFormState.FormOwnerId=... }`，并在 `HtmlXamlStrongTypeContractCatalog` 把 `input` 的 `AllowedElementNames` 中的 `""` 改为 `ContentControl`（隐藏态）。

#### P0-5 iframe 嵌套文档采集与投影端到端断裂

- **位置**：[WebView2DomPropertyFiller.cs](../src/Iwesun.Runtime.Web/Integration/WebView2DomPropertyFiller.cs) + [StandardHtmlDomFormsTablesMedia.cs](../src/Iwesun.Runtime.Web/Elements/StandardHtmlDomFormsTablesMedia.cs) `HtmlIframeDomElement`
- **验证**：
  - 采集端：filler 所有 JS 用 `document.evaluate`，**没有 `contentDocument` 切换**。iframe 内部元素用 `DocumentScope="frame-xxx"` + 自己的 XPath，但父文档的 `document.evaluate` 找不到这些节点 → 全部 `ConfirmedAbsent`。
  - XAML 端：`HtmlIframeDomElement` 投影到 `HtmlEmbeddedDocumentHost`，但 `EnumerateDomChildren()` 在 iframe 上返回空（iframe 的 DOM 子节点不是内容文档），嵌套文档根没有作为 iframe 的 XAML 子节点挂载。
- **后果**：iframe 是**端到端完全断裂**（采集不到 + 投影空壳）。
- **修改意见**：
  1. 采集端：`QueryAsync` 在 `context.DocumentScope != 主文档 scope` 时，先通过 `document.querySelector('iframe[...]').contentDocument` 切换文档上下文，再 `evaluate`；
  2. XAML 端：`HtmlIframeDomElement.BuildXamlObjectChildPlans` 把 `EmbeddingFrameOwner` 反向关联的嵌套 `HtmlDocumentRoot` 的 `BuildXamlObjectPlans` 作为子节点挂载。

#### P0-6 track/video/audio 媒体状态无 XAML 后端

- **位置**：[StandardHtmlDomFormsTablesMedia.cs](../src/Iwesun.Runtime.Web/Elements/StandardHtmlDomFormsTablesMedia.cs) `HtmlVideoDomElement`/`HtmlAudioDomElement`
- **验证**：投影到 `HtmlMediaElementControl` —— 这是一个**字符串占位**，项目内没有实现。`track` 子元素被 `NonVisual` 静默丢弃，`source` 备选源没有写入任何属性。运行时 `currentTime/duration/paused` 能被 filler 采集到 DOM 端，但 XAML 端没有真实控件回读，`XamlFillAsync` 永远 `TargetUnsupported`。
- **后果**：媒体元素是**双端填充链路整体断裂**。
- **修改意见**：
  1. 短期：在 `HtmlXamlStrongTypeContractCatalog` 把 `video/audio` 的 `AllowedElementNames` 改为 `MediaPlayerElement`（WinUI 真实控件），`track` 的 `src/kind/srclang/label` 写入 `MediaPlayerElement` 的 `TimedTextTracks`（通过自定义附加属性或 `Tag` JSON）；
  2. 长期：消费方实现 `HtmlMediaElementControl : MediaPlayerElement`，本库在契约目录附带"必需消费方类型清单"供启动自检。

---

### P1 — 高优先级（导致数据错位或覆盖缺失）

#### P1-1 attribute 名大小写错位（覆盖面极广）

- **位置**：[DomElement.cs](../src/Iwesun.Runtime.Web/Elements/DomElement.cs) `CreateDomFillRequests` + filler `QueryInitializationAsync`
- **验证**：`CreateDomFillRequests` 用 `traits.PropertyName`（C# 属性名，如 `Disabled`、`ViewBox`、`ColSpan`、`MaxLength`、`AutoComplete`）作为 `context.PropertyName`；filler 直接 `node.hasAttribute('Disabled')` —— HTML attribute 大小写敏感（全小写），返回 false。
- **后果**：**所有非全小写 attribute 的初始化槽位落空**，包括 `viewBox/preserveAspectRatio/colSpan/rowSpan/maxLength/autoComplete/formAction/formMethod/formTarget` 等。
- **修改意见**：`CreateDomFillRequests` 在 `owner is HtmlDomAttributeProperty attr` 时用 `attr.AttributeName`（全小写）作为查询名，而非反射属性名。`DomPropertyQueryContext` 需要同时携带 `ReflectedPropertyName`（用于身份）和 `QueryName`（用于 JS 查询）。

#### P1-2 SVG 运行时属性类型错误

- **位置**：filler `QueryElementPropertyAsync`
- **验证**：`circle.cx` 返回 `SVGLength` 对象，`String(value)` → `"[object SVGLength]"`。
- **后果**：14 个 SVG 元素的几何运行时值**值错位**。
- **修改意见**：filler 对 `context.Element is SvgDomElementDefinition`（或 `context.ElementNamespace == Svg`）时，JS 走 `node[prop].baseVal?.valueAsString ?? node.getAttribute(prop)`。

#### P1-3 `BuildResolvedXamlAttributes` 的 `TryAdd` 合并顺序导致 SVG 专属 attribute 被 CSS 覆盖

- **位置**：[DomElement.cs](../src/Iwesun.Runtime.Web/Elements/DomElement.cs) `BuildResolvedXamlAttributes`
- **验证**：三个 `TryAdd` 循环顺序：① `IsXamlOutputProperty` 槽位属性 → ② `XamlRuntimeProperties`（含 `style.width` → `Width`）→ ③ `BuildXamlAttributes()`（基类+派生类合并后）。`TryAdd` 先注册者赢。
- **后果**：`SvgRootDomElement.BuildXamlAttributes` 用 `SetXamlAttribute` 写 `Width`（来自 SVG `width` attribute），但最后被 `TryAdd` 进总表时，如果 ② 已写入 `Width`（来自 `style.width`），SVG 的 `width` attribute **被静默丢弃**。SVG 的 `width` attribute（视口尺寸）与 `style.width`（CSS 尺寸）**语义不等价**。
- **修改意见**：明确优先级并文档化 —— 建议：派生类 `BuildXamlAttributes` 返回的 attribute 应**覆盖**运行时属性（即调整循环顺序为 ③→②→①，或对 ③ 用 `attributes[name] = ...` 覆盖赋值）。

#### P1-4 map/area 客户端图像地图完全断裂

- **位置**：[HtmlXamlStrongTypeContractCatalog.cs](../src/Iwesun.Runtime.Web/Elements/HtmlXamlStrongTypeContractCatalog.cs) + `HtmlImageDomElement`
- **验证**：`img` → `Image`/`HtmlImageMapComposite`；`map`/`area` → `""`（非可视）。`HtmlImageMapComposite` 在 `CanOwnPlacement` 中被列为可拥有 `DirectChildren`，但 `map`/`area` 不产生子对象，`HtmlImageDomElement` 没有通过 `usemap` attribute 解析关联 `map`。
- **后果**：客户端图像地图的点击区域**完全没有投影**。
- **修改意见**：`HtmlImageDomElement.BuildXamlObjectChildPlans` 在 `usemap` 有值时，通过 `HtmlRoot.DocumentRoots` 查找关联 `HtmlMapDomElement`，把每个 `area[shape/coords/href/alt]` 投影为 `HtmlImageMapOverlay` 子节点（`shape=rect → Rectangle`、`shape=circle → Ellipse`、`shape=poly → Polygon`）。

#### P1-5 col/colgroup 列宽没有传入表格

- **位置**：`HtmlTableDomElement`
- **验证**：`col`/`colgroup` 非可视化，但它们的 `span/width/style` 决定表格列宽。`HtmlTableDomElement` 没有遍历 `colgroup > col` 构造 `ColumnDefinitions`。
- **后果**：`<col style="width:200px">` 被静默忽略，表格列宽完全依赖内容自适应。
- **修改意见**：`HtmlTableDomElement.BuildXamlObjectPlans` 在构造 `HtmlTablePanel` 时，遍历 `colgroup/col` 子节点，把 `width` 转换为 `ColumnDefinition.Width`，`span` 展开为多个列轨道。

#### P1-6 details/dialog/input 状态单向，且未标注是否设计意图

- **位置**：`HtmlDetailsDomElement`/`HtmlDialogDomElement`/`HtmlInputDomElement`
- **验证**：DOM→XAML 方向正确（`open` → `IsExpanded`、`checked` → `IsChecked`），但 XAML 端用户交互后**没有回写 DOM** 的机制。
- **后果**：`XamlFillAsync` 回读 `IsExpanded/IsChecked` 写到 `TargetRuntime`，但不同步 DOM。考虑到项目规范"只复刻 UI 和纯界面行为"，单向**可能是设计意图**，但代码中没有任何注释/文档标注。
- **修改意见**：在这些元素的 `CreateXaml`/`BuildXamlAttributes` 注释中明确标注"本属性为单向投影（DOM→XAML），不回写"，或在 `IXamlPropertyExecutionOwner` 增加 `SupportsWriteBack` 元数据。

---

### P2 — 中优先级（契约/可维护性问题）

#### P2-1 `HtmlFormState.*` 附加属性跨项目契约无验证

- **位置**：`HtmlInputDomElement.BuildXamlAttributes` 等写 `HtmlFormState.InitialValue/InitialChecked/FormOwnerId`
- **问题**：这些是**附加属性**，需要消费方（DoubaoUIClone.App）实现 `HtmlFormState` 类。本库没有任何机制验证消费方注册了这些附加属性，未注册时 `XamlParseException` 在运行时才暴露。
- **修改意见**：`HtmlXamlStrongTypeContractCatalog` 附带 `RequiredAttachedProperties` 清单，消费方启动时反射自检。

#### P2-2 `CanOwnPlacement` 与契约目录两处硬编码

- **位置**：[HtmlDomElementArchitectureAudit.cs](../src/Iwesun.Runtime.Web/Elements/HtmlDomElementArchitectureAudit.cs) `CanOwnPlacement`
- **问题**：新增 `Html*Panel` 类型必须同时改 `HtmlXamlStrongTypeContractCatalog` 和 `CanOwnPlacement` 两处，漏一处才报错。违反 DRY/单一来源。
- **修改意见**：`CanOwnPlacement` 从契约目录派生，或契约目录附带 `AllowedPlacements`。

#### P2-3 `HtmlGenericDomElement` 审核两难

- **位置**：`HtmlDomElementTypeCatalog.Create` + `HtmlDomElementArchitectureAuditTests`
- **问题**：标准标签无具体类型时 `NotSupportedException`（正确）；非标准标签（自定义元素）返回 `HtmlGenericDomElement`，但架构审核明确判其不通过。真实豆包页面有大量 `doubao-*` 自定义元素，门禁永远不可能通过。
- **修改意见**：引入"自定义元素白名单"机制 —— 消费方显式登记允许的自定义标签，白名单内的 `HtmlGenericDomElement` 审核豁免。

#### P2-4 `service.globalLayout`/`service.globalStyle` 永远 `ConfirmedAbsent`

- **位置**：filler `QueryInitializationAsync` 硬编码 `return ConfirmedAbsent("Service property")`
- **问题**：这两个属性被标记 `IsFillRequired=true`，`ElementInheritanceService` 提供了分辨率/容器尺寸发布机制，但 filler 没有把 `window.innerWidth/devicePixelRatio/getComputedStyle` 发布进去。
- **修改意见**：filler 在 `QueryLinkAsync` 对这两个属性，从 `ElementInheritanceService` 读取当前快照的 `ContextId` 并返回 `LinkedConstant` 链接。

---

### P3 — 低优先级（健壮性/文档）

#### P3-1 `input.CreateXaml` 依赖未填充 attribute 的默认 Type

- **位置**：`HtmlInputDomElement.XamlElementName => CreateXaml().ElementName`，`CreateXaml` 依赖 `InputType`，`InputType` 依赖 `SourceInitialization(Type)`
- **问题**：`XamlElementName` 标记为 `TypeIntrinsic` 阶段属性，但值依赖 `DomRuntime` 阶段的 `Type` attribute —— **阶段耦合颠倒**。在 DOM 填充前调用会得到默认 `TextBox`。
- **修改意见**：`BuildXamlObjectTree` 入口校验关键 attribute（`input.type` 等）已填，未填时抛明确异常而非用默认值静默生成错误控件。

#### P3-2 审核体系只看"声明"不看"运行"

- **位置**：`HtmlDomElementArchitectureAudit.Inspect`
- **问题**：只检查 attribute/事件是否声明、`CreateXaml` 是否重写、契约目录是否覆盖；不检查 `BuildXamlAttributes` 输出的 attribute 名是否合法（`CanSetDataTarget` 只在 `AddDataSourceAttributes` 用，`SetXamlAttribute` 写任意名字时不校验）。
- **修改意见**：`Inspect` 增加"输出 attribute 白名单校验"，对每个元素的 `BuildXamlAttributes` 输出，校验 attribute 名在该元素 `AllowedElementNames` 对应控件的合法属性集合内。

#### P3-3 审核对非可视元素误判

- **位置**：`DomElement.Audit` 的 `ownPassed = ownResults.Count > 0 && ...`
- **问题**：非可视元素（`head/title/meta/datalist/optgroup`）本来就无任何槽位，`ownResults.Count == 0` 被判 FAILED。
- **修改意见**：`XamlSupport == NonVisual` 时豁免 `ownResults.Count > 0` 要求。

#### P3-4 缺端到端对账（reconciliation）层

- **位置**：`DomElement.Audit` 只调 `owner.AuditSlots()`（单属性内部比较）
- **问题**：最严格的端到端验证应是 `DomFillAsync` 抓值 → `BuildXamlObjectTree` 建 XAML → `XamlFillAsync` 读回 → 逐属性比较 `SourceInitialization↔TargetInitialization`、`SourceRuntime↔TargetRuntime`。目前缺这个聚合层。
- **修改意见**：新增 `EndToEndReconciliationAuditor`，消费 `SlottedElementAuditor`/`NumericPropertyAuditor` 的比较器，做跨元素聚合和跨端到端一致性校验。

---

## 四、修复路线图（按依赖关系排序）

| 阶段   | 任务                         | 依赖                   | 预估影响                              |
| ------ | ---------------------------- | ---------------------- | ------------------------------------- |
| **S1** | P0-3 flex/grid Link 自检矛盾 | 无                     | 消除静默吞错，恢复 flex/grid 布局链接 |
| **S1** | P0-2 `content.*` 采集通道    | 无                     | 恢复所有非叶文本元素自有文本          |
| **S1** | P1-1 attribute 名大小写      | 无                     | 恢复所有非全小写 attribute 初始化槽位 |
| **S2** | P0-1 事件槽位采集            | 需注入 instrumentation | 恢复事件运行时证据                    |
| **S2** | P1-2 SVG 运行时值            | 无                     | 恢复 14 个 SVG 元素几何运行时值       |
| **S2** | P1-3 属性合并优先级          | 需决策                 | 消除 SVG width 被 CSS 覆盖的错位      |
| **S3** | P0-4 hidden input            | 无                     | 恢复表单隐藏数据                      |
| **S3** | P0-5 iframe 嵌套文档         | 采集端+XAML端同时改    | 恢复 iframe 端到端                    |
| **S3** | P1-4 map/area                | 无                     | 恢复图像地图                          |
| **S3** | P1-5 col/colgroup            | 无                     | 恢复表格列宽                          |
| **S4** | P0-6 媒体元素后端            | 需消费方配合           | 恢复 video/audio 双端链路             |
| **S4** | P2-* 契约/白名单/自检        | 无                     | 提升可维护性                          |
| **S5** | P3-* 端到端对账              | S1-S4 完成             | 建立最终安全网                        |

**S1 三个修复相互独立、风险最低、收益最大，建议立即进行。**

---

## 五、修改意见汇总（代码级）

### 5.1 WebView2DomPropertyFiller.cs（采集端，5 处修改）

1. `QueryAsync` 增加 `OwnerKind == Event` 分支（P0-1）
2. `QueryInitializationAsync`/`QueryRuntimeAsync` 增加 `content.*` 前缀分派（P0-2）
3. `QueryLayoutLinkAsync` flex/grid 分支构造 `ContainerLayoutBinding`，`link.Kind = ContainerLayout`（P0-3）
4. `QueryAsync` 外层 `catch` 过滤 `ArgumentException`（自检矛盾不应降级）（P0-3 配套）
5. `QueryInitializationAsync` 增加 iframe `contentDocument` 路由（P0-5）+ SVG `baseVal` 分支（P1-2）+ `service.globalLayout/globalStyle` 链接（P2-4）

### 5.2 DomElement.cs（核心，3 处修改）

1. `CreateDomFillRequests` 对 attribute owner 用 `AttributeName` 作为查询名（P1-1）
2. `BuildResolvedXamlAttributes` 调整三个 `TryAdd` 循环顺序，派生类 `BuildXamlAttributes` 优先（P1-3）
3. `Audit` 对 `XamlSupport == NonVisual` 豁免 `ownResults.Count > 0`（P3-3）

### 5.3 StandardHtmlDomFormsTablesMedia.cs（元素，5 处修改）

1. `HtmlInputDomElement` hidden 态生成 `ContentControl { Visibility=Collapsed }` 持有数据（P0-4）
2. `HtmlIframeDomElement.BuildXamlObjectChildPlans` 挂载嵌套文档根（P0-5）
3. `HtmlImageDomElement.BuildXamlObjectChildPlans` 解析 `usemap` 生成 `HtmlImageMapOverlay`（P1-4）
4. `HtmlTableDomElement` 遍历 `colgroup/col` 构造列轨道（P1-5）
5. `HtmlVideoDomElement`/`HtmlAudioDomElement` 的 `track` 子元素写入控件属性（P0-6）

### 5.4 HtmlXamlStrongTypeContractCatalog.cs（契约，3 处修改）

1. `input` 的 hidden 态 `AllowedElementNames` 加 `ContentControl`（P0-4 配套）
2. `video/audio` 的 `AllowedElementNames` 改 `MediaPlayerElement`（P0-6 配套）
3. 每个契约附带 `RequiredAttachedProperties` 清单（P2-1）

### 5.5 HtmlDomElementArchitectureAudit.cs（审核，3 处修改）

1. `CanOwnPlacement` 从契约目录派生（P2-2）
2. `Inspect` 增加输出 attribute 白名单校验（P3-2）
3. `HtmlGenericDomElement` 白名单豁免机制（P2-3）

### 5.6 新增文件（2 个）

1. `Auditing/EndToEndReconciliationAuditor.cs`（P3-4）
2. `Integration/WebView2EventInstrumentation.cs`（P0-1 配套的 JS 注入脚本生成器）

---

## 六、结论

**架构层面**：项目已经建立了一个严谨、证据驱动、带自检的 DOM→XAML 转换框架，133 个元素类全部有自己的 `CreateXaml` 强类型实现，264 个测试通过。这一项**符合设计要求**。

**第一项（WebView2 采集）**：框架正确，但 `WebView2DomPropertyFiller` 在**事件、`content.*` 自有文本、SVG 运行时值、iframe 嵌套文档、attribute 名大小写**五个方面有真实的数据缺口或错位；其中 flex/grid 的 Link 槽位存在**已逐行验证的自检矛盾**，会被外层 catch 静默吞掉。

**第二项（XAML 创建）**：元素专属分析深度严重不均衡 —— `input/select/textarea/table/fieldset` 做到了教科书级的专属创建，但 **`iframe/video/audio/track/map/area/col/hidden input` 这一组"嵌入与资源类"元素存在端到端断裂**。此外 `BuildResolvedXamlAttributes` 的 `TryAdd` 合并顺序会让 SVG 等专属 attribute 被 CSS 同名属性静默覆盖，是一个真实的错位风险。

**建议按 §四 的 S1→S5 阶段路线图推进**，S1 三项修复（flex/grid Link、content.\* 采集、attribute 大小写）相互独立、风险最低、收益最大，可立即进行。

---

## 七、2026-07-30 源码复核后的实施状态

本节记录报告生成后的实际代码修正。原发现保留为历史证据，
但后续判断必须以本节和当前测试为准。

### 已修复

- `content.*` 已进入专用查询通道；`ownText` 读取直接文本节点，
  不再查询不存在的 `node["content.ownText"]`。
- flex/grid 尺寸链接改为检查父容器，并生成强类型
  `ContainerLayoutBinding`；契约异常不再降级为 `SourceUnsupported`。
- HTML/SVG 查询身份已确认使用 owner 的规范名：
  `AutoComplete → autocomplete`、`ViewBox → viewBox`，并加入回归测试。
- SVG 运行值读取支持 `animVal/baseVal/valueAsString/value`，
  不再输出 `[object SVGLength]`。
- XAML 同名 attribute 合并由具体元素的有来源决策优先；
  无来源默认值仍可被正式槽位值覆盖。
- hidden input 生成 `Visibility=Collapsed` 的 `ContentControl`，
  并保留 value/checked/form-owner 槽位。
- iframe 查询按 `DocumentScope` 逐级进入 `contentDocument`；
  已有的 `AttachFrameDocumentRoots` 继续负责 XAML 子树挂载。
- 导航前事件 instrumentation、运行时事件发现连接、事件三槽查询和
  invocation evidence 已接入正式 Live HTML 流程。
- 非可视元素无自有审核槽位时不再误判失败。
- 新增 `EndToEndReconciliationAuditor`；正式 XAML 文件导出要求
  DOM Fill、XAML 对象、XAML Fill 和双向审核全部对账通过。
- input 在公开 XAML 对象构建前必须完成 DOM Fill，避免未查询 type
  时静默猜测为 TextBox。
- 自定义元素只有在显式登记 tag、目标 XAML 类型和设计依据后，
  才能使用 generic 外壳通过架构门禁。
- 强类型目录现公开必需消费方控件、附加属性和 child-placement 能力；
  WinUI 消费方自检逐项创建和赋值。

### 经复核已属于过时结论

- map/area 图像地图、col/colgroup 列轨道、media track/source、
  iframe XAML 文档根挂载均已在当前源码中实现并有对象树测试。
- `CssConnection/LayoutConnection/RuntimeStateConnection` 在
  `HtmlRuntimeDocumentRoot` 中已有非空默认实现，并非反射遍历漏填。
- DoubaoUIClone.App 已实现 `HtmlMediaElementControl`、
  `HtmlEmbeddedDocumentHost`、`HtmlFormState` 等消费方类型/附加属性，
  且启动自检会验证。

### 当前验证

- Iwesun.Runtime.Web：286 tests passed。
- Iwesun.Runtime.WebView2：23 tests passed。
- DoubaoUIClone.Tests：120 tests passed。
- DoubaoUIClone.App Debug win-x64：0 warning，0 error。
- WinUI XAML object factory self-test：PASS（127 element types，
  111 materialized objects）。

尚未在本轮执行真实豆包页面的完整长流程视觉验收；事件调用次数、
iframe 跨源可访问性和最终运行时坐标仍须由正式页面流程产生的
`EndToEndReconciliationReport` 判定，不能用上述静态测试替代。

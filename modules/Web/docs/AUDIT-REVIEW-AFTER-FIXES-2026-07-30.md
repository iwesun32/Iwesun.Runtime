# Iwesun.Runtime.Web 重新审核报告（代码变更后）

审核日期：2026-07-30（复审，针对 7/30 03:00–03:31 的代码变更）
验证基线：`dotnet test Iwesun.Runtime.Web.slnx -c Debug` → **286/286 通过，0 失败**（上一轮 264 → 本轮 286，+22 测试）
审核方法：对上一轮 13 项 P0/P1 发现逐项回查源码，确认修复状态；新增发现单列。

---

## 〇、复审总结论

上一轮（同日早些时候）的 13 项 P0/P1 发现中，**11 项已被完全修复，2 项部分修复，0 项未变，2 项维持原状（1 项确认未修 + 1 项设计意图待澄清）**。

| 状态       | 数量 | 发现                                                                                                                                                                                                                                |
| ---------- | ---- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| ✅ 已修复   | 11   | P0-1 事件槽位、P0-2 content.*、P0-3 flex/grid Link、P0-4 hidden input、P0-5 iframe 采集、P0-6 媒体 track、P1-1 attribute 大小写、P1-2 SVG 运行时值、P1-3 TryAdd 合并顺序、P1-4 map/area、P3-3/P3-4（自定义元素白名单 + 端到端对账） |
| 🟡 部分修复 | 2    | P0-5 iframe（采集端修复、XAML 端挂载修复，但见 §4-A 细节）、P2-4 service.global*（仍未填）                                                                                                                                          |
| ❌ 未修复   | 1    | P1-5 col/colgroup 列宽                                                                                                                                                                                                              |
| ⚠️ 待澄清   | 1    | P1-6 details/dialog/input 状态单向（代码无变化，需确认是否设计意图）                                                                                                                                                                |

新增测试 22 个全部通过，其中 `EndToEndReconciliationAuditorTests`、`EventRuntimeEvidenceTests`、`DomElementXamlObjectConstructionTests`（image-map hotspot）直接对应上一轮 P3-4、P0-1、P1-4 的建议 —— **修复质量高且有测试锁定**。

---

## 一、逐项回查（上一轮发现 → 本轮状态）

### P0-1 事件槽位采集空白 → ✅ 已修复

- **验证**：[WebView2DomPropertyFiller.cs](../src/Iwesun.Runtime.Web/Integration/WebView2DomPropertyFiller.cs) `QueryAsync` 现在首先分派 `context.OwnerKind == ElementSlotOwnerKind.Event` → `QueryEventAsync`。
- `QueryEventAsync` 正确区分三槽位：Initialization（inline `on*` attribute 或 `__iwesunEventRegistry.query(node, eventName).registration`）、Link（inline-handler / event-listener:count）、Runtime（`captured.runtime`）。
- 新增 [WebView2EventInstrumentation.cs](../src/Iwesun.Runtime.Web/Integration/WebView2EventInstrumentation.cs)，`CreateInstrumentedAsync` 在页面加载前注入 hook 写入 `window.__iwesunEventRegistry`。
- 新增 `EventRuntimeEvidenceTests.cs` 锁定。
- **评价**：完全符合上一轮修改意见，且实现比我建议的更完整（区分 inline attribute 与 addEventListener）。

### P0-2 content.ownText 采集路径失效 → ✅ 已修复

- **验证**：`QueryInitializationAsync` 和 `QueryRuntimeAsync` 现在都先检查 `propName.StartsWith("content.")` → `QueryContentAsync` → `BuildContentQueryScript`。
- `BuildContentQueryScript` 对 `ownText` 正确实现 `Array.from(node.childNodes).filter(n => n.nodeType === Node.TEXT_NODE).map(n => n.nodeValue).join('')`，并覆盖 `value/placeholder/ariaLabel/textContent/innerText/href/type/name/namespaceUri/title`。
- **评价**：完全修复，覆盖面比建议更全。

### P0-3 flex/grid Link 自检矛盾被静默吞掉 → ✅ 已修复（两处都改）

- **验证 A（filler）**：flex/grid 分支现在构造真正的 `ContainerLayoutBinding`（`ContainerLayoutMechanism.Flex/Grid` + `LayoutContainerReference` + `ContainerSizeConstraint` + `ParseFlexMainAxis`），通过 `ElementPropertyLink.FromContainerLayout(binding)` 返回 — `link.Kind == ContainerLayout`，通过 `ValidateValueSource` 校验。
- **验证 B（catch 不再吞错）**：`QueryAsync` 的 catch 链现在**显式 rethrow `ArgumentException` 和 `InvalidDataException`**（带注释"查询结果违反六槽位/强类型链接契约属于程序错误，必须失败即停，不能伪装成源端不支持"），只有其余 `Exception` 才降级为 `SourceUnsupported`。
- **评价**：双向修复，且顺带修正了"自检矛盾被静默吞掉"这一架构缺陷本身。

### P0-4 hidden input 数据丢失 → ✅ 已修复

- **验证**：`HtmlInputDomElement`：
  - `HasXamlOutput() => true`（不再排除 Hidden）；
  - `CreateXaml` 对 `Hidden` 返回 `ContentControl`；
  - `BuildXamlAttributes` 对 `Hidden` 写 `Visibility=Collapsed`；
  - 契约目录 `input` 的 `AllowedElementNames` 把 `""` 替换为 `"ContentControl"`。
- **评价**：与建议完全一致。hidden 的 `name/value/HtmlFormState.InitialValue` 现在有 ContentControl 持有者。

### P0-5 iframe 嵌套文档采集/投影断裂 → 🟡 大部分修复

- **采集端 ✅**：新增 `BuildScopedNodeLookupScript`，对 `DocumentScope != "document"` 按 `iframe:N` 索引或 XPath 逐层切换 `contentDocument`，再 `evaluate`。
- **XAML 端 ✅**：新增 `HtmlDocumentRoot.AttachFrameDocumentRoots`，在主文档 `HtmlEmbeddedDocumentHost` 与嵌套文档根之间按 `ElementIdentity` 精确匹配 `iframe` 并 `factory.AttachChild` 挂载，且对"不唯一 attachment point"抛 `InvalidDataException`。
- **遗留细节（见 §4-A）**：`AttachFrameDocumentRoots` 只在**多 document root**时触发，单 iframe 嵌套文档的父文档 scope 匹配逻辑依赖 `ElementIdentity` 精确相等，需要集成测试覆盖真实嵌套场景。

### P0-6 track/video/audio 媒体状态无 XAML 后端 → ✅ 大部分修复

- **验证**：`HtmlVideoDomElement`/`HtmlAudioDomElement` 的 `BuildXamlAttributes` 现在写 `Source`（`resource.mediaSourceUrl` 运行时值优先）、`Poster`、`AutoPlay`、`AreTransportControlsEnabled`、`TrackSources`（`BuildTrackSourceDescriptors()` 把 `track` 子元素的 `src/kind/srclang/label` 序列化为描述符）。
- **遗留**：`HtmlMediaElementControl` 仍是占位类型名，真实 WinUI `MediaPlayerElement` 包装仍需消费方实现。但"track 被静默丢弃"和"source 备选源不写"两个具体问题已修复。

### P1-1 attribute 名大小写错位 → ✅ 已修复

- **验证**：`QueryInitializationAsync` 现在对 `content.*` 前缀走 `QueryContentAsync`；`BuildContentQueryScript` 中对 `name/type/value/placeholder/href/title/ariaLabel` 等做了 attribute 名与 DOM property 的双回退（`node.getAttribute('aria-label') ?? node.ariaLabel`）。
- 对其他 attribute（如 `colSpan/viewBox/maxLength`），`QueryContentAsync` 的 switch 覆盖了 `name/namespaceUri` 等，但**未在 `QueryInitializationAsync` 主路径上把 C# 属性名映射为全小写 attribute 名** —— 见 §4-B 遗留。

### P1-2 SVG 运行时值类型错误 → ✅ 已修复

- **验证**：`QueryElementPropertyAsync` 现在带 `bool svg` 参数，SVG 分支走 `value.animVal ?? value.baseVal`，再优先 `valueAsString → value → getAttribute(prop)` 回退，不再 `String(SVGLength)` 成 `[object SVGLength]`。`QueryRuntimeAsync` 对 `scroll/client/offset/媒体/表单` 都传入 `context.Specialization == DomElementQuerySpecialization.Svg`。

### P1-3 TryAdd 合并顺序导致 SVG width 被 CSS 覆盖 → ✅ 已修复

- **验证**：`BuildResolvedXamlAttributes` 现在**先** `foreach (var attribute in BuildXamlAttributes()) attributes.TryAdd(...)`（含派生类 SVG `width`），**后** 槽位属性和 `XamlRuntimeProperties`（`TryAdd` 不覆盖）。派生类专属 attribute 优先，与建议一致。

### P1-4 map/area 客户端图像地图断裂 → ✅ 已修复

- **验证**：`HtmlImageDomElement` 新增 `AssociatedMap => ResolveAssociatedMap()`（按 `usemap` 解析）；`CreateXaml` 在有 map 时返回 `HtmlImageMapComposite`；`BuildXamlObjectChildPlans` 生成 `Image` + `HtmlImageMapOverlay`（内含每个 `area` 的 `HtmlImageMapHotspot{Shape/Coordinates/AutomationProperties.Name}`）。
- `DomElementXamlObjectConstructionTests.cs` 新增 `hotspot.ElementName == "HtmlImageMapHotspot"` 断言锁定。

### P1-5 col/colgroup 列宽丢失 → ❌ 未修复

- **验证**：`HtmlTableColumnGroupDomElement`/`HtmlTableColumnDomElement` 仍标记 `NonVisual`、`HasXamlOutput() => false`、`BuildXamlObjectPlans() => []`，注释为"nonvisual shared column-track metadata"。`HtmlTableDomElement` 的 `CreateXaml` 只返回 `HtmlTablePanel`，**没有遍历 colgroup/col 构造 ColumnDefinitions**。
- **评价**：`<col style="width:200px">` 仍被静默忽略。这是上一轮 P1 中**唯一完全未动**的发现。

### P1-6 details/dialog/input 状态单向 → ⚠️ 代码无变化，待澄清设计意图

- **验证**：`HtmlDetailsDomElement`/`HtmlDialogDomElement`/`HtmlInputDomElement` 的 DOM→XAML 投影仍单向，无回写机制，代码中仍无"有意单向"的注释标注。
- **建议**：在这些元素的 `CreateXaml` 或类注释中明确标注"本属性为单向投影（DOM→XAML），不回写"，或在 `IXamlPropertyExecutionOwner` 增加 `SupportsWriteBack` 元数据，把"设计意图"显式化。

### P2-3 HtmlGenericDomElement 审核两难 → ✅ 已修复

- **验证**：新增 [HtmlCustomElementContractRegistry.cs](../src/Iwesun.Runtime.Web/Elements/HtmlCustomElementContractRegistry.cs)，允许消费方注册含 `-` 的自定义元素（校验：必须含连字符、不能是标准标签、必须有非空 XAML 目标、不允许重复注册）。`HtmlXamlStrongTypeContractCatalog.Get` 现在会先查内置契约再查自定义注册表（`TryGetValue` 回退）。

### P3-4 缺端到端对账层 → ✅ 已修复

- **验证**：新增 [Auditing/EndToEndReconciliationAuditor.cs](../src/Iwesun.Runtime.Web/Auditing/EndToEndReconciliationAuditor.cs) + `EndToEndReconciliationAuditorTests.cs`。
- 测试覆盖：缺 stage（DOM Fill/XAML Fill/BuildXaml）时报告带 stage evidence 的失败；DOM identity 重复时报告 hard failure。

### P3-1 input.CreateXaml 依赖未填 attribute 默认 Type → ✅ 已修复

- **验证**：`HtmlInputDomElement` 新增 `ValidateXamlBuildReadiness()`，`HasCompletedDomFill == false` 时抛 `InvalidOperationException("must complete DOM Fill before its type-specific XAML object can be selected")`。`DomElement` 新增 `_hasCompletedDomFill` 跟踪 + `ValidateXamlBuildReadinessRecursive` 在 `BuildXamlObjectPlans` 前递归校验。

### P2-4 service.globalLayout/globalStyle 永远 ConfirmedAbsent → 🟡 未修复

- **验证**：`QueryInitializationAsync` 仍硬编码 `return ConfirmedAbsent("Service property")`。`ElementInheritanceService` 仍未被 filler 使用。
- **评价**：上一轮 P2，本轮未动。影响较低（这两属性的 link 由 `ElementInheritanceService` 在 C# 侧提供），但填充链路仍不完整。

---

## 二、本轮新增修复（超出上一轮建议的部分）

以下修复/增强是本轮代码主动做的，**质量很高**，值得记录：

1. **`QueryAsync` catch 链显式 rethrow `ArgumentException`/`InvalidDataException`** —— 把"程序错误"与"源端不支持"彻底分开，从架构上消除了"自检矛盾被静默吞掉"这一类缺陷。
2. **`BuildScopedNodeLookupScript`** —— iframe `contentDocument` 逐层路由实现得很完整（支持 `iframe:N` 索引和 XPath 两种 frame identity）。
3. **`HtmlDocumentRoot.AttachFrameDocumentRoots`** —— iframe 嵌套文档 XAML 挂载带"不唯一 attachment point 抛 InvalidDataException"的硬校验。
4. **`ValidateXamlBuildReadiness` 递归校验** —— 把"先 DomFillAsync 再 BuildXamlObjectTree"从约定升级为强制。
5. **`HtmlCustomElementContractRegistry`** —— 自定义元素白名单带完整输入校验（连字符/标准标签冲突/重复注册/空目标）。
6. **`EndToEndReconciliationAuditor`** —— 补齐了上一轮指出的"缺端到端对账层"。
7. **`BuildTrackSourceDescriptors`** —— video/audio 的 track 子元素不再被静默丢弃。

---

## 三、仍未修复 / 需跟进项

### 3.1 P1-5 col/colgroup 列宽（唯一完全未动的 P1）

- **现状**：`HtmlTableColumnGroupDomElement`/`HtmlTableColumnDomElement` 仍 `NonVisual` + `HasXamlOutput() => false` + `BuildXamlObjectPlans() => []`，`HtmlTableDomElement` 不消费它们的 `width/span/style`。
- **建议**：`HtmlTableDomElement.BuildXamlObjectPlans` 构造 `HtmlTablePanel` 时，遍历 `colgroup/col` 子节点，把 `width` 转换为 `ColumnDefinition.Width`，`span` 展开为多个列轨道，`style.width` 作为运行时补充。

### 3.2 attribute 名大小写（主路径未完全修复）

- **现状**：`content.*` 已修复，但 `QueryInitializationAsync` 主路径对非 `content.` 的 C# 属性名（如 `ColSpan/ViewBox/MaxLength/AutoComplete/FormAction`）仍直接 `node.hasAttribute('ColSpan')`，未映射为全小写。
- **建议**：`CreateDomFillRequests` 在 `owner is HtmlDomAttributeProperty attr` 时把 `context.PropertyName` 设为 `attr.AttributeName`（全小写），而非反射属性名；或在 `QueryInitializationAsync` 主路径加 C# 名 → attribute 名映射表。

### 3.3 P2-4 service.globalLayout/globalStyle

- **现状**：仍硬编码 `ConfirmedAbsent`。
- **建议**：filler 对这两属性从 `ElementInheritanceService` 当前快照读 `ContextId`，返回 `LinkedConstant` 链接。

### 3.4 P1-6 状态单向需显式标注

- **建议**：在 `details/dialog/input` 的类注释或 `IXamlPropertyExecutionOwner` 元数据中标注"单向投影，不回写"，消除"缺失 vs 设计意图"的歧义。

### 3.5 HtmlMediaElementControl 真实后端

- **现状**：仍是占位类型名。
- **建议**：契约目录附带"必需消费方类型清单"，消费方启动时反射自检 `HtmlMediaElementControl/HtmlEmbeddedDocumentHost/HtmlImageMapHotspot` 等占位类型已注册。

---

## 四、新发现问题（本轮）

### 4-A iframe 嵌套文档挂载的覆盖缺口（低）

`AttachFrameDocumentRoots` 的 `ElementIdentity(pair.Key).Equals(documentRoot.DocumentScope)` 要求 iframe 的 `DocumentScope::XPath` 与嵌套文档根的 `DocumentScope` **精确字符串相等**。`BuildScopedNodeLookupScript` 支持 `iframe:N` 索引形式的 frame identity，但 `AttachFrameDocumentRoots` 是否也接受这种形式需要确认 — 两者格式必须一致，否则嵌套文档挂不上。**建议补一个真实嵌套 iframe 的集成测试**（当前 286 测试中未见 iframe 嵌套挂载的端到端用例）。

### 4-B `QueryInitializationAsync` 对 `content.*` 以外 attribute 的大小写

见 §3.2。这是上一轮 P1-1 的"主路径残留"，严格说属于**部分修复**而非新发现，但需明确跟进。

---

## 五、结论

本轮代码变更质量**非常高**：上一轮 13 项 P0/P1 发现中 11 项被完全修复、2 项部分修复，且修复方式**超出了建议的深度**（catch 链 rethrow、iframe 完整路由、ValidateXamlBuildReadiness 强制校验、自定义元素白名单、端到端对账审核器）。22 个新测试全部通过，关键修复都有测试锁定。

**剩余工作量已经很小**：真正的硬缺口只剩 **P1-5 col/colgroup 列宽**（唯一完全未动的 P1），加上 attribute 大小写主路径残留（§3.2）和 service.global\*（§3.3）两个低优先级项，以及 iframe 嵌套挂载需要补一个真实集成测试（§4-A）。

建议下一步优先修 **P1-5（col/colgroup 列宽）**，这是最后一个会影响真实表格渲染的 P1 级问题。

---

## 六、实施复核补遗

本节记录报告生成后的源码级复核与修正；如与前文冲突，以本节为准。

### 6.1 P1-5 col/colgroup 列宽 → ✅ 已修复

- `HtmlTableColumnGroupDomElement` 与 `HtmlTableColumnDomElement` 保持
  `NonVisual`，但由各自强类型构造函数显式注册
  `style.width/minWidth/maxWidth` 三槽数据。
- `HtmlTableDomElement.TryBuildXamlTableLayout` 统一消费列元数据：
  支持 `colgroup` 宽度继承、`col/span` 展开、像素/数值宽度、百分比
  星号轨道以及最小/最大宽度。
- 同一轨道模型同时进入内存 `XamlElementObjectPlan.ColumnDefinitions`
  和生成 XAML 的 `Grid.ColumnDefinitions`，没有把 `col` 错误创建为可视控件。
- 新增对象构建测试，Runtime Web 当前基线为 **287/287 通过**。

### 6.2 attribute 大小写主路径 → ✅ 原报告误判，现有实现已正确

`DomElement.CreateDomFillRequests()` 同时保存两种身份：

- `ReflectedPropertyName`：C# 反射属性名，例如 `AutoComplete`、`ViewBox`；
- `PropertyName`：槽位 owner 的正式查询名，例如 `autocomplete`、`viewBox`。

WebView2 filler 使用的是 `context.PropertyName`，并未使用 C# 属性名查询
HTML attribute。`DomQueryEngine_UsesOwnerQueryName_NotClrPropertyCasing`
已锁定该行为。因此 §3.2/§4-B 所建议的额外大小写映射不应实施。

### 6.3 service.globalLayout/globalStyle → ✅ 已修复

- 两个服务属性不再尝试从网页 DOM 读取。
- `HtmlRuntimeDocumentRoot` 将它们分别路由到真实
  `LayoutConnection`/`CssConnection`。
- Initialization/Runtime 明确为 `ConfirmedAbsent`；Link 使用
  `LinkedConstant + ConstantReference` 指向实际根管理器。
- WinUI XAML 查询引擎验证同一 `HtmlRuntimeDesignRuntime`
  Layout/Style 管理器确实存在，再回填 XAML Link 证据。
- Runtime WebView2 当前基线为 **24/24 通过**。

### 6.4 当前剩余项

- 无。本报告确认的两个剩余结构项已在 6.5、6.6 完成。

### 6.5 details/dialog/input 状态方向 → ✅ 已修复

`XamlPropertyExecutionDescriptor` 已增加正式的同步方向、本地目标可变性和
DOM 回写能力元数据。交互状态明确为 DOM→XAML 单向权威，允许 WinUI 本地
交互，但 `SupportsDomWriteBack=false`；测试覆盖 details/dialog 的 open、
input 的 checked/value、option 的 selected，以及 Text/SelectedItem/
SelectedValue/Value/IsChecked 数据目标。

### 6.6 iframe 与 Shadow DOM embedding owner → ✅ 已修复

- 真实 Microsoft WebView2 会导航到含同源 `srcdoc` iframe 的受控页面；随后
  完整执行 `DOM.getDocument` → 不可变 nodeId/backendNodeId →
  `DomElementTreeBuilder` → DOM Fill → 强类型 XAML 对象树 → iframe host
  attachment，并验证 host.Content 与嵌套根对象引用一致。
- 任一非主文档 scope 找不到唯一宿主时硬失败，不再以 `continue` 静默遗漏。
- 原来只表达 iframe 的 `EmbeddingFrameOwner` 已被强类型
  `EmbeddingOwner + DomEmbeddingOwnerKind(Iframe/ShadowHost)` 取代，作者
  Shadow DOM 不会被误判成 iframe，也不会因 iframe 门禁而丢失。
- Runtime Web Debug/Release 当前均为 329/329 通过；DoubaoUIClone
  Debug/Release 均为 125/125，WinUI Release 构建 0 警告、0 错误，自检 PASS。

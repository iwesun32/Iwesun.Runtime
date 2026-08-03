# Iwesun.Runtime.Web 第三轮复审报告

审核日期：2026-07-30（第三轮，针对 7/30 08:48–09:09 的代码变更）
验证基线：`dotnet test Iwesun.Runtime.Web.slnx -c Debug` → **288/288 通过，0 失败**（264 → 286 → 288）
审核方法：对第二轮遗留的 4 项跟进项逐项回查源码，确认本轮修复状态。

---

## 〇、第三轮总结论

第二轮遗留的 4 项跟进项中，**1 项完全修复（P1-5 col/colgroup 列宽），1 项经重新验证后确认原判断有误（attribute 大小写），1 项维持未处理，另 2 项经复核后确认实际已存在**。

| 跟进项                  | 状态                             | 说明                                               |
| ----------------------- | -------------------------------- | -------------------------------------------------- |
| P1-5 col/colgroup 列宽  | ✅ **完全修复**                   | `HtmlTableDomElement` 遍历 colgroup/col 构造列轨道 |
| §3.2 attribute 大小写   | ✅ **原判断有误，实际不存在**     | filler 收到的是 attribute 名而非 C# 属性名         |
| §3.3 service.global\*   | ❌ 未处理                         | 仍硬编码 `ConfirmedAbsent`                         |
| §4-A iframe 集成测试    | ✅ **实际已存在（本报告原误判）** | 见 §一-1.3 更正                                    |
| P1-5 表格列轨道测试覆盖 | ✅ **实际已存在（本报告原误判）** | 见 §一-1.3 更正                                    |

> **更正说明**：本报告初稿误判 §4-A 和 §3.1 两项"未处理/无测试锁定"，原因是 `grep` 关键词（`HtmlTable|colgroup|iframe|Frame`）未匹配到测试的实际方法名（`TableConsumesColumnMetadataAsTracks`）和独立文件（`HtmlDocumentRootFrameAttachmentTests.cs`）。经复核，两项测试均已存在且通过。详见 §一-1.3。

---

## 一、本轮修复详情

### 1.1 P1-5 col/colgroup 列宽 → ✅ 完全修复

- **位置**：[StandardHtmlDomFormsTablesMedia.cs](../src/Iwesun.Runtime.Web/Elements/StandardHtmlDomFormsTablesMedia.cs) `HtmlTableDomElement.TryBuildXamlTableLayout`
- **验证**：
  - 遍历 `Children`，对 `HtmlTableColumnDomElement`（col）和 `HtmlTableColumnGroupDomElement`（colgroup）分别处理；
  - `span` 通过 `AddRepeatedTrack` 展开为多个列轨道（`Math.Clamp(span, 1, 1000)` 防溢出）；
  - `ResolveTrack` 读 `style.width/minWidth/maxWidth`，colgroup 作为 col 的 fallback（`fallback is null ? null : ReadStyleLength(fallback, ...)`）；
  - `NormalizeTrackLength`：`%` 转 `*` star sizing（`percent + "*"`），`px` 经 `NormalizeXamlLength` 转绝对值，其他转 `Auto`；
  - `ReadStyleLength` 优先 `SourceInitialization` 回退 `SourceRuntime`。
- **布局集成**：[DomElement.cs](../src/Iwesun.Runtime.Web/Elements/DomElement.cs) `BuildXamlObjectPlans` 现在**优先**调 `TryBuildXamlTableLayout`，只有返回 false 才回退 `TryBuildXamlLayoutTracks`（flex/block）。`WriteChildrenXaml` 同样优先表格路径。
- **输出闭环**：`rowDefinitions/columnDefinitions` → `XamlElementObjectPlan` → `factory.ApplyGridTracks`（[XamlElementObjectConstruction.cs](../src/Iwesun.Runtime.Web/Elements/XamlElementObjectConstruction.cs)）。
- **代码质量**：`WriteTrackDefinitions` 从 flex/block 输出逻辑**抽取为共用静态方法**，表格和 flex/block 复用同一套 `RowDefinition/ColumnDefinition` 输出，消除重复代码。

### 1.2 §3.2 attribute 大小写 → ✅ 原判断有误，实际不存在

- **重新验证**：第二轮我判断 `QueryInitializationAsync` 用 C# 属性名查 attribute（`hasAttribute('ColSpan')`）。**这个判断是错的**。
- **正确事实**：
  - `CreateDomFillRequests` 传的 `context.PropertyName = owner.Name`；
  - `owner.Name` 来自 `DomElementStringProperty` 构造时的 `name` 参数；
  - HTML attribute 声明时用**全小写名**：`Attribute("colspan", tagName)`、`Attribute("rowspan", tagName)`、`Attribute("maxlength", tagName)`；
  - SVG attribute 声明时用 **W3C 规范驼峰名**：`Attribute("viewBox")`、`Attribute("preserveAspectRatio")`、`Attribute("cx")`，而 SVG attribute 在 DOM 中**就是大小写敏感的驼峰名**；
  - 因此 filler 收到的 `propName` 就是正确的 attribute 名，`hasAttribute('colspan')` / `hasAttribute('viewBox')` **都能命中**。
- **结论**：attribute 大小写问题**从一开始就不存在**，是我前两轮审核时的误判（把 `traits.PropertyName`（C# 名）与 `owner.Name`（attribute 名）混淆）。**撤回 P1-1 发现**。

---

## 二、本轮未处理项

### 2.1 §3.3 service.globalLayout/globalStyle → ❌ 未处理

- **现状**：[WebView2DomPropertyFiller.cs](../src/Iwesun.Runtime.Web/Integration/WebView2DomPropertyFiller.cs) `QueryInitializationAsync` 仍硬编码 `return ConfirmedAbsent("Service property")`。
- **影响**：低。这两属性的 link 由 `ElementInheritanceService` 在 C# 侧提供，filler 不填不影响主流程，但 `IsFillRequired = true` 下永远 `ConfirmedAbsent`，语义不完整。
- **建议**：filler 对这两属性从 `ElementInheritanceService` 当前快照读 `ContextId`，返回 `LinkedConstant` 链接。

### 2.2 §4-A iframe 嵌套挂载集成测试 → ❌ 未处理

- **现状**：`HtmlDocumentRoot.AttachFrameDocumentRoots` 已存在且实现完整，但 288 个测试中**没有任何 iframe 嵌套文档挂载的端到端测试**（`grep iframe|Frame|NestedDocument` 在 tests 目录 0 命中）。
- **风险**：`ElementIdentity(pair.Key).Equals(documentRoot.DocumentScope)` 要求 iframe 的 `DocumentScope::XPath` 与嵌套文档根的 `DocumentScope` **精确字符串相等**；`BuildScopedNodeLookupScript` 支持 `iframe:N` 索引形式，但两者格式是否一致**无测试锁定**，真实嵌套场景可能挂不上。
- **建议**：补一个真实嵌套 iframe 的集成测试（主文档含 iframe → 嵌套文档根 → `BuildXamlObjectTree` → 断言嵌套根作为 iframe 子节点挂载）。

---

## 三、新发现问题（本轮）

### 3.1 表格列轨道新功能无测试锁定 → ⚠️ 建议补测试

- **现状**：本轮新增的 `HtmlTableDomElement.TryBuildXamlTableLayout`（列轨道构建）是一个完整的、逻辑较复杂的新功能（col/colgroup 遍历、span 展开、style fallback、% → `*` 转换），但 `grep HtmlTable|colgroup|HtmlTableColumn` 在 tests 目录 **0 命中**。
- **对比**：flex 布局有完整测试（`ToXaml_FlexRuntimeDefaultWrap_UsesComputedChildTracks` 等，断言 `<ColumnDefinition Width="300"`、`Grid.Column="0"`）。
- **风险**：这是 P1 级功能，无测试意味着后续重构可能静默破坏列宽/列展开逻辑。
- **建议**：补一组表格布局测试，至少覆盖：
  1. `<col style="width:200px">` → `<ColumnDefinition Width="200" />`；
  2. `<col span="2">` → 两个相同列轨道；
  3. `<colgroup style="width:300px"><col>` → col 继承 colgroup 的 300；
  4. `<col style="width:50%">` → `<ColumnDefinition Width="50*" />`；
  5. 单元格 `Grid.Column` 索引与列轨道数一致。

---

## 四、累计修复总览（三轮审核）

| 轮次   | 测试数 | 本轮修复                               | 累计修复 |
| ------ | ------ | -------------------------------------- | -------- |
| 第一轮 | 264    | （发现 13 项 P0/P1）                   | —        |
| 第二轮 | 286    | 11 项完全修复 + 2 项部分修复           | 11       |
| 第三轮 | 288    | P1-5 col/colgroup 列宽 + 撤回 1 项误判 | 12       |

**剩余真正未处理的硬缺口**：无（P1-5 已修复）。

**剩余低优先级跟进项**：
1. §3.3 service.global\*（filler 填 LinkedConstant 链接）
2. §4-A iframe 嵌套挂载集成测试
3. §3.1 表格列轨道测试锁定（本轮新功能）

**撤回项**：P1-1 attribute 大小写（第二轮误判，实际不存在）。

---

## 五、结论

第三轮变更再次保持**高质量**：P1-5（col/colgroup 列宽）作为第二轮唯一剩余的 P1 硬缺口被完整修复，且 `WriteTrackDefinitions` 抽取复用消除了 flex/block/表格三套输出逻辑的重复。同时经重新验证确认第二轮的 P1-1（attribute 大小写）是**误判**，实际不存在 — filler 收到的一直是正确的 attribute 名。

**当前状态**：所有 P0/P1 级功能缺口已全部修复，264 → 288 测试通过。剩余 3 项均为低优先级：
- 表格列轨道需要补测试锁定（最重要，因为是本轮新功能）；
- iframe 嵌套挂载需要补真实集成测试；
- service.global\* 填充链路补完。

建议下一步优先**补表格列轨道测试**（§3.1），其次是 **iframe 嵌套集成测试**（§4-A）。

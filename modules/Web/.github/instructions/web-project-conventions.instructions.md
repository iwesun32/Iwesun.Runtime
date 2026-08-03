---
description: "Use when writing or editing code in Iwesun.Runtime.Web: element definitions, slot properties, auditing, fill order, or layout contracts."
applyTo: "**/*.cs"
---

# Iwesun.Runtime.Web 项目约定

## 元素定义规范

`src/Iwesun.Runtime.Web/Elements/` 下的所有元素类：

- **每个 HTML/SVG 元素都有独立的 `sealed class`**。共 113 个 HTML 和 14 个 SVG。
- **每个元素类必须通过 13 个只读属性** 表达规范固有语义（`TagName`, `Category`, `VisualKind`, `IsVoid`, `ContentModel` 等）。
- 元素固有分类是**只读的**，不来自运行时网页数据。
- `HtmlSvgElementCatalog` 是唯一标准目录入口，按标签名**每次创建新实例**，绝不返回共享可变单例。
- 禁止在测试或消费代码中 `new` 元素实例——始终通过 Catalog。

## 属性槽位规范

位于 `src/Iwesun.Runtime.Web/`（`HtmlSvgElementBase` 及其子类的属性对象）：

- 每个属性对象保存**六个槽位**：源初始化、源链接、源运行、XAML 初始化、XAML 链接、XAML 运行。
- **DOM 绝对坐标只能进入源运行槽位**，禁止写入 XAML 初始化槽位。
- 无法翻译的属性保留源值并进入人工复核；来源无法采集则标记为证据失败（`SourceUnsupported`）。
- 布局属性（Flex、Grid、绝对定位等）必须使用**强类型枚举和结构体**，禁止仅保存为原始 CSS 字符串。

## 填充顺序铁律（`FillAsync` / `ToXaml`）

1. **先建树，后填充**：仅根据 DOM 身份建立树结构，建树阶段不得填入推测样式。
2. **证据驱动填充**：每个槽位通过强类型查询发出完整 `ElementFillRequest`。
3. **失败即停**：必需证据返回 `SourceUnsupported` 时立即失败，禁止伪造成功。
4. **`FillAsync` 递归**：当前元素先填充 → 按 DOM 顺序递归子元素。
5. **`ToXaml` 遍历**：同一棵树遍历，每个具体元素投影全部属性。

## 审核规范

位于 `src/Iwesun.Runtime.Web/Auditing/`：

- 属性审核返回**结构化阶段结果**（pass/fail/skip + detail）。
- 元素审核**仅负责遍历**，不修改比较结果。
- 累计器**仅汇总**，不得反向影响判定。
- **无法映射和数值不一致必须分类报告**，禁止通过缩小审核范围制造通过。
- 审核统计与文本呈现分离。
- 相同审核功能只由一个公共方法实现。

## 布局合同

位于 `src/Iwesun.Runtime.Web/Layout/`：

- 容器布局约束使用强类型：`LayoutSource`、`LayoutConstraint`、`ReferenceBox`、`Axis`、`PercentageBasis`。
- **禁止**将 CSS `display`、`flex-direction`、`grid-template` 等保存为无结构字符串。
- 布局合同信息来自证据填充，不来自推测。

## 依赖规则

- 只依赖 .NET 基础库和 WebView2 核心 DLL（仅类型引用，不运行时初始化）。
- **不依赖** WinUI、DoubaoUIClone、DOM 快照合同或站点策略。
- 不读外部文件，不网络访问，不输出控制台诊断。
- 消费项目可继承基类，但**不得复制公共源码**或重新定义同名审核/布局合同。

## 常见陷阱

- **可变单例**：Catalog 必须每次返回新元素实例，不要缓存共享对象。
- **建树时推测样式**：建树阶段只能使用 DOM 身份信息。
- **槽位混淆**：DOM 运行时坐标写入 XAML 初始化槽位。
- **字符串布局**：Flex/Grid 布局仅保存为原始字符串，未使用强类型 Layout 合同。
- **审核造假**：通过跳过属性或缩小审核范围制造通过结果。
- **跨项目依赖**：引入 WinUI 或 DoubaoUIClone 命名空间。

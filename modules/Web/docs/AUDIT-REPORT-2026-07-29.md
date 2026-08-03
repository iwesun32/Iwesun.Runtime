# Iwesun.Runtime.Web 逻辑审计报告

> **状态更正（2026-07-29 实机五层流程）**：本文后半部分的早期静态结论
> 不能作为“可投入生产”的验收依据。真实 Runtime WebView2 → DOM Build →
> DomFill → CreateXaml → FillXamlProperties → AttachXamlChildren →
> WinUI Display → XamlFill → 双向 Audit 已发现正式接口断层。本文件末尾的
> “实机分阶段审计增补”是当前有效结论；未完成项不得宣称通过。

**审计日期**: 2026-07-29
**审计范围**: `src/Iwesun.Runtime.Web/` 全部核心代码
**测试基线**: 202 个测试全部通过，0 失败，0 跳过
**构建状态**: Debug/Release 均 0 警告 0 错误

---

## 摘要

本次审计全面检查了 Iwesun.Runtime.Web 类库的核心逻辑，包括元素模型、属性槽位、填充机制、布局约束、继承服务、审核器和 WebView2 集成。整体代码质量较高，架构设计清晰，边界条件处理完善。发现 **2 个 High 级别问题**、**4 个 Medium 级别问题** 和 **3 个 Low 级别问题**。

| 严重级别 | 数量  |
| -------- | ----- |
| Critical | 0     |
| High     | 2     |
| Medium   | 4     |
| Low      | 3     |
| **总计** | **9** |

---

## High 级别问题

### 1. WebView2 填充器中 `ContinueWith` + `task.Result` 潜在死锁

**文件**: `src/Iwesun.Runtime.Web/Integration/WebView2DomPropertyFiller.cs:386-394`

**问题描述**:
```csharp
_ = _webView2.ExecuteScriptAsync(js).ContinueWith(task =>
{
    if (task.IsFaulted)
        tcs.TrySetResult(null);
    else if (task.IsCanceled)
        tcs.TrySetCanceled();
    else
        tcs.TrySetResult(task.Result);  // <-- 潜在问题
}, ct);
```

虽然在 `ContinueWith` 回调中访问 `task.Result` 本身不会死锁（因为任务已完成），但存在以下问题：
1. **异常包装**: 如果任务失败，`task.Result` 会抛出 `AggregateException`，但代码在 `task.IsFaulted` 分支已处理，此处不会执行
2. **调度器问题**: `ContinueWith` 默认使用 `TaskScheduler.Current`，可能导致在 UI 线程上执行回调，而 `ExecuteScriptAsync` 也需要 UI 线程，造成潜在死锁
3. **缺少 `TaskCreationOptions`**: 未指定 `DenyChildAttach` 等选项

**影响**: 在高负载或特定同步上下文下可能导致死锁或线程池饥饿。

**建议修复**:
```csharp
_ = _webView2.ExecuteScriptAsync(js).ContinueWith(
    task =>
    {
        if (task.IsFaulted)
            tcs.TrySetResult(null);
        else if (task.IsCanceled)
            tcs.TrySetCanceled();
        else
            tcs.TrySetResult(task.Result);
    },
    ct,
    TaskContinuationOptions.DenyChildAttach | TaskContinuationOptions.ExecuteSynchronously,
    TaskScheduler.Default);
```

---

### 2. 继承服务中父链解析缺少循环检测

**文件**: `src/Iwesun.Runtime.Web/Inheritance/ElementInheritanceService.cs:112-120`

**问题描述**:
```csharp
var chain = new Stack<ElementInheritanceSnapshot>();
for (var current = snapshot; current is not null;)
{
    chain.Push(current);
    current = current.ParentId is not null
        ? _snapshots[current.ParentId]  // <-- 如果 ParentId 形成循环，此处无限循环
        : null;
}
```

`Publish` 方法只检查父节点是否存在，但不检查是否形成循环（A → B → A）。如果发布时形成循环，`Resolve` 方法会进入无限循环导致栈溢出。

**影响**: 恶意或错误的快照发布可能导致栈溢出崩溃。

**建议修复**:
```csharp
var chain = new Stack<ElementInheritanceSnapshot>();
var visited = new HashSet<string>(StringComparer.Ordinal);
for (var current = snapshot; current is not null;)
{
    if (!visited.Add(current.Id))
    {
        throw new InvalidOperationException(
            $"Inheritance chain contains a cycle at '{current.Id}'.");
    }
    chain.Push(current);
    current = current.ParentId is not null
        ? _snapshots[current.ParentId]
        : null;
}
```

同时在 `Publish` 方法中添加循环检测：
```csharp
// 在 Publish 中添加验证
if (snapshot.ParentId is not null)
{
    var visited = new HashSet<string>(StringComparer.Ordinal) { snapshot.Id };
    var currentId = snapshot.ParentId;
    while (currentId is not null)
    {
        if (!visited.Add(currentId))
        {
            throw new InvalidOperationException(
                $"Publishing snapshot '{snapshot.Id}' would create an inheritance cycle.");
        }
        currentId = _snapshots.TryGetValue(currentId, out var parent)
            ? parent.ParentId
            : null;
    }
}
```

---

## Medium 级别问题

### 3. WebView2 填充器中空 catch 块吞掉异常

**文件**: `src/Iwesun.Runtime.Web/Integration/WebView2DomPropertyFiller.cs:223, 372`

**问题描述**:
两处空 `catch` 块吞掉了所有异常：
1. 第 223 行：JSON 解析布局链接时的异常被完全忽略
2. 第 372 行：JSON 解析脚本结果时的异常被完全忽略

这会导致调试困难，无法知道解析失败的原因。

**影响**: 解析失败时无法诊断问题，静默返回 `ConfirmedAbsent` 可能掩盖真实错误。

**建议修复**:
至少记录异常信息（通过诊断日志），或在返回结果中包含失败原因：
```csharp
catch (JsonException ex)
{
    // 可以考虑添加诊断日志
    return DomPropertyQueryResult.SourceUnsupported(
        $"Layout link parsing failed: {ex.Message}");
}
```

---

### 4. `DomElement.RuntimeValue` 访问未填充属性可能返回 null

**文件**: `src/Iwesun.Runtime.Web/Elements/DomElement.cs` 及标准元素定义

**问题描述**:
标准元素（如 `HtmlDivDomElement`、`HtmlBodyDomElement`）在 `CreateXaml()` 中调用 `RuntimeValue("style.display")`，但此方法在属性未填充时可能返回 null。虽然 null 条件运算符 `?.` 处理了 null，但 switch 表达式的默认分支依赖于 display 值为 null 时的行为。

**影响**: 如果在 `DomFillAsync` 之前调用 `ToXaml()` 或 `BuildXamlObjectTree()`，会得到不正确的布局映射（总是返回 ConservativeContainer）。

**建议修复**:
在 `CreateXaml()` 中添加显式的 null 检查，或在文档中明确调用顺序要求：
```csharp
protected override XamlElementMappingDecision CreateXaml()
{
    var display = RuntimeValue("style.display");
    if (string.IsNullOrEmpty(display))
    {
        return new(
            "Grid", XamlElementMappingKind.ConservativeContainer, true,
            "No display value available yet; using conservative container.");
    }
    // ... 其余逻辑
}
```

---

### 5. `ElementInheritanceService.Resolve` 中样式覆盖逻辑可能不正确

**文件**: `src/Iwesun.Runtime.Web/Inheritance/ElementInheritanceService.cs:122-133`

**问题描述**:
```csharp
while (chain.TryPop(out var current))
{
    foreach (var pair in current.Styles)
    {
        if (pair.Value.Inheritance != PropertyInheritanceKind.NotInherited
            || ReferenceEquals(current, snapshot))
        {
            styles[pair.Key] = pair.Value;
        }
    }
}
```

当前逻辑：从根到子节点遍历，子节点样式覆盖父节点。但条件 `ReferenceEquals(current, snapshot)` 意味着即使是 `NotInherited` 的样式，在目标元素自身上也会被应用。这是正确的。

但问题是：**中间节点的 `NotInherited` 样式不会被应用，但也不会被子节点看到**——这是正确的。然而，代码没有处理 `Inherit` 显式声明的情况，也没有处理 CSS `initial`/`unset` 值。

**影响**: 对于显式声明 `inherit` 但属性本身默认不继承的情况，可能无法正确继承。

**建议修复**:
当前实现对于基础场景足够，但建议添加注释说明限制，或在未来版本中添加显式 `inherit` 关键字支持。

---

### 6. `WebView2DomPropertyFiller` 中 JS 注入可能存在 XSS 风险

**文件**: `src/Iwesun.Runtime.Web/Integration/WebView2DomPropertyFiller.cs`

**问题描述**:
虽然 `EscapeJs` 方法处理了基本的转义（反斜杠、单引号、换行），但 XPath 和属性名如果包含恶意内容，可能突破转义：
```csharp
private static string EscapeJs(string value) =>
    value.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "\\r");
```

缺少对 `</script>`、Unicode 转义序列等的处理。虽然 XPath 通常来自可信的 DOM 快照，但在安全边界上应做更严格的处理。

**影响**: 如果 XPath 或属性名来自不可信来源，可能导致 JS 注入。

**建议修复**:
1. 添加对 `\u2028`、`\u2029` 等行分隔符的转义
2. 考虑使用 JSON 序列化来安全传递字符串参数：
```csharp
private static string EscapeJs(string value) =>
    JsonSerializer.Serialize(value).Trim('"');
```

---

## Low 级别问题

### 7. `DomElement.EffectiveMaximumHierarchyLevel` 递归未处理循环引用

**文件**: `src/Iwesun.Runtime.Web/Elements/DomElement.cs:270-278`

**问题描述**:
```csharp
public int? EffectiveMaximumHierarchyLevel
{
    get
    {
        var root = this;
        while (root.Parent is not null)
            root = root.Parent;
        return root.MaximumHierarchyLevel
            ?? root.EmbeddingFrameOwner?.EffectiveMaximumHierarchyLevel;
    }
}
```

如果 iframe 嵌套形成循环（理论上不应该，但代码未防护），会导致栈溢出。由于 `AttachEmbeddingFrameOwner` 有基本检查，风险较低。

**建议修复**: 添加 visited 集合防止循环。

---

### 8. 数值审核器中 `tolerance` 计算可能为 0

**文件**: `src/Iwesun.Runtime.Web/Auditing/NumericPropertyAuditor.cs:55-58`

**问题描述**:
```csharp
var tolerance = Math.Max(
    policy.AbsoluteTolerance,
    Math.Max(Math.Abs(expected), Math.Abs(xaml.Value))
        * policy.RelativeTolerance);
```

如果 `AbsoluteTolerance` 和 `RelativeTolerance` 都为 0，tolerance 为 0，导致浮点精度问题被误判为不匹配。

**影响**: 严格零容差配置下可能出现误报。

**建议修复**: 强制最小容差（如 `1e-9`），或在文档中说明零容差的风险。

---

### 9. `HtmlDomElementTypeCatalog.CreateFactories` 使用反射扫描整个程序集

**文件**: `src/Iwesun.Runtime.Web/Elements/HtmlDomElementTypeCatalog.cs:106-128`

**问题描述**:
静态构造时扫描整个程序集查找所有元素类型，这会：
1. 增加启动时间
2. 如果程序集中有非元素类型但匹配签名，可能导致意外注册

**影响**: 性能影响微小，但可能引入难以发现的注册问题。

**建议修复**: 考虑使用显式注册或源生成器替代反射扫描。

---

## 测试覆盖分析

现有 202 个测试覆盖了以下方面：
- ✅ 元素类型目录和创建
- ✅ DOM 树构建和遍历
- ✅ 属性槽位填充
- ✅ 事件冒泡
- ✅ 布局约束验证
- ✅ 数值审核
- ✅ 继承服务
- ✅ XAML 对象构建

**建议补充测试**:
1. 继承链循环检测测试
2. WebView2 填充器异常路径测试
3. 最大层级限制边界测试
4. 跨 iframe 层级计算测试
5. 并发审核统计累加测试

---

## 架构评价

### 优点
1. **清晰的分层架构**: 元素模型 → 属性槽位 → 继承服务 → 布局约束 → 审核器，依赖方向明确
2. **强类型设计**: 布局使用枚举和结构体而非字符串，避免了 CSS 字符串解析的脆弱性
3. **槽位分离**: 源端和 XAML 端六个槽位严格分离，DOM 坐标只进源运行槽位
4. **证据驱动**: `FillAsync` 机制通过强类型请求获取证据，禁止猜测
5. **零警告策略**: `TreatWarningsAsErrors=true` 保证代码质量
6. **不可变 DTO**: 大量使用 `record` 和 `sealed class`，减少可变状态

### 建议改进
1. 添加 `ILogger` 依赖用于诊断日志，替代当前的静默失败
2. 考虑使用 `Channel<T>` 或更现代的异步模式处理 WebView2 脚本执行
3. 为公共 API 添加更完整的 XML 文档注释
4. 添加性能基准测试，特别是树遍历和审核路径

---

## 结论

Iwesun.Runtime.Web 整体代码质量优秀，架构设计合理，核心逻辑正确。发现的问题均不影响主流程运行，且现有测试全部通过。建议优先修复 High 级别的死锁和循环检测问题，Medium 级别的异常处理问题可在后续迭代中改进。

**审计结论**: 代码可以正常使用，建议按本报告修复后投入生产。

---

## 实机分阶段审计增补（当前有效）

### 审计规则

完整流程拆为相互独立的硬门禁，前一门失败时不得进入后一门：

1. `DomBuild`：只建立强类型元素树、XPath、document scope、父子关系和全局层级。
2. `DomFill`：依据反射特性逐属性、逐槽位调用 Runtime WebView2 API；设计初值、
   CSS/JS 链接、computed/runtime 值和绝对几何必须分槽保存。
3. `CreateXaml`：由最终 HTML/SVG 具体类型选择具体 XAML 对象类型；禁止公共
   决策器根据现场数据猜类型。
4. `FillXamlProperties`：只把已确认的设计属性和运行连接写入已创建对象；绝对
   DOM 几何只作验收证据，禁止作为初始化坐标回填。
5. `AttachXamlChildren`：按具体容器的 `Children/Content/Inlines/Items` 槽位
   串接 XAML 对象树，包括 iframe 嵌套文档根。
6. `DisplayXaml`：直接显示内存对象树，不经过旧快照或旧 XAML 文件。
7. `XamlFill + Audit`：读取真实 WinUI 对象的初始化、链接、运行值和绝对几何，
   再按元素全局层级逐属性双向审核。

统计数字只用于自动门禁。人工审核必须选择典型元素和首个失败属性，核对其类型、
父子槽位、数据来源、转换路径和真实运行目标。

### 已确认并修复

- 113 个 HTML 与 14 个 SVG 最终具体类型均声明自己的 `CreateXaml()`；
  公共 `ResolveTypeDefinedXamlElementMapping` 回退已移除。
- XAML 对象流程已拆为 `CreateElement`、`FillElementProperties`、
  `AttachChild`，不再由创建函数同时填属性和挂子树。
- iframe 查询不再以 `FrameCreated` 时间序号冒充 DOM 顺序。正式查询使用
  document scope + 完整 URI 唯一匹配；缺失或重复均硬失败。
- 五层限制改为全局层级。嵌套文档根层级等于 iframe owner 层级加一，不再在
  每个 iframe 内从 1 重新计数。
- iframe 嵌套文档根已通过 owner 的 `Content` 槽位挂入真实 WinUI 树。
- 单属性 DomFill 实机门禁通过：`Captured=41675`、
  `ConfirmedAbsent=82999`、`SourceUnsupported=0`、
  `Incorrect=0`、`TraversalMetadataMissing=0`。
- 审核不再要求 DOM-only 属性伪造同名 XAML 值。目标为空时为
  `NotRequired`；目标意外出现值仍报 `UnexpectedXamlValue`。

### 当前首个未完成接口

五层实机审核的第 1 层根元素几何已经一致，Space 失败为 0；但 CSS 语义仍有
74 个属性失败。首错为：

```text
document::/html
style.display
DOM runtime = block
XAML runtime = missing
```

同组失败还包括：

- `style.width/height`：DOM 初始化为 `100%`，并保留 CSS
  URL/selector/property 链；XAML 根实际已 Stretch，但审核器没有读取新布局
  管理器的连接。
- `style.boxSizing`：DOM 为 `border-box`，XAML 侧未提供经实际目标验证的语义值。
- `style.visibility/opacity/zIndex/pointerEvents/position`：WinUI 有真实属性或
  映射语义，但新内存 `HtmlRuntimeDesignRuntime` 尚未接入查询引擎。

根因不是采集遗漏。`WinUiXamlElementObjectFactory` 已向
`HtmlRuntimeDesignRuntime.Styles/Layout` 注册目标，而
`WinUiXamlPropertyQueryEngine` 仍主要读取旧的可空
`CssRuntimeContractController`，形成新旧接口断层。

### 当前不得宣称通过的项目

- CSS 初始化表达式到 WinUI 属性/布局输入的活动连接尚未全部可执行。
- `ConservativeContainer` 仍有注册目标但缺少完整的 WinUI block/position
  布局执行器。
- 新样式/布局管理器尚未覆盖全部实际属性读取与链接验证。
- 第 2–5 层仍需在第 1 层完全通过后逐层纠正。
- 全深度元素树、动态状态、事件、动画、资源和最终文件写出尚未进入验收。

因此当前正式结论是：**DomBuild、DomFill、强类型 CreateXaml 和 iframe 子树
连接已通过当前五层典型验证；样式/布局执行与真实 XAML 属性审核仍未完成，项目
不可宣称 1:1 或可投入生产。**

### 连续复验记录

- 修正 DOM-only 属性审核后，第 1 层失败从 149 降为 74；Space 仍为 0。
- 将 `HtmlRuntimeDesignRuntime` 接入真实 XAML 查询器后，第 1 层失败从 74
  降为 62；`display/position/visibility/opacity/zIndex/pointerEvents/
  boxSizing` 已能从实际 WinUI 目标或已物化映射读取。
- 新首错为 `width/height:100%` 初始化语义以及四边
  margin/padding 的活动布局连接。进一步检查发现
  `HtmlRuntimeLayoutManager.LayoutStyleProperties` 只登记了 margin/padding
  简写，遗漏 `marginTop/Right/Bottom/Left` 与
  `paddingTop/Right/Bottom/Left`，导致 DOM 有 CSS 规则链接而布局管理器没有
  对应数据点。该目录遗漏已补齐，下一轮必须继续实机复验。

# DOM 元素属性填充方案规划

## 1. 目标

基于 `Iwesun.Runtime.WebView2` 提供的 API，实现 `Iwesun.Runtime.Web.DomElement` 对象树的属性填充，
将 WebView2 中实时 DOM 元素的属性值正确填入六槽位模型的对应位置。

## 2. 可用 API 分析

### 2.1 WebView2 提供的能力

| API                                                            | 能力                                                         | 适用槽位                  |
| -------------------------------------------------------------- | ------------------------------------------------------------ | ------------------------- |
| `WebRuntimeDomSnapshot.CaptureAsync()`                         | 一次性采集完整 DOM 快照（attributes + primitive properties） | SourceInitialization      |
| `IWebRuntimeScriptSession.EvaluateStringAsync()`               | 执行任意 JS 表达式，返回 JSON                                | SourceRuntime, SourceLink |
| `IWebRuntimeDevToolsSession.CallDevToolsProtocolMethodAsync()` | 调用 CDP 协议方法                                            | 布局、计算样式            |
| `WebRuntimeCSharpDom.QuerySelectorAsync()`                     | 通过 CDP DOM.querySelector 获取节点                          | 元素定位                  |
| `WebRuntimeCSharpDom.GetOuterHtmlAsync()`                      | 获取 outerHTML                                               | 调试/验证                 |

### 2.2 快照 JSON 结构（CaptureAsync 返回）

```json
{
  "schema": "iwesun.webview2.dom-snapshot/1.0",
  "top": {
    "documentElement": {
      "nodeType": 1,
      "localName": "html",
      "path": "/html",
      "attributes": [
        { "name": "lang", "value": "zh-CN" }
      ],
      "properties": {
        "tabIndex": 0,
        "hidden": false,
        "scrollWidth": 1920,
        "clientWidth": 1920,
        "offsetWidth": 1920
      },
      "childNodes": [...]
    }
  }
}
```

**快照已采集的 primitive properties：**
- 表单：`value`, `checked`, `indeterminate`, `selected`, `selectedIndex`, `disabled`, `readOnly`, `required`, `multiple`, `open`
- 状态：`hidden`, `contentEditable`, `isContentEditable`, `tabIndex`, `title`, `lang`, `dir`, `draggable`, `spellcheck`
- 布局：`scrollLeft`, `scrollTop`, `scrollWidth`, `scrollHeight`, `clientWidth`, `clientHeight`, `offsetWidth`, `offsetHeight`
- 媒体：`naturalWidth`, `naturalHeight`, `currentSrc`, `currentTime`, `duration`, `paused`

## 3. 槽位填充策略

### 3.1 源端（HTML/DOM）三槽位填充规则

| 槽位                     | 数据来源                     | 填充时机                   | 示例                                      |
| ------------------------ | ---------------------------- | -------------------------- | ----------------------------------------- |
| **SourceInitialization** | HTML attributes + CSS 声明值 | 快照采集阶段               | `style="width:100%"` → `"100%"`           |
| **SourceLink**           | 强类型语义分析               | 转换阶段                   | `width:100%` → `ContainerLayoutBinding`   |
| **SourceRuntime**        | DOM 运行时计算值             | 快照采集阶段（properties） | `getBoundingClientRect().width` → `800px` |

### 3.2 属性分类与填充方式

#### A. HTML 全局属性（36 个）

| 属性                                                              | SourceInitialization     | SourceRuntime                 | 备注       |
| ----------------------------------------------------------------- | ------------------------ | ----------------------------- | ---------- |
| `id`, `class`, `title`, `lang`, `dir`                             | attributes[].value       | properties.title/lang/dir     | 直接值     |
| `hidden`                                                          | attributes 存在 → "true" | properties.hidden (boolean)   | 布尔属性   |
| `tabindex`                                                        | attributes[].value       | properties.tabIndex (number)  | 数值       |
| `style`                                                           | attributes[].value       | getComputedStyle()            | CSS 声明块 |
| `contenteditable`, `draggable`, `spellcheck`                      | attributes[].value       | properties.contentEditable 等 | 枚举       |
| `role`, `slot`, `part`, `exportparts`                             | attributes[].value       | -                             | 直接值     |
| `popover`, `inert`, `autofocus`                                   | attributes 存在 → "true" | -                             | 布尔属性   |
| `inputmode`, `enterkeyhint`, `autocapitalize`                     | attributes[].value       | -                             | 枚举       |
| `nonce`, `itemid`, `itemprop`, `itemref`, `itemscope`, `itemtype` | attributes[].value       | -                             | 直接值     |
| `data-*`, `aria-*`                                                | attributes[].value       | -                             | 扩展属性   |

#### B. 元素专属属性

| 元素        | 属性                                       | SourceInitialization | SourceRuntime                             |
| ----------- | ------------------------------------------ | -------------------- | ----------------------------------------- |
| `<a>`       | `href`, `target`, `download`, `rel`        | attributes[].value   | -                                         |
| `<input>`   | `type`, `value`, `placeholder`, `disabled` | attributes[].value   | properties.value, properties.disabled     |
| `<button>`  | `type`, `disabled`, `formaction`           | attributes[].value   | properties.disabled                       |
| `<img>`     | `src`, `alt`, `width`, `height`            | attributes[].value   | properties.naturalWidth/Height            |
| `<video>`   | `src`, `poster`, `autoplay`, `controls`    | attributes[].value   | properties.paused, properties.currentTime |
| `<td>/<th>` | `colspan`, `rowspan`                       | attributes[].value   | -                                         |

#### C. 布局属性（Space 类别）

这些属性需要额外的 JS 查询获取计算值：

| 属性                        | SourceInitialization  | SourceLink                      | SourceRuntime                          |
| --------------------------- | --------------------- | ------------------------------- | -------------------------------------- |
| `rect.x`, `rect.y`          | -                     | -                               | `getBoundingClientRect().x/y`          |
| `rect.width`, `rect.height` | style.width/height    | 百分比→ContainerLayoutBinding   | `getBoundingClientRect().width/height` |
| `style.margin`              | style.margin          | -                               | `getComputedStyle().margin`            |
| `style.padding`             | style.padding         | -                               | `getComputedStyle().padding`           |
| `style.display`             | style.display         | flex/grid→LayoutBinding         | `getComputedStyle().display`           |
| `style.position`            | style.position        | absolute/fixed→AnchorConstraint | `getComputedStyle().position`          |
| `style.backgroundColor`     | style.backgroundColor | -                               | `getComputedStyle().backgroundColor`   |
| `style.color`               | style.color           | -                               | `getComputedStyle().color`             |
| `style.fontSize`            | style.fontSize        | -                               | `getComputedStyle().fontSize`          |
| `style.opacity`             | style.opacity         | -                               | `getComputedStyle().opacity`           |
| `style.transform`           | style.transform       | -                               | `getComputedStyle().transform`         |

#### D. 事件属性

| 槽位                 | 来源                                      |
| -------------------- | ----------------------------------------- |
| SourceInitialization | `onclick` 等 attribute 值（字符串）       |
| SourceLink           | 事件类型映射（Click→XamlEventKind.Click） |
| SourceRuntime        | `addEventListener` 注册情况（如有）       |

## 4. 填充流程设计

### 4.1 阶段一：快照采集 → SourceInitialization + SourceRuntime

```
WebRuntimeDomSnapshot.CaptureAsync(session)
    ↓
解析快照 JSON
    ↓
递归遍历节点树，按 XPath 匹配 DomElement 对象
    ↓
对每个元素：
  1. 从 attributes[] 填充 SourceInitialization 槽位
  2. 从 properties{} 填充 SourceRuntime 槽位（数值/布尔属性）
  3. 从 childNodes 建立/验证父子关系
```

### 4.2 阶段二：语义分析 → SourceLink

```
对每个元素的属性值进行语义分析：
  - width: "100%" → 创建 ContainerLayoutBinding
  - display: "flex" → 创建 FlexLayoutBinding
  - position: "absolute" + right: "0" → 创建 AnchorConstraint
  - color: "var(--primary)" → 创建 CssExpression 链接
    ↓
填入 SourceLink 槽位
```

### 4.3 阶段三：计算样式补充 → SourceRuntime（扩展）

快照已包含 offsetWidth/Height 等基础布局属性，
对于颜色、字体、边距等计算样式，需要额外 JS 查询：

```csharp
// 批量查询计算样式
var computedStyles = await session.EvaluateStringAsync(@"
    JSON.stringify(
        Array.from(document.querySelectorAll('[data-audit]')).map(el => ({
            xpath: getXPath(el),
            style: {
                color: getComputedStyle(el).color,
                backgroundColor: getComputedStyle(el).backgroundColor,
                fontSize: getComputedStyle(el).fontSize,
                fontWeight: getComputedStyle(el).fontWeight,
                margin: getComputedStyle(el).margin,
                padding: getComputedStyle(el).padding,
                opacity: getComputedStyle(el).opacity,
                transform: getComputedStyle(el).transform,
                zIndex: getComputedStyle(el).zIndex
            }
        }))
    )
");
```

### 4.4 阶段四：XAML 端填充（XamlInitialization/XamlLink/XamlRuntime）

XAML 端三槽位在 XAML 控件生成并挂载后填充：

| 槽位               | 来源                                  | 时机        |
| ------------------ | ------------------------------------- | ----------- |
| XamlInitialization | ToXaml() 生成的属性值                 | XAML 生成时 |
| XamlLink           | Binding/StaticResource 描述           | XAML 生成时 |
| XamlRuntime        | WinUI 运行时 ActualWidth/ActualHeight | XAML 加载后 |

## 5. 类设计

### 5.1 填充器接口

```csharp
public interface IDomElementFiller
{
    /// <summary>
    /// 从 WebView2 快照填充 DomElement 树的源端槽位
    /// </summary>
    Task FillFromSnapshotAsync(
        DomElement root,
        string snapshotJson,
        CancellationToken ct = default);

    /// <summary>
    /// 通过 JS 查询填充运行时计算样式和布局
    /// </summary>
    Task FillRuntimeStylesAsync(
        DomElement root,
        IWebRuntimeScriptSession session,
        CancellationToken ct = default);
}
```

### 5.2 快照解析器

```csharp
public static class DomSnapshotParser
{
    /// <summary>
    /// 解析快照 JSON 并填充到 DomElement 树
    /// </summary>
    public static void ParseSnapshot(
        DomElement root,
        JsonDocument snapshot);

    /// <summary>
    /// 按 XPath 在快照中查找节点
    /// </summary>
    public static JsonElement? FindNodeByXPath(
        JsonElement root,
        string xpath);
}
```

### 5.3 属性映射器

```csharp
public static class DomPropertyMapper
{
    /// <summary>
    /// 将 HTML attribute 名称映射到 DomElement  CLR 属性
    /// </summary>
    public static bool TryGetPropertyInfo(
        DomElement element,
        string attributeName,
        out PropertyInfo property);

    /// <summary>
    /// 将快照 property 名称映射到 DomElement  CLR 属性
    /// </summary>
    public static bool TryGetRuntimePropertyInfo(
        DomElement element,
        string propertyName,
        out PropertyInfo property);
}
```

## 6. 关键技术点

### 6.1 XPath 匹配

快照中每个节点有 `path` 字段（如 `/html/body/div[1]/button`），
与 DomElement.XPath 格式一致，直接字符串匹配即可。

### 6.2 类型转换

快照中的值是 JSON 原始类型（string/number/boolean），
需要转换为 DomElementStringProperty 的字符串值：
- boolean → "true"/"false"
- number → 不变（如 tabIndex: 0 → "0"）
- null → 不设置

### 6.3 扩展属性处理

`data-*`、`aria-*` 等不在 CLR 属性中定义的属性，
通过 `AddExtensionAttribute(name)` 添加到 ExtensionAttributes 集合。

### 6.4 布局链接识别

需要 CSS 解析能力来识别：
- `width: 100%` → ContainerLayoutBinding（相对父容器宽度）
- `width: 50vw` → ContainerLayoutBinding（相对视口宽度）
- `display: flex` → FlexLayoutBinding
- `position: absolute` + `left/right/top/bottom` → AnchorConstraint

### 6.5 批量优化

避免逐元素 JS 调用，使用批量查询：
1. 快照一次性获取所有 attributes 和 primitive properties
2. 计算样式使用单次 JS 查询批量获取
3. 布局矩形使用单次 JS 查询批量获取

## 7. 实现优先级

| 优先级 | 内容                                               | 依赖                     |
| ------ | -------------------------------------------------- | ------------------------ |
| P0     | 快照解析 → SourceInitialization 填充（attributes） | WebRuntimeDomSnapshot    |
| P0     | 快照 properties → SourceRuntime 填充               | WebRuntimeDomSnapshot    |
| P1     | 布尔/数值/枚举属性类型转换                         | -                        |
| P1     | ExtensionAttributes（data-*, aria-*）填充          | -                        |
| P2     | SourceLink 语义分析（百分比、flex、grid）          | CSS 解析器               |
| P2     | 计算样式批量查询填充                               | IWebRuntimeScriptSession |
| P3     | 事件属性填充                                       | -                        |
| P3     | XAML 端槽位填充                                    | XAML 生成完成后          |

## 8. 测试策略

1. **快照解析测试**：使用已知快照 JSON，验证属性正确填充
2. **XPath 匹配测试**：验证嵌套元素正确关联
3. **类型转换测试**：boolean/number/string 转换正确
4. **扩展属性测试**：data-*, aria-* 正确进入 ExtensionAttributes
5. **布局链接测试**：width:100% 正确生成 ContainerLayoutBinding
6. **端到端测试**：使用真实 WebView2 采集并填充，验证审核通过

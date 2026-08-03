# Iwesun.Runtime.Web 技术说明

`Iwesun.Runtime.Web` 是与站点和浏览器内核无关的 HTML/SVG 公共模型库。它不采集网页，
不执行任意 JavaScript；它提供转换器和审核器共同依赖的稳定语义基础，并把已验证槽位投影为
框架无关的 XAML 节点合同。站点转换器仍负责具体 WinUI 控件模板和运行绑定。

## 能力范围

- HTML/SVG 元素类型、分类、内容模型、闭合方式和 XAML 支持等级。
- 元素实例树：父元素、子元素、左兄弟、右兄弟和绝对 XPath。
- 元素事件：事件目录、Click 注册/注销、异步派发和父链冒泡。
- 分辨率、容器尺寸和可继承样式的公共发布/链接服务。
- Space、Style、Effect、Action、DataOrganization 五类泛型槽位属性。
- HTML 初始化、链接描述、DOM 运行值以及 XAML 初始化、链接描述、WinUI 运行值。
- 强类型容器布局来源、约束、参考盒、轴和百分比基准。
- 属性自审核、元素遍历审核、数值统计累计与文本报告。
- 递归 `FillAsync`：当前元素先填充、随后按 DOM 顺序递归子元素；每个槽位通过强类型查询身份发布完整证据请求。
- 递归 `ToXaml`：每个具体元素投影全部属性；未翻译和仅运行时属性也进入结果，但不得伪装为
  XAML 初始化值。

## 填充、转换与审核顺序

1. 消费方只根据 DOM 身份建立树和 `documentScope + XPath`，此时不得填入推测样式。
2. 根元素调用 `FillAsync`。每个具体 HTML/SVG 类发出 `ElementFillRequest`，标准委托按
   属性名、领域和槽位查询网页证据。
3. 委托逐请求返回 `Captured`、`ConfirmedAbsent` 或 `SourceUnsupported`。
   必需证据为 `SourceUnsupported` 时立即失败；禁止把请求 ID 全部标为成功。
4. `ToXaml` 遍历同一棵树。属性投影保留源初始化、源链接、源运行、XAML 初始化、
   XAML 链接和 XAML 运行六个槽位。DOM 绝对坐标只允许进入源运行槽位。
5. WinUI 隐藏运行后，由消费方按相同身份回填 XAML 运行槽位，再执行子元素优先的递归审核。

`SourceContainerLayout` 保存强类型容器布局绑定。普通流、Flex、Grid、绝对定位、固定定位、
Sticky、兄弟顺序、对齐、尺寸和百分比基准不能只保存为无结构字符串。无法翻译的属性仍保留
源值并进入人工复核；来源本身无法采集则属于证据失败，不是人工复核。

## 项目入口

| 内容         | 路径                                                             |
| ------------ | ---------------------------------------------------------------- |
| 产品项目     | `src/Iwesun.Runtime.Web/Iwesun.Runtime.Web.csproj`               |
| 测试项目     | `tests/Iwesun.Runtime.Web.Tests/Iwesun.Runtime.Web.Tests.csproj` |
| 领域解决方案 | `Iwesun.Runtime.Web.slnx`                                        |

## 文档

- **[元素属性完整实现指南](ELEMENT-PROPERTY-COMPLETE-GUIDE.md)** — 从元素定义到自动填充的全链路文档
- [架构与依赖边界](ARCHITECTURE.md)
- [HTML/SVG 元素树与事件](HTML-SVG-ELEMENT-MODEL.md)
- [槽位属性与审核](SLOTTED-PROPERTIES-AND-AUDIT.md)
- [容器布局合同](CONTAINER-LAYOUT.md)
- [HTML/SVG 元素基础类设计](HTML_SVG_ELEMENT_BASE_CLASS.md)
- [DOM 属性填充方案规划](DOM_PROPERTY_FILL_PLAN.md)

## 使用边界

消费项目负责把自己的 DOM 快照 DTO 适配为公共元素和槽位属性，再负责目标 UI 框架转换。
站点 XPath 终止端点、豆包控件替换、WebView2 采集和 WinUI 类型选择不进入本模块。

构建与测试：

```powershell
dotnet test modules/Web/Iwesun.Runtime.Web.slnx -c Debug
dotnet test modules/Web/Iwesun.Runtime.Web.slnx -c Release
```

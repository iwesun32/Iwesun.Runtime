# Agent 指南：Iwesun.Runtime.Web

与站点和浏览器内核无关的 HTML/SVG 公共模型库，提供转换器和审核器共同依赖的稳定语义基础。

## 代理工作优先级

- 保持该库为纯语义模型：不要引入 WinUI、DoubaoUIClone 或站点特定依赖；新增能力应落在公共元素、槽位属性或审核逻辑中。
- 优先扩展现有元素类、布局合同和审核流程，而不是并行建立一套新模型。
- 涉及属性填充、布局或审核时，先阅读 [docs/ELEMENT-PROPERTY-COMPLETE-GUIDE.md](docs/ELEMENT-PROPERTY-COMPLETE-GUIDE.md)、[docs/CONTAINER-LAYOUT.md](docs/CONTAINER-LAYOUT.md) 和 [docs/SLOTTED-PROPERTIES-AND-AUDIT.md](docs/SLOTTED-PROPERTIES-AND-AUDIT.md)。
- 保留可审计性：不要跳过属性、伪造审计结果，或把运行时坐标误写入 XAML 初始化槽位。
- 变更后优先运行解决方案级测试，并保持零警告、零错误。

## 快速参考

| 项       | 值                                             |
| -------- | ---------------------------------------------- |
| 目标框架 | `net10.0`（标准类库，无 WinUI 依赖）           |
| 测试框架 | xUnit                                          |
| JSON 库  | `System.Text.Json`（**禁止 Newtonsoft.Json**） |
| 解决方案 | `Iwesun.Runtime.Web.slnx`                      |

## 必读文档

- [docs/ELEMENT-PROPERTY-COMPLETE-GUIDE.md](docs/ELEMENT-PROPERTY-COMPLETE-GUIDE.md)：从元素定义到自动填充的全链路文档
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)：分层、依赖边界和稳定原则
- [docs/HTML-SVG-ELEMENT-MODEL.md](docs/HTML-SVG-ELEMENT-MODEL.md)：元素树与事件模型
- [docs/SLOTTED-PROPERTIES-AND-AUDIT.md](docs/SLOTTED-PROPERTIES-AND-AUDIT.md)：五类槽位属性与审核机制
- [docs/CONTAINER-LAYOUT.md](docs/CONTAINER-LAYOUT.md)：强类型容器布局合同
- [docs/HTML_SVG_ELEMENT_BASE_CLASS.md](docs/HTML_SVG_ELEMENT_BASE_CLASS.md)：基础类设计
- [docs/DOM_PROPERTY_FILL_PLAN.md](docs/DOM_PROPERTY_FILL_PLAN.md)：DOM 属性填充方案

## 能力范围

- HTML/SVG 元素类型、分类、内容模型、闭合方式和 XAML 支持等级
- 元素实例树：父元素、子元素、左兄弟、右兄弟和绝对 XPath
- 元素事件：事件目录、Click 注册/注销、异步派发和父链冒泡
- 分辨率、容器尺寸和可继承样式的公共发布/链接服务
- Space、Style、Effect、Action、DataOrganization 五类泛型槽位属性
- HTML 初始化、链接描述、DOM 运行值以及 XAML 初始化、链接描述、WinUI 运行值
- 强类型容器布局来源、约束、参考盒、轴和百分比基准
- 属性自审核、元素遍历审核、数值统计累计与文本报告
- 递归 `FillAsync`：当前元素先填充、随后按 DOM 顺序递归子元素
- 递归 `ToXaml`：每个具体元素投影全部属性

## 项目结构

| 路径                                  | 职责                                    |
| ------------------------------------- | --------------------------------------- |
| `src/Iwesun.Runtime.Web/Elements/`    | HTML/SVG 元素类型定义、目录、属性和事件 |
| `src/Iwesun.Runtime.Web/Inheritance/` | 分辨率、尺寸、样式继承服务              |
| `src/Iwesun.Runtime.Web/Layout/`      | 强类型容器布局约束和合同                |
| `src/Iwesun.Runtime.Web/Auditing/`    | 属性审核、元素遍历审核和统计报告        |
| `src/Iwesun.Runtime.Web/Integration/` | WebView2 填充器和外部集成               |
| `tests/Iwesun.Runtime.Web.Tests/`     | xUnit 单元测试（150+ 用例）             |

## 构建和测试

```powershell
dotnet test Iwesun.Runtime.Web.slnx -c Debug
dotnet test Iwesun.Runtime.Web.slnx -c Release
```

Debug/Release 必须 0 警告、0 错误。

## 依赖规则

`Iwesun.Runtime.Web`：
- 只依赖 .NET 基础库和 WebView2 核心 DLL（仅类型引用，不运行时初始化）
- **不依赖** DoubaoUIClone、DOM 快照合同、WinUI、WebView2 运行时或站点策略
- 不读取外部文件，不进行网络访问，不输出控制台诊断

消费项目：
- 可以继承公共元素基类，增加 DOM→XAML 转换能力
- 可以把站点证据转换为公共槽位属性
- 不得复制公共源码，不得重新定义同名审核或布局合同

## 代码约定

### 全局编译设置

- `Nullable=enable`、`ImplicitUsings=enable`，所有元素类默认 `sealed`。
- 字段 `_camelCase`，属性 `PascalCase`。

### 元素、槽位、审核、填充与布局

详细规范见 [.github/instructions/web-project-conventions.instructions.md](.github/instructions/web-project-conventions.instructions.md)。

### 测试约定

- xUnit 测试，方法名：`MethodName_WhenCondition_ExpectsResult`。
- 临时文件/目录使用 `IDisposable` 模式，不硬编码绝对路径。
- 测试覆盖：元素目录、属性反射、槽位填充、事件冒泡、布局约束、审核统计。

## 常见陷阱

- **依赖越界**：不要引入 WinUI、DoubaoUIClone 或站点特定依赖，本模块是纯基础库
- **可变单例**：元素目录每次返回新实例，不要缓存共享元素对象
- **推测样式**：建树阶段不得填入推测样式，所有值必须来自 FillAsync 证据请求
- **槽位混淆**：不要把 DOM 运行时坐标写入 XAML 初始化槽位
- **字符串布局**：不要把 Flex/Grid 布局保存为原始字符串，必须使用强类型 Layout 合同
- **审核造假**：不得通过跳过属性或缩小范围来制造审核通过结果

## 可用的 Slash 命令提示

在 `.github/prompts/` 目录下提供了以下预定义提示，可通过 `/` 命令调用：

| 命令 | 用途 |
|------|------|
| `/build-test` | 构建和测试解决方案，包含零警告验证 |
| `/new-html-element` | 添加新的 HTML/SVG 元素类型，包含完整的槽位和审核规范 |

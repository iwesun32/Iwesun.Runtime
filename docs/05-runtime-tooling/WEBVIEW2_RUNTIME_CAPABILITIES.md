# WebView2 Runtime Capability Plan

本文整理 AIGateway 对 WebView2 的正式运行时能力边界。目标不是暴露 WebView2 的全部底层 COM API，而是把 Web 后端调试、自动化、登录、人机协助、协议探索所需的能力统一到一个可调用、可返回、可组合的控制面。

本文是能力规划和命名规范，不重复列出完整命令协议。实际接口参考见 [WEB_RUNTIME_CONTROL.md](WEB_RUNTIME_CONTROL.md)，AI 操作流程见 [WEBVIEW2-AI-CONTROL-SKILL.md](../archive/WEBVIEW2-AI-CONTROL-SKILL.md)。

## 1. 总原则

WebView2 运行时控制面属于 Service 正式功能。Tester、外部脚本和 AI 调试器只是调用方，不拥有业务逻辑。

控制面必须满足四条规则：

| 规则 | 说明 |
| ---- | ---- |
| 运行时直连 | 控制当前服务内真实 WebView2 会话，不另起一套浏览器 |
| 返回值优先 | 命令结果直接通过管道返回，日志只是辅助 |
| 低侵入观察 | 事件、网络、JS 监视先缓冲，调用方需要时再读取 |
| 分类稳定 | 新能力先归类，再命名动作，避免零散堆积 |

## 2. 能力分类

### 2.1 会话生命周期

职责：启动、显示、隐藏、等待当前 Web 后端浏览器会话。

现有动作：

| action | 状态 |
| ---- | ---- |
| `start` | 已实现 |
| `show` | 已实现 |
| `hide` | 已实现 |
| `wait` | 已实现 |

设计约束：

- `show` 只用于人工协助登录、人机验证、页面异常诊断。
- 正常服务不应依赖启动参数粗暴决定所有浏览器是否可见。
- 显示/隐藏是运行时状态动作，不是调试开关。

### 2.1.1 登录状态机

职责：从运行时管道启动、显示并启动、停止、读取 Web 后端正式登录状态机。

现有动作：

| action | 状态 |
| ---- | ---- |
| `loginStart` | 已实现 |
| `showLogin` | 已实现 |
| `loginStop` | 已实现 |
| `loginState` | 已实现 |

设计约束：

- `show` 只显示窗口，不发起登录。
- `showLogin` 同时显示窗口并启动后端自己的 `WebLoginStateMachine`。
- 登录逻辑必须留在正式 Web 后端和状态机内，Tester 只负责发命令。

### 2.2 导航与页面状态

职责：读取页面 URL、导航、页面快照、当前可见控件。

现有动作：

| action | 状态 |
| ---- | ---- |
| `navigate` | 已实现 |
| `url` | 已实现 |
| `snapshot` | 已实现 |
| `discover` | 已实现 |

设计约束：

- `discover` 是 AI 自动探索页面的首选入口。
- `snapshot` 偏人工概览，`discover` 偏结构化可操作目标。
- 导航动作必须作用于已有后端会话，不创建独立测试浏览器。

### 2.3 DOM 与 XPath

职责：按 DOM/XPath 读取元素、展开节点树、定位按钮和文档块。

现有动作：

| action | 状态 |
| ---- | ---- |
| `resolveXPath` | 已实现 |
| `readXPath` | 已实现，`resolveXPath` 别名 |
| `resolveXPathTree` | 已实现 |
| `eval` | 已实现 |

设计约束：

- `resolveXPath` 只读取，不点击。
- `clickXPath` 只点击，不承担读取职责。
- `resolveXPathTree` 必须限制深度和子节点数量，防止一次返回整页 DOM。
- 文档、菜单、工具面板的结构探索优先使用 `resolveXPathTree`。

### 2.4 输入与人机行为

职责：页面级虚拟鼠标、XPath 点击、坐标点击、后续键盘输入。

现有动作：

| action | 状态 |
| ---- | ---- |
| `clickXPath` | 已实现 |
| `mouseMove` | 已实现 |
| `mouseClick` | 已实现 |
| `mouseCheck` | 已实现 |

待整理能力：

| 能力 | 建议动作 | 说明 |
| ---- | ---- | ---- |
| 文本键入 | `typeText` | 调用统一虚拟键盘，支持首字慢、后续批量输入 |
| 按键 | `keyPress` | Enter、Delete、Backspace、Ctrl/Shift 组合 |
| 空闲行为 | `ambientMouseStart` / `ambientMouseStop` | 页面级持续漂移，不绑定某个控件 |

设计约束：

- 鼠标必须是页面级事件，不应移动系统鼠标。
- 人机检测相关动作必须进入状态机和管理层状态，不只是隐藏日志。
- 发送路径只保留一种主策略；DOM 发送和 HTTP 发送应作为不同实现类或重载，不混用。

### 2.5 Cookie 与会话身份

职责：读取、注入、清除 WebView2 Cookie，辅助自动登录。

现有动作：

| action | 状态 |
| ---- | ---- |
| `cookieGet` | 已实现 |
| `cookieSet` | 已实现 |
| `cookieClear` | 已实现 |

设计约束：

- Cookie 动作属于运行时控制面，可用于现场修复和协议探索。
- 持久化 Cookie 仍由 `ServiceOrchestrator` / `SecretStore` 管理。
- 运行时注入 Cookie 后，状态机仍需重新检测登录证据。

### 2.6 网页脚本监管与接管

职责：监管网页原生 JavaScript 的接口行为，记录它调用了什么接口、传递了什么数据、触发了什么页面变化；在摸清协议后，逐步屏蔽原始实现并替换成 AIGateway 可控实现。

现有动作：

| action | 状态 |
| ---- | ---- |
| `monitorStart` | 已实现 |
| `events` | 已实现 |

监管对象：

| 对象 | 当前覆盖 |
| ---- | ---- |
| `fetch` / `XMLHttpRequest` | 已监视 request / response，输出 `AIG_PUSH` / `AIG_PROTO` |
| `EventSource` / `WebSocket` | 已监视消息和错误 |
| `history` / `location` | 已监视页面内部导航 |
| DOM click | 已监视普通点击和文档点击 |
| Clipboard / Media | 已监视复制、媒体播放、麦克风调用 |
| Script inventory | 已记录页面 script 列表 |
| MutationObserver | ChatGPT 路径已有专用观察器，尚未统一为通用动作 |

现有事件类型：

| kind | 来源 |
| ---- | ---- |
| `push` | 页面 push / JS 上报 |
| `network` | HTTP 响应拦截 |
| `script` | JS 诊断输出 |
| `message` | 其他 WebMessage |

设计约束：

- 监视器可以动态挂载。
- 挂载后默认进入服务内存缓冲，不主动推给调用方。
- 没有读取命令时页面照常运行，事件通道保持直通。
- 监管阶段只观察和记录，不改变网页行为。
- 屏蔽阶段必须按接口、URL、调用栈、后端类型精确控制，禁止全局粗暴断开。
- 替换阶段必须让替代实现返回与网页原生调用兼容的数据格式，确保 DOM 状态机不乱。
- 后续应把 `AIG_PROTO` 这类结构化协议事件解析成更细 `kind`，便于 HTTP 发送重放和 JS 替换。

三阶段路线：

| 阶段 | 目标 | 建议动作 |
| ---- | ---- | ---- |
| 监管 | 记录网页 JS 对 fetch/XHR/SSE/WS/history/location/DOM 的调用行为 | `monitorStart`, `events`, 后续 `scriptAuditStart` |
| 屏蔽 | 对指定接口或 URL 进行选择性阻断、短路、模拟失败或跳过副作用 | 后续 `scriptBlockRuleAdd`, `scriptBlockRuleClear` |
| 替换 | 用 AIGateway 实现接管指定接口，直接返回兼容响应或执行直接 HTTP 发送 | 后续 `scriptReplaceRuleAdd`, `scriptReplaceRuleClear` |

### 2.7 可视化与证据

职责：截图、元素矩形、页面可见状态，用于人工协助和视觉排障。

现有动作：

| action | 状态 |
| ---- | ---- |
| `screenshot` | 已实现 |
| `resolveXPath.rect` | 已实现 |
| `discover.targets[].x/y/w/h` | 已实现 |

待整理能力：

| 能力 | 建议动作 | 说明 |
| ---- | ---- | ---- |
| 元素截图 | `screenshotXPath` | 先解析 XPath 矩形，再截图该区域 |
| 高亮目标 | `highlightXPath` | 临时给目标元素加视觉标记，辅助人工确认 |

### 2.8 异步推拉

职责：服务端状态推、UI/Tester 主动拉、事件缓冲读取。

已存在通道：

| 通道 | 说明 |
| ---- | ---- |
| `AIGateway.UI` | UI 状态推拉，服务状态、头像、登录态 |
| `Iwesun.Runtime.WebView2` | WebView2 运行时控制和读取公共契约 |
| `events` | WebRuntime 内存事件拉取 |

设计约束：

- UI 状态和 WebRuntime 调试事件不能混在同一条管道。
- 服务状态改变要立即推给 UI；UI 启动或超时未收到推送时主动拉。
- WebRuntime 事件按命令读取，不应污染 UI 状态流。

## 3. 动作命名规范

| 类别 | 命名 |
| ---- | ---- |
| 生命周期 | `start`, `show`, `hide`, `wait` |
| 登录状态机 | `loginStart`, `showLogin`, `loginStop`, `loginState` |
| 导航 | `navigate`, `url`, `snapshot`, `discover` |
| DOM | `resolveXPath`, `readXPath`, `resolveXPathTree`, `eval` |
| 输入 | `clickXPath`, `mouseMove`, `mouseClick`, `mouseCheck` |
| Cookie | `cookieGet`, `cookieSet`, `cookieClear` |
| 网页脚本监管与接管 | `monitorStart`, `events` |
| 证据 | `screenshot` |
| 元信息 | `capabilities` |

代码内部会把 action 做大小写、横线、下划线归一化；文档中统一使用上表写法。

## 4. 当前缺口

| 缺口 | 优先级 | 说明 |
| ---- | ---- | ---- |
| 运行时能力自描述 | 高 | 程序应返回当前支持的 action 分类，防止文档和代码分叉 |
| 虚拟键盘运行时动作 | 高 | 当前发送路径有虚拟键盘能力，但 WebRuntime 控制面还未标准化暴露 |
| 元素截图和高亮 | 中 | 有助于人工确认 XPath |
| 结构化网络协议事件 | 高 | 需要把 request/response 解析成可复用 HTTP 发送格式 |
| 动态 monitor 配置 | 中 | 需要支持追加 filter、查看已挂 filter、清空事件缓冲 |
| JS 接口屏蔽/替换规则 | 高 | 在监管清楚后，支持按 URL/接口/后端添加阻断或替换规则 |

## 5. 实施顺序

1. 先补 `capabilities` 动作，让程序可返回当前 WebRuntime 能力分类。
2. 把 `WEB_RUNTIME_CONTROL.md` 按本文分类重排，保留命令示例。
3. 后续再补虚拟键盘、元素截图、高亮、结构化协议事件和 JS 接口屏蔽/替换。
4. 每次新增能力必须同时更新本文和 `capabilities` 返回。

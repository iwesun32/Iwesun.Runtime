# WebView2 运行时控制接口

本文档记录 AIGateway 对运行中 WebView2 会话的标准控制面。它不是 `--trace` 调试进程，不会启动另一套浏览器；调用方通过正在运行的服务管道 `AIGateway.WebRuntime` 控制当前服务内的真实 WebView2 会话。

`AIGateway.WebRuntime` 与桌面 UI 管道 `AIGateway.UI` 是两条独立管道。UI 管道负责状态、配置、头像、登录信息推拉；WebRuntime 管道只负责页面运行时控制和读取，避免 UI 客户端收到页面调试命令或运行时事件。

本文是接口参考，负责请求/动作/返回模型。能力规划见 [WEBVIEW2_RUNTIME_CAPABILITIES.md](WEBVIEW2_RUNTIME_CAPABILITIES.md)，AI 操作流程见 [WEBVIEW2-AI-CONTROL-SKILL.md](../archive/WEBVIEW2-AI-CONTROL-SKILL.md)。

## 管道命令

命令：`ManagementPipeCommand.WebRuntimeControl`

管道名：`AIGateway.WebRuntime`

命令行客户端：

```bash
dotnet run --project tests/AIGateway.Tester -- web-runtime doubao-web snapshot
dotnet run --project tests/AIGateway.Tester -- web-runtime doubao-web eval "{\"script\":\"location.href\"}"
dotnet run --project tests/AIGateway.Tester -- web-runtime "{\"backendId\":\"doubao-web\",\"action\":\"events\",\"count\":20,\"clear\":false}"
```

这个客户端不会写记录文件，返回值直接输出到 stdout。Codex/调试脚本运行该命令后，可以直接看到 WebView2 运行时返回的 JSON 内容。

请求体：`WebRuntimeControlRequest`

```json
{
  "backendId": "doubao-web",
  "action": "snapshot",
  "xpath": "/html/body/...",
  "script": "location.href",
  "x": 320,
  "y": 240,
  "width": 1280,
  "height": 720,
  "durationMs": 500,
  "count": 50,
  "clear": false,
  "args": {}
}
```

`backendId` 支持 `doubao-web`、`deepseek-web`、`openai-web`，也兼容短名 `doubao`、`deepseek`、`openai`、`chatgpt`。

## 统一 JSON 命令规范（CLI / 主监控代理 / WebRuntime）

统一入口采用 RuntimeDiagnostics V2 frame：

```json
{
  "header": {
    "schema": "rtdiag/2.0",
    "frameType": "request",
    "category": "instruction",
    "operation": "invoke"
  },
  "command": {
    "domain": "proxy",
    "target": "diagnostics.proxy",
    "action": "invoke",
    "args": {
      "module": "web.runtime",
      "pipe": "web.runtime",
      "backendId": "doubao-web",
      "proxyAction": "snapshot"
    }
  }
}
```

代理层会校验：

1. `module` 与分支管道登记项匹配（防止跨模块误调用）。
2. `pipe` 已在主监控中登记且为 active。
3. 仅把非保留参数透传给目标 WebRuntime。

模块白名单来自 `diagnostic-switchboard.json` 配置层：

```json
{
  "proxyModuleWhitelistEnabled": true,
  "proxyAllowedModules": ["web.runtime"]
}
```

推荐 CLI 标准命令：

```powershell
iwrt web.runtime.pipe.acquire web.runtime
iwrt web.runtime.capabilities web.runtime web.runtime doubao-web
iwrt web.runtime.invoke web.runtime web.runtime doubao-web navigate url=https://example.com
iwrt web.runtime.events web.runtime web.runtime doubao-web 20 false
```

## 动作列表

完整分类见 [WEBVIEW2_RUNTIME_CAPABILITIES.md](WEBVIEW2_RUNTIME_CAPABILITIES.md)。程序本身也支持 `capabilities` 动作返回当前能力表。

### 元信息

| action | 作用 |
| --- | --- |
| `capabilities` | 返回当前 WebRuntime 支持的能力分类、动作列表和计划项 |

### 生命周期

| action | 作用 |
| --- | --- |
| `start` | 启动对应后端的 WebView2 会话，复用服务运行时实例 |
| `show` | 将该后端浏览器窗口显示出来，供人工处理人机验证 |
| `hide` | 将该后端浏览器收回静默模式 |
| `wait` | 等待 `durationMs`，用于外部脚本串行动作 |

### 登录状态机

| action | 作用 |
| --- | --- |
| `loginStart` | 启动对应 Web 后端的正式登录状态机 |
| `showLogin` | 显示 WebView2 窗口并启动正式登录状态机 |
| `loginStop` | 停止对应 Web 后端的登录状态机 |
| `loginState` | 读取对应 Web 后端登录状态机状态 |

### 导航与页面状态

| action | 作用 |
| --- | --- |
| `navigate` | 导航到 `url` |
| `url` | 读取当前 `location.href` |
| `discover` | 生成通用页面发现结果，包含标题、正文预览、可排序目标 `targets`、按钮、链接、输入框、表单 |
| `snapshot` | 读取页面标题、URL、正文预览、可见控件、控件 XPath 与坐标 |

### DOM 与 XPath

| action | 作用 |
| --- | --- |
| `eval` | 执行 `script` 并返回字符串结果 |
| `resolveXPath` | 按 XPath 读取元素信息、属性、矩形、文本，以及可选的 `outerHTML` / `innerHTML` |
| `readXPath` | `resolveXPath` 的别名，适合脚本侧更直观的“读取元素”语义 |
| `resolveXPathTree` | 按 XPath 读取节点树，有限深度展开父子结构，适合文档/菜单层级分析 |

### 输入与人机行为

| action | 作用 |
| --- | --- |
| `clickXPath` | 用虚拟鼠标点击 XPath 对应元素 |
| `mouseMove` | 在页面坐标 `x`,`y` 执行页面级虚拟鼠标移动 |
| `mouseClick` | 在页面坐标 `x`,`y` 执行页面级虚拟鼠标移动并点击 |
| `mouseCheck` | 检查页面内虚拟鼠标状态 |

### Cookie 与会话身份

| action | 作用 |
| --- | --- |
| `cookieGet` | 读取当前 WebView2 会话指定域名 Cookie |
| `cookieSet` | 向当前 WebView2 会话注入 Cookie |
| `cookieClear` | 清除当前 WebView2 会话 Cookie 和浏览数据 |

### 网页脚本监管与接管

| action | 作用 |
| --- | --- |
| `monitorStart` | 挂载网页脚本监管器，监视 fetch/XHR/SSE/WS/history/location/DOM 等接口行为，并开始事件缓冲 |
| `events` | 读取运行时事件缓冲，`count` 控制数量，`clear=true` 表示读后移除 |

### 可视化证据

| action | 作用 |
| --- | --- |
| `screenshot` | 截取指定矩形并返回 PNG base64 |

## 事件缓冲

`monitorStart` 会订阅当前 WebView2 会话的 `WebMessage`，并把事件保存在服务内存缓冲中。它的定位是监管网页原生 JavaScript 的接口行为，先观察网页脚本做了什么，再为后续屏蔽和替换做准备。`events` 返回统一事件结构：

```json
{
  "timestamp": "2026-07-06T12:00:00.0000000+00:00",
  "backendId": "doubao-web",
  "source": "webmessage",
  "kind": "network",
  "payload": "AIG_NET:..."
}
```

`kind` 当前分类：

| kind | 来源 |
| --- | --- |
| `push` | `AIG_PUSH:` 页面推送/脚本消息 |
| `network` | `AIG_NET:` fetch/XHR 响应拦截 |
| `script` | `AIG_JS:` 页面脚本上报 |
| `message` | 其他 WebMessage |

默认 HTTP 拦截关键词包括 `/chat/completion`、`/samantha/chat/completion`、`/conversation`、`/backend-api/`。调用方可通过 `args.responseFilters` 覆盖。

网页脚本监管与接管分三步推进：

| 阶段 | 说明 |
| --- | --- |
| 监管 | 记录网页 JS 调用 fetch/XHR/SSE/WS/history/location/DOM 等接口时的参数、响应和触发路径 |
| 屏蔽 | 对已经确认的接口行为进行选择性阻断或短路，避免副作用 |
| 替换 | 用 AIGateway 自己的实现返回兼容数据，最终让直接 HTTP 发送取代网页原始 JS 发送 |

`discover` 是给上层 AI 的通用接口。它比 `snapshot` 更偏“可爬取、可理解”的发现结果，适合自动找按钮、链接、输入框、表单以及后续点击目标。`targets` 按优先级排序，AI 可以先看 `buttons` / `targets`，再退回到 `links`、`inputs`、`forms`。

`resolveXPath` 则是更偏“直接取元素”的接口。它不做点击，只把 XPath 对应的 DOM 节点解析出来，返回标签、属性、文本、位置信息，以及可选的 HTML 片段。适合“拿一个文档节点出来”“先看这个按钮到底是什么，再决定点不点”这类流程。

`resolveXPathTree` 是树形版本。它会把目标节点及其子节点按有限深度展开，方便看一个文档块、一个弹出菜单、一个工具面板的结构，不需要先在页面里手动一层层点开。

## 虚拟鼠标原则

运行时坐标鼠标不是 Win32 系统鼠标，也不会移动用户真实鼠标。它在页面内从上一次位置出发，按多步随机漂移轨迹派发 `pointermove`、`mousemove`、`mouseover`、点击相关事件，并维护 `window.__aigateway_virtual_mouse`。这用于让页面级风控脚本看到连续鼠标活动。

## 与历史诊断入口的边界

历史 `--trace web-live-*` 入口只允许作为人工诊断工具。正式功能入口是本接口。状态机、管理层、UI、Tester 和 AI 工具都应通过 `WebRuntimeControl` 访问运行中 WebView2。

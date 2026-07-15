# WebRuntime 页面脚本受控反射：说明与任务计划

## 目标

为公共 `Iwesun.Runtime.WebView2` 提供一个明确的脚本入口，用于读取真实页面的登录前置状态、Cookie 驱动布局输入和异步 DOM 结果。入口不改变页面自身 JavaScript 的正常运行，也不把脚本执行混入普通 DOM 命令。

## 工作原理

1. CLI 或业务程序通过 `WebRuntimePipeClient` 发送标准 `web.runtime` 请求。
2. 请求使用 `action=script.evaluate`（兼容短别名 `eval`），脚本放在 `script` 或 `args.script`。
3. 公共模块的 `WebRuntimeScriptDispatcher` 校验动作、脚本是否为空和最大长度。
4. 分发器调用现有 `IWebRuntimeScriptSession.EvaluateStringAsync`，由宿主在 WebView2 所属 STA 线程执行。
5. 返回统一结果：`executed`、`length`、`result`；失败返回稳定错误码，不吞异常。

该链路只增加“显式请求脚本评估”能力，不自动注入登录、不伪造 Cookie，也不替业务决定页面布局。布局取证时，调用方可以先读取页面实际的 Cookie/登录信号，再将结果交给 C# 布局函数。

## 当前实施

- 新增 `WebRuntimeScriptActions`：`script.evaluate` 与 `eval`。
- `WebRuntimeControlRequest` 增加 `Script` 字段，同时保留 `args.script` 和 `Text` 作为输入来源。
- 新增 `WebRuntimeScriptDispatcher`，限制脚本长度为 256 KiB，并提供 `MISSING_SCRIPT`、`SCRIPT_TOO_LARGE`、`SCRIPT_EVALUATION_FAILED` 错误码。
- 新增有界 `WebRuntimeScriptAuditLog`，记录时间、动作、脚本长度、成功状态和错误码；不记录脚本文本、Cookie 或令牌。
- 新增 `WebRuntimeNetworkRuleRegistry`，支持按后端、HTTP 方法和 URL 通配模式决定阻断或固定响应替换；宿主在 WebView2 网络事件中应用该决策。
- 新增 `IWebRuntimeHostAdapter` 与 `WebRuntimeHostController`，统一把网络决策和 XPath 证据请求接入宿主。
- 保留现有监视器、DOM、虚拟鼠标和键盘接口不变。

## 后续任务

1. 在 Runtime CLI 能力清单中登记 `scriptEvaluate/eval`。
2. 用该入口采集豆包真实页面的登录判断、Cookie 访问和技能按钮 DOM，保存为取证 JSON。
3. 将取证结果映射到 `DoubaoLayoutEngine`，验证 C# 顺序与页面顺序。
4. 在宿主提供 WebView2 网络拦截挂接点后，再设计脚本阻断/替换规则；规则必须单独授权、可审计、可清除，不能隐式改变页面行为。

## 安全边界

脚本入口是高权限调试能力，只应通过 Runtime 专用管道暴露。生产 CLI 应记录请求来源、页面 URL、脚本长度和结果摘要，避免记录 Cookie 值和访问令牌。

# WebView2 完整运行时 DOM 真快照：技术与使用手册

需要同时保存 CSS、脚本、事件、CDP、MHTML 和 HTTP 输入正文时，使用[完整页面与 HTTP 输入证据 API](FULL_PAGE_EVIDENCE_API.md)。本页继续定义可恢复 DOM 真快照本身。

本文是 `Iwesun.Runtime.WebView2` 完整运行时 DOM 真快照能力的权威说明，面向 Runtime 宿主开发者、管理程序开发者和 CLI 使用者。

## 1. 能力目标

真快照保存的是页面在采集时刻已经形成的 DOM 结果态，而不是网站源 HTML、CSS 文件、截图或 MHTML：

- 递归保存顶层 `document`、可访问的 iframe document 和开放 shadow root。
- 保存元素、文本、注释节点及其父子顺序。
- 保存每个元素的全部 attribute。
- 保存表单值、选择状态、滚动、媒体、尺寸等可序列化的 primitive property。
- 恢复时重建 DOM，不重新运行原网站的业务 JavaScript。
- 数据入口和事件入口通过独立 Link Plan 重新连接。

该 API 与站点无关，不包含豆包、账号、权限、历史记录、技能栏或业务文件路径语义。

### 1.1 与其他保存方式的区别

| 方式 | 保存内容 | 适用范围 |
| --- | --- | --- |
| `outerHTML` | HTML 字符串和序列化 attribute | 不含多数运行时 property、iframe 内文档和事件链接 |
| MHTML | 页面及部分静态资源归档 | 适合辅助显示，不等于完整运行时 DOM |
| 截图 | 像素结果 | 不能恢复控件、XPath 或交互 |
| DOM 真快照 | 节点、attribute、primitive property、iframe、shadow root | 结果态 UI 克隆和后续数据/事件重连 |

CSS、图片和字体仍由宿主已加载的资源环境或独立静态资源提供。事件监听器不是 DOM attribute，不能从节点序列化中可靠还原，因此必须通过事件链接重新挂载。

## 2. 工作流程

```text
真实 WebView2 页面稳定
  -> CaptureAsync
  -> 原始 snapshot JSON
  -> 静态资源环境中 RestoreAsync / RestoreAndLinkAsync
  -> 按 XPath 注入本地数据并挂载稳定事件 ID
  -> WebMessage 回到 C# 业务逻辑
  -> 可选：再次 CaptureAsync，生成已链接的第二快照
```

建议把“原始快照”和“已链接快照”分开保存。原始快照用于核对页面，第二快照用于文件回放或进一步静态化。

## 3. 公共 C# API

命名空间：

```csharp
using Iwesun.Runtime.WebView2;
```

### 3.1 会话契约

宿主至少实现：

```csharp
public interface IWebRuntimeScriptSession
{
    Task<string> EvaluateStringAsync(string expression, CancellationToken ct);
    Task<string> EvaluateStringInFrameAsync(
        string sourceUrlContains,
        string expression,
        CancellationToken ct);
}
```

包含多个相同 URL iframe，或需要按 DOM 顺序可靠恢复 frame 时，同时实现：

```csharp
public interface IWebRuntimeIndexedFrameScriptSession
{
    Task<string> EvaluateStringInFrameAsync(
        int frameIndex,
        string expression,
        CancellationToken ct);
}
```

所有调用必须在 WebView2 所属 STA/Dispatcher 上执行。取消令牌会向调用方传播；无效或空脚本结果不会返回伪成功快照。

### 3.2 采集

```csharp
string snapshotJson = await WebRuntimeDomSnapshot.CaptureAsync(session, cancellationToken);
await File.WriteAllTextAsync(
    snapshotPath,
    snapshotJson,
    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
    cancellationToken);
```

返回值就是原始快照 JSON，不带管理协议 Envelope。

### 3.3 只恢复 DOM

```csharp
string restoreResultJson = await WebRuntimeDomSnapshot.RestoreAsync(
    session,
    snapshotJson,
    cancellationToken);
```

恢复会替换当前 document tree，并保留宿主已经加载的资源环境。返回 JSON 至少包含 `restored=true`，并报告顶层和 frame 恢复结果。

### 3.4 恢复并连接数据、事件

```csharp
using var titleValue = JsonDocument.Parse("\"本地文档标题\"");

var plan = new WebRuntimeDomLinkPlan
{
    DataLinks =
    [
        new WebRuntimeDomDataLink
        {
            XPath = "/html/body/main/h1",
            Operation = "text",
            DataKey = "document.title",
            Value = titleValue.RootElement.Clone()
        },
        new WebRuntimeDomDataLink
        {
            XPath = "/html/body/main/a",
            Operation = "attribute",
            Name = "href",
            DataKey = "document.url",
            Value = JsonDocument.Parse("\"app://documents/42\"").RootElement.Clone()
        }
    ],
    EventLinks =
    [
        new WebRuntimeDomEventLink
        {
            XPath = "/html/body/main/button",
            EventType = "click",
            EventId = "document.close",
            PreventDefault = true,
            StopPropagation = true
        }
    ]
};

string resultJson = await WebRuntimeDomSnapshot.RestoreAndLinkAsync(
    session,
    snapshotJson,
    plan,
    cancellationToken);
```

数据操作：

| `operation` | 作用 | `name` |
| --- | --- | --- |
| `link` | 写入稳定数据链接标记 | 可省略 |
| `text` | 设置 `textContent` | 不使用 |
| `attribute` | 设置 attribute | 必填，例：`href`、`src` |
| `property` | 设置 DOM property | 必填，例：`value`、`checked` |
| `hidden` | 设置元素隐藏状态 | 不使用 |

`RestoreAndLinkAsync` 的结果包含 `restored` 与 `links` 两部分。`links` 会报告命中、缺失和无效链接；业务程序应把缺失 XPath 当作模板版本不匹配，而不是静默忽略。

## 4. 快照 JSON 格式

顶层 schema 固定为：

```json
{
  "schema": "iwesun.webview2.dom-snapshot/1.0",
  "capturedAt": "2026-07-17T08:00:00.000Z",
  "top": {
    "url": "https://example.test/",
    "title": "示例",
    "documentElement": {}
  }
}
```

节点按类型保存。元素节点的主要字段如下：

```json
{
  "nodeType": 1,
  "nodeName": "BUTTON",
  "namespaceURI": "http://www.w3.org/1999/xhtml",
  "attributes": [
    { "namespaceURI": null, "name": "aria-label", "value": "关闭" }
  ],
  "properties": {
    "hidden": false,
    "tabIndex": 0
  },
  "ownPrimitiveProperties": {},
  "childNodes": [],
  "shadowRoot": null,
  "contentDocument": null
}
```

重要约束：

- `attributes` 是数组，保留 namespace、名称和值，不应降级成只保留常见属性的字典。
- `childNodes` 保留元素、文本和注释节点的原始顺序。
- `contentDocument` 仅在同源或 WebView2 允许访问的 frame 上存在。
- 关闭的 shadow root 无法由页面脚本读取，因此不会伪造。
- Cookie、localStorage、sessionStorage 和网络响应不是 DOM；除非其结果已进入 DOM，否则不属于本快照。
- 快照可能包含用户文本、链接和表单当前值，必须按敏感业务数据管理，禁止直接提交到源码仓库。

## 5. 管理动作与业务 JSON

WebRuntime 公共动作名：

| 动作 | 说明 |
| --- | --- |
| `dom.snapshot.capture` | 采集当前完整结果态 DOM |
| `dom.snapshot.restoreAndLink` | 恢复快照并连接数据与事件 |

业务宿主收到的 `WebRuntimeControlRequest` JSON：

```json
{
  "programId": "sample.webview2",
  "backendId": "doubao-web",
  "action": "dom.snapshot.capture"
}
```

恢复和链接：

```json
{
  "programId": "sample.webview2",
  "backendId": "doubao-web",
  "action": "dom.snapshot.restoreAndLink",
  "args": {
    "snapshot": "{\"schema\":\"iwesun.webview2.dom-snapshot/1.0\",...}",
    "linkPlan": {
      "dataLinks": [
        {
          "xpath": "/html/body/main/h1",
          "operation": "text",
          "dataKey": "document.title",
          "value": "本地标题"
        }
      ],
      "eventLinks": [
        {
          "xpath": "/html/body/main/button",
          "eventType": "click",
          "eventId": "document.close",
          "preventDefault": true,
          "stopPropagation": true
        }
      ]
    }
  }
}
```

`args.snapshot` 必须是 JSON 字符串。`args.linkPlan` 可以是 JSON object，也可以是包含 JSON 的字符串；推荐使用 object，避免双重转义。

### 5.1 标准 RuntimeDiagnosticFrame

跨进程调用不另建私有 Envelope。请求采用 4 字节小端长度前缀加 UTF-8 `RuntimeDiagnosticFrame`：

```json
{
  "header": {
    "schema": "rtdiag/2.0",
    "frameType": "request",
    "category": "instruction",
    "operation": "invoke",
    "requestId": "client-generated-id",
    "source": "sample.client",
    "destination": "resolved-web-runtime-pipe"
  },
  "command": {
    "domain": "web.runtime",
    "target": "doubao-web",
    "action": "dom.snapshot.capture",
    "args": {
      "programId": "sample.webview2"
    }
  }
}
```

管理客户端应使用 Runtime 的 Frame 编解码和 `RuntimePipeRegistry` 解析出的管道名，不应手写无长度前缀的 JSON 管道协议。

## 6. CLI

源代码目录中的 CLI v3 命令：

```powershell
iwrt web.dom.snapshot doubao-web
iwrt web.dom.restore-link doubao-web "<snapshot-json>" "<link-plan-json>"
```

帮助：

```powershell
iwrt web.dom.snapshot --help
iwrt web.dom.restore-link --help
```

`web.dom.snapshot` 与 `web.snapshot` 不同：前者返回完整 DOM 真快照，后者是供人工诊断使用的页面概览。

CLI 先连接 RuntimeDiagnostics，再由 `diagnostics.proxy` 根据 `RuntimePipeRegistry` 的 WebRuntime 租约转发请求。等价代理参数为：

```text
module=WebRuntime
pipe=WebRuntime
targetId=<backend>
domain=web.runtime
proxyAction=dom.snapshot.capture | dom.snapshot.restoreAndLink
```

完整 DOM 通常为数 MB。Windows 命令行不适合把大快照作为参数传给 `web.dom.restore-link`；生产业务和大文件恢复应调用 C# API 或使用标准管理客户端。CLI 恢复命令主要用于小型样例和连通性验证。

## 7. 返回数据与接收方式

### 7.1 直接 C# API

- `CaptureAsync`：返回原始 snapshot JSON 字符串。
- `RestoreAsync`：返回恢复验证 JSON。
- `RestoreAndLinkAsync`：返回恢复验证与链接统计 JSON。

### 7.2 Dispatcher

`WebRuntimeScriptDispatcher.TryExecuteAsync` 返回 `WebRuntimeScriptDispatchResult`：

- 采集成功：`Handled=true`、`Success=true`，`Value` 为 `{ snapshot }`。
- 恢复成功：`Handled=true`、`Success=true`，`Value` 为 `{ result }`。
- 失败：`Handled=true`、`Success=false`，并提供 `Code`、`Error`。

### 7.3 管道与 CLI

标准响应仍是 `RuntimeDiagnosticFrame`：

```json
{
  "header": {
    "schema": "rtdiag/2.0",
    "frameType": "response",
    "requestId": "client-generated-id"
  },
  "status": {
    "ok": true,
    "code": "OK",
    "message": "",
    "retryable": false
  },
  "data": {
    "snapshot": "{\"schema\":\"iwesun.webview2.dom-snapshot/1.0\",...}"
  },
  "meta": {
    "durationMs": 123
  }
}
```

经过 `diagnostics.proxy` 时，外层 `data` 还包含租约、代理目标、内层 `status` 和内层 `data`；快照位于内层 `data.snapshot`。调用方必须先检查外层 `status.ok`，再检查代理内层 `status.ok`，不能只判断进程退出码。

一个不依赖固定代理嵌套层数的 C# 接收示例：

```csharp
using var response = JsonDocument.Parse(responseJson);

JsonElement root = response.RootElement;
if (!root.GetProperty("status").GetProperty("ok").GetBoolean())
    throw new InvalidOperationException(
        root.GetProperty("status").GetProperty("message").GetString());

static bool TryFindSnapshot(JsonElement element, out string? snapshot)
{
    if (element.ValueKind == JsonValueKind.Object)
    {
        if (element.TryGetProperty("snapshot", out var value)
            && value.ValueKind == JsonValueKind.String)
        {
            snapshot = value.GetString();
            return true;
        }

        foreach (JsonProperty property in element.EnumerateObject())
            if (TryFindSnapshot(property.Value, out snapshot))
                return true;
    }

    snapshot = null;
    return false;
}

if (!TryFindSnapshot(root.GetProperty("data"), out string? snapshotJson)
    || string.IsNullOrWhiteSpace(snapshotJson))
    throw new InvalidDataException("Response does not contain a DOM snapshot.");
```

RuntimeDiagnostics 命令和代理响应的单帧上限为 16 MiB。超过上限必须改用直接 C# API 或业务自己的分块存储，禁止截断后仍标记为完整快照。

## 8. 事件接收

事件链接触发后，页面通过 WebView2 WebMessage 发出：

```json
{
  "source": "iwesun.runtime.webview2.dom-snapshot",
  "kind": "event",
  "eventId": "document.close",
  "eventType": "click",
  "xpath": "/html/body/main/button",
  "value": null,
  "checked": null
}
```

宿主在正式 WebView2 业务通道接收消息，并按稳定 `eventId` 路由：

```csharp
coreWebView2.WebMessageReceived += (_, args) =>
{
    using var message = JsonDocument.Parse(args.WebMessageAsJson);
    JsonElement root = message.RootElement;

    if (root.TryGetProperty("source", out var source)
        && source.GetString() == "iwesun.runtime.webview2.dom-snapshot"
        && root.TryGetProperty("eventId", out var eventId))
    {
        eventRouter.Dispatch(eventId.GetString() ?? "", root.Clone());
    }
};
```

这是正式业务事件通道，不依赖 CLI、FIFO 调试记录器或系统鼠标。

## 9. 完整端到端样例

```csharp
// 1. Capture the stable live page.
string original = await WebRuntimeDomSnapshot.CaptureAsync(liveSession, ct);

// 2. Persist the unmodified evidence.
await File.WriteAllTextAsync("page.original.snapshot.json", original, ct);

// 3. Define local data and stable event routes.
var plan = new WebRuntimeDomLinkPlan
{
    DataLinks =
    [
        new()
        {
            XPath = "/html/body/main/article/h1",
            Operation = "text",
            DataKey = "article.title",
            Value = JsonDocument.Parse("\"离线标题\"").RootElement.Clone()
        }
    ],
    EventLinks =
    [
        new()
        {
            XPath = "/html/body/main/article/button",
            EventType = "click",
            EventId = "article.close"
        }
    ]
};

// 4. Restore in the local/static WebView2 and attach business links.
string linkResult = await WebRuntimeDomSnapshot.RestoreAndLinkAsync(
    replaySession,
    original,
    plan,
    ct);

// 5. Capture a second, linked snapshot for replay verification.
string linked = await WebRuntimeDomSnapshot.CaptureAsync(replaySession, ct);
await File.WriteAllTextAsync("page.linked.snapshot.json", linked, ct);
```

实际宿主应把文件名、数据源和事件处理器放在自己的业务层；Runtime API 不负责决定保存目录，也不把站点数据固化进公共库。

## 10. 错误码与处理

| 错误码/异常 | 含义 | 处理 |
| --- | --- | --- |
| `DOM_SNAPSHOT_CAPTURE_FAILED` | 页面执行、JSON 或 schema 验证失败 | 保留错误消息，确认页面已稳定且会话仍有效 |
| `MISSING_DOM_SNAPSHOT` | 恢复请求缺少字符串 `args.snapshot` | 修正请求格式 |
| `DOM_SNAPSHOT_RESTORE_FAILED` | DOM 或 frame 恢复、链接阶段失败 | 核对 schema、frame 会话和 XPath |
| `InvalidDataException` | 空结果、schema 不符、缺少根节点或恢复验证失败 | 不得把结果当作成功快照 |
| `OperationCanceledException` | 调用取消或宿主超时 | 按宿主策略重试，不吞掉取消状态 |

错误检查顺序：

1. 检查 Runtime Frame 外层 `status.ok`、`code`、`retryable`。
2. 使用代理时检查代理内层 `status.ok`。
3. 检查 dispatcher `Success`。
4. 检查快照 schema 和 `top.documentElement`。
5. 检查恢复结果与链接的 missing/invalid 统计。

## 11. 验收建议

不要只用截图验收。至少比较：

- XPath 集合及父子层级。
- 节点类型、顺序和父容器。
- attribute 数量和值。
- 表单值、checked/selected、scroll 等 property。
- iframe document 和开放 shadow root 数量。
- 数据链接命中、缺失和无效数量。
- 每个动态元素的稳定事件 ID、点击结果和 WebMessage。
- resize、折叠、隐藏、拖动等行为所需事件是否已由宿主正式接管。

真快照解决“完整保存结果态 DOM”；自动布局、网络数据解释器、业务数组和复杂编辑器仍应作为明确的宿主模块连接，不能把原网站环境判断脚本重新引入静态页。

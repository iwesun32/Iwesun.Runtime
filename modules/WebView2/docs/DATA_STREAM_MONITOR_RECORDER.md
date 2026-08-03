# WebView2 数据流监视记录器

## 1. 目标

`Iwesun.Runtime.WebView2` 提供与站点无关的数据流监视记录器。业务代码和 Runtime CLI 使用同一套公共协议创建、启动、查询、修改、停止和删除监视器。记录器在数据进入 DOM 或业务解释器之前保存应用响应正文，并以清单文件记录关联元数据。

记录器不理解豆包历史、会话或文档语义。业务项目通过 URL、方法、Content-Type 和内容匹配条件完成分类；复杂语义可以注册 C# 分类器。

## 2. 边界

- 公共层负责生命周期、匹配、限额、安全文件名、原始正文、清单和事件。
- WebView2 宿主负责把 HTTP/Fetch/XHR、WebSocket、SSE、WebMessage 或下载数据送入记录器。
- CLI 只发送管理命令和接收小型状态/事件；大型正文由宿主直接写文件。
- WebView2 `GetContentAsync()` 提供浏览器解码后的应用正文，不承诺保存 TCP/TLS 线上压缩字节。逐包抓取属于代理或网络诊断层。
- Cookie、Authorization、Set-Cookie 等敏感头默认不记录。

## 3. 生命周期

监视器状态为 `Created -> Running -> Stopped`，异常进入 `Faulted`，删除后从管理器移除。操作语义如下：

- `CreateAsync`：登记定义，不开始捕获。
- `StartAsync`：开始匹配新记录；已运行时幂等返回当前状态。
- `UpdateAsync`：原子替换定义并增加修订号，下一条记录使用新定义。
- `StopAsync`：停止新匹配，保留定义、统计和输出文件。
- `DeleteAsync`：删除定义；默认不删除输出目录。运行中的监视器必须先停止，或明确请求强制停止。
- `Get`/`List`：返回单个或全部监视器状态。
- `WatchEventsAsync`：异步订阅元数据事件，不传输正文。

## 4. 两阶段匹配

第一阶段只检查元数据：BackendId、传输类型、方向、URL、HTTP 方法、状态码和 Content-Type。第一阶段不命中时不得读取正文。

第二阶段检查正文：文件特征、文本标志和 JSON Path。业务可注册 `IDataStreamContentClassifier`，返回数据类型、业务 ID 和推荐扩展名。公共记录器只保存原始请求/响应交换；从一个响应提取多个 Markdown、图片或业务对象属于业务解释层，不由 Runtime 隐式生成。

### 4.1 通用复合条件

简单的 `Source` 和 `Content` 字段保持可用，内部按隐式 `All` 处理。复杂条件使用递归表达式树：

```json
{
  "condition": {
    "kind": "all",
    "conditions": [
      { "field": "response.contentType", "operator": "contains", "value": "json" },
      {
        "kind": "any",
        "conditions": [
          { "field": "response.json", "operator": "pathExists", "value": "$..content_block" },
          { "field": "response.json", "operator": "pathExists", "value": "$..source_content" }
        ]
      },
      {
        "kind": "not",
        "conditions": [
          { "field": "request.url", "operator": "contains", "value": "/prepare" }
        ]
      }
    ]
  }
}
```

组合节点为 `All`、`Any`、`Not`、`AtLeast`、`Always`、`Never`。`AtLeast` 使用 `minimumMatches` 指定最少命中数。

叶子操作符包括存在、相等、包含、正则、集合、数值比较、类型、数组数量、JSON Path 和文件特征。字段名使用稳定命名空间，例如 `request.url`、`request.method`、`response.statusCode`、`response.contentType`、`response.bodyLength`、`response.text`、`response.json` 和 `response.body`。宿主没有提供的数据字段按“不存在”处理。

请求与响应使用同一个 `DataStreamRecord` 关联。请求正文使用 `RequestContent`，响应正文使用 `Content`。额外通用字段为 `request.bodyLength`、`request.text`、`request.json` 和 `request.body`。例如：

```json
{
  "kind": "all",
  "conditions": [
    { "field": "request.json", "operator": "pathExists", "value": "$..conversation_id" },
    { "field": "response.json", "operator": "pathExists", "value": "$..content_block" }
  ]
}
```

`RequestHeaders` 和 `ResponseHeaders` 只包含经过宿主安全过滤的头；Authorization、Cookie、Set-Cookie 默认不可见。

### 4.2 监视器专属匹配委托

每个监视器最多绑定一个 `DataStreamMatchDelegate`。委托和通用条件使用以下组合模式：

| 模式 | 最终条件 |
|---|---|
| `None` | `General` |
| `And` | `General && Delegate` |
| `Or` | `General || Delegate` |
| `Override` | `Delegate`，完全忽略通用条件 |

CLI JSON 只保存 `matcherId`、`mode` 和数据访问方式。实际委托由宿主注册：

```csharp
manager.RegisterMatcher("doubao.document", MatchDoubaoDocumentAsync);
```

业务代码也可以用 `SetMatcherAsync` 给单个监视器直接挂载委托。找不到命名委托时创建或启动失败，不允许静默改成只使用通用条件。

委托数据访问方式为 `MetadataOnly`、`BodyPreview`、`FullBody`、`ParsedText`、`ParsedJson`。委托异常只使当前记录失败；达到配置的连续错误阈值后，该监视器进入 `Faulted`，其他监视器不受影响。

## 5. 公共 C# API

公共入口为 `IDataStreamRecorderManager`：

```csharp
var definition = new DataStreamMonitorDefinition
{
    MonitorId = "doubao-document",
    Source = new DataStreamSourceMatch
    {
        BackendId = "doubao-web",
        UrlContains = "/im/chain/single",
        Methods = ["GET"],
        ContentTypeContains = "json"
    },
    Output = new DataStreamOutputDefinition
    {
        RootDirectory = @"D:\captures",
        DirectoryTemplate = "{monitorId}/{date}",
        FileNameTemplate = "{timestamp}-{sequence}-{dataType}-{identity}.{extension}"
    }
};

await manager.CreateAsync(definition, ct);
await manager.StartAsync(definition.MonitorId, ct);
```

管理器必须由宿主持有并在 WebView2 会话结束时释放：

```csharp
await using IDataStreamRecorderManager manager = new DataStreamRecorderManager();
manager.RegisterMatcher("product.history", MatchHistoryAsync);
// CreateAsync -> StartAsync -> ObserveAsync ... -> StopAsync
```

`HasMetadataMatch(record)` 是宿主读取大正文前的快速门。宿主先用空正文构造元数据记录；只有至少一个运行中监视器可能命中时，才读取请求/响应正文并调用 `ObserveAsync`。如果宿主已经因其他业务目的取得正文，可以直接复用同一字节数组，禁止再次读取不可回绕的 WebView2 流。

直接挂载特异性委托：

```csharp
await manager.SetMatcherAsync(
    "doubao-document",
    MatchDoubaoDocumentAsync,
    DataStreamCustomMatchMode.And,
    DataStreamDelegateDataAccess.ParsedJson,
    ct);
```

WebView2 宿主把已取得的应用正文送入 `ObserveAsync`：

```csharp
await manager.ObserveAsync(new DataStreamRecord
{
    BackendId = "doubao-web",
    Transport = DataStreamTransport.Http,
    Direction = DataStreamDirection.Response,
    Method = request.Method,
    Url = request.Uri,
    StatusCode = response.StatusCode,
    ContentType = contentType,
    RequestContent = requestBytes,
    Content = bytes
}, ct);
```

事件订阅：

```csharp
await foreach (var item in manager.WatchEventsAsync(
    new DataStreamEventSubscription { MonitorId = "doubao-document" }, ct))
{
    // RecordWritten, RecordDropped, MonitorStopped, MonitorFaulted...
}
```

事件只包含 MonitorId、RecordId、分类、路径、字节数和错误摘要；调用方根据路径读取正文。

完整可编译样例位于 `modules/WebView2/samples/Iwesun.Runtime.WebView2.SampleHost/DataStreamRecorderSample.cs`，覆盖命名委托、请求/响应联合记录、请求 sidecar、manifest 和生命周期释放。

## 6. 文件输出

内置文件输出支持：

- 根目录、目录模板和文件名模板；
- `{monitorId}`、`{backendId}`、`{date}`、`{timestamp}`、`{sequence}`、`{dataType}`、`{identity}`、`{extension}`、`{hash}`；
- 非法字符清理、路径穿越阻断、自动建目录和自动序号防覆盖；
- `manifest.jsonl`，每行对应一个完成的原始记录；
- 可选请求正文 sidecar 与响应正文分开保存，并在 manifest 中关联。

停止或删除监视器不会默认删除业务数据。

## 7. CLI 映射

CLI 和 C# API 一一对应：

```text
web.data-recorder.create
web.data-recorder.start
web.data-recorder.status
web.data-recorder.list
web.data-recorder.update
web.data-recorder.stop
web.data-recorder.delete
web.data-recorder.events
```

命令位置参数如下：

| 命令 | 必填位置参数 | 可选位置参数 |
|---|---|---|
| `create` | `targetId backendId definition` | `module pipe proxyAction domain programId` |
| `start/status/stop` | `targetId backendId monitorId` | `module pipe proxyAction domain programId` |
| `list` | `targetId backendId` | `module pipe proxyAction domain programId` |
| `update` | `targetId backendId monitorId definition` | `module pipe proxyAction domain programId` |
| `delete` | `targetId backendId monitorId` | `forceStop deleteOutputFiles module pipe proxyAction domain programId` |
| `events` | `targetId backendId` | `monitorId count waitMs module pipe proxyAction domain programId` |

PowerShell 示例：

```powershell
$definition = '{"monitorId":"history","source":{"backendId":"doubao-web","transports":["Http"],"directions":["Response"],"urlContains":"/im/","methods":["POST"],"statusCodes":[200],"contentTypeContains":"json"},"output":{"rootDirectory":"D:\\captures","directoryTemplate":"{monitorId}/{date}","fileNameTemplate":"{timestamp}-{sequence}-{hash}.{extension}","writeManifest":true,"writeRequestBody":true}}'
iwrt web.data-recorder.create doubao-web doubao-web $definition
iwrt web.data-recorder.start doubao-web doubao-web history
iwrt web.data-recorder.status doubao-web doubao-web history
iwrt web.data-recorder.events doubao-web doubao-web history 20 500
iwrt web.data-recorder.stop doubao-web doubao-web history
iwrt web.data-recorder.delete doubao-web doubao-web history false false
```

CLI 不实现第二套匹配、命名或文件写入逻辑。命名 `matcherId` 必须已经由目标宿主注册；仅在 CLI 中写入一个未知 ID 会明确失败。

管理请求的 `proxyAction` 分别为：

```text
data.recorder.create
data.recorder.start
data.recorder.status
data.recorder.list
data.recorder.update
data.recorder.stop
data.recorder.delete
data.recorder.events
```

状态和事件响应不得包含正文。

### 7.1 管理 JSON Frame

CLI 最终编译为标准 `RuntimeDiagnosticFrame`。create 请求示例：

```json
{
  "header": {
    "schema": "rtdiag/2.0",
    "frameType": "request",
    "category": "instruction",
    "operation": "create",
    "requestId": "req-data-recorder-1",
    "source": "runtime-cli",
    "destination": "Product.RuntimeDiagnostics"
  },
  "command": {
    "domain": "proxy",
    "target": "diagnostics.proxy",
    "action": "invoke",
    "args": {
      "targetId": "doubao-web",
      "backendId": "doubao-web",
      "module": "WebRuntime",
      "pipe": "WebRuntime",
      "programId": "aigateway.webview2",
      "proxyAction": "data.recorder.create",
      "definition": {
        "monitorId": "history",
        "source": {
          "backendId": "doubao-web",
          "transports": ["Http"],
          "directions": ["Response"],
          "urlContains": "/im/",
          "methods": ["POST"],
          "statusCodes": [200],
          "contentTypeContains": "json"
        },
        "output": {
          "rootDirectory": "D:\\captures",
          "writeManifest": true,
          "writeRequestBody": false
        }
      }
    }
  }
}
```

通过命名管道发送时仍使用 4 字节 little-endian 长度前缀加 UTF-8 JSON。业务代码应优先调用公共 C# API或 CLI，不手写 Frame 编解码器。

### 7.2 返回数据

- create/start/status/update/stop 返回 `DataStreamMonitorInfo`；
- list 返回监视器状态数组；
- delete 返回删除结果，不返回文件正文；
- events 返回有界 `DataStreamRecorderEvent` 数组；
- 错误使用 Frame `Status`、错误 Code 和 Retryable，不创建私有 `success/error` Envelope。

## 8. 豆包业务配置建议

- 历史列表：URL/方法粗过滤，再检查会话数组、分页游标和 `has_more`。
- 单会话：按 `/im/chain/single` 等接口过滤，以 conversation ID 建目录，保存完整原始 JSON。
- 文档：检查 artifact/document 标志及 `source_content`；保存原始 JSON，同时派生 `.md`。

这些规则属于豆包业务项目，不进入 Runtime 公共程序集。UI 点击顺序可以触发网络加载，但不作为分类依据。

## 9. 并发和安全

- WebView2 STA 回调只复制必要元数据和正文，不执行同步磁盘阻塞。
- 同一个监视器的写入通过异步门串行化；多个监视器彼此独立。事件通过有界通道发布。
- 更新定义按修订号原子切换。
- 达到正文、文件数、总字节或队列上限时丢弃并发布 `RecordDropped`，不得拖死页面。
- Dispose 必须停止全部监视器、完成队列并解除宿主事件订阅。

宿主事件订阅不由公共管理器自动安装。正确释放顺序是：先停止接受新的 WebView2 回调并解除宿主事件，再等待当前 `ObserveAsync`，最后 `DisposeAsync` 管理器和 WebView2 会话。

## 10. 验收

1. C# 直接调用可完成 create/start/observe/status/stop/delete。
2. CLI 可完成同一生命周期，不传输正文。
3. URL、方法、Content-Type 不匹配时不产生文件。
4. 命中时正文和 `manifest.jsonl` 一致，文件名不可越出根目录。
5. 停止后继续产生网络响应不会写入新文件。
6. Debug/Release 编译通过，宿主退出后无残留事件订阅或文件句柄。

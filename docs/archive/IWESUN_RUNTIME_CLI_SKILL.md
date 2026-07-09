# Iwesun Runtime CLI Skill

> DDNS Snap note: this skill document was copied with the newer CLI from
> AIGateway. In DDNS Snap, the command JSON defaults to
> `DdnsSnap.RuntimeDiagnostics` and `DdnsSnap.Service.Ui`. Pipe names remain
> configurable through command JSON, `--pipe=...`, `--web-pipe=...`, and pipe
> slots.

本文是 AI 使用 `Iwesun.Runtime.Cli` 的操作技能。目标是用最少命令控制正在运行的 AIGateway，读取实时证据，并调用适配器功能。

## 触发条件

当任务涉及以下内容时使用本技能：

- RuntimeDiagnostics 管道。
- WebRuntime / WebView2 实时控制。
- `adapter.doubao-web` 等适配器 target。
- 通过 CLI 调用豆包上传、发送、读取 HTTP 事件。
- 不希望新增临时 trace 文件或控制台刷屏。

## 基本原则

1. 先确认服务运行，再发 CLI 命令。
2. 优先用默认配置和短命令。
3. WebRuntime 控制真实服务里的 WebView2，不另起测试浏览器。
4. RuntimeDiagnostics 调适配器函数，WebRuntime 抓页面 HTTP/DOM 证据。
5. 证据来自 JSON 返回和事件缓冲，不来自截图猜测。
6. 调试输出用完后执行 `quiet`。
7. DDNS Snap 的 CLI 命令模板放在 `config/Iwesun.Runtime.Cli.commands.json`；管道名由 CLI 命令 JSON、`--pipe=...`、`--web-pipe=...` 和 slots 管理。
8. UI 只通过 Service 管道读写 Service 业务配置；不要把 CLI 调试管道写进 `config.json`。

## 启动检查

服务推荐启动方式：

```powershell
dotnet src\AIGateway.Service\bin\Debug\net10.0-windows\win-x64\AIGateway.dll --headless --show-browser --no-ambient-input 11436
```

CLI 基本检查：

```powershell
src\Iwesun.Runtime.Cli\bin\Debug\net10.0\Iwesun.Runtime.Cli.exe help
src\Iwesun.Runtime.Cli\bin\Debug\net10.0\Iwesun.Runtime.Cli.exe status
```

如果只是控制网页：

```powershell
iwrt wstart doubao-web
iwrt wshow doubao-web
iwrt wurl doubao-web
```

## 常用工作流

### 1. 查看运行状态

```powershell
iwrt status
iwrt list
iwrt monitor
iwrt ui-info
```

需要诊断点：

```powershell
iwrt points --text=doubao
iwrt focus-text doubao
iwrt events-clean 100
iwrt quiet
```

停止当前连接的 DDNS Snap 宿主：

```powershell
iwrt host-exit
```

该命令走管理管道发送 `service.stop`，让宿主通过服务停止接口关闭。

### 2. 控制豆包页面

```powershell
iwrt wstart doubao-web
iwrt wshow doubao-web
iwrt wmon doubao-web --clear=true
iwrt we doubao-web 100
```

读取页面：

```powershell
iwrt wsnap doubao-web
iwrt wdisc doubao-web
iwrt wrx doubao-web <xpath>
```

点击页面：

```powershell
iwrt wcx doubao-web <xpath>
iwrt wmk doubao-web <x> <y>
```

只做明确点击。不要随机点击。服务启动时优先加 `--no-ambient-input`。

### 3. 调用豆包适配器发送附件

推荐先清事件：

```powershell
iwrt wmon doubao-web --clear=true
```

发送图片：

```powershell
iwrt dbimg --local_file_path=D:\AIG_FILE_2322.jpg --file_name=AIG_FILE_2322.jpg --prompt=read --mime_type=image/jpeg --timeout-ms=120000
```

发送 Markdown / 普通文件：

```powershell
iwrt dbimg --local_file_path=D:\AIG_FILE_2345.md --file_name=AIG_FILE_2345.md --prompt=marker --mime_type=text/markdown --timeout-ms=120000
```

当前 CLI 对带空格路径和带空格 prompt 有解析限制。测试时使用无空格路径和短 prompt，或走 raw JSON。

成功结果要看：

```text
success = true
value.storeUri / value.imageUri = tos-cn-...
value.content 包含模型回复
```

### 4. 抓 HTTP 证据

```powershell
iwrt we doubao-web 260
```

重点找：

```text
ApplyImageUpload
/upload/v1/
CommitImageUpload
/chat/completion
attachment_block
image.uri
file.uri
```

图片发送体：

```json
"attachments": [
  {
    "type": 1,
    "image": {
      "name": "AIG_FILE_2322.jpg",
      "uri": "tos-cn-i-a9rns2rl98/....jpg"
    }
  }
]
```

普通文件发送体：

```json
"attachments": [
  {
    "type": 3,
    "file": {
      "name": "AIG_FILE_2345.md",
      "uri": "tos-cn-i-ik7evvg4ik/....md",
      "size": 134
    }
  }
]
```

不要把 `blob:https://www.doubao.com/...` 当服务器引用。它只是输入框附件卡片/图标的本地预览地址。

## 判断标准

附件发送成功的最低标准：

- CLI 返回 `success:true`。
- WebRuntime 事件里有 `/chat/completion` 响应。
- 模型回复和附件内容有关。

严格标准：

- TOS `/upload/v1` 返回成功。
- `CommitImageUpload` 返回 `Result.Results[0].Uri`。
- `/chat/completion` request 里有 `attachment_block`。
- 图片用 `image.uri`，文件用 `file.uri`。

## 管道和记忆

默认记忆槽是 `aigateway`，默认管道是：

```text
N1.diagnosticsPipeName = AIGateway.RuntimeDiagnostics
N1.webRuntimePipeName  = AIGateway.WebRuntime
N1.managementPipeName  = AIGateway.UI
```

查看或修改记忆：

```powershell
iwrt mem
iwrt mem list
iwrt mem set backendId doubao-web
iwrt mem set count 100
```

设置管道：

```powershell
iwrt dpi AIGateway.RuntimeDiagnostics
iwrt wpi AIGateway.WebRuntime
iwrt mpi AIGateway.UI
```

## 扩展命令

新增常规命令优先修改：

```text
Iwesun.Runtime.Cli/Iwesun.Runtime.Cli.commands.json
```

新增适配器能力时，优先通过 RuntimeDiagnostics target 暴露：

```text
targetId = adapter.doubao-web
action   = uploadImageAttachment
```

然后在 CLI JSON 里添加一个短命令映射到该 target/action。

## 常见错误

| 错误 | 处理 |
| ---- | ---- |
| `Local file not found: D:\Git` | 路径被空格切断，用无空格路径或 raw JSON |
| 浏览器不显示 | 重启服务并加 `--show-browser` |
| 物理鼠标不能点 | 重启服务并加 `--no-ambient-input` |
| 事件被淹没 | `wmon --clear=true` 后复现，再 `we 100` |
| 返回成功但 uri 为空 | 检查代码是否从 `/chat/completion` request 解析附件引用 |
| 有上传无回复 | 查登录、人机验证、SSE、页面是否卡住 |

## 结束动作

完成调试后：

```powershell
iwrt quiet
```

报告时说明：

- 调用了哪个 CLI 命令。
- 返回的 target/action。
- 捕获到的服务器 `uri`。
- `/chat/completion` 里是 `image.uri` 还是 `file.uri`。
- 是否有模型回复。

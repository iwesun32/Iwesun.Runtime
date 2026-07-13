# Iwesun Runtime CLI v3

## 1. 定位

`iwrt` 是 Runtime 诊断与业务代理的命令客户端。CLI 文本不是协议；`RuntimeCliSystemConfig.json` 才是命令路由的权威配置，CLI 将命令编译为 `RuntimeDiagnosticFrame`，通过 4 字节 little-endian 长度前缀的 UTF-8 JSON 发送。

当前配置 schema 固定为：

```text
iwesun.runtime.cli/3.0
```

旧版 `commands.v2`、`baseCommands`、`compositeCommands`、`sw.*`、`bp.*`、`reg.*` 和 `--web-pipe` 均不再支持。

## 2. 管道与代理边界

- CLI 连接 RuntimeDiagnostics 控制入口。
- Runtime 同时统一管理 Management、WebRuntime 等业务专用管道。
- 每类业务使用独立物理管道，不与 RuntimeDiagnostics 混发业务消息。
- `RuntimePipeRegistry` 负责申请、重名避让、登记、解析、租约和释放。
- `RuntimeProxyCommandTarget` 根据登记租约，把统一 JSON frame 转发到目标专用管道。
- “专用管道”表示物理隔离，不表示脱离 Runtime 管理。

## 3. 安装位置

默认安装后：

```text
C:\Program Files\Iwesun\Runtime\bin\Iwesun.Runtime.Cli\iwrt.exe
C:\ProgramData\Iwesun\Runtime\config\RuntimeCliSystemConfig.json
```

安装器将 CLI 目录加入系统 `PATH`。已打开的终端不会自动刷新环境变量，需要重新打开终端。

## 4. 调用格式

```powershell
iwrt [--target=ALIAS | --server=SERVER --pipe=NAME] [--config=PATH] [--timeout-ms=15000] <command> [arguments]
```

全局参数：

| 参数 | 作用 |
| --- | --- |
| `--help` / `-h` | 显示当前目录生成的帮助 |
| `--config=PATH` | 显式指定 v3 命令 JSON |
| `--pipe=NAME` | 覆盖 diagnostics endpoint 的 RuntimeDiagnostics 管道名 |
| `--server=NAME` | 远程 Windows 节点名；必须与 `--pipe` 一起使用 |
| `--target=ALIAS` | 使用配置中的 Node/Target 插槽；不得与 `--server/--pipe` 混用 |
| `--timeout-ms=N` | 覆盖本次请求超时 |

命令参数支持三种等价形式：

```powershell
iwrt pipe.acquire runtime.worker
iwrt pipe.acquire -requestedPipeName runtime.worker
iwrt pipe.acquire -requestedPipeName:runtime.worker
```

规则：

- 命令参数使用单横线。
- 布尔值必须显式写 `true` 或 `false`。
- 位置参数和命名参数不得重复赋值。
- 未登记参数、缺少必需参数或类型错误都会返回结构化 JSON 错误。

## 5. 当前内置命令

### 5.1 宿主与生命周期

```powershell
iwrt host.info
iwrt host.events 100
iwrt lifecycle.status
iwrt lifecycle.shutdown true
```

### 5.2 诊断开关板

```powershell
iwrt switchboard.get
iwrt switchboard.enable
iwrt switchboard.enable worker
iwrt switchboard.disable worker
iwrt switchboard.disable
```

单独查询或控制输出点：

```powershell
iwrt switchboard.point.list
iwrt switchboard.point.enable sample.host.random
iwrt switchboard.point.disable sample.host.random
```

记录发布需要全局、section、output point 和输出通道同时启用。不存在的输出点会返回 `OUTPUT_POINT_NOT_FOUND`，不会静默成功。

诊断默认保持静默。完成观察后应恢复所启用的 section，并关闭全局开关。

### 5.3 断点

```powershell
iwrt breakpoint.list
iwrt breakpoint.enable my-product.worker.pause
iwrt breakpoint.resume my-product.worker.pause
iwrt breakpoint.disable my-product.worker.pause
```

CLI 连接和断开不改变断点状态。只有显式 `breakpoint.resume` 才恢复相应等待链。Release 不装配断点注入。

安装包包含独立的 Diagnostics Debug/Release DLL。需要调试断点的宿主必须引用 `lib/Iwesun.Runtime.Diagnostics/Debug` 版本；根目录和 `Release` 子目录版本均按生产策略裁掉断点。

### 5.4 登记与执行对象

```powershell
iwrt hook.list
iwrt registry.list
iwrt process.list
iwrt thread.list
iwrt task.list
```

### 5.5 专用管道

```powershell
iwrt pipe.list
iwrt pipe.acquire runtime.worker
iwrt pipe.acquire runtime.worker runtime.aggregate
iwrt pipe.resolve runtime.worker
```

申请返回 `Name / RequestedPipeName / ResolvedPipeName`。发生重名时，落地名称依次使用 `_001`、`_002` 等后缀。

### 5.6 文件与反射

```powershell
iwrt file.list
iwrt reflection.list
iwrt reflection.get runtime.execution
iwrt reflection.get runtime.execution Processes
iwrt reflection.invoke my-host.control GetSnapshot
```

`reflection.get` 只读取白名单属性/字段，不调用方法。Debug 宿主中的 `reflection.invoke <target> <member>` 只调用宿主在 `InvokableMembers` 中显式登记的无参数方法；未登记方法、带参数方法和未知目标均拒绝执行。Release Diagnostics 不编译 invoke 路由和执行代码，命令会返回不支持。CLI 不会扩大反射访问范围。

### 5.7 WebRuntime 代理

```powershell
iwrt web.programs.capabilities
iwrt web.programs.status
iwrt web.snapshot openai-web
iwrt web.navigate openai-web https://chatgpt.com/
iwrt web.mouse.click openai-web 640 480
iwrt web.keyboard.press openai-web Enter
iwrt web.keyboard.type openai-web "hello"
```

CLI 不直接连接 WebRuntime 管道。请求先进入 RuntimeDiagnostics，再由 `diagnostics.proxy` 根据 `RuntimePipeRegistry` 中的 `WebRuntime` 租约解析真实 `ResolvedPipeName`，转发标准 `RuntimeDiagnosticFrame`。代理参数固定采用 `module=WebRuntime / pipe=WebRuntime / targetId=<backend> / domain=web.runtime / proxyAction=<action>`；业务参数和 ProgramId 继续作为 typed Args 转发。

## 6. 配置结构

最小配置由以下根字段组成：

```json
{
  "schema": "iwesun.runtime.cli/3.0",
  "application": {},
  "endpoints": {},
  "commands": [],
  "composites": [],
  "extensions": {}
}
```

每个命令显式声明：

- `endpoint`
- `request.category`
- `request.operation`
- `request.domain`
- `request.target`
- `request.action`
- `parameters`

CLI 不根据命令名或 action 猜测协议字段。旧 schema 会返回 `CLI_CONFIG_SCHEMA_UNSUPPORTED` 或 `CLI_CONFIG_INVALID`，不会自动迁移。

当前发布版默认使用标准 v3 catalog，不预置用户别名或组合指令。使用 `--user-config=PATH` 可在标准 catalog 之上加载用户增量配置：`add` 新增命令、`extend` 为现有命令追加别名、`replace` 替换完整命令定义、`disable` 禁用命令。合并顺序固定为 `disable -> replace -> add -> extend`，冲突或未知目标会返回配置错误。

命令级帮助使用 `iwrt <command> --help`，显示 endpoint、参数、风险、Debug/Release 能力和示例。路由由纯 `iwesun.runtime.cli/3.0` 的 `RuntimeCliSystemConfig.json` 提供；帮助元数据位于 `RuntimeCliSystemMetadata.json`。Composite 默认仍只输出一个完整 JSON Frame。

```powershell
iwrt --user-config="C:\ProgramData\Iwesun\Runtime\config\Iwesun.Runtime.Cli.user.json" diagnostics.status
```

`RuntimeCliUserConfig.example.json` 是可复制模板。复制为当前目录的 `RuntimeCliUserConfig.json` 后会自动加载；显式 `--user-config=PATH` 优先并取代缺省文件。组合能力统一使用 `composites`。

## 上下文 Shell

使用 `iwrt shell` 或 `iwrt --interactive` 进入上下文模式。支持 `pwd/cd/ls/get/root` 统一 Runtime 虚拟路径，以及 `set/unset/vars/history/clear`。变量使用 `$name` 或 `${name}`。`exit`、`quit` 和 EOF 只退出 CLI Shell，不向宿主发送任何命令；停止宿主必须显式执行 `lifecycle.shutdown`。

多宿主和多节点调试使用 `node add/list/show/test/remove` 管理 Windows 节点，使用 `target add/list/show/test/use/current/remove` 管理节点上的管道插槽，或用 `@name command` 临时选择一次目标。CLI 不会猜测相似管道。`RuntimeCliUserConfig.json` 可通过 `nodes` 和 `targets[].node` 预置远程插槽。

Shell 在分派命令前统一展开全部 `$name`/`${name}` token，因此变量可用于本地命令参数、虚拟路径、Target 登记和 `@target`。普通命令与 `get/ls` 使用同一个当前 Target；一次性 `@target` 不改变当前 Target。

```text
node add atlas Atlas
target add atlas-ui diagnostics DdnsSnap.UI.RuntimeDiagnostics --node atlas
target use atlas-ui
host.summary
get /host
@atlas-ui runtime.inspect
```

本机连接超时、写入超时、响应超时与本地取消分别返回 `CLI_CONNECT_TIMEOUT`、`CLI_WRITE_TIMEOUT`、`CLI_RESPONSE_TIMEOUT` 和 `CLI_LOCAL_CANCELLED`。远程连接进一步区分 `CLI_REMOTE_ACCESS_DENIED`、`CLI_REMOTE_CREDENTIAL_CONFLICT`、`CLI_REMOTE_NODE_UNREACHABLE`、`CLI_REMOTE_PIPE_NOT_FOUND`、`CLI_REMOTE_CONNECT_TIMEOUT` 和 `CLI_REMOTE_PROTOCOL_ERROR`。错误数据包含 Node、serverName、实际管道、阶段、Windows 错误码和可重试性，不包含凭据。

CLI 复用当前 Windows 登录会话的 SMB/IPC 身份，不保存密码，也不自动注销 IPC 会话。首次认证由管理员使用 Windows Credential Manager、安全的 `net use \\Server\IPC$ /user:User *`，或 Shell 的 `node auth` 完成。服务端必须在 Program.cs 通过 `RuntimeNamedPipeAccessOptions` 授权对应 AI 账号；详见 [IWESUN_RUNTIME_REMOTE_ACCESS.md](IWESUN_RUNTIME_REMOTE_ACCESS.md)。

交互式 Shell 也提供 `node auth atlas --user DOMAIN\\User`，密码仅通过不可回显终端读取；输入被重定向时该命令拒绝执行。`node logout atlas --confirm` 会影响当前 Windows 登录会话中访问同一服务器的其他程序，因此必须显式确认。

只读多节点查询使用：

```powershell
iwrt --user-config=RuntimeCliUserConfig.json multi.query host.summary atlas-service,atlas-ui 4
```

并发数限制为 1–16；每个 Target 独立超时，结果保留各自完整 Frame。状态修改和破坏性命令在建立任何连接前返回 `CLI_MULTI_TARGET_READ_ONLY`。

`runtime.inspect` 使用轻量 `host.summary`，不默认展开程序集类型。`process.list/thread.list/task.list/pipe.list/file.list/reflection.list` 等目录命令使用默认 100、最大 500 的分页结果。

### 6.1 复合命令

`composites` 已实现：它将多条已有命令打包为一个协议 batch，一次发送并统一返回。每一步必须引用 catalog 中的现有命令；它不是 workflow，不支持条件、循环、结果绑定或脚本文本。

```json
{
  "name": "diagnostics.quick-check",
  "aliases": ["diag.quick"],
  "stopOnError": false,
  "steps": [
    { "command": "host.info" },
    { "command": "switchboard.get" },
    { "command": "host.events", "arguments": ["20"] }
  ]
}
```

同一复合命令中的步骤必须引用相同 endpoint。执行时直接使用 `iwrt diagnostics.quick-check`。

## 7. 返回与退出码

本地错误格式：

```json
{
  "schema": "iwesun.runtime.cli.result/1.0",
  "ok": false,
  "code": "CLI_PARAMETER_REQUIRED",
  "message": "Parameter 'requestedPipeName' is required.",
  "command": "pipe.acquire"
}
```

| 退出码 | 含义 |
| --- | --- |
| `0` | 成功 |
| `2` | 命令、选项或参数错误 |
| `3` | 配置错误 |
| `4` | 连接或传输错误 |
| `5` | 协议错误 |
| `6` | Runtime 执行失败 |
| `130` | 用户取消 |

Runtime 响应默认输出完整 frame，便于脚本继续组合处理。

## 8. 快速检查

```powershell
iwrt --help
iwrt --pipe=MyProduct.RuntimeDiagnostics host.info
iwrt --pipe=MyProduct.RuntimeDiagnostics pipe.list
```

如果命令执行失败，依次检查：

1. 宿主是否已经启动 RuntimeDiagnostics。
2. `--pipe` 是否指向正确控制管道。
3. JSON schema 是否为 `iwesun.runtime.cli/3.0`。
4. 命令是否存在于当前权威 catalog。
5. 目标业务专用管道是否已经登记并保持活动租约。

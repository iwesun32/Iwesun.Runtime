# Iwesun Runtime CLI v3 全面重构设计

## 1. 目标

CLI v3 将彻底删除 v2 配置、旧命令名、旧解析模型和文本组合步骤，重建为分层清晰、JSON 协议权威、可扩展且可严格验证的命令行客户端。

新版保留并规范化以下自定义能力：

- 用户别名。
- 用户单命令。
- 结构化组合工作流。
- endpoint 管道名和超时覆盖。
- 内置命令的安全扩展、完整替换和禁用。

## 2. 非目标

- 不保留 v2 schema 兼容层。
- 不保留旧命令名或旧默认别名。
- 不在 JSON 中建立通用脚本语言。
- 不允许组合步骤携带 CLI 命令行字符串。
- 不为 WebView2 维护第二套 CLI 分派器或协议。
- 不通过 action 名称推断 category、operation 或 domain。

## 3. 架构

```text
CLI Application
├── Bootstrap
│   ├── 启动参数
│   ├── 配置路径
│   └── 退出码
├── Configuration
│   ├── JSON 加载
│   ├── Schema 校验
│   ├── 用户合并
│   └── 全目录冲突校验
├── Syntax
│   ├── 分词
│   ├── 位置参数
│   └── 命名参数
├── Semantics
│   ├── CommandDescriptor
│   ├── ParameterDescriptor
│   └── SemanticCommand
├── Compilation
│   ├── 单命令 → rtdiag/2.0
│   └── 工作流 → rtdiag/3.0 batch
├── Transport
│   ├── endpoint 选择
│   ├── 4 字节 little-endian 长度前缀
│   ├── 连接、读写、超时与取消
│   └── 响应大小限制
└── Presentation
    ├── 完整 JSON frame
    ├── 可选人类摘要
    └── 稳定错误结构
```

每层只依赖其下一层的公开契约。`Program.cs` 只负责组合对象和返回退出码，不再容纳配置发现、管道实现和命令解析。

## 4. 权威 JSON 配置

默认文件名：`Iwesun.Runtime.Cli.commands.json`。

用户示例文件名：`Iwesun.Runtime.Cli.user.example.json`。

根结构：

```json
{
  "schema": "iwesun.runtime.cli/3.0",
  "application": {
    "name": "Iwesun Runtime CLI",
    "description": "Runtime diagnostics command client"
  },
  "endpoints": {},
  "commands": [],
  "workflows": [],
  "extensions": {}
}
```

CLI 只接受完全匹配的 `iwesun.runtime.cli/3.0`。缺少 schema、v2 整数版本、`baseCommands`、`compositeCommands` 或其他旧根字段均作为配置错误拒绝。

### 4.1 endpoints

```json
{
  "diagnostics": {
    "transport": "namedPipe",
    "pipeName": "DdnsSnap.RuntimeDiagnostics",
    "connectTimeoutMs": 5000,
    "requestTimeoutMs": 15000,
    "maxResponseBytes": 16777216
  }
}
```

endpoint 名在配置内唯一。v3 首版只允许 `namedPipe`，但传输层通过 `IRuntimeTransport` 保留新传输类型的代码扩展边界。

### 4.2 commands

```json
{
  "name": "pipe.acquire",
  "aliases": ["pipe.new"],
  "summary": "Acquire a dedicated runtime pipe",
  "endpoint": "diagnostics",
  "request": {
    "category": "instruction",
    "operation": "register",
    "domain": "diagnostics",
    "target": "diagnostics.pipes",
    "action": "acquire"
  },
  "parameters": [
    {
      "name": "requestedPipeName",
      "type": "string",
      "position": 0,
      "required": true
    },
    {
      "name": "aggregatePipeName",
      "type": "string",
      "position": 1,
      "required": false
    }
  ],
  "allowAdditionalArguments": false
}
```

命令名必须是小写 dot-style，由一个或多个仅含小写字母、数字和连字号的分段组成。别名使用同样的格式，且不得与任何命令名或别名冲突。

`request.category`、`operation`、`domain`、`target` 和 `action` 都必须显式提供，不进行文本推断。

### 4.3 parameters

支持类型：

- `string`
- `bool`
- `int32`
- `int64`
- `double`
- `json`

参数可声明：

- `position`
- `required`
- `default`
- `enum`
- `minimum` / `maximum`
- `minLength` / `maxLength`

同一 position 不得重复。必需参数不得依赖空默认值。默认值和枚举值必须在配置加载期就通过目标类型校验。

### 4.4 workflows

```json
{
  "name": "diagnostics.focus",
  "aliases": ["focus"],
  "summary": "Enable a diagnostics section and return its state",
  "parameters": [
    {
      "name": "section",
      "type": "string",
      "position": 0,
      "required": true
    }
  ],
  "execution": {
    "stopOnError": true,
    "deadlineMs": 30000
  },
  "steps": [
    {
      "id": "enable-global",
      "command": "switchboard.enable"
    },
    {
      "id": "enable-section",
      "request": {
        "category": "instruction",
        "operation": "enable",
        "domain": "switchboard",
        "target": "diagnostics.switchboard",
        "action": "enable",
        "arguments": {
          "section": { "$input": "section" }
        }
      }
    },
    {
      "id": "snapshot",
      "command": "switchboard.get",
      "when": {
        "step": "enable-section",
        "status": "ok"
      }
    }
  ]
}
```

`step.command` 是对已登记 v3 单命令的精确引用，不是 CLI 命令行。该字段不允许空格或参数文本。步骤也可直接声明完整结构化 `request`，但 `command` 和 `request` 必须且只能提供一个。

工作流支持：

- `$input`：引用工作流输入。
- `$step`：引用前置步骤的结果路径。
- `when`：基于前置步骤状态或结果的结构化条件。
- `continueOnError`：本步失败后是否继续。
- `delayMs`：本步执行前延迟。

工作流编译为一个 `rtdiag/3.0` batch frame，只使用一次管道连接。

### 4.5 extensions 与用户合并

用户配置中的扩展操作只允许：

- `add`：新增命令或工作流。
- `extend`：增加别名，或修改说明、参数默认值、endpoint 管道名和超时。
- `replace`：显式完整替换一个命令或工作流。
- `disable`：禁用指定命令或工作流。

`extend` 不得修改命令的 request 路由。需要修改 category、operation、domain、target 或 action 时必须使用 `replace`。

合并顺序为内置配置后用户配置。合并完成后必须重新执行全目录校验；任何重名、无效引用或类型矛盾都阻止 CLI 启动。

## 5. 命令命名规范

| 功能域 | 规范前缀 | 示例 |
| --- | --- | --- |
| 主机 | `host.*` | `host.info`、`host.events` |
| 生命周期 | `lifecycle.*` | `lifecycle.status`、`lifecycle.shutdown` |
| 开关板 | `switchboard.*` | `switchboard.get`、`switchboard.enable` |
| 断点 | `breakpoint.*` | `breakpoint.list`、`breakpoint.resume` |
| 钩子 | `hook.*` | `hook.list`、`hook.attach` |
| 登记表 | `registry.*` | `registry.list` |
| 进程 | `process.*` | `process.list`、`process.state.get` |
| 线程 | `thread.*` | `thread.list` |
| 任务 | `task.*` | `task.list` |
| 管道 | `pipe.*` | `pipe.acquire`、`pipe.resolve` |
| 文件 | `file.*` | `file.list`、`file.resolve` |
| 反射 | `reflection.*` | `reflection.get`、`reflection.invoke` |
| WebRuntime | `web.*` | `web.snapshot`、`web.navigate` |

`sw.*`、`bp.*`、`reg.*` 等旧命令不再作为内置命令或默认别名。用户仍可在 v3 配置中显式定义不冲突的自定义别名。

## 6. 调用语法

支持：

```powershell
iwrt pipe.acquire runtime.worker
iwrt pipe.acquire -requestedPipeName runtime.worker
iwrt pipe.acquire -requestedPipeName:runtime.worker
```

规则：

- 位置参数和命名参数可混用。
- 同一参数被位置语法和命名语法同时赋值时报重复参数错误。
- 命名参数只使用单横线。
- 布尔参数必须提供明确的 `true` 或 `false`，不存在缺值即 `true` 的规则。
- `json` 参数必须是合法 JSON。
- 不根据未知字符串自动猜测布尔、整数或浮点数。
- 未声明参数报错，除非命令显式设置 `allowAdditionalArguments: true`。

## 7. 协议编译

单命令编译为 `rtdiag/2.0` request frame。所有 header 语义来自配置和启动上下文，不从 action 字符串推断。

工作流编译为 `rtdiag/3.0` batch frame。步骤顺序、deadline、错误策略、条件和结果绑定由 Runtime 执行，CLI 只执行一次管道请求。

WebRuntime 命令与其他命令使用同一 `SemanticCommand` 和 `RuntimeDiagnosticFrame`，由 Diagnostics Proxy 转发，不使用 `--web-pipe` 或私有 CLI transport。

## 8. 错误结构与退出码

CLI 本地错误输出：

```json
{
  "schema": "iwesun.runtime.cli.result/1.0",
  "ok": false,
  "code": "CLI_PARAMETER_REQUIRED",
  "message": "Parameter 'requestedPipeName' is required.",
  "command": "pipe.acquire",
  "details": {
    "parameter": "requestedPipeName"
  }
}
```

错误码类别：

- `CLI_CONFIG_*`
- `CLI_COMMAND_*`
- `CLI_PARAMETER_*`
- `CLI_WORKFLOW_*`
- `CLI_ENDPOINT_*`
- `CLI_TRANSPORT_*`
- `CLI_PROTOCOL_*`
- `RUNTIME_*`

退出码：

| 退出码 | 含义 |
| --- | --- |
| 0 | 成功 |
| 2 | CLI 使用或参数错误 |
| 3 | 配置错误 |
| 4 | 连接或传输错误 |
| 5 | 协议错误 |
| 6 | Runtime 执行失败 |
| 130 | 用户取消 |

Runtime 返回的完整 frame 默认原样输出。人类可读摘要是可选 Presentation 模式，不替换机器可组合 JSON。

## 9. 源码组织

```text
Iwesun.Runtime.Cli/
├── Program.cs
├── Application/
│   ├── CliApplication.cs
│   ├── CliOptions.cs
│   └── CliExitCode.cs
├── Configuration/
│   ├── CliConfiguration.cs
│   ├── CliConfigurationLoader.cs
│   ├── CliConfigurationMerger.cs
│   └── CliConfigurationValidator.cs
├── Syntax/
│   ├── CliTokenizer.cs
│   └── CliInvocationParser.cs
├── Semantics/
│   ├── CommandDescriptor.cs
│   ├── ParameterDescriptor.cs
│   └── SemanticCommand.cs
├── Compilation/
│   ├── CommandCompiler.cs
│   └── WorkflowCompiler.cs
├── Transport/
│   ├── IRuntimeTransport.cs
│   └── NamedPipeRuntimeTransport.cs
├── Presentation/
│   ├── CliResult.cs
│   └── CliRenderer.cs
├── Iwesun.Runtime.Cli.commands.json
└── Iwesun.Runtime.Cli.user.example.json
```

## 10. 彻底删除清单

- v2 配置加载和版本兼容代码。
- `Iwesun.Runtime.Cli.commands.v2.json`。
- `Iwesun.Runtime.Cli.user.v2.json`。
- `Iwesun.Runtime.Cli.user.v2.example.json`。
- `baseCommands` / `compositeCommands` 模型。
- `CommandParser.cs` 的 Namespace / Verb / Target / RawTail 猜测模型。
- 组合步骤的 CLI 命令字符串。
- `condition`、`capture`、`mode` 等旧工作流字段。
- category 和 domain 文本推断。
- 未知参数自动类型猜测。
- `--web-pipe` 和 WebView2 私有 CLI 分派。
- `sw.*`、`bp.*`、`reg.*` 等旧内置命令和默认别名。
- 旧配置发现路径和 v2 文件名。

## 11. 兼容与发布策略

这是断层升级，不保留运行期兼容。发布产物必须把 CLI 可执行文件、`Iwesun.Runtime.Cli.commands.json` 和用户示例文件作为同一版本交付。

加载旧配置时不做迁移，返回 `CLI_CONFIG_SCHEMA_UNSUPPORTED` 并指明需要 `iwesun.runtime.cli/3.0`。

## 12. 验证规则

### 12.1 配置

- 内置 v3 配置成功加载。
- 缺少 schema、v2 schema 和旧根字段被拒绝。
- endpoint、命令名、别名、参数 position 和 workflow step ID 冲突被拒绝。
- `add`、`extend`、`replace`、`disable` 按规则合并。

### 12.2 语法与语义

- 位置、`-name value` 和 `-name:value` 成功。
- 重复、缺失、未知、类型错误和超出约束的参数返回稳定错误码。
- 别名与规范名编译为同一 SemanticCommand。

### 12.3 编译与传输

- 每个内置命令的 frame 字段与 JSON request 完全一致。
- workflow 使用一个 `rtdiag/3.0` batch 和一次管道连接。
- `$input`、`$step`、`when`、`continueOnError` 和 `delayMs` 正确编译。
- 部分读写、连接超时、请求超时、超大响应和管道中断返回对应错误码。

### 12.4 端到端

- SampleHost 所有 CLI 场景迁移到 v3 规范命令。
- 断点、数值断点、钩子、开关板、文件、管道、反射、生命周期和 WebRuntime Proxy 命令均有真实管道验证。
- CLI 失败时退出码与 JSON 错误码一致。
- Debug 和 Release 构建均为 0 错误。
- 源码、项目文件、文档和功能测试中不再引用 v2 配置名或已废弃命令。

## 13. 完成标准

CLI v3 完成必须同时满足：

1. 分层源码结构已落地，`Program.cs` 仅保留组合入口。
2. v2 代码、配置、命令名和文档痕迹已从活动路径删除。
3. v3 JSON 是命令和 workflow 的权威配置。
4. 别名、自定义命令和结构化组合工作流均可扩展。
5. 所有本地错误均使用稳定 JSON 结构和退出码。
6. 全量功能测试和 Debug / Release 构建通过。

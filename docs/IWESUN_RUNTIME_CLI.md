# Iwesun Runtime CLI v3

## 1. 定位

`iwrt` 是 Runtime 诊断与业务代理的命令客户端。CLI 文本不是协议；`Iwesun.Runtime.Cli.commands.json` 才是命令路由的权威配置，CLI 将命令编译为 `RuntimeDiagnosticFrame`，通过 4 字节 little-endian 长度前缀的 UTF-8 JSON 发送。

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
C:\ProgramData\Iwesun\Runtime\config\Iwesun.Runtime.Cli.commands.json
```

安装器将 CLI 目录加入系统 `PATH`。已打开的终端不会自动刷新环境变量，需要重新打开终端。

## 4. 调用格式

```powershell
iwrt [--pipe=NAME] [--config=PATH] [--timeout-ms=15000] <command> [arguments]
```

全局参数：

| 参数 | 作用 |
| --- | --- |
| `--help` / `-h` | 显示当前目录生成的帮助 |
| `--config=PATH` | 显式指定 v3 命令 JSON |
| `--pipe=NAME` | 覆盖 diagnostics endpoint 的 RuntimeDiagnostics 管道名 |
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
```

反射目标必须由宿主显式加入白名单，不允许通过 CLI 扩大可访问成员范围。

### 5.7 WebRuntime 代理

```powershell
iwrt web.snapshot backend-id
```

CLI 不直接连接 WebRuntime 管道。请求先进入 RuntimeDiagnostics，再由 Proxy 根据 Runtime 管道租约转发到 WebRuntime 专用管道。

## 6. 配置结构

最小配置由以下根字段组成：

```json
{
  "schema": "iwesun.runtime.cli/3.0",
  "application": {},
  "endpoints": {},
  "commands": [],
  "workflows": [],
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

当前发布版使用内置 v3 catalog。用户扩展示例属于后续完整扩展加载能力的配置模板；在 `--user-config`、结构化 workflow 和 `add/extend/replace/disable` 合并链完成前，不应将示例文件当作已自动加载的运行配置。

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

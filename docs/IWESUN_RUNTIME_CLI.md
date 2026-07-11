# Iwesun Runtime CLI

> **状态**: CURRENT | **最后更新**: 2026-07-09
> **源码参考**: `Iwesun.Runtime.Cli/`

`Iwesun.Runtime.Cli` 是连接正在运行的服务的统一命令行入口，用于实时诊断、快照查询、事件排空、登记表读取和 WebRuntime 控制命令。

> 规范：系统内置命令统一使用 **dot-style 标准命令名**（如 `lifecycle.status`）。默认模板仅保留 canonical 名称（`aliases: []`）；解释器仍保持可配置，用户可在 `Iwesun.Runtime.Cli.commands.v2.json` 自行添加 aliases、compositeCommands 和新命令。

## 定位

CLI 不直接拥有浏览器，也不直接实现业务 HTTP。它负责把类 PowerShell 命令语法解析为语义命令，再统一包装为 JSON Frame，通过命名管道调用运行中的服务。

```text
CLI → 语义命令 → RuntimeDiagnosticFrame(JSON) → RuntimeDiagnosticHub → 反射目标/适配器动作/开关板
CLI → WebRuntime 管道 → WebRuntimeControlRequest → WebView2Bridge → WebView2Session
```

CLI 的职责是"发令"，不是"解释业务"。

当前可用的诊断能力包括：
- `host.info` / `host.*`：主机信息与快照
- `reg.*`：登记表与 watch/break/hook 查询
- `bp.*`：断点列表、启用、恢复、数值断点绑定
- `process.*`：进程注册列表 + 进程反射内存变量读取
- `thread.*`：线程执行快照
- `lifecycle.*`：全局生命周期状态（Initialize/Running/Pause/Stop/Exit）与停机广播
- `unit.state.*`：进程/线程/任务共享状态与状态历史（DList 语义）
- `pipe.*`：分支管道申请、列表、解析、释放
- `web.runtime.*`：通过主监控代理访问 WebRuntime（模块/管道保护）
- `hook.*`：钩子列表、挂接、卸载
- `sw.*`：开关板状态、输出点、FIFO、pipe 配置

## 启动

DDNS Snap 的 CLI 运行配置文件：`config/Iwesun.Runtime.Cli.commands.v2.json`

管道名不写入 Service 的 `config.json`。CLI 调试通道只由 CLI 命令 JSON、命令行参数和 slots 管理，避免污染业务运行配置。

CLI 启动时配置优先级：
1. `--config=path`
2. `DDNSSNAP_CONFIG_DIRECTORY/Iwesun.Runtime.Cli.commands.v2.json`
3. 仓库根目录 `config/Iwesun.Runtime.Cli.commands.v2.json`
4. 程序输出目录旁 `Iwesun.Runtime.Cli.commands.v2.json`
5. 当前目录 `Iwesun.Runtime.Cli.commands.v2.json`
6. 嵌入默认配置

CLI 诊断管道名优先级（固定第一管道位）：
1. `--pipe=name`（启动参数覆盖）
2. `pipes.diagnostics`（JSON 配置中的第一管道位）
3. 默认值 `DdnsSnap.RuntimeDiagnostics`

说明：
- `pipes.diagnostics` 作为 CLI 诊断主通道固定保留，不与其他业务管道混用。
- 主程序起点页可写入初始值；用户可通过启动参数或 JSON 配置覆盖。

基本用法：

```powershell
iwrt help
iwrt status
```

常用全局参数：
- `--config=path`：指定命令配置 JSON（v2）
- `--pipe=name`：覆盖 RuntimeDiagnostics 管道名
- `--web-pipe=name`：覆盖 WebRuntime 管道名

DDNS Snap 默认管道：
- RuntimeDiagnostics：`DdnsSnap.RuntimeDiagnostics`
- WebRuntime：`AIGateway.WebRuntime`
- Management：`DdnsSnap.Service.Ui`

## 常用命令

### 诊断监控

```powershell
# 实时监控
iwrt monitor

# 查看状态
iwrt status

# 列出所有监控点
iwrt list

# 获取根快照
iwrt invoke service.root-snapshot GetSnapshot

# 列出指定 section 的监控点
iwrt points --section=agent-sync

# 查看单个监控点详情
iwrt point agent.worker-cycle

# 查看登记表
iwrt reg.list

# 查看断点
iwrt bp.list

# 查看数值断点绑定与支持的操作符
iwrt bp.listNumeric

# 绑定默认数值断点（只改比较符与常量）
iwrt bp.setNumericThreshold numeric.default.threshold gt 7
iwrt bp.setNumericThreshold numeric.default.range between 3 9

# 进程/线程/任务列表
iwrt process.list
iwrt thread.list
iwrt task.list

# 进程反射读写内存变量（按 unitId）
iwrt process.mem.get process.abc123 UnitId
iwrt process.mem.nav process.abc123 StartInfo.FileName
iwrt process.mem.set process.abc123 StateName Running

# 全局生命周期状态
iwrt lifecycle.get
iwrt lifecycle.set Running
iwrt lifecycle.shutdown 10000
iwrt lifecycle.status
iwrt lifecycle.broadcast Stop

# 单个单元状态与历史（DList 语义）
iwrt unit.state.get process.abc123
iwrt unit.state.history process.abc123 50
iwrt unit.state.transition process.abc123 Stop
iwrt unit.state.trytransition process.abc123 Pause
iwrt unit.state.subtask.append task.abc123 Working
iwrt unit.state.subtask.tryappend task.abc123 Running

# 反射登记表刷新与目标列表
iwrt reflection.refresh
iwrt reflection.list

# 分支专有管道注册与解析
iwrt pipe.acquire webview2-agent
iwrt pipe.list
iwrt pipe.resolve 1
iwrt pipe.release 1

# WebRuntime 模块管道 + 主监控代理调用
iwrt web.runtime.pipe.acquire web.runtime
iwrt web.runtime.capabilities web.runtime web.runtime doubao-web
iwrt web.runtime.snapshot web.runtime web.runtime doubao-web
iwrt web.runtime.events web.runtime web.runtime doubao-web 20 false
iwrt web.runtime.invoke web.runtime web.runtime doubao-web navigate url=https://example.com
iwrt web.runtime.navigate web.runtime web.runtime doubao-web https://example.com
iwrt web.runtime.cookie.get web.runtime web.runtime doubao-web www.example.com
iwrt web.runtime.cookie.set web.runtime web.runtime doubao-web www.example.com session abc123 /
iwrt web.runtime.cookie.clear web.runtime web.runtime doubao-web

# 代理模块白名单（配置层）
# 位置：diagnostic-switchboard.json
# 字段：proxyModuleWhitelistEnabled / proxyAllowedModules
# 默认：只允许 web.runtime

# 查看钩子
iwrt hook.list
```

### 快速聚焦

```powershell
# 一步打开指定监控点及其 section
iwrt focus-point agent.worker-cycle

# 打开匹配文本的唯一监控点
iwrt focus-text heartbeat

# 打开一个 section（不打开任何点）
iwrt focus-section agent-sync

# 打开一个 section 及其所有注册点
iwrt focus-section-points agent-sync

# 一步恢复静默模式
iwrt quiet
```

### 开关控制

说明：
- `sw.enable` / `sw.disable` 的可选参数是 `section` 名称，不是 `true/false`。
- `sw.pipe` / `sw.file` 必须传布尔参数（`true|false`）。

误用示例（错误 vs 正确）：
- 错误：`iwrt sw.enable true`；正确：`iwrt sw.enable` 或 `iwrt sw.enable runtime.diagnostics`
- 错误：`iwrt sw.file`；正确：`iwrt sw.file true`（或 `iwrt sw.file false`）

最短排障指令（确认 file 开关是否生效）：
- `iwrt sw.file true`
- `iwrt sw.list`（检查返回中的 `fileOutputEnabled` 是否为 `true`）

```powershell
iwrt pipe on
iwrt sw.enable runtime.diagnostics
iwrt sw.point.enable agent.worker-cycle
iwrt sw.enable
iwrt host.events 100
iwrt sw.point.disable agent.worker-cycle
iwrt sw.disable runtime.diagnostics
iwrt sw.disable
iwrt sw.pipe false

# 控制断点
iwrt bp.enable tree.fill
iwrt bp.resume tree.fill

# 控制钩子
iwrt hook.enable tree.updated
iwrt hook.disable tree.updated
```

### Service UI 管理

```powershell
iwrt ui-info
iwrt ui-status
iwrt host-exit
```

`host-exit` 通过 `DdnsSnap.Service.Ui` 发送 `service.stop`，由宿主进程收到响应后调用 Host 停止接口。

## 配置驱动命令

CLI 是配置驱动的正则命令解释器。除 `help`、`quit`、`exit` 等内置命令外，命令主要来自 JSON 配置：

```json
{
  "name": "web-events",
  "aliases": [ "we" ],
  "pattern": "^(?:we|web-events)\\s+(?<backendId>\\S+)(?:\\s+(?<count>\\d+))?(?<tail>.*)$",
  "transport": "webview2",
  "action": "events",
  "namedArgs": true
}
```

对数值断点，JSON 命令结构就是普通的 `baseCommands` 项：

```json
{
  "name": "bp.setNumericThreshold",
  "aliases": ["bp-set-numeric-threshold"],
  "usage": "bp.setNumericThreshold <id> <operator> <threshold1> [threshold2]",
  "transport": "diagnostics",
  "targetId": "diagnostics.breakpoints",
  "action": "setNumericThreshold",
  "params": {
    "id": { "type": "string", "position": 0, "required": true },
    "operator": { "type": "string", "position": 1, "required": true },
    "threshold1": { "type": "double", "position": 2, "required": true },
    "threshold2": { "type": "double", "position": 3, "required": false, "default": "0" }
  }
}
```

进程/线程相关的基本 list 命令也是同一结构：

```json
{
  "name": "process.list",
  "transport": "diagnostics",
  "targetId": "runtime.managed",
  "action": "processes",
  "params": {
    "count": { "type": "int", "position": 0, "required": false, "default": "100" }
  }
}
```

匹配规则：
- 精确命令优先
- 只匹配到一个命令时执行
- 匹配到多个且没有精确命令时报歧义，不执行
- 命令和别名大小写不敏感

## CLI 配置与调试建议

CLI 的命令模板文件位于 `config/Iwesun.Runtime.Cli.commands.v2.json`。当你需要增加新的调试命令时，优先复用已有的 `bp` / `reg` / `sw` 分类，再在配置中追加别名和正则规则。

对于数值断点，建议保持下面两类用法：
- **默认缺省绑定**：由宿主程序集上的 `DiagnosticNumericBreakpointAttribute` 提供
- **运行时改参**：通过 `bp setNumericThreshold` 只调整 `operator` 和 `threshold`

这样可以避免在 CLI 配置里引入复杂表达式，只保留简单、稳定、可回放的参数。

## 管道槽和记忆区

配置支持 16 个管道槽（N1...N16），默认 N1 对应 DDNS Snap。

记忆区允许命令将上次使用的 `backendId`、`targetId`、`xpath`、`count` 等写回，后续命令参数缺省时从记忆区读取。

```powershell
iwrt mem
iwrt mem list
iwrt mem set backendId doubao-web
iwrt mem set count 100
iwrt mem clear xpath
```

## WebView2 桥接

CLI 打包了 `Iwesun.Runtime.WebView2`，可与兼容主机的 WebRuntime 管道通信。DDNS Snap 当前不托管 WebView2 会话，但 CLI 可与运行中的 AIGateway WebRuntime 管道通信。

保持诊断管道命令和 WebRuntime 命令分离：诊断命令使用 `RuntimeDiagnosticFrame`，WebView2 命令使用 `WebRuntimeControlRequest`。当需要跨进程访问 WebRuntime 时，统一走 `web.runtime.*`，由主监控 `diagnostics.proxy` 目标转发到已登记分支管道。

## 相关文档

- 运行时诊断 → [RUNTIME_DIAGNOSTICS.md](RUNTIME_DIAGNOSTICS.md)
- 活跃需求与测试矩阵 → [REQUIREMENTS_ACTIVE.md](REQUIREMENTS_ACTIVE.md)
- 统一界面规范 → [UNIFIED_INTERFACE.md](UNIFIED_INTERFACE.md)

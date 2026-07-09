# Iwesun Runtime CLI

> **状态**: CURRENT | **最后更新**: 2026-07-08
> **源码参考**: `Iwesun.Runtime.Cli/`

`Iwesun.Runtime.Cli` 是连接正在运行的 DDNS Snap 服务的统一命令行入口，用于实时诊断、快照查询、事件排空和 WebRuntime 控制命令。

## 定位

CLI 不直接拥有浏览器，也不直接实现业务 HTTP。它负责把类 PowerShell 命令语法解析为语义命令，再统一包装为 JSON Frame，通过命名管道调用运行中的服务。

```text
CLI → 语义命令 → RuntimeDiagnosticFrame(JSON) → RuntimeDiagnosticHub → 反射目标/适配器动作/开关板
CLI → WebRuntime 管道 → WebRuntimeControlRequest → WebView2Bridge → WebView2Session
```

CLI 的职责是"发令"，不是"解释业务"。

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

```powershell
iwrt pipe on
iwrt enable agent-sync
iwrt enable-point agent.worker-cycle
iwrt enable
iwrt events-clean 100
iwrt disable-point agent.worker-cycle
iwrt disable agent-sync
iwrt disable
iwrt pipe off
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

匹配规则：
- 精确命令优先
- 只匹配到一个命令时执行
- 匹配到多个且没有精确命令时报歧义，不执行
- 命令和别名大小写不敏感

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

保持诊断管道命令和 WebRuntime 命令分离：诊断命令使用 `RuntimeDiagnosticFrame`，WebView2 命令使用 `WebRuntimeControlRequest`。

## 相关文档

- 运行时诊断 → [RUNTIME_DIAGNOSTICS.md](RUNTIME_DIAGNOSTICS.md)
- 统一界面原则 → [../04-interface/UNIFIED_INTERFACE.md](../04-interface/UNIFIED_INTERFACE.md)

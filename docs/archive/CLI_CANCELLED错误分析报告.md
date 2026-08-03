# CLI_CANCELLED 错误分析报告

> 日期：2026-07-13  
> 状态：根因已定位，等待 Runtime/CLI 组修正错误分类并同步文档  
> 涉及项目：Iwesun.Runtime.Cli、Iwesun.Runtime.Diagnostics、DDNS Snap Agent、运行时技能与文档  
> 本报告性质：静态源码分析 + 只读实机验证，不包含源码修改

## 1. 执行摘要

DDNS Snap 四宿主联调期间，使用以下命令访问 Agent：

```powershell
iwrt --pipe=DdnsSnap.Agent.RuntimeDiagnostics lifecycle.status
```

CLI 等待约 5 秒后返回：

```json
{
  "schema": "iwesun.runtime.cli.result/1.0",
  "ok": false,
  "code": "CLI_CANCELLED",
  "message": "Operation cancelled.",
  "command": null
}
```

当时 Agent 业务心跳仍正常，因此该现象一度被推断为“Agent 业务仍运行，但 Runtime Diagnostics Monitor 或退出控制面已经失效”。静态分析和正确管道复测证明，这个推断不成立。

### 已证实的直接根因

调用方使用了旧管道名：

```text
DdnsSnap.Agent.RuntimeDiagnostics
```

Agent 当前源码和 DDNS Snap CLI 配置使用的真实管道名是：

```text
DdnsSnap.Agent.Server.RuntimeDiagnostics
```

不存在的旧管道触发 CLI 的 5000 ms 连接超时。CLI 将连接超时产生的 `OperationCanceledException` 统一转换成 `CLI_CANCELLED`，导致错误信息看起来像用户主动取消或宿主生命周期取消。

### 已证实的 CLI 次生缺陷

CLI 将以下不同原因统一折叠为 `CLI_CANCELLED`：

- 调用方 CancellationToken 被取消；
- 命名管道连接超时；
- 请求读写超时；
- 其他传播到顶层的 `OperationCanceledException`。

错误码无法表达失败阶段、目标管道、超时值和可重试性，使一个普通的“管道名错误/目标不存在”被误诊为 Agent 生命周期故障。

## 2. 影响范围

### 2.1 已造成的调试影响

1. Agent 被错误认定为诊断控制面失效。
2. Agent 安全退出被错误认定为不可靠。
3. 一次 Debug Agent 因错误管道无法发送协调退出，最终使用了进程终止来释放 DLL。
4. 多宿主退出隔离和共享生命周期残留被列为高优先级怀疑方向，增加了无效排查范围。
5. 原四宿主联调意见书中写入了尚未成立的 Agent 生命周期结论。

### 2.2 潜在通用影响

任何以下情况都可能被 CLI 错误显示为 `CLI_CANCELLED`：

- 管道拼写错误；
- 宿主尚未启动；
- 宿主实际使用了冲突避让后的管道名；
- 命名管道 ACL 拒绝或连接无法完成；
- 服务端已连接但没有在请求期限内返回；
- 用户按 Ctrl+C；
- 上层自动化取消命令；
- Composite 总期限到达并向外传播取消。

这些场景的责任方、重试策略和修复动作完全不同，不应共享同一个错误码。

## 3. 环境与对象

### 3.1 Agent 进程

实机验证时：

```text
ProcessName: DdnsSnap.Agent
ProcessId:   52152
BaseDirectory: D:\Git Space\Ddns Snap\src\DdnsSnap.Agent\bin\Debug\net10.0\
```

Agent 持续向 Service 上报心跳，证明业务 Worker 与 HTTP 心跳链仍在运行。

### 3.2 CLI 配置的超时

Runtime 系统 CLI 配置：

```json
"diagnostics": {
  "transport": "namedPipe",
  "pipeName": "DdnsSnap.RuntimeDiagnostics",
  "connectTimeoutMs": 5000,
  "requestTimeoutMs": 60000,
  "maxResponseBytes": 16777216
}
```

DDNS Snap 项目 CLI 配置的 Agent endpoint：

```json
"agent": {
  "transport": "namedPipe",
  "pipeName": "DdnsSnap.Agent.Server.RuntimeDiagnostics",
  "connectTimeoutMs": 5000,
  "requestTimeoutMs": 15000,
  "maxResponseBytes": 16777216
}
```

错误调用约 5 秒返回，与 `connectTimeoutMs=5000` 完全吻合，而不是 15 秒或 60 秒请求超时。

## 4. 完整证据链

### 4.1 Agent 源码声明的管道

文件：`D:\Git Space\Ddns Snap\src\DdnsSnap.Agent\Program.cs`

```csharp
builder.Services.StartWindowsService(
    // ...
    startupRuntimeDiagnosticsPipeName: "DdnsSnap.Agent.Server.RuntimeDiagnostics");
```

这是一手权威来源。Agent 通过 Runtime 标准 Windows Service 启动入口创建诊断宿主。

### 4.2 DDNS Snap CLI 配置使用正确名称

文件：`D:\Git Space\Ddns Snap\config\Iwesun.Runtime.Cli.commands.json`

```json
"agent": {
  "transport": "namedPipe",
  "pipeName": "DdnsSnap.Agent.Server.RuntimeDiagnostics"
}
```

配置还提供了以下标准命令：

- `agent.status`
- `agent.tasks`
- `agent.shutdown`
- `agent.inspect`
- `agent.safe-exit`

因此 DDNS Snap 项目已经具备正确端点配置，问题发生在调试时绕过项目配置、手工使用了旧管道名。

### 4.3 旧技能使用错误名称

文件：`C:\Users\LYH\.codex\skills\ddnssnap-multiprocess-runtime-debug\SKILL.md`

其中仍写有：

```text
DdnsSnap.Agent.RuntimeDiagnostics
iwrt --pipe=DdnsSnap.Agent.RuntimeDiagnostics host.list
```

该技能同时以 CLI v2 为主要描述，与实际 CLI v3 和当前四宿主接入状态存在漂移。错误管道名直接影响了 AI 的调试决策。

### 4.4 Runtime 文档也保留旧名称

至少以下文件出现旧名称：

- `docs/RUNTIME_无配置文件整改方案.md`
- `docs/DDNS_Snap四宿主运行时联调改进意见书.md`

原意见书的四宿主管道表将 Agent 写成 `DdnsSnap.Agent.RuntimeDiagnostics`，因此后续按表复现必然再次产生同样的 `CLI_CANCELLED`。

### 4.5 正确管道实机验证成功

以下命令成功返回：

```powershell
iwrt --pipe=DdnsSnap.Agent.Server.RuntimeDiagnostics lifecycle.status
iwrt --pipe=DdnsSnap.Agent.Server.RuntimeDiagnostics host.info
```

`host.info` 返回：

```text
ProcessName: DdnsSnap.Agent
ProcessId: 52152
RegisteredTargets:
  agent.config-store
  agent.heartbeat
  agent.pipe
  agent.server-addresses
  agent.worker
  diagnostics.monitor
  diagnostics.switchboard
  runtime.managed
  runtime.root
  runtime.state
```

这证明：

- Agent Runtime Diagnostics Monitor 正常监听；
- 管道 ACL 正常；
- Hub 路由正常；
- `runtime.managed` 生命周期目标正常；
- Agent 业务运行与诊断宿主没有发生先前推测的分裂。

### 4.6 使用项目配置验证成功

```powershell
iwrt --config="D:\Git Space\Ddns Snap\config\Iwesun.Runtime.Cli.commands.json" agent.status
```

返回成功 Frame：

```json
{
  "status": {
    "ok": true,
    "code": "OK",
    "message": "success"
  },
  "data": {
    "status": {
      "CanExit": true,
      "TimedOut": false,
      "RecommendedExitCode": 0,
      "ExitDeadlineUtc": null,
      "PendingUnits": []
    },
    "result": null
  }
}
```

## 5. CLI 源码调用链

文件：`Iwesun.Runtime.Cli/CliV3.cs`

### 5.1 连接阶段

```csharp
using var connect = CancellationTokenSource.CreateLinkedTokenSource(ct);
connect.CancelAfter(connectTimeoutMs);
await pipe.ConnectAsync(connect.Token);
```

当目标管道不存在时，`ConnectAsync` 等待至 `connectTimeoutMs`，然后因 `connect.Token` 取消而抛出 `OperationCanceledException`。

### 5.2 请求阶段

连接成功后创建第二个令牌：

```csharp
using var request = CancellationTokenSource.CreateLinkedTokenSource(ct);
request.CancelAfter(requestTimeoutMs);
```

该令牌同时用于：

- 写四字节长度头；
- 写 JSON 请求体；
- Flush；
- 读四字节响应头；
- 读完整响应体。

请求发送、服务端执行和响应读取中的任何超时都会产生相同异常类型。

### 5.3 顶层错误折叠

```csharp
catch (OperationCanceledException)
{
    RenderFailure(new CliException("CLI_CANCELLED", "Operation cancelled.", 130));
    return 130;
}
```

这里没有检查：

- 原始调用方令牌 `ct` 是否取消；
- `connect` 是否超时；
- `request` 是否超时；
- 当前处于连接、写入还是读取阶段；
- 目标管道名称；
- 配置的超时值。

因此丢失了根因所需的全部判别信息。

## 6. Runtime 服务端静态分析

### 6.1 `lifecycle.status` 不存在业务等待

CLI 路由为：

```text
target: runtime.managed
action: lifecycle.status
```

`RuntimeManagedCommandTarget.ExecuteAsync` 对该 action 直接同步返回：

```csharp
return Task.FromResult(RuntimeDiagnosticActionResult.Ok(
    TargetId,
    command.Action,
    new {
        status = _shutdownCoordinator.Snapshot(),
        result = _shutdownCoordinator.LastResult
    }));
```

`RuntimeManagedRegistry.ImportSharedStates()` 当前为空实现。`Snapshot()` 只枚举进程内登记，不包含网络等待。因此在正确管道下，`lifecycle.status` 没有合理路径稳定阻塞 5 秒。

### 6.2 Monitor 使用 Host stoppingToken

`RuntimeDiagnosticsMonitor`：

1. 创建 `NamedPipeServerStream`；
2. `WaitForConnectionAsync(stoppingToken)`；
3. 读取请求；
4. 调用 `_hub.ExecuteFrameAsync(requestFrame, ct)`；
5. 写响应后断开该客户端。

正确管道的 `host.info` 和 `lifecycle.status` 均成功，说明 Monitor 的 `stoppingToken` 未被错误取消。

### 6.3 当前没有证据支持的旧假设

本次问题不支持以下结论：

- Agent Monitor 已停止但业务仍运行；
- Agent 继承了其他宿主的全局 Stop；
- Agent lifecycle target 被业务锁阻塞；
- Service shutdown 连带取消了 Agent 控制面；
- Agent 的共享内存残留导致所有 CLI 请求取消。

这些问题可以作为其他独立测试课题，但不能再用本次 `CLI_CANCELLED` 作为证据。

## 7. 根因树

```text
CLI_CANCELLED
  └─ OperationCanceledException 被顶层统一捕获
      ├─ 本次实际路径：ConnectAsync 超时
      │   └─ 目标管道不存在
      │       └─ 使用旧名称 DdnsSnap.Agent.RuntimeDiagnostics
      │           ├─ 已安装技能仍写旧名称
      │           ├─ Runtime 历史文档仍写旧名称
      │           └─ 四宿主意见书复制了旧名称
      ├─ 其他可能路径：请求写入超时
      ├─ 其他可能路径：响应读取超时
      └─ 其他可能路径：用户或上层主动取消
```

### 主根因

端点名称在源码、项目配置、技能和文档之间发生漂移，调试者使用了非权威旧名称。

### 放大因素

CLI 将连接超时伪装为通用取消，缺少阶段化错误码和诊断字段。

## 8. 建议修复方案

## 8.1 CLI：按取消来源分类

建议不要在 `RunAsync` 顶层统一处理全部 `OperationCanceledException`。`SendAsync` 应在各阶段建立明确异常边界。

建议错误码：

| 阶段 | 建议错误码 | 退出码 | Retryable |
|---|---|---:|---|
| 调用方主动取消 | `CLI_LOCAL_CANCELLED` | 130 | false |
| 连接目标管道超时 | `CLI_CONNECT_TIMEOUT` | 4 | true |
| 请求写入超时 | `CLI_WRITE_TIMEOUT` | 4 | true |
| 等待响应超时 | `CLI_RESPONSE_TIMEOUT` | 4 | true |
| 响应中途断管 | `CLI_PROTOCOL_TRUNCATED` | 5 | 视情况 |
| 管道不存在/无法连接 | `CLI_PIPE_UNAVAILABLE` | 4 | true |

最低限度也应区分：

- `CLI_CANCELLED`
- `CLI_CONNECT_TIMEOUT`
- `CLI_REQUEST_TIMEOUT`

### 判别原则

```text
if 外部 ct 已取消:
    CLI_LOCAL_CANCELLED
else if connect CTS 已取消:
    CLI_CONNECT_TIMEOUT
else if request CTS 已取消:
    CLI_REQUEST_TIMEOUT
else:
    CLI_TRANSPORT_FAILURE
```

### 建议错误数据

结构化结果至少包含：

```json
{
  "code": "CLI_CONNECT_TIMEOUT",
  "message": "Timed out connecting to named pipe.",
  "data": {
    "transport": "namedPipe",
    "pipeName": "DdnsSnap.Agent.RuntimeDiagnostics",
    "phase": "connect",
    "timeoutMs": 5000,
    "elapsedMs": 5004,
    "retryable": true
  }
}
```

不得输出密钥、令牌或其他业务敏感参数。

## 8.2 CLI：连接前提供端点可诊断性

可选增强：

- `pipe.resolve`/`pipe.list` 返回当前登记的 Requested/Resolved 名称；
- 连接失败提示用户运行 `pipe.list` 或检查 `host.info`；
- 使用项目 config 时，在错误结果中返回 endpoint 名称；
- Shell 目标上下文显示目标别名、请求管道和最近确认的 ProcessId。

CLI 不应自动猜测或连接“第一个相似管道”，避免误操作其他宿主。

## 8.3 DDNS Snap：统一权威管道常量

建议由 DDNS Snap 在共享常量中定义四个诊断管道名，并由以下位置共同引用或生成：

- Service Program；
- Agent Program；
- Service UI Startup；
- Agent UI Startup；
- 项目 CLI config；
- 调试脚本；
- 项目技能和文档。

如果 JSON 和 Markdown 无法编译期引用，应由一个发布脚本从同一清单生成，并在 CI 中做字符串一致性检查。

## 8.4 文档和技能纠偏

需要同步修正：

1. 已安装 `ddnssnap-multiprocess-runtime-debug` 技能；
2. 仓库内对应技能源；
3. `docs/RUNTIME_无配置文件整改方案.md`；
4. `docs/DDNS_Snap四宿主运行时联调改进意见书.md`；
5. 任何仍使用 v2 命令或旧 Agent 管道名的示例；
6. 安装版 Runtime docs/skills 副本。

原意见书应明确追加纠偏结论：本次 Agent `CLI_CANCELLED` 已证实为旧管道名导致的连接超时，不能继续作为跨宿主退出耦合的证据。

## 9. 最小实施建议

为降低改动风险，建议分两步：

### 第一步：CLI 错误分类

只修改 `CliV3.SendAsync` 和顶层异常映射：

- 连接超时返回 `CLI_CONNECT_TIMEOUT`；
- 请求超时返回 `CLI_REQUEST_TIMEOUT`；
- 外部取消保留 `CLI_CANCELLED` 或改名 `CLI_LOCAL_CANCELLED`；
- 返回 pipe、phase、timeoutMs。

该步骤不改变线协议、命令 catalog 或 Runtime Monitor。

### 第二步：端点一致性检查

- 修正文档和技能；
- 增加四宿主管道名称一致性测试；
- 项目调试默认使用 `--config` 下的语义命令，不再手写管道名。

## 10. 回归测试矩阵

### 10.1 CLI 单元/功能测试

| 场景 | 操作 | 预期 |
|---|---|---|
| 正确管道 | 连接 SampleHost | `OK` |
| 不存在管道 | 随机 GUID 管道名 | `CLI_CONNECT_TIMEOUT`，包含 pipe/5000ms |
| 外部取消 | 连接等待期间取消外部 token | `CLI_LOCAL_CANCELLED`，退出码 130 |
| 连接后不响应 | 测试服务端接受连接但不回包 | `CLI_RESPONSE_TIMEOUT` |
| 写后断管 | 服务端读取请求后关闭 | `CLI_PROTOCOL_TRUNCATED` 或明确断管错误 |
| 超大响应 | 服务端声明超预算长度 | `CLI_PROTOCOL_RESPONSE_LENGTH` |
| 正确 Composite | 单端点 batch | 完整 `batchResult` |
| Composite deadline | 某步骤超时 | `DEADLINE_EXCEEDED`，不得变成顶层 `CLI_CANCELLED` |

### 10.2 DDNS Snap 四宿主测试

| 宿主 | 正确管道 |
|---|---|
| Service | `DdnsSnap.Service.RuntimeDiagnostics` |
| Agent | `DdnsSnap.Agent.Server.RuntimeDiagnostics` |
| Service UI | `DdnsSnap.UI.RuntimeDiagnostics` |
| Agent UI | `DdnsSnap.Agent.UI.RuntimeDiagnostics` |

每个宿主验证：

1. `host.info` 返回正确 ProcessName/ProcessId；
2. `lifecycle.status` 成功；
3. `runtime.inspect` 成功且实际目标身份正确；
4. Debug 反射目标与宿主匹配；
5. 使用错误管道时返回连接类错误，而不是生命周期类错误；
6. 使用正确管道执行独立 `lifecycle.shutdown`，确认安全退出行为。

### 10.3 文档一致性测试

对源码、CLI config、技能和文档提取四个固定管道名，要求集合完全一致。发现以下任一字符串即失败：

```text
DdnsSnap.Agent.RuntimeDiagnostics
```

除非它仅出现在迁移说明或本错误报告的“旧名称”上下文中。

## 11. 验收标准

### CLI

- 不存在管道稳定返回 `CLI_CONNECT_TIMEOUT`，不返回 `CLI_CANCELLED`。
- 用户 Ctrl+C 或调用方 token 取消才返回本地取消错误。
- 连接和请求超时分别携带 phase、pipeName、timeoutMs、elapsedMs。
- 所有错误仍保持单个合法 JSON 结果。
- Composite 内部 deadline 不泄漏为顶层通用取消。

### DDNS Snap

- 四个宿主的真实管道名在源码、项目 config、技能和文档中一致。
- `agent.status` 使用项目 config 可直接成功。
- Agent 安全退出必须用正确管道重新验证；验证前不得继续引用旧失败结论。

### 文档与发布

- Runtime 安装版不再包含 CLI v2 操作步骤。
- 已安装技能与仓库技能字节级一致。
- 四宿主意见书完成纠偏，不把错误端点结果当作 Runtime 生命周期故障证据。

## 12. 风险与兼容性

### 错误码兼容

若已有自动化依赖 `CLI_CANCELLED`：

- 可在一个版本内保留 `category: cancelled`，同时增加具体 `code`；
- 或提供 `legacyCode: CLI_CANCELLED`；
- 不建议继续让连接超时使用退出码 130，因为 130 通常表示用户中断。

### 安全边界

错误响应可以返回目标管道名，但不得返回：

- 密钥或 token；
- 完整用户配置内容；
- 业务命令敏感参数；
- 未显式登记的反射信息。

### 自动纠错风险

CLI 不应在连接失败后自动尝试相似名称。多宿主环境中，相似管道可能属于另一进程，自动连接会把只读或破坏性命令发送给错误目标。

## 13. 最终结论

本次 `CLI_CANCELLED` 由两个问题叠加形成：

1. **业务接入资料错误**：技能和部分文档使用了已过期的 Agent 管道名，调试命令连接了不存在的 endpoint。
2. **CLI 可诊断性不足**：连接超时被统一翻译为 `CLI_CANCELLED`，掩盖了真正的传输阶段和目标信息。

Agent Runtime Diagnostics Monitor、Hub 和 `lifecycle.status` 已通过正确管道验证正常。本次证据不能证明 Agent 存在跨宿主退出耦合或业务生命周期分裂。

建议 Runtime/CLI 组优先完成 CLI 错误分类；DDNS Snap/技能维护侧同步修正管道清单。两项同时完成后，同类问题可以在第一次命令失败时直接定位，而不再进入业务退出链和共享状态的误排查。

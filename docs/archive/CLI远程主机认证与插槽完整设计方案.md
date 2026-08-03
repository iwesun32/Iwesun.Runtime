# CLI 远程主机认证与插槽完整设计方案

## 1. 目标

在不改变现有 Runtime 命令目录、Frame 协议和管道插槽语义的前提下，使一个 CLI 同时管理本机与多台远程 Windows 主机。

设计分为两个独立概念：

- **主机（host）**：负责主机名解析、Windows 身份认证和 IPC 会话。
- **插槽（target）**：负责 endpoint 与具体 pipeName，只增加一个 `host` 头引用主机。

一台主机认证一次，该主机下所有插槽复用同一 Windows 登录会话。密码不进入 Runtime Frame，也不进入 CLI 配置。

## 2. 配置模型

```json
{
  "schema": "iwesun.runtime.cli/3.0",
  "hosts": {
    "local": {
      "serverName": "."
    },
    "atlas": {
      "serverName": "Atlas",
      "credentialTarget": "Atlas"
    },
    "selene": {
      "serverName": "192.168.32.10",
      "credentialTarget": "Selene"
    }
  },
  "targets": {
    "service": {
      "host": "local",
      "endpoint": "diagnostics",
      "pipeName": "DdnsSnap.Service.RuntimeDiagnostics"
    },
    "atlas-service": {
      "host": "atlas",
      "endpoint": "diagnostics",
      "pipeName": "DdnsSnap.Service.RuntimeDiagnostics"
    },
    "atlas-ui": {
      "host": "atlas",
      "endpoint": "diagnostics",
      "pipeName": "DdnsSnap.UI.RuntimeDiagnostics"
    }
  }
}
```

### 2.1 Host 字段

| 字段 | 必需 | 含义 |
|---|---:|---|
| `serverName` | 是 | `NamedPipeClientStream` 的服务器参数；本机使用 `.` |
| `credentialTarget` | 否 | Windows 凭据管理器或 IPC 会话使用的稳定名称 |
| `connectTimeoutMs` | 否 | 主机级默认连接超时 |

Host 配置禁止保存用户名密码。用户名可以作为一次命令参数或非敏感默认提示值，但密码只能安全交互输入。

### 2.2 Target 字段

| 字段 | 必需 | 含义 |
|---|---:|---|
| `host` | 否 | Host 别名；缺省为 `local` |
| `endpoint` | 是 | Runtime 命令目录 endpoint |
| `pipeName` | 是 | 纯管道名，不允许 UNC |
| `connectTimeoutMs` | 否 | 覆盖 Host/endpoint 的连接超时 |
| `requestTimeoutMs` | 否 | 请求响应超时 |
| `maxResponseBytes` | 否 | 最大响应大小 |

旧配置没有 `host` 时自动绑定 `local`，保持兼容。

## 3. Windows 认证模型

推荐每台受管机器建立：

- 本地低权限账户：`IwesunAiDiag`
- 本地组：`Iwesun Runtime Operators`
- 将 `IwesunAiDiag` 加入该组

Runtime 创建 Diagnostics 管道时：

- SYSTEM、NetworkService、Administrators：完全控制
- Iwesun Runtime Operators：读写和连接
- Anonymous、Guest：不授权

认证通过 Windows SMB/IPC 与 LSA 完成。CLI 不实现密码校验，不把密码放进 Runtime 协议。

## 4. Shell 命令

### 4.1 主机管理

```text
host add atlas Atlas
host list
host show atlas
host auth atlas --user Atlas\IwesunAiDiag
host test atlas
host logout atlas
host remove atlas
```

行为：

- `host add <alias> <serverName>`：登记主机，不执行认证。
- `host auth <alias> --user <user>`：打开安全密码输入并建立该主机的 IPC 会话。
- `host test <alias>`：只读检查名称解析、445、IPC 会话和认证身份。
- `host logout <alias>`：确认后断开该主机 IPC 会话，不删除 Host。
- `host remove <alias>`：若仍有 target 引用则拒绝，并列出引用者。

`host auth` 不得把密码回显或写入 Shell history。若终端不支持安全输入，应拒绝执行并提示使用 Windows Credential UI，不能降级为明文输入。

### 4.2 插槽管理

```text
target add atlas-service diagnostics DdnsSnap.Service.RuntimeDiagnostics --host atlas
target add atlas-ui diagnostics DdnsSnap.UI.RuntimeDiagnostics --host atlas
target list
target show atlas-service
target use atlas-service
target current
target test atlas-service
target remove atlas-service
```

也可支持更短的等价位置语法：

```text
target add atlas-service atlas diagnostics DdnsSnap.Service.RuntimeDiagnostics
```

帮助和错误信息必须始终显示字段顺序，避免把 host、endpoint、pipeName 混淆。建议文档以显式 `--host` 形式为主。

### 4.3 使用插槽

```text
target use atlas-service
host.summary
reflection.get service.root-snapshot

@atlas-ui host.summary
@atlas-service runtime.inspect
```

`@target command` 只临时选择一次插槽，不修改 Shell 当前插槽。

### 4.4 Host 上下文

可选支持主机上下文，以便连续登记同一主机的多个插槽：

```text
host use atlas
target add service diagnostics DdnsSnap.Service.RuntimeDiagnostics
target add ui diagnostics DdnsSnap.UI.RuntimeDiagnostics
```

生成的真实 target 名仍应明确，例如 `atlas:service`、`atlas:ui`。Host 上下文不能替代 Target 上下文；业务命令最终必须解析到唯一 target。

## 5. 非交互单行命令

单行模式必须覆盖 Shell 的全部远程能力，便于 AI、脚本和 CI 使用。

### 5.1 使用已配置插槽

```powershell
iwrt --target atlas-service host.summary
iwrt @atlas-service host.summary
```

推荐标准形式为 `--target`；`@target` 是交互友好缩写。

### 5.2 临时远程插槽

不写配置时，可以在单行命令中临时组成插槽：

```powershell
iwrt --host Atlas --pipe DdnsSnap.Service.RuntimeDiagnostics host.summary
```

这里的 `--host` 是临时 serverName，不是 Host 别名。为避免歧义，也可采用更明确形式：

```powershell
iwrt --server Atlas --pipe DdnsSnap.Service.RuntimeDiagnostics host.summary
```

建议最终保留：

- `--target atlas-service`：引用配置插槽
- `--server Atlas --pipe ...`：创建一次性临时插槽

禁止同时传 `--target` 与 `--server/--pipe`，冲突时返回参数错误。

### 5.3 单行认证

交互式人工终端：

```powershell
iwrt host.auth atlas --user Atlas\IwesunAiDiag
```

该命令允许弹出安全密码输入。认证成功后退出，后续任意 CLI 进程复用 Windows IPC 会话。

AI 或非交互脚本不得携带密码。必须提前由管理员建立凭据：

```powershell
iwrt host.test atlas
iwrt --target atlas-service host.summary
```

如果未认证，返回 `CLI_REMOTE_AUTH_REQUIRED`，不得弹出无限等待的密码窗口。

### 5.4 单行注销

```powershell
iwrt host.logout atlas
```

注销影响当前 Windows 登录会话中访问同一主机的其他进程。非交互模式必须增加显式确认：

```powershell
iwrt host.logout atlas --confirm
```

## 6. 连接解析

执行 `--target atlas-service host.summary` 时：

```text
target atlas-service
  -> host atlas
  -> serverName Atlas
  -> endpoint diagnostics
  -> pipeName DdnsSnap.Service.RuntimeDiagnostics
  -> NamedPipeClientStream("Atlas", "DdnsSnap.Service.RuntimeDiagnostics", ...)
```

绝对禁止拼成 `\\Atlas\pipe\DdnsSnap.Service.RuntimeDiagnostics` 后作为 pipeName 传给本地连接构造器。

解析后形成不可变的 `ResolvedRuntimeTarget`：

```text
Alias
HostAlias
ServerName
EndpointName
PipeName
ConnectTimeout
RequestTimeout
MaxResponseBytes
```

错误和结果都回送这些非敏感字段，便于 AI 准确判断当前连接的是哪台机器。

## 7. 认证与连接状态

Host 只保存观察状态，不把“曾认证成功”当作永久有效：

```text
Unknown
Unauthenticated
Authenticated
CredentialConflict
HostUnreachable
```

Target 状态独立：

```text
Unknown
Ready
PipeNotFound
AccessDenied
ConnectTimeout
ProtocolError
```

`host auth` 成功不代表某条管道存在；`host test` 成功后仍需 `target test` 验证具体管道。

## 8. 错误码

| 错误码 | 含义 |
|---|---|
| `CLI_HOST_UNKNOWN` | Host 别名不存在 |
| `CLI_TARGET_HOST_UNKNOWN` | Target 引用了不存在的 Host |
| `CLI_REMOTE_AUTH_REQUIRED` | 尚未建立远程认证 |
| `CLI_REMOTE_ACCESS_DENIED` | 已到达目标但权限不足 |
| `CLI_REMOTE_CREDENTIAL_CONFLICT` | 当前会话已有不同账户连接同一主机 |
| `CLI_REMOTE_HOST_UNREACHABLE` | 主机或 SMB 不可达 |
| `CLI_REMOTE_PIPE_NOT_FOUND` | 主机可达但管道不存在 |
| `CLI_REMOTE_CONNECT_TIMEOUT` | 远程管道连接超时 |
| `CLI_REMOTE_PROTOCOL_ERROR` | 已连接但 Frame 交换失败 |

错误数据包含 hostAlias、serverName、targetAlias、pipeName、阶段、Windows 错误码、当前用户、retryable。不得包含密码或凭据材料。

## 9. Composite 规则

一个 composite 的所有步骤必须解析到同一 target。默认禁止：

- 跨 Host
- 同 Host 跨 pipeName
- 运行中切换当前 target

跨目标编排未来应作为更高层 orchestrator 单独设计，不能混入单个 `rtdiag/3.0 batch`。

## 10. 安全规则

1. CLI 不提供 `--password`。
2. Host/Target JSON 不保存密码、哈希或可逆密文。
3. 密码不进入 Shell history、事件、日志、Frame、错误信息。
4. 不自动断开已有 IPC 会话。
5. 不自动把 AI 账户加入 Administrators。
6. 管道 ACL 使用专用 Runtime Operators 组；临时兼容 Authenticated Users 必须可配置并有迁移期限。
7. 远程 shutdown、invoke、set 等变更命令继续遵守现有风险等级和确认规则。
8. Host 认证只解决 Windows 身份，不能绕过目标管道自身授权。

## 11. 验收测试

必须覆盖：

1. 本机旧 target 无 host 字段仍可用。
2. 一台远程主机挂载 Service/UI 多条管道，只认证一次即可分别访问。
3. 两台远程主机使用不同认证会话，插槽互不串线。
4. NetworkService 创建管道，Runtime Operators 成员可远程读写。
5. 非成员返回 Access Denied，匿名和 Guest 被拒绝。
6. 凭据冲突返回专用错误，不折叠为 timeout。
7. Host 可达但程序未运行时返回 Pipe Not Found/Connect Timeout 的准确阶段。
8. `@atlas-ui` 不改变 Shell 当前 target。
9. 单行 `--target` 与 Shell 执行产生相同 Frame。
10. `--target` 与 `--server/--pipe` 同时出现时拒绝。
11. 大于 64 KiB 的远程响应完整收发。
12. composite 不允许跨 target。

## 12. 推荐实施顺序

1. 配置模型增加 hosts 和 target.host，兼容旧配置。
2. 传输层改为 `serverName + pipeName` 两参数。
3. 完成 `host list/show/test` 和远程只读连接。
4. 完成 `host auth/logout` 的 Windows 安全交互。
5. 完成 Shell `target --host` 与单行 `--target`。
6. 增加准确错误码和凭据冲突处理。
7. 增加专用 Runtime Operators 管道 ACL。
8. 完成多主机、多插槽、大 Frame 自动化测试。

完成第 1、2、3、5 项后，即可立即用于 DDNS Snap 的 Atlas 远程只读调试；认证与 ACL 按第 4、7 项完善后形成正式安全方案。

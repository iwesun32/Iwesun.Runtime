# CLI 远程命名管道与 Shell 插槽改进意见书

## 实测结论

2026-07-13 使用当前用户 `apollo\lyh` 对 Atlas 实测：

- SMB 445 可达，`\\Atlas\IPC$` 可连接。
- `DdnsSnap.UI.RuntimeDiagnostics` 可通过 `NamedPipeClientStream("Atlas", pipeName, ...)` 远程连接并执行标准 `rtdiag/2.0` Frame。
- `host.summary` 完整成功，返回 Atlas 上的 `DdnsSnap.UI`、PID 和 Runtime 10.0.9。
- `DdnsSnap.Service.RuntimeDiagnostics` 返回 Access Denied，说明 Service 管道 ACL 尚未与 UI 的已验证 ACL 对齐。
- Agent Server 与 Agent UI 管道连接超时，当时没有对应监听实例。
- 当前 CLI 的 `--pipe=\\Atlas\pipe\...` 会把完整 UNC 当成本地 pipeName，不能表达远程服务器与管道名两个独立参数。

## 建议一：主机与管道插槽分层

远程主机必须独立登记，负责主机寻址与一次性 Windows 认证；管道插槽只引用主机并描述该主机上的 endpoint 和 pipeName。不要在每个插槽内重复认证配置，也不要把 UNC 文本塞入 pipeName。

```json
{
	"hosts": {
		"atlas": {
			"serverName": "Atlas",
			"credentialTarget": "Atlas"
		},
		"apollo": {
			"serverName": "."
		}
	},
  "targets": {
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

一个主机可以挂载任意数量的管道插槽。删除主机时，CLI 必须拒绝或明确列出仍引用它的插槽，不能留下悬空引用。

命令行可保留临时覆盖，但日常使用主机与插槽登记：

```powershell
iwrt --server=Atlas --pipe=DdnsSnap.Service.RuntimeDiagnostics host.summary
```

底层构造器应分别传入服务器和管道：

```csharp
new NamedPipeClientStream(serverName, pipeName, PipeDirection.InOut,
    PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation);
```

`serverName` 缺省为 `.`，保持全部本机调用兼容。

## 建议二：主机认证指令

认证以主机为单位执行一次，同一 Windows 登录会话中，该主机下所有管道插槽复用同一个 SMB/IPC 认证上下文。

Shell 建议提供：

```text
host add atlas Atlas
host list
host current atlas
host auth atlas --user Atlas\IwesunAiDiag
host test atlas
host logout atlas
host remove atlas
```

`host auth` 应调用 Windows 原生凭据入口或建立 `\\Atlas\IPC$` 会话，并通过安全交互提示读取密码。密码不得进入命令参数、Shell history、环境变量、JSON、Frame 或日志。

认证成功后以下插槽全部直接可用：

```text
@atlas-service host.summary
@atlas-ui host.summary
```

`host test` 应分层返回：名称解析、445 可达、IPC 会话、认证身份、管道握手。`host logout` 只清理该主机的 IPC/凭据会话；执行前应提示这会影响当前 Windows 登录会话中访问同一主机的其他程序。

同一 Windows 登录会话不能用不同凭据同时连接同一主机。遇到凭据冲突时返回 `CLI_REMOTE_CREDENTIAL_CONFLICT`，明确建议先执行 `host logout atlas`，不得自动断开用户已有会话。

## 建议三：Shell 远程插槽

Shell 的 `target add/list/use/current/remove` 应允许远程目标：

```text
target add atlas-ui atlas diagnostics DdnsSnap.UI.RuntimeDiagnostics
target add atlas-service atlas diagnostics DdnsSnap.Service.RuntimeDiagnostics
@atlas-ui host.summary
@atlas-service reflection.get service.root-snapshot
```

目标状态应显示 `serverName`、`pipeName`、当前 Windows 身份、最近连接结果和错误阶段。CLI 不应自动降级为匿名或来宾身份。

在正式语法完成前，可允许用户配置中的自定义 target 作为临时远程插槽；传输层仍必须从 host 引用解析 serverName，并与 pipeName 分开传递。

## 紧急需求：统一 AI 远程调试账户

多机运行环境应统一建立一个专用低权限账户，例如 `IwesunAiDiag`。该账户只用于 Runtime 远程诊断，不作为 DDNS Snap 服务运行账户，不加入 Administrators，也不授予交互式远程桌面权限。

推荐在每台受管机器建立同名本地组 `Iwesun Runtime Operators`，把本机 `IwesunAiDiag` 加入该组。Runtime 创建 Diagnostics 管道时按 SID 授予该组读写权限；SYSTEM、NetworkService 和 Administrators 保留完全控制。正式版本应优先授权专用组，而不是长期向全部 Authenticated Users 开放。

非域环境中，各机器可建立同名、同密码的 `IwesunAiDiag` 本地账户。域环境则使用一个域服务账户并加入各机器的 Runtime Operators 本地组。密码只进入 Windows LSA/凭据管理器，禁止写入 CLI JSON、DDNS 配置、命令历史、日志或诊断 Frame。

首次登录使用 Windows 原生认证：

```powershell
net use \\Atlas\IPC$ /user:Atlas\IwesunAiDiag *
```

`*` 必须触发安全的交互式密码输入。也可由管理员通过 Windows Credential Manager 预置 Generic/Windows Credential。CLI 只复用当前 Windows SMB 认证会话，不自行实现密码协议。

需要支持显式注销和切换账户：

```powershell
net use \\Atlas\IPC$ /delete
```

Windows 同一登录会话不能用两套不同凭据同时连接同一远程主机。CLI 遇到 `ERROR_SESSION_CREDENTIAL_CONFLICT` 时应明确提示先断开旧 IPC 会话，不能模糊返回管道超时。

### CLI 急需修改

1. 用户配置增加独立 `hosts`；target 使用 `host` 键引用主机。
2. endpoint/target 的 pipeName 仍只保存纯管道名，不保存 UNC。
3. `NamedPipeClientStream` 使用从 host 解析的 `serverName` 与 target 的 `pipeName` 两个独立参数。
4. Shell 增加 `host add/list/current/auth/test/logout/remove`。
5. `host auth` 对一台主机只认证一次，该主机全部插槽共享认证会话。
6. `target list/current` 显示主机引用、解析后的 serverName、管道和最近连接结果。
7. 增加只读的 `target test <name>`：在 host test 基础上继续报告管道连接和协议握手阶段。
8. 将 Access Denied、凭据冲突、找不到管道、连接超时分别映射为稳定错误码，禁止全部折叠为 `CLI_CONNECT_TIMEOUT`。
9. CLI 不增加 `--password`，不解析密码，不把凭据放入 user config。
10. Shell 远程 target 与本地 target 使用同一命令目录、composites、分页和路径导航能力。
11. `@atlas-service reflection.get service.root-snapshot` 必须保持目标隔离，不能误发往当前本地插槽。
12. 增加 NetworkService 创建服务端、专用 AI 用户远程连接的自动化回归测试。

### 建议错误码

- `CLI_REMOTE_AUTH_REQUIRED`
- `CLI_REMOTE_ACCESS_DENIED`
- `CLI_REMOTE_CREDENTIAL_CONFLICT`
- `CLI_REMOTE_HOST_UNREACHABLE`
- `CLI_REMOTE_PIPE_NOT_FOUND`
- `CLI_REMOTE_CONNECT_TIMEOUT`

错误数据至少包含 `serverName`、`pipeName`、阶段、Windows 错误码、当前用户、是否可重试；不得包含密码、令牌或凭据材料。

## 建议四：ACL 成对一致

同一产品的 Service、Agent Server、Service UI、Agent UI Diagnostics 管道必须使用统一的安全描述符工厂。当前实测 UI 允许远程当前用户，而 Service 拒绝，说明四宿主仍存在 ACL 漂移。

建议增加自动化测试，至少覆盖：

- 本机交互用户访问 NetworkService 创建的管道；
- 远程已认证用户访问；
- 未认证/匿名访问被拒绝；
- 四宿主 ACL 规则完全一致。

## 建议五：大响应完整发送

远程执行 `host.info` 时，响应长度前缀声明 155,890 字节，但客户端只收到 65,536 字节后服务端关闭；同一链路的 `host.summary` 849 字节完整成功。

服务端发送必须循环写完完整 Frame，并在关闭实例前完成 Flush。客户端也必须循环读取到长度前缀声明的全部字节。建议加入大于 64 KiB、1 MiB 和接近 `maxResponseBytes` 的远程管道回归测试。

日常命令继续优先使用摘要、路径过滤和分页，避免 `host.info` 或 Root 整树无条件展开。

## DDNS Snap 本次排障收益

远程 UI 管道证明远程 Runtime 调试方案可行。Atlas Service 暂因 ACL 无法读取 `service.root-snapshot`，但通过 Atlas Web 状态接口已定位本次 IP 丢失：本机四张网卡以同一 DeviceName 逐条进入字典，后写网卡覆盖先写网卡，最终只保留一张仅有链路本地 IPv6 的网卡。远程 Service 插槽和统一 ACL 完成后，这类问题可直接在 Root 的收集、聚合、发布三层逐层核对。

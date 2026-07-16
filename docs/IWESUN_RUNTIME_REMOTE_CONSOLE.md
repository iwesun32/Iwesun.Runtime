# Iwesun Runtime 远程控制服务手册

## 1. 定位与边界

`Iwesun.Runtime.RemoteConsole` 是 Runtime CLI Shell 的远程执行端。它作为 Windows 服务运行，负责：

- 使用 Windows 命名管道接收 Runtime Frame；
- 依据 Windows SID 区分命令提交者和审批者；
- 管理隔离工作区、分块上传、审批、执行和输出跟随；
- 把标准输出、标准错误和退出码返回 CLI；
- 在服务停止时停止接收新任务，并把仍在运行的任务登记为 `Interrupted`。

它不是安装器代理，也不会自动创建 Windows 账号、保存密码、注册其他服务或替代业务 Diagnostics 管道。UI 审批与监视目前只保留协议接口，当前可用操作端是 CLI Shell。

本地 Diagnostics 管道不受影响。普通诊断命令继续连接各宿主的 Diagnostics 管道；只有 `console.*`、`workspace.*`、`console.file.list` 和 `file.upload` 连接 RemoteConsole 管道。

## 2. 发布前源码声明

服务身份和默认授权在 `Iwesun.Runtime.RemoteConsole/Program.cs` 集中声明：

```csharp
var remoteConsoleOptions = new RemoteConsoleOptions
{
    PipeName = RemoteConsoleProtocol.DefaultPipeName,
    SubmitterPrincipals = [$@"{Environment.MachineName}\IwesunAiDiag"],
    ApproverPrincipals = ["S-1-5-32-544"], // BUILTIN\Administrators
    ApprovalMode = RemoteConsoleApprovalMode.Manual
};
```

发布宿主前应按部署环境修改这一个连续代码段：

- `PipeName`：服务控制管道；默认 `Iwesun.Runtime.RemoteConsole`；
- `SubmitterPrincipals`：允许提交任务的账号名或 SID；
- `ApproverPrincipals`：允许批准、拒绝和修改审批模式的账号名或 SID；
- `ApprovalMode`：`Manual`、`Guarded` 或 `Automatic`。

账号名会在服务启动时解析成 SID。无法解析、提交者为空或审批者为空时，服务拒绝启动，不会退化为宽泛授权。

`--pipe=<name>` 可覆盖源码默认管道，适合临时实例或并行调试。它只改变端点名，不改变 Windows ACL 和角色声明。

Debug 构建另有 `--test-current-user`，仅供本机自动化测试：它把当前 Windows SID 同时设为提交者和审批者，并使用 `Automatic`。此代码由 `#if DEBUG` 隔离，Release 不包含该测试入口。即使在此测试模式下，操作系统关机和重启命令仍由内建规则强制拒绝。

## 3. 发布服务

```powershell
dotnet publish .\Iwesun.Runtime.RemoteConsole\Iwesun.Runtime.RemoteConsole.csproj `
  -c Release -r win-x64 --self-contained false
```

发布目录：

```text
Iwesun.Runtime.RemoteConsole\bin\Release\net10.0\win-x64\publish
```

安装机必须满足 Runtime 发布包声明的 .NET 运行时依赖。

## 4. 准备真实 AI 账号

账号建立、密码策略和“作为服务登录”权限由部署管理员在目标机完成。Runtime 只按源码声明解析账号并生成管道 ACL。

要求：

1. 创建专用 Windows 账号，例如 `Machine\IwesunAiDiag`；
2. 设置符合组织策略的密码；
3. 授予“作为服务登录”；
4. 不把密码写入源码、JSON、脚本、命令行历史或文档；
5. 分别准备提交者与审批者身份；生产环境不应由提交者审批自己的任务。

## 5. 交互安装为 Windows 服务

在管理员 PowerShell 中执行。凭据由 `Get-Credential` 交互获取，不落入命令历史：

```powershell
$exe = 'C:\Program Files\Iwesun\Runtime\bin\Iwesun.Runtime.RemoteConsole\Iwesun.Runtime.RemoteConsole.exe'
$credential = Get-Credential -UserName "$env:COMPUTERNAME\IwesunAiDiag" `
  -Message '输入 RemoteConsole 服务账号凭据'

New-Service `
  -Name 'Iwesun.Runtime.RemoteConsole' `
  -BinaryPathName ('"{0}"' -f $exe) `
  -DisplayName 'Iwesun Runtime Remote Console' `
  -Description 'Approved remote Runtime CLI command execution service.' `
  -StartupType Manual `
  -Credential $credential

Start-Service Iwesun.Runtime.RemoteConsole
Get-Service Iwesun.Runtime.RemoteConsole
```

验收结果必须显示 `Running`。若当前会话无管理员权限、账号不存在或无法交互输入凭据，应记录为“环境受限，未执行”，不能标记通过。

卸载只删除服务登记，不删除账号：

```powershell
Stop-Service Iwesun.Runtime.RemoteConsole -ErrorAction SilentlyContinue
sc.exe delete Iwesun.Runtime.RemoteConsole
```

## 6. CLI Shell 连接

远程 Windows 命名管道先使用目标机账号建立 IPC 会话，再登记 RemoteConsole 插槽：

```text
iwrt --interactive
node add atlas Atlas
node auth atlas --user Atlas\IwesunAiDiag
console target add atlas-console atlas Iwesun.Runtime.RemoteConsole
console target use atlas-console
console target current
console.info
```

`console target` 与普通 `target` 分别保存当前 RemoteConsole 端点和当前 Diagnostics 端点。切换远程控制目标不会改变当前业务诊断目标。

`exit` 或 `quit` 只退出本地 CLI Shell，不停止 RemoteConsole 服务，也不停止被控制宿主。

## 7. 工作区、上传和执行

```text
workspace.create deployment
workspace.list
file.upload <workspaceId> C:\packages\app.zip app.zip
console.file.list <workspaceId>
console.submit --workspace <workspaceId> --shell powershell -- Expand-Archive app.zip -DestinationPath app
console.status <jobId>
console.follow <jobId>
workspace.remove <workspaceId>
```

`file.upload` 是 CLI 客户端命令：读取本地文件，计算 SHA-256，然后发送 `begin/chunk/commit`。服务端验证顺序、长度、配额和哈希后才原子落地。

工作目录只能位于服务工作区根目录内。盘符路径、UNC、ADS、路径穿越和重解析点逃逸均被拒绝。工作区默认保留 24 小时，过期项由服务清理。

## 8. 审批模式

- `Manual`：所有任务进入 `AwaitingApproval`；
- `Guarded`：匹配拒绝规则的任务拒绝，匹配自动批准规则的任务执行，其余等待审批；
- `Automatic`：通过拒绝规则和基础校验后自动批准。

三种模式都不能放行操作系统电源命令。`Restart-Computer`、`Stop-Computer` 以及带 `/r`、`/s`、`/g`、`/sg` 或 `/hybrid` 的 `shutdown.exe` 命令由服务内建规则直接拒绝；该规则不能通过 `console.policy.set` 或用户拒绝规则覆盖。Runtime 的 `lifecycle.shutdown` 只让目标宿主从正常程序出口结束，不会调用操作系统重启。

```text
console.pending
console.approve <jobId>
console.reject <jobId> <reason>
console.policy.get
console.policy.set Manual
```

服务以连接管道时取得的 Windows SID鉴权，不接受客户端 JSON 自报身份。任务保存提交内容的不可变哈希；重复 RequestId 不会重复启动。提交者与审批者角色分离时，提交者不能自行批准。

## 9. 输出和停止语义

`console.follow` 按递增序号轮询 stdout/stderr，直到终态。输出受总字节预算约束；发生截断时协议返回最早可读序号和 `truncated=true`，客户端不得把它解释为完整输出。

服务停止时：

1. 停止接受新连接；
2. 通知执行器停止调度；
3. 尚未完成的任务标记为 `Interrupted`；
4. 不把它们伪报为 `Completed` 或 `Cancelled`；
5. 服务重启后不自动重放旧任务。

当前快速适配版不承诺终止已经脱离服务生命周期的外部子进程。需要跨重启持久化、进程树强约束和 UI 审批时，应在后续 Manager 架构阶段实现。

## 10. 本机调试

无需 SCM 的 Debug 调试：

```powershell
dotnet run --project .\Iwesun.Runtime.RemoteConsole -c Debug -- `
  --console --test-current-user --pipe=Iwesun.Runtime.RemoteConsole.Debug

dotnet run --project .\Iwesun.Runtime.Cli -c Debug -- `
  --server=. --pipe=Iwesun.Runtime.RemoteConsole.Debug console.info
```

自动化验收场景：

```powershell
dotnet run --project .\Iwesun.Runtime.FunctionalTests -c Debug -- `
  --child --scenario remote-console-cli
```

该场景验证独立管道、Windows 身份模拟、Shell 目标上下文、`exit` 不停止服务、工作区创建、分块上传、PowerShell 执行和输出跟随。

## 11. 当前环境验收记录

- Debug `win-x64` 发布：已通过；
- 控制台服务宿主：已通过；
- CLI 真实命名管道端到端：已通过；
- 当前机器 `IwesunAiDiag` 账号：存在且启用；
- SCM 指定账号安装/启动：当前执行会话不是管理员，环境受限，未执行；
- 真实服务账号 `whoami` 与 SCM stop/start：须在管理员交互安装后执行，当前不得标记完成。

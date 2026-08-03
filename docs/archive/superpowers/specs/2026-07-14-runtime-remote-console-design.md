# Runtime 远程控制台快速适配版设计

> 日期：2026-07-14
> 状态：已批准设计，待制定实施计划
> 目标：让 AI 通过现有 CLI Shell 连接远端 Windows 服务，上传临时文件、提交终端命令、接受本机审批并增量读取输出。

## 一、范围与边界

第一版解决当前最直接的自动化缺口：业务 Runtime 管道只能调试已经运行的宿主，不能在远端执行安装命令、启动程序或运行构建测试。新功能采用独立 RemoteConsole 服务，不扩大业务 Diagnostics 管道权限。

管理员手工完成 RemoteConsole Windows Service 的首次安装，并在安装时指定真实 Windows AI 账号。服务及其子进程继承该账号权限；Runtime 不创建账号、不保存账号密码、不提权、不切换 SYSTEM。权限不足时命令按原始 Windows 错误失败。

第一版不包含：

- 公网 Broker、云端账户或跨平台代理；
- Setup 项目代理、安装步骤编排或自动推断安装流程；
- 交互式密码、安装向导、桌面可见终端或 PTY；
- 服务重启后的命令续跑、自动重放或自动重试；
- 已运行进程树的强制停止；
- 完整 VS Code 风格终端管理体验。

## 二、选定架构

```text
AI / Iwesun.Runtime.Cli Shell
        |
        | Runtime JSON Frame over remote named pipe
        v
Iwesun.Runtime.RemoteConsole Windows Service
        |-- client identity and action authorization
        |-- approval policy and pending queue
        |-- workspace and chunk upload
        |-- command execution
        `-- sequenced stdout/stderr buffer
              ^
              |
Iwesun.Runtime.RemoteConsole.Manager (WPF)
        |-- node connection
        |-- approval queue
        |-- job state
        `-- output monitor scaffold
```

CLI 和管理 UI 均直接连接目标服务器，不引入中央中转服务。RemoteConsole Server 是审批和执行权威；UI 只调用批准、拒绝和撤回接口，不能绕开服务端策略。

## 三、项目划分

### 3.1 Iwesun.Runtime.RemoteConsole.Protocol

只包含公共值类型、JSON Frame 命令参数和结果模型，不启动进程、不访问磁盘、不依赖 UI。

核心模型：

- `RemoteConsoleJobId`、`RemoteConsoleWorkspaceId`；
- `RemoteConsoleApprovalMode`：Manual、Guarded、Automatic；
- `RemoteConsoleJobState`：Submitted、AwaitingApproval、Starting、Running、Completed、Failed、Rejected、Cancelled、Interrupted；
- `RemoteConsoleOutputChunk`：Sequence、Stream、Timestamp、Text；
- 工作区、上传会话、文件摘要和审批决定。

### 3.2 Iwesun.Runtime.RemoteConsole

Windows Service 可执行项目，职责包括：

- 托管独立命名管道；
- 识别连接方 Windows 身份；
- 执行动作级授权；
- 维护三级审批策略；
- 创建工作区并接收文件块；
- 原样启动非交互命令；
- 捕获 stdout、stderr、退出码和状态；
- 维护有界输出缓冲与工作区过期清理。

### 3.3 Iwesun.Runtime.Cli

在现有 Shell 中增加 RemoteConsole 上下文与命令。CLI 仍是 AI 的工作端；管理 UI 不代替 CLI Shell。

### 3.4 Iwesun.Runtime.RemoteConsole.Manager

简单 WPF 管理程序。第一版提供节点、审批、任务和输出四个区域，只完成可用骨架，不实现复杂终端标签、PTY 输入或桌面会话注入。

## 四、管道与身份

RemoteConsole 使用独立固定管道，例如 `Iwesun.Runtime.RemoteConsole`。远程机器名与纯管道名仍通过现有 Node/Target 模型分开表达，不允许把 UNC 写进 pipeName。

主程序源码声明两组 Windows principals：

- Submitters：允许上传文件、提交命令、读取本人任务和撤回未执行任务；
- Approvers：允许读取待审批任务、批准、拒绝、调整审批等级和查看全部任务。

服务端必须取得实际管道客户端 Windows 身份并逐动作检查。客户端传入的账号名、角色或风险标签都不作为授权依据。Submitter 与 Approver 相同则拒绝批准自己的任务；管理员可在源码中登记多个独立审批账号或组。

SYSTEM、服务账号和 Administrators 保留服务维护权限。Anonymous 与 Guest 不授权。凭据继续由 Windows IPC 会话管理，不进入 JSON、历史、日志或审计数据。

## 五、审批策略

### 5.1 Manual

所有 `console.submit` 进入 AwaitingApproval，只有 Approver 明确批准后执行。

### 5.2 Guarded

只有命中服务端配置的自动允许规则时直接执行，其他命令进入 AwaitingApproval。规则匹配最终展开后的 shell、命令文本和工作目录。第一版不尝试理解 PowerShell 语义；未明确匹配即要求审批。

### 5.3 Automatic

除命中服务端明确拒绝规则外直接执行。拒绝规则是服务端权威，AI 的 `riskHint` 只用于 UI 显示。

审批决定绑定不可变的 Job 内容摘要。UI 修改命令时必须撤回原 Job 并提交新 Job，禁止批准后替换命令。只有 AwaitingApproval 状态可 Reject 或 Cancel；第一版不把运行中的进程终止伪装成撤回。

## 六、命令执行

`console.submit` 提交：

- shell：powershell、cmd 或受配置允许的可执行程序；
- command：最终完整命令文本；
- workspaceId；
- workingDirectory：工作区内相对目录；
- 非敏感环境变量；
- 仅供显示的 riskHint 和说明。

Server 不改写、不拆分、不代理命令业务流程。批准后按提交内容启动一个非交互子进程，重定向 stdout 和 stderr，并记录真实退出码。第一版不提供执行超时或运行中 Cancel，避免只结束 shell 却遗留子进程树；需要限时行为时由提交的原始命令自行实现。

第一版 Job 状态只保存在服务进程内存中。正常停止服务时，Running Job 标记为 Interrupted 并发布最终事件；进程崩溃时活动状态直接丢失。下一次启动不恢复、不重放，AI 必须重新提交新命令。

## 七、输出模型

每个 Job 的 stdout 和 stderr 合并进入一个按到达顺序编号的有界缓冲：

- Sequence 单调递增；
- Stream 明确为 stdout 或 stderr；
- Text 保留原始内容，不混入服务日志；
- 完成结果单独返回 exitCode、结束时间和最终状态。

`console.follow(jobId, afterSequence, limit, waitMs)` 使用有限长轮询返回增量块。CLI 持续调用形成接近实时输出；UI 使用同一接口镜像监控。超出缓冲预算时返回 earliestAvailableSequence 和 truncated=true，客户端不得误认为输出完整。

## 八、临时工作区与上传

工作区根目录固定为：

```text
C:\ProgramData\Iwesun\RemoteConsole\workspaces\<workspaceId>\
```

接口包括：

- `workspace.create/list/show/remove`；
- `file.upload.begin`；
- `file.upload.chunk`；
- `file.upload.commit`；
- `file.list`。

客户端只能提交工作区内相对路径。服务拒绝 `..`、绝对路径、盘符、UNC、重解析点逃逸和规范化后越界。文件块使用 Runtime 长度帧 JSON 和 Base64，第一版以可靠适配为优先；每块、单文件、单工作区和全局磁盘额度均有固定上限。

上传先写服务生成的临时文件，commit 时校验声明长度和 SHA-256，通过后在同一工作区原子落地。上传完成不会自动执行。工作区默认保留 24 小时，允许 CLI 或 UI 提前删除；正在上传或被 Running Job 使用的工作区拒绝删除。

## 九、最小 CLI

```text
console target add atlas Atlas Iwesun.Runtime.RemoteConsole
console target use atlas

workspace.create
file.upload <workspaceId> <localFile> [remoteRelativePath]
workspace.list
workspace.remove <workspaceId>

console.submit --workspace <id> --shell powershell -- <command>
console.status <jobId>
console.follow <jobId>
console.cancel <jobId>
```

审批接口由 Manager 使用，但仍进入标准 catalog 和 JSON Frame：

```text
console.pending
console.approve <jobId>
console.reject <jobId> <reason>
console.policy.get
console.policy.set <Manual|Guarded|Automatic>
```

CLI 的 `exit` 和 `quit` 仍只退出本地 Shell。RemoteConsole 命令不复用业务 Runtime 当前路径，避免把终端命令误发给 Diagnostics Target。

## 十、Manager UI 骨架

第一版 WPF 界面采用单窗口四区布局：

- 左侧节点列表和连接状态；
- 上方待审批命令队列；
- 中部命令全文、提交账号、工作区、上传文件、内容摘要；
- 下方任务状态和 stdout/stderr 监控区域。

操作按钮仅包括 Approve、Reject、Cancel Pending 和 Refresh。审批等级修改需要 Approver 权限并明确显示当前节点。UI 不缓存密码，不在客户端自行判定命令是否可执行。

## 十一、错误与逻辑安全

稳定错误至少包括：

- RC_ACCESS_DENIED；
- RC_SELF_APPROVAL_DENIED；
- RC_APPROVAL_REQUIRED；
- RC_JOB_STATE_CONFLICT；
- RC_WORKSPACE_NOT_FOUND；
- RC_PATH_OUTSIDE_WORKSPACE；
- RC_UPLOAD_HASH_MISMATCH；
- RC_QUOTA_EXCEEDED；
- RC_COMMAND_START_FAILED；
- RC_OUTPUT_TRUNCATED；
- RC_REMOTE_UNREACHABLE。

重复 submit 必须使用 requestId 幂等去重，避免 CLI 超时重试启动两次命令。Approve、Reject、Cancel 和 upload commit 均采用原子状态转换；竞争操作只有一个成功，其余返回当前权威状态。

## 十二、测试与发布

第一版验证矩阵：

1. Submitter 可上传和提交，但不能 approve；Approver 可审批但不能伪造 Submitter。
2. Manual、Guarded、Automatic 三种模式分别验证自动、等待和拒绝路径。
3. 相同 requestId 不会重复启动进程。
4. stdout、stderr、非零 exitCode、输出截断和 CLI follow 均正确。
5. 路径穿越、UNC、重解析点、超额和哈希错误全部在执行前拒绝。
6. Pending 可撤回；Running 不接受第一版 cancel；服务重启不自动重放。
7. CLI Shell 切换 Diagnostics Target 与 Console Target 时不串管道。
8. Manager 使用非 Approver 身份时按钮操作由 Server 拒绝。
9. Debug/Release 全解决方案构建、功能测试、Windows Service 实装和真实远端 AI 账号联调通过。
10. 全量安装包包含 Service、Protocol、Manager、CLI JSON、文档、样例和技能，并由安装验证脚本检查版本与文件完整性。

## 十三、实施顺序

1. Protocol、状态机和身份授权；
2. Windows Service 管道与原样命令执行；
3. 工作区和分块上传；
4. CLI Shell 目标、submit/status/follow；
5. 审批接口与三级策略；
6. WPF Manager 最小可用骨架；
7. 功能测试、远端联调、文档技能和全量发布。

第一版以连通 AI CLI Shell 为发布核心。Manager 只要求完成审批与监视骨架；复杂终端、任务编排、中央 Broker、自更新和桌面用户会话助手留待后续版本。

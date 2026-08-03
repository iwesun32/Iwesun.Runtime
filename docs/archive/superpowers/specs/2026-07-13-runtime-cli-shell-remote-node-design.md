# Runtime CLI Shell 与远程节点统一设计

## 一、设计目标

在保持 `rtdiag/2.0`、`rtdiag/3.0` 和现有命令目录不变的前提下，重构 CLI 的上下文解析，并正式支持本机及多台远程 Windows 节点上的多个 Runtime 管道目标。

本设计同时解决：

- Shell 变量只在部分 token 展开；
- 虚拟路径没有继承当前 Target；
- 远程服务器与管道名无法独立表达；
- 多目标执行容易误发；
- 远程连接错误被折叠为超时；
- 大 Frame 缺少远程完整性回归。

## 二、基本决定

1. 使用 `Node` 表示本机或远程 Windows 机器，避免与 Runtime `host.*` 命令域冲突。
2. 使用 `Target` 表示 Node 上的 endpoint 与纯管道名插槽。
3. 单次 CLI、Shell、虚拟路径、alias 和 composite 共用同一个目标解析器。
4. Windows 认证由操作系统 SMB/IPC 会话负责。CLI 不保存密码，不实现密码协议，不自动注销现有会话。
5. 单个 composite 始终绑定一个 Target。跨 Target 协调属于客户端 `MultiTargetCoordinator`，不改变 batch Frame。
6. 先实现远程只读诊断和上下文一致性，再开放远程状态修改操作。

## 三、总体结构

```text
Raw input
  -> tokenizer
  -> variable expansion
  -> shell command classifier
  -> target selector
  -> virtual path mapper / catalog binder
  -> risk policy
  -> ResolvedRuntimeTarget
  -> CliTransport
  -> Runtime Frame
```

所有执行入口最终只接受一个不可变的 `ResolvedRuntimeTarget`，禁止各层分别选择管道。

## 四、核心模型

### 4.1 Node

```json
{
  "nodes": {
    "local": { "serverName": "." },
    "atlas": {
      "serverName": "Atlas",
      "credentialTarget": "Atlas",
      "connectTimeoutMs": 5000
    }
  }
}
```

- `serverName` 是 `NamedPipeClientStream` 的服务器参数。
- `credentialTarget` 仅是非敏感的 Windows 凭据定位提示。
- 配置中禁止用户名密码、哈希、令牌和可逆密文。
- `local` 为内置 Node；旧配置自动绑定 `local`。

### 4.2 Target

```json
{
  "targets": {
    "atlas-service": {
      "node": "atlas",
      "endpoint": "diagnostics",
      "pipeName": "DdnsSnap.Service.RuntimeDiagnostics"
    }
  }
}
```

`pipeName` 必须是纯管道名，不允许 UNC。Target 可覆盖连接超时、请求超时和最大响应长度。

### 4.3 ResolvedRuntimeTarget

一次执行解析为不可变值：

- `TargetAlias`
- `NodeAlias`
- `ServerName`
- `EndpointName`
- `PipeName`
- `ConnectTimeoutMs`
- `RequestTimeoutMs`
- `MaxResponseBytes`

传输错误和测试结果回送这些非敏感字段。

### 4.4 ShellContext

只存在于当前 CLI 进程：

- 变量表；
- 当前虚拟路径；
- 当前 Target；
- 原始输入历史；
- 非敏感命令参数记忆；
- Node/Target 的会话内登记覆盖。

`clear` 清除变量、历史、参数记忆和路径，但不删除来自配置文件的 Node/Target。当前 Target 回到启动 Target；没有启动 Target 时清空。

## 五、统一解析顺序

1. 保存原始输入到 history；密码类命令不进入 history。
2. Tokenize，并保留 token 是否禁止展开的元数据。
3. 对允许展开的全部 token 执行 `$name`、`${name}` 展开。
4. 未定义变量返回 `CLI_CONTEXT_VARIABLE_NOT_FOUND`，不得建立连接。
5. 识别 `exit/quit`、上下文命令、Node 命令和 Target 命令。
6. 解析一次性 `@target` 或当前 Target。
7. 将虚拟路径映射为标准 catalog 命令。
8. 绑定参数、恢复安全的参数记忆并执行风险检查。
9. 生成一个 `ResolvedRuntimeTarget` 并交给传输层。

`@target` 只影响当前命令，不修改 Shell 当前 Target。虚拟路径和普通远程命令不得使用不同 Target。

## 六、命令界面

### 6.1 Node

```text
node add atlas Atlas
node list
node show atlas
node test atlas
node remove atlas
```

删除仍被 Target 引用的 Node 时返回引用列表并拒绝删除。

认证辅助命令在第二阶段提供：

```text
node auth atlas --user Atlas\IwesunAiDiag
node logout atlas --confirm
```

`node auth` 只能使用安全交互或 Windows UI；输入重定向环境拒绝读取密码。`node logout` 明确提示会影响当前 Windows 登录会话中的其他程序。

### 6.2 Target

```text
target add atlas-service diagnostics DdnsSnap.Service.RuntimeDiagnostics --node atlas
target list
target show atlas-service
target use atlas-service
target current
target test atlas-service
target remove atlas-service
```

不提供多个位置参数排列方式，避免 node、endpoint、pipeName 顺序歧义。

### 6.3 单次模式

```powershell
iwrt --target=atlas-service host.summary
iwrt --server=Atlas --pipe=DdnsSnap.Service.RuntimeDiagnostics host.summary
```

`--target` 与 `--server/--pipe` 互斥。`--server` 必须与 `--pipe` 同时出现。

## 七、传输与错误

传输层分别向 `NamedPipeClientStream(serverName, pipeName, ...)` 传参。客户端循环读取 4 字节长度头和完整 payload；服务端必须完成完整写入与 Flush 后才释放实例。

稳定错误码：

- `CLI_NODE_NOT_FOUND`
- `CLI_TARGET_NODE_NOT_FOUND`
- `CLI_REMOTE_AUTH_REQUIRED`
- `CLI_REMOTE_ACCESS_DENIED`
- `CLI_REMOTE_CREDENTIAL_CONFLICT`
- `CLI_REMOTE_NODE_UNREACHABLE`
- `CLI_REMOTE_PIPE_NOT_FOUND`
- `CLI_REMOTE_CONNECT_TIMEOUT`
- `CLI_REMOTE_PROTOCOL_ERROR`

Windows 原始错误码作为数据返回，不直接作为 CLI 稳定契约。错误不得包含凭据材料。

## 八、认证与 ACL 边界

- Runtime CLI 负责检测、解释和显式调用 Windows IPC 认证入口。
- 安装器或管理员脚本负责创建 `Iwesun Runtime Operators` 本地组和低权限账户。
- Runtime 管道安全工厂负责按 SID 统一授权。
- SYSTEM、NetworkService、Administrators 保留完全控制。
- Runtime Operators 获得连接和读写权限。
- Anonymous 与 Guest 不授权。
- 是否在迁移期兼容 Authenticated Users 必须是显式部署策略，不能由 CLI 动态放宽。

## 九、多节点协调

`MultiTargetCoordinator` 是客户端只读协调层：

- 输入显式 Target 集合和一个标准只读命令；
- 每个 Target 独立连接、超时和取消；
- 有界并发，缺省 4；
- 一个目标失败不取消其他目标；
- 结果按 Target 分组，保留每个完整 Runtime Frame；
- 汇总只包含总数、成功、失败、超时和总耗时。

第一阶段只允许 `host.summary`、`lifecycle.status`、`runtime.inspect` 等只读命令。远程 shutdown、invoke 和状态修改继续逐目标显式执行。

## 十、阶段划分

### 阶段 A：Shell 内核与远程只读

- 统一 token 展开和 Target 解析；
- 修复虚拟路径继承；
- Node/Target 配置模型；
- `--target`、`--server/--pipe`；
- `node list/show/test`、`target show/test`；
- 远程只读命令和错误分类。

### 阶段 B：Windows 认证与 ACL

- 安全的 `node auth/logout`；
- Runtime Operators 管道 ACL；
- NetworkService 与低权限远程用户联调。

### 阶段 C：多节点协调

- 有界并发只读 fan-out；
- 单 JSON 汇总；
- 明确禁止 destructive fan-out。

## 十一、验收标准

- 所有 Shell token 按规则统一展开，history 保留原始输入。
- `target use` 同时影响普通命令和虚拟路径。
- `@target` 不污染当前 Target。
- 本机旧配置无 `node` 字段仍可用。
- 一台 Node 可连接多个不同管道，两台 Node 不串线。
- 单次模式与 Shell 解析出相同 `ResolvedRuntimeTarget`。
- Access Denied、凭据冲突、节点不可达、管道不存在和超时具有不同错误码。
- 64 KiB、1 MiB 和接近最大限制的 Frame 完整收发。
- Shell 失败命令不退出 REPL；`exit/quit/EOF` 不停止宿主。
- composite 不跨 Target；协调层不执行 destructive fan-out。
- Debug/Release 构建、功能测试、安装版验证和技能副本一致性全部通过。

## 十二、不在本轮范围

- 自建密码协议或凭据数据库；
- 自动创建管理员账户；
- 自动断开既有 IPC 会话；
- 在 Runtime Frame 内携带认证材料；
- 将多节点协调伪装成单 Target composite；
- 非 Windows 远程命名管道传输。

# Iwesun Runtime 远程管道账号与授权手册

本文说明如何让远端 CLI 使用真实 AI Windows 账号访问 Runtime 命名管道。操作系统账号和凭据由用户管理；Runtime 只按业务主程序首页的固定声明生成管道 ACL。

## 1. 职责边界

- 管理员：在目标服务器或域中创建 AI 账号、设置密码并允许 Windows 远程 IPC 登录。
- 业务主程序：声明允许连接 Runtime 管道的 Windows 账号或组。
- Runtime：解析声明为 SID，并在每次创建管道实例时应用统一 ACL。
- CLI：使用 AI 账号凭据建立 `IPC$` 会话，再连接目标命名管道。

账号不存在或名称无法解析时，Runtime 抛出 `RuntimeHostConfigurationException`。Runtime 不创建 Windows 用户，不保存密码，也不把凭据放入 JSON、Runtime Frame、日志或程序源码。

## 2. Program.cs 严格授权

以下代码允许本机交互式 CLI，并只允许目标服务器上的 `IwesunAiDiag` 账号进行远程访问：

```csharp
var pipeAccess = new RuntimeNamedPipeAccessOptions
{
    AllowLocalInteractiveUsers = true,
    AllowAuthenticatedUsers = false,
    AllowedWindowsPrincipals =
    [
        $@"{Environment.MachineName}\IwesunAiDiag"
    ]
};

builder.Services.Start(
    runtimeDirectory,
    startupRuntimeDiagnosticsPipeName: "Company.Product.RuntimeDiagnostics",
    pipeAccessOptions: pipeAccess);
```

域账号写成 `DOMAIN\IwesunAiDiag`。推荐授权账号组而不是在多个程序中重复账号列表：

```csharp
AllowedWindowsPrincipals =
[
    $@"{Environment.MachineName}\Iwesun Runtime Operators"
]
```

组和成员仍由管理员提前建立。主程序声明是授权来源，不是操作系统账号创建器。

## 3. Windows Service

Windows Service 使用同一配置对象：

```csharp
builder.Services.StartWindowsService(
    serviceOptions: new RuntimeWindowsServiceOptions
    {
        ServiceName = "Company.ProductService",
        DisplayName = "Product Service",
        Description = "Product background service.",
        ShutdownTimeout = TimeSpan.FromSeconds(30)
    },
    runtimeDirectory: runtimeDirectory,
    startupRuntimeDiagnosticsPipeName: "Company.Product.RuntimeDiagnostics",
    pipeAccessOptions: pipeAccess);
```

SYSTEM、NetworkService 和 Administrators 始终保留完全控制。`AllowLocalInteractiveUsers=true` 保证本机终端用户可以使用 CLI；它不代替远端 AI 账号授权。本机非交互自动化账号需要显式加入 `AllowedWindowsPrincipals`。

## 4. CLI 登录与目标登记

交互式登录：

```text
node add atlas Atlas
node auth atlas --user Atlas\IwesunAiDiag
target add atlas-service diagnostics Company.Product.RuntimeDiagnostics --node atlas
target use atlas-service
host.info
```

密码由不可回显终端读取。`exit` 和 `quit` 只退出 Shell，不停止远端宿主。需要结束 Windows IPC 会话时显式执行：

```text
node logout atlas --confirm
```

自动化场景应由用户提前通过 Windows Credential Manager 建立凭据；CLI 不接受命令行密码参数。

## 5. 本地与远端权限结果

| 身份 | 严格配置结果 |
| --- | --- |
| SYSTEM / NetworkService / Administrators | 完全控制 |
| 本机交互式 CLI 用户 | `AllowLocalInteractiveUsers=true` 时允许 |
| 首页声明的 AI 账号或组 | 允许读写管道 |
| 其他远端认证账号 | `AllowAuthenticatedUsers=false` 时拒绝 |
| Anonymous / Guest | 拒绝 |

只有迁移旧系统时才可临时设置 `AllowAuthenticatedUsers=true`。这会放行所有已认证 Windows 身份，不属于严格远程授权。

## 6. 发布前验收

1. 使用本机普通终端执行 `host.info`，确认本地入口不受影响。
2. 使用已登记 AI 账号执行 `node auth` 和 `target test`，确认远端可访问。
3. 使用未登记账号连接，必须得到 `CLI_REMOTE_ACCESS_DENIED`。
4. 检查四个业务宿主分别声明自己的固定管道名和同一账号策略。
5. 执行 `quiet`，确保诊断输出恢复静默。


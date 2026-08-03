# 进程 / PowerShell 端点

> **状态**: CURRENT | **最后更新**: 2026-07-09
> **源码参考**: `modules/Networks/src/Iwesun.Runtime.Networks/ProcessCommandEndpoint.cs`、`PowerShellSingleCommandEndpoints.cs`
> **上游**: [ENDPOINT_BASE.md](ENDPOINT_BASE.md)（基类 API）
> **下游**: 无

---

## 一、ProcessCommandEndpoint

进程命令执行端点。启动外部进程，捕获标准输出/错误，返回退出码和输出内容。

### 请求/结果类型

```csharp
readonly record struct ProcessCommandRequest(
    string FileName,
    string Arguments = "",
    string WorkingDirectory = "",
    int TimeoutMs = 30000,
    bool RedirectStandardOutput = true,
    bool RedirectStandardError = true,
    bool UseShellExecute = false,
    IDictionary<string, string>? EnvironmentVariables = null
);

readonly record struct ProcessCommandResult(
    string FileName,
    string Arguments,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut,
    bool Success,
    string? ErrorMessage
);
```

### 说明

- 使用 `System.Diagnostics.Process` 启动外部进程。
- 默认重定向标准输出和错误，不使用 Shell 执行。
- 超时后强制终止进程（`Kill()`），`TimedOut = true`。
- 进程退出码为 0 时 `Success = true`。
- 支持设置工作目录和环境变量。
- 每个请求启动一个独立进程，执行完成后释放资源。

---

## 二、PowerShellWriteOutputEndpoint

PowerShell 命令执行端点。通过 `powershell.exe` 执行单条 PowerShell 命令，捕获输出。

### PowerShell 请求/结果类型

```csharp
readonly record struct PowerShellCommandRequest(
    string Command,
    int TimeoutMs = 30000,
    bool NoProfile = true,
    ExecutionPolicy ExecutionPolicy = ExecutionPolicy.Bypass
);

readonly record struct PowerShellCommandResult(
    string Command,
    int ExitCode,
    string Output,
    string Error,
    bool TimedOut,
    bool Success,
    string? ErrorMessage
);
```

### ExecutionPolicy 枚举

| 值             | 说明                               |
| -------------- | ---------------------------------- |
| `Bypass`       | 绕过执行策略（默认，无警告无提示） |
| `Unrestricted` | 不限制，加载未签名脚本会警告       |
| `RemoteSigned` | 远程脚本需签名，本地脚本无需       |

### PowerShell 端点说明

- 内部通过 `ProcessCommandEndpoint` 启动 `powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "<command>"`。
- 默认使用 `-NoProfile` 避免加载用户配置文件，加快启动速度。
- 输出为 PowerShell 命令的标准输出文本（非对象序列化）。
- 适用于需要调用系统 PowerShell cmdlet（如 `Get-NetNeighbor`、`Get-NetIPAddress`）的场景。

---

## 三、使用示例

```csharp
// 执行命令行进程
using var proc = new ProcessCommandEndpoint();
proc.Start();
proc.Send(new ProcessCommandRequest(
    FileName = "ipconfig",
    Arguments = "/all",
    TimeoutMs = 10000
));
if (proc.TryReadReceived(out var result) && result.Success)
{
    Console.WriteLine(result.StandardOutput);
}

// 执行 PowerShell 命令
using var ps = new PowerShellWriteOutputEndpoint();
ps.Start();
ps.Send(new PowerShellCommandRequest(
    Command = "Get-NetIPAddress -AddressFamily IPv4 | Select-Object IPAddress,InterfaceAlias",
    TimeoutMs = 15000
));
if (ps.TryReadReceived(out var psResult) && psResult.Success)
{
    Console.WriteLine(psResult.Output);
}
```

---

## 四、安全注意事项

1. **命令注入风险**：`Command` 和 `Arguments` 直接传递给进程/PowerShell，调用方必须验证输入，避免注入恶意命令。
2. **超时设置**：始终设置合理的超时时间，防止进程挂起导致资源泄漏。
3. **输出截断**：长输出可能占用大量内存，建议在命令中使用 `Select-Object -First N` 或管道到 `Out-String -Width 200` 控制输出大小。
4. **权限**：某些系统命令需要管理员权限，端点本身不做权限提升。

---

## 五、相关文档

- 端点基类 → [ENDPOINT_BASE.md](ENDPOINT_BASE.md)
- 本地系统表端点 → [LOCAL_TABLES.md](LOCAL_TABLES.md)

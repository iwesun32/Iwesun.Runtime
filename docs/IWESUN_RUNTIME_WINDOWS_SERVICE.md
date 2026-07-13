# Iwesun Runtime Windows Service 接入手册

本文是 Windows Service 宿主的专项说明。Runtime 负责 Windows SCM 生命周期与协调退出；业务代码只负责业务执行、可选退出清理和业务状态汇报。

## 1. 职责边界

Runtime 主控统一管理：

- Windows SCM Start、Stop 和系统 Shutdown；
- `WindowsServiceLifetime` 与服务上下文识别；
- Root 中进程、线程、任务的扁平登记；
- Stop/Wakeup 广播；
- `CleanupRequested` 清理钩子；
- `Requested → Draining → Completed/Timeout` 状态；
- 轻量退出信号、0/124 退出码与自动反登记；
- CLI shutdown 与 SCM Stop 的幂等合并。

业务程序只负责：

- 注册标准 `IHostedService` 或 `BackgroundService`；
- 使用 `RProcess`、`RThread`、`RTask` 创建受管执行单元；
- 在确有资源需要清理时挂载 `CleanupRequested`；
- 使用 `IRManagedState` 汇报业务细分状态。

业务程序不再实现 `ServiceBase`、`OnStart`、`OnStop`、`OnShutdown`、`AddWindowsService()`、`UseWindowsService()`、`ApplicationStopping` 或私有退出倒计时。

## 2. 最小 Program.cs

```csharp
using System.Reflection;
using Iwesun.Runtime.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

[assembly: DiagnosticPipePrefix("MyProduct")]

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddRuntimeDiagnostics();

var runtimeDirectory = Path.Combine(AppContext.BaseDirectory, "runtime");
builder.Services.StartWindowsService(
    serviceOptions: new RuntimeWindowsServiceOptions
    {
        ServiceName = "MyCompany.MyProductService",
        DisplayName = "My Product Service",
        Description = "业务程序提供的服务说明。",
        ShutdownTimeout = TimeSpan.FromSeconds(30)
    },
    runtimeDirectory: runtimeDirectory,
    startupRuntimeDiagnosticsPipeName: "MyProduct.RuntimeDiagnostics");

builder.Services.AddHostedService<MyBusinessWorker>();

using var host = builder.Build();
host.Services.Activate(Assembly.GetExecutingAssembly());
await host.RunAsync();
```

不要在调用 `StartWindowsService(...)` 后再调用 `Start()`、`AddWindowsService()` 或 `UseWindowsService()`。

### 2.1 不在 Program.cs 设置服务身份

固定主程序可以只保留平台启动调用：

```csharp
builder.Services.StartConfiguredWindowsService(
    runtimeDirectory: runtimeDirectory,
    startupRuntimeDiagnosticsPipeName: "MyProduct.RuntimeDiagnostics");
```

任意业务 DI 模块在 `Build()` 前通过 API 提供服务身份：

```csharp
services.ConfigureRuntimeWindowsService(options =>
{
    options.ServiceName = "MyCompany.MyProductService";
    options.DisplayName = "My Product Service";
    options.Description = "业务程序提供的服务说明。";
    options.ShutdownTimeout = TimeSpan.FromSeconds(30);
});
```

配置 API 可以在 `StartConfiguredWindowsService(...)` 之前或之后调用，但必须在 `builder.Build()` 之前完成。Runtime 不知道业务服务名称，也不提供默认 `ServiceName`；缺少名称时服务 Lifetime 拒绝启动。

### 2.2 完整 API 配置例子

固定 `Program.cs`：

```csharp
using System.Reflection;
using Iwesun.Runtime.Diagnostics;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddRuntimeDiagnostics();

builder.Services.StartConfiguredWindowsService(
    runtimeDirectory: Path.Combine(AppContext.BaseDirectory, "runtime"),
    startupRuntimeDiagnosticsPipeName: "MyProduct.RuntimeDiagnostics");

builder.Services.AddMyProductService();

using var host = builder.Build();
host.Services.Activate(Assembly.GetExecutingAssembly());
await host.RunAsync();
```

业务模块 `MyProductServiceRegistration.cs`：

```csharp
using Iwesun.Runtime.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

public static class MyProductServiceRegistration
{
    public static IServiceCollection AddMyProductService(this IServiceCollection services)
    {
        services.ConfigureRuntimeWindowsService(options =>
        {
            options.ServiceName = "MyCompany.MyProductService";
            options.DisplayName = "My Product Service";
            options.Description = "执行 My Product 后台业务。";
            options.ShutdownTimeout = TimeSpan.FromSeconds(30);
        });

        services.AddSingleton<MyProductState>();
        services.AddHostedService<MyProductWorker>();
        return services;
    }
}
```

这样固定主程序不保存业务服务名，服务身份、说明和业务 DI 都集中在业务模块。

## 3. 参数

| 参数 | 用途 |
| --- | --- |
| `ServiceName` | SCM 内部服务名，由业务程序提供，必填 |
| `DisplayName` | 服务管理器显示名称，由业务程序提供；空值时采用 ServiceName |
| `Description` | 服务用途说明，由业务程序提供 |
| `runtimeDirectory` | Runtime 工作目录 |
| `shutdownTimeout` | 唯一总退出期限；所有单元共享，不创建单元私有倒计时 |
| `startupRuntimeDiagnosticsPipeName` | RuntimeDiagnostics 主管道 |
| `startupRuntimeDiagnosticsFilePath` | 可选诊断记录文件 |
| `hostScanOptions` | 可选程序集扫描设置 |

## 4. 业务服务

业务服务继续使用标准 .NET 接口，不继承 Runtime 私有服务基类：

```csharp
public sealed class MyBusinessWorker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOneBatchAsync(stoppingToken);
        }
    }
}
```

`BackgroundService` 的 `stoppingToken` 用于宿主业务循环；Runtime 创建的细分执行单元仍应使用 `RProcess`、`RThread`、`RTask`，以便进入 Root 登记、状态和清理流程。

### 4.1 受管任务、清理和状态汇报完整例子

```csharp
public sealed class MyProductWorker(MyProductState businessState) : BackgroundService
{
    private RTask? _managedLoop;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _managedLoop = RuntimeInjector.CreateTask(
            asyncToken => RunManagedLoop(asyncToken),
            unitId: "my-product.task.main-loop",
            category: "service",
            cancellationToken: stoppingToken,
            startImmediately: false);

        _managedLoop.CleanupRequested += async (_, _, cancellationToken) =>
        {
            businessState.TransitionTo("Draining");
            await businessState.FlushAsync(cancellationToken);
            businessState.TransitionTo("Completed");
        };

        _managedLoop.State.SetDetail("service", "MyCompany.MyProductService");
        _managedLoop.Start();
        await _managedLoop;
    }

    private void RunManagedLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            businessState.TransitionTo("Working");
            businessState.RunOneBatch(cancellationToken);
        }
    }
}
```

业务清理结束后不需要手工释放 Runtime 信号或反登记；`RTask` 会根据共享状态正常退出并自动销户。

## 5. 可选业务清理

没有额外资源时不挂钩。Runtime 会立即将清理状态置为 `Completed` 并释放退出信号。

需要清理数据库、文件、租约或业务管道时：

```csharp
worker.CleanupRequested += async (_, args, cancellationToken) =>
{
    await FlushBusinessStateAsync(cancellationToken);
    await ReleaseBusinessLeaseAsync(cancellationToken);
};
```

规则：

- 委托返回 `ValueTask`，禁止 `async void`；
- 所有已挂载钩子同时启动；
- 全部返回后 Runtime 写入 `Completed`；
- 单个钩子异常会被记录，但不会阻止其他钩子；
- 到达主控 deadline 后 Runtime 写入 `Timeout` 并继续退出；
- 重复 Stop 不重复执行钩子或释放信号；
- 业务代码不手工反登记。

## 6. 状态与退出码

共享 `RuntimeState.Code` 是权威状态，轻量信号只负责唤醒：

```text
Working
→ Requested
→ Draining
→ Completed  => 0
→ Timeout    => 124
```

执行入口收到退出信号后重新读取共享状态，根据状态返回退出码，并在真实出口的 `finally` 中自动从 Root 反登记。

业务细分状态通过 `SetDetail`、`TransitionTo`、`TryTransitionTo` 或子任务状态 DLIST 汇报，不另建退出状态体系。

## 7. SCM 与 CLI 的统一流程

```text
SCM Stop/Shutdown ─┐
                   ├→ RuntimeShutdownCoordinator（同一个幂等任务）
CLI shutdown ──────┘
                    → Root 遍历登记
                    → Stop/Wakeup
                    → CleanupRequested
                    → Completed/Timeout
                    → 自动反登记
                    → WindowsServiceLifetime 停止 Host
                    → 向 SCM 返回
```

普通 `Start(...)` 不注册 Windows Service Lifetime。`StartWindowsService(...)` 具有服务上下文检测，因此同一个服务 EXE 在控制台直接运行调试时不会误接管控制台 Lifetime。

## 8. SampleHost

发布包中的 SampleHost 使用同一份 `Program.cs` 演示两种模式：

```powershell
# 普通控制台模式
Iwesun.Runtime.SampleHost.exe

# Windows Service 启动声明模式
Iwesun.Runtime.SampleHost.exe --windows-service
```

安装为真实 Windows 服务时，在服务 ImagePath 中保留 `--windows-service` 参数。服务安装、账户、恢复策略和权限属于部署配置，不由 Runtime 自动修改。

## 9. 验收清单

- [ ] 普通程序继续调用 `Start(...)`；
- [ ] 服务程序使用直接 `StartWindowsService(options, ...)`，或 API 配置加 `StartConfiguredWindowsService(...)`，二选一；
- [ ] 业务服务保持标准 `IHostedService`/`BackgroundService`；
- [ ] 业务执行单元使用 `RProcess/RThread/RTask`；
- [ ] 清理钩子返回 `ValueTask`，没有 `async void`；
- [ ] SCM Stop 与 CLI shutdown 都能进入协调退出；
- [ ] 正常退出码为 0，超时退出码为 124；
- [ ] 退出后 Root 阻塞登记为空；
- [ ] 控制台调试没有误启用 Windows Service Lifetime；
- [ ] Debug/Release 使用对应 Diagnostics DLL，并携带 `Microsoft.Extensions.Hosting.WindowsServices` 10.0.9。

## 10. 常见错误

- 同时调用 `StartWindowsService()` 和 `AddWindowsService()`：会形成重复 Lifetime 配置；
- 既不直接传 options，也不调用 `ConfigureRuntimeWindowsService()`：Runtime 不知道业务服务名，会拒绝启动；
- 业务自己实现 `ServiceBase.OnStop()`：会绕开或重复 Runtime 协调退出；
- 在清理钩子中使用 `async void`：Runtime 无法等待真实完成；
- 为每个线程或任务创建独立退出倒计时：破坏唯一总 deadline；
- 在 Stop 路径调用 `Kill()`：不是受控正常出口；
- 对仍存活的执行单元提前反登记：会造成 Root 状态与真实执行不一致。

## 11. 服务诊断与 CLI

Windows Service 的状态快照可以继续通过显式反射白名单提供。读取属性使用生产安全的 `reflection.get`；调用方法仅限 Debug：

```csharp
RuntimeInjector.Data(hub, "my-service.status", serviceStatus, new RuntimeDiagnosticObjectAccess
{
    AllowReadAllPublic = false,
    ReadableMembers = ["State", "UpdatedAt"],
    InvokableMembers =
    [
#if DEBUG
        "GetPeerStatuses", "GetAgentSnapshots", "GetSnapshot"
#endif
    ]
});
```

```powershell
# Debug Diagnostics
iwrt reflection.invoke my-service.status GetPeerStatuses
iwrt reflection.invoke my-service.status GetAgentSnapshots
iwrt reflection.invoke my-service.status GetSnapshot

# Debug 和 Release 均可读取白名单属性
iwrt reflection.get my-service.status State
```

`reflection.invoke` 只支持 `InvokableMembers` 中登记的无参数方法。Debug 未登记方法返回拒绝；Release Diagnostics 不编译 invoke 路由并返回 `Unsupported action: invoke`。不要为每个业务方法另建一条 CLI 命令，统一使用通用 invoke 协议。
- 当前标准是单进程单 Runtime Host：同一 provider 重复 `Activate` 幂等，不同 provider 激活会抛出 `RuntimeHostConfigurationException`，不得静默覆盖上下文。
- 同一 `IServiceCollection` 使用相同参数重复 `Start` 幂等；参数冲突在 DI 阶段抛出 `RuntimeHostConfigurationException`，不是 CLI Frame 错误。
- `RuntimeInjector.Thread/Task` 只是描述性登记；`RThread/RTask` 才是实际受管执行。`BackgroundService` 属于 Generic Host 执行链，不因描述性登记而变成 RTask。

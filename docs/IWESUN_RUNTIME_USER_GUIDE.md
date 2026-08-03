# Iwesun Runtime 用户手册

> **状态**：CURRENT  
> **适用**：.NET 10 业务宿主  
> **CLI v3**：CURRENT；复合命令通过协议 batch 一次执行
> **源码样例**：`Iwesun.Runtime.SampleHost`

本手册按实际接入顺序说明如何把现有业务程序改造为 Iwesun Runtime 宿主。架构原理参见 [IWESUN_RUNTIME_DESIGN.md](IWESUN_RUNTIME_DESIGN.md)，诊断内核参见 [RUNTIME_DIAGNOSTICS.md](RUNTIME_DIAGNOSTICS.md)。

## 0. 宿主最低依赖版本

当前发布版使用 `Microsoft.Extensions.* 10.0.9`。宿主的 Hosting、DependencyInjection、Logging 及其相关 Extensions 包不得低于 10.0.9，也不得继续混用旧 preview 包。

旧 preview 宿主可能在引用阶段编译成功，但运行时因程序集版本和 API 绑定不一致而崩溃。迁移前应统一升级相关 PackageReference，清理宿主 `bin/obj/publish`，再分别构建 Debug 和 Release。

Runtime 正式发布 DLL 的 AssemblyVersion/FileVersion 与 MSI ProductVersion 同步，例如 `1.0.26.0` 对应安装包 `1.0.26`；InformationalVersion 用于显示发布标识。

## 1. 替换程序启动主程序

### 1.1 标准 Program.cs

新宿主统一使用 `RuntimeHostTemplate.Start` 和 `Activate`。不再在业务主程序里分散调用 `AddRuntimeDiagnostics`、`UseRuntimeDiagnostics` 和 `BuildDiagnosticRegistries`。

```csharp
using System.Reflection;
using Iwesun.Runtime.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Fixed diagnostics declarations.
[assembly: DiagnosticPipePrefix("MyProduct")]
[assembly: DiagnosticFileOutput(
    "logs/my-product-diagnostics.jsonl",
    FileWriteMode.CreateNew,
    Format = DiagnosticFileFormat.CompactJson)]

var builder = Host.CreateApplicationBuilder(args);

// Fixed Runtime host template.
builder.Logging.AddRuntimeDiagnostics();

var runtimeDirectory = Path.Combine(AppContext.BaseDirectory, "runtime");
builder.Services.Start(
    runtimeDirectory,
    startupRuntimeDiagnosticsPipeName: "MyProduct.RuntimeDiagnostics");

// Business registration area.
builder.Services.AddSingleton<MyBusinessState>();
builder.Services.AddHostedService<MyBusinessWorker>();

using var host = builder.Build();

// Fixed activation point: starts diagnostics and builds the host registries.
host.Services.Activate(Assembly.GetExecutingAssembly());

// Optional business reflection registration. Keep an explicit whitelist.
var hub = host.Services.GetRequiredService<RuntimeDiagnosticHub>();
var businessState = host.Services.GetRequiredService<MyBusinessState>();
RuntimeInjector.Data(hub, "my-product.state", businessState, new RuntimeDiagnosticObjectAccess
{
    AllowReadAllPublic = false,
    ReadableMembers = ["CurrentPhase", "UpdatedAt", "Snapshot"],
    WritableMembers = ["PauseRequested"],
    InvokableMembers =
    [
#if DEBUG
        "Snapshot"
#endif
    ]
});

await host.RunAsync();
```

#### Windows Service 主程序

Windows Service 不需要业务项目自行组合 SCM 回调和 Runtime 协调退出，只把普通 `Start(...)` 换成：

```csharp
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
```

其余 `AddHostedService`、`Build`、`Activate` 和 `RunAsync` 完全相同。SCM Stop/Shutdown 会先进入 `RuntimeShutdownCoordinator`，完成 Root 广播、清理事件、状态与反登记，再由标准 `WindowsServiceLifetime` 停止 Host。CLI shutdown 与 SCM Stop 共用同一幂等任务；CLI 未显式提供期限时同样采用这里的 `ShutdownTimeout`，不会退回固定 5 秒。该入口具有 Windows Service 上下文检测，服务程序在控制台直接运行时不会误接管控制台 Lifetime。

如果服务身份不希望写在 `Program.cs`，固定主程序调用 `StartConfiguredWindowsService(...)`，业务 DI 模块通过 `ConfigureRuntimeWindowsService(options => ...)` 提供 `ServiceName/DisplayName/Description/ShutdownTimeout`。Runtime 不知道业务服务名，也不提供默认名称。

### 1.2 固定区与业务区

| 内容 | 位置 | 是否按业务修改 |
| --- | --- | --- |
| `Logging.AddRuntimeDiagnostics()` | Builder 创建后 | 否 |
| `Services.Start(...)` | DI 构建期 | 只改运行目录和启动管道参数 |
| `Services.StartWindowsService(...)` | 仅 Windows Service 替换 `Start` | 改服务名、总退出期限和启动参数 |
| 业务 DI | `Start` 之后、`Build` 之前 | 是 |
| `Activate(hostAssembly)` | `Build` 之后 | 否 |
| `RuntimeInjector.Data` | `Activate` 之后 | 是，必须白名单 |
| `RunAsync()` | 最后 | 否 |

### 1.3 启动配置优先级

诊断主管道名和文件路径在启动期一次性决议：

1. 命令行启动参数 `--diag-pipe` / `--diag-file`。
2. `Services.Start(...)` 和程序集属性中的源码声明。
3. Runtime 编译默认值。

Runtime 不再读取、创建或写回 `diagnostic-switchboard.json`。路径和管道名不允许在业务运行期修改；CLI 输出开关只影响当前进程，重启后恢复源码默认值。

### 1.4 旧主程序放置与一次性替换步骤

不要在同一工程中保留两个可编译的顶级 `Program.cs`。标准迁移顺序：

1. 在宿主工程下创建 `_migration` 目录。
2. 把原 `Program.cs` 复制为 `_migration/Program.before-runtime.cs.txt`；扩展名必须是 `.txt`，确保不参与编译。
3. 用发布包 `samples/templates/RuntimeHost.Startup.Minimal.Template.cs.txt` 的内容整体替换现有 `Program.cs`。该最小模板的业务注册区为空时也能编译启动；HostedWorker、真实 RTask 和 Reflection 示例分别从 `RuntimeHost.DiagnosticsExamples.Template.cs.txt`、`RuntimeHost.ManagedWorker.Template.cs.txt` 按需复制。旧 `RuntimeHost.Startup.Template.cs.txt` 仅是废弃迁移参考。
4. 只把原主程序的业务 DI 注册、配置加载和 HostedService 注册移入模板标记的 `Business registration area`。
5. 不要把旧的 `Build()`、`Run()`、`RunAsync()`、手工 Diagnostics 初始化一起复制回来。
6. 构建通过并完成启动/退出测试后，再决定是否删除 `_migration` 备份。

建议目录：

```text
YourHost/
├─ Program.cs
├─ _migration/
│  └─ Program.before-runtime.cs.txt
└─ YourHost.csproj
```

### 1.5 主管道和主记录文件集中配置

以下程序集属性必须集中写在新 `Program.cs` 顶部，位于所有类型和顶级语句之前：

```csharp
[assembly: DiagnosticPipePrefix("MyProduct")]
[assembly: DiagnosticFileOutput(
    "logs/my-product-diagnostics.jsonl",
    FileWriteMode.CreateNew,
    Format = DiagnosticFileFormat.CompactJson)]
```

- `DiagnosticPipePrefix("MyProduct")` 定义宿主管道前缀。Runtime 根据前缀形成控制、数据和事件管道；不要在业务代码各处硬编码全名。
- 文件路径相对运行目录解析；也可使用绝对路径，例如 `C:\ProgramData\MyProduct\logs\diagnostics.jsonl`。
- `CompactJson` 适合机器读取，`PrettyJson` 适合人工检查，`PlainText` 适合普通文本日志。

写入模式：

| 模式 | 行为 | 示例结果 |
| --- | --- | --- |
| `FileWriteMode.Append` | 启动后继续追加同一文件 | `diagnostics.jsonl` |
| `FileWriteMode.Overwrite` | 启动时截断旧文件，再写入 | `diagnostics.jsonl` |
| `FileWriteMode.CreateNew` | 每次启动自动增加时间后缀 | `diagnostics_20260712T113500.jsonl` |

只修改上面这一处即可改变默认策略。命令行参数可以在启动期临时覆盖默认值；Runtime 不使用配置 JSON，业务运行中不能修改主管道名或文件模板路径。

## 2. 替换进程、线程和任务创建

### 2.1 统一替换表

| 原创建方式 | 标准方式 | 返回对象 |
| --- | --- | --- |
| `Process.Start(...)` | `RuntimeInjector.CreateProcess(...)` | `RProcess` |
| `new Process()` | `new RProcess(unitId)` 或 `CreateProcess` | `RProcess` |
| `new Thread(...)` | `RuntimeInjector.CreateThread(...)` | `RThread` |
| `Task.Run(...)` / `new Task(...)` | `RuntimeInjector.CreateTask(...)` | `RTask` |

`RProcess`、`RThread`、`RTask` 会自动登记执行状态、接收简单 FIFO 指令，并在真实结束后反登记。

### 2.2 全工程字符串替换顺序

按以下顺序逐步执行全局查找，完成一步就构建一次；不要一次替换全部后再处理错误：

| 步骤 | 全局查找 | 替换目标 | 随后必须检查 |
| --- | --- | --- | --- |
| 1 | `Process.Start(` | `RuntimeInjector.CreateProcess(` | 参数是否需要拆成 `fileName/arguments/unitId` |
| 2 | `new Process()` | `new RProcess(unitId)` | `StartInfo`、`EnableRaisingEvents` 和释放逻辑 |
| 3 | `new Thread(` | `RuntimeInjector.CreateThread(` | `ThreadStart`、名称、STA/UI 线程例外 |
| 4 | `Task.Run(` | `RuntimeInjector.CreateTask(` | lambda 是否接收 `CancellationToken` |
| 5 | `new Task(` | `RuntimeInjector.CreateTask(` | scheduler、返回值和启动时机 |
| 6 | `Process ` / `Process?` | `RProcess ` / `RProcess?` | 字段、参数、返回类型与集合泛型 |
| 7 | `Thread ` / `Thread?` | `RThread ` / `RThread?` | 不再依赖原生 Thread 转型 |
| 8 | `Task ` / `Task?` | `RTask ` / `RTask?` | `Task<T>` 不可机械替换 |

类型定义也要同步。例如：

```csharp
// Before
private Process? _workerProcess;
private Thread? _workerThread;
private Task? _workerTask;
private readonly List<Task> _runningTasks = [];

// After
private RProcess? _workerProcess;
private RThread? _workerThread;
private RTask? _workerTask;
private readonly List<RTask> _runningTasks = [];
```

创建方法的返回类型同样修改：

```csharp
// Before
private Process StartWorker() { ... }

// After
private RProcess StartWorker() { ... }
```

以下文本不要全局替换：`Task<T>`、方法返回 `Task` 的普通 async API、`BackgroundService.ExecuteAsync`、UI Dispatcher/STA 线程以及框架要求固定基类型的成员。它们应逐处判断。

### 2.3 进程

```csharp
// Before:
using var oldProcess = Process.Start("worker.exe", "--scan");

// After:
using RProcess process = RuntimeInjector.CreateProcess(
    fileName: "worker.exe",
    arguments: "--scan",
    unitId: "my-product.process.scanner",
    startImmediately: true);

await process.WaitForExitAsync(cancellationToken);
if (process.ExitCode != 0)
{
    throw new InvalidOperationException($"Scanner exited with {process.ExitCode}.");
}
```

如果需要先设置 `ProcessStartInfo`：

```csharp
using var process = RuntimeInjector.CreateProcess(
    new ProcessStartInfo("worker.exe", "--scan")
    {
        UseShellExecute = false,
        RedirectStandardOutput = true
    },
    unitId: "my-product.process.scanner",
    startImmediately: false);

process.Start();
```

行为要点：

- `Start()` 失败会回滚登记、反射目标和分支管道租约。
- 正常退出事件触发后才反登记。
- `Dispose()` 只请求 Stop；子进程从自己的托管主出口返回后才反登记，不调用 `Kill()`，也不把仍存活的 OS 进程伪装成已注销。
- 子进程使用自己的分支管道和 FIFO，不与父进程竞争同一消费句柄。

### 2.4 线程

```csharp
using RThread worker = RuntimeInjector.CreateThread(
    start: WorkerLoop,
    unitId: "my-product.thread.worker",
    name: "My Product Worker",
    lifetime: RuntimeExecutionLifetime.Static,
    kind: RuntimeThreadKind.Worker,
    owner: "my-product",
    sourceLocation: "Workers/MyWorker.cs",
    startImmediately: false);

worker.CleanupRequested += async (_, e, cancellationToken) =>
{
    await FlushBusinessStateAsync(cancellationToken);
};
worker.ExitCompleted += (_, e) =>
{
    // e.ExitCode == 0 means normal completion; 124 means timeout.
};

worker.Start();
worker.Join(TimeSpan.FromSeconds(10));
```

`RThread` 是对 `System.Threading.Thread` 的组合包装，不是继承类。Stop 先把共享状态写为 `Requested/Draining`，等待全部 `CleanupRequested` 钩子结束；随后写入 `Completed` 并释放轻量退出信号。只有主控 deadline 可以写入 `Timeout`，对应退出码 124；单元没有私有退出倒计时。

`CleanupRequested` 同样存在于 `RProcess` 和 `RTask`。它返回 `ValueTask`，禁止使用 `async void`；多个钩子同时启动，全部返回后 Runtime 才写 `Completed`。无人订阅时立即完成，重复 Stop 不会重复执行钩子。

### 2.5 任务

```csharp
using RTask task = RuntimeInjector.CreateTask(
    action: asyncToken =>
    {
        while (!asyncToken.IsCancellationRequested)
        {
            RunOneBatch();
            asyncToken.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(250));
        }
    },
    unitId: "my-product.task.batch",
    category: "batch",
    threadId: "my-product.thread.worker",
    lifetime: RuntimeExecutionLifetime.Dynamic,
    sourceLocation: "Workers/BatchWorker.cs",
    cancellationToken: cancellationToken,
    startImmediately: true);

await task;
```

`RTask` 是包含内部 `Task` 的组合类。它通过 `GetAwaiter()` 支持 `await`，但不能当作 `Task<T>` 使用。带 `Action<CancellationToken>` 的构造路径能在收到 FIFO Stop 时取消。非 token-aware 任务只能等待业务自然完成。

运行中调用 `Dispose()` 不会提前反登记；释放将延迟到真实任务完成。

### 2.6 不可机械替换的场景

- 需要 `Task<T>` 返回值的调用链必须先重构结果传递。
- UI 主线程、COM STA 线程或自定义消息泵不能直接机械替换。
- 明确依赖特定 `TaskScheduler` 的代码应手动传入 scheduler。
- 依赖将对象转型为原生 `Task` 的旧代码不能直接使用 `RTask`。

### 2.7 专用业务管道与统一代理

Runtime 统一管理 RuntimeDiagnostics、Management、WebRuntime 及其他业务管道，但不让它们共用同一个物理通道。

1. 业务宿主以登记键向 `RuntimePipeRegistry` 申请专用管道。
2. Runtime 执行重名避让并保存 `Name / RequestedPipeName / ResolvedPipeName`。
3. CLI 请求进入 RuntimeDiagnostics 控制管道，使用统一 `RuntimeDiagnosticFrame`。
4. `RuntimeProxyCommandTarget` 按登记租约解析目标，把 frame 转发到 Management 或 WebRuntime 专用管道。
5. 专用业务宿主执行命令；退出时释放服务端实例并注销租约。

这套结构统一了 JSON、登记、寻址和生命周期，同时保持物理隔离。禁止把 Management、WebRuntime 业务消息直接混发到 RuntimeDiagnostics 管道，也不应由业务项目另建一套脱离 Runtime 的管道登记表。

### 2.8 WebRuntime 公共平台与 C# 截获转接

`Iwesun.Runtime.WebView2` 不再维护第二套 WebRuntime 线协议。所有请求和响应都使用 `RuntimeDiagnosticFrame`：

```json
{
  "header": {
    "schema": "rtdiag/2.0",
    "frameType": "request",
    "category": "instruction",
    "operation": "invoke",
    "requestId": "req-1",
    "correlationId": "workflow-1",
    "source": "my-client",
    "destination": "MyProduct.WebRuntime"
  },
  "command": {
    "domain": "web.runtime",
    "target": "openai-web",
    "action": "input.keyboard.press",
    "args": {
      "programId": "my-product.webview2",
      "key": "Enter"
    }
  }
}
```

响应只使用 Frame 的 `status / data / meta`。不得再增加业务自己的 `schema / module / success / error` Envelope，也不得通过字符串 JSON 隐藏结构化状态。

业务方通过 `IWebRuntimeBusinessProgram` 提供预先实现并随宿主编译的 C# 程序。`WebRuntimeProgramHost` 负责发现、登记、初始化、停止和注销；`WebRuntimeProgramRegistry` 负责动作白名单、转接、超时、取消、活跃执行、历史和每动作指标。Runtime 不上传源码、不使用 Roslyn、不动态编译，也不解释执行 C# 文本。

C# 截获转接是可选能力：宿主可以保持现有 WebView2 实现；当某动作登记到 C# Program 后，管理层可把该动作完整转交 C#。启用强制截获时，未登记程序或未声明动作必须失败，禁止回退执行 JavaScript。页面自身 JavaScript 不受这一控制面规则影响。

公共监控目标为 `webruntime.programs`，支持：

- `capabilities / snapshot / list / status`
- `execute / cancel`
- `initialize / stop`

快照包含平台协议、程序 Descriptor、动作目录、活跃执行、最近 128 条历史，以及 Started、Completed、Failed、TimedOut、Canceled 指标。

公共请求模型 `WebRuntimeControlRequest` 只是 Command 参数对象，不是线协议 Envelope。它不包含 `Schema`、`Module` 或 `Script`。外部调用必须通过 `WebRuntimeProtocol.CreateRequestFrame` 或 `WebRuntimePipeClient` 构造标准 Frame。

## 3. 注入器、监视记录器与断点

### 3.1 单点注入定义

一个功能的 ID、条件、上下文和调用应集中在业务现场的一个连续代码段中。程序集属性负责静态目录和初始状态，运行代码负责在真实业务位置发布数据。

### 3.2 程序集属性

```csharp
using Iwesun.Runtime.Diagnostics;

[assembly: DiagnosticPipePrefix("MyProduct")]
[assembly: DiagnosticFileOutput(
    "logs/my-product-diagnostics.jsonl",
    FileWriteMode.CreateNew,
    Format = DiagnosticFileFormat.CompactJson)]

[assembly: DiagnosticWatchPoint(
    "my-product.worker.snapshot",
    "worker",
    "snapshot",
    "Worker state snapshot.",
    "Workers/MyWorker.cs")]

#if DEBUG
[assembly: DiagnosticBreakpoint(
    "my-product.worker.pause",
    "worker",
    "Pause before committing a batch.",
    "Workers/MyWorker.cs",
    Enabled = false,
    HitCountTarget = 1)]
[assembly: DiagnosticNumericBreakpoint(
    "my-product.worker.queue-depth",
    "gt",
    100)]
#endif

[assembly: DiagnosticHookableEvent(
    "my-product.worker.completed",
    typeof(MyWorkerState),
    nameof(MyWorkerState.Completed))]
```

| 属性 | 用途 | 默认行为 |
| --- | --- | --- |
| `DiagnosticPipePrefix` | 宿主管道前缀 | 每程序集一个 |
| `DiagnosticFileOutput` | 文件模板、写模式、格式 | `CreateNew` + `CompactJson` |
| `DiagnosticWatchPoint` | 静态监视点目录 | 默认静默 |
| `DiagnosticBreakpoint` | 逻辑断点 | `Enabled=false`；仅 Debug 注入 |
| `DiagnosticNumericBreakpoint` | 断点默认数值谓词 | 仅 Debug 注入 |
| `DiagnosticHookableEvent` | 可动态挂接事件目录 | 挂接状态由运行配置决定 |

Release 保留输出、日志、反射白名单、事件钩子、管道、状态、文件记录和安全退出。断点和数值断点调用必须由 `#if DEBUG` 隔离。

### 3.3 Debug 与 Release 必须引用不同 DLL

> 重点：安装 Runtime 只更新 `C:\Program Files\Iwesun\Runtime`，不会替换业务宿主已经复制到自身 `bin`、`publish` 或安装 staging 中的私有 DLL。

发布包同时提供两个 Diagnostics 编译变体：

```text
C:\Program Files\Iwesun\Runtime\lib\Iwesun.Runtime.Diagnostics\
├─ Iwesun.Runtime.Diagnostics.dll          # Release 兼容副本
├─ Debug\Iwesun.Runtime.Diagnostics.dll    # Compiled=DEBUG，包含断点装配
└─ Release\Iwesun.Runtime.Diagnostics.dll  # Compiled=RELEASE，裁掉断点装配
```

宿主项目应按构建配置选择 DLL：

```xml
<PropertyGroup>
  <IwesunRuntimeRoot>C:\Program Files\Iwesun\Runtime</IwesunRuntimeRoot>
</PropertyGroup>

<PropertyGroup Condition="'$(Configuration)' == 'Debug'">
  <RuntimeLibraryVariant>Debug</RuntimeLibraryVariant>
</PropertyGroup>

<PropertyGroup Condition="'$(Configuration)' == 'Release'">
  <RuntimeLibraryVariant>Release</RuntimeLibraryVariant>
</PropertyGroup>

<ItemGroup>
  <Reference Include="Iwesun.Runtime.Diagnostics">
    <HintPath>$(IwesunRuntimeRoot)\lib\Iwesun.Runtime.Diagnostics\$(RuntimeLibraryVariant)\Iwesun.Runtime.Diagnostics.dll</HintPath>
    <Private>true</Private>
  </Reference>
  <Reference Include="Iwesun.Runtime.Data">
    <HintPath>$(IwesunRuntimeRoot)\lib\Iwesun.Runtime.Data\$(RuntimeLibraryVariant)\Iwesun.Runtime.Data.dll</HintPath>
    <Private>true</Private>
  </Reference>
</ItemGroup>
```

`Private=true` 会把选中的 DLL 复制到宿主输出目录。Debug 应用如果错误加载 Release DLL，`#if DEBUG` 已在 Runtime DLL 编译期裁掉的断点服务无法通过 JSON、CLI 或运行时开关恢复。

`Iwesun.Runtime.Data.dll` 同时提供 Runtime 数据契约和 RecordStore；安装版宿主必须显式引用并复制它，且必须删除任何 `Iwesun.Data.dll` 旧副本。

安装目录`docs\Iwesun.Runtime.Data\`保存RecordStore完整文档树；公共接口见
`docs\Iwesun.Runtime.Data\02-api\RECORD_STORE_PUBLIC_API.md`，当前边界见
`docs\Iwesun.Runtime.Data\RELEASE_STATUS.md`。

从源码开发引用迁移到安装版 DLL 时，必须执行：

1. 删除旧的 Diagnostics `ProjectReference` 或本地 DLL `Reference`，禁止同一程序集保留两条引用路径。
2. 加入上述按 `$(Configuration)` 选择的 Diagnostics 条件引用，并加入唯一的 Runtime Data 固定引用。
3. 删除宿主项目旧的 `bin`、`obj`、`publish` 和安装 staging。
4. 分别重新构建 Debug 与 Release。
5. 验证 Debug 输出中的 DLL 为 `Compiled=DEBUG`，Release 输出为 `Compiled=RELEASE`。
6. 重新生成宿主自己的安装包；Runtime MSI 不会修改其他产品的发布目录。

如果继续使用源码 `ProjectReference`，宿主构建配置通常会传递给 Runtime 项目，Debug/Release 可自动匹配；切换到已安装 DLL 后，则必须显式采用上述条件路径。

仓库中的 `Iwesun.Runtime.SampleHost` 同时演示两种模式：

```powershell
# 源码开发模式（默认）
dotnet build modules/Diagnostics/samples/Iwesun.Runtime.SampleHost/Iwesun.Runtime.SampleHost.csproj -c Debug

# 已安装 Runtime DLL 模式（独立项目，避免复用 ProjectReference 的恢复缓存）
dotnet build modules/Diagnostics/samples/Iwesun.Runtime.SampleHost/installed/Iwesun.Runtime.SampleHost.Installed.csproj -c Debug
dotnet build modules/Diagnostics/samples/Iwesun.Runtime.SampleHost/installed/Iwesun.Runtime.SampleHost.Installed.csproj -c Release
```

可通过 `-p:IwesunRuntimeRoot=...` 覆盖安装根目录，用于企业镜像或非默认部署路径。

### 3.4 输出与 Watch

```csharp
var snapshot = state.Snapshot();

RuntimeInjector.Output(
    outputPointId: "my-product.worker.tick",
    section: "worker",
    kind: "tick",
    message: "Worker cycle completed.",
    payload: new
    {
        snapshot.CurrentPhase,
        snapshot.ProcessedCount,
        snapshot.UpdatedAt
    });

RuntimeInjector.Watch(
    watchPointId: "my-product.worker.snapshot",
    target: snapshot,
    typeName: nameof(MyWorkerSnapshot));
```

这段代码可以放在业务计算完成后的任意位置，不要为了输出而修改业务分支或返回值。

### 3.5 逻辑断点与数值断点

```csharp
#if DEBUG
var context = new
{
    batch.Id,
    QueueDepth = queue.Count,
    Limit = options.QueueLimit
};

await RuntimeInjector.Break(
    breakpointId: "my-product.worker.pause",
    condition: () => batch.RequiresInspection,
    context: context);

await RuntimeOutput.BreakIfNumbers(
    breakpointId: "my-product.worker.queue-depth",
    value1: queue.Count,
    value2: options.QueueLimit,
    context: context);
#endif
```

协作断点只暂停命中的异步调用链，不挂起整个进程。CLI 连接与断点生命周期无关；客户端断开不会自动恢复断点，只有显式 Resume 才恢复。

### 3.6 反射对象注入

```csharp
RuntimeInjector.Data(hub, "my-product.worker", workerState, new RuntimeDiagnosticObjectAccess
{
    AllowReadAllPublic = false,
    ReadableMembers = ["CurrentPhase", "QueueDepth", "UpdatedAt", "Snapshot"],
    WritableMembers = ["PauseRequested"],
    InvokableMembers =
    [
#if DEBUG
        "Snapshot", "RequestScan"
#endif
    ]
});
```

不要把业务对象的所有公开方法暴露给诊断管道。写和调用权限必须按成员显式白名单。`InvokableMembers` 和业务调试调用登记应放在 `#if DEBUG` 中；Release Diagnostics 不编译 invoke 路由，CLI `reflection.invoke` 会返回不支持。

## 4. 业务状态、事件与安全退出

### 4.1 状态分层

| 层级 | API | 用途 |
| --- | --- | --- |
| 全局生命周期 | `RuntimeManagedRegistry.GlobalLifecycleState` / `RuntimeStateManager` | Start、Working、Stop、Exit 等全程序状态 |
| 单元主状态 | `IRManagedState.TransitionTo` | 进程、线程、任务粗粒度状态 |
| 业务细分状态 | `SetDetail` / `RuntimeStateHistory` | 扫描阶段、队列深度、清理进度等 |

```csharp
worker.State.SetDetail("scanPhase", "enumerating");
worker.State.SetDetail("currentPath", currentPath);

if (!worker.State.TryTransitionTo("Working"))
{
    // The requested transition is invalid from the current state.
}

worker.State.AppendSubTaskState("Working");
```

`TransitionTo` 在转换无效时抛异常；`TryTransitionTo` 返回 `false`。成功转换后 `RManagedState` 自动 Sync，并向主控 FIFO 发送简单数值状态指令。大量上下文数据应继续通过 JSON 管道传输。

### 4.2 业务事件

```csharp
public sealed class MyWorkerState
{
    public event EventHandler<MyWorkerCompletedEventArgs>? Completed;

    public void Complete(string batchId)
    {
        Completed?.Invoke(this, new MyWorkerCompletedEventArgs(batchId));
    }
}

public sealed record MyWorkerCompletedEventArgs(string BatchId);
```

程序集登记：

```csharp
[assembly: DiagnosticHookableEvent(
    "my-product.worker.completed",
    typeof(MyWorkerState),
    nameof(MyWorkerState.Completed))]
```

实例事件钩子使用弱引用，不应阻止业务对象回收。静态事件没有实例生命周期，必须在停机时显式 Detach。

### 4.3 标准退出流程

1. 主控发布全局“准备关机/Stop”状态和退出 deadline。
2. 主控遍历当前注册表，逐个向进程、线程和任务 FIFO 发送 `Stop` 和 `Wakeup`。
3. 每个守护程序先写 `Requested`，再执行全部 `CleanupRequested` 钩子并写 `Draining`；无人订阅时立即完成。
4. 钩子全部返回时写 `Completed` 并释放单元退出信号；到达唯一的主控 deadline 时写 `Timeout` 并释放信号。
5. 执行入口醒来后依据共享状态返回 0 或 124，并在真实结束时销户登记、FIFO handler、管道和反射目标。
6. 主控持续检查 Root 下的扁平进程/线程/任务登记和相关 RuntimeStateHistory；全部清空后返回 0，deadline 到期仍有待退出单元时返回 124。

`Stop`、`Exit`、`Completed` 和 `Timeout` 均属于退出阶段。进入其中任一状态后，新的托管工作会在注册前被拒绝，避免监督器在退出完成或超时后重新拉起业务线程。

对于 AIGateway Server 这类分支数量较大的宿主，应把真实业务分支登记为阻塞单元，把指标采集、状态投影等纯观察器登记为 `BlocksShutdown=false`。两者都受中央终态准入门约束；`BlocksShutdown=false` 只表示“不等待它”，不表示退出后仍可创建。监管器收到拒绝后必须结束补建循环，不能用新 UnitId 重试绕过门禁。所有业务清理共享首次 shutdown 冻结的唯一 deadline，后续 CLI、SCM 或内部调用不会延长期限。

`RThread` 和 `RProcess` 使用对象初始化器设置非阻塞观察器属性，例如 `new RThread(Observe) { BlocksShutdown = false }`。UnitId 在活动登记期间必须唯一；重复 UnitId 和同一包装器的重复/并发 Start 都会同步抛出 `InvalidOperationException`。

```csharp
var shutdown = host.Services.GetRequiredService<RuntimeShutdownCoordinator>();
var result = await shutdown.ShutdownAsync(
    timeout: TimeSpan.FromSeconds(15),
    payload: "operator-request",
    cancellationToken: cancellationToken);

Environment.ExitCode = result.ExitCode;
```

标准 CLI 的位置参数为：

```text
iwrt lifecycle.shutdown [graceful] [timeoutMs] [payload]
```

期限选择顺序为 `timeoutMs`、兼容字段 `countdownMs`、宿主 `ShutdownTimeout`、30 秒回退值。响应中的 `requestId`、`timeoutMs`、`deadlineUtc` 是协调器实际接受的值；并发重复请求不得回显未采用的新期限。

`graceful` 当前只能为 `true`。传入 `false` 返回 `NON_GRACEFUL_SHUTDOWN_NOT_SUPPORTED`，不会执行未定义的强制终止。`ShutdownAsync` 的调用方 token 只取消等待；一旦请求被接受，协调器仍会在原 deadline 内完成或超时。

### 4.4 守护程序：轮询保底 + FIFO 唤醒

```csharp
while (!cancellationToken.IsCancellationRequested)
{
    if (managedRegistry.IsGlobalStopOrExitRequested)
    {
        await CleanupAsync(cancellationToken);
        return;
    }

    await RunOneCycleAsync(cancellationToken);

    // Keep this wait interruptible. FIFO Wakeup/Stop is the fast path;
    // polling the global state remains the fallback.
    await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
}
```

实际 `RProcess`、`RThread`、`RTask` 会注册内部命令 handler。业务守护程序必须保持清理可重入，因为全局状态轮询和 FIFO Stop 可能几乎同时触发。

## 5. JSON 与 CLI 清单

### 5.1 Wire format

所有 Runtime 命名管道 JSON 使用：

```text
[4-byte little-endian signed int32 payload length][N-byte UTF-8 JSON]
```

必须循环读取直到满 4 字节头部和完整 payload，不能假设一次 `ReadAsync` 就获得完整帧。CLI 已封装 framing，业务代码不应手写管道客户端。

### 5.2 rtdiag/2.0 单命令

```json
{
  "header": {
    "schema": "rtdiag/2.0",
    "frameType": "request",
    "category": "query",
    "operation": "get",
    "requestId": "2a6f1fa9f6774f11a5af862ea6c9741b",
    "timestamp": "2026-07-12T10:00:00Z",
    "source": "cli",
    "destination": "runtime"
  },
  "command": {
    "domain": "pipes",
    "target": "diagnostics.pipes",
    "action": "list",
    "args": {
      "includeInactive": false
    }
  }
}
```

响应：

```json
{
  "header": {
    "schema": "rtdiag/2.0",
    "frameType": "response",
    "correlationId": "2a6f1fa9f6774f11a5af862ea6c9741b",
    "source": "runtime",
    "destination": "cli"
  },
  "status": {
    "ok": true,
    "code": "OK",
    "message": "",
    "retryable": false
  },
  "data": [],
  "meta": {
    "durationMs": 1
  }
}
```

### 5.3 rtdiag/3.0 结构化 batch

```json
{
  "header": {
    "schema": "rtdiag/3.0",
    "frameType": "request",
    "category": "batch",
    "operation": "execute",
    "requestId": "59b5599bb2384a8394f7d43b142c05d7",
    "source": "cli",
    "destination": "runtime"
  },
  "batch": {
    "options": {
      "stopOnError": true,
      "deadlineMs": 30000
    },
    "steps": [
      {
        "id": "list-pipes",
        "command": {
          "domain": "pipes",
          "target": "diagnostics.pipes",
          "action": "list"
        }
      },
      {
        "id": "get-runtime-state",
        "command": {
          "domain": "runtime",
          "target": "runtime.managed",
          "action": "lifecycle"
        },
        "continueOnError": false,
        "delayMs": 0
      }
    ]
  }
}
```

### 5.4 CLI v3

CLI v3 已由当前源码和 `RuntimeCliSystemConfig.json` 实施，帮助元数据位于 `RuntimeCliSystemMetadata.json`。配置的权威结构如下：

```json
{
  "schema": "iwesun.runtime.cli/3.0",
  "application": {
    "name": "Iwesun Runtime CLI"
  },
  "endpoints": {
    "diagnostics": {
      "transport": "namedPipe",
      "pipeName": "DdnsSnap.RuntimeDiagnostics"
    }
  },
  "commands": [],
  "composites": [],
  "extensions": {}
}
```

v3 使用小写 dot-style 规范命令，例如 `switchboard.get`、`breakpoint.list`、`pipe.acquire`、`web.programs.status`、`web.snapshot`、`web.mouse.click`。标准 catalog 不预置用户别名；用户可用 `--user-config=PATH` 通过 `extensions.add/extend/replace/disable` 增量安装命令和别名。

WebRuntime CLI 命令使用 `diagnostics` endpoint，先进入 `diagnostics.proxy`，再按 `RuntimePipeRegistry` 中的 `WebRuntime` 租约解析业务管道。代理构造的内部 Frame 使用 `domain=web.runtime`、Target=backendId，ProgramId 作为 typed Args；顶层 Frame Destination 是 RuntimeDiagnostics 管道，内部 Frame Destination 是租约的 ResolvedPipeName。

结构化组合命令统一使用 `composites`，由 CLI 将已有 catalog 命令组装为标准 batch Frame；用户别名和命令扩展同样可用。组合步骤不执行脚本文本，也不提供条件、循环或结果绑定。旧 `workflows` 字段、旧 schema、旧命令名和旧式文本 workflow 步骤均已废止。

历史设计规格已移入 `docs/archive/superpowers/`。当前完整命令表以 `RuntimeCliSystemConfig.json` 为准；当前目录 `RuntimeCliUserConfig.json` 自动加载，`exit/quit` 只退出 Shell。

### 5.5 CLI 操作标准

1. 先查询宿主状态和目标目录。
2. 只启用必要的 section、输出点、断点或钩子。
3. 触发最小真实业务动作。
4. 读取 frame、事件、文件或状态快照。
5. 显式 Resume 所有等待断点。
6. 卸载动态钩子。
7. 运行 quiet 或恢复之前的开关状态。
8. 最后才发送安全退出命令。

CLI 客户端退出不改变服务端断点状态，也不触发宿主退出。

## 附录 A：最小接入检查表

- [ ] 主程序使用 `Services.Start` 和 `Activate`。
- [ ] 配置 Runtime 目录和管道名来源。
- [ ] 进程、线程、任务创建点已检查。
- [ ] 反射对象只暴露显式白名单。
- [ ] 断点调用使用 `#if DEBUG`。
- [ ] 关机通过 `RuntimeShutdownCoordinator`。
- [ ] 诊断默认保持静默。

## 附录 B：完整接入检查表

- [ ] 所有受管单元有稳定且唯一的 unitId。
- [ ] 所有动态单元在真实结束后反登记。
- [ ] 线程和任务有可中断等待或 CancellationToken。
- [ ] 状态转换使用统一 RuntimeState Code。
- [ ] 简单状态指令走 FIFO，复杂数据走管道。
- [ ] 守护程序同时支持全局状态轮询和 FIFO 唤醒。
- [ ] 事件登记和卸载与对象生命周期一致。
- [ ] 正常退出码为 0，退出超时码为 124。
- [ ] 真实 CLI 场景结束后恢复 quiet。

## 附录 C：Debug / Release 能力矩阵

| 能力 | Debug | Release |
| --- | --- | --- |
| 输出 / TracePoint / Log | 保留 | 保留 |
| 文件记录 | 保留 | 保留 |
| 命名管道 | 保留 | 保留 |
| 反射白名单 | 保留 | 保留 |
| 事件钩子 | 保留 | 保留 |
| 状态/执行登记 | 保留 | 保留 |
| 安全退出 | 保留 | 保留 |
| 逻辑断点 | 编译 | 不编译/不装配 |
| 数值断点 | 编译 | 不编译/不装配 |

## 附录 D：旧痕迹删除清单

- [ ] 删除业务代码中分散的 `UseRuntimeDiagnostics` / `BuildDiagnosticRegistries`。
- [ ] 删除用于诊断的 `Console.WriteLine` 和临时文件。
- [ ] 删除未受管的 `Process.Start`、`new Thread`、`Task.Run` 创建点，或在审核后明确保留理由。
- [ ] 删除宽泛反射 `InvokableMembers`。
- [ ] 删除 Release 中未隔离的 Break/Watch 调试注入。
- [ ] 删除“CLI 断开自动恢复断点”的旧逻辑和文档。
- [x] CLI v3 已删除 v2 配置、旧命令名和文本 workflow 步骤；简单组合统一使用 batch 复合命令。

## 附录 E：Codex 接入技能

Runtime 仓库包含可复制技能：

`skills/iwesun-runtime-integration/`

将该目录整体复制到其他 Codex 环境的技能目录后，可以通过 `$iwesun-runtime-integration` 触发主程序替换、受管对象替换、单点诊断注入、状态退出和 JSON/CLI 流程。

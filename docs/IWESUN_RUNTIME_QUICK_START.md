# Iwesun Runtime 接入速查手册

按下面顺序完成宿主、执行对象、诊断、管道、CLI 和 WebRuntime 迁移。完整接口见 `IWESUN_RUNTIME_USER_GUIDE.md`，CLI 命令表见 `IWESUN_RUNTIME_CLI.md`，WebRuntime 规范见同目录 `WEB_RUNTIME_CONTROL.md`。

新架构的边界固定为：

```text
业务代码 -> Runtime Diagnostics/受管执行 API
CLI -> RuntimeDiagnostics -> diagnostics.proxy -> RuntimePipeRegistry 租约 -> 业务专用管道
业务专用管道 -> 业务宿主持有的服务实例 -> 预编译 C# Program/业务处理器
```

Runtime 统一管理管道名称、租约、寻址、代理和 JSON Frame，但 RuntimeDiagnostics、Management、WebRuntime 使用不同的物理管道。

## 1. 备份并替换主程序

把原来的 `Program.cs` 保存为：

```text
_migration/Program.before-runtime.cs.txt
```

再把安装目录中的下列文件复制为新 `Program.cs`：

```text
C:\Program Files\Iwesun\Runtime\samples\templates\RuntimeHost.Startup.Minimal.Template.cs.txt
```

把旧主程序中的 DI、HostedService 和业务注册移到模板的 `Business registration area`。

## 2. Debug/Release 引用不同 DLL

删除旧 `ProjectReference` 和宿主目录里的 Runtime DLL 副本，在 `.csproj` 中加入：

最小模板不要求预先创建 Worker。真实 RTask、HostedWorker 和 Reflection 示例位于独立模板，按业务需要复制。

```xml
<PropertyGroup>
  <RuntimeRoot>$(ProgramFiles)\Iwesun\Runtime</RuntimeRoot>
</PropertyGroup>
<ItemGroup Condition="'$(Configuration)' == 'Debug'">
  <Reference Include="Iwesun.Runtime.Diagnostics">
    <HintPath>$(RuntimeRoot)\lib\Iwesun.Runtime.Diagnostics\Debug\Iwesun.Runtime.Diagnostics.dll</HintPath>
  </Reference>
</ItemGroup>
<ItemGroup Condition="'$(Configuration)' == 'Release'">
  <Reference Include="Iwesun.Runtime.Diagnostics">
    <HintPath>$(RuntimeRoot)\lib\Iwesun.Runtime.Diagnostics\Release\Iwesun.Runtime.Diagnostics.dll</HintPath>
  </Reference>
</ItemGroup>
<ItemGroup>
  <Reference Include="Iwesun.Runtime.Data">
    <HintPath>$(RuntimeRoot)\lib\Iwesun.Runtime.Data\Iwesun.Runtime.Data.dll</HintPath>
  </Reference>
</ItemGroup>
```

`Iwesun.Runtime.Data.dll` 同时提供 Runtime 数据契约和 RecordStore V2；不得再引用 `Iwesun.Data.dll`。
完整资料安装在`docs\Iwesun.Runtime.Data\`；首次接入至少阅读README、V2对外API和发布状态。
根`docs`下同时保留这些常用入口。

然后删除宿主项目旧的 `bin`、`obj`、`publish`，防止继续加载旧副本。

## 3. 全工程分步替换

每替换一步就编译一次：

| 步骤 | 查找 | 替换/改写 |
|---|---|---|
| 进程启动 | `Process.Start(` | `RuntimeInjector.CreateProcess(`，补稳定的 `unitId` |
| 进程对象 | `new Process()` | `new RProcess(unitId)` |
| 线程 | `new Thread(` | `RuntimeInjector.CreateThread(`，补 `unitId`、名称和类型 |
| 轻量任务 | `Task.Run(` | `RuntimeInjector.CreateTask(`，优先使用 `CancellationToken` 参数 |
| 显式任务 | `new Task(` | `RuntimeInjector.CreateTask(`，检查调度器和启动时机 |
| 字段/局部变量 | `Process` / `Thread` / `Task` | 对应改为 `RProcess` / `RThread` / `RTask` |
| 集合 | `List<Task>` | 只对受管轻量任务改为 `List<RTask>` |
| 返回值 | `Process` / `Thread` / `Task` | 仅当返回包装对象时改为 `RProcess` / `RThread` / `RTask` |

不要全局替换 `Task<T>`、普通 `async Task`、`BackgroundService.ExecuteAsync`、UI/STA/COM/message-pump 线程。

完整替换清单位于：

```text
C:\Program Files\Iwesun\Runtime\samples\templates\RuntimeCreation.Replacements.Template.txt
```

## 4. 填主管道和主记录文件

只改新 `Program.cs` 顶部这一处：

```csharp
[assembly: DiagnosticPipePrefix("YourProduct")]
[assembly: DiagnosticFileOutput(
    @"D:\YourProduct\logs\runtime.jsonl",
    FileWriteMode.CreateNew,
    Format = DiagnosticFileFormat.CompactJson)]
```

- `Append`：追加到原文件。
- `Overwrite`：启动时覆盖原文件。
- `CreateNew`：自动生成带时间后缀的新文件。
- 格式可选 `CompactJson`、`PrettyJson`、`PlainText`。
- `DiagnosticPipePrefix("YourProduct")` 生成主管道 `YourProduct.RuntimeDiagnostics`。
- Management、WebRuntime 等业务管道不得与主管道混发消息，但必须通过 `RuntimePipeRegistry` 统一申请、登记、解析和释放。

### Windows Service

服务程序只把 `Services.Start(...)` 替换为：

```csharp
builder.Services.StartWindowsService(
    serviceOptions: new RuntimeWindowsServiceOptions
    {
        ServiceName = "YourCompany.YourProductService",
        DisplayName = "Your Product Service",
        Description = "由业务程序提供的服务说明。",
        ShutdownTimeout = TimeSpan.FromSeconds(30)
    },
    runtimeDirectory: runtimeDirectory,
    startupRuntimeDiagnosticsPipeName: "YourProduct.RuntimeDiagnostics");
```

业务仍使用标准 `IHostedService`/`BackgroundService`，不继承 Runtime 私有基类。Runtime 统一管理 SCM Stop/Shutdown、Root 广播、业务清理、0/124 退出码和反登记；控制台运行该服务程序时不会误启用 Windows Service Lifetime。

也可以在固定主程序调用 `StartConfiguredWindowsService(...)`，然后由业务模块调用 `ConfigureRuntimeWindowsService(...)` 提供服务名、显示名、说明和总退出期限。Runtime 不预设业务服务身份。

## 5. 编译

```powershell
dotnet build YourHost.csproj -c Debug
dotnet build YourHost.csproj -c Release
```

Debug 必须加载安装目录 `Debug` DLL，Release 必须加载 `Release` DLL；安装器不会替换宿主自己的 `bin/publish` 副本。

## 6. 直接查看已验证样例

```text
C:\Program Files\Iwesun\Runtime\samples\source\Iwesun.Runtime.SampleHost\installed\Iwesun.Runtime.SampleHost.Installed.csproj
```

该项目就是上述做法的可编译样例，可直接复制后改产品名、业务注册、管道和日志路径。

## 7. 使用统一 JSON Frame

所有 RuntimeDiagnostics、Management、WebRuntime 请求、响应和事件统一使用 `RuntimeDiagnosticFrame`。线格式固定为：

```text
[4 字节 little-endian Int32 JSON 长度][UTF-8 JSON]
```

不要假设一次 `ReadAsync` 能读完头部或负载；优先使用 Runtime CLI 和公共客户端，不要自行实现管道协议。

单命令使用 `rtdiag/2.0`：

```json
{
  "header": {
    "schema": "rtdiag/2.0",
    "frameType": "request",
    "category": "query",
    "operation": "get",
    "requestId": "req-1",
    "correlationId": "session-1",
    "source": "my-host",
    "destination": "YourProduct.RuntimeDiagnostics"
  },
  "command": {
    "domain": "switchboard",
    "target": "diagnostics.switchboard",
    "action": "snapshot",
    "args": {}
  }
}
```

响应状态放在 `status`，业务结果放在 `data`，扩展信息放在 `meta`。禁止再增加私有 `schema/module/success/error` Envelope，也不要把结构化 JSON 塞入字符串字段。

批处理使用 `rtdiag/3.0`，包含 1–128 个结构化步骤。CLI 的 `composites` 是受限的简单 batch：只引用现有命令，不执行脚本文本，不提供循环或任意代码执行。

## 8. 登记业务专用管道

业务宿主负责创建和持有实际命名管道服务端；Runtime 负责登记与寻址。每个租约必须保留三个独立字段：

| 字段 | 含义 |
|---|---|
| `Name` | 注册表中的稳定登记键，例如 `WebRuntime` |
| `RequestedPipeName` | 业务方申请的原始名称 |
| `ResolvedPipeName` | Runtime 排他占用并避让重名后的真实名称 |

名称冲突时依次使用 `_001`、`_002`。申请、排他创建和租约登记必须是一个原子过程；不得试创建后立即释放。宿主退出顺序为：停止接收请求、等待业务处理完成、关闭服务端、释放租约。

CLI 只连接 RuntimeDiagnostics，再由代理按租约转发：

```text
RuntimeDiagnostics -> diagnostics.proxy -> RuntimePipeRegistry -> ResolvedPipeName
```

不得硬编码解析后的管道名，也不得由业务项目另建一套脱离 Runtime 的注册表。

## 9. 使用 CLI v3

当前配置 schema 为 `iwesun.runtime.cli/3.0`，命令以安装目录中的 `RuntimeCliSystemConfig.json` 为准；帮助元数据位于 `RuntimeCliSystemMetadata.json`。当前目录的 `RuntimeCliUserConfig.json` 会自动加载，显式 `--user-config` 优先。

```powershell
iwrt --pipe=YourProduct.RuntimeDiagnostics switchboard.get
iwrt --pipe=YourProduct.RuntimeDiagnostics pipe.list
iwrt --pipe=YourProduct.RuntimeDiagnostics web.programs.status
```

用户配置必须显式传入：

```powershell
iwrt --user-config="C:\ProgramData\Iwesun\Runtime\config\Iwesun.Runtime.Cli.user.json" --pipe=YourProduct.RuntimeDiagnostics runtime.inspect
```

用户配置支持别名、命令扩展/替换/禁用和结构化 `composites`。旧 v2 schema、旧命令名、`workflows` 和文本 workflow 步骤均已废止。CLI 断开不会恢复断点，也不会停止宿主。

## 10. 接入 WebRuntime C# Program

`Iwesun.Runtime.WebView2` 提供公共会话、虚拟鼠标、虚拟键盘、输入分发、程序登记和监控能力。业务宿主通过 `IWebRuntimeBusinessProgram` 提供已经实现并随宿主编译的 C# 程序，由 `WebRuntimeProgramHost` 管理初始化、登记、停止和注销。

`WebRuntimeProgramRegistry` 负责：

- Program 和动作白名单；
- 请求转接、超时和取消；
- 活跃执行、最近历史和每动作指标；
- 统一结果、错误 Code 和 Retryable 状态。

WebRuntime 调用仍走标准链路：

```powershell
iwrt --pipe=YourProduct.RuntimeDiagnostics web.snapshot openai-web
iwrt --pipe=YourProduct.RuntimeDiagnostics web.mouse.click openai-web 320 180
iwrt --pipe=YourProduct.RuntimeDiagnostics web.keyboard.type openai-web "hello"
```

`WebRuntimeControlRequest` 只是 Frame Command 的 typed Args，不是线协议 Envelope。控制面不接受调用方提供的 JavaScript；强制截获模式下，未登记 Program 或动作必须失败，禁止回退到脚本执行。网页自身为了正常工作而运行的 JavaScript 不受此限制。

## 11. 添加诊断、监视和断点

- 业务输出使用 `RuntimeOutput.TracePoint()` / `Log()` 或 `RuntimeInjector.Output/Data`，不要增加临时 `Console.WriteLine` 或调试文件。
- 诊断默认保持静默，只启用当前检查需要的最小 Section/Point，完成后执行 `quiet` 恢复。
- `Watch`、`BreakIf`、事件钩子和数值断点放在业务位置，并使用连续单点块；断点和监视注入放入 `#if DEBUG`。
- 反射目标必须显式列出可读、可写和可调用成员，禁止开放任意反射调用。
- `BreakIf` 只异步暂停当前调用链；其他线程继续运行。只有显式 resume 才恢复，CLI 连接生命周期不改变断点状态。
- 实例事件钩子使用弱引用；静态事件必须在退出时显式解绑。

## 12. 状态、Stop/Wakeup 与协调退出

受管进程、线程和任务全部直接登记到 Root。退出阶段使用共享状态 `Requested → Draining → Completed/Timeout`；轻量退出信号只负责唤醒，入口醒来后必须依据状态返回 0 或 124。业务可挂载 `CleanupRequested` 异步清理钩子；无人挂载时立即进入 `Completed`。

```csharp
unit.CleanupRequested += async (_, _, cancellationToken) =>
    await FlushBusinessStateAsync(cancellationToken);
```

该钩子必须返回 `ValueTask`，不得使用 `async void`。所有钩子完成后 Runtime 自动释放退出信号；业务代码不需要手工反登记。

宿主必须同时处理：

1. 全局状态轮询；
2. FIFO `Stop/Wakeup` 控制；
3. `CancellationToken`；
4. Runtime 安全 shutdown 命令。

FIFO 只传固定宽度的简单状态或控制值，复杂数据使用 Frame JSON 管道。Dispose 或等待超时不代表工作已经结束；只有底层进程、线程或任务实际完成后才能注销执行记录和释放相关资源。

推荐退出顺序：停止接收新工作 → 广播取消/Stop → 恢复需要收尾的等待点 → 等待受管执行对象结束 → 停止业务管道 → 释放租约 → `RuntimeShutdownCoordinator.ShutdownAsync(...)`。

## 13. 分层验证与完成清单

每完成一类替换就编译，最终执行：

```powershell
dotnet build YourHost.csproj -c Debug
dotnet build YourHost.csproj -c Release
iwrt --pipe=YourProduct.RuntimeDiagnostics switchboard.get
iwrt --pipe=YourProduct.RuntimeDiagnostics pipe.list
```

迁移完成必须全部满足：

- [ ] 新 `Program.cs` 使用 `Start`、`Activate` 和协调退出流程；
- [ ] Debug/Release 分别引用对应 Diagnostics DLL，并只引用当前 `Iwesun.Runtime.Data.dll`，不存在 `Iwesun.Data.dll` 或宿主私有旧副本；
- [ ] eligible 的 Process/Thread/轻量 Task 及字段、集合、返回类型已迁移；
- [ ] 普通 async、`Task<T>`、UI/STA/COM/message-pump 场景未被误替换；
- [ ] 诊断、监视、断点和反射白名单符合安全规则，默认静默；
- [ ] RuntimeDiagnostics 与业务管道物理隔离，业务管道已进入 Runtime 租约体系；
- [ ] 所有线消息使用长度前缀 `RuntimeDiagnosticFrame`；
- [ ] CLI 使用 v3 catalog，用户兼容只存在于别名、扩展和 `composites`；
- [ ] WebRuntime 使用公共输入和预编译 C# Program，不存在调用方脚本回退；
- [ ] Stop/Wakeup、取消、实际完成、注销和租约释放顺序已验证；
- [ ] Debug/Release 构建通过，并用真实 CLI 完成状态、管道和目标业务命令验证。

## 14. 远程PowerShell与文件上传

使用独立RemoteConsole服务，不改变当前Diagnostics目标：

```text
workspace.create deployment
file.upload <workspaceId> C:\packages\app.zip app.zip
console.submit --workspace <workspaceId> --shell powershell -- Expand-Archive app.zip -DestinationPath app
console.follow <jobId>
```

已支持本地文件上传到远端工作区；远端继续复制使用审批后的`Copy-Item`。当前不支持`file.download`。
服务安装、AI账号、审批和ACL参见`IWESUN_RUNTIME_REMOTE_CONSOLE.md`。

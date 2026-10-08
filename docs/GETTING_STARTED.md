# iwesun Runtime 使用入门

[English](en/USER_GUIDE.md) · [项目简介](../README.md) · [详细接入手册](IWESUN_RUNTIME_USER_GUIDE.md) · [发布指南](IWESUN_RUNTIME_1.0.47_BETA_RELEASE_GUIDE.md)

适用版本：Runtime `1.0.47-beta.1` / Networks `3.0.0-beta.6`。作者与品牌：iwesun。

## 项目解决什么问题

让已接入且获授权的 AI 或运维工具，通过统一 CLI 观察程序内部状态、定位执行问题并验证修复，同时规范服务启动、受管执行和退出流程。
配套公共库提供 Data/RecordStore、WebView2 控制和并行网络接口；不要求消费者一次启用所有模块。

## 获取、环境与首次运行

1. 从 [GitHub Releases](https://github.com/iwesun32/Iwesun.Runtime/releases) 下载同一版本的 MSI 或 ZIP，以及 `SHA256SUMS.txt`，核对 SHA-256。
2. 构建示例需要 Windows 和 .NET 10 SDK；运行依赖对应 .NET 10 运行环境，WebView2 示例还需要 WebView2 Runtime。MSI 不是这些环境的离线安装器。
3. MSI 默认安装到 `C:\Program Files\Iwesun\Runtime`。便携 ZIP 分为 `app` 与 `data`；不要覆盖已有业务数据。
4. 先读随包文档，使用 `app/scripts/verify-runtime-install.ps1` 检查文件、版本、CLI 和示例引用。脚本会构建示例，因此需要 SDK。
5. 消费宿主使用安装包中的 Debug/Release DLL，不跨仓库引用本项目的源码工程；升级后重新构建宿主自己的发布目录。

Windows PowerShell 可用 `Get-FileHash -Algorithm SHA256` 计算下载文件哈希。当前发布未附带 Authenticode 签名；哈希校验不能替代可信来源验证。

## 最小宿主接入

以随包 `samples/templates/RuntimeHost.Startup.Minimal.Template.cs.txt` 为起点，保留旧主程序的非编译备份。
在宿主工程中引用同一配置的 Diagnostics 与 Data DLL，统一 `Microsoft.Extensions.*` 到 10.0.9 或兼容的更新版本；不要混用旧 preview 依赖。

```csharp
using System.Reflection;
using Iwesun.Runtime.Diagnostics;
using Microsoft.Extensions.Hosting;

[assembly: DiagnosticPipePrefix("MyProduct")]

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddRuntimeDiagnostics();
builder.Services.Start(
    runtimeDirectory: Path.Combine(AppContext.BaseDirectory, "runtime"),
    startupRuntimeDiagnosticsPipeName: "MyProduct.RuntimeDiagnostics",
    pipeAccessOptions: new RuntimeNamedPipeAccessOptions
    {
        AllowLocalInteractiveUsers = true,
        AllowAuthenticatedUsers = false,
        AllowedWindowsPrincipals = []
    });

// Add business services here.
using var host = builder.Build();
host.Services.Activate(Assembly.GetExecutingAssembly());
await host.RunAsync();
```

示例运行目录必须可写。服务程序请使用获准的 ProgramData 目录并配置服务身份权限，不在 Program Files 写运行数据。
Windows Service 使用 `StartWindowsService` 或 `StartConfiguredWindowsService`，业务提供服务名和退出期限。见 [Windows Service 接入](IWESUN_RUNTIME_WINDOWS_SERVICE.md)。

<a id="diagnostics"></a>
## AI 诊断与受管执行

- 先用 CLI 的 `--help` 查当前命令，通过目标发现选择宿主，再读取状态和白名单对象。命令与授权边界见 [CLI 手册](IWESUN_RUNTIME_CLI.md)。
- AI 接入使用随包 [`iwesun-runtime-integration` 技能](https://github.com/iwesun32/Iwesun.Runtime/tree/main/skills/iwesun-runtime-integration)。技能不授予超出宿主配置的权限。
- 按需接入 `RuntimeInjector`、`RProcess`、`RThread`、`RTask`；不要机械替换所有 `Task<T>`、异步返回值或 UI/STA 线程。
- 反射读写和调用分别白名单授权。协作断点只暂停调用链；断点和 Watch 注入放在 `#if DEBUG`，不把 Release 当作全部调试能力均可用。
- 默认保持安静，诊断完成后用 `quiet` 恢复。使用 Runtime 输出管线，不另建临时控制台或文件调试通道。
- 退出须检查受管单元的真实结束、清理和反登记；超时不能记为成功。

<a id="data"></a>
## Data / RecordStore

`RecordStore<TValue,TPrimaryKey>` 面向程序内部的小型关系表，提供稳定身份、多 Key、索引、约束、快照和持久化。
它不是宣称替代所有数据库的通用 SQL 服务；性能需结合自己的规模、索引和读写组合测试。
从 [Data 文档](https://github.com/iwesun32/Iwesun.Runtime/tree/main/modules/Data/docs) 和源码规模工具开始。未完成 Tables 工程不属于此版本，Data 正常提供。

<a id="webview2"></a>
## WebView2 自动化

统一 CLI 连接已集成的 WebView2 宿主，控制会话、DOM/XPath、输入和网络证据。
对动态页面使用真实节点身份、revision 和刷新机制；页面读取结果不授权后续点击、下载或执行操作。
从 [WebView2 示例](https://github.com/iwesun32/Iwesun.Runtime/blob/main/modules/WebView2/docs/WEBVIEW2_SAMPLE_HOST.md) 开始，在目标页面和宿主中另行验收。

<a id="networks"></a>
## 并行网络接口

提交多个请求、并发执行并异步接收响应。关联链为 `RequestId → AttemptId → BranchId → ResponseId`。
先查询具体端点能力，再选择访问计划、超时、取消和背压策略；精确约束不支持时应明确拒绝，不能悄悄回退。
见 [Networks 文档与示例](https://github.com/iwesun32/Iwesun.Runtime/tree/main/modules/Networks)。真实 WFP、下一跳和长时间压力不包含在本轮完整验收声明中。

## 构建与排错

```powershell
dotnet build Iwesun.Runtime.slnx -c Release
dotnet test modules/Data/tests/Iwesun.Runtime.Data.Tests/Iwesun.Runtime.Data.Tests.csproj -c Release
dotnet test modules/Networks/tests/Iwesun.Runtime.Networks.Tests/Iwesun.Runtime.Networks.Tests.csproj -c Release
dotnet test modules/WebView2/tests/Iwesun.Runtime.WebView2.Tests/Iwesun.Runtime.WebView2.Tests.csproj -c Release
```

加载失败先检查配置、文件版本、宿主旧 DLL 副本及 Extensions 依赖；连接失败先确认进程存活、目标管道、账号权限和宿主登记。
报告问题时提供版本、最小复现和脱敏证据，不提交凭据、私密业务数据或大体积原始快照。

## 许可与参与

本项目是非商业源码开放项目，不是 OSI 开源许可。非商业使用免费并保留声明；商业使用和独立修改版发行须遵守 [LICENSE](https://github.com/iwesun32/Iwesun.Runtime/blob/main/LICENSE)。
通过 [工作组申请](https://github.com/iwesun32/Iwesun.Runtime/issues/new?template=working-group.yml) 或 PR 参与；入组不自动授予仓库写权限。
AI 按明确治理规则维护，不代表 OpenAI 官方背书。英文入口覆盖公开入门流程；深入设计文档当前主要为中文。

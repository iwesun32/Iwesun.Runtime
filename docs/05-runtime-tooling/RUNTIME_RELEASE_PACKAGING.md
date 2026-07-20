# Runtime 发布与安装总项目

## 唯一全量打包入口

以后只使用下列脚本生成安装包：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\release\build-runtime-setup.ps1 -ProductVersion 1.0.29
```

脚本固定执行完整流程：Debug 全解决方案构建、Release 全解决方案构建、清空并重建完整 staging、发布目录自检、WiX 强制 Rebuild、输出 MSI 大小和 SHA-256。不得再把普通增量 `dotnet build` 生成的 MSI 当作发布包。

每次发布必须提供新的 `ProductVersion`。所有 DLL、CLI 配置、文档、技能、样例和脚本均从当前源码重新收集，不复用旧 staging 或旧 MSI。

唯一入口还会先执行 Iwesun.Networks 与 Runtime Data 的 Debug/Release 测试，以及 Runtime Debug/Release
完整功能场景。任一上游基础库或 Runtime 场景失败都阻止 staging 和 MSI 生成。

RecordStore文档以两种布局发布：根`docs`保留V2设计、API、两份迁移指南、发布状态和复验报告等常用
入口；`docs/Iwesun.Runtime.Data/`保存Data根README，`docs/Iwesun.Runtime.Data/docs/`保存完整 V2 文档目录结构和可用
相对链接。安装验证必须同时检查两个入口，并拒绝旧的`RECORD_STORE_API.md`、
`RECORD_STORE_GUIDE.md`等已删除入口重新混入发布清单。

`Iwesun.Runtime.Data.dll`的唯一库入口是`app/lib/Iwesun.Runtime.Data/Iwesun.Runtime.Data.dll`。
发布树不得出现`Iwesun.Data.dll`。Diagnostics Debug/Release和 WebView2库目录不得保留传递发布产生的第二份Data DLL；应用程序bin目录可保留运行所需的本地副本，
但其SHA-256必须与唯一库入口一致。

`Iwesun.Runtime.WebView2` 是全量 Runtime 发布的固定组成部分。每次执行统一打包入口时必须从当前源码重新构建 WebView2 DLL，同步复制 WebView2 控制手册、能力状态和 JSON 管道/CLI 规划文档，并验证 WebView2 DLL 的 `FileVersion` 与本次 Diagnostics DLL 完全一致。即使某次没有修改 WebView2 源码，也不得复用上一次 staging 中的旧 DLL；发布结果必须让用户能够从安装目录判断本次 WebView2 能力状态。

`Iwesun.Networks` 以独立产品版本进入 Runtime 套件。1.0.29 包从 `D:\Git Space\Networks` 当前源码构建
`Iwesun.Networks` 1.2.0，保留 `1.2.0.0` 文件版本，并复制完整 Networks 文档和示例；不得用旧 NuGet
缓存或手工 DLL 代替源码构建。Runtime 的统一版本注入不改写 Networks 的独立语义版本。

打包程序把同一版本同步注入所有 Runtime DLL：`AssemblyVersion/FileVersion = major.minor.patch.0`，`InformationalVersion = major.minor.patch`。禁止继续发布文件版本固定为 `1.0.0.0` 的 DLL。

本文定义 Runtime 的统一发布工程、发布清单、目录结构，以及 Windows 可卸载安装工程。

## 1. 总项目

### 1.1 发布总项目

- 项目：`Iwesun.Runtime.Release/Iwesun.Runtime.Release.csproj`
- 目标：`PublishRuntimeRelease`
- 输出根目录：`artifacts/release/Iwesun.Runtime/`

执行：

```powershell
dotnet msbuild Iwesun.Runtime.Release\Iwesun.Runtime.Release.csproj /t:PublishRuntimeRelease /p:Configuration=Release
```

### 1.2 安装工程（MSI）

- 项目：`Iwesun.Runtime.Setup/Iwesun.Runtime.Setup.wixproj`
- WiX 源：`Iwesun.Runtime.Setup/Package.wxs`
- 安装类型：Windows MSI（标准安装 + 控制面板可卸载）

执行：

```powershell
dotnet build Iwesun.Runtime.Setup\Iwesun.Runtime.Setup.wixproj -c Release
```

> 说明：安装工程直接打包 `artifacts/release/Iwesun.Runtime/` 产物，因此必须先执行发布总项目。

## 2. 发布内容清单

### 2.1 类库 DLL

- `Iwesun.Runtime.Diagnostics.dll`
- `Iwesun.Runtime.Data.dll`
- `Iwesun.Networks.dll`（独立版本 1.2.0 的网络基础库）
- `Iwesun.Runtime.WebView2.dll`
- `WEBVIEW2_RELEASE_STATUS.md`、`WEB_RUNTIME_CONTROL.md`、`WEBVIEW2_RUNTIME_CAPABILITIES.md` 和 `WEBVIEW2_JSON_PIPE_CLI_PLAN.md`，用于区分本次发布状态、控制接口、已实现能力和后续边界。

### 2.2 CLI 可执行程序

- `Iwesun.Runtime.Cli.exe`
- `Iwesun.Runtime.Cli.deps.json`
- `Iwesun.Runtime.Cli.runtimeconfig.json`

### 2.3 配置 JSON

- `RuntimeCliSystemConfig.json`
- `RuntimeCliSystemMetadata.json`
- `RuntimeCliUserConfig.example.json`

### 2.4 使用说明书

- `IWESUN_RUNTIME_CLI.md`
- `RUNTIME_INTEGRATION_GUIDE.md`
- `INJECTOR_STANDARDIZATION.md`
- `RUNTIME_RELEASE_PACKAGING.md`

### 2.5 工程样例 / 注入格式 / 替换方法 / 用户接口规范

来自 `Iwesun.Runtime.SampleHost/templates`：

- `RuntimeHost.Startup.Minimal.Template.cs.txt`
- `RuntimeHost.DiagnosticsExamples.Template.cs.txt`
- `RuntimeHost.ManagedWorker.Template.cs.txt`
- `RuntimeHost.Shutdown.Template.cs.txt`
- `RuntimeIntegration.Interface.Template.json`
- `Iwesun.Runtime.Cli.commands.custom.sample.json`

## 3. 发布目录结构

```text
artifacts/release/Iwesun.Runtime/
├─ bin/
│  ├─ Iwesun.Runtime.Cli/
│  └─ Iwesun.Runtime.SampleHost/
├─ lib/
│  ├─ Iwesun.Runtime.Diagnostics/
│  ├─ Iwesun.Runtime.Data/
│  ├─ Iwesun.Networks/
│  └─ Iwesun.Runtime.WebView2/
├─ config/
│  ├─ RuntimeCliSystemConfig.json
│  ├─ RuntimeCliSystemMetadata.json
│  └─ RuntimeCliUserConfig.example.json
├─ docs/
│  ├─ IWESUN_RUNTIME_CLI.md
│  ├─ RUNTIME_INTEGRATION_GUIDE.md
│  ├─ INJECTOR_STANDARDIZATION.md
│  └─ RUNTIME_RELEASE_PACKAGING.md
├─ scripts/
│  └─ verify-runtime-install.ps1
└─ samples/
   └─ templates/
```

## 4. 安装目录结构（目标机）

### 4.1 Program Files（运行时二进制）

```text
C:\Program Files\Iwesun Runtime\
├─ bin\
└─ lib\
```

### 4.2 ProgramData（配置/文档/样板/脚本）

```text
C:\ProgramData\Iwesun\Runtime\
├─ config\
├─ docs\
├─ samples\
└─ scripts\
```

该布局将可执行内容与可读资料/配置分离，便于生产环境权限管控与运维巡检。

## 5. MSI 版本策略（CI / Release）

- 安装工程默认版本由 `Iwesun.Runtime.Setup.wixproj` 的 `ProductVersion` 提供。
- 可在构建时覆盖：

```powershell
dotnet build setup\Iwesun.Runtime.Setup\Iwesun.Runtime.Setup.wixproj -c Release /p:ProductVersion=1.0.1
```

- 版本变更配合 `MajorUpgrade` 策略，支持标准升级/回滚路径。

## 6. 安装后自检脚本

- 脚本路径（发布目录）：`artifacts/release/Iwesun.Runtime/scripts/verify-runtime-install.ps1`
- 用途：检查 Program Files / ProgramData 关键文件是否齐全，并执行 `Iwesun.Runtime.Cli.exe --help` 验证 CLI 可运行。

执行：

```powershell
powershell -ExecutionPolicy Bypass -File "C:\ProgramData\Iwesun\Runtime\scripts\verify-runtime-install.ps1"
```

## 7. 验收顺序

1. 执行 `PublishRuntimeRelease` 生成完整发布目录。
2. 检查 `artifacts/release/Iwesun.Runtime/` 结构是否完整。
3. 构建 MSI。
4. 安装后确认：
   - CLI 可运行；
   - 文档/样板/配置存在；
   - 自检脚本通过；
   - 控制面板可卸载并清理安装目录。

# Runtime 发布与安装总项目

## 唯一全量打包入口

以后只使用下列脚本生成安装包：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\release\build-runtime-setup.ps1 `
  -ProductVersion 1.0.46 -NetworksVersion 3.0.0-beta.5
```

脚本固定执行完整流程：Data/Networks双配置测试、Runtime Debug/Release构建与功能场景、清空并重建
完整staging、自检、Networks本地包、WiX强制Rebuild，以及统一β交付目录和SHA-256清单。不得把普通
增量`dotnet build`生成的MSI当作发布包。

每次发布必须提供新的 `ProductVersion`。所有 DLL、CLI 配置、文档、技能、样例和脚本均从当前源码重新收集，不复用旧 staging 或旧 MSI。

发布脚本按`Iwesun.Runtime/<ProductVersion>`稳定生成该三段产品版本的ProductCode；同版本重复构建得到相同ProductCode，
使用Windows Installer维护模式更新，不再因每次构建随机ProductCode而累计多个同版本产品。不同三段版本必须得到不同ProductCode，
共同使用固定UpgradeCode执行MajorUpgrade。

唯一入口还会先执行 Iwesun.Runtime.Networks 与 Runtime Data 的 Debug/Release 测试，以及 Runtime Debug/Release
完整功能场景。任一上游基础库或 Runtime 场景失败都阻止 staging 和 MSI 生成。

RecordStore 源码文档只使用 `modules/Data/docs/` 单一布局，保存 README、统一设计、公共 API、
发布状态和源码映射；发布后映射到 `docs/Iwesun.Runtime.Data/`。不得再生成重复的 `docs/Iwesun.Runtime.Data/docs/` 子树；安装验证同时拒绝旧
版本后缀文档和已经删除的历史报告重新混入发布清单。

`Iwesun.Runtime.Data.dll`的唯一库入口是`app/lib/Iwesun.Runtime.Data/Iwesun.Runtime.Data.dll`。
发布树不得出现`Iwesun.Data.dll`。Diagnostics Debug/Release和 WebView2库目录不得保留传递发布产生的第二份Data DLL；应用程序bin目录可保留运行所需的本地副本，
但其SHA-256必须与唯一库入口一致。

`Iwesun.Runtime.WebView2` 是全量 Runtime 发布的固定组成部分。每次执行统一打包入口时必须从当前源码重新构建 WebView2 DLL，同步复制 WebView2 控制手册、能力状态和 JSON 管道/CLI 规划文档，并验证 WebView2 DLL 的 `FileVersion` 与本次 Diagnostics DLL 完全一致。即使某次没有修改 WebView2 源码，也不得复用上一次 staging 中的旧 DLL；发布结果必须让用户能够从安装目录判断本次 WebView2 能力状态。

`modules/Web`与`Iwesun.Runtime.Web.dll`仍处于架构调试期，当前明确不发布。WebView2、CLI和
SampleHost不得通过项目引用或传递依赖把该DLL/PDB带入staging；安装验证必须递归拒绝它们。
Web组件可继续参与源码解决方案编译，但不属于MSI、便携包或Debug/Release公共库清单。

`Iwesun.Runtime.Networks` 以独立产品版本进入 Runtime 套件，源码位于 `D:\Git Space\Runtime\modules\Networks`。
`Iwesun.Runtime.Networks` 3.0.0-beta.5保留`3.0.0.0`文件版本，当前状态为
`GENERAL_BETA_READY_FORMAL_BLOCKED`。发布入口必须复制完整Networks文档和示例，不得用旧NuGet缓存或手工DLL
代替源码构建；Runtime的统一版本注入不改写Networks的独立语义版本。剩余M11门禁阻止正式版，不阻止普通β载荷。

打包程序把同一版本同步注入所有Runtime DLL：
`AssemblyVersion/FileVersion = major.minor.patch.0`，
`InformationalVersion = major.minor.patch-beta.number`。Networks保持独立版本注入。

普通β最终统一交付目录为：

```text
artifacts/packages/Iwesun.Runtime.<informational-version>/
```

目录固定包含版本化MSI、便携ZIP、Networks nupkg/snupkg、当前发布说明、`RELEASE_MANIFEST.md`
和`SHA256SUMS.txt`。脚本拒绝覆盖已经存在的同版本候选目录。

本文定义 Runtime 的统一发布工程、发布清单、目录结构，以及 Windows 可卸载安装工程。

## 1. 总项目

### 1.1 发布总项目

- 项目：`modules/Packaging/release/Iwesun.Runtime.Release/Iwesun.Runtime.Release.csproj`
- 目标：`PublishRuntimeRelease`
- 输出根目录：`artifacts/release/Iwesun.Runtime/`

执行：

```powershell
dotnet build Iwesun.Runtime.slnx -c Publish
```

### 1.2 安装工程（MSI）

- 项目：`modules/Packaging/setup/Iwesun.Runtime.Setup/Iwesun.Runtime.Setup.wixproj`
- WiX 源：`modules/Packaging/setup/Iwesun.Runtime.Setup/Package.wxs`
- 安装新产品或执行 MajorUpgrade 时，Setup 使用 WiX `RemoveFolderEx` 原生递归清理
  `C:\Program Files\Iwesun\Runtime`，包括 MSI 未登记的旧文件和旧子目录，然后从本次
  staging 重新铺设完整安装树。修复安装和卸载不执行这项“先清空再铺设”动作。
- 清理边界只包含 Program Files 安装根；`C:\ProgramData\Iwesun\Runtime` 属于可写用户
  数据根，升级、修复和卸载均不得把它作为递归清理目标。
- 安装类型：Windows MSI（标准安装 + 控制面板可卸载）

执行：

```powershell
dotnet build modules\Packaging\setup\Iwesun.Runtime.Setup\Iwesun.Runtime.Setup.wixproj -c Release
```

> 说明：安装工程直接打包 `artifacts/release/Iwesun.Runtime/` 产物，因此必须先执行发布总项目。

## 2. 发布内容清单

### 2.1 类库 DLL

- `Iwesun.Runtime.Diagnostics.dll`
- `Iwesun.Runtime.Data.dll`
- `Iwesun.Runtime.Networks.dll`（独立版本3.0.0-beta.5，文件版本3.0.0.0）
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

来自 `modules/Diagnostics/samples/Iwesun.Runtime.SampleHost/templates`：

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
│  ├─ Iwesun.Runtime.Networks/
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
dotnet build modules\Packaging\setup\Iwesun.Runtime.Setup\Iwesun.Runtime.Setup.wixproj -c Release /p:ProductVersion=1.0.1
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

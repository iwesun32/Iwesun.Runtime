# Runtime 发布与安装总项目

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

- 项目：`setup/Iwesun.Runtime.Setup/Iwesun.Runtime.Setup.wixproj`
- WiX 源：`setup/Iwesun.Runtime.Setup/Package.wxs`
- 安装类型：Windows MSI（标准安装 + 控制面板可卸载）

执行：

```powershell
dotnet build setup\Iwesun.Runtime.Setup\Iwesun.Runtime.Setup.wixproj -c Release
```

> 说明：安装工程直接打包 `artifacts/release/Iwesun.Runtime/` 产物，因此必须先执行发布总项目。

## 2. 发布内容清单

### 2.1 类库 DLL

- `Iwesun.Runtime.Diagnostics.dll`
- `Iwesun.Runtime.Data.dll`
- `Iwesun.Runtime.WebView2.dll`

### 2.2 CLI 可执行程序

- `Iwesun.Runtime.Cli.exe`
- `Iwesun.Runtime.Cli.deps.json`
- `Iwesun.Runtime.Cli.runtimeconfig.json`

### 2.3 配置 JSON

- `Iwesun.Runtime.Cli.commands.v2.json`

### 2.4 使用说明书

- `IWESUN_RUNTIME_CLI.md`
- `RUNTIME_INTEGRATION_GUIDE.md`
- `INJECTOR_STANDARDIZATION.md`
- `RUNTIME_RELEASE_PACKAGING.md`

### 2.5 工程样例 / 注入格式 / 替换方法 / 用户接口规范

来自 `Iwesun.Runtime.SampleHost/templates`：

- `RuntimeHost.Startup.Template.cs.txt`
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
│  └─ Iwesun.Runtime.WebView2/
├─ config/
│  └─ Iwesun.Runtime.Cli.commands.v2.json
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

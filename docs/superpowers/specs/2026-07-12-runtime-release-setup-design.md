# Iwesun Runtime Release 与 Setup 设计

## 1. 目标

建立可重复、可验证的 Runtime 完整发布链路：从 Release x64 编译产物收集出 staging 目录，同时生成免安装 ZIP、SHA-256 清单和 Windows x64 MSI 安装包。

发布包不仅包含运行库，还必须包含 CLI v3 配置、权威用户资料、Codex 接入技能、标准宿主源码和 SampleHost 完整典型源码。

## 2. 平台与运行时

- 目标平台：Windows x64。
- 目标框架：.NET 10。
- 发布方式：framework-dependent。
- Setup 不内置 .NET 10 Runtime。
- Setup 安装前检测 .NET 10 Runtime；不满足时阻止安装并给出明确说明。
- SampleHost 只是可运行样例，不注册为 Windows 服务，安装后不自动启动。

## 3. 项目责任

### 3.1 Iwesun.Runtime.Release

`Iwesun.Runtime.Release` 作为发布编排项目，负责：

1. 以 Release x64 构建并发布各组件。
2. 清理并重建 `artifacts/release/Iwesun.Runtime` staging。
3. 按发布清单收集二进制、配置、文档、技能和源码。
4. 验证必需文件、CLI schema 和禁止文件。
5. 生成 staging ZIP 和 SHA-256 清单。

### 3.2 Iwesun.Runtime.Setup

新增 `Iwesun.Runtime.Setup` WiX SDK 项目，负责：

1. 以 staging 为唯一输入，不重复发布 .NET 项目。
2. 定义 Windows x64 MSI 产品、组件、目录、升级和卸载规则。
3. 检测 .NET 10 Runtime。
4. 把不可变产物安装到 Program Files。
5. 首次安装时初始化 ProgramData 用户配置。
6. 将 CLI 目录加入系统 PATH，卸载时只移除自己的 PATH 项。

Setup 技术选择 WiX Toolset SDK，使安装包可以通过 `dotnet build` 纳入统一构建链路。

## 4. 发布依赖门槛

Setup 有一个不可绕过的前置条件：CLI v3 必须实施完成。

正式发布必须包含：

- `Iwesun.Runtime.Cli.commands.json`
- `Iwesun.Runtime.Cli.user.example.json`
- schema `iwesun.runtime.cli/3.0`

正式发布必须拒绝：

- `Iwesun.Runtime.Cli.commands.v2.json`
- `Iwesun.Runtime.Cli.user.v2.json`
- `Iwesun.Runtime.Cli.user.v2.example.json`
- 任何 `baseCommands` / `compositeCommands` 根结构

在 CLI v3 完成前，可以实现并构建 Release/Setup 项目骨架，但不得把携带 v2 配置的 MSI 标记为成功发布产物。

## 5. 安装目录

### 5.1 Program Files

安装根目录：

`C:\Program Files\Iwesun\Runtime`

```text
Runtime/
├── bin/
│   ├── Iwesun.Runtime.Cli/
│   └── Iwesun.Runtime.SampleHost/
├── lib/
│   ├── Iwesun.Runtime.Diagnostics/
│   ├── Iwesun.Runtime.Data/
│   └── Iwesun.Runtime.WebView2/
├── config/
├── docs/
├── skills/
├── samples/
│   ├── templates/
│   └── SampleHost/
├── source/
│   ├── runtime-integration/
│   └── sample-host/
└── scripts/
```

该目录只包含由 MSI 管理的不可变产物。

### 5.2 ProgramData

可写根目录：

`C:\ProgramData\Iwesun\Runtime`

```text
Runtime/
├── config/
│   └── Iwesun.Runtime.Cli.user.json
├── logs/
└── runtime/
```

首次安装时，如果用户配置不存在，从 Program Files 的 example 初始化。升级和修复安装不覆盖已存在用户配置。

## 6. 二进制发布清单

### 6.1 CLI

`bin/Iwesun.Runtime.Cli` 包含：

- CLI apphost EXE，安装后主命令为 `iwrt.exe`。
- `Iwesun.Runtime.Cli.dll`。
- `.deps.json`。
- `.runtimeconfig.json`。
- CLI 需要的 Runtime 依赖 DLL。
- PDB 和 XML 文档（如项目生成）。

CLI 安装目录加入系统 PATH。

### 6.2 类库

分别收集：

- `Iwesun.Runtime.Data.dll`
- `Iwesun.Runtime.Diagnostics.dll`
- `Iwesun.Runtime.WebView2.dll`
- 对应 PDB 和 XML 文档（如生成）。
- 每个 publish 目录中必需的外部依赖。

不使用一个扁平 lib 目录覆盖同名依赖；每个项目保留独立子目录。

### 6.3 SampleHost

`bin/Iwesun.Runtime.SampleHost` 包含可运行 SampleHost 的完整 framework-dependent 发布产物。

## 7. CLI 配置清单

Program Files `config` 包含：

- `Iwesun.Runtime.Cli.commands.json`：内置权威命令目录，升级时更新。
- `Iwesun.Runtime.Cli.user.example.json`：用户扩展示例。

ProgramData `config` 只包含可变用户配置。

## 8. 文档清单

安装当前权威文档与必要专题文档：

- `README.md`
- `IWESUN_RUNTIME_USER_GUIDE.md`
- `IWESUN_RUNTIME_DESIGN.md`
- `RUNTIME_DIAGNOSTICS.md`
- `RUNTIME_ROOT_DATA_STRUCTURE.md`
- CLI v3 正式手册
- `WEBVIEW2_JSON_PIPE_CLI_PLAN.md`
- `05-runtime-tooling/RUNTIME_RELEASE_PACKAGING.md`
- `05-runtime-tooling/WEB_RUNTIME_CONTROL.md`
- `05-runtime-tooling/SAMPLE_HOST.md`

不安装：

- 历史归档。
- 临时交接记录。
- 任务执行计划。
- 审计过程和中间报告。
- `docs/superpowers/plans` 与内部工作流文档。

## 9. 技能清单

完整收集：

```text
skills/iwesun-runtime-integration/
├── SKILL.md
├── agents/openai.yaml
└── references/
    ├── host-startup.md
    ├── managed-execution.md
    ├── diagnostic-injection.md
    ├── state-shutdown-events.md
    └── json-cli.md
```

Setup 不修改用户的 Codex 技能目录。文档说明如何手动复制或由其他工具安装。

## 10. 源码与样例清单

### 10.1 Runtime 接入源码

`source/runtime-integration` 包含：

- `RuntimeHostTemplate.cs`（同时包含 `RuntimeInjector`）。
- `RuntimeShutdownCoordinator.cs`。
- `RuntimeStateContracts.cs`。
- `RManagedState.cs`。
- `API-SOURCE-INDEX.md`：说明类型、命名空间、原始项目位置和推荐用途。

不为发布方便伪造拆分的 `RuntimeInjector.cs`；继续发布它当前所在的真实 `RuntimeHostTemplate.cs`。

### 10.2 SampleHost 典型源码

`source/sample-host` 包含：

- `Iwesun.Runtime.SampleHost.csproj`
- `Program.cs`
- `SampleHostWorker.cs`
- `SampleHostState.cs`
- `SampleHostRandomState.cs`
- `SampleHostProfile.cs`
- `templates/**`

文件保留原始相对结构，确保可以作为完整接入对照。

## 11. 安装、升级与卸载

### 11.1 安装

- 只允许 Windows x64。
- 验证 .NET 10 Runtime。
- 安装 Program Files 产物。
- 仅在缺少时初始化 ProgramData 配置和目录。
- 将 `C:\Program Files\Iwesun\Runtime\bin\Iwesun.Runtime.Cli` 加入系统 PATH。
- 不启动 SampleHost。
- 不自动安装 Codex 技能。

### 11.2 升级

- 使用稳定 UpgradeCode。
- 使用 Major Upgrade 策略移除旧版并安装新版。
- 更新 Program Files 中的内置产物。
- 保留 ProgramData 用户配置、日志和 runtime 数据。

### 11.3 卸载

- 删除 MSI 安装的 Program Files 文件。
- 移除由本产品添加的 PATH 项。
- 默认保留 ProgramData 中的用户配置、日志和 runtime 数据。
- 在文档中提供手动清理用户数据的说明。

## 12. 构建产物

```text
artifacts/
├── release/
│   ├── Iwesun.Runtime/
│   └── Iwesun.Runtime-<version>-win-x64.zip
├── setup/
│   └── Iwesun.Runtime.Setup-x64-<version>.msi
└── checksums/
    └── SHA256SUMS.txt
```

版本号来自统一 MSBuild 属性，ZIP、MSI、程序集和清单必须使用同一版本。

## 13. 验证和失败策略

### 13.1 staging 验证

构建必须在打包前检查：

- CLI EXE/DLL/runtimeconfig/deps 存在。
- Data、Diagnostics、WebView2 DLL 存在。
- SampleHost EXE/DLL 存在。
- v3 内置配置和用户示例存在。
- v3 schema 正确。
- 所有 v2 JSON 不存在。
- 用户手册、技能和源码清单完整。
- ZIP 和 SHA-256 清单与 staging 一致。

任何必需文件缺失、schema 错误或 v2 文件残留都必须使发布目标失败，不生成“部分成功”的 MSI。

### 13.2 安装验证

在隔离的 Windows x64 测试环境执行：

1. 静默安装 MSI。
2. 验证 Program Files 和 ProgramData 目录。
3. 在新终端中通过 PATH 解析 `iwrt.exe`。
4. 运行 `iwrt --help`，确认加载 v3 JSON。
5. 启动 SampleHost，通过 CLI 查询状态。
6. 发送安全退出，验证退出码 0。
7. 写入用户配置标记，执行升级，确认标记保留。
8. 卸载 MSI，确认 Program Files 文件和 PATH 项被删除。
9. 确认 ProgramData 用户配置和日志保留。

### 13.3 构建验证

- 全解决方案 Release x64 构建 0 错误。
- Release staging 验证脚本成功。
- WiX Setup 构建成功。
- MSI 内容清单与 staging 目录一致。
- 功能测试中的 CLI/SampleHost 完整场景通过。

## 14. 完成标准

1. CLI v3 已实施，旧 v2 配置不进入任何发布产物。
2. Release staging 包含完整二进制、配置、文档、技能和源码。
3. ZIP、MSI 和 SHA-256 清单使用统一版本。
4. MSI 支持安装、升级、修复和卸载。
5. ProgramData 用户数据在升级和默认卸载中保留。
6. CLI PATH、SampleHost 启动/安全退出、技能文件和源码样例均经自动验证。

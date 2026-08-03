# Iwesun Runtime 1.0.34-beta.1 失效私有测试基线说明

> 当前状态：`CORRECTION_REQUIRED_PRIVATE_BETA_BLOCKED`。  
> 原定用途：为 Runtime、Networks 的消费项目提供统一 DLL 基线，使各项目可以并行迁移和继续联调。  
> 当前用途：只作为失效基线和回归输入；禁止重新生成、分发、安装为消费候选或描述为可发布私有β。  
> Runtime 产品版本：`1.0.34`；程序集文件版本：`1.0.34.0`；产品信息版本：`1.0.34-beta.1`。  

安装程序在首次安装或 MajorUpgrade 时，会先递归清空
`C:\Program Files\Iwesun\Runtime` 内的全部旧文件和子目录（包括不受旧 MSI 管理的残留），
再写入本版完整发布树；`C:\ProgramData\Iwesun\Runtime` 用户数据不参与此清理。
> Networks 产品信息版本：`3.0.0-beta.1`；程序集文件版本：`3.0.0.0`。

Networks三轴终态、精确接口Ping证据和全协议审计完成前，本文以下布局与消费合同只用于复现旧基线，
不构成当前安装、交付或发布授权。

## 1. 本版冻结的消费合同

- 对外唯一跟踪标识统一为非空 `Guid`，网络请求使用
  `RequestId → AttemptId → BranchId → ResponseId` 完整身份链。
- Networks 请求—响应端点统一为无版本后缀的 `Network*Endpoint<TKey>`、请求和响应类型；
  不提供 V2/V3 兼容别名，遗漏迁移必须以编译错误暴露。
- 精确网络访问使用非空 `RequestedAccessPlan`；不得恢复 `Route = null`、旧 Flags 或隐式回退语义。
- Runtime 数据能力只由 `Iwesun.Runtime.Data.dll` / `Iwesun.Runtime.Data` 提供；
  `RecordStore<TValue,TPrimaryKey>` 是唯一活动 RecordStore。DList、RecordStore V1 和
  `Iwesun.Data.dll` 不进入发布载荷。
- RecordStore 配套类型统一为 `RecordStoreOrigin` 与 `RecordStoreSchemaRegistry`；不提供带版本后缀的
  类型别名，旧消费者必须在编译期完成强制迁移。
- Debug、Release 使用相同程序集名、命名空间、公共类型名和接口名，只按构建配置选择不同 DLL 文件。

## 2. 安装目录和 DLL 选择

标准安装根目录为：

```text
C:\Program Files\Iwesun\Runtime
```

公共库统一布局：

```text
lib\Iwesun.Runtime.Diagnostics\Debug\Iwesun.Runtime.Diagnostics.dll
lib\Iwesun.Runtime.Diagnostics\Release\Iwesun.Runtime.Diagnostics.dll
lib\Iwesun.Runtime.Data\Debug\Iwesun.Runtime.Data.dll
lib\Iwesun.Runtime.Data\Release\Iwesun.Runtime.Data.dll
lib\Iwesun.Runtime.Networks\Debug\Iwesun.Runtime.Networks.dll
lib\Iwesun.Runtime.Networks\Release\Iwesun.Runtime.Networks.dll
lib\Iwesun.Runtime.WebView2\Debug\Iwesun.Runtime.WebView2.dll
lib\Iwesun.Runtime.WebView2\Release\Iwesun.Runtime.WebView2.dll
```

各库目录顶层的同名 DLL 是 Release 兼容入口；新项目不得依赖该模糊入口，必须显式选择
`Debug` 或 `Release`。旧的根级 `Iwesun.Runtime.WebView2\docs` 重复目录已经废止，文档只位于统一
`docs` 目录。

## 3. 消费项目标准引用模板

删除指向 Runtime、Networks 源码仓库的外部 `ProjectReference`，并删除 Networks 的
`PackageReference`。内部业务项目之间的 `ProjectReference` 保持不变。

```xml
<PropertyGroup>
  <IwesunRuntimeRoot Condition="'$(IwesunRuntimeRoot)' == ''">$(ProgramFiles)\Iwesun\Runtime</IwesunRuntimeRoot>
  <RuntimeLibraryVariant Condition="'$(Configuration)' == 'Debug'">Debug</RuntimeLibraryVariant>
  <RuntimeLibraryVariant Condition="'$(Configuration)' != 'Debug'">Release</RuntimeLibraryVariant>
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
  <Reference Include="Iwesun.Runtime.Networks">
    <HintPath>$(IwesunRuntimeRoot)\lib\Iwesun.Runtime.Networks\$(RuntimeLibraryVariant)\Iwesun.Runtime.Networks.dll</HintPath>
    <Private>true</Private>
  </Reference>
</ItemGroup>
```

若项目目录可能残留旧程序集，建议把 DLL 的完整路径直接写入 `Reference Include`，避免 MSBuild
从历史 `artifacts` 目录选择同名旧程序集。迁移后必须分别执行 Debug、Release 编译。

## 4. β 版已完成验证

- Networks Debug/Release 构建与测试：各 `107/107` 通过。
- Runtime staging：Diagnostics、Data、Networks、WebView2 的 Debug/Release DLL 均已生成；
  文件版本和产品信息版本一致，配置间不改变公共名称。
- Aether：切换安装目录 DLL 后，Debug/Release 全解决方案编译均为 0 警告、0 错误。
- DDNS Snap：切换安装目录 DLL 后，x64 Debug/Release 全解决方案编译均为 0 警告、0 错误。
- Windows Installer：ProductVersion 已提升到 1.0.34，可对现有 1.0.33 执行 MajorUpgrade；升级时
  递归清理旧 Program Files 安装树，安装后必须用随包验证脚本逐项核对 DLL 与 staging 的 SHA-256。

## 5. β 期间继续优化的核心问题

以下项目不阻止消费项目使用本 β 同步开发，但阻止提升为正式版：

1. 完成管理员权限下 WFP 双真实网关并发隔离，以及正常退出、异常退出和崩溃后的清理矩阵。
2. 补齐 Windows 真实网络矩阵中尚未覆盖的精确源地址、接口、网关和恢复组合，继续核对 Actual 路径证据。
3. 继续优化精确访问控制的组合表达、能力查询和失败证据；不得破坏本版已冻结的 GUID 身份链。
4. Aether 端到端统计属于多日长周期测试，本 β 只执行短回归和编译门禁；正式版前另行登记长周期证据。
5. 完成干净机器上的安装、卸载、升级、Debug/Release 消费编译和正式版本冻结。

## 6. 发布和回退边界

- 本版允许通过 Runtime MSI 或内部文件源分发，不上传 nuget.org。
- 不允许在消费项目中复制 Runtime/Networks 源码作为临时修复；问题应回到对应源码仓库处理并重发 DLL。
- 不允许重新加入 V2/V3 别名、可空 Route、DList、RecordStore V1 或 `Iwesun.Data.dll` 来绕过编译错误。
- 回退必须整体回退 Runtime 安装版本，不能混用不同版本的 Debug/Release DLL 或手工覆盖单个程序集。

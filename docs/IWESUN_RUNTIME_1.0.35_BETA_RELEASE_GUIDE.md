# Iwesun Runtime 1.0.35-beta.1 普通 β 发布说明

> 当前状态：`GENERAL_BETA_READY_FORMAL_BLOCKED`。  
> 用途：为 Runtime、DDNS Snap、Aether 及其他内部消费者提供统一的 Debug/Release DLL 联调基线。  
> Runtime 产品版本：`1.0.35`；程序集文件版本：`1.0.35.0`；产品信息版本：`1.0.35-beta.1`。  
> Networks 产品信息版本：`3.0.0-beta.2`；程序集文件版本：`3.0.0.0`。

安装程序在首次安装或 MajorUpgrade 时递归清理
`C:\Program Files\Iwesun\Runtime` 内的旧文件和子目录，再写入完整发布树；
`C:\ProgramData\Iwesun\Runtime` 中的用户数据不参与清理。

## 1. 本版冻结的消费合同

- 网络对外绝对跟踪身份使用非空 `Guid`，固定身份链为
  `RequestId → AttemptId → BranchId → ResponseId`；`NetworkFlowSerial : uint`只用于进程内高速索引。
- Networks 公共端点统一为无版本后缀的 `Network*Endpoint<TKey>`；请求必须提供非空
  `RequestedAccessPlan`。不提供 V2/V3、可空 Route 或旧 Flags 兼容层。
- UDP 使用长期 Socket/端口租约、严格实际远端匹配、迟到隔离和有界容量；达到容量时在发送前拒绝，
  不清空未到期隔离记录。
- 终态使用 `TerminalState × ProtocolOutcome × AccessCompliance` 三轴表达；零响应不再反向改写已成立的
  精确路径绑定证据，组播目标与实际响应方分别记录。
- Runtime 数据能力只由 `Iwesun.Runtime.Data.dll` / `Iwesun.Runtime.Data` 提供；
  `RecordStore<TValue,TPrimaryKey>` 是唯一活动 RecordStore。DList、RecordStore V1 和 `Iwesun.Data.dll`
  不进入发布载荷。
- Debug、Release 保持完全相同的程序集名、命名空间、公共类型和接口，仅按消费项目配置选择 DLL。

## 2. 安装目录和 DLL 选择

标准安装根目录：

```text
C:\Program Files\Iwesun\Runtime
```

每个公共库都提供 Debug 与 Release：

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

各库目录顶层同名 DLL 是 Release 兼容入口。新项目必须显式选择 `Debug` 或 `Release`，不得引用 Runtime
源码工程或混用两套 DLL。

## 3. 标准引用模板

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

## 4. β 发布质量门

- Data Debug/Release 测试必须各 86 项全部通过。
- Networks Debug/Release 测试必须各 126 项全部通过。
- Runtime 根解决方案 Debug/Release、诊断功能测试、Publish staging 和 MSI 重建必须通过。
- staging 与安装目录必须同时包含四个公共库的 Debug/Release DLL，并通过版本、旧文件缺失和载荷一致性检查。
- Networks 已完成三轴终态、IPv6 组播/单播实测、UDP 长期数据面和 DDNS Snap 隔离 DLL 消费验证。

## 5. 普通 β 已知限制

以下项目允许消费项目继续联调，但阻止提升为正式版：

1. WFP 双真实网关并发隔离和 M11 剩余真实网络矩阵仍待完成。
2. DDNS Snap 完整 Service 地址恢复链可能执行 RS、DHCPv6 renew 和网卡重启，尚未纳入本次普通 β 自动验收。
3. UDP 默认 64 槽是安全保护值，最终容量需由用户全流量 β 统计决定。
4. Aether 多日端到端统计不纳入短发布门禁，正式版前另行登记。
5. 正式版仍需完成干净环境 restore/build/test/install、升级/卸载和版本冻结。

## 6. 分发边界

- 允许通过 Runtime MSI、便携 staging 或内部文件源分发本普通 β；不上传 nuget.org。
- Networks NuGet 包只生成到仓库本地 `artifacts`，供离线消费和回归，不发布到公共源。
- 不允许恢复旧 API、DList、RecordStore V1 或 `Iwesun.Data.dll` 来绕过编译错误。
- 回退必须整体回退 Runtime 安装版本，不得手工覆盖单个程序集或混用 Debug/Release。

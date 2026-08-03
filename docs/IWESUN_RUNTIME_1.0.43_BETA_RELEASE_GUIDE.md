# Iwesun Runtime 1.0.43-beta.1 普通 β 发布说明

> 状态：`GENERAL_BETA_INSTALLED_VALIDATED`  
> Runtime 产品版本：`1.0.43`  
> Runtime 程序集/文件版本：`1.0.43.0`  
> Runtime 产品信息版本：`1.0.43-beta.1`  
> Networks 产品信息版本：`3.0.0-beta.4`

本版发布 1.0.42 之后完成的协调退出、终态准入和大规模分支现场清理修正。它是新的不可覆盖候选，不覆盖 1.0.42，不上传公共 NuGet 源。

## 1. 主要升级

- CLI、JSON、SCM 和内部调用汇入同一个 `RuntimeShutdownCoordinator` 操作。
- 第一次请求拥有唯一 RequestId、实际 timeout 和 deadline；后续并发请求不能延长期限。
- `Stop`、`Exit`、`Completed`、`Timeout` 形成不可逆退出域，中央 Registry 原子拒绝新增工作。
- `RTask`、`RThread`、`RProcess` 只有在 Start 时登记并且只允许一个 Start 所有者；失败释放命令处理器、管道和反射目标，Execution 保留 `Faulted` 终态证据。
- 活动 UnitId 原子唯一；首次 deadline 在 Registry 冻结，直接请求、终态更新和调用方取消都不能改写。
- AIGateway Server 等大分支宿主在同一个 deadline 内清理已有现场，监管器不能在退出门关闭后补建分支。
- CLI 接受请求返回 0；宿主清理完成返回 0；宿主清理超时返回 124，三者不得混写。

完整合同和迁移步骤见 [Runtime 1.0.43 协调退出修正与迁移说明](IWESUN_RUNTIME_1.0.43_SHUTDOWN_MIGRATION.md)。

## 2. 发布边界

发布 Diagnostics、Data、Networks、WebView2、CLI、RemoteConsole、SampleHost、配置、技能、样例和文档。`modules/Web`、`Iwesun.Runtime.Web.dll`、PDB 和源码不进入安装载荷。Networks 继续保持 `3.0.0-beta.4` 独立版本。

## 3. β 验收重点

- AIGateway 在存在大量分支时发起 shutdown，确认退出后无新业务 UnitId 登记。
- 清理钩子、登录任务、WebView2 STA、ProgramHost 和服务线程在一个总 deadline 内退出。
- 同时从 CLI、SCM 和内部调用发起退出，响应的 RequestId、timeoutMs、deadlineUtc 必须一致。
- 正常清理验证宿主退出码 0；故意保留阻塞单元验证宿主退出码 124 和 PendingUnits 证据。
- 使用 1.0.42 编译且不重新编译的宿主，整体换入 1.0.43 Runtime 文件后验证 DI、Activate、shutdown 和退出码。

## 4. 标准 DLL 布局

```text
lib\Iwesun.Runtime.Diagnostics\Debug|Release\Iwesun.Runtime.Diagnostics.dll
lib\Iwesun.Runtime.Data\Debug|Release\Iwesun.Runtime.Data.dll
lib\Iwesun.Runtime.Networks\Debug|Release\Iwesun.Runtime.Networks.dll
lib\Iwesun.Runtime.WebView2\Debug|Release\Iwesun.Runtime.WebView2.dll
```

各库顶层同名 DLL 为 Release 兼容入口。

## 5. 交付目录

```text
artifacts\packages\Iwesun.Runtime.1.0.43-beta.1\
```

候选完成后包含 MSI、便携 ZIP、Networks nupkg/snupkg、发布说明、清单和 SHA-256 清单。

## 6. 正式版阻塞项

- AIGateway 多分支长时压力和多轮现场清理；
- WebView2 长时间 DOM 事件与 STA 压力；
- WFP 双真实网关并发隔离和完整网络恢复矩阵；
- 干净环境安装、升级、卸载和最终版本冻结。

## 7. 当前预检结果

- Runtime 根解决方案 Debug/Release：均为 0 警告、0 错误。
- Diagnostics 完整功能回归：Debug 29/29、Release 24/24，全部 `success=true`；两种配置结束时托管登记均为 0。
- Data：Debug/Release 各 101/101；Networks：各 202/202；WebView2：各 34/34。
- 以 Diagnostics `1.0.42.0` 编译且未重新编译的旧宿主，已由当前程序集完成 DI、Activate、shutdown 和退出码 0 验证。
- 唯一候选目录已生成 7 项交付物；staging 与 Program Files 官方自检均通过，确认 Runtime
  `1.0.43.0`、Networks `3.0.0.0`、Debug/Release 双配置和递归零 Web 载荷。
- Aether 使用默认安装根完成 Debug/Release 编译、Release 测试 242/242 和 DNS/DoH
  端到端精确匹配 36/36，mismatch=0、failure=0。
- 当前是本机已安装验证的普通 β；第 6 节压力与真实环境项目仍阻止正式版冻结。

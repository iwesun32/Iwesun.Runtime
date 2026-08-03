# Runtime 1.0.43 协调退出修正与迁移说明

> 状态：发布前设计与迁移基线  
> 日期：2026-07-31  
> 范围：`Iwesun.Runtime.Diagnostics`  
> 建议候选：`1.0.43-beta.1`

## 1. 是否需要再次升级发布

需要。AIGateway 的业务清理修正可以在当前已安装 Runtime 的 5 秒期限内完成退出，但本次同时修改了 Diagnostics 公共库的退出期限和终态准入语义。其他宿主只有升级公共 DLL 后才能获得这些修正。

现有 `1.0.42-beta.1` 是不可覆盖候选，不得原地重打。公共修正应使用新的 `1.0.43-beta.1` 候选完成构建、测试、打包、安装和回退验证。本说明不授权立即发布、安装或覆盖 `C:\Program Files\Iwesun\Runtime`。

## 2. 问题与修改原因

### 2.1 CLI 退出期限与宿主配置不一致

宿主通过 `RuntimeWindowsServiceOptions.ShutdownTimeout` 配置协调退出期限，但 `diagnostics.switchboard/shutdown` 和 `runtime.managed/lifecycle.shutdown` 原先在命令目标中使用固定 5 秒。业务清理尚在正常进行时，协调器会按 5 秒进入 Timeout，最终使宿主采用退出码 124，造成“宿主配置 30 秒、实际协调只等 5 秒”的双重语义。CLI 成功提交请求自身仍返回 0。

修正后：

- 请求显式提供 `timeoutMs` 时使用该值；
- 兼容读取 `countdownMs`；
- 两者都未提供时，采用宿主 `RuntimeWindowsServiceOptions.ShutdownTimeout`；
- 无有效宿主配置时回退到 30 秒；
- 最小值仍限制为 100ms；
- 命令结果返回实际采用的 `timeoutMs`。

### 2.2 生命周期终态仍允许登记新工作

公共注册表原先只把 `Stop` 和 `Exit` 当作停止阶段。协调器已经进入 `Completed` 或 `Timeout` 后，后台监管代码仍可能重新登记新的 `RTask`/`RThread`，导致退出尾部重新产生业务工作。

修正后，`Stop`、`Exit`、`Completed`、`Timeout` 都属于不可登记新托管工作的退出阶段。

### 2.3 退出完成必须表示业务执行体真实结束

`CleanupRequested` 不是状态通知的终点。业务托管单元必须停止产生新工作、取消执行体、释放资源并注销；协调器只有在阻塞单元真实注销后才能完成退出。AIGateway 通过一个阻塞的 `aigateway.task.runtime-cleanup` 单元接入该合同。

### 2.4 大规模分支的统一现场清理

AIGateway Server 本身会管理较多分支线程和任务。第一次退出请求现在冻结唯一的 `RequestId`、总期限和 `deadlineUtc`；CLI、SCM 或内部并发重复请求只能观察该操作，不能另起期限。退出门关闭后，监管器不得补建业务分支。已经登记的阻塞分支在同一 deadline 内完成清理和反登记，纯观察器可使用 `BlocksShutdown=false` 避免形成等待自身的环。

Registry 同时拒绝活动 UnitId 重复登记，三类包装器只允许一个 Start 调用取得所有权。调用方 token 只取消等待，不取消已接受的退出；直接 Registry 请求和终态更新也不能改写首次 deadline。`graceful=false` 明确返回不支持，避免参数被静默忽略。

## 3. 公共源码修改清单

| 文件 | 修改 |
| --- | --- |
| `DiagnosticSwitchboardTarget.cs` | 读取宿主退出配置；支持显式期限覆盖；在返回帧报告实际 `timeoutMs` |
| `RuntimeManagedCommandTarget.cs` | `lifecycle.shutdown` 使用相同的期限选择规则并报告实际期限 |
| `RuntimeManagedRegistry.cs` | 四种终态形成不可逆中央准入门；准入检查与登记原子化 |
| `RuntimeShutdownCoordinator.cs` | 第一次请求拥有实际 RequestId、期限和 deadline；重复并发请求复用同一操作 |
| `RTask.cs`、`RThread.cs`、`RProcess.cs` | 登记推迟到 Start；单一 Start 所有权；失败时释放本次活动资源，Execution 保留 Faulted 证据 |
| `RuntimeDiagnosticsServiceCollectionExtensions.cs` | 将 `IOptions<RuntimeWindowsServiceOptions>` 传入命令目标 |
| `Iwesun.Runtime.FunctionalTests` | 增加独立 shutdown-contract 场景，真实启动三类包装器并检查并发、终态和回滚 |
| `SampleHostCliFullScenario.cs` | 验证 SampleHost 的 30 秒配置通过 CLI 正确生效 |
| `IWESUN_RUNTIME_CLI.md`、`IWESUN_RUNTIME_USER_GUIDE.md` | 更新期限优先级、终态和退出合同 |

## 4. 协议与兼容性

### 4.1 CLI/JSON 协议

请求保持兼容：

```json
{
  "target": "runtime.managed",
  "action": "lifecycle.shutdown",
  "arguments": {
    "timeoutMs": 30000,
    "payload": "maintenance"
  }
}
```

未提供 `timeoutMs`/`countdownMs` 时的行为从“固定 5000ms”改为“采用宿主配置”。响应新增或固定公开 `timeoutMs`，旧客户端可忽略该字段。

### 4.2 源码兼容性

使用 `StartWindowsService`/依赖注入注册的宿主无需修改调用代码。显式期限调用仍保持原行为。

### 4.3 二进制兼容发布阻断项

源码已恢复 `DiagnosticSwitchboardTarget(RuntimeShutdownCoordinator, IHostApplicationLifetime?)` 和 `RuntimeManagedCommandTarget(RuntimeManagedRegistry, RuntimeDiagnosticHub, RuntimeShutdownCoordinator, IHostApplicationLifetime?)` 两个原有公共签名；配置感知版本保留为第三参数明确必填的重载，避免 `(..., null)` 产生源码重载歧义。

发布前必须：

1. 使用 1.0.42 编译的最小宿主，仅替换为完整 1.0.43 Runtime 文件集合；
2. 验证启动、CLI shutdown、业务清理和宿主退出码；
3. 该旧二进制实测通过前不得生成正式候选。

## 5. 宿主迁移步骤

### 5.1 发布前

1. 复核已恢复的二进制兼容重载，并运行旧宿主实测。
2. 构建 Diagnostics Debug/Release。
3. 运行 Diagnostics 完整功能场景和 SampleHost CLI 全流程。
4. 运行旧二进制宿主替换 DLL 的兼容烟测。
5. 将版本提升为新的 `1.0.43-beta.1`，不得覆盖 1.0.42 产物。
6. 走 Runtime 唯一发布入口生成清单、ZIP、MSI 和 SHA-256。

### 5.2 消费宿主升级

1. 停止宿主服务并确认承载 Runtime DLL 的进程已经退出。
2. 备份当前安装目录、版本清单和宿主配置。
3. 安装新的不可变候选，或按正式部署规则替换完整 Runtime 文件集合；禁止只混换单个 DLL。
4. 保留宿主原有 `RuntimeWindowsServiceOptions.ShutdownTimeout` 配置。
5. 启动宿主并通过 `host.summary` 核对版本、管道和托管单元。
6. 执行一次不带期限的 `lifecycle.shutdown true`，确认响应 `timeoutMs` 等于宿主配置。
7. 执行至少三轮严格串行启动/退出，要求 ExitCode=0、无残留进程、无未注销托管单元。
8. 单独验证一个清理超时场景，要求 ExitCode=124 且诊断数据保留未完成单元，不得伪报成功。

### 5.3 AIGateway 迁移验证

AIGateway 已在旧安装版 5 秒期限下完成 3/3 退出回归，并在修正后的源码 Runtime 30 秒期限下完成 5/5 回归。安装 1.0.43 后仍必须重新执行：

- AIGateway L1 状态管理测试；
- 全解决方案构建和 Service 全量测试；
- 三轮以上 `lifecycle.shutdown` 实际退出；
- 登录任务、WebView2 STA 线程、ProgramHost 和服务进程残留检查。

## 6. 回退

如果 1.0.43 出现启动或退出回归：

1. 停止宿主并保存 Runtime Diagnostics 文本摘要；
2. 使用完整备份恢复 1.0.42 文件集合，不混用版本；
3. 恢复后重新启动并执行一次退出验证；
4. AIGateway 自身的业务清理门、登录任务取消和 WebView2 队列结束逻辑无需回退，它们在旧 Runtime 5 秒期限下已经验证兼容。

## 7. 当前验证结论

- Runtime 根解决方案与 Diagnostics Debug/Release 领域构建：0 警告、0 错误；
- `managed` 与新增 `shutdown-contract` 独立场景已通过；后者真实验证三类包装器拒绝、失败回滚、既有同名登记保护、终态不可重开、既有 Registry deadline 接管，以及重复/并发请求复用同一操作；
- 完整功能回归 Debug 29/29、Release 24/24，SampleHost CLI 全流程均为 `success=true`，结束时托管登记为 0；
- Data Debug/Release 各 101/101、Networks 各 202/202、WebView2 各 34/34；
- 兼容宿主的程序集引用已强制核对为 Diagnostics `1.0.42.0`；该旧二进制已由 1.0.43 当前程序集成功加载，完成 Host DI、Activate、shutdown 命令和退出码 0 验证；
- 版本已提升到 `1.0.43-beta.1`，尚未执行 staging、ZIP、MSI 或安装；发布脚本已纳入旧二进制门禁。

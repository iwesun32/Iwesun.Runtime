# Runtime 无配置文件整改方案

## 1. 整改目标

彻底取消 `diagnostic-switchboard.json` 对 Runtime 的控制，消除旧文件、相对路径、复制目录、升级残留和多进程共用目录造成的配置反向覆盖。

最终原则：

```text
源码声明固定身份和默认行为
命令行参数提供唯一启动期覆盖
CLI 指令只修改当前进程内存状态
进程退出后恢复源码默认值
```

## 2. 最终优先级

### 2.1 主管道名

```text
--diag-pipe / --runtime-diagnostics-pipe
    >
Services.Start(...startupRuntimeDiagnosticsPipeName)
    >
[assembly: DiagnosticPipePrefix(...)]
    >
Runtime 编译默认值
```

JSON 不再参与管道名决议，也不保存最终管道名。

### 2.2 主记录文件

```text
--diag-file / --runtime-diagnostics-file
    >
Services.Start(...startupRuntimeDiagnosticsFilePath)
    >
[assembly: DiagnosticFileOutput(...)]
    >
Runtime 编译默认值
```

文件写入模式和格式由源码声明；如需临时变更，应使用明确的启动参数，不写入持久化 JSON。

### 2.3 运行开关

全局、section、output point、管道输出、文件输出和 FIFO 深度使用：

```text
源码编译默认值 -> CLI 当前进程动态修改
```

CLI 动态状态不跨进程重启保存。

## 3. 实现修改清单

### 3.1 删除配置文件链路

- [ ] 删除 `DiagnosticSwitchboardConfigStore.Load()` 的文件读取和反序列化。
- [ ] 删除 `Save()`、`ReloadConfig()`、`SaveConfig()` 的文件写入语义。
- [ ] 删除 `ConfigPath` 对业务运行的影响。
- [ ] 删除 `MergeWithCompiledDefaults` 对外部 JSON 的合并路径。
- [ ] 旧 `diagnostic-switchboard.json` 即使存在也必须完全忽略。
- [ ] Runtime 不自动删除旧文件，避免越权破坏用户目录；文档说明它已失效，可由用户自行清理。

### 3.2 建立不可变启动配置

- [ ] 新增只读启动配置对象，集中保存最终管道名、主记录路径、格式和写入模式。
- [ ] DI 注册阶段完成源码声明和命令行参数决议。
- [ ] `RuntimeDiagnosticsMonitor` 直接接收最终管道名，不再从 Switchboard 动态快照取名。
- [ ] 文件记录器直接接收最终路径和写入策略。
- [ ] 启动完成后禁止修改主管道名和主记录路径。
- [ ] Snapshot 只读公开最终决议值及来源，例如 `Source / CommandLine`。

### 3.3 清理持久化接口

- [ ] CLI 命令删除 `persist` 参数。
- [ ] 代码 API 删除或废止 `persist` 参数。
- [ ] `reload/save/saveconfig` 返回明确的“不支持”错误，过渡后删除。
- [ ] `setRuntimeDiagnosticsPipeName` 和 `setFilePath` 运行期命令明确拒绝。
- [ ] 防止旧客户端收到成功但实际未生效。

### 3.4 补齐 point 级 CLI

- [ ] 新增 `switchboard.point.list`。
- [ ] 新增 `switchboard.point.enable <id>`。
- [ ] 新增 `switchboard.point.disable <id>`。
- [ ] 不存在的 point 返回 `OUTPUT_POINT_NOT_FOUND`，禁止静默成功。
- [ ] 文档明确发布条件为 Global、Section、Point 和输出通道同时启用。

### 3.5 修复反射命令

- [ ] `reflection.get` 的目标改为实际注册目标，不再固定指向不存在的 `diagnostics.reflection`。
- [ ] 目标参数只承担路由模板作用，避免同时作为重复位置参数发送。
- [ ] 分别验证位置参数和命名参数。
- [ ] 未注册目标返回明确错误和可查询的目标清单。

### 3.6 修复退出闭环

- [ ] shutdown-watch 标记为退出协调基础设施，不参与 `CanExit` 业务门槛。
- [ ] 退出门槛只统计必须先结束的进程、线程和业务任务。
- [ ] 退出监视器在其他登记清空后自行销户并返回。
- [ ] 超时仍返回约定超时退出码。

## 4. 四宿主管道规范

四个宿主必须在源码中声明互不相同的固定主管道：

```text
DdnsSnap.Service.RuntimeDiagnostics
DdnsSnap.Agent.Server.RuntimeDiagnostics
DdnsSnap.UI.RuntimeDiagnostics
DdnsSnap.Agent.UI.RuntimeDiagnostics
```

实际名称以宿主项目最终约定为准，但禁止回落为四个程序共用的默认管道。测试时允许使用 `--diag-pipe` 创建临时唯一实例。

## 5. 迁移与兼容

- 旧 JSON schema 不迁移、不加载、不合并。
- 旧 JSON 中的开关和路径不再生效。
- 旧 CLI 的 `persist=true` 必须返回明确不支持，不得静默忽略。
- 用户需要长期默认值时直接修改宿主源码声明并重新构建。
- 用户需要临时值时使用启动参数或当前进程 CLI 指令。

## 6. 验证清单

- [ ] 四个宿主目录分别放置互相冲突的旧 JSON，最终管道仍使用源码声明。
- [ ] `--diag-pipe` 能临时覆盖源码管道。
- [ ] 不带参数重启后恢复源码管道。
- [ ] `--diag-file` 能临时覆盖源码记录路径。
- [ ] CLI 动态开关重启后恢复源码默认值。
- [ ] Runtime 全程不创建、不修改 `diagnostic-switchboard.json`。
- [ ] 四条主管道可同时运行并分别响应 CLI。
- [ ] point 级启用后 `Published` 增长，禁用后仅 `Seen/Suppressed` 增长。
- [ ] shutdown-watch 不再进入退出 pending 清单。
- [ ] Service、Agent、UI、Agent UI 均能安全退出。
- [ ] `reflection.get` 位置参数和命名参数均成功。
- [ ] Debug/Release 全解决方案构建通过。
- [ ] 全部真实 CLI 联调通过后才重新生成安装包。

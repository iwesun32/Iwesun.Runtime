# Runtime CLI Emergency Stabilization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** 不升级协议，完成多宿主目标选择、传输错误分类、有限列表、轻量修改响应、状态身份和资料纠偏。

**Architecture:** 目标上下文和传输错误属于 CLI；分页和 Mutation 在 Runtime 目标执行阶段完成；大对象查询预算只写下一阶段设计。

**Tech Stack:** C# .NET 10、NamedPipe、System.Text.Json、Runtime Frame v2/v3。

---

### Task 1：先建立失败测试

**Files:** Create Iwesun.Runtime.FunctionalTests/CliTransportFailureScenario.cs；Modify Iwesun.Runtime.FunctionalTests/Program.cs

- [ ] 用随机 GUID 管道复现当前 CLI_CANCELLED。
- [ ] 建立外部取消、接受连接但不响应、读取中途断管三个测试端点。
- [ ] 运行 Debug FunctionalTests 构建，确认测试稳定失败在错误分类而不是超时失控。

### Task 2：阶段化传输错误

**Files:** Create Iwesun.Runtime.Cli/CliTransport.cs；Modify Iwesun.Runtime.Cli/CliV3.cs

- [ ] 提取 SendAsync(endpoint, pipeName, targetAlias, frame, connectTimeoutMs, requestTimeoutMs, maxBytes, cancellationToken)。
- [ ] 分别包围 connect、write、read-header、read-body，生成设计规定的错误码和 data。
- [ ] 只有外部 cancellationToken 已取消才返回 CLI_LOCAL_CANCELLED/130。
- [ ] RenderFailure 保持单个合法 JSON。
- [ ] 运行 Task 1，预期依次得到 CONNECT_TIMEOUT、LOCAL_CANCELLED、RESPONSE_TIMEOUT、PROTOCOL_TRUNCATED。

### Task 3：CLI 目标目录

**Files:** Create Iwesun.Runtime.Cli/CliTargetContext.cs；Modify CliInteractiveShell.cs、CliV3.cs、RuntimeCliSystemConfig.json、RuntimeCliUserConfig.example.json

- [ ] 配置模型增加 targets(name, endpoint, pipeName)，冲突配置拒绝加载。
- [ ] 实现 target add/list/use/current/remove。
- [ ] 实现 @name 临时覆盖，并严格执行 --pipe > @name > current target > endpoint。
- [ ] destructive 命令在多目标且无明确目标时拒绝执行。
- [ ] 更新 Shell help；exit/quit 仍只退出 Shell。

### Task 4：目标选择回归

**Files:** Create Iwesun.Runtime.FunctionalTests/CliTargetContextScenario.cs；Modify Program.cs 和 fixtures/cli-default/RuntimeCliUserConfig.json

- [ ] 启动两个独立 SampleHost，登记 service/agent。
- [ ] 验证 target use 和 @agent 返回不同 ProcessId/pipe，且 @agent 不改变 current。
- [ ] 验证未知目标不发起连接。
- [ ] 验证 quit 后两个宿主仍运行，再显式 shutdown。

### Task 5：统一分页模型

**Files:** Create Iwesun.Runtime.Diagnostics/RuntimePagedResult.cs；Modify RuntimeDiagnosticModels.cs、RuntimeCliSystemConfig.json、RuntimeCliSystemMetadata.json

- [ ] 定义 RuntimePagedResult<T>：Items、Total、Offset、Limit、Returned、HasMore。
- [ ] 默认 offset=0/limit=100，硬上限 500，非法参数结构化拒绝。
- [ ] 给十一条目录命令增加兼容的尾部 offset/limit 参数和帮助示例。
- [ ] 新增 host.summary catalog/metadata，字段限定为宿主身份、版本、程序集名称/数量和目标数量；runtime.inspect 改用 host.summary，host.info 保留显式详细入口。

### Task 6：目录目标稳定分页

**Files:** Modify RuntimeDiagnosticHub.cs、RuntimeManagedCommandTarget.cs、DiagnosticSwitchboardTarget.cs、RuntimePipeRegistryTarget.cs、RuntimeFileRegistryTarget.cs

- [ ] registry/reflection、breakpoint、hook、process/thread/task、pipe、file、point 在稳定排序后 Skip/Take。
- [ ] host.events 保持 count 兼容，同时返回分页摘要。
- [ ] 不修改 reflection.get/invoke。
- [ ] 新增 cli-list-bounds 场景，验证默认 100、显式 25、硬上限和 hasMore。
- [ ] 验证 runtime.inspect 响应不包含程序集类型明细，host.info 仍可显式读取旧详细结果。

### Task 7：Switchboard 轻量 Mutation

**Files:** Create Iwesun.Runtime.Diagnostics/RuntimeMutationResult.cs；Modify DiagnosticSwitchboard.cs、DiagnosticSwitchboardTarget.cs

- [ ] Switchboard 维护单调 SnapshotVersion，有效修改时递增。
- [ ] enable/disable/set/input/pipe/file/fifo/point 返回 Mutation，不调用 Snapshot。
- [ ] Snapshot 增加 GeneratedAt/SnapshotVersion，Section 增加 ConfiguredEnabled/EffectiveEnabled。
- [ ] 测试修改响应不含 statements/outputPoints，随后 switchboard.get 可见变更。

### Task 8：宿主实例和状态取证

**Files:** Modify RuntimeHostScanModels.cs、RuntimeHostScanner.cs、RuntimeManagedCommandTarget.cs、RuntimeDiagnosticHub.cs

- [ ] 启动时生成一次 InstanceId，记录 ProcessId、ProcessStartTimeUtc、resolved pipe。
- [ ] Host、生命周期、状态响应返回相同 InstanceId。
- [ ] Runtime 状态快照增加 GeneratedAt 和单调版本，不伪称用户业务属性是原子快照。
- [ ] 测试同进程 InstanceId 稳定、重启变化、版本不倒退。

### Task 9：纠正文档、配置和技能

**Files:** Modify CLI_CANCELLED错误分析报告.md、docs/DDNS_Snap四宿主运行时联调改进意见书.md、docs/RUNTIME_无配置文件整改方案.md、CLI/Quick Start 文档、skills/iwesun-runtime-integration/references/json-cli.md，以及本机 ddnssnap-multiprocess-runtime-debug 技能

- [ ] Agent 权威管道统一为 DdnsSnap.Agent.Server.RuntimeDiagnostics。
- [ ] 撤回由旧端点产生的 Agent 控制面和跨宿主耦合结论。
- [ ] 文档增加 target、错误分类、列表限制、Mutation 和状态字段。
- [ ] 扫描旧名称，只允许出现在明确的历史错误说明。
- [ ] 同步仓库技能与本机技能。

### Task 10：只写下一阶段大数据架构

**Files:** Create docs/superpowers/specs/2026-07-13-runtime-bounded-query-architecture-design.md；Modify 四宿主意见书

- [ ] 定义 RuntimeQueryOptions、服务端预算、字段投影、分页 token、业务适配器和原子快照契约。
- [ ] 比较单帧分页、分块多帧、流式 JSON 三种方案。
- [ ] 规定 Diagnostics、WebView2、代理命令的共同接口。
- [ ] 标注下一阶段待批准，本轮不创建实现类型。

### Task 11：全面回归但不发布

**Files:** Modify docs/REQUIREMENTS_ACTIVE.md

- [ ] 运行全解决方案 Debug/Release 构建。
- [ ] 运行 cli-context-shell、cli-transport-failure、cli-target-context、cli-list-bounds、sample-host-cli-full。
- [ ] 用正确 Agent 管道只读验证 host.info/lifecycle.status；未经授权不停止活动宿主。
- [ ] 只勾选有测试证据的需求。
- [ ] 不运行 build-runtime-setup.ps1，不生成正式 MSI，等待下一轮架构确认。

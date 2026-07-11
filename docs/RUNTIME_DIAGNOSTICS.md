# 运行时诊断

> command/state 控制面使用固定宽度匿名共享 FIFO：主控 FIFO 深度 128，逐单元 FIFO 深度 64。状态发送 `RuntimeState.Code`，控制发送 `RuntimeManagedCommandKind`；复杂内容继续走诊断管道。旧的命名 command/state MMF 和字符串/Base64 帧已退出活动实现。

> **状态**: CURRENT | **最后更新**: 2026-07-09
> **源码参考**: `Iwesun.Runtime.Diagnostics/`, `Iwesun.Runtime.Cli/`

宿主应用（如 DDNS Snap）通过 `Iwesun.Runtime.Diagnostics` 路由运行时诊断输出，替代直接的控制台/文件调试日志。正常运行时保持静默，直到监控器显式启用输出。

## 核心组件

| 组件                              | 职责                                  |
| --------------------------------- | ------------------------------------- |
| `RuntimeOutput`                   | 静态 TracePoint / Watch / Break API，业务代码调用入口 |
| `DiagnosticSwitchboard`           | 静态配置驱动的输出路由器、输出点与 FIFO 统计 |
| `RuntimeDiagnosticHub`            | 命名管道服务器，供实时监控、登记表查询和反射目标访问 |
| `RuntimeDiagnosticLoggerProvider` | Serilog 提供者集成                    |
| `RuntimeExecutionManager`         | 线程/任务执行状态、心跳和快照管理     |
| `RuntimeManagedRegistry`          | 统一登记表：注册、事件、命令队列与快照 |

## 数据流

```text
ILogger / RuntimeOutput.TracePoint()
  → RuntimeOutputSwitch.Enabled（一个可变静态布尔检查）
  → DiagnosticSwitchboard FIFO<string>
  → 后台泵
  → RuntimeDiagnosticHub 事件
  → 可选诊断文件
```

当管道输出和文件输出都禁用时，诊断调用在格式化或序列化载荷之前返回，性能开销最小。

## 开关配置

配置文件：`diagnostic-switchboard.json`

- DDNS Snap 中位于 `C:\ProgramData\DdnsSnap\diagnostic-switchboard.json`
- 存储开关和输出点元数据，不是日志文件
- 首次启动时从编译的监控目录生成，后续启动时修复（添加新监控点）
- Schema 版本 4 默认静默启动：`globalEnabled`、管道输出、文件输出、每个 section、每个编译输出点均生成为 `false`
- 保留用户对已知点 ID 的 `enabled` 更改，同时自动添加新编译的点 ID
- 输出点是稳定的诊断采样入口；默认通过 `SetOutputPoint()` 控制启用/禁用
- 逻辑断点与数值断点都支持编译期反射加载；数值断点使用 `DiagnosticNumericBreakpointAttribute`

每个 `outputPoints[]` 条目是一个稳定监控点：

```json
{
  "id": "log.pipeline",
  "category": "logging",
  "purpose": "DDNS pipeline stage ILogger events.",
  "section": "pipeline",
  "enabled": false
}
```

配置还包含 `runtimeDiagnosticsPipeName`，同一机器上运行多个 DDNS Snap 构建或服务时保持唯一。

## TracePoint / Watch / Break API

业务代码中使用静态 `RuntimeOutput.TracePoint()` 调用：

```csharp
RuntimeOutput.TracePoint("pipeline.stage", "DnsUpdateStage",
    new { Domain = domain, TargetIp = targetIp, Updated = true });
```

常见观测 API：
- `RuntimeOutput.TracePoint()`：输出结构化事件。
- `RuntimeOutput.Watch()`：对对象做快照并投递到诊断管道。
- `RuntimeOutput.BreakIf()`：协作式断点，命中后等待恢复信号。
- `RuntimeOutput.BreakIfNumbers()`：固定数值通道断点，默认通过白名单谓词/阈值绑定控制。
- `runtime.managed` 的 `processes/threads/tasks` 列表命令用于按单位类型查看注册项。
- `runtime.execution` 的 `snapshot` 命令用于查看线程/任务执行快照。

- 所有运行时追踪通过这些 API，不直接使用 `Console.WriteLine` 或文件写入
- 由 `DiagnosticSwitchboard` 根据配置路由到输出点（管道、文件、日志）
- 支持结构化 JSON 载荷

## 诊断管道

Service 和 Agent 主机从 `diagnostic-switchboard.json` 启动专用运行时诊断管道（默认名：`DdnsSnap.Runtime.Diagnostics`）。

管道接受 4 字节小端长度前缀的 `RuntimeDiagnosticFrame` JSON 消息，并返回 `RuntimeDiagnosticFrame` 响应（`frameType=response`）。`rtdiag/2.0` 保留单命令兼容；`rtdiag/3.0` 提供服务端原生 batch，一次连接可完成多个有序功能步骤。

统一协议约束：
- 管道协议以 JSON Frame 为唯一入口，不再支持旧命令对象直连。
- JSON 是权威功能协议，CLI 只是输入适配器；组合命令必须编译成一个 `rtdiag/3.0` batch frame，不能由客户端逐条重连发送。
- batch 最多 128 步，每步有稳定 `id`、独立状态、数据和耗时；支持统一 deadline、`stopOnError`、单步 `continueOnError`、延迟及基于前序步骤成功状态的条件执行。
- `when.path`/`when.expected` 可对前序步骤的 JSON 结果做字段条件；`bindings` 可把前序结果字段按原始 JSON 类型绑定到后续命令参数。绑定不经过字符串格式化，数字、布尔值、字符串和对象类型均保持不变。
- 服务端按步骤返回 `OK`、`ERROR`、`SKIPPED` 或 `DEADLINE_EXCEEDED`，整体状态反映是否存在失败步骤。

托管执行对象的退出规则：`RThread`、`RTask` 和 `RProcess` 只有在底层执行单元真实结束后才允许从 managed 登记表移除。token-aware `RTask` 通过 FIFO Stop 取消其框架拥有的 token；无 token 任务收到 Stop 后保持登记并等待自然完成，因此主控会在其未完成时正确进入超时，而不会误报正常退出。运行中的 `Dispose` 仅请求/延迟清理，不能提前反登记。
- CLI 命令语法仅作为人机接口，最终全部包装为 Frame 下发到运行时。
- 新增字段应优先扩展 `header`、`status`、`extStatus`、`data`，避免回退到旧结构。

不要实现自定义诊断客户端，使用 `Iwesun.Runtime.Cli` 连接。

## 数值断点策略

数值断点推荐保持“静态类型通道 + 运行时改参”：

- 宿主程序集用 `DiagnosticNumericBreakpointAttribute` 声明默认绑定
- 运行时仅调整 `operator` 与 `threshold1/threshold2`
- `RuntimeNumericPredicateCatalog` 负责固定谓词原语（如 `gt2`、`between3`、`delta-le3`）
- `RuntimeDiagnosticHub` 负责 CLI 入口与绑定修改

这样能保证：
- 不引入任意表达式执行
- 不改变变量位/输入位
- 只在白名单内做比较符和常量调整

## Serilog 集成

`RuntimeDiagnosticLoggerProvider` 将 Serilog 事件路由到诊断开关板。日志事件通过 `log.*` 监控点输出，可按 section（如 `pipeline`、`agent-sync`、`peer-sync`）控制。

## 诊断目标

开关板支持多个输出目标：
- **命名管道**：实时监控（CLI 连接）
- **文件**：持久化诊断输出
- **日志**：Serilog 集成

输出目标可独立启用/禁用。

## 登记表与可观测对象

当前实现中的登记表主要由 `RuntimeDiagnosticHub` 提供：
- `diagnostics.registry`：返回 watch points、breakpoints、hooks 的登记快照。
- `diagnostics.pipes`：返回分支管道注册表，可申请/释放分支专有管道并按内部 ID 解析。
- 反射目标：通过 `RegisterObject()` 暴露可读/可写/可调用成员。
- 功能测试场景中的树对象：通过登记表和反射目标暴露树快照、节点路径、统计值、断点命中与输出点记录。

对于需要“先知道程序里有什么，再去测试什么”的场景，优先从登记表入手，而不是直接猜测对象模型。

## 设计原则

- 默认不输出
- 通过 switchboard/配置控制监视点
- 输出点与业务配置分离
- 不改变主程序逻辑
- 只能观察和取样，不能代替业务决策

## 相关文档

- CLI 使用 → [IWESUN_RUNTIME_CLI.md](IWESUN_RUNTIME_CLI.md)
- 状态分类基础类型 → [05-runtime-tooling/STATE_CLASSIFICATION.md](05-runtime-tooling/STATE_CLASSIFICATION.md)
- 状态分类在线业务实现 → [05-runtime-tooling/STATE_CLASSIFICATION_ONLINE.md](05-runtime-tooling/STATE_CLASSIFICATION_ONLINE.md)
- 状态分类实施计划 → [05-runtime-tooling/STATE_CLASSIFICATION_PLAN.md](05-runtime-tooling/STATE_CLASSIFICATION_PLAN.md)
- 状态分类任务书 → [05-runtime-tooling/STATE_CLASSIFICATION_TASKS.md](05-runtime-tooling/STATE_CLASSIFICATION_TASKS.md)
- 线程与任务管理 → [05-runtime-tooling/THREAD_TASK_MANAGEMENT.md](05-runtime-tooling/THREAD_TASK_MANAGEMENT.md)
- 线程与任务管理实施计划 → [05-runtime-tooling/THREAD_TASK_MANAGEMENT_PLAN.md](05-runtime-tooling/THREAD_TASK_MANAGEMENT_PLAN.md)
- 独立样板宿主 → [05-runtime-tooling/SAMPLE_HOST.md](05-runtime-tooling/SAMPLE_HOST.md)
- 注入器标准化 → [05-runtime-tooling/INJECTOR_STANDARDIZATION.md](05-runtime-tooling/INJECTOR_STANDARDIZATION.md)
- 注入器标准化技术方案 → [05-runtime-tooling/INJECTOR_STANDARDIZATION_PLAN.md](05-runtime-tooling/INJECTOR_STANDARDIZATION_PLAN.md)
- 注入器标准化任务书 → [05-runtime-tooling/INJECTOR_STANDARDIZATION_TASKS.md](05-runtime-tooling/INJECTOR_STANDARDIZATION_TASKS.md)
- 守护代理与管道注册中心设计 → [05-runtime-tooling/GUARDIAN_PIPE_REGISTRY_DESIGN.md](05-runtime-tooling/GUARDIAN_PIPE_REGISTRY_DESIGN.md)
- 注入器静态方案（旧方案保留）→ [05-runtime-tooling/INJECTOR_STATIC_SCHEME.md](05-runtime-tooling/INJECTOR_STATIC_SCHEME.md)
- 注入器动态缓冲池方案（对象级登记与回收）→ [05-runtime-tooling/INJECTOR_DYNAMIC_POOL_SCHEME.md](05-runtime-tooling/INJECTOR_DYNAMIC_POOL_SCHEME.md)
- 统一界面规范 → [UNIFIED_INTERFACE.md](UNIFIED_INTERFACE.md)
- AI 访问规则复核 → [AI_ACCESS_RECHECK.md](AI_ACCESS_RECHECK.md)
- 全局访问规则 → [../.github/instructions/copilot-access-rules.instructions.md](../.github/instructions/copilot-access-rules.instructions.md)

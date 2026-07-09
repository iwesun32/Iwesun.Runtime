# Iwesun Runtime 文档索引

Iwesun Runtime 是独立的运行时工具库，提供诊断内核、CLI 控制入口与 WebRuntime 通信模型。

## 快速入口

| 文档 | 说明 |
| --- | --- |
| [PROJECT_SUMMARY.md](PROJECT_SUMMARY.md) | 项目总览：定位、结构、依赖关系、核心链路 |
| [RUNTIME_DIAGNOSTICS.md](RUNTIME_DIAGNOSTICS.md) | 运行时诊断核心机制、协议与输出控制 |
| [IWESUN_RUNTIME_CLI.md](IWESUN_RUNTIME_CLI.md) | CLI 命令手册、配置驱动与管道入口 |
| [REQUIREMENTS_ACTIVE.md](REQUIREMENTS_ACTIVE.md) | 当前活跃需求、功能覆盖与执行复核 |
| [UNIFIED_INTERFACE.md](UNIFIED_INTERFACE.md) | 统一界面与术语规范 |
| [AI_ACCESS_RECHECK.md](AI_ACCESS_RECHECK.md) | AI 忽略/禁止/推荐读取范围复核 |

## 文档分组

### 当前权威入口
- `docs/README.md`：文档索引入口。
- `docs/REQUIREMENTS_ACTIVE.md`：当前活跃需求与功能测试状态。
- `docs/RUNTIME_DIAGNOSTICS.md`：诊断内核、开关板、Hub、输出点说明。
- `docs/IWESUN_RUNTIME_CLI.md`：CLI 配置、命令、管道与 WebRuntime 桥接说明。

### 专题文档
- `docs/05-runtime-tooling/`：状态、线程/任务、样板宿主、注入器标准化等专题。
- `docs/HANDOFF_*`：阶段性交接记录。
- `docs/archive/`：历史资料，仅作参考，不作为当前权威入口。

## 项目结构

| 项目 | 说明 |
| --- | --- |
| `Iwesun.Runtime.Diagnostics` | 运行时诊断内核：开关板、FIFO、Hub、反射、监控管道 |
| `Iwesun.Runtime.WebView2` | WebRuntime 客户端模型和管道客户端 |
| `Iwesun.Runtime.Cli` | 命令行工具：诊断、快照查询、事件消费、WebRuntime 控制 |
| `Iwesun.Runtime.SampleHost` | 最小样板宿主：用于本地接入与演示 |

解决方案：`Iwesun.Runtime.slnx`

## 专题文档（05-runtime-tooling）

| 文档 | 说明 |
| --- | --- |
| [STATE_CLASSIFICATION.md](05-runtime-tooling/STATE_CLASSIFICATION.md) | 可继承状态值类型、精确相等与 is 属于判断 |
| [STATE_MACHINE_HIERARCHY.md](05-runtime-tooling/STATE_MACHINE_HIERARCHY.md) | 分层状态机基础设计、主态与子态分层及跃迁规则 |
| [STATE_MACHINE_HIERARCHY_TASKS.md](05-runtime-tooling/STATE_MACHINE_HIERARCHY_TASKS.md) | 分层状态机基础任务拆解与验收清单 |
| [STATE_CLASSIFICATION_ONLINE.md](05-runtime-tooling/STATE_CLASSIFICATION_ONLINE.md) | 在线业务实现技术文档、启动/工作/停止状态接入 |
| [STATE_CLASSIFICATION_PLAN.md](05-runtime-tooling/STATE_CLASSIFICATION_PLAN.md) | 状态分类实施计划、阶段拆分、验收标准 |
| [STATE_CLASSIFICATION_TASKS.md](05-runtime-tooling/STATE_CLASSIFICATION_TASKS.md) | 状态分类任务书、可执行拆分 |
| [THREAD_TASK_MANAGEMENT.md](05-runtime-tooling/THREAD_TASK_MANAGEMENT.md) | 线程与任务管理设计、静态/动态表、启动/停止注入 |
| [PROCESS_THREAD_INJECTION.md](05-runtime-tooling/PROCESS_THREAD_INJECTION.md) | 进程/线程标准注入、前缀替换策略与生命周期注入 |
| [PROCESS_THREAD_INTERCEPTION_DECISION.md](05-runtime-tooling/PROCESS_THREAD_INTERCEPTION_DECISION.md) | 继承与包装讨论结论、放行方案与路线图 |
| [THREAD_TASK_MANAGEMENT_PLAN.md](05-runtime-tooling/THREAD_TASK_MANAGEMENT_PLAN.md) | 线程与任务管理实施计划、阶段拆分、验收标准 |
| [SAMPLE_HOST.md](05-runtime-tooling/SAMPLE_HOST.md) | 独立样板宿主、代码注入、诊断接入、发布模板 |
| [INJECTOR_STANDARDIZATION.md](05-runtime-tooling/INJECTOR_STANDARDIZATION.md) | 六大类注入器标准化、最简写法、宏式体验边界 |
| [INJECTOR_STANDARDIZATION_PLAN.md](05-runtime-tooling/INJECTOR_STANDARDIZATION_PLAN.md) | 注入器标准化技术方案、实现边界、分阶段实施 |
| [INJECTOR_STANDARDIZATION_TASKS.md](05-runtime-tooling/INJECTOR_STANDARDIZATION_TASKS.md) | 注入器标准化任务书、交付清单、验收标准 |

### 当前测试与诊断专题
- `REQUIREMENTS_ACTIVE.md`：功能清单、测试矩阵、扩展层与树场景。
- `RUNTIME_DIAGNOSTICS.md`：输出点、登记表、断点、钩子、Hub 路由。
- `IWESUN_RUNTIME_CLI.md`：CLI 命令、配置与管道协议。

## 交接文档

| 文档 | 说明 |
| --- | --- |
| [HANDOFF_2026-07-09_INJECTOR_STANDARDIZATION.md](HANDOFF_2026-07-09_INJECTOR_STANDARDIZATION.md) | 注入器标准化阶段交接与后续重点 |

## 使用说明

- 先看 `docs/README.md` 找入口。
- 如果要确认当前实现和测试状态，优先看 `docs/REQUIREMENTS_ACTIVE.md`。
- 如果要理解运行时诊断机制，优先看 `docs/RUNTIME_DIAGNOSTICS.md`。
- 如果要理解 CLI 行为和命令配置，优先看 `docs/IWESUN_RUNTIME_CLI.md`。

## 归档说明

`docs/archive/` 为历史资料区，不作为当前权威入口。当前迭代仅以本索引列出的活跃文档为准。

## 构建

```bash
dotnet build Iwesun.Runtime.slnx -c Release
```

## 宿主集成

宿主应用（如 DDNS Snap）通过跨仓库 `ProjectReference` 引用 `Iwesun.Runtime.Diagnostics`。CLI 作为独立工具运行，不被宿主引用。

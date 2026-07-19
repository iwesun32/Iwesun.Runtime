# Iwesun Runtime 文档索引

- [下一阶段框架规划](RUNTIME_NEXT_STAGE_FRAMEWORK_PLAN.md)：1.0.19 稳定基线、客户证据池、候选架构和实施准入门槛。

Iwesun Runtime 是独立的运行时工具库，提供诊断内核、CLI 控制入口与 WebRuntime 通信模型。

## 快速入口

| 文档 | 说明 |
| --- | --- |
| **[IWESUN_RUNTIME_QUICK_START.md](IWESUN_RUNTIME_QUICK_START.md)** | ⭐ **速查手册**：备份主程序、引用 DLL、全局替换、管道/日志配置、编译验证 |
| **[RELEASE_NOTES.md](RELEASE_NOTES.md)** | 当前版本更新记录、修复内容、验证结果和已知边界 |
| [IWESUN_RUNTIME_RELEASE_INDEX.md](IWESUN_RUNTIME_RELEASE_INDEX.md) | 安装包专用文档索引；发布器将其安装为 `docs/README.md` |
| **[IWESUN_RUNTIME_DESIGN.md](IWESUN_RUNTIME_DESIGN.md)** | ⭐ **完整设计文档**：架构、功能分类、注入界面、JSON指令、CLI格式 |
| **[IWESUN_RUNTIME_USER_GUIDE.md](IWESUN_RUNTIME_USER_GUIDE.md)** | 业务接入权威手册：主程序、受管执行、单点注入、状态退出、JSON/CLI |
| **[IWESUN_RUNTIME_WINDOWS_SERVICE.md](IWESUN_RUNTIME_WINDOWS_SERVICE.md)** | Windows Service 专项手册：SCM 生命周期、业务边界、清理状态与退出码 |
| **[IWESUN_RUNTIME_REMOTE_ACCESS.md](IWESUN_RUNTIME_REMOTE_ACCESS.md)** | 远程管道专项手册：AI 账号、Program.cs ACL、本地兼容与 CLI 登录 |
| **[IWESUN_RUNTIME_REMOTE_CONSOLE.md](IWESUN_RUNTIME_REMOTE_CONSOLE.md)** | 远程控制服务：账号授权、SCM 安装、Shell 插槽、上传、审批与输出跟随 |
| [PROJECT_SUMMARY.md](PROJECT_SUMMARY.md) | 项目总览：定位、结构、依赖关系、核心链路 |
| [RUNTIME_DIAGNOSTICS.md](RUNTIME_DIAGNOSTICS.md) | 运行时诊断核心机制、协议与输出控制 |
| [RUNTIME_ROOT_DATA_STRUCTURE.md](RUNTIME_ROOT_DATA_STRUCTURE.md) | RuntimeRoot 单根挂载的数据结构总览（T01~T10） |
| [DATA_PROJECT_RUNTIME_ROOT.md](05-runtime-tooling/DATA_PROJECT_RUNTIME_ROOT.md) | Data 公共项目、RuntimeRoot 基础类型与容器技术说明 |
| [RECORD_STORE_V2_PUBLIC_API.md](../../Data/docs/02-api/RECORD_STORE_V2_PUBLIC_API.md) | RecordStore V2公共属性、事件、委托和方法语义 |
| [DLIST_TO_RECORD_STORE_V2_MIGRATION.md](../../Data/docs/02-api/DLIST_TO_RECORD_STORE_V2_MIGRATION.md) | DList到RecordStore V2迁移指南 |
| [RECORD_STORE_1_0_25_TO_V2_MIGRATION.md](../../Data/docs/02-api/RECORD_STORE_1_0_25_TO_V2_MIGRATION.md) | RecordStore 1.0.25到V2迁移指南 |
| [IWESUN_RUNTIME_CLI.md](IWESUN_RUNTIME_CLI.md) | CLI 命令手册、配置驱动与管道入口 |
| [WEBVIEW2_JSON_PIPE_CLI_PLAN.md](../Iwesun.Runtime.WebView2/docs/WEBVIEW2_JSON_PIPE_CLI_PLAN.md) | WebView2 统一 JSON 协议、管道唯一申请与 CLI v3 迁移结果 |
| [SCRIPT_REFLECTION_PLAN.md](../Iwesun.Runtime.WebView2/docs/SCRIPT_REFLECTION_PLAN.md) | WebView2 受控脚本、反射边界与实现状态 |
| [DOM_SNAPSHOT_API.md](../Iwesun.Runtime.WebView2/docs/DOM_SNAPSHOT_API.md) | WebView2 完整 DOM 真快照 API、恢复与链接 |
| [DATA_STREAM_MONITOR_RECORDER.md](../Iwesun.Runtime.WebView2/docs/DATA_STREAM_MONITOR_RECORDER.md) | WebView2 数据流记录器 C# API、JSON Frame、CLI 和安全边界 |
| [WEBVIEW2_1.0.26_UPGRADE.md](../Iwesun.Runtime.WebView2/docs/WEBVIEW2_1.0.26_UPGRADE.md) | WebView2 1.0.26 升级、配置兼容和回退说明 |
| [REQUIREMENTS_ACTIVE.md](REQUIREMENTS_ACTIVE.md) | 当前活跃需求、功能覆盖与执行复核 |
| [iwesun-runtime-integration](../skills/iwesun-runtime-integration/SKILL.md) | 可复制的 Codex Runtime 业务接入技能 |
| [UNIFIED_INTERFACE.md](UNIFIED_INTERFACE.md) | 统一界面与术语规范 |
| [AI_ACCESS_RECHECK.md](AI_ACCESS_RECHECK.md) | AI 忽略/禁止/推荐读取范围复核 |

## 文档分组

### 当前权威入口
- `docs/IWESUN_RUNTIME_QUICK_START.md`：最短接入路径，照步骤替换即可。
- `docs/IWESUN_RUNTIME_DESIGN.md`：**主设计文档**，完整覆盖所有设计主题。
- `docs/README.md`：文档索引入口。
- `docs/REQUIREMENTS_ACTIVE.md`：当前活跃需求与功能测试状态。
- `docs/RUNTIME_DIAGNOSTICS.md`：诊断内核、开关板、Hub、输出点说明。
- `docs/RUNTIME_ROOT_DATA_STRUCTURE.md`：统一根对象下的数据结构、键设计与关联关系。
- `docs/IWESUN_RUNTIME_CLI.md`：CLI 配置、命令、管道与 WebRuntime 桥接说明。
- `docs/IWESUN_RUNTIME_WINDOWS_SERVICE.md`：Windows Service 平台入口和业务接入边界。
- `docs/IWESUN_RUNTIME_REMOTE_ACCESS.md`：远端 AI 账号、源码固定授权与 CLI IPC 登录边界。
- `docs/IWESUN_RUNTIME_REMOTE_CONSOLE.md`：独立远程控制服务、管理员安装和 CLI 审批执行边界。

### 专题文档
- `docs/05-runtime-tooling/`：状态、线程/任务、样板宿主、注入器标准化等专题。
- `docs/HANDOFF_*`：阶段性交接记录。
- `docs/archive/`：历史资料，仅作参考，不作为当前权威入口。

## 项目结构

| 项目 | 说明 |
| --- | --- |
| `Iwesun.Runtime.Data` | Runtime 专属数据层：RuntimeRoot 条目、静态注入目录和值类型指令 |
| `Iwesun.Data` | 独立数据基础库：RecordStore 稳定记录、身份、约束、深复制与快照能力 |
| `Iwesun.Runtime.Diagnostics` | 运行时诊断内核：开关板、FIFO、Hub、反射、监控管道 |
| `Iwesun.Runtime.WebView2` | WebRuntime 公共平台：标准 Frame 编解码、专用管道客户端、虚拟输入、预编译 C# 程序截获转接、生命周期与执行监控 |
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
| [RUNTIME_INTEGRATION_GUIDE.md](05-runtime-tooling/RUNTIME_INTEGRATION_GUIDE.md) | 发布版接入手册：启动/结束模板、创建API、状态接口、CLI扩展与发布清单 |
| [RUNTIME_RELEASE_PACKAGING.md](05-runtime-tooling/RUNTIME_RELEASE_PACKAGING.md) | Runtime 发布总项目、发布清单、目录结构、MSI 安装与卸载 |
| [WEB_RUNTIME_CONTROL.md](../Iwesun.Runtime.WebView2/docs/WEB_RUNTIME_CONTROL.md) | WebView2 公共控制、C# Program 与监控接口 |
| [DOM_SNAPSHOT_API.md](../Iwesun.Runtime.WebView2/docs/DOM_SNAPSHOT_API.md) | WebView2 完整运行时 DOM 真快照技术手册：C# API、CLI、JSON、响应接收与样例 |
| [WEBVIEW2_RUNTIME_CAPABILITIES.md](../Iwesun.Runtime.WebView2/docs/WEBVIEW2_RUNTIME_CAPABILITIES.md) | WebView2 运行时能力规划与动作命名规范 |
| [INJECTOR_STANDARDIZATION.md](05-runtime-tooling/INJECTOR_STANDARDIZATION.md) | 六大类注入器标准化、最简写法、宏式体验边界 |
| [INJECTOR_STANDARDIZATION_PLAN.md](05-runtime-tooling/INJECTOR_STANDARDIZATION_PLAN.md) | 注入器标准化技术方案、实现边界、分阶段实施 |
| [INJECTOR_STANDARDIZATION_TASKS.md](05-runtime-tooling/INJECTOR_STANDARDIZATION_TASKS.md) | 注入器标准化任务书、交付清单、验收标准 |
| [GUARDIAN_PIPE_REGISTRY_DESIGN.md](05-runtime-tooling/GUARDIAN_PIPE_REGISTRY_DESIGN.md) | 守护代理、管道注册中心、注入器缓冲池登记与回收设计 |
| [INJECTOR_STATIC_SCHEME.md](05-runtime-tooling/INJECTOR_STATIC_SCHEME.md) | 注入器静态方案（旧方案保留） |
| [INJECTOR_DYNAMIC_POOL_SCHEME.md](05-runtime-tooling/INJECTOR_DYNAMIC_POOL_SCHEME.md) | 注入器动态缓冲池方案（对象级登记与回收） |
| [DATA_PROJECT_RUNTIME_ROOT.md](05-runtime-tooling/DATA_PROJECT_RUNTIME_ROOT.md) | Data 公共项目的 RuntimeRoot 基础类技术说明 |
| [RUNTIME_ROOT_CONTAINER_TECHNICAL.md](05-runtime-tooling/RUNTIME_ROOT_CONTAINER_TECHNICAL.md) | RuntimeRoot 容器技术实现（DLIST + 辅助索引） |

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

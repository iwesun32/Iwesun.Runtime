# 状态分类在线业务实现技术文档

> **状态**: CURRENT | **最后更新**: 2026-07-09
> **源码参考**: `Iwesun.Runtime.Diagnostics/RuntimeState.cs`, `Iwesun.Runtime.Diagnostics/RuntimeStateCatalog.cs`, `Iwesun.Runtime.Diagnostics/RuntimeStateContracts.cs`, `Iwesun.Runtime.Diagnostics/RuntimeOutput.cs`, `Iwesun.Runtime.Diagnostics/RuntimeDiagnosticHub.cs`, `Iwesun.Runtime.Diagnostics/RuntimeDiagnosticsMonitor.cs`, `Iwesun.Runtime.Diagnostics/RuntimeDiagnosticsServiceCollectionExtensions.cs`

本文描述状态分类基础类型在在线业务里的落地方式。这里的“在线业务”指真实宿主正在运行时的状态流转、登记、查询和收尾，而不是离线定义阶段。

当前 Runtime.Diagnostics 已实现基础状态值类型、状态目录和运行时状态管理器，宿主可通过 DI 和诊断 Hub 查询当前状态与状态树。独立样板宿主也已接入该能力，用来验证启动、切换、查询和收尾的完整最小闭环。

在线管理器当前提供三类便捷切换：`SetStart()`、`SetWorking()`、`SetStop()`，并暴露 `IsStart`、`IsWorking`、`IsStopping` 供监控界面快速判断。

状态的显示与转换已经拆成独立接口 `IRuntimeStateTextConverter<TState>`：默认会根据当前 UI 文化输出对应名称，必要时可以按枚举名、状态名或显示名解析回状态值，而不依赖 `ToString()` 作为唯一转换入口。

## 目标

- 让业务运行时能够实时表达当前大状态与子状态。
- 让监控侧同时看到粗粒度判断和精确判断。
- 让状态登记可以贯穿启动、工作、停止三个生命周期阶段。
- 让业务代码只做最小注入，不把状态分类硬编码成一大坨 if/else。

## 在线使用方式

在线业务里，状态分类不会以“纯文本标签”形式散落在各处，而是通过基础值类型承载：

1. 宿主或模块在启动时登记自己的初始状态。
2. 进入工作流后切换为 `Working`。
3. 在工作态内部继续细分为 `Idle`、`Collecting`、`Analyzing`、`NetworkAccess`。
4. 停止时切回 `Stop`，并继续细分 `Requested`、`Draining`、`Completed`、`Timeout`。

## 在线业务中的判断规则

### 精确判断

当业务需要知道“现在是不是就是这个状态本身”时，使用精确判断：

- 是否正处于 `Working.Analyzing`
- 是否正处于 `Stop.Timeout`
- 是否正处于 `Start`

### 包含判断

当业务只关心大类时，使用 `is 属于`：

- `Working.Idle` 属于 `Working`
- `Working.Collecting` 属于 `Working`
- `Stop.Draining` 属于 `Stop`

这样监控和业务判断可以同时存在：

- 监控界面可以看细状态。
- 上层逻辑可以看粗状态。

## 在线注入点

状态分类在在线业务中的注入点建议和生命周期绑定：

| 阶段 | 注入内容                                     |
| ---- | -------------------------------------------- |
| 启动 | 注册初始状态、注册静态状态族、初始化查询面。 |
| 工作 | 状态切换、细分子状态、心跳更新、附带上下文。 |
| 停止 | 切换停止族、发出收尾状态、等待退出完成。     |

## 与线程/任务管理的配合

状态分类不是单独孤立使用，而是给线程和任务表提供统一的状态语义：

- 线程记录可以写入 `Start / Working / Stop`。
- 任务记录可以写入 `Working.Idle / Working.Collecting / Working.Analyzing / Working.NetworkAccess`。
- 停止记录可以写入 `Stop.Requested / Stop.Draining / Stop.Completed / Stop.Timeout`。

这样做的好处是：

1. 线程和任务查询可以复用同一套状态判断接口。
2. 在线业务和监控界面看到的是同一种状态树。
3. 新增子状态时，不会破坏上层对父状态的判断。

## 实现约束

- 在线业务不应直接依赖魔法字符串比较状态。
- 状态变化必须有明确的登记点。
- 子状态可以扩展，但父状态必须保持稳定。
- 状态值对象必须能被快照、序列化和查询。

## 当前状态

本文所述在线接入能力**已实现基础闭环**：基础状态值类型、判断接口、目录管理和运行时状态管理器已可用，并可通过 `runtime.state` 进行查询与更新。样板宿主已验证了启动、工作、停止这条主线。更细的业务侧注入点、线程/任务联动和跨模块统一收尾策略仍可继续扩展。

## 相关文档

- 基础类型设计 → [STATE_CLASSIFICATION.md](STATE_CLASSIFICATION.md)
- 实施计划 → [STATE_CLASSIFICATION_PLAN.md](STATE_CLASSIFICATION_PLAN.md)
- 任务书 → [STATE_CLASSIFICATION_TASKS.md](STATE_CLASSIFICATION_TASKS.md)

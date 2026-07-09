# 状态分类基础类型设计

> **状态**: CURRENT | **最后更新**: 2026-07-09
> **源码参考**: `Iwesun.Runtime.Diagnostics/RuntimeState.cs`, `Iwesun.Runtime.Diagnostics/RuntimeStateCatalog.cs`, `Iwesun.Runtime.Diagnostics/RuntimeStateContracts.cs`, `Iwesun.Runtime.Diagnostics/RuntimeStateManager.cs`, `Iwesun.Runtime.Diagnostics/RuntimeDiagnosticsServiceCollectionExtensions.cs`, `Iwesun.Runtime.Diagnostics/RuntimeOutput.cs`

本文定义一种可扩展的运行时状态分类基础类型，用来描述“粗粒度状态”和“细粒度子状态”的同时存在关系。

当前实现已经采用枚举键作为基础标识，支持树状父子关系、精确相等判断、`is 属于` 判断，以及独立的多语言文本转换接口。

它面向的不是单纯的枚举常量，而是**带层级关系的数值值对象**：

- 可以做**精确相等**判断。
- 可以做**is 属于**判断，也就是“包含/归属”判断。
- 可以在后续继续扩展子状态，而不破坏已有粗粒度判断。

## 背景

在运行时观测里，经常会先有一组很粗的状态：

- 启动
- 工作
- 停止

但程序深入以后，`工作` 往往会拆出更细的子状态，例如：

- 空闲
- 数据收集
- 分析
- 网络访问

这些子状态都属于 `工作`。因此，状态系统需要同时支持：

1. **粗判**：某个状态是不是 `工作`。
2. **精判**：某个状态是不是 `数据收集`。

## 设计目标

- 先支持最小三态：启动、工作、停止。
- 支持后续为“工作”继续拆分子状态。
- 支持粗粒度包含判断与精确判断同时成立。
- 允许继续 `Add` 新状态，而不破坏旧代码对父状态的判断。
- 保持值类型语义，方便高频比较和快照传递。

## 核心语义

这个类型不是简单的平铺枚举，而是**树状状态分类**。

### 两种比较接口

需要两个能力完全分离：

| 能力     | 语义                                 | 示例                                |
| -------- | ------------------------------------ | ----------------------------------- |
| 精确相等 | 当前状态是否就是这个状态本身         | `current == Working.DataCollecting` |
| is 属于  | 当前状态是否属于某个父状态或祖先状态 | `current.Is(Working)`               |

精确相等只看当前节点。
is 属于 要看父链或祖先链。

### 继承和扩展

基础状态要允许继续扩展：

- `Working` 可以继续扩展出 `Idle`、`Collecting`、`Analyzing`、`NetworkAccess`。
- 未来如果还要更细，可以继续在子状态下加孙状态。
- 父状态不需要改，旧代码依然可以只判断父状态。

## 类型定位

这里建议使用一个**基础值类型**来承载状态，而不是直接使用裸 `enum`。

原因是裸 `enum` 只有平铺值，不天然表达“属于某个父状态”的语义。

推荐的概念模型是：

- 外层：一个不可变值类型，作为状态实例。
- 内层：一个数值标识，作为状态码。
- 旁路：父状态链，作为包含关系的依据。

换句话说，这不是 CLR 层面的 `enum` 继承，而是**数值值对象 + 父子关系** 形成的可扩展状态树。

## 接口模型

### 精确相等接口

精确相等接口负责“是不是这个状态本身”。

建议语义如下：

```text
ExactEqual(other) -> bool
```

或者直接由值对象实现标准相等语义：

- `Equals(...)`
- `==`
- `!=`

### is 属于接口

is 属于接口负责“是不是某个状态族的一员”。

建议语义如下：

```text
Is(parent) -> bool
```

例如：

- `Working.DataCollecting.Is(Working) == true`
- `Working.DataCollecting.Is(Working.DataCollecting) == true`
- `Working.DataCollecting.Is(Stop) == false`

### 推荐接口拆分

| 接口                          | 职责                                 |
| ----------------------------- | ------------------------------------ |
| `IStateExactMatch<TState>`    | 提供精确相等判断。                   |
| `IStateHierarchy<TState>`     | 提供 `Is(...)` / 包含判断。          |
| `IStateExtension<TState>`     | 提供扩展、派生、注册新子状态的能力。 |
| `IStateTextConverter<TState>` | 提供多语言显示与文本转换能力。       |

接口可以按需要命名，但职责必须分离：**精确判断和包含判断不能混成一个模糊方法**。

### 枚举基础

基础状态现在同时具备枚举基础与值对象语义：

- `RuntimeStateKey` 作为稳定的枚举键，用来表示已知状态族。
- `RuntimeState` 作为值对象，承载代码、层级、显示名和目录引用。
- `RuntimeStateCatalog` 负责枚举键、代码、名称和显示名之间的转换。

这意味着：

- 枚举适合做“标准状态集”的基础。
- 值对象适合做“可扩展状态树”的承载。

## 建议的数据结构

建议的状态值对象可以用如下要素描述：

| 要素          | 说明                                        |
| ------------- | ------------------------------------------- |
| `Code`        | 数值状态码，适合快比较。                    |
| `Name`        | 语义名称。                                  |
| `ParentCode`  | 父状态码，用于 `Is(...)` 判断。             |
| `Level`       | 层级深度，可选。                            |
| `Kind`        | 状态分类组，例如 `Lifecycle`、`WorkPhase`。 |
| `DisplayName` | 人类可读名称。                              |
| `Aliases`     | 可选别名。                                  |

## 语义示例

### 初始三态

最初只保留三种粗粒度状态：

- `Start`
- `Working`
- `Stop`

此时程序只要知道“开始了没有”“正在工作没有”“准备结束没有”就够了。

### 细分工作态

后续对 `Working` 继续扩展：

- `Working.Idle`
- `Working.Collecting`
- `Working.Analyzing`
- `Working.NetworkAccess`

这些都应满足：

- `Working.Idle.Is(Working) == true`
- `Working.Collecting.Is(Working) == true`
- `Working.Analyzing.Is(Working) == true`
- `Working.NetworkAccess.Is(Working) == true`

但精确相等仍然要区分：

- `Working.Idle == Working` 应为 `false`
- `Working.Idle == Working.Idle` 应为 `true`

## 扩展规则

1. 父状态优先稳定，不随细分频繁改名。
2. 子状态可以继续 `Add`，但不能破坏旧父状态判断。
3. 新增状态应当显式注册，不能只靠魔法字符串临时拼接。
4. 状态码必须保持唯一。
5. 状态树必须可查询、可序列化、可快照。

## 和线程/任务管理的关系

线程和任务管理会用到这套状态分类：

- 线程可以有 `Start`、`Working`、`Stop`。
- 任务可以在 `Working` 下面继续细分为 `Idle`、`Collecting`、`Analyzing`、`NetworkAccess`。
- 停止流程可以继续细分为 `Stop.Requested`、`Stop.Draining`、`Stop.Completed`、`Stop.Timeout`。

这样一来，监控程序既能粗看大类，也能精看子状态。

## 当前状态

本文所述基础类型与目录管理能力**已实现**，对应源码位于：

- `Iwesun.Runtime.Diagnostics/RuntimeState.cs`
- `Iwesun.Runtime.Diagnostics/RuntimeStateCatalog.cs`
- `Iwesun.Runtime.Diagnostics/RuntimeStateContracts.cs`
- `Iwesun.Runtime.Diagnostics/RuntimeStateManager.cs`

已完成的关键点：

- 枚举键作为稳定基础标识。
- `RuntimeState` 作为可扩展值对象。
- `ExactEquals(...)` 与 `Is(...)` 分离。
- 独立的文本转换接口，不依赖 `ToString()` 作为唯一入口。
- 运行时目录可通过 `runtime.state` 暴露给诊断 Hub。

当前仍待补齐的是更完整的线程/任务联动层和统一停止流程联动层。本文保留为基础语义设计文档，但不再把已实现内容写成待办。

## 相关文档

- 线程与任务管理 → [THREAD_TASK_MANAGEMENT.md](THREAD_TASK_MANAGEMENT.md)
- 线程与任务管理实施计划 → [THREAD_TASK_MANAGEMENT_PLAN.md](THREAD_TASK_MANAGEMENT_PLAN.md)
- 运行时诊断总览 → [../RUNTIME_DIAGNOSTICS.md](../RUNTIME_DIAGNOSTICS.md)

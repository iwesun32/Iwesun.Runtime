# RuntimeRoot 统一数据结构总览

> **状态**: CURRENT | **最后更新**: 2026-07-17
> **源码参考**: `Iwesun.Runtime.Diagnostics/DiagnosticSwitchboard.cs`, `Iwesun.Runtime.Diagnostics/RegistryBuilder.cs`, `Iwesun.Runtime.Diagnostics/RuntimeDiagnosticBreakpoints.cs`, `Iwesun.Runtime.Diagnostics/RuntimeDiagnosticHooks.cs`, `Iwesun.Runtime.Diagnostics/RuntimeStateManager.cs`, `Iwesun.Runtime.Diagnostics/RuntimeExecutionManagement.cs`, `Iwesun.Runtime.Diagnostics/RuntimeManagedRegistry.cs`, `Iwesun.Runtime.Diagnostics/RuntimeDiagnosticHub.cs`, `Iwesun.Runtime.Diagnostics/ReflectionRuntimeDiagnosticTarget.cs`, `Iwesun.Runtime.Diagnostics/RuntimeDiagnosticModels.cs`

本文将 Runtime.Diagnostics 当前可观测与可管理的数据结构统一挂在一个逻辑根对象 `RuntimeRoot` 下，便于后续接口、存储、序列化和管理端访问对齐。

## 实现状态（2026-07-17）

- 已将基础条目包装 `struct` 下沉到 Data 公共项目：`Iwesun.Runtime.Data/RuntimeRootEntryEnvelope`（`Id`、主键、辅助键、时间戳、`Payload`）。
- 主存储统一为 `Iwesun.Runtime.Data.RecordStoreV2<TValue,TPrimaryKey>`。
- 已新增表容器：`RuntimeRootTable`（基于 RecordStore 存储，使用 `StoreRecordId` 精确更新和废止，提供主键/辅助键索引访问）。
- 状态历史和文件路径登记使用 RecordStoreV2；RuntimeRoot 当前不启用 AutoMerge，仍由容器业务逻辑归一记录。
- 已新增根容器：`RuntimeRootContainer`（按表名统一管理）。
- 已新增可插拔辅助索引接口：`IRuntimeRootAuxIndex`。
- 已新增默认辅助索引：`RuntimeRootSortedPrimaryKeyIndex`（主键排序 + 二分前缀查询）。
- 已新增诊断目标：`runtime.root`（`RuntimeRootContainerTarget`），可通过 `snapshot/refresh/table/get/query/prefix/indexes` 访问根容器数据。
- 已补齐 T08/T09 的表化快照覆盖：`T08.ManagedTable.Commands`、`T08.ManagedTable.Events`、`T09.DiagnosticHubTable.Events`、`T09.DiagnosticHubTable.Registry.*`、`T09.DiagnosticHubTable.Meta`。
- `RuntimeManagedRegistry.Snapshot*` 与 `RuntimeDiagnosticHub.SnapshotEvents` 使用非破坏性快照（不再 drain 队列）以保证 root 刷新不影响运行时消费语义。
- 已新增显式 `Process` 视图表：`T06.ProcessTable` 与 `T08.ManagedTable.ProcessRegistrations`。
- 已新增显式反射采集表：`T09.DiagnosticHubTable.Reflection.Assemblies` 与 `T09.DiagnosticHubTable.Reflection.Meta`。
- 已新增反射类型分页表：`T09.DiagnosticHubTable.Reflection.Types.Catalog` 与 `T09.DiagnosticHubTable.Reflection.Types.Page.0001~N`（分页 + 总量上限，避免超大快照）。
- `RuntimeRootTable` 现强制主键一致性：当出现 `PrimaryKey` 冲突时执行单主键保留（新值覆盖旧值，旧条目从索引与容器移除）。
- `runtime.root` 查询动作默认采用短 TTL 缓存刷新（降低高频调用下的全量重建压力）；`refresh` 动作为强制刷新。
- `runtime.root` 已升级为“表分组增量刷新”：按表前缀 (`T01~T10`) 解析到刷新分组，并基于分组 TTL 进行按需刷新；`refresh` 仍执行全分组强制刷新。
- 输出/断点注入器事件已自动补齐跟踪元信息：`processId`、`managedThreadId`、`injectorCompileId`、`injectorRuntimeId`、`timestamp`。
- Data 层新增跨进程 FIFO 传输基础模型：`RuntimeInjectorFifoHeader` + `RuntimeInjectorTransportCodec`（固定头 + UTF8 字节负载，避免跨进程字符串引用问题）。
- `RuntimeManagedRegistry` 的每目标命令队列已收敛为小深度 FIFO（`depth=8`，超限丢弃最旧项并计入 `droppedCommandCount`），用于值类型命令状态流转。

## 1. 统一根对象

```text
RuntimeRoot
├─ T01 OutputMonitor
├─ T02 Registry
├─ T03 BreakpointTable
├─ T04 HookTable
├─ T05 RuntimeStateTable
├─ T06 ThreadTable
├─ T07 TaskTable
├─ T08 ManagedTable
├─ T09 DiagnosticHubTable
└─ T10 ProtocolTable
```

## 2. T01~T10 结构定义

### T01 OutputMonitor

- 开关字段
  - `GlobalEnabled: bool`
  - `PipeOutputEnabled: bool`
  - `FileOutputEnabled: bool`
  - `InputEnabled: bool`（派生，等价 `RuntimeOutputSwitch.Enabled`）
- 元数据
  - `FifoDepth: int`
  - `FifoCount: int`
  - `FifoDropped: long`
  - `RuntimeDiagnosticsPipeName: string`
  - `ConfigPath: string?`
- 子结构
  - `Sections: DiagnosticSectionSnapshot[]`
  - `Statements: DiagnosticStatementSnapshot[]`
  - `OutputPoints: DiagnosticOutputPointConfig[]`

`DiagnosticSectionSnapshot`:
- `Section, Enabled, Seen, Published, Suppressed`

`DiagnosticStatementSnapshot`:
- `Id, OutputPointId, Section, Kind, Sample, Seen, Published, Suppressed, LastSeenAt`

#### OutputMonitor 两条输出线（当前实现）

1. Pipe 线：`PipeOutputEnabled`
2. File 线：`FileOutputEnabled`

联动规则：

```text
InputEnabled = PipeOutputEnabled || FileOutputEnabled
```

发布规则：
- Pipe 发布：`if (PipeOutputEnabled) _hub?.Publish(...)`
- File 发布：`if (FileOutputEnabled) WriteFileLineIfEnabled(...)`

---

### T02 Registry

- `RegistrySnapshot`
  - `WatchPoints: WatchPointEntry[]`
  - `Breakpoints: BreakpointEntry[]`
  - `Hooks: HookEntry[]`

`WatchPointEntry`:
- `Id, Section, Kind, Description, SourceLocation, Enabled`

`BreakpointEntry`:
- `Id, Section, Description, SourceLocation, HitCountTarget, Enabled`

`HookEntry`:
- `HookId, EventName, TargetTypeName, IsAttached`

---

### T03 BreakpointTable

- `BreakpointState`
  - `Id, Section, Description, SourceLocation`
  - `Enabled, HitCountTarget`
  - `HitCount, IsWaiting, LastHitAt, LastContext`

- 快照结构：`BreakpointSnapshot`
  - `Id, Section, Description, SourceLocation, Enabled, HitCountTarget, HitCount, IsWaiting, LastHitAt`

---

### T04 HookTable

- 可挂接表：`HookableEvent`
  - `Id, TargetType, EventName`

- 已挂接表：`ActiveHook`
  - `HookId, EventName, TargetTypeName, IsStatic`
  - `WeakTarget, Handler`
  - `HitCount, AttachedAt, LastFiredAt`

---

### T05 RuntimeStateTable

- `RuntimeStateManager`（当前状态管理器）
- `RuntimeStateSnapshot`
  - `CurrentState, CurrentPath, UpdatedAt, States`
- 基础定义
  - `RuntimeState`
  - `RuntimeStateCatalog`
  - `RuntimeStateKey`

---

### T06 ThreadTable

- `RuntimeThreadRecord`
  - `Id, Name, Lifetime, Kind, Owner, SourceLocation`
  - `ManagedThreadId, NativeThreadId`
  - `State, StartedAt, LastHeartbeatAt, ExitedAt`
  - `CurrentTaskId, Tags, Payload`

---

### T07 TaskTable

- `RuntimeTaskRecord`
  - `Id, Name, Lifetime, Category, SourceLocation`
  - `ThreadId, ParentTaskId`
  - `State, Step, StartedAt, CompletedAt, LastHeartbeatAt`
  - `Error, Tags, Payload`

说明：当前 `process` 并非独立 `ProcessRecord`，而是以 `Task(category=process)` + `ManagedRegistration(unitType=process)` 组合体现。

---

### T08 ManagedTable

- `RuntimeManagedRegistration`
  - `UnitId, UnitType, Ownership, RegisteredAt, UpdatedAt, State`

- `RuntimeManagedCommand`
  - `Sequence, TargetUnitId, Kind, Payload, EnqueuedAt`

- `RuntimeManagedEvent`
  - `Sequence, UnitId, Kind, Message, Payload, Timestamp`

- `RuntimeManagedRegistry`
  - 注册集合
  - 每 Unit 命令 FIFO
  - 全局事件 FIFO

---

### T09 DiagnosticHubTable

- `RuntimeDiagnosticHub`
  - `Targets`（`targetId -> IRuntimeDiagnosticTarget`）
  - `Events`（`RuntimeDiagnosticEvent` FIFO）
  - `HostSnapshot`
  - `RegistrySnapshot`

- `RuntimeDiagnosticEvent`
  - `Timestamp, TargetId, Kind, Message, Payload`

---

### T10 ProtocolTable

- `RuntimeDiagnosticAction`
- `RuntimeDiagnosticActionResult`
- `RuntimeDiagnosticFrame`
  - `Header, Command, Status, ExtStatus, Data, Meta`
- `RuntimeDiagnosticFrameHeader`
- `RuntimeDiagnosticFrameCommand`
- `RuntimeDiagnosticFrameStatus`
- `RuntimeDiagnosticFrameMeta`

## 3. 主键与索引键建议（逻辑层）

- T01
  - `Sections: section`
  - `Statements: id`
  - `OutputPoints: id`
- T02
  - `WatchPoints: id`
  - `Breakpoints: id`
  - `Hooks: hookId`
- T03
  - `id`
- T04
  - `AvailableHooks: id`
  - `ActiveHooks: hookId`
- T05
  - `StateCatalog: code`（唯一辅键：`name`）
- T06
  - `id`（可辅键 `managedThreadId`）
- T07
  - `id`
- T08
  - `Registrations: unitId`
  - `CommandFifos: targetUnitId`
  - `ManagedEvents: sequence`
- T09
  - `Targets: targetId`
  - `Events: eventSeq/timestamp`
- T10
  - `Frames: requestId`（辅键：`correlationId`）

## 4. 跨表关联矩阵（逻辑关联）

- `T06.Thread.id` ↔ `T08.Registrations.unitId`（`unitType=thread`）
- `T07.Task.id` ↔ `T08.Registrations.unitId`（`unitType=task`）
- `Process.unitId` ↔ `T08.Registrations.unitId`（`unitType=process`）

- `T06.Thread.currentTaskId` -> `T07.Task.id`
- `T07.Task.threadId` -> `T06.Thread.id`
- `T07.Task.parentTaskId` -> `T07.Task.id`

- `T08.CommandFifos.targetUnitId` -> `T08.Registrations.unitId`
- `T08.ManagedEvent.unitId` -> `T08.Registrations.unitId`

- `T02.Breakpoints.id` ↔ `T03.BreakpointState.id`
- `T02.Hooks.hookId` ↔ `T04.AvailableHooks.id` / `T04.ActiveHooks.hookId`
- `T02.WatchPoints.id` ↔ `T01.OutputPoints.id`（推荐关联）

- `T01.Statements.outputPointId` -> `T01.OutputPoints.id`
- `T01.Statements.section` ↔ `T01.Sections.section`

- `T09.Events.targetId` -> `T09.Targets.targetId`
- `T10.requestId` ↔ 响应帧 `requestId`
- `T10.correlationId` 贯穿跨命令链路

## 5. 最终统一 ER 文本图

```text
RuntimeRoot
├─ T01 OutputMonitor
│  ├─ Sections(section PK)
│  ├─ Statements(id PK, outputPointId -> OutputPoints.id, section -> Sections.section)
│  └─ OutputPoints(id PK)
│
├─ T02 Registry
│  ├─ WatchPoints(id PK)
│  ├─ Breakpoints(id PK) --------------------------┐
│  └─ Hooks(hookId PK) -------------------------┐  │
│                                               │  │
├─ T03 BreakpointTable                           │  │
│  └─ BreakpointState(id PK) <-------------------┘  │
│                                                    │
├─ T04 HookTable                                     │
│  ├─ AvailableHooks(id PK) <------------------------┘
│  └─ ActiveHooks(hookId PK)
│
├─ T05 RuntimeStateTable
│  ├─ StateCatalog(code PK, name UK)
│  └─ CurrentState(singleton)
│
├─ T06 ThreadTable
│  └─ Thread(id PK, currentTaskId -> T07.Task.id)
│
├─ T07 TaskTable
│  └─ Task(id PK, threadId -> T06.Thread.id, parentTaskId -> T07.Task.id)
│
├─ T08 ManagedTable
│  ├─ Registrations(unitId PK, unitType)
│  ├─ CommandFifos(targetUnitId -> Registrations.unitId)
│  └─ ManagedEvents(sequence PK, unitId -> Registrations.unitId)
│
├─ T09 DiagnosticHubTable
│  ├─ Targets(targetId PK)
│  ├─ Events(eventSeq/timestamp PK, targetId -> Targets.targetId)
│  └─ RegistrySnapshot(引用 T02)
│
└─ T10 ProtocolTable
   └─ Frames(requestId PK, correlationId IDX, route=(targetId,action))
```

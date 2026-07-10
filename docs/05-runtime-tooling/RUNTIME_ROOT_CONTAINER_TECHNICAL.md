# RuntimeRoot 容器技术说明（DLIST + 辅助索引）

> **状态**: CURRENT | **最后更新**: 2026-07-10  
> **源码**: `Iwesun.Runtime.Data/RuntimeRootDataStructures.cs`, `Iwesun.Runtime.Diagnostics/RuntimeRootContainer.cs`, `Iwesun.Runtime.Diagnostics/RuntimeRootContainerTarget.cs`

## 1. 目标

在不改变 RuntimeRoot 逻辑模型（T01~T10）的前提下，先以 DLIST 作为主容器完成统一包装，再叠加可插拔辅助索引提升查询性能。

## 2. 基础条目结构

核心条目使用值类型包装（位于公共项目 `Iwesun.Runtime.Data`）：

- `RuntimeRootEntryEnvelope`（`readonly struct`）
  - `Id`：条目唯一标识
  - `PrimaryKey`：主键
  - `SecondaryKeys`：辅助键集合
  - `RegisteredAt` / `UpdatedAt`：登记时间戳
  - `Payload`：实际负载对象（`object?`）

设计意图：先统一条目元信息，再挂业务对象，降低不同表结构差异。

## 3. DLIST 主容器（公共 Data 项目）

- `RuntimeDListNode<T>`：双向节点
- `RuntimeDList<T>`：双向链表容器
  - `AddLast`
  - `TryAdd`
  - `AddRange`
  - `Remove`
  - `Clear`
  - `ToArraySnapshot`
  - `Sort(Comparison<T>)` / `Sort(IComparer<T>)`

`RuntimeRootTable`（在 Diagnostics 项目）以 DLIST 作为条目主存储结构，保证插入/遍历语义统一。

同时支持以下策略注入：

- `AllowDuplicates`：是否允许重复
- `DuplicatePredicate`：重复归一判定
- `FilterPredicate`：入集合过滤
- `MergeOnDuplicate`：重复时是否走合并
- `MergeDelegate`：重复合并逻辑

`RuntimeRootTable` 当前默认策略：
- 不允许重复（按 `Id` 归一）
- 重复时合并
- 合并后刷新主键和辅助键索引

并且 `RuntimeDList<T>` 允许通过继承覆盖算法（虚方法），在保留委托配置能力的同时支持子类定制策略。

## 4. 表级容器与根容器

### 4.1 RuntimeRootTable

职责：
- 管理单表条目
- 主键/辅助键查询
- 辅助索引重建

内置索引：
- `id -> node`
- `primaryKey -> node`
- `secondary(key,value) -> id set`

对外能力：
- `SetEntries`
- `Upsert`
- `TryGetById`
- `TryGetByPrimary`
- `QueryBySecondary`
- `QueryByPrimaryPrefix`（通过辅助索引）

### 4.2 RuntimeRootContainer

职责：
- 按 `tableName` 统一管理 `RuntimeRootTable`
- 提供跨表统一查询入口
- 输出根快照 `RuntimeRootSnapshot`

## 5. 辅助索引机制

### 5.1 可插拔接口

- `IRuntimeRootAuxIndex`
  - `Name`
  - `Rebuild(entries)`
  - `QueryPrefix(keyPrefix, take)`

### 5.2 当前默认实现

- `RuntimeRootSortedPrimaryKeyIndex`
  - 维护 `PrimaryKey` 有序快照
  - 使用二分下界定位 + 顺序扫描实现前缀查询
  - 由 `RuntimeRootTable` 默认注册（`sorted-primary`）

说明：DLIST 仍是主容器，排序索引是辅助结构，可后续替换为更高性能实现。

## 6. 诊断目标接入

新增 target：`runtime.root`（`RuntimeRootContainerTarget`）

动作：
- `snapshot`：刷新并返回根快照
- `refresh`：仅刷新
- `table`：返回单表快照
- `get`：按 `id` 查询
- `query`：按辅助键查询
- `prefix`：按主键前缀查询（走 `sorted-primary`）
- `indexes`：查看表可用辅助索引名

## 7. 演进建议

1. 保持 DLIST 为主存储结构不变。
2. 按热点查询补充专用辅助索引（例如状态分桶、时间窗口索引）。
3. 将索引重建策略从全量重建升级为增量更新（按数据规模再做）。
4. 对外协议保持兼容，仅扩展 `runtime.root` 查询动作。

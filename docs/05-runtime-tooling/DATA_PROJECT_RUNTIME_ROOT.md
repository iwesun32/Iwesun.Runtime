# Data 公共项目（RuntimeRoot 基础类）技术说明

> **状态**: CURRENT | **最后更新**: 2026-07-10  
> **源码**: `Iwesun.Runtime.Data/RuntimeRootDataStructures.cs`

## 1. 目的

将 RuntimeRoot 的基础数据类从业务项目分离，统一放入公共 Data 项目，避免容器和条目模型与当前业务实现强耦合。

## 2. 项目定位

- 项目：`Iwesun.Runtime.Data`
- 目标框架：`net10.0`
- 角色：公共数据结构层（不包含业务流程逻辑）

## 3. 当前下沉的基础类型

1. 条目与快照
- `RuntimeRootEntryEnvelope`
- `RuntimeRootTableSnapshot`
- `RuntimeRootSnapshot`

2. DLIST 容器
- `RuntimeDListNode<T>`
- `RuntimeDList<T>`

3. 辅助索引扩展点
- `IRuntimeRootAuxIndex`
- `RuntimeRootSortedPrimaryKeyIndex`

## 3.1 RuntimeDList 新增能力（本轮）

- 重复归一委托：`DuplicatePredicate: Func<T,T,bool>`
- 可重复开关：`AllowDuplicates`
- 过滤委托：`FilterPredicate: Func<T,bool>`
- 条目合并委托：`MergeDelegate: Func<T,T,T>`
- 合并开关：`MergeOnDuplicate`
- 批量新增：`AddRange(IEnumerable<T>)`
- 排序方法：
  - `Sort(Comparison<T>)`
  - `Sort(IComparer<T>)`
- 新增结果模型：
  - `RuntimeDListAddResult<T>`
  - `RuntimeDListBatchAddResult`
- 标准接口增强：
  - `RuntimeRootEntryEnvelope` 实现 `IEquatable<>`、`IComparable<>`
  - `RuntimeDList<T>` 实现 `IReadOnlyCollection<T>`

### 归一与合并语义

当 `AllowDuplicates = false` 且 `MergeOnDuplicate = true`，新条目命中重复判定后会走：

```text
MergedValue = MergeDelegate(OldValue, NewValue)
```

即执行“合并更新”，不是拒绝，也不是简单覆盖。

### 继承扩展点（子类可改算法）

`RuntimeDList<T>` 已提供可覆写虚方法，子类可替换算法而不改调用面：

- `ShouldAccept(T value)`：过滤策略
- `IsDuplicate(T left, T right)`：重复判定策略
- `TryMergeDuplicate(T existing, T incoming, out T merged)`：重复合并策略
- `FindFirstDuplicateNode(T value)`：重复查找策略
- `SortCore(IReadOnlyList<T> values, Comparison<T> comparison)`：排序策略
- `RebuildFromOrdered(IReadOnlyList<T> values)`：重建策略

## 4. 分层边界

- `Iwesun.Runtime.Data`
  - 仅负责数据结构定义、通用容器、通用索引接口
- `Iwesun.Runtime.Diagnostics`
  - 负责 RuntimeRoot 表装配、运行时数据刷新、诊断 target 暴露

该边界确保后续可在不改业务逻辑的前提下升级容器实现。

## 5. 依赖关系

```text
Iwesun.Runtime.Data          (基础数据层)
         ↑
Iwesun.Runtime.Diagnostics   (使用 Data 层封装运行时数据)
         ↑
SampleHost / Cli / FunctionalTests
```

## 6. 后续演进建议

1. 在 Data 层继续扩展更多可插拔索引实现（按热点查询类型）。
2. 视数据规模将索引重建策略升级为增量维护。
3. 保持 Data 层不引入业务依赖，维持可复用性。

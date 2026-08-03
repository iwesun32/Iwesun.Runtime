# RecordStore 对外 API 说明

> **状态**：ACTIVE — 与隔离实现 `RecordStore<TValue,TPrimaryKey>` 同步
> **最后更新**：2026-07-24
> **源码依据**：`RecordStore*.cs` 与 `RecordStore.Abstractions.cs`
> **适用范围**：只说明 `Iwesun.Runtime.Data.RecordStore<TValue,TPrimaryKey>`

## 1. 类型约束和公开字段

```csharp
public partial class RecordStore<TValue, TPrimaryKey>
    where TValue : IRecordStoreValue
    where TPrimaryKey : notnull
```

- `TValue` 必须实现 `IRecordStoreValue`，可以是值类型或引用类型；
- 引用类型必须在首次取得记录所有权前设置完整的 `IDeepCloneStrategy<TValue>`；
- `TPrimaryKey` 是业务主键最终值的类型，不是多个 Key 的列表；
- 当前实现没有公共可写字段，全部对外状态通过属性、事件和方法提供；
- TValue 必须公开内部记录身份属性：

```csharp
public interface IRecordStoreValue
{
    StoreRecordId StoreRecordId { get; set; }
}

public readonly record struct StoreRecordId(long Value);
```

Add 会忽略输入中的旧 ID 并分配新 ID。查询返回的 TValue 携带权威 ID，Update 采用先读后写。

派生业务表可以覆盖 `CreateDerivedOutputStore`，使 Publish、Snapshot、View、DeepClone 和 Compact
保持派生运行时类型，并复制隔离的表头业务状态。表头原子属性变化后通过
`MarkAdditionalStateChanged` 更新 Source 的 DataVersion。详细合同见
[派生业务表合同](../01-design/RECORD_STORE_DERIVED_TABLES.md)。

`Clear()` 只清 Source 记录并保留 Snapshot、发布版本和订阅状态。派生表覆盖时必须先
调用基类，再递归清自己的表头值和下级 Source。

## 2. 公共委托

| 委托 | 签名 | 用途 |
| --- | --- | --- |
| `ValueFormatter<TValue>` | `TValue (TValue value)` | Add 输入规范化；例如名称大小写、MAC/IP 格式 |
| `RecordFilter<TValue>` | `bool (TValue value)` | 完整记录合法性过滤；覆盖Add、Update、Merge结果及同`TValue`的Publish/View输出；`CreateView<TResult>`使用独立`ResultFilter<TResult>` |
| `MergeResolver<TValue>` | `TValue (TValue existing, TValue incoming)` | 由继承业务表调用，单纯执行 `C = Merge(A,B)` |
| `MergeAdd<TValue>` | `bool (TValue incoming, out StoreRecordId recordId)` | 实际冲突发生后，由继承业务表完成候选查询、完整性判断和合并编排 |
| `Aggregate<TValue>` | `TValue (IReadOnlyList<TValue> values)` | Publish/View 阶段把多条记录聚合为一条 |
| `LimitPredicate<TValue>` | `bool (TValue value, IReadOnlyList<TValue> current)` | 表级关系约束；不是单纯数量上限 |
| `RecordKeySelector<TValue,TKey>` | `TKey (TValue value)` | 普通 Key 的结构选择器 |
| `PrimaryKeySelector<TValue,TPrimaryKey>` | `TPrimaryKey (TValue value)` | 主键结构选择器 |
| `UniqueConstraintKeySelector<TValue,TConstraintKey>` | `TConstraintKey (TValue value)` | 唯一约束选择器 |
| `PublishSourceFilter<TValue>` | `bool (TValue value)` | Publish 输入过滤 |
| `PublishResultFilter<TValue>` | `bool (TValue value)` | Publish 输出过滤 |
| `PublishResultComparison<TValue>` | `int (TValue left,TValue right)` | Publish 输出排序 |
| `ViewSourceFilter<TValue>` | `bool (TValue value)` | View 输入过滤 |
| `ViewGroupComparison<TValue>` | `int (TValue left,TValue right)` | View 分组与排序 |
| `ViewResultFilter<TValue>` | `bool (TValue value)` | View 输出过滤 |
| `ViewResultComparison<TValue>` | `int (TValue left,TValue right)` | View 输出排序 |
| `SnapshotPublishedEventHandler<TValue,TPrimaryKey>` | `void (object? sender, SnapshotPublishedEventArgs<...> args)` | Snapshot 发布通知事件 |

所有公开回调位置都使用正式命名的 delegate，不以 `Func<>`、`Predicate<>`、`Comparison<>` 或裸
`Action<>` 表达业务合同。委托为 null 时，相应功能不执行。业务委托抛出的异常不会被静默吞掉。

处理委托允许使用 `+=` 串联，但必须通过 `RecordStoreDelegateChains` 执行明确语义：Formatter 和
MergeResolver 把前一步结果交给下一步；Filter 与 Limit 全部通过才通过；MergeAdd 按登记顺序在首个
成功项停止；Comparison 使用首个非零结果；Aggregate 把前一次聚合结果作为下一次的单元素输入。
Key、主键、唯一约束、Projector 和异型 ViewAggregate 是结构映射，只允许单一委托，不允许多播。

## 3. Key、主键和唯一约束定义

### 3.1 普通 Key

```csharp
public RecordKeyDefinition(
    string name,
    RecordKeySelector<TValue,TKey> selector,
    IEqualityComparer<TKey>? equalityComparer = null,
    IComparer<TKey>? comparer = null);
```

| 成员 | 类型 | 说明 |
| --- | --- | --- |
| `Name` | `string` | Key 的登记名称 |
| `KeyType` | `Type` | Key 的运行时类型 |
| `GetTypedKey(value)` | `TKey` | 从记录提取强类型 Key |
| `KeysEqual(left,right)` | `bool` | 使用本 Key 的相等比较器 |
| `CompareKeys(left,right)` | `int` | 使用本 Key 的排序比较器 |

`selector`、`equalityComparer` 和 `comparer` 都属于这个 Key，自构造完成后不可修改。多个 Key 各自拥有独立规则。
未显式传入比较器时，普通 Key 自动采用 `EqualityComparer<TKey>.Default` 和
`Comparer<TKey>.Default`，因此 Key 数值类型自身实现的相等与比较接口会直接生效。

### 3.2 业务主键

```csharp
public PrimaryKeyDefinition(
    IReadOnlyList<string> componentKeyNames,
    PrimaryKeySelector<TValue,TPrimaryKey> selector,
    IEqualityComparer<TPrimaryKey>? equalityComparer = null,
    IComparer<TPrimaryKey>? comparer = null);
```

| 成员 | 说明 |
| --- | --- |
| `ComponentKeyNames` | 主键引用的已登记 Key 名称，顺序固定 |
| `EqualityComparer` | 主键索引、相等和冲突判断规则 |
| `Comparer` | 显式提供时覆盖缺省的主键输出排序规则 |
| `GetPrimaryKey(value)` | 从一条记录生成组合后的业务主键值 |

主键索引的相等与冲突判断始终使用 `TPrimaryKey` 的相等比较器。主键稳定排序则分为两种：

- 未显式提供主键 `comparer`：基础类按 `ComponentKeyNames` 的固定顺序，逐项调用每个已登记
  普通 Key 的 `CompareKeys`；复合主键类型不必重复实现一份比较器。
- 显式提供主键 `comparer`：使用该比较器整体比较 `TPrimaryKey`，覆盖组件 Key 的自动组合排序。

因此复合主键 selector、组件名称顺序及各普通 Key 的比较语义必须保持一致。

### 3.3 唯一约束

```csharp
public UniqueConstraintDefinition(
    string name,
    IReadOnlyList<string> componentKeyNames,
    UniqueConstraintKeySelector<TValue,TConstraintKey> selector,
    IEqualityComparer<TConstraintKey>? equalityComparer = null);
```

唯一约束可以运行时增删替换，但必须通过 Store 的受控方法；不得直接修改 `UniqueConstraints` 返回值。

### 3.4 Definition

```csharp
public RecordStoreDefinition(
    IReadOnlyList<IRecordKeyDefinition<TValue>> keys,
    PrimaryKeyDefinition<TValue,TPrimaryKey> primaryKey);
```

公开成员：

| 成员 | 说明 |
| --- | --- |
| `Keys` | 已冻结的普通 Key 定义 |
| `PrimaryKey` | 已冻结的业务主键定义 |
| `TryGetKey(name,out key)` | 按名称读取 Key 定义 |

Definition 只冻结影响基础索引结构的 Key 和主键。需要更换它们时必须新建 Definition 和 Store。

## 4. Store 可修改属性

| 属性 | 类型/默认值 | 说明 |
| --- | --- | --- |
| `SchemaId` | `string` / 空 | JSON 持久化注册名称；序列化前必须设置 |
| `CloneStrategy` | `IDeepCloneStrategy<TValue>` / 值复制 | 输入、输出、发布和视图的所有权复制策略 |
| `Codec` | `ITableValueCodec<TValue>?` / null | 可选二进制值编码器；null 时 JSON 直接编码 TValue |
| `RuntimeProfileVersion` | `int` / 2 | Runtime Profile 正整数版本；JSON Restore 必须与 Registry 登记精确匹配 |
| `IndexThreshold` | `int` / 1024 | `Automatic` 模式下普通 Key 从紧凑排序表升级为哈希索引的阈值；必须大于 0 |
| `KeyIndexMode` | `RecordStoreKeyIndexMode` / `Automatic` | 普通 Key 索引策略：自动、强制紧凑排序表或强制哈希 |
| `AllowPrimaryKeyDuplicate` | `bool` / false | 是否允许 Working 表存在相同业务主键；切回 false 前验证现有数据 |
| `AutoMerge` | `bool` / false | 是否在 Add 时尝试自动合并 |
| `AllowUniqueConstraintViolation` | `bool` / false | 是否允许 Working 表暂存唯一约束冲突 |
| `ValueFormatter` | `ValueFormatter<TValue>?` / null | Add 输入规范化 |
| `RecordFilter` | `RecordFilter<TValue>?` / null | 公共记录过滤器 |
| `MergeResolver` | `MergeResolver<TValue>?` / null | 供继承业务表使用的二元内容合成器；基类 Add 不直接调用 |
| `MergeAdd` | `MergeAdd<TValue>?` / null | 实际主键或唯一性冲突发生后的业务合并入口 |
| `Limit` | `LimitPredicate<TValue>?` / null | 表级关系约束 |
| `DefaultPublishFormat` | `RecordStorePublishFormat<TValue>` | 无参数 Publish 的缺省格式 |

Add 对标准化后的 B 分别检查实际主键冲突和实际唯一性冲突，不受两个 Allow 开关影响。只要至少一种
实际冲突存在、`AutoMerge=true` 且 `MergeAdd` 非 null，基类就调用一次 `MergeAdd(B)`。基类不选择 A，
也不直接调用 `MergeResolver`。`MergeAdd` 返回 false 后，基类重新检查冲突，再由
`AllowPrimaryKeyDuplicate`、`AllowUniqueConstraintViolation`、`RecordFilter` 和 `Limit` 决定是否
普通登记。没有实际冲突时，即使 `AutoMerge=true` 也不调用 `MergeAdd`。

继承业务表可在 `MergeAdd(B)` 中查询 A，调用 `MergeResolver(A,B)` 产生 C，并使用：

```csharp
protected bool TryCommitMergedRecord(
    StoreRecordId existingId,
    in TValue mergedValue,
    out StoreRecordId recordId);
```

该方法只承担机械完整性：验证 C、保留 A 的 `StoreRecordId`、原子替换记录、同步全部索引并递增
`DataVersion`。选择 A、判断业务完整性以及失败前是否修改其他记录，全部由继承类型负责。
`MergeAdd` 不得递归调用同一输入的 Add/TryAdd。运行时修改属性不会改变 Key/主键基础索引定义。

普通 Key 的两套查询方案并存，并以每个 Key 为单位独立激活。`SortedTable` 为每个已查询 Key 维护
按 `Key + StoreRecordId` 排序的连续值类型数组；`Hash` 维护稳定桶索引。`Automatic` 在记录数低于
1024 时先采用排序表，达到阈值后的首次查询建立或升级为哈希索引。修改 `KeyIndexMode` 会清除普通
Key 的活动查询索引并在下一次查询按新模式重建；业务主键和唯一约束索引始终存在，不受该模式控制。

两种普通 Key 索引都维护唯一 Key 辅助信息。排序表在统一 Add/Update/Deprecate 提交中同步增减
`Key + 引用数`摘要，因此 `GetKeyCount` 为 O(1)，`GetKeys` 只复制唯一 Key；哈希索引直接读取活动桶数，
并缓存排序后的 Key 数组，仅在首次出现或最后移除某个 Key 时失效。

排序表要求同一个Key的相等比较器与排序比较器满足`Compare(left,right)==0`当且仅当
`Equals(left,right)==true`。检查覆盖初次建表、Add、Update和查询Key，包括相等项在排序表中不相邻的
情况。`Automatic`发现不一致会取消尚未提交的排序变更、重建Hash并重试；强制`SortedTable`会抛出
`InvalidOperationException`派生异常，并保持Source、版本、ID、摘要和Prepared状态不变。

同一`StringComparer`实例以及使用缺省比较器的常见基础值类型属于已证明一致的快速路径；其他或自定义
比较器使用稳定的相等目录执行全域检查。自定义`IEqualityComparer<TKey>`仍必须遵守.NET标准契约：
`Equals`为true的值必须返回相同Hash Code。客户比较器自身抛出的其他异常不被吞掉，Store保持原子不变
并把异常交还调用方。

`ApplyOptions(RecordStoreOptions options)` 可批量设置：

```csharp
public sealed record RecordStoreOptions(
    bool AllowPrimaryKeyDuplicate = false,
    bool AutoMerge = false,
    bool AllowUniqueConstraintViolation = false);
```

三个目标选项先整体校验，全部合法后才一次提交；任一目标与现有数据冲突时，三个当前值均保持不变。

## 5. Store 只读状态属性

| 属性 | 说明 |
| --- | --- |
| `Definition` | 本表使用的固定 Key/主键定义 |
| `UniqueConstraints` | 当前唯一约束只读视图 |
| `TableId` | 本表实例的全局唯一标识 |
| `Origin` | Source、Published、Snapshot、View、Clone 或 Restored |
| `IsReadOnly` | 当前实例是否禁止业务写入 |
| `Count` | 当前记录数量 |
| `PhysicalCount` | 分段存储中的物理记录数量，包含墓碑 |
| `DeprecatedCount` | `PhysicalCount - Count` |
| `TombstoneRatio` | 墓碑占物理记录的比例；空表为 0 |
| `DataVersion` | 数据修改版本 |
| `PublicationVersion` | Source 的发布次数 |
| `LastPublishedDataVersion` | 最近一次成功发布对应的数据版本 |
| `PublishedAt` | 本输出的发布时间 |
| `HasUnpublishedChanges` | Source 是否存在尚未发布的修改 |
| `IsPublishing` | 是否正在执行 Publish |
| `AccessGate` | 可选的外部同步/异步协作门；默认操作不自动加锁 |
| `Snapshot` | Source 当前安装的只读 Snapshot，没有则为 null |
| `SnapshotObserverFailureCount` | SnapshotPublished 观察者累计异常次数 |
| `PendingSnapshotSubscriberCount` | 当前版本尚未领取的预期订阅者数量 |
| `AreAllExpectedSnapshotSubscribersTaken` | 当前版本预期订阅者是否已经全部领取；没有预期订阅者时为 true |

## 6. 写入和约束方法

| 方法 | 返回 | 说明 |
| --- | --- | --- |
| `Add(in TValue value)` | `StoreRecordId` | 添加或合并；拒绝时抛 `InvalidOperationException` |
| `TryAdd(in TValue value,out id)` | `bool` | 添加、合并成功返回 true；规则拒绝返回 false |
| `AddRange(IEnumerable<TValue>)` | `int` | 逐条执行，返回成功接纳或合并数量；非事务 |
| `AddRange(ReadOnlySpan<TValue>)` | `int` | Span 批量入口，语义相同 |
| `TryUpdate(in TValue value)` | `RecordUpdateResult` | 按 TValue 中的 ID 先读后写；不允许修改业务主键 |
| `TryDeprecate(StoreRecordId id)` | `bool` | 按 ID 删除记录；成功后 ID 不复用 |
| `CreateCompactedStore(token)` | 新的可写 Store | 仅复制活动记录并保持顺序、ID、Profile 和版本；不复制 Snapshot/订阅/事件 |
| `SetUniqueConstraints(constraints)` | `void` | 整体替换约束，先构建临时索引再切换 |
| `AddUniqueConstraint(constraint)` | `void` | 增加约束并建立索引 |
| `RemoveUniqueConstraint(name)` | `bool` | 删除指定约束及其辅助索引 |
| `ReplaceUniqueConstraint(constraint)` | `void` | 按同名约束原子替换 |
| `ValidateConstraints(token)` | 冲突列表 | 报告 `$primary`、唯一约束名和 `$limit` 冲突，不修改表 |

`RecordUpdateResult`：`Updated`、`NotFound`、`Rejected`、`PrimaryKeyChanged`、`Busy`。Publish 冻结期间
`TryUpdate` 返回 Busy，`TryAdd` 和 `TryDeprecate` 返回 false；非 Try 修改入口抛 `InvalidOperationException`。
废止会把物理 Slot 的 TValue 清为 default 以释放托管引用，但 Segment 只由显式紧凑回收。Source 不会启动
后台压缩；业务程序应在作业边界根据 `DeprecatedCount`、`TombstoneRatio` 和 `PhysicalCount` 决定是否
用 `CreateCompactedStore` 返回的新表替换旧表。

Add、Update、Merge 和废止采用准备/提交两阶段。选择器、比较器、委托和容量预留全部发生在准备阶段；
提交阶段不调用用户代码。内部索引节点保存缓存Hash和带代次句柄，Key迁移或废止产生的空桶按句柄即时
删除并复用槽位，不会按历史Key数量累积。受控异常不会改变公开数据、索引顺序、Count、DataVersion
或下一RecordId。

## 7. 查询方法

| 方法 | 说明 |
| --- | --- |
| `TryGetRecord(id,out value)` | 按内部 ID 读取完整 TValue |
| `GetKeys(keyDefinition)` | 返回指定普通 Key 的去重、稳定排序数组 |
| `GetKeyCount(keyDefinition)` | 返回指定普通 Key 的去重数量 |
| `GetValues(keyDefinition,keyValue)` | 返回指定普通 Key 值匹配的 TValue 数组 |
| `GetValueCount(keyDefinition,keyValue)` | 返回指定普通 Key 值的记录数量 |
| `GetPrimaryKeys()` | 返回业务主键去重、稳定排序数组 |
| `GetPrimaryKeyCount()` | 返回业务主键去重数量 |
| `GetValues(primaryKey)` | 返回指定业务主键匹配的 TValue 数组 |
| `GetValueCount(primaryKey)` | 返回指定业务主键的记录数量 |
| `GetEnumerator()` | 按稳定登记顺序枚举 TValue 的复制值 |

传给普通 Key 查询的 `RecordKeyDefinition` 必须是本 Store Definition 中登记的同一个对象，不能只使用同名的新对象。

## 8. Publish、Snapshot、View 和 Clone

### 8.1 发布格式

```csharp
public sealed record RecordStorePublishFormat<TValue>(
    PublishSourceFilter<TValue>? SourceFilter = null,
    PublishResultFilter<TValue>? ResultFilter = null,
    PublishResultComparison<TValue>? ResultComparison = null,
    Aggregate<TValue>? ConflictAggregator = null);
```

- `SourceFilter`：冲突归并前过滤；
- `ConflictAggregator`：把一个冲突闭包聚合为一条；聚合结果产生新冲突时继续迭代，直到稳定；
- `ResultFilter`：聚合后过滤；
- `ResultComparison`：最终输出排序。

### 8.2 Publish 重载

```csharp
Publish(CancellationToken token = default)
Publish(RecordStorePublishFormat<TValue> format, CancellationToken token = default)
Publish(IReadOnlyList<TValue> values, Aggregate<TValue> aggregate, CancellationToken token = default)
Publish(RecordStorePublishTarget target, RecordStorePublishFormat<TValue>? format = null, CancellationToken token = default)
Publish(RecordStorePublishTarget target, IReadOnlyList<TValue> values, Aggregate<TValue> aggregate, CancellationToken token = default)
```

`RecordStorePublishTarget` 为 `Standalone` 或 `Snapshot`。Publish 产生新表，不大规模改写 Source。Snapshot 输出只读并安装到 Source 的 `Snapshot` 属性。
Publish 使用原子作业冻结：执行期间数据、约束和运行策略不能修改；失败不替换旧 Snapshot。成功提交后先解除
冻结，再分发 `SnapshotPublished`。

### 8.3 Snapshot 订阅与事件

```csharp
public event SnapshotPublishedEventHandler<TValue,TPrimaryKey>? SnapshotPublished;
```

事件参数：`SourceTableId`、`PublicationVersion`、`SourceDataVersion`、`PublishedAt`、`Snapshot`。
观察者异常不会破坏已提交发布，异常次数进入 `SnapshotObserverFailureCount`。

| 方法 | 说明 |
| --- | --- |
| `RegisterSnapshotSubscriber(id)` | 登记订阅者 |
| `UnregisterSnapshotSubscriber(id)` | 注销订阅者 |
| `TryTakeSnapshot(id,out snapshot)` | 每个订阅者对当前版本首次领取成功 |
| `IsSnapshotTaken(id)` | 是否已领取当前版本 |
| `GetSnapshotTakeState(id)` | NoSnapshot、NotSubscribed、Pending、Taken |
| `TryGetSnapshotTakenAt(id,out time)` | 读取首次领取时间 |

注销会同时把订阅者从当前版本预期集合移除；已经产生的 Taken 时间保留到下一次成功发布清理。

### 8.4 View 与 Clone

```csharp
public sealed record RecordStoreViewDefinition<TValue>(
    ViewSourceFilter<TValue>? SourceFilter = null,
    ViewGroupComparison<TValue>? GroupComparison = null,
    Aggregate<TValue>? GroupAggregate = null,
    ViewResultFilter<TValue>? ResultFilter = null,
    ViewResultComparison<TValue>? ResultComparison = null);
```

`GroupComparison` 和 `GroupAggregate` 必须同时提供。公开方法：

- `CreateView(definition,token)`：同步物化 View；
- `CreateViewAsync(definition,token)`：捕获源值后在线程池物化；
- `DeepClone()`：生成 `Origin=Clone` 的独立、可写本地副本，保留记录 ID、顺序、版本和当前实例配置。
  Clone 不是 Source，不能用它管理或发布 Snapshot；阶段衔接应直接使用 Source 的 Snapshot/订阅接口，
  只有确实需要脱离发布链修改原始数据时才使用 Clone。
- `CreateDetachedSourceCopy()`：为进程间载荷或完整根视图生成独立、可写的 Source 副本，同时保留
  当前 Source 与已安装 Snapshot 的差异；订阅登记和领取状态不复制，仍只属于原 Source。

异型数组 View 使用两个互斥模式：

```csharp
new RecordStoreResultViewDefinition<TValue,TResult>(
    SourceFilter: ...,
    Projector: ...); // 逐条 TValue -> TResult

new RecordStoreResultViewDefinition<TValue,TResult>(
    GroupComparison: ...,
    GroupAggregate: ...); // IReadOnlyList<TValue> -> TResult
```

公开方法为 `CreateView<TResult>(definition,token)` 和 `CreateViewAsync<TResult>(definition,token)`。
Projector 与分组委托不能混用；空输入返回空数组；Aggregate 不接收空组。TResult 包含引用时必须提供
`IDeepCloneStrategy<TResult>`。ResultFilter 和 ResultComparison 接收复制值，不能通过可变引用改写
View 内部结果；排序周期检查 CancellationToken，取消以 `OperationCanceledException` 返回。

## 9. JSON、Codec 和 Schema Registry

```csharp
string json = store.ToJson(options);
var restored = RecordStore<TValue,TPrimaryKey>.RestoreJson(json, options);
```

序列化前必须设置非空 `SchemaId`。恢复前必须注册：

```csharp
RecordStoreSchemaRegistry<TValue,TPrimaryKey>.Register(store);
```

或者显式注册 SchemaId、Definition 和 `RecordStoreRuntimeProfile<TValue,TPrimaryKey>`。Profile 包含版本、
复制、Codec、索引阈值、唯一约束、重复开关、过滤、限制、Merge 和发布格式。同一 SchemaId 的 Key/主键
Definition 必须保持同一个结构实例；运行时 Profile 不被冻结，可以在 Source 上修改，并在再次
`Register(store)` 或 `ToJson()` 时刷新 Registry 当前配置。持久化格式版本为 3，JSON 同时保存 SchemaId
与 ProfileVersion；恢复时必须与 Registry 当时登记的当前 Profile 精确匹配。

`Register(store)` 会从当前表创建一个不含记录的恢复原型。派生表覆盖
`CreateDerivedOutputStore` 后，恢复原型和后续 `RestoreJson` 结果会保持派生运行时类型，而 Registry
不会持有包含业务记录的 Source。需要完全自定义构造时，也可以使用带
`Func<RecordStore<TValue,TPrimaryKey>> restoreFactory` 的登记重载；工厂必须返回使用同一 Definition
的空、可写 Source。JSON 只保存 RecordStore 记录和运行配置标识；派生表头若需持久化，仍由业务层
定义独立格式或在恢复工厂中提供登记时的初始状态。

## 10. 可选访问 Gate

当前实现默认保持单写者快速路径，不自动加锁。需要跨异步流协作时：

```csharp
using var lease = store.AccessGate.Enter(cancellationToken);

await using var asyncLease = await store.AccessGate.EnterAsync(cancellationToken);
```

`RecordStoreAccessGate.IsEntered` 只用于观察，不能代替持有 Lease。`RecordStoreAccessLease` 同时实现 `IDisposable` 和 `IAsyncDisposable`。

## 11. 最小构造示例

```csharp
var machineKey = new RecordKeyDefinition<DeviceRow,string?>(
    "machine", static row => row.Machine, StringComparer.OrdinalIgnoreCase, StringComparer.OrdinalIgnoreCase);
var macKey = new RecordKeyDefinition<DeviceRow,string?>(
    "mac", static row => row.Mac, StringComparer.OrdinalIgnoreCase, StringComparer.OrdinalIgnoreCase);
var ipKey = new RecordKeyDefinition<DeviceRow,string?>(
    "ip", static row => row.Ip, StringComparer.OrdinalIgnoreCase, StringComparer.OrdinalIgnoreCase);

var primaryKey = new PrimaryKeyDefinition<DeviceRow,DevicePrimaryKey>(
    ["machine", "mac", "ip"],
    static row => new(row.Machine, row.Mac, row.Ip));

var definition = new RecordStoreDefinition<DeviceRow,DevicePrimaryKey>(
    [machineKey, macKey, ipKey], primaryKey);

var store = new RecordStore<DeviceRow,DevicePrimaryKey>(definition)
{
    SchemaId = "device.v2",
    RecordFilter = static row => row.Machine is not null
};
```

构造函数只确定 Key 和业务主键；所有其他业务策略在 Store 实例上设置。

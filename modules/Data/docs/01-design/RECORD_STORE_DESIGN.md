# RecordStore 统一设计

> **状态**：ACTIVE — `RecordStore<TValue,TPrimaryKey>` 隔离实现已落地，尚未晋升替换 1.0.25
> **最后更新**：2026-07-24
> **定位**：RecordStore 唯一设计权威
> **当前代码**：活动源码只保留无版本后缀的正式实现；设计以本文和 Runtime Data 源码为准

## 1. 定位

RecordStore 是采用关系表语义的轻量内存数据类型，不是数据库。它提供多个业务 Key、内部与业务
双层主键、唯一约束、顺序登记、运行时索引查询、局部 Merge、Publish 聚合重载、受管
Snapshot、物化 View、Clone 和序列化。

它不提供事务、回滚日志、Insert、物理位置编辑、查询语言、联表、动态 Schema、分页、游标、查询
计划、多线程安全或数据库服务器。

核心原则：

1. 构造函数只决定 Key 与主键结构，其他运行策略和业务委托允许运行时改变；
2. Working Source 单写者，条目操作只做局部变化；
3. 整体操作不改写来源表，只构造新表；
4. 全部公开查询可在运行时直接调用并使用索引优化；
5. 失败操作不留下半条记录、半个索引或半张输出表。

## 2. 类型与构造

### 2.1 主类型

```csharp
public class RecordStore<TValue, TPrimaryKey>
    where TValue : IRecordStoreValue
    where TPrimaryKey : notnull
{
    public RecordStore(
        RecordStoreDefinition<TValue, TPrimaryKey> definition);
}
```

- `TValue` 是带系统记录 ID 的完整记录；
- `TPrimaryKey` 是强类型业务/事件主键；
- `StoreRecordId` 是系统生成的内部主键；
- 不把每个普通 Key 变成 RecordStore 泛型参数。

```csharp
public interface IRecordStoreValue
{
    StoreRecordId StoreRecordId { get; set; }
}
```

查询仍返回 TValue，不增加记录信封。Add 忽略输入值中已有的 `StoreRecordId`，分配系统 ID 后写入表内
值；所有查询返回的 TValue 都携带权威 ID。用户保存查询结果即可完成先读后写，并可把其中 ID 交给
Delete。外部修改 ID 不会改变表内记录；Update 必须验证写回 ID 存在且不能改变。

### 2.2 不可变索引定义

`RecordStore<TValue,TPrimaryKey>` 的第二个泛型参数只是“业务主键最终值的类型”，不是把多个 Key
逐个放进泛型。普通 Key 先分别定义，再由 `PrimaryKeyDefinition` 使用 Key 名称组合：

```csharp
public readonly record struct DevicePrimaryKey(string? Machine, string? Mac, string? Ip);

var machineKey = new RecordKeyDefinition<DeviceRow, string?>(
    "machine", static row => row.Machine);
var macKey = new RecordKeyDefinition<DeviceRow, string?>(
    "mac", static row => row.Mac);
var ipKey = new RecordKeyDefinition<DeviceRow, string?>(
    "ip", static row => row.Ip);

var primaryKey = new PrimaryKeyDefinition<DeviceRow, DevicePrimaryKey>(
    ["machine", "mac", "ip"],
    static row => new DevicePrimaryKey(row.Machine, row.Mac, row.Ip));

var definition = new RecordStoreDefinition<DeviceRow, DevicePrimaryKey>(
    [machineKey, macKey, ipKey],
    primaryKey);

var store = new RecordStore<DeviceRow, DevicePrimaryKey>(definition);
```

其中 `["machine", "mac", "ip"]` 不是传入三个 Key 值，而是声明主键由哪三个已登记 Key 组成；
后面的 selector 才负责从一条 `DeviceRow` 生成一个 `DevicePrimaryKey`。这两处必须保持相同顺序和语义。

普通 Key 未显式配置比较器时，自动采用其 `TKey` 数值类型的缺省相等与排序比较器。复合主键未显式
配置整体比较器时，RecordStore 按 `componentKeyNames` 顺序逐项调用这些普通 Key 的比较器，
不要求 `DevicePrimaryKey` 再重复实现 `IComparable<DevicePrimaryKey>`。只有业务显式提供主键整体
比较器时，才覆盖这一组件组合排序。主键索引相等性仍由 `TPrimaryKey` 的相等比较器负责。

```csharp
public sealed class RecordStoreDefinition<TValue, TPrimaryKey>
    where TValue : struct, IRecordStoreValue
    where TPrimaryKey : notnull
{
    public RecordStoreDefinition(
        IReadOnlyList<IRecordKeyDefinition<TValue>> keys,
        PrimaryKeyDefinition<TValue, TPrimaryKey> primaryKey);
}
```

Definition 只冻结 Key 与业务主键，因为两者决定主索引和辅助 Key 索引的结构与维护成本。构造时
防御性复制 Key 数组；构造后不存在 `AddKey` 或 `SetPrimaryKey`。需要另一种索引结构时创建新的
Definition 和 Store，不能在运行中的表上重建基础索引。

SchemaId、CloneStrategy、Codec、IndexThreshold、唯一约束、格式化、过滤、限制、合并、发布格式和
行为开关都属于 `RecordStore` 实例配置，可以在运行时修改。唯一约束可能带有辅助索引，因此只能
通过 `SetUniqueConstraints`、`AddUniqueConstraint`、`RemoveUniqueConstraint`、
`ReplaceUniqueConstraint` 修改；实现先在临时结构完成校验和索引构建，再原子替换。

`ValueFormatter` 是写入格式化委托，不是持久化功能：

```csharp
public delegate TValue ValueFormatter<TValue>(TValue value)
```

它把每个外部输入规范化，例如统一名称大小写、规范 IP 或补齐缺省字段。它只在 Add/AddRange 的
AddCore 输入阶段执行；Clone、反序列化和内部结果表构造不重复执行。RecordStore 先复制外部输入，再把
工作副本交给 Formatter，并复制格式化结果后才进入 RecordFilter、主键、唯一约束、Merge 和 Limit，避免
委托通过引用成员反向修改调用方对象。委托为 null 时只执行正常的一次输入复制，不产生额外调用。

RecordStore 的全部公开回调和事件都有正式命名 delegate；不使用 `Func<>`、`Predicate<>`、
`Comparison<>` 或裸 `Action<>` 隐含业务语义。处理委托允许串联，并由统一链执行器解释：顺序转换、
全通过过滤、首个成功 MergeAdd、首个非零排序和逐级聚合。结构选择器与异型投影只允许单一委托，
避免 CLR 多播委托“执行全部但只返回最后结果”的隐含行为。

### 2.3 可变实例选项

```csharp
public sealed record RecordStoreOptions(
    bool AllowPrimaryKeyDuplicate = false,
    bool AutoMerge = false,
    bool AllowUniqueConstraintViolation = false);
```

`RecordStoreOptions` 只是一次性应用多个实例开关的便利值，不是 Definition，也不会冻结配置。
各开关仍可单独修改；从“允许冲突”切换到“不允许冲突”时必须先验证已有数据，失败则保持原状态。

### 2.4 公开状态

```csharp
public enum RecordStoreOrigin : byte
{
    Source,
    Published,
    Snapshot,
    View,
    Clone,
    Restored
}

public Guid TableId { get; }
public RecordStoreOrigin Origin { get; }
public bool IsReadOnly { get; }
public long DataVersion { get; }
public long PublicationVersion { get; }
public long? LastPublishedDataVersion { get; }
public DateTimeOffset? PublishedAt { get; }
public bool HasUnpublishedChanges { get; }
public bool IsPublishing { get; }
```

- 每个新建、Publish、View、Clone 和反序列化结果拥有新的 `TableId`；
- 只有 Publish 的 Snapshot 目标结果为只读；Standalone Publish、View、Clone 和 Restored 结果可写；
- 每次成功 Add、Merge、Update 或 Delete 后 `DataVersion` 单调递增；
- `PublicationVersion`、`LastPublishedDataVersion` 和 `PublishedAt` 描述来源对象最近一次成功
  Snapshot 目标 Publish；
- `HasUnpublishedChanges` 只对可发布 Source 有业务意义；
- `IsPublishing` 只是观察状态，不是同步原语。

## 3. 内部主键 StoreRecordId

```csharp
public readonly record struct StoreRecordId(long Value);
```

这不是二进制 ID，也不是把旧的字符串订阅者 ID 改成整数。当前 RecordStore 的记录 ID 本来就是
`long` 的强类型包装；Snapshot 订阅者标识继续使用 `string`。两者属于不同身份域，不能互换。

- 每条成功登记的物理记录唯一；
- 系统生成、不复用、不可修改；
- Add 对外返回；
- 用于读取、完整值写回和按 ID 删除；
- 只在所属表身份域内有效；
- 对业务无含义，不参与最终业务去重和排序。

每张表维护 `StoreRecordId -> Slot` 索引，期望 O(1) 定位。

## 4. 普通 Key

### 4.1 定义

```csharp
public sealed class RecordKeyDefinition<TValue, TKey>
    : IRecordKeyDefinition<TValue>
    where TValue : struct
{
    public RecordKeyDefinition(
        string name,
        Func<TValue, TKey?> selector,
        IEqualityComparer<TKey?> equalityComparer,
        IComparer<TKey?> comparer);
}
```

Key 比较不是 Store 上的可变属性，而是在 `RecordKeyDefinition` 构造时确定的两套规则：

```csharp
var machineKey = new RecordKeyDefinition<DeviceRow, string?>(
    name: "machine",
    selector: static row => row.Machine,
    equalityComparer: StringComparer.OrdinalIgnoreCase,
    comparer: StringComparer.OrdinalIgnoreCase);
```

- `equalityComparer` 负责相等判断和哈希，供索引、查询和冲突候选使用；
- `comparer` 负责大小顺序，供 Key 排序使用；
- 省略时分别采用 `EqualityComparer<TKey>.Default` 和 `Comparer<TKey>.Default`；
- 两者与 selector 一起决定索引语义，Key 构造完成后不能修改。

构造参数可以包含任意数量、不同类型的 Key。要求：

- 名称唯一；
- 类型是值类型或 `string`；
- 有稳定的相等、哈希和排序规则；
- 可以重复；
- 可以为 null；
- 可以用于索引查询、排序、业务主键和唯一约束。

### 4.2 null

null 是普通单一值：

- `null == null`；
- null 与非 null 不相等；
- null 进入正常索引桶；
- null 排序位置由 Key 的比较器决定；
- RecordStore 不解释“未知、缺失、未采集”；
- 更严格业务规则由 Store 公共属性 `RecordFilter` 表达；它不是 Add 专用过滤器。

## 5. 业务/事件主键

业务主键由一个或多个已登记 Key 按构造时顺序组成，例如：

```text
MachineName + MacAddress + IpAddress
```

它定义业务事件身份，用于：

- 自动 Merge 候选定位；
- Publish 主键冲突整理；
- 最终业务身份；
- 标准主键排序；
- 运行时主键查询。

允许部分组成 Key 为 null；只禁止全部主键 Key 同时为 null。

缺省不允许 Working 主键重复。主键索引形态：

```text
不允许重复：TPrimaryKey -> StoreRecordId
允许重复：  TPrimaryKey -> ordered StoreRecordId bucket
```

主键重复与 AutoMerge：

| AllowPrimaryKeyDuplicate | AutoMerge | 相同主键输入 |
| ---: | ---: | --- |
| false | false | 拒绝 |
| false | true | 调用继承表的 MergeAdd，失败则拒绝 |
| true | false | 保留多条 |
| true | true | 优先调用 MergeAdd；返回失败后继续按允许重复规则保留多条 |

Add 使用主键索引和唯一性索引判断是否存在实际冲突。至少一种冲突存在、`AutoMerge=true` 且
`MergeAdd` 已配置时，基类只调用一次 `MergeAdd(B)`；不在基类选择 A，也不循环执行通用合并算法。
`MergeAdd` 返回失败后，Add 重新读取冲突状态，再继续正常接纳判定：主键冲突由
`AllowPrimaryKeyDuplicate` 决定，附加唯一约束冲突由 `AllowUniqueConstraintViolation` 决定，同时
仍须通过 `RecordFilter` 和 `Limit`。

## 6. 附加唯一约束

唯一约束是业务主键之外，由一个或多个已登记 Key 组成的附加规则。

- 不使用 `Global / WithinGroup` Scope；
- 所有约束都对自己的复合 Key 执行全表唯一；
- “组内唯一”通过把分组 Key 放进复合约束表达；
- null 沿用普通 Key 相等规则；
- 定义和顺序构造后不可变；
- 缺省强制；
- `AllowUniqueConstraintViolation=true` 时 Working 可暂存冲突；
- `ValidateConstraints()` 是一次检查，不是持久运行模式。

不保留旧 `Disabled / Validate / Enforce` 三态。

```csharp
public sealed record RecordConstraintConflict(
    string ConstraintName,
    IReadOnlyList<StoreRecordId> RecordIds);

public IReadOnlyList<RecordConstraintConflict> ValidateConstraints(
    CancellationToken cancellationToken = default);
```

验证同时扫描业务主键和全部附加唯一约束。即使 Working 允许暂存主键或唯一约束冲突，也会返回这些
冲突，供调用方在 Publish 前检查。结果按主键、约束声明顺序和记录登记顺序稳定输出；取消不修改表。

## 7. 排序

只支持三种明确排序：

1. 选择一个普通 Key 排序；
2. 按业务复合主键顺序排序；
3. 客户提供完整 TValue 比较器。

排序只作用于查询数组或新表，不重排 Source。相等项保持来源登记顺序。null 服从对应 Key 比较器。

## 8. 条目操作

### 8.1 支持范围

| 操作 | 语义 |
| --- | --- |
| Add | 末尾登记，返回 `StoreRecordId` |
| AddRange | 逐条调用同一个 AddCore，返回成功接纳数量 |
| Update | 先读完整值，再按同一 ID 写回完整值 |
| Deprecate | 用户按确切 ID 删除记录 |
| Insert | 不支持 |
| Restore | 不支持 |
| 用户直接 Merge | 不支持 |

### 8.2 Add

```csharp
public StoreRecordId Add(in TValue value);
public bool TryAdd(in TValue value, out StoreRecordId id);
```

统一 AddCore：

1. 深复制外部输入为工作副本；
2. ValueFormatter 格式化工作副本；
3. Formatter 存在时深复制格式化结果为最终候选；
4. RecordFilter 检查候选值自身；
5. 提取所有 Key 和业务主键；
6. 拒绝业务主键全部为 null；
7. 查业务主键索引；
8. 需要时执行内部 Merge；
9. 按主键重复开关处理；
10. 检查未放宽的唯一约束；
11. Limit 检查输入与已有记录关系；
12. 分配 `StoreRecordId`；
13. 把系统 ID 写入候选 TValue；
14. 一次提交记录、索引、Count 和 DataVersion。

Add、Update、Merge 和废止采用准备/提交两阶段。准备阶段完成全部选择器、Hash、Equals、业务委托、
冲突检查和容量预留；提交阶段只操作已预留的框架容器，不再调用用户代码。受控异常不能留下半条记录、
半套索引、顺序移动、版本增加或 RecordId 消耗。灾难性进程终止不属于进程内恢复保证。

索引节点在准备阶段取得缓存Hash、带代次`IndexEntryHandle`和Prepared Entry，但尚不进入活动哈希链。
提交阶段只按内部句柄链接或摘除节点，不重新执行用户Hash/Equals。准备失败会回收Prepared Entry和
BucketArena槽；成功迁移或废止使桶变空时立即按句柄删除映射并释放Arena槽。因此索引容量随当前活动
Key集合保持有界，不随历史Key数量线性增长。

### 8.3 批量 Add

```csharp
public int AddRange(IEnumerable<TValue> values);
public int AddRange(ReadOnlySpan<TValue> values);
```

- 非事务；
- 按输入顺序逐条处理；
- RecordFilter、Limit、主键或约束拒绝时跳过当前项并继续；
- Added 和 Merged 都计入成功接纳数量；
- 用户委托抛异常时保留此前成功项、停止后续处理并传播异常；
- 不返回逐项结果或 ID 数组。

### 8.4 Update

```csharp
public bool TryGetRecord(StoreRecordId id, out TValue value);
public RecordUpdateResult TryUpdate(in TValue value);
```

调用方必须先通过 ID、Key 或业务主键查询取得带 `StoreRecordId` 的完整 TValue，再写回该 TValue。
不提供 updater 委托、UpdateAt、UpdateWhere 或字段 Patch。

- 使用 TValue 内的 `StoreRecordId` 定位原记录；
- 业务主键不能改变；
- 其他普通 Key、唯一约束字段和普通字段可以改变；
- 写回前重跑 RecordFilter、Limit 和当前强制唯一约束；
- 更新受影响索引；
- 失败保持原值和版本不变；
- 简单模型不增加单记录版本参数。

### 8.5 用户删除

```csharp
public bool TryDeprecate(StoreRecordId id);
```

“废止”对用户就是删除。记录从查询、Count、索引、Clone、序列化和输出中消失。物理 Slot 立即把
TValue 清为 default 以释放托管引用，Segment 位置保留为不可见墓碑；没有恢复或删除原因 API，也不复用
ID。`PhysicalCount`、`DeprecatedCount` 和 `TombstoneRatio` 用于监视物理容量；
`CreateCompactedStore` 在业务作业边界产生只含活动记录的独立可写表，不进行后台自动压缩。

## 9. 继承业务表 Merge

```csharp
public delegate TValue MergeResolver<TValue>(TValue existing, TValue incoming);
public delegate bool MergeAdd<TValue>(TValue incoming, out StoreRecordId recordId);
```

- `MergeResolver(A,B)` 只产生 C，不查询表、不决定候选、不修改索引；
- `MergeAdd(B)` 由继承业务表实现，负责查询 A、选择 A、调用 Resolver、判断业务完整性和编排提交；
- RecordStore 只在 Add 已确认实际主键或唯一性冲突且 `AutoMerge=true` 时调用一次 `MergeAdd(B)`；
- 基类不再提供 `MergePredicate`、`GetMergeCandidates` 或通用候选循环；
- B 是经过 `ValueFormatter` 和 `RecordFilter` 的尚未登记输入，`StoreRecordId` 为空；
- A、B、C 的业务含义与完整性由继承类型负责。

继承类型使用 `TryCommitMergedRecord(existingId,C,out recordId)` 请求基类提交。该受保护方法验证 C 的
主键、RecordFilter、Limit 和唯一约束，保留 A 的 ID，并原子更新记录、全部索引和 DataVersion。
它只保证存储机械完整性，不判断 C 的业务完整性。`MergeAdd` 返回 false 后不得假设原冲突缓存仍然
有效；Add 会重新查询主键和唯一性索引，再执行两个 Allow 开关。

## 10. 八个运行时查询

查询可直接用于 Source、Standalone Publish 返回表、Snapshot、View 和 Clone。查询只读，不修改版本或状态。

### 10.1 普通 Key 四种

```csharp
public TKey?[] GetKeys<TKey>(
    RecordKeyDefinition<TValue, TKey> key);

public int GetKeyCount<TKey>(
    RecordKeyDefinition<TValue, TKey> key);

public TValue[] GetValues<TKey>(
    RecordKeyDefinition<TValue, TKey> key,
    TKey? keyValue);

public int GetValueCount<TKey>(
    RecordKeyDefinition<TValue, TKey> key,
    TKey? keyValue);
```

- `GetKeys` 去重，null 最多一次，按 Key 比较器排序；
- `GetKeyCount` 返回不同 Key 数量，不先创建数组；
- `GetValues` 返回指定 Key 的全部 TValue，保持来源登记顺序；
- `GetValueCount` 返回对应 TValue 总数并包含第一条，不先创建数组。

### 10.2 业务主键四种

```csharp
public TPrimaryKey[] GetPrimaryKeys();
public int GetPrimaryKeyCount();
public TValue[] GetValues(TPrimaryKey primaryKey);
public int GetValueCount(TPrimaryKey primaryKey);
```

- 主键数组去重并按主键比较器排序；
- 主键数量不先创建数组；
- 允许 Working 主键重复时，Value 数组返回全部命中记录；
- Value Count 与对应数组长度一致。

不提供无条件全表 `ToArray()`；全表访问使用顺序枚举。不提供 `GetDuplicateCount`、批量计数、Predicate
查询、分页或查询计划；复杂过滤进入 View。

### 10.3 查询优化

- 业务主键索引始终维护；
- 唯一约束索引始终维护；
- 原始记录表只按单调递增的`StoreRecordId`保持顺序，不按任意业务Key重排；
- 每个普通Key独立建立`Key + StoreRecordId`紧凑排序表，按Key、ID排序并使用二分查找；
- `Automatic`在记录数达到`IndexThreshold`时改用或升级为稳定哈希索引，缺省阈值为1024；
- 调用方可通过`KeyIndexMode`强制使用`SortedTable`或`Hash`，修改模式后普通Key索引按需重建；
- 排序表维护`Key + 引用数`唯一Key摘要，`GetKeyCount`直接读取摘要长度，`GetKeys`只复制摘要；
- 哈希索引以活动桶数回答`GetKeyCount`，并缓存排序Key数组，仅在Key集合变化时失效；
- 每个普通 Key 独立激活；建立后随 Add、Update、废止持续维护；
- 排序表要求排序零值与相等比较语义双向一致；自定义比较器通过稳定相等目录覆盖构建、Add、Update和
  查询Key的全域检查，`Automatic`冲突时原子回退Hash，强制`SortedTable`原子拒绝；
- 排序表唯一Key摘要从小容量开始并只随唯一Key数量增长，不按活动记录总数预留；
- Value 数组只复制命中桶；
- 哈希Key数组按Key集合变化失效；只修改同Key记录或非Key字段不使其失效；
- 调用方请求数组即承担 O(k) 结果复制成本。

## 11. 业务 Aggregate 委托

```csharp
public delegate TValue Aggregate<TValue>(
    IReadOnlyList<TValue> values)
    where TValue : struct;
```

这段代码只定义业务委托的输入和输出格式，不包含任何聚合实现。RecordStore 不知道机器、IP、MAC 或
其他业务字段应该如何合并，也不提供通用缺省算法。具体 `Aggregate<TValue>` 委托必须由业务代码实现，
再通过 Publish 格式或 Publish 数组重载传入。

当前实现不公开独立 `Aggregate(...)` 操作方法。业务 Aggregate 委托只由 Publish 重载或 View 定义调用，
避免整体输出出现多个平行入口。委托语义固定为：

- 把传入的整个明确 List/Array 一次变成一条 TValue；
- 用结果构造一张只有一条记录的新 RecordStore；
- 忽略聚合结果中携带的旧 ID，由结果表分配新的 `StoreRecordId`；
- 来源数组和来源表不变；
- 不排序、不查找、不分组、不递归；
- 结果必须通过 RecordFilter、Limit、业务主键和唯一约束；
- 失败不产生结果表。

空 List/Array 没有可以产生的 TValue，对应 Publish 重载直接抛 `ArgumentException`。这与批量 Add 无关；
Publish 内部只对至少两条记录的冲突集合调用聚合委托，不会传入空数组。取消在委托调用前后和结果
提交前检查。

## 12. Publish

Publish 重载族是除 View 外唯一的整体输出入口：

```csharp
public RecordStore<TValue, TPrimaryKey> Publish(
    CancellationToken cancellationToken = default);

public RecordStore<TValue, TPrimaryKey> Publish(
    RecordStorePublishFormat<TValue> format,
    CancellationToken cancellationToken = default);

public RecordStore<TValue, TPrimaryKey> Publish(
    IReadOnlyList<TValue> values,
    Aggregate<TValue> aggregate,
    CancellationToken cancellationToken = default);

public RecordStore<TValue, TPrimaryKey> Publish(
    RecordStorePublishTarget target,
    RecordStorePublishFormat<TValue>? format = null,
    CancellationToken cancellationToken = default);

public RecordStore<TValue, TPrimaryKey> Publish(
    RecordStorePublishTarget target,
    IReadOnlyList<TValue> values,
    Aggregate<TValue> aggregate,
    CancellationToken cancellationToken = default);
```

```csharp
public enum RecordStorePublishTarget : byte
{
    Standalone,
    Snapshot
}

public sealed record RecordStorePublishFormat<TValue>(
    PublishSourceFilter<TValue>? SourceFilter = null,
    PublishResultFilter<TValue>? ResultFilter = null,
    PublishResultComparison<TValue>? ResultComparison = null,
    Aggregate<TValue>? ConflictAggregator = null);
```

重载分别表达缺省格式、显式格式、指定数组聚合，以及 Standalone/Snapshot 目标：

1. 缺省格式：使用 Source 当前可修改的 `DefaultPublishFormat`；未提供时等价于全部字段为 null 的空格式；
2. 指定格式：SourceFilter、ResultFilter 和 ResultComparison 完全使用本次对象，null 表示本次不执行
   对应步骤；ConflictAggregator 为 null 表示本次不聚合冲突，存在必须收敛的冲突时发布失败；
3. 指定数组聚合发布：调用业务传入的 Aggregate 委托，把已经选定的整个数组一次变成一条记录并返回单记录新表，取代旧的
   独立 Aggregate 方法。

Publish 的“格式”是同一 TValue 表的筛选、冲突收敛和排序格式，不是任意 TResult 或 JSON 文本格式。
其他结果类型使用 View/外部投影；JSON 使用持久化接口。

- 不修改来源；
- 不保存到来源属性；
- 不管理订阅、事件或已读；
- 不增加线程安全；
- 不强制只读；
- 返回表由调用方管理。

Standalone 目标返回独立、可写的本地 RecordStore，通常由调用方私有使用。

固定流程：

1. 读取现存记录；
2. 从主键索引提取重复冲突关系；
3. 按声明顺序从唯一约束索引提取冲突关系；
4. 对相互重叠的冲突关系求连通闭包，保证每条来源记录最多进入一个冲突集合；
5. 每个至少两条记录的冲突集合调用一次有效聚合器，得到一条 TValue；
6. 聚合结果重新建立冲突图；仍有冲突时继续下一轮，每轮记录数必须严格减少；
7. 忽略来源和聚合结果携带的旧 ID，由结果表统一分配新 `StoreRecordId`；
8. 不执行客户统计或通用业务分组；
9. 应用固定排序；
10. 完整验证后一次性构造新表。

冲突连通闭包只是防止主键约束与多个唯一约束重叠时重复消费同一条记录，不是面向客户的 Group By，
也不会改变聚合器的“一个明确数组一次变成一条记录”契约。

稳定迭代最多执行初始记录数轮；有冲突但记录数未减少表示聚合器不收敛，Publish 失败且不产生半张表。
需要收敛冲突但本次格式没有业务 Aggregate 委托时，Publish 明确失败；RecordStore 不猜测合并规则。
取消后抛出 `OperationCanceledException`，不产生半张结果表，也不改变来源状态。

## 13. Publish 的 Snapshot 目标

`RecordStorePublishTarget.Snapshot` 使用与 Standalone 相同的主键/唯一约束冲突整理，但把成功结果纳入
来源对象的受管 Snapshot 生命周期：

```csharp
public RecordStore<TValue, TPrimaryKey>? Snapshot { get; }
```

- 挂到来源对象 `Snapshot` 属性；
- 递增发布版本并记录源数据版本和时间；
- 管理订阅者；
- 发送发布事件；
- 登记订阅者是否已读；
- 完整成功后替换旧 Snapshot，失败保留旧值；
- Snapshot 只读，不能再次发布；
- 不承诺多线程安全，调用方自行串行化。

Publish 自身使用原子作业冻结。冻结期间 Add、Update、废止、约束替换和运行策略修改全部被拒绝；
Snapshot 数据和版本在冻结内提交，随后解除冻结，再调用观察者。回调写入不能改变本次
`SourceDataVersion`。

从 Snapshot 调用 `DeepClone()` 后得到 `Origin=Clone` 的独立、可写本地 RecordStore；Clone 不保留来源只读属性，
但它不是 Source，不能管理或发布 Snapshot。常规阶段衔接使用 Source 的 Snapshot 与订阅状态，Clone 只用于确实
需要脱离发布链修改原始数据的低层所有权场景。

Snapshot 沿用 RecordStore 自身 Clone/序列化格式，不定义特殊输出格式。当前实现不再公开
`PublishSnapshot()`；调用方统一使用 `Publish(RecordStorePublishTarget.Snapshot, ...)`。

发布事件在 Snapshot、版本和时间全部提交后触发：

```csharp
public sealed class SnapshotPublishedEventArgs<TValue, TPrimaryKey> : EventArgs
    where TValue : struct, IRecordStoreValue
    where TPrimaryKey : notnull
{
    public Guid SourceTableId { get; }
    public long PublicationVersion { get; }
    public long SourceDataVersion { get; }
    public DateTimeOffset PublishedAt { get; }
    public RecordStore<TValue, TPrimaryKey> Snapshot { get; }
}

public delegate void SnapshotPublishedEventHandler<TValue, TPrimaryKey>(
    object? sender,
    SnapshotPublishedEventArgs<TValue, TPrimaryKey> args);

public event SnapshotPublishedEventHandler<TValue, TPrimaryKey>?
    SnapshotPublished;
public long SnapshotObserverFailureCount { get; }
```

RecordStore 对每个观察者分别调用。观察者异常会增加 `SnapshotObserverFailureCount`，但不能回滚已经提交的
Snapshot，也不能阻止其他观察者。事件只在 Snapshot 目标 Publish 成功后触发；Standalone 不触发。

## 14. View

View 是一次性物化新表：

```csharp
public RecordStore<TValue, TPrimaryKey> CreateView(
    RecordStoreViewDefinition<TValue, TPrimaryKey> definition,
    CancellationToken cancellationToken = default);

public Task<RecordStore<TValue, TPrimaryKey>> CreateViewAsync(
    RecordStoreViewDefinition<TValue, TPrimaryKey> definition,
    CancellationToken cancellationToken = default);
```

- 可以临时过滤、排序和调用聚合委托；
- 完整建立 TValue、Key、业务主键和索引；
- 不跟踪来源变化；
- 不修改来源；
- 不进入 Snapshot 订阅生命周期；
- 仍是相同 TValue、TPrimaryKey 和 Schema 的 RecordStore；
- 可使用独立 `RecordStoreResultViewDefinition<TValue,TResult>` 输出任意结构型 TResult 数组；
- 同步 View 对小表同样是正式入口，不设置大小阈值；
- 异步 View 先取得完整深复制输入，再在后台物化，不能让后台任务读取正在变化的 Source；
- 取消不产生半张 View，也不修改来源。

复杂过滤和客户输出格式放在 View，不扩张八个基础查询。

TResult View 分为互斥的逐条 Projector 模式和 GroupComparison+GroupAggregate 模式。空输入返回空数组，
Aggregate 不接收空组；TResult 包含引用成员时必须提供 `IDeepCloneStrategy<TResult>`。同步和异步版本
都支持 SourceFilter、ResultFilter、ResultComparison 和取消。

## 15. Clone 与序列化

Clone 只有一种语义：按 RecordStore 存储格式生成独立本地副本。

```csharp
public RecordStore<TValue, TPrimaryKey> DeepClone();
```

- 共享不可变 Key/主键定义，复制实例配置、现存 TValue、`StoreRecordId` 和顺序；
- 不复制已删除记录；
- 不重跑 RecordFilter、Limit、Merge 或约束拒绝；
- 不提供多线程安全；
- 新对象身份独立，记录 ID 在副本自己的身份域内保持存储值。
- Clone 结果统一可写，包括从只读 Snapshot 产生的 Clone。

序列化与反序列化使用同一存储边界。反序列化是持久化恢复表，不是行 Restore。委托不能直接序列化，
通过稳定 `schemaId` 和 `ProfileVersion` 在 Schema Registry 中恢复不可变 Key/主键 Definition 及当前
Runtime Profile。同一 SchemaId 不允许替换 Key/主键 Definition；其他实例策略可运行时修改并在登记或
写出时刷新 Registry。反序列化必须校验槽位 ID 与 TValue 内 ID 一致，并把下一个 ID 种子恢复到大于
全部现存 ID 的位置，避免重新加载后复用身份。客户特殊格式使用 View。

### 15.1 JSON 持久化格式

RecordStore 使用带版本的 JSON 对象：

```json
{
  "format": "iwesun.record-store",
  "formatVersion": 3,
  "schemaId": "sample.machine.v1",
  "profileVersion": 1,
  "dataVersion": 12,
  "nextRecordId": 18,
  "valueEncoding": "json",
  "records": [
    { "recordId": 17, "value": { "storeRecordId": 17 } }
  ]
}
```

- `records` 保持登记顺序，只保存现存记录；
- `valueEncoding` 为 `json` 或 `codec-base64`；TValue 由 Schema Registry 对应的 Key/主键 Definition 与外部 Codec 配置解码；
- 不保存 Snapshot、订阅者、已读状态、事件、访问 Gate、索引缓存或正在发布状态；
- 恢复后产生新的 `TableId`、`Origin=Restored`、`IsReadOnly=false`；
- 保留记录 ID 和 DataVersion，并严格验证 `nextRecordId` 大于全部记录 ID；
- 未知 formatVersion、未知 schemaId、重复 ID、ID 不一致或无效主键均明确失败；
- ProfileVersion 与登记不一致、恢复后主键/唯一约束/Limit 违反 Profile 时明确失败；
- 反序列化器只接受已登记且版本匹配的当前格式，不静默兼容其他格式。

## 16. 顺序与枚举

- Add 成功记录保持全局登记顺序；
- 同一个普通 Key 桶保持登记顺序；
- 同一业务主键桶保持登记顺序；
- Update 不移动记录；
- Delete 隐藏/移除目标但不改变剩余记录相对顺序；
- 全表顺序访问使用枚举，不提供全表数组复制；
- 排序只影响查询数组或新表。

## 17. 线程边界

RecordStore、Source、Snapshot、View、Clone 和 Publish 返回表都不提供多线程安全。只读不等于线程安全。
调用方需要跨线程访问时必须自行串行化。类型不为每次操作增加隐式锁，但提供统一的可选访问 Gate：

```csharp
public RecordStoreAccessGate AccessGate { get; }

using var lease = store.AccessGate.Enter(cancellationToken);
await using var asyncLease = await store.AccessGate.EnterAsync(cancellationToken);
```

```csharp
public sealed class RecordStoreAccessGate
{
    public bool IsEntered { get; }

    public RecordStoreAccessLease Enter(
        CancellationToken cancellationToken = default);

    public ValueTask<RecordStoreAccessLease> EnterAsync(
        CancellationToken cancellationToken = default);
}

public sealed class RecordStoreAccessLease : IDisposable, IAsyncDisposable
{
    public void Dispose();
    public ValueTask DisposeAsync();
}
```

`RecordStoreAccessGate` 使用轻量 `SemaphoreSlim`，同步和异步入口互斥，Lease Dispose 后释放。Gate 不会
自动包裹 RecordStore 方法；同一共享实例的全部参与者必须遵循同一 Gate 才有意义。单线程调用方无需
进入 Gate。

同一个 Lease 重复 Dispose 只释放一次。等待取消不会取得 Lease，也不会改变 Gate 状态。Gate 不支持
递归进入；已经持有 Lease 的调用链再次 Enter 会等待，因此调用方必须把 Lease 放在最外层访问边界。

不提供 `IsSync`/`IsSynchronized` 作为安全判断。布尔值只能看到某一瞬间，检查完成后状态即可变化，
会产生先检查后使用竞态。是否安全由调用方是否持有 Lease 决定；可提供仅用于监视的 `AccessGate.IsEntered`
状态，但它不能代替获取 Lease。

## 18. .NET 接口边界

当前实现只保留不会扩张语义的基础接口：

```text
IEnumerable<TValue>
IReadOnlyCollection<TValue>
```

不实现 `ICollection<TValue>`、`IList<TValue>`、`IReadOnlyList<TValue>`、`ILookup<TKey,TValue>`、
`IDictionary<TKey,TValue>` 或 `ISet<TValue>`，避免暴露 Clear、Remove、Insert、位置索引、分组和单值键
等不支持或误导性的语义。完整遍历使用 IEnumerable，精确运行时查询使用八个正式方法。

## 19. 失败原子性

| 场景 | 行为 |
| --- | --- |
| 单条 Add 被规则拒绝 | Try 返回 false；强 API 抛明确异常 |
| 批量某项被规则拒绝 | 跳过该项并继续 |
| 用户委托抛异常 | 保留此前成功项，停止并传播 |
| Merge 候选无效 | A 不变，B Add 失败 |
| Update 改变业务主键 | 拒绝，原记录不变 |
| ID 不存在 | Try 返回 false |
| Publish 聚合结果无效 | 不产生结果表 |
| Publish 最终仍冲突 | 不产生结果表 |
| Snapshot 目标 Publish 失败 | 旧 Snapshot 和版本不变 |
| 整体操作被取消 | 抛 OperationCanceledException，不产生半张结果表 |
| Snapshot 观察者抛异常 | Snapshot 保持已提交，计数后继续其他观察者 |

## 20. 不变量

1. `StoreRecordId` 在一张表内唯一且不复用；
2. Key、Key 比较器和主键 Definition 构造后不变；约束、业务委托与实例选项通过受控属性和方法运行时改变；
3. 普通 Key 永远允许重复；
4. 业务主键不能全部为 null；
5. null 相等、哈希和排序服从同一 Key 定义；
6. Update 不改变业务主键；Merge 可以；
7. 删除记录不出现在查询、Clone、序列化和输出；
8. 数量查询等于对应数组长度但不先物化数组；
9. 失败操作不部分修改记录、索引、Count、版本或 Snapshot；
10. 整体操作不改写来源；
11. 新表拥有独立表身份和内部 ID 域；
12. 可变引用不跨输入、查询、Clone、View、Publish 和 Snapshot 意外共享。
13. 新输出表重新分配 ID；只有 Clone 和同格式反序列化保留已有 ID。

## 21. 复杂度目标

| 操作 | 目标 |
| --- | --- |
| Add | 期望 O(主键组件数 + 强制约束数) |
| StoreRecordId 定位 | 期望 O(1) |
| 普通 Key Value Count | 索引后 O(1) |
| 主键 Value Count | O(1) |
| 普通 Key Value Array | O(k) |
| 主键 Value Array | O(k) |
| Key/主键数组 | O(d log d)，可按版本缓存 |
| Update/Delete | O(受影响索引数) |
| Publish 数组聚合重载 | O(m + 用户委托成本) |
| Publish | O(n + 冲突整理 + 可选排序) |

普通 Add 不执行全表排序、全表复制或递归聚合。

活动实现使用不可复用的稳定 SlotHandle；Handle 通过 RecordId 到物理位置的间接映射解析，不使用会随
删除移动的列表位置。底层是固定容量 Segment 中的值类型 Slot；扩段不复制旧段，废止清空 TValue、写
墓碑并局部移除索引。索引桶通过分段Arena间接定位，按RecordId保持稳定顺序；Token映射使用缓存Hash
和带代次的稳定节点句柄，Entry槽与Arena槽均由自由链复用。Update、Merge和废止只维护受影响的主键、
已激活普通Key与唯一约束桶，变空节点在提交阶段即时退出生命周期。

## 22. 强制迁移

1. 新建只含 Key/主键的不可变索引定义；
2. 从单 GroupKey 迁移到多 Key；
3. 删除 `AllowKeyDuplicate`；
4. 在 Store 实例上设置主键重复、约束放宽及其他可变策略；
5. 建立多 Key、主键和唯一约束索引；
6. 用完整值写回替换 updater 委托；
7. 把 Deprecate 收敛为用户按 ID 删除，删除 Restore/DeprecateWhere；
8. 把旧 PublishAggregator 分成 Add Merge 和 Publish 聚合器；
9. 删除公开两记录 Merge；
10. 重写八个基础运行时查询和缓存；
11. 把普通发布、聚合发布和 Snapshot 发布统一为 Publish 重载族，并保留独立物化 View；
12. 统一 Clone/序列化存储边界；
13. 使用 ValueFormatter 完成写入格式化；
14. 保留公开状态、发布事件、订阅四态和领取时间；
15. 增加版本化 JSON、可选 AccessGate 和整体操作取消；
16. 更新消费者、迁移手册、样例和完整测试；
17. 删除旧集合接口，只保留 IEnumerable/IReadOnlyCollection。
18. 长期增删消费者按 PhysicalCount 计费，并在作业边界用 CreateCompactedStore 替换墓碑密集表。

## 23. 测试矩阵

- 任意数量异构 Key、重复名和无效主键引用；
- null Key、部分 null 主键、全 null 主键拒绝；
- 四种主键重复/AutoMerge 组合；
- 唯一约束放宽、Publish 聚合与最终冲突失败；
- AddRange 部分成功和异常中断；
- ValueFormatter 在所有 Add/AddRange 入口执行一次，内部重建不执行；
- 查询返回 TValue 携带权威 StoreRecordId，伪造/修改 ID 不能改错记录；
- Update 普通 Key 合法变化与主键变化拒绝；
- Merge 改变主键、失败原子性和 A 的 ID 保留；
- 按 ID 删除及所有索引/查询不可见；
- 普通 Key 四查询和主键四查询逐项一致；
- 数量查询不物化数组；
- Key/主键排序缓存及写后失效；
- Publish 数组聚合重载一次调用并产生单记录新表；
- Publish 不保存结果；
- Publish 冻结期间全部写入口和配置修改被拒绝，事件在解冻后运行；
- 聚合结果产生新冲突时继续到稳定，不收敛时确定性失败；
- Publish 的重叠冲突闭包不重复消费记录，输出重新分配 ID；
- Snapshot 目标 Publish 的事件、订阅和已读；
- Snapshot 观察者异常隔离和失败计数；
- 四态领取、首次领取时间和新订阅者下版本生效；
- 同型 View 和 TResult 投影/分组聚合 View 完整物化；
- 小表同步 View、异步 View 和取消原子性；
- Clone/序列化不恢复删除记录；
- Clone/反序列化保留 ID，恢复后的 ID 种子不复用旧值；
- Snapshot Clone 与 Standalone Publish 返回表可写；
- Publish 数组聚合重载拒绝空输入；
- 旧集合接口不可用；
- JSON 正常往返、未知版本、未知 Schema、重复/不一致 ID 拒绝；
- Runtime Profile 完整恢复、版本不匹配和重复 Schema 登记拒绝；
- 1023/1024缺省自适应边界、强制索引模式、比较器不一致回退和Key摘要生命周期；
- 10 万次 Update、5 万次墓碑废止热路径；
- AccessGate 同步/异步互斥、取消和 Lease 释放；
- 128/256 MB 唯一、高重复和多 Key 索引规模测试。

## 24. Snapshot 订阅

订阅者标识继续使用现有 `string`，不引入二进制 ID：

```csharp
public enum SnapshotTakeState : byte
{
    NoSnapshot,
    NotSubscribed,
    Pending,
    Taken
}

public void RegisterSnapshotSubscriber(string subscriberId);
public bool UnregisterSnapshotSubscriber(string subscriberId);
public bool TryTakeSnapshot(
    string subscriberId,
    out RecordStore<TValue, TPrimaryKey>? snapshot);
public bool IsSnapshotTaken(string subscriberId);
public SnapshotTakeState GetSnapshotTakeState(string subscriberId);
public bool TryGetSnapshotTakenAt(
    string subscriberId,
    out DateTimeOffset takenAt);
public int PendingSnapshotSubscriberCount { get; }
public bool AreAllExpectedSnapshotSubscribersTaken { get; }
```

subscriberId 使用 `StringComparer.Ordinal`，不能为空。每个订阅者对一个发布版本最多首次领取一次；新
发布版本重新开始 Pending 状态。发布提交时冻结该版本的预期订阅者集合；发布之后新登记的订阅者在
当前版本返回 NotSubscribed，从下一版本开始参与。首次成功领取时记录 UTC `DateTimeOffset`；重复领取
返回 false，但不改变首次时间。
注销同时退出当前版本预期集合；已经产生的 Taken 时间保留到下一次成功发布清理。

订阅和已读属于来源对象运行状态，不进入 Standalone Publish 返回表、Clone 或持久化数据。所有订阅方法只
允许在可发布 Source 上调用。

## 25. 遗漏审计

### 25.1 已补齐

- Key 与主键各四个运行时查询；
- 数量查询不先创建数组；
- 删除不再保留可观察废止状态；
- Publish 聚合委托明确不分组；
- Publish 重载族内明确分离 Standalone 与受管 Snapshot 目标；
- View 固定物化；
- Clone/序列化没有特殊格式；
- 无全表 `ToArray()`、分页和 Predicate 基础查询；
- 所有实例无多线程安全承诺。
- TValue 携带公开 StoreRecordId，查询结果可直接用于先读后写；
- Publish 数组聚合重载明确拒绝空输入；
- Snapshot Clone 和 Standalone Publish 返回表统一可写；
- 旧集合接口删除；
- Snapshot 订阅者继续使用 string。
- StoreRecordId 与 string 订阅者 ID 的身份域已明确分离。
- Publish 重叠冲突集合和新表/Clone/反序列化的 ID 规则已补齐。
- ValueFormatter、公开状态、事件、订阅四态、JSON、AccessGate 和取消契约已补齐。
- 原子 Publish 冻结、稳定迭代聚合、完整 Runtime Profile、稳定 Handle、分段值存储、局部索引提交和
  TResult View 已补齐。

### 25.2 审计结论

六项代码整改已经进入活动实现并通过 Data、Runtime、Aether 和 DDNS Snap 当前回归。单位记录内存
仍未达到相对 1.0.25 不超过 1.25 倍的目标，且消费者迁移尚未全部完成，因此仍不得宣称性能等价或直接
覆盖当前正式类型。

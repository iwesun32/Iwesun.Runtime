# RecordStore Add、合并、事件、订阅与 Clear 生命周期

> **状态**：CURRENT（进入Runtime 1.0.36-beta.1）
>
> **最后更新**：2026-07-24
>
> **源码参考**：`RecordStore.cs`、`RecordStore.Definitions.cs`、
> `RecordStore.DelegateChains.cs`、`RecordStore.Publication.cs`

## 1. 配置边界

构造函数只固定 Key、主键及其比较语义；这些结构决定索引，构造后不可更换。唯一约束、
输入策略和业务行为通过 Source 的运行时属性配置，可在未发布冻结时调整：

- `ValueFormatter`
- `RecordFilter`
- `MergeResolver`
- `MergeAdd`
- `Limit`
- `AutoMerge`
- `AllowPrimaryKeyDuplicate`
- `AllowUniqueConstraintViolation`
- `DefaultPublishFormat`

所有公开业务回调均使用命名 `delegate`，发布通知使用命名 `event`。不得用无名
`Func<>`、`Predicate<>`、`Comparison<>` 或 `Action<>` 隐藏业务语义。

## 2. Add 输入与冲突流程

`Add/TryAdd(B)` 按以下顺序执行：

1. 确认对象是可写 Source，且当前不在发布冻结期。
2. 克隆输入，依次执行 `ValueFormatter`、`RecordFilter`、主键计算和唯一约束计算。
3. 查询主键索引及唯一约束索引，形成“是否存在真实冲突”的存储事实。
4. 仅当存在主键或唯一约束冲突、`AutoMerge=true` 且 `MergeAdd` 已配置时，
   调用一次 `MergeAdd(B)`。
5. `MergeAdd` 返回成功时，Add 直接返回它给出的 `StoreRecordId`。
6. `MergeAdd` 返回失败时，基类重新查询主键和唯一约束冲突；不得复用调用前的冲突缓存。
7. 按 `AllowPrimaryKeyDuplicate`、`AllowUniqueConstraintViolation`、`Limit` 判断是否允许普通登记。
8. 通过后分配稳定 `StoreRecordId`，原子提交记录及全部索引并增加 `DataVersion`。

基类只负责发现冲突和维护索引完整性，不替业务选择已有记录 A，也不判断合并后的业务完整性。

## 3. MergeAdd 与 MergeResolver

```csharp
public delegate TValue MergeResolver<TValue>(TValue existing, TValue incoming);
public delegate bool MergeAdd<TValue>(TValue incoming, out StoreRecordId recordId);
```

派生表的 `MergeAdd(B)` 负责：

1. 根据本表的主键和唯一约束查询所有可能的 A。
2. 按业务规则选择正确 A；基类不提供缺省候选选择。
3. 调用 `MergeResolver(A, B)` 产生 C，或执行等价的显式业务合成。
4. 验证 C 的业务完整性。
5. 通过 RecordStore 的稳定 ID 更新/替换入口提交 C。
6. 返回成功与最终 `StoreRecordId`。

`MergeAdd` 不得对同一个 B 递归调用 `Add/TryAdd`。`MergeResolver` 只描述
`C = Merge(A,B)`，不能承担候选查询或提交。

命名处理委托支持串联，但必须由 `RecordStoreDelegateChains` 解释：

- Formatter、Resolver：前一步输出作为后一步输入。
- Filter、Limit：全部通过才通过。
- MergeAdd：按登记顺序，首个成功即停止。
- 排序比较：首个非零结果生效。
- Aggregate：逐级聚合。

Key、主键、唯一约束 Key 选择器以及异型投影属于结构定义，只允许单一委托。

## 4. 发布与普通聚合

`Publish(Standalone, ...)` 返回独立只读输出，不安装 Snapshot、不登记已读状态、不触发
`SnapshotPublished`。`Publish(Snapshot, ...)` 执行以下原子边界：

1. 冻结 Source 写入和运行时配置修改。
2. 克隆 Source 活动记录。
3. 执行 `SourceFilter`。
4. 对发布冲突集合调用业务 `ConflictAggregator`；没有委托时不得猜测合并。
5. 执行 `ResultFilter` 和 `ResultComparison`。
6. 构造同类型只读 Snapshot。
7. 原子安装 Snapshot，递增 `PublicationVersion`，记录 `LastPublishedDataVersion` 和 `PublishedAt`。
8. 将本次发布时已登记的订阅者全部置为 `Pending`。
9. 解除发布冻结，再触发 `SnapshotPublished` 事件。

发布聚合与 Add 合并是两个独立阶段：Add 合并用于控制 Source 的实时重复堆积；发布聚合用于
形成最终输出结构，不能互相替代。

## 5. 显式发布事件

```csharp
public event SnapshotPublishedEventHandler<TValue, TPrimaryKey>? SnapshotPublished;
```

事件参数包含 `SourceTableId`、`PublicationVersion`、`SourceDataVersion`、`PublishedAt`
和已安装的只读 `Snapshot`。事件在提交完成、解除冻结后触发。

每个观察者独立调用；某个观察者异常不会撤销发布，也不会阻止其他观察者，
只增加 `SnapshotObserverFailureCount`。事件用于立即唤醒消费者，不代表消费者已经完成领取。

## 6. 订阅、领取与等待

消费者必须先以稳定名称调用 `RegisterSnapshotSubscriber(subscriberId)`。每次新 Snapshot
发布时，只复制当时已登记的订阅者到本版期望集合，并清空本版领取记录。

`GetSnapshotTakeState` 的四态为：

| 状态 | 含义 |
| --- | --- |
| `NoSnapshot` | Source 尚未发布任何 Snapshot |
| `NotSubscribed` | 本版发布时该消费者没有登记 |
| `Pending` | 本版 Snapshot 尚未被该消费者领取 |
| `Taken` | 本版 Snapshot 已由该消费者领取 |

消费者收到事件后调用 `TryTakeSnapshot`。领取成功会原子写入本版领取时间；同一消费者不能重复领取
同一版。处理完成后再次检查状态或发布版本；若处理期间上游已经发布下一版，应立即再次领取。

RecordStore 不提供内部阻塞等待线程的业务循环。标准等待方式是“事件唤醒 + 四态查询 +
`TryTakeSnapshot`”；调用方自己的取消、调度和重试不得塞入 RecordStore。

## 7. Clear

`Clear()` 只清空可写 Source 当前的活动记录：

- 通过正式废止路径逐条移除，因此主键、普通 Key、唯一约束和自适应索引同步更新。
- 保留已经安装的 Snapshot。
- 保留 `PublicationVersion`、订阅登记和当前 Snapshot 的 Pending/Taken 状态。
- Snapshot/只读输出调用 `Clear()` 必须拒绝。
- 发布冻结期间调用必须拒绝。

派生表若包含表头值、Local、无名区或子表，必须覆盖 `Clear()`：先调用 `base.Clear()`，
再递归清理附加可写状态，并用正式附加状态版本入口记录变化。不得借 Clear 删除或替换 Snapshot。

## 8. 当前源码消费方式

本批次尚未集中发布。DDNS Snap 在开发阶段通过 `ProjectReference` 直接引用 Runtime Data 源项目，
确保公共源码与消费者参加同一次编译。集中发布完成后，再统一切换为 Runtime 安装目录中的正式 DLL；
不得长期保留“源码 DLL 路径”这种介于源码引用和正式发布引用之间的状态。

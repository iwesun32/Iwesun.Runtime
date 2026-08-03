# RecordStore 派生业务表合同

> **状态**：IMPLEMENTED  
> **最后更新**：2026-07-23

## 目标

允许业务集合直接派生 `RecordStore<TValue,TPrimaryKey>`，在继承的记录集合之外增加少量表头业务属性，
同时继续使用 RecordStore 的 Source、Snapshot、View、Clone、约束、合并和发布能力。

公共库不得引用任何消费者业务类型。

## 记录类型

`TValue` 必须实现 `IRecordStoreValue`，可以是值类型或引用类型。

- 值类型保持现有行为。
- 引用类型必须显式设置完整的 `IDeepCloneStrategy<TValue>`。
- 引用类型没有显式深克隆策略时，首次取得记录所有权的操作必须明确失败。
- 输入、查询、更新、合并、发布、View、Clone 和序列化边界均使用同一深克隆合同。
- null 不是合法记录。

## 派生输出

RecordStore 提供受保护的派生输出工厂。Publish、Snapshot、View、DeepClone 和 Compact 创建输出时，
必须经过该工厂，而不是固定构造基础 `RecordStore<TValue,TPrimaryKey>`。

派生类负责在工厂中：

- 创建同一派生业务表类型；
- 复制或发布自己的表头业务属性；
- 保证 Snapshot 表头与 Source 不共享可变所有权；
- 保证 Snapshot 表头只读。

派生记录含有下级可变对象时，可以覆盖 `ConfigureDerivedOutputStore`，在基础运行配置复制完成后，
根据输出 Origin 安装对应的深克隆策略；Snapshot 使用只读子对象策略，Clone 使用可写副本策略。

基础类继续负责：

- 复制 Store 配置；
- 写入输出记录；
- 设置 Origin、只读状态、版本和发布时间；
- 安装 Snapshot 并管理订阅领取。

## 表头变化

公共库提供受保护的 Source 变化通知方法，供派生表在表头原子属性变化后更新 DataVersion。
派生表不得自行修改 RecordStore 的版本字段或发布状态。

## Source 清空

`RecordStore.Clear()` 只清空当前可写 Source 的活动记录，保留已安装 Snapshot、
PublicationVersion、订阅登记和 Pending/Taken。Snapshot 上调用必须拒绝。

带表头值或子表的派生业务表必须覆盖 `Clear()`：先调用 `base.Clear()` 清继承记录，
再递归清空自己的可变表头和下级 Source，并通过 `MarkAdditionalStateChanged()` 登记
实际发生的表头变化。不得通过 Clear 删除或替换 Snapshot。

## 兼容性

- 现有值类型消费者不需要修改。
- 不增加旧类型别名或包装集合。
- 派生类没有覆盖输出工厂时，行为与当前基础 RecordStore 完全一致。
- `Register(store)` 通过派生输出工厂创建不含记录的恢复原型，JSON 恢复保持派生运行时类型，
  Registry 不得永久持有包含业务记录的 Source。
- 特殊构造可以显式登记恢复工厂；工厂必须返回同一 Definition 的空、可写 Source。
- JSON 不隐式序列化派生表头；需要持久化的表头由业务格式或恢复工厂明确提供。

## 验收

1. 现有 Data Debug/Release 测试全部通过。
2. 新增引用记录深克隆所有权测试。
3. 新增派生类型 Publish/Snapshot/View/Clone/Compact 类型保持测试。
4. 新增派生表头 Source/Snapshot 隔离测试。
5. 新增派生类型 JSON 登记与恢复测试。
6. Runtime、Aether、DDNS Snap Debug/Release 构建通过。

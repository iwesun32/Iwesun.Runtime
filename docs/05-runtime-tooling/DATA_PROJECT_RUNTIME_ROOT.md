# Runtime Data 与 RecordStore 技术说明

> **状态**：CURRENT
> **最后更新**：2026-07-18
> **源码**：`Iwesun.Runtime.Data/`、`D:\Git Space\Data\Iwesun.Data/RecordStore*.cs`

## 1. 两个 Data 项目的边界

- `Iwesun.Runtime.Data`：Runtime 专属值类型和协议，包括 RuntimeRoot 条目、文件路径描述、
  静态注入目录、FIFO 传输模型和值类型指令。
- `Iwesun.Data`：独立、业务无关的数据基础库；Runtime使用其中的
  `RecordStoreV2<TValue,TPrimaryKey>`作为结构型内存记录存储。V2仍保持隔离名称，不能在消费者文档中
  提前写成已晋升的正式`RecordStore`。

依赖方向固定为：

```text
Iwesun.Data                    Iwesun.Runtime.Data
          \                    /
           Iwesun.Runtime.Diagnostics
                      ↑
       SampleHost / CLI / FunctionalTests
```

`Iwesun.Runtime.Data` 不再提供或维护私有 `RuntimeDList<T>`。

## 2. RuntimeRoot 基础类型

- `RuntimeRootEntryEnvelope`
- `RuntimeRootTableSnapshot`
- `RuntimeRootSnapshot`
- `RuntimeFilePathDescriptor`
- `IRuntimeRootAuxIndex`
- `RuntimeRootSortedPrimaryKeyIndex`

这些类型仍属于 `Iwesun.Runtime.Data`，因为它们描述 Runtime 协议和快照，而不是通用数据库能力。

## 3. RecordStore 使用方式

Runtime 当前使用 RecordStore 的范围只有：

- 状态历史：稳定追加、`StoreRecordId` 精确废止、128 条有界裁剪；
- RuntimeRoot 表：以 `Id` 分组，显式 Upsert 和主键冲突替换；
- 文件路径登记：以 `FilePathName` 分组，显式更新和主记录归一。

不使用以下能力：持久化、JSON 转换、发布快照、聚合发布、流程 Marker。

RuntimeRoot当前不启用添加时自动合并，继续由容器锁、`StoreRecordId`和业务索引完成Upsert/主记录
归一。V2的Definition只冻结多个Key与业务主键；过滤、限制、Merge、唯一约束和Publish格式属于Store
实例配置。完整公共契约见随安装包发布的`RECORD_STORE_V2_PUBLIC_API.md`。

## 4. 克隆和所有权

RecordStore 要求结构值中的可变引用具有明确深复制策略：

- `SecondaryKeys`、`Annotations` 每次克隆为新的只读字典；
- RuntimeRoot 任意 Payload 转换为独立 `JsonElement`；不可序列化对象退化为类型摘要；
- 状态历史复制公开字段，不把可变 `RuntimeStateCatalog` 引用带入历史快照。

这保证调用方修改原对象后不会覆盖 Store 内记录。

## 5. 迁移资料

- Runtime 实施计划：`RECORD_STORE_MIGRATION_PLAN.md`
- DList迁移手册：`D:\Git Space\Data\docs\02-api\DLIST_TO_RECORD_STORE_V2_MIGRATION.md`
- 1.0.25迁移手册：`D:\Git Space\Data\docs\02-api\RECORD_STORE_1_0_25_TO_V2_MIGRATION.md`
- 上游API：`D:\Git Space\Data\docs\02-api\RECORD_STORE_V2_PUBLIC_API.md`
- 上游设计：`D:\Git Space\Data\docs\01-design\RECORD_STORE_DESIGN_V2.md`
- 发布状态：`D:\Git Space\Data\docs\RELEASE_STATUS.md`

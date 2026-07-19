# RuntimeRoot 容器技术说明（RecordStore + 辅助索引）

> **状态**：CURRENT
> **最后更新**：2026-07-17
> **源码**：`Iwesun.Runtime.Data/RuntimeRootDataStructures.cs`、`Iwesun.Runtime.Diagnostics/RuntimeRootContainer.cs`

## 1. 主存储

`RuntimeRootTable` 使用 `RecordStore<string,RuntimeRootEntryEnvelope>`：

- 分组键为 `Id`；
- 新条目使用 `Append`；
- 同 ID 更新使用 `StoreRecordId` 和 `TryUpdate`；
- `PrimaryKey` 冲突时显式废止旧 RecordId，再登记新映射；
- 不使用 Add 自动合并、节点引用、按键隐式删除或 Source 原地排序。

`RuntimeRootContainer` 的文件路径表使用
`RecordStore<string,RuntimeFilePathDescriptor>`，分组键为 `FilePathName`。主记录唯一化和
输出排序由 Runtime 层明确执行。

## 2. 索引

RecordStore 管理记录身份和稳定遍历；Runtime 同时维护协议所需的业务索引：

- `id -> StoreRecordId`
- `primaryKey -> StoreRecordId`
- `secondary(key,value) -> id set`
- `filePathName -> StoreRecordId`

可插拔的 `RuntimeRootSortedPrimaryKeyIndex` 维护独立有序快照，通过二分下界和顺序扫描完成
前缀查询。更新后在同一 RuntimeRoot 锁内重建，避免返回混合版本。

## 3. 并发与快照

RecordStore 的普通写入不承诺并发安全，因此 RuntimeRoot 的所有写入、索引维护和快照读取均
在容器锁内完成。RecordStore Schema 的深复制策略隔离字典、注解和 Payload，快照不会暴露
Store 内部可变引用。

## 4. 对外动作

`runtime.root` 继续提供 `snapshot/refresh/table/get/query/prefix/indexes`，JSON 和 CLI 契约不变。
本次迁移只替换内部存储，不改变 T01~T10 表名、查询动作或分页边界。

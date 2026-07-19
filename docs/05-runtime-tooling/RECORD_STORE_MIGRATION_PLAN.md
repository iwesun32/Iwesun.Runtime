# RuntimeDList 到 Iwesun.Data RecordStore V2 迁移计划

> 状态：已实施并通过回归
> 日期：2026-07-18
> 权威上游：`D:\Git Space\Data`及其`DLIST_TO_RECORD_STORE_V2_MIGRATION.md`

## 1. 目标

Runtime 不再维护或使用私有 `RuntimeDList<T>`，改为引用独立数据基础库的
`Iwesun.Data.RecordStoreV2<TValue,TPrimaryKey>`。`Iwesun.Runtime.Data`继续承载Runtime专属的
根数据记录、注入目录和值类型传输协议，不与独立 `Iwesun.Data` 合并。

## 2. 迁移范围

当前实际调用分为三类：

1. `RManagedState`、`RuntimeManagedRegistry` 的有界状态历史；
2. `RuntimeRootTable` 的条目登记、按 ID 更新、主键唯一替换和辅助索引；
3. `RuntimeRootContainer` 的文件路径描述登记、主记录归一和稳定输出顺序。

发布工程、安装版 SampleHost、验证脚本和 Data 专项文档必须同步增加
`Iwesun.Data.dll`，不能依赖仓库内手工复制的 DLL。

## 3. 语义决策

- 历史记录允许重复状态码，使用 `Append` 保持插入顺序；超出深度后按
  `StoreRecordId` 废止最旧记录并压缩。
- RuntimeRoot以`Id`作为V2业务主键。Upsert显式按`StoreRecordId`更新，当前RuntimeRoot不启用
  Add自动合并；如业务需要，必须同时设置`AutoMerge`、`MergePredicate`和`MergeResolver`。
- `PrimaryKey` 冲突继续采用“新记录替换旧记录”语义，但冲突对象先显式废止，
  不使用按键隐式删除。
- 文件路径以 `FilePathName` 为分组键。主记录选择和输出排序由 Runtime 业务层显式完成，
  不要求 RecordStore 原地排序。
- RecordStore 对外输出必须经过深复制策略。辅助字典复制为新的只读字典；任意 Payload
  转换为独立 `JsonElement`，无法序列化时退化为有限诊断摘要，避免共享可变业务对象。
- 不使用 RecordStore 的 JSON 持久化、发布快照或流程 Marker；本轮只迁移内存登记与查询。

## 4. 实施步骤

1. 在 Runtime 解决方案中加入跨仓库 `Iwesun.Data` ProjectReference，并保持依赖方向为
   Runtime -> Data。
2. 新增线程安全的状态历史适配器，集中封装 Schema、追加、裁剪、清空和快照。
3. 将 RuntimeRoot 两类容器改为 RecordStore 与 `StoreRecordId` 映射。
4. 删除 `RuntimeDListNode`、结果枚举和 `RuntimeDList<T>` 私有实现及所有引用。
5. 更新安装版引用、全量发布目录、自检脚本和当前文档。
6. 运行Data全测试、Runtime Debug/Release构建、Data相关`root-safety`场景和发布树校验；Runtime完整
   功能工具的独立失败由Runtime发布门槛单独跟踪。

## 5. 验收条件

- Runtime 源码不存在 `RuntimeDList` 使用或实现；
- 状态历史仍保持最多 128 条、顺序不变且并发访问受保护；
- RuntimeRoot 的 ID Upsert、PrimaryKey 替换、辅助键查询、前缀查询和快照保持兼容；
- 文件路径主记录唯一且快照顺序稳定；
- `Iwesun.Data.dll` 来自上游项目构建，并进入 Debug、Release、安装样例与全量发布；
- Data测试、Runtime Debug/Release构建及Data相关功能场景通过。

## 6. 验收结果（2026-07-18）

- Data删除DList活动实现后，V2 Debug/Release各121项通过，包含稳定索引节点、10万次Key迁移、全索引
  废止、混合操作、异常原子性、Snapshot、View、Clone和序列化；
- Runtime Debug/Release：均为 0 警告、0 错误；
- Runtime Data相关`root-safety`场景通过；完整工具仍有构建模式场景清单、CLI catalog和断点子进程等
  Runtime独立失败，不能写成全量通过；
- 聚焦验证新增状态历史 128 条边界、值引用隔离、ID Upsert、PrimaryKey 替换、辅助键查询和
  文件主记录稳定排序；
- 全量 staging 自检通过，安装版 SampleHost 的 Debug/Release 输出均包含与发布树哈希一致的
  `Iwesun.Data.dll`。

上游公共属性、开关和委托语义以`RECORD_STORE_V2_PUBLIC_API.md`为准。RuntimeRoot由V2业务主键和
自身辅助索引逻辑完成归一，不把MergePredicate当作独立唯一性约束。

## 7. 单写者与快照并发边界

- Runtime 现有 StateHistory、RuntimeRoot 和文件登记已经由各自 `_gate` 完整保护，继续使用外层锁，
  不重复获取 RecordStore `SourceAccess`。
- RecordStore Source 不允许重叠写入或发布；误用快速失败。Runtime 当前不使用 RecordStore Publish，
  因而不会改变既有登记路径。
- 若未来 Runtime 把 RecordStore Snapshot 用于跨线程交付，创建和 Publish 仍由 Source 所有者执行；
  其他线程只读取完整 Snapshot。Snapshot 的自适应索引支持并发首次查询。

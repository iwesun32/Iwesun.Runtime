# 当前活动需求：RecordStore 统一体系

> 状态：实施与发布验证中  
> 更新日期：2026-07-24

## 1. 唯一公共体系

1. 正式公共类型为 `RecordStore<TValue,TPrimaryKey>`。
2. 配套公共名称统一为 `RecordStoreOrigin`、`RecordStoreSchemaRegistry`、`RecordStoreDefinition` 等无版本后缀名称。
3. 活动源码文件、测试文件、测试命名空间、规模工具、文档和技能不得再使用 RecordStore 版本后缀。
4. 不提供旧类型别名、继承壳、类型转发或条件编译兼容层；遗漏迁移必须在编译期暴露。
5. 持久化文档中的 schema/profile 数字版本仍承担格式兼容职责，不与公共 CLR 类型命名混为一谈。

## 2. 程序集与命名空间

1. 唯一程序集为 `Iwesun.Runtime.Data.dll`。
2. 唯一命名空间为 `Iwesun.Runtime.Data`。
3. `Iwesun.Data.dll`、`Iwesun.Data`、DList 和 RecordStore V1 只允许存在于忽略存档。
4. Runtime、Aether、DDNS Snap 和其他自有项目必须完成强制引用升级。

## 3. 行为合同

1. `RecordStoreDefinition` 只冻结 Key 定义与业务主键定义。
2. Schema、Clone、Codec、索引阈值、唯一约束、过滤、限制、合并及发布策略属于 Store 实例配置。
3. `StoreRecordId` 是内部稳定记录身份；业务主键和普通 Key 不得取代它。
4. Source 保持单逻辑写者；跨流程主要消费完成后的只读 Snapshot。
5. 用户比较器与业务委托只在准备阶段调用，提交阶段不得重复执行用户代码。
6. 失败必须保持顺序、索引、记录身份、版本和发布状态原子一致。
7. JSON 恢复必须匹配已登记的 SchemaId 与正整数 ProfileVersion。
8. Add 只负责检测实际主键冲突和唯一性冲突；冲突且 `AutoMerge=true` 时把标准化后的输入 B
   交给继承类型配置的 `MergeAdd(B)`。基类不得替业务类型选择 A，也不得提供通用候选循环。
9. `MergeResolver(A,B)` 只负责产生 C；继承类型负责候选查询、业务完整性判断和合并编排，并通过
   基类受保护的原子提交方法更新记录及全部索引。
10. 引用类型记录不接受 null；所有取得记录所有权的入口必须明确拒绝，不得泄漏空引用异常。
11. Schema Registry 不得长期持有包含业务记录的 Source；派生类型恢复使用空恢复原型或显式
    恢复工厂，并校验 Definition、空表、可写状态和 Origin。

## 4. 发布门槛

- Data Debug/Release 各 101 项测试全部通过。
- Data Any CPU、x86、x64 的 Debug/Release 六种解决方案配置全部构建通过。
- 发布、冲突聚合、热更新/删除、百万次周转压缩和索引基准通过。
- Runtime Debug/Release 全解决方案 0 错误。
- Aether 与 DDNS Snap Debug/Release 编译通过。
- 发布和安装目录不得包含旧程序集、旧类型名或旧源码文档入口。

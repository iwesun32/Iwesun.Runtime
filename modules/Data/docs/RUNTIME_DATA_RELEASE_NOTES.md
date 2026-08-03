# Iwesun.Runtime.Data 更新记录

## Runtime 1.0.36-beta.1：MergeAdd、显式委托/事件与 Source Clear

- Add 在真实主键/唯一约束冲突且启用 `AutoMerge` 时调用派生表 `MergeAdd(B)`；候选 A 的选择和
  业务完整性归派生表，基类只维护索引安全提交。
- 公共业务回调统一为命名委托，Snapshot 通知统一为命名事件，并明确多播链执行语义。
- Snapshot 发布保留订阅登记、四态领取和事件唤醒；消费者不再通过清空公共数据表达已读。
- 增加 Source `Clear()`，只清活动记录并保留 Snapshot、发布版本和订阅领取状态；派生表负责递归
  清理自己的表头和子表。
- 引用类型记录的 null 更新统一返回 `NotFound`；派生表 JSON 恢复通过不含业务记录的登记原型
  保持派生运行时类型，避免 Registry 长期持有带数据 Source。
- Data 模块解决方案补齐 Any CPU、x86、x64 的 Debug/Release 配置与项目映射。
- 规模工具的活动输出统一为 `record-store`，清除旧实现版本后缀。
- 本批次随Runtime `1.0.36-beta.1`统一发布Debug/Release DLL、文档和校验清单；Data不单独生成包，
  跨仓库消费者不得使用源码`ProjectReference`。

## 2026-07-22：RecordStore 正式命名统一

- 正式类型由带版本后缀的开发名称统一为 `RecordStore<TValue,TPrimaryKey>`。
- 配套类型统一为 `RecordStoreOrigin` 与 `RecordStoreSchemaRegistry`。
- 源码、测试、规模工具、Runtime Diagnostics、Aether 和 DDNS Snap 消费点同步迁移。
- 不提供旧名称兼容别名；未迁移消费者在编译期失败。
- CLR 公共类型命名与 JSON Schema/Profile 的数字版本职责分离，持久化格式版本不因本次改名自动重写。
- DList、RecordStore V1、`Iwesun.Data.dll` 和旧命名空间继续保持退役状态。

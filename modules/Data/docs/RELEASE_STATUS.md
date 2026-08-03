# Runtime Data RecordStore 发布状态

> 状态：GENERAL_BETA_READY_FORMAL_BLOCKED  
> 最后更新：2026-07-25  
> 正式类型：`Iwesun.Runtime.Data.RecordStore<TValue,TPrimaryKey>`

## 当前结论

- 2026-07-24 的 MergeAdd、显式委托/事件、订阅领取和 Source Clear 批次已进入源码与文档，
  随Runtime `1.0.36-beta.1`统一生成Debug/Release DLL、完整文档与校验清单；Data不单独生成包。
- Runtime 只构建和发布 `Iwesun.Runtime.Data.dll`，命名空间统一为 `Iwesun.Runtime.Data`。
- `RecordStore<TValue,TPrimaryKey>` 是唯一活动实现；正式 API、文件名、测试名和消费者引用均不带版本后缀。
- DList、RecordStore V1、`Iwesun.Data.dll` 与 `Iwesun.Data` 命名空间不提供兼容别名或类型转发。
- 旧实现和晋升阶段审查材料只保存在忽略存档，不参与解决方案、编译、发布或安装。
- 本次属于强制迁移：旧类型名应直接产生编译错误，促使消费者同步升级。

## 已验证能力

RecordStore 当前覆盖多 Key、业务主键、稳定 `StoreRecordId`、自适应索引、唯一约束、过滤与限制、
Merge、Publish、Snapshot、View、Clone、JSON 持久化及可选访问 Gate。

- Data Debug：101/101 测试通过。
- Data Release：101/101 测试通过。
- Any CPU、x86、x64 的 Debug/Release 解决方案配置和项目映射完整，六种组合均构建通过。
- 10 万记录发布、20 万记录冲突聚合、10 万次更新/5 万次删除、100 万次周转压缩和完整索引基准通过。
- Runtime Diagnostics 内部状态历史与 RuntimeRoot 已迁移到正式 `RecordStore`。
- 自动合并已改为冲突检测后调用继承表 `MergeAdd(B)`；基类不再选择候选 A。
- `MergeAdd`仍完全拥有继承表的候选选择和业务合并逻辑；基类只做最低成本防护，成功返回的
  `StoreRecordId`必须属于当前Source的活动记录，防止错误委托返回外部或失效ID。
- 引用类型记录的 null 更新返回 `NotFound`，不再泄漏 `NullReferenceException`。
- Schema Registry 使用空恢复原型保持派生表运行时类型，不持有带数据 Source；显式恢复工厂也会校验
  Definition、空表、可写状态和 Origin。
- Aether 与 DDNS Snap 使用本普通β完成新`MergeAdd`合同迁移和Debug/Release编译；结果是正式版冻结门禁，
  不阻止本次β供其同步升级。

## 发布边界

安装版唯一数据程序集位置：

```text
C:\Program Files\Iwesun\Runtime\lib\Iwesun.Runtime.Data\Iwesun.Runtime.Data.dll
```

Runtime仓库内部测试可使用项目引用；跨仓库开发和发布消费者统一引用安装目录或便携载荷DLL。
发布树中出现旧数据程序集、带版本后缀的 RecordStore 类型或 DList 即判定失败。

## 权威文档

- [统一设计](01-design/RECORD_STORE_DESIGN.md)
- [公共 API](02-api/RECORD_STORE_PUBLIC_API.md)
- [Add、合并、事件、订阅与 Clear 生命周期](02-api/RECORD_STORE_ADD_PUBLICATION_LIFECYCLE.md)
- [源码映射](03-reference/SOURCE_MAP.md)

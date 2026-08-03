# RecordStore 源码映射

> 状态：CURRENT  
> 最后更新：2026-07-22

## 活动源码

| 文件 | 责任 |
| --- | --- |
| `RecordStore.Abstractions.cs` | 记录值、克隆、访问 Gate 等基础合同 |
| `RecordStore.Definitions.cs` | Key、业务主键、约束、选项、发布与来源定义 |
| `RecordStore.cs` | 写入、查询、索引、稳定身份、墓碑与紧凑 |
| `RecordStore.SortedKeyIndex.cs` | 普通 Key 排序索引与自适应索引路径 |
| `RecordStore.Publication.cs` | Publish、Snapshot、订阅与冲突聚合 |
| `RecordStore.View.cs` | 同型/投影视图与 DeepClone |
| `RecordStore.Serialization.cs` | Schema Registry、Runtime Profile、Codec 与 JSON 恢复 |
| `SegmentedAppendStore.cs` | 分段追加物理存储 |

## 活动验证

- `Iwesun.Runtime.Data.Tests`：101 项功能、合同、异常安全、派生恢复、序列化、索引与发布测试。
- `Iwesun.Runtime.Data.ScaleTest`：发布、聚合、热路径、长期替换和索引基准。
- `Iwesun.Runtime.Diagnostics`：RuntimeRoot 与状态历史的真实内部消费者。
- Aether 与 DDNS Snap：跨仓库业务消费者编译与回归。

## 权威文档

- [统一设计](../01-design/RECORD_STORE_DESIGN.md)
- [公共 API](../02-api/RECORD_STORE_PUBLIC_API.md)
- [Add、合并、事件、订阅与 Clear 生命周期](../02-api/RECORD_STORE_ADD_PUBLICATION_LIFECYCLE.md)
- [发布状态](../RELEASE_STATUS.md)

晋升前的审查、性能报告和旧实现已保存在忽略存档，不进入活动文档树或发布包。

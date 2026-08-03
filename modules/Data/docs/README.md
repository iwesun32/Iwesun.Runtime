# Iwesun.Runtime.Data

`Iwesun.Runtime.Data` 是 Runtime 的唯一数据程序集和命名空间，同时承载 Runtime 数据契约与
`RecordStore<TValue,TPrimaryKey>`。

## 正式边界

- DLL：`Iwesun.Runtime.Data.dll`
- 命名空间：`Iwesun.Runtime.Data`
- 记录表：`RecordStore<TValue,TPrimaryKey>`
- 稳定内部身份：`StoreRecordId`
- 旧 DList、RecordStore V1、`Iwesun.Data.dll`：只保存在忽略存档，不参与构建和发布
- 旧版本后缀类型：不提供兼容入口，消费者必须强制升级

RecordStore 提供多 Key、业务主键、自适应索引、约束、过滤、限制、Merge、Publish、Snapshot、
View、Clone、JSON 持久化和可选访问 Gate。

## 文档入口

- [当前需求](REQUIREMENTS_ACTIVE.md)
- [发布状态](RELEASE_STATUS.md)
- [统一设计](01-design/RECORD_STORE_DESIGN.md)
- [派生业务表合同](01-design/RECORD_STORE_DERIVED_TABLES.md)
- [公共 API](02-api/RECORD_STORE_PUBLIC_API.md)
- [Add、合并、事件、订阅与 Clear 生命周期](02-api/RECORD_STORE_ADD_PUBLICATION_LIFECYCLE.md)
- [源码映射](03-reference/SOURCE_MAP.md)
- [更新记录](RUNTIME_DATA_RELEASE_NOTES.md)

## 构建与验证

```powershell
dotnet build modules/Data/Iwesun.Runtime.Data.slnx -c Debug
dotnet build modules/Data/Iwesun.Runtime.Data.slnx -c Release
dotnet test modules/Data/tests/Iwesun.Runtime.Data.Tests/Iwesun.Runtime.Data.Tests.csproj -c Debug
dotnet test modules/Data/tests/Iwesun.Runtime.Data.Tests/Iwesun.Runtime.Data.Tests.csproj -c Release
```

当前源码单元测试基线为 Debug/Release 各 101 项。规模测试位于
`Iwesun.Runtime.Data.ScaleTest`，按发布风险选择执行，不作为每次快速编译的阻塞长测。

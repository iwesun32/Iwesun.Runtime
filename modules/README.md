# Runtime 领域模块目录

`modules/` 按功能领域组织 Runtime 的可维护源码。程序集名称、命名空间、公共 API 和安装目录不因本次目录整理而改变。

| 领域 | 目录职责 |
| --- | --- |
| `Cli` | 命令行入口与配置 |
| `Data` | 数据实现、单元测试、规模测试和权威文档 |
| `Diagnostics` | 诊断实现、功能测试与 SampleHost |
| `Networks` | 网络实现、单元测试、样例、WFP 验证和权威文档 |
| `WebView2` | WebView2 实现、样例和专项文档 |
| `RemoteConsole` | 远程控制台实现与协议合同 |
| `Packaging` | 发布聚合工程与 MSI 安装工程 |

每个领域根目录的 `Iwesun.Runtime.<Domain>.slnx` 是该领域的日常开发入口，包含实现、测试、样例、验证项目及其跨领域产品依赖，确保从 Visual Studio 或 `dotnet build` 选择 Debug/Release 时，配置能完整传播到整个依赖图。安装后 DLL 消费验证工程只作为可见入口登记，默认不参与领域构建。仓库根 `Iwesun.Runtime.slnx` 只服务全量发布，不作为精细调试工作区。

根解决方案和 Packaging 解决方案的配置全部显式登记在 `.slnx`：`Debug` 和 `Release` 只编译发布产品；`Publish` 将全部产品映射到 `Release`，并由发布聚合项目生成完整 staging。统一命令为 `dotnet build Iwesun.Runtime.slnx -c Publish`。

每个领域只创建实际需要的角色目录：

- `src`：正式实现；
- `tests`：自动化测试；
- `samples`：可运行样例；
- `tools`：规模测试或辅助工具；
- `validation`：依赖真实系统能力的验收程序；
- `protocol`：跨进程协议合同；
- `docs`：该领域唯一的权威文档；
- `release`、`setup`：发布与安装工程。

仓库级设计、发布说明和活动需求仍位于根目录 `docs/`，领域细节由其链接到对应模块文档。

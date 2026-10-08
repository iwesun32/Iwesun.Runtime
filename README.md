# iwesun Runtime

**面向 AI 自动调试与服务运行管理的 .NET 公共基础库。**

[English](README.en.md) · [使用入门](docs/GETTING_STARTED.md) · [详细指南](docs/IWESUN_RUNTIME_USER_GUIDE.md) · [发布版本](https://github.com/iwesun32/Iwesun.Runtime/releases) · [参与维护](CONTRIBUTING.md) · [许可证](LICENSE)

iwesun Runtime 提供运行时诊断、服务生命周期管理、小型关系表数据结构、WebView2 访问控制和并行网络接口。
标准 CLI 与配套 AI 技能把业务现场变成可观察、可访问、可控制的接口，使 AI 在完成接入与授权后连续开展调试，减少人工逐步介入。

对外名称是 **iwesun**；GitHub 账号使用 `iwesun32`，仓库地址保持不变。

功能关键词：[AI 自动调试](docs/GETTING_STARTED.md#diagnostics) · [服务生命周期](docs/GETTING_STARTED.md#diagnostics) · [RecordStore](docs/GETTING_STARTED.md#data) · [WebView2 自动化](docs/GETTING_STARTED.md#webview2) · [并行网络接口](docs/GETTING_STARTED.md#networks)。

相关主题：[.NET](https://github.com/topics/dotnet) · [C#](https://github.com/topics/csharp) · [Diagnostics](https://github.com/topics/diagnostics) · [Windows Service](https://github.com/topics/windows-service) · [WebView2](https://github.com/topics/webview2) · [Networking](https://github.com/topics/networking)。

## 四项核心能力

### 1. AI 与远程诊断、标准服务生命周期

规范化日志、条件输出点、协作断点和管线跟踪，让业务执行过程可以按需定位。
AI 可通过登记对象的白名单读取数据、修改获准写入的成员，并在支持的调试配置中调用登记动作，监视和验证业务逻辑。

`RProcess`、`RThread`、`RTask` 接入 .NET Host 和 Windows Service 生命周期，统一登记、状态、协调退出及清理。CLI 停机与服务停机使用共同流程，真实报告超时和未退出单元。

CLI v3、结构化命令、远程账号授权及 [`iwesun-runtime-integration`](skills/iwesun-runtime-integration/SKILL.md) 技能支持 AI 独立执行观察、定位和验证。默认诊断安静；数据写入和调用须明确授权，Debug/Release 能力见用户指南。

### 2. 面向小型关系表的高性能数据结构

[`Iwesun.Runtime.Data`](modules/Data/docs/README.md) 提供 `RecordStore<TValue,TPrimaryKey>`：业务主键、稳定记录身份、多 Key、自适应索引、约束、过滤、合并、快照、视图、发布和 JSON 持久化。

适合程序内部频繁查询与更新的状态表、会话表和业务关系。性能须结合数据规模、索引和操作组合评估；规模工具随源码提供。

### 3. 接入统一 CLI 的 WebView2 控制

[`Iwesun.Runtime.WebView2`](modules/WebView2/docs/WEBVIEW2_RUNTIME_CAPABILITIES.md) 覆盖会话、DOM/XPath、输入、网络事件和页面证据访问。
固定 CDP 操作访问浏览器当前 DOM，辅助树用于批量读取，revision 和失效重试管理动态页面变化。AI 通过统一 CLI 联合观察网页行为和服务状态。

### 4. 并行化、多进多出的网络接口

[`Iwesun.Runtime.Networks`](modules/Networks/docs/README.md) 提供批量提交、并发执行和异步响应，统一请求关联、精确访问计划、重试、取消、超时和背压。

正式跟踪链为 `RequestId → AttemptId → BranchId → ResponseId`；多响应按协议窗口管理。UDP 结合长期槽、实际远端匹配及迟到隔离保障关联。TCP、UDP、Ping、HTTP/DoH、PTR、NBNS 的具体能力按端点合同区分。

## 开始使用

- 开发：Windows、.NET 10 SDK；WebView2 示例另需 WebView2 Runtime。MSI 构建依赖项目声明的 WiX 工具包。
- 消费：从 [Releases](https://github.com/iwesun32/Iwesun.Runtime/releases) 取得对应 Debug/Release DLL；跨仓库消费者引用发布 DLL。
- 源码构建：`dotnet build Iwesun.Runtime.slnx -c Release`。
- 模块开发：打开 `modules/<Domain>/Iwesun.Runtime.<Domain>.slnx`。
- 接入与验证：[快速开始](docs/IWESUN_RUNTIME_QUICK_START.md)、[SampleHost](modules/Diagnostics/samples/Iwesun.Runtime.SampleHost/Program.cs)、[CLI](docs/IWESUN_RUNTIME_CLI.md)。

核心公共库不要求每个消费者启用全部组件。Windows 专用能力应在具备对应系统条件时使用。

## 发布与支持

本仓库首次公开候选为 **Runtime 1.0.47-beta.1 / Networks 3.0.0-beta.6**。验证范围、已知限制和交付清单见[发布指南](docs/IWESUN_RUNTIME_1.0.47_BETA_RELEASE_GUIDE.md)。β 不代表所有实机网络、浏览器和长期压力场景均已通过。

公开交付只包含上述公共组件及 CLI、RemoteConsole、样例和工具。尚未完成的 Tables 工程不在公开交付内；已有 Data / RecordStore 正常提供。

## 授权与共同维护

本项目采用自定义 **Iwesun Runtime Noncommercial Source-Available License 1.0**：

- 非商业使用免费，须保留版权与许可声明。
- 商业使用须取得另行授权。
- 独立发行修改版须先申请认可，并按许可公开对应修改源码。
- 欢迎申请工作组，共同维护代码、文档、样例和测试。

这是源码开放许可，含商业和独立衍生发行限制，不是 OSI 开源许可证。平台内 Fork 和上游贡献按许可证规定办理。

日常维护由 iwesun 委托的 GPT/Dot 等 AI 助手协助处理，决定必须标明 AI 代理身份并留下依据；项目所有者为 iwesun，不代表 OpenAI 官方维护或背书。

[工作组申请](https://github.com/iwesun32/Iwesun.Runtime/issues/new?template=working-group.yml) · [分支申请](https://github.com/iwesun32/Iwesun.Runtime/issues/new?template=derivative-release.yml) · [治理规则](GOVERNANCE.md) · [漏洞报告](SECURITY.md)

Copyright © 2026 iwesun. 第三方组件遵循各自许可证，见 [NOTICE](NOTICE) 和 [第三方声明](THIRD_PARTY_NOTICES.md)。

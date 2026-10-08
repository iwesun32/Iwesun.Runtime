# iwesun Runtime 1.0.47-beta.1 / Networks 3.0.0-beta.6

## 中文

iwesun Runtime 首个公开 β 版本：面向 AI 自动调试与标准服务生命周期的 .NET 10 公共基础库。

- **诊断与服务管理**：结构化 CLI、条件输出、协作断点、白名单对象访问，以及受管进程/线程/任务的协调退出。
- **Data / RecordStore**：程序内小型关系表、多 Key、索引、快照及持久化。
- **WebView2**：统一 CLI 访问会话、DOM/XPath、输入和网络证据。
- **并行网络接口**：批量请求、异步多响应、四级关联与明确的访问约束。

提供 MSI、完整 ZIP、Networks 本地 NuGet 包、调试符号与 SHA-256；附 Debug/Release DLL、示例、AI 技能、中英文入门与发布说明。
Tables 不公开、不打包；独立 Web 开发工程不进入当前源码快照或运行载荷。没有上传 nuget.org，也没有修改现有宿主安装。

本地验证：Debug/Release 各通过 Networks 212、Data 101、WebView2 53 项测试；Diagnostics 分别通过 30/25 场景；旧 1.0.46 二进制宿主与包内一致性自检通过。
仍是 β：不宣称真实 WFP/下一跳、全部网页和长期高负载已全面验收。MSI 未附 Authenticode 签名，.NET/WebView2 运行环境需另行具备。

[中文使用入门](https://github.com/iwesun32/Iwesun.Runtime/blob/v1.0.47-beta.1/docs/GETTING_STARTED.md) · [发布指南](https://github.com/iwesun32/Iwesun.Runtime/blob/v1.0.47-beta.1/docs/IWESUN_RUNTIME_1.0.47_BETA_RELEASE_GUIDE.md) · [门禁清单](https://github.com/iwesun32/Iwesun.Runtime/blob/v1.0.47-beta.1/docs/IWESUN_RUNTIME_1.0.47_BETA_RELEASE_MANIFEST.md)

## English

The first public beta of iwesun Runtime: a .NET 10 foundation for AI-operated diagnostics and coordinated service lifecycle management.

- Structured CLI diagnostics, cooperative breakpoints, allowlisted object access and managed execution.
- Data/RecordStore for small in-process relational tables, indexes, snapshots and persistence.
- WebView2 session, DOM/XPath, input and network-evidence access.
- Parallel network endpoints with asynchronous responses, explicit access plans and four-level request correlation.

Assets include MSI, portable ZIP, a local-feed Networks NuGet package, symbols and checksums. Debug/Release DLLs, samples, an integration skill, and Chinese/English onboarding are included.
The unfinished Tables project is excluded. The separate Web development project is not in this snapshot or runtime payload. Nothing is uploaded to nuget.org.

Local validation passed 212 Networks, 101 Data and 53 WebView2 tests in each configuration; Diagnostics passed 30 Debug and 25 Release scenarios. Compatibility with a host compiled against Runtime 1.0.46 and staged-payload checks also passed.
Real WFP/next-hop, all browser environments and endurance workloads are not fully certified. The MSI is unsigned; appropriate .NET/WebView2 runtimes must be installed separately.

[English user guide](https://github.com/iwesun32/Iwesun.Runtime/blob/v1.0.47-beta.1/docs/en/USER_GUIDE.md) · [English release guide](https://github.com/iwesun32/Iwesun.Runtime/blob/v1.0.47-beta.1/docs/en/RELEASE_GUIDE.md)

## License / 许可

Noncommercial source-available, **not OSI open source**. Noncommercial use is free with attribution and license preservation; commercial use requires separate permission, and independent modified distributions require prior written approval. See the bundled LICENSE.
对外作者与品牌为 **iwesun**，GitHub 账号为 `iwesun32`。欢迎通过工作组申请或 PR 参与。AI 代理协助维护，不代表 OpenAI 官方背书。

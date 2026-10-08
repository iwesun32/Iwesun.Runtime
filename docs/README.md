# iwesun Runtime 文档

[English documentation](en/README.md) · [规范总纲与必读路线](SPECIFICATION.md) · [完整目录](DOCUMENTATION_CATALOG.md) · [独立文档包](https://github.com/iwesun32/Iwesun.Runtime/releases/tag/docs-v1.0.47-r1)

本索引面向公开源码。当前公开候选为 Runtime `1.0.47-beta.1` / Networks `3.0.0-beta.6`。

Runtime 是完整接入规范及其实现，不是零配置工具。首次接入先读[规范总纲](SPECIFICATION.md)，再按下列分类阅读正文。总目录包含公开技术正文、版本记录及配套规则；保留历史记录不表示旧接口仍受支持，也不表示实施计划已经验收。

## 使用与发布

- [项目介绍](../README.md)
- [使用入门（中文）](GETTING_STARTED.md) / [User guide (English)](en/USER_GUIDE.md)
- [快速接入](IWESUN_RUNTIME_QUICK_START.md)
- [用户指南](IWESUN_RUNTIME_USER_GUIDE.md)
- [当前发布指南](IWESUN_RUNTIME_1.0.47_BETA_RELEASE_GUIDE.md)
- [当前发布清单](IWESUN_RUNTIME_1.0.47_BETA_RELEASE_MANIFEST.md)
- [更新记录](RELEASE_NOTES.md)
- [生命周期与退出迁移](IWESUN_RUNTIME_1.0.43_SHUTDOWN_MIGRATION.md)

## 诊断、生命周期和 AI 接口

- [总体设计](IWESUN_RUNTIME_DESIGN.md)
- [诊断管线](RUNTIME_DIAGNOSTICS.md)
- [CLI](IWESUN_RUNTIME_CLI.md)
- [Windows Service](IWESUN_RUNTIME_WINDOWS_SERVICE.md)
- [远程授权](IWESUN_RUNTIME_REMOTE_ACCESS.md)
- [RemoteConsole](IWESUN_RUNTIME_REMOTE_CONSOLE.md)
- [AI 接入技能](../skills/iwesun-runtime-integration/SKILL.md)

## 启动、受管执行与状态合同

- [Runtime 注入与扩展接口](05-runtime-tooling/RUNTIME_INTEGRATION_GUIDE.md)
- [注入器标准化](05-runtime-tooling/INJECTOR_STANDARDIZATION.md)
- [进程与线程标准注入](05-runtime-tooling/PROCESS_THREAD_INJECTION.md)
- [线程与任务管理](05-runtime-tooling/THREAD_TASK_MANAGEMENT.md)
- [状态分类基础类型](05-runtime-tooling/STATE_CLASSIFICATION.md)
- [状态分类在线业务](05-runtime-tooling/STATE_CLASSIFICATION_ONLINE.md)
- [分层状态机](05-runtime-tooling/STATE_MACHINE_HIERARCHY.md)
- [进程守护与管道注册](05-runtime-tooling/GUARDIAN_PIPE_REGISTRY_DESIGN.md)
- [独立样板宿主](05-runtime-tooling/SAMPLE_HOST.md)
- [RuntimeRoot 数据总览](RUNTIME_ROOT_DATA_STRUCTURE.md) / [Data 与 RuntimeRoot](05-runtime-tooling/DATA_PROJECT_RUNTIME_ROOT.md)
- [发布与安装规范](05-runtime-tooling/RUNTIME_RELEASE_PACKAGING.md)
- [统一界面规范](UNIFIED_INTERFACE.md)

## 公共模块

- [Data / RecordStore](../modules/Data/docs/README.md)
- [Networks](../modules/Networks/docs/README.md)
- [WebView2 完整索引](../modules/WebView2/docs/README.md) / [能力](../modules/WebView2/docs/WEBVIEW2_RUNTIME_CAPABILITIES.md)
- [WebView2 DOM 与 CDP](../modules/WebView2/docs/CDP_DOM_NODE_TREE.md)
- [WebView2 示例](../modules/WebView2/docs/WEBVIEW2_SAMPLE_HOST.md)
- [WebView2 与 CLI 集成](IWESUN_RUNTIME_WEBVIEW2_CLI_INTEGRATION.md)

## 迁移、验证与版本记录

- [当前公开候选验证清单](IWESUN_RUNTIME_1.0.47_BETA_RELEASE_MANIFEST.md)
- [协调退出迁移](IWESUN_RUNTIME_1.0.43_SHUTDOWN_MIGRATION.md)
- [Networks API 迁移矩阵](../modules/Networks/docs/03-reference/NETWORKS_3_0_API_MIGRATION_MATRIX.md)
- [Networks 发布状态](../modules/Networks/docs/RELEASE_STATUS.md) / [WebView2 发布状态](../modules/WebView2/docs/WEBVIEW2_RELEASE_STATUS.md) / [Data 发布状态](../modules/Data/docs/RELEASE_STATUS.md)
- [WFP 实机验收](../modules/Networks/docs/02-endpoints/WFP_WINDOWS_VALIDATION_RUNBOOK.md)
- [Ping 终态验收](../modules/Networks/docs/03-reference/NETWORKS_3_0_PING_TERMINAL_BETA_TEST_GUIDE.md) / [UDP 数据面验收](../modules/Networks/docs/03-reference/NETWORKS_3_0_UDP_DATA_PLANE_BETA_TEST_GUIDE.md)
- [完整版本记录目录](DOCUMENTATION_CATALOG.md)列出历次发布指南、清单、RFC 与修复说明，不要求新用户按历史时间顺序通读。

## 共同维护

- [许可](../LICENSE) / [第三方声明](../THIRD_PARTY_NOTICES.md)
- [贡献流程](../CONTRIBUTING.md) / [工作组治理](../GOVERNANCE.md)
- [分支发行](../FORK_POLICY.md) / [安全报告](../SECURITY.md)
- [AI 维护运行说明](PUBLIC_MAINTENANCE.md)
- [推广计划与首发文案](PROMOTION_PLAN.md) / [Promotion plan (English)](en/PROMOTION.md)

较早版本文档保留原日期和验收背景；当前版本身份及验证结论以上述发布清单为准。

离线包保留原目录和全文，另附 HTML 导航及 SHA-256 清单。源码链接需配合同标签源码；[安装文档索引](IWESUN_RUNTIME_RELEASE_INDEX.md)只描述 MSI/运行包的安装布局。英文总纲与入门已提供，深入技术正文仍以中文为主。

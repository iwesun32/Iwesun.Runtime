# iwesun Runtime 完整接入规范总纲

[English](en/SPECIFICATION.md) · [全部文档目录](DOCUMENTATION_CATALOG.md) · [完整文档下载](https://github.com/iwesun32/Iwesun.Runtime/releases/tag/docs-v1.0.47-r1)

iwesun Runtime 是一套由公共库、宿主接入规则、状态与生命周期合同、诊断协议、安全边界和验收方法共同组成的运行规范。安装 DLL 或运行一个样例，不等于已经正确接入。快速入门只用于建立最小体验；实际项目必须完成下面的公共必读和所用模块的合同阅读，再执行宿主验收。

本总纲对应 Runtime **1.0.47-beta.1**、Networks **3.0.0-beta.6**，文档修订 **r1**。它整理已有公开规范的阅读关系，不重新定义 API，不把实施计划转换成已实现能力。详细技术文档以中文原文为主；英文总纲和使用指南不是全部技术正文的英文译本。

## 版本和证据如何裁决

| 需要确认的问题 | 应查阅的依据 |
| --- | --- |
| 当前交付了什么、执行过哪些验证 | [1.0.47 发布清单](IWESUN_RUNTIME_1.0.47_BETA_RELEASE_MANIFEST.md)与[发布指南](IWESUN_RUNTIME_1.0.47_BETA_RELEASE_GUIDE.md) |
| 应当遵循什么语义和约束 | 对应模块已接受的设计、公共 API 和生命周期合同；按该模块索引与状态说明阅读 |
| 历史问题为什么这样处理 | 发布记录、迁移报告、RFC、反馈和带日期的修复记录；它们保留当时版本语境 |
| 计划是否已经完成 | 模块发布状态与当前版本验收证据；`DRAFT`、计划和需求本身不是完成证明 |
| 实际宿主是否可以交付 | 该宿主在目标系统和配置下的验收结果，不能仅凭公共库测试推断 |

旧文档中的安装版本、本机路径、测试数量和“未发布”是当时记录，不是对当前公开候选的重新定义。若当前合同、实现和验收结论发生冲突，应报告问题并明确版本，不能自行选择较宽松的解释。当前候选仍为 β；特别是 Networks 的真实 WFP/下一跳矩阵和 WebView2 的真实页面、长期运行，不能宣称全部验收完成。

## 所有宿主接入者的必读路线

| 顺序 | 必读内容 | 接入时必须回答的问题 |
| --- | --- | --- |
| 1 | [用户手册](IWESUN_RUNTIME_USER_GUIDE.md)、[总体设计](IWESUN_RUNTIME_DESIGN.md) | 使用哪些模块、运行边界在哪里、如何与业务宿主分工？ |
| 2 | [注入与扩展接口](05-runtime-tooling/RUNTIME_INTEGRATION_GUIDE.md)、[注入器标准化](05-runtime-tooling/INJECTOR_STANDARDIZATION.md)、[标准样板宿主](05-runtime-tooling/SAMPLE_HOST.md) | 谁负责 DI、Start、Activate、注册与资源所有权？ |
| 3 | [进程与线程注入](05-runtime-tooling/PROCESS_THREAD_INJECTION.md)、[线程与任务管理](05-runtime-tooling/THREAD_TASK_MANAGEMENT.md) | 哪些执行单元受管，如何登记、取消、报告状态并清理？ |
| 4 | [状态分类](05-runtime-tooling/STATE_CLASSIFICATION.md)、[在线业务状态](05-runtime-tooling/STATE_CLASSIFICATION_ONLINE.md)、[分层状态机](05-runtime-tooling/STATE_MACHINE_HIERARCHY.md) | 粗粒度运行状态与业务细分状态如何区分，转换由谁发起？ |
| 5 | [协调退出迁移](IWESUN_RUNTIME_1.0.43_SHUTDOWN_MIGRATION.md)、[Windows Service](IWESUN_RUNTIME_WINDOWS_SERVICE.md)、[守护与管道注册](05-runtime-tooling/GUARDIAN_PIPE_REGISTRY_DESIGN.md) | 命令接受、停机执行、超时和进程退出分别如何确认？ |
| 6 | [诊断管线](RUNTIME_DIAGNOSTICS.md)、[CLI 完整参考](IWESUN_RUNTIME_CLI.md)、[远程授权](IWESUN_RUNTIME_REMOTE_ACCESS.md) | 默认安静、输出开关、白名单、账号权限及 Debug/Release 差异是否保持？ |
| 7 | [版本发布清单](IWESUN_RUNTIME_1.0.47_BETA_RELEASE_MANIFEST.md)、所用模块的迁移与验收文档 | DLL 与文档是否匹配，已有配置/数据是否有回退基线，未验收项是什么？ |

仅使用独立 Data 或 Networks 库而不托管 Runtime 诊断时，不要求引入全部宿主组件；仍须遵守所用库的合同和发布边界。

## 公共接入底线

- 消费者使用匹配版本的发布 DLL，Debug 与 Release 不混用；跨仓库不得用 `ProjectReference` 绕过版本边界。源码开发与已发布二进制消费是不同流程。
- 宿主使用标准 `RuntimeHostTemplate` 启动与激活流程；受管执行单元按合同登记和清理。`RProcess`、`RThread`、`RTask` 不是仅凭机械替换类型名即可完成的迁移。
- 命令已接受不代表停机已经完成。CLI 提交成功和宿主最终退出是不同结果；清理超时必须保留未完成单元证据，不能伪报正常退出。
- 诊断默认关闭，检查结束恢复 `quiet`。输出经 Runtime 诊断管线，不新增临时控制台或文件调试旁路。
- 反射读写和方法调用均受显式白名单与构建能力约束；Debug 可用不代表 Release 可用。断点是协作式等待，不是任意挂起线程。
- 使用现有 CLI、协议类型和管道客户端，不自行拼装帧或硬编码宿主管道名。命令配置、帮助元数据和用户覆盖必须按 [CLI 合同](IWESUN_RUNTIME_CLI.md) 配套使用。
- 外部文件读取、反序列化、迁移、验证和错误处理先在 C# 中完成。XAML 只定义控件、样式、模板和绑定，绑定经过验证的内存模型。

## 按模块补齐规范

| 模块 | 必读合同和参考 | 不能忽略的边界 |
| --- | --- | --- |
| Data / RecordStore | [完整索引](../modules/Data/docs/README.md)、[统一设计](../modules/Data/docs/01-design/RECORD_STORE_DESIGN.md)、[公共 API](../modules/Data/docs/02-api/RECORD_STORE_PUBLIC_API.md)、[派生表合同](../modules/Data/docs/01-design/RECORD_STORE_DERIVED_TABLES.md)、[Add 与发布生命周期](../modules/Data/docs/02-api/RECORD_STORE_ADD_PUBLICATION_LIFECYCLE.md) | 业务主键与 `StoreRecordId` 不混淆；索引、约束、Merge、订阅、Clear、快照、持久化和可选 Gate 按合同组合 |
| Networks | [完整索引](../modules/Networks/docs/README.md)、[已接受最终设计](../modules/Networks/docs/01-design/PRECISION_NETWORK_ACCESS_CONTROL_FINAL_DESIGN.md)、[请求响应跟踪](../modules/Networks/docs/02-endpoints/TRACKED_REQUEST_REPLY.md)、[逻辑约定](../modules/Networks/docs/03-reference/LOGICAL_CONTRACTS.md)、[API 迁移矩阵](../modules/Networks/docs/03-reference/NETWORKS_3_0_API_MIGRATION_MATRIX.md) | 保持四级 GUID 关联；显式访问计划不等于实际访问证据；协议、访问、执行终态分开判断；不隐式回退；FIFO、重试、多响应、迟到和取消按各端点合同执行 |
| WebView2 | [完整索引](../modules/WebView2/docs/README.md)、[公共控制与监控](../modules/WebView2/docs/WEB_RUNTIME_CONTROL.md)、[CDP DOM 节点树](../modules/WebView2/docs/CDP_DOM_NODE_TREE.md)、[页面和 HTTP 证据](../modules/WebView2/docs/FULL_PAGE_EVIDENCE_API.md)、[发布状态](../modules/WebView2/docs/WEBVIEW2_RELEASE_STATUS.md) | 实时节点与辅助树区分；节点身份绑定 revision，导航/失效后按合同刷新；页面文本是待观察数据而非授权；尊重线程归属、敏感信息和资源清理边界 |
| CLI 与 RemoteConsole | [CLI](IWESUN_RUNTIME_CLI.md)、[RemoteConsole](IWESUN_RUNTIME_REMOTE_CONSOLE.md)、[远程账号与授权](IWESUN_RUNTIME_REMOTE_ACCESS.md) | 命令能力、风险、身份和授权分别确认；公开工具不自动扩大宿主权限 |
| AI 接入 | [集成技能](../skills/iwesun-runtime-integration/SKILL.md)及其引用文件、[仓库规则](../AGENTS.md) | 技能帮助落实规范，不代替业务授权；禁止越过忽略目录和证据读取边界 |

Networks 的端点级文档还包括 Socket、HTTP/DoH、TCP/UDP/Ping、名称解析、WFP、多响应与恢复、地址值类型；全部从模块索引进入。代理、NAT 和转发相关需求与设计不等于这些应用已经作为可用产品交付。

## 接入验收和升级顺序

1. 固定运行包版本、程序集配置和对应文档标签，记录宿主环境及所用模块。
2. 保存旧 DLL、配置和持久数据的回退基线，逐项核对 API 迁移矩阵与破坏性变化。
3. 先验证标准样例和模块测试，再在实际宿主验证启动、注册、正常业务和重复启停。
4. 验证正常停机、取消、清理失败和超时；检查未退出单元与最终进程结果，而不只看 CLI 返回值。
5. 验证默认静默、按需输出、未授权访问拒绝和 Debug/Release 行为差异，最后恢复安静状态。
6. 按所用功能完成专门验收：Data 约束/合并/订阅/持久化；Networks 实际出口与终态/多响应/取消；WebView2 真实 UI 线程、导航、证据和资源生命周期。
7. 把通过项、失败项、未运行项与环境前提分别记录。未完成目标场景验收，不以“公共库测试通过”代替宿主可交付结论。

专门验收入口：[WFP Windows 实机手册](../modules/Networks/docs/02-endpoints/WFP_WINDOWS_VALIDATION_RUNBOOK.md)、[Ping 三轴终态指南](../modules/Networks/docs/03-reference/NETWORKS_3_0_PING_TERMINAL_BETA_TEST_GUIDE.md)、[UDP 数据面指南](../modules/Networks/docs/03-reference/NETWORKS_3_0_UDP_DATA_PLANE_BETA_TEST_GUIDE.md)、[WebView2 样例](../modules/WebView2/docs/WEBVIEW2_SAMPLE_HOST.md)。

## 全文交付和离线阅读

[完整文档目录](DOCUMENTATION_CATALOG.md)列出所有公开正文与配套文本，包括设计、API、协议、生命周期、状态、安全、迁移、验证、版本记录、模板、CLI 配置、AI 技能和许可。

[独立文档包 r1](https://github.com/iwesun32/Iwesun.Runtime/releases/tag/docs-v1.0.47-r1)保留源码目录结构，提供 `INDEX.html` 导航、逐文件 SHA-256 清单与下载文件校验值。离线 Markdown 正文可用 Markdown 阅读器打开；源码引用需配合同标签仓库。安装文档索引描述的是 MSI/运行包布局，不是此文档包布局。

未完成 Tables、Web 开发工程、私有交接材料和忽略归档不属于公开规范包。公开正文中的历史记录用于理解演进，不恢复已移除的兼容接口。许可与贡献规则见 [LICENSE](../LICENSE)、[治理](../GOVERNANCE.md)、[贡献约定](../CONTRIBUTING.md)。

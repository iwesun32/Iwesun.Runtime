# WebView2 规范与接口文档索引

[Runtime 规范总纲](../../../docs/SPECIFICATION.md) · [全部文档](../../../docs/DOCUMENTATION_CATALOG.md)

本模块公开控制、监控、DOM 访问和证据采集合同。接入时先阅读公共控制模型和当前发布状态，再按使用能力阅读接口文档；样例不能代替线程、节点有效期、安全和资源生命周期约束。

## 必读与接口

| 文档 | 阅读用途 |
| --- | --- |
| [公共控制与监控](WEB_RUNTIME_CONTROL.md) | 宿主、会话、程序和公共控制 API |
| [能力与边界](WEBVIEW2_RUNTIME_CAPABILITIES.md) | 能力分类、约束及计划背景，结合发布状态判断交付情况 |
| [发布状态](WEBVIEW2_RELEASE_STATUS.md) | 当前候选、历史修正和仍需真实宿主验收的范围 |
| [CDP DOM 节点树](CDP_DOM_NODE_TREE.md) | 实时节点、辅助树、revision、导航失效与刷新 |
| [类型化 DOM 查询](DOM_QUERY_INDEX_API.md) | 查询索引及强类型公共面 |
| [完整 DOM 真快照](DOM_SNAPSHOT_API.md) | 快照结构、采集与使用约束 |
| [页面和 HTTP 证据](FULL_PAGE_EVIDENCE_API.md) | 页面证据、资源关联与敏感信息处理 |
| [数据流监视记录器](DATA_STREAM_MONITOR_RECORDER.md) | 条件、记录与资源生命周期 |
| [样例宿主](WEBVIEW2_SAMPLE_HOST.md) | 示例配置、运行与验证入口 |

## 设计演进与跨模块接入

- [受控脚本反射说明与任务计划](SCRIPT_REFLECTION_PLAN.md)：设计及任务背景，不能仅凭计划标题认定全部已验收。
- [JSON、管道与 CLI 重规划](WEBVIEW2_JSON_PIPE_CLI_PLAN.md)：协议演进背景，当前命令以 [CLI 参考](../../../docs/IWESUN_RUNTIME_CLI.md)为准。
- [WebView2 与 CLI 集成](../../../docs/IWESUN_RUNTIME_WEBVIEW2_CLI_INTEGRATION.md)：跨模块接线与发布约束。

当前对应 Runtime `1.0.47-beta.1`；旧记录中的源码引用路径和“未发布”保留原历史语境，公开消费者应使用匹配版本的发布 DLL。完整真实页面、UI 线程及长期运行验收不能由合同测试直接替代。

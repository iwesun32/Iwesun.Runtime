# Iwesun Runtime 安装文档索引

本索引用于完整安装包；源码仓库索引见`docs/README.md`。

## 快速入口

- [速查手册](IWESUN_RUNTIME_QUICK_START.md)
- [用户手册](IWESUN_RUNTIME_USER_GUIDE.md)
- [CLI 手册](IWESUN_RUNTIME_CLI.md)
- [Windows Service 手册](IWESUN_RUNTIME_WINDOWS_SERVICE.md)
- [远程访问手册](IWESUN_RUNTIME_REMOTE_ACCESS.md)
- [远程控制台手册](IWESUN_RUNTIME_REMOTE_CONSOLE.md)
- [发布更新记录](RELEASE_NOTES.md)
- [1.0.47-beta.1 公开β发布说明](IWESUN_RUNTIME_1.0.47_BETA_RELEASE_GUIDE.md)
- [1.0.47-beta.1 发布清单](IWESUN_RUNTIME_1.0.47_BETA_RELEASE_MANIFEST.md)
- [1.0.43 协调退出迁移说明](IWESUN_RUNTIME_1.0.43_SHUTDOWN_MIGRATION.md)

## RecordStore

- [公共 API](Iwesun.Runtime.Data/02-api/RECORD_STORE_PUBLIC_API.md)
- [统一设计](Iwesun.Runtime.Data/01-design/RECORD_STORE_DESIGN.md)
- [发布状态](Iwesun.Runtime.Data/RELEASE_STATUS.md)
- [源码映射](Iwesun.Runtime.Data/03-reference/SOURCE_MAP.md)

> RecordStore 是当前唯一活动 RecordStore 实现，统一由 `Iwesun.Runtime.Data.dll` / `Iwesun.Runtime.Data` 提供；旧 DList、RecordStore V1 和 `Iwesun.Data.dll` 不进入安装包。

## Iwesun.Runtime.Networks 3.0.0-beta.6（公开β候选）

- [Networks 文档索引](Iwesun.Runtime.Networks/README.md)
- [发布状态](Iwesun.Runtime.Networks/docs/RELEASE_STATUS.md)
- [3.0 最终设计](Iwesun.Runtime.Networks/docs/01-design/PRECISION_NETWORK_ACCESS_CONTROL_FINAL_DESIGN.md)
- [3.0 迁移矩阵](Iwesun.Runtime.Networks/docs/03-reference/NETWORKS_3_0_API_MIGRATION_MATRIX.md)
- [beta.5升级迁移报告](Iwesun.Runtime.Networks/docs/03-reference/NETWORKS_3_0_BETA5_UPGRADE_MIGRATION_REPORT.md)
- [四级GUID请求/响应端点](Iwesun.Runtime.Networks/docs/02-endpoints/TRACKED_REQUEST_REPLY.md)
- [IP与MAC数值类型](Iwesun.Runtime.Networks/docs/02-endpoints/ADDRESS_VALUE_TYPES.md)
- [ARP与IPv6邻居强类型记录](Iwesun.Runtime.Networks/docs/02-endpoints/LOCAL_TABLES.md)
- [DoH 精确路由](Iwesun.Runtime.Networks/docs/02-endpoints/DOH_PRECISION_ROUTING_GUIDE.md)
- [可编译示例](../samples/source/Iwesun.Runtime.Networks.Examples/)

> 当前状态为`GENERAL_BETA_READY_FORMAL_BLOCKED`。三轴终态、精确接口Ping与UDP长期数据面已完成本地和消费端门禁；
> 当前允许生成普通β候选；统一门禁完成后才允许内部安装，剩余M11真实环境矩阵继续阻止正式发布。

## WebView2 与接入资料

- [WebView2 控制手册](WEB_RUNTIME_CONTROL.md)
- [DOM 真快照 API](DOM_SNAPSHOT_API.md)
- [数据流记录器](DATA_STREAM_MONITOR_RECORDER.md)
- [完整页面与 HTTP 输入证据 API](FULL_PAGE_EVIDENCE_API.md)
- [WebView2 发布状态](WEBVIEW2_RELEASE_STATUS.md)
- [WebView2 SampleHost](WEBVIEW2_SAMPLE_HOST.md)
- [Runtime 接入技能](../skills/iwesun-runtime-integration/SKILL.md)
- [示例与模板](../samples/)

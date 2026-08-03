# Iwesun.Runtime.Networks 1.3.0发布说明

> **状态**: READY_FOR_DISTRIBUTION（本地正式包） | **日期**: 2026-07-20

1.3.0把剩余网络协议端点纳入统一V2请求跟踪模型：

- 公共`Guid RequestId`是唯一跟踪身份，`TKey`仅透传用户业务键并提供只读Pending查询；调用方可显式提供
  `Guid`，也可使用末尾`Guid? requestId = null`的兼容构造重载在请求创建时自动生成；
- 新增`TrackedUdpDatagramEndpointV2`，内部以独立socket/NAT端口完成报文关联；
- 新增`TrackedDnsReverseLookupEndpointV2`，使用原生PTR wire、显式解析器、事务号校验及TCP回退；
- 新增`TrackedNetBiosNameEndpointV2`，使用原生NBNS UDP/137，不再依赖`nbtstat`；
- PowerShell端点继续作为不可拆分的命令执行端点，但请求/结果补齐`CorrelationId`并保留完整原请求；
- DDNS Snap活动名称扫描和连接探测已迁移到新端点。

Networks Debug/Release各26/26测试、Aether Service与DDNS Snap Release构建、Runtime 1.0.32候选Debug/Release构建、
23/23 Runtime Release功能场景和staging自检均已通过。程序集级检查确认三个旧公开类型不存在。

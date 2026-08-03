# WFP 精确下一跳执行合同

## 1. 支持边界

Networks 3.0 在 Windows 上使用 `FwpmConnectionPolicyAdd0` 实现 Direct 路径的非 OnLink
`ExactNextHop`。当前后端只覆盖 TCP 与 UDP；Ping/ICMP 没有可由库独占分配的本地端口匹配维度，仍在发送前明确拒绝，不能把路由查询推断标记为实际强制。

平台必须同时满足：

- `fwpuclnt.dll` 存在所需入口；
- 当前进程具有安装连接策略所需权限；
- 已解析 SourceAddress、接口 LUID 与 NextHop 均有效；
- Socket 已绑定非零本地端口。

不满足时返回固定能力或策略错误，不回退 Automatic，不修改系统全局路由表。

## 2. 隔离键

每个策略使用“应用程序标识 + IP协议 + 本地地址/端口 + 远端地址/端口”完整匹配条件。Socket 在安装策略前绑定源地址和独占本地端口。同一进程、同一目标的并发请求因此仍具有不同匹配键；托管隔离层还会在进入 WFP 前拒绝活动键碰撞。

用户业务 `TKey` 和四级 GUID 身份链都不承担 WFP 匹配职责，`PolicyId` 是策略实例身份，BranchId 用于审计归属。

## 3. 策略值与证据

每个成功加入的策略同时设置 `SOURCE_ADDRESS`、`NEXT_HOP_INTERFACE`（接口 LUID）和 `NEXT_HOP`。只有 WFP 加入成功后，Interface 与 NextHop 才使用 `PolicyEnforced`；仅通过路由查询获得的值继续使用 `Inferred`。

## 4. 生命周期与崩溃清理

每个活动连接拥有独立的 `FWPM_SESSION_FLAG_DYNAMIC` 引擎会话：先绑定 Socket，再打开动态会话并加入策略，连接流释放时关闭会话并删除策略。正常完成、取消、超时、连接异常和 HTTP 流回收都经过同一 Lease 释放路径。进程异常退出时由 Windows 关闭所属动态会话；实现不创建持久 Provider、Filter 或全局路由项。

关闭动态会话失败返回 `wfp-policy-cleanup-failed`，不能把清理不确定的结果声明为成功。

## 5. HTTP / DoH 连接复用

请求级 WFP 策略不得进入共享 HTTP 连接池。HTTP 与 DoH 使用 ExactNextHop 时必须选择 `NoReuseRequestPolicy`；其他复用策略在发送前返回 `wfp-policy-connection-reuse-unsupported`。策略 Lease 由连接 Stream 持有，直到 Stream 实际释放。

## 6. 当前验收状态

托管固定测试已覆盖匹配键碰撞、同目标不同网关的独立本地端口、原生 Acquire/清理异常、TCP 成功释放、UDP 取消释放和 `PolicyEnforced` 证据。HTTP 与 DoH 均已通过非复用连接、TLS/SNI、证书固定及 Lease 回收测试；可复用请求在连接前拒绝。PTR的UDP查询与TCP回退分别安装策略，NBNS多响应窗口在同一策略生命周期内收集，HTTP CONNECT适配器的客户端段也使用同一强制合同。x64 原生结构布局已按当前 Windows SDK 固定验证。

当前 Debug/Release 全量均为113通过、0失败、0跳过。真实 WFP 加入、管理员/非管理员身份及进程崩溃后的系统残留检查仍属于 M11 Windows 真实网络验收；完成前不得把当前工作区标记为 3.0 发布候选。

真实验收使用独立的`Iwesun.Runtime.Networks.WfpValidation`项目，具体参数和停线判据见[WFP Windows真实网络验收手册](WFP_WINDOWS_VALIDATION_RUNBOOK.md)。当前开发进程的只读能力结果为`platform-permission-missing`，因此没有执行任何真实策略写入。

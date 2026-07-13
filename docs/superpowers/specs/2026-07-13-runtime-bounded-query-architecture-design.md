# Runtime 有界查询架构设计（下一阶段）

> 状态：规划，未实施。必须另行批准后进入代码。

## 问题

当前目标先构造完整 Snapshot，再完整 JSON 序列化和写管道。客户端长度限制只能保护客户端分配，不能阻止服务端遍历、内存、CPU 和管道占用。

## 统一查询契约

下一阶段引入 RuntimeQueryOptions：Path、Depth、Offset、Limit、Fields、MaxBytes、ContinuationToken。所有 Diagnostics、WebView2 和代理查询目标在枚举和投影阶段消费预算，不允许完整生成后再裁剪。

响应返回 Items/Fields、Total、Returned、Truncated、ContinuationToken、SnapshotVersion、GeneratedAt 和 InstanceId。排序键、过滤条件、快照版本和游标共同进入 token；版本变化时返回明确的 continuation invalid 状态。

## 业务适配器

大型业务对象必须注册摘要、子节点目录和分页读取委托。无法提供稳定分页的方法只能显式声明 FullSnapshot，并受较小硬预算约束；Runtime 不对未知对象做无限递归反射。

## 协议方案

1. 单帧分页：兼容最好，作为默认方案。
2. 多帧分块：适合大批量导出，需要新增 frame sequence 和重连恢复。
3. 流式 JSON：解析和错误恢复复杂，不作为第一阶段方案。

推荐先实施单帧分页；只有有明确导出需求再设计多帧。任何截断都必须返回合法 JSON 和继续令牌。

## 原子状态

业务状态由宿主提供一次性快照委托或版本化读取接口。Runtime 不分别反射读取多个变化属性后宣称原子一致。顶层与阶段数据必须共享 SnapshotVersion。

## 发布门槛

需要基准测试服务端分配、序列化前枚举数、响应字节数和管道占用时间；仅验证 CLI 少打印内容不算通过。WebView2 与 Diagnostics 必须在同一全量版本同步发布。

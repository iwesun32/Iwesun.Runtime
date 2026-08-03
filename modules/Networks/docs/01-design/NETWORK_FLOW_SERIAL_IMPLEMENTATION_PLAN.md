# Networks内部流水号与UDP数据面实施计划

> **状态**：F0_F5_IMPLEMENTED_F6_VALIDATED  
> **最后更新**：2026-07-23  
> **权威设计**：[内部流水号与转发数据面技术设计](NETWORK_FLOW_SERIAL_AND_FORWARDING_DATA_PLANE_DESIGN.md)  
> **发布状态**：`GENERAL_BETA_READY_FORMAL_BLOCKED`

## 一、实施目标

本计划把现有“每个UDP Branch创建并销毁一个Socket”的执行路径迁移为：

```text
NetworkFlowSerial
  → DataPlane
  → 长期Socket池 / 端口租约
  → 实际远端严格匹配
  → DispatchEvidence
  → 迟到隔离和复用
```

同时冻结System Socket、WFP、VirtualAdapter和ExternalForwarder共同实现的数据面接口，使后续DNS代理、NAT/IP路由、
HTTP代理、Socket代理和三层数据面不需要修改上层Flow合同。

## 二、批次与门禁

### F0：合同冻结

- 活动需求登记流水号、UDP匹配和数据面抽象；
- 技术设计与本实施计划进入Networks文档索引；
- 保持私有beta发布阻断。

门禁：文档链接、状态和术语一致。

### F1：流水号基础

- 增加`NetworkFlowSerial`强类型值；
- 增加非泛型静态`NetworkFlowSerialAllocator`；
- 实现非零原子分配、活动登记、状态转换、迟到隔离、释放和回绕碰撞跳过；
- 增加并发唯一性、零值、隔离和回绕模拟测试。

门禁：所有活动流水号在同一运行跟踪域内不重复，释放前不能复用。

### F2：数据面合同

- 增加`INetworkDataPlane`、Datagram、Stream和Packet分层能力；
- 增加数据面身份、能力版本、运行Generation和状态；
- 增加UDP DispatchEvidence及执行结果中的Flow/Socket Generation；
- 当前Socket执行器通过接口暴露，不向业务端点泄漏Socket。

门禁：System Socket实现可替换，模拟数据面能够驱动同一UDP端点合同。

### F3：System Socket UDP池

- 按稳定访问计划和远端端口隔离池；
- 预绑定Socket并立即取得实际本地端点；
- 每个槽维护SocketGeneration和常驻接收循环；
- 无Token流同槽单活动租约；并发时增加槽和本地端口；
- 严格核对实际远端IP/端口；错误来源继续等待；
- 终态后槽和Flow进入迟到隔离，再允许复用。

门禁：第二次顺序请求复用同一SocketGeneration和本地端口；并发请求使用不同端口；错误远端不能串流。

### F4：UDP端点迁移

- `NetworkUdpDatagramEndpoint<TKey>`依赖`INetworkDatagramDataPlane`；
- 保留公开Request/Response主合同，新增发送证据事件；
- Timeout、Cancelled、TransportFailed与AccessCompliance保持三轴事实；
- Stop/Dispose排空并关闭数据面，不遗留活动Flow。

门禁：现有调用方继续编译，新增端口租约和Flow证据可观察。

### F5：代理与包数据面扩展合同

- 冻结DNS双ID映射、NAT映射、TCP双腿、HTTP Stream、透明转发和Packet Conntrack请求/结果类型；
- 每项能力独立声明，不提供伪实现；
- Future VirtualAdapter/ExternalForwarder使用相同Flow流水号和三轴终态。

门禁：能力查询能区分已实现、未实现和需要平台后端的功能；未实现组合发送前拒绝。

### F6：验证与治理

- Networks Debug/Release全量测试；
- Networks领域Debug/Release构建；
- 检查公开面无V2/V3名称和旧Route合同；
- 重读活动需求并更新实施状态；
- 不恢复beta发布状态，三轴Ping更正仍按独立计划完成。

## 三、明确不在本批伪完成的内容

- 不安装或创建虚拟网卡；
- 不修改Windows系统路由或启动服务；
- 不把接口声明当成DNS、NAT、HTTP代理已实现；
- 不迁移低频PTR和NBNS到通用UDP池；
- 不以流水号代替端口、远端端点、协议Token或五元组匹配。

## 四、停线条件

出现以下任一情况立即停止当前批次并保持发布阻断：

- 错误来源UDP包完成了其他Flow；
- 流水号在活动或隔离窗口内重复；
- Socket重建后旧Generation响应进入新Flow；
- Stop/Dispose后仍存在活动接收循环或端口租约；
- 新抽象迫使消费者改回源码ProjectReference或引入业务依赖；
- Debug/Release行为或公共类型不一致。

## 五、当前实施记录

- F0：技术设计、实施计划、索引和活动需求已登记；
- F1：流水号发号、活动登记、身份/传输绑定、隔离、释放和回绕测试已完成；
- F2：DataPlane身份、能力、Datagram/Stream/Packet接口和DispatchEvidence已完成；
- F3：System Socket长期UDP池、端口租约、Generation、常驻接收、并发槽和迟到隔离已完成；
- F4：UDP端点已迁移，保留原Request/Response主合同并新增发送证据和计数快照；
- F5：DNS、NAT、Socket、HTTP和Packet扩展合同已冻结，尚未登记为已实现能力；
- F6：发布前代码复核新增隔离发布、直接数据面校验、观察者隔离、容量拒绝和池指标守卫；当前Debug/Release
  全量均为121/121，领域解决方案Debug/Release均0警告0错误。

本计划的基础数据面批次已经完成，但Networks整体发布仍受三轴Ping更正、全协议终态审计和真实网络门禁阻断。

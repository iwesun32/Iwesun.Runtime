# Runtime 统一关机协调设计

## 目标

将 Runtime 的安全退出统一为“全局生命周期状态 + 单元 FIFO 命令”的双通道协议。主控负责发布准备关机、逐个发送停止命令、维护倒计时并核销登记表；每个进程、线程、任务的 Guardian 负责检测退出请求、执行清理、更新自身状态、注销登记并正常结束。

正常完成返回退出码 `0`；超过关机期限返回系统通用超时退出码 `124`。

## 设计原则

1. 全局生命周期状态是关机意图的最终权威来源。
2. FIFO 是即时唤醒和定向控制通道，不是唯一退出信号。
3. Guardian 必须同时支持主动查询总状态和消费本单元 FIFO 命令。
4. 主控以进程、线程、任务登记表全部清空作为正常退出依据。
5. 清理、状态更新和注销必须幂等，重复 Stop 不得重复释放资源或抛出破坏性异常。
6. 阶段性业务状态继续使用单元状态 DLIST，可登记扫描、同步、持久化等细分阶段。
7. Release 完整保留关机协调能力；它与 Debug-only 断点子系统无关。

## 组件边界

### RuntimeShutdownCoordinator

主控关机编排器，职责如下：

- 将全局状态切换到 `StopRequested`。
- 读取进程、线程、任务登记快照。
- 向每个仍登记的 `UnitId` 写入 FIFO Stop 命令。
- 在倒计时期间持续检查登记表、最后心跳和单元状态。
- 所有登记为空时将全局状态设为 `StopCompleted`，返回 `0`。
- 倒计时结束仍有登记时将全局状态设为 `StopTimeout`，生成超时快照并返回 `124`。

协调器不直接释放业务资源，也不替 Guardian 注销单元。

### RuntimeUnitGuardian

进程、线程和任务使用同一 Guardian 协议，具体包装器可以组合该能力。Guardian 职责如下：

- 定期读取全局生命周期状态。
- 消费按 `UnitId` 路由的 FIFO 简单枚举命令。
- 将全局 Stop 或 FIFO Stop 归一为一次退出请求。
- 将单元状态切换到 `StopRequested`，随后进入 `StopDraining`。
- 调用已登记的业务清理处理器。
- 清理成功后设置 `StopCompleted`；清理失败时记录失败信息和结束状态。
- 从 RuntimeManagedRegistry、执行登记表和反射目标中注销自身。
- 返回正常结束结果并退出执行体。

### RuntimeManagedRegistry 与 RuntimeRoot

- RuntimeManagedRegistry 继续作为活动单元权威登记表。
- RuntimeRoot 将进程、线程、任务、全局生命周期、单元状态历史投影到现有 DLIST 表。
- 业务程序可以为单元注册额外阶段性状态；这些状态属于单元清理过程的可观测轨迹，不改变主控核销条件。
- 主控检查活动登记，不依赖只读投影刷新是否及时。

## 状态模型

全局和单元使用相同的关机锚点：

```text
Working
  -> StopRequested
  -> StopDraining
  -> StopCompleted
```

主控超时路径：

```text
StopRequested -> StopTimeout
```

业务细分状态以 DLIST 追加，例如：

```text
StopDraining
  - scan.canceling
  - cache.flushing
  - state.persisting
  - connection.closing
```

细分状态不代替 `StopCompleted`。只有 Guardian 完成注销后，该单元才被视为退出。

## FIFO 命令

关机协议使用简单枚举命令，至少包含：

- `Stop`：请求单元执行正常清理并退出。
- `Wake`：唤醒阻塞循环以便立即重新检查全局状态或命令。

命令帧必须包含目标 `UnitId`、命令枚举、发布时间和关联关机请求 ID。重复 Stop 按幂等请求处理。未知命令被忽略并记录诊断事件，不得导致 Guardian 崩溃。

## 关机时序

1. CLI、UI、系统服务控制器或业务代码调用统一关机入口。
2. 协调器创建关机请求 ID、截止时间和初始登记快照。
3. 协调器发布全局 `StopRequested`。
4. 协调器枚举活动单元，逐个发送 Stop；必要时附加 Wake。
5. Guardian 通过总状态轮询或 FIFO 收到请求，进入幂等清理流程。
6. Guardian 更新状态、执行清理、记录结果、注销自身并退出。
7. 协调器持续核对三类登记表；新登记在 StopRequested 后应被拒绝，或立即进入停止流程。
8. 登记全部清空时协调器返回 `0`，宿主再调用 `StopApplication()` 结束 Generic Host。
9. 截止时间到达仍有活动登记时，协调器生成残留快照并返回 `124`。

## 并发和幂等

- 关机入口使用单飞任务：并发调用共享同一关机结果。
- 每个 Guardian 使用原子状态保证清理处理器最多执行一次。
- Stop 命令可以重复发送，FIFO 消费与全局轮询可以同时命中。
- 注销操作采用 Try/幂等语义；已经注销视为成功。
- `StopRequested` 发布后，Registry 的新注册必须拒绝，或由协调器立即补发 Stop。
- 主控轮询使用条件等待和短周期唤醒，不使用无界阻塞。

## 错误与超时

- 单元清理异常被捕获并写入单元退出结果，不得阻止注销路径。
- 无法注销的单元保留在登记表中，最终由主控超时报告识别。
- 超时报告至少包含 `UnitId`、单元类型、当前状态、阶段状态、最后心跳、清理错误和已等待时间。
- 超时返回 `124`，不伪装为正常退出。
- 是否在返回 `124` 后强制终止残留 OS 进程属于宿主策略，不由 RuntimeShutdownCoordinator 默认执行。

## CLI 与宿主集成

- `safe-shutdown` 改为调用 RuntimeShutdownCoordinator。
- CLI 收到的是关机请求受理结果；目标宿主的最终退出码才是完成证据。
- `lifecycle.shutdown [countdownMs]` 使用同一协调器，不维护第二套流程。
- `lifecycle.status` 返回请求 ID、全局状态、截止时间、剩余时间、活动登记计数和残留单元。
- 协调器返回 `0` 后，宿主调用 `IHostApplicationLifetime.StopApplication()`。
- 不再以静态 shutdown 事件或单一委托链作为唯一退出机制。

## 验收测试

### 正常退出

- 启动包含真实 RProcess、RThread、RTask 的宿主。
- 发布 StopRequested，并验证每个单元同时可通过总状态轮询和 FIFO Stop 响应。
- 验证清理处理器执行一次、状态经过 StopDraining/StopCompleted、登记与反射目标清空。
- 验证主控在登记表为空后返回 `0`，SampleHost 以退出码 `0` 结束。

### FIFO 丢失与重复

- 丢弃某单元 Stop 命令，验证它仍通过全局状态主动退出。
- 重复发送 Stop，验证清理和注销只执行一次。

### 超时

- 注入一个拒绝注销或持续阻塞的单元。
- 验证倒计时到期后返回 `124`。
- 验证超时报告准确列出残留单元及其最后状态。

### 并发与注册竞态

- 并发触发多次 safe-shutdown，验证共享同一请求和结果。
- 在 StopRequested 后尝试注册新单元，验证被拒绝或立即收到 Stop。

### Release

- Release 构建运行输出、日志、反射、钩子、管道和安全退出。
- 验证关机协调不依赖 Debug 断点类型或命令。

## 不在本次范围

- 超时后的强制 Kill 策略。
- 分布式跨机器关机协调。
- 替换现有 RuntimeRoot/DLIST 数据结构。
- 恢复 Release 断点功能。

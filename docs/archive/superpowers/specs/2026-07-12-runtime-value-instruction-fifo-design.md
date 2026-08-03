# Runtime 固定值指令 FIFO 设计

## 目标

为主控、进程、线程、任务和业务状态对象提供统一的高速控制面：状态转换完成后向主控发送整数状态指令；主控向每个执行单元发送整数控制指令。FIFO 只传固定宽度值类型，复杂内容统一通过命名管道读取。

本设计不使用命名 MMF，不把 CLI 连接生命周期与运行状态绑定，也不以 JSON、字符串或 Base64 作为 FIFO 存储格式。

## FIFO 拓扑

运行时包含两层 FIFO：

1. 全局主控收件 FIFO：全局唯一，默认固定深度 128。所有已登记进程、线程、任务及业务状态对象向它写入状态 Code。主控被唤醒后连续读取，直到 FIFO 为空，然后重新等待。
2. 单元收件 FIFO：每个登记项拥有一个固定深度 FIFO，默认深度 64。主控遍历登记表即可取得单元 FIFO 描述符，并向目标单元写入控制指令。

状态流和控制流使用相同的固定指令布局，但保持独立 FIFO，防止高频状态变化阻塞 Stop、Wakeup 等控制指令。

```text
进程/线程/任务/业务状态 -- RuntimeState.Code --> 主控 FIFO(128) --> 主控 dispatcher

主控 -- RuntimeManagedCommandKind --> 单元 FIFO(64) --> 单元 dispatcher
```

## 固定指令协议

统一使用固定宽度值类型：

```csharp
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct RuntimeValueInstruction(
    long sequence,
    int processId,
    int managedThreadId,
    int entityKind,
    int entityIdHash,
    int value,
    long arg0,
    long arg1);
```

字段语义：

- `Sequence`：发送端单调递增序号。
- `ProcessId`、`ManagedThreadId`：发送现场标识。
- `EntityKind`：进程、线程、任务、业务状态对象等固定整数类型。
- `EntityIdHash`：稳定实体 ID 哈希；完整 ID 从登记表或管道获取。
- `Value`：主控 FIFO 中为 `RuntimeState.Code`；单元 FIFO 中为 `RuntimeManagedCommandKind` 的整数值。
- `Arg0`、`Arg1`：只允许承载固定整数、标志位、期限 ticks、版本等简单参数。

不新增 `StateChanged` 命令。向主控 FIFO 写入状态 Code 本身即表示该实体已完成状态转换。状态名称、层级、历史、异常、字符串 payload 和业务对象不进入 FIFO。

## 统一状态转换入口

`RProcess`、`RThread`、`RTask`、`RManagedState` 和业务开放接口统一提供：

```csharp
RuntimeState TransitionTo(string stateName);
bool TryTransitionTo(string stateName);
```

成功转换的固定顺序为：

1. 完成本地状态转换。
2. 更新登记表中的当前状态快照。
3. 构造以 `RuntimeState.Code` 为 `Value` 的固定指令。
4. 写入全局主控 FIFO。
5. 提交成功后触发主控异步唤醒。

失败的 `TryTransitionTo` 不更新快照、不写 FIFO、不触发唤醒。框架内部状态变化也必须经过统一入口，禁止直接调用 `RuntimeStateManager` 绕开通知。

## 主控向单元发送指令

主控遍历统一登记表，通过登记项取得单元 FIFO，并写入固定指令：

- `Value` 为 `RuntimeManagedCommandKind` 整数值。
- Stop、Wait、Wakeup、Initialize、Snapshot 等均遵循同一协议。
- 简单整数参数可使用 `Arg0`、`Arg1`。
- 需要字符串或结构化参数时，FIFO 只发送控制枚举和关联序号，接收端再通过管道读取详细内容。

进程、线程、任务的接收和 dispatch 规则一致，不分别维护不同命令协议。

## 共享内存与原子安全

FIFO 使用共享非托管内存中的固定槽位有界环形队列。槽位、读写游标、序号、提交状态和丢弃计数均为固定宽度字段，并使用跨线程/进程可见的原子操作与内存屏障。

推荐采用带槽位序号的有界 MPSC/MPMC 环形算法：全局主控 FIFO 支持多个执行单元并发写、主控单消费者读；单元 FIFO 至少支持主控及守护路径并发写、单元单消费者读。热路径不分配托管对象、不获取全局互斥锁、不执行字符串编码。

共享内存由根运行时创建并随受管子进程继承或复制句柄。禁止通过固定名称再次打开旧映射；最后一个持有句柄释放后，内存自然销毁。

## 异步 dispatch 与自动唤醒

每个 FIFO 配套一个可等待的唤醒句柄和一个提交代数：

1. 写入者先原子保留槽位并写完整指令。
2. 写入者发布槽位后递增提交代数并触发唤醒句柄。
3. 接收 dispatcher 异步等待唤醒句柄。
4. 被唤醒后连续排空 FIFO，并将指令 dispatch 到已注册处理器。
5. 排空后核对提交代数，避免“清除唤醒”与新写入之间的竞态；确认无新增指令后才重新等待。

线程和任务使用进程内等待句柄。受管子进程由 `RProcess` 创建时继承或复制共享映射句柄及唤醒句柄，因此同一机制对进程有效。外部直接启动、未登记或未继承句柄的进程不属于该共享 FIFO 范围，只能通过命名管道通信。

异步事件只负责 dispatch 和唤醒，不在写入线程上同步执行主控业务回调。

## 满载与可靠性

FIFO 不扩容，深度在创建后不可变。

- 状态通知：写满时允许记录丢弃计数并返回失败。状态 FIFO 是变化提示，主控可通过登记表或管道读取权威当前状态；不阻塞业务状态转换。
- Stop/Wakeup 等控制命令：不得静默丢失。写满时先唤醒目标、进行有限重试，并由关机协调器周期性补发，直到单元反登记或倒计时超时。
- 其他控制指令：调用方收到明确的成功/失败结果，由业务决定重试或转管道。

所有 FIFO 均暴露深度、待处理数量、提交序号、读取序号、丢弃计数和最后活动时间的只读快照；详细诊断仍通过管道返回。

## 生命周期与登记

登记项拥有单元 FIFO 描述符和唤醒资源。固定生命周期为：

1. 创建 FIFO 和唤醒资源。
2. 将描述符登记到主控登记表。
3. 启动单元 dispatcher。
4. 正常运行并接收控制指令。
5. 停机时先停止接收新业务、排空必要控制指令并清理业务资源。
6. 更新终态、从登记表反登记。
7. 停止 dispatcher，释放 FIFO、共享内存和唤醒句柄。

销毁和反登记必须幂等。主控只能在进程、线程、任务及业务登记表全部清空后返回正常退出码；倒计时到期仍有登记项则返回超时退出码 124。

## 测试要求

必须覆盖：

- `RProcess`、`RThread`、`RTask` 和独立 `RManagedState` 转换后均向主控 FIFO 写入相同格式的 Code 指令。
- `TryTransitionTo` 失败时无 FIFO 写入。
- 主控一次唤醒能够连续排空到空。
- 主控按登记表向每个单元 FIFO 发送 Stop/Wakeup，并触发目标 dispatcher。
- 多线程及多进程并发写入不产生半帧、重复消费或游标损坏。
- FIFO 满载时状态通知不阻塞，控制命令报告失败并按规则重试。
- 受管子进程能够收到控制指令并向主控报告状态；外部未登记进程不能访问共享 FIFO。
- 单元销毁后完成反登记并释放句柄；重复销毁安全。
- 复杂内容没有进入 FIFO，而是通过管道正确取得。

## 迁移要求

现有基于命名 MMF、Mutex、字符串、JSON/Base64 编码的 command/state FIFO 不能继续作为活动控制路径。迁移后删除或隔离旧实现，避免新旧协议同时生效。监视输出、断点及其他数据通道不在本设计范围内，除非它们明确迁移到相同的固定值指令约束。

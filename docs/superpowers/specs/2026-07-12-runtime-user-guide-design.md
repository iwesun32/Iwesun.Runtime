# Iwesun Runtime 用户手册重构设计

## 1. 文档定位

新建 `docs/IWESUN_RUNTIME_USER_GUIDE.md`，作为业务开发者接入 Iwesun Runtime 的权威操作手册。手册按实际改造顺序组织，不按内部类型或历史功能堆叠。

`docs/IWESUN_RUNTIME_DESIGN.md` 继续作为架构和原理入口，删减重复的长篇操作内容，并链接到新用户手册。`docs/README.md` 将新手册放入快速入口。

## 2. 目标读者

- 需要把现有 .NET 业务程序接入 Runtime 的开发者。
- 需要把直接创建的进程、线程和任务替换为受管对象的开发者。
- 需要注入输出、监视、断点、反射对象和事件的开发者。
- 需要实施标准状态管理和协调退出流程的业务开发者。
- 需要通过 JSON 或 CLI 检查和控制运行程序的运维人员。

## 3. 写作原则

1. 以可复制的完整代码段为主，不用伪 API 或省略关键清理步骤的片段。
2. 每个接入点同时说明“修改位置、返回类型、所有权、结束方式、Debug/Release 差异”。
3. 代码标识符与注释使用英文；正文使用中文。
4. 固定宿主模板和业务单点注入分开说明。
5. 优先引用 `Iwesun.Runtime.SampleHost` 和真实源码，不在多份文档中复制并分叉设计。
6. 严格区分“当前可用”与“已批准但尚未落地”。

## 4. 状态标识

手册对所有与版本迁移相关的内容使用两种明确标识：

- **当前可用**：源码已存在且通过构建或功能测试。
- **CLI v3 迁移中**：来自已批准规格，但 CLI v3 代码尚未完成。

CLI v3 实施完成前，手册不得将 v3 命令写为当前可执行命令。实施完成后，统一移除“迁移中”标记。

## 5. 章节结构

### 5.1 第一章：替换程序启动主程序

提供一份完整、可编译的标准 `Program.cs`，覆盖：

- `Host.CreateApplicationBuilder`。
- `Logging.AddRuntimeDiagnostics`。
- `Services.Start`。
- 业务 DI 注册。
- `Build`。
- `Activate`。
- 主线程、常驻线程和常驻任务登记。
- 业务启动位置。
- `RunAsync`。
- `RuntimeShutdownCoordinator` 协调退出。
- 正常退出码和超时退出码。

手册需明确区分：

- 固定模板代码。
- 程序名、运行目录、管道名等宿主配置。
- 业务服务注册和业务启动代码。

旧 `AddRuntimeDiagnostics / UseRuntimeDiagnostics / BuildDiagnosticRegistries` 分散模式只进入迁移删除清单，不作为推荐用法展示。

### 5.2 第二章：替换进程、线程和任务创建

每类创建点都使用固定的对照格式：

1. 旧代码。
2. 标准替换代码。
3. 返回对象类型。
4. 启动方式。
5. 停止和等待方式。
6. 销毁与反登记时机。
7. 不可机械替换的边界。

对照关系：

| 原创建方式 | 标准接入 | 返回类型 |
| --- | --- | --- |
| `Process.Start(...)` | `RuntimeInjector.CreateProcess(...)` | `RProcess` |
| `new Process()` | `RProcess` / `RuntimeInjector.CreateProcess(...)` | `RProcess` |
| `new Thread(...)` | `RuntimeInjector.CreateThread(...)` | `RThread` |
| `Task.Run(...)` / `new Task(...)` | `RuntimeInjector.CreateTask(...)` | `RTask` |

需说明 `unitId`、lifetime、owner、sourceLocation、category、threadId、CancellationToken、创建失败回滚和完成后反登记。

### 5.3 第三章：注入器、监视记录器与断点注入器

按“编译期属性 + 业务现场单点代码”说明：

- `DiagnosticPipePrefix`。
- `DiagnosticFileOutput`。
- `DiagnosticWatchPoint`。
- `DiagnosticBreakpoint`。
- `DiagnosticNumericBreakpoint`。
- `DiagnosticHookableEvent`。
- `RuntimeInjector.Output`。
- `RuntimeInjector.Watch`。
- `RuntimeInjector.Break`。
- `RuntimeOutput.BreakIfNumbers`。
- `RuntimeInjector.Data`。

每个功能都需给出：

- 属性完整格式。
- 运行代码完整格式。
- 稳定 ID 命名规范。
- 缺省启用/禁用状态。
- Debug/Release 编译行为。
- CLI 动态控制入口。

“单点注入”定义为：一个功能的 ID、条件、上下文和调用集中在业务现场一个连续代码段中，不把同一功能的代码散布到多个无关位置。

### 5.4 第四章：业务状态、事件与安全退出

覆盖：

- `RuntimeStateManager` 全局生命周期状态。
- `RManagedState` / `IRManagedState` 业务细分状态。
- `SetDetail / TryGetDetail`。
- `TransitionTo / TryTransitionTo`。
- 状态转换后向主控 FIFO 发送简单状态指令。
- 复杂状态数据走命名管道。
- 业务事件声明、静态登记、触发、弱引用和静态事件卸载。
- 进程、线程和任务守护循环。
- 全局“准备关机”状态。
- 主控逐项发送 FIFO Stop。
- 业务清理、状态更新、销户和退出。
- 主控检查进程/线程/任务登记表与 DLIST。
- 正常退出码与超时退出码。

标准流程必须同时展示两种退出触发：

1. 守护程序主动查询全局状态位。
2. 守护程序事件驱动接收 FIFO 简单枚举指令。

### 5.5 第五章：JSON 与 CLI 清单

覆盖：

- `rtdiag/2.0` 单命令 frame。
- `rtdiag/3.0` batch frame。
- 4 字节 little-endian 长度前缀。
- request / response / event / error 结构。
- endpoint、target、action、status、data、meta。
- CLI v3 的 `application / endpoints / commands / workflows / extensions`。
- 别名、自定义命令、结构化 workflow、扩展、替换和禁用。
- 规范命令分类与参数表。
- CLI 本地错误结构和退出码。
- 从启动 SampleHost 到恢复 quiet 的标准检查流程。

CLI v3 清单以 `docs/superpowers/specs/2026-07-12-cli-v3-redesign.md` 为设计源。CLI v3 实施完成后，再用新 `Iwesun.Runtime.Cli.commands.json` 的真实内容生成最终命令表，不手工虚构一份可能偏离代码的清单。

### 5.6 附录

- 最小接入检查表。
- 完整接入检查表。
- Debug/Release 能力矩阵。
- 常见错误和排查。
- 旧接入方式删除清单。
- SampleHost 和核心类对照索引。

## 6. 源码校验规则

写入手册前必须从以下源码核对真实签名和行为：

- `Iwesun.Runtime.SampleHost/Program.cs`。
- `Iwesun.Runtime.SampleHost/SampleHostWorker.cs`。
- `Iwesun.Runtime.Diagnostics/RuntimeHostTemplate.cs`。
- `Iwesun.Runtime.Diagnostics/RProcess.cs`。
- `Iwesun.Runtime.Diagnostics/RThread.cs`。
- `Iwesun.Runtime.Diagnostics/RTask.cs`。
- `Iwesun.Runtime.Diagnostics/RManagedState.cs`。
- `Iwesun.Runtime.Diagnostics/RuntimeStateManager.cs`。
- `Iwesun.Runtime.Diagnostics/RuntimeShutdownCoordinator.cs`。
- `Iwesun.Runtime.Diagnostics/RuntimeDiagnosticModels.cs`。
- CLI v3 实施完成后的新配置和命令目录。

任何无法从当前源码验证的用法不得标记为当前可用。

## 7. 文档一致性修订

新手册完成后需同步修订：

- `docs/README.md`：增加权威用户手册快速入口。
- `docs/IWESUN_RUNTIME_DESIGN.md`：将重复的接入步骤改为概要与链接。
- `docs/RUNTIME_DIAGNOSTICS.md`：保留诊断内核说明，删除与用户手册冲突的旧入口。
- `docs/IWESUN_RUNTIME_CLI.md`：CLI v3 实施后按新配置重写。
- `docs/UNIFIED_INTERFACE.md`：更新宿主模板、注入器和命令命名规范。

冲突时，当前源码和已通过测试的行为高于历史文档；已批准但尚未实施的 CLI v3 规格必须显式标记。

## 8. 验收标准

1. 新手册五个主章和附录完整。
2. 启动主程序样例与 SampleHost 当前模板一致。
3. 进程、线程、任务替换样例的返回类型、启停和反登记说明正确。
4. 监视、断点、数值断点、事件和反射注入均有属性与单点代码样例。
5. 业务状态与协调退出流程覆盖主动查询和 FIFO 事件驱动两种机制。
6. JSON 帧、wire format、CLI 配置与命令表的版本状态明确。
7. 手册内代码片段的 API 可从当前源码验证。
8. 权威文档之间不再存在相互矛盾的启动模式、协议字节序或 CLI 版本说明。

# Iwesun Runtime CLI Server 标准框架审计综合答复与整改方案

日期：2026-07-12  
答复对象：`CLI_Server标准框架审计意见书.md`  
适用范围：Iwesun Runtime Diagnostics、CLI v3、SampleHost、Windows Service 接入、发布模板、文档、技能和全量安装包  
计划版本：1.0.17

## 一、正式结论

Runtime 项目接受审计意见书中的 P0、P1 整改项，并将其全部列为 1.0.17 正式发布阻断条件。P2 项作为保持兼容的增强项纳入实施，但不得延迟 P0、P1，也不得借此引入脚本工作流、任意反射执行或新的私有协议。

本次整改由 `D:\Git Space\Runtime` 统一实施。WebView2、DDNS Snap、AIGateway 等使用方以后只提交问题、复现步骤、日志、预期行为和审查意见，不再直接修改 Runtime 公共实现。确需调整宿主代码时，只执行适配新公共接口所必需的最小修改。

审计意见已经写入 `docs/REQUIREMENTS_ACTIVE.md`。在本文验收清单全部满足以前，现有 1.0.16 只能视为可用的阶段版本，不作为 CLI Server 标准框架最终发布版。

## 二、已确认并继续保留的设计

以下能力方向正确，本轮不推倒重做：

1. `RuntimeHostTemplate.Start` 负责服务登记，`Activate` 负责启动诊断服务和建立宿主登记表。
2. 普通 Generic Host 与 Windows Service 使用同一套 Runtime 诊断、状态和协调退出核心；Windows Service 只增加 SCM 生命周期适配。
3. 服务名、显示名、说明和退出期限由业务程序通过 Program.cs 或业务 DI API 提供，Runtime 不猜测业务身份。
4. CLI v3 catalog 面向功能生成 `RuntimeDiagnosticFrame`；CLI 文本不是协议权威层。
5. RuntimeDiagnostics、Management、WebRuntime 等通道物理隔离，由 Runtime 统一申请、登记、解析和代理。
6. Composite 是同一 endpoint 上的固定步骤 batch，不是 workflow，不支持条件、循环、结果绑定或脚本文本。
7. `reflection.get` 保持只读；`reflection.invoke` 仅在 Debug Host 中开放，而且只能调用宿主明确列入 `InvokableMembers` 的无参数方法。
8. SCM Stop、系统 Shutdown 和 CLI shutdown 汇聚到同一个幂等协调退出任务；不得建立第二套业务退出状态机。

## 三、逐项正式答复

### P0-1：发布 Startup 模板必须能够直接编译

答复：接受，作为最高优先级整改。

现有模板同时承担最小启动代码和诊断示例，包含 `YourHostedWorker`、`yourStateObject` 等业务占位符。它适合作为说明材料，但不满足“复制、替换、编译”的发布标准。

整改决定：

- 新增 `RuntimeHost.Startup.Minimal.Template.cs.txt`，只保留可编译的固定启动骨架和清晰的业务注册区域。
- 新增 `RuntimeHost.DiagnosticsExamples.Template.cs.txt`，集中存放可选 Output、Data、Thread、Task、Reflection 注入示例。
- 所有必须替换的文本统一使用 `__PLACEHOLDER__`，并在模板顶部列出完整替换表。
- 最小模板不得引用未在模板内声明的业务变量、类型或固定业务 target ID。
- 最小模板的业务注册区为空时也必须能够编译、启动和响应基础 CLI；不得保留 `YourHostedWorker` 等要求用户先创建类型的语句。
- HostedWorker、真实 `RTask`、Reflection 及其他业务示例全部移入独立示例模板，不作为最小模板编译的前置条件。
- 旧的混合模板停止发布；若源码仓库短期保留，只能标记为废弃迁移参考，不得出现在安装包推荐入口。

验收证据：把最小模板复制到空白 .NET 10 Worker 项目，仅执行顶部替换清单，Debug、Release 构建成功；启动后 `host.info` 和 `lifecycle.status` 正常往返。

### P0-2：区分描述性登记与真实执行对象

答复：接受，属于模型和文档一致性缺陷。

整改决定：

- `RuntimeInjector.Thread/Task` 明确定义为描述性或外部对象登记，不创建线程或任务，也不因此获得 Runtime 托管退出能力。
- `RuntimeInjector.CreateThread/CreateTask`、`RThread/RTask` 明确定义为实际受管执行对象。
- `BackgroundService.ExecuteAsync` 仍由 Generic Host 管理，不伪装成独立 `RTask`；Runtime 只登记其 HostedService 生命周期和业务状态。
- 执行登记快照和 CLI 输出增加明确的 execution kind/origin 字段或等价可辨识信息，使 descriptive、managed 和 hosted service 不依赖名称猜测。
- Server 示例至少包含一个真实 `RTask`，覆盖启动、Stop、正常完成、异常和实际完成后反登记。

安全边界：Dispose、清理超时或主控超时不得把仍在运行的对象伪报为已停止，也不得提前从登记表删除。

### P0-3：Server Worker 完整展示 Stop 与 Wakeup

答复：接受。Wakeup 与 Stop 必须是两个独立语义。

标准 Worker 示例将同时覆盖：

- 主控全局 Stop/Exit 状态轮询；
- 单元 FIFO `Stop`；
- 单元 FIFO `Wakeup`；
- 可取消、可唤醒的长等待；
- `CleanupRequested` 业务清理事件；
- `Requested -> Draining -> Completed/Timeout` 状态；
- 清理信号与正常执行出口；
- 底层执行实际结束后的自动反登记。

行为规定：

- Wakeup 只终止当前等待并立即进入下一轮，不改变运行状态。
- Stop 发布一次清理周期，推动受管入口从正常出口返回。
- 无清理订阅者时立即标记清理完成，不增加空等待。
- 有清理订阅者时等待委托返回或主控唯一截止时间；单元不得创建私有倒计时。
- CLI 连接断开不得触发 Stop、Wakeup、恢复断点或改变运行状态。

### P1-1：CLI 帮助完整性

答复：接受。

标准 catalog 中每条内置命令必须提供：

- 非空 `summary`；
- endpoint；
- 参数名称、类型、位置、是否必需和默认值；
- 只读、状态修改或破坏性风险等级；
- Debug-only、Release 或通用能力标记；
- 至少一个简短示例。

CLI 增加命令级帮助：`iwrt <command> --help`。`lifecycle.shutdown` 必须明确退出影响和确认方式；`reflection.invoke` 必须明确 Debug-only、显式白名单和无参数限制。

自动化调用不得靠交互猜测。需要跳过确认时必须使用 catalog 明确声明的参数。

Catalog 元数据兼容策略：

- `risk`、`capability`、`examples` 作为 `iwesun.runtime.cli/3.0` 的向后兼容可选字段加入，不改变既有命令路由语义。
- 新 CLI 读取缺少这些字段的旧 v3 catalog 时采用安全默认值，并保持原命令可执行。
- 旧 CLI 读取包含新增字段的新 v3 catalog 时必须忽略未知可选字段，不得因严格反序列化失败。
- 若现有解析器无法满足双向兼容，必须显式升级 catalog schema、提供清晰版本错误并成套发布 CLI/catalog，不得静默复用 `3.0` 标识。
- 自动化测试覆盖“旧 CLI + 新 catalog”和“新 CLI + 旧 catalog”；旧二进制无法直接复现时，使用冻结解析器或兼容样本建立等价证据。

### P1-2：Start/Activate 重复调用和多 Host 语义

答复：接受，并确定采用“单进程单 Runtime Host”。

当前静态 `RuntimeInjectionContext` 不具备安全的多 Host 隔离。1.0.17 不宣称支持同一进程多个 Runtime Host，也不以静默覆盖维持表面兼容。

整改决定：

- `Start` 对同一 `IServiceCollection` 使用相同有效参数重复调用必须幂等。
- `Start` 参数冲突时抛出专用 .NET 异常 `RuntimeHostConfigurationException`，异常包含冲突字段；DI 注册阶段没有 Runtime Frame 响应通道，不得伪装成 CLI 协议错误。
- 同一 provider 重复 `Activate` 必须幂等返回同一激活结果，不得重复启动管道、重复扫描或重复登记。
- 不同 provider 在同一进程再次 `Activate` 必须 fail-fast，异常中说明首次 Host 身份和当前冲突身份。
- 首次激活成功后，后续失败激活不得覆盖 Execution、Managed、Hub 或宿主身份。
- Stop/Dispose 后是否允许重新激活必须由测试固定；在完整重置语义实现前，默认不允许同进程再次激活。

未来若确需多 Host，必须把静态上下文改造成 Host/Scope 隔离，并另行升级公共生命周期模型，不能在本轮暗中扩展。

CLI 结构化错误只适用于宿主成功启动后的命令处理阶段；启动前 DI 配置错误遵循标准 .NET 异常边界。

### P1-3：CLI 手册补齐用户增量配置

答复：接受，属于文档与实现不一致。

主调用格式统一为：

```text
iwrt [--pipe=NAME] [--config=PATH] [--user-config=PATH] [--timeout-ms=15000] <command> [arguments]
```

`--config` 表示替换标准 catalog 的显式入口；`--user-config` 表示在标准 catalog 上加载用户增量扩展。用户配置继续支持别名、自定义命令和 `composites`，但不得恢复旧 schema、旧命令字符串步骤或 workflow 字段。

该格式必须同步到 CLI 手册、速查手册、Server 手册、技能、CLI 总帮助和安装包文档。

### P1-4：官方验证入口

答复：接受。

整改后提供四类可发现入口：

1. Debug/Release 解决方案构建。
2. FunctionalTests 正式场景入口，输出场景名、检查总数、成功数、失败数和进程退出码。
3. 安装版验证脚本，检查目录、DLL 变体、依赖、文档、模板、技能、catalog 和版本一致性。
4. 使用安装 DLL 的 SampleHost + CLI 端到端测试，覆盖 host、lifecycle、execution、Wakeup、Stop、Composite 和安全退出。SampleHost 不得使用 Runtime 源码 `ProjectReference`，不得引用仓库内手工 DLL；引用来源必须是 `C:\Program Files\Iwesun\Runtime`。`Private=true` 复制到宿主输出目录属于正常 .NET 行为，但输出副本哈希必须与对应 Debug/Release 安装 DLL 一致。

没有标准摘要、没有断言数量或只返回构建成功的命令，不得登记为功能测试通过。

## 四、P2 增强答复

### 命令风险元数据

接受。风险字段作为 catalog 的结构化元数据加入校验和帮助输出。它用于提示和自动化策略，不改变协议的功能权威边界。

### Composite 汇总

接受，但不得产生第二段非 JSON 输出。逐步结果和完整 `RuntimeDiagnosticFrame` 继续是权威输出；总步骤、成功、失败、跳过、首个失败代码和总耗时放入 Frame 的 `Data` 或 `Meta` 稳定字段。CLI 默认仍只输出一个完整 JSON Frame，不改变现有脚本的单 JSON 边界。服务端仍执行单 endpoint、单连接、固定步骤 batch。

### runtime.inspect

接受并固定为只读健康检查。它只能查询 host、lifecycle、registry、process、thread、task 和 pipe 状态；不得启用诊断点、恢复断点或发出 shutdown。

### Release 能力协商

接受。Release Host 对 `reflection.invoke` 等未编译能力返回明确 capability code 和构建能力信息，不再只返回容易被误判的通用 Unsupported 文本。

## 五、实施顺序

### 第一阶段：模板和执行语义

1. 拆分并修复最小启动模板与诊断示例模板。
2. 修订 descriptive、managed、hosted service 模型与输出。
3. 完成真实 RTask Server 示例。
4. 完成 Stop/Wakeup 可唤醒 Worker 示例和聚焦测试。

阶段出口：P0 全部通过，模板可独立构建，Wakeup/Stop/反登记行为有自动化证据。

### 第二阶段：生命周期唯一性

1. 为 Start/Activate 增加进程 Host 身份和幂等保护。
2. 阻止不同 provider 静默覆盖上下文。
3. 增加重复 Start、重复 Activate、冲突 provider 和停止后的测试。

阶段出口：单进程单 Runtime Host 语义稳定，无静态上下文污染。

### 第三阶段：CLI catalog 和帮助

1. 扩充 catalog 摘要、参数、风险、能力和示例元数据。
2. 实现命令级帮助和结构化确认规则。
3. 补充 Composite 汇总与 Release capability code。
4. 为 `runtime.inspect` 建立正式回归。

阶段出口：全部内置命令帮助完整，JSON 校验、别名、用户增量配置和 Composite 回归通过。

### 第四阶段：文档、技能和发布验证

1. 同步用户手册、速查手册、CLI 手册、Windows Service 专项手册和文档索引。
2. 同步仓库技能和安装版技能，重点更新 host-startup、managed-execution、state-shutdown-events、json-cli。
3. 用安装版 DLL 构建 SampleHost，并执行真实 CLI 端到端验证。
4. 使用唯一全量打包入口生成 1.0.17 MSI，执行发布目录自检和安装验证。

阶段出口：源码、JSON、模板、样例、文档、技能和 MSI 版本及内容一致。

## 六、测试矩阵

| 范围 | 必测场景 | 关键断言 |
| --- | --- | --- |
| 模板 | 空白 Worker 复制最小模板 | Debug/Release 可编译，启动无占位符错误 |
| 模板 | 最小模板业务注册区为空 | 无需创建 Worker/RTask/Reflection 类型即可启动并响应基础 CLI |
| Host | Start/Activate 正常和重复调用 | 幂等或明确拒绝，不重复管道/登记 |
| Host 冲突 | 两个 provider 依次 Activate | 第二次 fail-fast，首次上下文不变 |
| Worker | 长等待时 Wakeup | 立即继续下一轮，不退出 |
| Worker | FIFO Stop/全局 Stop | 同一清理周期，正常出口返回 |
| 清理 | 零钩子/多钩子/异常/超时 | 状态正确，唯一总截止时间 |
| Execution | RProcess/RThread/RTask | 真实结束后反登记，不伪完成 |
| Windows Service | SCM Stop/Shutdown | 与 CLI shutdown 共用幂等协调任务 |
| CLI | 全局帮助/命令帮助 | 摘要、参数、风险、能力、示例完整 |
| Catalog | 旧 CLI + 新 catalog；新 CLI + 旧 catalog | 可选元数据双向兼容，或返回明确 schema 版本错误 |
| CLI 配置 | `--user-config` | 增量加载、别名、命令和 composites 可用 |
| Composite | `runtime.inspect` | 单连接、逐步结果、只读、汇总位于 Frame 内且只输出一个 JSON |
| Reflection | Debug/Release invoke | Debug 白名单成功；Release capability 拒绝 |
| 安装版 | SampleHost 引用安装 DLL | 无源码 ProjectReference/手工 DLL；输出副本与对应安装 DLL 哈希一致 |

## 七、发布阻断与验收

以下条件必须全部满足才能发布 1.0.17：

- [ ] 两个新模板职责分离，最小模板可直接构建。
- [ ] 最小模板在业务注册区为空时仍可启动，不依赖任何预先创建的业务类型。
- [ ] 所有模板占位符统一且具有完整替换清单。
- [ ] descriptive、managed、hosted service 能在模型和 CLI 中区分。
- [ ] 真实 RTask 的完成、异常、Stop 和反登记测试通过。
- [ ] Wakeup 与 Stop 分别通过长等待场景验证。
- [ ] RProcess/RThread/RTask 均只在底层实际完成后反登记。
- [ ] Start/Activate 重复与 provider 冲突语义通过测试。
- [ ] `Start` 相同参数重复调用幂等，参数冲突抛出 `RuntimeHostConfigurationException`；CLI 错误边界不延伸到 DI 注册阶段。
- [ ] SCM Stop、系统 Shutdown 和 CLI shutdown 共用幂等协调链。
- [ ] 所有内置 CLI 命令具有非空摘要和命令级帮助。
- [ ] Catalog 新增元数据完成旧/新解析器双向兼容验证，或升级 schema 并给出明确版本错误。
- [ ] `--user-config` 在实现、帮助、文档和技能中一致。
- [ ] `runtime.inspect` 的只读边界和汇总结果通过回归。
- [ ] Composite 汇总保持在完整 Frame 内，CLI 默认仍只有一个 JSON 输出。
- [ ] Debug/Release Diagnostics 能力差异和 capability code 通过验证。
- [ ] FunctionalTests 输出明确检查总数和退出码。
- [ ] 安装版 SampleHost 使用安装 DLL 完成端到端测试。
- [ ] SampleHost 无 Runtime 源码 ProjectReference 和手工 DLL 引用，输出 DLL 哈希与安装目录对应变体一致。
- [ ] 全量包不包含旧模板、旧 schema、`diagnostic-switchboard.json` 或样例 `bin/obj`。
- [ ] 发布文档、catalog、技能、模板、DLL 和 MSI 的版本一致。

任何一项未完成，都不得把阶段性构建标记为 CLI Server 标准框架正式版。

## 八、交付物

1. Runtime Diagnostics 和 CLI 源码整改。
2. 两个标准 Server 模板及真实 RTask/Worker 示例。
3. 更新后的 CLI v3 catalog 和用户增量配置样例。
4. 用户手册、速查手册、CLI 手册、Windows Service 专项手册和验证说明。
5. 仓库技能与安装版技能。
6. FunctionalTests 和安装版端到端验证报告。
7. 1.0.17 Debug/Release DLL、CLI、JSON、样例、文档、技能及全量 MSI。

## 九、责任声明

本文是 Runtime 项目对审计意见的正式综合答复和整改承诺。文中“已保留”表示现有架构方向继续采用；“接受”表示纳入整改范围；只有通过对应测试并在验收清单中勾选后，才能写为“已完成”。后续使用方提出的新增意见先进入本文件或活动需求，经 Runtime 侧统一评估后实施。

审阅补充的五项边界已经纳入本文，方案状态为“可进入实施”；该状态不代表任何整改项已经完成。

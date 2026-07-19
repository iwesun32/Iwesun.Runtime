# Iwesun Runtime 发布更新记录

## 1.0.29（2026-07-19）

- 全量发布新增 `Iwesun.Networks` 1.2.0：从 Networks 当前源码构建程序集，并携带完整文档、1.2.0
  发布状态和可编译示例；Networks 仍保持独立 `System.*` 基础库边界。
- 同步当前 Iwesun.Data：正式兼容面仍为 1.0.25，RecordStore V2 继续作为隔离类型随文档和验证结果
  交付；固定 50 万条 Source 内存仍高于 1.25 倍门槛，不宣称 V2 正式晋升。
- WebView2 的 DOM 真快照、数据流记录器、请求/响应交换、复合条件、匹配委托、CLI v3 命令和
  SampleHost 由当前源码重新构建，发布状态同步到 1.0.29。
- 唯一全量入口新增 Networks/Data 双配置测试和 Runtime 双配置完整功能场景，随后从空 staging 重建
  Diagnostics、CLI、Runtime Data、RemoteConsole、Protocol、WebView2、Networks、Data、配置、文档、
  技能、样例及 MSI。
- Networks Debug/Release 各 18/18、Data Debug/Release 各 141/141、Runtime Debug 26/26 与 Release
  21/21 完整功能场景通过；安装树自检、安装版 SampleHost 双配置哈希及 MSI Rebuild 通过。

## 1.0.28（2026-07-19）

- RemoteConsole与CLI真实命名管道端到端通过：独立控制目标、工作区、分块文件上传、PowerShell执行、
  stdout/stderr和退出码跟随均可用。
- 明确文件传输边界：`file.upload`支持CLI本地到远端工作区；远端后续复制使用审批后的`Copy-Item`并受
  服务账号ACL约束；当前不提供`file.download`。
- RemoteConsole、Protocol、CLI、WebView2、Diagnostics、Data、配置、文档、技能和样例由唯一入口
  重新构建为1.0.28全量包；MSI仍不自动创建账号、注册或启动RemoteConsole服务。

## 1.0.27 候选版（2026-07-18）

- Iwesun.Data RecordStore V2完成稳定内部索引节点：成功Key迁移、Update和Deprecate产生的空桶按
  `IndexEntryHandle`即时退出哈希链并归还Arena槽，提交阶段不重新调用用户比较器。
- Data Debug/Release各121项通过；10万次Key迁移、1万条全索引废止、混合操作、128/256 MiB发布矩阵、
  HotPath及100万次Churn通过。
- Runtime、Aether、DDNS Snap双配置构建通过；Aether 20/20，DDNS Snap 556通过、8项外部条件跳过；
  Runtime Data相关`root-safety`通过。
- Runtime全量发布清单删除旧Data文档入口，改为V2设计、公共API、两份迁移指南、发布状态、生命周期
  审查和完整复验报告；安装自检与接入技能同步更新。
- 修复ScaleTest HotPath把剩余Active数量固定写成5万的错误断言。
- 修正FunctionalTests按构建模式选择场景的清单；Debug 26个、Release 21个完整功能场景全部通过，
  包含CLI catalog/metadata、数据流记录器、WebRuntime脚本和断点子进程。
- 发布器默认版本升级到1.0.27；全量staging、安装版SampleHost Debug/Release编译、DLL版本/哈希、
  CLI配置、技能、样例和发布目录残留自检通过。
- 安装文档改用独立发布索引并保留Runtime、Data与WebView2所需目录结构；发布目录Markdown
  相对链接检查为0个失效链接。
- 当前尚未生成正式MSI；后续收到明确打包指令后再由唯一全量入口生成。

## 1.0.26 候选版（2026-07-18）

- WebView2 新增公共数据流监视记录器，支持通用复合条件、分类器、命名/直接委托、请求响应交换、请求 sidecar、manifest、限额和事件订阅。
- CLI v3 新增 `web.data-recorder.create/start/status/list/update/stop/delete/events`，系统路由和帮助元数据同步登记。
- 新增完整 C# API、RuntimeDiagnosticFrame、CLI 参数、宿主接线、升级/回退说明和可编译 SampleHost 样例。
- DOM 真快照 API、数据流记录器文档及 1.0.26 升级说明进入统一 staging 和安装自检。
- 发布器默认版本升级到 1.0.26，正式流程先执行 Debug/Release 与 Setup clean，再重建 staging；不复用旧 publish/staging。
- 当前仅完成源码、文档、配置和发布工程准备；未经明确发布指令，不生成或签署正式 MSI，也不覆盖 1.0.25。

## 1.0.25 候选版（2026-07-17）

- `Iwesun.Data.RecordStore` 新增语义明确的 `AllowKeyDuplicate` 和缺省关闭的 `AutoMerge`；不保留
  易歧义的 `AllowDuplicate` 别名。
- `DuplicateComparison` 固定为按需合并分组器：普通 Add 不调用，不构成 TValue 唯一性约束；
  添加时仅在 AutoMerge 和两个委托齐备时运行，最终聚合发布显式运行。
- Source 业务添加入口统一进入 AddCore；内部快照、Clone 和反序列化物化按业务语义保留直接
  重建能力。
- Runtime 全量发布文档增加属性与委托语义、开发样例、技术设计、Data 更新记录和完整测试报告。
- Data Debug/Release 各 54/54；128/256 MB 普通与聚合发布均 PASS；Runtime 25/25 功能场景和
  Aether.Service.Tests 19/19 通过。
- 版本推进到 1.0.25，当前只完成源码、文档和发布工程准备；尚未生成新的 MSI，不覆盖 1.0.24。
- DDNS Snap 业务项目和安装包主体完成构建，但消费者测试替身 `NullRootSnapshot` 尚未实现
  `PublishServerAccessAddresses(...)`。该问题与 RecordStore 无关，正式全量验证时必须单列。

## 1.0.24（2026-07-17）

- Runtime 私有 `RuntimeDList<T>` 已迁移到独立 `Iwesun.Data.RecordStore<TKey,TValue>`。
- 状态历史、RuntimeRoot 条目和文件路径登记改用 Schema、`StoreRecordId` 与显式克隆策略。
- 发布工程、安装版 SampleHost、用户手册和接入技能增加 `Iwesun.Data.dll` 必需依赖及哈希校验。
- 上游 RecordStore 迁移手册、API 和使用说明纳入统一文档集合。
- Data 删除 DList 专用测试后，当时的 44 项 RecordStore 测试、Runtime Debug/Release 全构建和 25 项完整功能场景通过。
- `DList<TKey,TValue>`、`DList<TValue>` 与旧 JSON 转换器已从 Data 活动源码和程序集删除；
  历史实现移入忽视存档，残留旧调用会直接产生编译错误。
- Runtime 编译目录同步移除最后一处 `service.merge.dlist-policy` 旧目录项，改为
  `service.merge.recordstore-policy`，并将诊断配置 SchemaVersion 提升到 5。
- RecordStore 明确采用单写者 Source 与多线程只读 Snapshot 模型；重叠写入/发布快速失败，
  可选 `SourceAccess` 为跨异步执行流提供合作式协调，快照首次索引支持并发读取。
- Data Debug/Release 当时各 44 项 RecordStore 测试通过；128 MB、256 MB 普通发布与聚合发布四组规模测试均通过。
- Runtime Debug/Release 全解决方案均为 0 警告、0 错误；25/25 动态功能场景全部通过，报告为
  `full-report-20260717-052432.json`。
- WebView2、CLI、Diagnostics、Data、RemoteConsole、样例、配置、文档和技能继续使用统一全量
  打包入口，不发布局部增量包。
- 全量 staging 自检、安装版 SampleHost Debug/Release 依赖哈希及 MSI Rebuild 均通过。

## 1.0.23（2026-07-16）

### Debug 断点可靠性

- 同一断点 ID 支持多个并发等待调用链，每个 `resume` 只释放一个实际等待者，`resumeAll` 和 `disable` 释放全部等待者。
- 断点控制与诊断输出总开关解耦；关闭输出不会使已启用断点失效。
- `CancellationToken` 能在等待开始后解除断点，并回收与恢复竞争产生的信号许可。
- 命中上下文先转换为安全 JSON 快照；循环引用或不支持对象不再把序列化异常传入业务调用链。
- 重复断点 ID 明确拒绝，不再静默覆盖旧登记和遗失等待者。
- 断点注册表实现幂等 `IDisposable`，释放前恢复等待者并避免关闭仍在使用的等待句柄。
- 未知断点 ID 的 enable、disable、resume 返回 `NOT_FOUND` 结构化失败。
- 保留匿名 MMF、Semaphore、共享状态布局和旧设计注释；现状注释明确跨进程控制经主管道进入宿主后操作本地断点。

### 测试与分页契约

- 新增 `breakpoint-safety` 默认功能场景，覆盖 32 路突发恢复、Disable 全释放、取消、输出解耦、循环上下文、重复登记、释放和未知 ID。
- 修正 `process` 与 `pipe-registry` 功能场景对分页返回的旧数组读取方式；生产接口继续使用 `RuntimePagedResult<T>`，不回退大列表分页保护。
- 默认完整运行器由 24 项扩展为 25 项，并把断点安全测试纳入发布阻断。

### 发布内容

- Diagnostics 同时发布 Debug 与 Release 版本；Release 继续不编译、不装配断点功能。
- CLI、Data、Diagnostics、WebView2、RemoteConsole、SampleHost、WebView2 SampleHost、系统/用户 JSON、文档、技能、脚本和源码样例统一进入全量包。
- Data 同步发布 `RUNTIME_ROOT_DATA_STRUCTURE.md` 与 `DATA_PROJECT_RUNTIME_ROOT.md`，覆盖共享根数据结构、静态注入目录和值类型指令模型。
- WebView2 随本版重新构建并同步文件版本，不复用旧 staging。
- WebView2 同步发布 `SCRIPT_REFLECTION_PLAN.md`，与控制手册、能力清单、JSON/管道方案和发布状态共同构成当前文档集。

### 发布验证

- Debug 与 Release 全解决方案构建均为 0 警告、0 错误。
- 默认 25 项功能场景全部通过，失败数为 0。
- 断点安全场景完成 30 轮压力回归；跨进程 CLI 断点和 CLI 数值断点分别完成聚焦回归。
- 全量 staging、自检、安装版 SampleHost Debug/Release 引用哈希和 WiX MSI Rebuild 由统一打包入口执行。
- 安装自检明确阻断 Data 两份权威文档和 WebView2 脚本/反射说明缺失的发布。
- 发布自检会先把安装目录和数据目录归一化为绝对路径；从仓库根目录传入相对路径时不再导致 SampleHost 错误解析 DLL 引用。

### 已知边界

- `RuntimeShutdownCoordinator` 尚未在准备关机时显式执行断点 `ResumeAll()`；本版依靠调用方取消令牌、明确恢复或最终 DI 释放解除等待。是否将“关机自动解除全部调试等待”纳入公共协议，继续结合真实服务退出数据评估。
- WebView2 的四宿主联合生命周期和受控故障注入仍属于消费宿主实机验证项目。

## 1.0.22

- 首版 RemoteConsole 快速适配、远程 Node/Target、AI 账号管道 ACL、CLI Shell 和 WebView2 全量同步发布。

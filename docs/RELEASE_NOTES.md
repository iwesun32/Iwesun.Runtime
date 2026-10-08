# Iwesun Runtime 发布更新记录

## 1.0.47-beta.1（2026-10-09，首次公开源码候选）

- 对外品牌统一为 iwesun；GitHub 项目保持 `iwesun32/Iwesun.Runtime`。
- 增加中英文首页、非商业源码开放许可、贡献协议、工作组与分支申请、AI 管理规则及 CI。
- 汇入受管线程退出互斥、停机投递去重、HTTP 请求取消与接口身份修正，Networks 独立产品版本升为 `3.0.0-beta.6`。
- Tables 不进入公开源码及包；已有 Data 保留。Web 开发工程不进入当前交付。
- 验证范围和最终结果见 [本版发布清单](IWESUN_RUNTIME_1.0.47_BETA_RELEASE_MANIFEST.md)。下列较早记录仅对应历史版本。

## 1.0.46-beta.1（2026-09-22，全量候选已打包验证）

- 全量 Runtime 发布基线更新为 Diagnostics、Data、Networks、WebView2、CLI、RemoteConsole、
  SampleHost、配置、技能、样例和技术文档；`Iwesun.Runtime.Web`继续按调试边界排除。
- Networks 升级为`3.0.0-beta.5`：长期TCP流持有精确下一跳策略至连接释放，Packet Capture/Inject
  数据面合同为DNS代理、NAT和转发后端预留统一接口。
- 发布脚本、安装自检、安装文档索引和包清单统一切换到`1.0.46-beta.1`与Networks beta.5；
  预发布3.0程序集版本仍为`3.0.0.0`，用户须核对产品信息版本而非仅核对程序集版本。
- 根解决方案构建、Data/Networks/WebView2双配置测试、Diagnostics功能测试、旧二进制宿主兼容、
  staging自检、ZIP、MSI和SHA-256门禁均已通过；本次仍不安装、不上传公共NuGet源。

发布说明见[1.0.46-beta.1普通β发布说明](IWESUN_RUNTIME_1.0.46_BETA_RELEASE_GUIDE.md)，
交付物见[发布清单](IWESUN_RUNTIME_1.0.46_BETA_RELEASE_MANIFEST.md)。

## 1.0.43-beta.1（2026-08-01，普通β已安装验证）

- shutdown 期限统一采用 `timeoutMs > countdownMs > host ShutdownTimeout > 30s`；标准 CLI 公开期限和 payload；
- 第一次退出请求冻结唯一 RequestId、实际期限与 deadline，并发重复请求复用同一操作；
- Stop、Exit、Completed、Timeout 形成不可逆中央准入门，退出后真实拒绝 RTask、RThread、RProcess 和直接登记；
- 活动 UnitId 原子唯一；包装器只允许一个 Start 所有者，启动失败释放活动资源且保留 Faulted Execution 证据；
- Registry 冻结首次 deadline，调用方取消只取消等待；`graceful=false` 明确拒绝；
- AIGateway 等大分支服务共享一个总 deadline 清理现场，退出后监管器不得补建业务分支；
- 恢复两个公共 Target 的旧构造函数签名，并增加旧 1.0.42 二进制宿主替换验证；
- 根解决方案双配置 0 警告、0 错误；Diagnostics Debug 29/29、Release 24/24，Data 各 101/101、Networks 各 202/202、WebView2 各 34/34；
- Web 组件继续不进入发布载荷，Networks 保持 `3.0.0-beta.4` 独立版本。
- 7 项不可变候选交付物已生成；staging 与 Program Files 官方自检通过，Aether 安装版
  Release 测试 242/242、DNS/DoH 精确匹配 36/36，零错配、零失败。

发布说明见[1.0.43-beta.1普通β发布说明](IWESUN_RUNTIME_1.0.43_BETA_RELEASE_GUIDE.md)，
迁移细节见[协调退出修正与迁移说明](IWESUN_RUNTIME_1.0.43_SHUTDOWN_MIGRATION.md)，
交付物见[发布清单](IWESUN_RUNTIME_1.0.43_BETA_RELEASE_MANIFEST.md)。

## 1.0.42-beta.1（2026-07-30，普通β准备）

- 实时单节点操作使用 CDP `DOM.performSearch` 直接解析浏览器当前 XPath，不注入 JavaScript；
- 搜索结果执行唯一性、document scope、当前 URL、`nodeId/backendNodeId` 和树 revision 核对；
- 完整节点树增加整页、iframe、SPA History API 导航失效，并提供显式失效/刷新入口；
- 新 revision 发布前不再暴露旧树；失效 nodeId 自动刷新、重新解析并只重试一次；
- 批量证据读取固定同一树 revision，导航期间禁止把旧树和新 DOMSnapshot 拼接；
- WebView2 合同 Debug/Release 各 34/34；
- 不覆盖或重打既有 `1.0.41-beta.1`，Web 组件继续不进入发布载荷。

发布说明见[1.0.42-beta.1普通β发布说明](IWESUN_RUNTIME_1.0.42_BETA_RELEASE_GUIDE.md)，
交付物见[发布清单](IWESUN_RUNTIME_1.0.42_BETA_RELEASE_MANIFEST.md)。

## 1.0.41-beta.1（2026-07-30，普通β准备）

- 精确点击从未裁剪 box 中心升级为可见 content quad + CSS 视口裁剪；
- 中心和内缩采样点通过 `DOM.getNodeForLocation` 验证目标/后代命中，鼠标移动后再次验证；
- 命中祖先、兄弟、覆盖层或无可见区域时明确失败，不再报告输入已发送即成功；
- 点击结果增加实际输入坐标和 HitNodeId/HitBackendNodeId 证据；
- WebView2 Debug/Release 各 25/25，根 Runtime 双配置零警告零错误；
- Web 暂停发布边界和 Networks `3.0.0-beta.4` 独立版本保持不变。

发布说明见[1.0.41-beta.1普通β发布说明](IWESUN_RUNTIME_1.0.41_BETA_RELEASE_GUIDE.md)，
交付物见[发布清单](IWESUN_RUNTIME_1.0.41_BETA_RELEASE_MANIFEST.md)。

## 1.0.40-beta.1（2026-07-30，普通β准备）

> 已由 1.0.41-beta.1 取代。实机确认其固定点击仍缺少可见区域裁剪和实际命中证明，
> 不应继续作为精确点击测试基线。

- 固定 CDP 点击严格采用“滚入视区 → 读取 content quad → mousePressed →
  mouseReleased”，确保滚动容器外的精确目标进入可命中区域；
- 保留调用方记录 XPath 的选择语义，禁止父节点替代、首项匹配和 JavaScript 后备点击；
- 新增严格调用顺序、同一 nodeId 和滚动失败短路合同，WebView2 Debug/Release 各 22/22；
- Web/WebView2 单向依赖和零 Web 发布载荷边界保持不变；
- Networks 继续使用独立版本 `3.0.0-beta.4`。

发布说明见[1.0.40-beta.1普通β发布说明](IWESUN_RUNTIME_1.0.40_BETA_RELEASE_GUIDE.md)，
交付物见[发布清单](IWESUN_RUNTIME_1.0.40_BETA_RELEASE_MANIFEST.md)。

## 1.0.39-beta.1（2026-07-30，普通β准备）

- WebView2 平台层建立 CDP DOM 节点树，以当前 revision 的 `nodeId/backendNodeId` 执行固定
  DOM 访问；XPath 保持兼容定位能力，不再作为正式执行通道；
- HTML/XAML 组合、`HtmlRuntime*`、Web 对象映射和脚本 DOM 诊断全部归回未发布的 Web 项目；
- WebView2 删除对 Web 的项目和程序集依赖，平台节点树改用自身
  `WebRuntimeDomTreeElement` 合同；
- 分层后 WebView2 Debug/Release 合同各 21/21，Web 组合层 Debug/Release 合同各
  305/305，均零失败；
- 发布载荷继续包含 Diagnostics、Data、Networks、WebView2 和工具，递归拒绝
  `Iwesun.Runtime.Web.dll`、符号、源码和传递依赖；
- Networks 继续使用已审计的 `3.0.0-beta.4`。

发布说明见[1.0.39-beta.1普通β发布说明](IWESUN_RUNTIME_1.0.39_BETA_RELEASE_GUIDE.md)，
交付物见[发布清单](IWESUN_RUNTIME_1.0.39_BETA_RELEASE_MANIFEST.md)。

## 1.0.38-beta.1（2026-07-28，普通β候选）

- 撤回误带调试中`Iwesun.Runtime.Web.dll`的1.0.37候选；
- WebView2解除对Web项目的反向依赖，Web专属DOM属性填充适配器回归未发布Web组件；
- Diagnostics、Data、Networks、WebView2及工具继续全量发布；
- 安装验证新增递归Web DLL/PDB和项目源拒绝门禁；
- Networks继续使用已审计的`3.0.0-beta.4`。

发布说明见[1.0.38-beta.1普通β发布说明](IWESUN_RUNTIME_1.0.38_BETA_RELEASE_GUIDE.md)，
交付物见[发布清单](IWESUN_RUNTIME_1.0.38_BETA_RELEASE_MANIFEST.md)。

## 1.0.37-beta.1（2026-07-28，普通β候选）

> 当前状态：`WITHDRAWN_UNRELEASED_WEB_DEPENDENCY`。首次候选误带仍在调试的
> `Iwesun.Runtime.Web.dll`，不得分发或安装。

完整说明见[1.0.37-beta.1普通β发布说明](IWESUN_RUNTIME_1.0.37_BETA_RELEASE_GUIDE.md)，
交付物见[发布清单](IWESUN_RUNTIME_1.0.37_BETA_RELEASE_MANIFEST.md)。

- Networks升级为`3.0.0-beta.4`，显式区分IPv4/IPv6裸字节入口并修正ICMPv6 ABI；
- 协议端点使用请求级并发启动和请求私有路由快照；
- HTTP/DoH使用有界租约连接池；
- IP/MAC值增加.NET 10 UTF-8 Span泛型接口；
- Aether源码候选迁移测试242/242、DNS/DoH E2E 15/15通过。

## 1.0.36-beta.1（2026-07-25，普通 β）

> 当前状态：`GENERAL_BETA_READY_FORMAL_BLOCKED`。允许通过统一β交付目录分发MSI、便携载荷和
> Networks本地包；不上传公共NuGet源，剩余真实网络、长期统计和干净环境门禁继续阻止正式版冻结。

完整变更、强制迁移、双配置DLL布局和β测试重点见
[1.0.36-beta.1普通β发布说明](IWESUN_RUNTIME_1.0.36_BETA_RELEASE_GUIDE.md)；
交付物及验证摘要见[发布清单](IWESUN_RUNTIME_1.0.36_BETA_RELEASE_MANIFEST.md)。

- Data合入MergeAdd继承表合同、显式委托/事件、订阅领取、Source Clear、null更新和派生恢复加固，
  继续只发布`Iwesun.Runtime.Data.dll`与无版本后缀`RecordStore`。
- Networks升级为`3.0.0-beta.3`：统一17字节`IpAddressValue`和7字节`MacAddressValue`，
  删除`BinaryIpAddress`，ARP/IPv6邻居记录不再暴露字符串地址。
- Networks全部低层入口同步拒绝空RequestId，UDP长期池补齐安全空闲回收；活动和未到期隔离槽不得回收。
- WebView2补齐证据会话启动失败回滚和Program停止补偿清理，并携带当前CSS双源与HTTP资源证据资料。
- 修正`RTask`完成与await继续之间的清理竞态：await现在覆盖Managed注销闭环，运行中Dispose在实际
  完成后安全释放底层Task，不再偶发遗留任务注册。
- Runtime四个公共库统一发布Debug/Release目录；文件名和公共API一致，仅配置与可查询版本证据不同。
- 唯一发布入口生成版本化MSI、便携ZIP、Networks nupkg/snupkg、发布说明、发布清单和SHA-256清单，
  集中到`artifacts\packages\Iwesun.Runtime.1.0.36-beta.1\`。

## 1.0.35-beta.1（2026-07-23，普通 β）

> 当前状态：`GENERAL_BETA_READY_FORMAL_BLOCKED`。允许通过 MSI、便携载荷和内部文件源安装及分发；
> 不上传公共 NuGet 源，剩余 M11 与干净环境门禁继续阻止正式版冻结。

完整消费合同、Debug/Release布局及已知限制见
[1.0.35-beta.1普通β发布说明](IWESUN_RUNTIME_1.0.35_BETA_RELEASE_GUIDE.md)。

- Networks升级为`3.0.0-beta.2`，修正三轴终态、精确接口IPv6 Ping、组播目标/响应方证据和压缩原生结构解析。
- 新增`NetworkFlowSerial : uint`内部高速索引；对外绝对身份仍为四级非空GUID链。
- 通用UDP迁移到长期Socket/端口租约，严格校验实际远端，提供迟到隔离、容量拒绝、池指标与可替换数据面合同。
- Networks Debug/Release测试门禁提升到各126项；Data维持各86项。
- Runtime同时发布Diagnostics、Data、Networks、WebView2的Debug与Release DLL，文件名和公共API保持一致。
- WebView2 HTTP 证据正文策略扩展为页面复现默认集，统一保存 CSS/脚本、结构化数据、文档、图像/图标和字体；
  新增 `network.evidence.status / policy / export` 公共分发器与 CLI v3 命令，响应清单明确正文捕获、策略跳过和失败状态。
- MSI ProductVersion提升至1.0.35，升级时清理Program Files旧树并保留ProgramData用户数据。

## 1.0.34-beta.1（2026-07-22，已阻断的私有测试基线）

> 当前状态：`CORRECTION_REQUIRED_PRIVATE_BETA_BLOCKED`。真实IPv6组播联调确认Networks三轴终态和精确接口证据错误；
> 本版已失去候选资格，只能保留为失效基线或回归输入，不得继续生成、分发或安装为消费候选。

完整交付边界、标准 DLL 引用模板和正式版阻塞项见
[1.0.34-beta.1 私有测试版交付说明](IWESUN_RUNTIME_1.0.34_BETA_RELEASE_GUIDE.md)。

- Runtime发布载荷已切换为从Networks源码构建`3.0.0-beta.1`，程序集版本固定为`3.0.0.0`；
  不复制Networks实现，也不把Runtime版本注入解释为Networks API版本。
- Networks 源码、107 项测试、示例、WFP 验证和文档并入 Runtime 仓库；程序集、命名空间和项目统一为
  `Iwesun.Runtime.Networks`，不再发布 `Iwesun.Networks.dll`。
- 安装文档同步携带3.0最终设计、迁移矩阵、四级GUID跟踪与精确访问控制说明；安装验证改为检查
  当前3.0合同文件。
- Diagnostics、Data、Networks、WebView2 统一发布顶层 Release 兼容 DLL 与 `Debug`、`Release` 配置目录；
  文件名、程序集名、命名空间和公共类型名不因配置变化。
- Runtime Data 正式类型统一为 `RecordStore<TValue,TPrimaryKey>`、`RecordStoreOrigin` 和
  `RecordStoreSchemaRegistry`；源码、测试、文档及自有消费者不再保留 RecordStore 版本后缀，旧名不提供兼容别名。
- Aether 与 DDNS Snap 的 Runtime/Networks 外部依赖改为安装目录 DLL 引用，不再直接引用源码工程或 NuGet 包；
  被移除的 V2/V3 API 不提供兼容别名，以编译错误强制迁移。
- 移除安装根目录下重复的 `Iwesun.Runtime.WebView2\docs` 兼容副本，WebView2 文档只发布到统一 `docs` 目录。
- Setup 在首次安装或 MajorUpgrade 时递归清理 Program Files 下的旧 Runtime 树，再铺设完整 staging；
  ProgramData 用户数据不参与清理。
- 本版原用于私有β验证；当前除管理员WFP和完整网络矩阵外，三轴终态修复、全协议审计及真实组播回归均为前置门禁。

## 1.0.32（2026-07-21）

- WebView2 页面快照新增 CSS 双源状态恢复证据：运行态结果源由 `complete-dom-properties.json` 和 `layout-state.json` 组成；原始定义源由 `style-provenance.json` 和 `maximum-template.css` 组成。消费方按元素路径综合两源，原始布局语义优先，最终计算值和绝对几何用于校准。
- 页面证据 manifest schema 升级为 `iwesun.webview2.page-evidence/1.2`，相关能力已进入本版安装包。
- 嵌入 `Iwesun.Networks` 2.0.1：请求—响应端点只保留 GUID 唯一跟踪的 `Tracked*V2` 标准；
  被取代的 HTTP、DoH、TCP、Ping、UDP、PTR、NBNS V1 公开类型全部移除。
- IPv6 Ping精确路由会跳过不支持IPv6属性查询并返回10043的Windows适配器；初始/重试路由解析异常
  统一进入请求失败闭环，不再终止Networks发送线程或宿主进程。DDNS Snap地址守护真实CLI复测通过。
- PowerShell、Process、ARP、IPv6 邻居快照等无法等价转换为请求—响应模型的特殊端点继续保留，
  但统一采用同一异步端点生命周期和批量 FIFO 格式。
- Networks Debug/Release 各 29/29、Runtime Data Debug/Release 各 86/86、Runtime Debug 28 个与
  Release 23 个完整功能场景全部通过；staging、自检、安装版 SampleHost 双配置及 MSI Rebuild 通过。

## 1.0.31（2026-07-20）

- 在 1.0.30 单数据程序集强制升级基础上执行全量再发布，默认发布器和 WiX ProductVersion 统一升级到 1.0.31。
- RecordStore 明确为唯一活动 RecordStore 实现；发布索引、安装说明与集成技能不再保留“隔离类型/正式 1.0.25 API”旧状态。
- Networks、Runtime Data、Runtime Debug/Release、完整功能场景、staging、自检、安装版 SampleHost 双配置与 MSI 均由唯一入口从当前源码重新验证。
- 安装树继续强制拒绝 `Iwesun.Data.dll`、DList 与 RecordStore V1，并核对全部 `Iwesun.Runtime.Data.dll` 副本的版本和哈希。

## 1.0.30（2026-07-20）

- Data 强制收口为单程序集：RecordStore 源码和公共基础类型并入 `Iwesun.Runtime.Data`；
  `Iwesun.Data.dll`、DList 和 RecordStore V1 从活动工程与发布清单移除，不提供兼容别名。
- AIGateway、DDNS Snap 与 Aether 的源码引用及命名空间统一升级为 `Iwesun.Runtime.Data`，遗漏引用
  以编译错误暴露。
- WebView2 新增 `WebRuntimeNetworkEvidenceSession` 和 `WebRuntimePageEvidenceCapture`，统一完整 DOM、计算样式、CSS、脚本、事件、CDP、MHTML、请求/响应元数据及结构化 HTTP 正文证据。
- 默认过滤 Cookie、Authorization、Token、Secret、API Key 等敏感头；正文保存失败、超限或等待超时不返回伪成功。
- DoubaoUIClone 已删除本地完整页面和 HTTP 抓取实现，改为调用 Runtime 公共 API；业务项目继续负责快捷键、页面版本、XAML 和 WinUI。
- 1.0.30 staging 与 MSI 已从当前源码重建并通过安装树自检；最终文件哈希随发布交付记录提供。
- 发布树明确拒绝 `Iwesun.Data.dll`，并验证全部 `Iwesun.Runtime.Data.dll` 副本版本与哈希一致。
- 外部文档/JSON 改为共享内容寻址资源池：正文只保存一次，主/辅助快照通过稳定资源 ID 引用，公共索引反向记录所有消费快照。

## 1.0.29（2026-07-19）

- 全量发布新增 `Iwesun.Networks` 1.2.0：从 Networks 当前源码构建程序集，并携带完整文档、1.2.0
  发布状态和可编译示例；Networks 仍保持独立 `System.*` 基础库边界。
- 同步当前 Iwesun.Data：正式兼容面仍为 1.0.25，RecordStore 继续作为隔离类型随文档和验证结果
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

- Iwesun.Data RecordStore完成稳定内部索引节点：成功Key迁移、Update和Deprecate产生的空桶按
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

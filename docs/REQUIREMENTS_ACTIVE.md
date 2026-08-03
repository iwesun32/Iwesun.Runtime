# 活跃需求文档

> 状态：ACTIVE  
> 最后更新：2026-08-03

## 2026-08-03 Diagnostics 统一对象与 JSON 序列化

- Runtime Diagnostics 必须由公共 JSON 基础模型和统一序列化服务负责协议对象、事件、
  命令结果及任意诊断载荷的序列化；具体诊断模型不得各自创建互不一致的
  `JsonSerializerOptions`。
- `double/float` 的 `NaN`、`PositiveInfinity`、`NegativeInfinity` 必须以合法、可逆且明确的
  JSON 字符串字面量表示，不能因为 WinUI 尚未完成测量而使诊断调用抛错。
- 任意业务诊断载荷若包含循环引用、不受支持类型或属性 getter 异常，必须转换为结构化的
  `serializationFailure` 诊断载荷；诊断失败不得反向中断宿主业务流程。
- Runtime Frame、CLI 命令响应、FIFO envelope、文件格式化、事件筛选和断点上下文必须复用
  同一数值及容错合同；严格协议模型仍需保持固定 Frame schema。
- 必须增加基类/派生类、特殊浮点数、循环引用和不可序列化载荷的功能门禁，并以真实 CLI
  监视点复验 HTML→XAML 过程不会因诊断序列化失败而停止。

## 2026-08-02 HTML/SVG 强类型 XAML 布局语义回归 Runtime Web

- HTML/SVG 通用数据采集、六槽位填充、盒模型、Flex/Grid 子项、定位、溢出和
  具体元素的 XAML 对象计划，必须由 `Iwesun.Runtime.Web` 的具体强类型元素或其真实
  公共语义基类实现；禁止由消费 App 根据现场数值逐元素补丁。
- 每个具体 HTML/SVG 类仍由自身 `CreateXaml`/对象投影决定目标类型；属性和布局
  计划使用强类型合同，不得退回大 `switch`、字符串类名或全元素共用的动态属性表。
- 当 Runtime Web 已经生成子项过滤、轨道、对齐和参考盒计划时，消费 App 必须直接物化
  该计划，不得再次从 CSS 字符串重建一套相互冲突的通用布局器。
- 豆包项目是通用 Runtime Web 的消费样例，主要责任是组合 Build → Fill → 全局关联 →
  CreateXaml → Display → XamlFill → Audit 管线，不得建立第二套通用布局实现。
- `DoubaoLiveWinUiLayoutEngine` 只能是代码量很小且稳定的站点配置/管线适配边界；
  不允许遍历普通 HTML/SVG 元素修改 Width/Height/Margin/Padding/Flex/Grid/
  Position 等细节。可复用的响应式、容器、分辨率和状态布局能力也归 Runtime Web，
  豆包层只提供必要的站点参数。
- 已经通过真实运行对比证明有效的修正语义，必须转化为 Runtime Web 强类型
  合同和回归测试；不得只保留在 App 现场补丁中，也不得丢弃已确认的语义。

## 2026-08-02 CDP DOM 文本证据的有界内存构建

- `DOM.getDocument` 返回的完整树只能遍历一次来建立 `OwnText` 与 `TextContent` 证据；禁止在
  创建每个元素时重新递归该元素的整棵子树，否则深层页面会形成 `元素数 × 子树大小` 的重复计算。
- 所有元素、document scope、XPath、NodeId 和 BackendNodeId 必须完整保留；不得通过删除
  `script/head/iframe` 或跳过标准属性来掩盖内存问题。
- 相同文本证据应在一次快照事务内共享同一字符串实例；元素槽位和 Fill 审核轨迹引用该证据，
  不得为每个槽位复制大段脚本、JSON 或业务正文。
- 典型深层树测试必须证明每个节点的 `TextContent` 与 DOM 顺序一致，并对重复的大文本子树执行
  引用共享门禁；真实全树 Fill 必须在 DOM Build 断点核对元素数后再放行。

## 2026-08-01 HTML 动态数据捕获与 XAML 数据源绑定

- 文本和图形数据必须区分 `UiResource` 与 `BusinessInput`；前者跟随语言、主题和
  UI 样式资源，后者来自程序、用户或业务页面状态。
- `DomFill` 后的全局关系阶段必须建立具有稳定强身份和递增 revision 的动态数据源；
  `RefreshRuntimeAsync` 再次捕获 DOM 运行值时更新同一个数据源对象，不得创建无关联的
  字符串副本。
- 通用可见文本、表单实时值/状态、图像和媒体运行源必须进入动态捕获目录；不能只覆盖
  少数按标签硬编码的数据源。
- XAML 全局关系阶段必须把真实 WinUI 依赖属性绑定到该动态数据源。源值变化应通过正式
  变更通知更新现有控件；图像、布尔和数值使用强类型转换，不得依赖字符串 XAML。
- `XamlFill` 继续只读取显示后的真实 WinUI 对象。动态源、DOM/CSS 缓存和控制器值均不得
  作为 XAML 运行值读取失败时的回退。
- 测试必须证明一次初始捕获与至少一次运行时刷新使用同一数据源身份，并使已绑定 WinUI
  文本/状态/图形属性发生对应变化。

## 2026-08-01 Runtime 1.0.43-beta.1 本机发布授权

- 用户已明确授权把当前新版 Runtime 发布到 `C:\Program Files\Iwesun\Runtime`。
- 本次只允许安装不可变候选 `1.0.43-beta.1`；不得覆盖重建候选，也不得回退安装旧版本。
- 安装前必须以候选 staging 运行官方自检，核对版本、Debug/Release 双配置、旧宿主兼容载荷、
  零 Web 载荷和进程占用；任一门禁失败即停止安装。
- 安装仅替换 Program Files 产品载荷，保留 `C:\ProgramData\Iwesun\Runtime` 运行数据；
  本次授权不包含公共 NuGet 上传、提交、推送或启动业务服务。
- 安装后必须再次运行 Program Files 官方自检，并让 Aether 从默认安装根完成 Debug/Release
  编译、完整测试与 DNS/DoH 端到端回归；全部通过后才能报告本机普通β发布完成。
- 本机 Program Files 已确认加载 `1.0.43-beta.1`；候选 staging 与安装目录官方自检均通过，
  Runtime 文件版本为 `1.0.43.0`、Networks 文件版本为 `3.0.0.0`，递归零 Web 载荷。
- Aether 已直接消费安装目录完成 Debug/Release 编译和 Release 测试 242/242；DNS/DoH
  冷/热 UDP 与 TCP 各 12 项，精确匹配 36/36，mismatch=0、failure=0，评价反馈无丢弃。
- 当前状态为 `GENERAL_BETA_INSTALLED_VALIDATED`；AIGateway 长时多分支、WebView2 STA 压力、
  WFP 双网关和完整恢复矩阵继续阻止正式版冻结。

## 2026-07-31 协调退出期限与终态准入

- `lifecycle.shutdown` 未显式给出期限时，必须采用宿主通过
  `RuntimeWindowsServiceOptions.ShutdownTimeout` 配置的协调退出期限，不得在命令目标中固定为 5 秒。
- 显式 `timeoutMs` 优先于兼容字段 `countdownMs` 和宿主默认值，并保持最小 100ms 的输入约束；
  标准 CLI 必须公开 `timeoutMs` 与 `payload`。协调器拥有首次接受的实际期限和 deadline，重复或
  并发请求必须报告该实际期限，不能回显未生效的后续请求值。
- `Stop`、`Exit`、`Completed`、`Timeout` 均属于不可逆退出域；进入后不得切回运行态，也不得再启动
  新 `RTask`、`RThread`、`RProcess` 或直接登记 Registry 业务单元。准入判断与登记必须在中央入口原子完成。
- 执行包装器若在退出门竞争中登记失败，必须终结 Execution 投影并回滚命令处理器、分支管道和其他已分配资源，
  不得留下伪注册、孤儿任务或分支线程。
- 活动 `UnitId` 必须唯一，重复登记不得覆盖原所有者；同一包装器的重复或并发 Start 必须在接触底层执行体前拒绝。Execution 投影可保留 `Faulted` 终态证据，但不得继续表现为活动登记。
- 第一次建立的 shutdown deadline 必须由 Registry 自身冻结；直接 `RequestShutdown`、终态切换或调用方取消都不得改写或清空。调用方 token 只取消等待，不能取消已接受的全局退出。
- 业务单元收到 `Stop/Wakeup` 后必须通过 `CleanupRequested` 完成现场清理并实际退出、注销；只更新清理状态而执行体仍运行不算完成。
- AIGateway Server 等管理大量分支线程的宿主允许已登记阻塞单元在同一总 deadline 内完成现场清理，
  但退出开始后监管线程不得重新创建业务分支。观察器和调度基础设施必须使用
  `BlocksShutdown=false`，不得形成等待自身的环。
- CLI 接受 shutdown 请求返回 0；宿主正常退出返回 0；宿主清理超时返回 124。三个结果必须分别记录。
- `graceful=false` 在没有正式强杀合同时必须明确拒绝，不得读取后忽略；`RThread`、`RProcess` 必须允许纯观察器显式设置 `BlocksShutdown=false`。
- Diagnostics 功能测试和 SampleHost CLI 全流程必须覆盖期限优先级、重复/并发 shutdown、四种退出状态
  真实拒绝三类托管执行体、终态不可重开、失败回滚、宿主自定义期限和退出码 0/124。
- 公共修正若进入安装载荷，必须使用新的不可覆盖 `1.0.43-beta.1` 候选；发布前恢复两个命令 Target 的旧公共构造函数重载，并通过“旧宿主二进制仅替换新 Runtime 文件集合”的兼容测试。当前文档与源码验证不等同于发布或安装授权。

## 2026-07-30 Runtime 1.0.42-beta.1 全量打包发布

- 当前 WebView2 实时 XPath 直查、导航后树失效/刷新、失效 nodeId 单次恢复和跨 revision
  证据拒绝已经晚于不可覆盖的 `1.0.41-beta.1`，必须生成新的
  `1.0.42-beta.1` 普通 β 候选，不得覆盖或重打旧候选。
- Runtime 程序集/文件版本统一为 `1.0.42.0`，产品信息版本为
  `1.0.42-beta.1`；Networks 保持独立 `3.0.0-beta.4` /
  `3.0.0.0` 边界。
- 发布 Diagnostics、Data、Networks、WebView2、CLI、RemoteConsole、SampleHost、配置、
  技能、样例和文档，并同时提供 Debug/Release 同名 DLL。
- `modules/Web`、`Iwesun.Runtime.Web.dll`、PDB、源码和独立目录继续不进入 staging、ZIP、
  MSI 或安装载荷。
- 唯一发布入口必须完成 Data、Networks、WebView2 双配置测试、Runtime 根双配置构建、
  双配置功能场景、Publish staging、自检、Networks NuGet/符号包、便携 ZIP、MSI Rebuild
  和 SHA-256 清单。
- 本轮授权生成并分发本地候选；不上传公共 NuGet、不提交、不推送，也不启动业务服务。
- `1.0.42-beta.1` 唯一发布入口已以退出码 0 完成；交付目录包含 7 项文件，全部
  SHA-256 复核一致。staging 自检确认 Runtime 文件版本 `1.0.42.0`、Networks 文件版本
  `3.0.0.0`、Debug/Release 双配置、安装版 SampleHost 哈希和递归零 Web 载荷均通过。

## 2026-07-30 WebView2 实时节点直查与导航后树恢复

- 点击、属性、聚焦和高亮等少量实时单节点操作必须通过 CDP
  `DOM.performSearch` 在浏览器当前 DOM 中直接解析 XPath；不得把辅助树中的旧
  `nodeId` 当作实时查询结果，也不得注入 JavaScript。
- 搜索结果必须唯一，必须通过 `DOM.getSearchResults` 取得当前 `nodeId`，并在
  `finally` 中调用 `DOM.discardSearchResults`；零结果和多结果必须明确失败。
- 完整节点树继续服务于批量采集、对象模型和 XPath 辅助索引。实时查询所得节点必须与当前
  document scope 和树 revision 核对；URL 或节点身份不一致时自动使旧树失效并原子重建。
- 跟踪树必须订阅 `Page.frameNavigated` 与 `Page.navigatedWithinDocument`，把整页导航、
  iframe 导航和 SPA History API 导航都视为树失效边界；不能只依赖 `DOM.*` 结构事件。
- Runtime 必须提供显式失效和刷新入口。CDP 操作若明确报告 `nodeId` 已失效，必须强制刷新
  整树、重新按原 XPath 取得当前节点并仅重试一次；不得无限重试或把其他传输/协议异常误判
  为节点失效。
- 导航完成后的旧 `nodeId`、旧 XPath 缓存及旧 revision 不得继续对外提供。测试必须覆盖
  SPA 导航、漏失结构事件、失效节点单次恢复、搜索结果释放和重试仍失败的终态。

## 2026-07-30 WebView2 精确 CDP 点击未触发 SPA 导航

- 实机确认记录 XPath 能唯一解析到首条历史会话内部 `div[1]`，但固定 CDP 鼠标派发后
  `CoreWebView2.Source` 仍为 `https://www.doubao.com/chat/`，没有进入父 `<a>` 指向的
  `/chat/38435325191836930`；后续附件不存在只是会话未打开的连锁结果。
- 禁止新增或恢复 `InteractionXPath`，不得把记录 XPath 改写为父 `<a>`、首项匹配或业务 URL；
  Runtime 必须对原始精确节点完成真实浏览器输入。
- 点击链必须在滚动和读取可见 quad 后执行 CDP 坐标命中验证；只有命中目标节点或其后代才允许
  派发输入。鼠标序列必须覆盖移动、按下、释放，并保留同一节点、坐标和 revision 证据。
- 坐标命中不符、节点失效、滚动失败或可见 quad 无可点击区域必须明确失败；不得报告伪成功，也不得
  使用 JavaScript `click()` 后备。SPA 导航是否完成由宿主业务层按预期结果另行确认。
- 修正后的 Runtime 必须提升为不可覆盖 `1.0.41-beta.1`；1.0.40 保留为缺少命中证明的旧候选，
  不得覆盖重打。
- `1.0.41-beta.1` 已完成唯一发布入口、SHA-256 复核、MSI 升级和 Program Files 官方自检；
  Runtime 文件版本为 `1.0.41.0`，Networks 为 `3.0.0.0`，安装载荷递归零 Web。
- 真实豆包复验前必须重启承载 WebView2 的消费进程以加载新 DLL；复验应同时记录请求 XPath、
  NodeId、InputX/InputY、HitNodeId/HitBackendNodeId 和点击后的 `CoreWebView2.Source`。

## 2026-07-30 Runtime 1.0.40-beta.1 本机安装授权

- 用户明确授权把已完成发布门禁的 `Iwesun.Runtime.1.0.40-beta.1.msi` 安装到
  `C:\Program Files\Iwesun\Runtime`。
- 安装必须使用不可变候选目录中的 MSI，不得重新生成或覆盖候选；保留
  `C:\ProgramData\Iwesun\Runtime` 运行数据。
- 安装前核对 SHA-256、现有版本和占用进程；安装后运行官方自检，确认 Runtime 文件版本
  `1.0.40.0`、Networks 文件版本 `3.0.0.0`、Debug/Release 双配置载荷及递归零 Web。
- 本次授权不包含提交、推送、公共 NuGet 上传或启动业务服务。
- 本机安装目录已确认是 `1.0.40-beta.1`；官方自检通过，Runtime 文件版本 `1.0.40.0`、
  Networks 文件版本 `3.0.0.0`，Debug/Release 双配置和安装版 SampleHost 哈希一致，
  `Iwesun.Runtime.Web`、旧 Data/Networks 合同及重复兼容目录均为零。

## 2026-07-30 Runtime 1.0.40-beta.1 无 Web 全量发布准备

- 生成新的不可覆盖 Runtime `1.0.40-beta.1` 普通 β 候选；不得修改或复用既有
  `1.0.39-beta.1` 候选。1.0.40 增加精确点击严格顺序和滚动失败短路合同门禁。
- `modules/Web`、`Iwesun.Runtime.Web.dll`、其符号和源码继续处于调试边界，不进入 staging、
  ZIP、MSI、CLI、SampleHost 或任何公共 Debug/Release DLL 目录。
- Runtime 根 `.slnx` 只登记本次发布组件；Web 及其测试只保留在 Web 领域 `.slnx` 中。
- `Iwesun.Runtime.WebView2` 必须恢复平台组件边界，不得项目引用或程序集引用
  `Iwesun.Runtime.Web`；HTML/XAML 组合、脚本 DOM 诊断和 Web 对象映射全部归回 Web 项目。
- WebView2 节点树使用平台自有 `WebRuntimeDomTreeElement` 合同；Web 项目在组合边界显式转换为
  `DomElementConstructionNode`，禁止平台层反向依赖 Web 模型。
- 发布前必须通过 WebView2、Data、Networks 和 Runtime 的 Debug/Release 构建与测试、功能场景、
  staging 自检、安装载荷递归零 Web 检查、ZIP/MSI/NuGet 及 SHA-256 清单核验。
- 本轮只准备并生成候选，不安装、不启动服务、不提交、不推送、不上传公共 NuGet。

## 2026-07-30 WebView2 CDP DOM 节点树与 NodeId 统一访问

- 精确点击必须保留调用方记录 XPath 的选择语义，禁止替换为父节点、相邻节点或首个匹配；
  固定调用顺序必须包含前台激活、滚动、可见 quad、视口裁剪、移动前后命中证明，以及
  `Input.dispatchMouseEvent(mousePressed/mouseReleased)`。
- 滚动失败、盒模型缺失或 XPath 歧义必须立即失败；不得继续派发鼠标事件，也不得使用
  JavaScript `click()` 作为后备。合同测试必须验证严格顺序、同一 `nodeId` 和失败短路。
- `Iwesun.Runtime.WebView2` 必须在页面会话内部维护与当前网页对应的 DOM 节点树；节点至少保存
  `nodeId`、`backendNodeId`、绝对 XPath、节点名称、父子关系和兄弟顺序。
- XPath 继续作为公共定位条件，但不得交给页面 JavaScript。批量采集在内存节点树中导航；
  点击、属性、聚焦和高亮等实时单节点操作通过 `DOM.performSearch` 查询浏览器当前 DOM，
  再使用其 `nodeId` 或 `backendNodeId` 调用固定 CDP 方法完成访问。
- 正式 DOM 读取、属性访问、点击、聚焦、高亮等固定能力不得注入页面 JavaScript；受控
  `script.evaluate` 继续作为独立显式能力保留，不能作为内部 DOM API 的隐式后备。
- 节点树首次通过 CDP `DOM.getDocument` 建立，随后订阅 CDP DOM 与 Page 导航事件增量维护；
  发生文档更新、导航、事件缺口或局部更新无法安全合并时，必须把索引标为失效并原子重建整树。
- 节点树发布必须保持一致快照；XPath、`nodeId` 和 `backendNodeId` 三个索引必须指向同一 revision，
  禁止外部观察到只更新部分索引的中间状态。
- 节点身份只在所属文档 revision 内有效。重建后旧 `nodeId` 不得继续使用；执行前发现 revision
  变化时必须重新解析一次 XPath，不能对旧节点盲目发送 CDP 命令。
- 实现必须覆盖初始化失败反向清理、事件退订、并发刷新、节点插入/删除/属性变化、文档重建和释放，
  并通过 WebView2 领域 Debug/Release 构建及固定合同测试。
- 组合层拆分及点击门禁补齐后，WebView2 平台合同 Debug/Release 各 25/25，Web 组合层合同 Debug/Release
  各 305/305；WebView2 领域解决方案不再登记或传递 Web 调试项目，两个领域均为零失败。

## 2026-07-30 WebRuntime Program 停止失败隔离

- `WebRuntimeProgramHost.StopAsync` 必须在任一业务 Program 停止失败后继续停止其余 Program。
- 停止流程无论成功或失败都必须释放全部 Program 注册，禁止留下仍可执行的过期入口。
- 停止异常继续通过 `WebRuntimeProgramStopResult.Errors` 和 `StoppedWithErrors` 返回，不得因清理注册而吞掉。
- 已尝试停止的生命周期实例必须从 Host 的已初始化集合移除，避免释放阶段重复执行同一个失败停止操作。
- 使用 AIGateway 公共运行时合同测试验证停止失败、剩余 Program 停止及注册清理三项行为。

## 2026-07-29 Runtime Web HTML 根与元素直接 Fill

- HTML 根类型只负责启动、持有页面运行访问能力和挂载元素树；具体 Build、Fill、XAML
  创建与审核行为继续由具体元素对象递归实现。
- 每个 `DomElement` 必须公开只读 `HtmlRoot` 关系，指向挂载它的同一个
  `HtmlDocumentRoot` 实例；挂载既有树和后续 `AddChild` 都必须自动传播该关系。
- 正式 `DomFillAsync(CancellationToken)` 不接收查询委托，也不从 JSON 文件回填；元素
  反射枚举自身属性后，直接调用 `HtmlRoot` 提供的 DOM 查询 API。
- 消费宿主的 HTML 根组合 `Iwesun.Runtime.WebView2` 会话，并在根的 DOM 查询入口中调用
  Runtime WebView2 强类型 API。平台 WebView2 模块不得反向引用 Web 元素模块，避免循环依赖。
- 现有委托和显式 `IDomPropertyQueryEngine` 重载只作为迁移兼容入口保留；按钮、CLI 和新
  对象树流程统一使用挂载根后的无参数 Fill。
- 根引用、DOM 身份、父子关系和属性槽位都只存在于内存对象中；不得通过 JSON 文件在
  Build 与 Fill 之间传递，不得把 DOM 源值写入 XAML 运行槽位。
- Fill 热路径必须使用 `IWebRuntimeDomQuerySession.QueryDomPropertiesAsync` 的强类型
  批量 API；禁止逐属性脚本调用，禁止通过 JSON 文件或 JSON DTO 中转。
- 调试属性提取失败时允许宿主显式启用 `WebRuntimeScriptDomEvidenceReader` 的逐属性诊断
  模式；该模式仍使用固定脚本和强类型请求，但每次 WebView2 调用只查询一个属性，并在绝对
  XPath 无法解析时立即硬失败，输出该属性的完整强身份。诊断模式不得作为默认生产热路径。
- `IWebRuntimeSingleDomPropertyQuerySession.QueryDomPropertyAsync` 是与批量接口并列的正式
  强类型单属性 API；它不得要求调用方创建单元素集合。实现可与批量路径共享固定的底层读取
  原语，但必须独立返回一个请求对应的一个结果，供逐属性断点和正确性验证使用。
- Runtime WebView2 实现应建立一次页面/文档索引并批量返回属性结果；JSON 只允许作为
  Fill 完成后的可选持久化格式，不属于运行查询契约。
- `HtmlDocumentRoot.PrepareDomFillAsync` 必须汇总由各元素自身生成的查询计划并只执行
  一次底层批量查询；后续递归 Fill 必须从内存结果按强身份读取，不得再次跨进程访问。
- Runtime WebView2 必须提供可复用的 `WebRuntimeIndexedDomQuerySession`：以不可变
  `WebRuntimeDomQueryIndex` 执行 O(1) 强身份查询，并通过递增 revision 原子替换整页
  索引；索引缺少任一计划槽位必须硬失败，不能用空值或 Unsupported 自动补齐。
- 正式页面入口使用 `WebRuntimeLiveDomQuerySession`，把全树请求一次交给
  `IWebRuntimeDomEvidenceReader`；读取器必须返回强类型记录，Builder 完整验收后才允许
  发布 `LastIndex`。业务按钮与 CLI 必须共用该会话，不得各自复制采集或映射逻辑。
- `DomElement.BuildXamlObjectTree` 必须由元素自身生成控件映射、初始化属性、子槽位、
  Grid 轨道、合成文本和包装容器计划；UI 项目仅通过 `IXamlElementObjectFactory`
  实例化并挂载真实对象。成功创建的主控件必须写入该元素的 `XamlElement`，非可视元素
  不创建占位控件而是穿透其可视子树。
- `NonVisual` HTML 元素的 `content.ownText` 必须保留为 DOM 证据，但 XAML 投影审核
  明确返回 `NotRequired`；脚本、样式源码不得生成可见 TextBlock，也不得误报为缺失值。
- 纯空白直接文本节点保留在原始 DOM tree evidence 中，但
  `content.ownText` 初始化槽按视觉文本语义返回 `ConfirmedAbsent`，避免生成空白
  TextBlock 或产生 MissingXamlValue。

## 2026-07-28 Runtime 1.0.38-beta.1 本机发布授权

- 用户已明确授权把修正后的新版 Runtime 发布到 `C:\Program Files\Iwesun\Runtime`。
- 只允许安装 `1.0.38-beta.1`；已撤回的 `1.0.37-beta.1` 禁止安装、分发或作为消费引用。
- 安装前复核交付清单、进程占用和零 Web 载荷门禁；安装时只替换 Program Files 产品载荷，
  保留 `C:\ProgramData\Iwesun\Runtime` 的运行数据。
- 安装后必须运行官方自检，并让 Aether 从默认安装根完成 Debug/Release 编译、测试和
  DNS/DoH 端到端回归；未通过前不得报告发布完成。
- `1.0.38-beta.1` 已通过提权 MSI 安装到 Program Files；官方安装自检通过，确认 Runtime
  文件版本 `1.0.38.0`、Networks 文件版本 `3.0.0.0`、未发布 Web 载荷为零。
- Aether 已从默认安装根完成 Debug/Release 编译、Release 测试 242/242 和 DNS/DoH
  端到端回归：冷/热 UDP 与 TCP 各 12 项，精确匹配 36/36，mismatch=0、failure=0。

## 2026-07-28 Networks 完整架构审计与新版发布准备

- 完整审阅 `modules/Networks/src/Iwesun.Runtime.Networks` 全部活动源码、项目配置和程序集元数据，
  并从公共合同、身份跟踪、精确访问计划、协议端点、长期 UDP 数据面、转发抽象、地址值、
  Windows 路由/WFP/恢复边界和资源生命周期逐层确认体系结构闭环。
- 以当前源码而不是旧 β 审计报告为依据，逐项核对测试、样例、验证工具、最终设计、
  API 迁移矩阵和发布状态；旧版本推断不得直接登记为当前缺陷。
- 为下一版普通 β 编写独立升级迁移报告，明确破坏性变化、替换映射、消费者编译错误、
  Debug/Release 引用目录、测试重点、已通过证据和正式版剩余阻塞项。
- 新版不得复用 `3.0.0-beta.3`；完成审计后冻结新的 Networks 预发布版本，并同步项目、
  CHANGELOG、发布状态、文档索引、Runtime 聚合发布器、验证脚本和统一发布资料。
- 发布前必须完成 Networks 领域解决方案 Debug/Release 构建与全部测试、根 Runtime
  Debug/Release 构建、Publish staging 和安装载荷自检；需要管理员改变真实网络状态的
  WFP/恢复矩阵继续作为正式版门禁，不得在未授权时执行。
- 本轮允许准备并生成新的普通 β 候选；不自动安装、不启动服务、不提交、不推送，
  不上传公共 NuGet 源。
- Networks版本已冻结为`3.0.0-beta.4`；Networks Debug/Release各202/202，Aether上一轮
  staging复验242/242、DNS/DoH 15/15。
- 唯一发布入口的本轮调用在最后发现同名候选目录已由并发发布流程生成，按不可覆盖规则主动停止；
  已有目录7项交付物齐全。不得删除、覆盖或复用该候选，当前也未安装到Program Files。
- 后续载荷审计确认`1.0.37-beta.1`通过WebView2传递携带仍在调试的`Iwesun.Runtime.Web.dll`，
  因而整版撤回且不得分发或安装；修正后的全量候选提升为`1.0.38-beta.1`，不覆盖旧证据。
- `1.0.38-beta.1`必须重新完成Runtime根Debug/Release、功能场景、Publish staging、自检、
  NuGet/符号包、MSI Rebuild和哈希清单，且载荷中Web文件计数固定为零。

## 2026-07-28 Runtime Web 暂停发布边界

- `modules/Web`及`Iwesun.Runtime.Web.dll`当前仍处于架构调试期，只参与源码开发和解决方案编译，
  暂不进入Runtime MSI、便携包、CLI、WebView2、SampleHost或Debug/Release公共DLL载荷。
- WebView2不得反向引用未发布的Web基础项目；依赖`DomElement`的WebView2属性填充适配器归属
  未发布Web组件，使依赖方向保持为Web调试组件使用平台能力，而不是平台发布组件携带Web。
- 发布验证必须递归拒绝`Iwesun.Runtime.Web.dll`、其PDB和Web项目源文件；Diagnostics、Data、
  Networks、WebView2、CLI、RemoteConsole及固定样例继续全量发布。

## 2026-07-28 Networks 地址值泛型解析与 UTF-8 兼容

- `IpAddressValue` 与 `MacAddressValue` 在保持17/7字节二进制布局和现有文本合同兼容的前提下，
  补齐 .NET 10 `IUtf8SpanFormattable` 与 `IUtf8SpanParsable<TSelf>` 接口。
- 现有 `IParsable<TSelf>`、`ISpanParsable<TSelf>`、`ISpanFormattable` 继续保留；字符与 UTF-8
  `TryFormat` 必须直接写入调用方缓冲区，不得先构造格式化字符串。
- UTF-8 泛型入口必须与现有字符入口保持同一合法性规则、规范输出、Null 行为、IPv6 ScopeId
  拒绝规则和 MAC 的 G/N/D/C 格式合同，并增加接口存在性及往返测试。

## 2026-07-28 Networks IpAddressValue 地址族显式化与原生边界审计

- `IpAddressValue` 固定二进制合同继续为17字节；IPv4裸地址固定4字节，IPv6裸地址固定16字节，
  三者不得按调用处的缓冲区长度隐式猜测或互相覆盖。
- 公共构造与裸字节工厂必须显式区分IPv4和IPv6；删除依靠4/16长度自动判定地址族的
  `FromAddressBytes`入口，并通过编译错误完成全部调用迁移。
- `IPAddress`转换必须先读取其`AddressFamily`，再调用对应的IPv4或IPv6入口；带ScopeId的IPv6
  继续拒绝进入纯地址值，接口作用域保留在访问计划和原生端点结构中。
- 全面审计Networks源码中的DNS、Ping、ARP/NDP、Socket、路由快照、WFP及序列化边界，分别确认
  4字节IPv4、16字节IPv6、17字节`IpAddressValue`和7字节`MacAddressValue`没有错位。
- ICMPv6单响应与多响应必须使用同一个原生回复布局定义；不得分别保留不同的步长或
  `Status`、`RoundTripTime`偏移。
- 增加显式构造、错误长度拒绝、原生结构布局和单双响应一致性测试；完成Networks Debug/Release
  验证，并以真实IPv4、IPv6单播和IPv6多响应组播复核公共边界。
- Networks必须提供独立的`IpAddressValue`迁移手册，列出破坏性API替换、4/16/17字节边界、
  ScopeId归属、原生结构转换、禁止的兼容写法、消费者编译步骤和验收清单，并登记到文档索引。
- Aether已先使用本轮staging载荷完成源码候选迁移，随后从已安装的`1.0.38-beta.1`复验：消费源码中
  `IpAddressValue.FromAddressBytes`为零，新增IPv4/IPv6地址族、Exact目标、计划校验及ScopeId拒绝合同；
  Release测试242/242、DNS/DoH端到端36/36精确匹配通过。当前本机消费状态为
  `GENERAL_BETA_INSTALLED_VALIDATED`；该状态只表示普通β已安装验证，不代表正式版门禁完成。

## 2026-07-28 Iwesun.Runtime.Web ToXaml 文本投影修正

- `DomElement.ToXaml` 不得只输出容器、尺寸和背景而遗漏已经填充的运行文本。
- HTML 文本控件必须把 `content.ownText` 投影到 `TextBlock.Text`，按钮类投影到
  `Content`，输入类优先把 `state.value` 投影到 `Text`，并把
  `state.placeholder` 投影到 `PlaceholderText`。
- 文本投影必须继续使用源端初始化槽位；不得把 DOM 运行绝对坐标作为 XAML 初始化值。
- 公共 Runtime 只负责站点无关的 `ToXaml` 控件树投影。CSS 数据点、站点运行状态和布局契约
  由消费项目从同一 `DomElement` 树写入配套 C# 数据文件，禁止在 XAML 中读取外部文件。
- Runtime Web 领域测试必须覆盖文本、按钮和输入值投影，并验证空文本不产生伪造内容。

## 2026-07-25 Iwesun.Runtime.Web 公共 HTML/SVG 基础项目

- 在 Runtime 新建独立公共基础模块和程序集 `Iwesun.Runtime.Web`，统一承载与站点无关的
  HTML/SVG 元素类型、五类泛型槽位属性、强类型容器布局、属性审核、元素审核、累计统计和文本报告。
- 公共代码、程序集、命名空间、模块、项目和文件统一采用 `Iwesun.Runtime.Web` 名称；不得继续使用
  Doubao、Clone、Snapshot 或其他消费项目专属名称。
- 五类属性固定为 Space、Style、Effect、Action、DataOrganization。带槽位属性以
  `SlottedProperty<TValue>` 为泛型基础，全部实现审核接口并由具体属性覆盖详细审核。
- 每个元素只读发布默认 `XamlElementName`；它表示精确 XAML 元素名称，与较宽泛的
  `XamlControlFamily` 分开，动态输入类型可由消费转换器覆盖为更具体名称。
- 同名属性必须在一个包装内完整保存源端初始化/链接/运行值/值来源，以及 XAML 端
  初始化/链接/运行值/值来源；源端与 XAML 端的动态实现来源不得共用一个字段或靠推断替代。
- HTML 全部属性、CSS 值、DOM 运行值、事件和数据组织证据都必须能进入元素属性集合；
  当前无法翻译的属性也必须原样保存并标记人工复核，不得丢弃或进入消费项目旁路字段。
- DOM 树先只建立身份和父子关系，再递归调用具体元素自己的 `FillAsync`；每一种具体元素都必须
  声明自己的 Fill 规则和 `ToXaml` 方法。标准证据委托按作用域、XPath、属性、领域和槽位查询，
  并区分已捕获、已确认不存在和来源不支持；不得无条件回报全部请求成功。
- `ToXaml` 必须投影元素的全部槽位属性并递归转换子树。DOM 运行绝对坐标不得写成 XAML 初始化值；
  未翻译属性保留原因，动态布局保留强类型容器绑定，隐藏运行后再回填 XAML 运行槽位。
- 基类 `ToXaml(TextWriter)` 和受保护的整棵子树 `ToXaml(StringBuilder, depth, root)` 均允许
  子类整体覆盖并调用 `base`；基类内部固定拆成可独立覆盖的 `WriteOwnXaml` 与
  `WriteChildrenXaml`。自身节点只按“指定计算/绑定 → 局部容器布局 → 初始化常量”生成，
  初始化槽位未设置时不输出该 XAML 属性，运行时绝对坐标不得退化为初始化常量。
- 元素 `Audit()` 必须按容器后序递归：先审核全部子树，再审核容器自身；属性负责自身判定，
  元素负责属性遍历，容器负责递归，不为“无法翻译”建立第二套审核流程。
- 112 个 HTML 和 14 个 SVG 具体类必须分别重写自己的元素类型审核方法，发布不同的规则 ID、
  检查项、结论和文本；不得仅由公共基类按同一方法冒充逐元素审核。
- 全部具体 HTML/SVG 元素必须从新的 `DomElement` 对象模型继承；每个元素的规范专属属性必须声明在
  该元素类自身或语义明确的抽象家族基类中，并逐项附加 `ElementPropertyAttribute`。反射元数据必须
  明确属性是否需要 DOM 填充、XAML 运行回填、XAML 输出和审核；禁止继续使用无专属属性的类型壳，
  也禁止把不同元素的专属属性集中到外部 Fill、转换或审核功能类中。
- 每个 HTML/SVG 元素实例必须在公共基类中维护父元素、子元素、左兄弟、右兄弟和绝对 XPath，
  形成可校验、禁止循环引用的 DOM 树；元素目录每次创建独立实例，不得共享可变树状态。
- `DocumentScope`、绝对 `XPath`、父/子/兄弟 XPath 映射必须作为构造参数一次写入，创建后只读；
  Fill 委托不得重新查询或修改元素身份，运行时只允许通过受控 `AddChild` 建立对象引用关系。
- 每个元素实例必须提供结构化事件列表；首个公共动作能力为 Click，支持具名注册、取消注册、
  异步调用、取消令牌、阻止默认行为、停止传播和按父链冒泡。
- 容器自动布局必须使用强类型容器、参考盒、轴、约束、百分比基准和物理方向；字符串只保存原始证据，
  不得作为正式计算合同。
- 元素审核只遍历属性、读取 IsXXX/审核指示并调用属性自身审核；输出同时包含可跨元素累计的结构化数值
  统计和可显示、打印或由 C# 写入文件的文本说明。
- Runtime 公共项目不得依赖 DoubaoUIClone 的 DOM 快照 DTO、XAML 转换器、WinUI/WebView2 或站点策略；
  站点专属转换和 DTO 适配器继续留在消费项目。
- DoubaoUIClone 删除迁移后的重复公共实现，改为引用 `Iwesun.Runtime.Web` 项目；不得使用跨项目
  `<Compile Include>` 复制源码。
- Runtime 提供独立领域解决方案、中文稳定技术说明和公共测试项目；根解决方案登记发布产品项目，
  测试只进入领域解决方案。
- 迁移完成后必须验证 Runtime Web 领域 Debug/Release 测试、Runtime 根解决方案 Debug/Release 构建，
  以及 DoubaoUIClone Debug/Release 构建和完整测试。
- 本轮不自动提交、不推送、不发布、不安装 Runtime。

## 2026-07-25 新普通 β 发布准备

- 本轮目标是生成一个供其他消费项目同步升级和用户测试的普通β候选，不冒充正式版本；
  正式门禁未完成的能力必须在发布状态和测试指南中继续明确标记。
- 新候选不得覆盖、重建或复用已经分发的旧β版本号。Runtime与Networks分别采用新的预发布版本，
  同时保持Networks独立程序集/API版本边界。
- 发布资料统一包含：版本与状态、主要破坏性变更、强制迁移说明、Debug/Release DLL目录、
  文档目录、样例、测试指南、包/安装程序、文本清单及SHA-256校验清单。
- Runtime统一载荷必须包含Diagnostics、Data、Networks、WebView2及发布工具所需组件；
  Data只允许`Iwesun.Runtime.Data.dll`，Networks只允许`Iwesun.Runtime.Networks.dll`。
- Networks全部公共IP/MAC合同统一为`IpAddressValue`与`MacAddressValue`；旧类型、字符串地址记录、
  V2/V3后缀合同和兼容转发类型不得进入候选载荷。
- Debug与Release必须使用相同文件名、命名空间、公共类型和接口，只允许调试信息、优化方式及可查询
  版本/配置证据不同；两套目录均必须独立复核。
- `await RTask`必须在任务状态、Runtime执行快照和Managed注册清理全部完成后返回；运行中
  `Dispose()`只延迟底层Task释放，不得提前注销，也不得在实际完成后遗留注册。
- 候选生成前必须完成Data、Networks及相关模块的聚焦验证，以及Runtime根解决方案Debug/Release构建；
  长时间Aether统计测试和需要管理员改变网络状态的完整恢复矩阵不作为本轮候选生成前置条件，但必须
  保留为正式发布阻塞项。
- 本轮只准备和生成候选包、文档与校验资料；除非用户再次明确要求，不自动安装、不启动服务、不提交、
  不推送，也不上传公共NuGet源。

## 2026-07-24 正式发布前闭环修正

- Networks全部低层端点统一在发送FIFO前拒绝`Guid.Empty`；PowerShell、Process、ARP和IPv6邻居快照
  不再替调用方异步生成RequestId。
- System Socket UDP数据面定期回收超过空闲保留期的完全可用池；达到总池上限时优先回收可用池，
  任何包含Active或未到期Quarantine槽的池都不得回收。
- Networks删除`BinaryIpAddress`，所有合同、端点、证据和选择器统一使用`IpAddressValue`；
  不提供兼容包装或类型转发，以编译错误强制完成升级。
- `IpAddressValue`基础物理结构和固定二进制格式均为17字节，只保存128位纯地址及
  Null/IPv4/IPv6状态；ScopeId、接口索引、端口和分类缓存不得进入基础IP数值。
- 带`%zone`的IPv6文本不得在纯IP入口静默去除ScopeId；作用域必须通过访问计划、端点或路径证据
  单独表达。
- `MacAddressValue`基础物理结构和固定二进制格式均为7字节，只保存48位EUI-48数值及
  Null/Value状态；OUI、NIC标识和全部分类均为按需计算的只读属性，不得缓存为字段。
- ARP和IPv6邻居表记录不得继续暴露字符串IP/MAC；统一采用`IpAddressValue`和
  `MacAddressValue`，IPv6接口索引保持独立字段，未知MAC使用`MacAddressValue.Null`。
- WebView2证据会话启动失败必须完整回滚事件和已启用浏览器策略；Program停止失败必须保留失败项，
  允许后续无损补偿清理。
- Data继承表继续完全拥有`MergeAdd`业务逻辑；基类只确认成功ID属于当前Source活动记录。
- Data、Networks、WebView2领域解决方案按VS 2026 `.slnx`格式显式登记Any CPU、x86和x64，
  Debug/Release六组配置必须全部可解析并构建。

## 当前普通 β 发布

- Networks 源码、测试、示例、WFP 验证与文档已并入 Runtime，唯一活动程序集为
  `Iwesun.Runtime.Networks` `3.0.0-beta.3`，程序集版本为 `3.0.0.0`。
- Networks协议端点只允许`Network*Endpoint<TKey>`、非空`RequestedAccessPlan`及
  `RequestId → AttemptId → BranchId → ResponseId`四级GUID合同；V2、可空Route、旧Flags、旧处理器和
  旧路由适配器不得重新进入载荷。
- Runtime `1.0.36-beta.1` 统一提供 Diagnostics、Data、Networks、WebView2 的 Debug/Release DLL；
  消费项目仍必须从 `C:\Program Files\Iwesun\Runtime` 引用，不得直接引用 Runtime/Networks 源码工程。
- 当前状态统一为`GENERAL_BETA_READY_FORMAL_BLOCKED`。旧Networks `3.0.0-beta.1`/`beta.2`及Runtime
  `1.0.34-beta.1`/`1.0.35-beta.1`只保留为历史基线；本轮`3.0.0-beta.3`与`1.0.36-beta.1`
  允许通过MSI、便携载荷和内部文件源分发及安装。
  三轴终态、全协议源码审计、本机IPv6组播和DDNS Snap隔离DLL消费已经通过；完整Service恢复链和剩余M11门禁
  继续阻止正式版本冻结，但不阻止普通β联调。

## 2026-07-22 源码目录领域化整理

- Runtime 仓库根目录不再平铺全部项目；活动项目统一收纳到 `modules/<Domain>/`。
- 每个领域按职责使用 `src/`、`tests/`、`samples/`、`tools/`、`validation/`、`protocol/`、`release/`、
  `setup/` 和 `docs/` 子目录，只创建该领域实际需要的目录。
- 每个领域根目录提供独立 `.slnx`，覆盖该领域的实现、测试、样例、工具和验证项目，作为日常精细调试入口。
- 仓库根 `Iwesun.Runtime.slnx` 只包含实际参与发布的产品、发布聚合和安装工程，不纳入单元测试、规模测试、
  普通样例或 WFP 验收项目。
- 根解决方案显式登记 `Debug`、`Release` 和 `Publish`；全量 staging 只能通过
  `dotnet build Iwesun.Runtime.slnx -c Publish` 触发，发布脚本不得直接调用内部发布工程。
- 固定领域为 Diagnostics、Data、Networks、WebView2、Cli、RemoteConsole 和 Packaging；跨领域功能测试归
  Diagnostics 的 `tests/`，整体发布与安装工程归 Packaging。
- Data、Networks、WebView2 的领域文档随项目移动；仓库级设计、用户手册、发布说明和活动需求继续位于根 `docs/`。
- 本次只改变源码物理路径，不改变程序集名、RootNamespace、公共类型、DLL 文件名、NuGet 标识、安装布局或
  `C:\Program Files\Iwesun\Runtime` 消费合同。
- 必须同步更新解决方案、全部 ProjectReference、发布/安装脚本、样例模板、AI 指令、集成技能及活动文档中的源码路径。
- 迁移后仓库根只保留治理文件、`modules/`、`docs/`、`scripts/`、`skills/`、`artifacts/` 和忽略存档；旧项目根目录必须消失。
- 当前状态：目录迁移、领域解决方案、根发布解决方案、引用与脚本迁移已经完成；19 个活动工程全部登记，跨领域依赖显式进入相应领域方案，Debug/Release/Publish 与 Any CPU/x64 配置已由当前 VS/.NET `.slnx` 解析器和构建验证；等待提交前最终差异复核。
- 验收要求：Data 双配置86项测试、Networks双配置126项测试、Runtime Debug/Release全解决方案、staging、MSI、
  Aether 和 DDNS Snap DLL 消费编译全部通过。

## 当前正式基线

- Runtime 当前正式源码与安装包版本为 `1.0.32`。
- Runtime 数据能力只保留 `Iwesun.Runtime.Data.dll` / `Iwesun.Runtime.Data`；
  `RecordStore<TValue,TPrimaryKey>` 是唯一活动 RecordStore 实现。
- `Iwesun.Data.dll`、`Iwesun.Data`、DList、RecordStore V1 及其兼容别名不得重新进入活动源码、工程引用、
  文档入口或发布载荷。
- Runtime 1.0.32 已完成 Networks 与 Runtime Data Debug/Release 测试、Runtime Debug/Release 全解决方案
  构建、完整功能场景、staging 自检、安装版 SampleHost 双配置验证和 MSI Rebuild；Networks 2.0.1
  IPv6 Ping精确路由修复已通过DDNS Snap地址守护真实CLI联调，当前发布验证状态为通过。
- 发布器默认版本、WiX ProductVersion、Runtime程序集文件版本统一为`1.0.36` / `1.0.36.0`。
- Runtime 1.0.32嵌入`Iwesun.Networks` 2.0.1；Networks只允许`Tracked*V2`请求—响应规范，
  已被取代的旧HTTP、DoH、TCP、Ping、UDP、PTR和NBNS类型不得进入Runtime载荷。PowerShell、Process和
  本地快照端点作为非重复特殊模型保留。

## 当前架构边界

- XAML 只定义控件树、样式、模板和绑定；外部文件读取、反序列化、迁移、验证与错误处理必须在 C# 完成。
- 诊断默认保持静默；禁止新增 `Console.WriteLine` 或临时日志文件调试路径。
- 反射目标必须显式白名单；Debug-only 断点、监视和调用入口不得泄漏到 Release。
- Diagnostics、Management、WebRuntime、RemoteConsole 使用各自专用管道，并统一采用 4 字节小端长度前缀的
  `RuntimeDiagnosticFrame`。
- Runtime 不自动安装 MSI、不创建账号、不注册或启动 Windows 服务。

## 2026-07-23 Networks HTTP/DoH 失败证据保真

- HTTP/DoH 传输失败必须通过既有结构化失败合同区分超时、调用方取消、TLS 握手/证书失败、
  Socket 失败和其他传输失败；不得把全部异常压缩为同一个 `http-transport-failed`。
- 分类必须依据异常类型及内部异常链，不得解析本地化异常消息。HTTP 429、503 等协议状态继续保留为
  `NetworkFailureKind.Remote + StatusCode`，不得与 TLS 或 Socket 失败混淆。
- 本轮只细化既有 `ReasonCode`，不改变公共类型布局；消费程序可据此形成准确评价，但不得由缺失证据猜测根因。

## 2026-07-21 Networks 2.0.1精确路由修复证据

- IPv6 Ping按接口索引解析源地址时逐适配器隔离`NetworkInformationException` 10043；跟踪基类把初始和
  重试路由解析异常闭环为`route-resolution-threw`，不得终止发送线程或宿主进程。
- 已用Networks 2.0.1.0重建并启动DDNS Snap Debug Service，通过Runtime CLI调用
  `reflection.invoke service.ipv6-connectivity RunCheckNowForDiagnostics`；调用返回`true`，
  `LastCheckedUtc`更新，`LastDiagnosticError`为空，Service持续存活且管道继续响应。
- Networks Debug/Release各29/29、DDNS Snap Release 581通过/10跳过、Runtime staging自检和MSI Rebuild
  均通过，发布阻塞已解除。

## Networks下一次不兼容升级的路由RFC

- 批量网络请求必须具备真实的“整批启动、统一收集”语义：调用方提交一批独立 Request 后，端点不得在唯一发送线程上
  逐项完成昂贵的接口枚举和访问计划解析，再依次启动网络 I/O。整批登记后，各请求必须使用彼此隔离、不可变的
  访问计划并发完成解析和网络启动，最后按各自 RequestId 独立闭环并由调用方统一收集终态。
- 批量执行不得通过简单并发读取和覆盖共享的“当前路由快照”实现；每个 Request 的 Requested、Resolved、Actual
  证据必须绑定同一份请求级不可变解析结果，禁止一个请求读取另一个请求最后写入的接口或路由快照。
- `Send(ReadOnlySpan<TRequest>)`不得继续只表示“批量入 FIFO、单线程逐项解析”的性能提示。若保留该低层含义，
  必须另行提供名称和合同均明确的真实批量规划/启动 API；DDNS 扫描只能使用真实批量 API。
- DDNS 扫描批次预算是整批网络观察窗口，不得按候选数量线性扩大来掩盖串行启动。验收必须证明多网卡/Clash 环境下
  同批 IPv6 目标的发送启动时间集中在同一短窗口内，且在原有批次预算下不存在因内部排队产生的
  `PendingAtOuterTimeout`。
- 2026-07-28 本机DDNS Debug源码级集成验证：恢复`Normal=30000ms`、`Diagnostic=45000ms`、
  `PingDrain=350ms`后，IPv6候选9项全部在44～47ms闭环，9项Acknowledged、0项Failed、
  0项`PendingAtOuterTimeout`；修正前相同阶段终态耗时逐项累积到约2.5秒。Networks Debug/Release
  各189项通过，DDNS Debug 360通过/8跳过、Release 359通过/8跳过。
- 2026-07-28 Selene真实DDNS断点新增阻塞：`ff02::1`精确接口组播请求已被Networks接受并在
  3473ms后Acknowledged，但DDNS只观察到一条有效响应，后续NDP快照未激活Hades。Windows SDK
  `IPExport.h`证明`IPV6_ADDRESS_EX`为26字节压缩子结构，而外层`ICMPV6_ECHO_REPLY`按默认
  ULONG对齐，正式布局必须为`Status@28`、`RoundTripTime@32`、总步长36；当前源码及测试使用
  `Status@26`、`RoundTripTime@30`、步长34，导致第一条之后的回复每条错位2字节。修复必须使用
  与Windows ABI一致的显式结构布局，并增加两条及以上真实步长的解析合同测试；完成DDNS真实组播
  回归前，Networks多响应Ping发布状态为阻塞。修复后Networks Debug/Release各191项通过；
  Apollo DDNS Debug真实`ff02::1`回归在首个NDP快照得到11条Reachable邻居，其中包含Hades
  `fe80::...:7eaa`，证明36字节布局已恢复多响应激活。Selene安装新载荷后的同机回归仍为发布前门禁。
- 同一批量合同必须覆盖Ping、PTR、NBNS、精确UDP、TCP、HTTP和DoH，不能只修复Ping。上述端点均须显式启用
  请求级并发启动；批次中的路由解析、发送和接收按RequestId独立闭环，调用方的整批预算只取决于最慢请求，
  不得把各目标或各协议族的超时相加。
- 路由和接口快照的有效性必须按请求引用的不可变版本是否仍在有界版本集合中验证，不能与Provider最后写入版本做
  单值相等判断。并发解析每个目标都会产生新路由版本；把“不是最后版本”当作`resolved-plan-stale`会拒绝同批全部
  较早请求。只有版本已经从有界集合淘汰才是Stale，实际路径变化由Actual证据表达。
- Networks 3.0精确访问控制最终设计已停止征求意见并确认为`ACCEPTED`；旧路由RFC和总体规划仅保留设计依据，实施以
  `modules/Networks/docs/01-design/PRECISION_NETWORK_ACCESS_CONTROL_FINAL_DESIGN.md`为唯一权威合同。
- DDNS Snap真实IPv6组播联调曾确认：`NetworkPingEndpoint`在`ff02::1%InterfaceIndex`收到响应后，仍把精确接口
  约束固定判为`EvidenceIncomplete`，并错误降级为`NetworkFailureKind.Transport`。当前源码修正保留
  `ProtocolOutcome × AccessCompliance`双轴事实，对可证明的IPv6作用域接口发布`ApiBinding + Satisfied`，对无法
  证明的路径在发送前明确拒绝或保留证据不完整终态；不得以Transport冒充访问证据问题。完整反馈见
  `modules/Networks/docs/01-design/NETWORKS_3_0_EXACT_INTERFACE_PING_FEEDBACK.md`；本机接口19已得到
  `Succeeded + Satisfied`和独立Responder，接口8零响应已得到`TimedOut + Satisfied`；DDNS隔离DLL消费的真实DHCPv6
  源地址、接口19及公网目标测试已通过，完整Service恢复链因具有系统网络变更能力而未运行。
- 正式更正采用`TerminalState × ProtocolOutcome × AccessCompliance`三轴终态。只有底层API的实际目标、源地址、接口及
  IPv6 ScopeId绑定证据全部成立时，是否收到Echo Reply才只影响协议轴而不反向改变接口合规性；该严格前提下的IPv6
  组播窗口零响应应闭环为`TimedOut + Satisfied`，证据不全的零响应不得统一标记为`Satisfied`，
  有响应且路径可证明应为`Succeeded + Satisfied`。Actual Destination必须保留组播发送目标，响应方单播地址另列为
  Responder证据。跟踪基类不得再固定写入`EvidenceIncomplete`，全部协议端点必须审计双轴事实被布尔成功压缩的问题。
  实施批次和发布门禁见`modules/Networks/docs/01-design/NETWORKS_3_0_PROTOCOL_ACCESS_TERMINAL_CORRECTION_PLAN.md`。

- 对外统一使用非空`Guid RequestId`作为绝对跟踪身份；Pending、匹配、重试、完成和移除只按`RequestId`执行。
  `TKey`仅保留为允许重复的调用方查询数据；Attempt、竞速分支和多响应观测使用子GUID并显式关联根`RequestId`。
- Networks 3.0低层`Send`/`TrySend`必须在入队前拒绝`Guid.Empty`，不得异步换号；分支响应必须保留
  `RequestId → AttemptId → BranchId → ResponseId`完整父链。每个Attempt至少创建一个唯一`BranchId`，普通请求也不例外；
  不存在无Branch结果，不得用`null`、空GUID或序号代替任何一级身份。
- 候选方案废除`Route = null`兼容语义；请求必须显式提供非空路径模式，默认值必须被拒绝。
- 顶层候选模式为`Automatic`、`Direct`、`RouteAdapter`和`SystemProxy`；真实消费者审计已确认`Direct`还必须解决
  “固定目标但系统选择源/接口”“固定接口但系统选择源”和Preferred IPv6源地址竞速，不能简单限制为三个维度全部固定。
- 所有请求闭环统一返回实际目标、实际源地址、实际接口和实际路径类型；配置意图不得冒充执行证据。
- RS、DHCPv6 release/renew和网卡重启作为Networks公共恢复原语提供；默认Windows执行器接受管理员，或属于内置`Network Configuration Operators`组的服务身份；DDNS地址守护继续独立决定恢复顺序。
- 当前结论是四种顶层模式覆盖主要出站路径类别，但尚未完整覆盖数据报适配器、广播/组播多响应、PTR解析模型、
  实际证据不可用和入站Listener边界。RFC意见收敛前不得修改Networks公共API。
- 最终契约属于下一次破坏性版本，当前2.0.1不得引入半兼容实现；冻结后必须一次迁移Runtime、DDNS Snap与Aether。
- 路由语义RFC已扩展为完整精确访问控制规划：除四种路径提供者外，还必须系统表达传输目标、源地址、接口、
  下一跳/网关、本地端口、路由作用域、IP层政策和DNS Resolver等协议专属约束。只指定接口、只指定网关、只指定
  源地址等组合均使用非空正交选择器表达，由合法性矩阵、平台能力和Requested/Resolved/Actual证据闭环验证。
- Windows下一跳精确控制可能需要WFP连接策略；如果不能安全隔离并证明，不得静默修改全局路由或降级为系统选路。
- HTTP/DoH连接池键必须包含会改变路径或安全边界的稳定访问语义，但排除Request/Attempt/Branch GUID、超时、重试、
  时间戳和Actual观测值等瞬时字段；WFP请求级策略无法安全复用时必须禁用该连接复用。
- Aether已通过正式确认书接受最终设计并确认普通Attempt也创建Branch；发送前拒绝的Request只按RequestId终止，
  不伪造Attempt/Branch。后续不再征求设计意见；Networks 3.0原M0/M1设计冻结门禁已经完成，后续实施继续遵守
  M0～M12总体治理门禁及本轮新增F0～F6数据面门禁。
- 重试冻结RequestedAccessPlan：不得改变PathProvider、选择器类型、Required约束或规范候选集合；新的Attempt允许在原计划
  内重新解析，并允许PreferredSet选择不同成员、创建全新Branch。改变候选集合或放宽约束必须创建新的RequestId。
- Networks 3.0跟踪基类与协议端点的原等待限制已被本轮明确实施授权取代；当前按
  `modules/Networks/docs/01-design/NETWORK_FLOW_SERIAL_IMPLEMENTATION_PLAN.md`执行内部流水号、数据面合同和UDP长期池批次。
  DNS、NAT、HTTP/Socket代理及Packet后端本批只冻结可实现合同，未完成真实实现和验收前不得标记为已交付。

## UDP、DNS代理与路由数据面新增要求

- 内部高速跟踪统一使用`NetworkFlowSerial : uint`，唯一发号入口为非泛型静态
  `NetworkFlowSerialAllocator.GetNext()`；`0`无效，分配器必须原子登记、跳过活动及迟到隔离中的占用值，并在32位回绕时
  拒绝覆盖旧流。唯一性只要求覆盖当前运行跟踪域及可见生命周期，不承诺跨进程重启或跨计算机永久唯一。
- `NetworkFlowSerial`只作内部高速索引，不能冒充线上匹配键。UDP回包必须依据实际本地端点、实际远端端点、
  `SocketGeneration`及可用协议Token匹配；同Socket、同本地端口、同远端且无Token的并发请求必须分配不同端口或发送前拒绝。
- System Socket、WFP、未来VirtualAdapter和ExternalForwarder统一实现`INetworkDataPlane`分层能力；当前业务端点不得直接依赖
  Socket对象，以保证未来新增虚拟网卡或外部转发服务时不修改DNS、NAT、HTTP和Socket代理上层合同。完整设计见
  `modules/Networks/docs/01-design/NETWORK_FLOW_SERIAL_AND_FORWARDING_DATA_PLANE_DESIGN.md`。
- 通用单播UDP不得把“本地端口收到的第一个数据报”直接当作响应；底层必须核对实际远端IP和端口与当前Branch冻结目标一致，
  不匹配的数据报只能丢弃并继续等待，不能完成、失败或污染当前Request。
- 通用UDP已改为长期Socket/端口池。池槽在发送前完成`Bind(..., 0)`或精确端口绑定，并登记
  `SocketGeneration + LocalEndPoint + RemoteEndPoint + RequestId/AttemptId/BranchId`端口租约。
- System Socket UDP池按稳定访问计划和远端端口设置可配置槽位上限，工程β默认64；未到期隔离不得清零，达到上限时
  必须在发送前拒绝新Flow。正式默认值由全流量β实测的活动槽、隔离槽、单池峰值和容量拒绝次数决定。
- `Iwesun.Runtime.Networks.UdpBetaValidation`提供IPv4/IPv6回环结构化容量实测；64槽本机短基线已证明安全拒绝有效，
  但不代表用户全流量零拒绝。高速DNS必须使用协议Token共享Socket，不能无限扩大通用无Token端口池。
- `TrySend`只承诺入队；实际端口通过Branch发送证据事件或等待API发布。响应、超时或取消后租约必须进入迟到隔离期，
  通用无协议Token的UDP不得立即把同一端口复用于相同远端。
- PTR和NBNS属于低频协议，继续使用独立Socket和协议Transaction ID；本轮不迁移到通用UDP端口池。
- DNS代理必须建立客户端原始DNS ID与上游内部DNS ID的双向映射，并以SocketGeneration、上游端点和内部DNS ID共同匹配；
  UDP截断必须在同一Request/Attempt/Branch身份下执行TCP回退。
- DNS代理、四层NAT代理、透明IP代理和三层转发是不同数据面，不得用一个Endpoint类型混合实现；详细应用边界见
  `modules/Networks/docs/01-design/PROXY_ROUTER_APPLICATION_REQUIREMENTS.md`。

## 尚待真实环境完成

- 完整页面证据与请求到 DOM 跟踪器仍需在真实 WebView2 STA/UI 线程完成连续导航和采集验证。
- 消费宿主中的 WebView2 初始化失败回滚、Stop/取消/超时组合、重复启停和长期资源池压力仍需真实环境验证。

## 2026-07-29 HTML 运行时根与内存 XAML 对象流水线

- `Iwesun.Runtime.Web.HtmlDocumentRoot` 继续只负责元素树所有权、强查询身份、整树批量
  DOM Fill 与元素自有的 XAML 对象构建；每个 `DomElement` 必须持有同一个根引用并直接通过根查询，
  不得由 JSON 文件或旧四参数委托充当正式 Fill 通道。
- `Iwesun.Runtime.WebView2.HtmlRuntimeDocumentRoot` 是业务按钮与 CLI 共用的正式启动编排基类，
  内置 `HtmlRuntimeWebView2Context`，保存导航 URL、绝对 DOM XPath 点击序列、直接 DOM 查询会话、
  CSS/布局/运行状态公共连接点和事件连接点。
- 固定阶段顺序为 Navigate、Build、Fill、BuildXaml、DisplayXaml、XamlFill、AuditXaml、
  WriteXaml。
  Build 只挂载由元素工厂创建的正式类型树；Fill 必须先提交一次完整强类型查询批次，再由各元素
  自己遍历反射槽位；BuildXaml 直接创建 XAML 对象并写入 `DomElement.XamlElement`，不得先写
  XAML 文件再反读对象。
- Display 后的 XamlFill 必须通过 `IXamlPropertyQueryEngine` 读取真实显示对象，Audit 调用每个
  元素自己的双向槽位审核；DOM 值不得回填冒充 XAML 运行值。只有完成该审核的对象树才允许交给
  `IHtmlRuntimeXamlDocumentWriter` 持久化。
- BuildXaml/ToXaml 只负责生成对象或文档计划，不得调用 `ApplyXamlQueryResult`，不得把生成
  属性、合成文本或转换后的常量预写入 XAML 证据槽。XAML 初始化、链接和运行槽只能由 Display
  之后的 `IXamlPropertyQueryEngine` 查询结果写入。
- 当 DOM 源槽有值而真实 XAML 查询返回 `TargetUnsupported` 时，目标槽必须保持未设置，使双向
  审核报告 `MissingXamlValue`；禁止保留生成阶段的旧值，也禁止把“不支持读取”降级为通过。
- `WebRuntimeDomQueryBridge` 是 Web 元素槽位与 WebView2 查询契约之间的唯一枚举转换点。
  Owner、Category、Slot、ValueSource 与 LinkKind 必须显式逐值映射；禁止依赖枚举整数顺序，
  特别是 Web 的 `ContainerLayout` 不存在于 WebView2 通用链接枚举中。
- Build 必须先通过 `IWebRuntimeDomTreeSession` 取得强类型、分文档作用域的元素节点。
  顶层和 iframe 内部 XPath 都保持浏览器本地绝对 XPath，不拼接外层路径；iframe 外层元素身份只作为
  `DocumentScope`。`DomElementTreeBuilder` 必须双向核对 parent/children，拒绝缺边和重复强身份。
- `DomElementRuntimePropertyCatalog` 为每个实时 HTML/SVG 元素登记几何、状态、内容、资源、效果以及
  CSS 布局/外观运行属性；DOM 捕获同时把页面实际 attribute 名称交给元素类型。没有登记槽位就不可能
  产生 Fill 查询，因此不得把空 `RuntimeProperties` 当作合法完整树。
- `WebRuntimeScriptDomEvidenceReader` 对每个文档作用域一次提交整批固定查询程序，iframe 使用稳定
  FrameIndex；调用者只能看到 typed request/result。绝对几何初始化明确为 absent，只作为运行时验收，
  CSS/布局 Link 不能由 computed value 冒充，必须交给已连接的 CSS/布局证据连接点。
- 单属性 Fill 必须按槽位区分语义：`rect.*` 只在 Runtime 保存实时坐标/尺寸；CSS
  Initialization 保存 authored declaration、Link 保存 stylesheet/selector/declaration 身份、
  Runtime 保存 computed value；状态和内容 Initialization 保存 attribute/defaultValue/同修订版
  直接文本，Runtime 保存当前 property/text 状态。禁止把同一个 live/computed 值复制进两个槽位。
- 内存 XAML 构建完成后，嵌套文档根必须自动挂入对应 iframe `Content` 槽，禁止把 iframe 根留作
  独立 0×0 XAML 根。事件连接点在 Build 阶段按 DocumentScope + XPath + PropertyName 强身份挂入
  元素事件集合，缺失目标和重复事件身份均硬失败。

## 2026-07-20 WebView2 CSS 状态恢复证据

- `WebRuntimePageEvidenceCapture` 的公共页面快照必须同时保存浏览器最终运行值与原始 CSS 定义来源，不能只保存
  `getComputedStyle()` 结果。
- 最终运行值继续包含逐元素几何、可见性、完整计算样式和伪元素计算样式。
- 新增逐元素 CSS 来源链，至少记录 DOM 路径、CDP node/backend node、匹配选择器、样式表 ID 与 URL、规则
  origin、源码范围、声明值、`!important`、继承链、inline/attributes style、伪元素规则和 CSS keyframes。
- 来源链必须由 Runtime 内部固定 CDP 方法生成，不向宿主暴露任意 CDP 方法名或 JavaScript 文本。
- CSS 原文继续由 `maximum-template.css` 保存；来源链通过 `styleSheetId` 与范围指向原文，避免为每个元素重复复制整份 CSS。
- 同源 iframe 与开放 Shadow DOM 尽量保持与 `complete-dom-properties.json` 一致的路径；浏览器无法解析的节点必须
  明确记录失败，不得伪造来源。
- 页面证据清单、API 文档、能力文档、发布状态和消费项目采集清单必须同步更新。
- “原始定义”不限于规则文本：必须额外保存决定当前状态的布局关系和条件，包括 parent/offsetParent、Shadow/slot、
  Flex/Grid/定位/尺寸/对齐/书写方向/contain/container 的语义值、活动伪类/表单状态、滚动状态、当前动画与 timing。
- 必须保存页面级样式环境和条件结果，包括 viewport/DPR、颜色方案、motion/contrast/forced-colors、pointer/hover、
  orientation、语言/方向、CSS media query 条件与当前匹配结果。
- 原始声明值（如 `auto`、百分比、`fr`、`minmax()`、`calc()`、CSS variable）由匹配规则/inline/stylesheet 原文保留；
  转换器不得用最终像素值覆盖这些布局语义，只能把绝对值作为最终校准和验收依据。
- 真实动态页面的 CDP DOM 与匹配样式 JSON 允许超过 `System.Text.Json` 默认深度 64；固定证据 API 必须使用明确的
  深层只读解析上限，并由消费方采用一致上限，不能把浏览器合法深层结构误报为采集失败。
- `CSS.getMatchedStylesForNode` 返回的共享规则和继承链不得按元素重复原样落盘；`style-provenance.json` 必须使用
  全局规则目录去重，元素仅保存有序规则引用、元素自身声明、继承层级、伪元素与关键帧引用。去重不能丢失 selector、
  stylesheet、origin、source range、声明值、`!important` 或规则应用顺序，消费方必须按引用还原同一逐元素来源模型。
- 来源采集必须保持有界内存和可观察进度。正常数千元素页面不得因重复证据膨胀到 GB 级文件；未完成的输出不得生成
  成功 manifest，取消或失败后宿主可以把该时间戳版本判为失败。

## 2026-07-23 WebView2 CSS 条件与属性数据点连接

- `style-provenance.json` 不得只保存元素到匹配规则的引用；每个规则定义还必须保存其有效
  `media`、`container query`、`supports` 与 scope 条件链、当前活动结果及稳定条件 ID。
- `layout-state.json:environment.conditionalRules[]` 必须为条件规则提供稳定 ID、父条件链和样式表来源定位；
  `style-provenance.json:definitionCatalog[]` 使用相同 ID 引用，消费方不得按 selector 文本猜测连接。
- 条件链必须覆盖同一规则外层的嵌套条件；只保存页面级 media query 列表不构成元素属性到条件的完整连接。
- CSS variable 引用、规则 ID、source range、条件 ID 和最终 computed value 必须能够由消费方合并成同一个属性数据点；
  视口或状态改变时，消费方只重算数据点并通知全部属性绑定，不重新生成控件树。
- 公共证据 schema 必须升级版本并同步 API 文档、能力文档、Release 状态与可编译 SampleHost/消费验证。
- 验收至少证明：同一媒体规则绑定多个元素时只产生一个共享条件数据点；跨断点后规则活动状态切换，
  所有引用属性同步改变；无条件规则恢复时不丢失级联顺序。

## 2026-07-23 WebView2 HTTP 视觉资源完整证据

- 对要求“一次导航得到完整原始响应正文”的采集宿主，公共网络证据选项必须支持在首次导航前仅清理 WebView2
  `DiskCache` 与 `CacheStorage`，并在证据会话期间禁用网络缓存和绕过 Service Worker；不得清除 Cookie、登录状态、
  LocalStorage、IndexedDB、Service Worker 注册或其他业务数据。会话释放时必须恢复缓存与 Service Worker 开关。
  高并发响应不得逐项争用并重写共享目录；宿主必须可选择先持续写会话暂存区，再在快照导出时一次批量进入内容寻址公共库。
  消费宿主不得把缓存未产生响应事件误报成原正文已保存。
- `WebRuntimeNetworkEvidenceSession` 必须通过公共、可配置的正文捕获策略保存页面复现所需的原始应用响应正文，除原有
  文档/文本/JSON 外，至少覆盖 `image/*`、SVG、ICO、PNG、JPEG、GIF、WebP、AVIF、BMP、WOFF/WOFF2、TTF、OTF 和 EOT。
- 保存的是 `CoreWebView2WebResourceResponseView.GetContentAsync()` 返回的浏览器应用响应正文，不得声称是 TLS 线上压缩字节；
  每项必须记录方法、URL、状态、去敏响应头、MIME、长度、SHA-256 和内容寻址对象路径。
- `GetContentAsync()` 对已观察响应返回不可用错误时，Runtime 必须可按同一固定 CDP `requestId` 使用内部
  `Network.getResponseBody` 作受限后备读取；不得向宿主开放任意 CDP 方法。清单必须记录正文来源，两个入口均只声明为
  浏览器应用响应正文，后备也必须遵守相同单项字节上限、哈希、失败记录与敏感数据边界。
- 正文策略必须是显式公共契约，提供安全默认值、最大单项字节数、允许类别与扩展名；业务程序直接通过
  `WebRuntimeNetworkEvidenceOptions` 使用，同一实现不得在消费项目重复维护。
- 页面导出清单必须保留资源 URL 到 `resource-container-map.json` 的连接，并区分正文已保存、按策略跳过和保存失败。
- WebRuntime 命令面必须提供会话状态与策略查询；业务宿主把真实 WebView2 会话注册到公共命令目标后，CLI v3 使用标准
  Runtime Frame/Diagnostics Proxy 调用，不新增私有线协议、不暴露任意 CDP 或脚本文本。
- 同步更新 CLI 系统 JSON 配置、帮助元数据、Shell 命令示例、可编译 SampleHost、完整页面证据文档、能力/发布状态和
  `iwesun-runtime-integration` 技能。JSON 示例必须写出完整 typed arguments 与响应字段。
- DoubaoUIClone 按钮 1 只能调用安装版 `Iwesun.Runtime.WebView2` 公共 API；删除宿主内重复视觉资源响应抓取器，并用
  小型覆盖清单验证 CSS 双源、运行时绝对布局和 HTTP 视觉资源正文证据。
- 视觉资源应用映射不得只检查 `src/href/data/poster`。必须保留响应式图片的 `currentSrc/srcset/sizes`、SVG
  `href/xlink:href`、CSS `background/mask/content/cursor/list-style/border-image` 中每个 URL、伪元素 URL、作用属性、
  原始值、解析后绝对 URL、元素 XPath 与呈现容器；开放 Shadow DOM 和可访问 iframe 使用稳定作用域路径。
- Runtime 不得通过 Canvas、屏幕截图或重新编码图片来替代 HTTP 正文。`data:` 与内联 SVG 必须标为内联来源，
  `blob:` 必须标为非 HTTP/待解析来源；只有 HTTP(S) 引用才可声称已连接到 HTTP 响应正文。

## 2026-07-20 Runtime 仓库全量清理

- 保留活动源码、工程文件、权威文档、样例、配置、技能和发布脚本。
- 历史计划、任务书、旧交接、旧审计答复和旧候选发布说明统一进入 `docs/archive/`；该目录由
  `.copilotignore` 与 `.codexignore` 排除，不作为当前权威入口或发布载荷。
- 删除全部 `bin`、`obj`、`artifacts`、`logs`、发布 staging、MSI/WiX 中间文件、测试输出、临时探针和
  本机 `.user` 文件；需要时由构建或唯一全量发布入口重新生成。
- 清理只作用于 `D:\Git Space\Runtime`，不得修改相邻仓库。

### 清理验收

- [x] 历史参考材料已集中到忽视存档目录。
- [x] 生成物、中间文件、本机配置与临时探针已删除。
- [x] 活动文档、发布清单和忽略规则完成一致性检查。
- [x] Git 差异只包含预期归档、删除、清理入口和索引修订。
## Live HTML-to-XAML object lifecycle

- A click-driven navigation workflow may finish at a different path on the same
  origin. DOM-tree ownership validation must accept that final same-origin URI
  while still rejecting evidence from another origin.
- The product host must keep navigation, direct DOM Build/Fill, live XAML object
  construction, display, XamlFill, and audit in one process. Files are optional
  output after audit, never an object-transfer mechanism between those stages.
- The typed DOM construction snapshot must retain each element's direct text
  nodes as initialization evidence. `content.ownText` initialization reads that
  same-revision evidence so a reactive re-render between Build and Fill cannot
  silently erase text; runtime text remains a live query.

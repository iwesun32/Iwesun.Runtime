# Iwesun.Runtime.Web 深度审计报告：数据获取与 XAML 对等转换

> [!CAUTION]
> **2026-07-29 静态复审更正：本报告原结论无效，当前实现未通过 HTML→XAML 静态语义门禁。**
>
> 原报告把“属性名称出现在目录中”等同于“属性填充符合 HTML 规范”，又把
> “具体类声明了 `CreateXaml()` 并返回一个工厂已知名称”等同于“XAML 对等转换正确”。
> 这两个推论都不成立。`202/202` 旧测试只证明旧断言通过，不能证明 113 个元素的
> HTML 内容模型、状态分支、XAML 类型、可写属性和子槽位兼容。
>
> 静态门禁全部通过之前，禁止恢复“架构总体优秀”“大部分映射正确”“可进入动态
> 调试”等结论。

## 2026-07-29 强类型重实现进度（覆盖下方旧源码反例）

本节是当前源码状态；下方 `0.1`、`0.2` 保留的是重实现开始时的失败基线，不能再
拿其中旧类名和旧测试数描述当前代码。

已完成并由静态测试执行的修正：

1. `CreateXaml()` 保持抽象，113 个最终 HTML 类型必须由自己的最终类声明实现；
   `HtmlDomElementArchitectureAudit` 现检查方法归属，禁止标准类型退回
   `HtmlGenericDomElement` 或 `ConservativeContainer`。
2. 审计会实际构造 XAML 对象计划，并检查可视/非可视根数量以及
   `DirectChildren/Inlines/Content/Items` 与头部对象的槽位兼容性。该门禁已经
   发现并修正 TextBox、Image、MediaPlayerElement、option、meter、progress
   等类型继承错误子槽的问题。
3. `input` 已按 22 个规范状态强类型分派。date、month、week、time、
   datetime-local、file、image 不再冒充通用 TextBox/Button，而是使用各自的
   WinUI 组合控件；hidden 不生成可视对象。每个状态只写目标控件真实拥有的属性。
4. 普通 block flow、CSS flex、CSS grid、inline flow 已分开；标准最终类不再
   通过公共猜测器选择头部。复合 phrasing 使用专属换行面板，`br/wbr` 使用专属
   断行对象。
5. ul/ol/menu、li marker、details/summary、datalist、optgroup、fieldset、
   dialog、meter/progress 已建立各自的对象结构和状态分支。
6. table/section/row/cell 使用强类型二维面板；colgroup/col 为非可视轨道元数据；
   colspan、共享列宽、rowspan 和行列间距已进入实际测量/排列算法。
7. picture 只承载最终 img；map/area 本身为非可视定义，关联 `img[usemap]` 时生成
   Image + hotspot overlay，rect/circle/poly 坐标进入专属热点对象。
8. iframe/embed/object/canvas 不再共用空 ContentControl，已分别使用嵌套文档、
   typed embedded content、object fallback、drawing surface 强类型对象。
9. WebView2 证据增加 selected media、embedded source、poster 与
   checkbox indeterminate；HTML Initialization 严格使用 `getAttribute()`，
   当前 DOM property 只进入 Runtime 槽。
10. 新增 113 项强类型合同目录，逐项限定允许的最终 XAML 头部；宏观布局元素
    逐类验证 block/flex/grid 三种运行时 formatting context，不能以默认状态
    代替多状态判别。
11. 当前门禁基线：Runtime Web Debug/Release `264/264`、Runtime WebView2
    Debug/Release `22/22`；DoubaoUIClone App Debug/Release win-x64 构建均为
    0 警告、0 错误。
12. CSS 链接刷新不再永久缓存首次命中的规则；活动 `@media`/`@supports`
    条件进入规则身份。DOM 刷新同时重查 Link 与 Runtime，已显示/Audited 阶段
    仍可刷新；隐藏源 WebView2 保留到窗口关闭，窗口尺寸变化后重新计算 CSS 并
    把 computed value 写回已注册的 XAML 属性目标。结构属性变化时不再只改标量：
    现会清理旧投影引用、重新调用每个最终 HTML 类自己的 `CreateXaml()`，重建
    Grid 轨道、子槽位和对象类型，并替换已显示的 WinUI 树。
13. `label` 已改为专属关联面板；显式 `for` 和隐式后代标签控件均进入激活路径。
    `input[list]` 会从同一已挂载元素树解析 datalist/option，创建专属建议输入控件，
    不再把 datalist 当普通可视容器或丢弃它的数据。
14. `bdi/bdo` 已分别建立叶文本与复合子树的隔离/覆盖专属类型；`video/audio`
    使用专属媒体组合控件，选中媒体、transport、poster 与 `track` timed-text
    描述一并进入对象。`object` 使用可呈现受支持图像并在不支持时显示 fallback
    子树的专属容器。
15. 修正 `state.value/state.placeholder` 与正式目录
    `content.value/content.placeholder` 的串槽；当前表单值只从 Runtime 证据读取，
    HTML `value/placeholder` attribute 仍留在 Initialization。文本语义样式在没有
    authored/link 时允许使用 computed UA 默认值，但几何、宽高和绝对坐标仍禁止
    反向写成初始化值。
16. 表单约束验证增加 `state.valid`、`state.willValidate` 与
    `state.validationMessage` 三条 Runtime 证据，并投影到 WinUI 附加状态；
    XAML 查询引擎从真实显示对象读回这些状态。checkbox/option/textarea 等原先
    错读 Initialization 的 live state 也已改为读取 Runtime 槽。
17. CSS 单属性链接补充 shorthand 展开、继承来源、活动
    `@media/@supports` 条件和 `var(--*)` 依赖身份；computed value 与 authored
    link 仍分别保留，未压成同一个槽。
18. canvas 在导航前安装固定且有界的 `CanvasRenderingContext2D` 命令跟踪器，
    命令以 `resource.canvasCommandStream` Runtime 证据进入元素对象，再由
    `HtmlCanvasSurface` 重放矩形、折线、二次/三次曲线、圆弧/椭圆、状态栈、
    仿射变换、透明度、虚线、文本和原始 HTTP 图像源。Canvas gradient/pattern
    使用稳定资源 ID、color-stop/transform 命令与 XAML Brush 对象关联；该链路
    不使用截图，也不冒充 HTTP 原始图像正文。
19. form/input/textarea/select/option 现保存表单 owner 与初始化状态；reset 在
    WinUI 内恢复文本、勾选、数值、日期和选择状态，submit 只产生本地可订阅
    命令事件，不发送网络请求。
20. `effect.animations` 不再压成分号字符串；WebView2 现保存 timing、
    computed timing、keyframes、play state、current/start time 与 playback
    rate 的结构化时间轴。根对象新增独立动画管理器，分别持有 authored link、
    DOM runtime timeline 和 XAML target。WinUI 已实际物化 opacity keyframes；
    审核读取 XAML 自己的运行时间轴，禁止把 DOM JSON 原样回填冒充 XAML 值。
21. 动画执行器已扩展到 opacity、width/height、translate/scale/rotate/matrix
    transform 和前景/背景颜色；未物化的 keyframe 属性明确进入
    `unsupportedProperties`。动画审核使用独立结构比较器核对名称、持续时间、
    keyframe 属性集合和未支持项，不再用数字正则或源 JSON 字符串相等代替。
22. Canvas 审核不再读取 `CommandStream` 输入值冒充 XAML 运行结果。
    `HtmlCanvasSurface` 现在生成 executed/partial/unsupported 实际回放证据，
    专属比较器逐命令计数；clip、ImageData、非默认 composite、shadow/filter
    等未实现命令会使审核真实失败。
23. Canvas 矩形 path clip 已物化为 WinUI 裁剪层；连续 clip 形成嵌套交集，
    save/restore 保留独立裁剪状态。任意曲线路径 clip 没有被降级成包围盒，仍明确
    失败。`filter: opacity()` 与 `destination-over` 已执行；复杂 filter、彩色
    shadow 和非等价 composite 继续标记 partial/unsupported。
24. `putImageData` 不再记录为 `[object ImageData]`。导航前跟踪器以 4 MiB
    上限保存 RGBA 命令证据，WinUI 端执行 RGBA→预乘 BGRA 转换并创建真实
    `WriteableBitmap`；超限或 dirty-rectangle 分支不会冒充完整执行。

尚未因此宣称“全部 HTML 规范完成”。仍需继续逐项硬化：

- 113 类的全部合法属性非空样本与值域门禁，而不只是默认实例；
- bidi、ruby、自动引号、上下标已建立专属对象，但还需真实字体基线与复杂嵌套
  文本顺序的运行时审核；
- form submit/reset、约束验证、label/for 与 datalist/list 已建立本地行为、
  证据和对象状态，但仍需覆盖所有表单状态组合；
- media track、object fallback 与 canvas 主要路径/画刷命令已建立；矩形 clip、
  opacity filter、destination-over 和有界 ImageData 已执行，仍需任意路径 clip、
  精确 shadow/复杂 filter、其他 composite 与媒体默认轨道选择的运行时审核；
- CSS Link 已记录活动媒体条件、变量依赖和结构化动画时间轴；opacity、transform、
  color、width/height 已有 XAML 执行器，仍需其他布局/画刷属性和完整级联来源门禁；
- 上述静态门禁全过后，才进入真实 WinUI 运行值双向审核。

## 0. 静态复审的判定标准

每个 HTML 最终类型必须逐项通过以下门禁，不能用同族元素或当前豆包页面未出现该
元素来代替：

1. HTML 元素类别、内容模型、闭合规则和默认呈现语义正确。
2. 标准属性集合精确，并且属性的值语法、适用状态和缺省规则正确。
3. Initialization、Link、Runtime 三类 DOM 证据使用正确 API，查询名不能串槽。
4. 多态元素先按 HTML 设计状态判别，再由最终类型自己的 `CreateXaml()` 创建对象。
5. XAML 头部类型能够表达该 HTML 状态；不能只保证工厂“认识这个类名”。
6. 每个输出属性在该 XAML 类型上真实存在、可写、值类型可转换。
7. 子对象槽位与父 XAML 类型兼容，并保持 HTML 的顺序、文本流和替代内容语义。
8. 非可视元素仍须保留资源、样式、事件、URL 基准或模板连接，不能等同于丢弃。

## 0.1 已由源码直接证实的系统性失败

| 编号 | 静态失败                               | 源码证据                                                                                                                                              | 结果                                                                                         |
| ---- | -------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------- |
| S-01 | 普通块流被当作无轨道 `Grid`            | `HtmlContainerDomElementDefinition` / `HtmlSectioningDomElementDefinition` 使用 `DirectChildren`，大量最终类型固定返回 `Grid + ConservativeContainer` | 多个块级兄弟会占据同一 Grid 单元并重叠，不等于 HTML normal flow                              |
| S-02 | 内联内容没有生成 `Inline` 对象         | `HtmlPhrasingDomElementDefinition` 声明 `Inlines`，但具体子元素仍创建 `TextBlock`、`Button` 等；公共路径在有子项时改成 `StackPanel`                   | `TextBlock.Inlines` 无法接收这些对象；横向 `StackPanel` 也不具备文本整形、换行和基线语义     |
| S-03 | 列表/表格的槽位与头部类型冲突          | `HtmlListDomElementDefinition` 和 `HtmlTableDomElementDefinition` 声明 `Items`，最终类型却返回 `Grid`                                                 | `WinUiXamlElementObjectFactory.AttachChild` 仅允许 `ItemsControl` 使用 `Items`，静态必然失败 |
| S-04 | `input` 状态机被压成一个 `TextBox`     | `HtmlInputDomElement.CreateXaml()` 无条件返回 `TextBox`                                                                                               | hidden、checkbox、radio、range、number、date/time、file、button/image 等状态全部错误         |
| S-05 | 数据源查询名与 WebView2 查询实现不一致 | 数据源使用 `content.value`、`state.checked`；`WebView2DomPropertyFiller` 只专门识别 `value`、`checked`，其余退回 `node[propName]`                     | `node['content.value']` / `node['state.checked']` 不存在，静态可判定为错误查询路由           |
| S-06 | Initialization 查询混用了 DOM property | `QueryInitializationAsync` 在 attribute 不存在时对布尔 `node[name]` 返回 `"true"`                                                                     | 设计属性槽不再忠实表示 `getAttribute()`；IDL 当前状态被写入 HTML 初始化证据                  |
| S-07 | 旧“完整属性”门禁只检查标签级属性并集   | `HtmlDomElementArchitectureAudit.Inspect()` 只比较 `GetSupportedAttributes(tag)` 与反射名称                                                           | 没有检查 `input type` 等状态下属性是否适用、缺省值、枚举域和互斥约束                         |
| S-08 | XAML 属性测试只在“值存在时”碰巧执行    | 旧架构测试没有为每种属性构造合法非空样本，也没有逐分支实例化                                                                                          | 不可写属性、错误值类型和遗漏分支可在 202 个测试全部通过时继续存在                            |

## 0.2 113 个 HTML 元素逐项静态复审登记

状态含义：`失败` 表示已有源码反例；`部分` 表示基础头部可用但规范语义未完整实现；
`通过` 只用于当前静态职责确实完整的非可视简单元素。任何 `失败/部分/待审` 都使
总门禁失败。

|    # | 元素              | 当前 XAML 头部         | 判定 | 首个静态失败或待补门禁                                                        |
| ---: | ----------------- | ---------------------- | ---- | ----------------------------------------------------------------------------- |
|    1 | `html`            | `Grid`                 | 部分 | 视口根可创建，但还未证明 head/body 范围、滚动根和 viewport 布局连接完整       |
|    2 | `head`            | 非可视                 | 失败 | 被统一声明为 `ContentModel.None`，未表达 metadata content 与全局资源连接      |
|    3 | `title`           | 非可视                 | 部分 | Raw text 可保留；文档标题连接尚无逐属性门禁                                   |
|    4 | `base`            | 非可视                 | 部分 | `href/target` 已声明，但 URL 解析基准没有进入根连接                           |
|    5 | `body`            | `Grid`                 | 失败 | 只分派 flex/grid；普通 block flow 仍为无轨道 Grid                             |
|    6 | `header`          | `Grid`                 | 失败 | 固定 Conservative Grid，未按实际 formatting context 创建                      |
|    7 | `footer`          | `Grid`                 | 失败 | 固定 Conservative Grid，未按实际 formatting context 创建                      |
|    8 | `main`            | `Grid`                 | 失败 | 仅识别 flex，grid/table/normal flow 不完整                                    |
|    9 | `nav`             | `Grid`                 | 失败 | 仅识别 flex，且导航 landmark 未落到自动化语义                                 |
|   10 | `section`         | `Grid`                 | 失败 | 仅识别 flex，普通块流与 grid/table 不完整                                     |
|   11 | `article`         | `Grid`                 | 失败 | 固定 Conservative Grid，普通块流错误                                          |
|   12 | `aside`           | `Grid`                 | 失败 | 仅识别 flex，普通块流错误                                                     |
|   13 | `address`         | `Grid`                 | 失败 | 固定 Conservative Grid，普通块流错误                                          |
|   14 | `figure`          | `Grid`                 | 失败 | 固定 Conservative Grid，figure/caption 排列未实现                             |
|   15 | `figcaption`      | `TextBlock`            | 失败 | 开放内容的子元素不是 `Inline`，复合路径退化为 StackPanel                      |
|   16 | `div`             | `Grid`                 | 失败 | flex/grid/table 有分支，但 normal flow 仍会重叠                               |
|   17 | `p`               | `TextBlock`            | 失败 | 内容模型错误标成 Flow；嵌套 phrasing 对象不能进入 Inlines                     |
|   18 | `h1`              | `TextBlock`            | 失败 | heading 默认字号/粗细/级别语义未实现，内联子树不兼容                          |
|   19 | `h2`              | `TextBlock`            | 失败 | heading 默认字号/粗细/级别语义未实现，内联子树不兼容                          |
|   20 | `h3`              | `TextBlock`            | 失败 | heading 默认字号/粗细/级别语义未实现，内联子树不兼容                          |
|   21 | `h4`              | `TextBlock`            | 失败 | heading 默认字号/粗细/级别语义未实现，内联子树不兼容                          |
|   22 | `h5`              | `TextBlock`            | 失败 | heading 默认字号/粗细/级别语义未实现，内联子树不兼容                          |
|   23 | `h6`              | `TextBlock`            | 失败 | heading 默认字号/粗细/级别语义未实现，内联子树不兼容                          |
|   24 | `hgroup`          | `Grid`                 | 失败 | heading group 的块流与可访问级别未实现                                        |
|   25 | `span`            | `TextBlock/StackPanel` | 失败 | 有子项时改横向 StackPanel，破坏内联文本整形与换行                             |
|   26 | `strong`          | `TextBlock`            | 失败 | strong 默认强调样式和真正 Inline 子树均未实现                                 |
|   27 | `em`              | `TextBlock`            | 失败 | emphasis 默认斜体语义和真正 Inline 子树均未实现                               |
|   28 | `b`               | `TextBlock`            | 失败 | 默认粗体呈现未实现，内联子树不兼容                                            |
|   29 | `i`               | `TextBlock`            | 失败 | 默认斜体呈现未实现，内联子树不兼容                                            |
|   30 | `u`               | `TextBlock`            | 失败 | 下划线文本装饰未实现，内联子树不兼容                                          |
|   31 | `s`               | `TextBlock`            | 失败 | 删除线文本装饰未实现，内联子树不兼容                                          |
|   32 | `small`           | `TextBlock`            | 失败 | 相对字号语义未实现，内联子树不兼容                                            |
|   33 | `code`            | `TextBlock`            | 失败 | UA 等宽字体语义未实现，内联子树不兼容                                         |
|   34 | `pre`             | `TextBlock`            | 失败 | 空白保留/不折叠与滚动语义未建立                                               |
|   35 | `blockquote`      | `Grid`                 | 失败 | 普通块流错误，`cite` 仅留作运行数据                                           |
|   36 | `br`              | `TextBlock`            | 失败 | 应产生内联换行，不应创建独立 TextBlock                                        |
|   37 | `wbr`             | `TextBlock`            | 失败 | 可选断行机会无法由独立 TextBlock 表达                                         |
|   38 | `abbr`            | `TextBlock`            | 失败 | 内联子树不兼容，title/提示语义未专属验证                                      |
|   39 | `bdi`             | `TextBlock`            | 失败 | 双向隔离未映射                                                                |
|   40 | `bdo`             | `TextBlock`            | 失败 | `dir` 覆盖不足以证明 bidi override 语义                                       |
|   41 | `cite`            | `TextBlock`            | 失败 | 默认语义样式和内联子树未实现                                                  |
|   42 | `data`            | `TextBlock`            | 失败 | `value` 机器值未与可见文本建立可审核连接                                      |
|   43 | `del`             | `TextBlock`            | 失败 | 删除线未实现；`cite/datetime` 仅作数据                                        |
|   44 | `dfn`             | `TextBlock`            | 失败 | 定义实例语义与默认样式未实现                                                  |
|   45 | `ins`             | `TextBlock`            | 失败 | 插入文本装饰未实现；内联子树不兼容                                            |
|   46 | `kbd`             | `TextBlock`            | 失败 | UA 等宽/键盘输入语义未实现                                                    |
|   47 | `mark`            | `TextBlock`            | 失败 | 默认高亮背景未实现                                                            |
|   48 | `q`               | `TextBlock`            | 失败 | 自动引号生成与嵌套引号语义未实现                                              |
|   49 | `ruby`            | `TextBlock`            | 失败 | ruby 基文与注音的二维排版未实现                                               |
|   50 | `rp`              | `TextBlock`            | 失败 | ruby fallback 条件呈现未实现                                                  |
|   51 | `rt`              | `TextBlock`            | 失败 | 注音定位与字号未实现                                                          |
|   52 | `samp`            | `TextBlock`            | 失败 | UA 等宽语义未实现                                                             |
|   53 | `sub`             | `TextBlock`            | 失败 | baseline/subscript 位移未实现                                                 |
|   54 | `sup`             | `TextBlock`            | 失败 | baseline/superscript 位移未实现                                               |
|   55 | `time`            | `TextBlock`            | 部分 | 可见文本可投影；`datetime` 机器值只保留数据，未完成语义审核                   |
|   56 | `var`             | `TextBlock`            | 失败 | 默认斜体/变量语义未实现                                                       |
|   57 | `a`               | `HyperlinkButton`      | 失败 | 块/内联分支、复合内容、NavigateUri 与下载行为未完整实现                       |
|   58 | `ul`              | `Grid`                 | 失败 | Grid+Items 静态不兼容，marker 与列表流缺失                                    |
|   59 | `ol`              | `Grid`                 | 失败 | Grid+Items 不兼容，start/type/reversed/li.value 序号状态机缺失                |
|   60 | `menu`            | `Grid`                 | 失败 | Grid+Items 不兼容，命令列表语义缺失                                           |
|   61 | `li`              | `Grid`                 | 失败 | marker/ordinal 和 list-item formatting context 缺失                           |
|   62 | `dl`              | `Grid`                 | 失败 | Grid+Items 不兼容，dt/dd 分组关系未实现                                       |
|   63 | `dt`              | `TextBlock`            | 失败 | 内联子树不兼容，术语与描述组关系未保留                                        |
|   64 | `dd`              | `Grid`                 | 失败 | 普通块流错误，组内缩进/关系未实现                                             |
|   65 | `details`         | `Button`               | 失败 | details 容器被压成 Button，open/summary/content 状态机缺失                    |
|   66 | `summary`         | `Button`               | 失败 | 未作为 details 的 disclosure header 槽位                                      |
|   67 | `dialog`          | `Grid`                 | 失败 | open、top layer、modal/backdrop、焦点约束未形成对象结构                       |
|   68 | `search`          | `Grid`                 | 失败 | 普通块流错误，search landmark 未落地                                          |
|   69 | `slot`            | `Grid`                 | 失败 | assigned nodes 与 fallback content 的分派关系未实现                           |
|   70 | `noscript`        | 非可视                 | 失败 | body/head 上下文与脚本启用状态被统一丢弃                                      |
|   71 | `script`          | 非可视                 | 部分 | 非可视正确；脚本资源/类型/执行连接仍需根级事件审计                            |
|   72 | `style`           | 非可视                 | 部分 | 非可视正确；样式表规则、media 和级联连接不能只保留属性                        |
|   73 | `link`            | 非可视                 | 部分 | 非可视正确；stylesheet/icon/preload 等 rel 状态未分派                         |
|   74 | `meta`            | 非可视                 | 部分 | 非可视正确；charset/http-equiv/name 状态语义未分派                            |
|   75 | `template`        | 非可视                 | 失败 | template content 是独立 DocumentFragment，当前 None/无槽位模型会丢树          |
|   76 | `form`            | `Grid`                 | 失败 | 普通块流错误，提交/校验/owner 关系只存数据未建立行为连接                      |
|   77 | `label`           | `TextBlock`            | 失败 | labelable-control 关联与复合 phrasing 子树不完整                              |
|   78 | `button`          | `Button`               | 部分 | 基础头部可用；type/submit/reset/command/form owner 状态未完成                 |
|   79 | `input`           | `TextBox`              | 失败 | 20+ type 状态未强类型分派，属性适用性也未按 state 限制                        |
|   80 | `textarea`        | `TextBox`              | 失败 | AcceptsReturn/TextWrapping/rows/cols/live value 未完整投影                    |
|   81 | `select`          | `ComboBox`             | 失败 | multiple/size 分支缺失；selected options 与 live value 未完整投影             |
|   82 | `option`          | `ComboBoxItem`         | 部分 | 单选 ComboBox 场景基础头部可用；label/text/value/selected live state 尚不完整 |
|   83 | `fieldset`        | `Grid`                 | 失败 | 普通块流错误，disabled 级联和 legend 特殊槽位未实现                           |
|   84 | `legend`          | `TextBlock`            | 失败 | fieldset caption 特殊槽位与复合 phrasing 子树未实现                           |
|   85 | `datalist`        | `Grid`                 | 失败 | datalist 本体应作为 input 建议数据源，不应呈现普通 Grid                       |
|   86 | `optgroup`        | `Grid`                 | 失败 | 应成为 selector 分组数据，不是可视 Grid 子树                                  |
|   87 | `output`          | `TextBlock`            | 失败 | form owner、for token、live calculated value 与内联子树未完整实现             |
|   88 | `meter`           | `ProgressBar`          | 失败 | low/high/optimum 区间状态无法由普通 ProgressBar 表达                          |
|   89 | `progress`        | `ProgressBar`          | 部分 | 基础控件可用；缺省 indeterminate 与 max/value 规则未完整验证                  |
|   90 | `table`           | `Grid`                 | 失败 | Grid+Items 不兼容，二维表模型未构建                                           |
|   91 | `thead`           | `Grid`                 | 失败 | Grid+Items 不兼容，row group 未投影                                           |
|   92 | `tbody`           | `Grid`                 | 失败 | Grid+Items 不兼容，隐式/显式 row group 未投影                                 |
|   93 | `tfoot`           | `Grid`                 | 失败 | Grid+Items 不兼容，footer row group 未投影                                    |
|   94 | `tr`              | `Grid`                 | 失败 | Grid+Items 不兼容，列定位未投影                                               |
|   95 | `th`              | `Grid`                 | 失败 | cell 的 scope/headers/abbr/rowspan/colspan 与内容槽位均不完整                 |
|   96 | `td`              | `Grid`                 | 失败 | cell 的 headers/rowspan/colspan 与二维位置均不完整                            |
|   97 | `caption`         | `TextBlock`            | 失败 | table caption 特殊槽位及复合内容未实现                                        |
|   98 | `colgroup`        | `Grid`                 | 失败 | 列轨道定义不应创建可视 Grid 子对象                                            |
|   99 | `col`             | `Grid`                 | 失败 | void 列元数据不应创建可视 Grid；span 未生成轨道                               |
|  100 | `picture`         | `Grid`                 | 失败 | source media/type/srcset 选择状态机未决定最终 Image source                    |
|  101 | `source`          | 非可视                 | 部分 | 非可视正确；必须连接到 picture/audio/video 的候选选择器                       |
|  102 | `video`           | `MediaPlayerElement`   | 部分 | 基础头部可用；source/track/fallback、controls/autoplay 等未完整映射           |
|  103 | `audio`           | `MediaPlayerElement`   | 部分 | 基础头部可用；source/track/fallback 与播放状态未完整映射                      |
|  104 | `map`             | `Grid`                 | 失败 | image-map 坐标系与关联 img/usemap 未建立                                      |
|  105 | `area`            | `Button`               | 失败 | shape/coords 未生成热点几何，普通 Button 不能表达区域                         |
|  106 | `track`           | 非可视                 | 部分 | 非可视正确；timed-text 与媒体父级连接未实现                                   |
|  107 | `img`             | `Image`                | 部分 | 基础头部可用；srcset/sizes/currentSrc/object-fit/usemap 未完整投影            |
|  108 | `iframe`          | `ContentControl`       | 部分 | 嵌套文档槽位已开始实现；sandbox/srcdoc/尺寸及独立范围仍未静态全过             |
|  109 | `canvas`          | `ContentControl`       | 失败 | drawing bitmap/state/命令没有替代对象，空 ContentControl 不对等               |
|  110 | `embed`           | `ContentControl`       | 失败 | 外部内容类型与替代宿主未按 type/source 分派                                   |
|  111 | `object`          | `ContentControl`       | 失败 | data/type 成功与 fallback content 两条路径未分派                              |
|  112 | `hr`              | `Path`                 | 失败 | 没有 Path.Data/Stroke，默认会成为无绘制几何                                   |
|  113 | `selectedcontent` | `TextBlock`            | 失败 | 应镜像 select 当前 option，当前没有状态连接                                   |

**当前总判定：113 项中 0 项达到“完整通过”；部分项只能证明头部类型具备基础承载
能力。静态审核失败，禁止进入动态一致性验收。**

**审计日期**：2026-07-29
**审计范围**：113 个 HTML + 14 个 SVG 元素类的数据填充与 XAML 转换全链路
**测试基线**：202 个测试全部通过，0 失败，0 跳过
**构建状态**：Debug/Release 均 0 警告 0 错误

---

## 摘要

本报告聚焦两个核心问题：
1. **数据获取**：100+ 元素类如何从 WebView2 运行时获取多维度真实数据（初始化值、布局值、链接值、运行值）
2. **XAML 对等转换**：每个元素类是否能正确地创建和转换为功能对等的 XAML 对象

结论：架构设计总体优秀，但在 **input 元素多态性**、**部分元素缺少专属 CreateXaml 覆盖**、**table/th/td 语义转换**、**ol/ul 缺少编号能力** 等关键领域存在设计缺陷。

| 严重级别 | 数量   | 类型                             |
| -------- | ------ | -------------------------------- |
| Critical | 3      | 语义丢失 / 功能缺失              |
| High     | 5      | XAML 控件选择错误 / 数据维度遗漏 |
| Medium   | 8      | 语义映射不完整 / 属性覆盖不足    |
| Low      | 4      | 样式映射 / 边界情况              |
| **总计** | **20** |

---

# 第一部分：数据获取体系审计

## 1.1 数据获取架构概述

```
WebView2 CoreWebView2
    │
    ▼
IDomPropertyQueryEngine.QueryAsync(DomPropertyQueryContext)
    │
    ├── DomPropertyDataSlot.Initialization  → HTML attribute 声明值
    ├── DomPropertyDataSlot.Link           → 语义链接（布局绑定、CSS 表达式）
    └── DomPropertyDataSlot.Runtime        → 浏览器运行时计算值
```

元素类通过 `ElementPropertyAttribute` 声明每个属性的填充要求（`IsFillRequired` / `IsXamlFillRequired`），然后由 `DomElement.CreateDomFillRequests()` 统一扫描生成填充请求，`HtmlDocumentRoot` 聚合后通过引擎一次批量查询。

## 1.2 数据维度分析

### 维度清单

| 维度                   | 数据来源                          | 槽位                                 | 覆盖属性数                | 评估     |
| ---------------------- | --------------------------------- | ------------------------------------ | ------------------------- | -------- |
| HTML Initialization    | `getAttribute()`                  | `DomPropertyDataSlot.Initialization` | ~30（全局属性）+ 元素专属 | ✅ 完整   |
| CSS Declarations       | `getComputedStyle()`              | `DomPropertyDataSlot.Runtime`        | ~108 CSS 属性             | ✅ 完整   |
| DOM Rect Geometry      | `getBoundingClientRect()`         | `DomPropertyDataSlot.Runtime`        | 4（x, y, width, height）  | ✅ 完整   |
| Scroll State           | `scrollLeft/Top/Width/Height`     | `DomPropertyDataSlot.Runtime`        | 4                         | ✅ 完整   |
| Form State             | `value`, `checked`, `disabled` 等 | `DomPropertyDataSlot.Runtime`        | ~7                        | ⚠️ 部分   |
| Content Text           | `ownText`, `textContent` 等       | `DomPropertyDataSlot.Initialization` | ~5                        | ✅ 完整   |
| Semantic Link          | `getComputedStyle()` + CSS 表达式 | `DomPropertyDataSlot.Link`           | ~25（布局相关）           | ⚠️ 不完整 |
| Resources              | `src`, `href`, `poster` 等        | `DomPropertyDataSlot.Runtime`        | 按元素类型                | ⚠️ 部分   |
| Event Handlers         | HTML `on*` attributes             | `DomPropertyDataSlot.Initialization` | 按元素类型                | ✅ 完整   |
| Pseudo-element Effects | `getComputedStyle()`              | `DomPropertyDataSlot.Runtime`        | 2                         | ✅ 完整   |

## 1.3 发现的问题

### Critical-1：`HtmlInputDomElement` 未按 `type` 分派不同数据源

**文件**：`StandardHtmlDomFormsTablesMedia.cs:73-171`

**问题**：
```csharp
// HtmlInputDomElement 的 XamlElementNameCatalog 映射：
"input" or "textarea" => "TextBox"
```

HTML `<input>` 有 20+ 种 type（text, password, email, number, date, checkbox, radio, file, color, range, submit, reset, button, hidden, image, month, week, time, datetime-local, url, tel, search），但当前实现：

- **XAML 映射**：始终返回 `TextBox` ❌
- **数据源**：只有 `value`、`checked`、`placeholder` 三种（`HtmlElementDataSourceCatalog`）
- **缺失数据维度**：
  - `type="checkbox"` → 应采集 `checked` 状态 + 映射 `CheckBox.IsChecked`
  - `type="radio"` → 应采集 `checked` 状态 + `name` 分组 + 映射 `RadioButton.IsChecked`
  - `type="number"` / `type="range"` → 应采集 `min`、`max`、`step`、`value` + 映射 `NumberBox` / `Slider`
  - `type="date"` / `type="time"` / `type="datetime-local"` → 应映射 `CalendarDatePicker` / `TimePicker`
  - `type="color"` → 应映射 `ColorPicker`
  - `type="file"` → 应映射 `FileOpenPicker`（按钮触发）
  - `type="submit"` / `type="reset"` / `type="button"` → 应映射 `Button`
  - `type="hidden"` → 不生成 XAML 控件
  - `type="image"` → 应映射 `Image` + 提交行为
  - `type="email"` / `type="url"` / `type="tel"` / `type="search"` → TextBox 可接受，但需 InputScope

**影响**：`<input type="checkbox">` 和 `<input type="radio">` 会被错误地映射为 `TextBox`，复选框/单选框功能完全丢失。`<input type="range">` 的滑块交互丢失。

**修复建议**：
在 `HtmlInputDomElement.CreateXaml()` 中根据 `type` 属性的运行时值分派：
```csharp
protected override XamlElementMappingDecision CreateXaml()
{
    var type = RuntimeInitialization("state.type") ?? SourceInitialization(Type);
    return type?.ToLowerInvariant() switch
    {
        "checkbox" => new("CheckBox", XamlElementMappingKind.TypeDefault, false, "..."),
        "radio" => new("RadioButton", XamlElementMappingKind.TypeDefault, false, "..."),
        "range" => new("Slider", XamlElementMappingKind.TypeDefault, false, "..."),
        "number" => new("NumberBox", XamlElementMappingKind.TypeDefault, false, "..."),
        "submit" or "reset" or "button" =>
            new("Button", XamlElementMappingKind.TypeDefault, false, "..."),
        "hidden" => new("", XamlElementMappingKind.TypeDefault, false, "..."),
        _ => new("TextBox", XamlElementMappingKind.TypeDefault, false, "...")
    };
}
```

---

### Critical-2：表格元素（table/tr/td/th）语义映射错误

**文件**：`StandardHtmlDomFormsTablesMedia.cs:414-489`

**问题**：
```csharp
// HtmlTableDomElement           → "Grid" + TableLayout
// HtmlTableRowDomElement        → "Grid" + TableLayout
// HtmlTableCellDomElement       → "Grid" + TableLayout  ← 错误！
// HtmlTableHeaderCellDomElement → "Grid" + TableLayout  ← 错误！
```

- `th` 和 `td` 映射为 `Grid` 是错误的，它们不是容器，而是**表格单元格**
- WinUI 3 没有 `DataGrid` 原语，但这不意味着应该把 `td` 映射为 `Grid`
- 正确的映射应该是：
  - `th` → `Border` + `TextBlock`（带 `FontWeight="Bold"` 样式）放在 Grid 的某个 cell 中
  - `td` → `Border` + `TextBlock` 或 `ContentControl` 放在 Grid 的某个 cell 中
- 父元素 `tr` 应该输出 `Grid.RowDefinitions`（`*`），子元素 td/th 应该写 `Grid.Column="n"`
- 当前 `RowSpan` 和 `ColumnSpan` 属性虽已声明但映射为 `RuntimeDataSource`，无法实际控制 Grid 布局

**影响**：表格结构完全丢失，单元格的跨行/跨列功能不可用。生成的 XAML 树中每个 `td` 都变成独立的 Grid 而非同一行/列中的子元素。这是 **113 个元素中最严重的语义丢失问题之一**。

**修复建议**：
1. `HtmlTableRowDomElement` → 保持 `Grid`，重写 `BuildXamlObjectChildPlans()` 为每个 `td`/`th` 生成 `Grid.Column="n"` 属性
2. `HtmlTableCellDomElement` / `HtmlTableHeaderCellDomElement` → 映射为 `TextBlock`（纯文本）或 `ContentControl`（有子元素），而非 `Grid`
3. `HtmlTableDomElement` → 保持 `Grid`，重写 `BuildXamlObjectChildPlans()` 计算 `RowDefinitions`

---

### Critical-3：`HtmlOrderedListDomElement` 缺少编号序列生成能力

**文件**：`StandardHtmlDomListsAndMetadata.cs:26-37`

**问题**：
```csharp
public sealed class HtmlOrderedListDomElement(DomElementMapping m) :
    HtmlListDomElementDefinition(m, "ol")
{
    protected override XamlElementMappingDecision CreateXaml() =>
        new("Grid", XamlElementMappingKind.ConservativeContainer, true, "...");
}
```

`<ol>` 元素在 HTML 语义中自动为 `<li>` 子元素生成递增值编号（1, 2, 3... 或 a, b, c... 或 I, II, III...），但当前实现：
- `ol` 只是 `Grid`，没有任何序列号生成能力
- `type` 属性（控制编号类型：1/A/a/I/i）声明了但只是 `RuntimeDataSource`
- `start` 属性（控制起始编号）声明了但只是 `RuntimeDataSource`
- `reversed` 属性（倒序）声明了但只是 `RuntimeDataSource`
- `li.value` 属性（控制单个项的值）声明了但只是 `RuntimeDataSource`

**影响**：有序列表的语义完全丢失，`<ol><li>A</li><li>B</li></ol>` 视觉上退化为普通 div 排列，没有任何序号前缀。

**修复建议**：
在 `HtmlOrderedListDomElement` 中重写 `BuildXamlObjectChildPlans()`，为每个 `li` 生成带编号前缀的 `TextBlock`，例如：
```csharp
protected override IReadOnlyList<XamlElementObjectPlan> BuildXamlObjectChildPlans()
{
    var start = int.TryParse(SourceInitialization(Start), out var s) ? s : 1;
    var plans = new List<XamlElementObjectPlan>();
    var index = start;
    foreach (var child in EnumerateDomChildren())
    {
        // 为每个 li 生成 "1. " 前缀的 TextBlock + 原 li 内容
        var prefix = $"{index}. ";
        plans.Add(CreateSyntheticXamlObjectPlan("TextBlock",
            [new("Text", prefix, null)],
            ElementXamlChildPlacementKind.None, [],
            $"Ordered list prefix {index}"));
        plans.AddRange(child.BuildXamlObjectPlans());
        index++;
    }
    return plans;
}
```

---

### High-1：`WebView2DomPropertyFiller` 对 Link 槽位查询不完整

**文件**：`Integration/WebView2DomPropertyFiller.cs:92-223`

**问题**：
`QueryLinkAsync` 方法只处理了 CSS 表达式（`var()`, `calc()`, `min()`, `max()`）和百分比/视口单位，但缺少：
- **CSS Grid 轨道**：`grid-template-columns: 1fr 2fr 100px` 未解析为完整的 `ContainerLayoutBinding`
- **CSS Flex basis**：`flex: 1 0 200px` 未解析 shorthand
- **继承链接**：`height: inherit` 未处理
- **环境变量**：`env(safe-area-inset-top)` 未处理
- 代码注释承认："完整 ContainerLayoutBinding 需要更多上下文"（第 200 行），说明当前实现是简化版
- `QueryLayoutLinkAsync` 中 `QueryRuntimeAsync` 实际做的是 Runtime 查询而非 Link 查询，布局语义链接（如 `style.width` 的 CSS 表达式 `calc(100% - 20px)`）走的是 `QueryRuntimeAsync` 路径

**影响**：复杂的 CSS 布局信息在 Link 槽位丢失，导致 XAML 布局转换不准确。特别是 Grid 轨道的 `fr` 单位在 Link 槽位没有任何解析。

---

### High-2：`HtmlSpanDomElement` 的 XAML 映射逻辑依赖运行时判断

**文件**：`StandardHtmlDomTextElements.cs:66-76`

**问题**：
```csharp
protected override XamlElementMappingDecision CreateXaml() =>
    Children.Count == 0
        ? new("TextBlock", XamlElementMappingKind.InlineFlow, ...)
        : new("StackPanel", XamlElementMappingKind.InlineFlow, ...);
```

`CreateXaml()` 在 `Children.Count == 0`（叶子节点）时返回 `TextBlock`，在有子节点时返回 `StackPanel`。这意味着：
- 叶子 `<span>` 正确地映射为 `TextBlock`
- 有子 `<span>` 映射为 `StackPanel` —— **但 `<span>` 是 `HtmlPhrasingDomElementDefinition`（phrasing 元素），默认 display 为 `inline`**
- `StackPanel` 是块级容器（display: block），破坏了内联布局语义
- 更合适的映射：有子 span 应保持 `TextBlock` + `InlineCollection`（通过 `ElementXamlChildPlacementKind.Inlines`）

**影响**：嵌套 span 在 XAML 中变成 `StackPanel` → `StackPanel` 嵌套（块级），而非 `TextBlock` → `Inline` 层次（内联），导致文本流布局被破坏。

**修复建议**：
```csharp
protected override XamlElementMappingDecision CreateXaml() =>
    new("TextBlock", XamlElementMappingKind.InlineFlow, false,
        "The span element preserves inline phrasing content.");
```
并设置 `ElementXamlChildPlacementKind.Inlines` 让子文本元素作为 `Inline` 插入。

---

### High-3：`HtmlSelectDomElement → ComboBox` 映射丢失多选语义

**文件**：`StandardHtmlDomFormsTablesMedia.cs:221-266`

**问题**：
```csharp
protected override XamlElementMappingDecision CreateXaml() =>
    new("ComboBox", XamlElementMappingKind.TypeDefault, false, "...");
```

HTML `<select>` 支持 `multiple` 属性实现多选，但当前始终映射为 `ComboBox`（单选）。`Multiple` 属性已声明但仅为 `RuntimeDataSource`。

`size` 属性（控制可见选项数）也仅为 `RuntimeDataSource`，但在 `multiple` 模式下 `size > 1` 应映射为 `ListBox` 而非 `ComboBox`。

**影响**：`<select multiple>` 无法正确转换为 `ListView` 或 `ListBox`，多选功能丢失。

---

### High-4：`HtmlImageDomElement → Image` 缺少拉伸模式映射

**文件**：`StandardHtmlDomFormsTablesMedia.cs:626-673`

**问题**：
```csharp
protected override XamlElementMappingDecision CreateXaml() =>
    new("Image", XamlElementMappingKind.TypeDefault, false, "...");
```

HTML `<img>` 通过 CSS `object-fit` 控制拉伸（fill/contain/cover/none/scale-down），但 `BuildXamlAttributes()` 中未将 `object-fit` CSS 值转换为 `Image.Stretch` 属性。

当前只在 `DomXamlPropertyExecutionCatalog` 中标记 `object-fit` 为 `UIElement.Interaction` 类型，但未进行值映射：
- `object-fit: fill` → `Stretch="Fill"`
- `object-fit: contain` → `Stretch="Uniform"`
- `object-fit: cover` → `Stretch="UniformToFill"`
- `object-fit: none` → `Stretch="None"`

**影响**：图片在 XAML 中的拉伸行为不正确。例如 `object-fit: cover` 的图片在 XAML 中可能被拉伸而非裁剪填充。

---

### High-5：`HtmlTextAreaDomElement` 未设置 `AcceptsReturn` 和 `TextWrapping`

**文件**：`StandardHtmlDomFormsTablesMedia.cs:171-219`

**问题**：
```csharp
protected override XamlElementMappingDecision CreateXaml() =>
    new("TextBox", XamlElementMappingKind.TypeDefault, false, "...");

// BuildXamlAttributes 只设置了 PlaceholderText, MaxLength, IsEnabled, IsReadOnly
```

HTML `<textarea>` 是多行文本输入，但 `BuildXamlAttributes` 中缺少：
- `AcceptsReturn="True"`（textarea 支持回车换行，普通 TextBox 默认不支持）
- `TextWrapping="Wrap"`（textarea 默认换行，普通 TextBox 默认不换行）
- `Rows` / `Cols` 属性（仅标记为 `RuntimeDataSource`，未实际计算高度/宽度）

**影响**：textarea 在 XAML 中退化为单行 TextBox，不支持多行输入和回车换行。

---

### Medium-1：通用容器元素均映射为无差别 Grid，丢失地标语义

**文件**：`StandardHtmlDomElements.cs` 和 `StandardHtmlDomTextElements.cs`

**观察**：
```csharp
// div (ConservativeContainer/FlexLayout) / section / header / footer
// nav / aside / article / address / figure
// 全部映射为 "Grid"
```

**问题**：
- 所有语义分区元素映射为无差别的 `Grid`，丢失了 HTML5 地标语义
- 没有利用 XAML 的 `AutomationProperties.LandmarkType`（Navigation/Main/Search/Form 等）
- 仅 `HtmlDivDomElement` 根据 `display` 值执行布局分派，其他语义容器（如 `section`）不做分派
- `nav`/`main`/`search`/`aside` 应至少设置 `AutomationProperties.Name` 或 `LandmarkType`

---

### Medium-2：`HtmlAnchorDomElement → HyperlinkButton` 语义不完整

**文件**：`StandardHtmlDomListsAndMetadata.cs:3-19`

**问题**：
```csharp
protected override XamlElementMappingDecision CreateXaml() =>
    new("HyperlinkButton", XamlElementMappingKind.TypeDefault, false, "...");
```

- `<a>` 元素在 HTML 中可以是块级或内联，但始终映射为 `HyperlinkButton`（块级按钮）
- 当 `<a>` 只有文本内容时，更合适的映射可能是 `Hyperlink`（内联元素）
- `href` 没有在 `BuildXamlAttributes` 中写入 `NavigateUri`
- `Download` / `Ping` / `Rel` 属性标记为 `RuntimeDataSource`

**影响**：链接的 URL 无法在静态 XAML 中导航。点击行为完全依赖运行时绑定。

---

### Medium-3：`HtmlDetailsDomElement → Button` + `HtmlSummaryDomElement → Button`

**文件**：`StandardHtmlDomListsAndMetadata.cs:87-102`

**问题**：
```csharp
// HtmlDetailsDomElement → "Button"  (应为 WinUI Expander)
// HtmlSummaryDomElement → "Button" (应为 Expander.Header)
```

HTML `<details>` 语义上是一个可展开/折叠的容器，包含 `<summary>` 标题和展开内容。最接近的 XAML 控件是 `Expander`（`Expander.Header` = summary, `Expander.Content` = 展开内容）。

**影响**：展开/折叠行为完全丢失，details 变为普通按钮 + 独立内容元素。

---

### Medium-4：`HtmlDialogDomElement → Grid` 缺少弹出/模态行为

**文件**：`StandardHtmlDomListsAndMetadata.cs:104-124`

**问题**：
```csharp
// HtmlDialogDomElement → "Grid" + PositionedLayout
protected override IReadOnlyList<GeneratedXamlAttribute> BuildXamlAttributes()
{
    // ...
    if (!Open.SourceInitialization.IsSet)
        SetXamlAttribute(attributes, "Visibility", "Collapsed", Open);
}
```

- `<dialog open>` → Grid 可见，`<dialog>` (无 open) → Grid Collapsed ✅
- 但缺少模态行为：HTML dialog 有 `showModal()` 半透明遮罩层
- `ClosedBy` 属性仅标记为 `RuntimeDataSource`

**影响**：模态对话框的半透明遮罩和模态焦点捕获行为丢失。

---

### Medium-5：`HtmlFieldSetDomElement` 的 disabled 映射语义错误

**文件**：`StandardHtmlDomFormsTablesMedia.cs:305-330`

**问题**：
```csharp
if (Disabled.SourceInitialization.IsSet)
    SetXamlAttribute(attributes, "IsHitTestVisible", "False", Disabled);
```

HTML `<fieldset disabled>` 禁用所有内部表单控件。当前映射为 `IsHitTestVisible="False"`，但：
- `IsHitTestVisible="False"` → 不响应点击但是焦点可能仍能到达子控件
- `IsEnabled="False"` → 完全禁用所有子控件，符合 HTML `disabled` 语义

`HtmlButtonDomElement` 使用 `IsEnabled="False"`（正确），`HtmlFieldSetDomElement` 应一致。

---

### Medium-6：SVG `use` 元素映射为独立的 Path 而非引用

**文件**：`StandardSvgDomElements.cs:138-152`

**问题**：
```csharp
// SvgUseDomElement → "Path" (应为引用/克隆)
protected override XamlElementMappingDecision CreateXaml() =>
    new("Path", XamlElementMappingKind.TypeDefault, false, "...");
```

SVG `<use>` 的语义是实例化指定的 SVG 片段（通过 `href="#id"` 引用），而非创建新的独立 Path。当前将其映射为独立 Path 会丢失引用关系，且 `Href`、`X`、`Y`、`Width`、`Height` 都标记为 `RuntimeDataSource`，不进入 XAML。

---

### Medium-7：`HtmlButtonDomElement` 未处理 `type` 属性

**文件**：`StandardHtmlDomFormsTablesMedia.cs:35-72`

**问题**：
`<button type="submit">` 和 `<button type="button">` 在 HTML 中有不同的表单行为。当前 type 属性仅标记为 `RuntimeDataSource`，XAML 中不区分提交按钮和普通按钮。

---

### Medium-8：`HtmlMeterDomElement → ProgressBar` 语义不精确

**文件**：`StandardHtmlDomFormsTablesMedia.cs:363-399`

**问题**：
`<meter>` 语义是"已知范围内的标量测量"（如磁盘使用率 73%），而 `<progress>` 是"任务完成进度"（如上传 45%）。WinUI `ProgressBar` 更接近 `<progress>` 而非 `<meter>`。`meter` 的 `low`/`high`/`optimum` 阈值信息在 ProgressBar 中没有对应概念。

---

### Low-1：CSS `border-style` 系列属性未注册

`DomElementRuntimePropertyCatalog` 注册了 `border` shorthand 和 `borderWidth`/`borderColor`/`borderRadius` 等，但没有 `borderStyle` 系列属性（`border-style`, `border-top-style`, `border-right-style`, `border-bottom-style`, `border-left-style`）。

---

### Low-2：`HtmlBreakDomElement` 的 `<br>` 映射为 TextBlock 而非 LineBreak

```csharp
// HtmlBreakDomElement → "TextBlock" (应为 InlineUIContainer + LineBreak)
```

`<br>` 在 XAML 中应映射为 `LineBreak` 元素（内联换行），而非独立的 `TextBlock`。

---

### Low-3：CSS `font-family` 值未做字体名称规范化

CSS `font-family: "Segoe UI", sans-serif` 在 XAML 中应提取第一个字体名称 "Segoe UI"，当前直接传递原始 CSS 字符串。

---

### Low-4：`HtmlAnchorDomElement` 的 `href` 未写入 XAML `NavigateUri`

虽然 `href` 已作为 `HtmlHyperlinkCommonProperties` 声明且注册了数据源（`Url("href", NavigateUri)`），但 `BuildXamlAttributes()` 中并未将其写入 XAML 属性。`DomXamlPropertyExecutionCatalog.Resolve` 已将 `href` 映射为 `HyperlinkButton.NavigateUri`，但最终 XAML attribute 列表中缺少此属性。

---

# 第二部分：XAML 转换对等性审计

## 2.1 转换架构概述

```
DomElement.CreateXaml()  ← 每个元素类重写
    │
    ▼
XamlElementMappingDecision (XamlElementName, MappingKind, RequiresRuntimeLayoutContract)
    │
    ▼
DomElement.GetXamlElementMapping()
    │
    ▼
BuildXamlAttributes()       ← 每个元素重写，填充 XAML attribute
BuildXamlObjectChildPlans() ← 布局容器重写，规划子元素
    │
    ▼
IXamlElementObjectFactory.CreateElement()  ← 消费方实现
IXamlElementObjectFactory.FillElementProperties()
IXamlElementObjectFactory.AttachChild()
```

## 2.2 每个元素类的 XAML 映射审计

### HTML 元素的转换完整性

| 元素数 | 映射类型                           | 典型元素                                                                                                                                                                                                             | 评估                    |
| ------ | ---------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------- |
| ~20    | `Grid` (ConservativeContainer)     | div, section, header, footer, nav, aside, article, address, figure, main, form, fieldset, ul, ol, menu, li, dl, dd, slot, datalist, optgroup                                                                         | ✅ 容器映射正确          |
| ~7     | `Grid` (TableLayout)               | table, thead, tbody, tfoot, tr, colgroup, col                                                                                                                                                                        | ❌ td/th 错误映射为 Grid |
| ~6     | `Grid` (FlexLayout, 条件)          | div(flex), body(flex), main(flex)                                                                                                                                                                                    | ✅                       |
| ~4     | `Grid` (PositionedLayout)          | svg, g, dialog, map                                                                                                                                                                                                  | ⚠️ dialog 缺少模态行为   |
| ~35    | `TextBlock`                        | h1-h6, p, span, strong, em, b, i, u, s, small, code, pre, abbr, bdi, bdo, cite, dfn, kbd, mark, samp, sub, sup, var, br, wbr, label, legend, output, figcaption, caption, selectedcontent, dt, q, ruby, rp, rt, time | ✅ 大部分合适            |
| 2      | `TextBlock` → `StackPanel`（条件） | span(有子元素)                                                                                                                                                                                                       | ❌ 应保持 Inline 层次    |
| 1      | `Button`                           | button                                                                                                                                                                                                               | ✅                       |
| 2      | `Button`                           | details, summary                                                                                                                                                                                                     | ❌ 应映射为 Expander     |
| 1      | `TextBox`                          | input (所有 type)                                                                                                                                                                                                    | ❌ 应按 type 分派        |
| 1      | `TextBox`                          | textarea                                                                                                                                                                                                             | ⚠️ 缺少多行支持          |
| 1      | `ComboBox`                         | select                                                                                                                                                                                                               | ⚠️ 缺少多选              |
| 1      | `ComboBoxItem`                     | option                                                                                                                                                                                                               | ✅                       |
| 1      | `ProgressBar`                      | progress                                                                                                                                                                                                             | ✅                       |
| 1      | `ProgressBar`                      | meter                                                                                                                                                                                                                | ⚠️ 语义不完全匹配        |
| 1      | `HyperlinkButton`                  | a                                                                                                                                                                                                                    | ⚠️ href 未写入 XAML      |
| 1      | `Image`                            | img                                                                                                                                                                                                                  | ⚠️ 缺少 Stretch 映射     |
| 1      | `MediaPlayerElement`               | video                                                                                                                                                                                                                | ✅                       |
| 1      | `MediaPlayerElement`               | audio                                                                                                                                                                                                                | ✅                       |
| 1      | `ContentControl`                   | iframe                                                                                                                                                                                                               | ✅                       |
| 2      | `ContentControl`                   | embed, object                                                                                                                                                                                                        | ⚠️ 待确认                |
| ~15    | `""` (空, 非可视)                  | head, title, base, meta, link, style, script, noscript, template, source, track                                                                                                                                      | ✅                       |
| 1      | `HyperlinkButton`                  | area                                                                                                                                                                                                                 | ✅                       |
| 1      | `Grid`                             | picture                                                                                                                                                                                                              | ✅                       |

### SVG 元素的转换完整性

| 元素     | 映射                      | 评估                    |
| -------- | ------------------------- | ----------------------- |
| svg      | Canvas + PositionedLayout | ✅                       |
| g        | Canvas + PositionedLayout | ✅                       |
| path     | Path                      | ✅                       |
| use      | Path                      | ❌ 应为引用, 非独立 Path |
| circle   | Ellipse                   | ✅                       |
| rect     | Rectangle                 | ✅                       |
| line     | Line                      | ✅                       |
| polygon  | Polygon                   | ✅                       |
| polyline | Polyline                  | ✅                       |
| ellipse  | Ellipse                   | ✅                       |
| defs     | Canvas                    | ✅                       |
| clipPath | Canvas                    | ✅                       |
| mask     | Canvas                    | ✅                       |
| text     | TextBlock                 | ✅                       |

## 2.3 缺少专属 CreateXaml 覆盖的元素

以下元素使用父类的默认 `CreateXaml` 实现（来自语义族基类），没有专属覆盖来定制 XAML 行为：

| 元素       | 使用的父类                        | CreateXaml 来源 | 是否需要专属覆盖    |
| ---------- | --------------------------------- | --------------- | ------------------- |
| `col`      | 直接继承 HtmlDomElementDefinition | 已覆盖          | ✅                   |
| `colgroup` | HtmlTableDomElementDefinition     | 已覆盖          | ✅                   |
| `tr`       | HtmlTableDomElementDefinition     | 已覆盖          | ❌ 需处理 td/th 布局 |
| `thead`    | HtmlTableDomElementDefinition     | 已覆盖          | ✅                   |
| `tbody`    | HtmlTableDomElementDefinition     | 已覆盖          | ✅                   |
| `tfoot`    | HtmlTableDomElementDefinition     | 已覆盖          | ✅                   |
| `input`    | HtmlSubmitterDomElementDefinition | 已覆盖          | ❌ 应按 type 分派    |
| `embed`    | HtmlEmbeddedDomElementDefinition  | 待确认          | ⚠️                   |
| `object`   | HtmlEmbeddedDomElementDefinition  | 待确认          | ⚠️                   |

## 2.4 元素属性覆盖率分析

### 属性维度声明覆盖率

| 元素族     | 应有专属 HTML 属性                                                                                                                                                                                                                                                | 已声明     | 缺失 |
| ---------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------- | ---- |
| `form`     | `accept-charset`, `action`, `autocomplete`, `enctype`, `method`, `name`, `novalidate`, `rel`, `target`                                                                                                                                                            | 全部 9 个  | 0 ✅  |
| `input`    | `accept`, `alt`, `autocomplete`, `checked`, `dirname`, `height`, `list`, `max`, `maxlength`, `min`, `minlength`, `multiple`, `name`, `pattern`, `placeholder`, `readonly`, `required`, `size`, `src`, `step`, `type`, `value`, `width`, `disabled`, `form*` (5个) | 全部 29 个 | 0 ✅  |
| `textarea` | `autocomplete`, `cols`, `dirname`, `maxlength`, `minlength`, `placeholder`, `readonly`, `required`, `rows`, `wrap`, `disabled`, `form`, `name`                                                                                                                    | 全部 13 个 | 0 ✅  |
| `select`   | `autocomplete`, `multiple`, `required`, `size`, `disabled`, `form`, `name`                                                                                                                                                                                        | 全部 7 个  | 0 ✅  |
| `button`   | `type`, `value`, `command`, `commandfor`, `popovertarget`, `popovertargetaction`, `disabled`, `form*` (5个), `name`                                                                                                                                               | 全部 11 个 | 0 ✅  |
| `video`    | `poster`, `playsinline`, `width`, `height`, `src`, `crossorigin`, `preload`, `autoplay`, `loop`, `muted`, `controls`, `loading`                                                                                                                                   | 全部 12 个 | 0 ✅  |
| `img`      | `alt`, `src`, `srcset`, `sizes`, `crossorigin`, `usemap`, `ismap`, `referrerpolicy`, `decoding`, `controls`, `loading`, `fetchpriority`, `width`, `height`                                                                                                        | 全部 14 个 | 0 ✅  |
| `a`        | `href`, `target`, `download`, `ping`, `rel`, `hreflang`, `type`, `referrerpolicy`                                                                                                                                                                                 | 全部 8 个  | 0 ✅  |
| `iframe`   | `src`, `srcdoc`, `name`, `sandbox`, `allow`, `allowfullscreen`, `width`, `height`, `referrerpolicy`, `loading`                                                                                                                                                    | 全部 10 个 | 0 ✅  |

**结论**：HTML 属性声明覆盖完整，所有 113 个元素的专属属性均已声明。缺失的是这些属性在 XAML 中的**实际映射和值转换**。

### 属性 XAML 映射覆盖率

| 映射类型            | 属性数 | 说明                                                                                    |
| ------------------- | ------ | --------------------------------------------------------------------------------------- |
| `InlineXaml`        | ~25    | 直接映射为 XAML attribute（如 `disabled`→`IsEnabled`, `placeholder`→`PlaceholderText`） |
| `RuntimeDataSource` | ~160+  | 运行时绑定/不进入 XAML attribute                                                        |
| 未映射到 XAML       | ~15    | 不应映射（如 `nonce`, `itemid` 等安全/元数据属性）                                      |

关键发现：`RuntimeDataSource` 属性过多（~160+），其中很多属性在静态 XAML 中应该有默认值或占位值（如 `href`, `src`, `alt`）。

---

# 第三部分：架构设计评价

## 3.1 优点

1. **完整的属性声明系统**：`ElementPropertyAttribute` 的 `IsFillRequired`、`IsXamlFillRequired`、`IsAuditRequired` 提供了声明式数据流控制，每个属性的数据需求被明确标记

2. **五类槽位严格分离**：SourceInitialization/Link/Runtime + XamlInitialization/Link/Runtime 六槽位设计，确保了 DOM 数据和 XAML 数据的独立性

3. **CSS → XAML 执行分类**：`DomXamlPropertyExecutionCatalog` 将 108 个 CSS 属性映射到 12 个 `XamlPropertyExecutionKind` 类别，分层清晰

4. **运行时属性目录完整**：`DomElementRuntimePropertyCatalog` 覆盖 108 个 CSS 属性 + 几何/滚动/表单/内容/资源属性

5. **语义族层次清晰**：`HtmlLayoutDomElementDefinition` → `HtmlContainerDomElementDefinition` → `HtmlSectioningDomElementDefinition` → `HtmlPhrasingDomElementDefinition` 等层次合理，避免了代码重复

6. **扩展属性机制**：`AddExtensionAttribute` / `HtmlDomAttributeProperty` 支持元素特定的 HTML attribute 注册

7. **批量查询优化**：`HtmlDocumentRoot.PrepareDomFillAsync()` 先扫描所有元素生成所有查询请求，再一次性批量执行，避免了逐元素的往返开销

## 3.2 架构缺陷

1. **`HtmlInputDomElement` 应为抽象工厂模式**：当前 `input` 的 type 多样性未通过多态解决。`CreateXaml()` 依赖 `RuntimeValue` 判断 type，而 `RuntimeValue` 又依赖 `DomFillAsync` 先完成——形成先有鸡还是先有蛋的循环依赖

2. **表格映射需要专门适配器层**：HTML table 模型（table → caption/colgroup/thead/tbody/tfoot → tr → td/th）与 WinUI Grid 的行列模型是两种不同的二维布局模型，需要专门的转换层来处理行列索引计算、colspan/rowspan 影响格子位置映射

3. **`XamlElementNameCatalog` 扁平化问题**：单个 switch 表达式将 127 个元素的 XAML 名称集中管理，且 `input → TextBox` 无法根据 type 分派。建议按语义族拆分

4. **`HtmlElementDataSourceCatalog` 覆盖不足**：只覆盖了 23 个元素类型，大量元素的数据源信息未被声明。应覆盖所有有交互行为的元素

5. **`CreateXaml()` 与 `DomFillAsync()` 的顺序依赖**：`CreateXaml()` 在 `HtmlDivDomElement`、`HtmlBodyDomElement`、`HtmlSpanDomElement` 中依赖运行时值（`RuntimeValue("style.display")`），但运行时值需要先执行 `DomFillAsync`。这意味着这些元素的 XAML 映射在填充前不确定

6. **`RuntimeDataSource` 过多**：160+ 个属性标记为 `RuntimeDataSource`，但实际上很多属性（如 `href`、`src`、`alt`）应该在静态 XAML 中有 placeholder 值或默认值

## 3.3 设计建议

1. 为 `input` 的每种 type 创建独立的元素类（`HtmlInputTextDomElement`、`HtmlInputCheckboxDomElement` 等），或至少根据 type 动态分派
2. 为表格模型创建 `ITableLayoutAdapter` 接口，在 `HtmlTableDomElement` 层面统一处理行列索引和 Grid 附属属性
3. 将 `XamlElementNameCatalog` 改为 `Dictionary<string, XamlElementNameResolver>` 并按语义族拆分
4. 补充 `HtmlElementDataSourceCatalog` 覆盖所有交互和媒体元素
5. 将 `CreateXaml()` 中对运行时值的依赖改为对 HTML attribute 值的依赖（如 `type` attribute 而非 `RuntimeValue`），消除循环依赖

---

# 第四部分：结论

Iwesun.Runtime.Web 的数据获取和 XAML 转换框架设计优秀，属性槽位体系完整，CSS 覆盖全面（108 个属性），HTML attribute 声明无遗漏。但在 3 个关键领域存在 Critical 级别的设计缺陷：

1. **`<input>` 多态性**：20+ 种 type 全部映射为 `TextBox`，复选框/单选框/滑块/日期选择器等控件功能完全丢失。这是 **最严重的问题**，影响 `<input>` 的所有非文本变体。

2. **表格语义**：`td`/`th` 错误映射为 `Grid`，而非 `Grid` 的行列子元素。行列关系、colspan/rowspan 完全丢失。这是 **第二严重的问题**。

3. **有序列表**：`ol` 的自动编号生成能力缺失，`type`/`start`/`reversed` 属性虽然在 HTML 端采集了，但 XAML 端无任何转换。

建议按优先级依次修复：Critical（input type 分派 → 表格语义 → ol 编号）→ High（Link 槽位完整性 → span Inline 层次 → select 多选 → img Stretch → textarea 多行）→ Medium 级别问题。架构重构（如 input type 分派）可能需要新增元素类或重构 `CreateXaml()` 与 `DomFillAsync()` 的执行顺序，其他问题可通过修改现有 `CreateXaml()` / `BuildXamlAttributes()` 方法解决。

---

# 第五部分：2026-07-30 实现复核与旧结论更正

本节以当前代码和真实 WinUI 自检为准。上文第二至第四部分保留为历史审计输入，其中
“input 全部转 TextBox”“td/th 转 Grid”“ol 无编号”“span 转 StackPanel”等描述已经过时，
不得再作为当前实现结论。

## 5.1 强类型创建与专属转换

- `DomElement.CreateXaml()` 已是抽象方法；113 个 HTML 与 14 个 SVG 末级具体类型必须提供实现，
  缺少实现会直接导致编译失败。
- `ToXaml` 已拆为“具体类型选择 XAML 对象”与“基于反射槽位填充对象属性”两阶段，公共基类
  只复用确定的公共属性与行为，不再根据现场尺寸猜测控件类型。
- `input` 按设计态 `type` 分派到文本、密码、复选、单选、范围、数值、日期时间、颜色、文件、
  按钮和隐藏等专属 WinUI 控件。
- 表格已使用 `HtmlTablePanel`、`HtmlTableSectionPanel`、`HtmlTableRowPanel` 与单元格 `Border`
  结构，行列及 `rowspan`/`colspan` 不再由普通 Grid 容器猜测。
- 有序列表使用专属列表与列表项逻辑，保留 `start`、`reversed`、`type` 和 `li[value]` 语义。
- 复合短语内容使用 `HtmlInlineFlowPanel`，不再用纵向 `StackPanel` 破坏行内顺序。

## 5.2 数据获取与全局连接

- 每个槽位由反射元数据路由到 Runtime WebView2 的直接 DOM API；设计值、CSS 规则链接、
  computed/runtime 值、运行几何与运行数据保持独立槽位。
- 根级 `HtmlRuntimeDocumentRoot.QueryDomPropertiesAsync()` 现在观察最终查询结果并填充
  `HtmlRuntimeDesignRuntime`。因此 CSS、布局和运行状态连接不再依赖某一个脚本读取器的内部副作用；
  替换 `IWebRuntimeDomQuerySession` 实现也不会得到空的全局管理器。
- Link 描述允许来源 API 返回空值，根观察器会规范化为空字符串，避免把可选描述误判为必填身份。
- Runtime 属性目录已补齐捕获契约实际产生的 `colorScheme`、SVG paint/line 属性、
  `textDecorationLine` 与 `resource.references`；未知属性仍硬失败，不通过静默忽略掩盖契约漂移。
- 表单 reset 的所有者查找同时支持已挂载视觉树与尚未显示的内存 XAML 逻辑树，保证
  BuildXaml 后、DisplayXaml 前也能执行真实控件状态恢复。

## 5.2.1 ToXaml 初始化槽语义

- `ToXaml` 写入普通 attribute 或合成子控件 attribute 时，同时把最终规范化值写入属性所有者的
  XAML Initialization 槽；例如 DOM `320px` 生成 `Width="320"` 后，XAML Initialization 为 `320`。
- 这里只记录生成对象的真实设计初值，不写 XAML Runtime。Runtime 槽仍必须由已显示 WinUI 控件的
  查询引擎读取，禁止用 DOM 值或生成值回填冒充运行结果。
- 旧版转换输入的顶层 `href/type/value/src` 等兼容字段，会在进入具体元素 `CreateXaml()` 前，
  通过反射得到的属性所有者迁移到 DOM Initialization；因此强类型决策不会再对空属性壳执行。

## 5.3 Canvas 与动画边界

- Canvas 命令数组、TypedArray、线帽、连接、斜接限制、虚线偏移、文本对齐/基线/方向、
  矩形裁剪、圆角路径、阴影/滤镜透明度及脏矩形 `putImageData` 已进入结构化命令回放。
- `putImageData` 明确忽略当前 transform 与 globalAlpha，符合其像素写入语义。
- 当前 WinUI 对象层回放仍不能对任意已有像素执行所有 Canvas composite/filter 的逐像素精确替换；
  此项仍是后续像素缓冲渲染器的明确边界，不能宣称完整 Canvas 2D 规范已经实现。

## 5.4 真实执行验证

2026-07-30 当前验证结果：

- Runtime Web Debug/Release：275/275 通过。
- Runtime WebView2 Debug：22/22 通过。
- DoubaoUIClone 转换/契约 Release：120/120 通过。
- DoubaoUIClone App Debug（win-x64）：0 警告、0 错误。
- WinUI XAML 对象工厂真实进程自检：PASS；127 个元素类型参与，111 个对象实际创建；
  文本、类型化属性、ARGB/附加属性、SVG、Grid tracks、iframe 文档挂接以及内存显示后审核均通过。

自检入口已改为异步等待 UI 调度，不再在 UI 线程用同步等待制造假死。以上结果只证明当前强类型
对象创建与所列运行链路通过，不等同于目标网页所有元素、所有视觉状态已经达到 1:1。

## 5.5 时间输入强类型运行控件

状态分支自检发现，`date/month/week/time/datetime-local` 虽然已经选择不同 XAML 类型，但旧实现
只在外层 StackPanel 保存字符串，内部 WinUI picker 没有与 HTML 状态连接；公共 disabled 转换还会
尝试写不存在的 `StackPanel.IsEnabled`。该状态现已更正：

- 五类控件的 `Value`、`Minimum`、`Maximum`、`Step`、`IsReadOnly`、`IsEnabled` 均为正式
  DependencyProperty。
- HTML 初值会写入实际 CalendarDatePicker、DatePicker、TimePicker；内部编辑器变化会反向更新
  规范化的 HTML value。
- 月份控件隐藏日选择；周值使用 ISO week/year 换算；可表示的秒级 step 转换为 TimePicker 的
  MinuteIncrement。
- readonly 保持控件外观但禁止编辑；disabled 独立传播为编辑器 IsEnabled，二者不再混用。
- password 的 readonly 不再错误转换成 disabled；PasswordBox 保持启用和提交语义，仅关闭交互与
  TabStop，运行查询据此返回实际 readonly 状态。
- WinUI 运行查询器可直接读取时间控件的当前 value/minimum/maximum/readOnly/disabled，不能读取时
  继续返回 TargetUnsupported，不使用 DOM 回填。
- WinUI 真实进程自检新增 `Temporal input state: PASS`，覆盖五类目标、约束、只读状态及日期编辑
  反向写回。

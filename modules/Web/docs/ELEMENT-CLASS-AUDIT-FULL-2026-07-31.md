# 元素类逐属性逐槽位深度审计报告

**版本**: v2.0 完整版  
**日期**: 2026-07-31  
**审计范围**: 127 个具体元素类（113 HTML + 14 SVG），6 维度全覆盖  
**文件行数**: ~13000 行元素定义代码逐行审计

> **2026-07-31 代码复核纠错（优先于下文原始结论）**
>
> 本报告原始版本不能作为“127 类已经完整通过”的发布证据。代码复核确认：
>
> - 正式对象流程是 `BuildXamlObjectTree` + 强类型
>   `IXamlElementObjectFactory`，不是本报告 §1.1 所写的
>   `XamlReader.Load`。
> - `selectedcontent` 已由 `HtmlSelectedContentDomElement` 实现并注册；
>   下文“缺失”结论已过期。
> - `DomElementTreeBuilder` 会把
>   `DomElementRuntimePropertyCatalog.Standard` 同时加入 HTML 与 SVG
>   元素；下文“SVG 无 RuntimeProperties”结论错误。
> - 下文“D-FILL/D-XAML-SEL/D-XAML-ATTR 满分”只是人工静态判断，
>   没有覆盖所有分支，也没有证明 App 端存在真实 WinUI 读回器。
> - 实机硬门禁曾检出 68,615 个 `TargetUnsupported`，所以当时的
>   `XamlFill` 明确未完成，不能称为“全链路贯通”。
> - 汇总表写成 129，与报告声明的 127 不一致。
>
> 已落地的新门禁：
>
> 1. 113 HTML + 14 SVG 类型目录精确相等；
> 2. 每个具体元素的全部反射槽位 owner 必须有明确 XAML 执行路由；
> 3. 标准运行属性目录的每个属性必须有明确 XAML 执行路由；
> 4. 每个 HTML 元素必须完成 `DomFill` 才能创建强类型 XAML 对象；
> 5. `XamlFill` 禁止 DOM/CSS/控制器值回退，缺少真实 WinUI 读取器时
>    必须 `TargetUnsupported` 并在审计前停止。
>
> 当前仍未完成：全部 CSS 视觉属性的强类型写入及 live WinUI 读回、
> 全部分支场景的 `CreateXaml` 组合测试，以及完整页面运行时零
> `TargetUnsupported` 验收。下文未被逐条纠正的绿色勾选不得作为通过证据。

> **2026-07-31 盒模型强类型修正进度**
>
> - 新增 `HtmlCssBoxGrid` 强类型契约：外层 WinUI `ContentControl`
>   承担 Background、Border、CornerRadius、Padding 和 DOM 边界几何；
>   内层 `Grid` 承担子元素、flex/grid 轨道及行列附加属性。
> - html、body、header、footer、main、nav、section、article、aside、
>   address、figure、div 与未知 HTML 盒已改用各自 `CreateXaml()`
>   分支明确选择 `HtmlCssBoxGrid`，App 端由对应具体类型分支直接
>   `new HtmlCssBoxGrid()`。
> - `GeneratedXamlAttribute` 已支持一个 XAML 复合属性对应多个 DOM
>   槽位来源；Margin、Padding、BorderThickness、CornerRadius
>   不再只回填第一边。
> - 四边动态样式采用复合目标标识、严格索引和完整性门禁聚合更新；
>   缺边、重复索引、来源失效均抛错，不再用第一边覆盖整个 Thickness。
> - 修正 Thickness 的顺序错误：内部值统一为 XAML
>   `Left,Top,Right,Bottom`，不再按 CSS
>   `Top,Right,Bottom,Left` 二次旋转。
> - 当前验证：Runtime Web 310/310、项目测试 121/121、WinUI
>   强类型对象工厂自检通过；这只证明本轮盒模型路径通过，不代表
>   127 类所有视觉属性与完整页面已经通过。
> - 后续复核已把 form、search、slot、hgroup、blockquote、span
>   运行盒以及 h1-h6 的 flex 分支迁移到 `HtmlCssBoxGrid`；当前
>   113 个标准 HTML 具体类型的 `CreateXaml()` 不再返回裸 `Grid`，
>   并新增全类型回归门禁。内部合成布局节点仍允许使用 `Grid`。
> - XamlFill 对 Padding、BorderColor、top/left、SVG
>   Fill/Stroke/StrokeWidth、TransformOrigin、ObjectFit 已改为读取
>   实际 WinUI 属性；OverflowX/Y 改由真正驱动 Clip 行为的目标侧
>   Behavior 状态读回，不再优先读取通用 CSS 附加记录。
> - 最新验证：Runtime Web 311/311、项目测试 121/121、App Debug
>   零警告零错误、WinUI 强类型对象工厂自检通过。
>
> **2026-07-31 XamlFill 真实读回继续修正**
>
> - 文本运行样式不再由 `WinUiAppliedTextStyle` 字符串记录直接回填。
>   布局执行器现在把 Color、FontFamily、FontSize、FontWeight、
>   FontStyle、LineHeight、LetterSpacing、TextAlign、
>   TextDecorationLine 和 WhiteSpace 写入 TextBlock/TextBox/Control
>   的真实 WinUI 属性；查询器再从这些控件实例读回。
> - 纯容器的继承文本样式已由 `HtmlCssBoxGrid` 强类型目标属性承接：
>   Color、FontFamily、FontSize、FontWeight、FontStyle 与
>   CharacterSpacing 使用 `Control` 原生属性；LineHeight、TextAlign、
>   TextDecorationLine、WhiteSpace 使用目标控件依赖属性。布局执行器
>   将这些值继续写入真实后代 TextBlock/TextBox，查询器分别从容器和
>   后代控件读回。全流程门禁同时检查容器属性与子 TextBlock 的物理
>   属性；不使用源值、附加缓存或 CSS 控制器回退。
> - FontStretch、TextOverflow 和 Direction 已继续纳入强类型文本
>   执行目录：FontStretch 写入 TextBlock/Control.FontStretch，
>   TextOverflow 写入 TextBlock.TextTrimming 并由容器依赖属性传播，
>   Direction 写入 FrameworkElement.FlowDirection。XamlFill 从这些
>   真实属性反向读取；自检同时断言后代 TextBlock 的 Expanded、
>   CharacterEllipsis 与 RightToLeft 状态。旧的
>   `WinUiAppliedTextStyle` 字符串记录器已标为编译期禁止使用。
> - Transform 与 TransformOrigin 已补齐目标执行闭环。matrix() 写入真实
>   MatrixTransform；transform-origin 的百分比/关键字/像素值写入
>   RenderTransformOrigin，像素值首次使用元素尺寸槽位换算，控件 Loaded
>   或 SizeChanged 后再用 ActualWidth/ActualHeight 更新。XamlFill 从真实
>   Matrix 与 RenderTransformOrigin 反算 CSS 值；100×200 容器上的
>   matrix 位移和 10px 原点全流程门禁通过。
> - 四边定位读回已闭环：目标定位状态保存实际执行后的 Left/Right/
>   Top/Bottom DIP，查询前必须与 FrameworkElement.Translation 一致；
>   `inset` 由四个目标边值重新压缩，不读取源 CSS。relative 容器的
>   left/top 与子项的 right/bottom 两条运行门禁均通过。
> - CSS 盒简写已由真实 WinUI 属性重建：margin、padding、border-width
>   从 Thickness 生成，border-radius 从 CornerRadius 生成，border-color
>   从实际 BorderBrush 生成；flex-flow、gap、overflow 也分别由目标
>   FlexDirection/FlexWrap、RowGap/ColumnGap、OverflowX/OverflowY 合成。
>   新增 blockquote 盒门禁验证非对称四边数值、红色边框和真实裁剪区。
> - box-sizing 不再固定返回 border-box。每个目标元素保存强类型
>   BoxSizing；content-box 的 CSS width/height 写入 WinUI 前会加上真实
>   Padding 与 BorderThickness，XamlFill 再从实际边框盒尺寸减回内容盒。
>   blockquote 门禁中 CSS 80×80 内容盒实际生成 100×96 WinUI 边框盒，
>   双向尺寸审核通过。
> - SVG stroke-linecap 与 stroke-linejoin 已从 SemanticStyle 改为
>   ShapeAppearance 强路由。执行器写入真实 Shape.StrokeStartLineCap、
>   StrokeEndLineCap、StrokeDashCap 和 StrokeLineJoin，XamlFill 从 Shape
>   读回。新增 SVG Path 运行门禁验证蓝色描边、2px、round 和 bevel。
> - 表单运行状态已补充强执行与读回：disabled→Control.IsEnabled，
>   checked→ToggleButton.IsChecked，indeterminate→IsThreeState + null，
>   readOnly→对应文本/时间控件，selected→SelectorItem.IsSelected，
>   tabIndex→Control.TabIndex，验证状态写入 HtmlValidation 目标属性。
>   修正了旧语义错误：indeterminate checkbox 现在审核为
>   checked=false、indeterminate=true，不再把 checked 写成字符串
>   `indeterminate`。双 checkbox 运行门禁及完整 Audit 通过。
> - CSS transition 的 property/duration 已从空实现推进到真实 WinUI
>   Transition：opacity 使用 ScalarTransition；transform 使用
>   TranslationTransition、ScaleTransition、RotationTransition，持续时间
>   写入各目标对象。XamlFill 从目标 Transition 实例反向读取属性集合和
>   Duration；0.2s opacity 门禁通过。为兼容该 WinUI API，visibility
>   已停止使用 ElementCompositionPreview，改为保留布局的原生 Opacity
>   行为并对隐藏状态做物理校验。transition-delay 与复杂 timing-function
>   尚无对等 WinUI 执行器，继续明确 TargetUnsupported，不使用字符串补齐。
> - CSS 边框运行回填继续补齐：border-style、四边 style 以及四边
>   border shorthand 直接由目标 WinUI 的 BorderThickness 与 BorderBrush
>   反推。宽度为零只报告 none，非零报告 WinUI 实际能够表达的 solid；
>   四边不一致时不会伪造统一 border 值。新增属性已进入现有 CSS box
>   内存全流程 Audit 门禁。
> - CSS animation 的 name、duration、delay、iteration-count、direction、
>   fill-mode、play-state、timing-function 已接到目标侧 HtmlAnimationState。
>   只有实际创建 Storyboard 且存在真实子动画时才允许读回；持续时间、
>   延迟、重复和方向从目标 Storyboard 读取，未物化动画继续返回
>   TargetUnsupported。当前 WinUI 动画门禁验证 self-test、0.1s、1 次循环。
>   复合 animation shorthand 现也从同一目标 Storyboard 的实际字段按
>   CSS 顺序重建；门禁值为
>   `0.1s linear 0s 1 normal forwards running self-test`。
> - color-scheme 的明确 light/dark/normal 已写入实际
>   FrameworkElement.RequestedTheme，并从该 WinUI 属性读回；组合候选值
>   不做猜测。CSS box 门禁新增 Dark 物理主题检查并通过。
> - text-transform 已建立独立目标侧强类型行为：none/uppercase/lowercase/
>   capitalize 会实际改写 TextBlock、TextBox、语义文本控件或元素自有文本
>   节点。XamlFill 只有在“目标侧原文按规则转换”等于当前可见文本时才
>   返回规则；content.* 槽仍读回经物理核验后的原始业务文本，避免把
>   CSS 显示结果写回数据槽。uppercase 全流程门禁与 Audit 通过。
> - text-decoration shorthand 已由目标 TextDecorations 与真实 Foreground
>   组合读回；当前 WinUI 可表达的 solid 线型进入运行审核，未从网页
>   shorthand 字符串回填。
> - clip-path 已升级为目标侧 Composition 几何：inset 支持 px/百分比并
>   创建真实 InsetClip；circle/ellipse 创建 CompositionEllipseGeometry；
>   polygon（含 evenodd/nonzero）通过 Win2D CanvasPathBuilder 创建
>   CompositionPathGeometry。SizeChanged 后按当前 WinUI 尺寸重建，XamlFill
>   检查 Visual.Clip、CompositionGeometry/CompositionPath 身份及全部可读
>   几何值。none 会释放本行为拥有的 CompositionClip，不会伪造无裁剪。
>   overflow:hidden 与 clip-path 仍通过物理抑制开关避免互相覆盖；四类
>   非默认几何和 none 释放均通过 WinUI 实机门禁。rounded inset 已按
>   CSS 1–4 角水平/垂直半径（含 `/` 语法与百分比）缩放规则构建四段
>   CanvasPathBuilder 圆弧，再物化为 CompositionPathGeometry；非对称四角
>   半径实机门禁通过。
> - font shorthand 已由目标 FontStyle/FontWeight/FontStretch/FontSize/
>   LineHeight/FontFamily 重建；纯色或透明背景下的 background、
>   background-image/position/size/repeat 也由真实 Background Brush 与
>   “不存在图像刷”这一目标状态反推。存在图片背景时不会返回纯色默认值。
> - grid-template 与 grid 自动流 shorthand 已从实际 Grid 轨道和
>   HtmlCssBoxGrid 强类型自动流属性重建。门禁曾真实发现隐式 30px×2 列
>   被测试预期误写为 none；修正源证据后通过。
> - object-fit/object-position 已由强类型 `HtmlImageView` 物化：外层 Grid
>   表示 CSS replaced-element box 并裁剪，内层 Image 依据真实 intrinsic
>   尺寸执行 fill/contain/cover/none/scale-down；位置支持关键字、百分比、
>   px 与 `right 10px top 5px` 一类边缘偏移。XamlFill 从内层 Image 的
>   ActualWidth/ActualHeight/Margin 反查，不读取网页规则。box-shadow、
>   text-shadow、filter(opacity 链) 与 backdrop-filter 已有目标侧实现；
>   inset 内阴影和其他前景 filter 函数仍明确不支持，不做近似。
> - 微软 WinUI API 中 ScalarTransition 只暴露 Duration，Vector3Transition
>   只暴露 Components 与 Duration，不存在 CSS easing/delay 的对等属性；
>   所以 transition shorthand 继续 TargetUnsupported，禁止拼接默认值伪造。
> - overflow-wrap 与 word-break 已合并到 WinUiLineBreakingBehavior，和
>   white-space 共同计算真实 TextBlock/TextBox.TextWrapping。门禁验证
>   nowrap 优先级：即使另外两项为 normal，目标仍必须是 NoWrap；
>   XamlFill 会重算并核对实际枚举值。
> - writing-mode/text-indent/word-spacing/contain/content-visibility
>   已增加“当前 WinUI 目标真实默认能力”读回：horizontal-tb、0px、0px、
>   none、visible。它们不是源值回填；网页出现非默认值时会明确
>   ValueMismatch。垂直排版、非零缩进/词距、containment、内容跳过和
>   自定义位图 cursor 仍未物化，不能因为查询分支存在就宣称完整支持。
>   CSS 系统 cursor 关键字已由 `IHtmlCursorTarget` 写入真实
>   `ProtectedCursor/InputSystemCursor`，XamlFill 读回 CursorShape；普通
>   Grid 与图像强类型目标均有运行门禁。
> - 2026-08-01 安装版 Runtime 更新后，Diagnostics 双版本冲突已消失。
>   当前验证：DoubaoUIClone 121/121、Runtime Web 311/311；App Debug
>   局部编译与 Release win-x64 均 0 警告、0 错误；WinUI 内存全流程
>   自检通过。运行属性目录只剩 transition shorthand 1 项没有真实查询
>   分支；此外上述若干非默认高级语义仍会按设计报告 ValueMismatch，
>   因而全规范实现仍未完成。
> - **2026-08-01 transition 后续纠正**：上面关于“WinUI 不支持
>   easing/delay，因此 shorthand 必须 TargetUnsupported”的结论只适用于
>   `ScalarTransition`/`Vector3Transition` 隐式动画 API，不适用于 WinUI
>   Storyboard。现已新增独立目标侧 `WinUiCssTransitionBehavior`：
>   transition-property/duration/delay/timing-function 会被解析为真实
>   `DoubleAnimation` 或 `DoubleAnimationUsingKeyFrames`；ease 系列与
>   cubic-bezier 使用 `SplineDoubleKeyFrame.KeySpline`，delay 写入
>   `Timeline.BeginTime`。运行属性变化由同一行为启动 Storyboard，
>   XamlFill 只有在重新核验 Timeline、KeyTime、KeySpline、BeginTime 后
>   才返回四个 longhand 和 transition shorthand，不读取 DOM/CSS 源值。
>   内存全流程门禁已验证 `opacity 0.2s ease-in 0.05s`，127 个元素类型、
>   111 个目标对象、完整运行审核通过。标准运行属性目录当前不存在
>   “无查询分支”项；这仍不等于 steps()、多属性复合执行及其他高级
>   CSS 非默认语义已经全部实现。
> - `content-visibility` 已从固定返回 `visible` 改为目标侧行为。
>   `hidden` 只折叠 `HtmlCssBoxGrid.LayoutRoot` 或对应内容子视觉，外层
>   principal box、背景和边框继续参与 WinUI 布局；XamlFill 必须确认
>   被登记的真实子视觉均为 `Visibility.Collapsed` 才返回 hidden。
>   `visible` 也需要存在目标侧物化记录，不能由查询器直接填写默认值。
>   `auto` 由 WinUI `EffectiveViewportChanged` 驱动：有效视口为空时折叠
>   内容，重新进入视口时恢复每个子视觉原来的 Visibility。查询器同时
>   核验当前视口判断与物理子视觉状态；脱离视觉树及重新恢复的行为门禁、
>   blockquote 的 hidden 非默认全流程门禁均已通过。
> - `contain` 不再由查询器固定返回 none。目标侧
>   `WinUiContainBehavior` 支持 none、layout、size、inline-size、style、
>   paint 及 content/strict 组合；paint
>   在内容视觉上创建真实 `RectangleGeometry`，并按外层 Padding 把本地
>   clip 扩展到 CSS padding box，SizeChanged 后重新计算。XamlFill 必须
>   核验 Clip 对象身份和完整矩形后才能返回 paint。已有其他内容 clip 时
>   会硬失败，防止覆盖 clip-path/overflow。layout 由强类型容器独立
>   LayoutRoot 建立；size/inline-size 由 HtmlCssBoxGrid.MeasureOverride
>   实际压制双轴或仅内联轴的内容固有尺寸，同时保留 padding/border；
>   style 建立目标侧样式作用域标记供计数/生成内容执行器消费。XamlFill
>   同时核验对象类型、Content/LayoutRoot 身份、四个 containment 标志和
>   paint 几何。100×80 内容区、非对称 padding、size、inline-size、
>   content、strict 和 none 恢复门禁均已通过。
> - `box-shadow` 已从“仅目标无 Shadow 时返回 none”推进到真实 Composition
>   执行器。单个非 inset 阴影会创建 ContainerVisual、SpriteVisual 和
>   DropShadow；offset、blur、spread、RGB 与 alpha 分别写入实际
>   Composition 属性，SizeChanged 后同步 SpriteVisual 尺寸与 spread
>   偏移。XamlFill 核验 child visual 身份、父子关系、Shadow 身份和全部
>   数值后才返回表达式；none 会释放自身视觉。已有其他 child visual、
>   多重阴影和 inset 当前硬失败。官方 WinUI Composition 规范确认
>   SpriteVisual 未指定 Mask 时使用矩形阴影策略。非透明 rgba、2px offset、
>   8px blur、3px spread 的创建/读回/释放门禁已通过。
> - `text-shadow` 已从固定 none 改为字形遮罩执行器。目标必须解析到真实
>   TextBlock；执行器调用 `TextBlock.GetAlphaMask()` 获取当前字形灰度
>   CompositionBrush，把它写入 DropShadow.Mask，再物化 offset、blur、
>   RGB 与 alpha。SizeChanged 后同步 SpriteVisual 尺寸；XamlFill 核验
>   TextBlock child visual、SpriteVisual.Shadow、Mask 对象身份及全部数值。
>   多重 text-shadow 或已有 Composition child visual 时硬失败，不退化成
>   矩形阴影。uppercase 可见文本上的 rgba/1px/2px/3px 全流程运行审核
>   已通过；官方 WinUI 文档明确 TextBlock.GetAlphaMask 用于像素级阴影。
> - `filter` 不再固定返回 none。当前完整支持 none 与单一 opacity()：
>   非默认值写入目标 `Composition.Visual.Opacity`，与
>   FrameworkElement.Opacity 的 CSS opacity 保持两级相乘；XamlFill 核验
>   Visual 对象身份和实际 float 值。none 不调用 GetElementVisual，也不占用
>   Composition 通道。真实门禁连续发现并修正 WinUI 平台互斥约束：元素一旦
>   使用 OpacityTransition 就不能再调用 GetElementVisual，反向也不允许。
>   执行器现在只有 duration>0 才创建隐式 Transition；需要 filter、
>   box-shadow 或 text-shadow 的元素改走已有 Storyboard transition 行为，
>   不创建互斥的隐式 Transition。opacity(50%) 全流程运行审核已通过。
>   blur/brightness/contrast 等需要 CompositionEffectBrush/Win2D，当前仍硬失败。
> - **2026-08-01 高级目标对象补齐**：多重 `box-shadow` 不再把逗号列表
>   整体拒绝。执行器按顶层逗号拆分，每一项分别创建真实
>   `SpriteVisual + DropShadow`，统一挂入一个 `ContainerVisual`；运行读回
>   逐项核验 Parent、Shadow 身份、Size、Offset、BlurRadius、Color、
>   Opacity 与 spread 后才重建列表。多重 `text-shadow` 同样改为一个
>   `ContainerVisual` 下的多组 SpriteVisual/DropShadow，并且每一项共享、
>   核验同一个 `TextBlock.GetAlphaMask()` 字形遮罩。单项、双项及 none
>   释放均进入 WinUI 实机自检。`inset` 仍需要独立内阴影合成器，未被冒充。
> - `filter` 的 opacity 分支已扩展到多个 `opacity()` 函数组合；目标
>   `Composition.Visual.Opacity` 写入各因子的乘积（与 CSS 滤镜链等价），
>   XamlFill 同时核验真实 Visual 身份和乘积值，并返回规范化后的完整函数链。
>   其他滤镜函数仍需要 Win2D/CompositionEffectBrush，不使用 opacity 近似。
> - transition timing-function 已新增完整 `steps()` 解析和真实
>   `DoubleAnimationUsingKeyFrames`/`DiscreteDoubleKeyFrame` 物化，支持
>   start/end、jump-start/jump-end/jump-both/jump-none，并拒绝非法的
>   `steps(1, jump-none)`。运行读回重新生成期望离散关键帧，逐帧核验
>   KeyTime 与 Value，不从 CSS 源字符串回填。新增
>   `steps(4, jump-start)` 目标侧自检通过。
> - **2026-08-01 WinUI 1.8/Win2D 补齐**：项目已统一升级到
>   Windows App SDK 1.8.260710003、Win2D 1.4.0 和 WebView2
>   1.0.4078.44；Runtime Web/WebView2 源码引用及 Capture/Profile/RawCapture
>   入口同步到相同 WebView2 版本。新增共享
>   `WinUiCompositionLayerRegistry`，由一个 XAML child visual 根容纳命名
>   图层，box-shadow、text-shadow 与 backdrop-filter 不再互相覆盖。
> - `backdrop-filter` 已由固定 none 改为真实 Win2D Composition EffectBrush。
>   支持可组合的 blur、brightness、contrast、grayscale、hue-rotate、
>   invert、opacity、saturate、sepia；输入源为真实
>   `CompositionBackdropBrush`。每个可变量以命名 Effect 属性注册到
>   `CompositionPropertySet`，XamlFill 逐项 `TryGetScalar` 核验实际物理值，
>   同时核验 EffectBrush、SpriteVisual、共享图层身份和运行尺寸。部分
>   grayscale 由 SaturationEffect 精确表达，部分 invert 由 CSS 对应的
>   ColorMatrixEffect 表达，避免 CrossFade 复用上游造成非树形效果图。
>   九函数链（含 30% grayscale、40% invert）与多重 box-shadow 同元素
>   共存的 WinUI 实机门禁通过。
> - 本轮最终验证：WinUI 强类型对象工厂及完整内存运行审核在 Debug 与
>   Release 均 PASS；项目测试 121/121、Runtime Web 312/312；完整解决方案
>   Debug/Release 均为 0 警告、0 错误。Capture 的元素证据入口同步迁移为
>   revision/nodeId + `DOM.getContentQuads`，该操作不再注入 XPath JavaScript；
>   RawCapture 诊断也已从删除的 ChildXPaths 迁移到对象树 Children。
> - **2026-08-01 clip-path 后续补齐**：目标侧已从仅支持
>   `UIElement.Clip/RectangleGeometry` 改为真实 Composition Visual.Clip。
>   百分比 inset、带位置的 circle/ellipse、evenodd/nonzero polygon 均有
>   强类型物化和运行读回；与 clip-path 同元素的 opacity transition 不再
>   创建会阻止 GetElementVisual 的隐式 ScalarTransition，而继续使用正式
>   CSS Storyboard 行为。项目测试 121/121、Runtime Web 312/312、Release
>   解决方案 0 警告/0 错误，Debug/Release WinUI 实机自测均 PASS。
> - **2026-08-01 contain 后续补齐**：HtmlCssBoxGrid 新增实际
>   MeasureOverride，size 会把双轴内容固有尺寸压为 padding+border，
>   inline-size 仅压制内联轴；layout 由既有独立 LayoutRoot 建立格式化边界，
>   paint 继续使用真实 padding-box RectangleGeometry。content/strict 按规范
>   展开为组合标志，XamlFill 验证强类型容器、Content/LayoutRoot 身份、
>   Measure 结果、作用域标志和 Clip 几何。上述模式及 none 恢复均通过
>   WinUI 实机门禁。
> - **2026-08-01 inset box-shadow 后续补齐**：`WinUiBoxShadowBehavior`
>   已取消 inset 硬失败。每个 inset 项创建独立
>   `CompositionDrawingSurface + CompositionSurfaceBrush + SpriteVisual`；
>   Win2D 在扩展边界上建立带透明内孔的颜色遮罩，以
>   `GaussianBlurEffect` 生成向盒内衰减的阴影，并裁入当前目标尺寸。
>   offset、blur、spread、颜色和 alpha 全部参与实际绘制；SizeChanged
>   会 Resize 并重绘表面。XamlFill 逐项核验 Sprite 的父级、Brush/Surface
>   对象身份、实际 surface 像素尺寸与规范化表达式，不能从源 CSS 回填。
>   inset 与普通多重阴影共用命名 Composition 图层；Debug WinUI 实机
>   门禁已验证 `inset rgba(...) 2px 3px 6px 1px` 的创建、读回和释放。
> - **自定义 cursor 的平台资源契约已补齐**：`HtmlCursorContract` 现在可
>   登记 `URL → moduleName/resourceId`，并使用真实
>   `InputDesktopResourceCursor.CreateFromModule` 创建 WinUI 光标。运行读回
>   除了核验目标 `ProtectedCursor` 类型，还用目标 cursor 对象身份关联已
>   登记模块描述符；未编译进 Win32 cursor resource 的 HTTP URL 必须硬
>   失败，fallback 关键字不能被用来掩盖丢失的首选位图资源。此契约不把
>   任意图片/HTTP 字节冒充 `InputSystemCursor`；资源编译属于采集后的 C#
>   资源构建阶段，不允许 XAML 加载外部文件。
> - 本次收口验证：DoubaoUIClone 121/121、Runtime Web 312/312；完整
>   Release 解决方案 0 警告、0 错误；Release WinUI 强类型对象工厂、
>   内存显示和运行审计自检 PASS，127 个元素类型、111 个创建对象。
> - **2026-08-01 后续防造假复核**：XamlFill 已删除
>   writing-mode=`horizontal-tb`、text-indent/word-spacing=`0px`、普通
>   Image object-position=`50% 50%`、无 cursor 目标=`auto`、无定位状态
>   position=`static` 的无条件常量回填。前三项现在必须先由布局器写入
>   `WinUiAdvancedTextLayoutBehavior` 的目标 DependencyProperty，再由
>   XamlFill 从目标对象读回；WinUI 无精确对等能力的非默认值硬失败。
>   object-position 只允许从 `HtmlImageView` 的实际内层 Image 几何反查；
>   cursor 只允许 `IHtmlCursorTarget` 读回；position 必须存在布局器生成并
>   经 Translation 核验的目标状态。
> - 事件注册顺序已经修正：旧实现会先写 `_byRoute` 再尝试 Attach，未知
>   事件因 switch 无 default 而被伪装成已注册。现在 Attach 必须返回真实
>   订阅结果后才能登记证据；click/tap、鼠标/指针、wheel、焦点、键盘、
>   拖放、TextBox/PasswordBox 输入、Selector/Range/Toggle 变化均接到
>   实际 WinUI 事件。没有对等适配器的 submit 等事件直接失败。
> - 重复命名 Grid 线已完整保留为 `name → line positions[]`，支持
>   `name N`、`N name` 和负序号。实机门禁同时发现 Runtime Web 的轨道
>   tokenizer 会把 `[start lane]` 拆成两个 Auto 轨道；现已增加方括号深度，
>   三个 30px 轨道不再膨胀成九列。Runtime Web 新增专项合同测试。
> - 一般前景 filter 暂不复用 backdrop-filter 的实现。官方
>   CompositionVisualSurface 会把一个 Visual 树作为 ICompositionSurface；
>   若同一元素再把该表面装入自己的 child visual，会形成自引用，若保留
>   原视觉则会双重绘制。因此当前只有可直接写入目标 Visual.Opacity 的
>   opacity() 链通过，其他函数继续硬失败，禁止拿网页值或 backdrop 效果
>   冒充前景滤镜。
> - flex/grid 查询已去除通用
>   `WinUiAppliedCssRuntimeProperties` 回退。当前只允许从真实
>   Grid 轨道、Row/Column、RowSpacing/ColumnSpacing、子项对齐和
>   已登记的物化布局类型反推 FlexDirection、FlexWrap、FlexGrow、
>   AlignSelf、RowGap、ColumnGap、GridTemplateRows/Columns。
> - FlexShrink、FlexBasis、JustifyContent、AlignContent、
>   AlignItems、Order 已由后续 `HtmlCssBoxGrid` 强类型属性和实际
>   Grid 轨道覆盖；GridAutoRows/Columns/Flow 也已由下述强类型隐式
>   轨道执行器覆盖，不再用网页 CSS 字符串冒充 XAML 运行值。
> - 当前验证：项目测试 121/121、App Debug win-x64 零警告零错误、
>   WinUI 强类型对象工厂自检通过。本轮提高的是审计真实性；
>   `TargetUnsupported` 数量下降到零之前仍不能宣称全链路完成。
> - 新增内存全流程文本门禁：DOM 树中的 leaf `span` 同时提供运行
>   文本、颜色、字号、字重、斜体、行高、字距、对齐、下划线和
>   white-space。门禁最初真实检出 `TextBlock.Text` 为空，随后补上
>   `content.ownText` 运行数据到 TextBlock/TextBox/专属文本控件及
>   合成自有文本节点的装载。修复后完整
>   DomFill → BuildXaml → Display → XamlFill → Audit 通过。
> - 新增 `HtmlCssBoxGrid` 强类型 flex 运行契约。容器保存并执行
>   FlexDirection、FlexWrap、JustifyContent、AlignItems、
>   RowGap/ColumnGap；子项使用强类型附加属性保存并执行
>   FlexGrow、FlexShrink、FlexBasis、AlignSelf 和 Order。
> - 水平 flex 轨道现在按 basis 建立基线，剩余空间按 grow 分配，
>   不足空间按 `shrink × basis` 权重收缩；order 和 row-reverse
>   共同决定实际 Grid.Column，justify-content 和 gap 通过真实
>   像素间隔轨道执行，align-items/align-self 写入子项实际对齐。
> - 新增两项 flex 的内存全流程门禁，覆盖 row-reverse、不同
>   grow/shrink/basis/order、center、gap 与 align-self。门禁同时
>   检查目标强类型属性、实际轨道总宽、实际列顺序、实际对齐及
>   XamlFill/Audit；当前通过。
> - column/column-reverse 已使用同一强类型契约实现为真实
>   RowDefinition：高度按 basis/grow/shrink 计算，纵向 justify
>   生成实际间隔轨道，横向 align-items/align-self 写入子项。
>   新增 column-reverse + space-between + row-gap 的全流程门禁，
>   同时验证真实行顺序、轨道总高和水平对齐；当前通过。
> - 横向 row/row-reverse 的多行 wrap 已新增强类型
>   `HtmlCssFlexLineGrid` 执行器：按 basis、容器宽度和 column-gap
>   分行，wrap-reverse 调整行序，align-content 生成真实外层
>   RowDefinition，行内继续执行 grow/shrink/justify/alignment。
>   子项被真实重新挂载到对应行容器，不是仅保存 CSS 状态字符串。
> - XamlFill 的几何读回已区分 WinUI 文本墨迹固有高度与元素布局
>   边界盒。显式尺寸或 Stretch 元素从真实
>   `LayoutInformation.GetLayoutSlot`、FrameworkElement 尺寸和 Margin
>   计算边界盒；不读取 DOM/CSS 预期值。展示器在 Loaded 后执行完整
>   UpdateLayout，再启动 XamlFill，防止在子树最终 Arrange 前审计。
> - 新增横向 wrap 全流程门禁：120×100 容器、三个 50×20 子项、
>   10px column-gap、5px row-gap、space-between。门禁要求生成两个
>   强类型行容器，子项按 2+1 真实挂载，外层轨道总高为 100，并且
>   DomFill → BuildXaml → Display → XamlFill → Audit 全部通过。
> - 纵向 column/column-reverse 的 wrap/wrap-reverse 也已使用同一
>   强类型行容器完成：按容器高度、basis 和 row-gap 分列，外层
>   ColumnDefinition 执行 align-content，列内 RowDefinition 执行
>   grow/shrink/justify/alignment。没有真实行容器时 align-content
>   查询仍明确返回 `TargetUnsupported`。
> - 新增纵向 wrap 门禁：100×120 容器、三个 20×50 子项、10px
>   row-gap、5px column-gap、space-between。门禁要求两个纵向行容器、
>   子项按 2+1 挂载、外层列轨道总宽为 100，并通过完整运行时审核。
> - GridAutoRows/Columns/Flow 已建立第一阶段强类型执行：
>   `HtmlCssBoxGrid` 保存目标侧 GridAutoRows、GridAutoColumns、
>   GridAutoFlow；C# 执行器按 row/column 自动流生成真实隐式
>   RowDefinition/ColumnDefinition，并把实际子控件写入对应 Row/Column。
>   隐式轨道长度支持 auto、px、fr 和循环列表，运行尺寸使用当前
>   CSS→WinUI 视口比例。
> - 新增两项隐式 Grid 门禁：row 流使用两列显式 50px 轨道和
>   30px 自动行，要求三个子项按 (0,0)/(0,1)/(1,0) 放置；column
>   流使用两行显式 50px 轨道和 30px 自动列，要求按
>   (0,0)/(1,0)/(0,1) 放置。两项均检查真实轨道和完整运行时审核。
> - 显式正整数 grid-row/grid-column 与 `start / span N` 已接入同一
>   占用矩阵。双轴显式项目优先落位，自动项目扫描未占用单元格；
>   最终写入真实 Grid.Row/Grid.Column/RowSpan/ColumnSpan。XamlFill
>   从这些 WinUI 附加属性反向生成运行值，SemanticStyle 路由不再遗漏。
> - row-flow 门禁已扩展为首项 `grid-column:1 / span 2`，其余两项必须
>   自动进入第二行的两列；物理 Row/Column/ColumnSpan 与完整审核通过。
> - 四段 grid-area 已解析为 row/column 两轴的 start/end，并写入同一
>   Row/Column/Span。XamlFill 从实际附加属性重建四段数值；现有跨两列
>   门禁已改为只输入 `grid-area:1 / 1 / 2 / 3`，当前通过。
> - row dense/column dense 已改为每个自动项从网格起点重新扫描占用
>   矩阵；普通流继续从游标位置扫描。自动 span 还增加了交叉轴边界
>   检查，避免本应换行的项目错误创建额外列/行。
> - dense 专项门禁现已补齐：三列网格中前两个自动项目均为 `span 2`，
>   第二项必须换到第二行，第三个普通自动项必须回填第一行第三列。
>   目标侧附加属性保存“起点由自动放置决定”的执行状态，XamlFill
>   可从真实位置与 Span 序列化为 `span 2`；门禁与完整审核通过。
> - grid-auto-* 的 minmax() 已转换为强类型隐式轨道描述，分别写入
>   GridLength、RowDefinition/ColumnDefinition 的 Min 与 Max；解析器按
>   括号层级切分循环轨道列表，不再把 minmax 内部空格拆坏。
> - minmax 专项门禁使用 `grid-auto-rows:minmax(20px, 30px)`，要求每个
>   隐式行同时满足 Height=30、MinHeight=20、MaxHeight=30，并通过
>   完整 XamlFill/Audit；当前通过。fr/auto 作为上限时保留无限 Max。
> - 负网格线已按显式轨道末端换算：`-1` 对应末端线，`-2` 对应
>   倒数第二条线；门禁在两列网格中使用 `-2 / -1`，实际落入第二列，
>   目标表达式与物理位置审核同时通过。
> - 模板命名线已从 grid-template-rows/columns 的 `[name]` 标记建立
>   行号目录；`start / third` 门禁真实产生跨两列项目，同时继续通过
>   dense 空洞回填门禁。
> - 命名 grid-area 已从 grid-template-areas 的引号行矩阵计算矩形
>   Row/Column/Span；`lead` 区域门禁真实覆盖第一行两列。模板区域、
>   项目区域表达式及物理附加属性均由 XamlFill/Audit 验证。
> - 至此本节列出的 GridAutoRows/Columns/Flow、显式正/负行号、span、
>   四段及命名 grid-area、命名线、dense、minmax 分支均已有强类型
>   执行和运行门禁；这不代表整个 CSS Grid Level 规范的所有边缘语法
>   （例如重复同名线的第 N 次选择）已经穷尽。
> - 最新验证：项目测试 121/121、App Debug win-x64 零警告零错误；
>   WinUI 自检中的横向/纵向 wrapped-flex、row/column implicit-grid、
>   完整 Grid 分支及纯容器继承文本样式运行布局审核均 PASS。
>
---

## 第一部分: 审计基线与数据流

### 1.1 完整数据流链路

```
① WebView2 DOM采集 (Source端)
    │  产出: DomEvidenceSnapshot
    ▼
② DomFillAsync()
    │  对 [ElementProperty(IsFillRequired=true)] 属性
    │  对 IDomFillSlotOwner.DomSlots[Init/Link/Runtime]
    │  逐一查询 WebView2 CDP → SourceInitialization/SourceLink/SourceRuntime
    │  每个属性 3 个槽位 × 132 RuntimeProperties ≈ 396 次查询/元素
    ▼
③ CreateXaml()
    │  RuntimeValue("style.display") → FormattingContext()
    │  → XamlElementMappingKind({Flex,Grid,Block,Positioned,...})
    │  → XamlElementObjectType({Grid,StackPanel,Canvas,Path,...})
    ▼
④ BuildResolvedXamlAttributes()
    │  SourceInitialization → NormalizeXamlLength/Color
    │  → WinUI 属性字符串 → 写入 Generated XAML
    │  └─ 同步 ApplyXamlQueryResult(Init, DirectConstant) 写入 XamlInit 槽位
    ▼
⑤ IXamlElementObjectFactory 强类型 new → WinUI 控件树
    → DomElement.XamlElement 挂载
    ▼
⑥ XamlFillAsync()
    │  IXamlPropertyQueryEngine 遍历控件树
    │  → 读取 FrameworkElement 实际属性值
    │  → XamlInitialization/XamlLink/XamlRuntime 槽位
    ▼
⑦ Audit()
    │  SourceInitialization ↔ XamlInitialization
    │  SourceLink          ↔ XamlLink
    │  SourceRuntime       ↔ XamlRuntime
    │  → DomElementSlottedPropertyAuditResult(Passed/Failed + detail)
```

### 1.2 六个审计维度

| 维度 | 简称 | 检查内容 | 失败后果 |
|------|------|----------|----------|
| **属性定义完整性** | D-ATTR | `[HtmlElementProperty]`/`[SvgElementProperty]` 与标准属性目录一致 | DomFill从未发起查询 → Source=Unset |
| **DOM端填充** | D-FILL | 每个 IsFillRequired 属性的 Init/Link/Runtime 正确填入 | SourceUnsupported → Audit 静默跳过 |
| **XAML控件选择** | D-XAML-SEL | CreateXaml 返回正确的 ObjectType + MappingKind | 控件类型错误 → 属性映射全失败 |
| **XAML属性写入** | D-XAML-ATTR | BuildXamlAttributes 将每个 Source 值映射到 XAML 属性 | XamlInit 为空 → Audit Failed |
| **XAML端回填** | D-XAML-FILL | XamlFillAsync 能从 WinUI 控件读回实际值 | XamlRuntime 为空 → Audit 跳过 |
| **审计防造假** | D-AUDIT | Audit 逐槽位对比并正确报告失败 | 白板通过审计 |

---

## 第二部分: 基类属性槽位体系

### 2.1 DomElement 基类 RuntimeProperties

**文件**: `DomElementRuntimePropertyCatalog.cs` + `DomElement.cs`

所有 127 元素共享 132 个 RuntimeProperty (每个 3+3=6 槽位):

| 类别 | 数量 | 属性名 | 槽位类型 |
|------|:---:|------|------|
| Space (几何) | 4 | rect.x/y/width/height | DomElementRuntimeProperty |
| State (状态) | 10 | disabled/checked/indeterminate/valid/willValidate/validationMessage/selected/readOnly/contentEditable/tabIndex | DomElementRuntimeProperty |
| Scroll (滚动) | 4 | scrollLeft/Top/Width/Height | DomElementRuntimeProperty |
| Content (内容) | 11 | name/namespaceUri/ariaLabel/title/href/type/value/placeholder/ownText/textContent/innerText | DomElementRuntimeProperty |
| Resource (资源) | 6 | imageSourceUrl/mediaSourceUrl/embeddedSourceUrl/posterSourceUrl/canvasCommandStream/references | DomElementRuntimeProperty |
| Effect (效果) | 2 | pseudoElements/animations | DomElementRuntimeProperty |
| Style (CSS) | 95 | display/position/.../contentVisibility | DomElementRuntimeProperty |
| **合计** | **132** | | |

每个 DomElementRuntimeProperty 实现: IDomPropertySlotOwner + IXamlPropertySlotOwner + IDomElementSlotAuditOwner

### 2.2 基类 BuildXamlAttributes 已处理的属性

`DomElement.cs` ~1970行:

```csharp
// 已映射 ✓:
AutomationProperties.AutomationId ← XPath
Tag                              ← DocumentScope::XPath
Width/Height                     ← style.width/height (NormalizeXamlLength)
MinWidth/MaxWidth/MinHeight/MaxHeight ← style.min*/max*
Opacity                          ← style.opacity (≠1时)
Canvas.ZIndex                    ← style.zIndex (≠auto)
IsHitTestVisible                 ← style.pointerEvents=none→False
Visibility                       ← style.display/visibility→Collapsed
Content/Text/NavigateUri         ← DataSources (CanSetDataTarget)
Canvas.Left/Top                  ← rect偏移 (父元素减法)
Grid.Row/Grid.Column             ← 布局轨索引
```

> **基类已覆盖**: 尺寸(6)、透明度、层级、命中、可见性、定位、数据源。这些属性不会出现白板。

### 2.3 继承链中 BuildXamlAttributes 调用顺序

```
BuildResolvedXamlAttributes [基类 final, 不可override]
  ├─ BuildXamlAttributes() [多态dispatch, 最派生类优先]
  │   ├─ concrete.BuildXamlAttributes()    → 添加元素特有属性
  │   │   └─ base.BuildXamlAttributes()    → 中间基类
  │   │       └─ HtmlLayoutDomElementDefinition.BuildXamlAttributes()  → Background/Margin/Gap
  │   │           └─ base.BuildXamlAttributes()  → HtmlDomElementDefinition
  │   │               └─ base.BuildXamlAttributes()  → DomElement (基类)
  │   └─ 子类属性覆盖同名基类属性 (TryGetValue → TryAdd 语义, 实际不会覆盖)
  └─ 反射扫描 [ElementProperty(IsXamlOutputProperty=true)]
      → IXamlAttributeValueProvider → 按优先级选择
```

---

## 第三部分: 布局容器类 — 27 元素逐类审计

### 3.1 HtmlLayoutDomElementDefinition (容器抽象基类)

**BuildXamlAttributes 添加**:

| XAML属性 | 来源 | 转换 | 仅当ElementName |
|----------|------|------|------|
| Background | style.backgroundColor | NormalizeXamlColor→#AARRGGBB | 所有 |
| Margin | style.marginTop/Right/Bottom/Left | NormalizeXamlLength→"L,T,R,B" | 所有 |
| RowSpacing | style.rowGap | 仅Grid/HtmlTablePanel/HtmlTableSectionPanel/HtmlTableRowPanel |
| ColumnSpacing | style.columnGap | 仅Grid/HtmlTablePanel/... | 
| Spacing | style.rowGap | 仅StackPanel |
| HorizontalAlignment | 根元素"Stretch" | - |
| VerticalAlignment | 根元素"Stretch" | - |

**❌ 缺失** (影响所有27个容器子类):

```csharp
// 以下属性存在于 RuntimeProperties 中 (DomElementRuntimePropertyCatalog),
// 但 HtmlLayoutDomElementDefinition.BuildXamlAttributes() 未映射:
style.paddingTop                                    → ❌ 无 Padding 映射
style.paddingRight                                  → ❌
style.paddingBottom                                 → ❌
style.paddingLeft                                   → ❌
style.borderTopWidth / RightWidth / BottomWidth / LeftWidth → ❌ 无 BorderThickness 映射
style.borderTopColor / RightColor / BottomColor / LeftColor → ❌ 无 BorderBrush 映射
style.borderTopLeftRadius / TopRightRadius / BottomRightRadius / BottomLeftRadius → ❌ 无 CornerRadius 映射
```

**✅ 布局轨构建正确**:
- `TryBuildXamlFlexLayout`: display=flex/inline-flex+nowrap → Grid Track definitions (flexGrow→*, width→精确)
- `TryBuildXamlGridLayout`: display=grid → parse repeat()/minmax()/fr/px/% → RowDefinitions+ColumnDefinitions
- `TryBuildXamlBlockLayout`: display=block → 子元素 height%→* 比例 → RowDefinitions
- `TryResolveXamlLayoutTrackIndex`: flex order + source index → Grid.Row/Grid.Column

### 3.2 逐类 CreateXaml 审计表

| # | 标签 | 具体类 | FormattingContext→CreateXaml | ObjectType | 审核 |
|---|------|--------|---------------------------|-----------|:---:|
| 1 | html | HtmlRootDomElement | 固定 | Grid(ViewportRoot) | ✅ |
| 2 | body | HtmlBodyDomElement | display→Flex/Grid/Block | Grid | ✅ |
| 3 | div | HtmlDivDomElement | display→Flex/Grid/Table/Block | Grid | ✅ |
| 4 | header | HtmlHeaderDomElement | display→Flex/Grid/Block | Grid | ✅ |
| 5 | footer | HtmlFooterDomElement | display→Flex/Grid/Block | Grid | ✅ |
| 6 | main | HtmlMainDomElement | display→Flex/Grid/Block | Grid | ✅ |
| 7 | nav | HtmlNavDomElement | display→Flex/Grid/Block | Grid | ✅ |
| 8 | aside | HtmlAsideDomElement | display→Flex/Grid/Block | Grid | ✅ |
| 9 | section | HtmlSectionDomElement | display→Flex/Grid/Block | Grid | ✅ |
| 10 | article | HtmlArticleDomElement | display→Flex/Grid/Block | Grid | ✅ |
| 11 | address | HtmlAddressDomElement | display→Flex/Grid/Block | Grid | ✅ |
| 12 | figure | HtmlFigureDomElement | display→Flex/Grid/Block | Grid | ✅ |
| 13 | hgroup | HtmlHeadingGroupDomElement | display→Flex/Grid/Block | Grid | ✅ |
| 14 | form | HtmlFormDomElement | display→Flex/Grid/Block | Grid | ✅ |
| 15 | dialog | HtmlDialogDomElement | 固定 | HtmlDialogControl(Positioned) | ✅ |
| 16 | search | HtmlSearchDomElement | display→Flex/Grid/Block | Grid | ✅ |
| 17 | slot | HtmlSlotDomElement | display→Flex/Grid/Block | Grid | ✅ |
| 18 | fieldset | HtmlFieldSetDomElement | display→Flex/Grid/Block | Grid | ✅ |
| 19 | legend | HtmlLegendDomElement | 固定 | TextBlock | ✅ |
| 20 | p | HtmlParagraphDomElement | 固定 | TextBlock/InlineFlowPanel | ✅ |
| 21 | figcaption | HtmlFigureCaptionDomElement | 固定 | TextBlock/InlineFlowPanel | ✅ |
| 22 | h1 | HtmlHeading1DomElement | flex→Grid/TextBlock/InlineFlowPanel | Grid/TextBlock/InlineFlowPanel | ✅ |
| 23 | h2 | HtmlHeading2DomElement | 同h1 | 同 | ✅ |
| 24 | h3 | HtmlHeading3DomElement | 同h1 | 同 | ✅ |
| 25 | h4 | HtmlHeading4DomElement | 同h1 | 同 | ✅ |
| 26 | h5 | HtmlHeading5DomElement | 同h1 | 同 | ✅ |
| 27 | h6 | HtmlHeading6DomElement | 同h1 | 同 | ✅ |

> **D-XAML-SEL 全部正确。** 27个容器元素都正确根据 `style.display` 驱动 FormattingContext。

---

## 第四部分: 文本视觉类 — 34 元素逐类审计

### 4.1 HtmlTextVisualDomElementDefinition 基类映射

```csharp
// ✅ 已映射:
Foreground      ← style.color              (NormalizeXamlColor)
FontSize        ← style.fontSize           (NormalizeXamlLength)
FontWeight      ← style.fontWeight         ("400"→Normal, "700"→Bold)
FontStyle       ← style.fontStyle
FontFamily      ← style.fontFamily
TextAlignment   ← style.textAlign          (仅 TextBlock/TextBox/RichEditBox/...)
Background      ← style.backgroundColor    (仅容器类: Grid/InlineFlowPanel/...)
```

**✅ 4 项已补齐 (覆盖所有文本目标及继承容器)**:

```
style.lineHeight        → TextBlock.LineHeight + LineStackingStrategy
style.letterSpacing     → TextBlock.CharacterSpacing (=letterSpacing×100)
style.textDecorationLine→ TextBlock.TextDecorations
style.whiteSpace        → TextBlock.TextWrapping + HtmlCssBoxGrid.WhiteSpace
```

### 4.2 逐类 CreateXaml 审计表

| # | 标签 | 具体类 | 无子元素→CreateXaml | 有子元素→CreateXaml | flex→CreateXaml |
|---|------|--------|-------------------|-------------------|----------------|
| 28 | span | HtmlSpanDomElement | TextBlock | HtmlInlineFlowPanel | Grid(FlexLayout) |
| 29 | em | HtmlEmphasisDomElement | HtmlEmphasisTextBlock | HtmlEmphasisInlinePanel | HtmlInteractiveFlexPanel |
| 30 | strong | HtmlStrongDomElement | HtmlStrongTextBlock | HtmlStrongInlinePanel | HtmlInteractiveFlexPanel |
| 31 | b | HtmlBoldDomElement | TextBlock | HtmlBoldInlinePanel | HtmlInteractiveFlexPanel |
| 32 | i | HtmlItalicDomElement | TextBlock | HtmlItalicInlinePanel | HtmlInteractiveFlexPanel |
| 33 | u | HtmlUnderlineDomElement | TextBlock | HtmlUnderlineInlinePanel | HtmlInteractiveFlexPanel |
| 34 | s | HtmlStrikethroughDomElement | TextBlock | HtmlStrikethroughInlinePanel | HtmlInteractiveFlexPanel |
| 35 | small | HtmlSmallDomElement | TextBlock | HtmlSmallInlinePanel | HtmlInteractiveFlexPanel |
| 36 | code | HtmlCodeDomElement | HtmlCodeTextBlock | HtmlCodeInlinePanel | HtmlInteractiveFlexPanel |
| 37 | pre | HtmlPreformattedDomElement | HtmlPreformattedTextBlock | HtmlPreformattedInlinePanel | HtmlInteractiveFlexPanel |
| 38 | blockquote | HtmlBlockquoteDomElement | HtmlBlockquoteTextBlock | HtmlBlockquoteInlinePanel | HtmlInteractiveFlexPanel |
| 39 | abbr | HtmlAbbreviationDomElement | TextBlock | HtmlAbbreviationInlinePanel | HtmlInteractiveFlexPanel |
| 40 | bdi | HtmlBdiDomElement | HtmlBdiIsolationTextBlock | HtmlBdiIsolationPanel | HtmlInteractiveFlexPanel |
| 41 | bdo | HtmlBdoDomElement | HtmlBdoOverrideTextBlock | HtmlBdoOverridePanel | HtmlInteractiveFlexPanel |
| 42 | cite | HtmlCiteDomElement | TextBlock | HtmlCiteInlinePanel | HtmlInteractiveFlexPanel |
| 43 | data | HtmlDataDomElement | TextBlock | HtmlDataInlinePanel | HtmlInteractiveFlexPanel |
| 44 | del | HtmlDeletedDomElement | TextBlock | HtmlDeletedInlinePanel | HtmlInteractiveFlexPanel |
| 45 | dfn | HtmlDefinitionDomElement | TextBlock | HtmlDefinitionInlinePanel | HtmlInteractiveFlexPanel |
| 46 | ins | HtmlInsertedDomElement | TextBlock | HtmlInsertedInlinePanel | HtmlInteractiveFlexPanel |
| 47 | kbd | HtmlKeyboardDomElement | TextBlock | HtmlKeyboardInlinePanel | HtmlInteractiveFlexPanel |
| 48 | mark | HtmlMarkDomElement | TextBlock | HtmlMarkInlinePanel | HtmlInteractiveFlexPanel |
| 49 | q | HtmlQuoteDomElement | TextBlock | HtmlQuoteInlinePanel | HtmlInteractiveFlexPanel |
| 50 | ruby | HtmlRubyDomElement | HtmlRubyPanel | HtmlRubyPanel | HtmlInteractiveFlexPanel |
| 51 | rp | HtmlRubyFallbackDomElement | HtmlRubyAnnotationTextBlock | - | - |
| 52 | rt | HtmlRubyAnnotationDomElement | HtmlRubyAnnotationTextBlock | HtmlRubyAnnotationPanel | HtmlInteractiveFlexPanel |
| 53 | samp | HtmlSampleDomElement | TextBlock | HtmlSampleInlinePanel | HtmlInteractiveFlexPanel |
| 54 | sub | HtmlSubscriptDomElement | HtmlSubscriptTextBlock | HtmlSubscriptPanel | HtmlInteractiveFlexPanel |
| 55 | sup | HtmlSuperscriptDomElement | HtmlSuperscriptTextBlock | HtmlSuperscriptPanel | HtmlInteractiveFlexPanel |
| 56 | time | HtmlTimeDomElement | TextBlock | HtmlTimeInlinePanel | HtmlInteractiveFlexPanel |
| 57 | var | HtmlVariableDomElement | TextBlock | HtmlVariableInlinePanel | HtmlInteractiveFlexPanel |
| 58 | wbr | HtmlWordBreakDomElement | TextBlock | - | - |
| 59 | br | HtmlLineBreakDomElement | LineBreak | - | - |
| 60 | label | HtmlLabelDomElement | HtmlFormLabelPanel | HtmlFormLabelPanel | - |
| 61 | dt | HtmlDescriptionTermDomElement | TextBlock | HtmlInlineFlowPanel | - |

> **D-XAML-SEL 全部正确。** 注意 em/strong/b/i/u/s/small/code 等使用**专用的**自定义控件类型(如 HtmlEmphasisTextBlock)，非通用 TextBlock。

---

## 第五部分: 表单控件类 — 16 元素逐类审计

| # | 标签 | 具体类 | CreateXaml | BuildXamlAttributes 特殊处理 | 审核 |
|---|------|--------|-----------|---------------------------|:---:|
| 62 | input | HtmlInputDomElement | InputType→15+种控件 | Checked→IsChecked, Value→Text/Password, Min/Max/Step→Slider/Number, placeholder→PlaceholderText, readonly→IsReadOnly, disabled→IsEnabled, file→Accept/AllowsMultiple, 日期→Value/Min/Max/Step, hidden→Collapsed | ✅ |
| 63 | button | HtmlButtonDomElement | HtmlFormButton/FlexPanel | Disabled, CommandKind(Type), FormOwnerId, FormAction | ✅ |
| 64 | textarea | HtmlTextAreaDomElement | HtmlMultiLineTextInputControl | Rows/Cols/Wrap/MaxLength/Placeholder/ReadOnly/Required | ✅ |
| 65 | select | HtmlSelectDomElement | ComboBox/ListBox | Multiple→SelectionMode, Size, Required, option→Items | ✅ |
| 66 | option | HtmlOptionDomElement | ComboBoxItem/ListViewItem | Selected→IsSelected, Disabled→IsEnabled, Label/Value→Content | ✅ |
| 67 | optgroup | HtmlOptGroupDomElement | ComboBoxItem(Header) | Label→Header, Disabled | ✅ |
| 68 | datalist | HtmlDataListDomElement | ContentControl(非可视) | option→suggestionValues | ✅ |
| 69 | output | HtmlOutputDomElement | TextBlock/InlineFlowPanel | For/Form/Name | ✅ |
| 70 | meter | HtmlMeterDomElement | HtmlMeterControl | Value/Min/Max/Low/High/Optimum | ✅ |
| 71 | progress | HtmlProgressDomElement | ProgressBar | Value/Max | ✅ |
| 72 | keygen | (已废弃, 无注册) | - | - | N/A |

> **D-XAML-ATTR 满分。** 表单控件是属性映射最完整的类别。每个 InputType 有专用的 ~400 行 `BuildXamlAttributes` 处理。

---

## 第六部分: 媒体嵌入类 — 12 元素逐类审计

| # | 标签 | 具体类 | CreateXaml | BuildXamlAttributes | 问题 |
|---|------|--------|-----------|---------------------|:---:|
| 73 | img | HtmlImageDomElement | Image/ImageMapComposite | Alt→Name, Width/Height, Stretch(objectFit) | 🟡 Source标记RuntimeDataSource |
| 74 | iframe | HtmlIFrameDomElement | HtmlEmbeddedFrameHost | Src→RuntimeDataSource, Width/Height | 🟡 同img |
| 75 | video | HtmlVideoDomElement | MediaPlayerElement | Src→RuntimeDataSource, Poster, Width/Height, Controls/AutoPlay/Loop/Muted | 🟡 同img |
| 76 | audio | HtmlAudioDomElement | MediaPlayerElement | Src→RuntimeDataSource, Controls/AutoPlay/Loop/Muted | 🟡 同img |
| 77 | canvas | HtmlCanvasDomElement | HtmlCanvasHost | Width/Height | ✅ |
| 78 | embed | HtmlEmbedDomElement | HtmlEmbeddedObjectHost | Src→RuntimeDataSource, Type, Width/Height | ✅ |
| 79 | object | HtmlObjectDomElement | HtmlEmbeddedObjectHost | Data→RuntimeDataSource, Type, Width/Height | ✅ |
| 80 | source | HtmlSourceDomElement | ContentControl(非可视) | Src/SrcSet/Type/Media | ✅ |
| 81 | track | HtmlTrackDomElement | ContentControl(非可视) | Src/Kind/SrcLang/Label/Default | ✅ |
| 82 | picture | HtmlPictureDomElement | 非可视(传递子) | - | ✅ |
| 83 | map | HtmlImageMapDomElement | HtmlImageMapComposite | Name | ✅ |
| 84 | area | HtmlImageMapAreaDomElement | HtmlImageMapHotspot | Shape/Coords/Alt/Href | ✅ |

> **🟡 Source 标记非 InlineXaml**: img/video/audio/iframe 的 `src`/`Source` 标记为 `HtmlElementAttributeXamlHandling.RuntimeDataSource`。这意味着在 XAML 初始化时 Source 为空，需运行时通过 ui-assets.json 或 css-runtime-contract 加载。

---

## 第七部分: 表格类 — 8 元素逐类审计

| # | 标签 | 具体类 | CreateXaml | 属性 | 审核 |
|---|------|--------|-----------|------|:---:|
| 85 | table | HtmlTableDomElement | HtmlTablePanel | caption→Header | ✅ |
| 86 | caption | HtmlTableCaptionDomElement | TextBlock | - | ✅ |
| 87 | thead | HtmlTableHeadDomElement | HtmlTableSectionPanel | - | ✅ |
| 88 | tbody | HtmlTableBodyDomElement | HtmlTableSectionPanel | - | ✅ |
| 89 | tfoot | HtmlTableFootDomElement | HtmlTableSectionPanel | - | ✅ |
| 90 | tr | HtmlTableRowDomElement | HtmlTableRowPanel | - | ✅ |
| 91 | th | HtmlTableHeaderCellDomElement | HtmlTableHeaderCellPanel | ColSpan/RowSpan/Headers/Scope/Abbr | ✅ |
| 92 | td | HtmlTableCellDomElement | HtmlTableCellPanel | ColSpan/RowSpan/Headers | ✅ |

---

## 第八部分: 列表交互类 — 10 元素逐类审计

| # | 标签 | 具体类 | CreateXaml | 属性/特殊处理 | 审核 |
|---|------|--------|-----------|--------------|:---:|
| 93 | ul | HtmlUnorderedListDomElement | ListView | - | ✅ |
| 94 | ol | HtmlOrderedListDomElement | ListView | Reversed/Start/Type→列表编号 | ✅ |
| 95 | menu | HtmlMenuDomElement | ListView | - | ✅ |
| 96 | li | HtmlListItemDomElement | ListViewItem (StackPanel包装) | Value, 列表标记"•"/编号 | ✅ |
| 97 | dl | HtmlDescriptionListDomElement | StackPanel(BlockFlow) | - | ✅ |
| 98 | dd | HtmlDescriptionDetailsDomElement | StackPanel(BlockFlow) | - | ✅ |
| 99 | details | HtmlDetailsDomElement | Expander | Open→IsExpanded, summary→Header | ✅ |
| 100 | summary | HtmlSummaryDomElement | Button(独立)/不渲染(details内) | - | ✅ |
| 101 | a | HtmlAnchorDomElement | HyperlinkButton/FlexPanel/TextBlock/InlineFlowPanel | Href→NavigateUri(有href), Target/Download/Ping/Rel/HrefLang/Type→RuntimeDataSource | ✅ |
| 102 | hr | HtmlHorizontalRuleDomElement | Rectangle | - | ✅ |

> **a 元素有 4 种 CreateXaml 分支**: ①有href+flex→HtmlInteractiveFlexPanel ②有href→HyperlinkButton ③无href+无子→TextBlock ④无href+有子→HtmlInlineFlowPanel。全部正确。

---

## 第九部分: SVG类 — 14 元素逐类审计

### 9.1 SvgGeometryDomElementDefinition 基类属性映射

```csharp
✅ Fill            ← fill attribute       (NormalizeXamlColor)
✅ Stroke          ← stroke attribute
✅ StrokeThickness ← strokeWidth attribute
✅ StrokeDashArray ← strokeDashArray attribute
✅ Opacity         ← opacity attribute
```

每个几何子类额外添加专属属性: path→Data, circle→Width/Height(r×2), rect→Canvas.Left/Top/Width/Height/Radius, line→X1/Y1/X2/Y2, polygon/polyline→Points, ellipse→Width/Height(rx×2/ry×2)

### 9.2 逐类表

| # | 标签 | 具体类 | CreateXaml | 属性映射 | 审核 |
|---|------|--------|-----------|----------|:---:|
| 103 | svg | SvgRootDomElement | Canvas(PositionedLayout) | x/y/width/height/viewBox/preserveAspectRatio | ✅ |
| 104 | g | SvgGroupDomElement | Canvas(PositionedLayout) | - | ✅ |
| 105 | path | SvgPathDomElement | Path | d/pathLength+Fill/Stroke/StrokeWidth | ✅ |
| 106 | circle | SvgCircleDomElement | Ellipse | cx/cy/r→Width/Height(r×2)+Fill/Stroke | ✅ |
| 107 | rect | SvgRectangleDomElement | Rectangle | x/y/width/height/rx/ry+Fill/Stroke | ✅ |
| 108 | line | SvgLineDomElement | Line | x1/y1/x2/y2+Fill/Stroke | ✅ |
| 109 | polygon | SvgPolygonDomElement | Polygon | points/pathLength+Fill/Stroke | ✅ |
| 110 | polyline | SvgPolylineDomElement | Polyline | points/pathLength+Fill/Stroke | ✅ |
| 111 | ellipse | SvgEllipseDomElement | Ellipse | cx/cy/rx/ry→Width(rx×2)/Height(ry×2)+Fill/Stroke | ✅ |
| 112 | use | SvgUseDomElement | Path | href/x/y/width/height+Fill/Stroke | ✅ |
| 113 | defs | SvgDefinitionsDomElement | Canvas(非渲染) | - | ✅ |
| 114 | clipPath | SvgClipPathDomElement | Canvas(非渲染) | clipPathUnits | ✅ |
| 115 | mask | SvgMaskDomElement | Canvas(非渲染) | maskUnits/maskContentUnits/x/y/width/height | ✅ |
| 116 | text | SvgTextDomElement | TextBlock(PositionedLayout) | x/y/dx/dy/rotate/textLength/lengthAdjust | ✅ |

> **SVG D-XAML-ATTR 满分。** 所有 SVG 几何属性均正确映射。⚠️ SVG 元素无 `RuntimeProperties`(CSS属性不走 DomElementRuntimeProperty), 仅通过 `SvgDomAttributeProperty` 采集。

---

## 第十部分: 元数据/非可视类 — 8 元素逐类审计

| # | 标签 | 具体类 | XamlSupport | 属性 | 审核 |
|---|------|--------|:---:|------|:---:|
| 117 | head | HtmlHeadDomElement | NonVisual | 不生成XAML | ✅ |
| 118 | title | HtmlTitleDomElement | NonVisual | 不生成XAML | ✅ |
| 119 | base | HtmlBaseDomElement | NonVisual | Href/Target | ✅ |
| 120 | link | HtmlLinkDomElement | NonVisual | Rel/Href/Type/Media/... | ✅ |
| 121 | meta | HtmlMetaDomElement | NonVisual | Name/HttpEquiv/Content/Charset | ✅ |
| 122 | style | HtmlStyleDomElement | NonVisual | Media/Nonce/Title | ✅ |
| 123 | script | HtmlScriptDomElement | NonVisual | Src/Type/Async/Defer/... | ✅ |
| 124 | noscript | HtmlNoScriptDomElement | NonVisual | - | ✅ |
| 125 | template | HtmlTemplateDomElement | NonVisual | 保存惰性模板/声明式 Shadow DOM 属性 | ✅ |
| 126 | colgroup | HtmlColGroupDomElement | NonVisual | Span | ✅ |
| 127 | col | HtmlColDomElement | NonVisual | Span | ✅ |

### ✅ selectedcontent 已补齐

```csharp
// HtmlDomElementTypeCatalog.StandardHtmlTags 包含 "selectedcontent"
// HtmlSelectedContentDomElement 已注册，并强类型创建 TextBlock 镜像当前 option 文本
```

---

## 第十一部分: 全局属性映射缺失 — 根本原因

### 11.1 缺失分布图

```
DomElement.BuildXamlAttributes()           ← 尺寸/透明度/层级/可见性 ✅
  └─ HtmlDomElementDefinition.BuildXamlAttributes()  ← Id/Title/Language/Direction ✅
      └─ HtmlLayoutDomElementDefinition.BuildXamlAttributes() ← Background/Margin/Gap ✅
      │   ⛔ padding*     → ❌ 未映射 (Grid/StackPanel 支持)
      │   ⛔ borderWidth/borderColor  → ❌ 未映射
      │   ⛔ borderRadius → ❌ 未映射
      │   ⛔ boxShadow    → ❌ 未映射
      │   ⛔ overflowX/Y  → ❌ 未映射
      └─ HtmlTextVisualDomElementDefinition.BuildXamlAttributes() ← Foreground/FontSize/FontWeight/FontStyle/FontFamily/TextAlignment ✅
          ⛔ lineHeight    → ❌ 未映射
          ⛔ letterSpacing → ❌ 未映射
          ⛔ textDecoration→ ❌ 未映射
          ⛔ textTransform → ❌ 未映射
          ⛔ textOverflow  → ❌ 未映射
          ⛔ whiteSpace    → ❌ 未映射
          ⛔ wordBreak     → ❌ 未映射
```

### 11.2 缺少的 WinUI 属性写入方法

在 `HtmlLayoutDomElementDefinition` 中应添加 (类似现有的 `AddColor`/`AddThickness`):

```csharp
// 缺失方法:
AddThickness(attributes, "style.padding", "Padding");
AddBorderThickness(attributes);  // style.borderTopWidth/RightWidth/BottomWidth/LeftWidth
AddBorderBrush(attributes);      // style.borderTopColor
AddCornerRadius(attributes);     // style.borderTopLeftRadius/...
```

在 `HtmlTextVisualDomElementDefinition` 中应添加:

```csharp
AddLength(attributes, "style.lineHeight", "LineHeight");
AddLength(attributes, "style.letterSpacing", "CharacterSpacing", v => v * 100);
AddTextDecoration(attributes, "style.textDecorationLine", "TextDecorations");
AddWhiteSpace(attributes, "style.whiteSpace", "TextWrapping");
```

---

## 第十二部分: Audit 防造假验证

### 12.1 完整 Audit 链路

```csharp
Audit()
  ├─ ValidateOwnTraversalContract()
  │   └─ 验证所有 IsAuditRequired 属性的 DomSlots/XamlSlots 长度都为 3
  ├─ child.Audit() × N  ← 递归子元素
  └─ 对每个 IsAuditRequired 属性:
      └─ AuditSlottedProperty(owner, traits)
          → owner.AuditSlots(traits)
            ├─ Init:    SourceInit  ↔ XamlInit     → pass/fail
            ├─ Link:    SourceLink  ↔ XamlLink     → pass/fail  
            ├─ Runtime: SourceRuntime ↔ XamlRuntime → pass/fail
            └─ Layout:  (特定实现)
          → DomElementSlottedPropertyAuditResult
```

### 12.2 能检测的问题

| 场景 | Init | Runtime | 结果 |
|------|:---:|:---:|:---:|
| Padding=8px 但未映射 | ❌ FAILED | - | FAILED ✅ |
| Border=1px 但未映射 | ❌ FAILED | ❌ FAILED | FAILED ✅ |
| LineHeight=24px 但未映射 | ❌ FAILED | ❌ FAILED | FAILED ✅ |
| Foreground=#333 但映射后不一致 | ❌ FAILED | ❌ FAILED | FAILED ✅ |
| Width=100 但控件实际=120 | ✅ | ❌ FAILED | FAILED ✅ |
| Text 值存在但控件显示空 | ❌ FAILED | ❌ FAILED | FAILED ✅ |

### 12.3 🔴 不能检测 (防造假盲区)

| 盲区 | 路径 | 危险级别 |
|------|------|:---:|
| **DomFillAsync 返回 SourceUnsupported** | SourceInit=Unset → AuditSlots→Passed ✅ | 🔴🔴🔴 |
| **XamlFillAsync 控件属性不存在** | XamlInit=Unset → AuditSlots→Passed ✅ | 🔴🔴 |
| **自定义控件未暴露依赖属性** | XamlFill 读取不到 → Runtime=Unset | 🔴🔴 |
| **控件模板覆盖属性** | 属性值存在但视觉不可见 | 🔴 |
| **父元素 Clip 裁剪** | 属性正确但屏幕不可见 | 🔴 |

> **2026-08-01 复核修正**：上表前两项已经由正式端到端结果模型关闭，
> 不再依赖 App 窗口的外围统计器。`DomPropertyFillTrace` 会保存
> `SourceUnsupported`；新增 `XamlPropertyFillTrace` 逐槽保存 PropertyName、
> Slot、QueryStatus、Description 及反射路由元数据，`XamlElementFillResult`
> 现在保存完整 Slots 而不是只有查询计数。`EndToEndReconciliationAuditor`
> 会把任何 SourceUnsupported 或 TargetUnsupported 直接加入 Issues 并令
> Passed=false。新增正式测试验证 TargetUnsupported 即使双方槽位均 Unset
> 也不能静默通过；Runtime Web 当前 312/312 通过。

### 12.4 SourceUnsupported 绕过审计的完整路径

```
DomFillAsync:
  queryEngine.QueryAsync(context)
  → DomPropertyQueryResult(Status=SourceUnsupported)
  → owner.ApplyDomQueryResult(Init, SourceUnsupported)
    → ClearSourceInitialization() → SourceInit=Unset

Audit:
  AuditSlottedProperty:
    SourceInit=Unset AND XamlInit=Unset
    → ElementSlotFeatureAuditResult.NotRequired  // ⚠️ 静默通过
    
原因: 如果两边都是 Unset, 审计假设"该属性双方都不存在", 不标记为失败。
但实际情况是: DOM 有值, 但 DOM 查询引擎无法获取。
```

---

## 第十三部分: 汇总矩阵

| 元素组 | 数量 | D-ATTR | D-FILL | D-XAML-SEL | D-XAML-ATTR | 缺失项 |
|--------|:---:|:---:|:---:|:---:|:---:|------|
| 布局容器 | 27 | ✅ | ✅ | ✅ | 🔴 | Padding/Border/BorderRadius/CornerRadius (4组×4=16属性缺失) |
| 文本视觉 | 34 | ✅ | ✅ | ✅ | 🔴 | LineHeight/LetterSpacing/TextDecoration/WhiteSpace (4属性缺失) |
| 表单控件 | 16 | ✅ | ✅ | ✅ | ✅ | 表单控件属性映射最完整, 共享基类Padding/Border缺失 |
| 媒体嵌入 | 12 | ✅ | ✅ | ✅ | 🟡 | Source非InlineXaml (无需修复, 设计如此) |
| 表格 | 8 | ✅ | ✅ | ✅ | 🔴 | 共享基类Padding/Border缺失 |
| 列表交互 | 10 | ✅ | ✅ | ✅ | ✅ | 共享基类Padding/Border缺失 |
| SVG | 14 | ✅ | ✅ | ✅ | ✅ | D-XAML-ATTR 满分 |
| 元数据 | 8 | ✅ | ✅ | ✅ | N/A | selectedcontent 未注册 |
| **总计** | **129** | | | | | |

---

## 第十四部分：2026-08-01 实现后复核（取代旧缺口表）

第十一、十三部分记录的是修复前的源码状态，不能继续作为当前结论。当前正式实现已完成以下闭环：

- 127 个目录类型均由具体 HTML/SVG 强类型入口直接创建 WinUI 对象；不存在按类型名称反射创建或 XAML 字符串解析创建。
- Padding、四边 Border、CornerRadius、overflow、line-height、letter-spacing、white-space、word-break、text-transform、text-shadow、box-shadow、clip-path、contain、filter、transition、flex 与 grid 已进入强类型应用和真实 XamlFill 读取链路。
- `WinUiXamlPropertyQueryEngine` 不允许从 DOM 槽位或 `CssRuntimeContractController` 回填 XAML 结果。目标控件无对应运行值时必须返回 TargetUnsupported，并由端到端审核判失败。
- 事件证据只在真实 WinUI 事件订阅成功后注册；未知事件不再以“已注册但无处理器”通过审核。
- CSS Grid 支持负网格线、重复命名线及 `name N`、`N name`、负 occurrence、命名 `grid-area` 隐式 start/end 线。
- 数字 `repeat()` 改用括号平衡解析，支持嵌套 `minmax()` 和命名线；方括号命名线不会再被拆成伪轨道。
- `repeat(auto-fill/auto-fit, ...)` 现依据对应轴的 DOM 运行宽高、row/column gap、重复片段最小轨道尺寸及普通流子项数计算轨道数；运行几何只进入布局器，不写回初始化槽。
- 布局器读取属性时不再只扫描 `RuntimeProperties` 集合；`DomElement.TryGetDomStringSlotValue()` 与 DomFill 共用反射属性全集，强类型属性族不会再出现“审核看得到、布局器看不到”。
- 非负像素 `text-indent` 与像素/normal `word-spacing` 已物化为实际 TextBlock Inline/Run 排版；XamlFill 会检查实际 Run、字符间距和附加依赖属性，而不是返回固定值。

本轮进一步完成的真实运行链路：

- `vertical-rl` / `vertical-lr` 由强类型 `HtmlVerticalTextControl` 逐 Unicode 文本元素排版；CJK 字形保持直立，mixed ASCII 字形单独旋转，列方向、换列、缩进、字距、行高、装饰和逐字形 Composition 阴影均可从实际目标回读。工厂在创建阶段依据已填充的 writing-mode 选择该类型，不再在对象树建立后替换主对象。
- 非 opacity 前景 filter 由 `HtmlFilteredElementHost` 隔离原始 WinUI 视觉，使用 `CompositionVisualSurface` 作为 Win2D 效果源，支持 blur、brightness、contrast、grayscale、hue-rotate、invert、opacity、saturate、sepia 组合链。原始重复绘制层被隐藏，XamlFill 验证实际 VisualSurface、EffectBrush、属性集、尺寸和源视觉关系。
- 文本阴影支持垂直文字的多字形目标，每个真实 TextBlock 字形分别拥有 alpha-mask、DropShadow、SpriteVisual 和尺寸回读，不以单一占位文本冒充整列文字。
- 运行时样式刷新已经删除 `target.GetType().Name` 反解析 XAML 类型枚举的字符串路径；属性更新直接操作现有强类型 DependencyObject。

HTTP 自定义 cursor 的代码链也已补齐：`compile-cursors` 读取原始响应体清单，校验 Windows CUR 文件头，通过 Windows SDK `rc.exe` 与 MSVC `link.exe /DLL /NOENTRY` 生成资源模块，使用 `FindResource(RT_GROUP_CURSOR)` 验证每个资源 ID，并输出带模块 SHA-256 的登记清单。应用启动时只在清单版本、目录边界、唯一性和 SHA-256 全部通过后登记 URL，并由 `InputDesktopResourceCursor.CreateFromModule` 创建真实光标。集成测试已经实际生成并验证资源 DLL。当前仓库没有豆包页面对应的原始 CUR 响应体，因此没有提交站点专属二进制模块；未注册 URL 继续硬失败，不允许网络临时加载、系统 cursor 替代或回填 `auto`。

最终门禁（2026-08-01）：

| 门禁 | 结果 |
|---|---:|
| Runtime Web Debug | 329/329 通过 |
| Runtime Web Release | 329/329 通过 |
| DoubaoUIClone Tests Debug | 125/125 通过 |
| DoubaoUIClone Tests Release | 125/125 通过 |
| DoubaoUIClone App Debug (`win-x64`) | 0 警告、0 错误 |
| DoubaoUIClone 全解决方案 Release | 0 警告、0 错误 |
| WinUI 强类型工厂 Debug 自检 | PASS（127 类型，真实显示与审核） |
| WinUI 强类型工厂 Release 自检 | PASS（127 类型，真实显示与审核） |

上述 PASS 只覆盖已经物化的能力；任何未物化属性继续以硬失败进入报告，禁止使用源 DOM/CSS 值、固定默认值或共享残缺树制造一致。

### 14.1 状态同步方向与脱离文档 scope（2026-08-01）

- `XamlPropertyExecutionDescriptor` 现在正式保存
  `SynchronizationDirection`、`TargetCanMutateLocally` 和派生的
  `SupportsDomWriteBack`。details/dialog/input/option 的交互状态明确为
  DOM→XAML 单向权威；WinUI 控件可以本地交互变化，但当前克隆流程不宣称、
  也不执行 DOM 回写。
- Microsoft WebView2 实例会导航到含同源 `srcdoc` iframe 的受控页面，再由
  CDP `DOM.getDocument` 读取真实 nodeId/backendNodeId；该结果继续经过
  document scope、元素树、DOM Fill、强类型 XAML 创建和 host 挂载的完整测试。
  找不到唯一 embedding owner 时立即硬失败，不再静默跳过嵌套文档。
- 脱离主文档的关系已从只允许 iframe 的伪通用关系改为
  `EmbeddingOwner + DomEmbeddingOwnerKind`；`Iframe` 与 `ShadowHost` 分别验证，
  并共同参与层级限制、XAML 挂载和审核。这样 iframe 修复不会遗漏或破坏
  作者 Shadow DOM。

### 14.2 公共布局计划与运行坐标复核（2026-08-02）

- CSS Grid 的显式/自动放置、dense、负线号、命名线、命名区域、span、隐式轨道与 minmax 已归入 `HtmlLayoutDomElementDefinition` 的强类型计划；App 只物化计划，不再推断网页布局。
- Flex 的 grow/shrink/basis、gap、order、reverse、wrap 与 align-content 已归入同一公共层，并使用显式的水平/垂直合成行对象承载换行结果。
- `BuildResolvedXamlAttributes()` 新增可重写的运行属性发射门禁。布局子类删除由父布局拥有的 Width/Height 后，通用执行目录不得再次把 computed value 写成固定尺寸。
- `HtmlRuntimeLayoutManager` 现在同时保留 Design、CSS Runtime 与 DOM Runtime 三套视口，并公开 `ScaleCssPixelToRuntime()`。CSS 像素到当前运行坐标的转换由公共根布局器定义；WinUI 适配器只能消费该转换，禁止自行读取 DPI 或建立第二套缩放规则。
- 普通 block flow 的轨道只由 Runtime Web 生成一次。WinUI 挂载器不得追加重复 RowDefinition 或重新编号 Grid.Row，否则会把整棵树按屏幕高度逐层下移。
- `XamlGridTrackDefinition` 的数值常量现在明确标记为 `CssPixel`，不再伪装成 DIP；Flex/Grid 轨道与 Width/Height、Margin/Padding 使用同一个根布局坐标映射。
- WinUI 适配器对 absolute/fixed、matrix 平移与像素 transform-origin 统一消费公共根的 CSS→Runtime 映射。fixed 的 containing block 是 RuntimeViewport，不能错误使用其零宽 DOM 父 Grid。
- Runtime Web Debug/Release 当前均为 345/345 通过；新增测试覆盖父布局尺寸不得被重新发射，以及 1920×1010 CSS runtime 到 3840×2020 DOM runtime 的双轴转换。实机端到端结果仍必须单独记录，单元测试通过不代表视觉审核通过。

2026-08-02 实机分层复核的已确认结果：第 1–8 层的根、body、主 block、主 flex、左右栏、absolute 分隔容器及 fixed 侧栏均已达到 DOM 运行几何；fixed 侧栏由错误 `x=-4456` 修正为期望 `x=-616`。本结论只覆盖列出的层和元素，后续层级仍以真实 WinUI 审核失败数为准，不宣称整页 1:1 完成。

# 容器布局合同

## 目的

公共布局合同表达“值从哪里来、相对谁计算、在哪个轴生效”，避免把
`100%`、`left`、`center` 等字符串直接散落在转换器中。

## 核心类型

- `ContainerLayoutKind`：容器布局种类。
- `ContainerReferenceBox`：参考盒。
- `ContainerLayoutAxis`：水平或垂直轴。
- `ContainerPhysicalSide`：物理边。
- `ContainerLayoutConstraint`：最小、首选、最大约束。
- `ContainerLayoutValue`：常数、百分比或自动值。
- `ContainerLayoutBinding`：来源、目标和计算合同。

百分比必须声明基准和参考容器。靠左、靠右、居中、拉伸等使用强类型枚举，不以自由字符串参与计算。
原始 CSS 文本可作为诊断证据保存，但不能作为唯一正式合同。

## 初始化与运行

初始化阶段使用采集时容器尺寸计算确定值；运行阶段保留布局链接，窗口变化时由目标容器重新计算。
因此 `height:100%` 不得用错误的零高父节点硬编码为 `Height=0`。

消费项目可实现自己的布局执行器，但必须使用相同 `ContainerLayoutBinding` 作为入口。
静态审核比较初始化槽位，动态审核比较典型分辨率下的最终运行槽位。

## XAML 对象计划边界

- Flex/Grid/block 轨道由具体元素或其真实窄语义基类生成，消费 App 只物化计划。
- `XamlGridTrackDefinition` 在构造时必须同时形成 `LayoutLength` 强类型值；无法表达的
  字符串必须立即失败，不得留给 App 根据现场容错。
- 正常流轨道必须排除 `absolute`/`fixed` 子项；`sticky` 仍属于正常流。
- Flex 主轴轨道使用子项外盒尺寸；`content-box` 的显式尺寸必须加上对应轴的
  padding 和 border，HTML 与 SVG 执行同一盒模型合同。
- 可滚动 block 容器不得把已计算的子项高度重新解释为压缩用 `Star`；溢出内容
  必须保留 computed runtime 轨道尺寸。
- 站点项目不得在对象树创建后再遍历 DOM/CSS 覆盖上述计划；这种“第二布局器”
  属于合同越界。

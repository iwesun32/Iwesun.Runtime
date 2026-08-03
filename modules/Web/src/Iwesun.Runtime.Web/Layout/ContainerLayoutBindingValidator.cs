namespace Iwesun.Runtime.Web;

internal static class ContainerLayoutBindingValidator
{
	public static void Validate(
		ContainerLayoutMechanism mechanism,
		LayoutContainerReference container,
		ContainerLayoutConstraint constraint,
		LayoutPhysicalAxis? flexMainAxis)
	{
		if (mechanism == ContainerLayoutMechanism.Flex && flexMainAxis is null)
			throw new ArgumentException(
				"Flex 布局必须明确主轴的物理方向。",
				nameof(flexMainAxis));
		if (mechanism != ContainerLayoutMechanism.Flex && flexMainAxis is not null)
			throw new ArgumentException(
				"只有 Flex 布局可以声明 FlexMainAxis。",
				nameof(flexMainAxis));
		ValidateContainer(mechanism, container);
		ValidateConstraint(mechanism, container, constraint);
		ContainerLayoutValueValidator.Validate(
			mechanism,
			container,
			constraint,
			flexMainAxis);
	}

	private static void ValidateContainer(
		ContainerLayoutMechanism mechanism,
		LayoutContainerReference container)
	{
		var compatible = mechanism switch
		{
			ContainerLayoutMechanism.Flex =>
				container.Kind is
					LayoutContainerReferenceKind.FlexContainer
					or LayoutContainerReferenceKind.NamedElement,
			ContainerLayoutMechanism.Grid =>
				container.Kind is
					LayoutContainerReferenceKind.GridContainer
					or LayoutContainerReferenceKind.NamedElement,
			ContainerLayoutMechanism.FixedPositioning =>
				container.Kind == LayoutContainerReferenceKind.Viewport,
			ContainerLayoutMechanism.XamlPanel =>
				container.Kind is
					LayoutContainerReferenceKind.XamlParent
					or LayoutContainerReferenceKind.NamedElement,
			_ => true
		};
		if (!compatible)
			throw new ArgumentException(
				$"{mechanism} 与 {container.Kind} 容器引用不兼容。",
				nameof(container));
	}

	private static void ValidateConstraint(
		ContainerLayoutMechanism mechanism,
		LayoutContainerReference container,
		ContainerLayoutConstraint constraint)
	{
		if (constraint is ContainerAnchorConstraint
			&& mechanism is not (
				ContainerLayoutMechanism.AbsolutePositioning
				or ContainerLayoutMechanism.FixedPositioning
				or ContainerLayoutMechanism.StickyPositioning
				or ContainerLayoutMechanism.Canvas
				or ContainerLayoutMechanism.Overlay))
			throw new ArgumentException(
				$"{mechanism} 不能承载锚定约束。",
				nameof(mechanism));
		if (mechanism == ContainerLayoutMechanism.FixedPositioning
			&& container.Kind != LayoutContainerReferenceKind.Viewport)
			throw new ArgumentException(
				"FixedPositioning 必须明确引用 Viewport。",
				nameof(container));
		if (constraint is ContainerGridTrackConstraint
			&& mechanism != ContainerLayoutMechanism.Grid)
			throw new ArgumentException(
				"网格轨道约束只能使用 Grid 机制。",
				nameof(mechanism));
		if (constraint is ContainerGridPlacementConstraint
			&& mechanism != ContainerLayoutMechanism.Grid)
			throw new ArgumentException(
				"网格位置约束只能使用 Grid 机制。",
				nameof(mechanism));
		if (constraint is ContainerFlowPositionConstraint
			&& mechanism is not (
				ContainerLayoutMechanism.NormalFlow
					or ContainerLayoutMechanism.Flex
					or ContainerLayoutMechanism.Stack
					or ContainerLayoutMechanism.XamlPanel))
			throw new ArgumentException(
				$"{mechanism} 不能承载普通流位置约束。",
				nameof(mechanism));
		if (constraint is not ContainerDistributionConstraint distribution)
			return;
		var expected = distribution.TargetSlot switch
		{
			ElementSpaceSlotKind.FlexLayout => ContainerLayoutMechanism.Flex,
			ElementSpaceSlotKind.GridLayout => ContainerLayoutMechanism.Grid,
			_ => throw new InvalidOperationException("未知的空间分布槽位。")
		};
		if (mechanism != expected)
			throw new ArgumentException(
				$"{distribution.TargetSlot} 必须使用 {expected} 机制。",
				nameof(mechanism));
	}
}

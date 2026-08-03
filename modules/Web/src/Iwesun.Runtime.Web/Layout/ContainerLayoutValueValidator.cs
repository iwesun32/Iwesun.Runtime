namespace Iwesun.Runtime.Web;

internal static class ContainerLayoutValueValidator
{
	public static void Validate(
		ContainerLayoutMechanism mechanism,
		LayoutContainerReference container,
		ContainerLayoutConstraint constraint,
		LayoutPhysicalAxis? flexMainAxis)
	{
		var length = constraint switch
		{
			ContainerSizeConstraint size => size.Length,
			ContainerAnchorConstraint anchor => anchor.Offset,
			ContainerGridTrackConstraint track => track.TrackSize,
			_ => null
		};
		if (length is not null)
			ValidateLength(
				length,
				constraint.Axis,
				mechanism,
				container,
				flexMainAxis);
	}

	private static void ValidateLength(
		LayoutLength length,
		LayoutPhysicalAxis targetAxis,
		ContainerLayoutMechanism mechanism,
		LayoutContainerReference container,
		LayoutPhysicalAxis? flexMainAxis)
	{
		switch (length)
		{
			case LayoutLength.Percentage percentage:
				ValidatePercentageBasis(
					percentage.Basis,
					targetAxis,
					mechanism,
					container,
					flexMainAxis);
				break;
			case LayoutLength.Calculation calculation:
				ValidateExpression(
					calculation.Expression,
					targetAxis,
					mechanism,
					container,
					flexMainAxis);
				break;
		}
	}

	private static void ValidateExpression(
		LayoutCalculationExpression expression,
		LayoutPhysicalAxis targetAxis,
		ContainerLayoutMechanism mechanism,
		LayoutContainerReference container,
		LayoutPhysicalAxis? flexMainAxis)
	{
		switch (expression)
		{
			case LayoutCalculationExpression.PercentageTerm percentage:
				ValidatePercentageBasis(
					percentage.Basis,
					targetAxis,
					mechanism,
					container,
					flexMainAxis);
				break;
			case LayoutCalculationExpression.Binary binary:
				ValidateExpression(binary.Left, targetAxis, mechanism, container, flexMainAxis);
				ValidateExpression(binary.Right, targetAxis, mechanism, container, flexMainAxis);
				break;
		}
	}

	private static void ValidatePercentageBasis(
		LayoutPercentageBasis basis,
		LayoutPhysicalAxis targetAxis,
		ContainerLayoutMechanism mechanism,
		LayoutContainerReference container,
		LayoutPhysicalAxis? flexMainAxis)
	{
		RequireBasisContainer(basis, container);
		var basisAxis = basis switch
		{
			LayoutPercentageBasis.FlexLineMainSize =>
				RequireFlexAxis(mechanism, flexMainAxis),
			LayoutPercentageBasis.FlexLineCrossSize =>
				Opposite(RequireFlexAxis(mechanism, flexMainAxis)),
			_ => ContainerLayoutRules.GetPercentageAxis(basis)
		};
		if (basisAxis != targetAxis)
			throw new ArgumentException(
				$"{targetAxis} 槽位不能使用 {basis} 作为百分比基准。",
				nameof(basis));
	}

	private static void RequireBasisContainer(
		LayoutPercentageBasis basis,
		LayoutContainerReference container)
	{
		var compatible = basis switch
		{
			LayoutPercentageBasis.ContainingBlockWidth
				or LayoutPercentageBasis.ContainingBlockHeight =>
				container.Kind is
					LayoutContainerReferenceKind.ContainingBlock
					or LayoutContainerReferenceKind.NamedElement,
			LayoutPercentageBasis.ParentContentWidth
				or LayoutPercentageBasis.ParentContentHeight =>
				container.Kind is
					LayoutContainerReferenceKind.Parent
					or LayoutContainerReferenceKind.NamedElement,
			LayoutPercentageBasis.FlexLineMainSize
				or LayoutPercentageBasis.FlexLineCrossSize =>
				container.Kind is
					LayoutContainerReferenceKind.FlexContainer
					or LayoutContainerReferenceKind.NamedElement,
			LayoutPercentageBasis.GridTrackWidth
				or LayoutPercentageBasis.GridTrackHeight =>
				container.Kind is
					LayoutContainerReferenceKind.GridContainer
					or LayoutContainerReferenceKind.NamedElement,
			LayoutPercentageBasis.ViewportWidth
				or LayoutPercentageBasis.ViewportHeight =>
				container.Kind == LayoutContainerReferenceKind.Viewport,
			LayoutPercentageBasis.XamlParentWidth
				or LayoutPercentageBasis.XamlParentHeight =>
				container.Kind is
					LayoutContainerReferenceKind.XamlParent
					or LayoutContainerReferenceKind.NamedElement,
			_ => false
		};
		if (!compatible)
			throw new ArgumentException(
				$"{basis} 与 {container.Kind} 容器引用不一致。",
				nameof(basis));
	}

	private static LayoutPhysicalAxis RequireFlexAxis(
		ContainerLayoutMechanism mechanism,
		LayoutPhysicalAxis? flexMainAxis)
	{
		if (mechanism != ContainerLayoutMechanism.Flex || flexMainAxis is null)
			throw new ArgumentException("Flex 百分比基准只能用于已声明主轴的 Flex 布局。");
		return flexMainAxis.Value;
	}

	private static LayoutPhysicalAxis Opposite(LayoutPhysicalAxis axis) =>
		axis == LayoutPhysicalAxis.Horizontal
			? LayoutPhysicalAxis.Vertical
			: LayoutPhysicalAxis.Horizontal;
}

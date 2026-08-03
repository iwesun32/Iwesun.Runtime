using Iwesun.Runtime.Web;
using Xunit;

namespace Iwesun.Runtime.Web.Tests;

public sealed class ContainerLayoutBindingTests
{
	[Fact]
	public void WidthPercentage_RequiresExplicitHorizontalBasis()
	{
		var binding = new ContainerLayoutBinding(
			ContainerLayoutMechanism.NormalFlow,
			ContainingBlock(),
			new ContainerSizeConstraint(
				ElementSpaceSlotKind.Width,
				new LayoutLength.Percentage(
					100,
					LayoutPercentageBasis.ContainingBlockWidth)),
			"width: 100%");

		var size = Assert.IsType<ContainerSizeConstraint>(binding.Constraint);
		var percentage = Assert.IsType<LayoutLength.Percentage>(size.Length);
		Assert.Equal(LayoutPhysicalAxis.Horizontal, size.Axis);
		Assert.Equal(LayoutPercentageBasis.ContainingBlockWidth, percentage.Basis);
	}

	[Fact]
	public void WidthPercentage_WithHeightBasisIsRejected()
	{
		Assert.Throws<ArgumentException>(() => new ContainerLayoutBinding(
			ContainerLayoutMechanism.NormalFlow,
			ContainingBlock(),
			new ContainerSizeConstraint(
				ElementSpaceSlotKind.Width,
				new LayoutLength.Percentage(
					100,
					LayoutPercentageBasis.ContainingBlockHeight)),
			"invalid width: 100% of height"));
	}

	[Fact]
	public void PercentageBasis_MustMatchDeclaredContainer()
	{
		Assert.Throws<ArgumentException>(() => new ContainerLayoutBinding(
			ContainerLayoutMechanism.NormalFlow,
			new LayoutContainerReference(
				LayoutContainerReferenceKind.Parent,
				LayoutReferenceBox.ContentBox),
			new ContainerSizeConstraint(
				ElementSpaceSlotKind.Width,
				new LayoutLength.Percentage(
					100,
					LayoutPercentageBasis.ContainingBlockWidth)),
			"basis says containing block but reference says parent"));
	}

	[Fact]
	public void RightAnchor_IsDifferentFromEndAlignment()
	{
		var anchor = new ContainerLayoutBinding(
			ContainerLayoutMechanism.AbsolutePositioning,
			ContainingBlock(),
			new ContainerAnchorConstraint(
				ElementSpaceSlotKind.X,
				LayoutAnchor.Right,
				new LayoutLength.Constant(0, LayoutLengthUnit.CssPixel)),
			"position:absolute; right:0");
		var alignment = new ContainerLayoutBinding(
			ContainerLayoutMechanism.NormalFlow,
			new LayoutContainerReference(
				LayoutContainerReferenceKind.Parent,
				LayoutReferenceBox.ContentBox),
			new ContainerAlignmentConstraint(
				ElementSpaceSlotKind.X,
				LayoutPhysicalAxis.Horizontal,
				LayoutAlignment.Far),
			"horizontal alignment: end");

		Assert.IsType<ContainerAnchorConstraint>(anchor.Constraint);
		Assert.IsType<ContainerAlignmentConstraint>(alignment.Constraint);
	}

	[Fact]
	public void AnchorConstraint_CannotUseNormalFlow()
	{
		Assert.Throws<ArgumentException>(() => new ContainerLayoutBinding(
			ContainerLayoutMechanism.NormalFlow,
			ContainingBlock(),
			new ContainerAnchorConstraint(
				ElementSpaceSlotKind.X,
				LayoutAnchor.Right,
				new LayoutLength.Constant(0, LayoutLengthUnit.CssPixel)),
			"right:0 without positioned layout"));
	}

	[Fact]
	public void FlexBinding_RequiresPhysicalMainAxis()
	{
		var container = new LayoutContainerReference(
			LayoutContainerReferenceKind.FlexContainer,
			LayoutReferenceBox.ContentBox);
		var constraint = new ContainerDistributionConstraint(
			ElementSpaceSlotKind.FlexLayout,
			LayoutPhysicalAxis.Horizontal,
			LayoutDistribution.SpaceBetween);

		Assert.Throws<ArgumentException>(() => new ContainerLayoutBinding(
			ContainerLayoutMechanism.Flex,
			container,
			constraint,
			"justify-content:space-between"));
	}

	[Fact]
	public void AutomaticLayoutSource_RequiresTypedContainerLink()
	{
		var traits = ElementPropertyTraitsReflector.GetAttribute<
			NumericTestElement>(nameof(NumericTestElement.X));

		Assert.Throws<ArgumentException>(() => new ElementNumericProperty(
			"X",
			ElementSpaceSlotKind.X,
			traits,
			ElementPropertySlot<double>.Unset,
			ElementPropertyLink.None,
			ElementPropertySlot<double>.FromValue(10, PropertyUnit.CssPixel),
			ElementPropertyValueSource.ContainerAutomaticLayout,
			ElementPropertySlot<double>.Unset,
			ElementPropertyLink.None,
			ElementPropertySlot<double>.FromValue(10, PropertyUnit.Dip),
			ElementPropertyValueSource.DirectConstant,
			NumericPropertyAuditDefinition.RuntimeCssPixelToDip(.5)));
	}

	[Fact]
	public void FlowPosition_PreservesSiblingSequenceAndAlignment()
	{
		var binding = new ContainerLayoutBinding(
			ContainerLayoutMechanism.Flex,
			new(
				LayoutContainerReferenceKind.NamedElement,
				LayoutReferenceBox.ContentBox,
				"/html/body/main"),
			new ContainerFlowPositionConstraint(
				ElementSpaceSlotKind.X,
				LayoutPhysicalAxis.Horizontal,
				3,
				"/html/body/main/div[3]",
				LayoutAlignment.Far),
			"flex row child sequence",
			LayoutPhysicalAxis.Horizontal);

		var constraint = Assert.IsType<ContainerFlowPositionConstraint>(binding.Constraint);
		Assert.Equal(3, constraint.SequenceIndex);
		Assert.Equal("/html/body/main/div[3]", constraint.PreviousSiblingXPath);
		Assert.Equal(LayoutAlignment.Far, constraint.Alignment);
	}

	[Fact]
	public void GridPlacement_RequiresGridMechanism()
	{
		var constraint = new ContainerGridPlacementConstraint(
			ElementSpaceSlotKind.X,
			LayoutPhysicalAxis.Horizontal,
			2,
			3,
			LayoutAlignment.Stretch);

		Assert.Throws<ArgumentException>(() => new ContainerLayoutBinding(
			ContainerLayoutMechanism.NormalFlow,
			new(
				LayoutContainerReferenceKind.Parent,
				LayoutReferenceBox.ContentBox),
			constraint,
			"invalid grid placement"));
	}

	private static LayoutContainerReference ContainingBlock() =>
		new(
			LayoutContainerReferenceKind.ContainingBlock,
			LayoutReferenceBox.PaddingBox);

	private sealed class NumericTestElement
	{
		[ElementProperty(
			PropertyValueKind.Coordinate,
			PropertyValueStage.DomRuntime)]
		public double X { get; }
	}
}

namespace Iwesun.Runtime.Web;

public abstract record ContainerLayoutConstraint
{
	public ElementSpaceSlotKind TargetSlot { get; }
	public LayoutPhysicalAxis Axis { get; }

	private protected ContainerLayoutConstraint(
		ElementSpaceSlotKind targetSlot,
		LayoutPhysicalAxis axis)
	{
		TargetSlot = targetSlot;
		Axis = axis;
	}
}

public sealed record ContainerSizeConstraint : ContainerLayoutConstraint
{
	public LayoutLength Length { get; }

	public ContainerSizeConstraint(
		ElementSpaceSlotKind targetSlot,
		LayoutLength length) :
		base(targetSlot, ContainerLayoutRules.GetSizeAxis(targetSlot))
	{
		ArgumentNullException.ThrowIfNull(length);
		if (length is LayoutLength.Fraction)
			throw new ArgumentException(
				"fr 只能用于 Grid 轨道，不能直接作为元素尺寸。",
				nameof(length));
		Length = length;
	}
}

public sealed record ContainerAnchorConstraint : ContainerLayoutConstraint
{
	public LayoutAnchor Anchor { get; }
	public LayoutLength Offset { get; }

	public ContainerAnchorConstraint(
		ElementSpaceSlotKind targetSlot,
		LayoutAnchor anchor,
		LayoutLength offset) :
		base(targetSlot, ContainerLayoutRules.GetPositionAxis(targetSlot))
	{
		ArgumentNullException.ThrowIfNull(offset);
		if (offset is not (
			LayoutLength.Constant
			or LayoutLength.Percentage
			or LayoutLength.Calculation))
			throw new ArgumentException(
				"锚点偏移只能是常数、百分比或计算表达式。",
				nameof(offset));
		ContainerLayoutRules.RequireAnchorAxis(anchor, Axis);
		Anchor = anchor;
		Offset = offset;
	}
}

public sealed record ContainerAlignmentConstraint : ContainerLayoutConstraint
{
	public LayoutAlignment Alignment { get; }

	public ContainerAlignmentConstraint(
		ElementSpaceSlotKind targetSlot,
		LayoutPhysicalAxis axis,
		LayoutAlignment alignment) :
		base(targetSlot, axis)
	{
		ContainerLayoutRules.RequirePositionTarget(targetSlot, axis);
		Alignment = alignment;
	}
}

public sealed record ContainerDistributionConstraint : ContainerLayoutConstraint
{
	public LayoutDistribution Distribution { get; }

	public ContainerDistributionConstraint(
		ElementSpaceSlotKind targetSlot,
		LayoutPhysicalAxis axis,
		LayoutDistribution distribution) :
		base(targetSlot, axis)
	{
		if (targetSlot is not (
			ElementSpaceSlotKind.FlexLayout
			or ElementSpaceSlotKind.GridLayout))
			throw new ArgumentException(
				"空间分布只能属于 FlexLayout 或 GridLayout 槽位。",
				nameof(targetSlot));
		Distribution = distribution;
	}
}

public sealed record ContainerFlowPositionConstraint : ContainerLayoutConstraint
{
	public int SequenceIndex { get; }
	public string PreviousSiblingXPath { get; }
	public LayoutAlignment Alignment { get; }

	public ContainerFlowPositionConstraint(
		ElementSpaceSlotKind targetSlot,
		LayoutPhysicalAxis axis,
		int sequenceIndex,
		string? previousSiblingXPath,
		LayoutAlignment alignment) :
		base(targetSlot, axis)
	{
		ContainerLayoutRules.RequirePositionTarget(targetSlot, axis);
		if (sequenceIndex < 0)
			throw new ArgumentOutOfRangeException(nameof(sequenceIndex));
		SequenceIndex = sequenceIndex;
		PreviousSiblingXPath = previousSiblingXPath ?? string.Empty;
		Alignment = alignment;
	}
}

public sealed record ContainerGridPlacementConstraint : ContainerLayoutConstraint
{
	public int StartLine { get; }
	public int Span { get; }
	public LayoutAlignment Alignment { get; }

	public ContainerGridPlacementConstraint(
		ElementSpaceSlotKind targetSlot,
		LayoutPhysicalAxis axis,
		int startLine,
		int span,
		LayoutAlignment alignment) :
		base(targetSlot, axis)
	{
		ContainerLayoutRules.RequirePositionTarget(targetSlot, axis);
		if (startLine < 1)
			throw new ArgumentOutOfRangeException(nameof(startLine));
		if (span < 1)
			throw new ArgumentOutOfRangeException(nameof(span));
		StartLine = startLine;
		Span = span;
		Alignment = alignment;
	}
}

public sealed record ContainerGridTrackConstraint : ContainerLayoutConstraint
{
	public int TrackIndex { get; }
	public LayoutLength TrackSize { get; }

	public ContainerGridTrackConstraint(
		LayoutPhysicalAxis axis,
		int trackIndex,
		LayoutLength trackSize) :
		base(ElementSpaceSlotKind.GridLayout, axis)
	{
		if (trackIndex < 0)
			throw new ArgumentOutOfRangeException(
				nameof(trackIndex),
				"网格轨道索引不能为负数。");
		ArgumentNullException.ThrowIfNull(trackSize);
		if (trackSize is LayoutLength.Stretch)
			throw new ArgumentException(
				"Grid 轨道拉伸应由分布规则表达，不能使用 Stretch 长度。",
				nameof(trackSize));
		TrackIndex = trackIndex;
		TrackSize = trackSize;
	}
}

internal static class ContainerLayoutRules
{
	public static LayoutPhysicalAxis GetSizeAxis(ElementSpaceSlotKind targetSlot) =>
		targetSlot switch
		{
			ElementSpaceSlotKind.Width
				or ElementSpaceSlotKind.MinWidth
				or ElementSpaceSlotKind.MaxWidth => LayoutPhysicalAxis.Horizontal,
			ElementSpaceSlotKind.Height
				or ElementSpaceSlotKind.MinHeight
				or ElementSpaceSlotKind.MaxHeight => LayoutPhysicalAxis.Vertical,
			_ => throw new ArgumentException(
				$"{targetSlot} 不是尺寸槽位。",
				nameof(targetSlot))
		};

	public static LayoutPhysicalAxis GetPositionAxis(ElementSpaceSlotKind targetSlot) =>
		targetSlot switch
		{
			ElementSpaceSlotKind.X => LayoutPhysicalAxis.Horizontal,
			ElementSpaceSlotKind.Y => LayoutPhysicalAxis.Vertical,
			_ => throw new ArgumentException(
				"锚定约束的运行目标只能是 X 或 Y。",
				nameof(targetSlot))
		};

	public static void RequirePositionTarget(
		ElementSpaceSlotKind targetSlot,
		LayoutPhysicalAxis axis)
	{
		var expected = GetPositionAxis(targetSlot);
		if (expected != axis)
			throw new ArgumentException(
				$"{targetSlot} 不能使用 {axis} 对齐轴。",
				nameof(axis));
	}

	public static void RequireAnchorAxis(
		LayoutAnchor anchor,
		LayoutPhysicalAxis axis)
	{
		var expected = anchor switch
		{
			LayoutAnchor.Left
				or LayoutAnchor.Right
				or LayoutAnchor.CenterX => LayoutPhysicalAxis.Horizontal,
			LayoutAnchor.Top
				or LayoutAnchor.Bottom
				or LayoutAnchor.CenterY => LayoutPhysicalAxis.Vertical,
			_ => throw new ArgumentOutOfRangeException(nameof(anchor))
		};
		if (expected != axis)
			throw new ArgumentException(
				$"{anchor} 锚点不能用于 {axis} 轴。",
				nameof(anchor));
	}

	public static LayoutPhysicalAxis GetPercentageAxis(LayoutPercentageBasis basis) =>
		basis switch
		{
			LayoutPercentageBasis.ContainingBlockWidth
				or LayoutPercentageBasis.ParentContentWidth
				or LayoutPercentageBasis.GridTrackWidth
				or LayoutPercentageBasis.ViewportWidth
				or LayoutPercentageBasis.XamlParentWidth =>
				LayoutPhysicalAxis.Horizontal,
			LayoutPercentageBasis.ContainingBlockHeight
				or LayoutPercentageBasis.ParentContentHeight
				or LayoutPercentageBasis.GridTrackHeight
				or LayoutPercentageBasis.ViewportHeight
				or LayoutPercentageBasis.XamlParentHeight =>
				LayoutPhysicalAxis.Vertical,
			_ => throw new InvalidOperationException(
				$"{basis} 的物理轴取决于 Flex 方向，必须由 Flex 绑定验证。")
		};
}

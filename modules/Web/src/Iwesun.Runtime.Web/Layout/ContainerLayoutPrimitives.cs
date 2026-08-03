namespace Iwesun.Runtime.Web;

public enum ContainerLayoutMechanism
{
	NormalFlow,
	Flex,
	Grid,
	AbsolutePositioning,
	FixedPositioning,
	StickyPositioning,
	Stack,
	Canvas,
	Overlay,
	XamlPanel
}

public enum LayoutContainerReferenceKind
{
	Parent,
	ContainingBlock,
	FlexContainer,
	GridContainer,
	ScrollContainer,
	Viewport,
	XamlParent,
	NamedElement
}

public enum LayoutReferenceBox
{
	ContentBox,
	PaddingBox,
	BorderBox,
	MarginBox,
	Viewport,
	XamlLayoutSlot
}

public enum LayoutPhysicalAxis
{
	Horizontal,
	Vertical
}

public enum LayoutLengthUnit
{
	CssPixel,
	Dip,
	PhysicalPixel,
	ViewportWidth,
	ViewportHeight,
	Em,
	Rem
}

public enum LayoutPercentageBasis
{
	ContainingBlockWidth,
	ContainingBlockHeight,
	ParentContentWidth,
	ParentContentHeight,
	FlexLineMainSize,
	FlexLineCrossSize,
	GridTrackWidth,
	GridTrackHeight,
	ViewportWidth,
	ViewportHeight,
	XamlParentWidth,
	XamlParentHeight
}

public enum LayoutIntrinsicSize
{
	MinContent,
	MaxContent,
	FitContentAvailableSpace
}

public enum LayoutAlignment
{
	Near,
	Center,
	Far,
	Stretch,
	FirstBaseline,
	LastBaseline
}

public enum LayoutAnchor
{
	Left,
	Right,
	Top,
	Bottom,
	CenterX,
	CenterY
}

public enum LayoutDistribution
{
	Near,
	Center,
	Far,
	SpaceBetween,
	SpaceAround,
	SpaceEvenly,
	Stretch
}

public enum LayoutBinaryOperator
{
	Add,
	Subtract,
	Minimum,
	Maximum
}

public readonly record struct LayoutContainerReference
{
	public LayoutContainerReferenceKind Kind { get; }
	public LayoutReferenceBox Box { get; }
	public string Description { get; }

	public LayoutContainerReference(
		LayoutContainerReferenceKind kind,
		LayoutReferenceBox box,
		string? description = null)
	{
		if (kind == LayoutContainerReferenceKind.NamedElement
			&& string.IsNullOrWhiteSpace(description))
			throw new ArgumentException(
				"NamedElement 容器必须提供 XPath、元素 ID 或稳定描述。",
				nameof(description));
		if (kind == LayoutContainerReferenceKind.Viewport
			&& box != LayoutReferenceBox.Viewport)
			throw new ArgumentException("Viewport 容器只能使用 Viewport 参考盒。", nameof(box));
		if (kind == LayoutContainerReferenceKind.XamlParent
			&& box != LayoutReferenceBox.XamlLayoutSlot)
			throw new ArgumentException(
				"XamlParent 容器只能使用 XamlLayoutSlot 参考盒。",
				nameof(box));
		Kind = kind;
		Box = box;
		Description = description ?? string.Empty;
	}
}

namespace Iwesun.Runtime.Web;

public enum XamlElementContentProjectionKind
{
	None,
	DirectChildren,
	Content,
	Inlines,
	Items,
	GeneratedContent,
	Composite
}

public sealed record XamlElementObjectProjectionDecision(
	XamlElementMappingDecision Mapping,
	ElementXamlChildPlacementKind ChildPlacement,
	XamlElementContentProjectionKind ContentProjection);

public enum XamlElementCrossAxis
{
	Horizontal,
	Vertical
}

public enum XamlElementCrossAlignment
{
	Near,
	Center,
	Far,
	Stretch,
	Baseline
}

public enum XamlElementContainingBlockKind
{
	ContentBox,
	PaddingBox,
	Viewport
}

public sealed record XamlElementLayoutPlacement(
	bool IsOutOfFlow,
	XamlElementCrossAxis CrossAxis,
	XamlElementCrossAlignment CrossAlignment,
	bool HasDefiniteCrossSize,
	XamlElementContainingBlockKind ContainingBlock,
	LayoutLength? Left = null,
	LayoutLength? Top = null,
	LayoutLength? Right = null,
	LayoutLength? Bottom = null,
	LayoutLength? MainAxisOffset = null,
	bool HasDefiniteWidth = false,
	bool HasDefiniteHeight = false);

public sealed record XamlElementObjectPlan(
	DomElement? SourceElement,
	XamlElementMappingDecision Mapping,
	IReadOnlyList<GeneratedXamlAttribute> InitializationAttributes,
	ElementXamlChildPlacementKind ChildPlacement,
	IReadOnlyList<XamlGridTrackDefinition> RowDefinitions,
	IReadOnlyList<XamlGridTrackDefinition> ColumnDefinitions,
	IReadOnlyList<XamlElementObjectPlan> Children,
	string Description,
	DomElement? OwnerElement = null,
	XamlElementContentProjectionKind ContentProjection =
		XamlElementContentProjectionKind.None,
	XamlElementLayoutPlacement? LayoutPlacement = null);

public sealed record XamlElementObjectCreationContext(
	XamlElementObjectPlan Plan);

public sealed record XamlElementObjectPropertyFillContext(
	object Element,
	XamlElementObjectPlan Plan);

public sealed record XamlElementObjectAttachmentContext(
	object Parent,
	object Child,
	XamlElementObjectPlan ParentPlan,
	XamlElementObjectPlan ChildPlan,
	ElementXamlChildPlacementKind Placement);

public partial interface IXamlElementObjectFactory
{
	object CreateSyntheticElement(XamlElementObjectCreationContext context);

	void FillElementProperties(XamlElementObjectPropertyFillContext context);

	void AttachChild(XamlElementObjectAttachmentContext context);

	void ApplyGridTracks(
		object element,
		IReadOnlyList<XamlGridTrackDefinition> rowDefinitions,
		IReadOnlyList<XamlGridTrackDefinition> columnDefinitions);
}

public interface IXamlGlobalRelationshipBinder
{
	void BindGlobalRelationships(
		HtmlRuntimeXamlGlobalRelationshipGraph relationships);
}

public sealed record XamlElementObjectBuildNode(
	XamlElementObjectPlan Plan,
	object Element,
	IReadOnlyList<XamlElementObjectBuildNode> Children);

public sealed record XamlElementObjectBuildResult(
	IReadOnlyList<object> RootObjects,
	IReadOnlyList<XamlElementObjectBuildNode> Roots);

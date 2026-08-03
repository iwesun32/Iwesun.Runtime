namespace Iwesun.Runtime.Web;

[Flags]
public enum ElementEvidenceKind
{
	None = 0,
	Identity = 1 << 0,
	TreeRelationships = 1 << 1,
	Attributes = 1 << 2,
	CssDeclarations = 1 << 3,
	ComputedStyles = 1 << 4,
	LocalLayout = 1 << 5,
	DomRuntimeGeometry = 1 << 6,
	VisualEffects = 1 << 7,
	Animations = 1 << 8,
	Events = 1 << 9,
	Resources = 1 << 10,
	TextContent = 1 << 11,
	FormState = 1 << 12,
	ScrollState = 1 << 13
}

public enum ElementFillSlot
{
	Unspecified = -1,
	Attribute,
	SourceInitialization,
	SourceLink,
	SourceRuntime,
	TreeRelationship,
	Event,
	Resource
}

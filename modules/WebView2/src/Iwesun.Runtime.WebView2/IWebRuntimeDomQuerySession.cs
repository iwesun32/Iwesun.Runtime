namespace Iwesun.Runtime.WebView2;

public enum WebRuntimeDomOwnerKind
{
	Property,
	Attribute,
	ExtensionAttribute,
	RuntimeProperty,
	DataSource,
	Event
}

public enum WebRuntimeDomSlotCategory
{
	Unspecified = -1,
	Space,
	Style,
	Effect,
	Action,
	DataOrganization
}

[Flags]
public enum WebRuntimeDomEvidenceKind
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

public enum WebRuntimeDomDataSlot
{
	Initialization,
	Link,
	Runtime
}

public enum WebRuntimeDomElementSpecialization
{
	General,
	Layout,
	Text,
	Interactive,
	FormControl,
	Media,
	Metadata,
	Svg
}

/// <summary>
/// Identifies one strongly typed DOM property read. Implementations must batch
/// requests and must not expose script text or JSON payloads to callers.
/// </summary>
public sealed record WebRuntimeDomPropertyRequest(
	string QueryId,
	string DocumentScope,
	string XPath,
	string TagName,
	string PropertyName,
	string ReflectedPropertyName,
	WebRuntimeDomOwnerKind OwnerKind,
	WebRuntimeDomSlotCategory Category,
	WebRuntimeDomEvidenceKind EvidenceKind,
	WebRuntimeDomDataSlot Slot,
	WebRuntimeDomElementSpecialization Specialization =
		WebRuntimeDomElementSpecialization.General,
	int NodeId = 0,
	int BackendNodeId = 0,
	int HierarchyLevel = 0);

public enum WebRuntimeDomPropertyStatus
{
	Captured,
	ConfirmedAbsent,
	SourceUnsupported
}

public enum WebRuntimeDomValueSource
{
	Unspecified,
	ContainerAutomaticLayout,
	LinkedCalculation,
	LinkedConstant,
	DirectConstant
}

public enum WebRuntimeDomLinkKind
{
	None,
	DomDescription,
	XPath,
	Url,
	CssExpression,
	LayoutExpression,
	XamlBinding,
	ConstantReference,
	CustomString
}

/// <summary>
/// Strongly typed result returned by the live WebView2 DOM query API.
/// </summary>
public sealed record WebRuntimeDomPropertyResult(
	string QueryId,
	WebRuntimeDomPropertyStatus Status,
	string Value,
	WebRuntimeDomValueSource ValueSource,
	WebRuntimeDomLinkKind LinkKind,
	string LinkIdentity,
	string Description);

/// <summary>
/// Provides a direct, batched live-DOM API. JSON is not part of this contract;
/// serialization is reserved for optional persistence outside the Fill path.
/// </summary>
public interface IWebRuntimeDomQuerySession
{
	ValueTask<IReadOnlyList<WebRuntimeDomPropertyResult>>
		QueryDomPropertiesAsync(
			IReadOnlyList<WebRuntimeDomPropertyRequest> requests,
			CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads exactly one strongly identified DOM property without requiring a
/// caller-created batch.
/// </summary>
public interface IWebRuntimeSingleDomPropertyQuerySession
{
	ValueTask<WebRuntimeDomPropertyResult> QueryDomPropertyAsync(
		WebRuntimeDomPropertyRequest request,
		CancellationToken cancellationToken = default);
}

namespace Iwesun.Runtime.Web;

public static class DomElementRuntimePropertyCatalog
{
	private static readonly string[] StylePropertyNames =
	[
		"display", "position", "visibility", "opacity", "zIndex",
		"pointerEvents", "boxSizing", "width", "height", "minWidth",
		"maxWidth", "minHeight", "maxHeight", "top", "right", "bottom",
		"left", "inset", "margin", "marginTop", "marginRight",
		"marginBottom", "marginLeft", "padding", "paddingTop",
		"paddingRight", "paddingBottom", "paddingLeft", "border",
		"borderTop", "borderRight", "borderBottom", "borderLeft",
		"borderWidth", "borderTopWidth", "borderRightWidth",
		"borderBottomWidth", "borderLeftWidth", "borderStyle",
		"borderTopStyle", "borderRightStyle", "borderBottomStyle",
		"borderLeftStyle", "borderColor", "borderTopColor",
		"borderRightColor", "borderBottomColor", "borderLeftColor",
		"borderRadius", "borderTopLeftRadius", "borderTopRightRadius",
		"borderBottomRightRadius", "borderBottomLeftRadius",
		"background", "backgroundColor", "backgroundImage",
		"backgroundPosition", "backgroundSize", "backgroundRepeat", "color",
		"colorScheme", "fill", "stroke", "strokeWidth", "strokeLinecap",
		"strokeLinejoin",
		"boxShadow", "filter", "backdropFilter", "transform",
		"transformOrigin", "transition", "transitionProperty",
		"transitionDuration", "transitionTimingFunction", "transitionDelay",
		"animation", "animationName", "animationDuration",
		"animationTimingFunction", "animationDelay", "animationIterationCount",
		"animationDirection", "animationFillMode", "animationPlayState",
		"flex", "flexDirection", "flexWrap", "flexFlow", "flexGrow",
		"flexShrink", "flexBasis", "justifyContent", "alignContent",
		"alignItems", "alignSelf", "gap", "rowGap", "columnGap", "order",
		"grid", "gridTemplate", "gridTemplateColumns", "gridTemplateRows",
		"gridTemplateAreas", "gridAutoColumns", "gridAutoRows",
		"gridAutoFlow", "gridColumn", "gridRow", "gridArea", "overflow",
		"overflowX", "overflowY", "overflowWrap", "whiteSpace", "textAlign",
		"textDecoration", "textDecorationLine", "textTransform", "textOverflow", "textIndent",
		"textShadow", "font", "fontFamily", "fontSize", "fontWeight",
		"fontStyle", "fontStretch", "lineHeight", "letterSpacing",
		"wordBreak", "wordSpacing", "writingMode", "direction", "objectFit",
		"objectPosition", "cursor", "clipPath", "contain", "contentVisibility"
	];

	private static readonly IReadOnlySet<string> LayoutStylePropertyNames =
		new HashSet<string>(
			[
				"display", "position", "zIndex", "boxSizing",
				"width", "height", "minWidth", "maxWidth", "minHeight",
				"maxHeight", "top", "right", "bottom", "left", "inset",
				"margin", "marginTop", "marginRight", "marginBottom",
				"marginLeft", "padding", "paddingTop", "paddingRight",
				"paddingBottom", "paddingLeft", "flex", "flexDirection",
				"flexWrap", "flexFlow", "flexGrow", "flexShrink",
				"flexBasis", "justifyContent", "alignContent", "alignItems",
				"alignSelf", "gap", "rowGap", "columnGap", "order", "grid",
				"gridTemplate", "gridTemplateColumns", "gridTemplateRows",
				"gridTemplateAreas", "gridAutoColumns", "gridAutoRows",
				"gridAutoFlow", "gridColumn", "gridRow", "gridArea",
				"overflow", "overflowX", "overflowY", "writingMode",
				"direction", "contain", "contentVisibility"
			],
			StringComparer.Ordinal);

	public static IReadOnlyList<DomElementRuntimePropertyDefinition> Standard
	{
		get;
	} = Create();

	public static bool Contains(string name)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		return Standard.Any(property =>
			property.Name.Equals(name, StringComparison.Ordinal));
	}

	public static ElementEvidenceKind ResolveEvidenceKind(string name)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		var standard = Standard.FirstOrDefault(property =>
			property.Name.Equals(name, StringComparison.Ordinal));
		if (standard is not null)
			return standard.EvidenceKind;
		if (name.StartsWith("state.scroll", StringComparison.Ordinal))
			return ElementEvidenceKind.ScrollState;
		if (name.StartsWith("state.", StringComparison.Ordinal))
			return ElementEvidenceKind.FormState;
		if (name.StartsWith("content.", StringComparison.Ordinal))
			return ElementEvidenceKind.TextContent;
		if (name.StartsWith("resource.", StringComparison.Ordinal))
			return ElementEvidenceKind.Resources;
		throw new ArgumentOutOfRangeException(
			nameof(name),
			name,
			"Runtime DOM property has no registered evidence semantics.");
	}

	private static IReadOnlyList<DomElementRuntimePropertyDefinition> Create()
	{
		var properties = new List<DomElementRuntimePropertyDefinition>
		{
			Space("rect.x"),
			Space("rect.y"),
			Space("rect.width"),
			Space("rect.height"),
			State("state.disabled"),
			State("state.checked"),
			State("state.indeterminate"),
			State("state.valid"),
			State("state.willValidate"),
			State("state.validationMessage"),
			State("state.selected"),
			State("state.readOnly"),
			State("state.contentEditable"),
			State("state.tabIndex"),
			Scroll("state.scrollLeft"),
			Scroll("state.scrollTop"),
			Scroll("state.scrollWidth"),
			Scroll("state.scrollHeight"),
			Content("content.name"),
			Content("content.namespaceUri"),
			Content("content.ariaLabel"),
			Content("content.title"),
			Content("content.href"),
			Content("content.type"),
			Content("content.value"),
			Content("content.placeholder"),
			Content("content.ownText"),
			Content("content.textContent"),
			Content("content.innerText"),
			Resource("resource.imageSourceUrl"),
			Resource("resource.mediaSourceUrl"),
			Resource("resource.embeddedSourceUrl"),
			Resource("resource.posterSourceUrl"),
			Resource("resource.canvasCommandStream"),
			Resource("resource.references"),
			Effect(
				"effect.pseudoElements",
				ElementEvidenceKind.VisualEffects),
			Effect(
				"effect.animations",
				ElementEvidenceKind.Animations)
		};
		properties.AddRange(StylePropertyNames.Select(static name =>
			new DomElementRuntimePropertyDefinition(
				$"style.{name}",
				ElementSlotCategory.Style,
				ResolveStyleEvidence(name))));
		return properties;
	}

	private static DomElementRuntimePropertyDefinition Space(string name) =>
		new(
			name,
			ElementSlotCategory.Space,
			ElementEvidenceKind.LocalLayout
				| ElementEvidenceKind.DomRuntimeGeometry);

	private static DomElementRuntimePropertyDefinition State(string name) =>
		new(
			name,
			ElementSlotCategory.DataOrganization,
			ElementEvidenceKind.FormState);

	private static DomElementRuntimePropertyDefinition Scroll(string name) =>
		new(
			name,
			ElementSlotCategory.DataOrganization,
			ElementEvidenceKind.ScrollState);

	private static DomElementRuntimePropertyDefinition Content(string name) =>
		new(
			name,
			ElementSlotCategory.DataOrganization,
			ElementEvidenceKind.TextContent);

	private static DomElementRuntimePropertyDefinition Resource(string name) =>
		new(
			name,
			ElementSlotCategory.DataOrganization,
			ElementEvidenceKind.Resources);

	private static DomElementRuntimePropertyDefinition Effect(
		string name,
		ElementEvidenceKind evidenceKind) =>
		new(name, ElementSlotCategory.Effect, evidenceKind);

	private static ElementEvidenceKind ResolveStyleEvidence(string name) =>
		ElementEvidenceKind.CssDeclarations
			| ElementEvidenceKind.ComputedStyles
			| (LayoutStylePropertyNames.Contains(name)
				? ElementEvidenceKind.LocalLayout
				: ElementEvidenceKind.None);
}

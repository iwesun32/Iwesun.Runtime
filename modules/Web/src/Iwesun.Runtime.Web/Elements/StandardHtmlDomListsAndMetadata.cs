namespace Iwesun.Runtime.Web;

public sealed partial class HtmlAnchorDomElement(DomElementMapping m) :
	HtmlHyperlinkDomElementDefinition(m, "a")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		Href.SourceInitialization.IsSet
			&& FormattingContext() is
				HtmlCssFormattingContext.FlexRow
				or HtmlCssFormattingContext.FlexColumn
			? new(
				XamlElementObjectType.HtmlInteractiveFlexPanel,
				XamlElementMappingKind.FlexLayout,
				true,
				"The interactive hyperlink owns a multi-child flex "
					+ "formatting context.")
			: Href.SourceInitialization.IsSet
			? new(XamlElementObjectType.HyperlinkButton, XamlElementMappingKind.TypeDefault, false, "The a element with href is an interactive hyperlink control.")
			: Children.Count == 0
				? new(XamlElementObjectType.TextBlock, XamlElementMappingKind.InlineFlow, false, "The a element without href is leaf phrasing text.")
				: new(XamlElementObjectType.HtmlInlineFlowPanel, XamlElementMappingKind.InlineFlow, true, "The a element without href owns noninteractive phrasing.");

	protected override XamlElementObjectProjectionDecision
	ResolveXamlObjectProjection() =>
		CreateXaml().Kind == XamlElementMappingKind.FlexLayout
			? new(
				CreateXaml(),
				ElementXamlChildPlacementKind.DirectChildren,
				XamlElementContentProjectionKind.Composite)
			: Href.SourceInitialization.IsSet
			? base.ResolveXamlObjectProjection()
			: Children.Count == 0
				? new(
					CreateXaml(),
					ElementXamlChildPlacementKind.None,
					XamlElementContentProjectionKind.GeneratedContent)
				: new(
					CreateXaml(),
					ElementXamlChildPlacementKind.DirectChildren,
					XamlElementContentProjectionKind.Composite);

	protected override IReadOnlyList<XamlElementObjectPlan>
		BuildXamlObjectChildPlans() =>
		CreateXaml().Kind == XamlElementMappingKind.FlexLayout
			? BuildDirectDomChildObjectPlans()
			: base.BuildXamlObjectChildPlans();

	protected override bool TryBuildXamlFlexLayout(
		out bool vertical,
		out IReadOnlyList<XamlGridTrackDefinition> definitions)
	{
		if (CreateXaml().Kind != XamlElementMappingKind.FlexLayout)
		{
			vertical = false;
			definitions = [];
			return false;
		}
		return TryBuildStrongTextVisualFlexLayout(out vertical, out definitions);
	}

	protected override bool TryResolveXamlLayoutTrackIndex(
		DomElement child,
		out bool vertical,
		out int index) =>
		TryResolveStrongTextVisualFlexTrackIndex(
			child,
			out vertical,
			out index);

	private static XamlGridTrackDefinition BuildAnchorFlexTrack(
		DomElement child,
		bool vertical)
	{
		if (child is not HtmlDomElementDefinition html)
			return new("Auto", null, null);
		var properties = new[]
		{
			"style.flexGrow",
			"style.width",
			"style.height",
			"style.minWidth",
			"style.maxWidth",
			"style.minHeight",
			"style.maxHeight"
		}
			.Select(name => html.RuntimeProperties.FirstOrDefault(property =>
				property.Name.Equals(name, StringComparison.Ordinal))
				?? html.HtmlRoot?.ResolveGlobalStyleProperty(html, name))
			.Where(static property => property is not null)
			.Cast<DomElementRuntimeProperty>()
			.ToDictionary(
				static property => property.Name,
				StringComparer.Ordinal);
		var length = "Auto";
		if (properties.TryGetValue("style.flexGrow", out var grow)
			&& double.TryParse(
				ActiveRuntimeValue(grow),
				System.Globalization.NumberStyles.Float,
				System.Globalization.CultureInfo.InvariantCulture,
				out var growth)
			&& growth > 0)
		{
			length = growth == 1
				? "*"
				: $"{growth.ToString(
					"R",
					System.Globalization.CultureInfo.InvariantCulture)}*";
		}
		else if (properties.TryGetValue(
			vertical ? "style.height" : "style.width",
			out var size))
		{
			length = NormalizeXamlLength(SourceInitialization(size))
				?? NormalizeXamlLength(ActiveRuntimeValue(size))
				?? "Auto";
		}
		var axis = vertical ? "Height" : "Width";
		var minimum = properties.TryGetValue($"style.min{axis}", out var min)
			? NormalizeXamlLength(SourceInitialization(min))
				?? NormalizeXamlLength(ActiveRuntimeValue(min))
			: null;
		var maximum = properties.TryGetValue($"style.max{axis}", out var max)
			? NormalizeXamlLength(SourceInitialization(max))
				?? NormalizeXamlLength(ActiveRuntimeValue(max))
			: null;
		return new(length, minimum, maximum);
	}

	private static string? ActiveRuntimeValue(
		DomElement element,
		string propertyName)
	{
		var global = element.HtmlRoot?.ResolveGlobalStyleValue(
				element,
				propertyName,
				DomPropertyDataSlot.Runtime)
			?? element.HtmlRoot?.ResolveGlobalStyleValue(
				element,
				propertyName,
				DomPropertyDataSlot.Initialization);
		if (!string.IsNullOrWhiteSpace(global))
			return global.Trim();
		return element is HtmlDomElementDefinition html
			? ActiveRuntimeValue(
				html.RuntimeProperties.FirstOrDefault(property =>
					property.Name.Equals(
						propertyName,
						StringComparison.Ordinal)))
			: null;
	}

	private static string? ActiveRuntimeValue(
		DomElementRuntimeProperty? property) =>
		property?.SourceRuntime.IsSet == true
			? property.SourceRuntime.Value?.Trim()
			: property?.SourceInitialization.IsSet == true
				? property.SourceInitialization.Value?.Trim()
				: null;

	[HtmlElementProperty] public DomElementStringProperty HrefLang { get; } = Attribute("hreflang", "a");
	[HtmlElementProperty] public DomElementStringProperty Type { get; } = Attribute("type", "a");
	protected override IReadOnlyDictionary<string, HtmlElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleXamlAttributes(
			(nameof(Href), HtmlElementAttributeXamlHandling.InlineXaml),
			(nameof(Target), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Download), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Ping), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Rel), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(HrefLang), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Type), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(ReferrerPolicy), HtmlElementAttributeXamlHandling.RuntimeDataSource));

	protected override IReadOnlyList<GeneratedXamlAttribute> BuildXamlAttributes()
	{
		var attributes = base.BuildXamlAttributes()
			.ToDictionary(static attribute => attribute.Name, StringComparer.Ordinal);
		if (Href.SourceInitialization.IsSet)
		{
			SetXamlAttribute(
				attributes,
				"NavigateUri",
				SourceInitialization(Href),
				Href);
		}
		return attributes.Values.ToArray();
	}
}
public sealed partial class HtmlUnorderedListDomElement(DomElementMapping m) : HtmlListDomElementDefinition(m, "ul")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(XamlElementObjectType.ListView, XamlElementMappingKind.TypeDefault, false, "The ul element owns an unordered item collection.");
}
public sealed partial class HtmlOrderedListDomElement(DomElementMapping m) : HtmlListDomElementDefinition(m, "ol")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(XamlElementObjectType.ListView, XamlElementMappingKind.TypeDefault, false, "The ol element owns an ordinal item collection.");
	[HtmlElementProperty] public DomElementStringProperty Reversed { get; } = Attribute("reversed", "ol");
	[HtmlElementProperty] public DomElementStringProperty Start { get; } = Attribute("start", "ol");
	[HtmlElementProperty] public DomElementStringProperty Type { get; } = Attribute("type", "ol");
	protected override IReadOnlyDictionary<string, HtmlElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleXamlAttributes(
			(nameof(Reversed), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Start), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Type), HtmlElementAttributeXamlHandling.RuntimeDataSource));
}
public sealed partial class HtmlMenuDomElement(DomElementMapping m) : HtmlListDomElementDefinition(m, "menu")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(XamlElementObjectType.ListView, XamlElementMappingKind.TypeDefault, false, "The menu element owns a command item collection.");
}
public sealed partial class HtmlListItemDomElement(DomElementMapping m) : HtmlListDomElementDefinition(m, "li", ElementDefaultDisplay.ListItem)
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(XamlElementObjectType.ListViewItem, XamlElementMappingKind.TypeDefault, false, "The li element owns one selectable list-item content box.");
	[HtmlElementProperty] public DomElementStringProperty Value { get; } = Attribute("value", "li");
	protected override IReadOnlyDictionary<string, HtmlElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleXamlAttributes((nameof(Value), HtmlElementAttributeXamlHandling.RuntimeDataSource));

	protected override IReadOnlyList<GeneratedXamlAttribute> BuildXamlAttributes() =>
		base.BuildXamlAttributes()
			.Where(static attribute => attribute.Name != "Content")
			.ToArray();

	protected override XamlElementObjectProjectionDecision
		ResolveXamlObjectProjection() =>
		new(
			CreateXaml(),
			ElementXamlChildPlacementKind.Content,
			XamlElementContentProjectionKind.Composite);

	protected override IReadOnlyList<XamlElementObjectPlan>
		BuildXamlObjectChildPlans()
	{
		var content = BuildDirectDomChildObjectPlans().ToList();
		var ownText = DataSource("content.ownText");
		if (ownText?.SourceInitialization.IsSet == true
			&& !string.IsNullOrWhiteSpace(ownText.SourceInitialization.Value))
		{
			content.Insert(
				0,
				CreateSyntheticXamlObjectPlan(
					XamlElementObjectType.TextBlock,
					[new("Text", ownText.SourceInitialization.Value, ownText)],
					ElementXamlChildPlacementKind.None,
					[],
					$"List item text for {DocumentScope}::{XPath}."));
		}
		var marker = ResolveMarker();
		if (marker is not null)
		{
			content.Insert(
				0,
				CreateSyntheticXamlObjectPlan(
					XamlElementObjectType.TextBlock,
					[new("Text", marker, null)],
					ElementXamlChildPlacementKind.None,
					[],
					$"List marker for {DocumentScope}::{XPath}."));
		}
		return
		[
			CreateSyntheticXamlObjectPlan(
				XamlElementObjectType.StackPanel,
				[new("Orientation", "Horizontal", null)],
				ElementXamlChildPlacementKind.DirectChildren,
				content,
				$"List item content for {DocumentScope}::{XPath}.")
		];
	}

	private string? ResolveMarker()
	{
		if (Parent is HtmlUnorderedListDomElement)
			return "•";
		if (Parent is not HtmlOrderedListDomElement ordered)
			return null;
		var siblings = ordered.Children.OfType<HtmlListItemDomElement>().ToArray();
		var index = Array.IndexOf(siblings, this);
		if (index < 0)
			return null;
		var start = int.TryParse(
			ordered.Start.SourceInitialization.Value,
			System.Globalization.NumberStyles.Integer,
			System.Globalization.CultureInfo.InvariantCulture,
			out var parsedStart)
				? parsedStart
				: ordered.Reversed.SourceInitialization.IsSet
					? siblings.Length
					: 1;
		var ordinal = ordered.Reversed.SourceInitialization.IsSet
			? start - index
			: start + index;
		if (int.TryParse(
			Value.SourceInitialization.Value,
			System.Globalization.NumberStyles.Integer,
			System.Globalization.CultureInfo.InvariantCulture,
			out var explicitValue))
		{
			ordinal = explicitValue;
		}
		return HtmlListMarkerFormatter.FormatOrdered(
			ordinal,
			ordered.Type.SourceInitialization.Value);
	}
}
public sealed partial class HtmlDescriptionListDomElement(DomElementMapping m) : HtmlListDomElementDefinition(m, "dl")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(XamlElementObjectType.StackPanel, XamlElementMappingKind.BlockFlow, false, "The dl element vertically owns term-description groups.");

	protected override XamlElementObjectProjectionDecision
		ResolveXamlObjectProjection() =>
		new(
			CreateXaml(),
			ElementXamlChildPlacementKind.DirectChildren,
			XamlElementContentProjectionKind.DirectChildren);
}
public sealed partial class HtmlDescriptionTermDomElement(DomElementMapping m) : HtmlPhrasingDomElementDefinition(m, "dt")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		Children.Count == 0
			? new(XamlElementObjectType.TextBlock, XamlElementMappingKind.InlineFlow, false, "The dt element is leaf description-term text.")
			: new(XamlElementObjectType.HtmlInlineFlowPanel, XamlElementMappingKind.InlineFlow, true, "The dt element owns composite description-term phrasing.");
}
public sealed partial class HtmlDescriptionDetailsDomElement(DomElementMapping m) : HtmlSectioningDomElementDefinition(m, "dd")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(XamlElementObjectType.StackPanel, XamlElementMappingKind.BlockFlow, false, "The dd element owns vertical description-detail flow content.");
}
public sealed partial class HtmlDetailsDomElement(DomElementMapping m) :
	HtmlInteractiveDomElementDefinition(m, "details", ElementInteractionKind.Disclosure, XamlControlFamily.Button)
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(
			XamlElementObjectType.Expander,
			XamlElementMappingKind.TypeDefault,
			false,
			"The details element owns a disclosure header and expandable content.");
	[HtmlElementProperty] public DomElementStringProperty Open { get; } = Attribute("open", "details");
	[HtmlElementProperty] public DomElementStringProperty Name { get; } = Attribute("name", "details");
	protected override IReadOnlyDictionary<string, HtmlElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleXamlAttributes(
			(nameof(Open), HtmlElementAttributeXamlHandling.InlineXaml),
			(nameof(Name), HtmlElementAttributeXamlHandling.RuntimeDataSource));

	protected override IReadOnlyList<GeneratedXamlAttribute> BuildXamlAttributes()
	{
		var attributes = base.BuildXamlAttributes()
			.ToDictionary(static attribute => attribute.Name, StringComparer.Ordinal);
		if (Open.SourceInitialization.IsSet)
			SetXamlAttribute(attributes, "IsExpanded", "True", Open);
		var summary = Children.OfType<HtmlSummaryDomElement>().FirstOrDefault();
		var summaryText = summary?.DataSources
			.FirstOrDefault(static property => property.Name == "content.ownText");
		if (summaryText?.SourceInitialization.IsSet == true)
		{
			SetXamlAttribute(
				attributes,
				"Header",
				summaryText.SourceInitialization.Value,
				summaryText);
		}
		return attributes.Values.ToArray();
	}
}
public sealed partial class HtmlSummaryDomElement(DomElementMapping m) :
	HtmlInteractiveDomElementDefinition(m, "summary", ElementInteractionKind.Disclosure, XamlControlFamily.Button)
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(XamlElementObjectType.Button, XamlElementMappingKind.TypeDefault, false, "The summary element is the disclosure summary control.");

	protected override bool HasXamlOutput() =>
		Parent is not HtmlDetailsDomElement;

	protected internal override IReadOnlyList<XamlElementObjectPlan>
		BuildXamlObjectPlans() =>
		Parent is HtmlDetailsDomElement
			? []
			: base.BuildXamlObjectPlans();
}
public sealed partial class HtmlDialogDomElement(DomElementMapping m) : HtmlSectioningDomElementDefinition(m, "dialog")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(XamlElementObjectType.HtmlDialogControl, XamlElementMappingKind.PositionedLayout, true, "The dialog element owns top-layer visibility, close policy, and one content slot.");
	protected override XamlElementObjectProjectionDecision
		ResolveXamlObjectProjection() =>
		new(
			CreateXaml(),
			ElementXamlChildPlacementKind.Content,
			XamlElementContentProjectionKind.Composite);
	[HtmlElementProperty] public DomElementStringProperty Open { get; } = Attribute("open", "dialog");
	[HtmlElementProperty] public DomElementStringProperty ClosedBy { get; } = Attribute("closedby", "dialog");
	protected override IReadOnlyDictionary<string, HtmlElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleXamlAttributes(
			(nameof(Open), HtmlElementAttributeXamlHandling.InlineXaml),
			(nameof(ClosedBy), HtmlElementAttributeXamlHandling.RuntimeDataSource));
	protected override IReadOnlyList<GeneratedXamlAttribute> BuildXamlAttributes()
	{
		var attributes = base.BuildXamlAttributes()
			.ToDictionary(static attribute => attribute.Name, StringComparer.Ordinal);
		SetXamlAttribute(
			attributes,
			"IsOpen",
			Open.SourceInitialization.IsSet ? "True" : "False",
			Open);
		SetXamlAttribute(
			attributes,
			"ClosedBy",
			SourceInitialization(ClosedBy),
			ClosedBy);
		return attributes.Values.ToArray();
	}
}
public sealed partial class HtmlSearchDomElement(DomElementMapping m) : HtmlSectioningDomElementDefinition(m, "search")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		FormattingContext() switch
		{
			HtmlCssFormattingContext.FlexRow
				or HtmlCssFormattingContext.FlexColumn =>
				new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.FlexLayout, true,
					"The search landmark owns a CSS flex formatting context."),
			HtmlCssFormattingContext.Grid =>
				new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.GridLayout, true,
					"The search landmark owns a CSS grid formatting context."),
			_ => new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.BlockFlow, true,
				"The search landmark preserves normal block flow.")
		};
}
public sealed partial class HtmlSlotDomElement(DomElementMapping m) : HtmlContainerDomElementDefinition(m, "slot")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		FormattingContext() switch
		{
			HtmlCssFormattingContext.FlexRow
				or HtmlCssFormattingContext.FlexColumn =>
				new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.FlexLayout, true,
					"The slot's assigned or fallback nodes own a CSS flex context."),
			HtmlCssFormattingContext.Grid =>
				new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.GridLayout, true,
					"The slot's assigned or fallback nodes own a CSS grid context."),
			_ => new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.BlockFlow, true,
				"The slot preserves assigned-node or fallback normal flow.")
		};
	[HtmlElementProperty] public DomElementStringProperty Name { get; } = Attribute("name", "slot");
	protected override IReadOnlyDictionary<string, HtmlElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleXamlAttributes((nameof(Name), HtmlElementAttributeXamlHandling.RuntimeDataSource));
}
public sealed partial class HtmlNoScriptDomElement(DomElementMapping m) :
	HtmlDomElementDefinition(
		m, "noscript", ElementCategory.Metadata, ElementVisualKind.NonVisual,
		ElementContentModel.Transparent, ElementClosure.OpenContainer,
		ElementSyntax.Normal, XamlConversionSupport.NonVisual,
		XamlControlFamily.None, ElementDefaultDisplay.None,
		ElementInteractionKind.None, ElementXamlChildPlacementKind.None)
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(null, XamlElementMappingKind.TypeDefault, false, "The noscript element is nonvisual metadata in this runtime.");
}
public sealed partial class HtmlScriptDomElement(DomElementMapping m) : HtmlMetadataDomElementDefinition(m, "script", ElementSyntax.RawText)
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(null, XamlElementMappingKind.TypeDefault, false, "The script element is captured as nonvisual behavior evidence.");
	[HtmlElementProperty] public DomElementStringProperty Source { get; } = Attribute("src", "script");
	[HtmlElementProperty] public DomElementStringProperty Type { get; } = Attribute("type", "script");
	[HtmlElementProperty] public DomElementStringProperty NoModule { get; } = Attribute("nomodule", "script");
	[HtmlElementProperty] public DomElementStringProperty Async { get; } = Attribute("async", "script");
	[HtmlElementProperty] public DomElementStringProperty Defer { get; } = Attribute("defer", "script");
	[HtmlElementProperty] public DomElementStringProperty CrossOrigin { get; } = Attribute("crossorigin", "script");
	[HtmlElementProperty] public DomElementStringProperty Integrity { get; } = Attribute("integrity", "script");
	[HtmlElementProperty] public DomElementStringProperty ReferrerPolicy { get; } = Attribute("referrerpolicy", "script");
	[HtmlElementProperty] public DomElementStringProperty Blocking { get; } = Attribute("blocking", "script");
	[HtmlElementProperty] public DomElementStringProperty FetchPriority { get; } = Attribute("fetchpriority", "script");
}
public sealed partial class HtmlStyleDomElement(DomElementMapping m) : HtmlMetadataDomElementDefinition(m, "style", ElementSyntax.RawText)
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(null, XamlElementMappingKind.TypeDefault, false, "The style element is captured through the global style connection.");
	[HtmlElementProperty] public DomElementStringProperty Media { get; } = Attribute("media", "style");
	[HtmlElementProperty] public DomElementStringProperty Blocking { get; } = Attribute("blocking", "style");
}
public sealed partial class HtmlLinkDomElement(DomElementMapping m) : HtmlMetadataDomElementDefinition(m, "link", ElementSyntax.Void)
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(null, XamlElementMappingKind.TypeDefault, false, "The link element is nonvisual external-resource metadata.");
	[HtmlElementProperty] public DomElementStringProperty Href { get; } = Attribute("href", "link");
	[HtmlElementProperty] public DomElementStringProperty CrossOrigin { get; } = Attribute("crossorigin", "link");
	[HtmlElementProperty] public DomElementStringProperty Rel { get; } = Attribute("rel", "link");
	[HtmlElementProperty] public DomElementStringProperty Media { get; } = Attribute("media", "link");
	[HtmlElementProperty] public DomElementStringProperty Integrity { get; } = Attribute("integrity", "link");
	[HtmlElementProperty] public DomElementStringProperty HrefLang { get; } = Attribute("hreflang", "link");
	[HtmlElementProperty] public DomElementStringProperty Type { get; } = Attribute("type", "link");
	[HtmlElementProperty] public DomElementStringProperty As { get; } = Attribute("as", "link");
	[HtmlElementProperty] public DomElementStringProperty Color { get; } = Attribute("color", "link");
	[HtmlElementProperty] public DomElementStringProperty Sizes { get; } = Attribute("sizes", "link");
	[HtmlElementProperty] public DomElementStringProperty ImageSourceSet { get; } = Attribute("imagesrcset", "link");
	[HtmlElementProperty] public DomElementStringProperty ImageSizes { get; } = Attribute("imagesizes", "link");
	[HtmlElementProperty] public DomElementStringProperty ReferrerPolicy { get; } = Attribute("referrerpolicy", "link");
	[HtmlElementProperty] public DomElementStringProperty Blocking { get; } = Attribute("blocking", "link");
	[HtmlElementProperty] public DomElementStringProperty FetchPriority { get; } = Attribute("fetchpriority", "link");
	[HtmlElementProperty] public DomElementStringProperty Disabled { get; } = Attribute("disabled", "link");
}
public sealed partial class HtmlMetaDomElement(DomElementMapping m) : HtmlMetadataDomElementDefinition(m, "meta", ElementSyntax.Void)
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(null, XamlElementMappingKind.TypeDefault, false, "The meta element is nonvisual document metadata.");
	[HtmlElementProperty] public DomElementStringProperty Name { get; } = Attribute("name", "meta");
	[HtmlElementProperty] public DomElementStringProperty HttpEquiv { get; } = Attribute("http-equiv", "meta");
	[HtmlElementProperty] public DomElementStringProperty Content { get; } = Attribute("content", "meta");
	[HtmlElementProperty] public DomElementStringProperty Charset { get; } = Attribute("charset", "meta");
	[HtmlElementProperty] public DomElementStringProperty Media { get; } = Attribute("media", "meta");
}
public sealed partial class HtmlTemplateDomElement(DomElementMapping m) :
	HtmlDomElementDefinition(
		m, "template", ElementCategory.Metadata, ElementVisualKind.NonVisual,
		ElementContentModel.Transparent, ElementClosure.OpenContainer,
		ElementSyntax.Normal, XamlConversionSupport.NonVisual,
		XamlControlFamily.None, ElementDefaultDisplay.None,
		ElementInteractionKind.None, ElementXamlChildPlacementKind.None)
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(null, XamlElementMappingKind.TypeDefault, false, "The template element stores inert nonvisual template content.");
	[HtmlElementProperty] public DomElementStringProperty ShadowRootClonable { get; } = Attribute("shadowrootclonable", "template");
	[HtmlElementProperty] public DomElementStringProperty ShadowRootCustomElementRegistry { get; } = Attribute("shadowrootcustomelementregistry", "template");
	[HtmlElementProperty] public DomElementStringProperty ShadowRootDelegatesFocus { get; } = Attribute("shadowrootdelegatesfocus", "template");
	[HtmlElementProperty] public DomElementStringProperty ShadowRootMode { get; } = Attribute("shadowrootmode", "template");
	[HtmlElementProperty] public DomElementStringProperty ShadowRootSerializable { get; } = Attribute("shadowrootserializable", "template");
	[HtmlElementProperty] public DomElementStringProperty ShadowRootSlotAssignment { get; } = Attribute("shadowrootslotassignment", "template");
}
public sealed partial class HtmlSelectedContentDomElement(DomElementMapping m) :
	HtmlDomElementDefinition(
		m, "selectedcontent", ElementCategory.Text, ElementVisualKind.TextContent,
		ElementContentModel.None, ElementClosure.ClosedLeaf, ElementSyntax.Normal,
		XamlConversionSupport.Composite, XamlControlFamily.Text,
		ElementDefaultDisplay.Inline, ElementInteractionKind.None,
		ElementXamlChildPlacementKind.None)
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(XamlElementObjectType.TextBlock, XamlElementMappingKind.InlineFlow, false, "The selectedcontent element mirrors the active option text.");
}

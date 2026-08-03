namespace Iwesun.Runtime.Web;

public sealed partial class HtmlRootDomElement(DomElementMapping m) :
	HtmlSectioningDomElementDefinition(m, "html", ElementCategory.Document)
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(
			XamlElementObjectType.HtmlCssBoxGrid,
			XamlElementMappingKind.ViewportRoot,
			RequiresRuntimeLayoutContract: true,
			"The html element owns its document viewport.");
}
public sealed partial class HtmlHeadDomElement(DomElementMapping m) :
	HtmlDomElementDefinition(
		m, "head", ElementCategory.Metadata, ElementVisualKind.NonVisual,
		ElementContentModel.Transparent, ElementClosure.OpenContainer,
		ElementSyntax.Normal, XamlConversionSupport.NonVisual,
		XamlControlFamily.None, ElementDefaultDisplay.None,
		ElementInteractionKind.None, ElementXamlChildPlacementKind.None)
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(null, XamlElementMappingKind.TypeDefault, false, "The head element is a nonvisual metadata container.");
}
public sealed partial class HtmlTitleDomElement(DomElementMapping m) : HtmlMetadataDomElementDefinition(m, "title", ElementSyntax.RawText)
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(null, XamlElementMappingKind.TypeDefault, false, "The title element supplies document metadata and has no WinUI visual.");
}
public sealed partial class HtmlBaseDomElement(DomElementMapping m) : HtmlMetadataDomElementDefinition(m, "base", ElementSyntax.Void)
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(null, XamlElementMappingKind.TypeDefault, false, "The base element changes URL resolution and has no WinUI visual.");
	[HtmlElementProperty] public DomElementStringProperty Href { get; } = Attribute("href", "base");
	[HtmlElementProperty] public DomElementStringProperty Target { get; } = Attribute("target", "base");
}
public sealed partial class HtmlHeaderDomElement(DomElementMapping m) : HtmlSectioningDomElementDefinition(m, "header")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		FormattingContext() switch
		{
			HtmlCssFormattingContext.FlexRow or HtmlCssFormattingContext.FlexColumn =>
				new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.FlexLayout, true, "The header element owns a flex formatting context."),
			HtmlCssFormattingContext.Grid =>
				new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.GridLayout, true, "The header element owns a grid formatting context."),
			_ => new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.BlockFlow, true, "The header element owns normal block flow.")
		};
}
public sealed partial class HtmlFooterDomElement(DomElementMapping m) : HtmlSectioningDomElementDefinition(m, "footer")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		FormattingContext() switch
		{
			HtmlCssFormattingContext.FlexRow or HtmlCssFormattingContext.FlexColumn =>
				new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.FlexLayout, true, "The footer element owns a flex formatting context."),
			HtmlCssFormattingContext.Grid =>
				new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.GridLayout, true, "The footer element owns a grid formatting context."),
			_ => new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.BlockFlow, true, "The footer element owns normal block flow.")
		};
}
public sealed partial class HtmlArticleDomElement(DomElementMapping m) : HtmlSectioningDomElementDefinition(m, "article")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		FormattingContext() switch
		{
			HtmlCssFormattingContext.FlexRow or HtmlCssFormattingContext.FlexColumn =>
				new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.FlexLayout, true, "The article element owns a flex formatting context."),
			HtmlCssFormattingContext.Grid =>
				new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.GridLayout, true, "The article element owns a grid formatting context."),
			_ => new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.BlockFlow, true, "The article element owns normal block flow.")
		};
}
public sealed partial class HtmlAddressDomElement(DomElementMapping m) : HtmlSectioningDomElementDefinition(m, "address")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		FormattingContext() switch
		{
			HtmlCssFormattingContext.FlexRow or HtmlCssFormattingContext.FlexColumn =>
				new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.FlexLayout, true, "The address element owns a flex formatting context."),
			HtmlCssFormattingContext.Grid =>
				new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.GridLayout, true, "The address element owns a grid formatting context."),
			_ => new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.BlockFlow, true, "The address element owns normal block flow.")
		};
}
public sealed partial class HtmlFigureDomElement(DomElementMapping m) : HtmlSectioningDomElementDefinition(m, "figure")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		FormattingContext() switch
		{
			HtmlCssFormattingContext.FlexRow or HtmlCssFormattingContext.FlexColumn =>
				new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.FlexLayout, true, "The figure element owns a flex formatting context."),
			HtmlCssFormattingContext.Grid =>
				new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.GridLayout, true, "The figure element owns a grid formatting context."),
			_ => new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.BlockFlow, true, "The figure element owns media and caption block flow.")
		};
}
public sealed partial class HtmlFigureCaptionDomElement(DomElementMapping m) : HtmlBlockTextDomElementDefinition(m, "figcaption")
{ protected override XamlElementMappingDecision CreateXaml() => Children.Count == 0 ? new(XamlElementObjectType.TextBlock, XamlElementMappingKind.BlockFlow, false, "The figcaption element is leaf caption text.") : new(XamlElementObjectType.HtmlInlineFlowPanel, XamlElementMappingKind.InlineFlow, true, "The figcaption element owns composite caption phrasing."); }
public sealed partial class HtmlParagraphDomElement(DomElementMapping m) : HtmlBlockTextDomElementDefinition(m, "p")
{ protected override XamlElementMappingDecision CreateXaml() => Children.Count == 0 ? new(XamlElementObjectType.TextBlock, XamlElementMappingKind.BlockFlow, false, "The p element is a leaf paragraph.") : new(XamlElementObjectType.HtmlInlineFlowPanel, XamlElementMappingKind.InlineFlow, true, "The p element owns composite paragraph phrasing."); }
public abstract class HtmlHeadingDomElementDefinition(
	DomElementMapping mapping,
	string tagName) :
	HtmlBlockTextDomElementDefinition(mapping, tagName)
{
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
		vertical = RuntimeValue("style.flexDirection")
			?.StartsWith("column", StringComparison.OrdinalIgnoreCase) == true;
		definitions = Children
			.Where(static child => child is not HtmlDomElementDefinition html
				|| ActivePosition(html) is not ("absolute" or "fixed"))
			.Select(static _ => new XamlGridTrackDefinition(
				"Auto",
				null,
				null))
			.ToArray();
		return definitions.Count > 0;
	}

	private static string? ActivePosition(HtmlDomElementDefinition element)
	{
		var global = element.HtmlRoot?.ResolveGlobalStyleValue(
				element,
				"style.position",
				DomPropertyDataSlot.Runtime)
			?? element.HtmlRoot?.ResolveGlobalStyleValue(
				element,
				"style.position",
				DomPropertyDataSlot.Initialization);
		if (!string.IsNullOrWhiteSpace(global))
			return global;
		var property = element.RuntimeProperties.FirstOrDefault(static item =>
			item.Name.Equals("style.position", StringComparison.Ordinal));
		return property?.SourceRuntime.IsSet == true
			? property.SourceRuntime.Value
			: property?.SourceInitialization.IsSet == true
				? property.SourceInitialization.Value
				: null;
	}
}
public sealed partial class HtmlHeading1DomElement(DomElementMapping m) : HtmlHeadingDomElementDefinition(m, "h1")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		FormattingContext() is HtmlCssFormattingContext.FlexRow or HtmlCssFormattingContext.FlexColumn
			? new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.FlexLayout, true, "The h1 element owns its runtime flex heading layout.")
			: Children.Count == 0
				? new(XamlElementObjectType.TextBlock, XamlElementMappingKind.BlockFlow, false, "The h1 element is leaf level-one heading text.")
				: new(XamlElementObjectType.HtmlInlineFlowPanel, XamlElementMappingKind.InlineFlow, true, "The h1 element owns composite level-one heading phrasing.");
}
public sealed partial class HtmlHeading2DomElement(DomElementMapping m) : HtmlHeadingDomElementDefinition(m, "h2")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		FormattingContext() is HtmlCssFormattingContext.FlexRow or HtmlCssFormattingContext.FlexColumn
			? new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.FlexLayout, true, "The h2 element owns its runtime flex heading layout.")
			: Children.Count == 0
				? new(XamlElementObjectType.TextBlock, XamlElementMappingKind.BlockFlow, false, "The h2 element is leaf level-two heading text.")
				: new(XamlElementObjectType.HtmlInlineFlowPanel, XamlElementMappingKind.InlineFlow, true, "The h2 element owns composite level-two heading phrasing.");
}
public sealed partial class HtmlHeading3DomElement(DomElementMapping m) : HtmlHeadingDomElementDefinition(m, "h3")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		FormattingContext() is HtmlCssFormattingContext.FlexRow or HtmlCssFormattingContext.FlexColumn
			? new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.FlexLayout, true, "The h3 element owns its runtime flex heading layout.")
			: Children.Count == 0
				? new(XamlElementObjectType.TextBlock, XamlElementMappingKind.BlockFlow, false, "The h3 element is leaf level-three heading text.")
				: new(XamlElementObjectType.HtmlInlineFlowPanel, XamlElementMappingKind.InlineFlow, true, "The h3 element owns composite level-three heading phrasing.");
}
public sealed partial class HtmlHeading4DomElement(DomElementMapping m) : HtmlHeadingDomElementDefinition(m, "h4")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		FormattingContext() is HtmlCssFormattingContext.FlexRow or HtmlCssFormattingContext.FlexColumn
			? new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.FlexLayout, true, "The h4 element owns its runtime flex heading layout.")
			: Children.Count == 0
				? new(XamlElementObjectType.TextBlock, XamlElementMappingKind.BlockFlow, false, "The h4 element is leaf level-four heading text.")
				: new(XamlElementObjectType.HtmlInlineFlowPanel, XamlElementMappingKind.InlineFlow, true, "The h4 element owns composite level-four heading phrasing.");
}
public sealed partial class HtmlHeading5DomElement(DomElementMapping m) : HtmlHeadingDomElementDefinition(m, "h5")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		FormattingContext() is HtmlCssFormattingContext.FlexRow or HtmlCssFormattingContext.FlexColumn
			? new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.FlexLayout, true, "The h5 element owns its runtime flex heading layout.")
			: Children.Count == 0
				? new(XamlElementObjectType.TextBlock, XamlElementMappingKind.BlockFlow, false, "The h5 element is leaf level-five heading text.")
				: new(XamlElementObjectType.HtmlInlineFlowPanel, XamlElementMappingKind.InlineFlow, true, "The h5 element owns composite level-five heading phrasing.");
}
public sealed partial class HtmlHeading6DomElement(DomElementMapping m) : HtmlHeadingDomElementDefinition(m, "h6")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		FormattingContext() is HtmlCssFormattingContext.FlexRow or HtmlCssFormattingContext.FlexColumn
			? new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.FlexLayout, true, "The h6 element owns its runtime flex heading layout.")
			: Children.Count == 0
				? new(XamlElementObjectType.TextBlock, XamlElementMappingKind.BlockFlow, false, "The h6 element is leaf level-six heading text.")
				: new(XamlElementObjectType.HtmlInlineFlowPanel, XamlElementMappingKind.InlineFlow, true, "The h6 element owns composite level-six heading phrasing.");
}
public sealed partial class HtmlHeadingGroupDomElement(DomElementMapping m) : HtmlSectioningDomElementDefinition(m, "hgroup")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		FormattingContext() switch
		{
			HtmlCssFormattingContext.FlexRow or HtmlCssFormattingContext.FlexColumn =>
				new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.FlexLayout, true, "The hgroup element owns a flex formatting context."),
			HtmlCssFormattingContext.Grid =>
				new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.GridLayout, true, "The hgroup element owns a grid formatting context."),
			_ => new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.BlockFlow, true, "The hgroup element owns heading block flow.")
		};
}
public sealed partial class HtmlSpanDomElement(DomElementMapping m) :
	HtmlPhrasingDomElementDefinition(m, "span")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		FormattingContext() switch
		{
			HtmlCssFormattingContext.FlexRow
				or HtmlCssFormattingContext.FlexColumn => new(
					XamlElementObjectType.HtmlCssBoxGrid,
					XamlElementMappingKind.FlexLayout,
					true,
					"The span element owns a runtime flex formatting context."),
			HtmlCssFormattingContext.Grid => new(
				XamlElementObjectType.HtmlCssBoxGrid,
				XamlElementMappingKind.GridLayout,
				true,
				"The span element owns a runtime grid formatting context."),
			HtmlCssFormattingContext.BlockFlow => new(
				XamlElementObjectType.HtmlCssBoxGrid,
				XamlElementMappingKind.BlockFlow,
				true,
				"The span element establishes a runtime block box."),
			_ => CreateInlineXaml()
		};

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
		vertical = ActiveRuntimeValue(this, "style.flexDirection")
			?.StartsWith("column", StringComparison.OrdinalIgnoreCase) == true;
		var verticalAxis = vertical;
		var tracks = Children
			.Where(static child =>
				ActiveRuntimeValue(child, "style.position")
					is not ("absolute" or "fixed"))
			.Select(child => BuildSpanFlexTrack(child, verticalAxis))
			.ToList();
		InsertOwnTextTrack(tracks);
		definitions = tracks;
		return definitions.Count > 0;
	}

	protected override bool TryBuildXamlBlockLayout(
		out IReadOnlyList<XamlGridTrackDefinition> definitions)
	{
		if (CreateXaml().Kind != XamlElementMappingKind.BlockFlow)
		{
			definitions = [];
			return false;
		}
		var tracks = Children
			.Where(static child =>
				ActiveRuntimeValue(child, "style.position")
					is not ("absolute" or "fixed"))
			.Select(static _ => new XamlGridTrackDefinition(
				"Auto",
				null,
				null))
			.ToList();
		InsertOwnTextTrack(tracks);
		definitions = tracks;
		return definitions.Count > 0;
	}

	private void InsertOwnTextTrack(List<XamlGridTrackDefinition> tracks)
	{
		if (string.IsNullOrWhiteSpace(RuntimeInitialization("content.ownText")))
			return;
		var sourceInsertionIndex = Math.Clamp(
			CapturedOwnTextElementInsertionIndex,
			0,
			Children.Count);
		var normalFlowInsertionIndex = Children
			.Take(sourceInsertionIndex)
			.Count(static child =>
				ActiveRuntimeValue(child, "style.position")
					is not ("absolute" or "fixed"));
		tracks.Insert(
			Math.Clamp(normalFlowInsertionIndex, 0, tracks.Count),
			new("Auto", null, null));
	}

	private static XamlGridTrackDefinition BuildSpanFlexTrack(
		DomElement child,
		bool vertical)
	{
		if (child is not HtmlDomElementDefinition html)
			return new("Auto", null, null);
		var properties = new[]
		{
			"style.flexGrow",
			"style.width",
			"style.height"
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
		return new(length, null, null);
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

	private XamlElementMappingDecision CreateInlineXaml() =>
		Children.Count == 0
			&& RuntimeValue("style.position") is "absolute" or "fixed"
			&& RuntimeValue("style.height") is "0" or "0px"
			? new(
				XamlElementObjectType.HtmlSpanBoxControl,
				XamlElementMappingKind.PositionedLayout,
				true,
				"The positioned leaf span requires an explicit CSS box "
					+ "separate from its text line box.")
			: Children.Count == 0
			? new(
				XamlElementObjectType.TextBlock,
				XamlElementMappingKind.InlineFlow,
				false,
				"The span element preserves leaf inline phrasing content.")
			: new(
				XamlElementObjectType.HtmlInlineFlowPanel,
				XamlElementMappingKind.InlineFlow,
				true,
				"The span element owns wrapped mixed phrasing children.");
}
public sealed partial class HtmlStrongDomElement(DomElementMapping m) : HtmlPhrasingDomElementDefinition(m, "strong")
{ protected override XamlElementMappingDecision CreateXaml() => Children.Count == 0 ? new(XamlElementObjectType.TextBlock, XamlElementMappingKind.InlineFlow, false, "The strong element is leaf strong-importance text.") : new(XamlElementObjectType.HtmlInlineFlowPanel, XamlElementMappingKind.InlineFlow, true, "The strong element owns composite strong-importance phrasing."); }
public sealed partial class HtmlEmphasisDomElement(DomElementMapping m) : HtmlPhrasingDomElementDefinition(m, "em")
{ protected override XamlElementMappingDecision CreateXaml() => Children.Count == 0 ? new(XamlElementObjectType.TextBlock, XamlElementMappingKind.InlineFlow, false, "The em element is leaf emphasized text.") : new(XamlElementObjectType.HtmlInlineFlowPanel, XamlElementMappingKind.InlineFlow, true, "The em element owns composite emphasized phrasing."); }
public sealed partial class HtmlBoldDomElement(DomElementMapping m) : HtmlPhrasingDomElementDefinition(m, "b")
{ protected override XamlElementMappingDecision CreateXaml() => Children.Count == 0 ? new(XamlElementObjectType.TextBlock, XamlElementMappingKind.InlineFlow, false, "The b element is leaf bold text.") : new(XamlElementObjectType.HtmlInlineFlowPanel, XamlElementMappingKind.InlineFlow, true, "The b element owns composite bold phrasing."); }
public sealed partial class HtmlItalicDomElement(DomElementMapping m) : HtmlPhrasingDomElementDefinition(m, "i")
{ protected override XamlElementMappingDecision CreateXaml() => Children.Count == 0 ? new(XamlElementObjectType.TextBlock, XamlElementMappingKind.InlineFlow, false, "The i element is leaf alternate-voice text.") : new(XamlElementObjectType.HtmlInlineFlowPanel, XamlElementMappingKind.InlineFlow, true, "The i element owns composite alternate-voice phrasing."); }
public sealed partial class HtmlUnderlineDomElement(DomElementMapping m) : HtmlPhrasingDomElementDefinition(m, "u")
{ protected override XamlElementMappingDecision CreateXaml() => Children.Count == 0 ? new(XamlElementObjectType.TextBlock, XamlElementMappingKind.InlineFlow, false, "The u element is leaf annotated text.") : new(XamlElementObjectType.HtmlInlineFlowPanel, XamlElementMappingKind.InlineFlow, true, "The u element owns composite annotated phrasing."); }
public sealed partial class HtmlStrikeDomElement(DomElementMapping m) : HtmlPhrasingDomElementDefinition(m, "s")
{ protected override XamlElementMappingDecision CreateXaml() => Children.Count == 0 ? new(XamlElementObjectType.TextBlock, XamlElementMappingKind.InlineFlow, false, "The s element is leaf struck text.") : new(XamlElementObjectType.HtmlInlineFlowPanel, XamlElementMappingKind.InlineFlow, true, "The s element owns composite struck phrasing."); }
public sealed partial class HtmlSmallDomElement(DomElementMapping m) : HtmlPhrasingDomElementDefinition(m, "small")
{ protected override XamlElementMappingDecision CreateXaml() => Children.Count == 0 ? new(XamlElementObjectType.TextBlock, XamlElementMappingKind.InlineFlow, false, "The small element is leaf side-comment text.") : new(XamlElementObjectType.HtmlInlineFlowPanel, XamlElementMappingKind.InlineFlow, true, "The small element owns composite side-comment phrasing."); }
public sealed partial class HtmlCodeDomElement(DomElementMapping m) : HtmlPhrasingDomElementDefinition(m, "code")
{ protected override XamlElementMappingDecision CreateXaml() => Children.Count == 0 ? new(XamlElementObjectType.TextBlock, XamlElementMappingKind.InlineFlow, false, "The code element is leaf code text.") : new(XamlElementObjectType.HtmlInlineFlowPanel, XamlElementMappingKind.InlineFlow, true, "The code element owns composite code phrasing."); }
public sealed partial class HtmlPreformattedDomElement(DomElementMapping m) : HtmlBlockTextDomElementDefinition(m, "pre")
{ protected override XamlElementMappingDecision CreateXaml() => Children.Count == 0 ? new(XamlElementObjectType.TextBlock, XamlElementMappingKind.BlockFlow, false, "The pre element is leaf preformatted text.") : new(XamlElementObjectType.HtmlInlineFlowPanel, XamlElementMappingKind.InlineFlow, true, "The pre element owns composite preformatted phrasing."); }
public sealed partial class HtmlBlockQuoteDomElement(DomElementMapping m) :
	HtmlSectioningQuoteDomElementDefinition(m, "blockquote")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		FormattingContext() switch
		{
			HtmlCssFormattingContext.FlexRow or HtmlCssFormattingContext.FlexColumn =>
				new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.FlexLayout, true, "The blockquote element owns a flex formatting context."),
			HtmlCssFormattingContext.Grid =>
				new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.GridLayout, true, "The blockquote element owns a grid formatting context."),
			_ => new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.BlockFlow, true, "The blockquote element owns quoted block flow.")
		};
}
public sealed partial class HtmlBreakDomElement(DomElementMapping m) : HtmlVoidTextDomElementDefinition(m, "br")
{ protected override XamlElementMappingDecision CreateXaml() => new(XamlElementObjectType.HtmlLineBreak, XamlElementMappingKind.InlineFlow, false, "The br element forces an inline line break."); }
public sealed partial class HtmlWordBreakDomElement(DomElementMapping m) : HtmlVoidTextDomElementDefinition(m, "wbr")
{ protected override XamlElementMappingDecision CreateXaml() => new(XamlElementObjectType.HtmlWordBreakOpportunity, XamlElementMappingKind.InlineFlow, false, "The wbr element marks an optional inline break opportunity."); }
public sealed partial class HtmlAbbreviationDomElement(DomElementMapping m) : HtmlPhrasingDomElementDefinition(m, "abbr")
{ protected override XamlElementMappingDecision CreateXaml() => Children.Count == 0 ? new(XamlElementObjectType.TextBlock, XamlElementMappingKind.InlineFlow, false, "The abbr element is leaf abbreviation text.") : new(XamlElementObjectType.HtmlInlineFlowPanel, XamlElementMappingKind.InlineFlow, true, "The abbr element owns composite abbreviation phrasing."); }
public sealed partial class HtmlBidirectionalIsolateDomElement(DomElementMapping m) : HtmlPhrasingDomElementDefinition(m, "bdi")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		Children.Count == 0
			? new(
				XamlElementObjectType.HtmlBidiIsolationTextBlock,
				XamlElementMappingKind.InlineFlow,
				false,
				"The bdi leaf owns a Unicode bidi-isolate boundary.")
			: new(
				XamlElementObjectType.HtmlBidiIsolationPanel,
				XamlElementMappingKind.InlineFlow,
				true,
				"The bdi composite owns a distinct bidi-isolate layout scope.");
}
public sealed partial class HtmlBidirectionalOverrideDomElement(DomElementMapping m) : HtmlPhrasingDomElementDefinition(m, "bdo")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		Children.Count == 0
			? new(
				XamlElementObjectType.HtmlBidiOverrideTextBlock,
				XamlElementMappingKind.InlineFlow,
				false,
				"The bdo leaf applies a directional override boundary.")
			: new(
				XamlElementObjectType.HtmlBidiOverridePanel,
				XamlElementMappingKind.InlineFlow,
				true,
				"The bdo composite owns a directional override layout scope.");
}
public sealed partial class HtmlCitationDomElement(DomElementMapping m) : HtmlPhrasingDomElementDefinition(m, "cite")
{ protected override XamlElementMappingDecision CreateXaml() => Children.Count == 0 ? new(XamlElementObjectType.TextBlock, XamlElementMappingKind.InlineFlow, false, "The cite element is leaf citation text.") : new(XamlElementObjectType.HtmlInlineFlowPanel, XamlElementMappingKind.InlineFlow, true, "The cite element owns composite citation phrasing."); }
public sealed partial class HtmlDataDomElement(DomElementMapping m) : HtmlPhrasingDomElementDefinition(m, "data")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		Children.Count == 0
			? new(XamlElementObjectType.TextBlock, XamlElementMappingKind.InlineFlow, false, "The data element is leaf machine-valued text.")
			: new(XamlElementObjectType.HtmlInlineFlowPanel, XamlElementMappingKind.InlineFlow, true, "The data element owns machine-valued composite phrasing.");
	[HtmlElementProperty] public DomElementStringProperty Value { get; } = Attribute("value", "data");
	protected override IReadOnlyDictionary<string, HtmlElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleXamlAttributes((nameof(Value), HtmlElementAttributeXamlHandling.RuntimeDataSource));
}
public sealed partial class HtmlDeletedTextDomElement(DomElementMapping m) :
	HtmlModificationDomElementDefinition(m, "del")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		Children.Count == 0
			? new(XamlElementObjectType.TextBlock, XamlElementMappingKind.InlineFlow, false, "The del element is leaf deleted text.")
			: new(XamlElementObjectType.HtmlInlineFlowPanel, XamlElementMappingKind.InlineFlow, true, "The del element owns composite deleted phrasing.");
	protected override IReadOnlyDictionary<string, HtmlElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleXamlAttributes((nameof(DateTime), HtmlElementAttributeXamlHandling.RuntimeDataSource));
}
public sealed partial class HtmlDefinitionDomElement(DomElementMapping m) : HtmlPhrasingDomElementDefinition(m, "dfn")
{ protected override XamlElementMappingDecision CreateXaml() => Children.Count == 0 ? new(XamlElementObjectType.TextBlock, XamlElementMappingKind.InlineFlow, false, "The dfn element is a leaf defining instance.") : new(XamlElementObjectType.HtmlInlineFlowPanel, XamlElementMappingKind.InlineFlow, true, "The dfn element owns a composite defining instance."); }
public sealed partial class HtmlInsertedTextDomElement(DomElementMapping m) :
	HtmlModificationDomElementDefinition(m, "ins")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		Children.Count == 0
			? new(XamlElementObjectType.TextBlock, XamlElementMappingKind.InlineFlow, false, "The ins element is leaf inserted text.")
			: new(XamlElementObjectType.HtmlInlineFlowPanel, XamlElementMappingKind.InlineFlow, true, "The ins element owns composite inserted phrasing.");
	protected override IReadOnlyDictionary<string, HtmlElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleXamlAttributes((nameof(DateTime), HtmlElementAttributeXamlHandling.RuntimeDataSource));
}
public sealed partial class HtmlKeyboardDomElement(DomElementMapping m) : HtmlPhrasingDomElementDefinition(m, "kbd")
{ protected override XamlElementMappingDecision CreateXaml() => Children.Count == 0 ? new(XamlElementObjectType.TextBlock, XamlElementMappingKind.InlineFlow, false, "The kbd element is leaf user-input text.") : new(XamlElementObjectType.HtmlInlineFlowPanel, XamlElementMappingKind.InlineFlow, true, "The kbd element owns composite user-input phrasing."); }
public sealed partial class HtmlMarkDomElement(DomElementMapping m) : HtmlPhrasingDomElementDefinition(m, "mark")
{ protected override XamlElementMappingDecision CreateXaml() => Children.Count == 0 ? new(XamlElementObjectType.TextBlock, XamlElementMappingKind.InlineFlow, false, "The mark element is leaf highlighted text.") : new(XamlElementObjectType.HtmlInlineFlowPanel, XamlElementMappingKind.InlineFlow, true, "The mark element owns composite highlighted phrasing."); }
public sealed partial class HtmlQuoteDomElement(DomElementMapping m) :
	HtmlPhrasingQuoteDomElementDefinition(m, "q")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		Children.Count == 0
			? new(XamlElementObjectType.TextBlock, XamlElementMappingKind.InlineFlow, false, "The q element is a leaf inline quotation.")
			: new(XamlElementObjectType.HtmlInlineFlowPanel, XamlElementMappingKind.InlineFlow, true, "The q element owns composite quotation phrasing.");

	protected override IReadOnlyList<GeneratedXamlAttribute> BuildXamlAttributes()
	{
		var attributes = base.BuildXamlAttributes()
			.ToDictionary(static attribute => attribute.Name, StringComparer.Ordinal);
		if (attributes.TryGetValue("Text", out var text))
		{
			attributes["Text"] = text with
			{
				Value = $"“{text.Value}”"
			};
		}
		return attributes.Values.ToArray();
	}
}
public sealed partial class HtmlRubyDomElement(DomElementMapping m) : HtmlPhrasingDomElementDefinition(m, "ruby")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		Children.Count == 0
			? new(XamlElementObjectType.TextBlock, XamlElementMappingKind.InlineFlow, false, "The ruby element is leaf ruby text.")
			: new(XamlElementObjectType.HtmlRubyPanel, XamlElementMappingKind.InlineFlow, true, "The ruby element owns base text and annotation rows.");
	protected override XamlElementObjectProjectionDecision
		ResolveXamlObjectProjection() =>
		Children.Count == 0
			? new(
				CreateXaml(),
				ElementXamlChildPlacementKind.None,
				XamlElementContentProjectionKind.GeneratedContent)
			: new(
				CreateXaml(),
				ElementXamlChildPlacementKind.DirectChildren,
				XamlElementContentProjectionKind.Composite);
}
public sealed partial class HtmlRubyParenthesisDomElement(DomElementMapping m) : HtmlPhrasingDomElementDefinition(m, "rp")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		Children.Count == 0
			? new(XamlElementObjectType.TextBlock, XamlElementMappingKind.InlineFlow, false, "The rp element is ruby fallback text outside a supported ruby container.")
			: new(XamlElementObjectType.HtmlInlineFlowPanel, XamlElementMappingKind.InlineFlow, true, "The rp element owns composite fallback phrasing outside a supported ruby container.");
	protected override bool HasXamlOutput() =>
		Parent is not HtmlRubyDomElement;
	protected internal override IReadOnlyList<XamlElementObjectPlan>
		BuildXamlObjectPlans() =>
		Parent is HtmlRubyDomElement
			? []
			: base.BuildXamlObjectPlans();
}
public sealed partial class HtmlRubyTextDomElement(DomElementMapping m) : HtmlPhrasingDomElementDefinition(m, "rt")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		Children.Count == 0
			? new(XamlElementObjectType.HtmlRubyAnnotationTextBlock, XamlElementMappingKind.InlineFlow, false, "The rt element is leaf ruby annotation text.")
			: new(XamlElementObjectType.HtmlRubyAnnotationPanel, XamlElementMappingKind.InlineFlow, true, "The rt element owns composite ruby annotation phrasing.");
	protected override XamlElementObjectProjectionDecision
		ResolveXamlObjectProjection() =>
		Children.Count == 0
			? new(
				CreateXaml(),
				ElementXamlChildPlacementKind.None,
				XamlElementContentProjectionKind.GeneratedContent)
			: new(
				CreateXaml(),
				ElementXamlChildPlacementKind.DirectChildren,
				XamlElementContentProjectionKind.Composite);
}
public sealed partial class HtmlSampleDomElement(DomElementMapping m) : HtmlPhrasingDomElementDefinition(m, "samp")
{ protected override XamlElementMappingDecision CreateXaml() => Children.Count == 0 ? new(XamlElementObjectType.TextBlock, XamlElementMappingKind.InlineFlow, false, "The samp element is leaf sample output text.") : new(XamlElementObjectType.HtmlInlineFlowPanel, XamlElementMappingKind.InlineFlow, true, "The samp element owns composite sample output phrasing."); }
public sealed partial class HtmlSubscriptDomElement(DomElementMapping m) : HtmlPhrasingDomElementDefinition(m, "sub")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		Children.Count == 0
			? new(XamlElementObjectType.HtmlSubscriptTextBlock, XamlElementMappingKind.InlineFlow, false, "The sub element is baseline-shifted leaf text.")
			: new(XamlElementObjectType.HtmlSubscriptPanel, XamlElementMappingKind.InlineFlow, true, "The sub element owns baseline-shifted composite phrasing.");
	protected override XamlElementObjectProjectionDecision
		ResolveXamlObjectProjection() =>
		Children.Count == 0
			? new(
				CreateXaml(),
				ElementXamlChildPlacementKind.None,
				XamlElementContentProjectionKind.GeneratedContent)
			: new(
				CreateXaml(),
				ElementXamlChildPlacementKind.DirectChildren,
				XamlElementContentProjectionKind.Composite);
}
public sealed partial class HtmlSuperscriptDomElement(DomElementMapping m) : HtmlPhrasingDomElementDefinition(m, "sup")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		Children.Count == 0
			? new(XamlElementObjectType.HtmlSuperscriptTextBlock, XamlElementMappingKind.InlineFlow, false, "The sup element is baseline-shifted leaf text.")
			: new(XamlElementObjectType.HtmlSuperscriptPanel, XamlElementMappingKind.InlineFlow, true, "The sup element owns baseline-shifted composite phrasing.");
	protected override XamlElementObjectProjectionDecision
		ResolveXamlObjectProjection() =>
		Children.Count == 0
			? new(
				CreateXaml(),
				ElementXamlChildPlacementKind.None,
				XamlElementContentProjectionKind.GeneratedContent)
			: new(
				CreateXaml(),
				ElementXamlChildPlacementKind.DirectChildren,
				XamlElementContentProjectionKind.Composite);
}
public sealed partial class HtmlTimeDomElement(DomElementMapping m) : HtmlPhrasingDomElementDefinition(m, "time")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		Children.Count == 0
			? new(XamlElementObjectType.TextBlock, XamlElementMappingKind.InlineFlow, false, "The time element is leaf temporal text.")
			: new(XamlElementObjectType.HtmlInlineFlowPanel, XamlElementMappingKind.InlineFlow, true, "The time element owns composite temporal phrasing.");
	[HtmlElementProperty] public DomElementStringProperty DateTime { get; } = Attribute("datetime", "time");
	protected override IReadOnlyDictionary<string, HtmlElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleXamlAttributes((nameof(DateTime), HtmlElementAttributeXamlHandling.RuntimeDataSource));
}
public sealed partial class HtmlVariableDomElement(DomElementMapping m) : HtmlPhrasingDomElementDefinition(m, "var")
{ protected override XamlElementMappingDecision CreateXaml() => Children.Count == 0 ? new(XamlElementObjectType.TextBlock, XamlElementMappingKind.InlineFlow, false, "The var element is leaf variable-name text.") : new(XamlElementObjectType.HtmlInlineFlowPanel, XamlElementMappingKind.InlineFlow, true, "The var element owns composite variable-name phrasing."); }

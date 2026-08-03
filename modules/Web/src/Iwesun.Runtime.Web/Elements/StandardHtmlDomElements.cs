namespace Iwesun.Runtime.Web;

public sealed partial class HtmlBodyDomElement(DomElementMapping mapping) :
	HtmlSectioningDomElementDefinition(mapping, "body", ElementCategory.Document)
{
	protected override XamlElementMappingDecision CreateXaml()
	{
		return FormattingContext() switch
		{
			HtmlCssFormattingContext.FlexRow
				or HtmlCssFormattingContext.FlexColumn => new(
				XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.FlexLayout, true,
				"The body element explicitly owns a CSS flex formatting context."),
			HtmlCssFormattingContext.Grid => new(
				XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.GridLayout, true,
				"The body element explicitly owns a CSS grid formatting context."),
			_ => new(
				XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.BlockFlow, true,
				"The body element owns normal vertical document flow.")
		};
	}
}

public sealed partial class HtmlDivDomElement(DomElementMapping mapping) :
	HtmlContainerDomElementDefinition(mapping, "div")
{
	protected override XamlElementMappingDecision CreateXaml()
	{
		if (IsContentEditable())
		{
			return new(
				XamlElementObjectType.TextBox, XamlElementMappingKind.TypeDefault, false,
				"The div element declares the global contenteditable attribute and owns a text editor.");
		}
		if (HasRole("button"))
		{
			return new(
				XamlElementObjectType.Button, XamlElementMappingKind.TypeDefault, false,
				"The div element declares the ARIA role=\"button\" and owns a command surface.");
		}
		if (HasRole("separator"))
		{
			return new(
				XamlElementObjectType.Rectangle, XamlElementMappingKind.TypeDefault, false,
				"The div element declares the ARIA role=\"separator\" and owns thematic break geometry.");
		}
		return FormattingContext() switch
		{
			HtmlCssFormattingContext.FlexRow
				or HtmlCssFormattingContext.FlexColumn => new(
				XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.FlexLayout, true,
				"The div element explicitly owns a CSS flex formatting context."),
			HtmlCssFormattingContext.Grid => new(
				XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.GridLayout, true,
				"The div element explicitly owns a CSS grid formatting context."),
			HtmlCssFormattingContext.Table
				or HtmlCssFormattingContext.TableRowGroup
				or HtmlCssFormattingContext.TableRow
				or HtmlCssFormattingContext.TableCell => new(
					XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.TableLayout, true,
					"The div element explicitly owns a CSS table formatting context."),
			_ => new(
				XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.BlockFlow, true,
				"The div element preserves normal vertical block flow.")
		};
	}

	private bool HasRole(string role) =>
		SourceInitialization(Role)?.Equals(role, StringComparison.OrdinalIgnoreCase) == true;

	public string ResolveSeparatorFillColor() =>
		NormalizeXamlColor(RuntimeInitialization("style.backgroundColor")) ?? "#FF808080";

	private bool IsContentEditable() =>
		SourceInitialization(ContentEditable) is { } value
		&& !value.Equals("false", StringComparison.OrdinalIgnoreCase);

	protected override XamlElementObjectProjectionDecision ResolveXamlObjectProjection()
	{
		var mapping = CreateXaml();
		if (mapping.ObjectType == XamlElementObjectType.Button)
		{
			return new(
				mapping,
				ElementXamlChildPlacementKind.Content,
				XamlElementContentProjectionKind.Content);
		}
		if (mapping.ObjectType is XamlElementObjectType.Rectangle or XamlElementObjectType.TextBox)
		{
			return new(
				mapping,
				ElementXamlChildPlacementKind.None,
				XamlElementContentProjectionKind.None);
		}
		return base.ResolveXamlObjectProjection();
	}
}

public sealed partial class HtmlMainDomElement(DomElementMapping mapping) :
	HtmlSectioningDomElementDefinition(mapping, "main")
{
	protected override XamlElementMappingDecision CreateXaml()
	{
		return FormattingContext() switch
		{
			HtmlCssFormattingContext.FlexRow
				or HtmlCssFormattingContext.FlexColumn =>
				new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.FlexLayout, true,
					"The main element owns a CSS flex formatting context."),
			HtmlCssFormattingContext.Grid =>
				new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.GridLayout, true,
					"The main element owns a CSS grid formatting context."),
			_ => new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.BlockFlow, true,
				"The main landmark preserves normal block flow.")
		};
	}
}

public sealed partial class HtmlNavDomElement(DomElementMapping mapping) :
	HtmlSectioningDomElementDefinition(mapping, "nav")
{
	protected override XamlElementMappingDecision CreateXaml()
	{
		return FormattingContext() switch
		{
			HtmlCssFormattingContext.FlexRow
				or HtmlCssFormattingContext.FlexColumn =>
				new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.FlexLayout, true,
					"The nav element owns a CSS flex formatting context."),
			HtmlCssFormattingContext.Grid =>
				new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.GridLayout, true,
					"The nav element owns a CSS grid formatting context."),
			_ => new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.BlockFlow, true,
				"The nav landmark preserves normal block flow.")
		};
	}
}

public sealed partial class HtmlAsideDomElement(DomElementMapping mapping) :
	HtmlSectioningDomElementDefinition(mapping, "aside")
{
	protected override XamlElementMappingDecision CreateXaml()
	{
		return FormattingContext() switch
		{
			HtmlCssFormattingContext.FlexRow
				or HtmlCssFormattingContext.FlexColumn =>
				new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.FlexLayout, true,
					"The aside element owns a CSS flex formatting context."),
			HtmlCssFormattingContext.Grid =>
				new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.GridLayout, true,
					"The aside element owns a CSS grid formatting context."),
			_ => new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.BlockFlow, true,
				"The aside landmark preserves normal block flow.")
		};
	}
}

public sealed partial class HtmlSectionDomElement(DomElementMapping mapping) :
	HtmlSectioningDomElementDefinition(mapping, "section")
{
	protected override XamlElementMappingDecision CreateXaml()
	{
		return FormattingContext() switch
		{
			HtmlCssFormattingContext.FlexRow
				or HtmlCssFormattingContext.FlexColumn =>
				new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.FlexLayout, true,
					"The section element owns a CSS flex formatting context."),
			HtmlCssFormattingContext.Grid =>
				new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.GridLayout, true,
					"The section element owns a CSS grid formatting context."),
			_ => new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.BlockFlow, true,
				"The section element preserves normal block flow.")
		};
	}
}

public sealed partial class HtmlGenericDomElement(
	DomElementMapping mapping,
	string tagName) :
	HtmlContainerDomElementDefinition(mapping, tagName)
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(
			XamlElementObjectType.HtmlCssBoxGrid,
			XamlElementMappingKind.ConservativeContainer,
			RequiresRuntimeLayoutContract: true,
			$"The unknown HTML tag '{TagName}' is isolated in a conservative box.");
}

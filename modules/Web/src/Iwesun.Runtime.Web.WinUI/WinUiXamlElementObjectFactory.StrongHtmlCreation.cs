using Iwesun.Runtime.Web;

namespace Iwesun.Runtime.Web.WinUI;

public sealed partial class WinUiXamlElementObjectFactory
{
	[Obsolete(
		"Central DOM-type switching is forbidden; concrete element double dispatch "
			+ "must call its exact CreateStrong overload.",
		error: true)]
	private static Microsoft.UI.Xaml.DependencyObject CreateStrongHtmlElement(
		Iwesun.Runtime.Web.DomElement source,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan) =>
		throw new NotSupportedException(
			$"Central strong creation is disabled for {source.GetType().FullName}.");
	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlRootDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlCssBoxGrid => new HtmlCssBoxGrid(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlHeadDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		throw new System.InvalidOperationException("Nonvisual HTML element 'head' cannot create a WinUI object.");
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlTitleDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		throw new System.InvalidOperationException("Nonvisual HTML element 'title' cannot create a WinUI object.");
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlBaseDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		throw new System.InvalidOperationException("Nonvisual HTML element 'base' cannot create a WinUI object.");
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlBodyDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlCssBoxGrid => new HtmlCssBoxGrid(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlHeaderDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlCssBoxGrid => new HtmlCssBoxGrid(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlFooterDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlCssBoxGrid => new HtmlCssBoxGrid(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlMainDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlCssBoxGrid => new HtmlCssBoxGrid(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlNavDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlCssBoxGrid => new HtmlCssBoxGrid(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlSectionDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlCssBoxGrid => new HtmlCssBoxGrid(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlArticleDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlCssBoxGrid => new HtmlCssBoxGrid(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlAsideDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlCssBoxGrid => new HtmlCssBoxGrid(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlAddressDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlCssBoxGrid => new HtmlCssBoxGrid(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlFigureDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlCssBoxGrid => new HtmlCssBoxGrid(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlFigureCaptionDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlInlineFlowPanel => new HtmlInlineFlowPanel(),
			Iwesun.Runtime.Web.XamlElementObjectType.TextBlock => new Microsoft.UI.Xaml.Controls.TextBlock(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlDivDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlCssBoxGrid => new HtmlCssBoxGrid(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlParagraphDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlInlineFlowPanel => new HtmlInlineFlowPanel(),
			Iwesun.Runtime.Web.XamlElementObjectType.TextBlock => new Microsoft.UI.Xaml.Controls.TextBlock(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlHeading1DomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlCssBoxGrid => new HtmlCssBoxGrid(),
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlInlineFlowPanel => new HtmlInlineFlowPanel(),
			Iwesun.Runtime.Web.XamlElementObjectType.TextBlock => new Microsoft.UI.Xaml.Controls.TextBlock(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlHeading2DomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlCssBoxGrid => new HtmlCssBoxGrid(),
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlInlineFlowPanel => new HtmlInlineFlowPanel(),
			Iwesun.Runtime.Web.XamlElementObjectType.TextBlock => new Microsoft.UI.Xaml.Controls.TextBlock(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlHeading3DomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlCssBoxGrid => new HtmlCssBoxGrid(),
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlInlineFlowPanel => new HtmlInlineFlowPanel(),
			Iwesun.Runtime.Web.XamlElementObjectType.TextBlock => new Microsoft.UI.Xaml.Controls.TextBlock(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlHeading4DomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlCssBoxGrid => new HtmlCssBoxGrid(),
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlInlineFlowPanel => new HtmlInlineFlowPanel(),
			Iwesun.Runtime.Web.XamlElementObjectType.TextBlock => new Microsoft.UI.Xaml.Controls.TextBlock(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlHeading5DomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlCssBoxGrid => new HtmlCssBoxGrid(),
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlInlineFlowPanel => new HtmlInlineFlowPanel(),
			Iwesun.Runtime.Web.XamlElementObjectType.TextBlock => new Microsoft.UI.Xaml.Controls.TextBlock(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlHeading6DomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlCssBoxGrid => new HtmlCssBoxGrid(),
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlInlineFlowPanel => new HtmlInlineFlowPanel(),
			Iwesun.Runtime.Web.XamlElementObjectType.TextBlock => new Microsoft.UI.Xaml.Controls.TextBlock(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlHeadingGroupDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlCssBoxGrid => new HtmlCssBoxGrid(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlSpanDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlSpanBoxControl => new HtmlSpanBoxControl(),
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlCssBoxGrid => new HtmlCssBoxGrid(),
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlInlineFlowPanel => new HtmlInlineFlowPanel(),
			Iwesun.Runtime.Web.XamlElementObjectType.TextBlock => new Microsoft.UI.Xaml.Controls.TextBlock(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlStrongDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlInlineFlowPanel => new HtmlInlineFlowPanel(),
			Iwesun.Runtime.Web.XamlElementObjectType.TextBlock => new Microsoft.UI.Xaml.Controls.TextBlock(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlEmphasisDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlInlineFlowPanel => new HtmlInlineFlowPanel(),
			Iwesun.Runtime.Web.XamlElementObjectType.TextBlock => new Microsoft.UI.Xaml.Controls.TextBlock(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlBoldDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlInlineFlowPanel => new HtmlInlineFlowPanel(),
			Iwesun.Runtime.Web.XamlElementObjectType.TextBlock => new Microsoft.UI.Xaml.Controls.TextBlock(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlItalicDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlInlineFlowPanel => new HtmlInlineFlowPanel(),
			Iwesun.Runtime.Web.XamlElementObjectType.TextBlock => new Microsoft.UI.Xaml.Controls.TextBlock(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlUnderlineDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlInlineFlowPanel => new HtmlInlineFlowPanel(),
			Iwesun.Runtime.Web.XamlElementObjectType.TextBlock => new Microsoft.UI.Xaml.Controls.TextBlock(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlStrikeDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlInlineFlowPanel => new HtmlInlineFlowPanel(),
			Iwesun.Runtime.Web.XamlElementObjectType.TextBlock => new Microsoft.UI.Xaml.Controls.TextBlock(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlSmallDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlInlineFlowPanel => new HtmlInlineFlowPanel(),
			Iwesun.Runtime.Web.XamlElementObjectType.TextBlock => new Microsoft.UI.Xaml.Controls.TextBlock(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlCodeDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlInlineFlowPanel => new HtmlInlineFlowPanel(),
			Iwesun.Runtime.Web.XamlElementObjectType.TextBlock => new Microsoft.UI.Xaml.Controls.TextBlock(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlPreformattedDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlInlineFlowPanel => new HtmlInlineFlowPanel(),
			Iwesun.Runtime.Web.XamlElementObjectType.TextBlock => new Microsoft.UI.Xaml.Controls.TextBlock(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlBlockQuoteDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlCssBoxGrid => new HtmlCssBoxGrid(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlBreakDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlLineBreak => new HtmlLineBreakElement(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlWordBreakDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlWordBreakOpportunity => new HtmlWordBreakOpportunityElement(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlAbbreviationDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlInlineFlowPanel => new HtmlInlineFlowPanel(),
			Iwesun.Runtime.Web.XamlElementObjectType.TextBlock => new Microsoft.UI.Xaml.Controls.TextBlock(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlBidirectionalIsolateDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlBidiIsolationPanel => new HtmlBidiIsolationPanel(),
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlBidiIsolationTextBlock => new HtmlBidiIsolationTextBlock(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlBidirectionalOverrideDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlBidiOverridePanel => new HtmlBidiOverridePanel(),
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlBidiOverrideTextBlock => new HtmlBidiOverrideTextBlock(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlCitationDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlInlineFlowPanel => new HtmlInlineFlowPanel(),
			Iwesun.Runtime.Web.XamlElementObjectType.TextBlock => new Microsoft.UI.Xaml.Controls.TextBlock(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlDataDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlInlineFlowPanel => new HtmlInlineFlowPanel(),
			Iwesun.Runtime.Web.XamlElementObjectType.TextBlock => new Microsoft.UI.Xaml.Controls.TextBlock(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlDeletedTextDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlInlineFlowPanel => new HtmlInlineFlowPanel(),
			Iwesun.Runtime.Web.XamlElementObjectType.TextBlock => new Microsoft.UI.Xaml.Controls.TextBlock(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlDefinitionDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlInlineFlowPanel => new HtmlInlineFlowPanel(),
			Iwesun.Runtime.Web.XamlElementObjectType.TextBlock => new Microsoft.UI.Xaml.Controls.TextBlock(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlInsertedTextDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlInlineFlowPanel => new HtmlInlineFlowPanel(),
			Iwesun.Runtime.Web.XamlElementObjectType.TextBlock => new Microsoft.UI.Xaml.Controls.TextBlock(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlKeyboardDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlInlineFlowPanel => new HtmlInlineFlowPanel(),
			Iwesun.Runtime.Web.XamlElementObjectType.TextBlock => new Microsoft.UI.Xaml.Controls.TextBlock(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlMarkDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlInlineFlowPanel => new HtmlInlineFlowPanel(),
			Iwesun.Runtime.Web.XamlElementObjectType.TextBlock => new Microsoft.UI.Xaml.Controls.TextBlock(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlQuoteDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlInlineFlowPanel => new HtmlInlineFlowPanel(),
			Iwesun.Runtime.Web.XamlElementObjectType.TextBlock => new Microsoft.UI.Xaml.Controls.TextBlock(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlRubyDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlRubyPanel => new HtmlRubyPanel(),
			Iwesun.Runtime.Web.XamlElementObjectType.TextBlock => new Microsoft.UI.Xaml.Controls.TextBlock(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlRubyParenthesisDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlInlineFlowPanel => new HtmlInlineFlowPanel(),
			Iwesun.Runtime.Web.XamlElementObjectType.TextBlock => new Microsoft.UI.Xaml.Controls.TextBlock(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlRubyTextDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlRubyAnnotationPanel => new HtmlRubyAnnotationPanel(),
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlRubyAnnotationTextBlock => new HtmlRubyAnnotationTextBlock(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlSampleDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlInlineFlowPanel => new HtmlInlineFlowPanel(),
			Iwesun.Runtime.Web.XamlElementObjectType.TextBlock => new Microsoft.UI.Xaml.Controls.TextBlock(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlSubscriptDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlSubscriptPanel => new HtmlSubscriptPanel(),
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlSubscriptTextBlock => new HtmlSubscriptTextBlock(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlSuperscriptDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlSuperscriptPanel => new HtmlSuperscriptPanel(),
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlSuperscriptTextBlock => new HtmlSuperscriptTextBlock(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlTimeDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlInlineFlowPanel => new HtmlInlineFlowPanel(),
			Iwesun.Runtime.Web.XamlElementObjectType.TextBlock => new Microsoft.UI.Xaml.Controls.TextBlock(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlVariableDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlInlineFlowPanel => new HtmlInlineFlowPanel(),
			Iwesun.Runtime.Web.XamlElementObjectType.TextBlock => new Microsoft.UI.Xaml.Controls.TextBlock(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlAnchorDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlInlineFlowPanel => new HtmlInlineFlowPanel(),
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlInteractiveFlexPanel => new HtmlInteractiveFlexPanel(),
			Iwesun.Runtime.Web.XamlElementObjectType.HyperlinkButton => new HtmlCursorHyperlinkButton(),
			Iwesun.Runtime.Web.XamlElementObjectType.TextBlock => new Microsoft.UI.Xaml.Controls.TextBlock(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlUnorderedListDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.ListView => new Microsoft.UI.Xaml.Controls.ListView(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlOrderedListDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.ListView => new Microsoft.UI.Xaml.Controls.ListView(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlMenuDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.ListView => new Microsoft.UI.Xaml.Controls.ListView(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlListItemDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.ListViewItem => new Microsoft.UI.Xaml.Controls.ListViewItem(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlDescriptionListDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.StackPanel => new Microsoft.UI.Xaml.Controls.StackPanel(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlDescriptionTermDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlInlineFlowPanel => new HtmlInlineFlowPanel(),
			Iwesun.Runtime.Web.XamlElementObjectType.TextBlock => new Microsoft.UI.Xaml.Controls.TextBlock(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlDescriptionDetailsDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.StackPanel => new Microsoft.UI.Xaml.Controls.StackPanel(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlDetailsDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.Expander => new Microsoft.UI.Xaml.Controls.Expander(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlSummaryDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.Button => new HtmlCursorButton(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlDialogDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlDialogControl => new HtmlDialogControl(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlSearchDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlCssBoxGrid => new HtmlCssBoxGrid(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlSlotDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlCssBoxGrid => new HtmlCssBoxGrid(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlNoScriptDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		throw new System.InvalidOperationException("Nonvisual HTML element 'noscript' cannot create a WinUI object.");
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlScriptDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		throw new System.InvalidOperationException("Nonvisual HTML element 'script' cannot create a WinUI object.");
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlStyleDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		throw new System.InvalidOperationException("Nonvisual HTML element 'style' cannot create a WinUI object.");
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlLinkDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		throw new System.InvalidOperationException("Nonvisual HTML element 'link' cannot create a WinUI object.");
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlMetaDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		throw new System.InvalidOperationException("Nonvisual HTML element 'meta' cannot create a WinUI object.");
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlTemplateDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		throw new System.InvalidOperationException("Nonvisual HTML element 'template' cannot create a WinUI object.");
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlFormDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlCssBoxGrid => new HtmlCssBoxGrid(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlLabelDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlFormLabelPanel => new HtmlFormLabelPanel(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlButtonDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlFormButton => new HtmlFormButton(),
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlInteractiveFlexPanel => new HtmlInteractiveFlexPanel(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlInputDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.CheckBox => new Microsoft.UI.Xaml.Controls.CheckBox(),
			Iwesun.Runtime.Web.XamlElementObjectType.ColorPicker => new Microsoft.UI.Xaml.Controls.ColorPicker(),
			Iwesun.Runtime.Web.XamlElementObjectType.ContentControl => new Microsoft.UI.Xaml.Controls.ContentControl(),
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlDatalistInputControl => new HtmlDatalistInputControl(),
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlDateInputControl => new HtmlDateInputControl(),
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlDateTimeLocalInputControl => new HtmlDateTimeLocalInputControl(),
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlFileInputControl => new HtmlFileInputControl(),
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlFormButton => new HtmlFormButton(),
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlImageSubmitButton => new HtmlImageSubmitButton(),
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlMonthInputControl => new HtmlMonthInputControl(),
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlTimeInputControl => new HtmlTimeInputControl(),
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlWeekInputControl => new HtmlWeekInputControl(),
			Iwesun.Runtime.Web.XamlElementObjectType.NumberBox => new Microsoft.UI.Xaml.Controls.NumberBox(),
			Iwesun.Runtime.Web.XamlElementObjectType.PasswordBox => new HtmlCursorPasswordBoxHost(),
			Iwesun.Runtime.Web.XamlElementObjectType.RadioButton => new Microsoft.UI.Xaml.Controls.RadioButton(),
			Iwesun.Runtime.Web.XamlElementObjectType.Slider => new Microsoft.UI.Xaml.Controls.Slider(),
			Iwesun.Runtime.Web.XamlElementObjectType.TextBox => new HtmlCursorTextBox(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlTextAreaDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.TextBox => new HtmlCursorTextBox(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlSelectDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.ComboBox => new Microsoft.UI.Xaml.Controls.ComboBox(),
			Iwesun.Runtime.Web.XamlElementObjectType.ListView => new Microsoft.UI.Xaml.Controls.ListView(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlOptionDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.ComboBoxItem => new Microsoft.UI.Xaml.Controls.ComboBoxItem(),
			Iwesun.Runtime.Web.XamlElementObjectType.ListViewItem => new Microsoft.UI.Xaml.Controls.ListViewItem(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlFieldSetDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlFieldSetPanel => new HtmlFieldSetPanel(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlLegendDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlInlineFlowPanel => new HtmlInlineFlowPanel(),
			Iwesun.Runtime.Web.XamlElementObjectType.TextBlock => new Microsoft.UI.Xaml.Controls.TextBlock(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlDataListDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		throw new System.InvalidOperationException("Nonvisual HTML element 'datalist' cannot create a WinUI object.");
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlOptionGroupDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		throw new System.InvalidOperationException("Nonvisual HTML element 'optgroup' cannot create a WinUI object.");
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlOutputDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlInlineFlowPanel => new HtmlInlineFlowPanel(),
			Iwesun.Runtime.Web.XamlElementObjectType.TextBlock => new Microsoft.UI.Xaml.Controls.TextBlock(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlMeterDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlMeterControl => new HtmlMeterControl(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlProgressDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.ProgressBar => new Microsoft.UI.Xaml.Controls.ProgressBar(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlTableDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlTablePanel => new HtmlTablePanel(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlTableHeadDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlTableSectionPanel => new HtmlTableSectionPanel(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlTableBodyDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlTableSectionPanel => new HtmlTableSectionPanel(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlTableFootDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlTableSectionPanel => new HtmlTableSectionPanel(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlTableRowDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlTableRowPanel => new HtmlTableRowPanel(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlTableHeaderCellDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.Border => new Microsoft.UI.Xaml.Controls.Border(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlTableCellDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.Border => new Microsoft.UI.Xaml.Controls.Border(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlTableCaptionDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlInlineFlowPanel => new HtmlInlineFlowPanel(),
			Iwesun.Runtime.Web.XamlElementObjectType.TextBlock => new Microsoft.UI.Xaml.Controls.TextBlock(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlTableColumnGroupDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		throw new System.InvalidOperationException("Nonvisual HTML element 'colgroup' cannot create a WinUI object.");
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlTableColumnDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		throw new System.InvalidOperationException("Nonvisual HTML element 'col' cannot create a WinUI object.");
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlPictureDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.ContentControl => new Microsoft.UI.Xaml.Controls.ContentControl(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlSourceDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		throw new System.InvalidOperationException("Nonvisual HTML element 'source' cannot create a WinUI object.");
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlVideoDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlMediaElementControl => new HtmlMediaElementControl(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlAudioDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlMediaElementControl => new HtmlMediaElementControl(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlImageMapDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		throw new System.InvalidOperationException("Nonvisual HTML element 'map' cannot create a WinUI object.");
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlImageMapAreaDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		throw new System.InvalidOperationException("Nonvisual HTML element 'area' cannot create a WinUI object.");
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlTrackDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		throw new System.InvalidOperationException("Nonvisual HTML element 'track' cannot create a WinUI object.");
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlImageDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlImageMapComposite => new HtmlImageMapComposite(),
			Iwesun.Runtime.Web.XamlElementObjectType.Image => new HtmlImageView(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlIframeDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlEmbeddedDocumentHost => new HtmlEmbeddedDocumentHost(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlCanvasDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlCanvasSurface => new HtmlCanvasSurface(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlEmbedDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlEmbeddedContentHost => new HtmlEmbeddedContentHost(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlObjectDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.HtmlObjectContentHost => new HtmlObjectContentHost(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlHorizontalRuleDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.Rectangle => new Microsoft.UI.Xaml.Shapes.Rectangle(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static Microsoft.UI.Xaml.DependencyObject CreateStrong(
		Iwesun.Runtime.Web.HtmlSelectedContentDomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan)
	{
		System.ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			Iwesun.Runtime.Web.XamlElementObjectType.TextBlock => new Microsoft.UI.Xaml.Controls.TextBlock(),
			_ => throw StrongTypeMismatch(element, plan)
		};
	}

	private static System.Exception StrongTypeMismatch(
		Iwesun.Runtime.Web.DomElement element,
		Iwesun.Runtime.Web.XamlElementObjectPlan plan) =>
		new System.IO.InvalidDataException(
			$"{element.GetType().Name} cannot create {plan.Mapping.ObjectType?.ToString() ?? "<none>"}.");
}

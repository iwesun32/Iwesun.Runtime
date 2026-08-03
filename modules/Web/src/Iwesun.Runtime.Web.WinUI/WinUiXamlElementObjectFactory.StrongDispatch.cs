using Iwesun.Runtime.Web;
using Microsoft.UI.Xaml;

namespace Iwesun.Runtime.Web.WinUI;

public sealed partial class WinUiXamlElementObjectFactory
{
	public object CreateElement(HtmlGenericDomElement element, XamlElementObjectPlan plan)
	{
		ArgumentNullException.ThrowIfNull(element);
		var created = plan.Mapping.ObjectType == XamlElementObjectType.HtmlCssBoxGrid
			? new HtmlCssBoxGrid()
			: throw StrongTypeMismatch(element, plan);
		return FinalizeStrongElement(element, created);
	}

	public object CreateElement(HtmlAbbreviationDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlAddressDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlAnchorDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlArticleDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlAsideDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlAudioDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlBaseDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlBidirectionalIsolateDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlBidirectionalOverrideDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlBlockQuoteDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlBodyDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlBoldDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlBreakDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlButtonDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlCanvasDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlCitationDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlCodeDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlDataDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlDataListDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlDefinitionDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlDeletedTextDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlDescriptionDetailsDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlDescriptionListDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlDescriptionTermDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlDetailsDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlDialogDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlDivDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlEmbedDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlEmphasisDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlFieldSetDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlFigureCaptionDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlFigureDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlFooterDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlFormDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlHeadDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlHeaderDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlHeading1DomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlHeading2DomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlHeading3DomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlHeading4DomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlHeading5DomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlHeading6DomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlHeadingGroupDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlHorizontalRuleDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlIframeDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlImageDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlImageMapAreaDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlImageMapDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlInputDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlInsertedTextDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlItalicDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlKeyboardDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlLabelDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlLegendDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlLinkDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlListItemDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlMainDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlMarkDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlMenuDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlMetaDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlMeterDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlNavDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlNoScriptDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlObjectDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlOptionDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlOptionGroupDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlOrderedListDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlOutputDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlParagraphDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlPictureDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlPreformattedDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlProgressDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlQuoteDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlRootDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlRubyDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlRubyParenthesisDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlRubyTextDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlSampleDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlScriptDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlSearchDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlSectionDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlSelectDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlSelectedContentDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlSlotDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlSmallDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlSourceDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlSpanDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlStrikeDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlStrongDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlStyleDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlSubscriptDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlSummaryDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlSuperscriptDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlTableBodyDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlTableCaptionDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlTableCellDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlTableColumnDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlTableColumnGroupDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlTableDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlTableFootDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlTableHeadDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlTableHeaderCellDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlTableRowDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlTemplateDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlTextAreaDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlTimeDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlTitleDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlTrackDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlUnderlineDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlUnorderedListDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlVariableDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlVideoDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(HtmlWordBreakDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(SvgCircleDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(SvgClipPathDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(SvgDefinitionsDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(SvgEllipseDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(SvgGroupDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(SvgLineDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(SvgMaskDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(SvgPathDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(SvgPolygonDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(SvgPolylineDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(SvgRectangleDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(SvgRootDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(SvgTextDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	public object CreateElement(SvgUseDomElement element, XamlElementObjectPlan plan) =>
		FinalizeStrongElement(element, CreateStrong(element, plan));

	private static DependencyObject FinalizeStrongElement(
		DomElement source,
		DependencyObject created)
	{
		if (created is Microsoft.UI.Xaml.Controls.TextBlock
			&& IsVerticalWritingMode(source))
		{
			created = new HtmlVerticalTextControl();
		}
		if (created is FrameworkElement frameworkElement
			&& TryReadActiveString(source, "style.filter", out var filter)
			&& filter != "none"
			&& WinUiFilterBehavior.RequiresIsolation(filter))
		{
			return new HtmlFilteredElementHost(frameworkElement);
		}
		return created;
	}
}

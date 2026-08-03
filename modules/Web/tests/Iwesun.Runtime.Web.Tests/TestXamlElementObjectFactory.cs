namespace Iwesun.Runtime.Web;

internal abstract partial class TestXamlElementObjectFactory : IXamlElementObjectFactory
{
    protected abstract object CreateElementCore(XamlElementObjectPlan plan);

    public object CreateSyntheticElement(XamlElementObjectCreationContext context) =>
        CreateElementCore(context.Plan);

    public object CreateElement(HtmlAbbreviationDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlAddressDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlAnchorDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlArticleDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlAsideDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlAudioDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlBaseDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlBidirectionalIsolateDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlBidirectionalOverrideDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlBlockQuoteDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlBodyDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlBoldDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlBreakDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlButtonDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlCanvasDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlCitationDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlCodeDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlDataDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlDataListDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlDefinitionDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlDeletedTextDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlDescriptionDetailsDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlDescriptionListDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlDescriptionTermDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlDetailsDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlDialogDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlDivDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlEmbedDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlEmphasisDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlFieldSetDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlFigureCaptionDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlFigureDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlFooterDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlFormDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlGenericDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlHeadDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlHeaderDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlHeading1DomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlHeading2DomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlHeading3DomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlHeading4DomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlHeading5DomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlHeading6DomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlHeadingGroupDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlHorizontalRuleDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlIframeDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlImageDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlImageMapAreaDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlImageMapDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlInputDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlInsertedTextDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlItalicDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlKeyboardDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlLabelDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlLegendDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlLinkDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlListItemDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlMainDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlMarkDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlMenuDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlMetaDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlMeterDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlNavDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlNoScriptDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlObjectDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlOptionDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlOptionGroupDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlOrderedListDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlOutputDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlParagraphDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlPictureDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlPreformattedDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlProgressDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlQuoteDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlRootDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlRubyDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlRubyParenthesisDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlRubyTextDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlSampleDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlScriptDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlSearchDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlSectionDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlSelectDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlSelectedContentDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlSlotDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlSmallDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlSourceDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlSpanDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlStrikeDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlStrongDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlStyleDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlSubscriptDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlSummaryDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlSuperscriptDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlTableBodyDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlTableCaptionDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlTableCellDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlTableColumnDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlTableColumnGroupDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlTableDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlTableFootDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlTableHeadDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlTableHeaderCellDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlTableRowDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlTemplateDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlTextAreaDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlTimeDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlTitleDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlTrackDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlUnderlineDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlUnorderedListDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlVariableDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlVideoDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(HtmlWordBreakDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(SvgCircleDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(SvgClipPathDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(SvgDefinitionsDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(SvgEllipseDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(SvgGroupDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(SvgLineDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(SvgMaskDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(SvgPathDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(SvgPolygonDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(SvgPolylineDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(SvgRectangleDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(SvgRootDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(SvgTextDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public object CreateElement(SvgUseDomElement element, XamlElementObjectPlan plan) =>
        CreateElementCore(plan);

    public abstract void FillElementProperties(XamlElementObjectPropertyFillContext context);

    public abstract void AttachChild(XamlElementObjectAttachmentContext context);

    public abstract void ApplyGridTracks(
        object element,
        IReadOnlyList<XamlGridTrackDefinition> rowDefinitions,
        IReadOnlyList<XamlGridTrackDefinition> columnDefinitions);
}

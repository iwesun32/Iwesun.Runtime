namespace Iwesun.Runtime.Web;

// Compile-time data-source declarations. Each concrete HTML type owns its
// exact UI-resource/business-input contract; no tag-name lookup is used.

public sealed partial class HtmlAbbreviationDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlAddressDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlAnchorDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "href",
            ElementDataOrganizationSlotKind.NavigateUri,
            DomDataSourceKind.Url,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.NavigateUri),
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlArticleDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlAsideDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlAudioDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "src",
            ElementDataOrganizationSlotKind.Source,
            DomDataSourceKind.Url,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Source)
    ];
}

public sealed partial class HtmlBaseDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "href",
            ElementDataOrganizationSlotKind.NavigateUri,
            DomDataSourceKind.Url,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.NavigateUri)
    ];
}

public sealed partial class HtmlBidirectionalIsolateDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlBidirectionalOverrideDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlBlockQuoteDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "cite",
            ElementDataOrganizationSlotKind.NavigateUri,
            DomDataSourceKind.Url,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.NavigateUri),
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlBodyDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
        [];
}

public sealed partial class HtmlBoldDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlBreakDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
        [];
}

public sealed partial class HtmlButtonDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.value",
            ElementDataOrganizationSlotKind.Value,
            DomDataSourceKind.FormValue,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Value),
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.UiResource,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlCanvasDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
        [];
}

public sealed partial class HtmlCitationDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlCodeDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlDataDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlDataListDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
        [];
}

public sealed partial class HtmlDefinitionDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlDeletedTextDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "cite",
            ElementDataOrganizationSlotKind.NavigateUri,
            DomDataSourceKind.Url,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.NavigateUri),
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlDescriptionDetailsDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlDescriptionListDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
        [];
}

public sealed partial class HtmlDescriptionTermDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlDetailsDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlDialogDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
        [];
}

public sealed partial class HtmlDivDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlEmbedDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "src",
            ElementDataOrganizationSlotKind.Source,
            DomDataSourceKind.Url,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Source)
    ];
}

public sealed partial class HtmlEmphasisDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlFieldSetDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
        [];
}

public sealed partial class HtmlFigureCaptionDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlFigureDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
        [];
}

public sealed partial class HtmlFooterDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlFormDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "action",
            ElementDataOrganizationSlotKind.NavigateUri,
            DomDataSourceKind.Url,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.NavigateUri)
    ];
}

public sealed partial class HtmlHeadDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
        [];
}

public sealed partial class HtmlHeaderDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlHeading1DomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlHeading2DomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlHeading3DomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlHeading4DomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlHeading5DomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlHeading6DomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlHeadingGroupDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
        [];
}

public sealed partial class HtmlHorizontalRuleDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
        [];
}

public sealed partial class HtmlIframeDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "src",
            ElementDataOrganizationSlotKind.Source,
            DomDataSourceKind.Url,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Source)
    ];
}

public sealed partial class HtmlImageDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "src",
            ElementDataOrganizationSlotKind.Source,
            DomDataSourceKind.Url,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Source)
    ];
}

public sealed partial class HtmlImageMapAreaDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "href",
            ElementDataOrganizationSlotKind.NavigateUri,
            DomDataSourceKind.Url,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.NavigateUri)
    ];
}

public sealed partial class HtmlImageMapDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
        [];
}

public sealed partial class HtmlInputDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.value",
            ElementDataOrganizationSlotKind.Value,
            DomDataSourceKind.FormValue,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Value),
		new DomElementStringDataSource(
			"state.checked",
            ElementDataOrganizationSlotKind.IsChecked,
            DomDataSourceKind.CheckedState,
			DomDataSourceDomain.BusinessInput,
			XamlControlDataTargetKind.IsChecked),
		new DomElementStringDataSource(
			"state.indeterminate",
			ElementDataOrganizationSlotKind.Custom,
			DomDataSourceKind.Custom,
			DomDataSourceDomain.BusinessInput,
			XamlControlDataTargetKind.IsIndeterminate),
		new DomElementStringDataSource(
			"state.disabled",
			ElementDataOrganizationSlotKind.Custom,
			DomDataSourceKind.Custom,
			DomDataSourceDomain.BusinessInput,
			XamlControlDataTargetKind.IsEnabled),
		new DomElementStringDataSource(
			"state.readOnly",
			ElementDataOrganizationSlotKind.Custom,
			DomDataSourceKind.Custom,
			DomDataSourceDomain.BusinessInput,
			XamlControlDataTargetKind.IsReadOnly),
		new DomElementStringDataSource(
			"state.valid",
			ElementDataOrganizationSlotKind.Custom,
			DomDataSourceKind.Custom,
			DomDataSourceDomain.BusinessInput,
			XamlControlDataTargetKind.IsValid),
		new DomElementStringDataSource(
			"state.willValidate",
			ElementDataOrganizationSlotKind.Custom,
			DomDataSourceKind.Custom,
			DomDataSourceDomain.BusinessInput,
			XamlControlDataTargetKind.WillValidate),
		new DomElementStringDataSource(
			"state.validationMessage",
			ElementDataOrganizationSlotKind.Custom,
			DomDataSourceKind.Custom,
			DomDataSourceDomain.BusinessInput,
			XamlControlDataTargetKind.ValidationMessage),
		new DomElementStringDataSource(
			"state.tabIndex",
			ElementDataOrganizationSlotKind.Custom,
			DomDataSourceKind.Custom,
			DomDataSourceDomain.BusinessInput,
			XamlControlDataTargetKind.TabIndex),
        new DomElementStringDataSource(
            "placeholder",
            ElementDataOrganizationSlotKind.Placeholder,
            DomDataSourceKind.Attribute,
            DomDataSourceDomain.UiResource,
            XamlControlDataTargetKind.PlaceholderText)
    ];
}

public sealed partial class HtmlInsertedTextDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "cite",
            ElementDataOrganizationSlotKind.NavigateUri,
            DomDataSourceKind.Url,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.NavigateUri),
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlItalicDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlKeyboardDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlLabelDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.UiResource,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlLegendDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.UiResource,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlLinkDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "href",
            ElementDataOrganizationSlotKind.Source,
            DomDataSourceKind.Url,
            DomDataSourceDomain.UiResource,
            XamlControlDataTargetKind.Source)
    ];
}

public sealed partial class HtmlListItemDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlMainDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlMarkDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlMenuDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
        [];
}

public sealed partial class HtmlMetaDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
        [];
}

public sealed partial class HtmlMeterDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
        [];
}

public sealed partial class HtmlNavDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlNoScriptDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
        [];
}

public sealed partial class HtmlObjectDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "data",
            ElementDataOrganizationSlotKind.Source,
            DomDataSourceKind.Url,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Source)
    ];
}

public sealed partial class HtmlOptionDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.value",
            ElementDataOrganizationSlotKind.SelectedValue,
            DomDataSourceKind.SelectedValue,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.SelectedValue)
    ];
}

public sealed partial class HtmlOptionGroupDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
        [];
}

public sealed partial class HtmlOrderedListDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
        [];
}

public sealed partial class HtmlOutputDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
        [];
}

public sealed partial class HtmlParagraphDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlPictureDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
        [];
}

public sealed partial class HtmlPreformattedDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlProgressDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
        [];
}

public sealed partial class HtmlQuoteDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "cite",
            ElementDataOrganizationSlotKind.NavigateUri,
            DomDataSourceKind.Url,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.NavigateUri),
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlRootDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
        [];
}

public sealed partial class HtmlRubyDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
        [];
}

public sealed partial class HtmlRubyParenthesisDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
        [];
}

public sealed partial class HtmlRubyTextDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
        [];
}

public sealed partial class HtmlSampleDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlScriptDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "src",
            ElementDataOrganizationSlotKind.Source,
            DomDataSourceKind.Url,
            DomDataSourceDomain.UiResource,
            XamlControlDataTargetKind.Source)
    ];
}

public sealed partial class HtmlSearchDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
        [];
}

public sealed partial class HtmlSectionDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlSelectDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.value",
            ElementDataOrganizationSlotKind.SelectedValue,
            DomDataSourceKind.SelectedValue,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.SelectedValue)
    ];
}

public sealed partial class HtmlSelectedContentDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
        [];
}

public sealed partial class HtmlSlotDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
        [];
}

public sealed partial class HtmlSmallDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlSourceDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "src",
            ElementDataOrganizationSlotKind.Source,
            DomDataSourceKind.Url,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Source)
    ];
}

public sealed partial class HtmlSpanDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlStrikeDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlStrongDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlStyleDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
        [];
}

public sealed partial class HtmlSubscriptDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlSummaryDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlSuperscriptDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlTableBodyDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
        [];
}

public sealed partial class HtmlTableCaptionDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlTableCellDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlTableColumnDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
        [];
}

public sealed partial class HtmlTableColumnGroupDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
        [];
}

public sealed partial class HtmlTableDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
        [];
}

public sealed partial class HtmlTableFootDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
        [];
}

public sealed partial class HtmlTableHeadDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
        [];
}

public sealed partial class HtmlTableHeaderCellDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlTableRowDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
        [];
}

public sealed partial class HtmlTemplateDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
        [];
}

public sealed partial class HtmlTextAreaDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.value",
            ElementDataOrganizationSlotKind.Value,
            DomDataSourceKind.FormValue,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Value)
    ];
}

public sealed partial class HtmlTimeDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlTitleDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlTrackDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "src",
            ElementDataOrganizationSlotKind.Source,
            DomDataSourceKind.Url,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Source)
    ];
}

public sealed partial class HtmlUnderlineDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlUnorderedListDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
        [];
}

public sealed partial class HtmlVariableDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "content.ownText",
            ElementDataOrganizationSlotKind.Text,
            DomDataSourceKind.TextContent,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Text)
    ];
}

public sealed partial class HtmlVideoDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
    [
        new DomElementStringDataSource(
            "src",
            ElementDataOrganizationSlotKind.Source,
            DomDataSourceKind.Url,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Source),
        new DomElementStringDataSource(
            "poster",
            ElementDataOrganizationSlotKind.Source,
            DomDataSourceKind.Url,
            DomDataSourceDomain.BusinessInput,
            XamlControlDataTargetKind.Source)
    ];
}

public sealed partial class HtmlWordBreakDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() =>
        [];
}

public sealed partial class HtmlGenericDomElement
{
    protected override IReadOnlyList<DomElementDataSource> CreateDataSources() => [];
}

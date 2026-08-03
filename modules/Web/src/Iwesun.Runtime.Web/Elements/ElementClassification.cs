namespace Iwesun.Runtime.Web;

public enum ElementNamespace
{
	Html,
	Svg,
	MathMl,
	Custom
}

public enum ElementCategory
{
	Document,
	Metadata,
	Sectioning,
	LayoutContainer,
	Text,
	Interactive,
	FormControl,
	List,
	Table,
	Media,
	EmbeddedContent,
	VectorContainer,
	VectorGeometry,
	VectorDefinition
}

public enum ElementVisualKind
{
	NonVisual,
	LayoutContainer,
	TextContent,
	NativeControl,
	ReplacedContent,
	VectorContainer,
	VectorGeometry
}

public enum ElementContentModel
{
	None,
	Flow,
	Phrasing,
	TextOnly,
	FormOptions,
	TableStructure,
	MediaFallback,
	SvgChildren,
	Transparent
}

public enum ElementClosure
{
	NonVisual,
	OpenContainer,
	ClosedLeaf,
	ReplacedControl
}

public enum ElementSyntax
{
	Normal,
	Void,
	RawText,
	EscapableRawText,
	ForeignNamespace
}

public enum XamlConversionSupport
{
	Direct,
	Composite,
	RuntimeReplacement,
	VisualApproximation,
	NonVisual,
	Unsupported
}

public enum XamlControlFamily
{
	None,
	Panel,
	Text,
	Button,
	Input,
	Selector,
	Image,
	Media,
	Shape,
	EmbeddedHost,
	Custom
}

public enum XamlElementMappingKind
{
	TypeDefault,
	ViewportRoot,
	BlockFlow,
	InlineFlow,
	FlexLayout,
	GridLayout,
	PositionedLayout,
	TableLayout,
	ConservativeContainer
}

public enum XamlElementObjectType
{
	Border,
	Button,
	CalendarDatePicker,
	Canvas,
	CheckBox,
	ColorPicker,
	ComboBox,
	ComboBoxItem,
	ContentControl,
	DatePicker,
	Ellipse,
	Expander,
	Grid,
	HtmlCssBoxGrid,
	HtmlCssHorizontalFlexLineGrid,
	HtmlCssVerticalFlexLineGrid,
	HtmlBidiIsolationPanel,
	HtmlBidiIsolationTextBlock,
	HtmlBidiOverridePanel,
	HtmlBidiOverrideTextBlock,
	HtmlCanvasSurface,
	HtmlDatalistInputControl,
	HtmlDateInputControl,
	HtmlDateTimeLocalInputControl,
	HtmlDialogControl,
	HtmlEmbeddedContentHost,
	HtmlEmbeddedDocumentHost,
	HtmlFieldSetPanel,
	HtmlFileInputControl,
	HtmlFormButton,
	HtmlFormLabelPanel,
	HtmlImageMapComposite,
	HtmlImageMapHotspot,
	HtmlImageMapOverlay,
	HtmlImageSubmitButton,
	HtmlInteractiveFlexPanel,
	HtmlInlineFlowPanel,
	HtmlLineBreak,
	HtmlMediaElementControl,
	HtmlMeterControl,
	HtmlMonthInputControl,
	HtmlObjectContentHost,
	HtmlRubyAnnotationPanel,
	HtmlRubyAnnotationTextBlock,
	HtmlRubyPanel,
	HtmlSvgViewport,
	HtmlSpanBoxControl,
	HtmlSubscriptPanel,
	HtmlSubscriptTextBlock,
	HtmlSuperscriptPanel,
	HtmlSuperscriptTextBlock,
	HtmlTablePanel,
	HtmlTableRowPanel,
	HtmlTableSectionPanel,
	HtmlTimeInputControl,
	HtmlWeekInputControl,
	HtmlWordBreakOpportunity,
	HyperlinkButton,
	Image,
	Line,
	ListView,
	ListViewItem,
	MediaPlayerElement,
	NumberBox,
	PasswordBox,
	Path,
	Polygon,
	Polyline,
	ProgressBar,
	RadioButton,
	Rectangle,
	Slider,
	StackPanel,
	TextBlock,
	TextBox,
	TimePicker
}

public sealed record XamlElementMappingDecision(
	XamlElementObjectType? ObjectType,
	XamlElementMappingKind Kind,
	bool RequiresRuntimeLayoutContract,
	string Reason)
{
	public string ElementName => ObjectType?.ToString() ?? string.Empty;
}

public static class DomElementDesignPropertyCatalog
{
	public static IReadOnlyList<string> RequiredPropertyNames { get; } =
	[
		nameof(DomElement.TagName),
		nameof(DomElement.ElementNamespace),
		nameof(DomElement.Category),
		nameof(DomElement.VisualKind),
		nameof(DomElement.ContentModel),
		nameof(DomElement.Closure),
		nameof(DomElement.Syntax),
		nameof(DomElement.XamlSupport),
		nameof(DomElement.XamlControlFamily),
		nameof(DomElement.XamlElementName),
		nameof(DomElement.DefaultDisplay),
		nameof(DomElement.InteractionKind),
		nameof(DomElement.XamlChildPlacement)
	];

	public static IReadOnlyList<string> FindMissing(Type elementType)
	{
		ArgumentNullException.ThrowIfNull(elementType);
		var reflected = ElementPropertyTraitsReflector
			.GetAttributes(elementType)
			.Where(static traits => traits.IsElementDesignProperty)
			.Select(static traits => traits.PropertyName)
			.ToHashSet(StringComparer.Ordinal);
		return RequiredPropertyNames
			.Where(name => !reflected.Contains(name))
			.Order(StringComparer.Ordinal)
			.ToArray();
	}
}

public enum ElementDefaultDisplay
{
	None,
	Block,
	Inline,
	InlineBlock,
	ListItem,
	Table,
	TableSection,
	TableRow,
	TableCell,
	Replaced
}

public enum ElementInteractionKind
{
	None,
	Navigation,
	Command,
	Input,
	Selection,
	Disclosure,
	Media,
	EmbeddedDocument
}

public enum ElementXamlChildPlacementKind
{
	None,
	DirectChildren,
	Inlines,
	Content,
	Items
}

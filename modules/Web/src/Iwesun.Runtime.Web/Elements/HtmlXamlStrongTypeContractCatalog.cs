namespace Iwesun.Runtime.Web;

public sealed record HtmlXamlStrongTypeContract(
	string TagName,
	IReadOnlySet<string> AllowedElementNames,
	string DecisionBasis);

public static class HtmlXamlStrongTypeContractCatalog
{
	public static IReadOnlyDictionary<string, HtmlXamlStrongTypeContract>
		Contracts
	{ get; } = Create();

	public static HtmlXamlStrongTypeContract Get(string tagName) =>
		Contracts.TryGetValue(tagName, out var contract)
			? contract
			: throw new KeyNotFoundException(
				$"No strong XAML type contract is registered for '{tagName}'.");

	public static IReadOnlySet<string> RequiredConsumerTypes { get; } =
		Contracts.Values
			.SelectMany(static contract => contract.AllowedElementNames)
			.Where(static name =>
				name.StartsWith("Html", StringComparison.Ordinal))
			.ToHashSet(StringComparer.Ordinal);

	public static IReadOnlySet<string> RequiredAttachedProperties { get; } =
		new HashSet<string>(
			[
				"HtmlFormState.IsFormOwner",
				"HtmlFormState.FormOwnerId",
				"HtmlFormState.InitialValue",
				"HtmlFormState.InitialChecked",
				"HtmlFormState.InitialSelected",
				"HtmlValidation.IsValid",
				"HtmlValidation.WillValidate",
				"HtmlValidation.ValidationMessage",
				"HtmlTable.ColumnSpan",
				"HtmlTable.RowSpan"
			],
			StringComparer.Ordinal);

	public static bool CanOwnPlacement(
		string elementName,
		ElementXamlChildPlacementKind placement) =>
		placement switch
		{
			ElementXamlChildPlacementKind.None => true,
			ElementXamlChildPlacementKind.DirectChildren =>
				elementName is
					"Grid"
					or "HtmlCssBoxGrid"
					or "Canvas"
					or "StackPanel"
					or "HtmlInlineFlowPanel"
					or "HtmlInteractiveFlexPanel"
					or "HtmlFormLabelPanel"
					or "HtmlBidiIsolationPanel"
					or "HtmlBidiOverridePanel"
					or "HtmlTablePanel"
					or "HtmlTableSectionPanel"
					or "HtmlTableRowPanel"
					or "HtmlCanvasSurface"
					or "HtmlImageMapComposite"
					or "HtmlImageMapOverlay"
					or "HtmlFieldSetPanel"
					or "HtmlObjectContentHost"
					or "HtmlSubscriptPanel"
					or "HtmlSuperscriptPanel"
					or "HtmlRubyPanel"
					or "HtmlRubyAnnotationPanel"
					or "HtmlSvgViewport"
					or "HtmlDateTimeLocalInputControl",
			ElementXamlChildPlacementKind.Inlines =>
				elementName is
					"TextBlock"
					or "HtmlInlineFlowPanel"
					or "HtmlBidiIsolationTextBlock"
					or "HtmlBidiOverrideTextBlock",
			ElementXamlChildPlacementKind.Content =>
				elementName is
					"Border"
					or "Button"
					or "HtmlFormButton"
					or "HtmlImageSubmitButton"
					or "HyperlinkButton"
					or "ContentControl"
					or "HtmlEmbeddedContentHost"
					or "HtmlEmbeddedDocumentHost"
					or "HtmlObjectContentHost"
					or "HtmlDialogControl"
					or "ComboBoxItem"
					or "ListViewItem"
					or "Expander",
			ElementXamlChildPlacementKind.Items =>
				elementName is "ComboBox" or "ListView",
			_ => false
		};

	private static IReadOnlyDictionary<string, HtmlXamlStrongTypeContract>
		Create()
	{
		var contracts = new Dictionary<string, HtmlXamlStrongTypeContract>(
			StringComparer.OrdinalIgnoreCase);
		void Add(string tag, string basis, params string[] targets) =>
			contracts.Add(
				tag,
				new(
					tag,
					targets.ToHashSet(StringComparer.Ordinal),
					basis));

		Add("html", "viewport root", "HtmlCssBoxGrid");
		Add("head", "metadata container", "");
		Add("title", "document title metadata", "");
		Add("base", "URL base metadata", "");
		foreach (var tag in new[] { "body", "header", "footer", "main", "nav", "section", "article", "aside", "address", "figure", "hgroup", "blockquote", "search", "slot", "form" })
			Add(tag, "CSS formatting context selected by the final element class", "Grid", "HtmlCssBoxGrid");
		Add("div", "CSS formatting context, or ARIA role/contenteditable when explicitly declared",
			"Grid", "HtmlCssBoxGrid", "Button", "Rectangle", "TextBox");
		foreach (var tag in new[] { "figcaption", "p", "h1", "h2", "h3", "h4", "h5", "h6", "span", "strong", "em", "b", "i", "u", "s", "small", "code", "pre", "abbr", "cite", "data", "del", "dfn", "ins", "kbd", "mark", "samp", "time", "var", "dt", "legend", "output" })
			Add(tag, "leaf text versus composite phrasing", "TextBlock", "HtmlInlineFlowPanel", "HtmlCssBoxGrid");
		Add("bdi", "leaf or composite bidirectional isolation",
			"HtmlBidiIsolationTextBlock", "HtmlBidiIsolationPanel");
		Add("bdo", "leaf or composite bidirectional override",
			"HtmlBidiOverrideTextBlock", "HtmlBidiOverridePanel");
		Add("label", "labelable-control association and phrasing",
			"HtmlFormLabelPanel");
		Add("q", "leaf or composite quotation", "TextBlock", "HtmlInlineFlowPanel");
		Add("br", "forced inline line break", "HtmlLineBreak");
		Add("wbr", "optional inline break opportunity", "HtmlWordBreakOpportunity");
		Add("ruby", "leaf text versus ruby base/annotation layout", "TextBlock", "HtmlRubyPanel");
		Add("rp", "ruby fallback suppression or standalone phrasing", "TextBlock", "HtmlInlineFlowPanel");
		Add("rt", "leaf versus composite ruby annotation", "HtmlRubyAnnotationTextBlock", "HtmlRubyAnnotationPanel");
		Add("sub", "leaf versus composite subscript", "HtmlSubscriptTextBlock", "HtmlSubscriptPanel");
		Add("sup", "leaf versus composite superscript", "HtmlSuperscriptTextBlock", "HtmlSuperscriptPanel");
		Add("a", "href presence and phrasing shape", "HyperlinkButton", "TextBlock", "HtmlInlineFlowPanel");
		Add("ul", "unordered item collection", "ListView");
		Add("ol", "ordered item collection", "ListView");
		Add("menu", "command item collection", "ListView");
		Add("li", "list item content", "ListViewItem");
		Add("dl", "term-description block flow", "StackPanel");
		Add("dd", "description block flow", "StackPanel");
		Add("details", "disclosure state", "Expander");
		Add("summary", "standalone summary or details header suppression", "Button");
		Add("dialog", "top-layer dialog state", "HtmlDialogControl");
		foreach (var tag in new[] { "noscript", "script", "style", "link", "meta", "template" })
			Add(tag, "nonvisual document connection", "");
		Add(
			"button",
			"form command kind and an explicitly selected multi-child flex formatting context",
			"HtmlFormButton",
			"HtmlInteractiveFlexPanel");
		Add("input", "22-state input type discriminator",
			"ContentControl", "TextBox", "PasswordBox", "CheckBox", "RadioButton", "Slider",
			"NumberBox", "ColorPicker", "HtmlDateInputControl",
			"HtmlMonthInputControl", "HtmlWeekInputControl",
			"HtmlTimeInputControl", "HtmlDateTimeLocalInputControl",
			"HtmlFileInputControl", "HtmlFormButton", "HtmlImageSubmitButton",
			"HtmlDatalistInputControl");
		Add("textarea", "multiline text state", "TextBox");
		Add("select", "multiple and size state", "ComboBox", "ListView");
		Add("option", "owning selector presentation", "ComboBoxItem", "ListViewItem");
		Add("fieldset", "legend and grouped controls", "HtmlFieldSetPanel");
		Add("datalist", "nonvisual input suggestions", "");
		Add("optgroup", "nonvisual selector group expanded by select", "");
		Add("meter", "range thresholds", "HtmlMeterControl");
		Add("progress", "determinate versus indeterminate", "ProgressBar");
		Add("table", "table grid owner", "HtmlTablePanel");
		foreach (var tag in new[] { "thead", "tbody", "tfoot" })
			Add(tag, "table row group", "HtmlTableSectionPanel");
		Add("tr", "table row", "HtmlTableRowPanel");
		foreach (var tag in new[] { "th", "td" })
			Add(tag, "table cell and span state", "Border");
		Add("caption", "table caption phrasing", "TextBlock", "HtmlInlineFlowPanel");
		foreach (var tag in new[] { "colgroup", "col", "source", "track", "map", "area" })
			Add(tag, "nonvisual resource or geometry definition", "");
		Add("picture", "selected image content slot", "ContentControl");
		foreach (var tag in new[] { "video", "audio" })
			Add(tag, "selected media source, playback, and timed-text state",
				"HtmlMediaElementControl");
		Add("img", "plain image versus image-map composite", "Image", "HtmlImageMapComposite");
		Add("iframe", "embedded document scope", "HtmlEmbeddedDocumentHost");
		Add("canvas", "drawing command surface", "HtmlCanvasSurface");
		Add("embed", "typed embedded resource", "HtmlEmbeddedContentHost");
		Add("object", "typed embedded resource with fallback", "HtmlObjectContentHost");
		Add("hr", "thematic break geometry", "Rectangle");
		Add("selectedcontent", "selected option mirror text", "TextBlock");
		return contracts;
	}
}

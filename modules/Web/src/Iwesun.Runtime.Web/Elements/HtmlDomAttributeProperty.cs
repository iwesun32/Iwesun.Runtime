namespace Iwesun.Runtime.Web;

public enum HtmlAttributeValueSyntax
{
	Text,
	Boolean,
	BooleanOrKeyword,
	Url,
	UrlList,
	IdReference,
	IdReferenceList,
	Integer,
	NonNegativeInteger,
	FloatingPoint,
	Enumeration,
	Token,
	TokenList,
	MimeType,
	DateTime,
	MediaQuery,
	SourceSet,
	CssDeclarations
}

public enum HtmlAttributeXamlStrategy
{
	Direct,
	Semantic,
	Composite,
	RuntimeReplacement,
	ManualReview,
	NotApplicable
}

public enum HtmlElementAttributeXamlHandling
{
	InlineXaml,
	RuntimeDataSource,
	ManualReview,
	NotApplicable
}

public enum SvgElementAttributeXamlHandling
{
	InlineXaml,
	RuntimeDataSource,
	ManualReview,
	NotApplicable
}

public sealed record HtmlAttributeDefinition(
	string Name,
	HtmlAttributeValueSyntax ValueSyntax,
	HtmlAttributeXamlStrategy XamlStrategy,
	bool IsSecuritySensitive);

public sealed class HtmlDomAttributeProperty :
	DomElementStringProperty
{
	public HtmlDomAttributeProperty(string name, string? tagName = null) :
		base(name, ElementDataOrganizationSlotKind.Attribute)
	{
		Definition = HtmlAttributeDefinitionCatalog.GetRequired(name, tagName);
	}

	public HtmlAttributeDefinition Definition { get; }

	public override XamlPropertyExecutionDescriptor XamlExecution =>
		DomXamlPropertyExecutionCatalog.ResolveHtmlAttribute(
			Name,
			Definition);
}

public static class HtmlAttributeDefinitionCatalog
{
	private static readonly IReadOnlySet<string> BooleanAttributes = Set(
		"allowfullscreen", "alpha", "async", "autofocus", "autoplay",
		"checked", "controls", "default", "defer", "disabled", "formnovalidate",
		"headingreset", "hidden", "inert", "ismap", "itemscope", "loop",
		"multiple", "muted", "nomodule", "novalidate", "open", "playsinline",
		"readonly", "required", "reversed", "selected", "shadowrootclonable",
		"shadowrootcustomelementregistry", "shadowrootdelegatesfocus",
		"shadowrootserializable", "shadowrootslotassignment");
	private static readonly IReadOnlySet<string> UrlAttributes = Set(
		"action", "cite", "data", "formaction", "href", "itemid", "poster",
		"src");
	private static readonly IReadOnlySet<string> UrlListAttributes = Set(
		"ping");
	private static readonly IReadOnlySet<string> IdReferenceAttributes = Set(
		"commandfor", "for", "form", "list", "popovertarget");
	private static readonly IReadOnlySet<string> IdReferenceListAttributes = Set(
		"headers", "itemref");
	private static readonly IReadOnlySet<string> IntegerAttributes = Set(
		"start", "tabindex", "value");
	private static readonly IReadOnlySet<string> NonNegativeIntegerAttributes = Set(
		"cols", "colspan", "headingoffset", "height", "maxlength", "minlength",
		"rows", "rowspan", "size", "span", "width");
	private static readonly IReadOnlySet<string> FloatingPointAttributes = Set(
		"high", "low", "max", "min", "optimum", "step");
	private static readonly IReadOnlySet<string> TokenListAttributes = Set(
		"accept", "accesskey", "class", "itemprop", "itemtype", "part",
		"rel", "sandbox", "sizes");
	private static readonly IReadOnlySet<string> EnumerationAttributes = Set(
		"as", "autocapitalize", "autocomplete", "autocorrect", "blocking",
		"closedby", "colorspace", "command", "contenteditable", "crossorigin",
		"decoding", "dir", "draggable", "enctype", "enterkeyhint",
		"fetchpriority", "formenctype", "formmethod", "inputmode", "kind",
		"loading", "method", "popover", "popovertargetaction", "preload",
		"referrerpolicy", "scope", "shadowrootmode", "shadowrootslotassignment",
		"shape", "spellcheck", "target", "translate", "type", "wrap",
		"writingsuggestions");
	private static readonly IReadOnlySet<string> MimeTypeAttributes = Set(
		"accept", "type");
	private static readonly IReadOnlySet<string> DateTimeAttributes = Set(
		"datetime");
	private static readonly IReadOnlySet<string> MediaQueryAttributes = Set(
		"media");
	private static readonly IReadOnlySet<string> SourceSetAttributes = Set(
		"imagesrcset", "srcset");
	private static readonly IReadOnlySet<string> SecurityAttributes = Set(
		"allow", "crossorigin", "integrity", "nonce", "referrerpolicy",
		"sandbox");

	public static HtmlAttributeDefinition GetRequired(
		string name,
		string? tagName = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		var normalized = name.ToLowerInvariant();
		var normalizedTag = tagName?.ToLowerInvariant() ?? string.Empty;
		var syntax = ContextualSyntax(normalizedTag, normalized)
			?? (BooleanAttributes.Contains(normalized)
			? HtmlAttributeValueSyntax.Boolean
			: UrlAttributes.Contains(normalized)
				? HtmlAttributeValueSyntax.Url
				: UrlListAttributes.Contains(normalized)
					? HtmlAttributeValueSyntax.UrlList
					: IdReferenceAttributes.Contains(normalized)
						? HtmlAttributeValueSyntax.IdReference
						: IdReferenceListAttributes.Contains(normalized)
							? HtmlAttributeValueSyntax.IdReferenceList
							: NonNegativeIntegerAttributes.Contains(normalized)
								? HtmlAttributeValueSyntax.NonNegativeInteger
								: FloatingPointAttributes.Contains(normalized)
									? HtmlAttributeValueSyntax.FloatingPoint
									: IntegerAttributes.Contains(normalized)
										? HtmlAttributeValueSyntax.Integer
										: DateTimeAttributes.Contains(normalized)
											? HtmlAttributeValueSyntax.DateTime
											: MediaQueryAttributes.Contains(normalized)
												? HtmlAttributeValueSyntax.MediaQuery
												: SourceSetAttributes.Contains(normalized)
													? HtmlAttributeValueSyntax.SourceSet
													: normalized == "style"
														? HtmlAttributeValueSyntax.CssDeclarations
														: MimeTypeAttributes.Contains(normalized)
															? HtmlAttributeValueSyntax.MimeType
															: TokenListAttributes.Contains(normalized)
																? HtmlAttributeValueSyntax.TokenList
																: EnumerationAttributes.Contains(normalized)
																	? HtmlAttributeValueSyntax.Enumeration
																	: HtmlAttributeValueSyntax.Text);
		var strategy = SecurityAttributes.Contains(normalized)
			? HtmlAttributeXamlStrategy.ManualReview
			: syntax is HtmlAttributeValueSyntax.Url
				or HtmlAttributeValueSyntax.UrlList
				or HtmlAttributeValueSyntax.SourceSet
					? HtmlAttributeXamlStrategy.RuntimeReplacement
					: syntax == HtmlAttributeValueSyntax.CssDeclarations
						? HtmlAttributeXamlStrategy.Composite
						: HtmlAttributeXamlStrategy.Semantic;
		return new(
			normalized,
			syntax,
			strategy,
			SecurityAttributes.Contains(normalized));
	}

	private static HtmlAttributeValueSyntax? ContextualSyntax(
		string tagName,
		string attributeName) =>
		(attributeName, tagName) switch
		{
			("hidden", _) => HtmlAttributeValueSyntax.BooleanOrKeyword,
			("value", "li") => HtmlAttributeValueSyntax.Integer,
			("value", "meter" or "progress") => HtmlAttributeValueSyntax.FloatingPoint,
			("value", _) => HtmlAttributeValueSyntax.Text,
			("max" or "min" or "step", "input") => HtmlAttributeValueSyntax.Text,
			("type", "a" or "link" or "embed" or "object" or "source") =>
				HtmlAttributeValueSyntax.MimeType,
			("type", _) => HtmlAttributeValueSyntax.Enumeration,
			("accept", "input") => HtmlAttributeValueSyntax.TokenList,
			_ => null
		};

	private static IReadOnlySet<string> Set(params string[] names) =>
		new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
}

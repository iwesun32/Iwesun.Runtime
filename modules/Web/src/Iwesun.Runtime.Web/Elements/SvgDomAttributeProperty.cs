namespace Iwesun.Runtime.Web;

public enum SvgAttributeValueSyntax
{
	Text,
	Boolean,
	Enumeration,
	Length,
	LengthList,
	Number,
	NumberList,
	Url,
	Paint,
	TransformList,
	PathData,
	PointList,
	ViewBox,
	PreserveAspectRatio,
	CssDeclarations
}

public sealed record SvgAttributeDefinition(
	string Name,
	SvgAttributeValueSyntax ValueSyntax,
	HtmlAttributeXamlStrategy XamlStrategy);

public sealed class SvgDomAttributeProperty(string name) :
	DomElementStringProperty(name, ElementDataOrganizationSlotKind.Attribute)
{
	public SvgAttributeDefinition Definition { get; } =
		SvgAttributeDefinitionCatalog.GetRequired(name);

	public override XamlPropertyExecutionDescriptor XamlExecution =>
		DomXamlPropertyExecutionCatalog.ResolveSvgAttribute(
			Name,
			Definition);
}

public static class SvgAttributeDefinitionCatalog
{
	private static readonly IReadOnlySet<string> Lengths = Set(
		"baseline-shift", "cx", "cy", "dx", "dy", "font-size",
		"letter-spacing", "r", "rx", "ry", "stroke-dashoffset",
		"stroke-width", "textLength", "word-spacing", "x", "x1", "x2",
		"y", "y1", "y2", "width", "height");
	private static readonly IReadOnlySet<string> LengthLists = Set(
		"stroke-dasharray");
	private static readonly IReadOnlySet<string> Numbers = Set(
		"fill-opacity", "flood-opacity", "opacity", "pathLength",
		"stop-opacity", "stroke-miterlimit", "stroke-opacity");
	private static readonly IReadOnlySet<string> Urls = Set(
		"clip-path", "filter", "href", "marker-end", "marker-mid",
		"marker-start", "mask");
	private static readonly IReadOnlySet<string> Paints = Set(
		"color", "fill", "flood-color", "lighting-color", "stop-color",
		"stroke");

	public static SvgAttributeDefinition GetRequired(string name)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		var syntax = name switch
		{
			"style" => SvgAttributeValueSyntax.CssDeclarations,
			"transform" => SvgAttributeValueSyntax.TransformList,
			"d" => SvgAttributeValueSyntax.PathData,
			"points" => SvgAttributeValueSyntax.PointList,
			"viewBox" => SvgAttributeValueSyntax.ViewBox,
			"preserveAspectRatio" => SvgAttributeValueSyntax.PreserveAspectRatio,
			"rotate" => SvgAttributeValueSyntax.NumberList,
			_ when Lengths.Contains(name) => SvgAttributeValueSyntax.Length,
			_ when LengthLists.Contains(name) => SvgAttributeValueSyntax.LengthList,
			_ when Numbers.Contains(name) => SvgAttributeValueSyntax.Number,
			_ when Urls.Contains(name) => SvgAttributeValueSyntax.Url,
			_ when Paints.Contains(name) => SvgAttributeValueSyntax.Paint,
			_ => SvgAttributeValueSyntax.Text
		};
		var strategy = syntax switch
		{
			SvgAttributeValueSyntax.Url => HtmlAttributeXamlStrategy.RuntimeReplacement,
			SvgAttributeValueSyntax.CssDeclarations
				or SvgAttributeValueSyntax.TransformList =>
				HtmlAttributeXamlStrategy.Composite,
			_ => HtmlAttributeXamlStrategy.Semantic
		};
		return new(name, syntax, strategy);
	}

	private static IReadOnlySet<string> Set(params string[] values) =>
		new HashSet<string>(values, StringComparer.Ordinal);
}

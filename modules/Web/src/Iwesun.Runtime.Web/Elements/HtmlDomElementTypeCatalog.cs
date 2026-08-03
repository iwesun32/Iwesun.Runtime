using System.Collections.ObjectModel;

namespace Iwesun.Runtime.Web;

public sealed record DomElementTypeCatalogAudit(
	int ExpectedHtmlTypes,
	int ExpectedSvgTypes,
	int RegisteredHtmlTypes,
	int RegisteredSvgTypes,
	IReadOnlyList<string> MissingHtmlTags,
	IReadOnlyList<string> MissingSvgTags,
	bool Passed);

public static class HtmlDomElementTypeCatalog
{
	private static readonly string[] StandardHtmlTags =
	[
		"html", "head", "title", "base", "body", "header", "footer", "main",
		"nav", "section", "article", "aside", "address", "figure", "figcaption",
		"div", "p", "h1", "h2", "h3", "h4", "h5", "h6", "hgroup", "span",
		"strong", "em", "b", "i", "u", "s", "small", "code", "pre",
		"blockquote", "br", "wbr", "abbr", "bdi", "bdo", "cite", "data", "del",
		"dfn", "ins", "kbd", "mark", "q", "ruby", "rp", "rt", "samp", "sub",
		"sup", "time", "var", "a", "ul", "ol", "menu", "li", "dl", "dt", "dd",
		"details", "summary", "dialog", "search", "slot", "noscript", "script",
		"style", "link", "meta", "template", "form", "label", "button", "input",
		"textarea", "select", "option", "fieldset", "legend", "datalist",
		"optgroup", "output", "meter", "progress", "table", "thead", "tbody",
		"tfoot", "tr", "th", "td", "caption", "colgroup", "col", "picture",
		"source", "video", "audio", "map", "area", "track", "img", "iframe",
		"canvas", "embed", "object", "hr", "selectedcontent"
	];
	private static readonly string[] StandardSvgTags =
	[
		"svg", "g", "path", "use", "circle", "rect", "line", "polygon",
		"polyline", "ellipse", "defs", "clipPath", "mask", "text"
	];
	private static readonly IReadOnlyDictionary<string, Func<DomElementMapping, DomElement>>
		Factories = CreateFactories();

	public static IReadOnlyList<string> HtmlTags { get; } =
		Array.AsReadOnly(StandardHtmlTags);

	public static IReadOnlyList<string> SvgTags { get; } =
		Array.AsReadOnly(StandardSvgTags);

	public static DomElement Create(
		string tagName,
		DomElementMapping mapping)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(tagName);
		ArgumentNullException.ThrowIfNull(mapping);
		if (Factories.TryGetValue(tagName, out var factory))
			return factory(mapping);
		if (StandardHtmlTags.Contains(tagName, StringComparer.OrdinalIgnoreCase)
			|| StandardSvgTags.Contains(tagName, StringComparer.OrdinalIgnoreCase))
		{
			throw new NotSupportedException(
				$"标准元素 '{tagName}' 尚未迁移到 DomElement 递归体系；"
				+ "禁止使用通用壳继续转换。");
		}
		return new HtmlGenericDomElement(mapping, tagName);
	}

	public static bool MatchesRegisteredType(DomElement element)
	{
		ArgumentNullException.ThrowIfNull(element);
		return Factories.TryGetValue(element.TagName, out var factory)
			&& factory(CreateProbeMapping()).GetType() == element.GetType();
	}

	public static DomElementTypeCatalogAudit AuditCompleteness()
	{
		var missingHtml = StandardHtmlTags
			.Where(tag => !Factories.ContainsKey(tag))
			.ToArray();
		var missingSvg = StandardSvgTags
			.Where(tag => !Factories.ContainsKey(tag))
			.ToArray();
		var registeredHtml = Factories.Keys.Count(tag =>
			StandardHtmlTags.Contains(tag, StringComparer.OrdinalIgnoreCase));
		var registeredSvg = Factories.Keys.Count(tag =>
			StandardSvgTags.Contains(tag, StringComparer.OrdinalIgnoreCase));
		return new(
			StandardHtmlTags.Length,
			StandardSvgTags.Length,
			registeredHtml,
			registeredSvg,
			missingHtml,
			missingSvg,
			missingHtml.Length == 0 && missingSvg.Length == 0);
	}

	private static DomElementMapping CreateProbeMapping() =>
		new("architecture-probe", "/probe", null, [], null, null);

	private static IReadOnlyDictionary<string, Func<DomElementMapping, DomElement>>
		CreateFactories()
	{
		var factories = new Dictionary<string, Func<DomElementMapping, DomElement>>(
			StringComparer.OrdinalIgnoreCase);
		var mapping = CreateProbeMapping();
		foreach (var type in typeof(HtmlDomElementDefinition).Assembly
			.GetTypes()
			.Where(static type =>
				type.IsSealed
				&& !type.IsAbstract
				&& (typeof(HtmlDomElementDefinition).IsAssignableFrom(type)
					|| typeof(SvgDomElementDefinition).IsAssignableFrom(type))
				&& type != typeof(HtmlGenericDomElement)
				&& type.GetConstructor([typeof(DomElementMapping)]) is not null))
		{
			DomElement Factory(DomElementMapping value) =>
				Activator.CreateInstance(type, value) as DomElement
				?? throw new InvalidOperationException(
					$"Unable to construct DOM element type {type.FullName}.");
			var probe = Factory(mapping);
			if (!factories.TryAdd(probe.TagName, Factory))
			{
				throw new InvalidOperationException(
					$"Duplicate DOM element registration for tag '{probe.TagName}'.");
			}
		}
		return new ReadOnlyDictionary<string, Func<DomElementMapping, DomElement>>(
			factories);
	}
}

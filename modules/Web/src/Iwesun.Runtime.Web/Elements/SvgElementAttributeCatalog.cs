using System.Collections.ObjectModel;

namespace Iwesun.Runtime.Web;

public static class SvgElementAttributeCatalog
{
	private static readonly IReadOnlySet<string> CommonAttributes = Set(
		"id", "tabindex", "lang", "xml:space", "class", "style",
		"requiredExtensions", "systemLanguage",
		"alignment-baseline", "baseline-shift", "clip", "clip-path", "clip-rule",
		"color", "color-interpolation", "color-interpolation-filters",
		"color-rendering", "cursor", "direction", "display",
		"dominant-baseline", "fill", "fill-opacity", "fill-rule", "filter",
		"flood-color", "flood-opacity", "font-family", "font-size",
		"font-size-adjust", "font-stretch", "font-style", "font-variant",
		"font-weight", "glyph-orientation-horizontal",
		"glyph-orientation-vertical", "image-rendering", "letter-spacing",
		"lighting-color", "marker-end", "marker-mid", "marker-start", "mask",
		"opacity", "overflow", "paint-order", "pointer-events",
		"shape-rendering", "stop-color", "stop-opacity", "stroke",
		"stroke-dasharray", "stroke-dashoffset", "stroke-linecap",
		"stroke-linejoin", "stroke-miterlimit", "stroke-opacity",
		"stroke-width", "text-anchor", "text-decoration", "text-rendering",
		"transform", "unicode-bidi", "vector-effect", "visibility",
		"word-spacing", "writing-mode");

	private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>>
		ElementAttributes = new ReadOnlyDictionary<string, IReadOnlySet<string>>(
			new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
			{
				["svg"] = Set("x", "y", "width", "height", "viewBox", "preserveAspectRatio"),
				["path"] = Set("d", "pathLength"),
				["use"] = Set("href", "x", "y", "width", "height"),
				["circle"] = Set("cx", "cy", "r"),
				["rect"] = Set("x", "y", "width", "height", "rx", "ry"),
				["line"] = Set("x1", "y1", "x2", "y2"),
				["polygon"] = Set("points", "pathLength"),
				["polyline"] = Set("points", "pathLength"),
				["ellipse"] = Set("cx", "cy", "rx", "ry"),
				["clipPath"] = Set("clipPathUnits"),
				["mask"] = Set("maskUnits", "maskContentUnits", "x", "y", "width", "height"),
				["text"] = Set("x", "y", "dx", "dy", "rotate", "textLength", "lengthAdjust")
			});

	public static IReadOnlySet<string> GetCommonAttributes() => CommonAttributes;

	public static IReadOnlySet<string> GetElementSpecificAttributes(string tagName) =>
		ElementAttributes.TryGetValue(tagName, out var attributes)
			? attributes
			: Set();

	public static IReadOnlySet<string> GetSupportedAttributes(string tagName) =>
		new HashSet<string>(
			CommonAttributes.Concat(GetElementSpecificAttributes(tagName)),
			StringComparer.Ordinal);

	private static IReadOnlySet<string> Set(params string[] values) =>
		new HashSet<string>(values, StringComparer.Ordinal);
}

using System.Collections.ObjectModel;

namespace Iwesun.Runtime.Web;

public static class HtmlElementAttributeCatalog
{
	private static readonly IReadOnlySet<string> GlobalAttributes =
		new HashSet<string>(
			[
				"id", "class", "style", "title", "lang", "dir", "hidden",
				"tabindex", "accesskey", "contenteditable", "draggable",
				"spellcheck", "translate", "role", "slot", "part", "popover",
				"exportparts",
				"inert", "inputmode", "enterkeyhint", "autocapitalize",
				"autocorrect", "autofocus", "headingoffset", "headingreset",
				"is", "writingsuggestions", "nonce", "itemid", "itemprop",
				"itemref", "itemscope", "itemtype"
			],
			StringComparer.OrdinalIgnoreCase);

	private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> ElementAttributes =
		new ReadOnlyDictionary<string, IReadOnlySet<string>>(
			new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
			{
				["a"] = Set("href", "target", "download", "ping", "rel", "hreflang", "type", "referrerpolicy"),
				["area"] = Set("alt", "coords", "shape", "href", "target", "download", "ping", "rel", "referrerpolicy"),
				["audio"] = Set("src", "crossorigin", "preload", "autoplay", "loop", "muted", "controls", "loading"),
				["base"] = Set("href", "target"),
				["blockquote"] = Set("cite"),
				["button"] = Set("command", "commandfor", "disabled", "form", "formaction", "formenctype", "formmethod", "formnovalidate", "formtarget", "name", "popovertarget", "popovertargetaction", "type", "value"),
				["canvas"] = Set("width", "height"),
				["col"] = Set("span"),
				["colgroup"] = Set("span"),
				["data"] = Set("value"),
				["del"] = Set("cite", "datetime"),
				["details"] = Set("open", "name"),
				["dialog"] = Set("open", "closedby"),
				["embed"] = Set("src", "type", "width", "height"),
				["fieldset"] = Set("disabled", "form", "name"),
				["form"] = Set("accept-charset", "action", "autocomplete", "enctype", "method", "name", "novalidate", "rel", "target"),
				["iframe"] = Set("src", "srcdoc", "name", "sandbox", "allow", "allowfullscreen", "width", "height", "referrerpolicy", "loading"),
				["img"] = Set("alt", "src", "srcset", "sizes", "crossorigin", "usemap", "ismap", "controls", "width", "height", "referrerpolicy", "decoding", "loading", "fetchpriority"),
				["input"] = Set("accept", "alpha", "alt", "autocomplete", "checked", "colorspace", "dirname", "disabled", "form", "formaction", "formenctype", "formmethod", "formnovalidate", "formtarget", "height", "list", "max", "maxlength", "min", "minlength", "multiple", "name", "pattern", "placeholder", "popovertarget", "popovertargetaction", "readonly", "required", "size", "src", "step", "type", "value", "width"),
				["ins"] = Set("cite", "datetime"),
				["label"] = Set("for"),
				["li"] = Set("value"),
				["link"] = Set("href", "crossorigin", "rel", "media", "integrity", "hreflang", "type", "as", "color", "sizes", "imagesrcset", "imagesizes", "referrerpolicy", "blocking", "fetchpriority", "disabled"),
				["map"] = Set("name"),
				["meta"] = Set("name", "http-equiv", "content", "charset", "media"),
				["meter"] = Set("value", "min", "max", "low", "high", "optimum"),
				["object"] = Set("data", "type", "name", "form", "width", "height"),
				["ol"] = Set("reversed", "start", "type"),
				["optgroup"] = Set("disabled", "label"),
				["option"] = Set("disabled", "label", "selected", "value"),
				["output"] = Set("for", "form", "name"),
				["progress"] = Set("value", "max"),
				["q"] = Set("cite"),
				["script"] = Set("src", "type", "nomodule", "async", "defer", "crossorigin", "integrity", "referrerpolicy", "blocking", "fetchpriority"),
				["select"] = Set("autocomplete", "disabled", "form", "multiple", "name", "required", "size"),
				["slot"] = Set("name"),
				["source"] = Set("type", "src", "srcset", "sizes", "media", "width", "height"),
				["style"] = Set("media", "blocking"),
				["template"] = Set("shadowrootclonable", "shadowrootcustomelementregistry", "shadowrootdelegatesfocus", "shadowrootmode", "shadowrootserializable", "shadowrootslotassignment"),
				["td"] = Set("colspan", "rowspan", "headers"),
				["textarea"] = Set("autocomplete", "cols", "dirname", "disabled", "form", "maxlength", "minlength", "name", "placeholder", "readonly", "required", "rows", "wrap"),
				["th"] = Set("colspan", "rowspan", "headers", "scope", "abbr"),
				["time"] = Set("datetime"),
				["track"] = Set("default", "kind", "label", "src", "srclang"),
				["video"] = Set("src", "crossorigin", "poster", "preload", "autoplay", "playsinline", "loop", "muted", "controls", "loading", "width", "height")
			});

	public static IReadOnlySet<string> GetSupportedAttributes(string tagName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(tagName);
		if (!ElementAttributes.TryGetValue(tagName, out var elementAttributes))
		{
			return GlobalAttributes;
		}

		return new HashSet<string>(
			GlobalAttributes.Concat(elementAttributes),
			StringComparer.OrdinalIgnoreCase);
	}

	public static IReadOnlySet<string> GetGlobalAttributes() =>
		GlobalAttributes;

	public static bool IsSupported(string tagName, string attributeName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(attributeName);
		return attributeName.StartsWith("aria-", StringComparison.OrdinalIgnoreCase)
			|| attributeName.StartsWith("data-", StringComparison.OrdinalIgnoreCase)
			|| attributeName.StartsWith("on", StringComparison.OrdinalIgnoreCase)
			|| GetSupportedAttributes(tagName).Contains(attributeName);
	}

	public static IReadOnlySet<string> GetElementSpecificAttributes(string tagName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(tagName);
		return ElementAttributes.TryGetValue(tagName, out var attributes)
			? attributes
			: new HashSet<string>(StringComparer.OrdinalIgnoreCase);
	}

	private static IReadOnlySet<string> Set(params string[] values) =>
		new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
}

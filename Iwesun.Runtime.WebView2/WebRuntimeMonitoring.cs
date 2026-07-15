using System.Collections.Concurrent;

namespace Iwesun.Runtime.WebView2;

public sealed record WebRuntimeMonitorFilter(string FilterId, string? Kind = null, string? UrlContains = null, bool Enabled = true);

public sealed class WebRuntimeMonitorFilterRegistry
{
	private readonly ConcurrentDictionary<string, WebRuntimeMonitorFilter> _filters = new(StringComparer.OrdinalIgnoreCase);
	public void Add(WebRuntimeMonitorFilter filter) { ArgumentNullException.ThrowIfNull(filter); if (string.IsNullOrWhiteSpace(filter.FilterId)) throw new ArgumentException("FilterId is required.", nameof(filter)); _filters[filter.FilterId] = filter; }
	public bool Remove(string filterId) => _filters.TryRemove(filterId, out _);
	public void Clear() => _filters.Clear();
	public IReadOnlyList<WebRuntimeMonitorFilter> Snapshot() => _filters.Values.OrderBy(x => x.FilterId, StringComparer.OrdinalIgnoreCase).ToArray();
	public bool Accept(string kind, string url)
	{
		var filters = Snapshot().Where(x => x.Enabled).ToArray();
		return filters.Length == 0 || filters.Any(x => (x.Kind is null || x.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase)) && (x.UrlContains is null || url.Contains(x.UrlContains, StringComparison.OrdinalIgnoreCase)));
	}
}

public static class WebRuntimeEvidenceScripts
{
	public static string HighlightXPath(string xpath, string color = "#ff4d4f")
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(xpath);
		var x = System.Text.Json.JsonSerializer.Serialize(xpath);
		var c = System.Text.Json.JsonSerializer.Serialize(color);
		return $"(() => {{ const n=document.evaluate({x},document,null,XPathResult.FIRST_ORDERED_NODE_TYPE,null).singleNodeValue; if(!n) return JSON.stringify({{success:false,code:'XPATH_NOT_FOUND'}}); n.dataset.iwesunRuntimeHighlight='1'; n.style.outline='2px solid '+{c}; return JSON.stringify({{success:true,xpath:{x}}}); }})()";
	}
	public static string ClearHighlights() => "(() => { document.querySelectorAll('[data-iwesun-runtime-highlight=\"1\"]').forEach(n => { n.style.outline=''; delete n.dataset.iwesunRuntimeHighlight; }); return JSON.stringify({success:true}); })()";
}

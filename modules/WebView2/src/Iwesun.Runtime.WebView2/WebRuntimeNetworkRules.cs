using System.Collections.Concurrent;

namespace Iwesun.Runtime.WebView2;

public enum WebRuntimeNetworkRuleKind
{
	Block,
	Replace
}

public sealed record WebRuntimeNetworkRule(
	string RuleId,
	WebRuntimeNetworkRuleKind Kind,
	string UrlPattern,
	string? BackendId = null,
	string? Method = null,
	int StatusCode = 200,
	string ContentType = "application/json; charset=utf-8",
	string ResponseBody = "{}",
	bool Enabled = true);

public sealed record WebRuntimeNetworkDecision(
	bool Matched,
	WebRuntimeNetworkRule? Rule = null,
	bool Blocked = false,
	int? StatusCode = null,
	string? ContentType = null,
	string? ResponseBody = null);

/// <summary>
/// Thread-safe rule registry used by a WebView2 host's resource-request event.
/// The registry makes the decision; the host applies it to the native request.
/// </summary>
public sealed class WebRuntimeNetworkRuleRegistry
{
	private readonly ConcurrentDictionary<string, WebRuntimeNetworkRule> _rules = new(StringComparer.OrdinalIgnoreCase);

	public WebRuntimeNetworkRule Add(WebRuntimeNetworkRule rule)
	{
		ArgumentNullException.ThrowIfNull(rule);
		if (string.IsNullOrWhiteSpace(rule.RuleId)) throw new ArgumentException("RuleId is required.", nameof(rule));
		if (string.IsNullOrWhiteSpace(rule.UrlPattern)) throw new ArgumentException("UrlPattern is required.", nameof(rule));
		if (rule.Kind == WebRuntimeNetworkRuleKind.Replace && rule.StatusCode is < 100 or > 599)
			throw new ArgumentOutOfRangeException(nameof(rule), "Replacement status code must be between 100 and 599.");
		_rules[rule.RuleId] = rule;
		return rule;
	}

	public bool Remove(string ruleId) => _rules.TryRemove(ruleId, out _);
	public void Clear() => _rules.Clear();
	public IReadOnlyList<WebRuntimeNetworkRule> Snapshot() => _rules.Values.OrderBy(x => x.RuleId, StringComparer.OrdinalIgnoreCase).ToArray();

	public WebRuntimeNetworkDecision Decide(string backendId, string method, string url)
	{
		foreach (var rule in Snapshot())
		{
			if (!rule.Enabled || !Matches(rule, backendId, method, url)) continue;
			return rule.Kind == WebRuntimeNetworkRuleKind.Block
				? new(true, rule, Blocked: true)
				: new(true, rule, StatusCode: rule.StatusCode, ContentType: rule.ContentType, ResponseBody: rule.ResponseBody);
		}
		return new(false);
	}

	private static bool Matches(WebRuntimeNetworkRule rule, string backendId, string method, string url) =>
		(rule.BackendId is null || rule.BackendId.Equals(backendId, StringComparison.OrdinalIgnoreCase))
		&& (rule.Method is null || rule.Method.Equals(method, StringComparison.OrdinalIgnoreCase))
		&& GlobMatch(rule.UrlPattern, url);

	private static bool GlobMatch(string pattern, string value)
	{
		var parts = pattern.Split('*', StringSplitOptions.None);
		if (parts.Length == 1) return value.Contains(pattern, StringComparison.OrdinalIgnoreCase);
		var offset = 0;
		if (parts[0].Length > 0)
		{
			if (!value.StartsWith(parts[0], StringComparison.OrdinalIgnoreCase)) return false;
			offset = parts[0].Length;
		}
		for (var i = 1; i < parts.Length - 1; i++)
		{
			var index = value.IndexOf(parts[i], offset, StringComparison.OrdinalIgnoreCase);
			if (index < 0) return false;
			offset = index + parts[i].Length;
		}
		return parts[^1].Length == 0 || value.EndsWith(parts[^1], StringComparison.OrdinalIgnoreCase);
	}
}

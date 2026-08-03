using System.Collections.Concurrent;
using System.Text.Json;
using Iwesun.Runtime.WebView2;

namespace Iwesun.Runtime.Web;

public sealed record HtmlRuntimeAnimationTimeline(
	string Schema,
	JsonElement[] Animations);

public sealed record HtmlRuntimeAnimationBinding(
	string DocumentScope,
	string XPath,
	HtmlRuntimePropertyEvidence? Link,
	HtmlRuntimePropertyEvidence? Runtime,
	HtmlRuntimeAnimationTimeline? Timeline);

public sealed record HtmlRuntimeXamlAnimationTargetBinding(
	HtmlRuntimeAnimationBinding Source,
	object Target);

/// <summary>
/// Preserves authored animation linkage and the browser's structured live
/// timeline as separate evidence. It never treats currentTime as an authored
/// initialization value.
/// </summary>
public sealed class HtmlRuntimeAnimationManager
{
	private readonly ConcurrentDictionary<
		(string DocumentScope, string XPath),
		HtmlRuntimeAnimationBinding> _bindings = new();
	private readonly ConcurrentBag<HtmlRuntimeXamlAnimationTargetBinding>
		_xamlTargets = [];

	public IReadOnlyCollection<HtmlRuntimeAnimationBinding> Bindings =>
		_bindings.Values.ToArray();

	public IReadOnlyCollection<HtmlRuntimeXamlAnimationTargetBinding>
		XamlTargets => _xamlTargets.ToArray();

	public HtmlRuntimeAnimationBinding? Find(
		string documentScope,
		string xpath) =>
		_bindings.GetValueOrDefault((documentScope, xpath));

	public void RegisterXamlTarget(
		HtmlRuntimeAnimationBinding source,
		object target)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(target);
		_xamlTargets.Add(new(source, target));
	}

	internal void ClearXamlTargets() => _xamlTargets.Clear();

	internal void Observe(WebRuntimeDomIndexedProperty property)
	{
		if (!property.Identity.PropertyName.Equals(
			"effect.animations",
			StringComparison.Ordinal))
		{
			return;
		}
		var key = (
			property.Identity.DocumentScope,
			property.Identity.XPath);
		_bindings.AddOrUpdate(
			key,
			_ => Create(property),
			(_, current) => Update(current, property));
	}

	private static HtmlRuntimeAnimationBinding Create(
		WebRuntimeDomIndexedProperty property)
	{
		var evidence = HtmlRuntimeStyleManager.ToEvidence(property);
		return property.Identity.Slot switch
		{
			WebRuntimeDomDataSlot.Link => new(
				property.Identity.DocumentScope,
				property.Identity.XPath,
				evidence,
				null,
				null),
			WebRuntimeDomDataSlot.Runtime => new(
				property.Identity.DocumentScope,
				property.Identity.XPath,
				null,
				evidence,
				ParseTimeline(property)),
			_ => new(
				property.Identity.DocumentScope,
				property.Identity.XPath,
				null,
				null,
				null)
		};
	}

	private static HtmlRuntimeAnimationBinding Update(
		HtmlRuntimeAnimationBinding current,
		WebRuntimeDomIndexedProperty property)
	{
		var evidence = HtmlRuntimeStyleManager.ToEvidence(property);
		return property.Identity.Slot switch
		{
			WebRuntimeDomDataSlot.Link => current with { Link = evidence },
			WebRuntimeDomDataSlot.Runtime => current with
			{
				Runtime = evidence,
				Timeline = ParseTimeline(property)
			},
			_ => current
		};
	}

	private static HtmlRuntimeAnimationTimeline? ParseTimeline(
		WebRuntimeDomIndexedProperty property)
	{
		if (property.Status != WebRuntimeDomPropertyStatus.Captured
			|| string.IsNullOrWhiteSpace(property.Value))
		{
			return null;
		}
		using var document = JsonDocument.Parse(property.Value);
		var root = document.RootElement;
		if (root.ValueKind != JsonValueKind.Object
			|| !root.TryGetProperty("schema", out var schema)
			|| !root.TryGetProperty("animations", out var animations)
			|| animations.ValueKind != JsonValueKind.Array)
		{
			throw new InvalidDataException(
				"Animation evidence must use the structured timeline schema.");
		}
		return new(
			schema.GetString() ?? string.Empty,
			animations.EnumerateArray()
				.Select(static item => item.Clone())
				.ToArray());
	}
}

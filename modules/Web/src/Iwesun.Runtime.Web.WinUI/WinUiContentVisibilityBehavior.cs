using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Iwesun.Runtime.Web.WinUI;

/// <summary>
/// Implements content-visibility without collapsing the owner's principal box.
/// Only descendant visuals are collapsed; the owner keeps its layout size,
/// background and border.
/// </summary>
internal static class WinUiContentVisibilityBehavior
{
	private sealed record AffectedElement(
		UIElement Element,
		Visibility OriginalVisibility);

	private sealed class Materialization(
		string rule,
		IReadOnlyList<AffectedElement> affected)
	{
		internal string Rule { get; } = rule;
		internal IReadOnlyList<AffectedElement> Affected { get; } = affected;
		internal bool ContentIsVisible { get; set; } = true;
	}

	private static readonly ConditionalWeakTable<
		FrameworkElement,
		Materialization> Materializations = new();

	internal static void Apply(FrameworkElement owner, string rule)
	{
		ArgumentNullException.ThrowIfNull(owner);
		if (Materializations.TryGetValue(owner, out var previous))
		{
			if (previous.Rule == "auto")
				owner.EffectiveViewportChanged -= OnEffectiveViewportChanged;
			foreach (var affected in previous.Affected)
				affected.Element.Visibility = affected.OriginalVisibility;
			Materializations.Remove(owner);
		}
		var normalized = rule.Trim().ToLowerInvariant();
		if (normalized == "visible")
		{
			Materializations.Add(owner, new(normalized, []));
			return;
		}
		if (normalized is not ("hidden" or "auto"))
			throw new InvalidDataException(
				$"Unsupported CSS content-visibility '{rule}'.");
		var affectedElements = ResolveContent(owner)
			.Distinct()
			.Select(static element => new AffectedElement(
				element,
				element.Visibility))
			.ToArray();
		if (affectedElements.Length == 0)
			throw new InvalidDataException(
				$"{owner.GetType().Name} has no separable content visual for "
					+ "content-visibility:hidden.");
		var state = new Materialization(normalized, affectedElements);
		Materializations.Add(owner, state);
		if (normalized == "hidden")
		{
			SetContentVisibility(state, visible: false);
			return;
		}
		owner.EffectiveViewportChanged += OnEffectiveViewportChanged;
		SetContentVisibility(
			state,
			owner.XamlRoot is not null
				&& owner.ActualWidth > 0
				&& owner.ActualHeight > 0);
	}

	internal static bool TryRead(
		FrameworkElement owner,
		out string value)
	{
		value = string.Empty;
		if (!Materializations.TryGetValue(owner, out var state))
			return false;
		if (state.Rule is "hidden" or "auto")
		{
			if (state.Affected.Count == 0
				|| state.Affected.Any(affected =>
					affected.Element.Visibility != (state.ContentIsVisible
						? affected.OriginalVisibility
						: Visibility.Collapsed)))
			{
				return false;
			}
		}
		else if (state.Affected.Count != 0)
		{
			return false;
		}
		value = state.Rule;
		return true;
	}

	private static void OnEffectiveViewportChanged(
		FrameworkElement sender,
		EffectiveViewportChangedEventArgs args)
	{
		if (Materializations.TryGetValue(sender, out var state)
			&& state.Rule == "auto")
		{
			SetContentVisibility(
				state,
				args.EffectiveViewport.Width > 0
					&& args.EffectiveViewport.Height > 0);
		}
	}

	private static void SetContentVisibility(
		Materialization state,
		bool visible)
	{
		state.ContentIsVisible = visible;
		foreach (var affected in state.Affected)
		{
			affected.Element.Visibility = visible
				? affected.OriginalVisibility
				: Visibility.Collapsed;
		}
	}

	private static IEnumerable<UIElement> ResolveContent(
		FrameworkElement owner)
	{
		if (owner is HtmlCssBoxGrid box)
		{
			yield return box.LayoutRoot;
			yield break;
		}
		if (owner is Border { Child: UIElement borderChild })
		{
			yield return borderChild;
			yield break;
		}
		if (owner is ContentControl { Content: UIElement content })
		{
			yield return content;
			yield break;
		}
		if (owner is Panel panel)
		{
			foreach (var child in panel.Children)
				yield return child;
		}
	}
}

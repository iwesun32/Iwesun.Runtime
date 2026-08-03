using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Iwesun.Runtime.Web.WinUI;

/// <summary>
/// Materializes CSS layout, size, inline-size, style-scope, and paint
/// containment on the strong HTML box target.
/// </summary>
internal static class WinUiContainBehavior
{
	private sealed record Materialization(
		string Rule,
		FrameworkElement ClipTarget,
		Thickness Padding,
		RectangleGeometry? Geometry,
		bool Layout,
		bool Size,
		bool InlineSize,
		bool Style,
		bool Paint);

	private sealed record Containment(
		bool Layout,
		bool Size,
		bool InlineSize,
		bool Style,
		bool Paint);

	private static readonly ConditionalWeakTable<
		FrameworkElement,
		Materialization> Materializations = new();
	private static readonly ConditionalWeakTable<
		FrameworkElement,
		Materialization> ClipTargets = new();

	internal static void Apply(FrameworkElement owner, string rule)
	{
		ArgumentNullException.ThrowIfNull(owner);
		if (Materializations.TryGetValue(owner, out var previous))
		{
			previous.ClipTarget.SizeChanged -= OnClipTargetSizeChanged;
			ClipTargets.Remove(previous.ClipTarget);
			if (previous.Geometry is not null
				&& ReferenceEquals(
					previous.ClipTarget.Clip,
					previous.Geometry))
			{
				previous.ClipTarget.Clip = null;
			}
			Materializations.Remove(owner);
		}
		var normalized = rule.Trim().ToLowerInvariant();
		var containment = Parse(normalized);
		if (owner is HtmlCssBoxGrid targetBox)
		{
			targetBox.ApplyContainment(
				containment.Layout,
				containment.Size,
				containment.InlineSize,
				containment.Style);
		}
		else if (containment is
			{ Layout: true } or { Size: true } or { InlineSize: true } or { Style: true })
		{
			throw new InvalidOperationException(
				$"{owner.GetType().Name} cannot establish a CSS containment layout scope.");
		}
		var clipTarget = owner is HtmlCssBoxGrid box
			? box.LayoutRoot
			: owner;
		var padding = owner is HtmlCssBoxGrid cssBox
			? cssBox.Padding
			: default;
		if (normalized == "none")
		{
			Materializations.Add(
				owner,
				new(normalized, clipTarget, padding, null, false, false, false, false, false));
			return;
		}
		if (!containment.Paint)
		{
			Materializations.Add(
				owner,
				new(
					normalized,
					clipTarget,
					padding,
					null,
					containment.Layout,
					containment.Size,
					containment.InlineSize,
					containment.Style,
					false));
			return;
		}
		if (clipTarget.Clip is not null)
			throw new InvalidOperationException(
				$"{owner.GetType().Name} already has a content clip; paint "
					+ "containment cannot replace it.");
		var geometry = new RectangleGeometry();
		var state = new Materialization(
			normalized,
			clipTarget,
			padding,
			geometry,
			containment.Layout,
			containment.Size,
			containment.InlineSize,
			containment.Style,
			true);
		Materializations.Add(owner, state);
		clipTarget.Clip = geometry;
		ClipTargets.Add(clipTarget, state);
		UpdateGeometry(state);
		clipTarget.SizeChanged += OnClipTargetSizeChanged;
	}

	internal static bool TryRead(
		FrameworkElement owner,
		out string value)
	{
		value = string.Empty;
		if (!Materializations.TryGetValue(owner, out var state))
			return false;
		if (owner is HtmlCssBoxGrid box
			&& (box.ContainLayout != state.Layout
				|| box.ContainSize != state.Size
				|| box.ContainInlineSize != state.InlineSize
				|| box.ContainStyle != state.Style
				|| !ReferenceEquals(box, box.LayoutRoot)))
		{
			return false;
		}
		if (state.Geometry is null)
		{
			if (state.Paint)
				return false;
			value = state.Rule;
			return true;
		}
		if (!ReferenceEquals(state.ClipTarget.Clip, state.Geometry))
			return false;
		var expected = CalculateRect(state);
		var actual = state.Geometry.Rect;
		if (!Near(actual.X, expected.X)
			|| !Near(actual.Y, expected.Y)
			|| !Near(actual.Width, expected.Width)
			|| !Near(actual.Height, expected.Height))
		{
			return false;
		}
		value = state.Rule;
		return true;
	}

	private static Containment Parse(string rule)
	{
		if (rule == "none")
			return new(false, false, false, false, false);
		if (rule == "content")
			return new(true, false, false, true, true);
		if (rule == "strict")
			return new(true, true, false, true, true);
		var tokens = rule.Split(
			' ',
			StringSplitOptions.RemoveEmptyEntries
				| StringSplitOptions.TrimEntries);
		if (tokens.Length == 0
			|| tokens.Distinct(StringComparer.Ordinal).Count() != tokens.Length
			|| tokens.Any(static token => token is not (
				"layout" or "size" or "inline-size" or "style" or "paint"))
			|| (tokens.Contains("size", StringComparer.Ordinal)
				&& tokens.Contains("inline-size", StringComparer.Ordinal)))
		{
			throw new InvalidDataException(
				$"Unsupported CSS contain value '{rule}'.");
		}
		return new(
			tokens.Contains("layout", StringComparer.Ordinal),
			tokens.Contains("size", StringComparer.Ordinal),
			tokens.Contains("inline-size", StringComparer.Ordinal),
			tokens.Contains("style", StringComparer.Ordinal),
			tokens.Contains("paint", StringComparer.Ordinal));
	}

	private static void OnClipTargetSizeChanged(
		object sender,
		SizeChangedEventArgs args)
	{
		if (sender is not FrameworkElement target)
			return;
		if (ClipTargets.TryGetValue(target, out var state))
			UpdateGeometry(state);
	}

	private static void UpdateGeometry(Materialization state)
	{
		if (state.Geometry is not null)
			state.Geometry.Rect = CalculateRect(state);
	}

	private static Windows.Foundation.Rect CalculateRect(
		Materialization state)
	{
		var width = state.ClipTarget.ActualWidth > 0
			? state.ClipTarget.ActualWidth
			: double.IsFinite(state.ClipTarget.Width)
				? state.ClipTarget.Width
				: 0;
		var height = state.ClipTarget.ActualHeight > 0
			? state.ClipTarget.ActualHeight
			: double.IsFinite(state.ClipTarget.Height)
				? state.ClipTarget.Height
				: 0;
		return new(
			-state.Padding.Left,
			-state.Padding.Top,
			Math.Max(0, width + state.Padding.Left + state.Padding.Right),
			Math.Max(0, height + state.Padding.Top + state.Padding.Bottom));
	}

	private static bool Near(double left, double right) =>
		Math.Abs(left - right) <= .001;
}

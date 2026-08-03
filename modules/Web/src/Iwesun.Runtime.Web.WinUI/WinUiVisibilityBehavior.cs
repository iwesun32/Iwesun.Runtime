using Microsoft.UI.Xaml;

namespace Iwesun.Runtime.Web.WinUI;

internal static class WinUiVisibilityBehavior
{
	private static readonly DependencyProperty IsHiddenProperty =
		DependencyProperty.RegisterAttached(
			"IsHidden",
			typeof(bool),
			typeof(WinUiVisibilityBehavior),
			new PropertyMetadata(false));
	private static readonly DependencyProperty VisibleOpacityProperty =
		DependencyProperty.RegisterAttached(
			"VisibleOpacity",
			typeof(double),
			typeof(WinUiVisibilityBehavior),
			new PropertyMetadata(1d));

	internal static void Apply(FrameworkElement target, bool hidden)
	{
		ArgumentNullException.ThrowIfNull(target);
		target.SetValue(IsHiddenProperty, hidden);
		var visibleOpacity = target.Opacity;
		target.SetValue(VisibleOpacityProperty, visibleOpacity);
		target.Opacity = hidden ? 0 : visibleOpacity;
		target.IsHitTestVisible = !hidden;
	}

	internal static bool TryRead(
		FrameworkElement target,
		out string visibility)
	{
		ArgumentNullException.ThrowIfNull(target);
		var hidden = (bool)target.GetValue(IsHiddenProperty);
		if (hidden && target.Opacity > 0)
		{
			visibility = string.Empty;
			return false;
		}
		visibility = hidden ? "hidden" : "visible";
		return true;
	}

	internal static bool TryReadOpacity(
		FrameworkElement target,
		out double opacity)
	{
		ArgumentNullException.ThrowIfNull(target);
		var hidden = (bool)target.GetValue(IsHiddenProperty);
		if (hidden && target.Opacity > 0)
		{
			opacity = double.NaN;
			return false;
		}
		opacity = hidden
			? (double)target.GetValue(VisibleOpacityProperty)
			: target.Opacity;
		return double.IsFinite(opacity);
	}
}

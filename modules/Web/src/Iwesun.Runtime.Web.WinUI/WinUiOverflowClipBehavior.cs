using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Iwesun.Runtime.Web.WinUI;

/// <summary>
/// Keeps CSS overflow clipping synchronized with the live WinUI arrange size.
/// A zero-sized pre-layout clip is never installed because it would
/// permanently hide a correctly arranged subtree.
/// </summary>
internal static class WinUiOverflowClipBehavior
{
	private static readonly DependencyProperty IsEnabledProperty =
		DependencyProperty.RegisterAttached(
			"IsEnabled",
			typeof(bool),
			typeof(WinUiOverflowClipBehavior),
			new PropertyMetadata(false));
	private static readonly DependencyProperty OverflowXProperty =
		DependencyProperty.RegisterAttached(
			"OverflowX",
			typeof(string),
			typeof(WinUiOverflowClipBehavior),
			new PropertyMetadata("visible"));
	private static readonly DependencyProperty OverflowYProperty =
		DependencyProperty.RegisterAttached(
			"OverflowY",
			typeof(string),
			typeof(WinUiOverflowClipBehavior),
			new PropertyMetadata("visible"));
	private static readonly DependencyProperty IsPhysicalClipSuppressedProperty =
		DependencyProperty.RegisterAttached(
			"IsPhysicalClipSuppressed",
			typeof(bool),
			typeof(WinUiOverflowClipBehavior),
			new PropertyMetadata(false));

	internal static void SetPhysicalClipSuppressed(
		FrameworkElement target,
		bool suppressed)
	{
		ArgumentNullException.ThrowIfNull(target);
		target.SetValue(IsPhysicalClipSuppressedProperty, suppressed);
		if (!suppressed && (bool)target.GetValue(IsEnabledProperty))
			UpdateClip(target);
	}

	internal static void SetAxes(
		FrameworkElement target,
		string overflowX,
		string overflowY)
	{
		ArgumentNullException.ThrowIfNull(target);
		ArgumentException.ThrowIfNullOrWhiteSpace(overflowX);
		ArgumentException.ThrowIfNullOrWhiteSpace(overflowY);
		target.SetValue(OverflowXProperty, overflowX);
		target.SetValue(OverflowYProperty, overflowY);
		SetEnabled(
			target,
			overflowX is "clip" or "hidden"
				|| overflowY is "clip" or "hidden");
	}

	internal static bool TryRead(
		FrameworkElement target,
		string propertyName,
		out string value)
	{
		ArgumentNullException.ThrowIfNull(target);
		var property = propertyName switch
		{
			"style.overflowX" => OverflowXProperty,
			"style.overflowY" => OverflowYProperty,
			_ => null
		};
		if (property is null)
		{
			value = string.Empty;
			return false;
		}
		value = target.GetValue(property) as string ?? string.Empty;
		return value.Length != 0;
	}

	internal static void SetEnabled(
		FrameworkElement target,
		bool enabled)
	{
		ArgumentNullException.ThrowIfNull(target);
		var current = (bool)target.GetValue(IsEnabledProperty);
		if (current == enabled)
		{
			if (enabled)
				UpdateClip(target);
			return;
		}
		target.SetValue(IsEnabledProperty, enabled);
		if (enabled)
		{
			target.SizeChanged += OnSizeChanged;
			UpdateClip(target);
		}
		else
		{
			target.SizeChanged -= OnSizeChanged;
			target.Clip = null;
		}
	}

	private static void OnSizeChanged(
		object sender,
		SizeChangedEventArgs args)
	{
		if (sender is FrameworkElement target)
			UpdateClip(target);
	}

	private static void UpdateClip(FrameworkElement target)
	{
		if ((bool)target.GetValue(IsPhysicalClipSuppressedProperty))
			return;
		if (target.ActualWidth <= 0 || target.ActualHeight <= 0)
		{
			target.Clip = null;
			return;
		}
		target.Clip = new RectangleGeometry
		{
			Rect = new(
				0,
				0,
				target.ActualWidth,
				target.ActualHeight)
		};
	}
}

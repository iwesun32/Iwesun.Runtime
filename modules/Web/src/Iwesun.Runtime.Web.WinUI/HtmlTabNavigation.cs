using Microsoft.UI.Xaml.Controls;

namespace Iwesun.Runtime.Web.WinUI;

/// <summary>
/// Compile-time-only adapter for native WinUI Control.TabIndex.
/// Non-Control targets are deliberately not accepted.
/// </summary>
internal static class HtmlTabNavigation
{
	internal static void SetTabIndex(Control target, int value)
	{
		ArgumentNullException.ThrowIfNull(target);
		target.TabIndex = value;
	}

	internal static int GetTabIndex(Control target)
	{
		ArgumentNullException.ThrowIfNull(target);
		return target.TabIndex;
	}
}

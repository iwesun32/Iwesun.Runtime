using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;

namespace Iwesun.Runtime.Web.WinUI;

internal sealed record HtmlOutOfFlowPlacementValue(
	bool HasLeft,
	double Left,
	bool HasTop,
	double Top,
	bool HasRight,
	double Right,
	bool HasBottom,
	double Bottom,
	bool HasDefiniteWidth,
	bool HasDefiniteHeight);

internal static class HtmlOutOfFlowPlacementState
{
	private static readonly ConditionalWeakTable<
		FrameworkElement,
		HtmlOutOfFlowPlacementValue> Values = new();

	internal static void Set(
		FrameworkElement element,
		HtmlOutOfFlowPlacementValue value)
	{
		ArgumentNullException.ThrowIfNull(element);
		ArgumentNullException.ThrowIfNull(value);
		Values.Remove(element);
		Values.Add(element, value);
	}

	internal static bool TryGet(
		FrameworkElement element,
		out HtmlOutOfFlowPlacementValue value) =>
		Values.TryGetValue(element, out value!);
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Iwesun.Runtime.Web.WinUI;

internal sealed class HtmlFilteredElementHost : Grid
{
	internal HtmlFilteredElementHost(FrameworkElement innerElement)
	{
		ArgumentNullException.ThrowIfNull(innerElement);
		InnerElement = innerElement;
		HorizontalAlignment = HorizontalAlignment.Stretch;
		VerticalAlignment = VerticalAlignment.Stretch;
		Children.Add(innerElement);
	}

	internal FrameworkElement InnerElement { get; }
}

using Microsoft.UI.Xaml.Controls;

namespace Iwesun.Runtime.Web.WinUI;

/// <summary>
/// One materialized CSS flex line. The outer HtmlCssBoxGrid owns cross-axis
/// line placement; this grid owns the main-axis tracks for the items in one
/// line. It contains target controls, not copied DOM evidence.
/// </summary>
internal sealed class HtmlCssFlexLineGrid : Grid
{
	internal HtmlCssFlexLineGrid(bool vertical) => IsVertical = vertical;

	internal bool IsVertical { get; }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI.Text;

namespace Iwesun.Runtime.Web.WinUI;

/// <summary>
/// Typed target-side storage for inherited CSS text properties on WinUI
/// containers which do not expose Control/TextBlock font properties. Values
/// are DependencyProperties on the rendered target, not source-side evidence.
/// The layout engine additionally propagates them into descendant text visuals.
/// </summary>
internal static class WinUiInheritedTextStyleBehavior
{
	[Flags]
	private enum Applied : ushort
	{
		None = 0,
		Foreground = 1 << 0,
		FontFamily = 1 << 1,
		FontSize = 1 << 2,
		FontWeight = 1 << 3,
		FontStyle = 1 << 4,
		FontStretch = 1 << 5,
		LineHeight = 1 << 6,
		CharacterSpacing = 1 << 7,
		TextAlignment = 1 << 8,
		TextDecorations = 1 << 9,
		WhiteSpace = 1 << 10,
		TextOverflow = 1 << 11
	}

	private static readonly DependencyProperty AppliedProperty = Register("Applied", typeof(int), 0);
	private static readonly DependencyProperty ForegroundProperty = Register("Foreground", typeof(Brush), null);
	private static readonly DependencyProperty FontFamilyProperty = Register("FontFamily", typeof(FontFamily), null);
	private static readonly DependencyProperty FontSizeProperty = Register("FontSize", typeof(double), 0d);
	private static readonly DependencyProperty FontWeightProperty = Register("FontWeight", typeof(FontWeight), default(FontWeight));
	private static readonly DependencyProperty FontStyleProperty = Register("FontStyle", typeof(FontStyle), FontStyle.Normal);
	private static readonly DependencyProperty FontStretchProperty = Register("FontStretch", typeof(FontStretch), FontStretch.Normal);
	private static readonly DependencyProperty LineHeightProperty = Register("LineHeight", typeof(double), 0d);
	private static readonly DependencyProperty CharacterSpacingProperty = Register("CharacterSpacing", typeof(int), 0);
	private static readonly DependencyProperty TextAlignmentProperty = Register("TextAlignment", typeof(TextAlignment), TextAlignment.Left);
	private static readonly DependencyProperty TextDecorationsProperty = Register("TextDecorations", typeof(TextDecorations), TextDecorations.None);
	private static readonly DependencyProperty WhiteSpaceProperty = Register("WhiteSpace", typeof(string), "normal");
	private static readonly DependencyProperty TextOverflowProperty = Register("TextOverflow", typeof(string), "clip");

	internal static void SetForeground(FrameworkElement target, Brush? value) => Set(target, ForegroundProperty, value, Applied.Foreground);
	internal static void SetFontFamily(FrameworkElement target, FontFamily value) => Set(target, FontFamilyProperty, value, Applied.FontFamily);
	internal static void SetFontSize(FrameworkElement target, double value) => Set(target, FontSizeProperty, value, Applied.FontSize);
	internal static void SetFontWeight(FrameworkElement target, FontWeight value) => Set(target, FontWeightProperty, value, Applied.FontWeight);
	internal static void SetFontStyle(FrameworkElement target, FontStyle value) => Set(target, FontStyleProperty, value, Applied.FontStyle);
	internal static void SetFontStretch(FrameworkElement target, FontStretch value) => Set(target, FontStretchProperty, value, Applied.FontStretch);
	internal static void SetLineHeight(FrameworkElement target, double value) => Set(target, LineHeightProperty, value, Applied.LineHeight);
	internal static void SetCharacterSpacing(FrameworkElement target, int value) => Set(target, CharacterSpacingProperty, value, Applied.CharacterSpacing);
	internal static void SetTextAlignment(FrameworkElement target, TextAlignment value) => Set(target, TextAlignmentProperty, value, Applied.TextAlignment);
	internal static void SetTextDecorations(FrameworkElement target, TextDecorations value) => Set(target, TextDecorationsProperty, value, Applied.TextDecorations);
	internal static void SetWhiteSpace(FrameworkElement target, string value) => Set(target, WhiteSpaceProperty, value, Applied.WhiteSpace);
	internal static void SetTextOverflow(FrameworkElement target, string value) => Set(target, TextOverflowProperty, value, Applied.TextOverflow);

	internal static bool TryGetForeground(FrameworkElement target, out Brush? value) => TryGet(target, ForegroundProperty, Applied.Foreground, out value);
	internal static bool TryGetFontFamily(FrameworkElement target, out FontFamily? value) => TryGet(target, FontFamilyProperty, Applied.FontFamily, out value);
	internal static bool TryGetFontSize(FrameworkElement target, out double value) => TryGet(target, FontSizeProperty, Applied.FontSize, out value);
	internal static bool TryGetFontWeight(FrameworkElement target, out FontWeight value) => TryGet(target, FontWeightProperty, Applied.FontWeight, out value);
	internal static bool TryGetFontStyle(FrameworkElement target, out FontStyle value) => TryGet(target, FontStyleProperty, Applied.FontStyle, out value);
	internal static bool TryGetFontStretch(FrameworkElement target, out FontStretch value) => TryGet(target, FontStretchProperty, Applied.FontStretch, out value);
	internal static bool TryGetLineHeight(FrameworkElement target, out double value) => TryGet(target, LineHeightProperty, Applied.LineHeight, out value);
	internal static bool TryGetCharacterSpacing(FrameworkElement target, out int value) => TryGet(target, CharacterSpacingProperty, Applied.CharacterSpacing, out value);
	internal static bool TryGetTextAlignment(FrameworkElement target, out TextAlignment value) => TryGet(target, TextAlignmentProperty, Applied.TextAlignment, out value);
	internal static bool TryGetTextDecorations(FrameworkElement target, out TextDecorations value) => TryGet(target, TextDecorationsProperty, Applied.TextDecorations, out value);
	internal static bool TryGetWhiteSpace(FrameworkElement target, out string? value) => TryGet(target, WhiteSpaceProperty, Applied.WhiteSpace, out value);
	internal static bool TryGetTextOverflow(FrameworkElement target, out string? value) => TryGet(target, TextOverflowProperty, Applied.TextOverflow, out value);

	private static void Set(FrameworkElement target, DependencyProperty property, object? value, Applied flag)
	{
		target.SetValue(property, value);
		target.SetValue(AppliedProperty, (int)((Applied)(int)target.GetValue(AppliedProperty) | flag));
	}

	private static bool TryGet<T>(FrameworkElement target, DependencyProperty property, Applied flag, out T value)
	{
		if ((((Applied)(int)target.GetValue(AppliedProperty)) & flag) == 0)
		{
			value = default!;
			return false;
		}
		value = (T)target.GetValue(property);
		return true;
	}

	private static DependencyProperty Register(string name, Type type, object? value) =>
		DependencyProperty.RegisterAttached(name, type, typeof(WinUiInheritedTextStyleBehavior), new PropertyMetadata(value));
}

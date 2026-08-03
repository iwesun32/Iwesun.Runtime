using Microsoft.UI.Xaml;

namespace Iwesun.Runtime.Web.WinUI;

/// <summary>
/// Legacy string evidence store. Formal rendering and XamlFill must use the
/// live properties of the target WinUI control instead.
/// </summary>
[Obsolete(
	"String text-style evidence cannot be used as live WinUI runtime state.",
	true)]
internal static class WinUiAppliedTextStyle
{
	internal static readonly DependencyProperty ColorProperty =
		Register("Color");
	internal static readonly DependencyProperty FontFamilyProperty =
		Register("FontFamily");
	internal static readonly DependencyProperty FontSizeProperty =
		Register("FontSize");
	internal static readonly DependencyProperty FontWeightProperty =
		Register("FontWeight");
	internal static readonly DependencyProperty FontStyleProperty =
		Register("FontStyle");
	internal static readonly DependencyProperty LineHeightProperty =
		Register("LineHeight");
	internal static readonly DependencyProperty LetterSpacingProperty =
		Register("LetterSpacing");
	internal static readonly DependencyProperty TextAlignProperty =
		Register("TextAlign");
	internal static readonly DependencyProperty TextDecorationLineProperty =
		Register("TextDecorationLine");
	internal static readonly DependencyProperty WhiteSpaceProperty =
		Register("WhiteSpace");

	internal static void Set(
		DependencyObject target,
		string propertyName,
		string value)
	{
		ArgumentNullException.ThrowIfNull(target);
		ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
		target.SetValue(Resolve(propertyName), value);
	}

	internal static bool TryGet(
		DependencyObject target,
		string propertyName,
		out string value)
	{
		ArgumentNullException.ThrowIfNull(target);
		ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
		value = target.GetValue(Resolve(propertyName)) as string
			?? string.Empty;
		return value.Length != 0;
	}

	private static DependencyProperty Resolve(string propertyName) =>
		propertyName switch
		{
			"style.color" => ColorProperty,
			"style.fontFamily" => FontFamilyProperty,
			"style.fontSize" => FontSizeProperty,
			"style.fontWeight" => FontWeightProperty,
			"style.fontStyle" => FontStyleProperty,
			"style.lineHeight" => LineHeightProperty,
			"style.letterSpacing" => LetterSpacingProperty,
			"style.textAlign" => TextAlignProperty,
			"style.textDecorationLine" => TextDecorationLineProperty,
			"style.whiteSpace" => WhiteSpaceProperty,
			_ => throw new ArgumentOutOfRangeException(
				nameof(propertyName),
				propertyName,
				"Unsupported target-side text style property.")
		};

	private static DependencyProperty Register(string name) =>
		DependencyProperty.RegisterAttached(
			name,
			typeof(string),
			typeof(WinUiAppliedTextStyle),
			new PropertyMetadata(string.Empty));
}

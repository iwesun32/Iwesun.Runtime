using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI.Text;

namespace Iwesun.Runtime.Web.WinUI;

/// <summary>
/// Strong WinUI target for HTML/SVG nodes whose selected projection is a
/// Canvas. Cursor support belongs to the concrete target because WinUI exposes
/// ProtectedCursor only to derived controls.
/// </summary>
internal sealed class HtmlCanvasPanel : Canvas, IHtmlCursorTarget
{
	private string _cursorRule = "auto";
	internal static readonly DependencyProperty ForegroundProperty =
		Register(nameof(Foreground), typeof(Brush), null);
	internal static readonly DependencyProperty FontFamilyProperty =
		Register(nameof(FontFamily), typeof(FontFamily), new FontFamily("Segoe UI"));
	internal static readonly DependencyProperty FontSizeProperty =
		Register(nameof(FontSize), typeof(double), 14d);
	internal static readonly DependencyProperty FontWeightProperty =
		Register(nameof(FontWeight), typeof(FontWeight), new FontWeight { Weight = 400 });
	internal static readonly DependencyProperty FontStyleProperty =
		Register(nameof(FontStyle), typeof(FontStyle), FontStyle.Normal);
	internal static readonly DependencyProperty FontStretchProperty =
		Register(nameof(FontStretch), typeof(FontStretch), FontStretch.Normal);
	internal static readonly DependencyProperty TextLineHeightProperty =
		Register(nameof(TextLineHeight), typeof(double), 0d);
	internal static readonly DependencyProperty CharacterSpacingProperty =
		Register(nameof(CharacterSpacing), typeof(int), 0);
	internal static readonly DependencyProperty TextAlignmentProperty =
		Register(nameof(TextAlignment), typeof(TextAlignment), TextAlignment.Left);
	internal static readonly DependencyProperty TextDecorationsProperty =
		Register(nameof(TextDecorations), typeof(TextDecorations), TextDecorations.None);
	internal static readonly DependencyProperty WhiteSpaceProperty =
		Register(nameof(WhiteSpace), typeof(string), "normal");
	internal static readonly DependencyProperty TextOverflowProperty =
		Register(nameof(TextOverflow), typeof(string), "clip");

	internal Brush? Foreground
	{
		get => (Brush?)GetValue(ForegroundProperty);
		set => SetValue(ForegroundProperty, value);
	}

	internal FontFamily FontFamily
	{
		get => (FontFamily)GetValue(FontFamilyProperty);
		set => SetValue(FontFamilyProperty, value);
	}

	internal double FontSize
	{
		get => (double)GetValue(FontSizeProperty);
		set => SetValue(FontSizeProperty, value);
	}

	internal FontWeight FontWeight
	{
		get => (FontWeight)GetValue(FontWeightProperty);
		set => SetValue(FontWeightProperty, value);
	}

	internal FontStyle FontStyle
	{
		get => (FontStyle)GetValue(FontStyleProperty);
		set => SetValue(FontStyleProperty, value);
	}

	internal FontStretch FontStretch
	{
		get => (FontStretch)GetValue(FontStretchProperty);
		set => SetValue(FontStretchProperty, value);
	}

	internal double TextLineHeight
	{
		get => (double)GetValue(TextLineHeightProperty);
		set => SetValue(TextLineHeightProperty, value);
	}

	internal int CharacterSpacing
	{
		get => (int)GetValue(CharacterSpacingProperty);
		set => SetValue(CharacterSpacingProperty, value);
	}

	internal TextAlignment TextAlignment
	{
		get => (TextAlignment)GetValue(TextAlignmentProperty);
		set => SetValue(TextAlignmentProperty, value);
	}

	internal TextDecorations TextDecorations
	{
		get => (TextDecorations)GetValue(TextDecorationsProperty);
		set => SetValue(TextDecorationsProperty, value);
	}

	internal string WhiteSpace
	{
		get => (string)GetValue(WhiteSpaceProperty);
		set => SetValue(WhiteSpaceProperty, value);
	}

	internal string TextOverflow
	{
		get => (string)GetValue(TextOverflowProperty);
		set => SetValue(TextOverflowProperty, value);
	}

	public void ApplyCursor(string rule)
	{
		var cursor = HtmlCursorContract.Create(rule);
		_cursorRule = cursor.Rule;
		ProtectedCursor = cursor.Cursor;
	}

	public bool TryReadCursor(out string value)
	{
		value = string.Empty;
		if (!HtmlCursorContract.Matches(_cursorRule, ProtectedCursor))
			return false;
		value = _cursorRule;
		return true;
	}

	private static DependencyProperty Register(
		string name,
		Type propertyType,
		object? defaultValue) =>
		DependencyProperty.Register(
			name,
			propertyType,
			typeof(HtmlCanvasPanel),
			new PropertyMetadata(defaultValue));
}

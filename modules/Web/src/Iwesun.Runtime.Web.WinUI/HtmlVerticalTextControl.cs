using System.Globalization;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Iwesun.Runtime.Web.WinUI;

internal sealed class HtmlVerticalTextControl : HtmlCursorContentControl
{
	private readonly VerticalGlyphPanel _panel = new();

	internal HtmlVerticalTextControl()
	{
		HorizontalContentAlignment = HorizontalAlignment.Stretch;
		VerticalContentAlignment = VerticalAlignment.Stretch;
		Content = _panel;
		RefreshPanel();
	}

	internal static readonly DependencyProperty TextProperty =
		DependencyProperty.Register(
			nameof(Text),
			typeof(string),
			typeof(HtmlVerticalTextControl),
			new PropertyMetadata(string.Empty, OnLayoutPropertyChanged));

	internal static readonly DependencyProperty WritingModeProperty =
		DependencyProperty.Register(
			nameof(WritingMode),
			typeof(string),
			typeof(HtmlVerticalTextControl),
			new PropertyMetadata("vertical-rl", OnLayoutPropertyChanged));

	internal static readonly DependencyProperty TextIndentProperty =
		DependencyProperty.Register(
			nameof(TextIndent),
			typeof(double),
			typeof(HtmlVerticalTextControl),
			new PropertyMetadata(0d, OnLayoutPropertyChanged));

	internal static readonly DependencyProperty WordSpacingProperty =
		DependencyProperty.Register(
			nameof(WordSpacing),
			typeof(double),
			typeof(HtmlVerticalTextControl),
			new PropertyMetadata(0d, OnLayoutPropertyChanged));

	internal static readonly DependencyProperty TextLineHeightProperty =
		DependencyProperty.Register(nameof(TextLineHeight), typeof(double), typeof(HtmlVerticalTextControl), new PropertyMetadata(0d, OnLayoutPropertyChanged));
	internal static readonly DependencyProperty TextAlignmentProperty =
		DependencyProperty.Register(nameof(TextAlignment), typeof(TextAlignment), typeof(HtmlVerticalTextControl), new PropertyMetadata(TextAlignment.Left, OnLayoutPropertyChanged));
	internal static readonly DependencyProperty TextDecorationsProperty =
		DependencyProperty.Register(nameof(TextDecorations), typeof(Windows.UI.Text.TextDecorations), typeof(HtmlVerticalTextControl), new PropertyMetadata(Windows.UI.Text.TextDecorations.None, OnLayoutPropertyChanged));
	internal static readonly DependencyProperty WhiteSpaceProperty =
		DependencyProperty.Register(nameof(WhiteSpace), typeof(string), typeof(HtmlVerticalTextControl), new PropertyMetadata("normal", OnLayoutPropertyChanged));
	internal static readonly DependencyProperty TextOverflowProperty =
		DependencyProperty.Register(nameof(TextOverflow), typeof(string), typeof(HtmlVerticalTextControl), new PropertyMetadata("clip", OnLayoutPropertyChanged));

	public string Text
	{
		get => (string)GetValue(TextProperty);
		set => SetValue(TextProperty, value);
	}

	public string WritingMode
	{
		get => (string)GetValue(WritingModeProperty);
		set => SetValue(WritingModeProperty, value);
	}

	public double TextIndent
	{
		get => (double)GetValue(TextIndentProperty);
		set => SetValue(TextIndentProperty, value);
	}

	public double WordSpacing
	{
		get => (double)GetValue(WordSpacingProperty);
		set => SetValue(WordSpacingProperty, value);
	}
	public double TextLineHeight { get => (double)GetValue(TextLineHeightProperty); set => SetValue(TextLineHeightProperty, value); }
	public TextAlignment TextAlignment { get => (TextAlignment)GetValue(TextAlignmentProperty); set => SetValue(TextAlignmentProperty, value); }
	public Windows.UI.Text.TextDecorations TextDecorations { get => (Windows.UI.Text.TextDecorations)GetValue(TextDecorationsProperty); set => SetValue(TextDecorationsProperty, value); }
	public string WhiteSpace { get => (string)GetValue(WhiteSpaceProperty); set => SetValue(WhiteSpaceProperty, value); }
	public string TextOverflow { get => (string)GetValue(TextOverflowProperty); set => SetValue(TextOverflowProperty, value); }

	internal bool HasMaterializedGlyphs =>
		Text.Length == 0 || _panel.Children.Count > 0;

	internal IReadOnlyList<TextBlock> MaterializedGlyphs =>
		_panel.Children.OfType<TextBlock>().ToArray();

	internal void RefreshPanel()
	{
		_panel.Configure(
			Text,
			WritingMode,
			TextIndent,
			WordSpacing,
			FontFamily,
			FontSize,
			FontWeight,
			FontStyle,
			FontStretch,
			Foreground,
			CharacterSpacing,
			TextLineHeight,
			TextAlignment,
			TextDecorations,
			WhiteSpace);
	}

	private static void OnLayoutPropertyChanged(
		DependencyObject sender,
		DependencyPropertyChangedEventArgs args)
	{
		if (sender is HtmlVerticalTextControl control)
			control.RefreshPanel();
	}

	private sealed class VerticalGlyphPanel : Panel
	{
		private string _writingMode = "vertical-rl";
		private double _indent;
		private double _wordSpacing;
		private double _lineHeight;
		private TextAlignment _textAlignment;
		private string _whiteSpace = "normal";
		private readonly List<bool> _breakBefore = [];

		internal void Configure(
			string text,
			string writingMode,
			double indent,
			double wordSpacing,
			FontFamily fontFamily,
			double fontSize,
			Windows.UI.Text.FontWeight fontWeight,
			Windows.UI.Text.FontStyle fontStyle,
			Windows.UI.Text.FontStretch fontStretch,
			Brush foreground,
			int characterSpacing,
			double lineHeight,
			TextAlignment textAlignment,
			Windows.UI.Text.TextDecorations textDecorations,
			string whiteSpace)
		{
			_writingMode = writingMode;
			_indent = indent;
			_wordSpacing = wordSpacing;
			_lineHeight = lineHeight;
			_textAlignment = textAlignment;
			_whiteSpace = whiteSpace;
			Children.Clear();
			_breakBefore.Clear();
			var enumerator = StringInfo.GetTextElementEnumerator(text);
			var breakBefore = false;
			while (enumerator.MoveNext())
			{
				var glyph = enumerator.GetTextElement();
				if (glyph is "\r")
					continue;
				if (glyph is "\n")
				{
					breakBefore = true;
					continue;
				}
				var textBlock = new TextBlock
				{
					Text = glyph,
					FontFamily = fontFamily,
					FontSize = fontSize,
					FontWeight = fontWeight,
					FontStyle = fontStyle,
					FontStretch = fontStretch,
					Foreground = foreground,
					CharacterSpacing = characterSpacing,
					TextAlignment = TextAlignment.Center,
					TextDecorations = textDecorations
				};
				if (ShouldRotateMixedGlyph(glyph))
				{
					textBlock.RenderTransform = new RotateTransform { Angle = 90 };
					textBlock.RenderTransformOrigin = new(.5, .5);
				}
				Children.Add(textBlock);
				_breakBefore.Add(breakBefore);
				breakBefore = false;
			}
			InvalidateMeasure();
		}

		protected override Size MeasureOverride(Size availableSize)
		{
			var finiteHeight = double.IsFinite(availableSize.Height)
				? Math.Max(0, availableSize.Height)
				: double.PositiveInfinity;
			var columnHeight = 0d;
			var maximumColumnHeight = 0d;
			var columnWidth = 0d;
			var totalWidth = 0d;
			for (var index = 0; index < Children.Count; index++)
			{
				var child = Children[index];
				child.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
				var extent = Math.Max(child.DesiredSize.Height, _lineHeight)
					+ (IsWhitespace(child) ? _wordSpacing : 0);
				if (_breakBefore[index]
					|| columnHeight > 0 && columnHeight + extent > finiteHeight)
				{
					totalWidth += columnWidth;
					maximumColumnHeight = Math.Max(maximumColumnHeight, columnHeight);
					columnHeight = 0;
					columnWidth = 0;
				}
				if (index == 0)
					columnHeight += _indent;
				columnHeight += extent;
				columnWidth = Math.Max(columnWidth, child.DesiredSize.Width);
			}
			totalWidth += columnWidth;
			maximumColumnHeight = Math.Max(maximumColumnHeight, columnHeight);
			return new(totalWidth, maximumColumnHeight);
		}

		protected override Size ArrangeOverride(Size finalSize)
		{
			var columns = BuildColumns(finalSize.Height);
			var x = _writingMode == "vertical-rl" ? finalSize.Width : 0;
			foreach (var column in columns)
			{
				var width = column.Max(static item => item.Child.DesiredSize.Width);
				if (_writingMode == "vertical-rl")
					x -= width;
				var contentHeight = column.Sum(item =>
					Math.Max(item.Child.DesiredSize.Height, _lineHeight)
					+ (IsWhitespace(item.Child) ? _wordSpacing : 0));
				var y = _textAlignment switch
				{
					TextAlignment.Center => Math.Max(0, (finalSize.Height - contentHeight) / 2),
					TextAlignment.Right => Math.Max(0, finalSize.Height - contentHeight),
					_ => 0
				};
				if (column[0].Index == 0)
					y += _indent;
				foreach (var item in column)
				{
					var height = Math.Max(item.Child.DesiredSize.Height, _lineHeight);
					item.Child.Arrange(new(x, y, width, height));
					y += height + (IsWhitespace(item.Child) ? _wordSpacing : 0);
				}
				if (_writingMode != "vertical-rl")
					x += width;
			}
			return finalSize;
		}

		private IReadOnlyList<List<(UIElement Child, int Index)>> BuildColumns(double height)
		{
			var result = new List<List<(UIElement, int)>>
			{
				new()
			};
			var used = 0d;
			for (var index = 0; index < Children.Count; index++)
			{
				var child = Children[index];
				var extent = Math.Max(child.DesiredSize.Height, _lineHeight) + (IsWhitespace(child) ? _wordSpacing : 0);
				var mayWrap = !_whiteSpace.Contains("nowrap", StringComparison.OrdinalIgnoreCase);
				if ((_breakBefore[index] || mayWrap && used > 0 && used + extent > height)
					&& result[^1].Count > 0)
				{
					result.Add([]);
					used = 0;
				}
				result[^1].Add((child, index));
				used += extent;
			}
			return result;
		}

		private static bool IsWhitespace(UIElement element) =>
			element is TextBlock text && string.IsNullOrWhiteSpace(text.Text);

		private static bool ShouldRotateMixedGlyph(string glyph)
		{
			var rune = glyph.EnumerateRunes().FirstOrDefault();
			return rune.Value is >= 0x0021 and <= 0x007E;
		}
	}
}

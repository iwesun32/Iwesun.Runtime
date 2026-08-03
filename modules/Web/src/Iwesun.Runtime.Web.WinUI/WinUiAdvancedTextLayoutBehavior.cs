using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;

namespace Iwesun.Runtime.Web.WinUI;

internal static class WinUiAdvancedTextLayoutBehavior
{
	private sealed class TextLayoutState
	{
		internal double Indent;
		internal double WordSpacing;
		internal string SourceText = string.Empty;
		internal bool Rebuilding;
	}

	private static readonly ConditionalWeakTable<TextBlock, TextLayoutState>
		TextStates = new();

	internal static readonly DependencyProperty WritingModeProperty =
		DependencyProperty.RegisterAttached(
			"WritingMode",
			typeof(string),
			typeof(WinUiAdvancedTextLayoutBehavior),
			new PropertyMetadata(string.Empty));

	internal static readonly DependencyProperty TextIndentProperty =
		DependencyProperty.RegisterAttached(
			"TextIndent",
			typeof(string),
			typeof(WinUiAdvancedTextLayoutBehavior),
			new PropertyMetadata(string.Empty));

	internal static readonly DependencyProperty WordSpacingProperty =
		DependencyProperty.RegisterAttached(
			"WordSpacing",
			typeof(string),
			typeof(WinUiAdvancedTextLayoutBehavior),
			new PropertyMetadata(string.Empty));

	internal static void ApplyWritingMode(
		FrameworkElement target,
		string value)
	{
		ArgumentNullException.ThrowIfNull(target);
		var normalized = value.Trim().ToLowerInvariant();
		if (normalized is "vertical-rl" or "vertical-lr"
			&& target is HtmlVerticalTextControl vertical)
		{
			vertical.WritingMode = normalized;
		}
		else if (normalized != "horizontal-tb")
		{
			throw new InvalidDataException(
				$"CSS writing-mode '{value}' requires a vertical glyph-layout target; rotation is not an equivalent implementation.");
		}
		target.SetValue(WritingModeProperty, normalized);
	}

	internal static void ApplyTextIndent(
		FrameworkElement target,
		string value)
	{
		ArgumentNullException.ThrowIfNull(target);
		var pixels = ParsePixels("text-indent", value, allowNormal: false);
		if (target is RichTextBlock richText)
			richText.TextIndent = pixels;
		else if (target is HtmlVerticalTextControl vertical)
			vertical.TextIndent = pixels;
		else if (target is TextBlock text)
		{
			var state = GetTextState(text);
			state.Indent = pixels;
			RebuildTextInlines(text, state);
		}
		else if (pixels != 0)
		{
			throw new InvalidDataException(
				$"CSS text-indent '{value}' requires a TextBlock or RichTextBlock target.");
		}
		target.SetValue(TextIndentProperty, CssPixels(pixels));
	}

	internal static void ApplyWordSpacing(
		FrameworkElement target,
		string value)
	{
		ArgumentNullException.ThrowIfNull(target);
		var pixels = ParsePixels("word-spacing", value, allowNormal: true);
		if (target is TextBlock text)
		{
			var state = GetTextState(text);
			state.WordSpacing = pixels;
			RebuildTextInlines(text, state);
		}
		else if (target is HtmlVerticalTextControl vertical)
		{
			vertical.WordSpacing = pixels;
		}
		else if (pixels != 0)
		{
			throw new InvalidDataException(
				$"CSS word-spacing '{value}' requires a TextBlock glyph-run target.");
		}
		target.SetValue(WordSpacingProperty, CssPixels(pixels));
	}

	internal static bool TryRead(
		FrameworkElement target,
		DependencyProperty property,
		out string value)
	{
		ArgumentNullException.ThrowIfNull(target);
		value = string.Empty;
		if (target.ReadLocalValue(property) == DependencyProperty.UnsetValue
			|| target.GetValue(property) is not string actual
			|| actual.Length == 0)
		{
			return false;
		}
		if (property == TextIndentProperty)
		{
			var expected = ParsePixels("text-indent", actual, allowNormal: false);
			if (target is RichTextBlock richText
				&& Math.Abs(richText.TextIndent - expected) > .001)
				return false;
			if (target is TextBlock text
				&& !HasMaterializedIndent(text, expected))
				return false;
			if (target is HtmlVerticalTextControl vertical
				&& Math.Abs(vertical.TextIndent - expected) > .001)
				return false;
		}
		else if (property == WordSpacingProperty
			&& target is TextBlock text
			&& !HasMaterializedWordSpacing(text, ParsePixels("word-spacing", actual, true)))
		{
			return false;
		}
		else if (property == WordSpacingProperty
			&& target is HtmlVerticalTextControl vertical
			&& Math.Abs(vertical.WordSpacing - ParsePixels("word-spacing", actual, true)) > .001)
		{
			return false;
		}
		else if (property == WritingModeProperty
			&& target is HtmlVerticalTextControl verticalWriting
			&& (!verticalWriting.WritingMode.Equals(actual, StringComparison.Ordinal)
				|| !verticalWriting.HasMaterializedGlyphs))
		{
			return false;
		}
		value = actual;
		return true;
	}

	private static TextLayoutState GetTextState(TextBlock text)
	{
		if (TextStates.TryGetValue(text, out var state))
			return state;
		state = new() { SourceText = text.Text ?? string.Empty };
		TextStates.Add(text, state);
		text.RegisterPropertyChangedCallback(
			TextBlock.TextProperty,
			(sender, _) =>
			{
				if (sender is not TextBlock changed
					|| !TextStates.TryGetValue(changed, out var current)
					|| current.Rebuilding)
					return;
				current.SourceText = changed.Text ?? string.Empty;
				RebuildTextInlines(changed, current);
			});
		return state;
	}

	private static void RebuildTextInlines(TextBlock text, TextLayoutState state)
	{
		state.Rebuilding = true;
		try
		{
			text.Inlines.Clear();
			if (state.Indent != 0)
			{
				var em = text.FontSize > 0 ? text.FontSize : 14;
				text.Inlines.Add(new Run
				{
					Text = "\u200B",
					CharacterSpacing = (int)Math.Round(
						state.Indent / em * 1000,
						MidpointRounding.AwayFromZero)
				});
			}
			var pieces = System.Text.RegularExpressions.Regex.Split(
				state.SourceText,
				@"(\s+)");
			foreach (var piece in pieces.Where(static piece => piece.Length > 0))
			{
				var run = new Run { Text = piece };
				if (piece.All(char.IsWhiteSpace))
				{
					var em = text.FontSize > 0 ? text.FontSize : 14;
					run.CharacterSpacing = text.CharacterSpacing + (int)Math.Round(
						state.WordSpacing / em * 1000,
						MidpointRounding.AwayFromZero);
				}
				text.Inlines.Add(run);
			}
		}
		finally
		{
			state.Rebuilding = false;
		}
	}

	private static bool HasMaterializedIndent(TextBlock text, double expected) =>
		expected == 0
			? true
			: text.Inlines.FirstOrDefault() is Run run
				&& run.Text == "\u200B"
				&& run.CharacterSpacing == (int)Math.Round(
					expected / (text.FontSize > 0 ? text.FontSize : 14) * 1000,
					MidpointRounding.AwayFromZero);

	private static bool HasMaterializedWordSpacing(TextBlock text, double expected)
	{
		var em = text.FontSize > 0 ? text.FontSize : 14;
		var characterSpacing = text.CharacterSpacing
			+ (int)Math.Round(expected / em * 1000, MidpointRounding.AwayFromZero);
		return text.Inlines.OfType<Run>()
			.Where(static run => (run.Text ?? string.Empty).All(char.IsWhiteSpace))
			.All(run => run.CharacterSpacing == characterSpacing);
	}

	private static double ParsePixels(string propertyName, string value, bool allowNormal)
	{
		var normalized = value.Trim().ToLowerInvariant();
		if (allowNormal && normalized == "normal")
			return 0;
		var candidate = normalized.EndsWith("px", StringComparison.Ordinal)
			? normalized[..^2]
			: normalized;
		if (!double.TryParse(candidate, NumberStyles.Float, CultureInfo.InvariantCulture, out var pixels)
			|| !double.IsFinite(pixels))
		{
			throw new InvalidDataException($"CSS {propertyName} '{value}' is not a resolved pixel length.");
		}
		return pixels;
	}

	private static string CssPixels(double value) =>
		$"{value.ToString("R", CultureInfo.InvariantCulture)}px";
}

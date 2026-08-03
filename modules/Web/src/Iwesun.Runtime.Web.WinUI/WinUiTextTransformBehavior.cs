using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Iwesun.Runtime.Web.WinUI;

internal static partial class WinUiTextTransformBehavior
{
	private sealed record Materialization(
		string Rule,
		string? OriginalText,
		FrameworkElement? PhysicalTarget);

	private static readonly ConditionalWeakTable<
		FrameworkElement,
		Materialization> Materializations = new();

	internal static void Apply(
		FrameworkElement owner,
		string rule,
		string generatedTextMarker)
	{
		ArgumentNullException.ThrowIfNull(owner);
		var normalized = NormalizeRule(rule);
		var physicalTarget = ResolvePhysicalTarget(owner, generatedTextMarker);
		var original = physicalTarget is null
			? null
			: ReadText(physicalTarget);
		if (physicalTarget is not null && original is not null)
			WriteText(physicalTarget, Transform(original, normalized));
		Materializations.Remove(owner);
		Materializations.Add(
			owner,
			new(normalized, original, physicalTarget));
	}

	internal static bool TryRead(
		FrameworkElement owner,
		out string value)
	{
		value = string.Empty;
		if (!Materializations.TryGetValue(owner, out var state))
			return false;
		if (state.PhysicalTarget is not null
			&& state.OriginalText is not null
			&& !string.Equals(
				ReadText(state.PhysicalTarget),
				Transform(state.OriginalText, state.Rule),
				StringComparison.Ordinal))
		{
			return false;
		}
		value = state.Rule;
		return true;
	}

	internal static bool TryReadOriginalText(
		FrameworkElement owner,
		out string value)
	{
		value = string.Empty;
		if (!Materializations.TryGetValue(owner, out var state)
			|| state.OriginalText is null
			|| !TryRead(owner, out _))
		{
			return false;
		}
		value = state.OriginalText;
		return true;
	}

	private static FrameworkElement? ResolvePhysicalTarget(
		FrameworkElement owner,
		string generatedTextMarker)
	{
		if (ReadText(owner) is not null)
			return owner;
		return Enumerate(owner)
			.OfType<TextBlock>()
			.FirstOrDefault(text => string.Equals(
				text.Tag as string,
				generatedTextMarker,
				StringComparison.Ordinal));
	}

	private static IEnumerable<DependencyObject> Enumerate(
		DependencyObject root)
	{
		for (var index = 0;
			index < Microsoft.UI.Xaml.Media.VisualTreeHelper
				.GetChildrenCount(root);
			index++)
		{
			var child = Microsoft.UI.Xaml.Media.VisualTreeHelper
				.GetChild(root, index);
			yield return child;
			foreach (var descendant in Enumerate(child))
				yield return descendant;
		}
	}

	private static string? ReadText(FrameworkElement target) => target switch
	{
		TextBlock text => text.Text,
		HtmlSemanticTextControl semantic => semantic.Text,
		HtmlSpanBoxControl span => span.Text,
		HtmlVerticalTextControl vertical => vertical.Text,
		TextBox textBox => textBox.Text,
		ContentControl { Content: string content } => content,
		_ => null
	};

	private static void WriteText(FrameworkElement target, string value)
	{
		switch (target)
		{
			case TextBlock text:
				text.Text = value;
				break;
			case HtmlSemanticTextControl semantic:
				semantic.Text = value;
				break;
			case HtmlSpanBoxControl span:
				span.Text = value;
				break;
			case HtmlVerticalTextControl vertical:
				vertical.Text = value;
				break;
			case TextBox textBox:
				textBox.Text = value;
				break;
			case ContentControl content:
				content.Content = value;
				break;
			default:
				throw new InvalidOperationException(
					$"{target.GetType().Name} is not a text target.");
		}
	}

	private static string NormalizeRule(string value) =>
		value.Trim().ToLowerInvariant() switch
		{
			"none" => "none",
			"uppercase" => "uppercase",
			"lowercase" => "lowercase",
			"capitalize" => "capitalize",
			_ => throw new InvalidDataException(
				$"Unsupported CSS text-transform '{value}'.")
		};

	private static string Transform(string text, string rule) => rule switch
	{
		"uppercase" => text.ToUpperInvariant(),
		"lowercase" => text.ToLowerInvariant(),
		"capitalize" => WordStartRegex().Replace(
			text,
			static match => match.Value.ToUpperInvariant()),
		_ => text
	};

	[GeneratedRegex(@"(?<![\p{L}\p{N}])\p{L}")]
	private static partial Regex WordStartRegex();
}

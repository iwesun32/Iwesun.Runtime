using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Iwesun.Runtime.Web.WinUI;

internal static class WinUiLineBreakingBehavior
{
	private sealed record Materialization(
		string WhiteSpace,
		string OverflowWrap,
		string WordBreak,
		FrameworkElement? PhysicalTarget,
		TextWrapping ExpectedWrapping);

	private static readonly ConditionalWeakTable<
		FrameworkElement,
		Materialization> Materializations = new();

	internal static void Apply(
		FrameworkElement owner,
		string whiteSpace,
		string overflowWrap,
		string wordBreak,
		string generatedTextMarker)
	{
		ArgumentNullException.ThrowIfNull(owner);
		var normalizedWhiteSpace = NormalizeWhiteSpace(whiteSpace);
		var normalizedOverflowWrap = NormalizeOverflowWrap(overflowWrap);
		var normalizedWordBreak = NormalizeWordBreak(wordBreak);
		var target = ResolvePhysicalTarget(owner, generatedTextMarker);
		var wrapping = ResolveWrapping(
			normalizedWhiteSpace,
			normalizedOverflowWrap,
			normalizedWordBreak);
		if (target is not null)
			SetWrapping(target, wrapping);
		Materializations.Remove(owner);
		Materializations.Add(
			owner,
			new(
				normalizedWhiteSpace,
				normalizedOverflowWrap,
				normalizedWordBreak,
				target,
				wrapping));
	}

	internal static bool TryRead(
		FrameworkElement owner,
		string propertyName,
		out string value)
	{
		value = string.Empty;
		if (!Materializations.TryGetValue(owner, out var state))
			return false;
		if (state.PhysicalTarget is not null
			&& (!TryGetWrapping(state.PhysicalTarget, out var actual)
				|| actual != state.ExpectedWrapping))
		{
			return false;
		}
		value = propertyName switch
		{
			"style.overflowWrap" => state.OverflowWrap,
			"style.wordBreak" => state.WordBreak,
			_ => string.Empty
		};
		return value.Length != 0;
	}

	private static FrameworkElement? ResolvePhysicalTarget(
		FrameworkElement owner,
		string generatedTextMarker)
	{
		if (TryGetWrapping(owner, out _))
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
			index < VisualTreeHelper.GetChildrenCount(root);
			index++)
		{
			var child = VisualTreeHelper.GetChild(root, index);
			yield return child;
			foreach (var descendant in Enumerate(child))
				yield return descendant;
		}
	}

	private static bool TryGetWrapping(
		FrameworkElement target,
		out TextWrapping wrapping)
	{
		switch (target)
		{
			case TextBlock text:
				wrapping = text.TextWrapping;
				return true;
			case TextBox textBox:
				wrapping = textBox.TextWrapping;
				return true;
			default:
				wrapping = default;
				return false;
		}
	}

	private static void SetWrapping(
		FrameworkElement target,
		TextWrapping wrapping)
	{
		switch (target)
		{
			case TextBlock text:
				text.TextWrapping = wrapping;
				break;
			case TextBox textBox:
				textBox.TextWrapping = wrapping;
				break;
			default:
				throw new InvalidOperationException(
					$"{target.GetType().Name} has no TextWrapping target.");
		}
	}

	private static TextWrapping ResolveWrapping(
		string whiteSpace,
		string overflowWrap,
		string wordBreak)
	{
		if (whiteSpace is "nowrap" or "pre")
			return TextWrapping.NoWrap;
		if (wordBreak is "break-all" or "break-word"
			|| overflowWrap is "anywhere" or "break-word")
		{
			return TextWrapping.Wrap;
		}
		return TextWrapping.WrapWholeWords;
	}

	private static string NormalizeWhiteSpace(string value) =>
		value.Trim().ToLowerInvariant() switch
		{
			"normal" => "normal",
			"nowrap" => "nowrap",
			"pre" => "pre",
			"pre-wrap" => "pre-wrap",
			"pre-line" => "pre-line",
			"break-spaces" => "break-spaces",
			_ => throw new InvalidDataException(
				$"Unsupported CSS white-space '{value}'.")
		};

	private static string NormalizeOverflowWrap(string value) =>
		value.Trim().ToLowerInvariant() switch
		{
			"normal" => "normal",
			"break-word" => "break-word",
			"anywhere" => "anywhere",
			_ => throw new InvalidDataException(
				$"Unsupported CSS overflow-wrap '{value}'.")
		};

	private static string NormalizeWordBreak(string value) =>
		value.Trim().ToLowerInvariant() switch
		{
			"normal" => "normal",
			"break-all" => "break-all",
			"keep-all" => "keep-all",
			"break-word" => "break-word",
			_ => throw new InvalidDataException(
				$"Unsupported CSS word-break '{value}'.")
		};
}

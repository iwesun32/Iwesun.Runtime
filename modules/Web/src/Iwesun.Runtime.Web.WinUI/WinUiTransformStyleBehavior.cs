using System.Globalization;
using System.Text.RegularExpressions;
using Iwesun.Runtime.Web;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Iwesun.Runtime.Web.WinUI;

internal static partial class WinUiTransformStyleBehavior
{
	internal static void ApplyTransform(
		FrameworkElement target,
		string value,
		DomElement? source = null)
	{
		var normalized = value.Trim();
		if (normalized.Equals("none", StringComparison.OrdinalIgnoreCase))
		{
			target.RenderTransform = null;
			return;
		}
		var match = MatrixExpression().Match(normalized);
		if (!match.Success)
			throw new InvalidDataException($"Unsupported CSS transform '{value}'.");
		var values = match.Groups.Cast<Group>().Skip(1)
			.Select(static group => double.Parse(
				group.Value,
				NumberStyles.Float,
				CultureInfo.InvariantCulture))
			.ToArray();
		values[4] = ScaleCssPixel(source, values[4], vertical: false);
		values[5] = ScaleCssPixel(source, values[5], vertical: true);
		target.RenderTransform = new MatrixTransform
		{
			Matrix = new(
				values[0], values[1], values[2], values[3], values[4], values[5])
		};
	}

	internal static void ApplyOrigin(
		FrameworkElement target,
		string value,
		DomElement? source = null)
	{
		var parts = value.Split(
			' ',
			StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (parts.Length is < 1 or > 2)
			throw new InvalidDataException($"Unsupported CSS transform-origin '{value}'.");
		Apply();
		target.Loaded += (_, _) => Apply();
		target.SizeChanged += (_, _) => Apply();

		void Apply()
		{
			target.RenderTransformOrigin = new(
				Resolve(parts[0], target.ActualWidth, true, source),
				Resolve(parts.Length == 1 ? "center" : parts[1], target.ActualHeight, false, source));
		}
	}

	internal static void ApplyRelativeOffset(
		FrameworkElement target,
		string value,
		DomElement? source,
		bool horizontal,
		bool reverse = false)
	{
		if (value.Trim() is "auto" or "normal")
			return;
		var position = source?.HtmlRoot?.ResolveGlobalStyleValue(
			source,
			"style.position",
			DomPropertyDataSlot.Runtime)
			?? source?.HtmlRoot?.ResolveGlobalStyleValue(
				source,
				"style.position",
				DomPropertyDataSlot.Initialization);
		if (position is not ("relative" or "sticky" or "absolute" or "fixed"))
			return;
		if (position is "absolute" or "fixed")
		{
			var primaryName = horizontal ? "style.left" : "style.top";
			var secondaryName = horizontal ? "style.right" : "style.bottom";
			if (!reverse
				&& !HasDeclaredPrimaryOffset(source, primaryName)
				&& HasDeclaredPrimaryOffset(source, secondaryName))
			{
				return;
			}
			if (reverse && HasDeclaredPrimaryOffset(source, primaryName))
				return;
			if (position == "fixed")
			{
				ApplyFixedOffset(target, value, source, horizontal, reverse);
				return;
			}
			if (horizontal)
				target.HorizontalAlignment = reverse
					? HorizontalAlignment.Right
					: HorizontalAlignment.Left;
			else
				target.VerticalAlignment = reverse
					? VerticalAlignment.Bottom
					: VerticalAlignment.Top;
		}
		var offset = ScaleCssPixel(
			source,
			ParseLength(value),
			vertical: !horizontal) * (reverse ? -1 : 1);
		var translation = target.Translation;
		target.Translation = horizontal
			? new((float)offset, translation.Y, translation.Z)
			: new(translation.X, (float)offset, translation.Z);
	}

	private static void ApplyFixedOffset(
		FrameworkElement target,
		string value,
		DomElement? source,
		bool horizontal,
		bool reverse)
	{
		if (horizontal)
			target.HorizontalAlignment = HorizontalAlignment.Left;
		else
			target.VerticalAlignment = VerticalAlignment.Top;
		Apply();
		target.Loaded += (_, _) => Apply();
		target.SizeChanged += (_, _) => Apply();

		void Apply()
		{
			var runtimeRoot = source?.HtmlRoot as HtmlRuntimeDocumentRoot;
			var viewport = runtimeRoot?.DesignRuntime.Layout.Viewport;
			var extent = horizontal
				? viewport?.RuntimeWidth ?? 0
				: viewport?.RuntimeHeight ?? 0;
			var offset = ScaleCssPixel(
				source,
				ParseLength(value),
				vertical: !horizontal);
			var targetExtent = horizontal
				? target.ActualWidth
				: target.ActualHeight;
			var coordinate = reverse
				? extent - offset - targetExtent
				: offset;
			var translation = target.Translation;
			target.Translation = horizontal
				? new((float)coordinate, translation.Y, translation.Z)
				: new(translation.X, (float)coordinate, translation.Z);
		}
	}

	private static double Resolve(
		string token,
		double extent,
		bool horizontal,
		DomElement? source) =>
		token.Trim().ToLowerInvariant() switch
		{
			"left" when horizontal => 0,
			"top" when !horizontal => 0,
			"center" => .5,
			"right" when horizontal => 1,
			"bottom" when !horizontal => 1,
			var value when value.EndsWith('%') =>
				double.Parse(value[..^1], CultureInfo.InvariantCulture) / 100,
			var value when value.EndsWith("px", StringComparison.OrdinalIgnoreCase) =>
				extent > 0
					? ScaleCssPixel(source, ParseLength(value), !horizontal) / extent
					: 0,
			"0" => 0,
			_ => throw new InvalidDataException(
				$"Unsupported CSS transform-origin coordinate '{token}'.")
		};

	private static bool HasDeclaredPrimaryOffset(
		DomElement? source,
		string propertyName)
	{
		var value = source?.HtmlRoot?.ResolveGlobalStyleValue(
			source,
			propertyName,
			DomPropertyDataSlot.Initialization);
		return !string.IsNullOrWhiteSpace(value)
			&& value.Trim() is not ("auto" or "normal");
	}

	private static double ScaleCssPixel(
		DomElement? source,
		double value,
		bool vertical) =>
		(source?.HtmlRoot as HtmlRuntimeDocumentRoot)?
			.DesignRuntime.Layout.ScaleCssPixelToRuntime(value, vertical)
			?? value;

	private static double ParseLength(string value)
	{
		var normalized = value.Trim();
		if (normalized is "auto" or "normal" or "0")
			return 0;
		if (normalized.EndsWith("px", StringComparison.OrdinalIgnoreCase))
			normalized = normalized[..^2];
		return double.Parse(
			normalized,
			NumberStyles.Float,
			CultureInfo.InvariantCulture);
	}

	[GeneratedRegex(
		@"^matrix\(\s*([-+0-9.eE]+)\s*,\s*([-+0-9.eE]+)\s*,\s*([-+0-9.eE]+)\s*,\s*([-+0-9.eE]+)\s*,\s*([-+0-9.eE]+)\s*,\s*([-+0-9.eE]+)\s*\)$",
		RegexOptions.CultureInvariant)]
	private static partial Regex MatrixExpression();
}

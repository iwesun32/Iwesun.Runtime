using Iwesun.Runtime.Web;
using Iwesun.Runtime.WebView2;
using System.Globalization;

namespace Iwesun.Runtime.Web.WinUI;

public static class WinUiRuntimeStyleUpdater
{
	public static string LastShellStyleSummary { get; private set; } =
		string.Empty;

	public static int Apply(HtmlRuntimeDesignRuntime runtime)
	{
		ArgumentNullException.ThrowIfNull(runtime);
		var updated = 0;
		var shellUpdates = new List<string>();
		foreach (var group in runtime.Styles.XamlTargets
			.Where(static target => target.CompositeTargetId is not null)
			.GroupBy(
				static target => target.CompositeTargetId
					?? throw new InvalidOperationException(
						"Composite target identity is missing."),
				StringComparer.Ordinal))
		{
			updated += ApplyComposite(runtime, group);
		}
		foreach (var target in runtime.Styles.XamlTargets)
		{
			if (target.CompositeTargetId is not null)
				continue;
			var resolution = runtime.Styles.ResolveForXaml(
				target.Source.DocumentScope,
				target.Source.XPath,
				target.Source.PropertyName);
			if (resolution is null)
				continue;
			var value = NormalizeValue(
				runtime,
				target,
				resolution.ConcreteValue);
			if (value is null)
				continue;
			WinUiXamlElementObjectFactory.ApplyRuntimeStyleAttribute(
				target.Target,
				target.TargetProperty,
				value);
			if (target.Source.DocumentScope.Equals(
					"document",
					StringComparison.Ordinal)
				&& target.Source.XPath.StartsWith(
					"/html/body/div[1]/div/div",
					StringComparison.Ordinal)
				&& target.Source.XPath.Count(
					static character => character == '/') <= 9)
			{
				shellUpdates.Add(
					$"{target.Source.XPath}:{target.Source.PropertyName}"
					+ $"->{target.TargetProperty}:"
					+ $"{resolution.Expression}"
					+ $"=>{resolution.ConcreteValue}"
					+ $"=>{value}");
			}
			updated++;
		}
		LastShellStyleSummary = string.Join("|", shellUpdates);
		return updated;
	}

	private static int ApplyComposite(
		HtmlRuntimeDesignRuntime runtime,
		IGrouping<string, HtmlRuntimeXamlStyleTargetBinding> group)
	{
		var targets = group
			.OrderBy(static target => target.CompositeIndex)
			.ToArray();
		if (targets.Length == 0
			|| targets.Any(target =>
				target.CompositePartCount != targets.Length)
			|| targets.Select(static target => target.CompositeIndex)
				.Distinct()
				.Count() != targets.Length
			|| targets[0].CompositeIndex != 0
			|| targets[^1].CompositeIndex != targets.Length - 1
			|| targets.Any(target =>
				!ReferenceEquals(target.Target, targets[0].Target)
				|| !target.TargetProperty.Equals(
					targets[0].TargetProperty,
					StringComparison.Ordinal)
				|| target.CompositeValueKind != targets[0].CompositeValueKind)
			|| targets[0].CompositeValueKind == XamlCompositeValueKind.None)
		{
			throw new InvalidDataException(
				$"Composite XAML style target '{group.Key}' is incomplete.");
		}
		var values = new string[targets.Length];
		for (var index = 0; index < targets.Length; index++)
		{
			var target = targets[index];
			var resolution = runtime.Styles.ResolveForXaml(
				target.Source.DocumentScope,
				target.Source.XPath,
				target.Source.PropertyName)
				?? throw new InvalidDataException(
					$"Composite XAML style source "
						+ $"{target.Source.PropertyName} is unavailable.");
			values[index] = target.CompositeValueKind
				== XamlCompositeValueKind.UniformValue
					? resolution.ConcreteValue
					: NormalizeRuntimeCssLength(
						runtime,
						target.TargetProperty,
						target.Source.PropertyName,
						resolution.ConcreteValue,
						target.Target)
						?? throw new InvalidDataException(
							$"Composite XAML style source "
								+ $"{target.Source.PropertyName}="
								+ $"'{resolution.ConcreteValue}' is not a length.");
		}
		var compositeValue = targets[0].CompositeValueKind switch
		{
			XamlCompositeValueKind.SumLengths =>
				values.Sum(static value => double.Parse(
					value,
					CultureInfo.InvariantCulture)).ToString(
						"R",
						CultureInfo.InvariantCulture),
			XamlCompositeValueKind.Thickness
				or XamlCompositeValueKind.CornerRadius => string.Join(",", values),
			XamlCompositeValueKind.UniformValue when values.All(
				value => value.Equals(values[0], StringComparison.Ordinal)) => values[0],
			XamlCompositeValueKind.UniformValue => throw new InvalidDataException(
				$"Composite XAML style target '{group.Key}' is not uniform."),
			_ => throw new InvalidDataException(
				$"Unsupported composite value kind "
					+ $"{targets[0].CompositeValueKind}.")
		};
		WinUiXamlElementObjectFactory.ApplyRuntimeStyleAttribute(
			targets[0].Target,
			targets[0].TargetProperty,
			compositeValue);
		return targets.Length;
	}

	private static string? NormalizeValue(
		HtmlRuntimeDesignRuntime runtime,
		HtmlRuntimeXamlStyleTargetBinding target,
		string runtimeValue)
	{
		var normalized = runtimeValue.Trim();
		return target.TargetProperty switch
		{
			"Width"
				or "Height"
				or "MinWidth"
				or "MinHeight"
				or "MaxWidth"
				or "MaxHeight"
				or "FontSize"
				or "TextLineHeight"
				or "RowGap"
				or "ColumnGap"
				or "Canvas.Left"
				or "Canvas.Top" =>
				NormalizeRuntimeCssLength(
					runtime,
					target.TargetProperty,
					target.Source.PropertyName,
					normalized,
					target.Target),
			"TextDecorations" => NormalizeTextDecorations(normalized),
			"TextWrapping" => normalized.ToLowerInvariant() switch
			{
				"normal" or "pre-wrap" or "pre-line" or "break-spaces" => "Wrap",
				"nowrap" or "pre" => "NoWrap",
				_ => null
			},
			"TextTrimming" => normalized.Equals(
				"ellipsis",
				StringComparison.OrdinalIgnoreCase)
					? "CharacterEllipsis"
					: "None",
			"FontStretch" => normalized.Replace("-", string.Empty),
			"FlowDirection" => normalized.Equals(
				"rtl",
				StringComparison.OrdinalIgnoreCase)
					? "RightToLeft"
					: "LeftToRight",
			"Margin"
				or "Padding"
				or "BorderThickness"
				or "CornerRadius" =>
				ContainsUnsupportedCssBoxKeyword(normalized)
					? null
					: normalized,
			"HorizontalAlignment" => normalized.ToLowerInvariant() switch
			{
				"left" or "start" or "flex-start" => "Left",
				"right" or "end" or "flex-end" => "Right",
				"center" => "Center",
				"stretch" or "normal" or "auto" => "Stretch",
				_ => null
			},
			"VerticalAlignment" => normalized.ToLowerInvariant() switch
			{
				"top" or "start" or "flex-start" => "Top",
				"bottom" or "end" or "flex-end" => "Bottom",
				"center" => "Center",
				"stretch" or "normal" or "auto" => "Stretch",
				_ => null
			},
			"TextAlignment" => normalized.ToLowerInvariant() switch
			{
				"left" or "start" => "Left",
				"right" or "end" => "Right",
				"center" => "Center",
				"justify" => "Justify",
				_ => null
			},
			"Visibility" => normalized.Equals(
				"none",
				StringComparison.OrdinalIgnoreCase)
					? "Collapsed"
					: "Visible",
			"Stretch" => normalized.ToLowerInvariant() switch
			{
				"fill" => "Fill",
				"contain" => "Uniform",
				"cover" => "UniformToFill",
				"none" => "None",
				_ => null
			},
			"FontWeight" => normalized switch
			{
				"400" => "Normal",
				"600" => "SemiBold",
				"700" => "Bold",
				_ => normalized
			},
			_ when target.TargetProperty.StartsWith(
				"HtmlPosition.",
				StringComparison.Ordinal) =>
				NormalizeRuntimeCssLength(
					runtime,
					target.TargetProperty,
					target.Source.PropertyName,
					normalized,
					target.Target),
			_ when target.Expression.Equals(
				target.InitialConcreteValue,
				StringComparison.Ordinal) =>
				normalized,
			_ when IsDirectRuntimeValue(target.TargetProperty) =>
				normalized,
			_ => null
		};
	}

	private static string? NormalizeRuntimeCssLength(
		HtmlRuntimeDesignRuntime runtime,
		string targetProperty,
		string sourcePropertyName,
		string value,
		object? targetObject = null)
	{
		var dimensionOverride = HtmlRuntimeDimensionOverride.Resolve(
			targetProperty,
			value);
		if (dimensionOverride == HtmlRuntimeDimensionOverrideKind.AutomaticSize
			&& targetObject is Microsoft.UI.Xaml.Shapes.Shape)
		{
			// SVG geometry obtains its size from its strongly typed geometry.
			// CSS auto must not erase that intrinsic WinUI Shape bound.
			return null;
		}
		if (dimensionOverride != HtmlRuntimeDimensionOverrideKind.Unsupported)
		{
			return dimensionOverride switch
			{
				HtmlRuntimeDimensionOverrideKind.AutomaticSize => "Auto",
				HtmlRuntimeDimensionOverrideKind.ZeroMinimum => "Auto",
				HtmlRuntimeDimensionOverrideKind.UnboundedMaximum => "None",
				_ => throw new InvalidOperationException(
					$"Unsupported dimension override {dimensionOverride}.")
			};
		}
		var normalized = NormalizeCssLength(value);
		if (!double.TryParse(
			normalized,
			NumberStyles.Float,
			CultureInfo.InvariantCulture,
			out var cssPixels))
		{
			return null;
		}
		var vertical = sourcePropertyName.Contains(
			"height",
			StringComparison.OrdinalIgnoreCase)
			|| sourcePropertyName.Contains(
				"top",
				StringComparison.OrdinalIgnoreCase)
			|| sourcePropertyName.Contains(
				"bottom",
				StringComparison.OrdinalIgnoreCase)
			|| sourcePropertyName.Contains(
				"row",
				StringComparison.OrdinalIgnoreCase)
			|| sourcePropertyName.Contains(
				"lineHeight",
				StringComparison.OrdinalIgnoreCase)
			|| sourcePropertyName.Contains(
				"fontSize",
				StringComparison.OrdinalIgnoreCase);
		return runtime.Layout.ScaleCssPixelToRuntime(cssPixels, vertical)
			.ToString("R", CultureInfo.InvariantCulture);
	}

	private static string NormalizeTextDecorations(string value)
	{
		var values = new List<string>();
		if (value.Contains("underline", StringComparison.OrdinalIgnoreCase))
			values.Add("Underline");
		if (value.Contains("line-through", StringComparison.OrdinalIgnoreCase))
			values.Add("Strikethrough");
		return values.Count == 0 ? "None" : string.Join(", ", values);
	}

	private static string? NormalizeCssLength(string value)
	{
		if (value.EndsWith("px", StringComparison.OrdinalIgnoreCase))
			value = value[..^2];
		return double.TryParse(
			value,
			System.Globalization.NumberStyles.Float,
			System.Globalization.CultureInfo.InvariantCulture,
			out var number)
			&& double.IsFinite(number)
				? number.ToString(
					"R",
					System.Globalization.CultureInfo.InvariantCulture)
				: null;
	}

	private static bool ContainsUnsupportedCssBoxKeyword(string value) =>
		value.Split(
				[' ', ','],
				StringSplitOptions.RemoveEmptyEntries
					| StringSplitOptions.TrimEntries)
			.Any(static part =>
				!part.EndsWith("px", StringComparison.OrdinalIgnoreCase)
				&& !double.TryParse(
					part,
					System.Globalization.NumberStyles.Float,
					System.Globalization.CultureInfo.InvariantCulture,
					out _));

	private static bool IsDirectRuntimeValue(string targetProperty) =>
		targetProperty is
			"Width"
			or "Height"
			or "MinWidth"
			or "MinHeight"
			or "MaxWidth"
			or "MaxHeight"
			or "Margin"
			or "Padding"
			or "Opacity"
			or "Background"
			or "Foreground"
			or "BorderBrush"
			or "BorderThickness"
			or "CornerRadius"
			or "FontSize"
			or "FontStyle"
			or "FontFamily"
			or "RenderTransform";
}

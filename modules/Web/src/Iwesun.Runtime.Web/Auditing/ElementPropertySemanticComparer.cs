using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Iwesun.Runtime.Web;

public sealed record ElementPropertyComparisonResult(
	bool Equivalent,
	string NormalizedSource,
	string NormalizedXaml,
	double? NumericDifference,
	double? AllowedTolerance,
	string Description);

public static partial class ElementPropertySemanticComparer
{
	public static ElementSlotFeatureAuditResult AuditSlots(
		ElementPropertySlot<string> source,
		ElementPropertySlot<string> xaml,
		ElementPropertyTraits traits)
	{
		if (!source.IsSet && !xaml.IsSet)
			return ElementSlotFeatureAuditResult.NotRequired(
				"DOM and XAML values are absent.");
		if (!source.IsSet)
		{
			return new(
				ElementSlotFeatureAuditStatus.UnexpectedXamlValue,
				string.Empty,
				xaml.Value ?? string.Empty,
				"XAML contains a value that is absent from the DOM.");
		}
		if (!xaml.IsSet)
		{
			return new(
				ElementSlotFeatureAuditStatus.MissingXamlValue,
				source.Value ?? string.Empty,
				string.Empty,
				"XAML value is missing.");
		}
		if (traits.Comparison == PropertyComparisonKind.ManualReview)
		{
			if (string.Equals(
				source.Value,
				xaml.Value,
				StringComparison.Ordinal))
			{
				return new(
					ElementSlotFeatureAuditStatus.Passed,
					source.Value ?? string.Empty,
					xaml.Value ?? string.Empty,
					"Manual-review values are exactly equal.");
			}
			return new(
				ElementSlotFeatureAuditStatus.ManualReviewRequired,
				source.Value ?? string.Empty,
				xaml.Value ?? string.Empty,
				"Traits require manual comparison review.");
		}
		var comparison = Compare(
			source.Value,
			source.Unit,
			xaml.Value,
			xaml.Unit,
			traits);
		return new(
			comparison.Equivalent
				? ElementSlotFeatureAuditStatus.Passed
				: ElementSlotFeatureAuditStatus.ValueMismatch,
			comparison.NormalizedSource,
			comparison.NormalizedXaml,
			comparison.Description);
	}

	public static ElementSlotFeatureAuditResult AuditLinks(
		ElementPropertyLink source,
		ElementPropertyLink xaml,
		ElementPropertyTraits traits)
	{
		if (!source.IsSet && !xaml.IsSet)
			return ElementSlotFeatureAuditResult.NotRequired(
				"DOM and XAML links are absent.");
		if (!source.IsSet || !xaml.IsSet || source.Kind != xaml.Kind)
		{
			return new(
				ElementSlotFeatureAuditStatus.LinkMismatch,
				source.Description,
				xaml.Description,
				"DOM and XAML link kinds or presence differ.");
		}
		var comparison = Compare(
			source.Description,
			PropertyUnit.None,
			xaml.Description,
			PropertyUnit.None,
			traits);
		return new(
			comparison.Equivalent
				? ElementSlotFeatureAuditStatus.Passed
				: ElementSlotFeatureAuditStatus.LinkMismatch,
			comparison.NormalizedSource,
			comparison.NormalizedXaml,
			comparison.Description);
	}

	public static ElementPropertyComparisonResult Compare(
		string? source,
		PropertyUnit sourceUnit,
		string? xaml,
		PropertyUnit xamlUnit,
		ElementPropertyTraits traits)
	{
		source ??= string.Empty;
		xaml ??= string.Empty;
		if (!UnitsAreComparable(sourceUnit, xamlUnit))
		{
			return new(
				false,
				source,
				xaml,
				null,
				traits.Tolerance,
				$"Units differ: {sourceUnit} versus {xamlUnit}.");
		}
		return traits.Comparison switch
		{
			PropertyComparisonKind.Exact => Exact(source, xaml),
			PropertyComparisonKind.Semantic =>
				Semantic(source, xaml, traits.ValueKind),
			PropertyComparisonKind.NormalizedColor =>
				Color(source, xaml),
			PropertyComparisonKind.NumericTolerance =>
				Numeric(source, sourceUnit, xaml, xamlUnit, traits, false),
			PropertyComparisonKind.GeometryTolerance =>
				Numeric(source, sourceUnit, xaml, xamlUnit, traits, true),
			PropertyComparisonKind.ResourceIdentity =>
				Resource(source, xaml),
			PropertyComparisonKind.StructuredAnimation =>
				AnimationTimeline(source, xaml),
			PropertyComparisonKind.CanvasReplay =>
				CanvasReplay(source, xaml),
			PropertyComparisonKind.ManualReview =>
				new(
					false,
					source,
					xaml,
					null,
					null,
					"Property comparison requires manual review."),
			_ => Exact(source, xaml)
		};
	}

	private static ElementPropertyComparisonResult Exact(
		string source,
		string xaml)
	{
		var equal = string.Equals(source, xaml, StringComparison.Ordinal);
		return Result(equal, source, xaml, "exact");
	}

	private static ElementPropertyComparisonResult Semantic(
		string source,
		string xaml,
		PropertyValueKind valueKind)
	{
		var normalizedSource = NormalizeSemantic(source, valueKind);
		var normalizedXaml = NormalizeSemantic(xaml, valueKind);
		return Result(
			string.Equals(
				normalizedSource,
				normalizedXaml,
				StringComparison.Ordinal),
			normalizedSource,
			normalizedXaml,
			"semantic");
	}

	private static string NormalizeSemantic(
		string value,
		PropertyValueKind valueKind)
	{
		var normalized = CollapseWhitespace(value);
		if (valueKind is
			PropertyValueKind.Boolean
			or PropertyValueKind.Enumeration
			or PropertyValueKind.State)
		{
			normalized = normalized.ToLowerInvariant();
		}
		return normalized;
	}

	private static ElementPropertyComparisonResult Color(
		string source,
		string xaml)
	{
		var normalizedSource = NormalizeColor(source, xamlSyntax: false);
		var normalizedXaml = NormalizeColor(xaml, xamlSyntax: true);
		return Result(
			string.Equals(
				normalizedSource,
				normalizedXaml,
				StringComparison.Ordinal),
			normalizedSource,
			normalizedXaml,
			"normalized color");
	}

	private static ElementPropertyComparisonResult Resource(
		string source,
		string xaml)
	{
		if (TryCompareRelativeAndAbsoluteResource(source, xaml, out var paired))
			return paired;
		var normalizedSource = NormalizeResource(source);
		var normalizedXaml = NormalizeResource(xaml);
		return Result(
			string.Equals(
				normalizedSource,
				normalizedXaml,
				StringComparison.Ordinal),
			normalizedSource,
			normalizedXaml,
			"resource identity");
	}

	private static bool TryCompareRelativeAndAbsoluteResource(
		string source,
		string xaml,
		out ElementPropertyComparisonResult result)
	{
		var sourceIsUri = Uri.TryCreate(
			source.Trim().Replace('\\', '/'),
			UriKind.RelativeOrAbsolute,
			out var sourceUri);
		var xamlIsUri = Uri.TryCreate(
			xaml.Trim().Replace('\\', '/'),
			UriKind.RelativeOrAbsolute,
			out var xamlUri);
		if (!sourceIsUri
			|| !xamlIsUri
			|| sourceUri!.IsAbsoluteUri == xamlUri!.IsAbsoluteUri)
		{
			result = default!;
			return false;
		}
		var relative = sourceUri.IsAbsoluteUri ? xamlUri : sourceUri;
		var absolute = sourceUri.IsAbsoluteUri ? sourceUri : xamlUri;
		var normalizedRelative = Uri.UnescapeDataString(relative.OriginalString);
		var normalizedAbsolute = absolute.GetComponents(
			UriComponents.PathAndQuery | UriComponents.Fragment,
			UriFormat.SafeUnescaped);
		if (normalizedRelative.StartsWith("/", StringComparison.Ordinal))
			normalizedAbsolute = "/" + normalizedAbsolute.TrimStart('/');
		result = Result(
			string.Equals(
				normalizedRelative,
				normalizedAbsolute,
				StringComparison.Ordinal),
			normalizedRelative,
			normalizedAbsolute,
			"relative/absolute resource identity");
		return true;
	}

	private static ElementPropertyComparisonResult Numeric(
		string source,
		PropertyUnit sourceUnit,
		string xaml,
		PropertyUnit xamlUnit,
		ElementPropertyTraits traits,
		bool geometry)
	{
		if (string.Equals(
			NormalizeNumericText(source),
			NormalizeNumericText(xaml),
			StringComparison.Ordinal))
		{
			return new(
				true,
				NormalizeNumericText(source),
				NormalizeNumericText(xaml),
				0,
				traits.Tolerance,
				"Numeric expressions match exactly.");
		}
		var sourceNumbers = ReadNumbers(source);
		var xamlNumbers = ReadNumbers(xaml);
		var allowed = traits.Tolerance > 0
			? traits.Tolerance
			: geometry ? .5 : .01;
		if (sourceNumbers.Count == 0
			|| sourceNumbers.Count != xamlNumbers.Count
			|| !UnitsAreComparable(sourceUnit, xamlUnit))
		{
			return new(
				false,
				NormalizeNumericText(source),
				NormalizeNumericText(xaml),
				null,
				allowed,
				"Numeric shape or units differ.");
		}
		var maximumDifference = sourceNumbers
			.Zip(xamlNumbers, static (left, right) => Math.Abs(left - right))
			.Max();
		return new(
			maximumDifference <= allowed,
			NormalizeNumericText(source),
			NormalizeNumericText(xaml),
			maximumDifference,
			allowed,
			maximumDifference <= allowed
				? "Numeric values are within tolerance."
				: "Numeric values exceed tolerance.");
	}

	private static bool UnitsAreComparable(
		PropertyUnit source,
		PropertyUnit xaml) =>
		source == xaml
			|| source == PropertyUnit.None
			|| xaml == PropertyUnit.None
			|| (source is PropertyUnit.CssPixel or PropertyUnit.Dip
				&& xaml is PropertyUnit.CssPixel or PropertyUnit.Dip);

	private static IReadOnlyList<double> ReadNumbers(string value) =>
		NumberPattern()
			.Matches(value)
			.Cast<Match>()
			.Select(match => double.Parse(
				match.Value,
				NumberStyles.Float,
				CultureInfo.InvariantCulture))
			.ToArray();

	private static string NormalizeNumericText(string value) =>
		CollapseWhitespace(value).ToLowerInvariant();

	private static string NormalizeResource(string value)
	{
		var trimmed = value.Trim().Replace('\\', '/');
		if (!Uri.TryCreate(trimmed, UriKind.RelativeOrAbsolute, out var uri))
			return trimmed;
		if (!uri.IsAbsoluteUri)
			return Uri.UnescapeDataString(uri.OriginalString);
		return uri.GetComponents(
			UriComponents.SchemeAndServer
				| UriComponents.PathAndQuery
				| UriComponents.Fragment,
			UriFormat.SafeUnescaped);
	}

	private static string NormalizeColor(string value, bool xamlSyntax)
	{
		var normalized = CollapseWhitespace(value).ToLowerInvariant();
		if (normalized == "transparent")
			return "#00000000";
		if (normalized is "black" or "white" or "red" or "green" or "blue")
		{
			return normalized switch
			{
				"black" => "#ff000000",
				"white" => "#ffffffff",
				"red" => "#ffff0000",
				"green" => "#ff008000",
				_ => "#ff0000ff"
			};
		}
		if (normalized.StartsWith('#'))
			return NormalizeHexColor(normalized, xamlSyntax);
		var components = ReadNumbers(normalized);
		if (normalized.StartsWith("rgb(", StringComparison.Ordinal)
			&& components.Count == 3)
		{
			return ToArgb(255, components[0], components[1], components[2]);
		}
		if (normalized.StartsWith("rgba(", StringComparison.Ordinal)
			&& components.Count == 4)
		{
			var alpha = components[3] <= 1
				? components[3] * 255
				: components[3];
			return ToArgb(alpha, components[0], components[1], components[2]);
		}
		return normalized;
	}

	private static string NormalizeHexColor(string value, bool xamlSyntax) =>
		value.Length switch
		{
			4 => $"#ff{value[1]}{value[1]}{value[2]}{value[2]}{value[3]}{value[3]}",
			5 when xamlSyntax =>
				$"#{value[1]}{value[1]}{value[2]}{value[2]}{value[3]}{value[3]}{value[4]}{value[4]}",
			5 => $"#{value[4]}{value[4]}{value[1]}{value[1]}{value[2]}{value[2]}{value[3]}{value[3]}",
			7 => $"#ff{value[1..]}",
			9 when xamlSyntax => value,
			9 => $"#{value[7..9]}{value[1..7]}",
			_ => value
		};

	private static string ToArgb(
		double alpha,
		double red,
		double green,
		double blue) =>
		$"#{Clamp(alpha):x2}{Clamp(red):x2}{Clamp(green):x2}{Clamp(blue):x2}";

	private static int Clamp(double value) =>
		(int)Math.Round(
			Math.Clamp(value, 0, 255),
			MidpointRounding.AwayFromZero);

	private static ElementPropertyComparisonResult AnimationTimeline(
		string source,
		string xaml)
	{
		try
		{
			using var sourceDocument = JsonDocument.Parse(source);
			using var xamlDocument = JsonDocument.Parse(xaml);
			var sourceAnimations = sourceDocument.RootElement
				.GetProperty("animations");
			var xamlAnimations = xamlDocument.RootElement
				.GetProperty("animations");
			if (sourceAnimations.GetArrayLength()
				!= xamlAnimations.GetArrayLength())
			{
				return Result(
					false,
					$"animations={sourceAnimations.GetArrayLength()}",
					$"animations={xamlAnimations.GetArrayLength()}",
					"structured animation count");
			}
			for (var index = 0;
				index < sourceAnimations.GetArrayLength();
				index++)
			{
				var sourceAnimation = sourceAnimations[index];
				var xamlAnimation = xamlAnimations[index];
				var sourceName = sourceAnimation
					.GetProperty("animationName").GetString() ?? string.Empty;
				var xamlName = xamlAnimation
					.GetProperty("animationName").GetString() ?? string.Empty;
				var sourceProperties = sourceAnimation
					.GetProperty("keyframes")
					.EnumerateArray()
					.SelectMany(static frame => frame.EnumerateObject())
					.Select(static property => property.Name)
					.Where(static name => name is not
						"offset" and not "computedOffset"
						and not "easing" and not "composite")
					.ToHashSet(StringComparer.Ordinal);
				var xamlProperties = xamlAnimation
					.GetProperty("properties")
					.EnumerateArray()
					.Select(static item => item.GetString() ?? string.Empty)
					.ToHashSet(StringComparer.Ordinal);
				var unsupported = xamlAnimation
					.GetProperty("unsupportedProperties");
				var sourceDuration = sourceAnimation.GetProperty("timing")
					.GetProperty("duration");
				var xamlDuration = xamlAnimation.GetProperty("duration")
					.GetDouble();
				var durationMatches = sourceDuration.TryGetDouble(
					out var duration)
					&& Math.Abs(duration - xamlDuration) <= .5;
				if (!string.Equals(
						sourceName,
						xamlName,
						StringComparison.Ordinal)
					|| !sourceProperties.SetEquals(xamlProperties)
					|| unsupported.GetArrayLength() != 0
					|| !durationMatches)
				{
					return Result(
						false,
						$"{sourceName}:{string.Join(',', sourceProperties.Order())}",
						$"{xamlName}:{string.Join(',', xamlProperties.Order())}"
							+ $":unsupported={unsupported.GetArrayLength()}",
						"structured animation materialization");
				}
			}
			return Result(
				true,
				$"animations={sourceAnimations.GetArrayLength()}",
				$"animations={xamlAnimations.GetArrayLength()}",
				"structured animation materialization");
		}
		catch (Exception exception)
			when (exception is JsonException
				or InvalidOperationException
				or KeyNotFoundException)
		{
			return Result(
				false,
				source,
				xaml,
				"invalid structured animation evidence");
		}
	}

	private static ElementPropertyComparisonResult CanvasReplay(
		string source,
		string xaml)
	{
		try
		{
			using var sourceDocument = JsonDocument.Parse(source);
			using var xamlDocument = JsonDocument.Parse(xaml);
			var expected = sourceDocument.RootElement
				.EnumerateArray()
				.GroupBy(
					static command =>
						command.GetProperty("name").GetString() ?? string.Empty,
					StringComparer.Ordinal)
				.ToDictionary(
					static group => group.Key,
					static group => group.Count(),
					StringComparer.Ordinal);
			var root = xamlDocument.RootElement;
			var actual = root.GetProperty("executed")
				.EnumerateObject()
				.ToDictionary(
					static property => property.Name,
					static property => property.Value.GetInt32(),
					StringComparer.Ordinal);
			var partial = root.GetProperty("partial").EnumerateObject().Any();
			var unsupported = root.GetProperty("unsupported")
				.EnumerateObject().Any();
			var equal = !partial
				&& !unsupported
				&& expected.Count == actual.Count
				&& expected.All(pair =>
					actual.TryGetValue(pair.Key, out var count)
					&& count == pair.Value);
			return Result(
				equal,
				$"commands={expected.Values.Sum()}",
				$"executed={actual.Values.Sum()};"
					+ $"partial={partial};unsupported={unsupported}",
				"canvas replay execution");
		}
		catch (Exception exception)
			when (exception is JsonException
				or InvalidOperationException
				or KeyNotFoundException)
		{
			return Result(
				false,
				source,
				xaml,
				"invalid canvas replay evidence");
		}
	}

	private static string CollapseWhitespace(string value)
	{
		var builder = new StringBuilder(value.Length);
		var pendingSpace = false;
		foreach (var character in value.Trim())
		{
			if (char.IsWhiteSpace(character))
			{
				pendingSpace = builder.Length > 0;
				continue;
			}
			if (pendingSpace)
				builder.Append(' ');
			builder.Append(character);
			pendingSpace = false;
		}
		return builder.ToString();
	}

	private static ElementPropertyComparisonResult Result(
		bool equal,
		string source,
		string xaml,
		string strategy) =>
		new(
			equal,
			source,
			xaml,
			null,
			null,
			equal
				? $"Values match after {strategy} comparison."
				: $"Values differ after {strategy} comparison.");

	[GeneratedRegex(@"[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?",
		RegexOptions.CultureInvariant)]
	private static partial Regex NumberPattern();
}

public static class DomRuntimePropertyComparisonTraits
{
	private static readonly string[] NumericNames =
	[
		"width", "height", "top", "right", "bottom", "left",
		"margin", "padding", "gap", "radius", "size", "spacing",
		"basis", "grow", "shrink", "opacity", "order", "offset",
		"scroll", "lineheight", "letterspacing"
	];

	public static ElementPropertyTraits Resolve(
		string propertyName,
		ElementSlotCategory category,
		ElementPropertyTraits declared)
	{
		var normalized = propertyName.ToLowerInvariant();
		if (normalized == "effect.animations")
		{
			return declared with
			{
				ValueKind = PropertyValueKind.Animation,
				Comparison = PropertyComparisonKind.StructuredAnimation
			};
		}
		if (normalized == "resource.canvascommandstream")
		{
			return declared with
			{
				ValueKind = PropertyValueKind.Resource,
				Comparison = PropertyComparisonKind.CanvasReplay
			};
		}
		if (normalized.StartsWith("rect.", StringComparison.Ordinal))
		{
			return declared with
			{
				ValueKind = normalized is "rect.x" or "rect.y"
					? PropertyValueKind.Coordinate
					: PropertyValueKind.Size,
				Unit = PropertyUnit.CssPixel,
				Comparison = PropertyComparisonKind.GeometryTolerance,
				Tolerance = declared.Tolerance > 0 ? declared.Tolerance : .5
			};
		}
		if (normalized.Contains("color", StringComparison.Ordinal)
			|| normalized is "style.fill" or "style.stroke")
		{
			return declared with
			{
				ValueKind = PropertyValueKind.Color,
				Comparison = PropertyComparisonKind.NormalizedColor
			};
		}
		if (normalized.Contains("transform", StringComparison.Ordinal)
			|| category == ElementSlotCategory.Effect
				&& normalized.Contains("animation", StringComparison.Ordinal))
		{
			return declared with
			{
				ValueKind = PropertyValueKind.Transform,
				Comparison = PropertyComparisonKind.GeometryTolerance,
				Tolerance = declared.Tolerance > 0 ? declared.Tolerance : .01
			};
		}
		if (normalized.Contains("resource", StringComparison.Ordinal)
			|| normalized.EndsWith(".src", StringComparison.Ordinal)
			|| normalized.EndsWith(".url", StringComparison.Ordinal)
			|| normalized.EndsWith(".href", StringComparison.Ordinal)
			|| normalized.EndsWith(".action", StringComparison.Ordinal)
			|| normalized.EndsWith(".formaction", StringComparison.Ordinal)
			|| normalized.EndsWith(".cite", StringComparison.Ordinal)
			|| normalized.EndsWith(".data", StringComparison.Ordinal)
			|| normalized.EndsWith(".itemid", StringComparison.Ordinal)
			|| normalized.EndsWith(".poster", StringComparison.Ordinal)
			|| normalized is "href" or "src" or "action" or "formaction"
				or "cite" or "data" or "itemid" or "poster")
		{
			return declared with
			{
				ValueKind = PropertyValueKind.Resource,
				Comparison = PropertyComparisonKind.ResourceIdentity
			};
		}
		if (category is ElementSlotCategory.Space or ElementSlotCategory.Style
			&& NumericNames.Any(normalized.Contains))
		{
			return declared with
			{
				ValueKind = PropertyValueKind.Length,
				Unit = PropertyUnit.CssPixel,
				Comparison = PropertyComparisonKind.NumericTolerance,
				Tolerance = declared.Tolerance > 0 ? declared.Tolerance : .5
			};
		}
		return declared;
	}
}

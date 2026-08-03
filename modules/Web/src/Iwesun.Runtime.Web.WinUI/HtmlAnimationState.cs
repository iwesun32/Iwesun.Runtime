using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI;

namespace Iwesun.Runtime.Web.WinUI;

internal static class HtmlAnimationState
{
	private sealed record MaterializedAnimation(
		string Name,
		string[] Properties,
		string[] UnsupportedProperties,
		DateTimeOffset StartedAt,
		TimeSpan Duration,
		Storyboard? Storyboard);

	private sealed class Materialization
	{
		public List<MaterializedAnimation> Animations { get; } = [];
	}

	private static readonly ConditionalWeakTable<
		DependencyObject,
		Materialization> Materializations = new();

	public static readonly DependencyProperty SourceTimelineJsonProperty =
		DependencyProperty.RegisterAttached(
			"SourceTimelineJson",
			typeof(string),
			typeof(HtmlAnimationState),
			new PropertyMetadata(null));

	public static string? GetSourceTimelineJson(DependencyObject owner) =>
		(string?)owner.GetValue(SourceTimelineJsonProperty);

	public static void ApplyTimeline(
		DependencyObject owner,
		string? value)
	{
		owner.SetValue(SourceTimelineJsonProperty, value);
		var materialization = Materializations.GetOrCreateValue(owner);
		foreach (var existing in materialization.Animations)
			existing.Storyboard?.Stop();
		materialization.Animations.Clear();
		if (owner is not FrameworkElement element
			|| string.IsNullOrWhiteSpace(value))
		{
			return;
		}
		using var document = JsonDocument.Parse(value);
		if (!document.RootElement.TryGetProperty(
			"animations",
			out var animations)
			|| animations.ValueKind != JsonValueKind.Array)
		{
			throw new InvalidDataException(
				"Animation timeline has no animations array.");
		}
		foreach (var animation in animations.EnumerateArray())
			TryMaterialize(element, animation, materialization);
	}

	public static string CaptureRuntimeTimelineJson(
		DependencyObject owner)
	{
		if (!Materializations.TryGetValue(owner, out var state))
			return string.Empty;
		var now = DateTimeOffset.UtcNow;
		return JsonSerializer.Serialize(new
		{
			schema = "iwesun.xaml.animations/1",
			animations = state.Animations.Select(animation => new
			{
				animationName = animation.Name,
				playState = "running",
				currentTime = Math.Min(
					(now - animation.StartedAt).TotalMilliseconds,
					animation.Duration.TotalMilliseconds),
				duration = animation.Duration.TotalMilliseconds,
				properties = animation.Properties,
				unsupportedProperties = animation.UnsupportedProperties,
				values = ReadRuntimeValues(owner, animation.Properties)
			})
		});
	}

	public static bool TryReadCssProperty(
		DependencyObject owner,
		string propertyName,
		out string value)
	{
		value = string.Empty;
		if (!Materializations.TryGetValue(owner, out var state)
			|| state.Animations.Count == 0
			|| state.Animations.Any(static animation =>
				animation.Storyboard is null))
		{
			return false;
		}
		var animations = state.Animations;
		value = propertyName switch
		{
			"style.animation" => Join(static animation =>
				$"{CssSeconds(animation.Duration)} linear "
				+ $"{CssSeconds(animation.Storyboard?.BeginTime ?? TimeSpan.Zero)} "
				+ $"{ReadIterationCount(animation.Storyboard)} "
				+ $"{(animation.Storyboard?.AutoReverse == true ? "alternate" : "normal")} "
				+ $"{(animation.Storyboard?.FillBehavior == FillBehavior.HoldEnd ? "forwards" : "none")} "
				+ $"running {animation.Name}"),
			"style.animationName" => Join(static animation => animation.Name),
			"style.animationDuration" => Join(static animation =>
				CssSeconds(animation.Duration)),
			"style.animationDelay" => Join(static animation =>
				CssSeconds(animation.Storyboard?.BeginTime ?? TimeSpan.Zero)),
			"style.animationIterationCount" => Join(static animation =>
				ReadIterationCount(animation.Storyboard)),
			"style.animationDirection" => Join(static animation =>
				animation.Storyboard?.AutoReverse == true
					? "alternate"
					: "normal"),
			"style.animationFillMode" => Join(static animation =>
				animation.Storyboard?.FillBehavior == FillBehavior.HoldEnd
					? "forwards"
					: "none"),
			"style.animationPlayState" => Join(static _ => "running"),
			"style.animationTimingFunction" => Join(static _ => "linear"),
			_ => string.Empty
		};
		return value.Length != 0;

		string Join(Func<MaterializedAnimation, string> selector) =>
			string.Join(", ", animations.Select(selector));
	}

	private static string CssSeconds(TimeSpan duration) =>
		(duration.TotalSeconds.ToString(
			"R",
			System.Globalization.CultureInfo.InvariantCulture)) + "s";

	private static string ReadIterationCount(Storyboard? storyboard)
	{
		if (storyboard is null)
			return string.Empty;
		if (storyboard.RepeatBehavior == RepeatBehavior.Forever)
			return "infinite";
		return storyboard.RepeatBehavior.HasCount
			? storyboard.RepeatBehavior.Count.ToString(
				"R",
				System.Globalization.CultureInfo.InvariantCulture)
			: "1";
	}

	private static void TryMaterialize(
		FrameworkElement element,
		JsonElement source,
		Materialization destination)
	{
		if (!source.TryGetProperty("keyframes", out var keyframes)
			|| keyframes.ValueKind != JsonValueKind.Array
			|| keyframes.GetArrayLength() == 0)
		{
			return;
		}
		var duration = ReadDuration(source);
		var storyboard = new Storyboard();
		var supported = new HashSet<string>(StringComparer.Ordinal);
		var observed = keyframes.EnumerateArray()
			.SelectMany(static frame => frame.EnumerateObject())
			.Select(static property => property.Name)
			.Where(static name => name is not
				"offset" and not "computedOffset" and not "easing"
				and not "composite")
			.ToHashSet(StringComparer.Ordinal);
		AddDoubleAnimation(
			storyboard,
			element,
			"Opacity",
			"opacity",
			keyframes,
			duration,
			supported);
		AddDoubleAnimation(
			storyboard,
			element,
			"Width",
			"width",
			keyframes,
			duration,
			supported,
			dependent: true);
		AddDoubleAnimation(
			storyboard,
			element,
			"Height",
			"height",
			keyframes,
			duration,
			supported,
			dependent: true);
		AddTransformAnimations(
			storyboard,
			element,
			keyframes,
			duration,
			supported);
		AddColorAnimation(
			storyboard,
			element,
			"backgroundColor",
			keyframes,
			duration,
			supported);
		AddColorAnimation(
			storyboard,
			element,
			"color",
			keyframes,
			duration,
			supported);
		ApplyTiming(storyboard, source);
		if (storyboard.Children.Count > 0)
			storyboard.Begin();
		var name = source.TryGetProperty("animationName", out var nameNode)
			? nameNode.GetString() ?? string.Empty
			: string.Empty;
		destination.Animations.Add(new(
			name,
			supported.Order(StringComparer.Ordinal).ToArray(),
			observed.Except(supported, StringComparer.Ordinal)
				.Order(StringComparer.Ordinal)
				.ToArray(),
			DateTimeOffset.UtcNow,
			duration,
			storyboard.Children.Count > 0 ? storyboard : null));
	}

	private static void AddDoubleAnimation(
		Storyboard storyboard,
		DependencyObject target,
		string targetProperty,
		string sourceProperty,
		JsonElement keyframes,
		TimeSpan duration,
		ISet<string> supported,
		bool dependent = false)
	{
		var animation = new DoubleAnimationUsingKeyFrames
		{
			Duration = new Duration(duration),
			EnableDependentAnimation = dependent
		};
		AddDoubleFrames(
			animation,
			keyframes,
			sourceProperty,
			duration,
			static value => ParseCssNumber(value));
		if (animation.KeyFrames.Count == 0)
			return;
		Storyboard.SetTarget(animation, target);
		Storyboard.SetTargetProperty(animation, targetProperty);
		storyboard.Children.Add(animation);
		supported.Add(sourceProperty);
	}

	private static void AddTransformAnimations(
		Storyboard storyboard,
		FrameworkElement element,
		JsonElement keyframes,
		TimeSpan duration,
		ISet<string> supported)
	{
		if (!keyframes.EnumerateArray().Any(static frame =>
			frame.TryGetProperty("transform", out _)))
		{
			return;
		}
		var transform = element.RenderTransform as CompositeTransform
			?? new CompositeTransform();
		element.RenderTransform = transform;
		var mappings = new[]
		{
			("TranslateX", 0),
			("TranslateY", 1),
			("ScaleX", 2),
			("ScaleY", 3),
			("Rotation", 4)
		};
		foreach (var (property, index) in mappings)
		{
			var animation = new DoubleAnimationUsingKeyFrames
			{
				Duration = new Duration(duration)
			};
			AddDoubleFrames(
				animation,
				keyframes,
				"transform",
				duration,
				value => ParseTransform(value)[index]);
			if (animation.KeyFrames.Count == 0)
				continue;
			Storyboard.SetTarget(animation, transform);
			Storyboard.SetTargetProperty(animation, property);
			storyboard.Children.Add(animation);
		}
		supported.Add("transform");
	}

	private static void AddColorAnimation(
		Storyboard storyboard,
		FrameworkElement element,
		string sourceProperty,
		JsonElement keyframes,
		TimeSpan duration,
		ISet<string> supported)
	{
		var brush = ResolveBrush(element, sourceProperty);
		if (brush is null)
			return;
		var animation = new ColorAnimationUsingKeyFrames
		{
			Duration = new Duration(duration)
		};
		var index = 0;
		foreach (var frame in keyframes.EnumerateArray())
		{
			if (frame.TryGetProperty(sourceProperty, out var value)
				&& TryParseColor(value.GetString(), out var color))
			{
				animation.KeyFrames.Add(new LinearColorKeyFrame
				{
					KeyTime = FrameTime(frame, index, keyframes, duration),
					Value = color
				});
			}
			index++;
		}
		if (animation.KeyFrames.Count == 0)
			return;
		Storyboard.SetTarget(animation, brush);
		Storyboard.SetTargetProperty(animation, "Color");
		storyboard.Children.Add(animation);
		supported.Add(sourceProperty);
	}

	private static TimeSpan ReadDuration(JsonElement animation)
	{
		if (animation.TryGetProperty("timing", out var timing)
			&& timing.TryGetProperty("duration", out var duration)
			&& TryDouble(duration, out var milliseconds)
			&& milliseconds > 0)
		{
			return TimeSpan.FromMilliseconds(milliseconds);
		}
		return TimeSpan.FromMilliseconds(1);
	}

	private static void AddDoubleFrames(
		DoubleAnimationUsingKeyFrames animation,
		JsonElement keyframes,
		string property,
		TimeSpan duration,
		Func<string, double> converter)
	{
		var index = 0;
		foreach (var frame in keyframes.EnumerateArray())
		{
			if (frame.TryGetProperty(property, out var value))
			{
				var text = value.ValueKind == JsonValueKind.String
					? value.GetString() ?? string.Empty
					: value.ToString();
				var number = converter(text);
				if (double.IsFinite(number))
				{
					animation.KeyFrames.Add(new LinearDoubleKeyFrame
					{
						KeyTime = FrameTime(
							frame,
							index,
							keyframes,
							duration),
						Value = number
					});
				}
			}
			index++;
		}
	}

	private static KeyTime FrameTime(
		JsonElement frame,
		int index,
		JsonElement keyframes,
		TimeSpan duration)
	{
		var offset = frame.TryGetProperty("computedOffset", out var offsetNode)
			&& TryDouble(offsetNode, out var parsedOffset)
				? parsedOffset
				: index / Math.Max(1d, keyframes.GetArrayLength() - 1d);
		return KeyTime.FromTimeSpan(
			TimeSpan.FromMilliseconds(
				duration.TotalMilliseconds * Math.Clamp(offset, 0, 1)));
	}

	private static double ParseCssNumber(string value)
	{
		var match = Regex.Match(
			value,
			@"[-+]?(?:\d+\.?\d*|\.\d+)",
			RegexOptions.CultureInvariant);
		return match.Success
			&& double.TryParse(
				match.Value,
				System.Globalization.NumberStyles.Float,
				System.Globalization.CultureInfo.InvariantCulture,
				out var number)
					? number
					: double.NaN;
	}

	private static double[] ParseTransform(string value)
	{
		var result = new[] { 0d, 0d, 1d, 1d, 0d };
		var matrix = Regex.Match(
			value,
			@"matrix\(([^)]+)\)",
			RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
		if (matrix.Success)
		{
			var parts = matrix.Groups[1].Value.Split(
				',',
				StringSplitOptions.TrimEntries);
			if (parts.Length == 6
				&& parts.Select(ParseCssNumber).ToArray() is var values
				&& values.All(double.IsFinite))
			{
				result[0] = values[4];
				result[1] = values[5];
				result[2] = Math.Sqrt(
					values[0] * values[0] + values[1] * values[1]);
				result[3] = Math.Sqrt(
					values[2] * values[2] + values[3] * values[3]);
				result[4] = Math.Atan2(values[1], values[0])
					* 180 / Math.PI;
				return result;
			}
		}
		foreach (Match function in Regex.Matches(
			value,
			@"(?<name>[a-zA-Z]+)\((?<args>[^)]*)\)",
			RegexOptions.CultureInvariant))
		{
			var name = function.Groups["name"].Value;
			var args = function.Groups["args"].Value.Split(
				[',', ' '],
				StringSplitOptions.RemoveEmptyEntries
					| StringSplitOptions.TrimEntries)
				.Select(ParseCssNumber)
				.ToArray();
			if (args.Length == 0)
				continue;
			switch (name.ToLowerInvariant())
			{
				case "translate":
					result[0] = args[0];
					result[1] = args.Length > 1 ? args[1] : 0;
					break;
				case "translatex":
					result[0] = args[0];
					break;
				case "translatey":
					result[1] = args[0];
					break;
				case "scale":
					result[2] = args[0];
					result[3] = args.Length > 1 ? args[1] : args[0];
					break;
				case "scalex":
					result[2] = args[0];
					break;
				case "scaley":
					result[3] = args[0];
					break;
				case "rotate":
					result[4] = args[0];
					break;
			}
		}
		return result;
	}

	private static SolidColorBrush? ResolveBrush(
		FrameworkElement element,
		string property) =>
		(element, property) switch
		{
			(Control control, "backgroundColor") =>
				EnsureSolid(
					control.Background,
					brush => control.Background = brush),
			(Border border, "backgroundColor") =>
				EnsureSolid(
					border.Background,
					brush => border.Background = brush),
			(Panel panel, "backgroundColor") =>
				EnsureSolid(
					panel.Background,
					brush => panel.Background = brush),
			(TextBlock text, "color") =>
				EnsureSolid(
					text.Foreground,
					brush => text.Foreground = brush),
			(Control control, "color") =>
				EnsureSolid(
					control.Foreground,
					brush => control.Foreground = brush),
			_ => null
		};

	private static SolidColorBrush EnsureSolid(
		Brush? current,
		Action<SolidColorBrush> assign)
	{
		if (current is SolidColorBrush solid)
			return solid;
		var created = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
		assign(created);
		return created;
	}

	private static bool TryParseColor(string? value, out Color color)
	{
		color = default;
		if (string.IsNullOrWhiteSpace(value))
			return false;
		var numbers = Regex.Matches(
			value,
			@"[-+]?(?:\d+\.?\d*|\.\d+)",
			RegexOptions.CultureInvariant)
			.Select(match => ParseCssNumber(match.Value))
			.ToArray();
		if (value.StartsWith("rgb", StringComparison.OrdinalIgnoreCase)
			&& numbers.Length >= 3)
		{
			var alpha = numbers.Length >= 4
				? (byte)Math.Round(Math.Clamp(numbers[3], 0, 1) * 255)
				: (byte)255;
			color = Color.FromArgb(
				alpha,
				(byte)Math.Clamp(numbers[0], 0, 255),
				(byte)Math.Clamp(numbers[1], 0, 255),
				(byte)Math.Clamp(numbers[2], 0, 255));
			return true;
		}
		if (value.StartsWith('#')
			&& uint.TryParse(
				value[1..],
				System.Globalization.NumberStyles.HexNumber,
				System.Globalization.CultureInfo.InvariantCulture,
				out var packed)
			&& value.Length == 7)
		{
			color = Color.FromArgb(
				255,
				(byte)(packed >> 16),
				(byte)(packed >> 8),
				(byte)packed);
			return true;
		}
		return false;
	}

	private static void ApplyTiming(
		Storyboard storyboard,
		JsonElement source)
	{
		if (!source.TryGetProperty("timing", out var timing))
			return;
		if (timing.TryGetProperty("delay", out var delay)
			&& TryDouble(delay, out var delayMs))
			storyboard.BeginTime = TimeSpan.FromMilliseconds(delayMs);
		if (timing.TryGetProperty("iterations", out var iterations)
			&& TryDouble(iterations, out var count)
			&& double.IsFinite(count)
			&& count > 0)
			storyboard.RepeatBehavior = new RepeatBehavior(count);
		var direction = timing.TryGetProperty("direction", out var directionNode)
			? directionNode.GetString()
			: null;
		storyboard.AutoReverse = direction is "alternate" or "alternate-reverse";
		if (source.TryGetProperty("playbackRate", out var rate)
			&& TryDouble(rate, out var speed)
			&& speed > 0)
			storyboard.SpeedRatio = speed;
	}

	private static IReadOnlyDictionary<string, object?> ReadRuntimeValues(
		DependencyObject owner,
		IEnumerable<string> properties)
	{
		var values = new Dictionary<string, object?>(StringComparer.Ordinal);
		if (owner is not FrameworkElement element)
			return values;
		foreach (var property in properties)
		{
			values[property] = property switch
			{
				"opacity" => element.Opacity,
				"width" => element.ActualWidth,
				"height" => element.ActualHeight,
				"transform" when element.RenderTransform
					is CompositeTransform transform => new
					{
						transform.TranslateX,
						transform.TranslateY,
						transform.ScaleX,
						transform.ScaleY,
						transform.Rotation
					},
				"backgroundColor" => ReadBackground(element),
				"color" => ReadForeground(element),
				_ => null
			};
		}
		return values;
	}

	private static string? ReadBackground(FrameworkElement element) =>
		element switch
		{
			Control control => ColorText(control.Background),
			Border border => ColorText(border.Background),
			Panel panel => ColorText(panel.Background),
			_ => null
		};

	private static string? ReadForeground(FrameworkElement element) =>
		element switch
		{
			TextBlock text => ColorText(text.Foreground),
			Control control => ColorText(control.Foreground),
			_ => null
		};

	private static string? ColorText(Brush? brush) =>
		brush is SolidColorBrush solid
			? solid.Color.ToString()
			: null;

	private static bool TryDouble(
		JsonElement value,
		out double result)
	{
		if (value.TryGetDouble(out result))
			return true;
		return double.TryParse(
			value.GetString(),
			System.Globalization.NumberStyles.Float,
			System.Globalization.CultureInfo.InvariantCulture,
			out result);
	}
}

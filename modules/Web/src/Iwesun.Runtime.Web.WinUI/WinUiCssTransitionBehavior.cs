using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;

namespace Iwesun.Runtime.Web.WinUI;

/// <summary>
/// Materializes CSS transition declarations as target-side WinUI timelines.
/// The retained declarations are never used as audit evidence unless their
/// corresponding WinUI timeline objects still match the parsed CSS contract.
/// </summary>
internal static class WinUiCssTransitionBehavior
{
	private sealed class Materialization
	{
		internal string Properties { get; set; } = "all";
		internal string Durations { get; set; } = "0s";
		internal string Delays { get; set; } = "0s";
		internal string TimingFunctions { get; set; } = "ease";
		internal List<TransitionProbe> Probes { get; } = [];
		internal Dictionary<string, Storyboard> Active { get; } =
			new(StringComparer.OrdinalIgnoreCase);
	}

	private readonly record struct TransitionSpec(
		string Property,
		string DurationText,
		TimeSpan Duration,
		string TimingFunction,
		string DelayText,
		TimeSpan Delay);

	private sealed record TransitionProbe(
		TransitionSpec Spec,
		Timeline Timeline);

	private static readonly ConditionalWeakTable<
		FrameworkElement,
		Materialization> Materializations = new();

	internal static void ApplyMetadata(
		FrameworkElement target,
		string property,
		string expression)
	{
		ArgumentNullException.ThrowIfNull(target);
		ArgumentException.ThrowIfNullOrWhiteSpace(property);
		ArgumentNullException.ThrowIfNull(expression);
		var state = Materializations.GetOrCreateValue(target);
		switch (property)
		{
			case "transition-property":
				state.Properties = NormalizeList(expression, NormalizeProperty);
				break;
			case "transition-duration":
				state.Durations = NormalizeList(
					expression,
					NormalizeDuration);
				break;
			case "transition-delay":
				state.Delays = NormalizeList(expression, NormalizeDelay);
				break;
			case "transition-timing-function":
				state.TimingFunctions = NormalizeList(
					expression,
					NormalizeTimingFunction);
				break;
			default:
				throw new ArgumentOutOfRangeException(
					nameof(property),
					property,
					"Not a CSS transition longhand.");
		}
		RebuildProbes(state);
	}

	internal static bool TryStartDoubleTransition(
		FrameworkElement target,
		string cssProperty,
		string targetProperty,
		double from,
		double to,
		Action commit)
	{
		ArgumentNullException.ThrowIfNull(target);
		ArgumentNullException.ThrowIfNull(commit);
		if (Math.Abs(from - to) <= .0001
			|| !Materializations.TryGetValue(target, out var state)
			|| !TryResolve(state, cssProperty, out var spec)
			|| spec.Duration <= TimeSpan.Zero)
		{
			return false;
		}
		if (state.Active.Remove(cssProperty, out var active))
			active.Stop();
		var timeline = CreateTimeline(spec, from, to);
		Storyboard.SetTarget(timeline, target);
		Storyboard.SetTargetProperty(timeline, targetProperty);
		var storyboard = new Storyboard();
		storyboard.Children.Add(timeline);
		storyboard.Completed += (_, _) =>
		{
			commit();
			if (state.Active.TryGetValue(cssProperty, out var current)
				&& ReferenceEquals(current, storyboard))
			{
				state.Active.Remove(cssProperty);
			}
		};
		state.Active[cssProperty] = storyboard;
		storyboard.Begin();
		return true;
	}

	internal static bool TryRead(
		FrameworkElement target,
		string propertyName,
		out string value)
	{
		value = string.Empty;
		if (!Materializations.TryGetValue(target, out var state)
			|| !ValidateProbes(state))
		{
			return false;
		}
		value = propertyName switch
		{
			"style.transitionProperty" => state.Properties,
			"style.transitionDuration" => state.Durations,
			"style.transitionDelay" => state.Delays,
			"style.transitionTimingFunction" => state.TimingFunctions,
			"style.transition" => ComposeShorthand(state),
			_ => string.Empty
		};
		return value.Length > 0;
	}

	private static void RebuildProbes(Materialization state)
	{
		state.Probes.Clear();
		var properties = SplitCssList(state.Properties);
		for (var index = 0; index < properties.Count; index++)
		{
			if (properties[index] == "none")
				continue;
			var spec = ResolveAt(state, properties[index], index);
			state.Probes.Add(new(spec, CreateTimeline(spec, 0, 1)));
		}
	}

	private static bool ValidateProbes(Materialization state)
	{
		var expectedCount = SplitCssList(state.Properties)
			.Count(static property => property != "none");
		return state.Probes.Count == expectedCount
			&& state.Probes.All(static probe =>
				ValidateTimeline(probe.Timeline, probe.Spec));
	}

	private static bool ValidateTimeline(
		Timeline timeline,
		TransitionSpec spec)
	{
		if (timeline.BeginTime != spec.Delay)
			return false;
		if (timeline is DoubleAnimation linear)
		{
			return spec.TimingFunction == "linear"
				&& linear.Duration.TimeSpan == spec.Duration
				&& linear.EasingFunction is null;
		}
		if (IsSteps(spec.TimingFunction))
		{
			if (timeline is not DoubleAnimationUsingKeyFrames discrete)
				return false;
			var expectedSteps = CreateStepsTimeline(spec, 0, 1);
			if (discrete.KeyFrames.Count != expectedSteps.KeyFrames.Count)
				return false;
			for (var index = 0; index < discrete.KeyFrames.Count; index++)
			{
				if (discrete.KeyFrames[index] is not DiscreteDoubleKeyFrame actual
					|| expectedSteps.KeyFrames[index] is not DiscreteDoubleKeyFrame wanted
					|| actual.KeyTime.TimeSpan != wanted.KeyTime.TimeSpan
					|| Math.Abs(actual.Value - wanted.Value) > .0001)
				{
					return false;
				}
			}
			return true;
		}
		if (timeline is not DoubleAnimationUsingKeyFrames keyFrames
			|| keyFrames.KeyFrames.Count != 1
			|| keyFrames.KeyFrames[0] is not SplineDoubleKeyFrame spline
			|| spline.KeyTime.TimeSpan != spec.Duration)
		{
			return false;
		}
		var expected = ResolveSpline(spec.TimingFunction);
		return spline.KeySpline.ControlPoint1 == expected.ControlPoint1
			&& spline.KeySpline.ControlPoint2 == expected.ControlPoint2;
	}

	private static Timeline CreateTimeline(
		TransitionSpec spec,
		double from,
		double to)
	{
		if (IsSteps(spec.TimingFunction))
			return CreateStepsTimeline(spec, from, to);
		var animation = new DoubleAnimationUsingKeyFrames
		{
			BeginTime = spec.Delay
		};
		animation.KeyFrames.Add(new SplineDoubleKeyFrame
		{
			Value = to,
			KeyTime = KeyTime.FromTimeSpan(spec.Duration),
			KeySpline = ResolveSpline(spec.TimingFunction)
		});
		return animation;
	}

	private static DoubleAnimationUsingKeyFrames CreateStepsTimeline(
		TransitionSpec spec,
		double from,
		double to)
	{
		var (count, position) = ParseSteps(spec.TimingFunction);
		var animation = new DoubleAnimationUsingKeyFrames
		{
			BeginTime = spec.Delay
		};
		var delta = to - from;
		var points = position switch
		{
			"jump-start" => Enumerable.Range(0, count)
				.Select(index => (
					Time: index / (double)count,
					Value: (index + 1d) / count)),
			"jump-both" => Enumerable.Range(0, count + 1)
				.Select(index => (
					Time: index / (double)count,
					Value: (index + 1d) / (count + 1d))),
			"jump-none" => Enumerable.Range(1, count - 1)
				.Select(index => (
					Time: index / (double)count,
					Value: index / (double)(count - 1))),
			_ => Enumerable.Range(1, count)
				.Select(index => (
					Time: index / (double)count,
					Value: index / (double)count))
		};
		foreach (var point in points)
		{
			animation.KeyFrames.Add(new DiscreteDoubleKeyFrame
			{
				Value = from + delta * point.Value,
				KeyTime = KeyTime.FromTimeSpan(
					TimeSpan.FromTicks((long)Math.Round(
						spec.Duration.Ticks * point.Time)))
			});
		}
		return animation;
	}

	private static bool IsSteps(string value) =>
		value.StartsWith("steps(", StringComparison.Ordinal);

	private static (int Count, string Position) ParseSteps(string value)
	{
		if (!value.StartsWith("steps(", StringComparison.Ordinal)
			|| !value.EndsWith(')'))
			throw new InvalidDataException($"Invalid CSS steps timing function '{value}'.");
		var parts = value[6..^1].Split(
			',',
			StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length is < 1 or > 2
			|| !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var count)
			|| count < 1)
		{
			throw new InvalidDataException($"Invalid CSS steps timing function '{value}'.");
		}
		var position = parts.Length == 1 ? "jump-end" : parts[1] switch
		{
			"start" => "jump-start",
			"end" => "jump-end",
			"jump-start" or "jump-end" or "jump-both" or "jump-none" => parts[1],
			_ => throw new InvalidDataException($"Invalid CSS steps position in '{value}'.")
		};
		if (position == "jump-none" && count < 2)
			throw new InvalidDataException("steps(1, jump-none) has no transition step.");
		return (count, position);
	}

	private static KeySpline ResolveSpline(string timing) => timing switch
	{
		"ease" => CreateSpline(.25, .1, .25, 1),
		"ease-in" => CreateSpline(.42, 0, 1, 1),
		"ease-out" => CreateSpline(0, 0, .58, 1),
		"ease-in-out" => CreateSpline(.42, 0, .58, 1),
		_ when timing.StartsWith("cubic-bezier(", StringComparison.Ordinal) =>
			ParseCubicBezier(timing),
		_ => throw new InvalidDataException(
			$"Unsupported CSS transition timing function '{timing}'.")
	};

	private static KeySpline ParseCubicBezier(string value)
	{
		var parts = value[13..^1].Split(
			',',
			StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length != 4
			|| !parts.All(static part => double.TryParse(
				part,
				NumberStyles.Float,
				CultureInfo.InvariantCulture,
				out _)))
		{
			throw new InvalidDataException(
				$"Invalid CSS cubic-bezier timing function '{value}'.");
		}
		var numbers = parts.Select(static part => double.Parse(
			part,
			CultureInfo.InvariantCulture)).ToArray();
		if (numbers[0] is < 0 or > 1 || numbers[2] is < 0 or > 1)
			throw new InvalidDataException(
				$"Invalid CSS cubic-bezier x coordinate in '{value}'.");
		return CreateSpline(numbers[0], numbers[1], numbers[2], numbers[3]);
	}

	private static KeySpline CreateSpline(
		double x1,
		double y1,
		double x2,
		double y2) => new()
	{
		ControlPoint1 = new Point(x1, y1),
		ControlPoint2 = new Point(x2, y2)
	};

	private static bool TryResolve(
		Materialization state,
		string property,
		out TransitionSpec spec)
	{
		var properties = SplitCssList(state.Properties);
		var index = properties.FindIndex(item =>
			item.Equals(property, StringComparison.OrdinalIgnoreCase)
				|| item == "all");
		if (index < 0 || properties[index] == "none")
		{
			spec = default;
			return false;
		}
		spec = ResolveAt(state, property, index);
		return true;
	}

	private static TransitionSpec ResolveAt(
		Materialization state,
		string property,
		int index)
	{
		var durations = SplitCssList(state.Durations);
		var delays = SplitCssList(state.Delays);
		var timings = SplitCssList(state.TimingFunctions);
		var durationText = ValueAt(durations, index, "0s");
		var delayText = ValueAt(delays, index, "0s");
		return new(
			property,
			durationText,
			ParseTime(durationText),
			ValueAt(timings, index, "ease"),
			delayText,
			ParseTime(delayText));
	}

	private static string ComposeShorthand(Materialization state)
	{
		var properties = SplitCssList(state.Properties);
		if (properties.Count == 1 && properties[0] == "none")
			return "none";
		return string.Join(
			", ",
			properties.Select((property, index) =>
			{
				var spec = ResolveAt(state, property, index);
				return $"{property} {spec.DurationText} "
					+ $"{spec.TimingFunction} {spec.DelayText}";
			}));
	}

	private static string NormalizeList(
		string expression,
		Func<string, string> normalize)
	{
		var values = SplitCssList(expression);
		if (values.Count == 0)
			throw new InvalidDataException("A CSS transition list is empty.");
		return string.Join(", ", values.Select(normalize));
	}

	private static List<string> SplitCssList(string value)
	{
		var result = new List<string>();
		var start = 0;
		var depth = 0;
		for (var index = 0; index < value.Length; index++)
		{
			switch (value[index])
			{
				case '(':
					depth++;
					break;
				case ')':
					depth--;
					break;
				case ',' when depth == 0:
					AddListValue(result, value[start..index]);
					start = index + 1;
					break;
			}
			if (depth < 0)
				throw new InvalidDataException(
					$"Unbalanced CSS transition expression '{value}'.");
		}
		if (depth != 0)
			throw new InvalidDataException(
				$"Unbalanced CSS transition expression '{value}'.");
		AddListValue(result, value[start..]);
		return result;
	}

	private static void AddListValue(List<string> result, string value)
	{
		var normalized = value.Trim().ToLowerInvariant();
		if (normalized.Length > 0)
			result.Add(normalized);
	}

	private static string NormalizeProperty(string value)
	{
		if (value.Any(char.IsWhiteSpace))
			throw new InvalidDataException(
				$"Invalid CSS transition property '{value}'.");
		return value;
	}

	private static string NormalizeDuration(string value)
	{
		if (ParseTime(value) < TimeSpan.Zero)
			throw new InvalidDataException(
				$"CSS transition-duration cannot be negative: '{value}'.");
		return value;
	}

	private static string NormalizeDelay(string value)
	{
		_ = ParseTime(value);
		return value;
	}

	private static string NormalizeTimingFunction(string value)
	{
		if (value is "linear" or "ease" or "ease-in" or "ease-out" or "ease-in-out")
			return value;
		if (value.StartsWith("cubic-bezier(", StringComparison.Ordinal)
			&& value.EndsWith(')'))
		{
			_ = ParseCubicBezier(value);
			return value;
		}
		if (value.StartsWith("steps(", StringComparison.Ordinal)
			&& value.EndsWith(')'))
		{
			_ = ParseSteps(value);
			return value;
		}
		throw new InvalidDataException(
			$"Unsupported CSS transition timing function '{value}'.");
	}

	private static TimeSpan ParseTime(string value)
	{
		var text = value.Trim().ToLowerInvariant();
		var milliseconds = text.EndsWith("ms", StringComparison.Ordinal);
		var seconds = !milliseconds && text.EndsWith('s');
		if (!milliseconds && !seconds)
			throw new InvalidDataException(
				$"Invalid CSS transition time '{value}'.");
		var number = milliseconds ? text[..^2] : text[..^1];
		if (!double.TryParse(
				number,
				NumberStyles.Float,
				CultureInfo.InvariantCulture,
				out var parsed))
		{
			throw new InvalidDataException(
				$"Invalid CSS transition time '{value}'.");
		}
		return TimeSpan.FromMilliseconds(
			parsed * (milliseconds ? 1 : 1000));
	}

	private static string ValueAt(
		IReadOnlyList<string> values,
		int index,
		string fallback) => values.Count == 0
		? fallback
		: values[index % values.Count];
}

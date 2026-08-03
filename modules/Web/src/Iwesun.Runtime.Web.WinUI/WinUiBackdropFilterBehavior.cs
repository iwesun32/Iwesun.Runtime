using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Windows.Graphics.Effects;

namespace Iwesun.Runtime.Web.WinUI;

internal static partial class WinUiBackdropFilterBehavior
{
	private const string LayerKey = "backdrop-filter";

	private sealed record FilterSpec(string Name, float Value);

	private sealed record Materialization(
		string Rule,
		ContainerVisual? Layer,
		SpriteVisual? Sprite,
		CompositionEffectBrush? EffectBrush,
		CompositionBackdropBrush? BackdropBrush,
		IReadOnlyList<FilterSpec> Specs);

	private static readonly ConditionalWeakTable<FrameworkElement, Materialization>
		Materializations = new();

	internal static void Apply(FrameworkElement owner, string rule)
	{
		ArgumentNullException.ThrowIfNull(owner);
		if (Materializations.TryGetValue(owner, out var previous))
		{
			owner.SizeChanged -= OnSizeChanged;
			if (previous.Layer is not null)
				WinUiCompositionLayerRegistry.Release(owner, LayerKey);
			Materializations.Remove(owner);
		}
		var normalized = Regex.Replace(
			rule.Trim().ToLowerInvariant(),
			@"\s+",
			" ");
		if (normalized == "none")
		{
			Materializations.Add(owner, new(normalized, null, null, null, null, []));
			return;
		}
		var specs = Parse(normalized);
		var layer = WinUiCompositionLayerRegistry.Acquire(owner, LayerKey);
		var compositor = layer.Compositor;
		var sourceParameter = new CompositionEffectSourceParameter("backdrop");
		IGraphicsEffectSource source = sourceParameter;
		for (var index = 0; index < specs.Count; index++)
			source = CreateEffect(specs[index], source, index);
		var animatedProperties = specs
			.Select((spec, index) => EffectPropertyPath(spec, index))
			.Where(static path => path.Length != 0)
			.ToArray();
		var factory = compositor.CreateEffectFactory(
			(IGraphicsEffect)source,
			animatedProperties);
		var effectBrush = factory.CreateBrush();
		for (var index = 0; index < specs.Count; index++)
		{
			var path = EffectPropertyPath(specs[index], index);
			if (path.Length != 0)
				effectBrush.Properties.InsertScalar(path, PhysicalValue(specs[index]));
		}
		var backdropBrush = compositor.CreateBackdropBrush();
		effectBrush.SetSourceParameter("backdrop", backdropBrush);
		var sprite = compositor.CreateSpriteVisual();
		sprite.Brush = effectBrush;
		layer.Children.InsertAtTop(sprite);
		var state = new Materialization(
			normalized,
			layer,
			sprite,
			effectBrush,
			backdropBrush,
			specs);
		Materializations.Add(owner, state);
		UpdateSize(owner, state);
		owner.SizeChanged += OnSizeChanged;
	}

	internal static bool TryRead(FrameworkElement owner, out string value)
	{
		value = string.Empty;
		if (!Materializations.TryGetValue(owner, out var state))
			return false;
		if (state.Specs.Count == 0)
		{
			value = "none";
			return state.Layer is null
				&& state.Sprite is null
				&& state.EffectBrush is null
				&& state.BackdropBrush is null;
		}
		if (state.Layer is null
			|| state.Sprite is null
			|| state.EffectBrush is null
			|| state.BackdropBrush is null
			|| !WinUiCompositionLayerRegistry.Owns(owner, LayerKey, state.Layer)
			|| state.Layer.Children.Count != 1
			|| !ReferenceEquals(state.Sprite.Parent, state.Layer)
			|| !ReferenceEquals(state.Sprite.Brush, state.EffectBrush))
		{
			return false;
		}
		var expected = CurrentSize(owner);
		if (Math.Abs(state.Sprite.Size.X - expected.X) > .001f
			|| Math.Abs(state.Sprite.Size.Y - expected.Y) > .001f)
		{
			return false;
		}
		for (var index = 0; index < state.Specs.Count; index++)
		{
			var path = EffectPropertyPath(state.Specs[index], index);
			if (path.Length == 0)
				continue;
			if (state.EffectBrush.Properties.TryGetScalar(path, out var actual)
				!= CompositionGetValueStatus.Succeeded
				|| Math.Abs(actual - PhysicalValue(state.Specs[index])) > .0001f)
			{
				return false;
			}
		}
		value = state.Rule;
		return true;
	}

	private static IGraphicsEffectSource CreateEffect(
		FilterSpec spec,
		IGraphicsEffectSource source,
		int index) => spec.Name switch
	{
		"blur" => new GaussianBlurEffect
		{
			Name = EffectName(index),
			Source = source,
			BlurAmount = spec.Value,
			BorderMode = EffectBorderMode.Hard
		},
		"brightness" => new ExposureEffect
		{
			Name = EffectName(index),
			Source = source,
			Exposure = MathF.Log2(spec.Value)
		},
		"contrast" => new ContrastEffect
		{
			Name = EffectName(index),
			Source = source,
			Contrast = spec.Value - 1
		},
		"grayscale" => new SaturationEffect
		{
			Name = EffectName(index),
			Source = source,
			Saturation = 1 - spec.Value
		},
		"hue-rotate" => new HueRotationEffect
		{
			Name = EffectName(index),
			Source = source,
			Angle = spec.Value
		},
		"invert" => new ColorMatrixEffect
		{
			Name = EffectName(index),
			Source = source,
			ColorMatrix = CreateInvertMatrix(spec.Value)
		},
		"opacity" => new OpacityEffect
		{
			Name = EffectName(index),
			Source = source,
			Opacity = spec.Value
		},
		"saturate" => new SaturationEffect
		{
			Name = EffectName(index),
			Source = source,
			Saturation = spec.Value
		},
		"sepia" => new SepiaEffect
		{
			Name = EffectName(index),
			Source = source,
			Intensity = spec.Value
		},
		_ => throw new InvalidDataException(
			$"Unsupported CSS backdrop-filter function '{spec.Name}'.")
	};

	private static string EffectName(int index) => $"effect{index}";

	private static string EffectPropertyPath(FilterSpec spec, int index)
	{
		var property = spec.Name switch
		{
			"blur" => "BlurAmount",
			"brightness" => "Exposure",
			"contrast" => "Contrast",
			"grayscale" => "Saturation",
			"invert" => string.Empty,
			"hue-rotate" => "Angle",
			"opacity" => "Opacity",
			"saturate" => "Saturation",
			"sepia" => "Intensity",
			_ => string.Empty
		};
		return property.Length == 0 ? string.Empty : $"{EffectName(index)}.{property}";
	}

	private static float PhysicalValue(FilterSpec spec) => spec.Name switch
	{
		"brightness" => MathF.Log2(spec.Value),
		"contrast" => spec.Value - 1,
		"grayscale" => 1 - spec.Value,
		_ => spec.Value
	};

	private static Matrix5x4 CreateInvertMatrix(float amount)
	{
		var scale = 1 - (2 * amount);
		return new Matrix5x4(
			scale, 0, 0, 0,
			0, scale, 0, 0,
			0, 0, scale, 0,
			0, 0, 0, 1,
			amount, amount, amount, 0);
	}

	private static IReadOnlyList<FilterSpec> Parse(string expression)
	{
		var compact = Regex.Replace(expression, @"\s+", string.Empty);
		var matches = FunctionRegex().Matches(compact);
		if (matches.Count == 0
			|| string.Concat(matches.Select(match => match.Value)) != compact)
		{
			throw new InvalidDataException(
				$"Invalid CSS backdrop-filter '{expression}'.");
		}
		return matches.Select(match => ParseFunction(
			match.Groups["name"].Value,
			match.Groups["value"].Value)).ToArray();
	}

	private static FilterSpec ParseFunction(string name, string value)
	{
		if (name == "blur")
			return new(name, ParsePixels(value));
		if (name == "hue-rotate")
			return new(name, ParseAngle(value));
		var factor = ParseFactor(value);
		if (name is "grayscale" or "invert" or "opacity" or "sepia")
			factor = Math.Clamp(factor, 0, 1);
		if (name == "brightness" && factor is <= .25f or > 4f)
			throw new InvalidDataException(
				"Composition ExposureEffect supports CSS brightness from 25% through 400%.");
		if (name == "contrast" && factor is < 0 or > 2)
			throw new InvalidDataException(
				"Composition ContrastEffect supports CSS contrast from 0% through 200%.");
		return new(name, factor);
	}

	private static float ParsePixels(string value)
	{
		if (value == "0")
			return 0;
		if (!value.EndsWith("px", StringComparison.Ordinal)
			|| !float.TryParse(value[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
			|| result < 0)
		{
			throw new InvalidDataException($"Invalid CSS blur radius '{value}'.");
		}
		return result;
	}

	private static float ParseFactor(string value)
	{
		var percentage = value.EndsWith('%');
		var token = percentage ? value[..^1] : value;
		if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
			|| result < 0)
		{
			throw new InvalidDataException($"Invalid CSS filter factor '{value}'.");
		}
		return percentage ? result / 100 : result;
	}

	private static float ParseAngle(string value)
	{
		var factor = value.EndsWith("deg", StringComparison.Ordinal) ? MathF.PI / 180
			: value.EndsWith("grad", StringComparison.Ordinal) ? MathF.PI / 200
			: value.EndsWith("turn", StringComparison.Ordinal) ? MathF.Tau
			: value.EndsWith("rad", StringComparison.Ordinal) ? 1
			: float.NaN;
		var suffix = value.EndsWith("grad", StringComparison.Ordinal)
			|| value.EndsWith("turn", StringComparison.Ordinal) ? 4 : 3;
		if (float.IsNaN(factor)
			|| !float.TryParse(value[..^suffix], NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
		{
			throw new InvalidDataException($"Invalid CSS hue angle '{value}'.");
		}
		return number * factor;
	}

	private static void OnSizeChanged(object sender, SizeChangedEventArgs args)
	{
		if (sender is FrameworkElement owner
			&& Materializations.TryGetValue(owner, out var state))
		{
			UpdateSize(owner, state);
		}
	}

	private static void UpdateSize(FrameworkElement owner, Materialization state)
	{
		if (state.Layer is null || state.Sprite is null)
			return;
		var size = CurrentSize(owner);
		state.Layer.Size = size;
		state.Sprite.Size = size;
	}

	private static Vector2 CurrentSize(FrameworkElement owner)
	{
		var width = owner.ActualWidth > 0 ? owner.ActualWidth : owner.Width;
		var height = owner.ActualHeight > 0 ? owner.ActualHeight : owner.Height;
		return new(
			(float)(double.IsFinite(width) ? Math.Max(0, width) : 0),
			(float)(double.IsFinite(height) ? Math.Max(0, height) : 0));
	}

	[GeneratedRegex(@"(?<name>[a-z-]+)\((?<value>[^)]*)\)", RegexOptions.CultureInvariant)]
	private static partial Regex FunctionRegex();
}

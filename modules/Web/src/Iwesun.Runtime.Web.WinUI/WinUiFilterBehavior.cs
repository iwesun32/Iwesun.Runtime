using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Windows.Graphics.Effects;

namespace Iwesun.Runtime.Web.WinUI;

internal static partial class WinUiFilterBehavior
{
	private const string LayerKey = "foreground-filter";
	private sealed record FilterSpec(string Name, float Value);

	private sealed record Materialization(
		string Rule,
		Visual? Visual,
		float Opacity,
		ContainerVisual? Layer,
		SpriteVisual? Sprite,
		CompositionVisualSurface? Surface,
		CompositionSurfaceBrush? SurfaceBrush,
		CompositionEffectBrush? EffectBrush,
		Visual? SourceVisual,
		float SourceOpacity,
		IReadOnlyList<FilterSpec> Specs);

	private static readonly ConditionalWeakTable<
		FrameworkElement,
		Materialization> Materializations = new();

	internal static bool RequiresIsolation(string rule) =>
		Parse(rule.Trim().ToLowerInvariant())
			.Any(static spec => spec.Name != "opacity");

	internal static void Apply(FrameworkElement owner, string rule)
	{
		ArgumentNullException.ThrowIfNull(owner);
		var normalized = rule.Trim().ToLowerInvariant();
		if (Materializations.TryGetValue(owner, out var previous))
		{
			owner.SizeChanged -= OnSizeChanged;
			if (previous.Visual is not null)
				previous.Visual.Opacity = 1;
			if (previous.SourceVisual is not null)
				previous.SourceVisual.Opacity = previous.SourceOpacity;
			if (previous.Layer is not null)
				WinUiCompositionLayerRegistry.Release(owner, LayerKey);
		}
		Materializations.Remove(owner);
		if (normalized == "none")
		{
			Materializations.Add(owner, new(
				normalized, null, 1, null, null, null, null, null, null, 1, []));
			return;
		}
		var specs = Parse(normalized);
		if (specs.All(static spec => spec.Name == "opacity"))
		{
			var visual = ElementCompositionPreview.GetElementVisual(owner);
			var opacity = specs.Aggregate(1f, static (value, spec) => value * spec.Value);
			visual.Opacity = opacity;
			Materializations.Add(owner, new(
				normalized, visual, opacity, null, null, null, null, null, null, 1, specs));
			return;
		}
		if (owner is not HtmlFilteredElementHost host)
		{
			throw new InvalidOperationException(
				$"CSS filter '{normalized}' requires an isolated "
				+ $"{nameof(HtmlFilteredElementHost)} source visual.");
		}
		var layer = WinUiCompositionLayerRegistry.Acquire(owner, LayerKey);
		var compositor = layer.Compositor;
		var sourceVisual = ElementCompositionPreview.GetElementVisual(host.InnerElement);
		var sourceOpacity = sourceVisual.Opacity;
		var surface = compositor.CreateVisualSurface();
		surface.SourceVisual = sourceVisual;
		var surfaceBrush = compositor.CreateSurfaceBrush(surface);
		var sourceParameter = new CompositionEffectSourceParameter("source");
		IGraphicsEffectSource effect = sourceParameter;
		for (var index = 0; index < specs.Count; index++)
			effect = CreateEffect(specs[index], effect, index);
		var paths = specs.Select((spec, index) => EffectPropertyPath(spec, index))
			.Where(static path => path.Length != 0).ToArray();
		var effectBrush = compositor.CreateEffectFactory(
			(IGraphicsEffect)effect, paths).CreateBrush();
		for (var index = 0; index < specs.Count; index++)
		{
			var path = EffectPropertyPath(specs[index], index);
			if (path.Length != 0)
				effectBrush.Properties.InsertScalar(path, PhysicalValue(specs[index]));
		}
		effectBrush.SetSourceParameter("source", surfaceBrush);
		var sprite = compositor.CreateSpriteVisual();
		sprite.Brush = effectBrush;
		layer.Children.InsertAtTop(sprite);
		sourceVisual.Opacity = 0;
		var state = new Materialization(
			normalized, null, 1, layer, sprite, surface, surfaceBrush,
			effectBrush, sourceVisual, sourceOpacity, specs);
		Materializations.Add(owner, state);
		UpdateSize(owner, state);
		owner.SizeChanged += OnSizeChanged;
	}

	internal static bool TryRead(
		FrameworkElement owner,
		out string value)
	{
		value = string.Empty;
		if (!Materializations.TryGetValue(owner, out var state))
			return false;
		if (state.Visual is null)
		{
			if (state.Specs.Count == 0)
			{
				value = "none";
				return state.Rule == "none" && state.Layer is null;
			}
			if (state.Layer is null || state.Sprite is null
				|| state.Surface is null || state.SurfaceBrush is null
				|| state.EffectBrush is null || state.SourceVisual is null
				|| state.SourceVisual.Opacity != 0
				|| !WinUiCompositionLayerRegistry.Owns(owner, LayerKey, state.Layer)
				|| !ReferenceEquals(state.Sprite.Brush, state.EffectBrush)
				|| !ReferenceEquals(state.Surface.SourceVisual, state.SourceVisual))
				return false;
			var size = CurrentSize(owner);
			if (!Near(state.Sprite.Size.X, size.X)
				|| !Near(state.Sprite.Size.Y, size.Y)
				|| !Near(state.Surface.SourceSize.X, size.X)
				|| !Near(state.Surface.SourceSize.Y, size.Y))
				return false;
			for (var index = 0; index < state.Specs.Count; index++)
			{
				var path = EffectPropertyPath(state.Specs[index], index);
				if (path.Length != 0
					&& (state.EffectBrush.Properties.TryGetScalar(path, out var actual)
						!= CompositionGetValueStatus.Succeeded
						|| !Near(actual, PhysicalValue(state.Specs[index]))))
					return false;
			}
			value = state.Rule;
			return true;
		}
		if (!ReferenceEquals(
				ElementCompositionPreview.GetElementVisual(owner),
				state.Visual)
			|| Math.Abs(state.Visual.Opacity - state.Opacity) > .0001f)
		{
			return false;
		}
		value = state.Rule;
		return true;
	}

	internal static float ExpectedVisualOpacity(FrameworkElement owner) =>
		Materializations.TryGetValue(owner, out var state)
			? state.Opacity
			: 1f;

	private static IReadOnlyList<FilterSpec> Parse(string expression)
	{
		var compact = Regex.Replace(expression, @"\s+", string.Empty);
		var matches = FunctionRegex().Matches(compact);
		if (matches.Count == 0)
			throw new InvalidDataException(
				$"Unsupported CSS filter '{expression}'.");
		var consumed = string.Concat(matches.Select(match => match.Value));
		if (!string.Equals(consumed, compact, StringComparison.Ordinal))
			throw new InvalidDataException(
				$"Unsupported CSS filter '{expression}'.");
		return matches.Select(match => ParseFunction(
			match.Groups["name"].Value,
			match.Groups["value"].Value)).ToArray();
	}

	private static FilterSpec ParseFunction(string name, string value)
	{
		if (name == "blur") return new(name, ParsePixels(value));
		if (name == "hue-rotate") return new(name, ParseAngle(value));
		if (name is not ("brightness" or "contrast" or "grayscale" or
			"invert" or "opacity" or "saturate" or "sepia"))
			throw new InvalidDataException($"Unsupported CSS filter function '{name}'.");
		var factor = ParseFactor(value);
		if (name is "grayscale" or "invert" or "opacity" or "sepia")
			factor = Math.Clamp(factor, 0, 1);
		if (name == "brightness" && factor is <= .25f or > 4f)
			throw new InvalidDataException("Composition ExposureEffect supports brightness from 25% through 400%.");
		if (name == "contrast" && factor is < 0 or > 2)
			throw new InvalidDataException("Composition ContrastEffect supports contrast from 0% through 200%.");
		return new(name, factor);
	}

	private static IGraphicsEffectSource CreateEffect(FilterSpec spec, IGraphicsEffectSource source, int index) => spec.Name switch
	{
		"blur" => new GaussianBlurEffect { Name = EffectName(index), Source = source, BlurAmount = spec.Value, BorderMode = EffectBorderMode.Hard },
		"brightness" => new ExposureEffect { Name = EffectName(index), Source = source, Exposure = MathF.Log2(spec.Value) },
		"contrast" => new ContrastEffect { Name = EffectName(index), Source = source, Contrast = spec.Value - 1 },
		"grayscale" => new SaturationEffect { Name = EffectName(index), Source = source, Saturation = 1 - spec.Value },
		"hue-rotate" => new HueRotationEffect { Name = EffectName(index), Source = source, Angle = spec.Value },
		"invert" => new ColorMatrixEffect { Name = EffectName(index), Source = source, ColorMatrix = CreateInvertMatrix(spec.Value) },
		"opacity" => new OpacityEffect { Name = EffectName(index), Source = source, Opacity = spec.Value },
		"saturate" => new SaturationEffect { Name = EffectName(index), Source = source, Saturation = spec.Value },
		"sepia" => new SepiaEffect { Name = EffectName(index), Source = source, Intensity = spec.Value },
		_ => throw new InvalidDataException($"Unsupported CSS filter function '{spec.Name}'.")
	};

	private static string EffectName(int index) => $"effect{index}";
	private static string EffectPropertyPath(FilterSpec spec, int index)
	{
		var property = spec.Name switch { "blur" => "BlurAmount", "brightness" => "Exposure", "contrast" => "Contrast", "grayscale" => "Saturation", "hue-rotate" => "Angle", "opacity" => "Opacity", "saturate" => "Saturation", "sepia" => "Intensity", _ => string.Empty };
		return property.Length == 0 ? string.Empty : $"{EffectName(index)}.{property}";
	}
	private static float PhysicalValue(FilterSpec spec) => spec.Name switch { "brightness" => MathF.Log2(spec.Value), "contrast" => spec.Value - 1, "grayscale" => 1 - spec.Value, _ => spec.Value };
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
	private static float ParsePixels(string value) { if (value == "0") return 0; if (!value.EndsWith("px", StringComparison.Ordinal) || !float.TryParse(value[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var result) || result < 0) throw new InvalidDataException($"Invalid CSS blur radius '{value}'."); return result; }
	private static float ParseFactor(string value) { var percentage = value.EndsWith('%'); var token = percentage ? value[..^1] : value; if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) || result < 0) throw new InvalidDataException($"Invalid CSS filter factor '{value}'."); return percentage ? result / 100 : result; }
	private static float ParseAngle(string value) { var factor = value.EndsWith("deg", StringComparison.Ordinal) ? MathF.PI / 180 : value.EndsWith("grad", StringComparison.Ordinal) ? MathF.PI / 200 : value.EndsWith("turn", StringComparison.Ordinal) ? MathF.Tau : value.EndsWith("rad", StringComparison.Ordinal) ? 1 : float.NaN; var suffix = value.EndsWith("grad", StringComparison.Ordinal) || value.EndsWith("turn", StringComparison.Ordinal) ? 4 : 3; if (float.IsNaN(factor) || !float.TryParse(value[..^suffix], NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) throw new InvalidDataException($"Invalid CSS hue angle '{value}'."); return number * factor; }

	private static void OnSizeChanged(object sender, SizeChangedEventArgs args) { if (sender is FrameworkElement owner && Materializations.TryGetValue(owner, out var state)) UpdateSize(owner, state); }
	private static void UpdateSize(FrameworkElement owner, Materialization state) { if (state.Layer is null || state.Sprite is null || state.Surface is null) return; var size = CurrentSize(owner); state.Layer.Size = size; state.Sprite.Size = size; state.Surface.SourceSize = size; }
	private static Vector2 CurrentSize(FrameworkElement owner) { var width = owner.ActualWidth > 0 ? owner.ActualWidth : owner.Width; var height = owner.ActualHeight > 0 ? owner.ActualHeight : owner.Height; return new((float)(double.IsFinite(width) ? Math.Max(0, width) : 0), (float)(double.IsFinite(height) ? Math.Max(0, height) : 0)); }
	private static bool Near(double left, double right) => Math.Abs(left - right) <= .001;

	[GeneratedRegex(@"(?<name>[a-z-]+)\((?<value>[^)]*)\)", RegexOptions.CultureInvariant)]
	private static partial Regex FunctionRegex();
}

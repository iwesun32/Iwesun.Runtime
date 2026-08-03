using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.UI.Composition;
using Microsoft.Graphics.DirectX;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace Iwesun.Runtime.Web.WinUI;

internal static partial class WinUiBoxShadowBehavior
{
	private const string LayerKey = "box-shadow";

	private sealed record ShadowSpec(
		bool Inset,
		double OffsetX,
		double OffsetY,
		double Blur,
		double Spread,
		Color Color,
		float Opacity,
		string Expression);

	private sealed record Materialization(
		string Rule,
		ContainerVisual? Container,
		IReadOnlyList<SpriteVisual> Sprites,
		IReadOnlyList<DropShadow> Shadows,
		IReadOnlyList<CompositionDrawingSurface> InsetSurfaces,
		IReadOnlyList<CompositionSurfaceBrush> InsetBrushes,
		IReadOnlyList<ShadowSpec> Specs);

	private static readonly CanvasDevice CanvasDevice = CanvasDevice.GetSharedDevice();
	private static readonly ConditionalWeakTable<Compositor, CompositionGraphicsDevice>
		GraphicsDevices = new();

	private static readonly ConditionalWeakTable<
		FrameworkElement,
		Materialization> Materializations = new();

	internal static void Apply(FrameworkElement owner, string rule)
	{
		ArgumentNullException.ThrowIfNull(owner);
		if (Materializations.TryGetValue(owner, out var previous))
		{
			owner.SizeChanged -= OnSizeChanged;
			if (previous.Container is not null)
				WinUiCompositionLayerRegistry.Release(owner, LayerKey);
			Materializations.Remove(owner);
		}
		var normalized = rule.Trim().ToLowerInvariant();
		if (normalized == "none")
		{
			Materializations.Add(
				owner,
				new(normalized, null, [], [], [], [], []));
			return;
		}
		var specs = Parse(normalized);
		var container = WinUiCompositionLayerRegistry.Acquire(owner, LayerKey);
		var compositor = container.Compositor;
		var sprites = new List<SpriteVisual>(specs.Count);
		var shadows = new List<DropShadow>(specs.Count);
		var insetSurfaces = new List<CompositionDrawingSurface>();
		var insetBrushes = new List<CompositionSurfaceBrush>();
		foreach (var spec in specs)
		{
			var sprite = compositor.CreateSpriteVisual();
			if (spec.Inset)
			{
				var graphicsDevice = GraphicsDevices.GetValue(
					compositor,
					static value => CanvasComposition.CreateCompositionGraphicsDevice(
						value,
						CanvasDevice));
				var surface = graphicsDevice.CreateDrawingSurface(
					new Size(1, 1),
					DirectXPixelFormat.B8G8R8A8UIntNormalized,
					DirectXAlphaMode.Premultiplied);
				var brush = compositor.CreateSurfaceBrush(surface);
				sprite.Brush = brush;
				insetSurfaces.Add(surface);
				insetBrushes.Add(brush);
			}
			else
			{
				var shadow = compositor.CreateDropShadow();
				shadow.BlurRadius = (float)spec.Blur;
				shadow.Offset = new Vector3(
					(float)spec.OffsetX,
					(float)spec.OffsetY,
					0);
				shadow.Color = spec.Color;
				shadow.Opacity = spec.Opacity;
				sprite.Shadow = shadow;
				shadows.Add(shadow);
			}
			container.Children.InsertAtTop(sprite);
			sprites.Add(sprite);
		}
		var state = new Materialization(
			normalized,
			container,
			sprites,
			shadows,
			insetSurfaces,
			insetBrushes,
			specs);
		Materializations.Add(owner, state);
		// The shared registry owns the single XAML child-visual slot.
		// Installing this contribution directly would replace the registry root.
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
		if (state.Specs.Count == 0)
		{
			value = "none";
			return state.Container is null;
		}
		if (state.Container is null
			|| state.Sprites.Count != state.Specs.Count
			|| state.Shadows.Count != state.Specs.Count(spec => !spec.Inset)
			|| state.InsetSurfaces.Count != state.Specs.Count(spec => spec.Inset)
			|| state.InsetBrushes.Count != state.InsetSurfaces.Count
			|| !WinUiCompositionLayerRegistry.Owns(
				owner,
				LayerKey,
				state.Container)
			|| state.Container.Children.Count != state.Specs.Count)
		{
			return false;
		}
		var outerIndex = 0;
		var insetIndex = 0;
		for (var index = 0; index < state.Specs.Count; index++)
		{
			var spec = state.Specs[index];
			var sprite = state.Sprites[index];
			if (!ReferenceEquals(sprite.Parent, state.Container))
			{
				return false;
			}
			if (spec.Inset)
			{
				var expectedSize = CalculateOwnerSize(owner);
				var surface = state.InsetSurfaces[insetIndex];
				var brush = state.InsetBrushes[insetIndex++];
				if (!ReferenceEquals(sprite.Brush, brush)
					|| !ReferenceEquals(brush.Surface, surface)
					|| sprite.Shadow is not null
					|| !Near(sprite.Size.X, expectedSize.X)
					|| !Near(sprite.Size.Y, expectedSize.Y)
					|| !Near(surface.Size.Width, Math.Max(1, Math.Ceiling(expectedSize.X)))
					|| !Near(surface.Size.Height, Math.Max(1, Math.Ceiling(expectedSize.Y))))
				{
					return false;
				}
			}
			else
			{
				var shadow = state.Shadows[outerIndex++];
				var expectedSize = CalculateSize(owner, spec.Spread);
				var expectedOffset = new Vector3(
					(float)-spec.Spread,
					(float)-spec.Spread,
					0);
				if (!ReferenceEquals(sprite.Shadow, shadow)
					|| !Near(shadow.BlurRadius, spec.Blur)
					|| !Near(shadow.Offset.X, spec.OffsetX)
					|| !Near(shadow.Offset.Y, spec.OffsetY)
					|| shadow.Color != spec.Color
					|| !Near(shadow.Opacity, spec.Opacity)
					|| !Near(sprite.Size.X, expectedSize.X)
					|| !Near(sprite.Size.Y, expectedSize.Y)
					|| !Near(sprite.Offset.X, expectedOffset.X)
					|| !Near(sprite.Offset.Y, expectedOffset.Y))
				{
					return false;
				}
			}
		}
		value = string.Join(", ", state.Specs.Select(spec => spec.Expression));
		return true;
	}

	internal static string Describe(FrameworkElement owner)
	{
		if (!Materializations.TryGetValue(owner, out var state))
			return "materialization=missing";
		return $"root={WinUiCompositionLayerRegistry.OwnsRoot(owner)};"
			+ $"layer={(state.Container is not null && WinUiCompositionLayerRegistry.Owns(owner, LayerKey, state.Container))};"
			+ $"children={state.Container?.Children.Count ?? -1};"
			+ $"sprites={state.Sprites.Count};shadows={state.Shadows.Count};insetSurfaces={state.InsetSurfaces.Count};specs={state.Specs.Count};"
			+ $"firstParent={(state.Sprites.Count > 0 && ReferenceEquals(state.Sprites[0].Parent, state.Container))}";
	}

	internal static bool OwnsChildVisual(FrameworkElement owner) =>
		Materializations.TryGetValue(owner, out var state)
		&& state.Container is not null
		&& WinUiCompositionLayerRegistry.Owns(
			owner,
			LayerKey,
			state.Container);

	private static void OnSizeChanged(
		object sender,
		SizeChangedEventArgs args)
	{
		if (sender is FrameworkElement owner
			&& Materializations.TryGetValue(owner, out var state))
		{
			UpdateSize(owner, state);
		}
	}

	private static void UpdateSize(
		FrameworkElement owner,
		Materialization state)
	{
		if (state.Container is null)
			return;
		state.Container.Size = CalculateOwnerSize(owner);
		var insetIndex = 0;
		for (var index = 0; index < state.Specs.Count; index++)
		{
			var spec = state.Specs[index];
			if (spec.Inset)
			{
				var size = CalculateOwnerSize(owner);
				state.Sprites[index].Size = size;
				state.Sprites[index].Offset = Vector3.Zero;
				DrawInsetShadow(
					state.InsetSurfaces[insetIndex++],
					size,
					spec);
			}
			else
			{
				state.Sprites[index].Size = CalculateSize(owner, spec.Spread);
				state.Sprites[index].Offset = new Vector3(
					(float)-spec.Spread,
					(float)-spec.Spread,
					0);
			}
		}
	}

	private static void DrawInsetShadow(
		CompositionDrawingSurface surface,
		Vector2 size,
		ShadowSpec spec)
	{
		var width = Math.Max(1, (int)Math.Ceiling(size.X));
		var height = Math.Max(1, (int)Math.Ceiling(size.Y));
		CanvasComposition.Resize(surface, new Size(width, height));
		using var mask = new CanvasCommandList(CanvasDevice);
		using (var maskSession = mask.CreateDrawingSession())
		{
			var extent = (float)Math.Ceiling(
				spec.Blur * 3 + Math.Abs(spec.Spread)
				+ Math.Max(Math.Abs(spec.OffsetX), Math.Abs(spec.OffsetY)) + 2);
			var color = Color.FromArgb(
				(byte)Math.Round(spec.Opacity * 255),
				spec.Color.R,
				spec.Color.G,
				spec.Color.B);
			maskSession.Clear(Color.FromArgb(0, 0, 0, 0));
			maskSession.FillRectangle(
				-extent,
				-extent,
				width + extent * 2,
				height + extent * 2,
				color);
			maskSession.Blend = CanvasBlend.Copy;
			var holeLeft = (float)(spec.Spread + spec.OffsetX);
			var holeTop = (float)(spec.Spread + spec.OffsetY);
			maskSession.FillRectangle(
				holeLeft,
				holeTop,
				Math.Max(0, width - (float)spec.Spread * 2),
				Math.Max(0, height - (float)spec.Spread * 2),
				Color.FromArgb(0, 0, 0, 0));
		}
		using var drawingSession = CanvasComposition.CreateDrawingSession(surface);
		drawingSession.Clear(Color.FromArgb(0, 0, 0, 0));
		if (spec.Blur <= 0)
		{
			drawingSession.DrawImage(mask);
			return;
		}
		using var blur = new GaussianBlurEffect
		{
			Source = mask,
			BlurAmount = (float)spec.Blur,
			BorderMode = EffectBorderMode.Hard,
			Optimization = EffectOptimization.Quality
		};
		drawingSession.DrawImage(blur);
	}

	private static Vector2 CalculateSize(
		FrameworkElement owner,
		double spread)
	{
		var ownerSize = CalculateOwnerSize(owner);
		return new(
			(float)Math.Max(0, ownerSize.X + spread * 2),
			(float)Math.Max(0, ownerSize.Y + spread * 2));
	}

	private static Vector2 CalculateOwnerSize(FrameworkElement owner) =>
		new(
			(float)Math.Max(
				0,
				owner.ActualWidth > 0
					? owner.ActualWidth
					: double.IsFinite(owner.Width) ? owner.Width : 0),
			(float)Math.Max(
				0,
				owner.ActualHeight > 0
					? owner.ActualHeight
					: double.IsFinite(owner.Height) ? owner.Height : 0));

	private static IReadOnlyList<ShadowSpec> Parse(string expression)
	{
		var parts = SplitTopLevel(expression, ',');
		return parts.Select(ParseSingle).ToArray();
	}

	private static ShadowSpec ParseSingle(string expression)
	{
		var inset = expression.Split(
			' ',
			StringSplitOptions.RemoveEmptyEntries
				| StringSplitOptions.TrimEntries)
			.Contains("inset", StringComparer.Ordinal);
		var colorMatch = ColorRegex().Match(expression);
		if (!colorMatch.Success)
			throw new InvalidDataException(
				$"CSS box-shadow requires an explicit color: '{expression}'.");
		var colorText = colorMatch.Value;
		var brush = WinUiXamlElementObjectFactory.ParseBrush(colorText)
			as SolidColorBrush
			?? throw new InvalidDataException(
				$"Invalid CSS box-shadow color '{colorText}'.");
		var lengthText = expression.Remove(
			colorMatch.Index,
			colorMatch.Length)
			.Replace("inset", string.Empty, StringComparison.Ordinal)
			.Trim();
		var lengths = lengthText.Split(
			' ',
			StringSplitOptions.RemoveEmptyEntries
				| StringSplitOptions.TrimEntries);
		if (lengths.Length is < 2 or > 4)
			throw new InvalidDataException(
				$"Invalid CSS box-shadow lengths '{expression}'.");
		var values = lengths.Select(ParsePixels).ToArray();
		var blur = values.ElementAtOrDefault(2);
		var spread = values.ElementAtOrDefault(3);
		if (blur < 0)
			throw new InvalidDataException(
				$"CSS box-shadow blur cannot be negative: '{expression}'.");
		var opaqueColor = Color.FromArgb(
			255,
			brush.Color.R,
			brush.Color.G,
			brush.Color.B);
		return new(
			inset,
			values[0],
			values[1],
			blur,
			spread,
			opaqueColor,
			brush.Color.A / 255f,
			expression);
	}

	private static double ParsePixels(string value)
	{
		if (value == "0")
			return 0;
		if (!value.EndsWith("px", StringComparison.Ordinal)
			|| !double.TryParse(
				value[..^2],
				NumberStyles.Float,
				CultureInfo.InvariantCulture,
				out var result))
		{
			throw new InvalidDataException(
				$"Unsupported CSS box-shadow length '{value}'.");
		}
		return result;
	}

	private static List<string> SplitTopLevel(string value, char separator)
	{
		var result = new List<string>();
		var depth = 0;
		var start = 0;
		for (var index = 0; index < value.Length; index++)
		{
			if (value[index] == '(')
				depth++;
			else if (value[index] == ')')
				depth--;
			else if (value[index] == separator && depth == 0)
			{
				result.Add(value[start..index].Trim());
				start = index + 1;
			}
		}
		result.Add(value[start..].Trim());
		return result;
	}

	private static bool Near(double left, double right) =>
		Math.Abs(left - right) <= .001;

	[GeneratedRegex(
		@"(?:rgba?\([^)]*\)|#[0-9a-f]{3,8})",
		RegexOptions.CultureInvariant)]
	private static partial Regex ColorRegex();
}

using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Iwesun.Runtime.Web.WinUI;

internal static partial class WinUiTextShadowBehavior
{
	private const string LayerKey = "text-shadow";

	private sealed record ShadowSpec(
		double OffsetX,
		double OffsetY,
		double Blur,
		Color Color,
		float Opacity,
		string Expression);

	private sealed record TargetMaterialization(
		TextBlock TextTarget,
		ContainerVisual Container,
		IReadOnlyList<SpriteVisual> Sprites,
		IReadOnlyList<DropShadow> Shadows,
		CompositionBrush AlphaMask);

	private sealed record Materialization(
		string Rule,
		IReadOnlyList<TargetMaterialization> Targets,
		IReadOnlyList<ShadowSpec> Specs);

	private static readonly ConditionalWeakTable<
		FrameworkElement,
		Materialization> Materializations = new();
	private static readonly ConditionalWeakTable<
		TextBlock,
		Materialization> TextTargets = new();

	internal static void Apply(
		FrameworkElement owner,
		string rule,
		string generatedTextMarker)
	{
		ArgumentNullException.ThrowIfNull(owner);
		if (Materializations.TryGetValue(owner, out var previous))
		{
			foreach (var target in previous.Targets)
			{
				target.TextTarget.SizeChanged -= OnSizeChanged;
				TextTargets.Remove(target.TextTarget);
				WinUiCompositionLayerRegistry.Release(
					target.TextTarget,
					LayerKey);
			}
			Materializations.Remove(owner);
		}
		var normalized = rule.Trim().ToLowerInvariant();
		if (normalized == "none")
		{
			Materializations.Add(
				owner,
				new(normalized, [], []));
			return;
		}
		var textTargets = ResolveTextTargets(owner, generatedTextMarker);
		if (textTargets.Count == 0)
			throw new InvalidOperationException(
				$"{owner.GetType().Name} has no TextBlock alpha-mask target.");
		var specs = Parse(normalized);
		var targets = new List<TargetMaterialization>(textTargets.Count);
		foreach (var textTarget in textTargets)
		{
			var container = WinUiCompositionLayerRegistry.Acquire(
				textTarget,
				LayerKey);
			var compositor = container.Compositor;
			var mask = textTarget.GetAlphaMask();
			var sprites = new List<SpriteVisual>(specs.Count);
			var shadows = new List<DropShadow>(specs.Count);
			foreach (var spec in specs)
			{
				var shadow = compositor.CreateDropShadow();
				shadow.Mask = mask;
				shadow.BlurRadius = (float)spec.Blur;
				shadow.Offset = new Vector3(
					(float)spec.OffsetX,
					(float)spec.OffsetY,
					0);
				shadow.Color = spec.Color;
				shadow.Opacity = spec.Opacity;
				var sprite = compositor.CreateSpriteVisual();
				sprite.Shadow = shadow;
				container.Children.InsertAtTop(sprite);
				sprites.Add(sprite);
				shadows.Add(shadow);
			}
			targets.Add(new(textTarget, container, sprites, shadows, mask));
		}
		var state = new Materialization(normalized, targets, specs);
		Materializations.Add(owner, state);
		foreach (var target in targets)
		{
			TextTargets.Add(target.TextTarget, state);
			UpdateSize(target);
			target.TextTarget.SizeChanged += OnSizeChanged;
		}
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
			return state.Targets.Count == 0;
		}
		if (state.Targets.Count == 0)
			return false;
		foreach (var target in state.Targets)
		{
			if (target.Sprites.Count != state.Specs.Count
				|| target.Shadows.Count != state.Specs.Count
				|| !WinUiCompositionLayerRegistry.Owns(
					target.TextTarget, LayerKey, target.Container)
				|| target.Container.Children.Count != state.Specs.Count)
			{
				return false;
			}
			var expectedSize = CalculateSize(target.TextTarget);
			for (var index = 0; index < state.Specs.Count; index++)
			{
				var spec = state.Specs[index];
				var sprite = target.Sprites[index];
				var shadow = target.Shadows[index];
				if (!ReferenceEquals(sprite.Parent, target.Container)
					|| !ReferenceEquals(sprite.Shadow, shadow)
					|| !ReferenceEquals(shadow.Mask, target.AlphaMask)
					|| !Near(shadow.BlurRadius, spec.Blur)
					|| !Near(shadow.Offset.X, spec.OffsetX)
					|| !Near(shadow.Offset.Y, spec.OffsetY)
					|| shadow.Color != spec.Color
					|| !Near(shadow.Opacity, spec.Opacity)
					|| !Near(sprite.Size.X, expectedSize.X)
					|| !Near(sprite.Size.Y, expectedSize.Y))
					return false;
			}
		}
		value = string.Join(", ", state.Specs.Select(spec => spec.Expression));
		return true;
	}

	internal static bool OwnsChildVisual(FrameworkElement owner) =>
		Materializations.TryGetValue(owner, out var state)
		&& state.Targets.Count > 0
		&& state.Targets.All(target => WinUiCompositionLayerRegistry.Owns(
			target.TextTarget, LayerKey, target.Container));

	private static void OnSizeChanged(
		object sender,
		SizeChangedEventArgs args)
	{
		if (sender is not TextBlock textTarget)
			return;
		if (TextTargets.TryGetValue(textTarget, out var state))
		{
			var target = state.Targets.FirstOrDefault(candidate =>
				ReferenceEquals(candidate.TextTarget, textTarget));
			if (target is not null)
				UpdateSize(target);
		}
	}

	private static void UpdateSize(TargetMaterialization state)
	{
		var size = CalculateSize(state.TextTarget);
		state.Container.Size = size;
		foreach (var sprite in state.Sprites)
			sprite.Size = size;
	}

	private static Vector2 CalculateSize(TextBlock target) => new(
		(float)Math.Max(
			0,
			target.ActualWidth > 0
				? target.ActualWidth
				: double.IsFinite(target.Width) ? target.Width : 0),
		(float)Math.Max(
			0,
			target.ActualHeight > 0
				? target.ActualHeight
				: double.IsFinite(target.Height) ? target.Height : 0));

	private static IReadOnlyList<TextBlock> ResolveTextTargets(
		FrameworkElement owner,
		string generatedTextMarker)
	{
		if (owner is TextBlock text)
			return [text];
		if (owner is HtmlVerticalTextControl vertical)
		{
			vertical.RefreshPanel();
			return vertical.MaterializedGlyphs;
		}
		var marked = Enumerate(owner)
			.OfType<TextBlock>()
			.Where(candidate => string.Equals(
				candidate.Tag as string,
				generatedTextMarker,
				StringComparison.Ordinal))
			.ToArray();
		return marked.Length > 0
			? marked
			: Enumerate(owner).OfType<TextBlock>().ToArray();
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

	private static IReadOnlyList<ShadowSpec> Parse(string expression)
	{
		return SplitTopLevel(expression, ',').Select(ParseSingle).ToArray();
	}

	private static ShadowSpec ParseSingle(string expression)
	{
		var colorMatch = ColorRegex().Match(expression);
		if (!colorMatch.Success)
			throw new InvalidDataException(
				$"CSS text-shadow requires an explicit color: '{expression}'.");
		var brush = WinUiXamlElementObjectFactory
			.ParseBrush(colorMatch.Value) as SolidColorBrush
			?? throw new InvalidDataException(
				$"Invalid CSS text-shadow color '{colorMatch.Value}'.");
		var lengthText = expression.Remove(
			colorMatch.Index,
			colorMatch.Length);
		var lengths = lengthText.Split(
			' ',
			StringSplitOptions.RemoveEmptyEntries
				| StringSplitOptions.TrimEntries);
		if (lengths.Length is < 2 or > 3)
			throw new InvalidDataException(
				$"Invalid CSS text-shadow lengths '{expression}'.");
		var values = lengths.Select(ParsePixels).ToArray();
		var blur = values.ElementAtOrDefault(2);
		if (blur < 0)
			throw new InvalidDataException(
				$"CSS text-shadow blur cannot be negative: '{expression}'.");
		return new(
			values[0],
			values[1],
			blur,
			Color.FromArgb(
				255,
				brush.Color.R,
				brush.Color.G,
				brush.Color.B),
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
				$"Unsupported CSS text-shadow length '{value}'.");
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

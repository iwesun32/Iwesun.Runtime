using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace Iwesun.Runtime.Web.WinUI;

internal static partial class WinUiClipPathBehavior
{
	private sealed class Materialization(
		string expression,
		double scaleX,
		double scaleY)
	{
		internal string Expression { get; } = expression;
		internal double ScaleX { get; } = scaleX;
		internal double ScaleY { get; } = scaleY;
		internal CompositionClip? Clip { get; set; }
		internal CompositionGeometry? Geometry { get; set; }
		internal CompositionPath? Path { get; set; }
		internal CanvasGeometry? CanvasGeometry { get; set; }
		internal ClipShape? Shape { get; set; }
	}

	private abstract record ClipShape;
	private sealed record InsetShape(double Left, double Top, double Right, double Bottom)
		: ClipShape;
	private sealed record RoundedInsetShape(
		Vector2 Origin,
		Vector2 Size,
		Vector4 RadiusX,
		Vector4 RadiusY) : ClipShape;
	private sealed record EllipseShape(Vector2 Center, Vector2 Radius)
		: ClipShape;
	private sealed record PolygonShape(IReadOnlyList<Vector2> Points, bool EvenOdd)
		: ClipShape;

	private readonly record struct Coordinate(double Fraction, double Offset)
	{
		internal double Resolve(double extent, double scale) =>
			extent * Fraction + Offset * scale;
	}

	private static readonly ConditionalWeakTable<
		FrameworkElement,
		Materialization> Materializations = new();

	internal static void Apply(
		FrameworkElement target,
		string expression,
		double scaleX,
		double scaleY)
	{
		ArgumentNullException.ThrowIfNull(target);
		var normalized = expression.Trim().ToLowerInvariant();
		if (Materializations.TryGetValue(target, out var previous))
		{
			target.SizeChanged -= OnSizeChanged;
			var visual = ElementCompositionPreview.GetElementVisual(target);
			if (ReferenceEquals(visual.Clip, previous.Clip))
				visual.Clip = null;
			previous.CanvasGeometry?.Dispose();
		}
		Materializations.Remove(target);
		if (normalized == "none")
		{
			Materializations.Add(target, new("none", scaleX, scaleY));
			return;
		}
		var state = new Materialization(normalized, scaleX, scaleY);
		Materializations.Add(target, state);
		Update(target, state);
		target.SizeChanged += OnSizeChanged;
	}

	internal static bool TryRead(
		FrameworkElement target,
		out string value)
	{
		value = string.Empty;
		if (!Materializations.TryGetValue(target, out var state))
			return false;
		if (state.Clip is null)
		{
			value = "none";
			return state.Expression == "none";
		}
		var visual = ElementCompositionPreview.GetElementVisual(target);
		if (!ReferenceEquals(visual.Clip, state.Clip)
			|| state.Shape is null)
			return false;
		var expected = CreateShape(
			state.Expression,
			ReadSize(target),
			state.ScaleX,
			state.ScaleY);
		if (!ClipMatches(state, expected))
			return false;
		value = state.Expression;
		return true;
	}

	private static void OnSizeChanged(
		object sender,
		SizeChangedEventArgs args)
	{
		if (sender is FrameworkElement target
			&& Materializations.TryGetValue(target, out var state))
		{
			Update(target, state);
		}
	}

	private static void Update(
		FrameworkElement target,
		Materialization state)
	{
		if (state.Expression == "none")
			return;
		var visual = ElementCompositionPreview.GetElementVisual(target);
		var compositor = visual.Compositor;
		var shape = CreateShape(
			state.Expression,
			ReadSize(target),
			state.ScaleX,
			state.ScaleY);
		state.CanvasGeometry?.Dispose();
		state.CanvasGeometry = null;
		state.Geometry = null;
		state.Path = null;
		state.Shape = shape;
		state.Clip = shape switch
		{
			InsetShape inset => compositor.CreateInsetClip(
				(float)inset.Left,
				(float)inset.Top,
				(float)inset.Right,
				(float)inset.Bottom),
			RoundedInsetShape rounded => CreateRoundedInsetClip(
				compositor,
				rounded,
				state),
			EllipseShape ellipse => CreateEllipseClip(compositor, ellipse, state),
			PolygonShape polygon => CreatePolygonClip(compositor, polygon, state),
			_ => throw new InvalidOperationException("Unknown clip-path shape.")
		};
		visual.Clip = state.Clip;
	}

	private static Windows.Foundation.Size ReadSize(FrameworkElement target)
	{
		var width = target.ActualWidth > 0
			? target.ActualWidth
			: double.IsFinite(target.Width) ? target.Width : 0;
		var height = target.ActualHeight > 0
			? target.ActualHeight
			: double.IsFinite(target.Height) ? target.Height : 0;
		return new(Math.Max(0, width), Math.Max(0, height));
	}

	private static ClipShape CreateShape(
		string expression,
		Windows.Foundation.Size size,
		double scaleX,
		double scaleY)
	{
		var match = FunctionRegex().Match(expression);
		if (!match.Success)
			throw Unsupported(expression);
		return match.Groups["name"].Value switch
		{
			"inset" => CreateInset(match.Groups["body"].Value, size, scaleX, scaleY),
			"circle" => CreateCircle(match.Groups["body"].Value, size, scaleX, scaleY),
			"ellipse" => CreateEllipse(match.Groups["body"].Value, size, scaleX, scaleY),
			"polygon" => CreatePolygon(match.Groups["body"].Value, size, scaleX, scaleY),
			_ => throw Unsupported(expression)
		};
	}

	private static ClipShape CreateInset(
		string body,
		Windows.Foundation.Size size,
		double scaleX,
		double scaleY)
	{
		var parts = body.Split(" round ", 2, StringSplitOptions.TrimEntries);
		var values = SplitSpace(parts[0]).Select(ParseCoordinate).ToArray();
		if (values.Length is < 1 or > 4)
			throw Unsupported($"inset({body})");
		var top = values[0];
		var right = values.Length == 1 ? top : values[1];
		var bottom = values.Length < 3 ? top : values[2];
		var left = values.Length == 1 ? top : values.Length < 4 ? right : values[3];
		var topValue = top.Resolve(size.Height, scaleY);
		var rightValue = right.Resolve(size.Width, scaleX);
		var bottomValue = bottom.Resolve(size.Height, scaleY);
		var leftValue = left.Resolve(size.Width, scaleX);
		if (parts.Length == 1)
			return new InsetShape(leftValue, topValue, rightValue, bottomValue);
		var width = Math.Max(0, size.Width - leftValue - rightValue);
		var height = Math.Max(0, size.Height - topValue - bottomValue);
		var (radiusX, radiusY) = ParseCornerRadii(
			parts[1],
			width,
			height,
			scaleX,
			scaleY);
		return new RoundedInsetShape(
			new((float)leftValue, (float)topValue),
			new((float)width, (float)height),
			radiusX,
			radiusY);
	}

	private static EllipseShape CreateCircle(
		string body,
		Windows.Foundation.Size size,
		double scaleX,
		double scaleY)
	{
		var parts = body.Split(" at ", 2, StringSplitOptions.TrimEntries);
		var center = ParsePosition(parts.Length == 2 ? parts[1] : "center");
		var centerX = center.X.Resolve(size.Width, scaleX);
		var centerY = center.Y.Resolve(size.Height, scaleY);
		var radiusToken = parts[0].Length == 0 ? "closest-side" : parts[0];
		var (radiusX, radiusY) = ResolveCircleRadius(
			radiusToken,
			size,
			centerX,
			centerY,
			scaleX,
			scaleY);
		return new(
			new((float)centerX, (float)centerY),
			new((float)Math.Max(0, radiusX), (float)Math.Max(0, radiusY)));
	}

	private static EllipseShape CreateEllipse(
		string body,
		Windows.Foundation.Size size,
		double scaleX,
		double scaleY)
	{
		var parts = body.Split(" at ", 2, StringSplitOptions.TrimEntries);
		var radii = SplitSpace(parts[0]);
		if (radii.Length != 2)
			throw Unsupported($"ellipse({body})");
		var center = ParsePosition(parts.Length == 2 ? parts[1] : "center");
		return new(
			new(
				(float)center.X.Resolve(size.Width, scaleX),
				(float)center.Y.Resolve(size.Height, scaleY)),
			new(
				(float)Math.Max(0, ParseCoordinate(radii[0]).Resolve(size.Width, scaleX)),
				(float)Math.Max(0, ParseCoordinate(radii[1]).Resolve(size.Height, scaleY))));
	}

	private static PolygonShape CreatePolygon(
		string body,
		Windows.Foundation.Size size,
		double scaleX,
		double scaleY)
	{
		var parts = body.Split(',', StringSplitOptions.TrimEntries);
		var evenOdd = false;
		if (parts.Length > 0 && parts[0] is "evenodd" or "nonzero")
		{
			evenOdd = parts[0] == "evenodd";
			parts = parts[1..];
		}
		if (parts.Length < 3)
			throw Unsupported($"polygon({body})");
		var points = parts.Select(part =>
		{
			var pair = SplitSpace(part);
			if (pair.Length != 2)
				throw Unsupported($"polygon({body})");
			return new Vector2(
				(float)ParseCoordinate(pair[0]).Resolve(size.Width, scaleX),
				(float)ParseCoordinate(pair[1]).Resolve(size.Height, scaleY));
		}).ToArray();
		return new(points, evenOdd);
	}

	private static (Coordinate X, Coordinate Y) ParsePosition(string value)
	{
		var tokens = SplitSpace(value);
		if (tokens.Length == 4)
		{
			return (
				ParseEdgeOffset(tokens[0], tokens[1], horizontal: true),
				ParseEdgeOffset(tokens[2], tokens[3], horizontal: false));
		}
		if (tokens.Length == 1)
		{
			return tokens[0] switch
			{
				"top" or "bottom" =>
					(new(.5, 0), ParsePositionCoordinate(tokens[0], false)),
				_ => (ParsePositionCoordinate(tokens[0], true), new(.5, 0))
			};
		}
		if (tokens.Length != 2)
			throw Unsupported($"position '{value}'");
		if (tokens[0] is "top" or "bottom")
			(tokens[0], tokens[1]) = (tokens[1], tokens[0]);
		return (
			ParsePositionCoordinate(tokens[0], true),
			ParsePositionCoordinate(tokens[1], false));
	}

	private static Coordinate ParseEdgeOffset(
		string edge,
		string offset,
		bool horizontal)
	{
		var coordinate = ParseCoordinate(offset);
		return (horizontal, edge) switch
		{
			(true, "left") or (false, "top") => coordinate,
			(true, "right") or (false, "bottom") =>
				new(1 - coordinate.Fraction, -coordinate.Offset),
			_ => throw Unsupported($"edge position '{edge} {offset}'")
		};
	}

	private static Coordinate ParsePositionCoordinate(
		string token,
		bool horizontal) => token switch
	{
		"center" => new(.5, 0),
		"left" when horizontal => new(0, 0),
		"right" when horizontal => new(1, 0),
		"top" when !horizontal => new(0, 0),
		"bottom" when !horizontal => new(1, 0),
		_ => ParseCoordinate(token)
	};

	private static (double RadiusX, double RadiusY) ResolveCircleRadius(
		string token,
		Windows.Foundation.Size size,
		double centerX,
		double centerY,
		double scaleX,
		double scaleY)
	{
		var horizontal = new[] { centerX, size.Width - centerX };
		var vertical = new[] { centerY, size.Height - centerY };
		if (token is "closest-side" or "farthest-side")
		{
			var radius = token == "closest-side"
				? horizontal.Concat(vertical).Min()
				: horizontal.Concat(vertical).Max();
			return (radius, radius);
		}
		if (token is "closest-corner" or "farthest-corner")
		{
			var distances = from x in horizontal
				from y in vertical
				select Math.Sqrt(x * x + y * y);
			var radius = token == "closest-corner"
				? distances.Min()
				: distances.Max();
			return (radius, radius);
		}
		var coordinate = ParseCoordinate(token);
		if (coordinate.Fraction != 0)
		{
			var normalizedDiagonal = Math.Sqrt(
				(size.Width * size.Width + size.Height * size.Height) / 2);
			var radius = normalizedDiagonal * coordinate.Fraction;
			return (radius, radius);
		}
		return (
			coordinate.Offset * scaleX,
			coordinate.Offset * scaleY);
	}

	private static Coordinate ParseCoordinate(string value)
	{
		if (value == "0")
			return default;
		var percentage = value.EndsWith('%');
		var pixels = value.EndsWith("px", StringComparison.Ordinal);
		if (!percentage && !pixels)
			throw Unsupported($"coordinate '{value}'");
		var token = percentage ? value[..^1] : value[..^2];
		if (!double.TryParse(
			token,
			NumberStyles.Float,
			CultureInfo.InvariantCulture,
			out var number))
		{
			throw Unsupported($"coordinate '{value}'");
		}
		return percentage ? new(number / 100, 0) : new(0, number);
	}

	private static CompositionGeometricClip CreateEllipseClip(
		Compositor compositor,
		EllipseShape shape,
		Materialization state)
	{
		var geometry = compositor.CreateEllipseGeometry();
		geometry.Center = shape.Center;
		geometry.Radius = shape.Radius;
		state.Geometry = geometry;
		return compositor.CreateGeometricClip(geometry);
	}

	private static CompositionGeometricClip CreateRoundedInsetClip(
		Compositor compositor,
		RoundedInsetShape shape,
		Materialization state)
	{
		var left = shape.Origin.X;
		var top = shape.Origin.Y;
		var right = left + shape.Size.X;
		var bottom = top + shape.Size.Y;
		var device = CanvasDevice.GetSharedDevice();
		using var builder = new CanvasPathBuilder(device);
		builder.BeginFigure(new(left + shape.RadiusX.X, top));
		builder.AddLine(new(right - shape.RadiusX.Y, top));
		AddCorner(builder, new(right, top + shape.RadiusY.Y), shape.RadiusX.Y, shape.RadiusY.Y);
		builder.AddLine(new(right, bottom - shape.RadiusY.Z));
		AddCorner(builder, new(right - shape.RadiusX.Z, bottom), shape.RadiusX.Z, shape.RadiusY.Z);
		builder.AddLine(new(left + shape.RadiusX.W, bottom));
		AddCorner(builder, new(left, bottom - shape.RadiusY.W), shape.RadiusX.W, shape.RadiusY.W);
		builder.AddLine(new(left, top + shape.RadiusY.X));
		AddCorner(builder, new(left + shape.RadiusX.X, top), shape.RadiusX.X, shape.RadiusY.X);
		builder.EndFigure(CanvasFigureLoop.Closed);
		return CreatePathClip(compositor, CanvasGeometry.CreatePath(builder), state);
	}

	private static void AddCorner(
		CanvasPathBuilder builder,
		Vector2 end,
		float radiusX,
		float radiusY)
	{
		if (radiusX <= 0 || radiusY <= 0)
		{
			builder.AddLine(end);
			return;
		}
		builder.AddArc(
			end,
			radiusX,
			radiusY,
			0,
			CanvasSweepDirection.Clockwise,
			CanvasArcSize.Small);
	}

	private static CompositionGeometricClip CreatePolygonClip(
		Compositor compositor,
		PolygonShape shape,
		Materialization state)
	{
		var device = CanvasDevice.GetSharedDevice();
		using var builder = new CanvasPathBuilder(device);
		builder.SetFilledRegionDetermination(
			shape.EvenOdd
				? CanvasFilledRegionDetermination.Alternate
				: CanvasFilledRegionDetermination.Winding);
		builder.BeginFigure(shape.Points[0]);
		foreach (var point in shape.Points.Skip(1))
			builder.AddLine(point);
		builder.EndFigure(CanvasFigureLoop.Closed);
		return CreatePathClip(
			compositor,
			CanvasGeometry.CreatePath(builder),
			state);
	}

	private static CompositionGeometricClip CreatePathClip(
		Compositor compositor,
		CanvasGeometry canvas,
		Materialization state)
	{
		var path = new CompositionPath(canvas);
		var geometry = compositor.CreatePathGeometry(path);
		state.CanvasGeometry = canvas;
		state.Path = path;
		state.Geometry = geometry;
		return compositor.CreateGeometricClip(geometry);
	}

	private static (Vector4 RadiusX, Vector4 RadiusY) ParseCornerRadii(
		string expression,
		double width,
		double height,
		double scaleX,
		double scaleY)
	{
		var axes = expression.Split('/', 2, StringSplitOptions.TrimEntries);
		var horizontal = ExpandCorners(SplitSpace(axes[0]))
			.Select(value => ParseCoordinate(value).Resolve(width, scaleX))
			.Select(static value => Math.Max(0, value))
			.ToArray();
		var vertical = ExpandCorners(SplitSpace(
			axes.Length == 2 ? axes[1] : axes[0]))
			.Select(value => ParseCoordinate(value).Resolve(height, scaleY))
			.Select(static value => Math.Max(0, value))
			.ToArray();
		var factor = new[]
		{
			PairScale(width, horizontal[0] + horizontal[1]),
			PairScale(width, horizontal[3] + horizontal[2]),
			PairScale(height, vertical[0] + vertical[3]),
			PairScale(height, vertical[1] + vertical[2])
		}.Min();
		return (
			new(
				(float)(horizontal[0] * factor),
				(float)(horizontal[1] * factor),
				(float)(horizontal[2] * factor),
				(float)(horizontal[3] * factor)),
			new(
				(float)(vertical[0] * factor),
				(float)(vertical[1] * factor),
				(float)(vertical[2] * factor),
				(float)(vertical[3] * factor)));
	}

	private static string[] ExpandCorners(string[] values) => values.Length switch
	{
		1 => [values[0], values[0], values[0], values[0]],
		2 => [values[0], values[1], values[0], values[1]],
		3 => [values[0], values[1], values[2], values[1]],
		4 => values,
		_ => throw Unsupported("rounded inset corner radii")
	};

	private static double PairScale(double extent, double sum) =>
		sum > extent && sum > 0 ? extent / sum : 1;

	private static bool ClipMatches(
		Materialization state,
		ClipShape expected) => expected switch
	{
		InsetShape inset when state.Clip is InsetClip actual =>
			Near(actual.LeftInset, inset.Left)
			&& Near(actual.TopInset, inset.Top)
			&& Near(actual.RightInset, inset.Right)
			&& Near(actual.BottomInset, inset.Bottom),
		RoundedInsetShape rounded
			when state.Clip is CompositionGeometricClip actualClip
				&& state.Geometry is CompositionPathGeometry actualGeometry
				&& state.Path is not null
				&& state.CanvasGeometry is not null =>
			ReferenceEquals(actualClip.Geometry, actualGeometry)
			&& ReferenceEquals(actualGeometry.Path, state.Path)
			&& Equals(state.Shape, rounded),
		EllipseShape ellipse
			when state.Clip is CompositionGeometricClip actualClip
				&& state.Geometry is CompositionEllipseGeometry actualGeometry =>
			ReferenceEquals(actualClip.Geometry, actualGeometry)
			&& VectorEquals(actualGeometry.Center, ellipse.Center)
			&& VectorEquals(actualGeometry.Radius, ellipse.Radius),
		PolygonShape polygon
			when state.Clip is CompositionGeometricClip actualClip
				&& state.Geometry is CompositionPathGeometry actualGeometry
				&& state.Path is not null
				&& state.CanvasGeometry is not null
				&& state.Shape is PolygonShape actualPolygon =>
			ReferenceEquals(actualClip.Geometry, actualGeometry)
			&& ReferenceEquals(actualGeometry.Path, state.Path)
			&& actualPolygon.EvenOdd == polygon.EvenOdd
			&& actualPolygon.Points.Count == polygon.Points.Count
			&& actualPolygon.Points.Zip(polygon.Points, VectorEquals).All(static equal => equal),
		_ => false
	};

	private static bool VectorEquals(Vector2 left, Vector2 right) =>
		Near(left.X, right.X) && Near(left.Y, right.Y);

	private static string[] SplitSpace(string value) => value.Split(
		' ',
		StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

	private static InvalidDataException Unsupported(string expression) =>
		new($"Unsupported CSS clip-path {expression}.");

	private static bool Near(double left, double right) =>
		Math.Abs(left - right) <= .001;

	[GeneratedRegex(
		@"^(?<name>[a-z]+)\((?<body>[^()]*)\)$",
		RegexOptions.CultureInvariant)]
	private static partial Regex FunctionRegex();
}

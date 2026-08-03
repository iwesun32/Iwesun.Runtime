using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Windows.Foundation;
using Windows.UI;

namespace Iwesun.Runtime.Web.WinUI;

internal class HtmlEmbeddedContentHost : HtmlCursorContentControl
{
	public static readonly DependencyProperty SourceProperty =
		DependencyProperty.Register(
			nameof(Source),
			typeof(string),
			typeof(HtmlEmbeddedContentHost),
			new PropertyMetadata(null));

	public static readonly DependencyProperty MediaTypeProperty =
		DependencyProperty.Register(
			nameof(MediaType),
			typeof(string),
			typeof(HtmlEmbeddedContentHost),
			new PropertyMetadata(null));

	public string? Source
	{
		get => (string?)GetValue(SourceProperty);
		set => SetValue(SourceProperty, value);
	}

	public string? MediaType
	{
		get => (string?)GetValue(MediaTypeProperty);
		set => SetValue(MediaTypeProperty, value);
	}
}

internal sealed class HtmlEmbeddedDocumentHost : HtmlEmbeddedContentHost
{
	public static readonly DependencyProperty SourceDocumentProperty =
		DependencyProperty.Register(
			nameof(SourceDocument),
			typeof(string),
			typeof(HtmlEmbeddedDocumentHost),
			new PropertyMetadata(null));

	public static readonly DependencyProperty SandboxProperty =
		DependencyProperty.Register(
			nameof(Sandbox),
			typeof(string),
			typeof(HtmlEmbeddedDocumentHost),
			new PropertyMetadata(null));

	public static readonly DependencyProperty AllowProperty =
		DependencyProperty.Register(
			nameof(Allow),
			typeof(string),
			typeof(HtmlEmbeddedDocumentHost),
			new PropertyMetadata(null));

	public string? SourceDocument
	{
		get => (string?)GetValue(SourceDocumentProperty);
		set => SetValue(SourceDocumentProperty, value);
	}

	public string? Sandbox
	{
		get => (string?)GetValue(SandboxProperty);
		set => SetValue(SandboxProperty, value);
	}

	public string? Allow
	{
		get => (string?)GetValue(AllowProperty);
		set => SetValue(AllowProperty, value);
	}
}

internal sealed class HtmlObjectContentHost : HtmlCursorGrid
{
	private Image? _primaryImage;

	public static readonly DependencyProperty SourceProperty =
		DependencyProperty.Register(
			nameof(Source),
			typeof(string),
			typeof(HtmlObjectContentHost),
			new PropertyMetadata(null, OnDefinitionChanged));

	public static readonly DependencyProperty MediaTypeProperty =
		DependencyProperty.Register(
			nameof(MediaType),
			typeof(string),
			typeof(HtmlObjectContentHost),
			new PropertyMetadata(null, OnDefinitionChanged));

	public string? Source
	{
		get => (string?)GetValue(SourceProperty);
		set => SetValue(SourceProperty, value);
	}

	public string? MediaType
	{
		get => (string?)GetValue(MediaTypeProperty);
		set => SetValue(MediaTypeProperty, value);
	}

	private static void OnDefinitionChanged(
		DependencyObject owner,
		DependencyPropertyChangedEventArgs args) =>
		((HtmlObjectContentHost)owner).ApplyPrimaryContent();

	private void ApplyPrimaryContent()
	{
		if (_primaryImage is not null)
		{
			Children.Remove(_primaryImage);
			_primaryImage = null;
		}
		if (!IsImageMediaType(MediaType)
			|| !Uri.TryCreate(Source, UriKind.Absolute, out var uri))
		{
			SetFallbackVisibility(Visibility.Visible);
			return;
		}
		_primaryImage = new Image
		{
			Source = new BitmapImage(uri),
			Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform
		};
		Children.Insert(0, _primaryImage);
		SetFallbackVisibility(Visibility.Collapsed);
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		SetFallbackVisibility(
			_primaryImage is null
				? Visibility.Visible
				: Visibility.Collapsed);
		return base.ArrangeOverride(finalSize);
	}

	private void SetFallbackVisibility(Visibility visibility)
	{
		foreach (var child in Children)
		{
			if (!ReferenceEquals(child, _primaryImage))
				child.Visibility = visibility;
		}
	}

	private static bool IsImageMediaType(string? mediaType) =>
		mediaType?.StartsWith(
			"image/",
			StringComparison.OrdinalIgnoreCase) == true;
}

internal sealed class HtmlCanvasSurface : HtmlCursorCanvas
{
	private sealed record ReplayState(
		string FillStyle,
		string StrokeStyle,
		double LineWidth,
		double GlobalAlpha,
		DoubleCollection LineDash,
		Matrix3x2 Transform,
		string Font,
		IReadOnlyList<RectangleGeometry> ClipGeometries,
		double FilterOpacity,
		string ShadowColor,
		double ShadowBlur,
		double ShadowOffsetX,
		double ShadowOffsetY,
		string GlobalCompositeOperation,
		PenLineCap LineCap,
		PenLineJoin LineJoin,
		double MiterLimit,
		double LineDashOffset,
		string TextAlign,
		string TextBaseline,
		string Direction);

	private sealed record PathCommand(string Name, Point[] Points);

	private sealed class PaintResource
	{
		public required string Kind { get; init; }

		public required double[] Values { get; init; }

		public string? Source { get; init; }

		public List<(double Offset, string Color)> Stops { get; } = [];
	}

	private readonly Dictionary<string, PaintResource> _paintResources =
		new(StringComparer.Ordinal);

	public static readonly DependencyProperty DrawingConnectionKeyProperty =
		DependencyProperty.Register(
			nameof(DrawingConnectionKey),
			typeof(string),
			typeof(HtmlCanvasSurface),
			new PropertyMetadata(null));

	public static readonly DependencyProperty CommandStreamProperty =
		DependencyProperty.Register(
			nameof(CommandStream),
			typeof(string),
			typeof(HtmlCanvasSurface),
			new PropertyMetadata(
				null,
				static (owner, args) =>
					((HtmlCanvasSurface)owner).Replay(
						args.NewValue as string)));

	public string? DrawingConnectionKey
	{
		get => (string?)GetValue(DrawingConnectionKeyProperty);
		set => SetValue(DrawingConnectionKeyProperty, value);
	}

	public string? CommandStream
	{
		get => (string?)GetValue(CommandStreamProperty);
		set => SetValue(CommandStreamProperty, value);
	}

	public string ReplayEvidenceJson { get; private set; } = string.Empty;

	private void Replay(string? stream)
	{
		Children.Clear();
		_paintResources.Clear();
		ReplayEvidenceJson = string.Empty;
		if (string.IsNullOrWhiteSpace(stream))
			return;
		using var document = JsonDocument.Parse(stream);
		if (document.RootElement.ValueKind != JsonValueKind.Array)
			throw new InvalidDataException(
				"Canvas command stream must be a JSON array.");
		var state = DefaultState();
		var states = new Stack<ReplayState>();
		var path = new List<PathCommand>();
		var executed = new Dictionary<string, int>(StringComparer.Ordinal);
		var partial = new Dictionary<string, int>(StringComparer.Ordinal);
		var unsupported = new Dictionary<string, int>(StringComparer.Ordinal);
		foreach (var command in document.RootElement.EnumerateArray())
		{
			if (!command.TryGetProperty("name", out var nameNode)
				|| !command.TryGetProperty("args", out var args))
			{
				continue;
			}
			var name = nameNode.GetString() ?? string.Empty;
			var wasExecuted = true;
			var wasPartial = false;
			switch (name)
			{
				case "createLinearGradient":
				RegisterPaintResource(name, args, state);
					break;
				case "createRadialGradient":
					RegisterPaintResource(name, args, state);
					wasExecuted = false;
					wasPartial = true;
					break;
				case "createConicGradient":
					RegisterPaintResource(name, args, state);
					wasExecuted = false;
					wasPartial = true;
					break;
				case "createPattern":
					RegisterPaintResource(name, args, state);
					wasExecuted = false;
					wasPartial = true;
					break;
				case "gradient:addColorStop":
					AddGradientStop(args);
					break;
				case "set:fillStyle":
					state = state with { FillStyle = String(args, 0) };
					break;
				case "set:strokeStyle":
					state = state with { StrokeStyle = String(args, 0) };
					break;
				case "set:lineWidth":
					state = state with { LineWidth = Number(args, 0, 1) };
					break;
				case "set:lineCap":
					state = state with
					{
						LineCap = String(args, 0).ToLowerInvariant() switch
						{
							"round" => PenLineCap.Round,
							"square" => PenLineCap.Square,
							_ => PenLineCap.Flat
						}
					};
					break;
				case "set:lineJoin":
					state = state with
					{
						LineJoin = String(args, 0).ToLowerInvariant() switch
						{
							"round" => PenLineJoin.Round,
							"bevel" => PenLineJoin.Bevel,
							_ => PenLineJoin.Miter
						}
					};
					break;
				case "set:miterLimit":
					state = state with
					{
						MiterLimit = Math.Max(0, Number(args, 0, 10))
					};
					break;
				case "set:lineDashOffset":
					state = state with
					{
						LineDashOffset = Number(args, 0)
					};
					break;
				case "set:globalAlpha":
					state = state with
					{
						GlobalAlpha = Math.Clamp(Number(args, 0, 1), 0, 1)
					};
					break;
				case "set:filter":
					var filter = ParseFilter(String(args, 0));
					state = state with { FilterOpacity = filter.Opacity };
					if (!filter.IsComplete)
					{
						wasExecuted = false;
						wasPartial = true;
					}
					break;
				case "set:shadowColor":
					state = state with { ShadowColor = String(args, 0) };
					wasExecuted = false;
					wasPartial = true;
					break;
				case "set:shadowBlur":
					state = state with
					{
						ShadowBlur = Math.Max(0, Number(args, 0))
					};
					wasExecuted = false;
					wasPartial = true;
					break;
				case "set:shadowOffsetX":
					state = state with { ShadowOffsetX = Number(args, 0) };
					wasExecuted = false;
					wasPartial = true;
					break;
				case "set:shadowOffsetY":
					state = state with { ShadowOffsetY = Number(args, 0) };
					wasExecuted = false;
					wasPartial = true;
					break;
				case "set:font":
					state = state with { Font = String(args, 0) };
					break;
				case "set:textAlign":
					state = state with { TextAlign = String(args, 0) };
					break;
				case "set:textBaseline":
					state = state with { TextBaseline = String(args, 0) };
					break;
				case "set:direction":
					state = state with { Direction = String(args, 0) };
					break;
				case "setLineDash":
					state = state with { LineDash = ReadDash(args) };
					break;
				case "save":
					states.Push(Clone(state));
					break;
				case "restore":
					if (states.Count > 0)
						state = states.Pop();
					break;
				case "translate":
					state = state with
					{
						Transform = state.Transform
							* Matrix3x2.CreateTranslation(
								(float)Number(args, 0),
								(float)Number(args, 1))
					};
					break;
				case "scale":
					state = state with
					{
						Transform = state.Transform
							* Matrix3x2.CreateScale(
								(float)Number(args, 0, 1),
								(float)Number(args, 1, 1))
					};
					break;
				case "rotate":
					state = state with
					{
						Transform = state.Transform
							* Matrix3x2.CreateRotation((float)Number(args, 0))
					};
					break;
				case "transform":
					state = state with
					{
						Transform = state.Transform * ReadMatrix(args)
					};
					break;
				case "setTransform":
					state = state with { Transform = ReadMatrix(args) };
					break;
				case "resetTransform":
					state = state with { Transform = Matrix3x2.Identity };
					break;
				case "fillRect":
					AddRectangle(
						args,
						state,
						CreateBrush(state.FillStyle, EffectiveAlpha(state)),
						null);
					break;
				case "strokeRect":
					AddRectangle(
						args,
						state,
						null,
						CreateBrush(state.StrokeStyle, EffectiveAlpha(state)));
					break;
				case "beginPath":
					path.Clear();
					break;
				case "moveTo":
					path.Add(new(
						"move",
						[Transform(state, Number(args, 0), Number(args, 1))]));
					break;
				case "lineTo":
					path.Add(new(
						"line",
						[Transform(state, Number(args, 0), Number(args, 1))]));
					break;
				case "bezierCurveTo":
					path.Add(new(
						"bezier",
						[
							Transform(state, Number(args, 0), Number(args, 1)),
							Transform(state, Number(args, 2), Number(args, 3)),
							Transform(state, Number(args, 4), Number(args, 5))
						]));
					break;
				case "quadraticCurveTo":
					path.Add(new(
						"quadratic",
						[
							Transform(state, Number(args, 0), Number(args, 1)),
							Transform(state, Number(args, 2), Number(args, 3))
						]));
					break;
				case "arc":
					AddArc(path, state, args, ellipse: false);
					break;
				case "ellipse":
					AddArc(path, state, args, ellipse: true);
					break;
				case "rect":
					AddRectanglePath(path, state, args);
					break;
				case "roundRect":
					wasExecuted = AddRoundedRectanglePath(
						path,
						state,
						args);
					break;
				case "closePath":
					path.Add(new("close", []));
					break;
				case "fill":
					AddPath(
						path,
						CreateBrush(state.FillStyle, EffectiveAlpha(state)),
						null,
						state);
					break;
				case "stroke":
					AddPath(
						path,
						null,
						CreateBrush(state.StrokeStyle, EffectiveAlpha(state)),
						state);
					break;
				case "fillText":
					AddText(
						args,
						state,
						CreateBrush(state.FillStyle, EffectiveAlpha(state)));
					break;
				case "strokeText":
					AddText(
						args,
						state,
						CreateBrush(state.StrokeStyle, EffectiveAlpha(state)));
					break;
				case "drawImage":
					AddImage(args, state);
					if (args.GetArrayLength() >= 9)
					{
						wasExecuted = false;
						wasPartial = true;
					}
					break;
				case "clearRect":
					var clearsWholeSurface = Number(args, 0) == 0
						&& Number(args, 1) == 0
						&& Number(args, 2) >= Width
						&& Number(args, 3) >= Height
						&& state.ClipGeometries.Count == 0;
					if (clearsWholeSurface)
					{
						Children.Clear();
					}
					else
					{
						wasExecuted = false;
					}
					break;
				case "set:globalCompositeOperation":
					var operation = String(args, 0).ToLowerInvariant();
					wasExecuted = operation is
						"source-over" or "destination-over";
					if (wasExecuted || operation == "copy")
						state = state with
						{
							GlobalCompositeOperation = operation
						};
					if (operation == "copy")
						wasPartial = true;
					break;
				case "clip":
					var clip = CreateGeometry(path);
					if (args.GetArrayLength() > 0
						&& String(args, 0).Equals(
							"evenodd",
							StringComparison.OrdinalIgnoreCase))
					{
						clip.FillRule = FillRule.EvenOdd;
					}
					if (TryCreateRectangleClip(
						clip,
						out var rectangleClip))
					{
						state = state with
						{
							ClipGeometries =
								[.. state.ClipGeometries, rectangleClip]
						};
					}
					else
					{
						wasExecuted = false;
					}
					break;
				case "putImageData":
					var hadCanvasContent = Children.Count > 0;
					wasExecuted = TryAddImageData(args);
					if (hadCanvasContent && wasExecuted)
					{
						wasExecuted = false;
						wasPartial = true;
					}
					break;
				case "pattern:setTransform":
					wasExecuted = false;
					break;
				default:
					wasExecuted = false;
					break;
			}
			Increment(
				wasExecuted
					? executed
					: wasPartial
						? partial
						: unsupported,
				name);
		}
		ReplayEvidenceJson = JsonSerializer.Serialize(new
		{
			schema = "iwesun.xaml.canvas-replay/1",
			inputCommandCount = executed.Values.Sum()
				+ partial.Values.Sum()
				+ unsupported.Values.Sum(),
			executedCommandCount = executed.Values.Sum(),
			executed,
			partial,
			unsupported
		});
	}

	private static void Increment(
		IDictionary<string, int> counts,
		string name) =>
		counts[name] = counts.TryGetValue(name, out var count)
			? count + 1
			: 1;

	private void AddRectangle(
		JsonElement args,
		ReplayState state,
		Brush? fill,
		Brush? stroke)
	{
		var points = RectanglePoints(
			state,
			Number(args, 0),
			Number(args, 1),
			Number(args, 2),
			Number(args, 3));
		var polygon = new Polygon
		{
			Fill = fill,
			Stroke = stroke,
			StrokeThickness = state.LineWidth,
			StrokeDashArray = CloneDash(state.LineDash),
			StrokeDashOffset = state.LineDashOffset,
			StrokeStartLineCap = state.LineCap,
			StrokeEndLineCap = state.LineCap,
			StrokeLineJoin = state.LineJoin,
			StrokeMiterLimit = state.MiterLimit
		};
		foreach (var point in points)
			polygon.Points.Add(point);
		AddRendered(polygon, state);
	}

	private void AddPath(
		IReadOnlyList<PathCommand> commands,
		Brush? fill,
		Brush? stroke,
		ReplayState state)
	{
		var geometry = CreateGeometry(commands);
		if (geometry.Figures.Count == 0)
			return;
		AddRendered(new Microsoft.UI.Xaml.Shapes.Path
		{
			Data = geometry,
			Fill = fill,
			Stroke = stroke,
			StrokeThickness = state.LineWidth,
			StrokeDashArray = CloneDash(state.LineDash),
			StrokeDashOffset = state.LineDashOffset,
			StrokeStartLineCap = state.LineCap,
			StrokeEndLineCap = state.LineCap,
			StrokeLineJoin = state.LineJoin,
			StrokeMiterLimit = state.MiterLimit
		}, state);
	}

	private static PathGeometry CreateGeometry(
		IReadOnlyList<PathCommand> commands)
	{
		var geometry = new PathGeometry();
		PathFigure? figure = null;
		foreach (var command in commands)
		{
			switch (command.Name)
			{
				case "move":
					figure = new PathFigure { StartPoint = command.Points[0] };
					geometry.Figures.Add(figure);
					break;
				case "line":
					figure ??= StartFigure(geometry, command.Points[0]);
					figure.Segments.Add(
						new LineSegment { Point = command.Points[0] });
					break;
				case "bezier":
					figure ??= StartFigure(geometry, command.Points[2]);
					figure.Segments.Add(new BezierSegment
					{
						Point1 = command.Points[0],
						Point2 = command.Points[1],
						Point3 = command.Points[2]
					});
					break;
				case "quadratic":
					figure ??= StartFigure(geometry, command.Points[1]);
					figure.Segments.Add(new QuadraticBezierSegment
					{
						Point1 = command.Points[0],
						Point2 = command.Points[1]
					});
					break;
				case "close":
					if (figure is not null)
						figure.IsClosed = true;
					break;
			}
		}
		return geometry;
	}

	private void AddText(
		JsonElement args,
		ReplayState state,
		Brush foreground)
	{
		var point = Transform(
			state,
			Number(args, 1),
			Number(args, 2));
		var fontSize = ParseFontSize(state.Font);
		var text = new TextBlock
		{
			Text = String(args, 0),
			Foreground = foreground,
			FontSize = fontSize,
			FontFamily = new FontFamily(ParseFontFamily(state.Font)),
			FlowDirection = state.Direction.Equals(
				"rtl",
				StringComparison.OrdinalIgnoreCase)
					? FlowDirection.RightToLeft
					: FlowDirection.LeftToRight
		};
		text.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
		var left = ResolveTextLeft(
			point.X,
			text.DesiredSize.Width,
			state.TextAlign,
			text.FlowDirection);
		var top = ResolveTextTop(
			point.Y,
			text.DesiredSize.Height,
			fontSize,
			state.TextBaseline);
		SetLeft(text, left);
		SetTop(text, top);
		AddRendered(text, state);
	}

	private void AddImage(JsonElement args, ReplayState state)
	{
		if (args.ValueKind != JsonValueKind.Array
			|| args.GetArrayLength() < 3
			|| args[0].ValueKind != JsonValueKind.Object
			|| !args[0].TryGetProperty("source", out var sourceNode)
			|| !Uri.TryCreate(
				sourceNode.GetString(),
				UriKind.Absolute,
				out var uri))
		{
			return;
		}
		var destinationIndex = args.GetArrayLength() >= 9 ? 5 : 1;
		var x = Number(args, destinationIndex);
		var y = Number(args, destinationIndex + 1);
		var width = args.GetArrayLength() is 5 or >= 9
			? Math.Max(0, Number(args, destinationIndex + 2))
			: double.NaN;
		var height = args.GetArrayLength() is 5 or >= 9
			? Math.Max(0, Number(args, destinationIndex + 3))
			: double.NaN;
		var point = Transform(state, x, y);
		var image = new Image
		{
			Source = new BitmapImage(uri),
			Opacity = EffectiveAlpha(state),
			Stretch = Stretch.Fill
		};
		if (!double.IsNaN(width))
			image.Width = width * ScaleX(state.Transform);
		if (!double.IsNaN(height))
			image.Height = height * ScaleY(state.Transform);
		SetLeft(image, point.X);
		SetTop(image, point.Y);
		AddRendered(image, state);
	}

	private bool TryAddImageData(
		JsonElement args)
	{
		if (args.ValueKind != JsonValueKind.Array
			|| args.GetArrayLength() < 3
			|| args[0].ValueKind != JsonValueKind.Object
			|| !args[0].TryGetProperty("imageData", out var marker)
			|| marker.ValueKind != JsonValueKind.True
			|| args[0].GetProperty("truncated").GetBoolean())
		{
			return false;
		}
		var width = args[0].GetProperty("width").GetInt32();
		var height = args[0].GetProperty("height").GetInt32();
		if (width <= 0 || height <= 0)
			return false;
		var rgba = Convert.FromBase64String(
			args[0].GetProperty("rgbaBase64").GetString()
				?? string.Empty);
		if (rgba.Length != checked(width * height * 4))
			return false;
		var dirtyX = 0;
		var dirtyY = 0;
		var dirtyWidth = width;
		var dirtyHeight = height;
		if (args.GetArrayLength() >= 7)
		{
			dirtyX = (int)Math.Truncate(Number(args, 3));
			dirtyY = (int)Math.Truncate(Number(args, 4));
			dirtyWidth = (int)Math.Truncate(Number(args, 5));
			dirtyHeight = (int)Math.Truncate(Number(args, 6));
			if (dirtyWidth < 0)
			{
				dirtyX += dirtyWidth;
				dirtyWidth = -dirtyWidth;
			}
			if (dirtyHeight < 0)
			{
				dirtyY += dirtyHeight;
				dirtyHeight = -dirtyHeight;
			}
		}
		var sourceLeft = Math.Clamp(dirtyX, 0, width);
		var sourceTop = Math.Clamp(dirtyY, 0, height);
		var sourceRight = Math.Clamp(dirtyX + dirtyWidth, 0, width);
		var sourceBottom = Math.Clamp(dirtyY + dirtyHeight, 0, height);
		var outputWidth = Math.Max(0, sourceRight - sourceLeft);
		var outputHeight = Math.Max(0, sourceBottom - sourceTop);
		if (outputWidth == 0 || outputHeight == 0)
			return true;
		var bgra = new byte[checked(outputWidth * outputHeight * 4)];
		for (var y = 0; y < outputHeight; y++)
		{
			for (var x = 0; x < outputWidth; x++)
			{
				var sourceIndex = ((sourceTop + y) * width
					+ sourceLeft + x) * 4;
				var targetIndex = (y * outputWidth + x) * 4;
				var alpha = rgba[sourceIndex + 3];
				bgra[targetIndex] = Premultiply(
					rgba[sourceIndex + 2],
					alpha);
				bgra[targetIndex + 1] = Premultiply(
					rgba[sourceIndex + 1],
					alpha);
				bgra[targetIndex + 2] = Premultiply(
					rgba[sourceIndex],
					alpha);
				bgra[targetIndex + 3] = alpha;
			}
		}
		var bitmap = new WriteableBitmap(outputWidth, outputHeight);
		using (var stream = bitmap.PixelBuffer.AsStream())
		{
			stream.Write(bgra, 0, bgra.Length);
		}
		bitmap.Invalidate();
		var xPosition = Number(args, 1) + sourceLeft;
		var yPosition = Number(args, 2) + sourceTop;
		var image = new Image
		{
			Source = bitmap,
			Width = outputWidth,
			Height = outputHeight,
			Opacity = 1,
			Stretch = Stretch.Fill
		};
		SetLeft(image, xPosition);
		SetTop(image, yPosition);
		Children.Add(image);
		return true;
	}

	private static byte Premultiply(byte channel, byte alpha) =>
		(byte)((channel * alpha + 127) / 255);

	private void AddRendered(
		UIElement element,
		ReplayState state)
	{
		if (state.ShadowBlur > 0
			&& ParseColor(state.ShadowColor).A > 0)
		{
			element.Shadow = new ThemeShadow();
		}
		if (state.ClipGeometries.Count == 0)
		{
			AddComposited(element, state);
			return;
		}
		UIElement current = element;
		foreach (var geometry in state.ClipGeometries)
		{
			var layer = new Canvas
			{
				Width = double.IsFinite(Width)
					? Width
					: Math.Max(0, ActualWidth),
				Height = double.IsFinite(Height)
					? Height
					: Math.Max(0, ActualHeight),
				Clip = new RectangleGeometry
				{
					Rect = geometry.Rect
				}
			};
			layer.Children.Add(current);
			current = layer;
		}
		AddComposited(current, state);
	}

	private void AddComposited(
		UIElement element,
		ReplayState state)
	{
		switch (state.GlobalCompositeOperation)
		{
			case "copy":
				Children.Clear();
				Children.Add(element);
				break;
			case "destination-over":
				Children.Insert(0, element);
				break;
			default:
				Children.Add(element);
				break;
		}
	}

	private static bool TryCreateRectangleClip(
		PathGeometry source,
		out RectangleGeometry result)
	{
		result = new RectangleGeometry();
		if (source.Figures.Count != 1)
			return false;
		var figure = source.Figures[0];
		if (!figure.IsClosed
			|| figure.Segments.Count != 3
			|| figure.Segments.Any(static segment =>
				segment is not LineSegment))
		{
			return false;
		}
		var points = new List<Point> { figure.StartPoint };
		points.AddRange(
			figure.Segments.Cast<LineSegment>()
				.Select(static segment => segment.Point));
		var xs = points.Select(static point => point.X).Distinct().ToArray();
		var ys = points.Select(static point => point.Y).Distinct().ToArray();
		if (xs.Length != 2 || ys.Length != 2)
			return false;
		result.Rect = new(
			xs.Min(),
			ys.Min(),
			xs.Max() - xs.Min(),
			ys.Max() - ys.Min());
		return true;
	}

	private static PathFigure StartFigure(
		PathGeometry geometry,
		Point point)
	{
		var figure = new PathFigure { StartPoint = point };
		geometry.Figures.Add(figure);
		return figure;
	}

	private static ReplayState DefaultState() =>
		new(
			"#000000",
			"#000000",
			1,
			1,
			[],
			Matrix3x2.Identity,
			"10px sans-serif",
			[],
			1,
			"rgba(0,0,0,0)",
			0,
			0,
			0,
			"source-over",
			PenLineCap.Flat,
			PenLineJoin.Miter,
			10,
			0,
			"start",
			"alphabetic",
			"inherit");

	private static ReplayState Clone(ReplayState state) =>
		state with { LineDash = CloneDash(state.LineDash) };

	private static DoubleCollection CloneDash(
		IEnumerable<double> values)
	{
		var result = new DoubleCollection();
		foreach (var value in values)
			result.Add(value);
		return result;
	}

	private static DoubleCollection ReadDash(JsonElement args)
	{
		var result = new DoubleCollection();
		if (args.ValueKind != JsonValueKind.Array
			|| args.GetArrayLength() == 0
			|| args[0].ValueKind != JsonValueKind.Array)
		{
			return result;
		}
		foreach (var item in args[0].EnumerateArray())
		{
			if (item.TryGetDouble(out var value))
				result.Add(Math.Max(0, value));
		}
		return result;
	}

	private static Matrix3x2 ReadMatrix(JsonElement args) =>
		new(
			(float)Number(args, 0, 1),
			(float)Number(args, 1),
			(float)Number(args, 2),
			(float)Number(args, 3, 1),
			(float)Number(args, 4),
			(float)Number(args, 5));

	private static Point Transform(
		ReplayState state,
		double x,
		double y)
	{
		var value = Vector2.Transform(
			new((float)x, (float)y),
			state.Transform);
		return new(value.X, value.Y);
	}

	private static Point[] RectanglePoints(
		ReplayState state,
		double x,
		double y,
		double width,
		double height) =>
		[
			Transform(state, x, y),
			Transform(state, x + width, y),
			Transform(state, x + width, y + height),
			Transform(state, x, y + height)
		];

	private static void AddRectanglePath(
		ICollection<PathCommand> path,
		ReplayState state,
		JsonElement args)
	{
		var points = RectanglePoints(
			state,
			Number(args, 0),
			Number(args, 1),
			Number(args, 2),
			Number(args, 3));
		path.Add(new("move", [points[0]]));
		path.Add(new("line", [points[1]]));
		path.Add(new("line", [points[2]]));
		path.Add(new("line", [points[3]]));
		path.Add(new("close", []));
	}

	private static bool AddRoundedRectanglePath(
		ICollection<PathCommand> path,
		ReplayState state,
		JsonElement args)
	{
		if (!TryReadCornerRadii(args, out var radii))
			return false;
		var x = Number(args, 0);
		var y = Number(args, 1);
		var width = Number(args, 2);
		var height = Number(args, 3);
		if (width < 0)
		{
			x += width;
			width = -width;
			(radii[0], radii[1], radii[2], radii[3]) =
				(radii[1], radii[0], radii[3], radii[2]);
		}
		if (height < 0)
		{
			y += height;
			height = -height;
			(radii[0], radii[1], radii[2], radii[3]) =
				(radii[3], radii[2], radii[1], radii[0]);
		}
		var scale = Math.Min(
			1,
			new[]
			{
				SafeRatio(width, radii[0] + radii[1]),
				SafeRatio(width, radii[3] + radii[2]),
				SafeRatio(height, radii[0] + radii[3]),
				SafeRatio(height, radii[1] + radii[2])
			}.Min());
		for (var index = 0; index < radii.Length; index++)
			radii[index] *= scale;
		var topLeft = radii[0];
		var topRight = radii[1];
		var bottomRight = radii[2];
		var bottomLeft = radii[3];
		path.Add(new("move", [Transform(state, x + topLeft, y)]));
		path.Add(new("line", [Transform(state, x + width - topRight, y)]));
		path.Add(new("quadratic",
			[
				Transform(state, x + width, y),
				Transform(state, x + width, y + topRight)
			]));
		path.Add(new("line",
			[Transform(state, x + width, y + height - bottomRight)]));
		path.Add(new("quadratic",
			[
				Transform(state, x + width, y + height),
				Transform(state, x + width - bottomRight, y + height)
			]));
		path.Add(new("line",
			[Transform(state, x + bottomLeft, y + height)]));
		path.Add(new("quadratic",
			[
				Transform(state, x, y + height),
				Transform(state, x, y + height - bottomLeft)
			]));
		path.Add(new("line", [Transform(state, x, y + topLeft)]));
		path.Add(new("quadratic",
			[
				Transform(state, x, y),
				Transform(state, x + topLeft, y)
			]));
		path.Add(new("close", []));
		return true;
	}

	private static bool TryReadCornerRadii(
		JsonElement args,
		out double[] radii)
	{
		radii = [0, 0, 0, 0];
		if (args.GetArrayLength() < 5)
			return true;
		var source = args[4].ValueKind == JsonValueKind.Array
			? args[4].EnumerateArray()
				.Select(static item =>
					item.TryGetDouble(out var value)
						? Math.Max(0, value)
						: double.NaN)
				.ToArray()
			: args[4].TryGetDouble(out var scalar)
				? [Math.Max(0, scalar)]
				: [];
		if (source.Length is < 1 or > 4
			|| source.Any(static value => !double.IsFinite(value)))
			return false;
		radii = source.Length switch
		{
			1 => [source[0], source[0], source[0], source[0]],
			2 => [source[0], source[1], source[0], source[1]],
			3 => [source[0], source[1], source[2], source[1]],
			_ => [source[0], source[1], source[2], source[3]]
		};
		return true;
	}

	private static double SafeRatio(double available, double requested) =>
		requested <= 0 ? 1 : available / requested;

	private static void AddArc(
		ICollection<PathCommand> path,
		ReplayState state,
		JsonElement args,
		bool ellipse)
	{
		var centerX = Number(args, 0);
		var centerY = Number(args, 1);
		var radiusX = Math.Abs(Number(args, 2));
		var radiusY = ellipse
			? Math.Abs(Number(args, 3))
			: radiusX;
		var rotation = ellipse ? Number(args, 4) : 0;
		var start = Number(args, ellipse ? 5 : 3);
		var end = Number(args, ellipse ? 6 : 4);
		var anticlockwise = Boolean(args, ellipse ? 7 : 5);
		var sweep = NormalizeSweep(end - start, anticlockwise);
		var segmentCount = Math.Max(
			4,
			(int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 16)));
		for (var index = 0; index <= segmentCount; index++)
		{
			var angle = start + sweep * index / segmentCount;
			var localX = radiusX * Math.Cos(angle);
			var localY = radiusY * Math.Sin(angle);
			var rotatedX = localX * Math.Cos(rotation)
				- localY * Math.Sin(rotation);
			var rotatedY = localX * Math.Sin(rotation)
				+ localY * Math.Cos(rotation);
			var point = Transform(
				state,
				centerX + rotatedX,
				centerY + rotatedY);
			path.Add(new(index == 0 ? "line" : "line", [point]));
		}
	}

	private static double NormalizeSweep(
		double sweep,
		bool anticlockwise)
	{
		var full = Math.PI * 2;
		if (!anticlockwise)
		{
			while (sweep < 0)
				sweep += full;
			return Math.Min(sweep, full);
		}
		while (sweep > 0)
			sweep -= full;
		return Math.Max(sweep, -full);
	}

	private static bool Boolean(
		JsonElement args,
		int index) =>
		args.ValueKind == JsonValueKind.Array
		&& args.GetArrayLength() > index
		&& args[index].ValueKind is JsonValueKind.True;

	private static double ScaleX(Matrix3x2 matrix) =>
		Math.Sqrt(matrix.M11 * matrix.M11 + matrix.M12 * matrix.M12);

	private static double ScaleY(Matrix3x2 matrix) =>
		Math.Sqrt(matrix.M21 * matrix.M21 + matrix.M22 * matrix.M22);

	private static double ParseFontSize(string font)
	{
		var px = font.IndexOf("px", StringComparison.OrdinalIgnoreCase);
		if (px <= 0)
			return 10;
		var start = px - 1;
		while (start >= 0
			&& (char.IsDigit(font[start]) || font[start] == '.'))
		{
			start--;
		}
		return double.TryParse(
			font[(start + 1)..px],
			NumberStyles.Float,
			CultureInfo.InvariantCulture,
			out var value)
				? value
				: 10;
	}

	private static string ParseFontFamily(string font)
	{
		var px = font.IndexOf("px", StringComparison.OrdinalIgnoreCase);
		if (px < 0 || px + 2 >= font.Length)
			return "Segoe UI";
		var family = font[(px + 2)..].Trim().Trim('"', '\'');
		return family.Length == 0
			? "Segoe UI"
			: family.Split(',')[0].Trim().Trim('"', '\'');
	}

	private static double ResolveTextLeft(
		double anchor,
		double width,
		string alignment,
		FlowDirection direction)
	{
		var normalized = alignment.ToLowerInvariant();
		var alignsRight = normalized == "right"
			|| normalized == "end"
				&& direction == FlowDirection.LeftToRight
			|| normalized == "start"
				&& direction == FlowDirection.RightToLeft;
		return normalized == "center"
			? anchor - width / 2
			: alignsRight
				? anchor - width
				: anchor;
	}

	private static double ResolveTextTop(
		double anchor,
		double height,
		double fontSize,
		string baseline) =>
		baseline.ToLowerInvariant() switch
		{
			"top" or "hanging" => anchor,
			"middle" => anchor - height / 2,
			"bottom" or "ideographic" => anchor - height,
			_ => anchor - fontSize
		};

	private static double EffectiveAlpha(ReplayState state) =>
		Math.Clamp(state.GlobalAlpha * state.FilterOpacity, 0, 1);

	private static (double Opacity, bool IsComplete) ParseFilter(
		string value)
	{
		if (string.IsNullOrWhiteSpace(value)
			|| value.Equals("none", StringComparison.OrdinalIgnoreCase))
		{
			return (1, true);
		}
		var opacity = 1d;
		var isComplete = true;
		var matched = false;
		foreach (System.Text.RegularExpressions.Match match
			in System.Text.RegularExpressions.Regex.Matches(
				value,
				@"(?<name>[a-zA-Z-]+)\((?<value>[^)]*)\)",
				System.Text.RegularExpressions.RegexOptions.CultureInvariant))
		{
			matched = true;
			var name = match.Groups["name"].Value;
			if (!name.Equals("opacity", StringComparison.OrdinalIgnoreCase))
			{
				isComplete = false;
				continue;
			}
			var text = match.Groups["value"].Value.Trim();
			if (text.EndsWith('%')
				&& double.TryParse(
					text[..^1],
					NumberStyles.Float,
					CultureInfo.InvariantCulture,
					out var percent))
			{
				opacity *= Math.Clamp(percent / 100, 0, 1);
			}
			else if (double.TryParse(
				text,
				NumberStyles.Float,
				CultureInfo.InvariantCulture,
				out var scalar))
			{
				opacity *= Math.Clamp(scalar, 0, 1);
			}
			else
			{
				isComplete = false;
			}
		}
		return (opacity, isComplete && matched);
	}

	private void RegisterPaintResource(
		string kind,
		JsonElement args,
		ReplayState state)
	{
		if (!TryReadResourceId(args, args.GetArrayLength() - 1, out var id))
			return;
		var values = new List<double>();
		for (var index = 0; index < args.GetArrayLength() - 1; index++)
		{
			if (args[index].TryGetDouble(out var value))
				values.Add(value);
		}
		string? source = null;
		if (kind == "createPattern"
			&& args.GetArrayLength() > 0
			&& args[0].ValueKind == JsonValueKind.Object
			&& args[0].TryGetProperty("source", out var sourceNode))
		{
			source = sourceNode.GetString();
		}
		if (kind == "createLinearGradient" && values.Count >= 4)
		{
			var start = Transform(state, values[0], values[1]);
			var end = Transform(state, values[2], values[3]);
			values =
			[
				start.X,
				start.Y,
				end.X,
				end.Y
			];
		}
		_paintResources[id] = new()
		{
			Kind = kind,
			Values = values.ToArray(),
			Source = source
		};
	}

	private void AddGradientStop(JsonElement args)
	{
		if (!TryReadResourceId(args, 0, out var id)
			|| !_paintResources.TryGetValue(id, out var resource))
		{
			return;
		}
		resource.Stops.Add((
			Math.Clamp(Number(args, 1), 0, 1),
			String(args, 2)));
	}

	private Brush CreateBrush(
		string style,
		double opacity)
	{
		if (TryReadResourceId(style, out var id)
			&& _paintResources.TryGetValue(id, out var resource))
		{
			return CreateResourceBrush(resource, opacity);
		}
		return CreateSolidBrush(style, opacity);
	}

	private static Brush CreateResourceBrush(
		PaintResource resource,
		double opacity)
	{
		if (resource.Kind == "createLinearGradient"
			&& resource.Values.Length >= 4)
		{
			var brush = new LinearGradientBrush
			{
				MappingMode = BrushMappingMode.Absolute,
				StartPoint = new(resource.Values[0], resource.Values[1]),
				EndPoint = new(resource.Values[2], resource.Values[3]),
				Opacity = opacity
			};
			foreach (var stop in resource.Stops.OrderBy(static stop => stop.Offset))
			{
				brush.GradientStops.Add(new()
				{
					Offset = stop.Offset,
					Color = ParseColor(stop.Color)
				});
			}
			return brush;
		}
		if (resource.Kind == "createPattern"
			&& Uri.TryCreate(resource.Source, UriKind.Absolute, out var uri))
		{
			return new ImageBrush
			{
				ImageSource = new BitmapImage(uri),
				Stretch = Stretch.None,
				Opacity = opacity
			};
		}
		var fallback = resource.Stops
			.OrderBy(static stop => stop.Offset)
			.FirstOrDefault();
		return CreateSolidBrush(
			string.IsNullOrWhiteSpace(fallback.Color)
				? "#000000"
				: fallback.Color,
			opacity);
	}

	private static bool TryReadResourceId(
		JsonElement args,
		int index,
		out string id)
	{
		id = string.Empty;
		if (args.ValueKind != JsonValueKind.Array
			|| index < 0
			|| args.GetArrayLength() <= index
			|| args[index].ValueKind != JsonValueKind.Object
			|| !args[index].TryGetProperty("resourceId", out var idNode))
		{
			return false;
		}
		var candidate = idNode.GetString();
		if (string.IsNullOrWhiteSpace(candidate))
			return false;
		id = candidate;
		return true;
	}

	private static bool TryReadResourceId(
		string style,
		out string id)
	{
		id = string.Empty;
		if (!style.StartsWith('{'))
			return false;
		using var document = JsonDocument.Parse(style);
		if (!document.RootElement.TryGetProperty(
				"resourceId",
				out var idNode))
		{
			return false;
		}
		var candidate = idNode.GetString();
		if (string.IsNullOrWhiteSpace(candidate))
			return false;
		id = candidate;
		return true;
	}

	private static string String(JsonElement args, int index) =>
		args.ValueKind == JsonValueKind.Array
			&& args.GetArrayLength() > index
				? args[index].ToString()
				: string.Empty;

	private static double Number(
		JsonElement args,
		int index,
		double fallback = 0) =>
		args.ValueKind == JsonValueKind.Array
			&& args.GetArrayLength() > index
			&& args[index].TryGetDouble(out var value)
				? value
				: fallback;

	private static SolidColorBrush CreateSolidBrush(
		string value,
		double opacity)
	{
		var color = ParseColor(value);
		color.A = (byte)Math.Round(color.A * opacity);
		return new(color);
	}

	private static Color ParseColor(string value)
	{
		if (value.Equals("transparent", StringComparison.OrdinalIgnoreCase))
			return Color.FromArgb(0, 0, 0, 0);
		if (value.Equals("white", StringComparison.OrdinalIgnoreCase))
			return Color.FromArgb(255, 255, 255, 255);
		if (TryParseFunctionalColor(value, out var functionalColor))
			return functionalColor;
		if (value.StartsWith('#')
			&& uint.TryParse(
				value[1..],
				NumberStyles.HexNumber,
				CultureInfo.InvariantCulture,
				out var packed))
		{
			if (value.Length == 7)
				return Color.FromArgb(
					255,
					(byte)(packed >> 16),
					(byte)(packed >> 8),
					(byte)packed);
			if (value.Length == 9)
				return Color.FromArgb(
					(byte)(packed >> 24),
					(byte)(packed >> 16),
					(byte)(packed >> 8),
					(byte)packed);
		}
		return Color.FromArgb(255, 0, 0, 0);
	}

	private static bool TryParseFunctionalColor(
		string value,
		out Color color)
	{
		color = default;
		var open = value.IndexOf('(');
		var close = value.LastIndexOf(')');
		if (open < 0 || close <= open)
			return false;
		var name = value[..open].Trim();
		if (!name.Equals("rgb", StringComparison.OrdinalIgnoreCase)
			&& !name.Equals("rgba", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		var components = value[(open + 1)..close]
			.Split(',', StringSplitOptions.TrimEntries);
		if (components.Length is < 3 or > 4
			|| !byte.TryParse(
				components[0],
				NumberStyles.Integer,
				CultureInfo.InvariantCulture,
				out var red)
			|| !byte.TryParse(
				components[1],
				NumberStyles.Integer,
				CultureInfo.InvariantCulture,
				out var green)
			|| !byte.TryParse(
				components[2],
				NumberStyles.Integer,
				CultureInfo.InvariantCulture,
				out var blue))
		{
			return false;
		}
		var alpha = 255;
		if (components.Length == 4
			&& double.TryParse(
				components[3],
				NumberStyles.Float,
				CultureInfo.InvariantCulture,
				out var parsedAlpha))
		{
			alpha = (int)Math.Round(Math.Clamp(parsedAlpha, 0, 1) * 255);
		}
		color = Color.FromArgb((byte)alpha, red, green, blue);
		return true;
	}
}

using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Iwesun.Runtime.Web.WinUI;

internal sealed class HtmlImageMapComposite : HtmlCursorGrid;

internal sealed class HtmlImageMapOverlay : HtmlCursorCanvas;

internal sealed class HtmlImageMapHotspot : HtmlCursorButton
{
	public static readonly DependencyProperty ShapeProperty =
		DependencyProperty.Register(
			nameof(Shape),
			typeof(string),
			typeof(HtmlImageMapHotspot),
			new PropertyMetadata("rect", OnGeometryChanged));

	public static readonly DependencyProperty CoordinatesProperty =
		DependencyProperty.Register(
			nameof(Coordinates),
			typeof(string),
			typeof(HtmlImageMapHotspot),
			new PropertyMetadata(null, OnGeometryChanged));

	public string? Shape
	{
		get => (string?)GetValue(ShapeProperty);
		set => SetValue(ShapeProperty, value);
	}

	public string? Coordinates
	{
		get => (string?)GetValue(CoordinatesProperty);
		set => SetValue(CoordinatesProperty, value);
	}

	private static void OnGeometryChanged(
		DependencyObject sender,
		DependencyPropertyChangedEventArgs args)
	{
		if (sender is HtmlImageMapHotspot hotspot)
			hotspot.ApplyGeometry();
	}

	private void ApplyGeometry()
	{
		var values = (Coordinates ?? string.Empty)
			.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
			.Select(static item =>
				double.TryParse(
					item,
					NumberStyles.Float,
					CultureInfo.InvariantCulture,
					out var value)
						? value
						: double.NaN)
			.ToArray();
		if (values.Any(double.IsNaN))
			return;
		switch (Shape?.ToLowerInvariant())
		{
			case "circle" when values.Length >= 3:
				Canvas.SetLeft(this, values[0] - values[2]);
				Canvas.SetTop(this, values[1] - values[2]);
				Width = values[2] * 2;
				Height = values[2] * 2;
				CornerRadius = new CornerRadius(values[2]);
				break;
			case "poly" when values.Length >= 6 && values.Length % 2 == 0:
				var minimumX = values.Where(
					static (_, index) => index % 2 == 0).Min();
				var minimumY = values.Where(
					static (_, index) => index % 2 == 1).Min();
				var maximumX = values.Where(
					static (_, index) => index % 2 == 0).Max();
				var maximumY = values.Where(
					static (_, index) => index % 2 == 1).Max();
				Canvas.SetLeft(this, minimumX);
				Canvas.SetTop(this, minimumY);
				Width = maximumX - minimumX;
				Height = maximumY - minimumY;
				var figure = new PathFigure
				{
					StartPoint = new(
						values[0] - minimumX,
						values[1] - minimumY),
					IsClosed = true
				};
				for (var index = 2; index < values.Length; index += 2)
				{
					figure.Segments.Add(new LineSegment
					{
						Point = new(
							values[index] - minimumX,
							values[index + 1] - minimumY)
					});
				}
				var geometry = new PathGeometry();
				geometry.Figures.Add(figure);
				Content = new Microsoft.UI.Xaml.Shapes.Path
				{
					Data = geometry,
					Stretch = Stretch.None
				};
				break;
			default:
				if (values.Length < 4)
					return;
				Canvas.SetLeft(this, values[0]);
				Canvas.SetTop(this, values[1]);
				Width = Math.Max(0, values[2] - values[0]);
				Height = Math.Max(0, values[3] - values[1]);
				break;
		}
	}
}

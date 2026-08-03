using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;

namespace Iwesun.Runtime.Web.WinUI;

internal sealed class HtmlImageView : Grid, IHtmlCursorTarget
{
	private string _objectFit = "fill";
	private string _objectPosition = "50% 50%";
	private string _cursorRule = "auto";

	internal HtmlImageView()
	{
		Clip = new RectangleGeometry();
		Image.HorizontalAlignment = HorizontalAlignment.Left;
		Image.VerticalAlignment = VerticalAlignment.Top;
		Image.Stretch = Stretch.Fill;
		Children.Add(Image);
		SizeChanged += OnSizeChanged;
		Image.ImageOpened += OnImageOpened;
	}

	internal Image Image { get; } = new();

	public void ApplyCursor(string rule)
	{
		var cursor = HtmlCursorContract.Create(rule);
		_cursorRule = cursor.Rule;
		ProtectedCursor = cursor.Cursor;
	}

	public bool TryReadCursor(out string value)
	{
		value = string.Empty;
		if (!HtmlCursorContract.Matches(
			_cursorRule,
			ProtectedCursor))
			return false;
		value = _cursorRule;
		return true;
	}

	internal ImageSource? Source
	{
		get => Image.Source;
		set
		{
			Image.Source = value;
			UpdatePhysicalLayout();
		}
	}

	internal void ApplyObjectLayout(string objectFit, string objectPosition)
	{
		_objectFit = NormalizeFit(objectFit);
		_objectPosition = NormalizePosition(objectPosition);
		Image.Stretch = ResolveStretch(_objectFit);
		UpdatePhysicalLayout();
	}

	internal void ApplyStretch(Stretch stretch)
	{
		_objectFit = stretch switch
		{
			Stretch.Uniform => "contain",
			Stretch.UniformToFill => "cover",
			Stretch.None => "none",
			_ => "fill"
		};
		Image.Stretch = stretch;
		UpdatePhysicalLayout();
	}

	internal bool TryReadObjectFit(out string value)
	{
		value = string.Empty;
		if (!ValidatePhysicalLayout())
			return false;
		value = _objectFit;
		return true;
	}

	internal bool TryReadObjectPosition(out string value)
	{
		value = string.Empty;
		if (!ValidatePhysicalLayout())
			return false;
		value = _objectPosition;
		return true;
	}

	private void OnSizeChanged(object sender, SizeChangedEventArgs args) =>
		UpdatePhysicalLayout();

	private void OnImageOpened(object sender, RoutedEventArgs args) =>
		UpdatePhysicalLayout();

	private void UpdatePhysicalLayout()
	{
		var box = ResolveBoxSize();
		if (Clip is RectangleGeometry clip)
			clip.Rect = new Rect(0, 0, box.Width, box.Height);
		var intrinsic = ResolveIntrinsicSize();
		if (box.Width < 0 || box.Height < 0
			|| intrinsic.Width <= 0 || intrinsic.Height <= 0)
		{
			return;
		}
		var rendered = ResolveRenderedSize(box, intrinsic, _objectFit);
		var (x, y) = ParsePosition(_objectPosition);
		Image.Width = rendered.Width;
		Image.Height = rendered.Height;
		Image.Margin = new Thickness(
			ResolveOffset(x, box.Width - rendered.Width),
			ResolveOffset(y, box.Height - rendered.Height),
			0,
			0);
	}

	private bool ValidatePhysicalLayout()
	{
		if (Children.Count != 1
			|| !ReferenceEquals(Children[0], Image)
			|| Clip is not RectangleGeometry clip)
		{
			return false;
		}
		var box = ResolveBoxSize();
		var intrinsic = ResolveIntrinsicSize();
		if (box.Width <= 0 || box.Height <= 0
			|| intrinsic.Width <= 0 || intrinsic.Height <= 0)
		{
			return Near(clip.Rect.X, 0)
				&& Near(clip.Rect.Y, 0)
				&& Near(clip.Rect.Width, box.Width)
				&& Near(clip.Rect.Height, box.Height)
				&& Image.Stretch == ResolveStretch(_objectFit);
		}
		var rendered = ResolveRenderedSize(box, intrinsic, _objectFit);
		var (x, y) = ParsePosition(_objectPosition);
		return Near(Image.Width, rendered.Width)
			&& Near(Image.Height, rendered.Height)
			&& Near(Image.Margin.Left, ResolveOffset(x, box.Width - rendered.Width))
			&& Near(Image.Margin.Top, ResolveOffset(y, box.Height - rendered.Height))
			&& Near(clip.Rect.X, 0)
			&& Near(clip.Rect.Y, 0)
			&& Near(clip.Rect.Width, box.Width)
			&& Near(clip.Rect.Height, box.Height);
	}

	private Size ResolveBoxSize() => new(
		Math.Max(0, ActualWidth > 0 ? ActualWidth : FiniteOrZero(Width)),
		Math.Max(0, ActualHeight > 0 ? ActualHeight : FiniteOrZero(Height)));

	private Size ResolveIntrinsicSize()
	{
		if (Source is BitmapSource bitmap
			&& bitmap.PixelWidth > 0
			&& bitmap.PixelHeight > 0)
		{
			return new(bitmap.PixelWidth, bitmap.PixelHeight);
		}
		return new(
			Image.ActualWidth > 0 ? Image.ActualWidth : FiniteOrZero(Image.Width),
			Image.ActualHeight > 0 ? Image.ActualHeight : FiniteOrZero(Image.Height));
	}

	private static Size ResolveRenderedSize(
		Size box,
		Size intrinsic,
		string fit)
	{
		if (fit == "fill")
			return box;
		if (fit == "none")
			return intrinsic;
		var contain = Math.Min(
			box.Width / intrinsic.Width,
			box.Height / intrinsic.Height);
		var scale = fit == "cover"
			? Math.Max(box.Width / intrinsic.Width, box.Height / intrinsic.Height)
			: fit == "scale-down" ? Math.Min(1, contain) : contain;
		return new(intrinsic.Width * scale, intrinsic.Height * scale);
	}

	private static (Position X, Position Y) ParsePosition(string expression)
	{
		var tokens = expression.Split(
			' ',
			StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (tokens.Length == 1)
		{
			return IsVerticalKeyword(tokens[0])
				? (new(.5, 0), ParseCoordinate(tokens[0], false))
				: (ParseCoordinate(tokens[0], true), new(.5, 0));
		}
		if (tokens.Length == 2)
		{
			if (IsVerticalKeyword(tokens[0]) && IsHorizontalKeyword(tokens[1]))
				return (ParseCoordinate(tokens[1], true), ParseCoordinate(tokens[0], false));
			return (ParseCoordinate(tokens[0], true), ParseCoordinate(tokens[1], false));
		}
		if (tokens.Length is 3 or 4)
		{
			var x = new Position(.5, 0);
			var y = new Position(.5, 0);
			var index = 0;
			while (index < tokens.Length)
			{
				var edge = tokens[index++].ToLowerInvariant();
				var offset = index < tokens.Length && !IsEdgeKeyword(tokens[index])
					? ParseAbsoluteOffset(tokens[index++])
					: 0;
				switch (edge)
				{
					case "left": x = new(0, offset); break;
					case "right": x = new(1, -offset); break;
					case "top": y = new(0, offset); break;
					case "bottom": y = new(1, -offset); break;
					default:
						throw new InvalidDataException(
							$"Unsupported CSS object-position edge '{edge}'.");
				}
			}
			return (x, y);
		}
		throw new InvalidDataException($"Unsupported CSS object-position '{expression}'.");
	}

	private static bool IsHorizontalKeyword(string token) =>
		token is "left" or "right" or "center";

	private static bool IsVerticalKeyword(string token) =>
		token is "top" or "bottom";

	private static bool IsEdgeKeyword(string token) =>
		token is "left" or "right" or "top" or "bottom";

	private static double ParseAbsoluteOffset(string token)
	{
		if (token == "0")
			return 0;
		if (token.EndsWith("px", StringComparison.Ordinal)
			&& double.TryParse(token[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var pixels))
		{
			return pixels;
		}
		throw new InvalidDataException(
			$"Only px edge offsets are currently materialized for object-position: '{token}'.");
	}

	private static Position ParseCoordinate(string token, bool horizontal)
	{
		token = token.ToLowerInvariant();
		if (token is "center")
			return new(.5, 0);
		if (horizontal && token is "left" || !horizontal && token is "top")
			return new(0, 0);
		if (horizontal && token is "right" || !horizontal && token is "bottom")
			return new(1, 0);
		if (token.EndsWith('%')
			&& double.TryParse(token[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
			return new(percent / 100, 0);
		if (token == "0")
			return new(0, 0);
		if (token.EndsWith("px", StringComparison.Ordinal)
			&& double.TryParse(token[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var pixels))
			return new(0, pixels);
		throw new InvalidDataException($"Unsupported CSS object-position coordinate '{token}'.");
	}

	private static double ResolveOffset(Position position, double freeSpace) =>
		position.Fraction * freeSpace + position.Pixels;

	private static string NormalizeFit(string value) =>
		value.Trim().ToLowerInvariant() switch
		{
			"fill" or "contain" or "cover" or "none" or "scale-down" =>
				value.Trim().ToLowerInvariant(),
			_ => throw new InvalidDataException($"Unsupported CSS object-fit '{value}'.")
		};

	private static Stretch ResolveStretch(string fit) => fit switch
	{
		"contain" => Stretch.Uniform,
		"cover" => Stretch.UniformToFill,
		"none" => Stretch.None,
		"scale-down" => Stretch.Uniform,
		_ => Stretch.Fill
	};

	private static string NormalizePosition(string value) =>
		string.Join(' ', value.Split(
			' ',
			StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
			.ToLowerInvariant();

	private static double FiniteOrZero(double value) =>
		double.IsFinite(value) ? value : 0;

	private static bool Near(double left, double right) =>
		Math.Abs(left - right) <= .001;

	private readonly record struct Position(double Fraction, double Pixels);
}

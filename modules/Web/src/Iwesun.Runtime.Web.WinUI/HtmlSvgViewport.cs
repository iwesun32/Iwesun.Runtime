using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Iwesun.Runtime.Web.WinUI;

internal sealed class HtmlSvgViewport : ContentControl, IHtmlCursorTarget
{
	private readonly Viewbox _viewbox;
	private string _cursorRule = "auto";
	private string _viewBox = string.Empty;
	private string _preserveAspectRatio = "xMidYMid meet";

	internal HtmlSvgViewport()
	{
		DefaultStyleKey = typeof(ContentControl);
		CoordinateSurface = new HtmlCanvasPanel();
		_viewbox = new Viewbox
		{
			Child = CoordinateSurface,
			HorizontalAlignment = HorizontalAlignment.Stretch,
			VerticalAlignment = VerticalAlignment.Stretch,
			Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform
		};
		HorizontalContentAlignment = HorizontalAlignment.Stretch;
		VerticalContentAlignment = VerticalAlignment.Stretch;
		Content = _viewbox;
	}

	internal HtmlCanvasPanel CoordinateSurface { get; }

	internal string ViewBox
	{
		get => _viewBox;
		set
		{
			_viewBox = value ?? string.Empty;
			ApplyViewportContract();
		}
	}

	internal string PreserveAspectRatio
	{
		get => _preserveAspectRatio;
		set
		{
			_preserveAspectRatio = value ?? string.Empty;
			ApplyViewportContract();
		}
	}

	public void ApplyCursor(string rule)
	{
		var cursor = HtmlCursorContract.Create(rule);
		_cursorRule = cursor.Rule;
		ProtectedCursor = cursor.Cursor;
	}

	public bool TryReadCursor(out string value)
	{
		value = string.Empty;
		if (!HtmlCursorContract.Matches(_cursorRule, ProtectedCursor))
			return false;
		value = _cursorRule;
		return true;
	}

	private void ApplyViewportContract()
	{
		var parts = _viewBox.Split(
			[' ', ','],
			StringSplitOptions.RemoveEmptyEntries
				| StringSplitOptions.TrimEntries);
		if (parts.Length == 4
			&& parts.All(part => double.TryParse(
				part,
				NumberStyles.Float,
				CultureInfo.InvariantCulture,
				out _)))
		{
			var x = double.Parse(parts[0], CultureInfo.InvariantCulture);
			var y = double.Parse(parts[1], CultureInfo.InvariantCulture);
			var width = double.Parse(parts[2], CultureInfo.InvariantCulture);
			var height = double.Parse(parts[3], CultureInfo.InvariantCulture);
			if (width > 0 && height > 0)
			{
				CoordinateSurface.Width = width;
				CoordinateSurface.Height = height;
				CoordinateSurface.RenderTransform = new Microsoft.UI.Xaml.Media.TranslateTransform
				{
					X = -x,
					Y = -y
				};
			}
		}
		_viewbox.Stretch = _preserveAspectRatio.Trim().Equals(
			"none",
			StringComparison.OrdinalIgnoreCase)
				? Microsoft.UI.Xaml.Media.Stretch.Fill
				: Microsoft.UI.Xaml.Media.Stretch.Uniform;
	}
}

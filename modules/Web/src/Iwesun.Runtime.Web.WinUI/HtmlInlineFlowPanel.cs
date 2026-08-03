using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Iwesun.Runtime.Web.WinUI;

internal class HtmlInlineFlowPanel : HtmlCursorContentControl
{
	private readonly HtmlInlineLayoutPanel _layout = new();

	internal HtmlInlineFlowPanel()
	{
		HorizontalContentAlignment = HorizontalAlignment.Stretch;
		VerticalContentAlignment = VerticalAlignment.Stretch;
		Content = _layout;
	}

	internal UIElementCollection Children => _layout.Children;
}

internal sealed class HtmlInlineLayoutPanel : Panel
{
	protected override Size MeasureOverride(Size availableSize)
	{
		var availableWidth = double.IsFinite(availableSize.Width)
			? Math.Max(0, availableSize.Width)
			: double.PositiveInfinity;
		var lineWidth = 0d;
		var lineHeight = 0d;
		var desiredWidth = 0d;
		var desiredHeight = 0d;
		foreach (var child in Children)
		{
			child.Measure(new(availableWidth, availableSize.Height));
			if (child is HtmlLineBreakElement)
			{
				CommitLine(
					ref desiredWidth,
					ref desiredHeight,
					ref lineWidth,
					ref lineHeight);
				continue;
			}
			var childSize = child.DesiredSize;
			if (lineWidth > 0
				&& double.IsFinite(availableWidth)
				&& lineWidth + childSize.Width > availableWidth)
			{
				CommitLine(
					ref desiredWidth,
					ref desiredHeight,
					ref lineWidth,
					ref lineHeight);
			}
			lineWidth += childSize.Width;
			lineHeight = Math.Max(lineHeight, childSize.Height);
		}
		CommitLine(
			ref desiredWidth,
			ref desiredHeight,
			ref lineWidth,
			ref lineHeight);
		return new(
			double.IsFinite(availableWidth)
				? Math.Min(availableWidth, desiredWidth)
				: desiredWidth,
			desiredHeight);
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		var x = 0d;
		var y = 0d;
		var lineHeight = 0d;
		foreach (var child in Children)
		{
			if (child is HtmlLineBreakElement)
			{
				child.Arrange(new(x, y, 0, lineHeight));
				x = 0;
				y += lineHeight;
				lineHeight = 0;
				continue;
			}
			var childSize = child.DesiredSize;
			if (x > 0 && x + childSize.Width > finalSize.Width)
			{
				x = 0;
				y += lineHeight;
				lineHeight = 0;
			}
			child.Arrange(new Rect(x, y, childSize.Width, childSize.Height));
			x += childSize.Width;
			lineHeight = Math.Max(lineHeight, childSize.Height);
		}
		return finalSize;
	}

	private static void CommitLine(
		ref double desiredWidth,
		ref double desiredHeight,
		ref double lineWidth,
		ref double lineHeight)
	{
		desiredWidth = Math.Max(desiredWidth, lineWidth);
		desiredHeight += lineHeight;
		lineWidth = 0;
		lineHeight = 0;
	}
}

internal sealed class HtmlLineBreakElement : FrameworkElement
{
	protected override Size MeasureOverride(Size availableSize) => new(0, 0);
}

internal sealed class HtmlWordBreakOpportunityElement : FrameworkElement
{
	protected override Size MeasureOverride(Size availableSize) => new(0, 0);
}

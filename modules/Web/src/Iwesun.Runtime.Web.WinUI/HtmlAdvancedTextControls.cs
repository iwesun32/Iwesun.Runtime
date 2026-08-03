using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Iwesun.Runtime.Web.WinUI;

internal abstract class HtmlSemanticTextControl : HtmlCursorContentControl
{
	private readonly TextBlock _text = new();

	protected HtmlSemanticTextControl() => Content = _text;

	public static readonly DependencyProperty TextProperty =
		DependencyProperty.Register(
			nameof(Text),
			typeof(string),
			typeof(HtmlSemanticTextControl),
			new PropertyMetadata(null, OnTextChanged));

	public static readonly DependencyProperty TextAlignmentProperty =
		DependencyProperty.Register(
			nameof(TextAlignment),
			typeof(TextAlignment),
			typeof(HtmlSemanticTextControl),
			new PropertyMetadata(
				TextAlignment.Left,
				OnTextAlignmentChanged));

	public string? Text
	{
		get => (string?)GetValue(TextProperty);
		set => SetValue(TextProperty, value);
	}

	public TextAlignment TextAlignment
	{
		get => (TextAlignment)GetValue(TextAlignmentProperty);
		set => SetValue(TextAlignmentProperty, value);
	}

	private static void OnTextChanged(
		DependencyObject sender,
		DependencyPropertyChangedEventArgs args)
	{
		if (sender is HtmlSemanticTextControl control)
			control._text.Text = control.TransformText(
				args.NewValue as string ?? string.Empty);
	}

	protected virtual string TransformText(string value) => value;

	private static void OnTextAlignmentChanged(
		DependencyObject sender,
		DependencyPropertyChangedEventArgs args)
	{
		if (sender is HtmlSemanticTextControl control
			&& args.NewValue is TextAlignment alignment)
		{
			control._text.TextAlignment = alignment;
		}
	}
}

internal sealed class HtmlBidiIsolationTextBlock : HtmlSemanticTextControl
{
	protected override string TransformText(string value) =>
		"\u2068" + value + "\u2069";
}

internal sealed class HtmlBidiOverrideTextBlock : HtmlSemanticTextControl
{
	protected override string TransformText(string value) =>
		FlowDirection == FlowDirection.RightToLeft
			? "\u202E" + value + "\u202C"
			: "\u202D" + value + "\u202C";
}

internal sealed class HtmlBidiIsolationPanel : HtmlInlineFlowPanel;

internal sealed class HtmlBidiOverridePanel : HtmlInlineFlowPanel;

internal sealed class HtmlSubscriptTextBlock : HtmlSemanticTextControl
{
	public HtmlSubscriptTextBlock() => Translation = new Vector3(0, 4, 0);
}

internal sealed class HtmlSuperscriptTextBlock : HtmlSemanticTextControl
{
	public HtmlSuperscriptTextBlock() => Translation = new Vector3(0, -4, 0);
}

internal sealed class HtmlSubscriptPanel : HtmlInlineFlowPanel
{
	public HtmlSubscriptPanel() => Translation = new Vector3(0, 4, 0);
}

internal sealed class HtmlSuperscriptPanel : HtmlInlineFlowPanel
{
	public HtmlSuperscriptPanel() => Translation = new Vector3(0, -4, 0);
}

internal sealed class HtmlRubyAnnotationTextBlock : HtmlSemanticTextControl
{
	public HtmlRubyAnnotationTextBlock() => FontSize = 10;
}

internal sealed class HtmlRubyAnnotationPanel : HtmlInlineFlowPanel
{
	public HtmlRubyAnnotationPanel()
	{
		FontSize = 10;
	}

	public new double FontSize
	{
		get => base.FontSize;
		set => base.FontSize = value;
	}
}

internal sealed class HtmlRubyPanel : HtmlCursorPanel
{
	protected override Size MeasureOverride(Size availableSize)
	{
		var baseWidth = 0d;
		var baseHeight = 0d;
		var annotationWidth = 0d;
		var annotationHeight = 0d;
		foreach (var child in Children)
		{
			child.Measure(availableSize);
			if (IsAnnotation(child))
			{
				annotationWidth += child.DesiredSize.Width;
				annotationHeight = Math.Max(
					annotationHeight,
					child.DesiredSize.Height);
			}
			else
			{
				baseWidth += child.DesiredSize.Width;
				baseHeight = Math.Max(baseHeight, child.DesiredSize.Height);
			}
		}
		return new(
			Math.Max(baseWidth, annotationWidth),
			baseHeight + annotationHeight);
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		var annotationHeight = Children
			.Where(IsAnnotation)
			.Select(static child => child.DesiredSize.Height)
			.DefaultIfEmpty(0)
			.Max();
		var annotationX = 0d;
		var baseX = 0d;
		foreach (var child in Children)
		{
			if (IsAnnotation(child))
			{
				child.Arrange(new(
					annotationX,
					0,
					child.DesiredSize.Width,
					child.DesiredSize.Height));
				annotationX += child.DesiredSize.Width;
			}
			else
			{
				child.Arrange(new(
					baseX,
					annotationHeight,
					child.DesiredSize.Width,
					child.DesiredSize.Height));
				baseX += child.DesiredSize.Width;
			}
		}
		return finalSize;
	}

	private static bool IsAnnotation(UIElement element) =>
		element is HtmlRubyAnnotationTextBlock
			or HtmlRubyAnnotationPanel;
}

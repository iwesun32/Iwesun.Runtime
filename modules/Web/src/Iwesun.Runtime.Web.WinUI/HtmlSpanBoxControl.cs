using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Iwesun.Runtime.Web.WinUI;

/// <summary>
/// Preserves a positioned span's CSS principal box independently from the
/// intrinsic line box of its text content.
/// </summary>
internal sealed class HtmlSpanBoxControl : HtmlCursorContentControl
{
	private readonly TextBlock _text = new();

	internal HtmlSpanBoxControl()
	{
		Content = _text;
	}

	public static readonly DependencyProperty TextProperty =
		DependencyProperty.Register(
			nameof(Text),
			typeof(string),
			typeof(HtmlSpanBoxControl),
			new PropertyMetadata(string.Empty, OnTextChanged));

	public string Text
	{
		get => (string)GetValue(TextProperty);
		set => SetValue(TextProperty, value);
	}

	private static void OnTextChanged(
		DependencyObject owner,
		DependencyPropertyChangedEventArgs args)
	{
		var control = (HtmlSpanBoxControl)owner;
		control._text.Text = args.NewValue as string ?? string.Empty;
	}
}

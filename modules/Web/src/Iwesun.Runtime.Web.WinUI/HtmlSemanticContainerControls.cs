using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Iwesun.Runtime.Web.WinUI;

internal sealed class HtmlFieldSetPanel : HtmlCursorStackPanel
{
	private readonly TextBlock _header = new();

	public HtmlFieldSetPanel() => Children.Add(_header);

	public static readonly DependencyProperty HeaderProperty =
		DependencyProperty.Register(
			nameof(Header),
			typeof(string),
			typeof(HtmlFieldSetPanel),
			new PropertyMetadata(null, OnHeaderChanged));

	public string? Header
	{
		get => (string?)GetValue(HeaderProperty);
		set => SetValue(HeaderProperty, value);
	}

	private static void OnHeaderChanged(
		DependencyObject sender,
		DependencyPropertyChangedEventArgs args)
	{
		if (sender is HtmlFieldSetPanel panel)
			panel._header.Text = args.NewValue as string ?? string.Empty;
	}
}

internal sealed class HtmlDialogControl : HtmlCursorContentControl
{
	public static readonly DependencyProperty IsOpenProperty =
		DependencyProperty.Register(
			nameof(IsOpen),
			typeof(bool),
			typeof(HtmlDialogControl),
			new PropertyMetadata(false, OnIsOpenChanged));

	public static readonly DependencyProperty ClosedByProperty =
		DependencyProperty.Register(
			nameof(ClosedBy),
			typeof(string),
			typeof(HtmlDialogControl),
			new PropertyMetadata(null));

	public bool IsOpen
	{
		get => (bool)GetValue(IsOpenProperty);
		set => SetValue(IsOpenProperty, value);
	}

	public string? ClosedBy
	{
		get => (string?)GetValue(ClosedByProperty);
		set => SetValue(ClosedByProperty, value);
	}

	private static void OnIsOpenChanged(
		DependencyObject sender,
		DependencyPropertyChangedEventArgs args)
	{
		if (sender is HtmlDialogControl dialog)
		{
			dialog.Visibility = args.NewValue is true
				? Visibility.Visible
				: Visibility.Collapsed;
		}
	}
}

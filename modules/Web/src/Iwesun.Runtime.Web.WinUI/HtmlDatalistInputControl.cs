using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Iwesun.Runtime.Web.WinUI;

internal sealed class HtmlDatalistInputControl : HtmlCursorContentControl
{
	private readonly AutoSuggestBox _editor = new();

	public static readonly DependencyProperty TextProperty =
		DependencyProperty.Register(
			nameof(Text),
			typeof(string),
			typeof(HtmlDatalistInputControl),
			new PropertyMetadata(
				string.Empty,
				static (owner, args) =>
					((HtmlDatalistInputControl)owner)._editor.Text =
						args.NewValue as string ?? string.Empty));

	public static readonly DependencyProperty PlaceholderTextProperty =
		DependencyProperty.Register(
			nameof(PlaceholderText),
			typeof(string),
			typeof(HtmlDatalistInputControl),
			new PropertyMetadata(
				string.Empty,
				static (owner, args) =>
					((HtmlDatalistInputControl)owner)._editor.PlaceholderText =
						args.NewValue as string ?? string.Empty));

	public static readonly DependencyProperty SuggestionValuesProperty =
		DependencyProperty.Register(
			nameof(SuggestionValues),
			typeof(string),
			typeof(HtmlDatalistInputControl),
			new PropertyMetadata(
				string.Empty,
				static (owner, args) =>
					((HtmlDatalistInputControl)owner).ApplySuggestions(
						args.NewValue as string)));

	public static readonly DependencyProperty MaxLengthProperty =
		DependencyProperty.Register(
			nameof(MaxLength),
			typeof(int),
			typeof(HtmlDatalistInputControl),
			new PropertyMetadata(
				0,
				static (owner, args) =>
					((HtmlDatalistInputControl)owner).EnforceMaxLength()));

	public static readonly DependencyProperty IsReadOnlyProperty =
		DependencyProperty.Register(
			nameof(IsReadOnly),
			typeof(bool),
			typeof(HtmlDatalistInputControl),
			new PropertyMetadata(
				false,
				static (owner, args) =>
					((HtmlDatalistInputControl)owner)._editor.IsEnabled =
						!(bool)args.NewValue));

	internal HtmlDatalistInputControl()
	{
		Content = _editor;
		_editor.TextChanged += (_, _) =>
		{
			EnforceMaxLength();
			if (!Text.Equals(_editor.Text, StringComparison.Ordinal))
				SetValue(TextProperty, _editor.Text);
		};
	}

	public string Text
	{
		get => (string)GetValue(TextProperty);
		set => SetValue(TextProperty, value);
	}

	public string PlaceholderText
	{
		get => (string)GetValue(PlaceholderTextProperty);
		set => SetValue(PlaceholderTextProperty, value);
	}

	public string SuggestionValues
	{
		get => (string)GetValue(SuggestionValuesProperty);
		set => SetValue(SuggestionValuesProperty, value);
	}

	public int MaxLength
	{
		get => (int)GetValue(MaxLengthProperty);
		set => SetValue(MaxLengthProperty, value);
	}

	public bool IsReadOnly
	{
		get => (bool)GetValue(IsReadOnlyProperty);
		set => SetValue(IsReadOnlyProperty, value);
	}

	private void ApplySuggestions(string? value)
	{
		_editor.ItemsSource = string.IsNullOrEmpty(value)
			? Array.Empty<string>()
			: value.Split(
				'\u001F',
				StringSplitOptions.RemoveEmptyEntries
					| StringSplitOptions.TrimEntries);
	}

	private void EnforceMaxLength()
	{
		if (MaxLength > 0 && _editor.Text.Length > MaxLength)
			_editor.Text = _editor.Text[..MaxLength];
	}
}

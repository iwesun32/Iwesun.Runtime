using Microsoft.UI.Xaml.Controls;

namespace Iwesun.Runtime.Web.WinUI;

internal sealed class HtmlCursorTextBox : TextBox, IHtmlCursorTarget
{
	private string _cursorRule = "auto";

	public void ApplyCursor(string rule)
	{
		var cursor = HtmlCursorContract.Create(rule);
		_cursorRule = cursor.Rule;
		ProtectedCursor = cursor.Cursor;
	}

	public bool TryReadCursor(out string value)
	{
		value = _cursorRule;
		return HtmlCursorContract.Matches(_cursorRule, ProtectedCursor);
	}
}

internal sealed class HtmlCursorPasswordBoxHost : HtmlCursorContentControl
{
	private readonly PasswordBox _editor = new();

	internal HtmlCursorPasswordBoxHost()
	{
		HorizontalContentAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch;
		VerticalContentAlignment = Microsoft.UI.Xaml.VerticalAlignment.Stretch;
		Content = _editor;
		RegisterPropertyChangedCallback(IsEnabledProperty, (_, _) =>
			_editor.IsEnabled = IsEnabled);
	}

	internal PasswordBox Editor => _editor;

	internal string Password
	{
		get => _editor.Password;
		set => _editor.Password = value;
	}

	internal string PlaceholderText
	{
		get => _editor.PlaceholderText;
		set => _editor.PlaceholderText = value;
	}

	internal int MaxLength
	{
		get => _editor.MaxLength;
		set => _editor.MaxLength = value;
	}

	internal bool IsReadOnly
	{
		get => !_editor.IsHitTestVisible;
		set
		{
			_editor.IsHitTestVisible = !value;
			_editor.IsTabStop = !value;
		}
	}
}

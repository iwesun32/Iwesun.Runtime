using Microsoft.UI.Xaml.Controls;

namespace Iwesun.Runtime.Web.WinUI;

internal abstract class HtmlCursorContentControl
	: ContentControl, IHtmlCursorTarget
{
	private string _cursorRule = "auto";

	protected HtmlCursorContentControl()
	{
		DefaultStyleKey = typeof(ContentControl);
	}

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

internal abstract class HtmlCursorStackPanel
	: StackPanel, IHtmlCursorTarget
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

internal abstract class HtmlCursorPanel : Panel, IHtmlCursorTarget
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

internal class HtmlCursorGrid : Grid, IHtmlCursorTarget
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

internal class HtmlCursorCanvas : Canvas, IHtmlCursorTarget
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

internal class HtmlCursorProgressBar : ProgressBar, IHtmlCursorTarget
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

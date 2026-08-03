using Microsoft.UI.Xaml.Controls;

namespace Iwesun.Runtime.Web.WinUI;

/// <summary>
/// Strong Button base which exposes the protected WinUI cursor as auditable
/// runtime evidence for HTML controls.
/// </summary>
internal class HtmlCursorButton : Button, IHtmlCursorTarget
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

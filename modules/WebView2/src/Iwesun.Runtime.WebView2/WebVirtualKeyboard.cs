namespace Iwesun.Runtime.WebView2;

/// <summary>Dispatches reusable native keyboard and coordinate input operations.</summary>
public static class WebVirtualKeyboard
{
	public static Task TypeTextAsync(IWebRuntimeScriptSession session, string backendId, string stage, string text, CancellationToken ct) =>
		GetInputSession(session).InsertTextAsync(text, ct);

	public static async Task TypeTextHybridAsync(
		IWebRuntimeScriptSession session,
		string backendId,
		string stage,
		string text,
		CancellationToken ct,
		int slowPrefixChars = 1)
	{
		if (string.IsNullOrEmpty(text))
			return;

		var input = GetInputSession(session);
		var prefixLength = Math.Clamp(slowPrefixChars, 0, text.Length);
		if (prefixLength > 0)
			await input.InsertTextAsync(text[..prefixLength], ct).ConfigureAwait(false);
		if (prefixLength < text.Length)
			await input.InsertTextBulkAsync(text[prefixLength..], ct).ConfigureAwait(false);
	}

	public static Task PressEnterAsync(IWebRuntimeScriptSession session, string backendId, string stage, CancellationToken ct) =>
		GetInputSession(session).PressEnterAsync(ct);

	public static Task PressBackspaceAsync(IWebRuntimeScriptSession session, string backendId, string stage, CancellationToken ct) =>
		GetInputSession(session).PressBackspaceAsync(ct);

	public static Task PressDeleteAsync(IWebRuntimeScriptSession session, string backendId, string stage, CancellationToken ct) =>
		GetInputSession(session).PressDeleteAsync(ct);

	public static Task PressShortcutAsync(
		IWebRuntimeScriptSession session,
		string backendId,
		string stage,
		string key,
		string code,
		int windowsVirtualKeyCode,
		bool ctrl,
		bool shift,
		bool alt,
		bool meta,
		CancellationToken ct) =>
		GetInputSession(session).PressShortcutAsync(key, code, windowsVirtualKeyCode, ctrl, shift, alt, meta, ct);

	public static Task ClickAtAsync(IWebRuntimeScriptSession session, string backendId, string stage, int x, int y, CancellationToken ct) =>
		GetInputSession(session).ClickAtAsync(x, y, ct);

	private static IWebRuntimeInputSession GetInputSession(IWebRuntimeScriptSession session) =>
		session as IWebRuntimeInputSession
		?? throw new NotSupportedException($"{session.GetType().Name} does not support browser input dispatch.");
}

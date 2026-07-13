namespace Iwesun.Runtime.WebView2;

public sealed record WebRuntimeInputDispatchResult(bool Handled, bool Success, object? Value = null, string? Error = null);

/// <summary>Validates and dispatches standard WebRuntime input actions.</summary>
public static class WebRuntimeInputDispatcher
{
	public static async Task<WebRuntimeInputDispatchResult> TryExecuteAsync(
		IWebRuntimeScriptSession session,
		WebRuntimeControlRequest request,
		CancellationToken ct,
		WebAmbientInputController? ambientInput = null)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(request);
		var action = Normalize(request.Action);
		try
		{
			switch (action)
			{
				case "inputmouseclick":
					if (request.X is null || request.Y is null)
						return Failed("Mouse click requires x and y.");
					await WebVirtualKeyboard.ClickAtAsync(session, request.BackendId, request.Action, request.X.Value, request.Y.Value, ct).ConfigureAwait(false);
					return Completed(new { request.X, request.Y });
				case "inputmousemove":
					if (request.X is null || request.Y is null)
						return Failed("Mouse move requires x and y.");
					await GetInputSession(session).MoveMouseAsync(request.X.Value, request.Y.Value, ct).ConfigureAwait(false);
					return Completed(new { request.X, request.Y });
				case "inputmousedoubleclick":
					if (request.X is null || request.Y is null)
						return Failed("Mouse double click requires x and y.");
					await GetInputSession(session).DoubleClickAtAsync(request.X.Value, request.Y.Value, ct).ConfigureAwait(false);
					return Completed(new { request.X, request.Y });
				case "inputmousewheel":
					if (request.X is null || request.Y is null)
						return Failed("Mouse wheel requires x and y.");
					await GetInputSession(session).ScrollAsync(request.X.Value, request.Y.Value, request.DeltaX ?? 0, request.DeltaY ?? 0, ct).ConfigureAwait(false);
					return Completed(new { request.X, request.Y, request.DeltaX, request.DeltaY });
				case "inputkeyboardtype":
					if (request.Text is null)
						return Failed("Keyboard type requires text.");
					await WebVirtualKeyboard.TypeTextHybridAsync(session, request.BackendId, request.Action, request.Text, ct, request.SlowPrefixChars ?? 1).ConfigureAwait(false);
					return Completed(new { Length = request.Text.Length });
				case "inputkeyboardpress":
					return await DispatchKeyAsync(session, request, ct).ConfigureAwait(false);
				case "inputkeyboardshortcut":
					if (string.IsNullOrWhiteSpace(request.Key) || string.IsNullOrWhiteSpace(request.Code) || request.WindowsVirtualKeyCode is null)
						return Failed("Keyboard shortcut requires key, code and windowsVirtualKeyCode.");
					await WebVirtualKeyboard.PressShortcutAsync(
						session, request.BackendId, request.Action,
						request.Key, request.Code, request.WindowsVirtualKeyCode.Value,
						request.Ctrl, request.Shift, request.Alt, request.Meta, ct).ConfigureAwait(false);
					return Completed(new { request.Key, request.Code });
				case "inputambientstart":
					if (ambientInput is null)
						return Failed("Ambient input is not available for this session.");
					ambientInput.Start(mouse: true, keyboard: true);
					return Completed(new { Running = true });
				case "inputambientstop":
					if (ambientInput is null)
						return Failed("Ambient input is not available for this session.");
					await ambientInput.StopAsync().ConfigureAwait(false);
					return Completed(new { Running = false });
				case "inputambientstatus":
					return ambientInput is null
						? Failed("Ambient input is not available for this session.")
						: Completed(new { ambientInput.IsStarted, ambientInput.IsStopped });
				default:
					return new WebRuntimeInputDispatchResult(false, false);
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			return Failed(ex.Message);
		}
	}

	private static async Task<WebRuntimeInputDispatchResult> DispatchKeyAsync(
		IWebRuntimeScriptSession session,
		WebRuntimeControlRequest request,
		CancellationToken ct)
	{
		switch (request.Key?.Trim().ToLowerInvariant())
		{
			case "enter": await WebVirtualKeyboard.PressEnterAsync(session, request.BackendId, request.Action, ct).ConfigureAwait(false); break;
			case "backspace": await WebVirtualKeyboard.PressBackspaceAsync(session, request.BackendId, request.Action, ct).ConfigureAwait(false); break;
			case "delete": await WebVirtualKeyboard.PressDeleteAsync(session, request.BackendId, request.Action, ct).ConfigureAwait(false); break;
			default: return Failed("Keyboard press supports Enter, Backspace and Delete.");
		}
		return Completed(new { request.Key });
	}

	private static string Normalize(string action) =>
		action.Trim().Replace(".", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();

	private static WebRuntimeInputDispatchResult Completed(object value) => new(true, true, value);
	private static WebRuntimeInputDispatchResult Failed(string error) => new(true, false, null, error);
	private static IWebRuntimeInputSession GetInputSession(IWebRuntimeScriptSession session) =>
		session as IWebRuntimeInputSession
		?? throw new NotSupportedException($"{session.GetType().Name} does not support browser input dispatch.");
}

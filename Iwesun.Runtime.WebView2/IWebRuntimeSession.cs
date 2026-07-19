namespace Iwesun.Runtime.WebView2;

/// <summary>Provides the script surface required by reusable WebRuntime controls.</summary>
public interface IWebRuntimeScriptSession
{
	Task<string> EvaluateStringAsync(string expression, CancellationToken ct);
	Task<string> EvaluateStringInFrameAsync(string sourceUrlContains, string expression, CancellationToken ct);
}

/// <summary>Provides stable DOM-order frame evaluation for snapshot restoration.</summary>
public interface IWebRuntimeIndexedFrameScriptSession
{
	Task<string> EvaluateStringInFrameAsync(int frameIndex, string expression, CancellationToken ct);
}

/// <summary>Provides native browser input dispatch independent of a business host.</summary>
public interface IWebRuntimeInputSession
{
	Task InsertTextAsync(string text, CancellationToken ct);
	Task InsertTextBulkAsync(string text, CancellationToken ct);
	Task PressEnterAsync(CancellationToken ct);
	Task PressBackspaceAsync(CancellationToken ct);
	Task PressDeleteAsync(CancellationToken ct);
	Task PressShortcutAsync(string key, string code, int windowsVirtualKeyCode, bool ctrl, bool shift, bool alt, bool meta, CancellationToken ct);
	Task ClickAtAsync(int x, int y, CancellationToken ct);
	Task MoveMouseAsync(int x, int y, CancellationToken ct);
	Task DoubleClickAtAsync(int x, int y, CancellationToken ct);
	Task ScrollAsync(int x, int y, int deltaX, int deltaY, CancellationToken ct);
}

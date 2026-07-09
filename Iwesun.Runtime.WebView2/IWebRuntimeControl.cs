namespace Iwesun.Runtime.WebView2;

public interface IWebRuntimeControl
{
	Task<string> ExecuteWebRuntimeControlAsync(WebRuntimeControlRequest request, CancellationToken ct);
}

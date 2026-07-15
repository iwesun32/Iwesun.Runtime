namespace Iwesun.Runtime.WebView2;

public sealed record WebRuntimeNetworkRequest(string BackendId, string Method, string Url);
public sealed record WebRuntimeElementEvidence(string XPath, int X, int Y, int Width, int Height, string? ScreenshotPath = null);

/// <summary>Native-host bridge required to apply public WebRuntime decisions.</summary>
public interface IWebRuntimeHostAdapter
{
	Task ApplyNetworkDecisionAsync(WebRuntimeNetworkRequest request, WebRuntimeNetworkDecision decision, CancellationToken ct);
	Task<WebRuntimeElementEvidence?> CaptureXPathAsync(string xpath, CancellationToken ct);
}

/// <summary>Connects the public registries and script session to a host adapter.</summary>
public sealed class WebRuntimeHostController
{
	private readonly IWebRuntimeHostAdapter _host;
	public WebRuntimeNetworkRuleRegistry NetworkRules { get; } = new();
	public WebRuntimeMonitorFilterRegistry MonitorFilters { get; } = new();

	public WebRuntimeHostController(IWebRuntimeHostAdapter host)
	{
		_host = host ?? throw new ArgumentNullException(nameof(host));
	}

	public Task HandleNetworkRequestAsync(WebRuntimeNetworkRequest request, CancellationToken ct = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		var decision = NetworkRules.Decide(request.BackendId, request.Method, request.Url);
		return _host.ApplyNetworkDecisionAsync(request, decision, ct);
	}

	public Task<WebRuntimeElementEvidence?> CaptureXPathAsync(string xpath, CancellationToken ct = default) =>
		_host.CaptureXPathAsync(xpath, ct);
}

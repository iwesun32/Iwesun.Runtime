namespace Iwesun.Runtime.WebView2;

public sealed record WebRuntimeNetworkRequest(string BackendId, string Method, string Url);
public sealed record WebRuntimeDomNodeReference(
	long Revision,
	string DocumentScope,
	string XPath,
	int NodeId,
	int BackendNodeId);
public sealed record WebRuntimeElementEvidence(
	WebRuntimeDomNodeReference Node,
	int X,
	int Y,
	int Width,
	int Height,
	string? ScreenshotPath = null);

/// <summary>Native-host bridge required to apply public WebRuntime decisions.</summary>
public interface IWebRuntimeHostAdapter
{
	Task ApplyNetworkDecisionAsync(WebRuntimeNetworkRequest request, WebRuntimeNetworkDecision decision, CancellationToken ct);
	Task<WebRuntimeElementEvidence?> CaptureNodeAsync(
		WebRuntimeDomNodeReference node,
		CancellationToken ct);
}

/// <summary>Connects the public registries and script session to a host adapter.</summary>
public sealed class WebRuntimeHostController
{
	private readonly IWebRuntimeHostAdapter _host;
	private readonly WebRuntimeTrackedCdpDomTreeSession? _domTree;
	private WebRuntimeNetworkEvidenceSession? _networkEvidenceSession;
	public WebRuntimeNetworkRuleRegistry NetworkRules { get; } = new();
	public WebRuntimeMonitorFilterRegistry MonitorFilters { get; } = new();

	public WebRuntimeHostController(
		IWebRuntimeHostAdapter host,
		WebRuntimeTrackedCdpDomTreeSession? domTree = null)
	{
		_host = host ?? throw new ArgumentNullException(nameof(host));
		_domTree = domTree;
	}

	public Task HandleNetworkRequestAsync(WebRuntimeNetworkRequest request, CancellationToken ct = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		var decision = NetworkRules.Decide(request.BackendId, request.Method, request.Url);
		return _host.ApplyNetworkDecisionAsync(request, decision, ct);
	}

	public async Task<WebRuntimeElementEvidence?> CaptureXPathAsync(
		string xpath,
		CancellationToken ct = default)
	{
		if (_domTree is null)
		{
			throw new InvalidOperationException(
				"XPath capture requires a tracked CDP DOM tree.");
		}
		var reference = await _domTree.UseNodeAsync(
			"document",
			xpath,
			(node, _) => Task.FromResult(new WebRuntimeDomNodeReference(
				_domTree.Current!.Revision,
				node.DocumentScope,
				node.XPath,
				node.NodeId,
				node.BackendNodeId)),
			ct);
		return await _host.CaptureNodeAsync(reference, ct);
	}

	public void AttachNetworkEvidenceSession(WebRuntimeNetworkEvidenceSession session) =>
		_networkEvidenceSession = session ?? throw new ArgumentNullException(nameof(session));

	public bool DetachNetworkEvidenceSession(WebRuntimeNetworkEvidenceSession session)
	{
		ArgumentNullException.ThrowIfNull(session);
		if (!ReferenceEquals(_networkEvidenceSession, session)) return false;
		_networkEvidenceSession = null;
		return true;
	}

	public Task<WebRuntimeNetworkEvidenceDispatchResult> TryExecuteNetworkEvidenceAsync(
		WebRuntimeControlRequest request,
		CancellationToken ct = default) =>
		_networkEvidenceSession is null
			? Task.FromResult(WebRuntimeNetworkEvidenceDispatchResult.Failed(
				"NETWORK_EVIDENCE_SESSION_NOT_ATTACHED",
				"The host has not attached a WebRuntimeNetworkEvidenceSession."))
			: WebRuntimeNetworkEvidenceCommandDispatcher.TryExecuteAsync(_networkEvidenceSession, request, ct);
}

using Iwesun.Runtime.WebView2;
using Microsoft.Web.WebView2.Core;
using System.IO;

namespace Iwesun.Runtime.WebView2.SampleHost;

public partial class MainWindow : System.Windows.Window
{
    private RuntimeSampleAdapter? _adapter;
    private WebRuntimeNetworkEvidenceSession? _networkEvidence;
    public MainWindow() { InitializeComponent(); Loaded += OnLoaded; Closed += OnClosedAsync; }
    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        try
        {
            await Browser.EnsureCoreWebView2Async();
            _adapter = new RuntimeSampleAdapter(Browser.CoreWebView2);
            await _adapter.AttachAsync();
            var evidenceDirectory = Environment.GetEnvironmentVariable("IWESUN_WEBVIEW2_EVIDENCE_DIRECTORY");
            if (!string.IsNullOrWhiteSpace(evidenceDirectory))
            {
                _networkEvidence = await WebRuntimeNetworkEvidenceSession.StartAsync(
                    Browser.CoreWebView2,
                    Path.Combine(evidenceDirectory, "session"),
                    new WebRuntimeNetworkEvidenceOptions
                    {
                        SharedStoreDirectory = Path.Combine(evidenceDirectory, "shared-http"),
                        BodyCapturePolicy = new()
                        {
                            Kinds = WebRuntimeNetworkBodyKinds.PageReconstruction
                        }
                    });
                _adapter.AttachNetworkEvidenceSession(_networkEvidence);
            }
            Browser.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            Browser.CoreWebView2.Navigate("https://www.doubao.com/chat/");
            Status.Text = "WebView2 ready; navigating...";
        }
        catch (Exception ex) { Status.Text = $"WebView2 initialization failed: {ex.Message}"; }
    }
    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess || _adapter is null) return;
        try
        {
            var result = await _adapter.EvaluateAsync("JSON.stringify({url:location.href,title:document.title})");
            Status.Text = result.Success
                ? _networkEvidence is null
                    ? "script.evaluate succeeded; HTTP evidence is disabled."
                    : $"script.evaluate succeeded; HTTP bodies: {_networkEvidence.GetStatus().CapturedBodyCount}."
                : $"script.evaluate failed: {result.ErrorCode} {result.Error}";
        }
        catch (Exception ex)
        {
            Status.Text = $"script.evaluate failed: {ex.Message}";
        }
    }
    private async void OnClosedAsync(object? sender, EventArgs e)
    {
        if (_adapter is not null)
            await _adapter.DisposeAsync();
        if (_networkEvidence is not null)
            await _networkEvidence.DisposeAsync();
    }
}

internal sealed class RuntimeSampleAdapter : IAsyncDisposable
{
    private readonly CoreWebView2 _core;
    private readonly CoreWebView2DevToolsSession _devTools;
    private readonly WebRuntimeTrackedCdpDomTreeSession _domTree;
    private readonly WebRuntimeScriptAuditLog _audit = new();
    private readonly WebRuntimeHostController _controller;
    public RuntimeSampleAdapter(CoreWebView2 core)
    {
        _core = core;
        _devTools = new CoreWebView2DevToolsSession(core);
        _domTree = new WebRuntimeTrackedCdpDomTreeSession(_devTools);
        _controller = new WebRuntimeHostController(new SampleHostAdapter(), _domTree);
    }
    public async Task AttachAsync()
    {
        await _domTree.InitializeAsync();
        _core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        _core.WebResourceRequested += OnWebResourceRequested;
    }
    public void AttachNetworkEvidenceSession(WebRuntimeNetworkEvidenceSession session) => _controller.AttachNetworkEvidenceSession(session);
    public Task<WebRuntimeNetworkEvidenceDispatchResult> DispatchNetworkEvidenceAsync(
        WebRuntimeControlRequest request,
        CancellationToken cancellationToken = default) =>
        _controller.TryExecuteNetworkEvidenceAsync(request, cancellationToken);
    public Task<WebRuntimeScriptDispatchResult> EvaluateAsync(string script) => WebRuntimeScriptDispatcher.TryExecuteAsync(new CoreSession(_core), new WebRuntimeControlRequest { Action = "script.evaluate", Script = script }, CancellationToken.None, _audit);
    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e) { var d = _controller.NetworkRules.Decide("doubao-web", e.Request.Method, e.Request.Uri); if (d.Matched && d.Blocked) e.Response = _core.Environment.CreateWebResourceResponse(null, 403, "Blocked by Runtime rule", "content-type: text/plain"); }
    public async ValueTask DisposeAsync()
    {
        _core.WebResourceRequested -= OnWebResourceRequested;
        await _domTree.DisposeAsync();
    }
    private sealed class CoreSession(CoreWebView2 core) : IWebRuntimeScriptSession { public Task<string> EvaluateStringAsync(string expression, CancellationToken ct) => core.ExecuteScriptAsync(expression); public Task<string> EvaluateStringInFrameAsync(string sourceUrlContains, string expression, CancellationToken ct) => core.ExecuteScriptAsync(expression); }
    private sealed class SampleHostAdapter : IWebRuntimeHostAdapter { public Task ApplyNetworkDecisionAsync(WebRuntimeNetworkRequest request, WebRuntimeNetworkDecision decision, CancellationToken ct) => Task.CompletedTask; public Task<WebRuntimeElementEvidence?> CaptureNodeAsync(WebRuntimeDomNodeReference node, CancellationToken ct) => Task.FromResult<WebRuntimeElementEvidence?>(null); }
}

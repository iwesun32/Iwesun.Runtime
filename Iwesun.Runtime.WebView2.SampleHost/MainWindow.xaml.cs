using Iwesun.Runtime.WebView2;
using Microsoft.Web.WebView2.Core;

namespace Iwesun.Runtime.WebView2.SampleHost;

public partial class MainWindow : System.Windows.Window
{
    private RuntimeSampleAdapter? _adapter;
    public MainWindow() { InitializeComponent(); Loaded += OnLoaded; }
    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        try
        {
            await Browser.EnsureCoreWebView2Async();
            _adapter = new RuntimeSampleAdapter(Browser.CoreWebView2);
            _adapter.Attach();
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
                ? "script.evaluate succeeded."
                : $"script.evaluate failed: {result.ErrorCode} {result.Error}";
        }
        catch (Exception ex)
        {
            Status.Text = $"script.evaluate failed: {ex.Message}";
        }
    }
    protected override void OnClosed(EventArgs e) { _adapter?.Dispose(); base.OnClosed(e); }
}

internal sealed class RuntimeSampleAdapter : IDisposable
{
    private readonly CoreWebView2 _core;
    private readonly WebRuntimeScriptAuditLog _audit = new();
    private readonly WebRuntimeHostController _controller;
    public RuntimeSampleAdapter(CoreWebView2 core) { _core = core; _controller = new WebRuntimeHostController(new SampleHostAdapter()); }
    public void Attach() { _core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All); _core.WebResourceRequested += OnWebResourceRequested; }
    public Task<WebRuntimeScriptDispatchResult> EvaluateAsync(string script) => WebRuntimeScriptDispatcher.TryExecuteAsync(new CoreSession(_core), new WebRuntimeControlRequest { Action = "script.evaluate", Script = script }, CancellationToken.None, _audit);
    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e) { var d = _controller.NetworkRules.Decide("doubao-web", e.Request.Method, e.Request.Uri); if (d.Matched && d.Blocked) e.Response = _core.Environment.CreateWebResourceResponse(null, 403, "Blocked by Runtime rule", "content-type: text/plain"); }
    public void Dispose() => _core.WebResourceRequested -= OnWebResourceRequested;
    private sealed class CoreSession(CoreWebView2 core) : IWebRuntimeScriptSession { public Task<string> EvaluateStringAsync(string expression, CancellationToken ct) => core.ExecuteScriptAsync(expression); public Task<string> EvaluateStringInFrameAsync(string sourceUrlContains, string expression, CancellationToken ct) => core.ExecuteScriptAsync(expression); }
    private sealed class SampleHostAdapter : IWebRuntimeHostAdapter { public Task ApplyNetworkDecisionAsync(WebRuntimeNetworkRequest request, WebRuntimeNetworkDecision decision, CancellationToken ct) => Task.CompletedTask; public Task<WebRuntimeElementEvidence?> CaptureXPathAsync(string xpath, CancellationToken ct) => Task.FromResult<WebRuntimeElementEvidence?>(null); }
}

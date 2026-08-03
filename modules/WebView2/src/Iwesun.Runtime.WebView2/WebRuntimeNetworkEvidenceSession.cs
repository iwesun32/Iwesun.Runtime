using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Web.WebView2.Core;

namespace Iwesun.Runtime.WebView2;

public readonly record struct WebRuntimeNetworkEvidenceCheckpoint(int ResponseSequence, DateTimeOffset CapturedAt);

[Flags]
[JsonConverter(typeof(JsonStringEnumConverter<WebRuntimeNetworkBodyKinds>))]
public enum WebRuntimeNetworkBodyKinds
{
    None = 0,
    Text = 1,
    StructuredData = 2,
    Documents = 4,
    Images = 8,
    Fonts = 16,
    PageReconstruction = Text | StructuredData | Documents | Images | Fonts
}

[JsonConverter(typeof(JsonStringEnumConverter<WebRuntimeNetworkBodyDisposition>))]
public enum WebRuntimeNetworkBodyDisposition
{
    Captured,
    SkippedByPolicy,
    CaptureFailed
}

public sealed record WebRuntimeNetworkBodyCapturePolicy
{
    public WebRuntimeNetworkBodyKinds Kinds { get; init; } = WebRuntimeNetworkBodyKinds.PageReconstruction;
    public IReadOnlyCollection<string> AdditionalContentTypes { get; init; } = Array.Empty<string>();
    public IReadOnlyCollection<string> AdditionalExtensions { get; init; } = Array.Empty<string>();
}

public sealed record WebRuntimeNetworkEvidenceOptions
{
    public long MaximumBodyBytes { get; init; } = 128 * 1024 * 1024;
    public TimeSpan PendingBodyTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public string? SharedStoreDirectory { get; init; }
    public bool ClearHttpCachesOnStart { get; init; }
    public bool DisableHttpCacheDuringSession { get; init; }
    public bool BypassServiceWorkerDuringSession { get; init; }
    public bool PreserveSharedStoreDuringSession { get; init; } = true;
    public WebRuntimeNetworkBodyCapturePolicy BodyCapturePolicy { get; init; } = new();
}

public sealed record WebRuntimeNetworkEvidenceExportOptions
{
    public required string SharedStoreDirectory { get; init; }
    public required string ReferenceId { get; init; }
    public IReadOnlyCollection<string>? LinkedResourceUrls { get; init; }
    public string? ResourceApplicationSnapshot { get; init; }
    public string? RequestDomApplicationSnapshot { get; init; }
}

public sealed record WebRuntimeNetworkEvidenceStatus(
    int ObservedResponseCount,
    int CompletedResponseCount,
    int CapturedBodyCount,
    long CapturedBodyBytes,
    int FailureCount,
    int ActiveBodyCopyCount,
    int NetworkEventCount,
    int SkippedBodyCount,
    string? SharedStoreDirectory,
    WebRuntimeNetworkBodyCapturePolicy BodyCapturePolicy,
    bool ClearHttpCachesOnStart,
    bool DisableHttpCacheDuringSession,
    bool BypassServiceWorkerDuringSession,
    bool PreserveSharedStoreDuringSession,
    string? LatestFailure,
    DateTimeOffset LastActivityAt,
    double QuietDurationMilliseconds);

public sealed record WebRuntimeNetworkEvidenceFailure(
    int Sequence,
    string Url,
    int Status,
    string ContentType,
    string Error);

public sealed class WebRuntimeNetworkEvidenceSession : IAsyncDisposable
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _sessionDirectory;
    private readonly WebRuntimeNetworkEvidenceOptions _options;
    private readonly ConcurrentQueue<ResponseRecord> _responses = new();
    private readonly ConcurrentQueue<EventRecord> _events = new();
    private readonly ConcurrentQueue<CaptureFailure> _failures = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<string>> _responseRequestIds = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _finishedRequestIds = new(StringComparer.Ordinal);
    private CoreWebView2? _browser;
    private CoreWebView2DevToolsProtocolEventReceiver? _requestReceiver;
    private CoreWebView2DevToolsProtocolEventReceiver? _responseReceiver;
    private CoreWebView2DevToolsProtocolEventReceiver? _loadingFinishedReceiver;
    private int _responseSequence;
    private int _activeCopies;
    private int _capturedBodyCount;
    private int _skippedBodyCount;
    private long _capturedBodyBytes;
    private string? _latestFailure;
    private long _lastNetworkActivityUtcTicks = DateTime.UtcNow.Ticks;
    private SynchronizationContext? _browserContext;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private bool _networkEnabled;
    private bool _cacheDisabled;
    private bool _serviceWorkerBypassed;

    private WebRuntimeNetworkEvidenceSession(string sessionDirectory, WebRuntimeNetworkEvidenceOptions options)
    {
        _sessionDirectory = Path.GetFullPath(sessionDirectory);
        _options = options;
        Directory.CreateDirectory(Path.Combine(_sessionDirectory, "bodies"));
    }

    public static async Task<WebRuntimeNetworkEvidenceSession> StartAsync(
        CoreWebView2 browser,
        string sessionDirectory,
        WebRuntimeNetworkEvidenceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionDirectory);
        if (options?.MaximumBodyBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaximumBodyBytes must be positive.");
        var session = new WebRuntimeNetworkEvidenceSession(
            sessionDirectory,
            options ?? new WebRuntimeNetworkEvidenceOptions());
        try
        {
            await session.AttachAsync(browser).ConfigureAwait(false);
            return session;
        }
        catch (Exception startException)
        {
            try
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException(
                    "WebRuntime network evidence startup failed and rollback reported errors.",
                    startException,
                    cleanupException);
            }
            throw;
        }
    }

    public WebRuntimeNetworkEvidenceCheckpoint CreateCheckpoint() =>
        new(Volatile.Read(ref _responseSequence), DateTimeOffset.UtcNow);

    public IReadOnlyList<WebRuntimeNetworkEvidenceFailure> GetFailures(int limit = 20)
    {
        if (limit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(limit), "Failure limit must be between 1 and 100.");
        return _failures
            .Reverse()
            .Take(limit)
            .Select(failure => new WebRuntimeNetworkEvidenceFailure(
                failure.Sequence,
                failure.Url,
                failure.Status,
                failure.ContentType,
                failure.Error))
            .ToArray();
    }

    public WebRuntimeNetworkEvidenceStatus GetStatus()
    {
        var lastActivityAt = new DateTimeOffset(
            Volatile.Read(ref _lastNetworkActivityUtcTicks),
            TimeSpan.Zero);
        return new(
            Volatile.Read(ref _responseSequence),
            _responses.Count,
            Volatile.Read(ref _capturedBodyCount),
            Interlocked.Read(ref _capturedBodyBytes),
            _failures.Count,
            Volatile.Read(ref _activeCopies),
            _events.Count,
            Volatile.Read(ref _skippedBodyCount),
            _options.SharedStoreDirectory,
            _options.BodyCapturePolicy,
            _options.ClearHttpCachesOnStart,
            _options.DisableHttpCacheDuringSession,
            _options.BypassServiceWorkerDuringSession,
            _options.PreserveSharedStoreDuringSession,
            Volatile.Read(ref _latestFailure),
            lastActivityAt,
            Math.Max(0, (DateTimeOffset.UtcNow - lastActivityAt).TotalMilliseconds));
    }

    public async Task ExportAsync(
        string outputDirectory,
        WebRuntimeNetworkEvidenceCheckpoint after,
        CancellationToken cancellationToken = default)
    {
        await ExportCoreAsync(outputDirectory, after, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task ExportAsync(
        string outputDirectory,
        WebRuntimeNetworkEvidenceCheckpoint after,
        WebRuntimeNetworkEvidenceExportOptions exportOptions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exportOptions);
        await ExportCoreAsync(outputDirectory, after, exportOptions, cancellationToken).ConfigureAwait(false);
    }

    private async Task ExportCoreAsync(
        string outputDirectory,
        WebRuntimeNetworkEvidenceCheckpoint after,
        WebRuntimeNetworkEvidenceExportOptions? exportOptions,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        await WaitForPendingBodiesAsync(_options.PendingBodyTimeout, cancellationToken).ConfigureAwait(false);
        var failures = _failures.Where(item => item.Sequence > after.ResponseSequence).ToArray();

        var destination = Path.Combine(Path.GetFullPath(outputDirectory), "external-data");
        var bodiesDirectory = Path.Combine(destination, "bodies");
        Directory.CreateDirectory(destination);
        var linkedUrls = exportOptions?.LinkedResourceUrls is null
            ? null
            : new HashSet<string>(exportOptions.LinkedResourceUrls.Select(NormalizeResourceUrl), StringComparer.Ordinal);
        var responses = _responses.Where(item => linkedUrls is null
                ? item.Sequence > after.ResponseSequence
                : linkedUrls.Contains(NormalizeResourceUrl(item.Url)))
            .OrderBy(item => item.Sequence).ToArray();
        IReadOnlyList<WebRuntimeSharedHttpReference>? sharedReferences = null;
        if (exportOptions is not null)
        {
            var preservedReferences = responses.All(response => response.SharedReference is not null)
                && !string.IsNullOrWhiteSpace(_options.SharedStoreDirectory)
                && Path.GetFullPath(_options.SharedStoreDirectory).Equals(
                    Path.GetFullPath(exportOptions.SharedStoreDirectory), StringComparison.OrdinalIgnoreCase)
                ? responses.Select(response => response.SharedReference!).ToArray()
                : await WebRuntimeSharedHttpEvidenceStore.PreserveAsync(
                    exportOptions.SharedStoreDirectory,
                    responses.Select(response => new WebRuntimeSharedHttpResourceInput
                {
                    Sequence = response.Sequence,
                    CapturedAt = response.CapturedAt,
                    Method = response.Method,
                    Url = response.Url,
                    Status = response.Status,
                    ReasonPhrase = response.ReasonPhrase,
                    ContentType = response.ContentType,
                    Headers = response.Headers,
                    BodyFilePath = response.BodyFile is null
                        ? null
                        : Path.Combine(_sessionDirectory, response.BodyFile.Replace('/', Path.DirectorySeparatorChar)),
                        BodyLength = response.BodyLength,
                        BodySha256 = response.BodySha256,
                        BodyCaptureSource = response.BodyCaptureSource,
                        BodyDisposition = response.BodyDisposition
                }).ToArray(),
                cancellationToken).ConfigureAwait(false);
            sharedReferences = await WebRuntimeSharedHttpEvidenceStore.LinkAsync(
                exportOptions.SharedStoreDirectory,
                exportOptions.ReferenceId,
                preservedReferences,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            Directory.CreateDirectory(bodiesDirectory);
            foreach (var response in responses.Where(item => item.BodyFile is not null))
            {
                File.Copy(
                    Path.Combine(_sessionDirectory, response.BodyFile!.Replace('/', Path.DirectorySeparatorChar)),
                    Path.Combine(bodiesDirectory, Path.GetFileName(response.BodyFile)),
                    overwrite: true);
            }
        }

        var events = _events.Where(item => item.CapturedAt >= after.CapturedAt)
            .OrderBy(item => item.CapturedAt).ToArray();
        var manifest = sharedReferences is null
            ? JsonSerializer.Serialize(new
              {
                  schema = "iwesun.webview2.http-evidence/1.0",
                  capturedAt = DateTimeOffset.UtcNow,
                  storage = "snapshot-local",
                  responseCount = responses.Length,
                  bodyCount = responses.Count(item => item.BodyFile is not null),
                  bodyFailureCount = failures.Length,
                  bodyFailures = failures,
                  responses
              }, JsonOptions)
            : JsonSerializer.Serialize(new
              {
                  schema = "iwesun.webview2.http-evidence-references/1.0",
                  capturedAt = DateTimeOffset.UtcNow,
                  storage = "shared-content-addressed",
                  referenceId = exportOptions!.ReferenceId,
                  sharedStore = Path.GetRelativePath(destination, Path.GetFullPath(exportOptions.SharedStoreDirectory)).Replace('\\', '/'),
                  responseCount = sharedReferences.Count,
                  bodyCount = sharedReferences.Count(item => item.BodySha256 is not null),
                  bodyFailureCount = failures.Length,
                  bodyFailures = failures,
                  applications = CreateResourceApplications(
                      sharedReferences,
                      exportOptions.ResourceApplicationSnapshot,
                      exportOptions.RequestDomApplicationSnapshot),
                  resources = sharedReferences
              }, JsonOptions);
        await File.WriteAllTextAsync(Path.Combine(destination, "response-manifest.json"),
            manifest, Utf8NoBom, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(destination, "request-events.json"),
            JsonSerializer.Serialize(new
            {
                schema = "iwesun.webview2.network-event-log/1.0",
                capturedAt = DateTimeOffset.UtcNow,
                events
            }, JsonOptions), Utf8NoBom, cancellationToken).ConfigureAwait(false);
    }

    public async Task WaitForPendingBodiesAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (Volatile.Read(ref _activeCopies) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DateTimeOffset.UtcNow >= deadline)
                throw new TimeoutException("Timed out while preserving HTTP response bodies.");
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task AttachAsync(CoreWebView2 browser)
    {
        _browserContext = SynchronizationContext.Current
            ?? throw new InvalidOperationException("WebRuntimeNetworkEvidenceSession must start on the WebView2 UI thread.");
        _browser = browser;
        browser.WebResourceResponseReceived += OnResponse;
        _requestReceiver = browser.GetDevToolsProtocolEventReceiver("Network.requestWillBeSent");
        _responseReceiver = browser.GetDevToolsProtocolEventReceiver("Network.responseReceived");
        _loadingFinishedReceiver = browser.GetDevToolsProtocolEventReceiver("Network.loadingFinished");
        _requestReceiver.DevToolsProtocolEventReceived += OnRequestEvent;
        _responseReceiver.DevToolsProtocolEventReceived += OnResponseEvent;
        _loadingFinishedReceiver.DevToolsProtocolEventReceived += OnLoadingFinishedEvent;
        if (_options.ClearHttpCachesOnStart)
        {
            await browser.Profile.ClearBrowsingDataAsync(
                CoreWebView2BrowsingDataKinds.DiskCache
                | CoreWebView2BrowsingDataKinds.CacheStorage);
        }
        await browser.CallDevToolsProtocolMethodAsync("Network.enable",
            "{\"maxTotalBufferSize\":134217728,\"maxResourceBufferSize\":134217728,\"maxPostDataSize\":134217728}");
        _networkEnabled = true;
        if (_options.DisableHttpCacheDuringSession)
        {
            await browser.CallDevToolsProtocolMethodAsync(
                "Network.setCacheDisabled",
                "{\"cacheDisabled\":true}");
            _cacheDisabled = true;
        }
        if (_options.BypassServiceWorkerDuringSession)
        {
            await browser.CallDevToolsProtocolMethodAsync(
                "Network.setBypassServiceWorker",
                "{\"bypass\":true}");
            _serviceWorkerBypassed = true;
        }
    }

    private async void OnResponse(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs args)
    {
        MarkNetworkActivity();
        Interlocked.Increment(ref _activeCopies);
        var sequence = Interlocked.Increment(ref _responseSequence);
        var requestUrl = string.Empty;
        try
        {
            requestUrl = args.Request.Uri;
            var requestMethod = args.Request.Method;
            var statusCode = args.Response.StatusCode;
            var reasonPhrase = args.Response.ReasonPhrase;
            var headers = ReadSafeHeaders(args.Response.Headers);
            headers.TryGetValue("Content-Type", out var contentType);
            var cdpRequestId = await DequeueResponseRequestIdAsync(requestUrl);
            string? bodyFile = null;
            string? sha256 = null;
            long bodyLength = 0;
            var bodyCaptureSource = string.Empty;
            var bodyDisposition = WebRuntimeNetworkBodyDisposition.SkippedByPolicy;
            if (ShouldCaptureBody(requestUrl, contentType ?? string.Empty, _options.BodyCapturePolicy))
            {
                bodyFile = $"bodies/{sequence:D8}{SelectExtension(requestUrl, contentType ?? string.Empty)}";
                var path = Path.Combine(_sessionDirectory, bodyFile.Replace('/', Path.DirectorySeparatorChar));
                try
                {
                    try
                    {
                        await using var input = await args.Response.GetContentAsync().ConfigureAwait(false);
                        await using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
                        bodyLength = await CopyBodyAsync(input, output, _options.MaximumBodyBytes).ConfigureAwait(false);
                        await output.FlushAsync().ConfigureAwait(false);
                        bodyCaptureSource = "web-resource-response";
                    }
                    catch (Exception primaryException)
                    {
                        bodyLength = await CaptureBodyViaCdpAsync(
                            requestUrl,
                            path,
                            cdpRequestId,
                            primaryException).ConfigureAwait(false);
                        bodyCaptureSource = "cdp-network.getResponseBody";
                    }
                    await using var hashInput = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    sha256 = Convert.ToHexString(await SHA256.HashDataAsync(hashInput).ConfigureAwait(false)).ToLowerInvariant();
                    Interlocked.Increment(ref _capturedBodyCount);
                    Interlocked.Add(ref _capturedBodyBytes, bodyLength);
                    bodyDisposition = WebRuntimeNetworkBodyDisposition.Captured;
                }
                catch (Exception exception)
                {
                    RecordFailure(sequence, requestUrl, exception.Message, statusCode, contentType ?? string.Empty);
                    bodyDisposition = WebRuntimeNetworkBodyDisposition.CaptureFailed;
                    bodyFile = null;
                    bodyLength = 0;
                    sha256 = null;
                    bodyCaptureSource = string.Empty;
                    try { File.Delete(path); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            else
            {
                Interlocked.Increment(ref _skippedBodyCount);
            }
            WebRuntimeSharedHttpReference? sharedReference = null;
            if (_options.PreserveSharedStoreDuringSession
                && !string.IsNullOrWhiteSpace(_options.SharedStoreDirectory))
            {
                var preserved = await WebRuntimeSharedHttpEvidenceStore.PreserveAsync(
                    _options.SharedStoreDirectory,
                    [new WebRuntimeSharedHttpResourceInput
                    {
                        Sequence = sequence,
                        CapturedAt = DateTimeOffset.UtcNow,
                        Method = requestMethod,
                        Url = requestUrl,
                        Status = statusCode,
                        ReasonPhrase = reasonPhrase,
                        ContentType = contentType ?? string.Empty,
                        Headers = headers,
                        BodyFilePath = bodyFile is null ? null : Path.Combine(_sessionDirectory, bodyFile.Replace('/', Path.DirectorySeparatorChar)),
                        BodyLength = bodyLength,
                        BodySha256 = sha256,
                        BodyCaptureSource = bodyCaptureSource,
                        BodyDisposition = bodyDisposition
                    }]).ConfigureAwait(false);
                sharedReference = preserved[0];
            }
            _responses.Enqueue(new(sequence, DateTimeOffset.UtcNow, requestMethod, requestUrl,
                statusCode, reasonPhrase, contentType ?? string.Empty, headers,
                bodyFile, bodyLength, sha256, bodyDisposition, sharedReference, bodyCaptureSource));
        }
        catch (Exception exception)
        {
            RecordFailure(sequence, requestUrl, exception.Message);
        }
        finally
        {
            Interlocked.Decrement(ref _activeCopies);
            MarkNetworkActivity();
        }
    }

    private void OnRequestEvent(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs args) =>
        CaptureEvent("requestWillBeSent", args.ParameterObjectAsJson);

    private void OnResponseEvent(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs args) =>
        CaptureEvent("responseReceived", args.ParameterObjectAsJson);

    private void OnLoadingFinishedEvent(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs args) =>
        CaptureEvent("loadingFinished", args.ParameterObjectAsJson);

    private void CaptureEvent(string kind, string json)
    {
        MarkNetworkActivity();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (kind.Equals("responseReceived", StringComparison.Ordinal)
                && document.RootElement.TryGetProperty("response", out var response))
            {
                var url = ReadJsonString(response, "url");
                var requestId = ReadJsonString(document.RootElement, "requestId");
                if (!string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(requestId))
                    _responseRequestIds
                        .GetOrAdd(NormalizeResourceUrl(url), static _ => new())
                        .Enqueue(requestId);
            }
            else if (kind.Equals("loadingFinished", StringComparison.Ordinal))
            {
                var requestId = ReadJsonString(document.RootElement, "requestId");
                if (!string.IsNullOrWhiteSpace(requestId))
                    _finishedRequestIds[requestId] = 0;
            }
            _events.Enqueue(new(DateTimeOffset.UtcNow, kind, SanitizeNetworkPayload(document.RootElement)));
        }
        catch (JsonException)
        {
        }
    }

    private void MarkNetworkActivity() =>
        Interlocked.Exchange(ref _lastNetworkActivityUtcTicks, DateTime.UtcNow.Ticks);

    private async Task<string?> DequeueResponseRequestIdAsync(string url)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (_responseRequestIds.TryGetValue(NormalizeResourceUrl(url), out var requestIds)
                && requestIds.TryDequeue(out var requestId))
                return requestId;
            await Task.Delay(25).ConfigureAwait(false);
        }
        return null;
    }

    private async Task<long> CaptureBodyViaCdpAsync(
        string url,
        string path,
        string? requestId,
        Exception primaryException)
    {
        if (string.IsNullOrWhiteSpace(requestId))
            throw new InvalidOperationException(
                $"WebResource response body failed ({primaryException.Message}); no matching CDP requestId was observed.",
                primaryException);

        for (var attempt = 0; attempt < 200 && !_finishedRequestIds.ContainsKey(requestId); attempt++)
            await Task.Delay(50).ConfigureAwait(false);
        if (!_finishedRequestIds.ContainsKey(requestId))
            throw new InvalidOperationException(
                $"WebResource response body failed ({primaryException.Message}); "
                + $"CDP request {requestId} did not reach Network.loadingFinished.",
                primaryException);

        string? result = null;
        Exception? cdpException = null;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            try
            {
                result = await CallBrowserMethodAsync(
                    "Network.getResponseBody",
                    JsonSerializer.Serialize(new { requestId }, JsonOptions)).ConfigureAwait(false);
                break;
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                cdpException = exception;
                await Task.Delay(50).ConfigureAwait(false);
            }
        }
        if (result is null)
            throw new InvalidOperationException(
                $"WebResource response body failed ({primaryException.Message}); "
                + $"CDP body was not ready after retries ({cdpException?.Message}).",
                cdpException ?? primaryException);
        using var document = JsonDocument.Parse(result);
        var body = ReadJsonString(document.RootElement, "body");
        var base64Encoded = document.RootElement.TryGetProperty("base64Encoded", out var encoded)
            && encoded.ValueKind == JsonValueKind.True;
        byte[] bytes;
        try
        {
            bytes = base64Encoded ? Convert.FromBase64String(body) : Encoding.UTF8.GetBytes(body);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("CDP response body is not valid base64.", exception);
        }
        if (bytes.LongLength > _options.MaximumBodyBytes)
            throw new InvalidDataException($"Response body exceeded {_options.MaximumBodyBytes} bytes.");
        await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
        return bytes.LongLength;
    }

    private Task<string> CallBrowserMethodAsync(string method, string parameters)
    {
        var browser = _browser ?? throw new ObjectDisposedException(nameof(WebRuntimeNetworkEvidenceSession));
        var context = _browserContext
            ?? throw new InvalidOperationException("The WebView2 UI synchronization context is unavailable.");
        if (ReferenceEquals(SynchronizationContext.Current, context))
            return browser.CallDevToolsProtocolMethodAsync(method, parameters);

        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Post(async _ =>
        {
            try
            {
                completion.SetResult(await browser.CallDevToolsProtocolMethodAsync(method, parameters));
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        }, null);
        return completion.Task;
    }

    private Task RunOnBrowserContextAsync(Func<Task> action)
    {
        var context = _browserContext
            ?? throw new InvalidOperationException("The WebView2 UI synchronization context is unavailable.");
        if (ReferenceEquals(SynchronizationContext.Current, context))
            return action();

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Post(async _ =>
        {
            try
            {
                await action().ConfigureAwait(false);
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        }, null);
        return completion.Task;
    }

    private void RecordFailure(
        int sequence,
        string url,
        string error,
        int status = 0,
        string contentType = "")
    {
        _failures.Enqueue(new(sequence, url, status, contentType, error));
        Volatile.Write(ref _latestFailure, $"{url}: {error}");
    }

    private static JsonElement SanitizeNetworkPayload(JsonElement payload)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteSanitizedJson(writer, payload, null);
        return JsonDocument.Parse(stream.ToArray()).RootElement.Clone();
    }

    private static void WriteSanitizedJson(Utf8JsonWriter writer, JsonElement value, string? propertyName)
    {
        if (propertyName is not null && IsSensitiveHeader(propertyName))
        {
            writer.WriteStringValue("[redacted]");
            return;
        }
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    WriteSanitizedJson(writer, property.Value, property.Name);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) WriteSanitizedJson(writer, item, propertyName);
                writer.WriteEndArray();
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }

    private static Dictionary<string, string> ReadSafeHeaders(CoreWebView2HttpResponseHeaders headers)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var iterator = headers.GetIterator();
        while (iterator.HasCurrentHeader)
        {
            var current = iterator.Current;
            if (!IsSensitiveHeader(current.Key)) result[current.Key] = current.Value;
            iterator.MoveNext();
        }
        return result;
    }

    private static bool IsSensitiveHeader(string name) =>
        name.Contains("authorization", StringComparison.OrdinalIgnoreCase)
        || name.Contains("cookie", StringComparison.OrdinalIgnoreCase)
        || name.Contains("token", StringComparison.OrdinalIgnoreCase)
        || name.Contains("secret", StringComparison.OrdinalIgnoreCase)
        || name.Contains("api-key", StringComparison.OrdinalIgnoreCase)
        || name.Contains("apikey", StringComparison.OrdinalIgnoreCase)
        || name.Contains("websocket-key", StringComparison.OrdinalIgnoreCase);

    public static bool ShouldCaptureBody(
        string url,
        string contentType,
        WebRuntimeNetworkBodyCapturePolicy? policy = null)
    {
        policy ??= new WebRuntimeNetworkBodyCapturePolicy();
        var mediaType = contentType.Split(';', 2)[0].Trim();
        var extension = Path.GetExtension(Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : url);
        if (policy.AdditionalContentTypes.Contains(mediaType, StringComparer.OrdinalIgnoreCase)
            || policy.AdditionalExtensions.Select(NormalizeExtension).Contains(extension, StringComparer.OrdinalIgnoreCase))
            return true;
        if (policy.Kinds.HasFlag(WebRuntimeNetworkBodyKinds.Text)
            && (mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
                || mediaType.Contains("javascript", StringComparison.OrdinalIgnoreCase)
                || new[] { ".md", ".txt", ".html", ".htm", ".css", ".js", ".mjs" }.Contains(extension, StringComparer.OrdinalIgnoreCase)))
            return true;
        if (policy.Kinds.HasFlag(WebRuntimeNetworkBodyKinds.StructuredData)
            && (mediaType.Contains("json", StringComparison.OrdinalIgnoreCase)
                || mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase)
                || new[] { ".json", ".xml" }.Contains(extension, StringComparer.OrdinalIgnoreCase)))
            return true;
        if (policy.Kinds.HasFlag(WebRuntimeNetworkBodyKinds.Documents)
            && (mediaType.Contains("pdf", StringComparison.OrdinalIgnoreCase)
                || mediaType.Contains("msword", StringComparison.OrdinalIgnoreCase)
                || mediaType.Contains("officedocument", StringComparison.OrdinalIgnoreCase)
                || new[] { ".pdf", ".doc", ".docx" }.Contains(extension, StringComparer.OrdinalIgnoreCase)))
            return true;
        if (policy.Kinds.HasFlag(WebRuntimeNetworkBodyKinds.Images)
            && (mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                || new[] { ".svg", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".avif", ".ico", ".bmp" }.Contains(extension, StringComparer.OrdinalIgnoreCase)))
            return true;
        return policy.Kinds.HasFlag(WebRuntimeNetworkBodyKinds.Fonts)
            && (mediaType.StartsWith("font/", StringComparison.OrdinalIgnoreCase)
                || mediaType.Contains("font", StringComparison.OrdinalIgnoreCase)
                || new[] { ".woff", ".woff2", ".ttf", ".otf", ".eot" }.Contains(extension, StringComparer.OrdinalIgnoreCase));
    }

    private static string NormalizeExtension(string extension) =>
        extension.StartsWith(".", StringComparison.Ordinal) ? extension : "." + extension;

    private static async Task<long> CopyBodyAsync(Stream input, Stream output, long maximumBodyBytes)
    {
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer).ConfigureAwait(false);
            if (read == 0) return total;
            total += read;
            if (total > maximumBodyBytes)
                throw new InvalidDataException($"Response body exceeds {maximumBodyBytes} bytes.");
            await output.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
        }
    }

    private static IReadOnlyList<object> CreateResourceApplications(
        IReadOnlyList<WebRuntimeSharedHttpReference> references,
        string? resourceApplicationSnapshot,
        string? requestDomApplicationSnapshot)
    {
        using var resourceDocument = string.IsNullOrWhiteSpace(resourceApplicationSnapshot)
            ? null
            : JsonDocument.Parse(resourceApplicationSnapshot);
        using var requestDocument = string.IsNullOrWhiteSpace(requestDomApplicationSnapshot)
            ? null
            : JsonDocument.Parse(requestDomApplicationSnapshot);
        var results = new List<object>(references.Count);
        foreach (var reference in references)
        {
            var directContainers = new List<JsonElement>();
            if (resourceDocument?.RootElement.TryGetProperty("applications", out var applications) == true)
            {
                foreach (var application in applications.EnumerateArray())
                {
                    if (string.Equals(
                            NormalizeResourceUrl(ReadJsonString(application, "url")),
                            NormalizeResourceUrl(reference.Url),
                            StringComparison.Ordinal)
                        && application.TryGetProperty("renderContainer", out var container))
                        directContainers.Add(container.Clone());
                }
            }
            var candidateContainers = new List<JsonElement>();
            if (requestDocument?.RootElement.TryGetProperty("frames", out var frames) == true)
            {
                foreach (var frame in frames.EnumerateArray())
                foreach (var record in frame.GetProperty("records").EnumerateArray())
                {
                    if (!string.Equals(ReadJsonString(record, "url"), reference.Url, StringComparison.Ordinal)
                        || !record.TryGetProperty("containers", out var containers)) continue;
                    candidateContainers.AddRange(containers.EnumerateArray().Select(item => item.Clone()));
                }
            }
            var status = directContainers.Count > 0 ? "linked" : candidateContainers.Count > 0 ? "candidate" : "unknown";
            results.Add(new
            {
                reference.RecordId,
                reference.Url,
                mappingStatus = status,
                containers = directContainers.Count > 0 ? directContainers : candidateContainers,
                note = status switch
                {
                    "linked" => "The resource is directly referenced by a DOM rendering element.",
                    "candidate" => "The containers are temporal mutation candidates and require verification.",
                    _ => "No rendering container could be determined; assign it during the interpretation step."
                }
            });
        }
        return results;
    }

    private static string ReadJsonString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string NormalizeResourceUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return value;
        if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return value;
        var builder = new UriBuilder(uri) { Fragment = string.Empty };
        return builder.Uri.AbsoluteUri;
    }

    private static string SelectExtension(string url, string contentType)
    {
        if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase)) return ".json";
        if (contentType.Contains("html", StringComparison.OrdinalIgnoreCase)) return ".html";
        if (contentType.Contains("markdown", StringComparison.OrdinalIgnoreCase)) return ".md";
        if (contentType.Contains("xml", StringComparison.OrdinalIgnoreCase)) return ".xml";
        if (contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase)) return ".pdf";
        if (contentType.Contains("svg", StringComparison.OrdinalIgnoreCase)) return ".svg";
        if (contentType.Contains("png", StringComparison.OrdinalIgnoreCase)) return ".png";
        if (contentType.Contains("jpeg", StringComparison.OrdinalIgnoreCase)) return ".jpg";
        if (contentType.Contains("webp", StringComparison.OrdinalIgnoreCase)) return ".webp";
        if (contentType.Contains("avif", StringComparison.OrdinalIgnoreCase)) return ".avif";
        if (contentType.Contains("woff2", StringComparison.OrdinalIgnoreCase)) return ".woff2";
        if (contentType.Contains("woff", StringComparison.OrdinalIgnoreCase)) return ".woff";
        if (contentType.Contains("truetype", StringComparison.OrdinalIgnoreCase)) return ".ttf";
        if (contentType.Contains("opentype", StringComparison.OrdinalIgnoreCase)) return ".otf";
        if (contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)) return ".txt";
        var extension = Path.GetExtension(Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : url);
        return extension.Length is > 1 and <= 10 ? extension : ".bin";
    }

    public async ValueTask DisposeAsync()
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_browser is null) return;
            var errors = new List<Exception>();
            try
            {
                await RunOnBrowserContextAsync(() =>
                {
                    _browser.WebResourceResponseReceived -= OnResponse;
                    if (_requestReceiver is not null)
                        _requestReceiver.DevToolsProtocolEventReceived -= OnRequestEvent;
                    if (_responseReceiver is not null)
                        _responseReceiver.DevToolsProtocolEventReceived -= OnResponseEvent;
                    if (_loadingFinishedReceiver is not null)
                        _loadingFinishedReceiver.DevToolsProtocolEventReceived -= OnLoadingFinishedEvent;
                    return Task.CompletedTask;
                }).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }

            try
            {
                await WaitForPendingBodiesAsync(_options.PendingBodyTimeout, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }

            if (_serviceWorkerBypassed)
            {
                try
                {
                    await CallBrowserMethodAsync(
                        "Network.setBypassServiceWorker",
                        "{\"bypass\":false}").ConfigureAwait(false);
                    _serviceWorkerBypassed = false;
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
            }
            if (_cacheDisabled)
            {
                try
                {
                    await CallBrowserMethodAsync(
                        "Network.setCacheDisabled",
                        "{\"cacheDisabled\":false}").ConfigureAwait(false);
                    _cacheDisabled = false;
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
            }
            if (_networkEnabled)
            {
                try
                {
                    await CallBrowserMethodAsync("Network.disable", "{}").ConfigureAwait(false);
                    _networkEnabled = false;
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
            }

            _browser = null;
            _browserContext = null;
            _requestReceiver = null;
            _responseReceiver = null;
            _loadingFinishedReceiver = null;
            if (errors.Count > 0)
                throw new AggregateException("WebRuntime network evidence cleanup reported errors.", errors);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    internal sealed record ResponseRecord(int Sequence, DateTimeOffset CapturedAt, string Method, string Url,
        int Status, string ReasonPhrase, string ContentType, IReadOnlyDictionary<string, string> Headers,
        string? BodyFile, long BodyLength, string? BodySha256, WebRuntimeNetworkBodyDisposition BodyDisposition,
        WebRuntimeSharedHttpReference? SharedReference, string BodyCaptureSource);
    private sealed record EventRecord(DateTimeOffset CapturedAt, string Kind, JsonElement Payload);
    private sealed record CaptureFailure(
        int Sequence,
        string Url,
        int Status,
        string ContentType,
        string Error);
}

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Iwesun.Runtime.WebView2;

public readonly record struct WebRuntimeNetworkEvidenceCheckpoint(int ResponseSequence, DateTimeOffset CapturedAt);

public sealed record WebRuntimeNetworkEvidenceOptions
{
    public long MaximumBodyBytes { get; init; } = 128 * 1024 * 1024;
    public TimeSpan PendingBodyTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public string? SharedStoreDirectory { get; init; }
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
    string? SharedStoreDirectory,
    string? LatestFailure);

public sealed class WebRuntimeNetworkEvidenceSession : IAsyncDisposable
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _sessionDirectory;
    private readonly WebRuntimeNetworkEvidenceOptions _options;
    private readonly ConcurrentQueue<ResponseRecord> _responses = new();
    private readonly ConcurrentQueue<EventRecord> _events = new();
    private readonly ConcurrentQueue<CaptureFailure> _failures = new();
    private CoreWebView2? _browser;
    private CoreWebView2DevToolsProtocolEventReceiver? _requestReceiver;
    private CoreWebView2DevToolsProtocolEventReceiver? _responseReceiver;
    private int _responseSequence;
    private int _activeCopies;
    private int _capturedBodyCount;
    private long _capturedBodyBytes;
    private string? _latestFailure;

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
        var session = new WebRuntimeNetworkEvidenceSession(
            sessionDirectory,
            options ?? new WebRuntimeNetworkEvidenceOptions());
        await session.AttachAsync(browser).ConfigureAwait(false);
        return session;
    }

    public WebRuntimeNetworkEvidenceCheckpoint CreateCheckpoint() =>
        new(Volatile.Read(ref _responseSequence), DateTimeOffset.UtcNow);

    public WebRuntimeNetworkEvidenceStatus GetStatus() => new(
        Volatile.Read(ref _responseSequence),
        _responses.Count,
        Volatile.Read(ref _capturedBodyCount),
        Interlocked.Read(ref _capturedBodyBytes),
        _failures.Count,
        Volatile.Read(ref _activeCopies),
        _events.Count,
        _options.SharedStoreDirectory,
        Volatile.Read(ref _latestFailure));

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
                    BodySha256 = response.BodySha256
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
        _browser = browser;
        browser.WebResourceResponseReceived += OnResponse;
        _requestReceiver = browser.GetDevToolsProtocolEventReceiver("Network.requestWillBeSent");
        _responseReceiver = browser.GetDevToolsProtocolEventReceiver("Network.responseReceived");
        _requestReceiver.DevToolsProtocolEventReceived += OnRequestEvent;
        _responseReceiver.DevToolsProtocolEventReceived += OnResponseEvent;
        await browser.CallDevToolsProtocolMethodAsync("Network.enable",
            "{\"maxTotalBufferSize\":134217728,\"maxResourceBufferSize\":134217728,\"maxPostDataSize\":134217728}").ConfigureAwait(false);
    }

    private async void OnResponse(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs args)
    {
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
            string? bodyFile = null;
            string? sha256 = null;
            long bodyLength = 0;
            if (ShouldCaptureBody(requestUrl, contentType ?? string.Empty))
            {
                bodyFile = $"bodies/{sequence:D8}{SelectExtension(requestUrl, contentType ?? string.Empty)}";
                var path = Path.Combine(_sessionDirectory, bodyFile.Replace('/', Path.DirectorySeparatorChar));
                try
                {
                    await using var input = await args.Response.GetContentAsync().ConfigureAwait(false);
                    await using (var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
                    {
                        await input.CopyToAsync(output).ConfigureAwait(false);
                        bodyLength = output.Length;
                        if (bodyLength > _options.MaximumBodyBytes)
                            throw new InvalidDataException($"Response body exceeds {_options.MaximumBodyBytes} bytes.");
                        await output.FlushAsync().ConfigureAwait(false);
                    }
                    await using var hashInput = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    sha256 = Convert.ToHexString(await SHA256.HashDataAsync(hashInput).ConfigureAwait(false)).ToLowerInvariant();
                    Interlocked.Increment(ref _capturedBodyCount);
                    Interlocked.Add(ref _capturedBodyBytes, bodyLength);
                }
                catch (Exception exception)
                {
                    RecordFailure(sequence, requestUrl, exception.Message);
                    bodyFile = null;
                    bodyLength = 0;
                    sha256 = null;
                    try { File.Delete(path); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            WebRuntimeSharedHttpReference? sharedReference = null;
            if (!string.IsNullOrWhiteSpace(_options.SharedStoreDirectory))
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
                        BodySha256 = sha256
                    }]).ConfigureAwait(false);
                sharedReference = preserved[0];
            }
            _responses.Enqueue(new(sequence, DateTimeOffset.UtcNow, requestMethod, requestUrl,
                statusCode, reasonPhrase, contentType ?? string.Empty, headers,
                bodyFile, bodyLength, sha256, sharedReference));
        }
        catch (Exception exception)
        {
            RecordFailure(sequence, requestUrl, exception.Message);
        }
        finally
        {
            Interlocked.Decrement(ref _activeCopies);
        }
    }

    private void OnRequestEvent(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs args) =>
        CaptureEvent("requestWillBeSent", args.ParameterObjectAsJson);

    private void OnResponseEvent(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs args) =>
        CaptureEvent("responseReceived", args.ParameterObjectAsJson);

    private void CaptureEvent(string kind, string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            _events.Enqueue(new(DateTimeOffset.UtcNow, kind, SanitizeNetworkPayload(document.RootElement)));
        }
        catch (JsonException)
        {
        }
    }

    private void RecordFailure(int sequence, string url, string error)
    {
        _failures.Enqueue(new(sequence, url, error));
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

    private static bool ShouldCaptureBody(string url, string contentType)
    {
        if (contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("json", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("xml", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("msword", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("officedocument", StringComparison.OrdinalIgnoreCase)) return true;
        var extension = Path.GetExtension(Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : url);
        return new[] { ".json", ".md", ".txt", ".html", ".xml", ".pdf", ".doc", ".docx" }
            .Contains(extension, StringComparer.OrdinalIgnoreCase);
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
                    if (string.Equals(ReadJsonString(application, "url"), reference.Url, StringComparison.Ordinal)
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
        if (contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)) return ".txt";
        var extension = Path.GetExtension(Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : url);
        return extension.Length is > 1 and <= 10 ? extension : ".bin";
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is null) return;
        _browser.WebResourceResponseReceived -= OnResponse;
        if (_requestReceiver is not null) _requestReceiver.DevToolsProtocolEventReceived -= OnRequestEvent;
        if (_responseReceiver is not null) _responseReceiver.DevToolsProtocolEventReceived -= OnResponseEvent;
        try { await _browser.CallDevToolsProtocolMethodAsync("Network.disable", "{}").ConfigureAwait(false); }
        catch (ArgumentException) { }
        _browser = null;
    }

    internal sealed record ResponseRecord(int Sequence, DateTimeOffset CapturedAt, string Method, string Url,
        int Status, string ReasonPhrase, string ContentType, IReadOnlyDictionary<string, string> Headers,
        string? BodyFile, long BodyLength, string? BodySha256, WebRuntimeSharedHttpReference? SharedReference);
    private sealed record EventRecord(DateTimeOffset CapturedAt, string Kind, JsonElement Payload);
    private sealed record CaptureFailure(int Sequence, string Url, string Error);
}

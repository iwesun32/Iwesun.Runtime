using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Iwesun.Runtime.WebView2;

public sealed record WebRuntimeSharedHttpReference(
    int Sequence,
    string RecordId,
    string Method,
    string Url,
    int Status,
    string ContentType,
    long BodyLength,
    string? BodySha256,
    string? SharedBodyFile);

public sealed record WebRuntimeSharedHttpResourceInput
{
    public int Sequence { get; init; }
    public DateTimeOffset CapturedAt { get; init; }
    public required string Method { get; init; }
    public required string Url { get; init; }
    public int Status { get; init; }
    public string ReasonPhrase { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public string? BodyFilePath { get; init; }
    public long BodyLength { get; init; }
    public string? BodySha256 { get; init; }
}

public static class WebRuntimeSharedHttpEvidenceStore
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<IReadOnlyList<WebRuntimeSharedHttpReference>> RegisterAsync(
        string storeDirectory,
        string referenceId,
        IReadOnlyList<WebRuntimeSharedHttpResourceInput> responses,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceId);
        return await PreserveCoreAsync(storeDirectory, referenceId, responses, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<IReadOnlyList<WebRuntimeSharedHttpReference>> PreserveAsync(
        string storeDirectory,
        IReadOnlyList<WebRuntimeSharedHttpResourceInput> responses,
        CancellationToken cancellationToken = default) =>
        await PreserveCoreAsync(storeDirectory, null, responses, cancellationToken).ConfigureAwait(false);

    public static async Task<IReadOnlyList<WebRuntimeSharedHttpReference>> LinkAsync(
        string storeDirectory,
        string referenceId,
        IReadOnlyList<WebRuntimeSharedHttpReference> references,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceId);
        var store = Path.GetFullPath(storeDirectory);
        await using var storeLock = await AcquireLockAsync(store, cancellationToken).ConfigureAwait(false);
        var catalogPath = Path.Combine(store, "catalog.json");
        var catalog = File.Exists(catalogPath)
            ? JsonSerializer.Deserialize<SharedCatalog>(await File.ReadAllTextAsync(catalogPath, cancellationToken).ConfigureAwait(false), JsonOptions)
              ?? new SharedCatalog()
            : new SharedCatalog();
        foreach (var reference in references)
        {
            var entry = catalog.Resources.FirstOrDefault(item => item.RecordId.Equals(reference.RecordId, StringComparison.Ordinal))
                ?? throw new InvalidDataException($"Shared HTTP resource is not preserved: {reference.RecordId}");
            if (!entry.References.Contains(referenceId, StringComparer.Ordinal))
                entry.References.Add(referenceId);
            entry.References.Sort(StringComparer.Ordinal);
        }
        catalog.UpdatedAt = DateTimeOffset.UtcNow;
        await WriteAtomicAsync(catalogPath, JsonSerializer.Serialize(catalog, JsonOptions), cancellationToken).ConfigureAwait(false);
        return references;
    }

    private static async Task<IReadOnlyList<WebRuntimeSharedHttpReference>> PreserveCoreAsync(
        string storeDirectory,
        string? referenceId,
        IReadOnlyList<WebRuntimeSharedHttpResourceInput> responses,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeDirectory);
        var store = Path.GetFullPath(storeDirectory);
        var bodiesDirectory = Path.Combine(store, "bodies");
        var recordsDirectory = Path.Combine(store, "records");
        Directory.CreateDirectory(bodiesDirectory);
        Directory.CreateDirectory(recordsDirectory);

        await using var storeLock = await AcquireLockAsync(store, cancellationToken).ConfigureAwait(false);
        var catalogPath = Path.Combine(store, "catalog.json");
        var catalog = File.Exists(catalogPath)
            ? JsonSerializer.Deserialize<SharedCatalog>(await File.ReadAllTextAsync(catalogPath, cancellationToken).ConfigureAwait(false), JsonOptions)
              ?? new SharedCatalog()
            : new SharedCatalog();
        if (!catalog.Schema.Equals(SharedCatalog.SchemaValue, StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported shared HTTP evidence catalog: {catalog.Schema}");

        var references = new List<WebRuntimeSharedHttpReference>(responses.Count);
        foreach (var response in responses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(response.Method) || !Uri.TryCreate(response.Url, UriKind.Absolute, out _))
                throw new InvalidDataException($"Shared HTTP evidence input is invalid: {response.Method} {response.Url}");
            if ((response.BodyFilePath is null) != (response.BodySha256 is null))
                throw new InvalidDataException($"Shared HTTP body path and hash must be supplied together: {response.Url}");
            var recordId = CreateRecordId(response);
            string? sharedBodyFile = null;
            if (response.BodyFilePath is not null && response.BodySha256 is not null)
            {
                var extension = Path.GetExtension(response.BodyFilePath);
                sharedBodyFile = $"bodies/{response.BodySha256}{extension}";
                var destination = Path.Combine(store, sharedBodyFile.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(destination))
                {
                    var temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
                    File.Copy(Path.GetFullPath(response.BodyFilePath), temporary, overwrite: false);
                    var actualHash = await ComputeSha256Async(temporary, cancellationToken).ConfigureAwait(false);
                    if (!actualHash.Equals(response.BodySha256, StringComparison.Ordinal))
                    {
                        File.Delete(temporary);
                        throw new InvalidDataException($"Shared HTTP body hash mismatch for {response.Url}.");
                    }
                    try { File.Move(temporary, destination); }
                    catch (IOException) when (File.Exists(destination)) { File.Delete(temporary); }
                }
            }

            var recordPath = Path.Combine(recordsDirectory, recordId + ".json");
            if (!File.Exists(recordPath))
            {
                await WriteAtomicAsync(recordPath, JsonSerializer.Serialize(new
                {
                    schema = "iwesun.webview2.shared-http-record/1.0",
                    recordId,
                    response.CapturedAt,
                    response.Method,
                    response.Url,
                    response.Status,
                    response.ReasonPhrase,
                    response.ContentType,
                    response.Headers,
                    response.BodyLength,
                    response.BodySha256,
                    bodyFile = sharedBodyFile
                }, JsonOptions), cancellationToken).ConfigureAwait(false);
            }

            var entry = catalog.Resources.FirstOrDefault(item => item.RecordId.Equals(recordId, StringComparison.Ordinal));
            if (entry is null)
            {
                entry = new SharedResource
                {
                    RecordId = recordId,
                    Method = response.Method,
                    Url = response.Url,
                    Status = response.Status,
                    ContentType = response.ContentType,
                    BodyLength = response.BodyLength,
                    BodySha256 = response.BodySha256,
                    BodyFile = sharedBodyFile,
                    FirstCapturedAt = response.CapturedAt
                };
                catalog.Resources.Add(entry);
            }
            if (referenceId is not null)
            {
                if (!entry.References.Contains(referenceId, StringComparer.Ordinal))
                    entry.References.Add(referenceId);
                entry.References.Sort(StringComparer.Ordinal);
            }
            references.Add(new(response.Sequence, recordId, response.Method, response.Url, response.Status,
                response.ContentType, response.BodyLength, response.BodySha256, sharedBodyFile));
        }
        catalog.Resources.Sort((left, right) => string.CompareOrdinal(left.RecordId, right.RecordId));
        catalog.UpdatedAt = DateTimeOffset.UtcNow;
        await WriteAtomicAsync(catalogPath, JsonSerializer.Serialize(catalog, JsonOptions), cancellationToken).ConfigureAwait(false);
        return references;
    }

    private static string CreateRecordId(WebRuntimeSharedHttpResourceInput response)
    {
        var canonicalHeaders = string.Join('\n', response.Headers
            .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Key.ToLowerInvariant() + ":" + item.Value));
        var identity = string.Join('\n', response.Method, response.Url, response.Status.ToString(),
            response.ReasonPhrase, response.ContentType, canonicalHeaders,
            response.BodySha256 ?? string.Empty, response.BodyLength.ToString());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
    }

    private static async Task<FileStream> AcquireLockAsync(string store, CancellationToken cancellationToken)
    {
        var lockPath = Path.Combine(store, ".catalog.lock");
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(15);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task WriteAtomicAsync(string path, string content, CancellationToken cancellationToken)
    {
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(temporary, content, Utf8NoBom, cancellationToken).ConfigureAwait(false);
        File.Move(temporary, path, overwrite: true);
    }

    private sealed class SharedCatalog
    {
        internal const string SchemaValue = "iwesun.webview2.shared-http-catalog/1.0";
        public string Schema { get; init; } = SchemaValue;
        public DateTimeOffset UpdatedAt { get; set; }
        public List<SharedResource> Resources { get; init; } = [];
    }

    private sealed class SharedResource
    {
        public string RecordId { get; init; } = string.Empty;
        public string Method { get; init; } = string.Empty;
        public string Url { get; init; } = string.Empty;
        public int Status { get; init; }
        public string ContentType { get; init; } = string.Empty;
        public long BodyLength { get; init; }
        public string? BodySha256 { get; init; }
        public string? BodyFile { get; init; }
        public DateTimeOffset FirstCapturedAt { get; init; }
        public List<string> References { get; init; } = [];
    }
}

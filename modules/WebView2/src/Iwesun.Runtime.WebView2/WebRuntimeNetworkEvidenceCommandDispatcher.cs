using System.Text.Json;

namespace Iwesun.Runtime.WebView2;

public sealed record WebRuntimeNetworkEvidenceDispatchResult(
    bool Handled,
    bool Success,
    object? Value = null,
    string? ErrorCode = null,
    string? Error = null)
{
    public static WebRuntimeNetworkEvidenceDispatchResult Completed(object value) => new(true, true, value);
    public static WebRuntimeNetworkEvidenceDispatchResult Failed(string code, string error) => new(true, false, null, code, error);
}

/// <summary>Maps structured WebRuntime actions to one host-owned network evidence session.</summary>
public static class WebRuntimeNetworkEvidenceCommandDispatcher
{
    public static async Task<WebRuntimeNetworkEvidenceDispatchResult> TryExecuteAsync(
        WebRuntimeNetworkEvidenceSession session,
        WebRuntimeControlRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        if (request.Action.Equals(WebRuntimeNetworkEvidenceActions.Status, StringComparison.OrdinalIgnoreCase))
            return WebRuntimeNetworkEvidenceDispatchResult.Completed(session.GetStatus());
        if (request.Action.Equals(WebRuntimeNetworkEvidenceActions.Policy, StringComparison.OrdinalIgnoreCase))
            return WebRuntimeNetworkEvidenceDispatchResult.Completed(session.GetStatus().BodyCapturePolicy);
        if (!request.Action.Equals(WebRuntimeNetworkEvidenceActions.Export, StringComparison.OrdinalIgnoreCase))
            return new WebRuntimeNetworkEvidenceDispatchResult(false, false);

        var outputDirectory = ReadString(request.Args, "outputDirectory");
        if (string.IsNullOrWhiteSpace(outputDirectory))
            return WebRuntimeNetworkEvidenceDispatchResult.Failed(
                "MISSING_OUTPUT_DIRECTORY",
                "network.evidence.export requires args.outputDirectory.");
        var afterSequence = ReadInt32(request.Args, "afterSequence") ?? 0;
        if (afterSequence < 0)
            return WebRuntimeNetworkEvidenceDispatchResult.Failed(
                "INVALID_AFTER_SEQUENCE",
                "afterSequence cannot be negative.");
        try
        {
            await session.ExportAsync(
                outputDirectory,
                new WebRuntimeNetworkEvidenceCheckpoint(afterSequence, DateTimeOffset.MinValue),
                cancellationToken).ConfigureAwait(false);
            return WebRuntimeNetworkEvidenceDispatchResult.Completed(new
            {
                outputDirectory = Path.GetFullPath(outputDirectory),
                afterSequence,
                status = session.GetStatus()
            });
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or TimeoutException)
        {
            return WebRuntimeNetworkEvidenceDispatchResult.Failed("NETWORK_EVIDENCE_EXPORT_FAILED", exception.Message);
        }
    }

    private static string? ReadString(IReadOnlyDictionary<string, JsonElement>? arguments, string name) =>
        arguments is not null
        && arguments.TryGetValue(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadInt32(IReadOnlyDictionary<string, JsonElement>? arguments, string name) =>
        arguments is not null
        && arguments.TryGetValue(name, out var value)
        && value.TryGetInt32(out var result)
            ? result
            : null;
}

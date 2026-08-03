using System.Text;
using System.Text.Json;
using Iwesun.Runtime.WebView2;

internal static class DataStreamRecorderScenario
{
    public static async Task<FunctionalScenarioResult> RunAsync()
    {
        var checks = new List<string>();
        var failures = new List<string>();
        var outputRoot = Path.Combine(Path.GetTempPath(), "iwesun-data-recorder-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using IDataStreamRecorderManager manager = new DataStreamRecorderManager();
            manager.RegisterMatcher("functional.history", MatchHistoryAsync);
            var definition = new DataStreamMonitorDefinition
            {
                MonitorId = "functional-history",
                Source = new DataStreamSourceMatch
                {
                    BackendId = "functional-web",
                    Transports = [DataStreamTransport.Http],
                    Directions = [DataStreamDirection.Response],
                    UrlContains = "/history",
                    Methods = ["POST"],
                    StatusCodes = [200],
                    ContentTypeContains = "json"
                },
                CustomMatch = new DataStreamCustomMatchDefinition
                {
                    MatcherId = "functional.history",
                    Mode = DataStreamCustomMatchMode.And,
                    DataAccess = DataStreamDelegateDataAccess.ParsedJson
                },
                Output = new DataStreamOutputDefinition
                {
                    RootDirectory = outputRoot,
                    WriteManifest = true,
                    WriteRequestBody = true
                }
            };

            var created = await manager.CreateAsync(definition);
            if (created.State == DataStreamMonitorState.Created) checks.Add("recorder-created");
            else failures.Add("Recorder was not created in Created state.");
            var started = await manager.StartAsync(definition.MonitorId);
            if (started.State == DataStreamMonitorState.Running) checks.Add("recorder-started");
            else failures.Add("Recorder was not started.");

            var mismatch = CreateRecord("https://example.invalid/other");
            if (!manager.HasMetadataMatch(mismatch)) checks.Add("metadata-fast-reject");
            else failures.Add("Metadata gate accepted an unrelated URL.");
            await manager.ObserveAsync(mismatch);

            var record = CreateRecord("https://example.invalid/history");
            if (manager.HasMetadataMatch(record)) checks.Add("metadata-fast-accept");
            else failures.Add("Metadata gate rejected a matching record.");
            await manager.ObserveAsync(record);
            var written = manager.Get(definition.MonitorId);
            if (written?.MatchedCount == 1 && written.WrittenFileCount == 2) checks.Add("record-and-request-sidecar-written-once");
            else failures.Add("Matching record counters were not updated exactly once.");

            var files = Directory.GetFiles(outputRoot, "*", SearchOption.AllDirectories);
            if (files.Any(path => path.EndsWith("manifest.jsonl", StringComparison.OrdinalIgnoreCase))) checks.Add("manifest-written");
            else failures.Add("Manifest was not written.");
            if (files.Any(path => path.EndsWith(".request.bin", StringComparison.OrdinalIgnoreCase))) checks.Add("request-sidecar-written");
            else failures.Add("Request sidecar was not written.");

            await manager.StopAsync(definition.MonitorId);
            await manager.ObserveAsync(record with { RecordId = Guid.NewGuid().ToString("N") });
            var stopped = manager.Get(definition.MonitorId);
            if (stopped?.State == DataStreamMonitorState.Stopped && stopped.WrittenFileCount == 2) checks.Add("stop-prevents-new-writes");
            else failures.Add("Stopped recorder accepted a new write.");

            if (await manager.DeleteAsync(definition.MonitorId, new DataStreamDeleteOptions()) && manager.List().Count == 0) checks.Add("recorder-deleted");
            else failures.Add("Recorder definition was not deleted.");
            if (Directory.GetFiles(outputRoot, "*", SearchOption.AllDirectories).Length > 0) checks.Add("delete-preserves-output-by-default");
            else failures.Add("Default delete unexpectedly removed output files.");
        }
        catch (Exception exception)
        {
            failures.Add(exception.ToString());
        }
        finally
        {
            if (Directory.Exists(outputRoot)) Directory.Delete(outputRoot, recursive: true);
        }

        return failures.Count == 0
            ? FunctionalScenarioResult.Pass("data-stream-recorder", checks.ToArray())
            : FunctionalScenarioResult.Fail("data-stream-recorder", checks, failures);
    }

    private static DataStreamRecord CreateRecord(string url) => new()
    {
        BackendId = "functional-web",
        Transport = DataStreamTransport.Http,
        Direction = DataStreamDirection.Response,
        Method = "POST",
        Url = url,
        StatusCode = 200,
        ContentType = "application/json",
        RequestContent = Encoding.UTF8.GetBytes("{\"cursor\":0}"),
        Content = Encoding.UTF8.GetBytes("{\"items\":[{\"id\":\"conversation-1\"}]}")
    };

    private static ValueTask<DataStreamCustomMatchResult> MatchHistoryAsync(DataStreamMatchContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var matched = context.ParsedJson is { ValueKind: JsonValueKind.Object } json
            && json.TryGetProperty("items", out var items)
            && items.ValueKind == JsonValueKind.Array;
        return ValueTask.FromResult(new DataStreamCustomMatchResult
        {
            Matched = matched,
            DataType = "history-list",
            Identity = "page",
            SuggestedExtension = "json"
        });
    }
}

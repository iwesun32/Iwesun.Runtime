using System.IO;
using System.Text;
using System.Text.Json;
using Iwesun.Runtime.WebView2;

namespace Iwesun.Runtime.WebView2.SampleHost;

internal static class DataStreamRecorderSample
{
    public static async Task RunAsync(string outputDirectory, CancellationToken cancellationToken)
    {
        await using IDataStreamRecorderManager manager = new DataStreamRecorderManager();
        manager.RegisterMatcher("sample.history", MatchHistoryAsync);

        var definition = new DataStreamMonitorDefinition
        {
            MonitorId = "sample-history",
            DisplayName = "Sample history recorder",
            Source = new DataStreamSourceMatch
            {
                BackendId = "sample-web",
                Transports = [DataStreamTransport.Http],
                Directions = [DataStreamDirection.Response],
                UrlContains = "/api/history",
                Methods = ["POST"],
                StatusCodes = [200],
                ContentTypeContains = "json"
            },
            CustomMatch = new DataStreamCustomMatchDefinition
            {
                MatcherId = "sample.history",
                Mode = DataStreamCustomMatchMode.And,
                DataAccess = DataStreamDelegateDataAccess.ParsedJson
            },
            Output = new DataStreamOutputDefinition
            {
                RootDirectory = Path.GetFullPath(outputDirectory),
                DirectoryTemplate = "{monitorId}/{date}",
                FileNameTemplate = "{timestamp}-{sequence}-{identity}.{extension}",
                WriteManifest = true,
                WriteRequestBody = true
            }
        };

        await manager.CreateAsync(definition, cancellationToken);
        await manager.StartAsync(definition.MonitorId, cancellationToken);

        var requestBody = Encoding.UTF8.GetBytes("{\"cursor\":0}");
        var responseBody = Encoding.UTF8.GetBytes("{\"items\":[{\"id\":\"conversation-1\"}],\"hasMore\":false}");
        var record = new DataStreamRecord
        {
            BackendId = "sample-web",
            Transport = DataStreamTransport.Http,
            Direction = DataStreamDirection.Response,
            Method = "POST",
            Url = "https://example.invalid/api/history",
            StatusCode = 200,
            ContentType = "application/json; charset=utf-8",
            RequestContent = requestBody,
            Content = responseBody
        };

        if (manager.HasMetadataMatch(record))
            await manager.ObserveAsync(record, cancellationToken);

        await manager.StopAsync(definition.MonitorId, cancellationToken);
    }

    private static ValueTask<DataStreamCustomMatchResult> MatchHistoryAsync(
        DataStreamMatchContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var items = default(JsonElement);
        var matched = context.ParsedJson is { ValueKind: JsonValueKind.Object } json
            && json.TryGetProperty("items", out items)
            && items.ValueKind == JsonValueKind.Array;
        var identity = matched && items.GetArrayLength() > 0
            && items[0].TryGetProperty("id", out var id)
                ? id.GetString()
                : null;
        return ValueTask.FromResult(new DataStreamCustomMatchResult
        {
            Matched = matched,
            DataType = "history-list",
            Identity = identity ?? "page",
            SuggestedExtension = "json"
        });
    }
}

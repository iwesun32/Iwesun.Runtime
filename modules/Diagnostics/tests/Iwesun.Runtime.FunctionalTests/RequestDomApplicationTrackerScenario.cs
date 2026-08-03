using Iwesun.Runtime.WebView2;

internal static class RequestDomApplicationTrackerScenario
{
    public static Task<FunctionalScenarioResult> RunAsync()
    {
        var checks = new List<string>();
        var failures = new List<string>();
        const string valid = """
            {
              "schema": "iwesun.webview2.request-dom-application/1.0",
              "capturedAt": "2026-07-20T00:00:00Z",
              "correlation": { "kind": "temporal-window", "windowMilliseconds": 2500 },
              "frames": [
                {
                  "scope": "top",
                  "url": "https://example.test/",
                  "accessible": true,
                  "records": [
                    {
                      "id": "request-1",
                      "kind": "fetch",
                      "method": "GET",
                      "url": "https://example.test/data.json",
                      "mappingStatus": "candidate",
                      "mappingNote": "Temporal candidates only.",
                      "containers": [{ "path": "/html/body/main" }]
                    },
                    {
                      "id": "request-2",
                      "kind": "xhr",
                      "method": "GET",
                      "url": "https://example.test/document.json",
                      "mappingStatus": "unknown",
                      "mappingNote": "No render container was found.",
                      "containers": []
                    }
                  ]
                }
              ]
            }
            """;
        try
        {
            WebRuntimeResourceApplicationTracker.ValidateSnapshot(valid);
            checks.Add("valid-request-container-snapshot");
            using var assignments = System.Text.Json.JsonDocument.Parse(
                WebRuntimeResourceApplicationTracker.CreateManualAssignmentTasks(valid));
            if (assignments.RootElement.GetProperty("taskCount").GetInt32() == 1
                && assignments.RootElement.GetProperty("tasks")[0].GetProperty("status").GetString()
                    == "pending-user-assignment")
                checks.Add("unknown-deferred-to-user-assignment");
            else
                failures.Add("An unknown request was not deferred to one user assignment task.");
        }
        catch (Exception exception)
        {
            failures.Add("Valid request/container evidence was rejected: " + exception.Message);
        }

        try
        {
            WebRuntimeResourceApplicationTracker.ValidateSnapshot("{\"schema\":\"invalid\",\"frames\":[]}");
            failures.Add("Invalid request/container schema was accepted.");
        }
        catch (InvalidDataException)
        {
            checks.Add("invalid-schema-rejected");
        }

        return Task.FromResult(failures.Count == 0
            ? FunctionalScenarioResult.Pass("request-dom-application", checks.ToArray())
            : FunctionalScenarioResult.Fail("request-dom-application", checks, failures));
    }
}

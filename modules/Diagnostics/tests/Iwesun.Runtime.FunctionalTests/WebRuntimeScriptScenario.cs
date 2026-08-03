using System.Text.Json;
using Iwesun.Runtime.WebView2;

internal static class WebRuntimeScriptScenario
{
    public static async Task<FunctionalScenarioResult> RunAsync()
    {
        var checks = new List<string>();
        var failures = new List<string>();
        var session = new FakeSession();
        var audit = new WebRuntimeScriptAuditLog(8);

        var success = await WebRuntimeScriptDispatcher.TryExecuteAsync(session, new WebRuntimeControlRequest
        {
            Action = WebRuntimeScriptActions.Evaluate,
            Script = "document.title"
        }, CancellationToken.None, audit);
        if (success.Success && session.LastScript == "document.title") checks.Add("script-evaluate");
        else failures.Add("script.evaluate did not execute the explicit Script field.");

        var args = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["script"] = JsonSerializer.SerializeToElement("location.href")
        };
        var alias = await WebRuntimeScriptDispatcher.TryExecuteAsync(session, new WebRuntimeControlRequest
        {
            Action = WebRuntimeScriptActions.EvalAlias,
            Args = args
        }, CancellationToken.None, audit);
        if (alias.Success && session.LastScript == "location.href") checks.Add("eval-alias-and-args");
        else failures.Add("eval alias did not use args.script.");

        var missing = await WebRuntimeScriptDispatcher.TryExecuteAsync(session, new WebRuntimeControlRequest
        {
            Action = WebRuntimeScriptActions.Evaluate
        }, CancellationToken.None, audit);
        if (!missing.Success && missing.ErrorCode == "MISSING_SCRIPT") checks.Add("missing-script-rejected");
        else failures.Add("Missing script was not rejected with MISSING_SCRIPT.");

        var oversized = await WebRuntimeScriptDispatcher.TryExecuteAsync(session, new WebRuntimeControlRequest
        {
            Action = WebRuntimeScriptActions.Evaluate,
            Script = new string('x', WebRuntimeScriptDispatcher.MaxScriptLength + 1)
        }, CancellationToken.None, audit);
        if (!oversized.Success && oversized.ErrorCode == "SCRIPT_TOO_LARGE") checks.Add("oversized-script-rejected");
        else failures.Add("Oversized script was not rejected with SCRIPT_TOO_LARGE.");

        var auditRecords = audit.Snapshot();
        if (auditRecords.Count == 4 && auditRecords.Count(x => x.Success) == 2 && auditRecords.Any(x => x.ErrorCode == "MISSING_SCRIPT"))
            checks.Add("script-audit-recorded");
        else
            failures.Add("Script audit log did not retain the expected bounded records.");

        var rules = new WebRuntimeNetworkRuleRegistry();
        rules.Add(new WebRuntimeNetworkRule("block-chat", WebRuntimeNetworkRuleKind.Block, "*/chat/completion*", "doubao-web", "POST"));
        rules.Add(new WebRuntimeNetworkRule("replace-models", WebRuntimeNetworkRuleKind.Replace, "*/models", ResponseBody: "{\"data\":[]}"));
        var blocked = rules.Decide("doubao-web", "POST", "https://www.doubao.com/chat/completion");
        var replaced = rules.Decide("openai-web", "GET", "https://chat.openai.com/models");
        if (blocked.Blocked && replaced.ResponseBody == "{\"data\":[]}") checks.Add("network-block-replace-decisions");
        else failures.Add("Network rule registry did not produce block/replace decisions.");

        var filters = new WebRuntimeMonitorFilterRegistry();
        filters.Add(new WebRuntimeMonitorFilter("xhr", "XHR", "/chat/"));
        if (filters.Accept("XHR", "https://example.test/chat/completion") && !filters.Accept("FETCH", "https://example.test/chat/completion")) checks.Add("monitor-filter");
        else failures.Add("Monitor filter registry did not filter events correctly.");
        if (typeof(WebRuntimeCdpDomAccess).GetMethod(
            nameof(WebRuntimeCdpDomAccess.HighlightAsync)) is not null)
            checks.Add("highlight-cdp-node-access");
        else
            failures.Add("CDP node highlight access is unavailable.");

        var host = new FakeHostAdapter();
        var controller = new WebRuntimeHostController(host);
        controller.NetworkRules.Add(new WebRuntimeNetworkRule("block", WebRuntimeNetworkRuleKind.Block, "*/completion"));
        await controller.HandleNetworkRequestAsync(new WebRuntimeNetworkRequest("doubao-web", "POST", "https://example.test/completion"));
        if (host.LastDecision?.Blocked == true) checks.Add("host-network-adapter");
        else failures.Add("Host adapter did not receive the network decision.");

        var retryProgram = new RetryStopProgram();
        var registry = new WebRuntimeProgramRegistry();
        await using (var programHost = new WebRuntimeProgramHost(registry, [retryProgram]))
        {
            await programHost.ActivateAsync(CancellationToken.None);
            var firstStop = await programHost.StopAsync(CancellationToken.None);
            var removedAfterFailure = registry.Snapshot().Programs.All(program =>
                program.ProgramId != retryProgram.Descriptor.ProgramId);
            var secondStop = await programHost.StopAsync(CancellationToken.None);
            if (firstStop.State == WebRuntimeProgramHostState.StoppedWithErrors
                && removedAfterFailure
                && secondStop.State == WebRuntimeProgramHostState.StoppedWithErrors
                && retryProgram.StopCalls == 1)
                checks.Add("program-stop-failure-registration-cleanup");
            else
                failures.Add("Failed WebRuntime program cleanup left a stale registration or repeated stop.");
        }

        return failures.Count == 0
            ? FunctionalScenarioResult.Pass("web-runtime-script", checks.ToArray())
            : FunctionalScenarioResult.Fail("web-runtime-script", checks, failures);
    }

    private sealed class FakeSession : IWebRuntimeScriptSession
    {
        public string? LastScript { get; private set; }

        public Task<string> EvaluateStringAsync(string expression, CancellationToken ct)
        {
            LastScript = expression;
            return Task.FromResult("42");
        }

        public Task<string> EvaluateStringInFrameAsync(string sourceUrlContains, string expression, CancellationToken ct) =>
            Task.FromResult("42");
    }

    private sealed class FakeHostAdapter : IWebRuntimeHostAdapter
    {
        public WebRuntimeNetworkDecision? LastDecision { get; private set; }
        public Task ApplyNetworkDecisionAsync(WebRuntimeNetworkRequest request, WebRuntimeNetworkDecision decision, CancellationToken ct)
        {
            LastDecision = decision;
            return Task.CompletedTask;
        }
        public Task<WebRuntimeElementEvidence?> CaptureNodeAsync(
            WebRuntimeDomNodeReference node,
            CancellationToken ct) =>
            Task.FromResult<WebRuntimeElementEvidence?>(null);
    }

    private sealed class RetryStopProgram : IWebRuntimeBusinessProgram, IWebRuntimeBusinessProgramLifecycle
    {
        public WebRuntimeProgramDescriptor Descriptor { get; } = new(
            "retry-stop",
            "Retry Stop",
            "1.0",
            [new WebRuntimeProgramActionDescriptor("run", "Run")]);

        public int StopCalls { get; private set; }

        public Task InitializeAsync(CancellationToken ct) => Task.CompletedTask;

        public Task StopAsync(CancellationToken ct)
        {
            StopCalls++;
            return StopCalls == 1
                ? Task.FromException(new InvalidOperationException("first-stop-failed"))
                : Task.CompletedTask;
        }

        public Task<WebRuntimeProgramResult> ExecuteAsync(
            WebRuntimeProgramContext context,
            CancellationToken ct) =>
            Task.FromResult(WebRuntimeProgramResult.Ok(context.Request.Action));
    }
}

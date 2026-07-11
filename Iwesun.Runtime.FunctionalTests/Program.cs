using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Iwesun.Runtime.Data;
using Iwesun.Runtime.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

[assembly: DiagnosticWatchPoint("tree.root", "tree", "watch", "Tree root snapshot.", "Iwesun.Runtime.FunctionalTests.TreeScenarioModel")]
[assembly: DiagnosticWatchPoint("tree.branches", "tree", "watch", "Tree branch snapshot.", "Iwesun.Runtime.FunctionalTests.TreeScenarioModel")]
[assembly: DiagnosticBreakpoint("tree.fill", "tree", "Pause while tree is being filled.", "Iwesun.Runtime.FunctionalTests.TreeScenarioModel", TimeoutMs = 250, HitCountTarget = 1)]
[assembly: DiagnosticBreakpoint("tree.stabilize", "tree", "Pause before tree inspection.", "Iwesun.Runtime.FunctionalTests.TreeScenarioModel", TimeoutMs = 250, HitCountTarget = 1)]
[assembly: DiagnosticBreakpoint("numeric.default.threshold", "numeric", "Default numeric threshold breakpoint.", "Iwesun.Runtime.FunctionalTests.Program", TimeoutMs = 300, HitCountTarget = 1)]
[assembly: DiagnosticNumericBreakpoint("numeric.default.threshold", "gt", 7)]
[assembly: DiagnosticBreakpoint("numeric.default.range", "numeric", "Default numeric range breakpoint.", "Iwesun.Runtime.FunctionalTests.Program", TimeoutMs = 300, HitCountTarget = 1)]
[assembly: DiagnosticNumericBreakpoint("numeric.default.range", "between", 3, 9)]
[assembly: DiagnosticBreakpoint("numeric.default.delta", "numeric", "Default numeric delta breakpoint.", "Iwesun.Runtime.FunctionalTests.Program", TimeoutMs = 300, HitCountTarget = 1)]
[assembly: DiagnosticNumericBreakpoint("numeric.default.delta", "delta-le", 2)]
[assembly: DiagnosticHookableEvent("tree.updated", typeof(TreeScenarioSignals), nameof(TreeScenarioSignals.Updated))]

if (FunctionalArgs.Contains(args, "--help") || FunctionalArgs.Contains(args, "-h"))
{
    FunctionalHelp.Print();
    return 0;
}

if (FunctionalArgs.Contains(args, "--child"))
{
    var childOptions = FunctionalChildOptions.Parse(args);
    return await FunctionalChildRunner.RunAsync(childOptions);
}

return await FunctionalParentRunner.RunAsync(args);

internal static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
}

internal sealed record CommandConfig(
    Dictionary<string, string> Pipes,
    List<CommandConfigEntry> BaseCommands);

internal sealed record CommandConfigEntry(string Name);

internal sealed record FunctionalScenarioResult(
    string Scenario,
    bool Success,
    IReadOnlyList<string> Checks,
    IReadOnlyList<string> Failures,
    DateTimeOffset CheckedAt)
{
    public static FunctionalScenarioResult Pass(string scenario, params string[] checks) =>
        new(scenario, true, checks, Array.Empty<string>(), DateTimeOffset.UtcNow);

    public static FunctionalScenarioResult Fail(string scenario, IReadOnlyList<string> checks, IReadOnlyList<string> failures) =>
        new(scenario, false, checks, failures, DateTimeOffset.UtcNow);
}

internal sealed record FunctionalChildOptions(string Scenario)
{
    public static FunctionalChildOptions Parse(string[] args)
    {
        var scenario = FunctionalArgs.GetValue(args, "--scenario") ?? "probe";
        return new FunctionalChildOptions(scenario);
    }
}

static class FunctionalParentRunner
{
    public static async Task<int> RunAsync(string[] args)
    {
        var scenarios = new[] { "diagnostics", "managed", "thread", "task", "process", "tree", "root-safety", "sharedfifo-protocol", "numeric-breakpoint", "pipe-registry", "cli" };
        var results = new List<FunctionalScenarioResult>(scenarios.Length);
        var failures = new List<string>();

        foreach (var scenario in scenarios)
        {
            var result = await RunChildScenarioAsync(scenario);
            results.Add(result);
            if (!result.Success)
            {
                failures.AddRange(result.Failures.Select(f => $"{scenario}: {f}"));
            }
        }

        var report = new
        {
            success = failures.Count == 0,
            checkedAt = DateTimeOffset.UtcNow,
            results,
            failures
        };

        Console.WriteLine(JsonSerializer.Serialize(report, JsonDefaults.Options));
        return failures.Count == 0 ? 0 : 1;
    }

    private static async Task<FunctionalScenarioResult> RunChildScenarioAsync(string scenario)
    {
        var dllPath = ResolveSelfDllPath();
        var psi = new ProcessStartInfo("dotnet", $"\"{dllPath}\" --child --scenario {scenario}")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start child scenario '{scenario}'.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var json = ExtractLastJsonLine(stdout);
        if (string.IsNullOrWhiteSpace(json))
        {
            return FunctionalScenarioResult.Fail(scenario, Array.Empty<string>(), new[] { $"Child scenario produced no JSON output. stderr={stderr.Trim()}" });
        }

        try
        {
            var result = JsonSerializer.Deserialize<FunctionalScenarioResult>(json, JsonDefaults.Options)
                ?? throw new InvalidOperationException("Child result was null.");
            if (process.ExitCode != 0 && result.Success)
            {
                return result with { Success = false, Failures = result.Failures.Concat(new[] { $"Child exit code was {process.ExitCode}. stderr={stderr.Trim()}" }).ToArray() };
            }

            return result;
        }
        catch (Exception ex)
        {
            return FunctionalScenarioResult.Fail(scenario, Array.Empty<string>(), new[] { $"Failed to parse child output: {ex.GetType().Name}: {ex.Message}. stdout={stdout.Trim()} stderr={stderr.Trim()}" });
        }
    }

    private static string ResolveSelfDllPath()
    {
        var location = Assembly.GetExecutingAssembly().Location;
        if (string.IsNullOrWhiteSpace(location))
        {
            throw new InvalidOperationException("Cannot resolve test assembly location.");
        }

        return location;
    }

    private static string? ExtractLastJsonLine(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
            return null;

        var lines = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();
            if (line.StartsWith('{') && line.EndsWith('}'))
                return line;
        }

        return null;
    }
}

static class FunctionalChildRunner
{
    public static async Task<int> RunAsync(FunctionalChildOptions options)
    {
        var runtimeDirectory = Path.Combine(AppContext.BaseDirectory, "runtime-functional-tests", options.Scenario, Guid.NewGuid().ToString("N"));
        var diagnosticsPipeName = $"functional.diagnostics.{options.Scenario}.{Guid.NewGuid():N}";

        using var host = BuildHost(runtimeDirectory, diagnosticsPipeName);
        var provider = host.Services;
        await host.StartAsync();

        FunctionalScenarioResult result;
        try
        {
            result = options.Scenario.ToLowerInvariant() switch
            {
                "diagnostics" => await RunDiagnosticsScenario(provider),
                "managed" => await RunManagedScenario(provider),
                "thread" => await RunThreadScenario(provider),
                "task" => await RunTaskScenario(provider),
                "process" => await RunProcessScenario(provider),
                "tree" => await RunTreeScenario(provider),
                "root-safety" => await RunRootSafetyScenario(provider),
                "sharedfifo-protocol" => await RunSharedFifoProtocolScenario(provider),
                "numeric-breakpoint" => await RunNumericBreakpointScenario(provider),
                "pipe-registry" => await RunPipeRegistryScenario(provider, diagnosticsPipeName),
                "tree-process" => await RunTreeProcessScenario(provider),
                "cli" => await RunCliScenario(provider, diagnosticsPipeName),
                "probe" => FunctionalScenarioResult.Pass("probe", "probe child exited successfully."),
                _ => FunctionalScenarioResult.Fail(options.Scenario, Array.Empty<string>(), new[] { $"Unknown scenario: {options.Scenario}" })
            };
        }
        catch (Exception ex)
        {
            result = FunctionalScenarioResult.Fail(options.Scenario, Array.Empty<string>(), new[] { $"Unhandled child exception: {ex}" });
        }
        finally
        {
            await host.StopAsync();
        }

        Console.WriteLine(JsonSerializer.Serialize(result));
        return result.Success ? 0 : 1;
    }

    private static IHost BuildHost(string runtimeDirectory, string? diagnosticsPipeName)
    {
        Directory.CreateDirectory(runtimeDirectory);

        if (!string.IsNullOrWhiteSpace(diagnosticsPipeName))
        {
            var configStore = new DiagnosticSwitchboardConfigStore(runtimeDirectory);
            var config = DiagnosticSwitchboardCompiledConfig.CreateDefaults();
            config.RuntimeDiagnosticsPipeName = diagnosticsPipeName;
            configStore.Save(config);
        }

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.None);
        builder.Services.Start(runtimeDirectory);
        var host = builder.Build();
        host.Services.Activate(Assembly.GetExecutingAssembly());
        return host;
    }

    private static async Task<FunctionalScenarioResult> RunDiagnosticsScenario(IServiceProvider provider)
    {
        var monitor = provider.GetRequiredService<RuntimeDiagnosticsMonitor>();
        var hub = provider.GetRequiredService<RuntimeDiagnosticHub>();
        var checks = new List<string>();
        var failures = new List<string>();

        if (!await WaitUntilAsync(() => monitor.IsRunning && !string.IsNullOrWhiteSpace(monitor.PipeName), TimeSpan.FromSeconds(5)))
        {
            failures.Add("RuntimeDiagnosticsMonitor did not start.");
            return FunctionalScenarioResult.Fail("diagnostics", checks, failures);
        }

        checks.Add($"monitor-running:{monitor.PipeName}");

        var frame = new RuntimeDiagnosticFrame
        {
            Header = new RuntimeDiagnosticFrameHeader
            {
                Schema = RuntimeDiagnosticProtocol.V2Schema,
                FrameType = "request",
                Category = "query",
                Operation = "snapshot",
                RequestId = Guid.NewGuid().ToString("N")
            },
            Command = new RuntimeDiagnosticFrameCommand
            {
                Domain = "diagnostics",
                Target = "runtime.managed",
                Action = "snapshot"
            }
        };

        var response = await SendFrameAsync(monitor.PipeName, frame);
        if (response.Status?.Ok != true)
        {
            failures.Add($"Diagnostic frame response was not ok: {response.Status?.Code} {response.Status?.Message}");
        }
        else
        {
            checks.Add("pipe-frame-response-ok");
        }

        if (response.Data is null)
        {
            failures.Add("Diagnostic frame response missing snapshot data.");
        }
        else
        {
            checks.Add("pipe-frame-snapshot-present");
        }

        var hubSnapshot = hub.Snapshot("runtime.managed");
        if (hubSnapshot is not null)
        {
            checks.Add("hub-snapshot-available");
        }

        return failures.Count == 0
            ? FunctionalScenarioResult.Pass("diagnostics", checks.ToArray())
            : FunctionalScenarioResult.Fail("diagnostics", checks, failures);
    }

    private static async Task<FunctionalScenarioResult> RunManagedScenario(IServiceProvider provider)
    {
        var hub = provider.GetRequiredService<RuntimeDiagnosticHub>();
        var managed = provider.GetRequiredService<RuntimeManagedRegistry>();
        var checks = new List<string>();
        var failures = new List<string>();
        var unitId = $"functional.managed.{Guid.NewGuid():N}";

        managed.Register(unitId, "test", "Functional", CreateSnapshot(unitId));
        checks.Add("registered");

        var enqueue = await hub.ExecuteAsync(new RuntimeDiagnosticAction
        {
            TargetId = "runtime.managed",
            Action = "enqueue",
            Args = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["targetUnitId"] = JsonSerializer.SerializeToElement(unitId),
                ["kind"] = JsonSerializer.SerializeToElement("Snapshot"),
                ["payload"] = JsonSerializer.SerializeToElement("from-hub")
            }
        });
        if (!enqueue.Success)
        {
            failures.Add($"enqueue failed: {enqueue.Error}");
        }
        else
        {
            checks.Add("enqueue-ok");
        }

        if (!managed.TryDequeueCommand(unitId, out var command) || command is null || command.TargetUnitId != unitId || command.Kind != RuntimeManagedCommandKind.Snapshot)
        {
            failures.Add("registry did not expose the enqueued command by UnitId.");
        }
        else
        {
            checks.Add("dequeue-by-unitid-ok");
        }

        var events = managed.DrainEvents(10);
        if (events.Count == 0)
        {
            failures.Add("managed event stream was empty.");
        }
        else
        {
            checks.Add("events-present");
        }

        var lifecycleSet = await hub.ExecuteAsync(new RuntimeDiagnosticAction
        {
            TargetId = "runtime.managed",
            Action = "globalStateSet",
            Args = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = JsonSerializer.SerializeToElement("Running")
            }
        });
        if (!lifecycleSet.Success)
        {
            failures.Add($"global state set failed: {lifecycleSet.Error}");
        }
        else
        {
            checks.Add("global-state-set-ok");
        }

        var unitHistory = await hub.ExecuteAsync(new RuntimeDiagnosticAction
        {
            TargetId = "runtime.managed",
            Action = "unitStateHistory",
            Args = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["unitId"] = JsonSerializer.SerializeToElement(unitId),
                ["count"] = JsonSerializer.SerializeToElement(10)
            }
        });
        if (!unitHistory.Success)
        {
            failures.Add($"unit state history failed: {unitHistory.Error}");
        }
        else
        {
            var historyJson = JsonSerializer.SerializeToElement(unitHistory.Value);
            if (historyJson.ValueKind != JsonValueKind.Array || historyJson.GetArrayLength() == 0)
            {
                failures.Add("unit state history was empty.");
            }
            else
            {
                checks.Add("unit-state-history-ok");
            }
        }

        managed.Unregister(unitId);
        if (managed.SnapshotRegistrations().Any(x => x.UnitId == unitId))
        {
            failures.Add("registration was not removed on unregister.");
        }
        else
        {
            checks.Add("unregister-ok");
        }

        return failures.Count == 0
            ? FunctionalScenarioResult.Pass("managed", checks.ToArray())
            : FunctionalScenarioResult.Fail("managed", checks, failures);
    }

    private static async Task<FunctionalScenarioResult> RunRootSafetyScenario(IServiceProvider provider)
    {
        var hub = provider.GetRequiredService<RuntimeDiagnosticHub>();
        var checks = new List<string>();
        var failures = new List<string>();

        var snapshotResult = await hub.ExecuteAsync(new RuntimeDiagnosticAction
        {
            TargetId = "runtime.root",
            Action = "snapshot"
        });
        if (!snapshotResult.Success || snapshotResult.Value is not RuntimeRootSnapshot rootSnapshot)
        {
            failures.Add($"runtime.root snapshot failed: {snapshotResult.Error ?? "no snapshot"}");
            return FunctionalScenarioResult.Fail("root-safety", checks, failures);
        }

        checks.Add("runtime-root-snapshot-ok");

        var hasReflectionCatalog = rootSnapshot.Tables.Any(x =>
            x.TableName.Equals("T09.DiagnosticHubTable.Reflection.Types.Catalog", StringComparison.OrdinalIgnoreCase));
        if (!hasReflectionCatalog)
        {
            failures.Add("Reflection types catalog table is missing.");
        }
        else
        {
            checks.Add("reflection-types-catalog-present");
        }

        var hasReflectionPage = rootSnapshot.Tables.Any(x =>
            x.TableName.StartsWith("T09.DiagnosticHubTable.Reflection.Types.Page.", StringComparison.OrdinalIgnoreCase));
        if (!hasReflectionPage)
        {
            failures.Add("Reflection types paged tables are missing.");
        }
        else
        {
            checks.Add("reflection-types-pages-present");
        }

        var parallelRequests = Enumerable.Range(0, 24)
            .Select(_ => Task.Run(async () =>
            {
                for (var i = 0; i < 25; i++)
                {
                    var tableName = i % 2 == 0
                        ? "T09.DiagnosticHubTable.Meta"
                        : "T06.ThreadTable";
                    var result = await hub.ExecuteAsync(new RuntimeDiagnosticAction
                    {
                        TargetId = "runtime.root",
                        Action = "table",
                        Args = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["table"] = JsonSerializer.SerializeToElement(tableName)
                        }
                    });
                    if (!result.Success)
                    {
                        return false;
                    }
                }

                return true;
            }))
            .ToArray();
        var requestOutcomes = await Task.WhenAll(parallelRequests);
        if (requestOutcomes.Any(x => !x))
        {
            failures.Add("Concurrent runtime.root table queries failed.");
        }
        else
        {
            checks.Add("runtime-root-concurrency-stable");
        }

        return failures.Count == 0
            ? FunctionalScenarioResult.Pass("root-safety", checks.ToArray())
            : FunctionalScenarioResult.Fail("root-safety", checks, failures);
    }

    private static Task<FunctionalScenarioResult> RunSharedFifoProtocolScenario(IServiceProvider provider)
    {
        var managed = provider.GetRequiredService<RuntimeManagedRegistry>();
        var checks = new List<string>();
        var failures = new List<string>();
        var unitId = $"functional.sharedfifo.{Guid.NewGuid():N}";
        var unitHash = RuntimeInjectorTransportCodec.ComputeStableHash32(unitId);
        var state = CreateSnapshot(unitId);

        managed.Register(unitId, "thread", "Functional", state);

        var seedFrame = new RuntimeCommandFrame(
            processId: Environment.ProcessId + 1000,
            managedThreadId: 1,
            sequence: 1,
            targetIdHash: unitHash,
            commandKind: (int)RuntimeManagedCommandKind.Stop,
            timestampUtcTicks: DateTimeOffset.UtcNow.UtcTicks,
            arg0: 0,
            arg1: 0);
        if (!DiagnosticSwitchboard.TryPutCommandFrame(seedFrame))
        {
            failures.Add("shared command frame enqueue failed.");
            return Task.FromResult(FunctionalScenarioResult.Fail("sharedfifo-protocol", checks, failures));
        }

        if (!managed.TryDequeueCommand(unitId, out var imported) || imported is null || imported.Kind != RuntimeManagedCommandKind.Stop)
        {
            failures.Add("shared command frame was not imported/dequeued.");
        }
        else
        {
            checks.Add("shared-command-imported");
        }

        if (managed.ImportedSharedCommandCount <= 0)
        {
            failures.Add("shared command import counter did not increase.");
        }
        else
        {
            checks.Add("shared-command-counter-updated");
        }

        managed.UpdateState(unitId, state);
        var stateSeen = false;
        for (var i = 0; i < 200; i++)
        {
            if (DiagnosticSwitchboard.TryGetStateFrame(out var stateFrame))
            {
                if (stateFrame.EntityIdHash == unitHash)
                {
                    stateSeen = true;
                    break;
                }
            }

            Thread.Sleep(2);
        }

        if (!stateSeen)
        {
            failures.Add("shared state frame was not published.");
        }
        else
        {
            checks.Add("shared-state-published");
        }

        var produced = 0;
        for (var i = 0; i < 64; i++)
        {
            var frame = new RuntimeCommandFrame(
                processId: Environment.ProcessId + 2000 + i,
                managedThreadId: i + 1,
                sequence: 100 + i,
                targetIdHash: unitHash,
                commandKind: (int)RuntimeManagedCommandKind.Wakeup,
                timestampUtcTicks: DateTimeOffset.UtcNow.UtcTicks,
                arg0: i,
                arg1: 0);
            if (DiagnosticSwitchboard.TryPutCommandFrame(frame))
            {
                produced++;
            }
        }

        var consumed = 0;
        for (var i = 0; i < 128; i++)
        {
            if (managed.TryDequeueCommand(unitId, out var command) && command is not null)
            {
                consumed++;
            }
        }

        if (produced == 0 || consumed == 0)
        {
            failures.Add($"shared command stress path failed (produced={produced}, consumed={consumed}).");
        }
        else
        {
            checks.Add("shared-command-stress-stable");
        }

        managed.Unregister(unitId);
        return Task.FromResult(failures.Count == 0
            ? FunctionalScenarioResult.Pass("sharedfifo-protocol", checks.ToArray())
            : FunctionalScenarioResult.Fail("sharedfifo-protocol", checks, failures));
    }

    private static async Task<FunctionalScenarioResult> RunNumericBreakpointScenario(IServiceProvider provider)
    {
        var hub = provider.GetRequiredService<RuntimeDiagnosticHub>();
        var checks = new List<string>();
        var failures = new List<string>();
        const string breakpointId = "numeric.default.threshold";

        RuntimeOutputSwitch.Enabled = true;
        var enableResult = await hub.ExecuteAsync(new RuntimeDiagnosticAction
        {
            TargetId = "diagnostics.breakpoints",
            Action = "enable",
            Args = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = JsonSerializer.SerializeToElement(breakpointId)
            }
        });
        if (!enableResult.Success)
        {
            failures.Add($"enable breakpoint failed: {enableResult.Error}");
            return FunctionalScenarioResult.Fail("numeric-breakpoint", checks, failures);
        }
        checks.Add("numeric-breakpoint-enabled");

        var listDefault = await hub.ExecuteAsync(new RuntimeDiagnosticAction
        {
            TargetId = "diagnostics.breakpoints",
            Action = "listNumeric"
        });
        if (!listDefault.Success)
        {
            failures.Add($"listNumeric failed: {listDefault.Error}");
            return FunctionalScenarioResult.Fail("numeric-breakpoint", checks, failures);
        }
        var expectedBindings = new[] { breakpointId, "numeric.default.range", "numeric.default.delta" };
        var loadedBindings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var defaultList = JsonSerializer.SerializeToElement(listDefault.Value);
        if (defaultList.TryGetProperty("bindings", out var bindings) && bindings.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in bindings.EnumerateArray())
            {
                var hasId =
                    item.TryGetProperty("breakpointId", out var idNode)
                    || item.TryGetProperty("BreakpointId", out idNode);
                if (hasId
                    && idNode.ValueKind == JsonValueKind.String
                    && expectedBindings.Contains(idNode.GetString() ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                {
                    loadedBindings.Add(idNode.GetString()!);
                }
            }
        }

        if (loadedBindings.Count != expectedBindings.Length)
        {
            failures.Add("default numeric binding was not loaded from assembly attributes.");
            return FunctionalScenarioResult.Fail("numeric-breakpoint", checks, failures);
        }
        checks.Add("numeric-default-binding-loaded");

        var hitStart = DateTime.UtcNow;
        await RuntimeOutput.BreakIfNumbers(breakpointId, 10, 5, new { phase = "gt2-hit" });
        var hitElapsed = (DateTime.UtcNow - hitStart).TotalMilliseconds;
        if (hitElapsed < 200)
        {
            failures.Add($"numeric breakpoint did not block as expected under gt2 (elapsed={hitElapsed:F0}ms).");
        }
        else
        {
            checks.Add("numeric-break-hit-default");
        }

        var bindLt = await hub.ExecuteAsync(new RuntimeDiagnosticAction
        {
            TargetId = "diagnostics.breakpoints",
            Action = "setNumericThreshold",
            Args = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = JsonSerializer.SerializeToElement(breakpointId),
                ["operator"] = JsonSerializer.SerializeToElement("lt"),
                ["threshold"] = JsonSerializer.SerializeToElement(7)
            }
        });
        if (!bindLt.Success)
        {
            failures.Add($"setNumeric update failed: {bindLt.Error}");
            return FunctionalScenarioResult.Fail("numeric-breakpoint", checks, failures);
        }
        checks.Add("numeric-threshold-updated");

        var missStart = DateTime.UtcNow;
        await RuntimeOutput.BreakIfNumbers(breakpointId, 10, 5, new { phase = "lt2-miss" });
        var missElapsed = (DateTime.UtcNow - missStart).TotalMilliseconds;
        if (missElapsed > 150)
        {
            failures.Add($"numeric breakpoint should not block under lt2 (elapsed={missElapsed:F0}ms).");
        }
        else
        {
            checks.Add("numeric-break-miss");
        }

        var listNumeric = await hub.ExecuteAsync(new RuntimeDiagnosticAction
        {
            TargetId = "diagnostics.breakpoints",
            Action = "listNumeric"
        });
        if (!listNumeric.Success)
        {
            failures.Add($"listNumeric failed: {listNumeric.Error}");
        }
        else
        {
            checks.Add("numeric-binding-listed");
        }

        return failures.Count == 0
            ? FunctionalScenarioResult.Pass("numeric-breakpoint", checks.ToArray())
            : FunctionalScenarioResult.Fail("numeric-breakpoint", checks, failures);
    }

    private static async Task<FunctionalScenarioResult> RunPipeRegistryScenario(IServiceProvider provider, string diagnosticsPipeName)
    {
        var hub = provider.GetRequiredService<RuntimeDiagnosticHub>();
        var checks = new List<string>();
        var failures = new List<string>();

        var staticName = $"functional-branch-{Guid.NewGuid():N}";
        var staticPipe = RuntimePipeRegistry.AcquirePipe(staticName, diagnosticsPipeName);
        if (string.IsNullOrWhiteSpace(staticPipe))
        {
            failures.Add("static acquire returned empty pipe name.");
            return FunctionalScenarioResult.Fail("pipe-registry", checks, failures);
        }
        checks.Add("static-acquire-ok");

        var duplicatePipe = RuntimePipeRegistry.AcquirePipe(staticName, diagnosticsPipeName);
        if (string.Equals(staticPipe, duplicatePipe, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("duplicate acquire should append suffix and create another pipe name.");
        }
        else
        {
            checks.Add("duplicate-suffix-ok");
        }

        var acquireViaHub = await hub.ExecuteAsync(new RuntimeDiagnosticAction
        {
            TargetId = "diagnostics.pipes",
            Action = "acquire",
            Args = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = JsonSerializer.SerializeToElement($"functional-hub-{Guid.NewGuid():N}"),
                ["aggregatePipeName"] = JsonSerializer.SerializeToElement(diagnosticsPipeName)
            }
        });
        if (!acquireViaHub.Success)
        {
            failures.Add($"hub acquire failed: {acquireViaHub.Error}");
            return FunctionalScenarioResult.Fail("pipe-registry", checks, failures);
        }
        checks.Add("hub-acquire-ok");

        var list = await hub.ExecuteAsync(new RuntimeDiagnosticAction
        {
            TargetId = "diagnostics.pipes",
            Action = "list"
        });
        if (!list.Success)
        {
            failures.Add($"list failed: {list.Error}");
            return FunctionalScenarioResult.Fail("pipe-registry", checks, failures);
        }

        var leases = JsonSerializer.SerializeToElement(list.Value);
        if (leases.ValueKind != JsonValueKind.Array || leases.GetArrayLength() == 0)
        {
            failures.Add("pipe registry list is empty.");
            return FunctionalScenarioResult.Fail("pipe-registry", checks, failures);
        }
        checks.Add("pipe-list-ok");

        var firstLease = leases.EnumerateArray().FirstOrDefault();
        if (!firstLease.TryGetProperty("InternalId", out var internalIdNode)
            || internalIdNode.ValueKind != JsonValueKind.Number
            || !internalIdNode.TryGetInt32(out var internalId))
        {
            failures.Add("pipe lease missing numeric InternalId.");
            return FunctionalScenarioResult.Fail("pipe-registry", checks, failures);
        }
        checks.Add("pipe-id-present");

        var resolve = await hub.ExecuteAsync(new RuntimeDiagnosticAction
        {
            TargetId = "diagnostics.pipes",
            Action = "resolve",
            Args = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = JsonSerializer.SerializeToElement(internalId.ToString())
            }
        });
        if (!resolve.Success)
        {
            failures.Add($"resolve by id failed: {resolve.Error}");
        }
        else
        {
            checks.Add("resolve-by-id-ok");
        }

        var release = await hub.ExecuteAsync(new RuntimeDiagnosticAction
        {
            TargetId = "diagnostics.pipes",
            Action = "release",
            Args = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = JsonSerializer.SerializeToElement(internalId.ToString())
            }
        });
        if (!release.Success)
        {
            failures.Add($"release by id failed: {release.Error}");
        }
        else
        {
            checks.Add("release-by-id-ok");
        }

        return failures.Count == 0
            ? FunctionalScenarioResult.Pass("pipe-registry", checks.ToArray())
            : FunctionalScenarioResult.Fail("pipe-registry", checks, failures);
    }

    private static async Task<FunctionalScenarioResult> RunThreadScenario(IServiceProvider provider)
    {
        var managed = provider.GetRequiredService<RuntimeManagedRegistry>();
        var hub = provider.GetRequiredService<RuntimeDiagnosticHub>();
        var execution = provider.GetRequiredService<RuntimeExecutionManager>();
        var checks = new List<string>();
        var failures = new List<string>();
        var unitId = $"functional.thread.{Guid.NewGuid():N}";
        var bodyEntered = new ManualResetEventSlim(false);
        var interrupted = false;
        var exitRequested = false;
        var exitCompleted = false;

        var thread = new RThread(() =>
        {
            bodyEntered.Set();
            try
            {
                Thread.Sleep(Timeout.Infinite);
            }
            catch (ThreadInterruptedException)
            {
                interrupted = true;
            }
        }, unitId, "Functional Thread", RuntimeExecutionLifetime.Dynamic, RuntimeThreadKind.Worker, "FunctionalTests", nameof(FunctionalChildRunner));

        thread.ExitRequested += (_, _) => exitRequested = true;
        thread.ExitCompleted += (_, _) => exitCompleted = true;
        thread.Start();

        if (!bodyEntered.Wait(TimeSpan.FromSeconds(2)))
        {
            failures.Add("thread body did not start.");
        }
        else
        {
            checks.Add("thread-started");
        }

        var startSnapshot = execution.Snapshot();
        if (!startSnapshot.DynamicThreads.Any(x => x.Id == unitId && x.State == RuntimeThreadState.Running))
        {
            failures.Add("thread was not visible as running in execution snapshot.");
        }
        else
        {
            checks.Add("thread-running-visible");
        }

        var threadList = await hub.ExecuteAsync(new RuntimeDiagnosticAction
        {
            TargetId = "runtime.execution",
            Action = "snapshot"
        });
        if (!threadList.Success)
        {
            failures.Add($"thread snapshot command failed: {threadList.Error}");
        }
        else
        {
            var threadJson = JsonSerializer.SerializeToElement(threadList.Value);
            if (!threadJson.TryGetProperty("values", out var values)
                || !values.TryGetProperty("DynamicThreads", out var dynamicThreads)
                || dynamicThreads.ValueKind != JsonValueKind.Array
                || !dynamicThreads.EnumerateArray().Any(x => x.TryGetProperty("Id", out var idNode) && idNode.GetString() == unitId))
            {
                failures.Add("thread snapshot command did not list the active thread.");
            }
            else
            {
                checks.Add("thread-list-command-ok");
            }
        }

        var enqueue = await hub.ExecuteAsync(new RuntimeDiagnosticAction
        {
            TargetId = "runtime.managed",
            Action = "enqueue",
            Args = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["targetUnitId"] = JsonSerializer.SerializeToElement(unitId),
                ["kind"] = JsonSerializer.SerializeToElement("Stop"),
                ["payload"] = JsonSerializer.SerializeToElement("stop-thread")
            }
        });
        if (!enqueue.Success)
        {
            failures.Add($"thread stop enqueue failed: {enqueue.Error}");
        }
        else
        {
            checks.Add("stop-enqueued");
        }

        if (!thread.Join(TimeSpan.FromSeconds(5)))
        {
            failures.Add("thread did not stop within timeout.");
        }
        else
        {
            checks.Add("thread-joined");
        }

        var endSnapshot = execution.Snapshot();
        if (!interrupted)
        {
            failures.Add("thread body did not observe ThreadInterruptedException.");
        }
        else
        {
            checks.Add("interrupt-observed");
        }

        if (!exitRequested)
        {
            failures.Add("ExitRequested event did not fire.");
        }
        else
        {
            checks.Add("exit-requested-event");
        }

        if (!exitCompleted)
        {
            failures.Add("ExitCompleted event did not fire.");
        }
        else
        {
            checks.Add("exit-completed-event");
        }

        if (!endSnapshot.DynamicThreads.Any(x => x.Id == unitId && x.State == RuntimeThreadState.Completed))
        {
            failures.Add("thread did not end in Completed state.");
        }
        else
        {
            checks.Add("thread-completed-visible");
        }

        if (managed.SnapshotRegistrations().Any(x => x.UnitId == unitId))
        {
            failures.Add("thread registration remained after exit.");
        }
        else
        {
            checks.Add("thread-unregistered");
        }

        return failures.Count == 0
            ? FunctionalScenarioResult.Pass("thread", checks.ToArray())
            : FunctionalScenarioResult.Fail("thread", checks, failures);
    }

    private static async Task<FunctionalScenarioResult> RunTaskScenario(IServiceProvider provider)
    {
        var managed = provider.GetRequiredService<RuntimeManagedRegistry>();
        var execution = provider.GetRequiredService<RuntimeExecutionManager>();
        var checks = new List<string>();
        var failures = new List<string>();
        var unitId = $"functional.task.{Guid.NewGuid():N}";
        var ran = false;

        var task = new RTask(() => ran = true, unitId, "functional", threadId: "functional.thread", lifetime: RuntimeExecutionLifetime.Dynamic, sourceLocation: nameof(FunctionalChildRunner));
        task.Start();
        await task;

        if (!ran)
        {
            failures.Add("task body did not run.");
        }
        else
        {
            checks.Add("task-ran");
        }

        var snapshot = execution.Snapshot();
        if (!snapshot.DynamicTasks.Any(x => x.Id == unitId && x.State == RuntimeTaskState.Completed))
        {
            failures.Add("task was not observed as completed.");
        }
        else
        {
            checks.Add("task-completed-visible");
        }

        if (managed.SnapshotRegistrations().Any(x => x.UnitId == unitId))
        {
            failures.Add("task registration remained after completion.");
        }
        else
        {
            checks.Add("task-unregistered");
        }

        return failures.Count == 0
            ? FunctionalScenarioResult.Pass("task", checks.ToArray())
            : FunctionalScenarioResult.Fail("task", checks, failures);
    }

    private static async Task<FunctionalScenarioResult> RunProcessScenario(IServiceProvider provider)
    {
        var managed = provider.GetRequiredService<RuntimeManagedRegistry>();
        var hub = provider.GetRequiredService<RuntimeDiagnosticHub>();
        var checks = new List<string>();
        var failures = new List<string>();

        var pipeName = $"functional.pipe.{Guid.NewGuid():N}";
        var expectedResponse = "{\"ok\":true}";
        var pipeServerTask = HandlePipeServerAsync(pipeName, expectedResponse);
        var pipeClient = new RProcess($"functional.pipe.client.{Guid.NewGuid():N}");
        var response = await pipeClient.ExchangeWithMonitorAsync(pipeName, "{\"ping\":1}");
        if (response != expectedResponse)
        {
            failures.Add($"pipe exchange returned unexpected response: {response}");
        }
        else
        {
            checks.Add("pipe-exchange-ok");
        }

        await pipeServerTask;

        var dllPath = Assembly.GetExecutingAssembly().Location;
        using var process = new RProcess($"functional.process.{Guid.NewGuid():N}")
        {
            StartInfo = new ProcessStartInfo("dotnet", $"\"{dllPath}\" --child --scenario probe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        var started = process.Start();
        if (!started)
        {
            failures.Add("process did not start.");
        }
        else
        {
            checks.Add("process-started");
        }

        var announceDeadline = DateTimeOffset.UtcNow.AddSeconds(3);
        var processGuardianAnnounced = false;
        while (DateTimeOffset.UtcNow < announceDeadline && !processGuardianAnnounced)
        {
            var leaseList = await hub.ExecuteAsync(new RuntimeDiagnosticAction
            {
                TargetId = "diagnostics.pipes",
                Action = "list"
            });
            if (leaseList.Success)
            {
                var leaseJson = JsonSerializer.SerializeToElement(leaseList.Value);
                processGuardianAnnounced = leaseJson.ValueKind == JsonValueKind.Array
                    && leaseJson.EnumerateArray().Any(x =>
                        x.TryGetProperty("RequestedName", out var requestedName)
                        && requestedName.GetString() == process.UnitId);
            }

            if (!processGuardianAnnounced)
            {
                await Task.Delay(120);
            }
        }

        if (!processGuardianAnnounced)
        {
            failures.Add("process guardian did not announce branch pipe to diagnostics.pipes.");
        }
        else
        {
            checks.Add("process-guardian-announced");
        }

        var processList = await hub.ExecuteAsync(new RuntimeDiagnosticAction
        {
            TargetId = "runtime.managed",
            Action = "processes"
        });
        if (!processList.Success)
        {
            failures.Add($"process list command failed: {processList.Error}");
        }
        else
        {
            var processJson = JsonSerializer.SerializeToElement(processList.Value);
            if (processJson.ValueKind != JsonValueKind.Array
                || !processJson.EnumerateArray().Any(x => x.TryGetProperty("UnitId", out var idNode) && idNode.GetString() == process.UnitId))
            {
                failures.Add("process list command did not list the active process.");
            }
            else
            {
                checks.Add("process-list-command-ok");
            }
        }

        var reflectionGet = await hub.ExecuteAsync(new RuntimeDiagnosticAction
        {
            TargetId = $"runtime.process.{process.UnitId}",
            Action = "get",
            Member = "UnitId"
        });
        if (!reflectionGet.Success)
        {
            failures.Add($"process reflection get failed: {reflectionGet.Error}");
        }
        else
        {
            var reflectedUnitId = JsonSerializer.SerializeToElement(reflectionGet.Value).GetString();
            if (!string.Equals(reflectedUnitId, process.UnitId, StringComparison.Ordinal))
            {
                failures.Add($"process reflection returned unexpected UnitId: {reflectedUnitId}");
            }
            else
            {
                checks.Add("process-reflection-access-ok");
            }
        }

        if (!process.WaitForExit(10_000))
        {
            failures.Add("probe process did not exit within timeout.");
        }
        else
        {
            checks.Add("process-exited");
        }

        var output = await process.StandardOutput.ReadToEndAsync();
        if (!output.Contains("probe child exited successfully", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("probe process output did not confirm success.");
        }
        else
        {
            checks.Add("probe-output-ok");
        }

        if (process.ExitCode != 0)
        {
            failures.Add($"probe process exit code was {process.ExitCode}.");
        }
        else
        {
            checks.Add("process-exit-code-zero");
        }

        if (managed.SnapshotRegistrations().Any(x => x.UnitId == process.UnitId))
        {
            failures.Add("process registration remained after exit.");
        }
        else
        {
            checks.Add("process-unregistered");
        }

        return failures.Count == 0
            ? FunctionalScenarioResult.Pass("process", checks.ToArray())
            : FunctionalScenarioResult.Fail("process", checks, failures);
    }

    private static async Task<FunctionalScenarioResult> RunTreeScenario(IServiceProvider provider)
    {
        var hub = provider.GetRequiredService<RuntimeDiagnosticHub>();
        var managed = provider.GetRequiredService<RuntimeManagedRegistry>();
        var execution = provider.GetRequiredService<RuntimeExecutionManager>();
        var breakpoints = provider.GetRequiredService<RuntimeDiagnosticBreakpoints>();
        var hooks = provider.GetRequiredService<RuntimeDiagnosticHooks>();
        var checks = new List<string>();
        var failures = new List<string>();
        var tree = new TreeScenarioModel();

        DiagnosticSwitchboard.SetGlobal(true);
        DiagnosticSwitchboard.SetSection("runtime", true);
        DiagnosticSwitchboard.SetPipeOutput(true);
        DiagnosticSwitchboard.SetOutputPoint("runtime.console", true);
        DiagnosticSwitchboard.SetOutputPoint("runtime.error", true);

        hub.RegisterObject("diagnostics.tree", tree, new RuntimeDiagnosticObjectAccess
        {
            AllowReadAllPublic = true,
            ReadableMembers = ["Root", "MutationCount", "UpdatedAt", "LastWorker", "TotalNodes", "LeafCount", "MaxDepth", "TotalValue"],
            InvokableMembers = ["Snapshot"]
        });

        if (!hooks.Attach("tree.updated"))
        {
            failures.Add("tree hook did not attach.");
        }
        else
        {
            checks.Add("tree-hook-attached");
        }

        if (!breakpoints.Enable("tree.fill") || !breakpoints.Enable("tree.stabilize"))
        {
            failures.Add("tree breakpoints were not enabled.");
        }
        else
        {
            checks.Add("tree-breakpoints-enabled");
        }

        var dllPath = Assembly.GetExecutingAssembly().Location;
        using var processOne = CreateTreeProcess(dllPath, $"functional.tree.process.1.{Guid.NewGuid():N}");
        using var processTwo = CreateTreeProcess(dllPath, $"functional.tree.process.2.{Guid.NewGuid():N}");
        var processOneStarted = processOne.Start();
        var processTwoStarted = processTwo.Start();
        if (processOneStarted && processTwoStarted)
        {
            checks.Add("two-processes-started");
        }
        else
        {
            failures.Add("one or both tree processes failed to start.");
        }

        managed.Register("functional.tree.thread.1", "thread", "FunctionalTree", CreateSnapshot("functional.tree.thread.1"));
        managed.Register("functional.tree.thread.2", "thread", "FunctionalTree", CreateSnapshot("functional.tree.thread.2"));

        var threadOneReady = new ManualResetEventSlim(false);
        var threadTwoReady = new ManualResetEventSlim(false);
        var treeStartGate = new ManualResetEventSlim(false);
        var threadOne = CreateTreeWorkerThread("functional.tree.thread.1", "tree-worker-1", tree, execution, managed, threadOneReady, treeStartGate);
        var threadTwo = CreateTreeWorkerThread("functional.tree.thread.2", "tree-worker-2", tree, execution, managed, threadTwoReady, treeStartGate);
        threadOne.Start();
        threadTwo.Start();

        if (!threadOneReady.Wait(TimeSpan.FromSeconds(2)) || !threadTwoReady.Wait(TimeSpan.FromSeconds(2)))
        {
            failures.Add("tree thread workers did not enter the ready state.");
        }

        treeStartGate.Set();

        if (!await WaitUntilAsync(() => tree.MutationCount > 0, TimeSpan.FromSeconds(2)))
        {
            failures.Add("tree did not mutate before join.");
        }
        else
        {
            checks.Add("tree-mutated-before-join");
        }

        if (!threadOne.Join(TimeSpan.FromSeconds(5)) || !threadTwo.Join(TimeSpan.FromSeconds(5)))
        {
            failures.Add("one or both tree threads did not complete within timeout.");
        }
        else
        {
            checks.Add("two-threads-completed");
        }

        await RuntimeOutput.BreakIf("tree.fill", () => tree.MutationCount > 0, tree.Snapshot());
        await RuntimeOutput.BreakIf("tree.stabilize", () => tree.MutationCount > 0, tree.Snapshot());

        if (!processOne.WaitForExit(10_000) || !processTwo.WaitForExit(10_000))
        {
            failures.Add("one or both tree processes did not exit within timeout.");
        }
        else
        {
            checks.Add("two-processes-exited");
        }

        var processOneOutput = await processOne.StandardOutput.ReadToEndAsync();
        var processTwoOutput = await processTwo.StandardOutput.ReadToEndAsync();
        if (!processOneOutput.Contains("tree-process", StringComparison.OrdinalIgnoreCase)
            || !processTwoOutput.Contains("tree-process", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("one or both tree process outputs did not confirm tree-process execution.");
        }
        else
        {
            checks.Add("tree-process-output-ok");
        }

        if (processOne.ExitCode != 0 || processTwo.ExitCode != 0)
        {
            failures.Add($"tree process exit codes were {processOne.ExitCode} and {processTwo.ExitCode}.");
        }
        else
        {
            checks.Add("tree-process-exit-code-zero");
        }

        var treeResult = await hub.ExecuteAsync(new RuntimeDiagnosticAction
        {
            TargetId = "diagnostics.tree",
            Action = "snapshot"
        });
        if (!treeResult.Success)
        {
            failures.Add($"tree snapshot failed: {treeResult.Error}");
        }
        else
        {
            checks.Add("tree-snapshot-ok");
        }

        var navigateResult = await hub.ExecuteAsync(new RuntimeDiagnosticAction
        {
            TargetId = "diagnostics.tree",
            Action = "navigate",
            Member = "Root.Children[0].Value"
        });
        if (!navigateResult.Success)
        {
            failures.Add($"tree navigation failed: {navigateResult.Error}");
        }
        else
        {
            checks.Add("tree-navigation-ok");
        }

        var stablePathResult = await hub.ExecuteAsync(new RuntimeDiagnosticAction
        {
            TargetId = "diagnostics.tree",
            Action = "navigate",
            Member = "Root.Children[0].Label"
        });
        if (!stablePathResult.Success)
        {
            failures.Add($"tree stable path navigation failed: {stablePathResult.Error}");
        }
        else
        {
            checks.Add("tree-stable-structure-ok");
        }

        var snapshotResult = await hub.ExecuteAsync(new RuntimeDiagnosticAction
        {
            TargetId = "diagnostics.tree",
            Action = "snapshot"
        });
        if (!snapshotResult.Success)
        {
            failures.Add($"tree snapshot query failed: {snapshotResult.Error}");
        }
        else
        {
            checks.Add("tree-snapshot-query-ok");
        }

        var registryResult = await hub.ExecuteAsync(new RuntimeDiagnosticAction
        {
            TargetId = "diagnostics.registry",
            Action = "all"
        });
        if (!registryResult.Success)
        {
            failures.Add($"registry query failed: {registryResult.Error}");
        }
        else
        {
            var registryJson = JsonSerializer.Serialize(registryResult.Value, JsonDefaults.Options);
            if (!registryJson.Contains("tree.root", StringComparison.OrdinalIgnoreCase)
                || !registryJson.Contains("tree.fill", StringComparison.OrdinalIgnoreCase)
                || !registryJson.Contains("tree.updated", StringComparison.OrdinalIgnoreCase))
            {
                failures.Add("registry snapshot did not expose the tree watchpoints/breakpoints/hook.");
            }
            else
            {
                checks.Add("registry-exposed-tree-declarations");
            }
        }

        var switchboard = DiagnosticSwitchboard.Snapshot();
        if (!switchboard.OutputPoints.Any(x => x.Id.Equals("runtime.console", StringComparison.OrdinalIgnoreCase) && x.Enabled)
            || !switchboard.OutputPoints.Any(x => x.Id.Equals("runtime.error", StringComparison.OrdinalIgnoreCase) && x.Enabled))
        {
            failures.Add("tree output points were not enabled.");
        }
        else
        {
            checks.Add("output-points-enabled");
        }

        if (!switchboard.Statements.Any(x => x.Section.Equals("runtime", StringComparison.OrdinalIgnoreCase) && x.Published > 0))
        {
            failures.Add("runtime statements were not published for tree mutations.");
        }
        else
        {
            checks.Add("tree-output-published");
        }

        if (tree.Root.Children.Count != 2 || tree.Root.Children.Any(child => child.Children.Count != 2))
        {
            failures.Add("tree structure was not stable.");
        }
        else
        {
            checks.Add("tree-structure-stable");
        }

        if (!tree.Root.Children.All(child => child.Path.StartsWith("root/", StringComparison.OrdinalIgnoreCase)))
        {
            failures.Add("tree paths were not stable.");
        }
        else
        {
            checks.Add("tree-paths-stable");
        }

        var executionSnapshot = execution.Snapshot();
        if (!executionSnapshot.DynamicThreads.Any(x => x.Id == "functional.tree.thread.1" && x.State == RuntimeThreadState.Completed)
            || !executionSnapshot.DynamicThreads.Any(x => x.Id == "functional.tree.thread.2" && x.State == RuntimeThreadState.Completed))
        {
            failures.Add("tree threads were not recorded as completed.");
        }
        else
        {
            checks.Add("tree-thread-state-recorded");
        }

        if (breakpoints.Snapshot().All(x => x.Id != "tree.fill" || x.HitCount == 0))
        {
            failures.Add("tree breakpoint was not hit.");
        }
        else
        {
            checks.Add("tree-breakpoint-hit");
        }

        if (!hooks.ActiveHooks().Any(x => x.HookId.Equals("tree.updated", StringComparison.OrdinalIgnoreCase)))
        {
            failures.Add("tree hook was not active.");
        }
        else
        {
            checks.Add("tree-hook-active");
        }

        return failures.Count == 0
            ? FunctionalScenarioResult.Pass("tree", checks.ToArray())
            : FunctionalScenarioResult.Fail("tree", checks, failures);
    }

    private static async Task<FunctionalScenarioResult> RunCliScenario(IServiceProvider provider, string diagnosticsPipeName)
    {
        var checks = new List<string>();
        var failures = new List<string>();

        var rootDirectory = ResolveRepositoryRoot();
        var cliProjectPath = Path.Combine(rootDirectory, "Iwesun.Runtime.Cli", "Iwesun.Runtime.Cli.csproj");
        var cliConfigPath = Path.Combine(rootDirectory, "Iwesun.Runtime.Cli", "Iwesun.Runtime.Cli.commands.v2.json");
        if (!File.Exists(cliProjectPath))
        {
            failures.Add($"CLI project not found: {cliProjectPath}");
            return FunctionalScenarioResult.Fail("cli", checks, failures);
        }

        if (!File.Exists(cliConfigPath))
        {
            failures.Add($"CLI config not found: {cliConfigPath}");
            return FunctionalScenarioResult.Fail("cli", checks, failures);
        }

        var cliConfig = JsonSerializer.Deserialize<CommandConfig>(await File.ReadAllTextAsync(cliConfigPath), JsonDefaults.Options);
        if (cliConfig == null)
        {
            failures.Add("CLI config could not be parsed.");
            return FunctionalScenarioResult.Fail("cli", checks, failures);
        }

        if (!cliConfig.Pipes.TryGetValue("diagnostics", out var diagnosticsPipeFromConfig)
            || string.IsNullOrWhiteSpace(diagnosticsPipeFromConfig))
        {
            failures.Add("CLI config missing pipes.diagnostics (first pipe slot).");
            return FunctionalScenarioResult.Fail("cli", checks, failures);
        }
        checks.Add("cli-diagnostics-pipe-slot-present");

        var commandNames = cliConfig.BaseCommands.Select(command => command.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var requiredCommands = new[] { "bp.listNumeric", "bp.setNumeric", "bp.setNumericThreshold", "bp.clearNumeric", "process.list", "thread.list" };
        foreach (var requiredCommand in requiredCommands)
        {
            if (!commandNames.Contains(requiredCommand))
            {
                failures.Add($"CLI config missing command definition: {requiredCommand}");
            }
        }
        if (failures.Count > 0)
        {
            return FunctionalScenarioResult.Fail("cli", checks, failures);
        }
        checks.Add("cli-numeric-command-definitions-present");

        checks.Add($"diagnostics-pipe:{diagnosticsPipeName}");

        var psi = new ProcessStartInfo("dotnet", $"run --project \"{cliProjectPath}\" -- --config=\"{cliConfigPath}\" --pipe={diagnosticsPipeName} host.info")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = rootDirectory
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start CLI process.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            failures.Add($"CLI exit code was {process.ExitCode}. stderr={stderr.Trim()}");
        }
        else
        {
            checks.Add("cli-exit-code-zero");
        }

        RuntimeDiagnosticFrame? responseFrame;
        try
        {
            responseFrame = JsonSerializer.Deserialize<RuntimeDiagnosticFrame>(stdout, JsonDefaults.Options);
        }
        catch (Exception ex)
        {
            failures.Add($"CLI output was not valid JSON: {ex.GetType().Name}: {ex.Message}. stdout={stdout.Trim()} stderr={stderr.Trim()}");
            return FunctionalScenarioResult.Fail("cli", checks, failures);
        }

        if (responseFrame?.Status?.Ok != true)
        {
            failures.Add($"CLI response status was not ok: {responseFrame?.Status?.Code} {responseFrame?.Status?.Message}. stdout={stdout.Trim()} stderr={stderr.Trim()}");
        }
        else
        {
            checks.Add("cli-frame-success");
        }

        if (responseFrame?.Data is not JsonElement data || data.ValueKind != JsonValueKind.Object)
        {
            failures.Add($"CLI response did not contain host snapshot data. stdout={stdout.Trim()} stderr={stderr.Trim()}");
            return failures.Count == 0
                ? FunctionalScenarioResult.Pass("cli", checks.ToArray())
                : FunctionalScenarioResult.Fail("cli", checks, failures);
        }

        if (!data.TryGetProperty("RuntimeVersion", out _)
            || !data.TryGetProperty("RegisteredTargets", out var registeredTargets)
            || !data.TryGetProperty("ProcessId", out _))
        {
            failures.Add($"CLI response did not contain host snapshot fields. stdout={stdout.Trim()} stderr={stderr.Trim()}");
        }
        else
        {
            checks.Add("cli-host-info-output-ok");
            if (registeredTargets.ValueKind != JsonValueKind.Array || registeredTargets.GetArrayLength() == 0)
            {
                failures.Add("CLI host snapshot did not expose any registered targets.");
            }
        }

        return failures.Count == 0
            ? FunctionalScenarioResult.Pass("cli", checks.ToArray())
            : FunctionalScenarioResult.Fail("cli", checks, failures);
    }

    private static RProcess CreateTreeProcess(string dllPath, string unitId)
    {
        return new RProcess(unitId)
        {
            StartInfo = new ProcessStartInfo("dotnet", $"\"{dllPath}\" --child --scenario tree-process")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
    }

    private static RThread CreateTreeWorkerThread(
        string unitId,
        string name,
        TreeScenarioModel tree,
        RuntimeExecutionManager execution,
        RuntimeManagedRegistry managed,
        ManualResetEventSlim readySignal,
        ManualResetEventSlim startGate)
    {
        return new RThread(() =>
        {
            readySignal.Set();
            startGate.Wait();
            var rng = new Random(unchecked(Environment.TickCount ^ Thread.CurrentThread.ManagedThreadId ^ unitId.GetHashCode(StringComparison.OrdinalIgnoreCase)));
            for (var i = 0; i < 6; i++)
            {
                Thread.Sleep(35 + rng.Next(0, 35));
                var mutation = tree.Mutate(unitId, rng);
                execution.HeartbeatThread(unitId, new
                {
                    mutation.Iteration,
                    mutation.NodePath,
                    mutation.NodeValue,
                    mutation.TotalNodes,
                    mutation.MaxDepth,
                    tree.MutationCount
                });
                managed.PublishEvent(unitId, "tree-mutated", "Tree worker mutated a node.", new { mutation.Iteration, mutation.NodePath, mutation.NodeValue });
                RuntimeOutput.TracePoint("runtime.console", "runtime", "tree.mutation", $"Tree mutated by {unitId}", new { unitId, mutation.NodePath, mutation.NodeValue, mutation.TotalNodes });
                if (i == 2)
                {
                    RuntimeOutput.TracePoint("runtime.error", "runtime", "tree.checkpoint", $"Tree checkpoint reached by {unitId}", new { unitId, mutation.NodePath, mutation.NodeValue });
                }
            }
        }, unitId, name, RuntimeExecutionLifetime.Dynamic, RuntimeThreadKind.Worker, "FunctionalTests", nameof(RunTreeScenario));
    }

    private static async Task<FunctionalScenarioResult> RunTreeProcessScenario(IServiceProvider provider)
    {
        var managed = provider.GetRequiredService<RuntimeManagedRegistry>();
        var checks = new List<string>();
        var failures = new List<string>();
        var tree = TreeScenarioModel.CreateDefault();
        var rng = new Random(unchecked(Environment.TickCount ^ Environment.CurrentManagedThreadId ^ Guid.NewGuid().GetHashCode()));

        DiagnosticSwitchboard.SetGlobal(true);
        DiagnosticSwitchboard.SetSection("runtime", true);
        DiagnosticSwitchboard.SetPipeOutput(true);

        for (var i = 0; i < 4; i++)
        {
            Thread.Sleep(30 + rng.Next(0, 20));
            var mutation = tree.Mutate($"process-{Environment.ProcessId}", rng);
            managed.PublishEvent($"process-{Environment.ProcessId}", "tree-process-mutated", "Child process mutated its tree.", new { mutation.Iteration, mutation.NodePath, mutation.NodeValue });
            RuntimeOutput.TracePoint("runtime.console", "runtime", "tree.process", $"Tree process mutation {mutation.Iteration}", new { mutation.NodePath, mutation.NodeValue, mutation.TotalNodes });
            if (i == 1)
            {
                await RuntimeOutput.BreakIf("tree.stabilize", () => true, tree.Snapshot());
            }
        }

        var snapshot = tree.Snapshot();
        checks.Add($"tree-process-nodes:{snapshot.TotalNodes}");
        checks.Add($"tree-process-depth:{snapshot.MaxDepth}");
        checks.Add($"tree-process-total:{snapshot.TotalValue}");

        return FunctionalScenarioResult.Pass("tree-process", checks.ToArray());
    }

    private static string ResolveRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Iwesun.Runtime.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Unable to locate repository root.");
    }

    private static async Task<string> HandlePipeServerAsync(string pipeName, string expectedResponse)
    {
        await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await server.WaitForConnectionAsync();

        var lengthBuffer = new byte[4];
        await ReadExactAsync(server, lengthBuffer);
        var requestLength = BitConverter.ToInt32(lengthBuffer, 0);
        if (requestLength <= 0 || requestLength > 1024 * 1024)
        {
            throw new InvalidOperationException($"Invalid request length: {requestLength}.");
        }

        var requestBuffer = new byte[requestLength];
        await ReadExactAsync(server, requestBuffer);
        var request = Encoding.UTF8.GetString(requestBuffer);
        if (string.IsNullOrWhiteSpace(request))
        {
            throw new InvalidOperationException("Request payload was empty.");
        }

        var responseBuffer = Encoding.UTF8.GetBytes(expectedResponse);
        await server.WriteAsync(BitConverter.GetBytes(responseBuffer.Length));
        await server.WriteAsync(responseBuffer);
        await server.FlushAsync();
        return request;
    }

    private static async Task<RuntimeDiagnosticFrame> SendFrameAsync(string pipeName, RuntimeDiagnosticFrame frame)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        Exception? lastError = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                await using var client = new NamedPipeClientStream(
                    ".",
                    pipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous);
                using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await client.ConnectAsync(connectCts.Token);

                var json = JsonSerializer.Serialize(frame, JsonDefaults.Options);
                var bytes = Encoding.UTF8.GetBytes(json);
                await client.WriteAsync(BitConverter.GetBytes(bytes.Length));
                await client.WriteAsync(bytes);
                await client.FlushAsync();

                var responseLengthBuffer = new byte[4];
                await ReadExactAsync(client, responseLengthBuffer);
                var responseLength = BitConverter.ToInt32(responseLengthBuffer, 0);
                if (responseLength <= 0 || responseLength > 1024 * 1024)
                {
                    throw new InvalidOperationException($"Invalid response length: {responseLength}.");
                }

                var responseBuffer = new byte[responseLength];
                await ReadExactAsync(client, responseBuffer);
                var responseJson = Encoding.UTF8.GetString(responseBuffer);
                return JsonSerializer.Deserialize<RuntimeDiagnosticFrame>(responseJson, JsonDefaults.Options)
                    ?? throw new InvalidOperationException("Diagnostic response was null.");
            }
            catch (UnauthorizedAccessException ex)
            {
                lastError = ex;
            }
            catch (IOException ex)
            {
                lastError = ex;
            }

            await Task.Delay(100);
        }

        throw new InvalidOperationException($"Failed to connect to diagnostics pipe '{pipeName}'.", lastError);
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset));
            if (read == 0)
            {
                throw new EndOfStreamException("Pipe closed before the payload was fully read.");
            }

            offset += read;
        }
    }

    private static RManagedStateSnapshot CreateSnapshot(string unitId)
    {
        var manager = new RuntimeStateManager(RuntimeStateCatalog.CreateOnlineDefaults());
        return new RManagedStateSnapshot(unitId, manager.Snapshot(), new Dictionary<string, string>());
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var stopAt = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < stopAt)
        {
            if (predicate())
            {
                return true;
            }

            await Task.Delay(50);
        }

        return predicate();
    }
}

internal static class TreeScenarioSignals
{
    public static event EventHandler<TreeScenarioChangedEventArgs>? Updated;

    public static void RaiseUpdated(TreeScenarioSnapshot snapshot)
    {
        Updated?.Invoke(null, new TreeScenarioChangedEventArgs(snapshot));
    }
}

internal sealed class TreeScenarioChangedEventArgs(TreeScenarioSnapshot snapshot) : EventArgs
{
    public TreeScenarioSnapshot Snapshot { get; } = snapshot;
}

internal sealed class TreeScenarioModel
{
    private readonly object _gate = new();
    private readonly TreeNode _root = CreateDefaultRoot();

    public static TreeScenarioModel CreateDefault() => new();

    public TreeNode Root
    {
        get { lock (_gate) { return _root; } }
    }

    public int MutationCount { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public string LastWorker { get; private set; } = "";
    public int TotalNodes { get { lock (_gate) { return CountNodes(_root); } } }
    public int LeafCount { get { lock (_gate) { return CountLeaves(_root); } } }
    public int MaxDepth { get { lock (_gate) { return MeasureDepth(_root); } } }
    public int TotalValue { get { lock (_gate) { return SumValues(_root); } } }

    public TreeMutationResult Mutate(string workerId, Random rng)
    {
        lock (_gate)
        {
            var target = PickNode(_root, rng);
            target.Value += rng.Next(1, 9);
            MutationCount++;
            LastWorker = workerId;
            UpdatedAt = DateTimeOffset.UtcNow;
            var totalNodes = CountNodes(_root);
            var leafCount = CountLeaves(_root);
            var maxDepth = MeasureDepth(_root);
            var totalValue = SumValues(_root);
            var snapshot = new TreeScenarioSnapshot(_root, MutationCount, UpdatedAt, LastWorker, totalNodes, leafCount, maxDepth, totalValue);
            var result = new TreeMutationResult(MutationCount, BuildPath(target), target.Value, totalNodes, maxDepth, totalValue);
            TreeScenarioSignals.RaiseUpdated(snapshot);
            return result;
        }
    }

    public TreeScenarioSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new TreeScenarioSnapshot(_root, MutationCount, UpdatedAt, LastWorker, CountNodes(_root), CountLeaves(_root), MeasureDepth(_root), SumValues(_root));
        }
    }

    private static TreeNode PickNode(TreeNode root, Random rng)
    {
        var nodes = EnumerateNodes(root).ToArray();
        return nodes[rng.Next(nodes.Length)];
    }

    private static TreeNode CreateDefaultRoot()
    {
        var root = new TreeNode("root", 10);
        var left = new TreeNode("left", 7);
        left.Attach(new TreeNode("left.left", 3));
        left.Attach(new TreeNode("left.right", 4));
        var right = new TreeNode("right", 8);
        right.Attach(new TreeNode("right.left", 5));
        right.Attach(new TreeNode("right.right", 6));
        root.Attach(left);
        root.Attach(right);
        return root;
    }

    private static IEnumerable<TreeNode> EnumerateNodes(TreeNode node)
    {
        yield return node;
        foreach (var child in node.Children)
        {
            foreach (var nested in EnumerateNodes(child))
            {
                yield return nested;
            }
        }
    }

    private static int CountNodes(TreeNode node) => 1 + node.Children.Sum(CountNodes);
    private static int CountLeaves(TreeNode node) => node.Children.Count == 0 ? 1 : node.Children.Sum(CountLeaves);
    private static int MeasureDepth(TreeNode node) => node.Children.Count == 0 ? 1 : 1 + node.Children.Max(MeasureDepth);
    private static int SumValues(TreeNode node) => node.Value + node.Children.Sum(SumValues);
    private static string BuildPath(TreeNode node) => node.Path;
}

internal sealed record TreeScenarioSnapshot(
    TreeNode Root,
    int MutationCount,
    DateTimeOffset UpdatedAt,
    string LastWorker,
    int TotalNodes,
    int LeafCount,
    int MaxDepth,
    int TotalValue);

internal sealed record TreeMutationResult(
    int Iteration,
    string NodePath,
    int NodeValue,
    int TotalNodes,
    int MaxDepth,
    int TotalValue);

internal sealed class TreeNode(string label, int value)
{
    public string Label { get; set; } = label;
    public int Value { get; set; } = value;
    public List<TreeNode> Children { get; set; } = [];
    private string _path = "";
    public string Path => string.IsNullOrWhiteSpace(_path) ? ParentPath(this) : _path;
    private TreeNode? Parent { get; set; }

    public TreeNode Attach(TreeNode child)
    {
        child.Parent = this;
        child.UpdatePathRecursive(ParentPath(this));
        Children.Add(child);
        return this;
    }

    private void UpdatePathRecursive(string parentPath)
    {
        _path = string.IsNullOrWhiteSpace(parentPath) ? Label : $"{parentPath}/{Label}";
        foreach (var child in Children)
        {
            child.Parent = this;
            child.UpdatePathRecursive(_path);
        }
    }

    private static string ParentPath(TreeNode node)
    {
        var parts = new Stack<string>();
        var current = node;
        while (current != null)
        {
            parts.Push(current.Label);
            current = current.Parent;
        }

        return string.Join('/', parts);
    }
}

static class FunctionalArgs
{
    public static bool Contains(string[] args, string key) =>
        args.Any(x => x.Equals(key, StringComparison.OrdinalIgnoreCase));

    public static string? GetValue(string[] args, string key)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Equals(key, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return args[i + 1];
            }

            if (arg.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
            {
                return arg[(key.Length + 1)..];
            }
        }

        return null;
    }
}

internal static class FunctionalHelp
{
    public static void Print()
    {
        Console.WriteLine("Iwesun.Runtime.FunctionalTests");
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project Iwesun.Runtime.FunctionalTests");
        Console.WriteLine("  dotnet run --project Iwesun.Runtime.FunctionalTests -- --child --scenario diagnostics|managed|thread|task|process|tree|root-safety|sharedfifo-protocol|numeric-breakpoint|pipe-registry|tree-process|cli|probe");
    }
}

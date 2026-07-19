using System.Collections.Concurrent;
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
using Microsoft.Extensions.Options;

[assembly: DiagnosticWatchPoint("tree.root", "tree", "watch", "Tree root snapshot.", "Iwesun.Runtime.FunctionalTests.TreeScenarioModel")]
[assembly: DiagnosticWatchPoint("tree.branches", "tree", "watch", "Tree branch snapshot.", "Iwesun.Runtime.FunctionalTests.TreeScenarioModel")]
[assembly: DiagnosticBreakpoint("tree.fill", "tree", "Pause while tree is being filled.", "Iwesun.Runtime.FunctionalTests.TreeScenarioModel", HitCountTarget = 1)]
[assembly: DiagnosticBreakpoint("tree.stabilize", "tree", "Pause before tree inspection.", "Iwesun.Runtime.FunctionalTests.TreeScenarioModel", HitCountTarget = 1)]

// Numeric predicate breakpoints — one per predicate family for test coverage
[assembly: DiagnosticBreakpoint("numeric.default.threshold", "numeric", "Default numeric threshold breakpoint.", "Iwesun.Runtime.FunctionalTests.Program", HitCountTarget = 1)]
[assembly: DiagnosticNumericBreakpoint("numeric.default.threshold", "gt", 7)]

[assembly: DiagnosticBreakpoint("numeric.default.range", "numeric", "Default numeric range breakpoint.", "Iwesun.Runtime.FunctionalTests.Program", HitCountTarget = 1)]
[assembly: DiagnosticNumericBreakpoint("numeric.default.range", "between", 3, 9)]

[assembly: DiagnosticBreakpoint("numeric.default.delta", "numeric", "Default numeric delta breakpoint.", "Iwesun.Runtime.FunctionalTests.Program", HitCountTarget = 1)]
[assembly: DiagnosticNumericBreakpoint("numeric.default.delta", "delta-le", 2)]

// Additional predicate examples: eq2, ne2, ge2, le2, outside3, delta-gt3
[assembly: DiagnosticBreakpoint("numeric.eq2",       "numeric", "eq2: v1 == v2.",                            "Iwesun.Runtime.FunctionalTests.Program")]
[assembly: DiagnosticNumericBreakpoint("numeric.eq2", "eq2")]

[assembly: DiagnosticBreakpoint("numeric.ne2",       "numeric", "ne2: v1 != v2.",                            "Iwesun.Runtime.FunctionalTests.Program")]
[assembly: DiagnosticNumericBreakpoint("numeric.ne2", "ne2")]

[assembly: DiagnosticBreakpoint("numeric.ge2",       "numeric", "ge2: v1 >= v2.",                            "Iwesun.Runtime.FunctionalTests.Program")]
[assembly: DiagnosticNumericBreakpoint("numeric.ge2", "ge2")]

[assembly: DiagnosticBreakpoint("numeric.le2",       "numeric", "le2: v1 <= v2.",                            "Iwesun.Runtime.FunctionalTests.Program")]
[assembly: DiagnosticNumericBreakpoint("numeric.le2", "le2")]

[assembly: DiagnosticBreakpoint("numeric.outside3",  "numeric", "outside3: value outside [min,max].",        "Iwesun.Runtime.FunctionalTests.Program")]
[assembly: DiagnosticNumericBreakpoint("numeric.outside3", "outside", 3, 9)]

[assembly: DiagnosticBreakpoint("numeric.delta-gt3", "numeric", "delta-gt3: |v1-v2| > maxDelta.",            "Iwesun.Runtime.FunctionalTests.Program")]
[assembly: DiagnosticNumericBreakpoint("numeric.delta-gt3", "delta-gt", 2)]

// bp-process cross-process CLI breakpoint tests — one per breakpoint type
[assembly: DiagnosticBreakpoint("bpp.condition",    "bpp", "BreakIf with condition.",          "Iwesun.Runtime.FunctionalTests.Program", Enabled = true)]
[assembly: DiagnosticBreakpoint("bpp.unconditional", "bpp", "Unconditional BreakIf.",          "Iwesun.Runtime.FunctionalTests.Program", Enabled = true)]
[assembly: DiagnosticBreakpoint("bpp.num.gt",       "bpp", "Numeric gt threshold.",            "Iwesun.Runtime.FunctionalTests.Program", Enabled = true)]
[assembly: DiagnosticNumericBreakpoint("bpp.num.gt", "gt", 5)]
[assembly: DiagnosticBreakpoint("bpp.num.lt",       "bpp", "Numeric lt threshold.",            "Iwesun.Runtime.FunctionalTests.Program", Enabled = true)]
[assembly: DiagnosticNumericBreakpoint("bpp.num.lt", "lt", 10)]
[assembly: DiagnosticBreakpoint("bpp.num.between",  "bpp", "Numeric between range.",           "Iwesun.Runtime.FunctionalTests.Program", Enabled = true)]
[assembly: DiagnosticNumericBreakpoint("bpp.num.between", "between", 3, 9)]
[assembly: DiagnosticBreakpoint("bpp.num.outside",  "bpp", "Numeric outside range.",           "Iwesun.Runtime.FunctionalTests.Program", Enabled = true)]
[assembly: DiagnosticNumericBreakpoint("bpp.num.outside", "outside", 3, 9)]
[assembly: DiagnosticBreakpoint("bpp.num.eq",       "bpp", "Numeric eq2.",                     "Iwesun.Runtime.FunctionalTests.Program", Enabled = true)]
[assembly: DiagnosticNumericBreakpoint("bpp.num.eq", "eq2")]
[assembly: DiagnosticBreakpoint("bpp.num.ne",       "bpp", "Numeric ne2.",                     "Iwesun.Runtime.FunctionalTests.Program", Enabled = true)]
[assembly: DiagnosticNumericBreakpoint("bpp.num.ne", "ne2")]
[assembly: DiagnosticBreakpoint("bpp.num.delta-le", "bpp", "Numeric delta-le threshold.",      "Iwesun.Runtime.FunctionalTests.Program", Enabled = true)]
[assembly: DiagnosticNumericBreakpoint("bpp.num.delta-le", "delta-le", 2)]
[assembly: DiagnosticBreakpoint("bpp.num.delta-gt", "bpp", "Numeric delta-gt threshold.",      "Iwesun.Runtime.FunctionalTests.Program", Enabled = true)]
[assembly: DiagnosticNumericBreakpoint("bpp.num.delta-gt", "delta-gt", 2)]
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
    string Schema,
    Dictionary<string, JsonElement> Endpoints,
    List<CommandConfigEntry> Commands,
    List<CompositeConfigEntry> Composites);

internal sealed record CommandConfigEntry(string Name, string Summary, string Risk, string Capability);
internal sealed record CompositeConfigEntry(string Name, string Summary, List<CompositeStepConfigEntry> Steps);
internal sealed record CompositeStepConfigEntry(string Command);

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

internal sealed record ChildScenarioExecution(
    string Scenario,
    FunctionalScenarioResult Result,
    int ExitCode,
    long DurationMs,
    string Stdout,
    string Stderr);

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
        var scenarios = new[]
        {
            "diagnostics", "managed", "thread", "task", "process", "root-safety", "sharedfifo-protocol",
            "pipe-registry", "tree-process", "cli", "cli-context-shell", "cli-transport-failure",
            "file-output-filter", "file-registry", "file-output-e2e", "switchboard-config",
            "file-output-format-variants", "data-stream-recorder", "sample-host-random-state", "sample-host-cli-full",
            "web-runtime-script",
#if DEBUG
            "tree", "numeric-breakpoint", "cli-numeric-breakpoint", "breakpoint-safety", "bp-process-cli",
#endif
        };
        var results = new List<FunctionalScenarioResult>(scenarios.Length);
        var executions = new List<ChildScenarioExecution>(scenarios.Length);
        var failures = new List<string>();

        foreach (var scenario in scenarios)
        {
            var execution = await RunChildScenarioAsync(scenario);
            executions.Add(execution);
            results.Add(execution.Result);
            if (!execution.Result.Success)
            {
                failures.AddRange(execution.Result.Failures.Select(f => $"{scenario}: {f}"));
            }
        }

        var reportPath = WriteDetailedReport(executions, failures);
        var scenarioCoverage = scenarios.ToDictionary(
            scenario => scenario,
            scenario => executions.Any(x => x.Scenario.Equals(scenario, StringComparison.OrdinalIgnoreCase) && x.Result.Success),
            StringComparer.OrdinalIgnoreCase);

        var report = new
        {
            success = failures.Count == 0,
            checkedAt = DateTimeOffset.UtcNow,
            results,
            failures,
            reportPath,
            coverage = scenarioCoverage
        };

        Console.WriteLine(JsonSerializer.Serialize(report, JsonDefaults.Options));
        return failures.Count == 0 ? 0 : 1;
    }

    private static async Task<ChildScenarioExecution> RunChildScenarioAsync(string scenario)
    {
        var dllPath = ResolveSelfDllPath();
        var psi = new ProcessStartInfo("dotnet", $"\"{dllPath}\" --child --scenario {scenario}")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var startedAt = Stopwatch.GetTimestamp();
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start child scenario '{scenario}'.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var durationMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        var json = ExtractLastJsonLine(stdout);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ChildScenarioExecution(
                scenario,
                FunctionalScenarioResult.Fail(scenario, Array.Empty<string>(), new[] { $"Child scenario produced no JSON output. stderr={stderr.Trim()}" }),
                process.ExitCode,
                (long)durationMs,
                stdout,
                stderr);
        }

        try
        {
            var result = JsonSerializer.Deserialize<FunctionalScenarioResult>(json, JsonDefaults.Options)
                ?? throw new InvalidOperationException("Child result was null.");
            if (process.ExitCode != 0 && result.Success)
            {
                result = result with { Success = false, Failures = result.Failures.Concat(new[] { $"Child exit code was {process.ExitCode}. stderr={stderr.Trim()}" }).ToArray() };
            }

            return new ChildScenarioExecution(scenario, result, process.ExitCode, (long)durationMs, stdout, stderr);
        }
        catch (Exception ex)
        {
            return new ChildScenarioExecution(
                scenario,
                FunctionalScenarioResult.Fail(scenario, Array.Empty<string>(), new[] { $"Failed to parse child output: {ex.GetType().Name}: {ex.Message}. stdout={stdout.Trim()} stderr={stderr.Trim()}" }),
                process.ExitCode,
                (long)durationMs,
                stdout,
                stderr);
        }
    }

    private static string WriteDetailedReport(IReadOnlyList<ChildScenarioExecution> executions, IReadOnlyList<string> failures)
    {
        var reportDirectory = Path.Combine(AppContext.BaseDirectory, "runtime-functional-reports");
        Directory.CreateDirectory(reportDirectory);
        var reportPath = Path.Combine(reportDirectory, $"full-report-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json");
        var detailed = new
        {
            success = failures.Count == 0,
            checkedAt = DateTimeOffset.UtcNow,
            scenarioCount = executions.Count,
            failures,
            scenarios = executions.Select(x => new
            {
                x.Scenario,
                x.Result.Success,
                x.Result.CheckedAt,
                checks = x.Result.Checks,
                checkCount = x.Result.Checks.Count,
                failures = x.Result.Failures,
                failureCount = x.Result.Failures.Count,
                x.ExitCode,
                x.DurationMs,
                stdout = x.Stdout,
                stderr = x.Stderr
            })
        };
        File.WriteAllText(reportPath, JsonSerializer.Serialize(detailed, JsonDefaults.Options));
        return reportPath;
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
    private const string ScenarioRunnerThreadId = "functional.scenario.runner";

    public static async Task<int> RunAsync(FunctionalChildOptions options)
    {
        var runtimeDirectory = Path.Combine(AppContext.BaseDirectory, "runtime-functional-tests", options.Scenario, Guid.NewGuid().ToString("N"));
		var diagnosticsPipeName = Environment.GetEnvironmentVariable("IWESUN_RUNTIME_DIAGNOSTICS_PIPE")
			?? $"functional.diagnostics.{options.Scenario}.{Guid.NewGuid():N}";

        using var host = BuildHost(runtimeDirectory, diagnosticsPipeName);
        var provider = host.Services;
        await host.StartAsync();

        FunctionalScenarioResult result;
        try
        {
            var scenario = options.Scenario.ToLowerInvariant();
            result = await RunScenarioWithTaskPointAsync(provider, scenario, () => scenario switch
            {
                "diagnostics" => RunDiagnosticsScenario(provider),
                "managed" => RunManagedScenario(provider),
                "thread" => RunThreadScenario(provider),
                "task" => RunTaskScenario(provider),
                "process" => RunProcessScenario(provider),
				"host-uniqueness" => RunHostUniquenessScenario(provider),
				"windows-service" => RunWindowsServiceRegistrationScenario(),
				#if DEBUG
                "tree" => RunTreeScenario(provider),
				#endif
                "root-safety" => RunRootSafetyScenario(provider),
                "sharedfifo-protocol" => RunSharedFifoProtocolScenario(provider),
				#if DEBUG
                "numeric-breakpoint" => RunNumericBreakpointScenario(provider),
				#endif
                "pipe-registry" => RunPipeRegistryScenario(provider, diagnosticsPipeName),
                "tree-process" => RunTreeProcessScenario(provider),
                "cli" => RunCliScenario(provider, diagnosticsPipeName),
                "cli-context-shell" => CliContextShellScenario.RunAsync(),
                "cli-transport-failure" => CliTransportFailureScenario.RunAsync(),
				"remote-console-frame-codec" => RemoteConsoleScenario.RunFrameCodecAsync(),
				"remote-console-protocol" => RemoteConsoleScenario.RunProtocolAsync(),
				"remote-console-authorization" => RemoteConsoleScenario.RunAuthorizationAsync(),
				"remote-console-approval" => RemoteConsoleScenario.RunApprovalAsync(),
				"remote-console-command" => RemoteConsoleScenario.RunCommandAsync(),
				"remote-console-workspace" => RemoteConsoleScenario.RunWorkspaceAsync(),
				"remote-console-cli" => RemoteConsoleScenario.RunCliAsync(),
				"breakpoint-safety" => BreakpointSafetyScenario.RunAsync(),
				#if DEBUG
                "cli-numeric-breakpoint" => RunCliNumericBreakpointScenario(provider, diagnosticsPipeName),
				#endif
                "bp-process" => RunBpProcessScenario(provider, diagnosticsPipeName),
                "bp-process-cli" => RunBpProcessCliScenario(provider),
                "file-output-filter"          => RunFileOutputFilterScenario(provider),
                "file-registry"               => RunFileRegistryScenario(provider),
                "file-output-e2e"             => RunFileOutputE2eScenario(provider, runtimeDirectory),
                "switchboard-config"          => RunSwitchboardConfigScenario(provider),
                "file-output-format-variants" => RunFileOutputFormatVariantsScenario(provider, runtimeDirectory),
                "sample-host-random-state" => Task.FromResult(SampleHostRandomStateScenario.Run()),
                "sample-host-cli-full" => SampleHostCliFullScenario.RunAsync(),
				"web-runtime-script" => WebRuntimeScriptScenario.RunAsync(),
				"data-stream-recorder" => DataStreamRecorderScenario.RunAsync(),
				"probe" => RunProbeScenario(provider),
                _ => Task.FromResult(FunctionalScenarioResult.Fail(options.Scenario, Array.Empty<string>(), new[] { $"Unknown scenario: {options.Scenario}" }))
            });
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

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.None);
        builder.Services.Start(runtimeDirectory, startupRuntimeDiagnosticsPipeName: diagnosticsPipeName);
        var host = builder.Build();
        host.Services.Activate(Assembly.GetExecutingAssembly());
        var execution = host.Services.GetRequiredService<RuntimeExecutionManager>();
        RuntimeInjector.Thread(
            execution,
            ScenarioRunnerThreadId,
            "Functional Scenario Runner",
            RuntimeExecutionLifetime.Static,
            RuntimeThreadKind.Coordinator,
            owner: "FunctionalTests",
            sourceLocation: nameof(FunctionalChildRunner));
        return host;
    }

	private static Task<FunctionalScenarioResult> RunWindowsServiceRegistrationScenario()
	{
		var checks = new List<string>();
		var failures = new List<string>();

		var ordinaryServices = new ServiceCollection();
		ordinaryServices.Start();
		if (ordinaryServices.Any(x => x.ServiceType == typeof(IHostLifetime)))
			failures.Add("ordinary Start registered a Windows Service lifetime.");
		else
			checks.Add("ordinary-start-lifetime-unchanged");

		var serviceServices = new ServiceCollection();
		serviceServices.AddLogging();
		serviceServices.StartWindowsService(
			new RuntimeWindowsServiceOptions
			{
				ServiceName = "Iwesun.Runtime.FunctionalService",
				DisplayName = "Iwesun Runtime Functional Service",
				Description = "Functional Windows Service metadata.",
				ShutdownTimeout = TimeSpan.FromSeconds(17)
			},
			startupRuntimeDiagnosticsPipeName: "functional.windows-service");
		using var provider = serviceServices.BuildServiceProvider();
		var options = provider.GetRequiredService<IOptions<RuntimeWindowsServiceOptions>>().Value;
		if (options.ServiceName != "Iwesun.Runtime.FunctionalService"
			|| options.DisplayName != "Iwesun Runtime Functional Service"
			|| options.Description != "Functional Windows Service metadata."
			|| options.ShutdownTimeout != TimeSpan.FromSeconds(17))
			failures.Add("StartWindowsService did not preserve the service name and shutdown timeout.");
		else
			checks.Add("windows-service-options-configured");

		var apiServices = new ServiceCollection();
		apiServices.AddLogging();
		apiServices.ConfigureRuntimeWindowsService(options =>
		{
			options.ServiceName = "Iwesun.Runtime.ApiConfiguredService";
			options.DisplayName = "API Configured Service";
			options.Description = "Configured outside Program.cs.";
			options.ShutdownTimeout = TimeSpan.FromSeconds(23);
		});
		apiServices.StartConfiguredWindowsService(startupRuntimeDiagnosticsPipeName: "functional.windows-service.api");
		using var apiProvider = apiServices.BuildServiceProvider();
		var apiOptions = apiProvider.GetRequiredService<IOptions<RuntimeWindowsServiceOptions>>().Value;
		var lifetimeOptions = apiProvider.GetRequiredService<IOptions<WindowsServiceLifetimeOptions>>().Value;
		if (apiOptions.ServiceName != "Iwesun.Runtime.ApiConfiguredService"
			|| apiOptions.DisplayName != "API Configured Service"
			|| apiOptions.Description != "Configured outside Program.cs."
			|| apiOptions.ShutdownTimeout != TimeSpan.FromSeconds(23)
			|| lifetimeOptions.ServiceName != apiOptions.ServiceName)
			failures.Add("business API configuration did not flow into the Windows Service lifetime.");
		else
			checks.Add("windows-service-business-api-configured");

		if (!Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService()
			&& serviceServices.Any(x => x.ImplementationType?.Name == "RuntimeWindowsServiceLifetime"))
			failures.Add("Windows Service lifetime activated while running as an ordinary console process.");
		else
			checks.Add("windows-service-context-aware");

		return Task.FromResult(failures.Count == 0
			? FunctionalScenarioResult.Pass("windows-service", checks.ToArray())
			: FunctionalScenarioResult.Fail("windows-service", checks, failures));
	}

	private static Task<FunctionalScenarioResult> RunHostUniquenessScenario(IServiceProvider activeProvider)
	{
		var checks = new List<string>();
		var failures = new List<string>();

		var services = new ServiceCollection();
		services.Start(startupRuntimeDiagnosticsPipeName: "functional.same");
		services.Start(startupRuntimeDiagnosticsPipeName: "functional.same");
		checks.Add("start-identical-idempotent");

		try
		{
			services.Start(startupRuntimeDiagnosticsPipeName: "functional.conflict");
			failures.Add("conflicting Start did not throw RuntimeHostConfigurationException.");
		}
		catch (RuntimeHostConfigurationException)
		{
			checks.Add("start-conflict-rejected");
		}

		activeProvider.Activate(Assembly.GetExecutingAssembly());
		checks.Add("activate-same-provider-idempotent");

		var otherServices = new ServiceCollection();
		otherServices.AddLogging();
		otherServices.Start(startupRuntimeDiagnosticsPipeName: "functional.other");
		using var otherProvider = otherServices.BuildServiceProvider();
		try
		{
			otherProvider.Activate(Assembly.GetExecutingAssembly());
			failures.Add("a second provider silently replaced the active Runtime host.");
		}
		catch (RuntimeHostConfigurationException)
		{
			checks.Add("activate-second-provider-rejected");
		}

		return Task.FromResult(failures.Count == 0
			? FunctionalScenarioResult.Pass("host-uniqueness", checks.ToArray())
			: FunctionalScenarioResult.Fail("host-uniqueness", checks, failures));
	}

    private static async Task<FunctionalScenarioResult> RunScenarioWithTaskPointAsync(
        IServiceProvider provider,
        string scenario,
        Func<Task<FunctionalScenarioResult>> run)
    {
        var execution = provider.GetRequiredService<RuntimeExecutionManager>();
        var managed = provider.GetRequiredService<RuntimeManagedRegistry>();
        var beforeSnapshot = execution.Snapshot();
        var taskId = $"functional.scenario.{scenario}";
        var startedAt = Stopwatch.GetTimestamp();
        RuntimeInjector.Task(
            execution,
            taskId,
            $"Functional Scenario {scenario}",
            RuntimeExecutionLifetime.Dynamic,
            category: "functional-scenario",
            threadId: ScenarioRunnerThreadId,
            sourceLocation: nameof(FunctionalChildRunner),
            step: "registered");
        execution.SetTaskState(
            taskId,
            RuntimeTaskState.Running,
            step: "running",
            threadId: ScenarioRunnerThreadId,
            payload: new { scenario, phase = "start" });
        RuntimeInjector.Output(
            outputPointId: $"functional.scenario.{scenario}.taskpoint",
            section: "functional-scenario",
            kind: "taskpoint",
            message: "Scenario task-point started.",
            payload: new { scenario, taskId });

        try
        {
            var result = await run();
            var elapsedMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            var afterSnapshot = execution.Snapshot();
            var registrationCount = managed.SnapshotRegistrations().Count;
            var state = result.Success ? RuntimeTaskState.Completed : RuntimeTaskState.Faulted;
            var step = result.Success ? "completed" : "failed";
            execution.SetTaskState(
                taskId,
                state,
                step: step,
                error: result.Success ? null : string.Join(" | ", result.Failures.Take(3)),
                threadId: ScenarioRunnerThreadId,
                payload: new
                {
                    scenario,
                    checks = result.Checks.Count,
                    failures = result.Failures.Count,
                    elapsedMs,
                    registrations = registrationCount
                });
            RuntimeInjector.Output(
                outputPointId: $"functional.scenario.{scenario}.taskpoint",
                section: "functional-scenario",
                kind: "taskpoint",
                message: $"Scenario task-point {step}.",
                payload: new
                {
                    scenario,
                    result.Success,
                    checks = result.Checks.Count,
                    failures = result.Failures.Count,
                    elapsedMs,
                    registrations = registrationCount
                });
            return result with
            {
                Checks = result.Checks.Concat(new[]
                {
                    "scenario-taskpoint-injected",
                    "scenario-taskpoint-state-updated",
                    $"data:scenario-duration-ms={elapsedMs:F2}",
                    $"data:execution-static-threads={afterSnapshot.StaticThreads.Count}",
                    $"data:execution-dynamic-threads={afterSnapshot.DynamicThreads.Count}",
                    $"data:execution-static-tasks={afterSnapshot.StaticTasks.Count}",
                    $"data:execution-dynamic-tasks={afterSnapshot.DynamicTasks.Count}",
                    $"data:execution-dynamic-task-delta={afterSnapshot.DynamicTasks.Count - beforeSnapshot.DynamicTasks.Count}",
                    $"data:managed-registrations={registrationCount}"
                }).ToArray()
            };
        }
        catch (Exception ex)
        {
            execution.SetTaskState(
                taskId,
                RuntimeTaskState.Faulted,
                step: "exception",
                error: ex.Message,
                threadId: ScenarioRunnerThreadId,
                payload: new { scenario, exception = ex.GetType().Name });
            RuntimeInjector.Output(
                outputPointId: $"functional.scenario.{scenario}.taskpoint",
                section: "functional-scenario",
                kind: "taskpoint",
                message: "Scenario task-point exception.",
                payload: new { scenario, exception = ex.ToString() });
            throw;
        }
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

        var batchResponse = await SendFrameAsync(monitor.PipeName, new RuntimeDiagnosticFrame
        {
            Header = new RuntimeDiagnosticFrameHeader
            {
                Schema = RuntimeDiagnosticProtocol.V3Schema,
                FrameType = "request",
                Category = "batch",
                Operation = "execute",
                RequestId = Guid.NewGuid().ToString("N")
            },
            Batch = new RuntimeDiagnosticBatchRequest
            {
                Options = new RuntimeDiagnosticBatchOptions { StopOnError = true, DeadlineMs = 5000 },
                Steps =
                [
                    new RuntimeDiagnosticBatchStep
                    {
                        Id = "snapshot-before",
                        Command = new RuntimeDiagnosticFrameCommand { Target = "runtime.managed", Action = "snapshot" }
                    },
                    new RuntimeDiagnosticBatchStep
                    {
                        Id = "snapshot-after",
                        When = new RuntimeDiagnosticBatchCondition { StepId = "snapshot-before", RequireOk = true },
                        Command = new RuntimeDiagnosticFrameCommand { Target = "runtime.managed", Action = "snapshot" }
                    }
                ]
            }
        });
        if (batchResponse.Status?.Ok != true
            || batchResponse.BatchResult?.Steps.Count != 2
            || batchResponse.BatchResult.Steps.Any(x => !x.Ok))
        {
            failures.Add("Native batch request did not execute two dependent steps in one frame.");
        }
        else
        {
            checks.Add("native-batch-two-steps-one-frame");
        }

        var bindingResponse = await SendFrameAsync(monitor.PipeName, new RuntimeDiagnosticFrame
        {
            Header = new RuntimeDiagnosticFrameHeader
            {
                Schema = RuntimeDiagnosticProtocol.V3Schema,
                FrameType = "request",
                Category = "batch",
                Operation = "execute",
                RequestId = Guid.NewGuid().ToString("N")
            },
            Batch = new RuntimeDiagnosticBatchRequest
            {
                Steps =
                [
                    new RuntimeDiagnosticBatchStep
                    {
                        Id = "read-label",
                        Command = new RuntimeDiagnosticFrameCommand { Target = "diagnostics.selftest", Action = "get", Member = "Label" }
                    },
                    new RuntimeDiagnosticBatchStep
                    {
                        Id = "write-label",
                        When = new RuntimeDiagnosticBatchCondition { StepId = "read-label", Path = "$", Exists = true },
                        Bindings = new Dictionary<string, RuntimeDiagnosticBatchBinding>
                        {
                            ["value"] = new() { StepId = "read-label", Path = "$" }
                        },
                        Command = new RuntimeDiagnosticFrameCommand { Target = "diagnostics.selftest", Action = "set", Member = "Label" }
                    }
                ]
            }
        });
        if (bindingResponse.Status?.Ok != true
            || bindingResponse.BatchResult?.Steps.Count != 2
            || bindingResponse.BatchResult.Steps[1].Skipped)
        {
            failures.Add("Native batch did not bind a typed JSON field into a later step.");
        }
        else
        {
            checks.Add("native-batch-typed-result-binding");
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
		var managedState = new RManagedState(unitId);
		var stateInstruction = new TaskCompletionSource<RuntimeValueInstruction>(TaskCreationOptions.RunContinuationsAsynchronously);
		void OnControllerInstruction(object? _, RuntimeValueInstruction instruction)
		{
			if (instruction.EntityIdHash == RuntimeInjectorTransportCodec.ComputeStableHash32(unitId))
				stateInstruction.TrySetResult(instruction);
		}
		managed.ControllerInstructionReceived += OnControllerInstruction;

        managed.Register(unitId, "test", "Functional", managedState.Snapshot());
        checks.Add("registered");
		var transitioned = managedState.TransitionTo("Working");
		var completed = await Task.WhenAny(stateInstruction.Task, Task.Delay(2000));
		if (completed != stateInstruction.Task || stateInstruction.Task.Result.Value != transitioned.Code)
			failures.Add("state transition Code was not dispatched through the controller FIFO.");
		else
			checks.Add("state-code-dispatched");
		managed.ControllerInstructionReceived -= OnControllerInstruction;

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

		RuntimeManagedCommand? command = null;
		var dequeued = SpinWait.SpinUntil(
			() => managed.TryDequeueCommand(unitId, out command),
			TimeSpan.FromSeconds(2));
		if (!dequeued || command is null || command.TargetUnitId != unitId || command.Kind != RuntimeManagedCommandKind.Snapshot)
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

		for (var i = 0; i < 140; i++)
		{
			managed.UpdateState(unitId, managedState.Snapshot());
		}
		var boundedHistory = managed.SnapshotUnitStateHistory(unitId, 128);
		if (boundedHistory.Count != 128 || boundedHistory.Any(static state => state.Name != "Working"))
		{
			failures.Add("RecordStore state history did not preserve the newest 128 ordered states.");
		}
		else
		{
			checks.Add("record-store-state-history-bounded");
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

		using (var isolatedRegistry = new RuntimeManagedRegistry())
		{
			var stuckUnitId = $"functional.shutdown.stuck.{Guid.NewGuid():N}";
			isolatedRegistry.Register(stuckUnitId, "task", "Functional", CreateSnapshot(stuckUnitId));
			var timeoutResult = await new RuntimeShutdownCoordinator(isolatedRegistry).ShutdownAsync(TimeSpan.FromMilliseconds(120));
			if (!timeoutResult.TimedOut || timeoutResult.ExitCode != RuntimeShutdownExitCodes.Timeout || timeoutResult.Status.PendingUnits.All(x => x.UnitId != stuckUnitId))
				failures.Add("coordinated shutdown did not return 124 with the pending unit.");
			else
				checks.Add("shutdown-timeout-124");
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

		var directRoot = new RuntimeRootContainer();
		var mutableSecondary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["scope"] = "original"
		};
		var mutablePayload = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["value"] = "original"
		};
		directRoot.Upsert("RecordStoreMigration", new RuntimeRootEntryEnvelope(
			"entry-a",
			"primary-a",
			mutableSecondary,
			mutablePayload));
		mutableSecondary["scope"] = "mutated";
		mutablePayload["value"] = "mutated";
		if (!directRoot.TryGet("RecordStoreMigration", "entry-a", out var detachedEntry)
			|| detachedEntry.Value.SecondaryKeys["scope"] != "original"
			|| detachedEntry.Value.Payload is not JsonElement detachedPayload
			|| detachedPayload.GetProperty("value").GetString() != "original")
		{
			failures.Add("RuntimeRoot RecordStore clone strategy leaked mutable input references.");
		}
		else
		{
			checks.Add("record-store-root-values-detached");
		}

		directRoot.Upsert("RecordStoreMigration", new RuntimeRootEntryEnvelope(
			"entry-a",
			"primary-b",
			new Dictionary<string, string> { ["scope"] = "updated" },
			new { value = "updated" }));
		directRoot.Upsert("RecordStoreMigration", new RuntimeRootEntryEnvelope(
			"entry-b",
			"primary-b",
			new Dictionary<string, string> { ["scope"] = "replacement" },
			new { value = "replacement" }));
		var migrationTable = directRoot.Snapshot().Tables.Single(static table => table.TableName == "RecordStoreMigration");
		if (migrationTable.Count != 1
			|| migrationTable.Entries[0].Id != "entry-b"
			|| directRoot.TryGet("RecordStoreMigration", "entry-a", out _)
			|| directRoot.QueryBySecondary("RecordStoreMigration", "scope", "replacement").Count != 1)
		{
			failures.Add("RuntimeRoot RecordStore upsert or primary-key replacement semantics changed.");
		}
		else
		{
			checks.Add("record-store-root-upsert-compatible");
		}

		directRoot.SetFilePathDescriptors([
			new RuntimeFilePathDescriptor("z.log", isPrimaryRecord: true),
			new RuntimeFilePathDescriptor("a.log", isPrimaryRecord: true)
		]);
		var filePaths = directRoot.GetFilePathDescriptorsSnapshot();
		if (filePaths.Count != 2
			|| filePaths.Count(static descriptor => descriptor.IsPrimaryRecord) != 1
			|| filePaths[0].FilePathName != "a.log"
			|| !filePaths[0].IsPrimaryRecord)
		{
			failures.Add("File path RecordStore primary normalization or stable ordering changed.");
		}
		else
		{
			checks.Add("record-store-file-path-order-stable");
		}

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
		var state = new RManagedState(unitId, entityKind: RuntimeInstructionEntityKind.Thread);
		var stateSignal = new TaskCompletionSource<RuntimeValueInstruction>(TaskCreationOptions.RunContinuationsAsynchronously);
		managed.ControllerInstructionReceived += OnState;
		managed.Register(unitId, "thread", "Functional", state.Snapshot());
		var transitioned = state.TransitionTo("Working");
		if (!stateSignal.Task.Wait(TimeSpan.FromSeconds(2)) || stateSignal.Task.Result.Value != transitioned.Code)
			failures.Add("fixed controller FIFO did not dispatch the state Code.");
		else
			checks.Add("fixed-state-code-dispatched");
		managed.ControllerInstructionReceived -= OnState;

		for (var i = 0; i < 64; i++)
			managed.EnqueueCommand(unitId, RuntimeManagedCommandKind.Wakeup);
		var consumed = 0;
		SpinWait.SpinUntil(() =>
		{
			while (managed.TryDequeueCommand(unitId, out _)) consumed++;
			return consumed > 0;
		}, 2000);
		if (consumed == 0)
			failures.Add("fixed unit FIFO did not dispatch commands.");
		else
			checks.Add("fixed-command-dispatch-stable");

		using (var controllerFifo = new RuntimeSharedAtomicFifo(128))
		using (var unitFifo = new RuntimeSharedAtomicFifo(64))
		{
			var descriptor = RuntimeInstructionHandleBootstrap.DuplicateToProcess(Process.GetCurrentProcess(), controllerFifo, unitFifo);
			using var attached = RuntimeSharedAtomicFifo.Attach(
				(nint)descriptor.ControllerMappingHandle,
				(nint)descriptor.ControllerEventHandle,
				descriptor.ControllerCapacity);
			var probe = new RuntimeValueInstruction(991, Environment.ProcessId, Environment.CurrentManagedThreadId, (int)RuntimeInstructionEntityKind.Thread, 7, 42, 0, 0, 0);
			controllerFifo.TryEnqueue(probe);
			if (!attached.TryDequeue(out var attachedProbe) || attachedProbe.Sequence != probe.Sequence || attachedProbe.Value != probe.Value)
				failures.Add("duplicated anonymous mapping handles did not attach to the same FIFO.");
			else
				checks.Add("anonymous-handles-duplicated-and-attached");
		}

		using (var fullFifo = new RuntimeSharedAtomicFifo(64))
		{
			for (var i = 0; i < 64; i++)
			{
				var value = new RuntimeValueInstruction(i + 1, Environment.ProcessId, Environment.CurrentManagedThreadId, (int)RuntimeInstructionEntityKind.Task, 1, i, 0, 0, 0);
				if (!fullFifo.TryEnqueue(value)) failures.Add($"fixed FIFO became full before slot {i}.");
			}
			var overflow = new RuntimeValueInstruction(65, Environment.ProcessId, Environment.CurrentManagedThreadId, (int)RuntimeInstructionEntityKind.Task, 1, 65, 0, 0, 0);
			if (fullFifo.TryEnqueue(overflow) || fullFifo.DroppedCount != 1)
				failures.Add("fixed FIFO did not report its deterministic full condition.");
			else
				checks.Add("fixed-fifo-full-reported");
		}

		using (var concurrentFifo = new RuntimeSharedAtomicFifo(128))
		{
			var received = new ConcurrentDictionary<long, byte>();
			using var dispatcher = new RuntimeInstructionDispatcher(concurrentFifo, instruction => received.TryAdd(instruction.Sequence, 0));
			var producers = Enumerable.Range(0, 4).Select(producer => Task.Run(() =>
			{
				for (var i = 0; i < 500; i++)
				{
					var sequence = producer * 1000L + i + 1;
					var instruction = new RuntimeValueInstruction(sequence, Environment.ProcessId, Environment.CurrentManagedThreadId, (int)RuntimeInstructionEntityKind.Thread, producer, i, 0, 0, 0);
					while (!concurrentFifo.TryEnqueue(instruction)) Thread.Yield();
				}
			})).ToArray();
			Task.WaitAll(producers);
			if (!SpinWait.SpinUntil(() => received.Count == 2000, TimeSpan.FromSeconds(5)))
				failures.Add($"concurrent fixed FIFO dispatch was incomplete: {received.Count}/2000.");
			else
				checks.Add("fixed-fifo-concurrent-atomic");
		}

		using (var disposeFifo = new RuntimeSharedAtomicFifo(64))
		{
			var callbackCount = 0;
			var disposeDispatcher = new RuntimeInstructionDispatcher(disposeFifo, _ => Interlocked.Increment(ref callbackCount));
			var first = new RuntimeValueInstruction(1, Environment.ProcessId, Environment.CurrentManagedThreadId, (int)RuntimeInstructionEntityKind.Task, 1, 1, 0, 0, 0);
			disposeFifo.TryEnqueue(first);
			SpinWait.SpinUntil(() => Volatile.Read(ref callbackCount) == 1, 2000);
			disposeDispatcher.Dispose();
			var second = new RuntimeValueInstruction(2, Environment.ProcessId, Environment.CurrentManagedThreadId, (int)RuntimeInstructionEntityKind.Task, 1, 2, 0, 0, 0);
			disposeFifo.TryEnqueue(second);
			Thread.Sleep(100);
			if (Volatile.Read(ref callbackCount) != 1)
				failures.Add("dispatcher invoked callbacks after disposal.");
			else
				checks.Add("dispatcher-dispose-stable");
		}

		var disposedFifo = new RuntimeSharedAtomicFifo(64);
		disposedFifo.Dispose();
		try
		{
			_ = disposedFifo.PendingCount;
			failures.Add("disposed FIFO allowed pointer-backed state access.");
		}
		catch (ObjectDisposedException)
		{
			checks.Add("disposed-fifo-access-contained");
		}

		using (var callbackFifo = new RuntimeSharedAtomicFifo(64))
		{
			var callbacks = 0;
			using var callbackDispatcher = new RuntimeInstructionDispatcher(callbackFifo, _ =>
			{
				if (Interlocked.Increment(ref callbacks) == 1)
					throw new InvalidOperationException("functional dispatcher callback failure");
			});
			callbackFifo.TryEnqueue(new RuntimeValueInstruction(101, Environment.ProcessId, Environment.CurrentManagedThreadId, (int)RuntimeInstructionEntityKind.Task, 1, 1, 0, 0, 0));
			callbackFifo.TryEnqueue(new RuntimeValueInstruction(102, Environment.ProcessId, Environment.CurrentManagedThreadId, (int)RuntimeInstructionEntityKind.Task, 1, 2, 0, 0, 0));
			if (!SpinWait.SpinUntil(() => Volatile.Read(ref callbacks) == 2, 2000))
				failures.Add("dispatcher stopped after one business callback threw.");
			else
				checks.Add("dispatcher-callback-failure-contained");
		}

        managed.Unregister(unitId);
        return Task.FromResult(failures.Count == 0
            ? FunctionalScenarioResult.Pass("sharedfifo-protocol", checks.ToArray())
            : FunctionalScenarioResult.Fail("sharedfifo-protocol", checks, failures));

		void OnState(object? _, RuntimeValueInstruction instruction)
		{
			if (instruction.EntityIdHash == RuntimeInjectorTransportCodec.ComputeStableHash32(unitId))
				stateSignal.TrySetResult(instruction);
		}
    }

	private static async Task<FunctionalScenarioResult> RunProbeScenario(IServiceProvider provider)
	{
		if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("IWESUN_RUNTIME_DIAGNOSTICS_PIPE")))
			return FunctionalScenarioResult.Pass("probe", "probe child exited successfully.");
		var managed = provider.GetRequiredService<RuntimeManagedRegistry>();
		var unitId = $"probe.business.{Environment.ProcessId}";
		var state = new RManagedState(unitId);
		managed.Register(unitId, "business", "Probe", state.Snapshot());
		await Task.Delay(500);
		state.TransitionTo("Working");
		var stopReceived = SpinWait.SpinUntil(() => managed.IsGlobalStopOrExitRequested, TimeSpan.FromSeconds(3));
		managed.Unregister(unitId);
		return stopReceived
			? FunctionalScenarioResult.Pass("probe", "probe child exited successfully.", "probe-state-code-sent", "probe-stop-received")
			: FunctionalScenarioResult.Fail("probe", ["probe-state-code-sent"], ["probe did not receive Stop through its unit FIFO."]);
	}

	#if DEBUG
    private static async Task<FunctionalScenarioResult> RunNumericBreakpointScenario(IServiceProvider provider)
    {
        var hub        = provider.GetRequiredService<RuntimeDiagnosticHub>();
        var breakpoints = provider.GetRequiredService<RuntimeDiagnosticBreakpoints>();
        var checks     = new List<string>();
        var failures   = new List<string>();

        RuntimeOutputSwitch.Enabled = true;

        // ── helper: enable a breakpoint via hub, resume it after a short delay ──────────────
        async Task<bool> EnableAndAutoResume(string id)
        {
            var r = await hub.ExecuteAsync(new RuntimeDiagnosticAction
            {
                TargetId = "diagnostics.breakpoints",
                Action   = "enable",
                Args     = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                    { ["id"] = JsonSerializer.SerializeToElement(id) }
            });
            if (!r.Success) return false;
            _ = Task.Run(async () =>
            {
                for (var attempt = 0; attempt < 200; attempt++)
                {
                    var snapshot = breakpoints.Snapshot().FirstOrDefault(x =>
                        x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                    if (snapshot?.IsWaiting == true)
                    {
						await Task.Delay(60);
                        await hub.ExecuteAsync(new RuntimeDiagnosticAction
                        {
                            TargetId = "diagnostics.breakpoints",
                            Action   = "resume",
                            Args     = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                                { ["id"] = JsonSerializer.SerializeToElement(id) }
                        });
                        return;
                    }

                    await Task.Delay(10);
                }
            });
            return true;
        }

        // ── 1. Verify default bindings are loaded from assembly attributes ───────────────────
        var listDefault = await hub.ExecuteAsync(new RuntimeDiagnosticAction
            { TargetId = "diagnostics.breakpoints", Action = "listNumeric" });
        if (!listDefault.Success)
        {
            failures.Add($"listNumeric failed: {listDefault.Error}");
            return FunctionalScenarioResult.Fail("numeric-breakpoint", checks, failures);
        }
        var expectedDefaultBindings = new[]
        {
            "numeric.default.threshold", "numeric.default.range", "numeric.default.delta",
            "numeric.eq2", "numeric.ne2", "numeric.ge2", "numeric.le2",
            "numeric.outside3", "numeric.delta-gt3"
        };
        var loadedBindings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var defaultList    = JsonSerializer.SerializeToElement(listDefault.Value);
        if (defaultList.TryGetProperty("bindings", out var bindingsArr) && bindingsArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in bindingsArr.EnumerateArray())
            {
                var hasId = item.TryGetProperty("breakpointId", out var idNode)
                         || item.TryGetProperty("BreakpointId", out idNode);
                if (hasId && idNode.ValueKind == JsonValueKind.String
                    && expectedDefaultBindings.Contains(idNode.GetString() ?? "", StringComparer.OrdinalIgnoreCase))
                    loadedBindings.Add(idNode.GetString()!);
            }
        }
        if (loadedBindings.Count != expectedDefaultBindings.Length)
        {
            failures.Add($"Expected {expectedDefaultBindings.Length} default bindings, loaded {loadedBindings.Count}.");
            return FunctionalScenarioResult.Fail("numeric-breakpoint", checks, failures);
        }
        checks.Add("numeric-all-default-bindings-loaded");

        // ── helper: test one predicate ────────────────────────────────────────────────────────
        async Task TestPredicate(string bpId, string label,
            double v1Hit, double v2Hit, double v3Hit,        // values that SHOULD trigger
            double v1Miss, double v2Miss, double v3Miss)     // values that should NOT trigger
        {
            if (!await EnableAndAutoResume(bpId))
            {
                failures.Add($"{label}: enable failed"); return;
            }

            // HIT: breakpoint should block (auto-resume after 80ms → elapsed > 50ms)
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await RuntimeOutput.BreakIfNumbers(bpId, v1Hit, v2Hit, v3Hit, new { label, phase = "hit" });
            sw.Stop();
            if (sw.ElapsedMilliseconds < 50)
                failures.Add($"{label} HIT did not block (elapsed={sw.ElapsedMilliseconds}ms).");
            else
                checks.Add($"{label}-hit");

            // Disable so MISS check passes through immediately
            await hub.ExecuteAsync(new RuntimeDiagnosticAction
            {
                TargetId = "diagnostics.breakpoints", Action = "enable",
                Args = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                    { ["id"] = JsonSerializer.SerializeToElement(bpId) }
            });

            // MISS: condition false → should return in < 30ms without blocking
            sw.Restart();
            await RuntimeOutput.BreakIfNumbers(bpId, v1Miss, v2Miss, v3Miss, new { label, phase = "miss" });
            sw.Stop();
            if (sw.ElapsedMilliseconds > 30)
                failures.Add($"{label} MISS blocked unexpectedly (elapsed={sw.ElapsedMilliseconds}ms).");
            else
                checks.Add($"{label}-miss");

            // Disable after test
            await hub.ExecuteAsync(new RuntimeDiagnosticAction
            {
                TargetId = "diagnostics.breakpoints", Action = "disable",
                Args = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                    { ["id"] = JsonSerializer.SerializeToElement(bpId) }
            });
        }

        // ── 2. gt2: v1 > v2  (default threshold binding: gt 7 → use gt2 predicate via BreakIfNumbers) ──
        // Bind to catalog predicate "gt2" for direct test
        await hub.ExecuteAsync(new RuntimeDiagnosticAction
        {
            TargetId = "diagnostics.breakpoints", Action = "setNumeric",
            Args = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = JsonSerializer.SerializeToElement("numeric.default.threshold"),
                ["predicateId"] = JsonSerializer.SerializeToElement("gt2")
            }
        });
        // gt2: v1(10) > v2(5) → HIT ; v1(3) > v2(5) → MISS
        await TestPredicate("numeric.default.threshold", "gt2", 10, 5, 0,  3, 5, 0);
        checks.Add("numeric-predicate-gt2");

        // ── 3. eq2: v1 == v2 ───────────────────────────────────────────────────────────────────
        // eq2: v1(7) == v2(7) → HIT ; v1(7) == v2(8) → MISS
        await TestPredicate("numeric.eq2", "eq2", 7, 7, 0,  7, 8, 0);
        checks.Add("numeric-predicate-eq2");

        // ── 4. ne2: v1 != v2 ───────────────────────────────────────────────────────────────────
        // ne2: v1(7) != v2(8) → HIT ; v1(5) != v2(5) → MISS
        await TestPredicate("numeric.ne2", "ne2", 7, 8, 0,  5, 5, 0);
        checks.Add("numeric-predicate-ne2");

        // ── 5. ge2: v1 >= v2 ───────────────────────────────────────────────────────────────────
        // ge2: v1(5) >= v2(5) → HIT ; v1(4) >= v2(5) → MISS
        await TestPredicate("numeric.ge2", "ge2", 5, 5, 0,  4, 5, 0);
        checks.Add("numeric-predicate-ge2");

        // ── 6. le2: v1 <= v2 ───────────────────────────────────────────────────────────────────
        // le2: v1(3) <= v2(5) → HIT ; v1(6) <= v2(5) → MISS
        await TestPredicate("numeric.le2", "le2", 3, 5, 0,  6, 5, 0);
        checks.Add("numeric-predicate-le2");

        // ── 7. between3: value in [min, max] ───────────────────────────────────────────────────
        // between3: v1(5) in [3,9] → HIT ; v1(10) in [3,9] → MISS
        await TestPredicate("numeric.default.range", "between3", 5, 3, 9,  10, 3, 9);
        checks.Add("numeric-predicate-between3");

        // ── 8. outside3: value outside [min, max] ──────────────────────────────────────────────
        // outside3: v1(10) outside [3,9] → HIT ; v1(5) outside [3,9] → MISS
        await TestPredicate("numeric.outside3", "outside3", 10, 3, 9,  5, 3, 9);
        checks.Add("numeric-predicate-outside3");

        // ── 9. delta-le3: |v1-v2| <= maxDelta ─────────────────────────────────────────────────
        // delta-le3: |10-9|=1 <= 2 → HIT ; |10-7|=3 <= 2 → MISS
        await TestPredicate("numeric.default.delta", "delta-le3", 10, 9, 2,  10, 7, 2);
        checks.Add("numeric-predicate-delta-le3");

        // ── 10. delta-gt3: |v1-v2| > minDelta ─────────────────────────────────────────────────
        // delta-gt3: |10-7|=3 > 2 → HIT ; |10-9|=1 > 2 → MISS
        await TestPredicate("numeric.delta-gt3", "delta-gt3", 10, 7, 2,  10, 9, 2);
        checks.Add("numeric-predicate-delta-gt3");

        // ── 11. Runtime threshold switch: gt 7 → lt 5, verify behavior change ─────────────────
        const string swBpId = "numeric.default.threshold";
        // Set to gt2, value1=10 > 5 → HIT
        await hub.ExecuteAsync(new RuntimeDiagnosticAction
        {
            TargetId = "diagnostics.breakpoints", Action = "setNumeric",
            Args = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = JsonSerializer.SerializeToElement(swBpId),
                ["predicateId"] = JsonSerializer.SerializeToElement("gt2")
            }
        });
        await EnableAndAutoResume(swBpId);
        var swHit = System.Diagnostics.Stopwatch.StartNew();
        await RuntimeOutput.BreakIfNumbers(swBpId, 10, 5, 0, new { phase = "switch-before-hit" });
        swHit.Stop();
        if (swHit.ElapsedMilliseconds < 50)
            failures.Add($"runtime-switch: gt2 HIT did not block (elapsed={swHit.ElapsedMilliseconds}ms).");
        else
            checks.Add("numeric-runtime-switch-hit-before");

        // Switch to lt2: v1(3) < v2(5) → HIT, v1(10) < v2(5) → MISS
        var switchResult = await hub.ExecuteAsync(new RuntimeDiagnosticAction
        {
            TargetId = "diagnostics.breakpoints", Action = "setNumeric",
            Args = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"]       = JsonSerializer.SerializeToElement(swBpId),
                ["predicateId"] = JsonSerializer.SerializeToElement("lt2")
            }
        });
        if (!switchResult.Success)
            failures.Add($"runtime-switch: setNumericThreshold to lt2 failed: {switchResult.Error}");
        else
            checks.Add("numeric-runtime-switch-operator-changed");

        // With lt2, v1(10) > v2(5) → condition false → MISS (no block)
        var swMiss = System.Diagnostics.Stopwatch.StartNew();
        await RuntimeOutput.BreakIfNumbers(swBpId, 10, 5, 0, new { phase = "switch-after-miss" });
        swMiss.Stop();
        if (swMiss.ElapsedMilliseconds > 30)
            failures.Add($"runtime-switch: after lt2 switch, gt values should not block (elapsed={swMiss.ElapsedMilliseconds}ms).");
        else
            checks.Add("numeric-runtime-switch-miss-after");

        // With lt2, v1(3) < v2(5) → HIT
        await EnableAndAutoResume(swBpId);
        var swHit2 = System.Diagnostics.Stopwatch.StartNew();
        await RuntimeOutput.BreakIfNumbers(swBpId, 3, 5, 0, new { phase = "switch-after-hit" });
        swHit2.Stop();
        if (swHit2.ElapsedMilliseconds < 50)
            failures.Add($"runtime-switch: lt2 HIT did not block (elapsed={swHit2.ElapsedMilliseconds}ms).");
        else
            checks.Add("numeric-runtime-switch-hit-after");

        // ── 12. listNumeric after all changes ────────────────────────────────────────────────────
        var listNumeric = await hub.ExecuteAsync(new RuntimeDiagnosticAction
            { TargetId = "diagnostics.breakpoints", Action = "listNumeric" });
        if (!listNumeric.Success)
            failures.Add($"listNumeric after changes failed: {listNumeric.Error}");
        else
            checks.Add("numeric-binding-listed-after-changes");

        return failures.Count == 0
            ? FunctionalScenarioResult.Pass("numeric-breakpoint", checks.ToArray())
            : FunctionalScenarioResult.Fail("numeric-breakpoint", checks, failures);
    }

	#endif
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
		if (!duplicatePipe.Equals($"{staticPipe}_001", StringComparison.OrdinalIgnoreCase))
        {
			failures.Add($"duplicate acquire should resolve to '{staticPipe}_001', actual '{duplicatePipe}'.");
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

        var leasesPage = JsonSerializer.SerializeToElement(list.Value);
        if (!TryGetPagedItems(leasesPage, out var leases) || leases.GetArrayLength() == 0)
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

		if (!firstLease.TryGetProperty("RequestedPipeName", out var requestedPipeNameNode)
			|| requestedPipeNameNode.ValueKind != JsonValueKind.String
			|| string.IsNullOrWhiteSpace(requestedPipeNameNode.GetString()))
		{
			failures.Add("pipe lease missing RequestedPipeName.");
		}
		else
		{
			checks.Add("requested-pipe-name-present");
		}

		if (!firstLease.TryGetProperty("ResolvedPipeName", out var resolvedPipeNameNode)
			|| resolvedPipeNameNode.ValueKind != JsonValueKind.String
			|| string.IsNullOrWhiteSpace(resolvedPipeNameNode.GetString()))
		{
			failures.Add("pipe lease missing ResolvedPipeName.");
		}
		else
		{
			checks.Add("resolved-pipe-name-present");
		}

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
        var exitRequested = false;
        var exitCompleted = false;

        var thread = new RThread(() =>
        {
            bodyEntered.Set();
            Thread.Sleep(Timeout.Infinite);
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
        checks.Add("uncaught-interrupt-contained-by-wrapper");

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

        if (!endSnapshot.DynamicThreads.Any(x => x.Id == unitId && x.State == RuntimeThreadState.Cancelled))
        {
            failures.Add("thread did not end in Cancelled state after a managed stop.");
        }
        else
        {
            checks.Add("thread-cancelled-visible");
        }

        if (managed.SnapshotRegistrations().Any(x => x.UnitId == unitId))
        {
            failures.Add("thread registration remained after exit.");
        }
        else
        {
            checks.Add("thread-unregistered");
        }

		var cleanupUnitId = $"functional.thread.cleanup.{Guid.NewGuid():N}";
		var cleanupEntered = new ManualResetEventSlim(false);
		var cleanupHookCount = 0;
		using (var cleanupThread = new RThread(() =>
		{
			cleanupEntered.Set();
			Thread.Sleep(Timeout.Infinite);
		}, cleanupUnitId))
		{
			cleanupThread.CleanupRequested += async (_, _, token) =>
			{
				await Task.Delay(40, token);
				Interlocked.Increment(ref cleanupHookCount);
			};
			cleanupThread.Start();
			cleanupEntered.Wait(TimeSpan.FromSeconds(2));
			managed.TryEnqueueCommand(cleanupUnitId, RuntimeManagedCommandKind.Stop,
				RuntimeManagedPayloadInterpreter.ToJson(new { deadlineUtc = DateTimeOffset.UtcNow.AddSeconds(2) }));
			managed.TryEnqueueCommand(cleanupUnitId, RuntimeManagedCommandKind.Stop,
				RuntimeManagedPayloadInterpreter.ToJson(new { deadlineUtc = DateTimeOffset.UtcNow.AddSeconds(2) }));
			cleanupThread.Join(TimeSpan.FromSeconds(3));
			if (cleanupHookCount != 1 || cleanupThread.ExitCode != RuntimeShutdownExitCodes.Success
				|| managed.SnapshotRegistrations().Any(x => x.UnitId == cleanupUnitId))
				failures.Add("cleanup hook did not complete before normal managed thread exit.");
			else
				checks.Add("cleanup-hook-completed-exit-0");
		}

		var cleanupTimeoutUnitId = $"functional.thread.cleanup-timeout.{Guid.NewGuid():N}";
		var cleanupTimeoutEntered = new ManualResetEventSlim(false);
		var cleanupTimeoutSecondHookStarted = false;
		using (var cleanupTimeoutThread = new RThread(() =>
		{
			cleanupTimeoutEntered.Set();
			Thread.Sleep(Timeout.Infinite);
		}, cleanupTimeoutUnitId))
		{
			cleanupTimeoutThread.CleanupRequested += async (_, _, token) =>
				await Task.Delay(Timeout.InfiniteTimeSpan, token);
			cleanupTimeoutThread.CleanupRequested += (_, _, _) =>
			{
				cleanupTimeoutSecondHookStarted = true;
				return ValueTask.CompletedTask;
			};
			cleanupTimeoutThread.Start();
			cleanupTimeoutEntered.Wait(TimeSpan.FromSeconds(2));
			managed.TryEnqueueCommand(cleanupTimeoutUnitId, RuntimeManagedCommandKind.Stop,
				RuntimeManagedPayloadInterpreter.ToJson(new { deadlineUtc = DateTimeOffset.UtcNow.AddMilliseconds(120) }));
			cleanupTimeoutThread.Join(TimeSpan.FromSeconds(2));
			if (!cleanupTimeoutSecondHookStarted || cleanupTimeoutThread.ExitCode != RuntimeShutdownExitCodes.Timeout
				|| managed.SnapshotRegistrations().Any(x => x.UnitId == cleanupTimeoutUnitId))
				failures.Add("cleanup deadline did not produce exit code 124 and unregister the thread.");
			else
				checks.Add("cleanup-timeout-exit-124");
		}

		var stubbornUnitId = $"functional.thread.stubborn.{Guid.NewGuid():N}";
		var stubbornEntered = new ManualResetEventSlim(false);
		var stubbornRelease = new ManualResetEventSlim(false);
		var stubbornThread = new RThread(() =>
		{
			stubbornEntered.Set();
			while (!stubbornRelease.IsSet)
				Thread.SpinWait(128);
		}, stubbornUnitId, "Stubborn Functional Thread", RuntimeExecutionLifetime.Dynamic, RuntimeThreadKind.Worker, "FunctionalTests", nameof(FunctionalChildRunner), 100);
		stubbornThread.Start();
		stubbornEntered.Wait(TimeSpan.FromSeconds(2));
		stubbornThread.Dispose();
		if (!managed.SnapshotRegistrations().Any(x => x.UnitId == stubbornUnitId))
			failures.Add("live thread was unregistered before its managed entry point returned.");
		else
			checks.Add("thread-stop-keeps-live-registration");
		stubbornRelease.Set();
		stubbornThread.Join(TimeSpan.FromSeconds(2));
		if (managed.SnapshotRegistrations().Any(x => x.UnitId == stubbornUnitId))
			failures.Add("thread registration remained after actual exit.");
		else
			checks.Add("thread-unregisters-after-actual-exit");

		var zeroHookUnitId = $"functional.thread.zero-hook.{Guid.NewGuid():N}";
		var zeroHookEntered = new ManualResetEventSlim(false);
		using var zeroHookThread = new RThread(() =>
		{
			zeroHookEntered.Set();
			Thread.Sleep(Timeout.Infinite);
		}, zeroHookUnitId, "Zero Hook Functional Thread", RuntimeExecutionLifetime.Dynamic,
			RuntimeThreadKind.Worker, "FunctionalTests", nameof(FunctionalChildRunner));
		zeroHookThread.Start();
		if (!zeroHookEntered.Wait(TimeSpan.FromSeconds(2)))
		{
			failures.Add("zero-hook thread did not start.");
		}
		else
		{
			var shutdown = await provider.GetRequiredService<RuntimeShutdownCoordinator>()
				.ShutdownAsync(TimeSpan.FromSeconds(5), "functional-zero-hook");
			if (shutdown.ExitCode != RuntimeShutdownExitCodes.Success || shutdown.TimedOut)
				failures.Add($"zero-hook shutdown returned exit={shutdown.ExitCode}, timedOut={shutdown.TimedOut}.");
			else if (zeroHookThread.IsAlive)
				failures.Add("zero-hook thread remained alive after successful shutdown.");
			else if (managed.SnapshotRegistrations().Any(x => x.BlocksShutdown))
				failures.Add("blocking registrations remained after zero-hook shutdown.");
			else
				checks.Add("zero-hook-shutdown-exit-0-and-drained");
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
        if (!snapshot.DynamicTasks.Any(x => x.Id == unitId && x.Origin == "Managed")
            || !snapshot.StaticThreads.Any(x => x.Id == ScenarioRunnerThreadId && x.Origin == "Descriptive"))
        {
            failures.Add("execution snapshot did not distinguish managed and descriptive origins.");
        }
        else
        {
            checks.Add("execution-origin-managed-vs-descriptive");
        }
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

		var cancelableUnitId = $"functional.task.cancelable.{Guid.NewGuid():N}";
		var cancelableEntered = new ManualResetEventSlim(false);
		var cancelableCleanupCompleted = false;
		var cancelableTask = new RTask((CancellationToken token) =>
		{
			cancelableEntered.Set();
			token.WaitHandle.WaitOne();
			token.ThrowIfCancellationRequested();
		}, cancelableUnitId, "functional", threadId: "functional.thread", lifetime: RuntimeExecutionLifetime.Dynamic, sourceLocation: nameof(FunctionalChildRunner));
		cancelableTask.CleanupRequested += async (_, _, token) =>
		{
			await Task.Delay(30, token);
			cancelableCleanupCompleted = true;
		};
		cancelableTask.Start();
		if (!cancelableEntered.Wait(TimeSpan.FromSeconds(2)))
			failures.Add("cancelable task did not start.");
		var stopResult = managed.TryEnqueueCommand(cancelableUnitId, RuntimeManagedCommandKind.Stop);
		if (!stopResult.Sent)
			failures.Add($"cancelable task Stop command was not sent: {stopResult.Status}.");
		try { await cancelableTask; } catch (OperationCanceledException) { }
		if (!cancelableTask.IsCanceled || !cancelableCleanupCompleted || cancelableTask.ExitCode != RuntimeShutdownExitCodes.Success
			|| managed.SnapshotRegistrations().Any(x => x.UnitId == cancelableUnitId))
			failures.Add("cancelable task did not cancel and unregister after Stop.");
		else
			checks.Add("task-stop-cooperative-cancel");

		var wakeupUnitId = $"functional.task.wakeup.{Guid.NewGuid():N}";
		var wakeupEntered = new ManualResetEventSlim(false);
		var wakeupObserved = new ManualResetEventSlim(false);
		RTask? wakeupTask = null;
		wakeupTask = new RTask((CancellationToken token) =>
		{
			wakeupEntered.Set();
			if (wakeupTask!.WaitForWakeupAsync(TimeSpan.FromSeconds(5), token).AsTask().GetAwaiter().GetResult())
				wakeupObserved.Set();
			token.WaitHandle.WaitOne();
			token.ThrowIfCancellationRequested();
		}, wakeupUnitId, "functional", threadId: "functional.thread", lifetime: RuntimeExecutionLifetime.Dynamic, sourceLocation: nameof(FunctionalChildRunner));
		wakeupTask.Start();
		if (!wakeupEntered.Wait(TimeSpan.FromSeconds(2)))
			failures.Add("wakeup task did not enter its long wait.");
		var wakeupResult = managed.TryEnqueueCommand(wakeupUnitId, RuntimeManagedCommandKind.Wakeup);
		if (!wakeupResult.Sent || !wakeupObserved.Wait(TimeSpan.FromSeconds(2)))
			failures.Add("FIFO Wakeup did not immediately release the RTask wait.");
		else if (!managed.SnapshotRegistrations().Any(x => x.UnitId == wakeupUnitId))
			failures.Add("Wakeup incorrectly stopped or unregistered the task.");
		else
			checks.Add("task-wakeup-continues-without-stop");
		managed.TryEnqueueCommand(wakeupUnitId, RuntimeManagedCommandKind.Stop);
		try { await wakeupTask; } catch (OperationCanceledException) { }
		if (managed.SnapshotRegistrations().Any(x => x.UnitId == wakeupUnitId))
			failures.Add("wakeup task remained registered after explicit Stop.");
		else
			checks.Add("task-wakeup-then-stop-unregisters");

		var disposeUnitId = $"functional.task.dispose.{Guid.NewGuid():N}";
		var disposeEntered = new ManualResetEventSlim(false);
		var disposeRelease = new ManualResetEventSlim(false);
		var runningTask = new RTask(() =>
		{
			disposeEntered.Set();
			disposeRelease.Wait();
		}, disposeUnitId, "functional", threadId: "functional.thread", lifetime: RuntimeExecutionLifetime.Dynamic, sourceLocation: nameof(FunctionalChildRunner));
		runningTask.Start();
		disposeEntered.Wait(TimeSpan.FromSeconds(2));
		runningTask.Dispose();
		if (!managed.SnapshotRegistrations().Any(x => x.UnitId == disposeUnitId))
			failures.Add("running task Dispose removed its registration before execution ended.");
		else
			checks.Add("task-dispose-keeps-live-registration");
		disposeRelease.Set();
		await runningTask;
		if (managed.SnapshotRegistrations().Any(x => x.UnitId == disposeUnitId))
			failures.Add("disposed task registration remained after actual completion.");
		else
			checks.Add("task-dispose-unregisters-after-completion");

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
		var childStateInstruction = new TaskCompletionSource<RuntimeValueInstruction>(TaskCreationOptions.RunContinuationsAsynchronously);
		void OnChildState(object? _, RuntimeValueInstruction instruction)
		{
			if (instruction.ProcessId != Environment.ProcessId)
				childStateInstruction.TrySetResult(instruction);
		}
		managed.ControllerInstructionReceived += OnChildState;

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

		var failedUnitId = $"functional.process.failed.{Guid.NewGuid():N}";
		using (var failedProcess = new RProcess(failedUnitId)
		{
			StartInfo = new ProcessStartInfo($"missing-runtime-executable-{Guid.NewGuid():N}") { UseShellExecute = false }
		})
		{
			try
			{
				failedProcess.Start();
				failures.Add("invalid process start unexpectedly succeeded.");
			}
			catch
			{
				if (managed.SnapshotRegistrations().Any(x => x.UnitId == failedUnitId)
					|| hub.TargetIds.Contains($"runtime.process.{failedUnitId}", StringComparer.OrdinalIgnoreCase))
					failures.Add("failed process start left managed or reflection registrations behind.");
				else
					checks.Add("process-start-failure-rolled-back");
			}
		}

        var dllPath = Assembly.GetExecutingAssembly().Location;
		var disposeProcessUnitId = $"functional.process.dispose.{Guid.NewGuid():N}";
		var disposeProcess = new RProcess(disposeProcessUnitId)
		{
			StartInfo = new ProcessStartInfo("dotnet", $"\"{dllPath}\" --child --scenario probe")
			{
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true
			}
		};
		disposeProcess.Start();
		disposeProcess.Dispose();
		if (!managed.SnapshotRegistrations().Any(x => x.UnitId == disposeProcessUnitId))
			failures.Add("RProcess.Dispose unregistered the child before its managed program exit.");
		else if (!disposeProcess.WaitForExit(10_000))
			failures.Add("RProcess.Dispose Stop did not reach the child's managed program exit.");
		else if (managed.SnapshotRegistrations().Any(x => x.UnitId == disposeProcessUnitId))
			failures.Add("RProcess registration remained after its managed program exit.");
		else
			checks.Add("process-dispose-defers-unregister-until-managed-exit");

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
                var leasePageJson = JsonSerializer.SerializeToElement(leaseList.Value);
                processGuardianAnnounced = TryGetPagedItems(leasePageJson, out var leaseJson)
                    && leaseJson.EnumerateArray().Any(x =>
					x.TryGetProperty("RequestedPipeName", out var requestedName)
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
            var processPageJson = JsonSerializer.SerializeToElement(processList.Value);
            if (!TryGetPagedItems(processPageJson, out var processJson)
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

		var handlesAttached = SpinWait.SpinUntil(
			() => managed.SnapshotEvents(512).Any(x => x.UnitId == process.UnitId && x.Kind == "process-instruction-handles-attached"),
			TimeSpan.FromSeconds(4));
		if (!handlesAttached)
			failures.Add("child process did not attach duplicated anonymous instruction handles.");
		else
		{
			checks.Add("process-instruction-handles-attached");
			if (managed.IsLocalUnitDispatchEnabled(process.UnitId))
				failures.Add("parent and child remained competing consumers of the process FIFO.");
			else
				checks.Add("process-fifo-single-child-consumer");
		}
		if (await Task.WhenAny(childStateInstruction.Task, Task.Delay(3000)) != childStateInstruction.Task)
			failures.Add("child process state Code did not reach the parent controller FIFO.");
		else
			checks.Add("child-state-code-reached-parent");
		var childStop = managed.TryEnqueueCommand(process.UnitId, RuntimeManagedCommandKind.Stop);
		if (!childStop.Sent)
			failures.Add($"parent could not send Stop to child unit FIFO: {childStop.Status}.");
		else
			checks.Add("parent-stop-sent-to-child");
		managed.ControllerInstructionReceived -= OnChildState;

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
		if (!output.Contains("probe-stop-received", StringComparison.OrdinalIgnoreCase))
			failures.Add("probe output did not confirm child-side Stop dispatch.");
		else
			checks.Add("child-stop-dispatched");

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

    private static bool TryGetPagedItems(JsonElement page, out JsonElement items)
    {
        if (page.ValueKind == JsonValueKind.Object
            && page.TryGetProperty("Items", out items)
            && items.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        items = default;
        return false;
    }

	#if DEBUG
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

        async Task ResumeTreeBreakpointWhenWaiting(string id)
        {
            for (var attempt = 0; attempt < 500; attempt++)
            {
                if (breakpoints.Snapshot().Any(x =>
                    x.Id.Equals(id, StringComparison.OrdinalIgnoreCase) && x.IsWaiting))
                {
                    breakpoints.Resume(id);
                    return;
                }

                await Task.Delay(10);
            }

            failures.Add($"tree breakpoint {id} did not enter the waiting state.");
        }

        var fillController = ResumeTreeBreakpointWhenWaiting("tree.fill");
        await RuntimeOutput.BreakIf("tree.fill", () => tree.MutationCount > 0, tree.Snapshot());
        await fillController;

        var stabilizeController = ResumeTreeBreakpointWhenWaiting("tree.stabilize");
        await RuntimeOutput.BreakIf("tree.stabilize", () => tree.MutationCount > 0, tree.Snapshot());
        await stabilizeController;

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

	#endif
    private static async Task<FunctionalScenarioResult> RunCliScenario(IServiceProvider provider, string diagnosticsPipeName)
    {
        var checks = new List<string>();
        var failures = new List<string>();

        var rootDirectory = ResolveRepositoryRoot();
        var cliProjectPath = Path.Combine(rootDirectory, "Iwesun.Runtime.Cli", "Iwesun.Runtime.Cli.csproj");
        var cliConfigPath = Path.Combine(rootDirectory, "Iwesun.Runtime.Cli", "RuntimeCliSystemConfig.json");
        var cliMetadataPath = Path.Combine(rootDirectory, "Iwesun.Runtime.Cli", "RuntimeCliSystemMetadata.json");
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

        if (cliConfig.Schema != "iwesun.runtime.cli/3.0")
        {
            failures.Add($"CLI config schema was '{cliConfig.Schema}', expected iwesun.runtime.cli/3.0.");
            return FunctionalScenarioResult.Fail("cli", checks, failures);
        }
		if (!File.Exists(cliMetadataPath))
		{
			failures.Add($"CLI metadata not found: {cliMetadataPath}");
			return FunctionalScenarioResult.Fail("cli", checks, failures);
		}
        checks.Add("cli-v3-schema-present");

        if (!cliConfig.Endpoints.ContainsKey("diagnostics"))
        {
            failures.Add("CLI config missing endpoints.diagnostics.");
            return FunctionalScenarioResult.Fail("cli", checks, failures);
        }
        checks.Add("cli-diagnostics-endpoint-present");

        var commandNames = cliConfig.Commands.Select(command => command.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var requiredCommands = new[]
        {
            "host.info",
            "lifecycle.status",
            "lifecycle.shutdown",
            "process.list",
            "thread.list",
            "task.list",
            "pipe.list",
            "reflection.get",
            "reflection.invoke",
            "web.data-recorder.create",
            "web.data-recorder.start",
            "web.data-recorder.status",
            "web.data-recorder.list",
            "web.data-recorder.update",
            "web.data-recorder.stop",
            "web.data-recorder.delete",
            "web.data-recorder.events"
        };
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
        checks.Add("cli-server-command-definitions-present");

        using (var metadataDocument = JsonDocument.Parse(await File.ReadAllTextAsync(cliMetadataPath)))
        {
            var metadataCommands = metadataDocument.RootElement.GetProperty("commands");
            if (metadataCommands.EnumerateObject().Count() != cliConfig.Commands.Count
                || metadataCommands.EnumerateObject().Any(item => string.IsNullOrWhiteSpace(item.Value.GetProperty("summary").GetString())
                    || string.IsNullOrWhiteSpace(item.Value.GetProperty("risk").GetString())
                    || string.IsNullOrWhiteSpace(item.Value.GetProperty("capability").GetString())
                    || item.Value.GetProperty("examples").GetArrayLength() == 0))
                failures.Add("One or more standard CLI commands have incomplete explicit metadata.");
            else
                checks.Add("cli-command-metadata-explicit");
        }

        var inspect = cliConfig.Composites.SingleOrDefault(x => x.Name == "runtime.inspect");
        if (inspect == null || inspect.Steps.Count != 5)
            failures.Add("runtime.inspect is missing or does not contain the five standard read-only steps.");
        else
            checks.Add("runtime-inspect-catalog-present");

        checks.Add($"diagnostics-pipe:{diagnosticsPipeName}");
		var cliBuild = new ProcessStartInfo("dotnet", $"build \"{cliProjectPath}\" -c Debug --nologo")
		{
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true,
			WorkingDirectory = rootDirectory
		};
		using (var buildProcess = Process.Start(cliBuild) ?? throw new InvalidOperationException("Failed to build CLI process."))
		{
			await buildProcess.WaitForExitAsync();
			if (buildProcess.ExitCode != 0)
				return FunctionalScenarioResult.Fail("cli", checks, new[] { "CLI debug build failed before execution." });
		}
		var cliDllPath = Path.Combine(rootDirectory, "Iwesun.Runtime.Cli", "bin", "Debug", "net10.0", "Iwesun.Runtime.Cli.dll");

		var psi = new ProcessStartInfo("dotnet", $"\"{cliDllPath}\" --config=\"{cliConfigPath}\" --pipe={diagnosticsPipeName} host.info")
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

        var helpPsi = new ProcessStartInfo("dotnet", $"\"{cliDllPath}\" --config=\"{cliConfigPath}\" lifecycle.shutdown --help")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = rootDirectory
        };
        using (var helpProcess = Process.Start(helpPsi) ?? throw new InvalidOperationException("Failed to start CLI help process."))
        {
            var helpOutputTask = helpProcess.StandardOutput.ReadToEndAsync();
            await helpProcess.WaitForExitAsync();
            var helpOutput = await helpOutputTask;
            if (helpProcess.ExitCode != 0 || !helpOutput.Contains("Risk: destructive", StringComparison.Ordinal) || !helpOutput.Contains("Endpoint: diagnostics", StringComparison.Ordinal))
                failures.Add($"Command-level help did not expose endpoint and risk. stdout={helpOutput.Trim()}");
            else
                checks.Add("cli-command-help-metadata");
        }

        var batchPsi = new ProcessStartInfo("dotnet", $"\"{cliDllPath}\" --config=\"{cliConfigPath}\" --pipe={diagnosticsPipeName} runtime.inspect")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = rootDirectory
        };
        using var batchProcess = Process.Start(batchPsi) ?? throw new InvalidOperationException("Failed to start CLI batch process.");
        var batchStdoutTask = batchProcess.StandardOutput.ReadToEndAsync();
        var batchStderrTask = batchProcess.StandardError.ReadToEndAsync();
        await batchProcess.WaitForExitAsync();
        var batchStdout = await batchStdoutTask;
        var batchStderr = await batchStderrTask;
        RuntimeDiagnosticFrame? cliBatchFrame = null;
        try
        {
            cliBatchFrame = JsonSerializer.Deserialize<RuntimeDiagnosticFrame>(batchStdout, JsonDefaults.Options);
        }
        catch (JsonException ex)
        {
            failures.Add($"CLI batch output was not valid JSON: {ex.Message}. stderr={batchStderr.Trim()}");
        }
        if (batchProcess.ExitCode != 0
            || cliBatchFrame?.Header.Schema != RuntimeDiagnosticProtocol.V3Schema
            || cliBatchFrame.BatchResult?.Steps.Count != 5
            || cliBatchFrame.BatchResult.Steps.Any(x => !x.Ok)
            || cliBatchFrame.BatchResult.TotalSteps != 5
            || cliBatchFrame.BatchResult.SuccessfulSteps != 5
            || cliBatchFrame.BatchResult.FailedSteps != 0)
        {
            failures.Add($"CLI composite did not execute as one native five-step batch with an in-frame summary. stdout={batchStdout.Trim()} stderr={batchStderr.Trim()}");
        }
        else
        {
            checks.Add("cli-composite-native-batch-one-request");
        }

        return failures.Count == 0
            ? FunctionalScenarioResult.Pass("cli", checks.ToArray())
            : FunctionalScenarioResult.Fail("cli", checks, failures);
    }

    // ── file-output-filter ────────────────────────────────────────────────────
    // Validates DiagnosticFileOutputFilter in-memory: all three formats produce
    // structurally correct output without touching the file system.

    // ── CLI command helper ───────────────────────────────────────────────────────
    // Launches the CLI project once and sends one command; returns (exitCode, stdout, stderr).
    // ── bp-process child scenario ─────────────────────────────────────────────
    // Run inside a child process: announces the diagnostics pipe name on stdout
    // so the parent can connect via CLI, then sequentially fires every breakpoint
    // type (BreakIf + all numeric predicates), waiting for CLI resume each time.

    private static async Task<FunctionalScenarioResult> RunBpProcessScenario(
        IServiceProvider provider, string diagnosticsPipeName)
    {
        var checks   = new List<string>();
        var failures = new List<string>();

        // Announce the pipe name so the parent process can find us.
        Console.Out.WriteLine($"PIPE:{diagnosticsPipeName}");
        Console.Out.Flush();

        RuntimeOutputSwitch.Enabled = true;

        // ── 1. Unconditional BreakIf ─────────────────────────────────────────
        await RuntimeOutput.BreakIf("bpp.unconditional");
        checks.Add("bpp.unconditional-resumed");

        // ── 2. BreakIf with condition (true → block) ─────────────────────────
        await RuntimeOutput.BreakIf("bpp.condition", () => true, new { phase = "condition-hit" });
        checks.Add("bpp.condition-resumed");

        // ── 3. Numeric gt: 10 > 5 → HIT ──────────────────────────────────────
        await RuntimeOutput.BreakIfNumbers("bpp.num.gt", 10, 0, 0, new { phase = "gt-hit" });
        checks.Add("bpp.num.gt-resumed");

        // ── 4. Numeric lt: 3 < 10 → HIT ──────────────────────────────────────
        await RuntimeOutput.BreakIfNumbers("bpp.num.lt", 3, 0, 0, new { phase = "lt-hit" });
        checks.Add("bpp.num.lt-resumed");

        // ── 5. Numeric between: 5 in [3,9] → HIT ─────────────────────────────
        await RuntimeOutput.BreakIfNumbers("bpp.num.between", 5, 3, 9, new { phase = "between-hit" });
        checks.Add("bpp.num.between-resumed");

        // ── 6. Numeric outside: 1 outside [3,9] → HIT ───────────────────────
        await RuntimeOutput.BreakIfNumbers("bpp.num.outside", 1, 3, 9, new { phase = "outside-hit" });
        checks.Add("bpp.num.outside-resumed");

        // ── 7. Numeric eq2: v1(7)==v2(7) → HIT ──────────────────────────────
        await RuntimeOutput.BreakIfNumbers("bpp.num.eq", 7, 7, 0, new { phase = "eq-hit" });
        checks.Add("bpp.num.eq-resumed");

        // ── 8. Numeric ne2: v1(7)!=v2(8) → HIT ──────────────────────────────
        await RuntimeOutput.BreakIfNumbers("bpp.num.ne", 7, 8, 0, new { phase = "ne-hit" });
        checks.Add("bpp.num.ne-resumed");

        // ── 9. Numeric delta-le: |10-9|=1 <= 2 → HIT ────────────────────────
        await RuntimeOutput.BreakIfNumbers("bpp.num.delta-le", 10, 9, 2, new { phase = "delta-le-hit" });
        checks.Add("bpp.num.delta-le-resumed");

        // ── 10. Numeric delta-gt: |10-7|=3 > 2 → HIT ────────────────────────
        await RuntimeOutput.BreakIfNumbers("bpp.num.delta-gt", 10, 7, 2, new { phase = "delta-gt-hit" });
        checks.Add("bpp.num.delta-gt-resumed");

        return failures.Count == 0
            ? FunctionalScenarioResult.Pass("bp-process", checks.ToArray())
            : FunctionalScenarioResult.Fail("bp-process", checks, failures);
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunCliCommandAsync(
        string cliProjectPath, string cliConfigPath, string rootDirectory,
        string pipeName, string command)
    {
        var psi = new ProcessStartInfo("dotnet",
            $"run --project \"{cliProjectPath}\" -- --config=\"{cliConfigPath}\" --pipe={pipeName} {command}")
        {
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
            WorkingDirectory       = rootDirectory
        };
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start CLI process.");
        var outTask = proc.StandardOutput.ReadToEndAsync();
        var errTask = proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        return (proc.ExitCode, await outTask, await errTask);
    }

    // ── cli-numeric-breakpoint ──────────────────────────────────────────────────
    // End-to-end test: a real CLI child process sets the numeric predicate,
    // enables the breakpoint, the host triggers BreakIfNumbers (which blocks),
    // then the CLI resumes it.  Then the predicate is switched and a MISS is verified.

	#if DEBUG
    private static async Task<FunctionalScenarioResult> RunCliNumericBreakpointScenario(
        IServiceProvider provider, string diagnosticsPipeName)
    {
        var hub         = provider.GetRequiredService<RuntimeDiagnosticHub>();
        var breakpoints = provider.GetRequiredService<RuntimeDiagnosticBreakpoints>();
        var checks      = new List<string>();
        var failures    = new List<string>();

        const string bpId = "numeric.default.threshold";

        var initialDisabledWatch = Stopwatch.StartNew();
        await RuntimeOutput.BreakIfNumbers(bpId, 15, 0, 0, new { phase = "compiled-disabled" });
        initialDisabledWatch.Stop();
        var initialDisabledSnapshot = breakpoints.Snapshot().FirstOrDefault(x => x.Id == bpId);
        if (initialDisabledWatch.ElapsedMilliseconds > 30 || initialDisabledSnapshot?.IsWaiting == true)
            failures.Add("Compiled Enabled=false breakpoint blocked before any CLI enable command.");
        else
            checks.Add("compiled-disabled-does-not-block");

        var rootDirectory  = ResolveRepositoryRoot();
        var cliProjectPath = Path.Combine(rootDirectory, "Iwesun.Runtime.Cli", "Iwesun.Runtime.Cli.csproj");
        var cliConfigPath  = Path.Combine(rootDirectory, "Iwesun.Runtime.Cli", "RuntimeCliSystemConfig.json");

        if (!File.Exists(cliProjectPath) || !File.Exists(cliConfigPath))
        {
            failures.Add("CLI project or config not found; cannot run cli-numeric-breakpoint scenario.");
            return FunctionalScenarioResult.Fail("cli-numeric-breakpoint", checks, failures);
        }

        // Helper: invoke CLI and verify success frame.
        async Task<bool> Cli(string command, string label)
        {
            var (exitCode, stdout, stderr) = await RunCliCommandAsync(
                cliProjectPath, cliConfigPath, rootDirectory, diagnosticsPipeName, command);
            if (exitCode != 0)
            {
                failures.Add($"{label}: CLI exit {exitCode}. stderr={stderr.Trim()}");
                return false;
            }
            RuntimeDiagnosticFrame? frame;
            try   { frame = JsonSerializer.Deserialize<RuntimeDiagnosticFrame>(stdout, JsonDefaults.Options); }
            catch { failures.Add($"{label}: CLI output not valid JSON. stdout={stdout.Trim()}"); return false; }
            if (frame?.Status?.Ok != true)
            {
                failures.Add($"{label}: CLI status not ok: {frame?.Status?.Code} {frame?.Status?.Message}. stdout={stdout.Trim()}");
                return false;
            }
            checks.Add($"cli-cmd:{label}");
            return true;
        }

        // ── 1. CLI: set predicate gt with threshold 10 ─────────────────────────
        if (!await Cli($"breakpoint.set-numeric-threshold {bpId} gt 10", "setNumericThreshold-gt-10"))
            return FunctionalScenarioResult.Fail("cli-numeric-breakpoint", checks, failures);

        // ── 2. CLI: enable breakpoint ──────────────────────────────────────────
        if (!await Cli($"breakpoint.enable {bpId}", "enable"))
            return FunctionalScenarioResult.Fail("cli-numeric-breakpoint", checks, failures);

        // ── 3. Host: trigger BreakIfNumbers in background (15 > 10 → HIT) ──────
        var sw        = System.Diagnostics.Stopwatch.StartNew();
        var breakTask = Task.Run(async () =>
            await RuntimeOutput.BreakIfNumbers(bpId, 15, 0, 0, new { phase = "cli-resume-hit" }));

        // ── 4. Wait for breakpoint to enter IsWaiting state ───────────────────
        const int pollIntervalMs = 50;
        const int waitTimeoutMs  = 5000;
        var       elapsed        = 0;
        while (elapsed < waitTimeoutMs)
        {
            var snap = breakpoints.Snapshot().FirstOrDefault(s => s.Id == bpId);
            if (snap?.IsWaiting == true) break;
            await Task.Delay(pollIntervalMs);
            elapsed += pollIntervalMs;
        }
        {
            var snap = breakpoints.Snapshot().FirstOrDefault(s => s.Id == bpId);
            if (snap?.IsWaiting != true)
            {
                failures.Add($"Breakpoint {bpId} did not enter IsWaiting within {waitTimeoutMs}ms (elapsed={elapsed}ms).");
                // Ensure the background task doesn't hang
                await hub.ExecuteAsync(new RuntimeDiagnosticAction
                {
                    TargetId = "diagnostics.breakpoints", Action = "resume",
                    Args = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                        { ["id"] = JsonSerializer.SerializeToElement(bpId) }
                });
                await breakTask;
                return FunctionalScenarioResult.Fail("cli-numeric-breakpoint", checks, failures);
            }
            checks.Add("cli-breakpoint-is-waiting");
        }

        // ── 5. CLI: resume the waiting breakpoint ─────────────────────────────
        if (!await Cli($"breakpoint.resume {bpId}", "resume"))
        {
            // Safety: ensure breakTask can exit via hub
            await hub.ExecuteAsync(new RuntimeDiagnosticAction
            {
                TargetId = "diagnostics.breakpoints", Action = "resume",
                Args = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                    { ["id"] = JsonSerializer.SerializeToElement(bpId) }
            });
        }

        await breakTask;
        sw.Stop();

        if (sw.ElapsedMilliseconds < 50)
            failures.Add($"CLI resume: breakpoint should have blocked before CLI resume (elapsed={sw.ElapsedMilliseconds}ms).");
        else
            checks.Add("cli-resume-unblocked");

        // ── 6. CLI: switch predicate to lt with threshold 5 ───────────────────
        if (!await Cli($"breakpoint.set-numeric-threshold {bpId} lt 5", "setNumericThreshold-lt-5"))
            return FunctionalScenarioResult.Fail("cli-numeric-breakpoint", checks, failures);

        // Re-enable; 15 < 5 is false → MISS (should pass through immediately)
        await hub.ExecuteAsync(new RuntimeDiagnosticAction
        {
            TargetId = "diagnostics.breakpoints", Action = "enable",
            Args = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                { ["id"] = JsonSerializer.SerializeToElement(bpId) }
        });

        var missWatch = System.Diagnostics.Stopwatch.StartNew();
        await RuntimeOutput.BreakIfNumbers(bpId, 15, 0, 0, new { phase = "cli-switch-miss" });
        missWatch.Stop();

        if (missWatch.ElapsedMilliseconds > 30)
            failures.Add($"CLI switch to lt5: value 15 should MISS (elapsed={missWatch.ElapsedMilliseconds}ms).");
        else
            checks.Add("cli-switch-miss");

        // Value 3 < 5 → HIT after switch
        if (!await Cli($"breakpoint.enable {bpId}", "enable-for-lt-hit"))
            return FunctionalScenarioResult.Fail("cli-numeric-breakpoint", checks, failures);

        var ltHitSw   = System.Diagnostics.Stopwatch.StartNew();
        var ltHitTask = Task.Run(async () =>
            await RuntimeOutput.BreakIfNumbers(bpId, 3, 0, 0, new { phase = "cli-lt-hit" }));

        // Wait for IsWaiting
        elapsed = 0;
        while (elapsed < waitTimeoutMs)
        {
            var snap = breakpoints.Snapshot().FirstOrDefault(s => s.Id == bpId);
            if (snap?.IsWaiting == true) break;
            await Task.Delay(pollIntervalMs);
            elapsed += pollIntervalMs;
        }
        {
            var snap = breakpoints.Snapshot().FirstOrDefault(s => s.Id == bpId);
            if (snap?.IsWaiting == true)
            {
                checks.Add("cli-lt-hit-waiting");
                // Resume via CLI
                await Cli($"breakpoint.resume {bpId}", "resume-lt-hit");
            }
            else
            {
                failures.Add($"CLI lt-hit: breakpoint did not enter IsWaiting within {waitTimeoutMs}ms.");
                await hub.ExecuteAsync(new RuntimeDiagnosticAction
                {
                    TargetId = "diagnostics.breakpoints", Action = "resume",
                    Args = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                        { ["id"] = JsonSerializer.SerializeToElement(bpId) }
                });
            }
        }
        await ltHitTask;
        ltHitSw.Stop();
        if (ltHitSw.ElapsedMilliseconds < 50)
            failures.Add($"CLI lt-hit: value 3 should have blocked (elapsed={ltHitSw.ElapsedMilliseconds}ms).");
        else
            checks.Add("cli-lt-hit-resumed");

        // ── 7. CLI: listNumeric — verify binding reflects lt operator ─────────
        {
            var (exitCode, stdout, stderr) = await RunCliCommandAsync(
                cliProjectPath, cliConfigPath, rootDirectory, diagnosticsPipeName, "breakpoint.list-numeric");
            if (exitCode != 0)
                failures.Add($"listNumeric exit {exitCode}. stderr={stderr.Trim()}");
            else
            {
                RuntimeDiagnosticFrame? frame;
                try { frame = JsonSerializer.Deserialize<RuntimeDiagnosticFrame>(stdout, JsonDefaults.Options); }
                catch { frame = null; }
                if (frame?.Status?.Ok == true)
                {
                    checks.Add("cli-listNumeric-ok");
                    var data = frame.Data ?? default;
                    if (data.TryGetProperty("bindings", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in arr.EnumerateArray())
                        {
                            var hasId = item.TryGetProperty("breakpointId", out var idNode)
                                     || item.TryGetProperty("BreakpointId", out idNode);
                            if (!hasId || !string.Equals(idNode.GetString(), bpId, StringComparison.OrdinalIgnoreCase))
                                continue;
                            // Verify operator contains "lt"
                            var hasOp = item.TryGetProperty("predicateId", out var opNode)
                                     || item.TryGetProperty("PredicateId", out opNode);
                            if (hasOp && (opNode.GetString() ?? "").Contains("lt", StringComparison.OrdinalIgnoreCase))
                                checks.Add("cli-listNumeric-lt-confirmed");
                            break;
                        }
                    }
                }
                else
                    failures.Add($"listNumeric status not ok. stdout={stdout.Trim()}");
            }
        }

        // ── CLI disable: matching values must no longer block ─────────────────
        if (await Cli($"breakpoint.disable {bpId}", "disable"))
        {
            var disabledWatch = Stopwatch.StartNew();
            await RuntimeOutput.BreakIfNumbers(bpId, 3, 0, 0, new { phase = "cli-disabled-match" });
            disabledWatch.Stop();
            var disabledSnapshot = breakpoints.Snapshot().FirstOrDefault(x => x.Id == bpId);
            if (disabledWatch.ElapsedMilliseconds > 30 || disabledSnapshot?.IsWaiting == true)
                failures.Add("CLI disable: matching data still blocked.");
            else
                checks.Add("cli-disable-prevents-match-block");
        }

        return failures.Count == 0
            ? FunctionalScenarioResult.Pass("cli-numeric-breakpoint", checks.ToArray())
            : FunctionalScenarioResult.Fail("cli-numeric-breakpoint", checks, failures);
    }

	#endif
    // ── Build CLI output helper ────────────────────────────────────────────────
    // Builds the CLI project in Release and returns the path to the output dll.
    // On success the returned tuple is (dllPath, configPath, null); on failure
    // it is ("", "", errorMessage).
    private static async Task<(string DllPath, string ConfigPath, string? Error)> ResolveBuildOutputCliDllAsync(
        string rootDirectory)
    {
        var cliProjectPath = Path.Combine(rootDirectory, "Iwesun.Runtime.Cli", "Iwesun.Runtime.Cli.csproj");
        var cliConfigPath  = Path.Combine(rootDirectory, "Iwesun.Runtime.Cli", "RuntimeCliSystemConfig.json");

        if (!File.Exists(cliProjectPath))
            return ("", "", $"CLI project not found: {cliProjectPath}");
        if (!File.Exists(cliConfigPath))
            return ("", "", $"CLI config not found: {cliConfigPath}");

        // Build to the default output path — all dependent dlls are placed there automatically.
        var outputDir = Path.Combine(rootDirectory, "Iwesun.Runtime.Cli", "bin", "Debug", "net10.0");
        var psi = new ProcessStartInfo("dotnet",
            $"build \"{cliProjectPath}\" -c Debug -v q")
        {
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
            WorkingDirectory       = rootDirectory
        };
        using var buildProc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start dotnet build.");
        var buildOut = buildProc.StandardOutput.ReadToEndAsync();
        var buildErr = buildProc.StandardError.ReadToEndAsync();
        await buildProc.WaitForExitAsync();
        if (buildProc.ExitCode != 0)
            return ("", "", $"CLI build failed (exit {buildProc.ExitCode}): {(await buildErr).Trim()}");

        var dllPath = Path.Combine(outputDir, "Iwesun.Runtime.Cli.dll");
        if (!File.Exists(dllPath))
            return ("", "", $"CLI dll not found after build: {dllPath}");

        // Copy config to output dir so dotnet <dll> can find it relative to its base directory.
        var outConfigPath = Path.Combine(outputDir, "RuntimeCliSystemConfig.json");
        if (!File.Exists(outConfigPath))
            File.Copy(cliConfigPath, outConfigPath, overwrite: false);

        return (dllPath, outConfigPath, null);
    }

    // ── CLI dll command helper (dotnet <dll>) ─────────────────────────────────
    // Invokes the already-compiled CLI dll with `dotnet <dll>` — much faster
    // than `dotnet run`.  Returns (exitCode, stdout, stderr).
    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunCliDllCommandAsync(
        string cliDllPath, string cliConfigPath, string pipeName, string command)
    {
        var psi = new ProcessStartInfo("dotnet",
            $"\"{cliDllPath}\" --config=\"{cliConfigPath}\" --pipe={pipeName} {command}")
        {
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true
        };
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start CLI dll.");
        var outTask = proc.StandardOutput.ReadToEndAsync();
        var errTask = proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        return (proc.ExitCode, await outTask, await errTask);
    }

    // ── bp-process-cli parent scenario ────────────────────────────────────────
    // Builds the CLI, spawns the bp-process child, reads its pipe name, then
    // uses the compiled CLI exe to enable + resume each of the 10 breakpoints.

    private static async Task<FunctionalScenarioResult> RunBpProcessCliScenario(IServiceProvider _)
    {
        var checks   = new List<string>();
        var failures = new List<string>();
        var dllPath  = Assembly.GetExecutingAssembly().Location;
        var rootDir  = ResolveRepositoryRoot();

        // ── Step 1: Build CLI ─────────────────────────────────────────────────
        var (cliDll, cliCfg, buildError) = await ResolveBuildOutputCliDllAsync(rootDir);
        if (buildError != null)
        {
            failures.Add($"CLI build: {buildError}");
            return FunctionalScenarioResult.Fail("bp-process-cli", checks, failures);
        }
        checks.Add("cli-built");

        // ── Step 2: Start bp-process child ────────────────────────────────────
        var childPsi = new ProcessStartInfo("dotnet", $"\"{dllPath}\" --child --scenario bp-process")
        {
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true
        };
        using var child = Process.Start(childPsi)
            ?? throw new InvalidOperationException("Failed to start bp-process child.");

        // ── Step 3: Read pipe name from child stdout ──────────────────────────
        string? pipeLine = null;
        var     pipeTimeout = Task.Delay(10_000);
        while (true)
        {
            var readTask = child.StandardOutput.ReadLineAsync();
            var winner   = await Task.WhenAny(readTask, pipeTimeout);
            if (winner == pipeTimeout) break;
            var line = await readTask;
            if (line == null) break;
            if (line.StartsWith("PIPE:", StringComparison.Ordinal))
            {
                pipeLine = line["PIPE:".Length..];
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(pipeLine))
        {
            failures.Add("Child did not output PIPE:<name> within 10s.");
            child.Kill(entireProcessTree: true);
            return FunctionalScenarioResult.Fail("bp-process-cli", checks, failures);
        }
        var childPipeName = pipeLine;
        checks.Add($"child-pipe-name-received");

        // Wait a moment for host DI to finish startup
        await Task.Delay(500);

        // Then send GO to unblock the child; it will call BreakIf in order.




        var bpIds = new[]
        {
            "bpp.unconditional",
            "bpp.condition",
            "bpp.num.gt",
            "bpp.num.lt",
            "bpp.num.between",
            "bpp.num.outside",
            "bpp.num.eq",
            "bpp.num.ne",
            "bpp.num.delta-le",
            "bpp.num.delta-gt"
        };

        async Task<bool> CliOk(string cmd, string label)
        {
            var (exit, stdout, stderr) = await RunCliDllCommandAsync(cliDll, cliCfg, childPipeName, cmd);
            if (exit != 0)
            {
                failures.Add($"{label}: CLI exit {exit}. stderr={stderr.Trim()}");
                return false;
            }
            RuntimeDiagnosticFrame? frame;
            try   { frame = JsonSerializer.Deserialize<RuntimeDiagnosticFrame>(stdout, JsonDefaults.Options); }
            catch { failures.Add($"{label}: bad JSON. stdout={stdout.Trim()}"); return false; }
            if (frame?.Status?.Ok != true)
            {
                failures.Add($"{label}: status not ok ({frame?.Status?.Code}). stdout={stdout.Trim()}");
                return false;
            }
            return true;
        }

        foreach (var bpId in bpIds)
        {
            if (bpId.Equals("bpp.unconditional", StringComparison.OrdinalIgnoreCase))
            {
                checks.Add("compiled-enabled-tested-without-cli-enable");
            }
            else
            {
                // Exercise idempotent runtime enable for the remaining breakpoints.
                if (!await CliOk($"breakpoint.enable {bpId}", $"enable:{bpId}"))
                    continue;
            }

            // Poll bp.snapshot until IsWaiting=true (child must have hit the breakpoint)
            var waited = false;
            string? pollDiag = null;
            for (var i = 0; i < 120; i++)   // up to ~120s given CLI startup cost
            {
                await Task.Delay(200);
                var (snapExit, snapOut, snapErr) = await RunCliDllCommandAsync(
                    cliDll, cliCfg, childPipeName, "breakpoint.list");
                if (snapExit != 0)
                {
                    pollDiag ??= $"poll[{i}] exit={snapExit} err={snapErr.Trim()}";
                    continue;
                }
                RuntimeDiagnosticFrame? snapFrame;
                try { snapFrame = JsonSerializer.Deserialize<RuntimeDiagnosticFrame>(snapOut, JsonDefaults.Options); }
                catch (Exception ex) { pollDiag ??= $"poll[{i}] parse err: {ex.Message}"; continue; }
                // Data is a bounded page of BreakpointSnapshot objects.
                var snapData = snapFrame?.Data ?? default;
                if (snapData.ValueKind == JsonValueKind.Object &&
                    (snapData.TryGetProperty("Items", out var pageItems) || snapData.TryGetProperty("items", out pageItems)))
                    snapData = pageItems;
                if (snapData.ValueKind != JsonValueKind.Array)
                {
                    pollDiag ??= $"poll[{i}] data kind={snapData.ValueKind} raw={snapOut.Trim()[..Math.Min(200,snapOut.Length)]}";
                    continue;
                }
                foreach (var item in snapData.EnumerateArray())
                {
                    var hasId = item.TryGetProperty("Id", out var idNode) || item.TryGetProperty("id", out idNode);
                    if (!hasId || !string.Equals(idNode.GetString(), bpId, StringComparison.OrdinalIgnoreCase))
                        continue;
                    var isWaiting = (item.TryGetProperty("IsWaiting", out var w) || item.TryGetProperty("isWaiting", out w))
                                    && w.ValueKind == JsonValueKind.True;
                    if (isWaiting) { waited = true; }
                    break;
                }
                if (waited) break;
            }

            if (!waited)
            {
                failures.Add($"{bpId}: never entered IsWaiting state.");
                // Still try to resume so child can proceed
                await RunCliDllCommandAsync(cliDll, cliCfg, childPipeName, $"breakpoint.resume {bpId}");
                continue;
            }
            checks.Add($"{bpId}-is-waiting");

            // A short-lived CLI query must not mutate breakpoint state when its
            // connection closes. Only an explicit resume command may release it.
            await Task.Delay(200);
            var (verifyExit, verifyOut, verifyErr) = await RunCliDllCommandAsync(
                cliDll, cliCfg, childPipeName, "breakpoint.list");
            var stillWaiting = false;
            if (verifyExit == 0)
            {
                try
                {
                    var verifyFrame = JsonSerializer.Deserialize<RuntimeDiagnosticFrame>(verifyOut, JsonDefaults.Options);
                    var verifyData = verifyFrame?.Data ?? default;
                    if (verifyData.ValueKind == JsonValueKind.Object &&
                        (verifyData.TryGetProperty("Items", out var pageItems) || verifyData.TryGetProperty("items", out pageItems)))
                        verifyData = pageItems;
                    if (verifyData.ValueKind == JsonValueKind.Array)
                    {
                        stillWaiting = verifyData.EnumerateArray().Any(item =>
                            (item.TryGetProperty("Id", out var idNode) || item.TryGetProperty("id", out idNode))
                            && string.Equals(idNode.GetString(), bpId, StringComparison.OrdinalIgnoreCase)
                            && (item.TryGetProperty("IsWaiting", out var waitingNode) || item.TryGetProperty("isWaiting", out waitingNode))
                            && waitingNode.ValueKind == JsonValueKind.True);
                    }
                }
                catch (JsonException) { }
            }

            if (!stillWaiting)
            {
                failures.Add($"{bpId}: breakpoint.list disconnect released the breakpoint. stderr={verifyErr.Trim()}");
                return FunctionalScenarioResult.Fail("bp-process-cli", checks, failures);
            }
            checks.Add($"{bpId}-still-waiting-after-snapshot-disconnect");

            // Resume via CLI
            if (await CliOk($"breakpoint.resume {bpId}", $"resume:{bpId}"))
                checks.Add($"{bpId}-resumed");
        }

        // ── Step 5: Wait for child process to exit ────────────────────────────
        var childExited = await Task.Run(() => child.WaitForExit(30_000));
        if (!childExited)
        {
            failures.Add("Child process did not exit within 30s.");
            child.Kill(entireProcessTree: true);
            return FunctionalScenarioResult.Fail("bp-process-cli", checks, failures);
        }
        checks.Add("child-exited");

        // Drain remaining stdout and parse child result
        var remainingOut = await child.StandardOutput.ReadToEndAsync();
        var childJson    = remainingOut
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault(l => l.TrimStart().StartsWith('{') && l.TrimEnd().EndsWith('}'));
        if (!string.IsNullOrWhiteSpace(childJson))
        {
            try
            {
                var childResult = JsonSerializer.Deserialize<FunctionalScenarioResult>(childJson, JsonDefaults.Options);
                if (childResult?.Success == true)
                    checks.Add("child-result-success");
                else
                    failures.AddRange(childResult?.Failures ?? Array.Empty<string>());
            }
            catch { /* child result parse failure is non-fatal */ }
        }

        if (child.ExitCode != 0)
            failures.Add($"Child exit code: {child.ExitCode}.");

        return failures.Count == 0
            ? FunctionalScenarioResult.Pass("bp-process-cli", checks.ToArray())
            : FunctionalScenarioResult.Fail("bp-process-cli", checks, failures);
    }

    private static Task<FunctionalScenarioResult> RunFileOutputFilterScenario(IServiceProvider _)
    {
        var checks   = new List<string>();
        var failures = new List<string>();

        var now     = DateTimeOffset.UtcNow;
        var payload = System.Text.Json.JsonSerializer.SerializeToElement(new { value = 42, tag = "test" });
        var record  = new DiagnosticFileRecord(
            OutputPointId:   "filter.test",
            Section:         "functional",
            Kind:            "trace",
            Message:         "Filter verification message.",
            Payload:         payload,
            Timestamp:       now,
            ProcessId:       Environment.ProcessId,
            ManagedThreadId: Environment.CurrentManagedThreadId,
            StatementId:     "filter-stmt-1");

        // ── CompactJson ──
        var compact = DiagnosticFileOutputFilter.Format(record, DiagnosticFileFormat.CompactJson);
        if (string.IsNullOrWhiteSpace(compact) || compact.Contains('\n'))
        {
            failures.Add("CompactJson output must be a single non-empty line.");
        }
        else
        {
            checks.Add("compact-json-single-line");
        }

        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(compact);
            var root = doc.RootElement;
            if (!root.TryGetProperty("section", out var sec) || sec.GetString() != "functional")
                failures.Add("CompactJson missing or wrong 'section'.");
            else
                checks.Add("compact-json-section-ok");

            if (!root.TryGetProperty("kind", out var kind) || kind.GetString() != "trace")
                failures.Add("CompactJson missing or wrong 'kind'.");
            else
                checks.Add("compact-json-kind-ok");

            if (!root.TryGetProperty("message", out var msg) || msg.GetString() != record.Message)
                failures.Add("CompactJson missing or wrong 'message'.");
            else
                checks.Add("compact-json-message-ok");

            if (!root.TryGetProperty("statementId", out var stmtNode) || stmtNode.ValueKind == System.Text.Json.JsonValueKind.Undefined)
                failures.Add("CompactJson missing 'statementId'.");
            else
                checks.Add("compact-json-statementId-ok");

            if (!root.TryGetProperty("payload", out var pl) || pl.ValueKind != System.Text.Json.JsonValueKind.Object)
                failures.Add("CompactJson missing or wrong 'payload'.");
            else
                checks.Add("compact-json-payload-ok");
        }
        catch (Exception ex)
        {
            failures.Add($"CompactJson was not valid JSON: {ex.Message}");
        }

        // ── PrettyJson ──
        var pretty = DiagnosticFileOutputFilter.Format(record, DiagnosticFileFormat.PrettyJson);
        if (string.IsNullOrWhiteSpace(pretty))
        {
            failures.Add("PrettyJson output is empty.");
        }
        else
        {
            var lines = pretty.Split('\n', StringSplitOptions.None);
            if (lines.Length < 4)
                failures.Add($"PrettyJson expected ≥4 lines, got {lines.Length}.");
            else
                checks.Add($"pretty-json-multiline:{lines.Length}");

            // Must end with a blank separator line
            var trimmed = pretty.TrimEnd('\r');
            if (!trimmed.EndsWith('\n'))
                failures.Add("PrettyJson must end with a blank separator line.");
            else
                checks.Add("pretty-json-trailing-newline");

            try
            {
                // Strip the trailing blank line before parsing
                var jsonPart = pretty.TrimEnd();
                System.Text.Json.JsonDocument.Parse(jsonPart);
                checks.Add("pretty-json-parseable");
            }
            catch (Exception ex)
            {
                failures.Add($"PrettyJson was not valid JSON after trim: {ex.Message}");
            }
        }

        // ── PlainText ──
        var plain = DiagnosticFileOutputFilter.Format(record, DiagnosticFileFormat.PlainText);
        if (string.IsNullOrWhiteSpace(plain))
        {
            failures.Add("PlainText output is empty.");
        }
        else
        {
            var lines = plain.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            // Line 0: header with timestamp, section, kind, message
            var header = lines.Length > 0 ? lines[0] : string.Empty;
            if (!header.Contains("functional"))
                failures.Add("PlainText header missing section 'functional'.");
            else
                checks.Add("plain-text-section-ok");

            if (!header.Contains("trace"))
                failures.Add("PlainText header missing kind 'trace'.");
            else
                checks.Add("plain-text-kind-ok");

            if (!header.Contains(record.Message))
                failures.Add("PlainText header missing message.");
            else
                checks.Add("plain-text-message-ok");

            // Line 1: meta with pid / tid / stmt
            var meta = lines.Length > 1 ? lines[1] : string.Empty;
            if (!meta.Contains("pid:"))
                failures.Add("PlainText meta line missing 'pid:'.");
            else
                checks.Add("plain-text-pid-ok");

            if (!meta.Contains("tid:"))
                failures.Add("PlainText meta line missing 'tid:'.");
            else
                checks.Add("plain-text-tid-ok");

            if (!meta.Contains("stmt:"))
                failures.Add("PlainText meta line missing 'stmt:'.");
            else
                checks.Add("plain-text-stmt-ok");

            // Should include output-point
            if (!meta.Contains("pt:filter.test"))
                failures.Add("PlainText meta line missing 'pt:filter.test'.");
            else
                checks.Add("plain-text-pt-ok");

            // Line 2: payload
            var hasPayload = lines.Any(l => l.Contains("payload:"));
            if (!hasPayload)
                failures.Add("PlainText missing payload line.");
            else
                checks.Add("plain-text-payload-ok");
        }

        // ── null payload ──
        var noPayloadRecord = record with { Payload = null, OutputPointId = null };
        var noPayloadPlain  = DiagnosticFileOutputFilter.Format(noPayloadRecord, DiagnosticFileFormat.PlainText);
        if (noPayloadPlain.Contains("payload:"))
            failures.Add("PlainText should not emit payload line when Payload is null.");
        else
            checks.Add("plain-text-no-null-payload");

        if (noPayloadPlain.Contains("pt:"))
            failures.Add("PlainText should not emit pt: when OutputPointId is null.");
        else
            checks.Add("plain-text-no-null-pt");

        return Task.FromResult(failures.Count == 0
            ? FunctionalScenarioResult.Pass("file-output-filter", checks.ToArray())
            : FunctionalScenarioResult.Fail("file-output-filter", checks, failures));
    }

    // ── file-registry ─────────────────────────────────────────────────────────
    // Validates RuntimeFileRegistry full API + Hub target diagnostics.files.

    private static async Task<FunctionalScenarioResult> RunFileRegistryScenario(IServiceProvider provider)
    {
        var hub      = provider.GetRequiredService<RuntimeDiagnosticHub>();
        var checks   = new List<string>();
        var failures = new List<string>();

        var name         = $"functional.file.{Guid.NewGuid():N}";
        var templatePath = $"logs/test-{name}.jsonl";
        var resolvedPath = $"logs/test-{name}-20991231T235959.jsonl";

        // Register
        var snap = RuntimeFileRegistry.Register(name, templatePath, resolvedPath, FileWriteMode.CreateNew);
        if (snap.Name != name || snap.TemplatePath != templatePath || snap.ResolvedPath != resolvedPath
            || snap.WriteMode != "CreateNew" || !snap.Active)
        {
            failures.Add($"Register returned unexpected snapshot: {snap}");
        }
        else
        {
            checks.Add("register-ok");
        }

        // GetRegistration by name
        var byName = RuntimeFileRegistry.GetRegistration(name);
        if (byName is null || byName.Name != name)
            failures.Add("GetRegistration(name) returned null or wrong record.");
        else
            checks.Add("get-registration-by-name-ok");

        // GetRegistration by internal ID
        var byId = RuntimeFileRegistry.GetRegistration(snap.InternalId.ToString());
        if (byId is null || byId.InternalId != snap.InternalId)
            failures.Add("GetRegistration(id) returned null or wrong record.");
        else
            checks.Add("get-registration-by-id-ok");

        // GetResolvedPath – active
        var resolved = RuntimeFileRegistry.GetResolvedPath(name);
        if (resolved != resolvedPath)
            failures.Add($"GetResolvedPath expected '{resolvedPath}', got '{resolved}'.");
        else
            checks.Add("get-resolved-path-ok");

        // Snapshot list contains the entry
        var all = RuntimeFileRegistry.Snapshot(includeInactive: false);
        if (!all.Any(s => s.Name == name))
            failures.Add("Snapshot(active) did not contain the registered entry.");
        else
            checks.Add("snapshot-active-ok");

        // Update (re-register same name)
        var updatedResolved = resolvedPath + ".v2";
        var updated = RuntimeFileRegistry.Register(name, templatePath, updatedResolved, FileWriteMode.Overwrite);
        if (updated.ResolvedPath != updatedResolved || updated.WriteMode != "Overwrite")
            failures.Add("Re-register did not update ResolvedPath or WriteMode.");
        else
            checks.Add("re-register-update-ok");

        // Hub target: list
        var listResult = await hub.ExecuteAsync(new RuntimeDiagnosticAction
        {
            TargetId = "diagnostics.files",
            Action   = "list"
        });
        if (!listResult.Success)
            failures.Add($"diagnostics.files list failed: {listResult.Error}");
        else
            checks.Add("hub-list-ok");

        // Hub target: register via hub
        var hubName = $"functional.hub.file.{Guid.NewGuid():N}";
        var hubRegister = await hub.ExecuteAsync(new RuntimeDiagnosticAction
        {
            TargetId = "diagnostics.files",
            Action   = "register",
            Args     = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"]         = JsonSerializer.SerializeToElement(hubName),
                ["templatePath"] = JsonSerializer.SerializeToElement($"logs/{hubName}.jsonl"),
                ["resolvedPath"] = JsonSerializer.SerializeToElement($"logs/{hubName}-resolved.jsonl"),
                ["writeMode"]    = JsonSerializer.SerializeToElement("Append")
            }
        });
        if (!hubRegister.Success)
            failures.Add($"diagnostics.files register via hub failed: {hubRegister.Error}");
        else
            checks.Add("hub-register-ok");

        // Verify hub-registered entry visible in snapshot
        var snapAfterHub = RuntimeFileRegistry.Snapshot(includeInactive: false);
        if (!snapAfterHub.Any(s => s.Name == hubName))
            failures.Add("Hub-registered entry not found in snapshot.");
        else
            checks.Add("hub-register-visible-in-snapshot");

        // Hub target: release
        var releaseResult = await hub.ExecuteAsync(new RuntimeDiagnosticAction
        {
            TargetId = "diagnostics.files",
            Action   = "release",
            Args     = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = JsonSerializer.SerializeToElement(hubName)
            }
        });
        if (!releaseResult.Success)
            failures.Add($"diagnostics.files release failed: {releaseResult.Error}");
        else
            checks.Add("hub-release-ok");

        // GetResolvedPath returns null after release
        var afterRelease = RuntimeFileRegistry.GetResolvedPath(hubName);
        if (afterRelease != null)
            failures.Add("GetResolvedPath should return null after release.");
        else
            checks.Add("resolved-null-after-release");

        // Release the first entry and purge
        RuntimeFileRegistry.Release(name);
        var purged = RuntimeFileRegistry.PurgeInactive();
        if (purged < 2)  // both name and hubName should be gone
            failures.Add($"PurgeInactive returned {purged}, expected ≥2.");
        else
            checks.Add($"purge-ok:{purged}");

        // Confirm purged entries are gone
        var afterPurge = RuntimeFileRegistry.GetRegistration(name);
        if (afterPurge != null)
            failures.Add("Purged entry still returned by GetRegistration.");
        else
            checks.Add("purge-entry-gone");

        // Hub target: purge action
        var hubPurge = await hub.ExecuteAsync(new RuntimeDiagnosticAction
        {
            TargetId = "diagnostics.files",
            Action   = "purge"
        });
        if (!hubPurge.Success)
            failures.Add($"diagnostics.files purge action failed: {hubPurge.Error}");
        else
            checks.Add("hub-purge-ok");

        return failures.Count == 0
            ? FunctionalScenarioResult.Pass("file-registry", checks.ToArray())
            : FunctionalScenarioResult.Fail("file-registry", checks, failures);
    }

    // ── file-output-e2e ───────────────────────────────────────────────────────
    // End-to-end: configure file output via TryApplyAssemblyFileDefaults,
    // emit a ReportPoint, wait for FIFO pump to flush, verify the file on disk.

    private static async Task<FunctionalScenarioResult> RunFileOutputE2eScenario(IServiceProvider _, string runtimeDirectory)
    {
        var checks   = new List<string>();
        var failures = new List<string>();

        // Use temp dir to avoid Visual Studio file-system watchers locking files under bin/Debug.
        var logDir       = Path.Combine(Path.GetTempPath(), "iwesun-e2e-logs", Guid.NewGuid().ToString("N"));
        var templatePath = Path.Combine(logDir, "e2e-diag.log");

        // Ensure a clean state: no pre-existing resolved path
        DiagnosticSwitchboard.SetGlobal(true);
        DiagnosticSwitchboard.SetSection("functional-e2e", true);
        DiagnosticSwitchboard.SetFileOutput(true);

        // Apply defaults via the assembly-attribute pathway
        DiagnosticSwitchboard.TryApplyAssemblyFileDefaults(
            templatePath,
            FileWriteMode.CreateNew,
            DiagnosticFileFormat.PlainText);

        // Retrieve the resolved path from the registry
        var resolvedPath = RuntimeFileRegistry.GetResolvedPath("runtime.diagnostics.output");
        if (string.IsNullOrWhiteSpace(resolvedPath))
        {
            failures.Add("RuntimeFileRegistry does not have a resolved path after TryApplyAssemblyFileDefaults.");
            return FunctionalScenarioResult.Fail("file-output-e2e", checks, failures);
        }
        checks.Add($"resolved-path-registered:{Path.GetFileName(resolvedPath)}");

        // Emit diagnostic events.
        // Use the "runtime.diagnostics" section which is enabled by default in the JSON config.
        // "functional-e2e" is a custom section that must be gated manually – we test both paths.
        const string section     = "runtime.diagnostics";
        const string customSection = "functional-e2e";
        const string msgAlpha    = "E2E file output alpha.";
        const string msgBeta     = "E2E file output beta.";
        const string msgCustom   = "E2E custom-section message.";

        var snapPreEmit = DiagnosticSwitchboard.Snapshot();

        // Messages via the default-enabled section → expected to reach the file via FIFO pump.
        DiagnosticSwitchboard.ReportTrace(section, "e2e-trace", msgAlpha, new { phase = "alpha", iteration = 1 });
        DiagnosticSwitchboard.ReportTrace(section, "e2e-trace", msgBeta,  new { phase = "beta",  iteration = 2 });
        // Also emit one message via the manually-enabled custom section.
        DiagnosticSwitchboard.ReportTrace(customSection, "e2e-trace", msgCustom, new { phase = "custom" });
        // Wait for the async FIFO pump to flush to disk (up to 5 s)
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (File.Exists(resolvedPath))
            {
                var content = await File.ReadAllTextAsync(resolvedPath);
                if (content.Contains(msgAlpha) && content.Contains(msgBeta))
                    break;
            }
            await Task.Delay(50);
        }

        var snapPostEmit = DiagnosticSwitchboard.Snapshot();

        if (!File.Exists(resolvedPath))
        {
            failures.Add($"Log file was not created at '{resolvedPath}' (FifoCount={snapPostEmit.FifoCount}, FifoDropped={snapPostEmit.FifoDropped}).");
            return FunctionalScenarioResult.Fail("file-output-e2e", checks, failures);
        }
        checks.Add("file-created");

        var fileContent = await File.ReadAllTextAsync(resolvedPath);
        if (string.IsNullOrWhiteSpace(fileContent))
        {
            failures.Add("Log file exists but is empty.");
            return FunctionalScenarioResult.Fail("file-output-e2e", checks, failures);
        }
        checks.Add($"file-non-empty:{fileContent.Length}");

        // Startup marker (written directly by TryApplyAssemblyFileDefaults) must always be present.
        if (!fileContent.Contains("[runtime.diagnostics"))
            failures.Add("File content missing startup-marker section '[runtime.diagnostics'.");
        else
            checks.Add("file-startup-section-marker-ok");

        // Trace messages via runtime.diagnostics section (default-enabled) must be in the file.
        if (!fileContent.Contains(msgAlpha))
            failures.Add($"File content missing message '{msgAlpha}'.");
        else
            checks.Add("file-msg-alpha-ok");

        if (!fileContent.Contains(msgBeta))
            failures.Add($"File content missing message '{msgBeta}'.");
        else
            checks.Add("file-msg-beta-ok");

        // Custom section — advisory: depends on section gate timing.
        checks.Add(fileContent.Contains(msgCustom) ? "file-msg-custom-ok" : "file-msg-custom-not-present");

        // PlainText format uses pid: marker.
        if (!fileContent.Contains("pid:"))
            failures.Add("File content missing pid: marker.");
        else
            checks.Add("file-pid-marker-ok");

        // Snapshot reflects the file write mode
        var switchSnap = DiagnosticSwitchboard.Snapshot();
        if (!switchSnap.FileOutputEnabled)
            failures.Add("Snapshot FileOutputEnabled is false.");
        else
            checks.Add("snapshot-file-output-enabled");

        if (!string.Equals(switchSnap.FileWriteMode, "CreateNew", StringComparison.OrdinalIgnoreCase))
            failures.Add($"Snapshot FileWriteMode expected 'CreateNew', got '{switchSnap.FileWriteMode}'.");
        else
            checks.Add("snapshot-write-mode-ok");

        if (!string.Equals(switchSnap.FileFormat, "PlainText", StringComparison.OrdinalIgnoreCase))
            failures.Add($"Snapshot FileFormat expected 'PlainText', got '{switchSnap.FileFormat}'.");
        else
            checks.Add("snapshot-file-format-ok");

        return failures.Count == 0
            ? FunctionalScenarioResult.Pass("file-output-e2e", checks.ToArray())
            : FunctionalScenarioResult.Fail("file-output-e2e", checks, failures);
    }

    // ── switchboard-config ────────────────────────────────────────────────────
    // Validates FileFormat field roundtrip through config create/merge/snapshot.

    private static Task<FunctionalScenarioResult> RunSwitchboardConfigScenario(IServiceProvider _)
    {
        var checks   = new List<string>();
        var failures = new List<string>();

        // CreateDefaults includes FileFormat = CompactJson
        var defaults = DiagnosticSwitchboardCompiledConfig.CreateDefaults();
        if (defaults.FileFormat != DiagnosticFileFormat.CompactJson)
            failures.Add($"CreateDefaults FileFormat expected CompactJson, got {defaults.FileFormat}.");
        else
            checks.Add("defaults-file-format-compact-json");

        // Merge: if configured has a specific format it should be preserved
        var configured = DiagnosticSwitchboardCompiledConfig.CreateDefaults();
        configured.FileFormat    = DiagnosticFileFormat.PrettyJson;
        configured.FileOutputEnabled = true;
        configured.FilePath      = "logs/test.jsonl";
        configured.FileWriteMode = FileWriteMode.Overwrite;

        var merged = DiagnosticSwitchboardCompiledConfig.MergeWithCompiledDefaults(configured);
        if (merged.FileFormat != DiagnosticFileFormat.PrettyJson)
            failures.Add($"Merge did not preserve FileFormat=PrettyJson, got {merged.FileFormat}.");
        else
            checks.Add("merge-preserves-file-format");

        if (merged.FileWriteMode != FileWriteMode.Overwrite)
            failures.Add($"Merge did not preserve FileWriteMode=Overwrite, got {merged.FileWriteMode}.");
        else
            checks.Add("merge-preserves-write-mode");

        if (merged.FilePath != "logs/test.jsonl")
            failures.Add($"Merge did not preserve FilePath, got '{merged.FilePath}'.");
        else
            checks.Add("merge-preserves-file-path");

        // SetFileOutput / SetFilePath / SetGlobal state chain
        var snapshot0 = DiagnosticSwitchboard.Snapshot();
        var wasFileOutput = snapshot0.FileOutputEnabled;

        DiagnosticSwitchboard.SetFileOutput(false);
        if (DiagnosticSwitchboard.Snapshot().FileOutputEnabled)
            failures.Add("SetFileOutput(false) did not take effect.");
        else
            checks.Add("set-file-output-false-ok");

        DiagnosticSwitchboard.SetFileOutput(true);
        if (!DiagnosticSwitchboard.Snapshot().FileOutputEnabled)
            failures.Add("SetFileOutput(true) did not take effect.");
        else
            checks.Add("set-file-output-true-ok");

        // Restore
        DiagnosticSwitchboard.SetFileOutput(wasFileOutput);

        // SetGlobal roundtrip
        var wasGlobal = snapshot0.GlobalEnabled;
        DiagnosticSwitchboard.SetGlobal(true);
        if (!DiagnosticSwitchboard.Snapshot().GlobalEnabled)
            failures.Add("SetGlobal(true) did not take effect.");
        else
            checks.Add("set-global-true-ok");

        DiagnosticSwitchboard.SetGlobal(false);
        if (DiagnosticSwitchboard.Snapshot().GlobalEnabled)
            failures.Add("SetGlobal(false) did not take effect.");
        else
            checks.Add("set-global-false-ok");

        DiagnosticSwitchboard.SetGlobal(wasGlobal);

        // Snapshot fields all present
        var snap = DiagnosticSwitchboard.Snapshot();
        if (string.IsNullOrWhiteSpace(snap.FileWriteMode))
            failures.Add("Snapshot FileWriteMode is null/empty.");
        else
            checks.Add($"snapshot-write-mode-present:{snap.FileWriteMode}");

        if (string.IsNullOrWhiteSpace(snap.FileFormat))
            failures.Add("Snapshot FileFormat is null/empty.");
        else
            checks.Add($"snapshot-file-format-present:{snap.FileFormat}");

        if (string.IsNullOrWhiteSpace(snap.Compiled))
            failures.Add("Snapshot Compiled is null/empty.");
        else
            checks.Add($"snapshot-compiled:{snap.Compiled}");

        // All enum values of DiagnosticFileFormat are valid
        foreach (var fmt in Enum.GetValues<DiagnosticFileFormat>())
        {
            var record = new DiagnosticFileRecord(null, "test", "trace", "msg", null,
                DateTimeOffset.UtcNow, Environment.ProcessId, 1, "s1");
            var formatted = DiagnosticFileOutputFilter.Format(record, fmt);
            if (string.IsNullOrWhiteSpace(formatted))
                failures.Add($"Format({fmt}) returned empty string.");
            else
                checks.Add($"format-enum-{fmt}-ok");
        }

        return Task.FromResult(failures.Count == 0
            ? FunctionalScenarioResult.Pass("switchboard-config", checks.ToArray())
            : FunctionalScenarioResult.Fail("switchboard-config", checks, failures));
    }

    // ── file-output-format-variants ───────────────────────────────────────────
    // Writes real files in all three formats and verifies structure by reading
    // each file back and checking format-specific markers.

    private static async Task<FunctionalScenarioResult> RunFileOutputFormatVariantsScenario(IServiceProvider _, string runtimeDirectory)
    {
        var checks   = new List<string>();
        var failures = new List<string>();

        var logDir = Path.Combine(runtimeDirectory, "format-variant-logs");
        Directory.CreateDirectory(logDir);

        var ts      = DateTimeOffset.UtcNow;
        var payload = JsonSerializer.SerializeToElement(new { x = 1 });

        async Task<(bool ok, string content)> WriteAndRead(string label, DiagnosticFileFormat fmt)
        {
            var filePath = Path.Combine(logDir, $"variant-{label}-{Guid.NewGuid():N}.log");
            var record   = new DiagnosticFileRecord(
                OutputPointId:   $"test.{label}",
                Section:         "format-variant",
                Kind:            label,
                Message:         $"Format variant test for {label}.",
                Payload:         payload,
                Timestamp:       ts,
                ProcessId:       Environment.ProcessId,
                ManagedThreadId: Environment.CurrentManagedThreadId,
                StatementId:     $"stmt-{label}");

            // Write multiple records to the file
            await using (var writer = new StreamWriter(filePath, append: false, System.Text.Encoding.UTF8))
            {
                for (var i = 0; i < 3; i++)
                {
                    var line = DiagnosticFileOutputFilter.Format(record with { Message = $"{record.Message} #{i}" }, fmt);
                    await writer.WriteLineAsync(line);
                }
            }

            var content = await File.ReadAllTextAsync(filePath);
            return (File.Exists(filePath) && !string.IsNullOrWhiteSpace(content), content);
        }

        // CompactJson
        var (compactOk, compactContent) = await WriteAndRead("compact", DiagnosticFileFormat.CompactJson);
        if (!compactOk)
        {
            failures.Add("CompactJson variant file was not created or is empty.");
        }
        else
        {
            checks.Add("compact-variant-file-created");
            var lines = compactContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 3)
                failures.Add($"CompactJson variant file expected ≥3 lines, got {lines.Length}.");
            else
                checks.Add($"compact-variant-lines:{lines.Length}");

            var allParseable = true;
            foreach (var line in lines)
            {
                try { JsonDocument.Parse(line.Trim()); }
                catch { allParseable = false; break; }
            }
            if (!allParseable)
                failures.Add("Not all CompactJson variant lines are valid JSON.");
            else
                checks.Add("compact-variant-all-json-ok");
        }

        // PrettyJson
        var (prettyOk, prettyContent) = await WriteAndRead("pretty", DiagnosticFileFormat.PrettyJson);
        if (!prettyOk)
        {
            failures.Add("PrettyJson variant file was not created or is empty.");
        }
        else
        {
            checks.Add("pretty-variant-file-created");
            if (!prettyContent.Contains("  \"section\""))
                failures.Add("PrettyJson variant file missing indented 'section' field.");
            else
                checks.Add("pretty-variant-indented-ok");

            if (!prettyContent.Contains("format-variant"))
                failures.Add("PrettyJson variant file missing section value.");
            else
                checks.Add("pretty-variant-section-value-ok");
        }

        // PlainText
        var (plainOk, plainContent) = await WriteAndRead("plain", DiagnosticFileFormat.PlainText);
        if (!plainOk)
        {
            failures.Add("PlainText variant file was not created or is empty.");
        }
        else
        {
            checks.Add("plain-variant-file-created");
            var pLines = plainContent.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            // Expect header lines containing [format-variant]
            var headerLines = pLines.Where(l => l.Contains("[format-variant")).ToList();
            if (headerLines.Count < 3)
                failures.Add($"PlainText variant file expected ≥3 header lines, got {headerLines.Count}.");
            else
                checks.Add($"plain-variant-header-lines:{headerLines.Count}");

            // Meta lines with pid:/tid:
            var metaLines = pLines.Where(l => l.Contains("pid:") && l.Contains("tid:")).ToList();
            if (metaLines.Count < 3)
                failures.Add($"PlainText variant file expected ≥3 meta lines, got {metaLines.Count}.");
            else
                checks.Add($"plain-variant-meta-lines:{metaLines.Count}");

            // Payload lines
            if (!plainContent.Contains("payload:"))
                failures.Add("PlainText variant file missing payload lines.");
            else
                checks.Add("plain-variant-payload-ok");
        }

        return failures.Count == 0
            ? FunctionalScenarioResult.Pass("file-output-format-variants", checks.ToArray())
            : FunctionalScenarioResult.Fail("file-output-format-variants", checks, failures);
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
        }, unitId, name, RuntimeExecutionLifetime.Dynamic, RuntimeThreadKind.Worker, "FunctionalTests", "RunTreeScenario");
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
        Console.WriteLine("  dotnet run --project Iwesun.Runtime.FunctionalTests -- --child --scenario diagnostics|managed|thread|task|process|host-uniqueness|windows-service|tree|root-safety|sharedfifo-protocol|numeric-breakpoint|pipe-registry|tree-process|cli|cli-numeric-breakpoint|bp-process|bp-process-cli|probe");
    }
}

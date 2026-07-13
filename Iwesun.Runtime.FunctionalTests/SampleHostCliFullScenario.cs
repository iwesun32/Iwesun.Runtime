using System.Diagnostics;
using System.Text.Json;
using Iwesun.Runtime.Diagnostics;

internal static class SampleHostCliFullScenario
{
    private const string InitialBreakpoint = "sample.host.random.initial-enabled";
    private const string DynamicBreakpoint = "sample.host.random.dynamic";
    private const string NumericBreakpoint = "sample.host.random.numeric";

    public static async Task<FunctionalScenarioResult> RunAsync()
    {
        var checks = new List<string>();
        var failures = new List<string>();
        var root = ResolveRepositoryRoot();
        var hostDll = Path.Combine(root, "Iwesun.Runtime.SampleHost", "bin", "Debug", "net10.0", "Iwesun.Runtime.SampleHost.dll");
        var cliDll = Path.Combine(root, "Iwesun.Runtime.Cli", "bin", "Debug", "net10.0", "Iwesun.Runtime.Cli.dll");
        var cliConfig = Path.Combine(root, "Iwesun.Runtime.Cli", "bin", "Debug", "net10.0", "RuntimeCliSystemConfig.json");
        var pipeName = $"sample.host.validation.{Guid.NewGuid():N}";
        string? resolvedFilePath = null;

        if (!File.Exists(hostDll) || !File.Exists(cliDll) || !File.Exists(cliConfig))
            return FunctionalScenarioResult.Fail("sample-host-cli-full", checks, ["Build SampleHost and CLI before running this scenario."]);

        using var host = RuntimeInjector.CreateProcess(
            new ProcessStartInfo("dotnet", $"\"{hostDll}\" --runtime-diagnostics-pipe={pipeName}")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = root
            },
            $"functional.sample-host.{Guid.NewGuid():N}",
            startImmediately: false);

        Task<string>? hostOut = null;
        Task<string>? hostErr = null;
        try
        {
            host.Start();
            hostOut = host.StandardOutput.ReadToEndAsync();
            hostErr = host.StandardError.ReadToEndAsync();
            checks.Add("sample-host-started");

            if (!await WaitForCommandAsync("host.info", frame => frame.Status?.Ok == true, TimeSpan.FromSeconds(10)))
                failures.Add("SampleHost diagnostics pipe did not become ready.");
            else
                checks.Add("host-info");

            await CheckCommand("host.summary", "host-summary");
            await CheckCommand("host.events", "host-events");
            var switchboard = await RunCliAsync("switchboard.get");
            if (switchboard.ExitCode == 0 && switchboard.Frame?.Status?.Ok == true)
            {
                checks.Add("switchboard-list");
                var snapshot = switchboard.Frame.Data?.Deserialize<DiagnosticSwitchboardSnapshot>(JsonDefaults.Options);
                resolvedFilePath = snapshot?.ResolvedFilePath;
                if (snapshot?.FileWriteMode.Equals(nameof(FileWriteMode.CreateNew), StringComparison.OrdinalIgnoreCase) == true)
                    checks.Add("file-mode-create-new");
                else
                    failures.Add($"SampleHost file mode expected CreateNew, got '{snapshot?.FileWriteMode}'.");
                if (snapshot?.FileFormat.Equals(nameof(DiagnosticFileFormat.PlainText), StringComparison.OrdinalIgnoreCase) == true)
                    checks.Add("file-format-plain-text");
                else
                    failures.Add($"SampleHost file format expected PlainText, got '{snapshot?.FileFormat}'.");
                if (snapshot?.FilePath?.Replace('\\', '/').EndsWith("logs/sample-host-diag.jsonl", StringComparison.OrdinalIgnoreCase) == true &&
                    !string.IsNullOrWhiteSpace(resolvedFilePath))
                    checks.Add("file-path-configured");
                else
                    failures.Add($"SampleHost file path was not resolved from the configured template: '{snapshot?.FilePath}' -> '{resolvedFilePath}'.");
            }
            else
            {
                failures.Add($"switchboard.get: exit={switchboard.ExitCode} stderr={switchboard.Stderr.Trim()} stdout={switchboard.Stdout.Trim()}");
            }
            await CheckCommand("switchboard.enable", "switchboard-global-enable");
            await CheckCommand("switchboard.enable sample-host", "switchboard-sample-host-enable");
            await CheckCommand("switchboard.point.list", "switchboard-points");
            await CheckCommand("registry.list", "registry-all");
            await CheckCommand("hook.list", "hook-list");
            await CheckCommand("thread.list", "thread-list");
            await CheckCommand("task.list", "task-list");
            await CheckCommand("lifecycle.status", "lifecycle-status");

            var firstSnapshot = await RunCliAsync("breakpoint.list");
            if (IsWaiting(firstSnapshot.Frame, InitialBreakpoint)) checks.Add("breakpoint-compiled-enabled");
            else failures.Add("Compiled enabled breakpoint did not enter waiting state.");

            var secondSnapshot = await RunCliAsync("breakpoint.list");
            if (IsWaiting(secondSnapshot.Frame, InitialBreakpoint)) checks.Add("breakpoint-disconnect-stable");
            else failures.Add("Read-only CLI disconnect changed breakpoint state.");

            await CheckCommand($"breakpoint.resume {InitialBreakpoint}", "breakpoint-resume");
            var firstCount = await ReadLongAsync("sample.host.random", "SampleCount");
            if (await WaitForLongAsync("sample.host.random", "SampleCount", value => value > firstCount, TimeSpan.FromSeconds(5)))
                checks.Add("random-sample-count-increased");
            else
                failures.Add("Periodic random sample count did not increase.");

            var currentValue = await ReadLongAsync("sample.host.random", "CurrentValue");
            if (currentValue is >= 0 and <= 100) checks.Add("random-value-in-range");
            else failures.Add($"Random value out of range: {currentValue}.");

            await CheckCommand("reflection.invoke sample.host.random Snapshot", "object-invoke");
            var denied = await RunCliAsync("reflection.get sample.host.random NotAllowed");
            if (denied.Frame?.Status?.Ok == false) checks.Add("object-denied");
            else failures.Add("Non-whitelisted reflected member was not denied.");

            await CheckCommand($"breakpoint.enable {DynamicBreakpoint}", "breakpoint-enable");
            if (await WaitForCommandAsync("breakpoint.list", frame => IsWaiting(frame, DynamicBreakpoint), TimeSpan.FromSeconds(5)))
                checks.Add("breakpoint-dynamic-hit");
            else
                failures.Add("Dynamically enabled breakpoint did not hit.");
            await CheckCommand($"breakpoint.disable {DynamicBreakpoint}", "breakpoint-disable");

            await CheckCommand($"breakpoint.set-numeric-threshold {NumericBreakpoint} gt 0", "numeric-threshold-set");
            await CheckCommand($"breakpoint.enable {NumericBreakpoint}", "numeric-enable");
            if (await WaitForCommandAsync("breakpoint.list", frame => IsWaiting(frame, NumericBreakpoint), TimeSpan.FromSeconds(5)))
                checks.Add("numeric-threshold-hit");
            else
                failures.Add("Numeric breakpoint did not hit.");
            await CheckCommand($"breakpoint.resume {NumericBreakpoint}", "numeric-resume");
            await CheckCommand($"breakpoint.clear-numeric {NumericBreakpoint}", "numeric-clear");
            await CheckCommand($"breakpoint.disable {NumericBreakpoint}", "numeric-disable");
            await CheckCommand("switchboard.disable sample-host", "switchboard-sample-host-restored");
            await CheckCommand("switchboard.disable", "diagnostics-restored-quiet");
            var shutdown = await RunCliAsync("lifecycle.shutdown true");
            if (shutdown.ExitCode == 0) checks.Add("lifecycle-shutdown-command");
            else failures.Add($"lifecycle.shutdown CLI exit={shutdown.ExitCode}: {shutdown.Stderr}");

            if (await WaitForExitAsync(host, TimeSpan.FromSeconds(10)) && host.ExitCode == 0)
                checks.Add("sample-host-exit-code-zero");
            else
                failures.Add("SampleHost did not exit cleanly after lifecycle.shutdown.");
        }
        catch (Exception ex)
        {
            failures.Add(ex.ToString());
        }
        finally
        {
            if (!host.HasExited)
            {
                await RunCliAsync($"breakpoint.resume {InitialBreakpoint}");
                await RunCliAsync("lifecycle.shutdown true");
                if (!await WaitForExitAsync(host, TimeSpan.FromSeconds(5)))
                    host.Kill(entireProcessTree: true);
            }

            if (hostOut != null)
            {
                var stdout = await hostOut;
                if (stdout.Contains("Sample host started", StringComparison.OrdinalIgnoreCase)) checks.Add("host-start-log");
            }
            if (hostErr != null)
            {
                var stderr = await hostErr;
                if (!string.IsNullOrWhiteSpace(stderr)) failures.Add($"SampleHost stderr: {stderr.Trim()}");
            }
        }

        return failures.Count == 0
            ? FunctionalScenarioResult.Pass("sample-host-cli-full", checks.Distinct().ToArray())
            : FunctionalScenarioResult.Fail("sample-host-cli-full", checks.Distinct().ToArray(), failures);

        async Task CheckCommand(string command, string check)
        {
            var result = await RunCliAsync(command);
            if (result.ExitCode == 0 && (result.Frame == null || result.Frame.Status?.Ok == true)) checks.Add(check);
            else failures.Add($"{command}: exit={result.ExitCode} stderr={result.Stderr.Trim()} stdout={result.Stdout.Trim()}");
        }

        async Task<CliExecution> RunCliAsync(string command)
        {
            using var process = RuntimeInjector.CreateProcess(
                new ProcessStartInfo("dotnet", $"\"{cliDll}\" --config=\"{cliConfig}\" --pipe={pipeName} {command}")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = root
                },
                $"functional.cli.{Guid.NewGuid():N}",
                startImmediately: false);
            var started = Stopwatch.GetTimestamp();
            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            RuntimeDiagnosticFrame? frame = null;
            try
            {
                using var document = JsonDocument.Parse(stdout);
                if (document.RootElement.TryGetProperty("header", out _))
                    frame = JsonSerializer.Deserialize<RuntimeDiagnosticFrame>(stdout, JsonDefaults.Options);
            }
            catch (JsonException) { }
            return new CliExecution(command, process.ExitCode, stdout, stderr, frame, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        async Task<bool> WaitForCommandAsync(string command, Func<RuntimeDiagnosticFrame, bool> predicate, TimeSpan timeout)
        {
            var started = Stopwatch.GetTimestamp();
            while (Stopwatch.GetElapsedTime(started) < timeout)
            {
                var result = await RunCliAsync(command);
                if (result.Frame != null && predicate(result.Frame)) return true;
                await Task.Delay(100);
            }
            return false;
        }

        async Task<long> ReadLongAsync(string target, string member)
        {
            var result = await RunCliAsync($"reflection.get {target} {member}");
            var data = result.Frame?.Data ?? default;
            return data.ValueKind == JsonValueKind.Number && data.TryGetInt64(out var value) ? value : -1;
        }

        async Task<bool> WaitForLongAsync(string target, string member, Func<long, bool> predicate, TimeSpan timeout)
        {
            var started = Stopwatch.GetTimestamp();
            while (Stopwatch.GetElapsedTime(started) < timeout)
            {
                if (predicate(await ReadLongAsync(target, member))) return true;
                await Task.Delay(100);
            }
            return false;
        }
    }

    private static bool IsWaiting(RuntimeDiagnosticFrame? frame, string id)
    {
        var data = frame?.Data ?? default;
        var items = data.ValueKind == JsonValueKind.Object &&
            (data.TryGetProperty("Items", out var pageItems) || data.TryGetProperty("items", out pageItems))
            ? pageItems
            : data;
        if (items.ValueKind != JsonValueKind.Array) return false;
        return items.EnumerateArray().Any(item =>
            (item.TryGetProperty("Id", out var idNode) || item.TryGetProperty("id", out idNode))
            && string.Equals(idNode.GetString(), id, StringComparison.OrdinalIgnoreCase)
            && (item.TryGetProperty("IsWaiting", out var waiting) || item.TryGetProperty("isWaiting", out waiting))
            && waiting.ValueKind == JsonValueKind.True);
    }

    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout)
    {
        var exitTask = process.WaitForExitAsync();
        return await Task.WhenAny(exitTask, Task.Delay(timeout)) == exitTask;
    }

    private static string ResolveRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Iwesun.Runtime.slnx"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }

    private sealed record CliExecution(string Command, int ExitCode, string Stdout, string Stderr, RuntimeDiagnosticFrame? Frame, long DurationMs);

}

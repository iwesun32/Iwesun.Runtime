using System.Diagnostics;
using System.Text.Json;

internal static class CliTransportFailureScenario
{
    public static async Task<FunctionalScenarioResult> RunAsync()
    {
        var root = ResolveRepositoryRoot();
        var cli = Path.Combine(root, "Iwesun.Runtime.Cli", "bin", "Debug", "net10.0", "Iwesun.Runtime.Cli.dll");
        var config = Path.Combine(root, "Iwesun.Runtime.FunctionalTests", "fixtures", "cli-transport", "TransportTestConfig.json");
        var pipe = $"missing.runtime.pipe.{Guid.NewGuid():N}";
        var startInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = root
        };
        startInfo.ArgumentList.Add(cli);
        startInfo.ArgumentList.Add($"--config={config}");
        startInfo.ArgumentList.Add($"--pipe={pipe}");
        startInfo.ArgumentList.Add("host.info");

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start CLI.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var errorText = await stderr;
        var checks = new List<string>();
        var failures = new List<string>();

        try
        {
            using var document = JsonDocument.Parse(errorText);
            var rootElement = document.RootElement;
            Check(rootElement.GetProperty("code").GetString() == "CLI_CONNECT_TIMEOUT", "connect-timeout-classified", errorText);
            Check(rootElement.GetProperty("data").GetProperty("pipeName").GetString() == pipe, "connect-timeout-pipe", errorText);
            Check(rootElement.GetProperty("data").GetProperty("phase").GetString() == "connect", "connect-timeout-phase", errorText);
            Check(rootElement.GetProperty("data").GetProperty("timeoutMs").GetInt32() == 100, "connect-timeout-budget", errorText);
            Check(rootElement.GetProperty("data").GetProperty("retryable").GetBoolean(), "connect-timeout-retryable", errorText);
        }
        catch (Exception ex)
        {
            failures.Add($"Transport error was not the required structured result: {ex.Message}; stderr={errorText}; stdout={await stdout}");
        }

        return failures.Count == 0
            ? FunctionalScenarioResult.Pass("cli-transport-failure", checks.ToArray())
            : FunctionalScenarioResult.Fail("cli-transport-failure", checks, failures);

        void Check(bool condition, string name, string evidence)
        {
            if (condition) checks.Add(name);
            else failures.Add($"{name} failed: {evidence}");
        }
    }

    private static string ResolveRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Iwesun.Runtime.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not resolve Runtime repository root.");
    }
}

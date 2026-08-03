using System.Diagnostics;

internal static class CliContextShellScenario
{
    public static async Task<FunctionalScenarioResult> RunAsync()
    {
        var checks = new List<string>();
        var failures = new List<string>();
        var repositoryRoot = ResolveRepositoryRoot();
        var cliPath = Path.Combine(repositoryRoot, "modules", "Cli", "src", "Iwesun.Runtime.Cli", "bin", "Debug", "net10.0", "Iwesun.Runtime.Cli.dll");
        var fixtureDirectory = Path.Combine(repositoryRoot, "modules", "Diagnostics", "tests", "Iwesun.Runtime.FunctionalTests", "fixtures", "cli-default");
        var explicitConfig = Path.Combine(fixtureDirectory, "ExplicitUserConfig.json");

        if (!File.Exists(cliPath) || !File.Exists(explicitConfig))
            return FunctionalScenarioResult.Fail("cli-context-shell", checks, ["Build the CLI and retain the CLI configuration fixtures before running this scenario."]);

        var defaultResult = await RunCliAsync(cliPath, fixtureDirectory, ["default.host", "--help"]);
        Check(defaultResult.ExitCode == 0, "default-user-config-loaded", $"Default user configuration failed: {defaultResult.Stderr}");

        var explicitResult = await RunCliAsync(cliPath, fixtureDirectory, [$"--user-config={explicitConfig}", "explicit.host", "--help"]);
        Check(explicitResult.ExitCode == 0, "explicit-user-config-loaded", $"Explicit user configuration failed: {explicitResult.Stderr}");

        var suppressedResult = await RunCliAsync(cliPath, fixtureDirectory, [$"--user-config={explicitConfig}", "default.host", "--help"]);
        Check(suppressedResult.ExitCode != 0, "explicit-config-overrides-default", "The default current-directory user configuration was merged after an explicit --user-config selection.");

        var shellResult = await RunCliAsync(cliPath, fixtureDirectory, ["--interactive"], "quit" + Environment.NewLine);
        Check(shellResult.ExitCode == 0, "quit-exits-shell", $"Interactive quit failed: {shellResult.Stderr}");

        var summaryHelp = await RunCliAsync(cliPath, fixtureDirectory, ["host.summary", "--help"]);
        Check(summaryHelp.ExitCode == 0, "host-summary-catalogued", $"host.summary is missing: {summaryHelp.Stderr}");

        var targetShell = await RunCliAsync(cliPath, fixtureDirectory, ["--interactive"],
            "target add service Sample.Service.Pipe" + Environment.NewLine +
            "target add agent Sample.Agent.Pipe" + Environment.NewLine +
            "lifecycle.shutdown true" + Environment.NewLine +
            "target use service" + Environment.NewLine +
            "target current" + Environment.NewLine +
            "target list" + Environment.NewLine +
            "target remove service" + Environment.NewLine +
            "quit" + Environment.NewLine);
        Check(targetShell.ExitCode == 0 && targetShell.Stdout.Contains("service=Sample.Service.Pipe", StringComparison.Ordinal),
            "target-context-local-commands", $"Target context commands failed: {targetShell.Stdout} {targetShell.Stderr}");
        Check(targetShell.Stderr.Contains("CLI_TARGET_REQUIRED", StringComparison.Ordinal),
            "destructive-command-requires-target", $"Destructive target guard did not run: {targetShell.Stdout} {targetShell.Stderr}");

        var expandedShell = await RunCliAsync(cliPath, fixtureDirectory, ["--interactive"],
            "set pipe Sample.Variable.Pipe" + Environment.NewLine +
            "set path /lifecycle" + Environment.NewLine +
            "set destination gateway" + Environment.NewLine +
            "target add gateway $pipe" + Environment.NewLine +
            "target list" + Environment.NewLine +
            "cd $path" + Environment.NewLine +
            "pwd" + Environment.NewLine +
            "@${destination} host.summary" + Environment.NewLine +
            "quit" + Environment.NewLine);
        Check(expandedShell.Stdout.Contains("gateway=Sample.Variable.Pipe", StringComparison.Ordinal),
            "variables-expand-in-target-command", $"Target variable was not expanded: {expandedShell.Stdout} {expandedShell.Stderr}");
        Check(expandedShell.Stdout.Contains("/lifecycle", StringComparison.Ordinal),
            "variables-expand-in-virtual-path", $"Path variable was not expanded: {expandedShell.Stdout} {expandedShell.Stderr}");
        Check(!expandedShell.Stderr.Contains("CLI_TARGET_NOT_FOUND", StringComparison.Ordinal),
            "variables-expand-in-at-target", $"@target variable was not expanded: {expandedShell.Stdout} {expandedShell.Stderr}");
        Check(expandedShell.Stderr.Contains("Sample.Variable.Pipe", StringComparison.Ordinal),
            "expanded-target-selects-pipe", $"Expanded target did not select its pipe: {expandedShell.Stdout} {expandedShell.Stderr}");

        var missingVariable = await RunCliAsync(cliPath, fixtureDirectory, ["--interactive"],
            "cd $missing" + Environment.NewLine + "quit" + Environment.NewLine);
        Check(missingVariable.Stderr.Contains("CLI_CONTEXT_VARIABLE_NOT_FOUND", StringComparison.Ordinal),
            "undefined-variable-stays-local", $"Undefined variable was not rejected locally: {missingVariable.Stdout} {missingVariable.Stderr}");

        return failures.Count == 0
            ? FunctionalScenarioResult.Pass("cli-context-shell", checks.ToArray())
            : FunctionalScenarioResult.Fail("cli-context-shell", checks, failures);

        void Check(bool condition, string check, string failure)
        {
            if (condition)
                checks.Add(check);
            else
                failures.Add(failure);
        }
    }

    private static async Task<CliExecutionResult> RunCliAsync(
        string cliPath,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        string? standardInput = null)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardInput = standardInput is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };
        startInfo.ArgumentList.Add(cliPath);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start the CLI process.");
        if (standardInput is not null)
        {
            await process.StandardInput.WriteAsync(standardInput);
            process.StandardInput.Close();
        }

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new CliExecutionResult(process.ExitCode, await stdout, await stderr);
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

        throw new DirectoryNotFoundException("Could not resolve the Runtime repository root.");
    }

    private sealed record CliExecutionResult(int ExitCode, string Stdout, string Stderr);
}

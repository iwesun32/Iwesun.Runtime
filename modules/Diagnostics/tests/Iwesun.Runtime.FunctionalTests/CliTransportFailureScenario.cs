using System.Diagnostics;
using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

internal static class CliTransportFailureScenario
{
    public static async Task<FunctionalScenarioResult> RunAsync()
    {
        var root = ResolveRepositoryRoot();
        var cli = Path.Combine(root, "modules", "Cli", "src", "Iwesun.Runtime.Cli", "bin", "Debug", "net10.0", "Iwesun.Runtime.Cli.dll");
        var config = Path.Combine(root, "modules", "Diagnostics", "tests", "Iwesun.Runtime.FunctionalTests", "fixtures", "cli-transport", "TransportTestConfig.json");
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

        var remoteServer = $"missing-{Guid.NewGuid():N}";
        var remote = await RunCliAsync(cli, root, [
            $"--config={config}",
            $"--server={remoteServer}",
            $"--pipe={pipe}",
            "host.info"]);
        try
        {
            using var document = JsonDocument.Parse(remote.Stderr);
            var rootElement = document.RootElement;
            Check(rootElement.GetProperty("code").GetString() is "CLI_REMOTE_CONNECT_TIMEOUT" or "CLI_REMOTE_NODE_UNREACHABLE",
                "remote-failure-classified", remote.Stderr);
            Check(rootElement.GetProperty("data").GetProperty("serverName").GetString() == remoteServer,
                "remote-server-preserved", remote.Stderr);
            Check(rootElement.GetProperty("data").GetProperty("pipeName").GetString() == pipe,
                "remote-pipe-preserved", remote.Stderr);
        }
        catch (Exception ex)
        {
            failures.Add($"Remote transport error was not structured: {ex.Message}; stderr={remote.Stderr}; stdout={remote.Stdout}");
        }

        var largePipe = $"large.runtime.pipe.{Guid.NewGuid():N}";
        var largePayload = new string('x', 1024 * 1024);
        var serverTask = ServeOneResponseAsync(largePipe, JsonSerializer.Serialize(new
        {
            header = new { schema = "rtdiag/2.0", frameType = "response" },
            status = new { ok = true, code = "OK", message = "large-frame" },
            data = new { payload = largePayload }
        }));
        var large = await RunCliAsync(cli, root, [$"--config={config}", $"--pipe={largePipe}", "host.info"]);
        await serverTask;
        Check(large.ExitCode == 0 && large.Stdout.Length > 1024 * 1024,
            "large-frame-over-one-mib-complete", $"Large frame was truncated: exit={large.ExitCode}, length={large.Stdout.Length}, stderr={large.Stderr}");

        var smallFrame = JsonSerializer.Serialize(new
        {
            header = new { schema = "rtdiag/2.0", frameType = "response" },
            status = new { ok = true, code = "OK", message = "multi" },
            data = new { ready = true }
        });
        var multiServerA = ServeOneResponseAsync("Iwesun.Runtime.Tests.MultiA", smallFrame);
        var multiServerB = ServeOneResponseAsync("Iwesun.Runtime.Tests.MultiB", smallFrame);
        var multi = await RunCliAsync(cli, root, [$"--config={config}", "multi.query", "host.info", "multi-a,multi-b", "2"]);
        await Task.WhenAll(multiServerA, multiServerB);
        Check(multi.ExitCode == 0 && multi.Stdout.Contains("\"succeeded\": 2", StringComparison.Ordinal),
            "multi-target-read-only-query", $"Multi-target query failed: {multi.Stdout} {multi.Stderr}");
        var destructiveMulti = await RunCliAsync(cli, root, [$"--config={config}", "multi.query", "lifecycle.shutdown", "multi-a,multi-b"]);
        Check(destructiveMulti.ExitCode != 0 && destructiveMulti.Stderr.Contains("CLI_MULTI_TARGET_READ_ONLY", StringComparison.Ordinal),
            "multi-target-destructive-command-rejected", $"Destructive multi-target command was not rejected: {destructiveMulti.Stdout} {destructiveMulti.Stderr}");

        return failures.Count == 0
            ? FunctionalScenarioResult.Pass("cli-transport-failure", checks.ToArray())
            : FunctionalScenarioResult.Fail("cli-transport-failure", checks, failures);

        void Check(bool condition, string name, string evidence)
        {
            if (condition) checks.Add(name);
            else failures.Add($"{name} failed: {evidence}");
        }
    }

    private static async Task ServeOneResponseAsync(string pipeName, string response)
    {
        await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await server.WaitForConnectionAsync();
        var header = new byte[4];
        await ReadExactAsync(server, header);
        var request = new byte[BinaryPrimitives.ReadInt32LittleEndian(header)];
        await ReadExactAsync(server, request);
        var bytes = Encoding.UTF8.GetBytes(response);
        BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await server.WriteAsync(header);
        await server.WriteAsync(bytes);
        try
        {
            await server.FlushAsync();
        }
        catch (IOException) when (!server.IsConnected)
        {
            // The one-shot CLI may close immediately after reading the declared payload.
        }
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset));
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunCliAsync(string cli, string workingDirectory, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };
        startInfo.ArgumentList.Add(cli);
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start CLI.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await stdout, await stderr);
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

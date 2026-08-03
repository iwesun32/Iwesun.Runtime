using System.Diagnostics;
using System.Text.Json;
using Iwesun.Runtime.Diagnostics;

namespace Iwesun.Runtime.Cli;

internal static class CliMultiTargetCoordinator
{
    public static async Task<int> RunAsync(
        CliConfiguration configuration,
        string commandName,
        IReadOnlyList<string> targetAliases,
        int concurrency,
        CancellationToken cancellationToken)
    {
        var command = configuration.Commands.SingleOrDefault(x => x.Name.Equals(commandName, StringComparison.OrdinalIgnoreCase) || x.Aliases.Contains(commandName, StringComparer.OrdinalIgnoreCase))
            ?? throw new CliException("CLI_COMMAND_UNKNOWN", $"Unknown command '{commandName}'.", 2, commandName);
        if (!command.Risk.Equals("read-only", StringComparison.OrdinalIgnoreCase))
            throw new CliException("CLI_MULTI_TARGET_READ_ONLY", $"Command '{command.Name}' is not read-only and cannot be coordinated.", 2, command.Name);
        if (command.Parameters.Any(x => x.Required))
            throw new CliException("CLI_MULTI_TARGET_PARAMETERS", "Coordinated commands with required parameters are not supported.", 2, command.Name);
        if (targetAliases.Count == 0 || targetAliases.Count > 64)
            throw new CliException("CLI_MULTI_TARGET_COUNT", "Specify between 1 and 64 target aliases.", 2, command.Name);
        concurrency = Math.Clamp(concurrency, 1, 16);

        var started = Stopwatch.GetTimestamp();
        using var gate = new SemaphoreSlim(concurrency, concurrency);
        var tasks = targetAliases.Distinct(StringComparer.OrdinalIgnoreCase).Select(async alias =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var endpoint = configuration.Endpoints.GetValueOrDefault(command.Endpoint)
                    ?? throw new CliException("CLI_ENDPOINT_UNKNOWN", $"Endpoint '{command.Endpoint}' is not defined.", 3);
                var target = CliRuntimeTargetResolver.Resolve(configuration, endpoint, command.Endpoint, alias, null, null, null);
                var frame = new RuntimeDiagnosticFrame
                {
                    Header = CliApplication.CreateHeader(RuntimeDiagnosticProtocol.V2Schema, command.Request.Category, command.Request.Operation, target.PipeName),
                    Command = CliApplication.CreateFrameCommand(command, CliApplication.Bind(command, []))
                };
                var response = await CliTransport.SendAsync(target, frame, cancellationToken);
                return new CoordinatedResult(alias, true, JsonDocument.Parse(response).RootElement.Clone(), null, null);
            }
            catch (CliException ex)
            {
                return new CoordinatedResult(alias, false, null, ex.Code, ex.Message);
            }
            finally
            {
                gate.Release();
            }
        }).ToArray();

        var results = await Task.WhenAll(tasks);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schema = "iwesun.runtime.cli.multi/1.0",
            ok = results.All(x => x.Ok),
            command = command.Name,
            summary = new
            {
                total = results.Length,
                succeeded = results.Count(x => x.Ok),
                failed = results.Count(x => !x.Ok),
                durationMs = Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 2)
            },
            results
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        return results.All(x => x.Ok) ? 0 : 6;
    }

    private sealed record CoordinatedResult(string Target, bool Ok, JsonElement? Frame, string? Code, string? Message);
}

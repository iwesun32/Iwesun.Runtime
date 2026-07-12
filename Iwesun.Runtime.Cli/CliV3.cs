using System.Buffers.Binary;
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Iwesun.Runtime.Diagnostics;

namespace Iwesun.Runtime.Cli;

internal static class CliApplication
{
    private const string Schema = "iwesun.runtime.cli/3.0";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        try
        {
            var options = CliOptions.Parse(args);
            var config = Load(options.ConfigPath);
            Validate(config);
            if (options.Help || options.CommandArguments.Count == 0)
            {
                PrintHelp(config);
                return 0;
            }

            var name = options.CommandArguments[0];
            var command = config.Commands.SingleOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                || x.Aliases.Contains(name, StringComparer.OrdinalIgnoreCase))
                ?? throw new CliException("CLI_COMMAND_UNKNOWN", $"Unknown command '{name}'.", 2, name);
            var endpoint = config.Endpoints.GetValueOrDefault(command.Endpoint)
                ?? throw new CliException("CLI_ENDPOINT_UNKNOWN", $"Endpoint '{command.Endpoint}' is not defined.", 3, name);
            var bound = Bind(command, options.CommandArguments.Skip(1).ToArray());
            var frame = new RuntimeDiagnosticFrame
            {
                Header = new RuntimeDiagnosticFrameHeader
                {
                    Schema = RuntimeDiagnosticProtocol.V2Schema,
                    FrameType = "request",
                    Category = command.Request.Category,
                    Operation = command.Request.Operation,
                    RequestId = Guid.NewGuid().ToString("N"),
                    Timestamp = DateTimeOffset.UtcNow,
                    Source = "iwrt"
                },
                Command = new RuntimeDiagnosticFrameCommand
                {
                    Domain = command.Request.Domain,
                    Target = command.Request.Target,
                    Action = command.Request.Action,
                    Member = command.Request.Member,
                    Args = bound.Count == 0 ? null : bound
                }
            };
            var pipe = options.PipeName ?? endpoint.PipeName;
            var timeout = options.TimeoutMs ?? endpoint.RequestTimeoutMs;
            var response = await SendAsync(pipe, frame, endpoint.ConnectTimeoutMs, timeout, endpoint.MaxResponseBytes, cancellationToken);
            Console.WriteLine(response);
            using var document = JsonDocument.Parse(response);
            return document.RootElement.TryGetProperty("status", out var status)
                && status.TryGetProperty("ok", out var ok) && !ok.GetBoolean() ? 6 : 0;
        }
        catch (OperationCanceledException)
        {
            RenderFailure(new CliException("CLI_CANCELLED", "Operation cancelled.", 130));
            return 130;
        }
        catch (CliException ex)
        {
            RenderFailure(ex);
            return ex.ExitCode;
        }
        catch (JsonException ex)
        {
            RenderFailure(new CliException("CLI_CONFIG_INVALID", ex.Message, 3));
            return 3;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException)
        {
            RenderFailure(new CliException("CLI_TRANSPORT_FAILURE", ex.Message, 4));
            return 4;
        }
    }

    private static CliConfiguration Load(string? explicitPath)
    {
        var candidates = new[]
        {
            explicitPath,
            Path.Combine(AppContext.BaseDirectory, "Iwesun.Runtime.Cli.commands.json"),
            Path.Combine(Environment.CurrentDirectory, "Iwesun.Runtime.Cli.commands.json")
        };
        foreach (var path in candidates.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<CliConfiguration>(File.ReadAllText(path), Json)
                    ?? throw new CliException("CLI_CONFIG_EMPTY", "CLI configuration is empty.", 3);
        }

        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Iwesun.Runtime.Cli.Iwesun.Runtime.Cli.commands.json")
            ?? throw new CliException("CLI_CONFIG_NOT_FOUND", "CLI v3 configuration was not found.", 3);
        return JsonSerializer.Deserialize<CliConfiguration>(stream, Json)
            ?? throw new CliException("CLI_CONFIG_EMPTY", "CLI configuration is empty.", 3);
    }

    private static void Validate(CliConfiguration config)
    {
        if (!string.Equals(config.Schema, Schema, StringComparison.Ordinal))
            throw new CliException("CLI_CONFIG_SCHEMA_UNSUPPORTED", $"Required schema is '{Schema}'.", 3);
        if (config.Endpoints.Count == 0 || config.Commands.Count == 0)
            throw new CliException("CLI_CONFIG_INCOMPLETE", "At least one endpoint and command are required.", 3);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pattern = new Regex("^[a-z0-9-]+(?:\\.[a-z0-9-]+)*$", RegexOptions.CultureInvariant);
        foreach (var command in config.Commands)
        {
            if (!pattern.IsMatch(command.Name) || !names.Add(command.Name))
                throw new CliException("CLI_CONFIG_COMMAND_NAME", $"Invalid or duplicate command '{command.Name}'.", 3);
            foreach (var alias in command.Aliases)
                if (!pattern.IsMatch(alias) || !names.Add(alias))
                    throw new CliException("CLI_CONFIG_ALIAS_COLLISION", $"Invalid or duplicate alias '{alias}'.", 3);
            if (!config.Endpoints.ContainsKey(command.Endpoint))
                throw new CliException("CLI_CONFIG_ENDPOINT_REFERENCE", $"Command '{command.Name}' references an unknown endpoint.", 3);
            if (new[] { command.Request.Category, command.Request.Operation, command.Request.Domain, command.Request.Target, command.Request.Action }.Any(string.IsNullOrWhiteSpace))
                throw new CliException("CLI_CONFIG_REQUEST_INCOMPLETE", $"Command '{command.Name}' has an incomplete request route.", 3);
            if (command.Parameters.GroupBy(x => x.Position).Any(x => x.Count() > 1))
                throw new CliException("CLI_CONFIG_PARAMETER_POSITION", $"Command '{command.Name}' has duplicate parameter positions.", 3);
        }
    }

    private static Dictionary<string, JsonElement> Bind(CliCommand command, string[] tokens)
    {
        var raw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var position = 0;
        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];
            if (token.StartsWith("--", StringComparison.Ordinal))
                throw new CliException("CLI_PARAMETER_SYNTAX", "Command parameters use a single dash.", 2, command.Name);
            if (token.StartsWith('-'))
            {
                var body = token[1..];
                var separator = body.IndexOf(':');
                var key = separator >= 0 ? body[..separator] : body;
                var value = separator >= 0 ? body[(separator + 1)..] : index + 1 < tokens.Length ? tokens[++index] : "";
                if (!raw.TryAdd(key, value))
                    throw new CliException("CLI_PARAMETER_DUPLICATE", $"Parameter '{key}' was supplied more than once.", 2, command.Name);
                continue;
            }
            var parameter = command.Parameters.SingleOrDefault(x => x.Position == position++)
                ?? throw new CliException("CLI_PARAMETER_UNKNOWN", $"Unexpected positional argument '{token}'.", 2, command.Name);
            if (!raw.TryAdd(parameter.Name, token))
                throw new CliException("CLI_PARAMETER_DUPLICATE", $"Parameter '{parameter.Name}' was supplied more than once.", 2, command.Name);
        }

        var result = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in raw.Keys)
            if (!command.Parameters.Any(x => x.Name.Equals(key, StringComparison.OrdinalIgnoreCase)))
                throw new CliException("CLI_PARAMETER_UNKNOWN", $"Unknown parameter '{key}'.", 2, command.Name);
        foreach (var parameter in command.Parameters)
        {
            if (!raw.TryGetValue(parameter.Name, out var value))
            {
                if (parameter.Default.HasValue) result[parameter.Name] = parameter.Default.Value.Clone();
                else if (parameter.Required) throw new CliException("CLI_PARAMETER_REQUIRED", $"Parameter '{parameter.Name}' is required.", 2, command.Name);
                continue;
            }
            result[parameter.Name] = ConvertValue(parameter, value, command.Name);
        }
        return result;
    }

    private static JsonElement ConvertValue(CliParameter parameter, string value, string command)
    {
        try
        {
            object converted = parameter.Type switch
            {
                "string" => value,
                "bool" when bool.TryParse(value, out var parsed) => parsed,
                "int32" when int.TryParse(value, out var parsed) => parsed,
                "int64" when long.TryParse(value, out var parsed) => parsed,
                "double" when double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var parsed) => parsed,
                "json" => JsonDocument.Parse(value).RootElement.Clone(),
                _ => throw new FormatException()
            };
            return JsonSerializer.SerializeToElement(converted, Json);
        }
        catch
        {
            throw new CliException("CLI_PARAMETER_TYPE", $"Parameter '{parameter.Name}' is not a valid {parameter.Type}.", 2, command);
        }
    }

    private static async Task<string> SendAsync(string pipeName, RuntimeDiagnosticFrame frame, int connectTimeoutMs, int requestTimeoutMs, int maxBytes, CancellationToken ct)
    {
        var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(frame, Json));
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var connect = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connect.CancelAfter(connectTimeoutMs);
        await pipe.ConnectAsync(connect.Token);
        using var request = CancellationTokenSource.CreateLinkedTokenSource(ct);
        request.CancelAfter(requestTimeoutMs);
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await pipe.WriteAsync(header, request.Token);
        await pipe.WriteAsync(payload, request.Token);
        await pipe.FlushAsync(request.Token);
        await ReadExactAsync(pipe, header, request.Token);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > maxBytes)
            throw new CliException("CLI_PROTOCOL_RESPONSE_LENGTH", $"Invalid response length {length}.", 5);
        var response = new byte[length];
        await ReadExactAsync(pipe, response, request.Token);
        return Encoding.UTF8.GetString(response);
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), ct);
            if (read == 0) throw new CliException("CLI_PROTOCOL_TRUNCATED", "The pipe closed before the response completed.", 5);
            offset += read;
        }
    }

    private static void PrintHelp(CliConfiguration config)
    {
        Console.WriteLine(config.Application.Name);
        Console.WriteLine(config.Application.Description);
        Console.WriteLine("Usage: iwrt [--pipe=NAME] [--config=PATH] <command> [arguments]");
        foreach (var command in config.Commands.OrderBy(x => x.Name))
            Console.WriteLine($"  {command.Name,-28} {command.Summary}");
    }

    private static void RenderFailure(CliException error) => Console.Error.WriteLine(JsonSerializer.Serialize(new
    {
        schema = "iwesun.runtime.cli.result/1.0", ok = false, code = error.Code, message = error.Message, command = error.Command
    }, Json));
}

internal sealed record CliOptions(string? ConfigPath, string? PipeName, int? TimeoutMs, bool Help, IReadOnlyList<string> CommandArguments)
{
    public static CliOptions Parse(string[] args)
    {
        string? config = null, pipe = null;
        int? timeout = null;
        var help = false;
        var command = new List<string>();
        foreach (var arg in args)
        {
            if (arg is "--help" or "-h") help = true;
            else if (arg.StartsWith("--config=")) config = arg[9..].Trim('"');
            else if (arg.StartsWith("--pipe=")) pipe = arg[7..].Trim('"');
            else if (arg.StartsWith("--timeout-ms=") && int.TryParse(arg[13..], out var parsed) && parsed > 0) timeout = parsed;
            else if (arg.StartsWith("--")) throw new CliException("CLI_OPTION_UNKNOWN", $"Unknown option '{arg}'.", 2);
            else command.Add(arg);
        }
        return new(config, pipe, timeout, help, command);
    }
}

internal sealed class CliConfiguration
{
    public string Schema { get; init; } = "";
    public CliApplicationDescriptor Application { get; init; } = new();
    public Dictionary<string, CliEndpoint> Endpoints { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<CliCommand> Commands { get; init; } = [];
    public List<JsonElement> Workflows { get; init; } = [];
    public JsonElement Extensions { get; init; }
}

internal sealed class CliApplicationDescriptor { public string Name { get; init; } = ""; public string Description { get; init; } = ""; }
internal sealed class CliEndpoint { public string Transport { get; init; } = ""; public string PipeName { get; init; } = ""; public int ConnectTimeoutMs { get; init; } = 5000; public int RequestTimeoutMs { get; init; } = 15000; public int MaxResponseBytes { get; init; } = 16777216; }
internal sealed class CliCommand { public string Name { get; init; } = ""; public string[] Aliases { get; init; } = []; public string Summary { get; init; } = ""; public string Endpoint { get; init; } = ""; public CliRequest Request { get; init; } = new(); public List<CliParameter> Parameters { get; init; } = []; }
internal sealed class CliRequest { public string Category { get; init; } = ""; public string Operation { get; init; } = ""; public string Domain { get; init; } = ""; public string Target { get; init; } = ""; public string Action { get; init; } = ""; public string? Member { get; init; } }
internal sealed class CliParameter { public string Name { get; init; } = ""; public string Type { get; init; } = "string"; public int Position { get; init; } public bool Required { get; init; } public JsonElement? Default { get; init; } }
internal sealed class CliException(string code, string message, int exitCode, string? command = null) : Exception(message) { public string Code { get; } = code; public int ExitCode { get; } = exitCode; public string? Command { get; } = command; }

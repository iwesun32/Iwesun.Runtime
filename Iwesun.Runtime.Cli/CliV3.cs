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
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip
    };

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        try
        {
            var options = CliOptions.Parse(args);
            var config = Load(options.ConfigPath);
            var userConfigPath = !string.IsNullOrWhiteSpace(options.UserConfigPath)
                ? options.UserConfigPath
                : Path.Combine(Environment.CurrentDirectory, "RuntimeCliUserConfig.json");
            if (!string.IsNullOrWhiteSpace(options.UserConfigPath) || File.Exists(userConfigPath))
                config = Merge(config, LoadFile(userConfigPath, "CLI_USER_CONFIG_NOT_FOUND"));
            Validate(config);
            if (options.Interactive || (options.CommandArguments.Count == 1 && options.CommandArguments[0].Equals("shell", StringComparison.OrdinalIgnoreCase)))
            {
                var shell = new CliInteractiveShell(
                    (command, ct) => RunAsync([.. options.GlobalArguments(), .. command], ct),
                    config.Targets,
                    config.Commands.Where(x => x.Risk.Equals("destructive", StringComparison.OrdinalIgnoreCase)).Select(x => x.Name),
                    !string.IsNullOrWhiteSpace(options.PipeName));
                return await shell.RunAsync(cancellationToken);
            }
            if (options.Help)
            {
                if (options.CommandArguments.Count == 0)
                    PrintHelp(config);
                else
                    PrintCommandHelp(config, options.CommandArguments[0]);
                return 0;
            }
            if (options.CommandArguments.Count == 0)
            {
                PrintHelp(config);
                return 0;
            }

            var name = options.CommandArguments[0];
            var command = config.Commands.SingleOrDefault(x => Matches(x.Name, x.Aliases, name));
            var composite = config.Composites.SingleOrDefault(x => Matches(x.Name, x.Aliases, name));
            if (command == null && composite == null)
                throw new CliException("CLI_COMMAND_UNKNOWN", $"Unknown command '{name}'.", 2, name);

            CliEndpoint endpoint;
            string endpointName;
            RuntimeDiagnosticFrame frame;
            if (command != null)
            {
                endpointName = command.Endpoint;
                endpoint = config.Endpoints.GetValueOrDefault(command.Endpoint)
                    ?? throw new CliException("CLI_ENDPOINT_UNKNOWN", $"Endpoint '{command.Endpoint}' is not defined.", 3, name);
                var bound = Bind(command, options.CommandArguments.Skip(1).ToArray());
                frame = new RuntimeDiagnosticFrame
                {
                    Header = CreateHeader(RuntimeDiagnosticProtocol.V2Schema, command.Request.Category, command.Request.Operation, options.PipeName ?? endpoint.PipeName),
                    Command = CreateFrameCommand(command, bound)
                };
            }
            else
            {
                if (options.CommandArguments.Count != 1)
                    throw new CliException("CLI_COMPOSITE_ARGUMENTS", $"Composite command '{name}' does not accept runtime arguments.", 2, name);
                var resolved = composite!.Steps.Select((step, index) => new
                {
                    Step = step,
                    Command = config.Commands.Single(x => x.Name.Equals(step.Command, StringComparison.OrdinalIgnoreCase)),
                    Index = index
                }).ToList();
                endpoint = config.Endpoints[resolved[0].Command.Endpoint];
                endpointName = resolved[0].Command.Endpoint;
                frame = new RuntimeDiagnosticFrame
                {
                    Header = CreateHeader(RuntimeDiagnosticProtocol.V3Schema, "batch", "execute", options.PipeName ?? endpoint.PipeName),
                    Batch = new RuntimeDiagnosticBatchRequest
                    {
                        Options = new RuntimeDiagnosticBatchOptions { StopOnError = composite.StopOnError, DeadlineMs = composite.DeadlineMs },
                        Steps = resolved.Select(x => new RuntimeDiagnosticBatchStep
                        {
                            Id = $"step-{x.Index + 1:D3}-{x.Command.Name}",
                            Command = CreateFrameCommand(x.Command, Bind(x.Command, x.Step.Arguments))
                        }).ToList()
                    }
                };
            }
            var pipe = options.PipeName ?? endpoint.PipeName;
            var timeout = options.TimeoutMs ?? endpoint.RequestTimeoutMs;
            var response = await CliTransport.SendAsync(endpointName, pipe, options.TargetAlias, frame, endpoint.ConnectTimeoutMs, timeout, endpoint.MaxResponseBytes, cancellationToken);
            Console.WriteLine(response);
            using var document = JsonDocument.Parse(response);
            return document.RootElement.TryGetProperty("status", out var status)
                && status.TryGetProperty("ok", out var ok) && !ok.GetBoolean() ? 6 : 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            RenderFailure(new CliException("CLI_LOCAL_CANCELLED", "Operation cancelled by the local caller.", 130, data: new { retryable = false }));
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

    private static bool Matches(string name, string[] aliases, string value) =>
        name.Equals(value, StringComparison.OrdinalIgnoreCase) || aliases.Contains(value, StringComparer.OrdinalIgnoreCase);

    private static RuntimeDiagnosticFrameHeader CreateHeader(string schema, string category, string operation, string destination) => new()
    {
        Schema = schema,
        FrameType = "request",
        Category = category,
        Operation = operation,
        RequestId = Guid.NewGuid().ToString("N"),
        Timestamp = DateTimeOffset.UtcNow,
        Source = "iwrt",
        Destination = destination
    };

    private static RuntimeDiagnosticFrameCommand CreateFrameCommand(CliCommand command, Dictionary<string, JsonElement> args) => new()
    {
        Domain = command.Request.Domain,
        Target = ResolveRouteTemplate(command.Request.Target, args, command.Name),
        Action = command.Request.Action,
        Member = ResolveOptionalTemplate(command.Request.Member, args, command.Name),
        Args = args.Count == 0 ? null : args
    };

    private static string? ResolveOptionalTemplate(string? template, Dictionary<string, JsonElement> bound, string commandName)
    {
        if (string.IsNullOrWhiteSpace(template) || !template.StartsWith('{') || !template.EndsWith('}'))
            return template;
        var parameterName = template[1..^1];
        if (!bound.Remove(parameterName, out var value))
            return null;
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new CliException("CLI_ROUTE_PARAMETER", $"Command '{commandName}' requires string member parameter '{parameterName}'.", 2, commandName);
        return value.GetString();
    }

    private static CliConfiguration Load(string? explicitPath)
    {
        var candidates = new[]
        {
            explicitPath,
            Path.Combine(AppContext.BaseDirectory, "RuntimeCliSystemConfig.json"),
            Path.Combine(Environment.CurrentDirectory, "RuntimeCliSystemConfig.json")
        };
        foreach (var path in candidates.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            if (File.Exists(path))
            {
                var config = JsonSerializer.Deserialize<CliConfiguration>(File.ReadAllText(path), Json)
                    ?? throw new CliException("CLI_CONFIG_EMPTY", "CLI configuration is empty.", 3);
                ApplyMetadata(config, LoadMetadata(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "RuntimeCliSystemMetadata.json")));
                return config;
            }
        }

        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Iwesun.Runtime.Cli.RuntimeCliSystemConfig.json")
            ?? throw new CliException("CLI_CONFIG_NOT_FOUND", "CLI v3 configuration was not found.", 3);
        var embedded = JsonSerializer.Deserialize<CliConfiguration>(stream, Json)
            ?? throw new CliException("CLI_CONFIG_EMPTY", "CLI configuration is empty.", 3);
        using var metadataStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Iwesun.Runtime.Cli.RuntimeCliSystemMetadata.json");
        ApplyMetadata(embedded, metadataStream == null ? null : JsonSerializer.Deserialize<CliMetadataCatalog>(metadataStream, Json));
        return embedded;
    }

    private static CliMetadataCatalog? LoadMetadata(string path) =>
        File.Exists(path) ? JsonSerializer.Deserialize<CliMetadataCatalog>(File.ReadAllText(path), Json) : null;

    private static void ApplyMetadata(CliConfiguration config, CliMetadataCatalog? metadata)
    {
        if (metadata == null)
            return;
        if (!string.Equals(metadata.Schema, "iwesun.runtime.cli.metadata/1.0", StringComparison.Ordinal))
            throw new CliException("CLI_METADATA_SCHEMA_UNSUPPORTED", "Required metadata schema is 'iwesun.runtime.cli.metadata/1.0'.", 3);
        foreach (var command in config.Commands)
        {
            if (!metadata.Commands.TryGetValue(command.Name, out var value))
                continue;
            command.Summary = value.Summary;
            command.Risk = value.Risk;
            command.Capability = value.Capability;
            command.Examples = value.Examples;
        }
    }

    private static CliConfiguration LoadFile(string path, string missingCode)
    {
        if (!File.Exists(path))
            throw new CliException(missingCode, $"CLI configuration '{path}' was not found.", 3);
        return JsonSerializer.Deserialize<CliConfiguration>(File.ReadAllText(path), Json)
            ?? throw new CliException("CLI_CONFIG_EMPTY", "CLI configuration is empty.", 3);
    }

    private static CliConfiguration Merge(CliConfiguration catalog, CliConfiguration user)
    {
        if (!string.Equals(user.Schema, Schema, StringComparison.Ordinal))
            throw new CliException("CLI_CONFIG_SCHEMA_UNSUPPORTED", $"Required schema is '{Schema}'.", 3);
        if (user.Commands.Count != 0)
            throw new CliException("CLI_USER_CONFIG_COMMANDS_UNSUPPORTED", "User commands must be declared in extensions.add or extensions.replace.", 3);

        var endpoints = new Dictionary<string, CliEndpoint>(catalog.Endpoints, StringComparer.OrdinalIgnoreCase);
        foreach (var endpoint in user.Endpoints)
            endpoints[endpoint.Key] = endpoint.Value;
        var targets = new Dictionary<string, CliTarget>(catalog.Targets, StringComparer.OrdinalIgnoreCase);
        foreach (var target in user.Targets)
            targets[target.Key] = target.Value;

        var commands = catalog.Commands.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var name in user.Extensions.Disable)
            if (!commands.Remove(name))
                throw new CliException("CLI_CONFIG_EXTENSION_TARGET", $"Cannot disable unknown command '{name}'.", 3);
        foreach (var command in user.Extensions.Replace)
            if (!commands.ContainsKey(command.Name))
                throw new CliException("CLI_CONFIG_EXTENSION_TARGET", $"Cannot replace unknown command '{command.Name}'.", 3);
            else
                commands[command.Name] = command;
        foreach (var command in user.Extensions.Add)
            if (!commands.TryAdd(command.Name, command))
                throw new CliException("CLI_CONFIG_EXTENSION_COLLISION", $"Cannot add duplicate command '{command.Name}'.", 3);
        foreach (var extension in user.Extensions.Extend)
        {
            if (!commands.TryGetValue(extension.Name, out var command))
                throw new CliException("CLI_CONFIG_EXTENSION_TARGET", $"Cannot extend unknown command '{extension.Name}'.", 3);
            commands[extension.Name] = new CliCommand
            {
                Name = command.Name,
                Aliases = command.Aliases.Concat(extension.Aliases).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                Summary = command.Summary,
                Risk = command.Risk,
                Capability = command.Capability,
                Examples = command.Examples,
                Endpoint = command.Endpoint,
                Request = command.Request,
                Parameters = command.Parameters
            };
        }

        return new CliConfiguration
        {
            Schema = catalog.Schema,
            Application = catalog.Application,
            Endpoints = endpoints,
            Targets = targets,
            Commands = commands.Values.ToList(),
            Composites = catalog.Composites.Concat(user.Composites).ToList()
        };
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
            if (string.IsNullOrWhiteSpace(command.Summary))
                command.Summary = $"{command.Request.Action} {command.Request.Target}.";
            if (string.IsNullOrWhiteSpace(command.Risk))
                command.Risk = command.Request.Operation is "shutdown" or "delete" or "clear"
                    ? "destructive"
                    : command.Request.Category == "instruction" ? "state-changing" : "read-only";
            if (string.IsNullOrWhiteSpace(command.Capability))
                command.Capability = command.Name.Equals("reflection.invoke", StringComparison.OrdinalIgnoreCase)
                    ? "debug-only"
                    : "all-builds";
            if (command.Examples.Length == 0)
                command.Examples = [$"iwrt {command.Name}"];
            if (new[] { command.Request.Category, command.Request.Operation, command.Request.Domain, command.Request.Target, command.Request.Action }.Any(string.IsNullOrWhiteSpace))
                throw new CliException("CLI_CONFIG_REQUEST_INCOMPLETE", $"Command '{command.Name}' has an incomplete request route.", 3);
            if (command.Parameters.GroupBy(x => x.Position).Any(x => x.Count() > 1))
                throw new CliException("CLI_CONFIG_PARAMETER_POSITION", $"Command '{command.Name}' has duplicate parameter positions.", 3);
            if (command.Request.Target.StartsWith('{') && command.Request.Target.EndsWith('}'))
            {
                var routeParameter = command.Request.Target[1..^1];
                if (!command.Parameters.Any(parameter => parameter.Name.Equals(routeParameter, StringComparison.OrdinalIgnoreCase)))
                    throw new CliException("CLI_CONFIG_ROUTE_PARAMETER", $"Command '{command.Name}' target references undeclared parameter '{routeParameter}'.", 3);
            }
        }
        var commands = config.Commands.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var composite in config.Composites)
        {
            if (!pattern.IsMatch(composite.Name) || !names.Add(composite.Name))
                throw new CliException("CLI_CONFIG_COMPOSITE_NAME", $"Invalid or duplicate composite '{composite.Name}'.", 3);
            foreach (var alias in composite.Aliases)
                if (!pattern.IsMatch(alias) || !names.Add(alias))
                    throw new CliException("CLI_CONFIG_ALIAS_COLLISION", $"Invalid or duplicate alias '{alias}'.", 3);
            if (composite.Steps.Count is < 1 or > 128)
                throw new CliException("CLI_CONFIG_COMPOSITE_STEPS", $"Composite '{composite.Name}' must contain between 1 and 128 steps.", 3);
            var endpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var step in composite.Steps)
            {
                if (!commands.TryGetValue(step.Command, out var child))
                    throw new CliException("CLI_CONFIG_COMPOSITE_COMMAND", $"Composite '{composite.Name}' references unknown command '{step.Command}'.", 3);
                endpoints.Add(child.Endpoint);
                Bind(child, step.Arguments);
            }
            if (endpoints.Count != 1)
                throw new CliException("CLI_CONFIG_COMPOSITE_ENDPOINT", $"Composite '{composite.Name}' must use exactly one endpoint.", 3);
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
            var positionalIndex = position++;
            var parameter = command.Parameters.SingleOrDefault(x => x.Position == positionalIndex)
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

    private static string ResolveRouteTemplate(string template, Dictionary<string, JsonElement> bound, string commandName)
    {
        if (!template.StartsWith('{') || !template.EndsWith('}'))
            return template;
        var parameterName = template[1..^1];
        if (parameterName.Length == 0 || !bound.Remove(parameterName, out var value) || value.ValueKind != JsonValueKind.String)
            throw new CliException("CLI_ROUTE_PARAMETER", $"Command '{commandName}' requires string route parameter '{parameterName}'.", 2, commandName);
        var route = value.GetString();
        if (string.IsNullOrWhiteSpace(route))
            throw new CliException("CLI_ROUTE_PARAMETER", $"Route parameter '{parameterName}' cannot be empty.", 2, commandName);
        return route;
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

    private static void PrintHelp(CliConfiguration config)
    {
        Console.WriteLine(config.Application.Name);
        Console.WriteLine(config.Application.Description);
        Console.WriteLine("Usage: iwrt [--pipe=NAME] [--config=PATH] [--user-config=PATH] <command> [arguments]");
        Console.WriteLine("       iwrt shell | iwrt --interactive");
        foreach (var command in config.Commands.OrderBy(x => x.Name))
            Console.WriteLine($"  {command.Name,-28} {command.Summary}");
        foreach (var composite in config.Composites.OrderBy(x => x.Name))
            Console.WriteLine($"  {composite.Name,-28} {composite.Summary} [composite]");
    }

    private static void PrintCommandHelp(CliConfiguration config, string name)
    {
        var command = config.Commands.SingleOrDefault(x => Matches(x.Name, x.Aliases, name));
        if (command == null)
        {
            var composite = config.Composites.SingleOrDefault(x => Matches(x.Name, x.Aliases, name));
            if (composite == null)
                throw new CliException("CLI_COMMAND_UNKNOWN", $"Unknown command '{name}'.", 2, name);
            Console.WriteLine(composite.Name);
            Console.WriteLine(composite.Summary);
            Console.WriteLine("Type: composite batch (single endpoint)");
            Console.WriteLine($"Steps: {string.Join(", ", composite.Steps.Select(x => x.Command))}");
            return;
        }

        Console.WriteLine(command.Name);
        Console.WriteLine(command.Summary);
        Console.WriteLine($"Endpoint: {command.Endpoint}");
        Console.WriteLine($"Route: {command.Request.Domain}/{command.Request.Target}/{command.Request.Action}");
        Console.WriteLine($"Risk: {command.Risk}");
        Console.WriteLine($"Capability: {command.Capability}");
        Console.WriteLine("Parameters:");
        if (command.Parameters.Count == 0)
            Console.WriteLine("  (none)");
        foreach (var parameter in command.Parameters.OrderBy(x => x.Position))
            Console.WriteLine($"  {parameter.Position}: {parameter.Name} ({parameter.Type}, {(parameter.Required ? "required" : "optional")})");
        Console.WriteLine("Examples:");
        var examples = command.Examples.Length == 0 ? [$"iwrt {command.Name}"] : command.Examples;
        foreach (var example in examples)
            Console.WriteLine($"  {example}");
    }

    private static void RenderFailure(CliException error) => Console.Error.WriteLine(JsonSerializer.Serialize(new
    {
        schema = "iwesun.runtime.cli.result/1.0", ok = false, code = error.Code, message = error.Message, command = error.Command, data = error.ErrorData
    }, Json));

    internal static void RenderShellFailure(CliException error) => RenderFailure(error);
}

internal sealed record CliOptions(string? ConfigPath, string? UserConfigPath, string? PipeName, string? TargetAlias, int? TimeoutMs, bool Help, bool Interactive, IReadOnlyList<string> CommandArguments)
{
    public IEnumerable<string> GlobalArguments()
    {
        if (!string.IsNullOrWhiteSpace(ConfigPath)) yield return $"--config={ConfigPath}";
        if (!string.IsNullOrWhiteSpace(UserConfigPath)) yield return $"--user-config={UserConfigPath}";
        if (!string.IsNullOrWhiteSpace(PipeName)) yield return $"--pipe={PipeName}";
        if (!string.IsNullOrWhiteSpace(TargetAlias)) yield return $"--target-alias={TargetAlias}";
        if (TimeoutMs.HasValue) yield return $"--timeout-ms={TimeoutMs.Value}";
    }

    public static CliOptions Parse(string[] args)
    {
        string? config = null, userConfig = null, pipe = null, targetAlias = null;
        int? timeout = null;
        var help = false;
        var interactive = false;
        var command = new List<string>();
        foreach (var arg in args)
        {
            if (arg is "--help" or "-h") help = true;
            else if (arg == "--interactive") interactive = true;
            else if (arg.StartsWith("--config=")) config = arg[9..].Trim('"');
            else if (arg.StartsWith("--user-config=")) userConfig = arg[14..].Trim('"');
            else if (arg.StartsWith("--pipe=")) pipe = arg[7..].Trim('"');
            else if (arg.StartsWith("--target-alias=")) targetAlias = arg[15..].Trim('"');
            else if (arg.StartsWith("--timeout-ms=") && int.TryParse(arg[13..], out var parsed) && parsed > 0) timeout = parsed;
            else if (arg.StartsWith("--")) throw new CliException("CLI_OPTION_UNKNOWN", $"Unknown option '{arg}'.", 2);
            else command.Add(arg);
        }
        return new(config, userConfig, pipe, targetAlias, timeout, help, interactive, command);
    }
}

internal sealed class CliConfiguration
{
    public string Schema { get; init; } = "";
    public CliApplicationDescriptor Application { get; init; } = new();
    public Dictionary<string, CliEndpoint> Endpoints { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, CliTarget> Targets { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<CliCommand> Commands { get; init; } = [];
    public List<CliComposite> Composites { get; init; } = [];
    public CliExtensions Extensions { get; init; } = new();
}

internal sealed class CliApplicationDescriptor { public string Name { get; init; } = ""; public string Description { get; init; } = ""; }
internal sealed class CliEndpoint { public string Transport { get; init; } = ""; public string PipeName { get; init; } = ""; public int ConnectTimeoutMs { get; init; } = 5000; public int RequestTimeoutMs { get; init; } = 15000; public int MaxResponseBytes { get; init; } = 16777216; }
internal sealed class CliTarget { public string Endpoint { get; init; } = "diagnostics"; public string PipeName { get; init; } = ""; }
internal sealed class CliCommand { public string Name { get; init; } = ""; public string[] Aliases { get; init; } = []; public string Summary { get; set; } = ""; public string Risk { get; set; } = ""; public string Capability { get; set; } = ""; public string[] Examples { get; set; } = []; public string Endpoint { get; init; } = ""; public CliRequest Request { get; init; } = new(); public List<CliParameter> Parameters { get; init; } = []; }
internal sealed class CliRequest { public string Category { get; init; } = ""; public string Operation { get; init; } = ""; public string Domain { get; init; } = ""; public string Target { get; init; } = ""; public string Action { get; init; } = ""; public string? Member { get; init; } }
internal sealed class CliParameter { public string Name { get; init; } = ""; public string Type { get; init; } = "string"; public int Position { get; init; } public bool Required { get; init; } public JsonElement? Default { get; init; } }
internal sealed class CliComposite { public string Name { get; init; } = ""; public string[] Aliases { get; init; } = []; public string Summary { get; init; } = ""; public bool StopOnError { get; init; } = true; public int DeadlineMs { get; init; } = 30000; public List<CliCompositeStep> Steps { get; init; } = []; }
internal sealed class CliCompositeStep { public string Command { get; init; } = ""; public string[] Arguments { get; init; } = []; }
internal sealed class CliExtensions { public List<CliCommand> Add { get; init; } = []; public List<CliCommandExtension> Extend { get; init; } = []; public List<CliCommand> Replace { get; init; } = []; public string[] Disable { get; init; } = []; }
internal sealed class CliCommandExtension { public string Name { get; init; } = ""; public string[] Aliases { get; init; } = []; }
internal sealed class CliMetadataCatalog { public string Schema { get; init; } = ""; public Dictionary<string, CliCommandMetadata> Commands { get; init; } = new(StringComparer.OrdinalIgnoreCase); }
internal sealed class CliCommandMetadata { public string Summary { get; init; } = ""; public string Risk { get; init; } = ""; public string Capability { get; init; } = ""; public string[] Examples { get; init; } = []; }
internal sealed class CliException(string code, string message, int exitCode, string? command = null, object? data = null) : Exception(message) { public string Code { get; } = code; public int ExitCode { get; } = exitCode; public string? Command { get; } = command; public object? ErrorData { get; } = data; }

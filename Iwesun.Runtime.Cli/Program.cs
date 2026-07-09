using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Iwesun.Runtime.Diagnostics;
using Iwesun.Runtime.Cli;

var options = CliOptions.Parse(args);
var catalog = UnifiedCommandCatalog.Load(options.ConfigPath);

if (options.ShowHelp || options.CommandArgs.Count == 0)
{
	PrintHelp(catalog);
	return 0;
}

try
{
	var inputLine = string.Join(' ', options.CommandArgs);
	var parsed = CommandParser.Parse(inputLine);

	if (catalog.CompositeLookup.TryGetValue(parsed.FullName, out var composite))
	{
		var compositeResult = await CompositeCommandExecutor.ExecuteAsync(
			composite,
			parsed,
			catalog.BaseLookup,
			cmd => SendDiagnosticV2Async(options, null, null, cmd),
			catalog.Memory);
		Console.WriteLine(compositeResult);
		return 0;
	}

	if (!catalog.BaseLookup.TryGetValue(parsed.FullName, out var commandDef))
		throw new ArgumentException($"Unknown unified command: {parsed.FullName}");

	if (!IsDiagnosticsTransport(commandDef.Transport))
		throw new NotSupportedException($"Transport '{commandDef.Transport}' is not enabled in unified mode.");

	var command = CommandParser.Resolve(parsed, catalog.BaseLookup);
	var result = await SendDiagnosticV2Async(options, parsed, commandDef, command);
	Console.WriteLine(result);
	return 0;
}
catch (Exception ex)
{
	Console.Error.WriteLine(JsonSerializer.Serialize(new
	{
		success = false,
		errorType = ex.GetType().Name,
		error = ex.Message
	}, JsonDefaults.Options));
	return 1;
}

static bool IsDiagnosticsTransport(string transport) =>
	transport.Equals("diagnostics", StringComparison.OrdinalIgnoreCase)
	|| transport.Equals("runtime-diagnostics", StringComparison.OrdinalIgnoreCase);

static async Task<string> SendDiagnosticV2Async(
	CliOptions options,
	ParsedCommand? parsed,
	BaseCommandDef? commandDef,
	RuntimeDiagnosticFrameCommand command)
{
	var semantic = CliSemanticCommand.From(parsed, commandDef, command);
	var frame = RuntimeDiagnosticsFrameFactory.Wrap(semantic);

	var json = JsonSerializer.Serialize(frame, JsonDefaults.Options);
	return await SendLengthPrefixedJsonAsync(
		options.DiagnosticsPipeName,
		json,
		options.ConnectTimeout,
		options.RequestTimeout,
		options.MaxResponseBytes);
}

static async Task<string> SendLengthPrefixedJsonAsync(
	string pipeName,
	string json,
	TimeSpan connectTimeout,
	TimeSpan requestTimeout,
	int maxResponseBytes)
{
	var bytes = Encoding.UTF8.GetBytes(json);

	await using var client = new NamedPipeClientStream(
		".",
		pipeName,
		PipeDirection.InOut,
		PipeOptions.Asynchronous);

	using var connectCts = new CancellationTokenSource(connectTimeout);
	await client.ConnectAsync(connectCts.Token).ConfigureAwait(false);

	using var requestCts = new CancellationTokenSource(requestTimeout);
	var length = BitConverter.GetBytes(bytes.Length);
	await client.WriteAsync(length, requestCts.Token).ConfigureAwait(false);
	await client.WriteAsync(bytes, requestCts.Token).ConfigureAwait(false);
	await client.FlushAsync(requestCts.Token).ConfigureAwait(false);

	var responseLengthBuffer = new byte[4];
	await ReadExactAsync(client, responseLengthBuffer, requestCts.Token).ConfigureAwait(false);
	var responseLength = BitConverter.ToInt32(responseLengthBuffer, 0);
	if (responseLength <= 0 || responseLength > maxResponseBytes)
		throw new InvalidOperationException($"Invalid diagnostics response length: {responseLength}.");

	var responseBuffer = new byte[responseLength];
	await ReadExactAsync(client, responseBuffer, requestCts.Token).ConfigureAwait(false);
	return Encoding.UTF8.GetString(responseBuffer);
}

static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
{
	var offset = 0;
	while (offset < buffer.Length)
	{
		var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct).ConfigureAwait(false);
		if (read == 0)
			throw new EndOfStreamException("Diagnostics pipe closed before the response was complete.");
		offset += read;
	}
}

static void PrintHelp(UnifiedCommandCatalog catalog)
{
	Console.WriteLine(catalog.Meta.Name);
	if (!string.IsNullOrWhiteSpace(catalog.Meta.Description))
		Console.WriteLine(catalog.Meta.Description);
	Console.WriteLine();
	Console.WriteLine("Usage:");
	Console.WriteLine("  iwrt [--pipe=NAME] [--config=PATH] [--timeout-ms=15000] <command>");
	Console.WriteLine();
	Console.WriteLine("Unified Commands:");

	var rows = catalog.BaseCommands
		.Select(command => new
		{
			Name = command.Aliases.Length == 0
				? command.Name
				: $"{command.Name} ({string.Join(", ", command.Aliases)})",
			Help = BuildHelpText(command.Help, command.Usage, command.Examples)
		})
		.Concat(catalog.CompositeCommands.Select(command => new
		{
			Name = command.Aliases.Length == 0
				? command.Name
				: $"{command.Name} ({string.Join(", ", command.Aliases)})",
			Help = BuildHelpText(command.Help, command.Mode, command.Steps.Select(step => step.Command))
		}))
		.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
		.ToArray();

	var width = Math.Max(34, rows.Select(x => x.Name.Length).DefaultIfEmpty(0).Max() + 4);
	foreach (var row in rows)
		Console.WriteLine($"  {row.Name}".PadRight(width) + row.Help);
}

static string BuildHelpText(string help, string usage, IEnumerable<string> examples)
{
	var text = string.IsNullOrWhiteSpace(help) ? "" : help.Trim();
	if (!string.IsNullOrWhiteSpace(usage))
		text = string.IsNullOrWhiteSpace(text) ? usage.Trim() : $"{text} | {usage.Trim()}";
	var exampleText = examples.Where(x => !string.IsNullOrWhiteSpace(x)).Take(3).ToArray();
	if (exampleText.Length > 0)
		text = string.IsNullOrWhiteSpace(text) ? $"e.g. {string.Join("; ", exampleText)}" : $"{text} | e.g. {string.Join("; ", exampleText)}";
	return text;
}

internal sealed class CliOptions
{
	public string DiagnosticsPipeName { get; private init; } = "DdnsSnap.RuntimeDiagnostics";
	public string ConfigPath { get; private init; } = "";
	public TimeSpan ConnectTimeout { get; private init; } = TimeSpan.FromSeconds(5);
	public TimeSpan RequestTimeout { get; private init; } = TimeSpan.FromSeconds(15);
	public int MaxResponseBytes { get; private init; } = 16 * 1024 * 1024;
	public bool ShowHelp { get; private init; }
	public IReadOnlyList<string> CommandArgs { get; private init; } = [];

	public static CliOptions Parse(string[] args)
	{
		var pipeName = "DdnsSnap.RuntimeDiagnostics";
		var configPath = ResolveDefaultConfigPath();
		var connectTimeout = TimeSpan.FromSeconds(5);
		var requestTimeout = TimeSpan.FromSeconds(15);
		var maxResponseBytes = 16 * 1024 * 1024;
		var showHelp = false;
		var commandArgs = new List<string>();

		foreach (var arg in args)
		{
			if (arg.Equals("--help", StringComparison.OrdinalIgnoreCase) || arg.Equals("-h", StringComparison.OrdinalIgnoreCase))
			{
				showHelp = true;
				continue;
			}

			if (arg.StartsWith("--pipe=", StringComparison.OrdinalIgnoreCase))
			{
				pipeName = arg["--pipe=".Length..].Trim('"');
				continue;
			}

			if (arg.StartsWith("--config=", StringComparison.OrdinalIgnoreCase))
			{
				configPath = arg["--config=".Length..].Trim('"');
				continue;
			}

			if (arg.StartsWith("--timeout-ms=", StringComparison.OrdinalIgnoreCase)
				&& int.TryParse(arg["--timeout-ms=".Length..], out var timeoutMs)
				&& timeoutMs > 0)
			{
				requestTimeout = TimeSpan.FromMilliseconds(timeoutMs);
				continue;
			}

			if (arg.StartsWith("--connect-timeout-ms=", StringComparison.OrdinalIgnoreCase)
				&& int.TryParse(arg["--connect-timeout-ms=".Length..], out var connectTimeoutMs)
				&& connectTimeoutMs > 0)
			{
				connectTimeout = TimeSpan.FromMilliseconds(connectTimeoutMs);
				continue;
			}

			if (arg.StartsWith("--max-response-bytes=", StringComparison.OrdinalIgnoreCase)
				&& int.TryParse(arg["--max-response-bytes=".Length..], out var maxBytes)
				&& maxBytes > 0)
			{
				maxResponseBytes = maxBytes;
				continue;
			}

			commandArgs.Add(arg);
		}

		return new CliOptions
		{
			DiagnosticsPipeName = pipeName,
			ConfigPath = configPath,
			ConnectTimeout = connectTimeout,
			RequestTimeout = requestTimeout,
			MaxResponseBytes = maxResponseBytes,
			ShowHelp = showHelp,
			CommandArgs = commandArgs
		};
	}

	private static string ResolveDefaultConfigPath()
	{
		var primary = Path.Combine(AppContext.BaseDirectory, "Iwesun.Runtime.Cli.commands.v2.json");
		if (File.Exists(primary))
			return primary;

		return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Iwesun.Runtime.Cli.commands.v2.json"));
	}
}

internal sealed class UnifiedCommandCatalog
{
	public required CommandMeta Meta { get; init; }
	public required IReadOnlyList<BaseCommandDef> BaseCommands { get; init; }
	public required IReadOnlyList<CompositeCommandDef> CompositeCommands { get; init; }
	public required Dictionary<string, BaseCommandDef> BaseLookup { get; init; }
	public required Dictionary<string, CompositeCommandDef> CompositeLookup { get; init; }
	public required Dictionary<string, string> Memory { get; init; }

	public static UnifiedCommandCatalog Load(string configPath)
	{
		if (!File.Exists(configPath))
			throw new FileNotFoundException($"Unified command config not found: {configPath}");

		var config = CommandConfigStore.Load(configPath);
		return new UnifiedCommandCatalog
		{
			Meta = config.Meta,
			BaseCommands = config.BaseCommands,
			CompositeCommands = config.CompositeCommands,
			BaseLookup = CommandParser.BuildLookup(config.BaseCommands),
			CompositeLookup = CommandParser.BuildCompositeLookup(config.CompositeCommands),
			Memory = config.Memory
		};
	}
}

internal static class JsonDefaults
{
	public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
	{
		WriteIndented = true
	};
}

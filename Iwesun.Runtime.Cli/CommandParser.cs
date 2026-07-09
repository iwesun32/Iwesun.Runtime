using System.Text.Json;
using System.Text.RegularExpressions;
using Iwesun.Runtime.Diagnostics;

namespace Iwesun.Runtime.Cli;

// ── Command Models ────────────────────────────────────────

/// <summary>Parsed command from CLI input.</summary>
public sealed class ParsedCommand
{
	public string Namespace { get; init; } = "";
	public string Verb { get; init; } = "";
	public string? Target { get; init; }
	public Dictionary<string, string> Args { get; init; } = new(StringComparer.OrdinalIgnoreCase);
	public HashSet<string> Flags { get; init; } = new(StringComparer.OrdinalIgnoreCase);
	public string? RawTail { get; init; }

	public string FullName => string.IsNullOrEmpty(Namespace) ? Verb : $"{Namespace}.{Verb}";

	public override string ToString() => $"{FullName} {Target ?? ""} {string.Join(' ', Args.Select(kv => $"{kv.Key}={kv.Value}"))} {string.Join(' ', Flags.Select(f => $"--{f}"))}".Trim();
}

/// <summary>Base command definition (atomic operation).</summary>
public sealed class BaseCommandDef
{
	public string Name { get; init; } = "";
	public string[] Aliases { get; init; } = [];
	public string Help { get; init; } = "";
	public string Transport { get; init; } = "diagnostics";
	public string Category { get; init; } = "";
	public string? Operation { get; init; }
	public string? TargetId { get; init; }
	public string? Action { get; init; }
	public Dictionary<string, ParamDef> Params { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Parameter definition.</summary>
public sealed class ParamDef
{
	public string Type { get; init; } = "string";
	public int Position { get; init; } = -1;
	public bool Required { get; init; }
	public string? Default { get; init; }
	public string? From { get; init; }
	public string? Key { get; init; }
}

/// <summary>Composite command step.</summary>
public sealed class CompositeStep
{
	public string Command { get; init; } = "";
	public int Delay { get; init; }
	public bool SkipOnError { get; init; }
	public string? Condition { get; init; }
	public string? Capture { get; init; }
}

/// <summary>Composite command definition.</summary>
public sealed class CompositeCommandDef
{
	public string Name { get; init; } = "";
	public string[] Aliases { get; init; } = [];
	public string Help { get; init; } = "";
	public string Mode { get; init; } = "sequential";
	public List<CompositeStep> Steps { get; init; } = [];
	public Dictionary<string, ParamDef> Params { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Full command configuration.</summary>
public sealed class CommandConfig
{
	public int Version { get; init; } = 2;
	public CommandMeta Meta { get; init; } = new();
	public Dictionary<string, string> Pipes { get; init; } = new(StringComparer.OrdinalIgnoreCase);
	public Dictionary<string, string> Memory { get; init; } = new(StringComparer.OrdinalIgnoreCase);
	public List<BaseCommandDef> BaseCommands { get; init; } = [];
	public List<CompositeCommandDef> CompositeCommands { get; init; } = [];
}

public sealed class CommandMeta
{
	public string Name { get; init; } = "";
	public string Description { get; init; } = "";
}

// ── Command Parser ─────────────────────────────────────────

public static class CommandParser
{
	// Parse: "namespace.verb target key=value --flags"
	// Examples:
	//   "sw.enable dns"
	//   "sw.set pipe=true --persist"
	//   "bp.on bp-001 timeout=0"
	//   "ref.get agent.state Counter"
	//   "host.events 10"
	private static readonly Regex ParseRegex = new(
		@"^(?:(?<ns>\w+)\.)?(?<verb>\w+)(?:\s+(?<target>\S+))?(?<tail>.*)$",
		RegexOptions.Compiled | RegexOptions.IgnoreCase);

	private static readonly Regex ArgsRegex = new(
		@"(\w+)=(\S+)",
		RegexOptions.Compiled | RegexOptions.IgnoreCase);

	private static readonly Regex FlagsRegex = new(
		@"--(\w[\w-]*)",
		RegexOptions.Compiled | RegexOptions.IgnoreCase);

	public static ParsedCommand Parse(string input)
	{
		var match = ParseRegex.Match(input.Trim());
		if (!match.Success)
			return new ParsedCommand { Verb = input.Trim() };

		var ns = match.Groups["ns"].Success ? match.Groups["ns"].Value : "";
		var verb = match.Groups["verb"].Value;
		var target = match.Groups["target"].Success ? match.Groups["target"].Value : null;
		var tail = match.Groups["tail"].Success ? match.Groups["tail"].Value.Trim() : "";

		var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		// Parse key=value pairs
		foreach (Match m in ArgsRegex.Matches(tail))
		{
			args[m.Groups[1].Value] = m.Groups[2].Value;
		}

		// Parse --flags
		foreach (Match m in FlagsRegex.Matches(tail))
		{
			flags.Add(m.Groups[1].Value);
		}

		// If target is truthy and looks like a value (true/false/number), treat as first positional arg
		if (target != null && (target == "true" || target == "false" || int.TryParse(target, out _)))
		{
			// This is likely a positional arg, not a target
			// Move it to args with a positional key
			args["_p0"] = target;
			target = null;
		}

		// If target itself is key=value, normalize into named args.
		if (!string.IsNullOrWhiteSpace(target))
		{
			var splitTarget = target.IndexOf('=');
			if (splitTarget > 0)
			{
				args[target[..splitTarget]] = target[(splitTarget + 1)..];
				target = null;
			}
		}

		// Parse remaining positional args from tail
		if (!string.IsNullOrEmpty(tail))
		{
			var parts = tail.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			var posIndex = target == null ? 0 : 1;
			foreach (var part in parts)
			{
				if (part.Contains('=') || part.StartsWith("--"))
					continue;
				args[$"_p{posIndex++}"] = part;
			}
		}

		return new ParsedCommand
		{
			Namespace = ns,
			Verb = verb,
			Target = target,
			Args = args,
			Flags = flags,
			RawTail = tail
		};
	}

	/// <summary>
	/// Resolve a parsed command to a RuntimeDiagnosticFrameCommand using base command definitions.
	/// </summary>
	public static RuntimeDiagnosticFrameCommand Resolve(ParsedCommand parsed, Dictionary<string, BaseCommandDef> baseCommands)
	{
		var fullName = parsed.FullName;

		// Try exact match first
		if (!baseCommands.TryGetValue(fullName, out var def))
			return new RuntimeDiagnosticFrameCommand { Action = fullName };

		// Build args from parsed params
		Dictionary<string, JsonElement>? args = null;
		if (def.Params.Count > 0)
		{
			args = new Dictionary<string, JsonElement>();
			foreach (var (name, param) in def.Params)
			{
				string? value = null;
				if (param.Position >= 0 && param.Position == 0 && parsed.Target != null)
					value = parsed.Target;
				else if (parsed.Args.TryGetValue(name, out var v))
					value = v;
				else if (parsed.Args.TryGetValue($"_p{param.Position}", out var pv))
					value = pv;
				else if (param.Default != null)
					value = param.Default;

				if (value == null && param.Required)
					throw new ArgumentException($"Command '{def.Name}' missing required parameter: {name}.");

				if (value != null)
				{
					args[name] = param.Type switch
					{
						"bool" => JsonSerializer.SerializeToElement(bool.Parse(value)),
						"int" => JsonSerializer.SerializeToElement(int.Parse(value)),
						_ => JsonSerializer.SerializeToElement(value)
					};
				}
			}
		}

		var targetId = def.TargetId ?? "";
		string? member = null;
		JsonElement? payloadValue = null;

		if (!string.IsNullOrWhiteSpace(targetId) == false && args != null && args.TryGetValue("targetId", out var targetArg) && targetArg.ValueKind == JsonValueKind.String)
		{
			targetId = targetArg.GetString() ?? "";
			args.Remove("targetId");
		}

		if (args != null && args.TryGetValue("member", out var memberArg) && memberArg.ValueKind == JsonValueKind.String)
		{
			member = memberArg.GetString();
			args.Remove("member");
		}

		if (args != null && args.TryGetValue("value", out var valueArg))
		{
			payloadValue = valueArg.Clone();
			args.Remove("value");
		}

		if (payloadValue != null)
		{
			args ??= new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
			args["value"] = payloadValue.Value.Clone();
		}

		return new RuntimeDiagnosticFrameCommand
		{
			Domain = string.IsNullOrWhiteSpace(parsed.Namespace) ? "runtime" : parsed.Namespace,
			Target = targetId,
			Action = def.Action ?? parsed.Verb,
			Member = member,
			Args = args != null && args.Count == 0 ? null : args
		};
	}

	/// <summary>
	/// Build a lookup dictionary from flat command name to definition.
	/// </summary>
	public static Dictionary<string, BaseCommandDef> BuildLookup(List<BaseCommandDef> commands)
	{
		var dict = new Dictionary<string, BaseCommandDef>(StringComparer.OrdinalIgnoreCase);
		foreach (var cmd in commands)
		{
			dict[cmd.Name] = cmd;
			foreach (var alias in cmd.Aliases)
				dict[alias] = cmd;
		}
		return dict;
	}

	/// <summary>
	/// Build a lookup for composite commands.
	/// </summary>
	public static Dictionary<string, CompositeCommandDef> BuildCompositeLookup(List<CompositeCommandDef> commands)
	{
		var dict = new Dictionary<string, CompositeCommandDef>(StringComparer.OrdinalIgnoreCase);
		foreach (var cmd in commands)
		{
			dict[cmd.Name] = cmd;
			foreach (var alias in cmd.Aliases)
				dict[alias] = cmd;
		}
		return dict;
	}
}

// ── Command Config Store ───────────────────────────────────

public static class CommandConfigStore
{
	public static CommandConfig Load(string configPath)
	{
		if (!File.Exists(configPath))
			return new CommandConfig();

		var json = File.ReadAllText(configPath);
		return JsonSerializer.Deserialize<CommandConfig>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
			?? new CommandConfig();
	}

	public static void Save(string configPath, CommandConfig config)
	{
		var dir = Path.GetDirectoryName(configPath);
		if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
			Directory.CreateDirectory(dir);

		var json = JsonSerializer.Serialize(config, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
		File.WriteAllText(configPath, json);
	}
}
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
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
	public string Usage { get; init; } = "";
	public List<string> Examples { get; init; } = [];
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

	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
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
	//   "sw-set pipe=true --persist"
	//   "bp-on bp-001 timeout=0"
	//   "ref.get agent.state Counter"
	//   "host-events 10"
	private static readonly Regex ParseRegex = new(
		@"^(?<cmd>[\w.-]+)(?:\s+(?<target>\S+))?(?<tail>.*)$",
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

		var cmd = match.Groups["cmd"].Value;
		var firstDot = cmd.IndexOf('.');
		var ns = firstDot > 0 ? cmd[..firstDot] : "";
		var verb = firstDot > 0 && firstDot + 1 < cmd.Length ? cmd[(firstDot + 1)..] : cmd;
		var target = match.Groups["target"].Success ? match.Groups["target"].Value : null;
		var tail = match.Groups["tail"].Success ? match.Groups["tail"].Value.Trim() : "";

		// If the first token after command starts with '-', treat it as part of tail
		// so PowerShell-style named parameters are parsed correctly.
		if (!string.IsNullOrWhiteSpace(target) && target.StartsWith("-", StringComparison.Ordinal))
		{
			tail = string.IsNullOrWhiteSpace(tail)
				? target
				: $"{target} {tail}";
			target = null;
		}

		var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		// Parse key=value pairs
		foreach (Match m in ArgsRegex.Matches(tail))
		{
			args[m.Groups[1].Value] = m.Groups[2].Value;
		}

		// Parse PowerShell-style args: -name value, -name:value, -switch
		var parts = string.IsNullOrWhiteSpace(tail)
			? Array.Empty<string>()
			: tail.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		for (var i = 0; i < parts.Length; i++)
		{
			var part = parts[i];
			if (!part.StartsWith("-", StringComparison.Ordinal) || part.StartsWith("--", StringComparison.Ordinal))
				continue;

			var token = part.TrimStart('-');
			if (string.IsNullOrWhiteSpace(token))
				continue;

			var colonIndex = token.IndexOf(':');
			if (colonIndex > 0)
			{
				var key = token[..colonIndex];
				var value = token[(colonIndex + 1)..];
				args[key] = value;
				continue;
			}

			if (i + 1 < parts.Length)
			{
				var next = parts[i + 1];
				if (!next.StartsWith("-", StringComparison.Ordinal))
				{
					args[token] = next;
					i++;
					continue;
				}
			}

			// Switch-style parameter defaults to true.
			args[token] = "true";
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
			parts = tail.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			var posIndex = target == null ? 0 : 1;
			for (var i = 0; i < parts.Length; i++)
			{
				var part = parts[i];
				if (part.Contains('=') || part.StartsWith("--"))
					continue;

				if (part.StartsWith("-", StringComparison.Ordinal) && !part.StartsWith("--", StringComparison.Ordinal))
				{
					var token = part.TrimStart('-');
					if (token.Contains(':'))
						continue;

					if (i + 1 < parts.Length && !parts[i + 1].StartsWith("-", StringComparison.Ordinal))
						i++;
					continue;
				}

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
		var consumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (def.Params.Count > 0)
		{
			args = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
			foreach (var (name, param) in def.Params)
			{
				string? value = null;
				if (param.Position >= 0 && param.Position == 0 && parsed.Target != null)
				{
					value = parsed.Target;
				}
				else if (parsed.Args.TryGetValue(name, out var v))
				{
					value = v;
					consumed.Add(name);
				}
				else if (parsed.Args.TryGetValue($"_p{param.Position}", out var pv))
				{
					value = pv;
					consumed.Add($"_p{param.Position}");
				}
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
						"double" or "float" => JsonSerializer.SerializeToElement(double.Parse(value, CultureInfo.InvariantCulture)),
						"json" => JsonDocument.Parse(value).RootElement.Clone(),
						_ => JsonSerializer.SerializeToElement(value)
					};
				}
			}
		}
		else
		{
			args = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
		}

		// Preserve extra named arguments for extensibility (for example web.runtime.invoke url=... count=...).
		foreach (var (key, value) in parsed.Args)
		{
			if (consumed.Contains(key) || key.StartsWith("_p", StringComparison.OrdinalIgnoreCase))
				continue;
			if (args.ContainsKey(key))
				continue;
			args[key] = ParseUnknownValue(value);
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

	private static JsonElement ParseUnknownValue(string value)
	{
		if (bool.TryParse(value, out var boolValue))
			return JsonSerializer.SerializeToElement(boolValue);
		if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
			return JsonSerializer.SerializeToElement(intValue);
		if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
			return JsonSerializer.SerializeToElement(doubleValue);

		var trimmed = value.Trim();
		if ((trimmed.StartsWith("{") && trimmed.EndsWith("}")) || (trimmed.StartsWith("[") && trimmed.EndsWith("]")))
		{
			try
			{
				return JsonDocument.Parse(trimmed).RootElement.Clone();
			}
			catch (JsonException)
			{
				// Keep raw string when payload is not valid JSON.
			}
		}

		return JsonSerializer.SerializeToElement(value);
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
	public static CommandConfig LoadV2(string configPath, bool isBaseConfig)
	{
		if (!File.Exists(configPath))
		{
			if (isBaseConfig)
				throw new FileNotFoundException($"Unified command config not found: {configPath}");

			return new CommandConfig();
		}

		var json = File.ReadAllText(configPath);
		var config = JsonSerializer.Deserialize<CommandConfig>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
			?? new CommandConfig();

		if (config.ExtensionData != null && config.ExtensionData.ContainsKey("commands"))
		{
			throw new InvalidOperationException(
				$"Legacy CLI command schema detected in '{configPath}'. The runtime CLI now uses v2 schema only (baseCommands/compositeCommands).");
		}

		if (config.Version != 2 && config.Version != 0)
		{
			throw new InvalidOperationException(
				$"Unsupported CLI config version '{config.Version}' in '{configPath}'. Expected version 2.");
		}

		return config;
	}

	public static CommandConfig MergeWithUserOverrides(CommandConfig baseConfig, string? userConfigPath)
	{
		if (string.IsNullOrWhiteSpace(userConfigPath))
			return baseConfig;

		if (!File.Exists(userConfigPath))
			return baseConfig;

		var userConfig = LoadV2(userConfigPath, isBaseConfig: false);

		var mergedPipes = new Dictionary<string, string>(baseConfig.Pipes, StringComparer.OrdinalIgnoreCase);
		foreach (var (key, value) in userConfig.Pipes)
			mergedPipes[key] = value;

		var mergedMemory = new Dictionary<string, string>(baseConfig.Memory, StringComparer.OrdinalIgnoreCase);
		foreach (var (key, value) in userConfig.Memory)
			mergedMemory[key] = value;

		var mergedBaseCommands = MergeCommandsByName(baseConfig.BaseCommands, userConfig.BaseCommands);
		var mergedCompositeCommands = MergeCompositeCommandsByName(baseConfig.CompositeCommands, userConfig.CompositeCommands);

		return new CommandConfig
		{
			Version = 2,
			Meta = IsEmptyMeta(userConfig.Meta) ? baseConfig.Meta : userConfig.Meta,
			Pipes = mergedPipes,
			Memory = mergedMemory,
			BaseCommands = mergedBaseCommands,
			CompositeCommands = mergedCompositeCommands
		};
	}

	private static List<BaseCommandDef> MergeCommandsByName(
		IReadOnlyCollection<BaseCommandDef> source,
		IReadOnlyCollection<BaseCommandDef> overrides)
	{
		var merged = source.ToDictionary(cmd => cmd.Name, cmd => cmd, StringComparer.OrdinalIgnoreCase);
		foreach (var item in overrides)
			merged[item.Name] = item;

		return merged.Values.OrderBy(cmd => cmd.Name, StringComparer.OrdinalIgnoreCase).ToList();
	}

	private static List<CompositeCommandDef> MergeCompositeCommandsByName(
		IReadOnlyCollection<CompositeCommandDef> source,
		IReadOnlyCollection<CompositeCommandDef> overrides)
	{
		var merged = source.ToDictionary(cmd => cmd.Name, cmd => cmd, StringComparer.OrdinalIgnoreCase);
		foreach (var item in overrides)
			merged[item.Name] = item;

		return merged.Values.OrderBy(cmd => cmd.Name, StringComparer.OrdinalIgnoreCase).ToList();
	}

	private static bool IsEmptyMeta(CommandMeta meta)
	{
		return string.IsNullOrWhiteSpace(meta.Name)
			&& string.IsNullOrWhiteSpace(meta.Description);
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
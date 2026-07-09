using System.Text.Json;
using Iwesun.Runtime.Diagnostics;

namespace Iwesun.Runtime.Cli;

/// <summary>
/// Executes composite commands by running their steps sequentially or in parallel.
/// </summary>
public static class CompositeCommandExecutor
{
	public static async Task<string> ExecuteAsync(
		CompositeCommandDef composite,
		ParsedCommand parsed,
		Dictionary<string, BaseCommandDef> baseCommands,
		Func<RuntimeDiagnosticFrameCommand, Task<string>> sendAsync,
		Dictionary<string, string> memory)
	{
		var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		// Seed variables from parsed input
		variables["target"] = parsed.Target ?? "";
		foreach (var (key, value) in parsed.Args)
			variables[key] = value;
		foreach (var flag in parsed.Flags)
			variables[flag] = "true";

		// Seed from memory
		foreach (var (key, value) in memory)
			variables[key] = value;

		var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		var results = new List<string>();

		foreach (var step in composite.Steps)
		{
			// Apply variable substitution
			var resolvedCommand = ResolveVariables(step.Command, variables);

			// Check condition
			if (!string.IsNullOrEmpty(step.Condition) && !EvaluateCondition(step.Condition, variables, outputs))
				continue;

			// Delay if specified
			if (step.Delay > 0)
				await Task.Delay(step.Delay);

			// Parse and execute
			var stepParsed = CommandParser.Parse(resolvedCommand);
			var cmd = CommandParser.Resolve(stepParsed, baseCommands);

			try
			{
				var result = await sendAsync(cmd);
				results.Add(result);

				// Capture output
				if (!string.IsNullOrEmpty(step.Capture))
				{
					outputs[step.Capture] = result;
					variables[step.Capture] = result;
				}
			}
			catch when (step.SkipOnError)
			{
				results.Add($"{{\"error\":\"Step skipped: {step.Command}\"}}");
			}
		}

		return JsonSerializer.Serialize(new
		{
			composite = composite.Name,
			steps = composite.Steps.Count,
			results
		}, new JsonSerializerOptions(JsonSerializerDefaults.Web));
	}

	private static string ResolveVariables(string command, Dictionary<string, string> variables)
	{
		foreach (var (key, value) in variables)
		{
			command = command.Replace($"${key}", value);
		}
		return command;
	}

	private static bool EvaluateCondition(string condition, Dictionary<string, string> variables, Dictionary<string, string> outputs)
	{
		// Simple condition: $variable exists and is truthy
		var trimmed = condition.TrimStart('$');
		if (variables.TryGetValue(trimmed, out var value))
			return !string.IsNullOrEmpty(value) && value != "false" && value != "0";

		if (outputs.TryGetValue(trimmed, out var output))
			return !string.IsNullOrEmpty(output) && output != "false" && output != "0";

		return false;
	}
}
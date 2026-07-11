using Iwesun.Runtime.Diagnostics;

namespace Iwesun.Runtime.Cli;

/// <summary>
/// Compiles a CLI composite into one authoritative runtime batch frame.
/// The runtime owns ordering, deadlines, conditions, and error policy.
/// </summary>
public static class CompositeCommandExecutor
{
	public static async Task<string> ExecuteAsync(
		CompositeCommandDef composite,
		ParsedCommand parsed,
		Dictionary<string, BaseCommandDef> baseCommands,
		Func<RuntimeDiagnosticFrame, Task<string>> sendAsync,
		Dictionary<string, string> memory)
	{
		if (!composite.Mode.Equals("sequential", StringComparison.OrdinalIgnoreCase))
			throw new NotSupportedException($"Composite mode '{composite.Mode}' is not supported by the sequential runtime batch contract.");

		var variables = new Dictionary<string, string>(memory, StringComparer.OrdinalIgnoreCase)
		{
			["target"] = parsed.Target ?? ""
		};
		foreach (var (key, value) in parsed.Args)
			variables[key] = value;
		foreach (var flag in parsed.Flags)
			variables[flag] = "true";

		var steps = new List<RuntimeDiagnosticBatchStep>(composite.Steps.Count);
		for (var index = 0; index < composite.Steps.Count; index++)
		{
			var source = composite.Steps[index];
			if (!string.IsNullOrWhiteSpace(source.Condition) && !EvaluateInputCondition(source.Condition, variables))
				continue;

			RuntimeDiagnosticFrameCommand command;
			if (!string.IsNullOrWhiteSpace(source.Command))
			{
				var parsedStep = CommandParser.Parse(ResolveVariables(source.Command, variables));
				command = CommandParser.Resolve(parsedStep, baseCommands);
			}
			else
			{
				if (string.IsNullOrWhiteSpace(source.Action))
					throw new InvalidOperationException($"Composite '{composite.Name}' step {index + 1} requires command or action.");
				command = new RuntimeDiagnosticFrameCommand
				{
					Target = source.Target ?? "",
					Action = source.Action,
					Member = source.Member,
					Args = ResolveStructuredArgs(source.Args, variables)
				};
			}
			steps.Add(new RuntimeDiagnosticBatchStep
			{
				Id = $"step-{index + 1:D3}",
				Command = command,
				Bindings = source.Bindings,
				When = source.When,
				ContinueOnError = source.SkipOnError,
				DelayMs = Math.Max(0, source.Delay)
			});
		}

		if (steps.Count == 0)
			throw new InvalidOperationException($"Composite command '{composite.Name}' produced no executable steps.");

		var frame = new RuntimeDiagnosticFrame
		{
			Header = new RuntimeDiagnosticFrameHeader
			{
				Schema = RuntimeDiagnosticProtocol.V3Schema,
				FrameType = "request",
				Category = "batch",
				Operation = "execute",
				RequestId = Guid.NewGuid().ToString("N"),
				Timestamp = DateTimeOffset.UtcNow,
				Source = "cli",
				Destination = "runtime"
			},
			Batch = new RuntimeDiagnosticBatchRequest
			{
				Options = new RuntimeDiagnosticBatchOptions
				{
					StopOnError = true,
					DeadlineMs = Math.Clamp(30_000 + steps.Sum(x => x.DelayMs), 100, 60_000)
				},
				Steps = steps
			}
		};

		return await sendAsync(frame);
	}

	private static Dictionary<string, System.Text.Json.JsonElement>? ResolveStructuredArgs(
		IReadOnlyDictionary<string, System.Text.Json.JsonElement>? args,
		IReadOnlyDictionary<string, string> variables)
	{
		if (args == null)
			return null;
		var resolved = new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.OrdinalIgnoreCase);
		foreach (var (key, value) in args)
		{
			if (value.ValueKind == System.Text.Json.JsonValueKind.String
				&& value.GetString() is { } text
				&& text.StartsWith('$')
				&& variables.TryGetValue(text[1..], out var variable))
			{
				resolved[key] = bool.TryParse(variable, out var boolean)
					? System.Text.Json.JsonSerializer.SerializeToElement(boolean)
					: long.TryParse(variable, out var integer)
						? System.Text.Json.JsonSerializer.SerializeToElement(integer)
						: double.TryParse(variable, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number)
							? System.Text.Json.JsonSerializer.SerializeToElement(number)
							: System.Text.Json.JsonSerializer.SerializeToElement(variable);
			}
			else
			{
				resolved[key] = value.Clone();
			}
		}
		return resolved;
	}

	private static string ResolveVariables(string command, IReadOnlyDictionary<string, string> variables)
	{
		foreach (var (key, value) in variables.OrderByDescending(x => x.Key.Length))
			command = command.Replace($"${key}", value, StringComparison.OrdinalIgnoreCase);
		return command;
	}

	private static bool EvaluateInputCondition(string condition, IReadOnlyDictionary<string, string> variables)
	{
		var key = condition.Trim().TrimStart('$');
		return variables.TryGetValue(key, out var value)
			&& !string.IsNullOrWhiteSpace(value)
			&& !value.Equals("false", StringComparison.OrdinalIgnoreCase)
			&& value != "0";
	}
}

using System.Text.Json;
using Iwesun.Runtime.Diagnostics;

namespace Iwesun.Runtime.Cli;

/// <summary>
/// Semantic command is the boundary contract between CLI grammar and pipe JSON protocol.
/// CLI parsing should only produce this model; pipe payload is built afterward.
/// </summary>
public sealed class CliSemanticCommand
{
	public string Category { get; init; } = "instruction";
	public string Operation { get; init; } = "invoke";
	public string Domain { get; init; } = "runtime";
	public string TargetId { get; init; } = "";
	public string Action { get; init; } = "";
	public string? Member { get; init; }
	public Dictionary<string, JsonElement>? Args { get; init; }

	public static CliSemanticCommand From(ParsedCommand? parsed, BaseCommandDef? commandDef, RuntimeDiagnosticFrameCommand command)
	{
		var action = string.IsNullOrWhiteSpace(command.Action) ? "invoke" : command.Action;
		var operation = string.IsNullOrWhiteSpace(commandDef?.Operation)
			? action
			: commandDef.Operation!;
		var category = string.IsNullOrWhiteSpace(commandDef?.Category)
			? InferCategory(action)
			: commandDef.Category;

		return new CliSemanticCommand
		{
			Category = category,
			Operation = operation,
			Domain = ResolveDomain(parsed, command.Target),
			TargetId = command.Target,
			Action = action,
			Member = command.Member,
			Args = command.Args == null
				? null
				: new Dictionary<string, JsonElement>(command.Args, StringComparer.OrdinalIgnoreCase)
		};
	}

	private static string ResolveDomain(ParsedCommand? parsed, string targetId)
	{
		if (parsed != null && !string.IsNullOrWhiteSpace(parsed.Namespace))
			return parsed.Namespace;
		if (targetId.StartsWith("diagnostics.switchboard", StringComparison.OrdinalIgnoreCase))
			return "switchboard";
		if (targetId.StartsWith("diagnostics.breakpoints", StringComparison.OrdinalIgnoreCase))
			return "breakpoints";
		if (targetId.StartsWith("diagnostics.hooks", StringComparison.OrdinalIgnoreCase))
			return "hooks";
		if (targetId.StartsWith("diagnostics.registry", StringComparison.OrdinalIgnoreCase))
			return "registry";
		if (targetId.StartsWith("diagnostics.monitor", StringComparison.OrdinalIgnoreCase))
			return "host";
		return "runtime";
	}

	private static string InferCategory(string action)
	{
		if (action.Contains("events", StringComparison.OrdinalIgnoreCase))
			return "stream";
		if (action.Contains("snapshot", StringComparison.OrdinalIgnoreCase)
			|| action.Contains("list", StringComparison.OrdinalIgnoreCase)
			|| action.Contains("query", StringComparison.OrdinalIgnoreCase)
			|| action.Contains("get", StringComparison.OrdinalIgnoreCase)
			|| action.Contains("navigate", StringComparison.OrdinalIgnoreCase))
			return "query";
		if (action.Contains("shutdown", StringComparison.OrdinalIgnoreCase)
			|| action.Contains("reload", StringComparison.OrdinalIgnoreCase)
			|| action.Contains("save", StringComparison.OrdinalIgnoreCase)
			|| action.Contains("rescan", StringComparison.OrdinalIgnoreCase))
			return "system";
		return "instruction";
	}

}

/// <summary>
/// Pipe envelope factory. JSON protocol is the primary runtime contract.
/// </summary>
public static class RuntimeDiagnosticsFrameFactory
{
	public static RuntimeDiagnosticFrame Wrap(CliSemanticCommand command)
	{
		return new RuntimeDiagnosticFrame
		{
			Header = new RuntimeDiagnosticFrameHeader
			{
				Schema = RuntimeDiagnosticProtocol.V2Schema,
				FrameType = "request",
				Category = command.Category,
				Operation = command.Operation,
				RequestId = Guid.NewGuid().ToString("N"),
				Timestamp = DateTimeOffset.UtcNow,
				Source = "cli",
				Destination = "runtime"
			},
			Command = new RuntimeDiagnosticFrameCommand
			{
				Domain = command.Domain,
				Target = command.TargetId,
				Member = command.Member,
				Action = command.Action,
				Args = command.Args
			}
		};
	}
}

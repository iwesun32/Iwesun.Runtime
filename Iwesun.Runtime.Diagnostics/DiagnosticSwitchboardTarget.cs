using System.Text.Json;

namespace Iwesun.Runtime.Diagnostics;

public sealed class DiagnosticSwitchboardTarget : RuntimeDiagnosticTargetBase
{
	public DiagnosticSwitchboardTarget() : base("diagnostics.switchboard")
	{
	}

	public override object Snapshot() => DiagnosticSwitchboard.Snapshot();

	public override Task<RuntimeDiagnosticActionResult> ExecuteAsync(RuntimeDiagnosticAction command, CancellationToken ct)
	{
		switch (command.Action.ToLowerInvariant())
		{
			case "snapshot":
				return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, Snapshot()));
			case "reload":
			case "reloadconfig":
				DiagnosticSwitchboard.ReloadConfig();
				return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, Snapshot()));
			case "save":
			case "saveconfig":
				DiagnosticSwitchboard.SaveConfig();
				return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, Snapshot()));
			case "enable":
				Apply(command, true, ReadBool(command, "persist") ?? false);
				return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, Snapshot()));
			case "disable":
				Apply(command, false, ReadBool(command, "persist") ?? false);
				return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, Snapshot()));
			case "set":
				var enabled = ReadBool(command, "enabled") ?? false;
				Apply(command, enabled, ReadBool(command, "persist") ?? false);
				return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, Snapshot()));
			case "emittest":
				var section = ReadString(command, "section") ?? "console";
				var kind = ReadString(command, "kind") ?? "diagnostic.selftest";
				var message = ReadString(command, "message") ?? "diagnostic self-test output";
				DiagnosticSwitchboard.ReportTrace(section, kind, message, new { section, kind });
				return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, Snapshot()));
			case "setinput":
				DiagnosticSwitchboard.SetInput(ReadBool(command, "enabled") ?? false, ReadBool(command, "persist") ?? false);
				return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, Snapshot()));
			case "setpipeoutput":
				DiagnosticSwitchboard.SetPipeOutput(ReadBool(command, "enabled") ?? false, ReadBool(command, "persist") ?? false);
				return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, Snapshot()));
			case "setfileoutput":
				DiagnosticSwitchboard.SetFileOutput(ReadBool(command, "enabled") ?? false, ReadBool(command, "persist") ?? false);
				return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, Snapshot()));
			case "setfilepath":
				DiagnosticSwitchboard.SetFilePath(ReadString(command, "path"), ReadBool(command, "persist") ?? false);
				return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, Snapshot()));
			case "setfifodepth":
				DiagnosticSwitchboard.SetFifoDepth(ReadInt(command, "depth") ?? 1024, ReadBool(command, "persist") ?? false);
				return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, Snapshot()));
			case "querypoints":
			case "points":
				return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, DiagnosticSwitchboard.QueryOutputPoints(
					ReadString(command, "id"),
					ReadString(command, "section"),
					ReadString(command, "category"),
					ReadString(command, "source") ?? ReadString(command, "sourceLocation"),
					ReadString(command, "kind") ?? ReadString(command, "eventKind"),
					ReadString(command, "text"))));
			case "setruntimediagnosticspipename":
			case "setdiagnosticspipename":
				DiagnosticSwitchboard.SetRuntimeDiagnosticsPipeName(
					ReadString(command, "name") ?? ReadString(command, "pipeName"),
					ReadBool(command, "persist") ?? false);
				return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, Snapshot()));
			case "setoutputpoint":
			case "setpoint":
				DiagnosticSwitchboard.SetOutputPoint(
					ReadString(command, "id") ?? ReadString(command, "outputPointId") ?? "",
					ReadBool(command, "enabled") ?? false,
					ReadBool(command, "persist") ?? false);
				return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, Snapshot()));
			case "enableoutputpoint":
			case "enablepoint":
				DiagnosticSwitchboard.SetOutputPoint(
					ReadString(command, "id") ?? ReadString(command, "outputPointId") ?? "",
					true,
					ReadBool(command, "persist") ?? false);
				return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, Snapshot()));
			case "disableoutputpoint":
			case "disablepoint":
				DiagnosticSwitchboard.SetOutputPoint(
					ReadString(command, "id") ?? ReadString(command, "outputPointId") ?? "",
					false,
					ReadBool(command, "persist") ?? false);
				return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, Snapshot()));
			case "shutdown":
				var graceful = ReadBool(command, "graceful") ?? true;
				var exitCode = ReadInt(command, "exitCode") ?? 0;
				ShutdownRequested?.Invoke(graceful, exitCode);
				return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, new { graceful, exitCode, message = "Shutdown requested." }));
			default:
				return Task.FromResult(RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, $"Unsupported action: {command.Action}"));
		}
	}

	/// <summary>
	/// Raised when a shutdown command is received from CLI.
	/// The host should subscribe to perform graceful or forced termination.
	/// </summary>
	public static event Action<bool, int>? ShutdownRequested;

	private static void Apply(RuntimeDiagnosticAction command, bool enabled, bool persist)
	{
		var section = ReadString(command, "section");
		if (string.IsNullOrWhiteSpace(section))
			DiagnosticSwitchboard.SetGlobal(enabled, persist);
		else
			DiagnosticSwitchboard.SetSection(section, enabled, persist);
	}

	private static string? ReadString(RuntimeDiagnosticAction command, string key)
	{
		if (command.Args != null
			&& command.Args.TryGetValue(key, out var value)
			&& value.ValueKind == JsonValueKind.String)
			return value.GetString();
		return null;
	}

	private static bool? ReadBool(RuntimeDiagnosticAction command, string key)
	{
		if (command.Args != null
			&& command.Args.TryGetValue(key, out var value)
			&& (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False))
			return value.GetBoolean();
		return null;
	}

	private static int? ReadInt(RuntimeDiagnosticAction command, string key)
	{
		if (command.Args != null
			&& command.Args.TryGetValue(key, out var value)
			&& value.ValueKind == JsonValueKind.Number
			&& value.TryGetInt32(out var number))
			return number;
		return null;
	}
}

using System.Text.Json;
using Microsoft.Extensions.Hosting;

namespace Iwesun.Runtime.Diagnostics;

public sealed class DiagnosticSwitchboardTarget : RuntimeDiagnosticTargetBase
{
	private readonly IHostApplicationLifetime? _applicationLifetime;
	private readonly RuntimeShutdownCoordinator _shutdownCoordinator;

	public DiagnosticSwitchboardTarget(
		RuntimeShutdownCoordinator shutdownCoordinator,
		IHostApplicationLifetime? applicationLifetime = null) : base("diagnostics.switchboard")
	{
		_shutdownCoordinator = shutdownCoordinator ?? throw new ArgumentNullException(nameof(shutdownCoordinator));
		_applicationLifetime = applicationLifetime;
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
				ApplyWithLegacyBooleanSectionCompatibility(command, true, ReadBool(command, "persist") ?? false);
				return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, Snapshot()));
			case "disable":
				ApplyWithLegacyBooleanSectionCompatibility(command, false, ReadBool(command, "persist") ?? false);
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
				return Task.FromResult(RuntimeDiagnosticActionResult.Fail(
					TargetId,
					command.Action,
					"Runtime file path is startup-static and cannot be changed at runtime. Use startup args/JSON/source defaults."));
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
				var timeoutMs = Math.Max(100, ReadInt(command, "countdownMs") ?? 5000);
				var shutdownTask = _shutdownCoordinator.ShutdownAsync(TimeSpan.FromMilliseconds(timeoutMs), ReadString(command, "payload"), CancellationToken.None);
				_ = CompleteHostShutdownAsync(shutdownTask);
				return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, new
				{
					graceful,
					timeoutMs,
					message = "Coordinated shutdown requested."
				}));
			default:
				return Task.FromResult(RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, $"Unsupported action: {command.Action}"));
		}
	}

	private async Task CompleteHostShutdownAsync(Task<RuntimeShutdownResult> shutdownTask)
	{
		var result = await shutdownTask.ConfigureAwait(false);
		Environment.ExitCode = result.ExitCode;
		_applicationLifetime?.StopApplication();
	}

	private static void Apply(RuntimeDiagnosticAction command, bool enabled, bool persist)
	{
		var section = ReadString(command, "section");
		if (string.IsNullOrWhiteSpace(section))
			DiagnosticSwitchboard.SetGlobal(enabled, persist);
		else
			DiagnosticSwitchboard.SetSection(section, enabled, persist);
	}

	private static void ApplyWithLegacyBooleanSectionCompatibility(RuntimeDiagnosticAction command, bool enabled, bool persist)
	{
		// Compatibility path: if caller sends "section=true/false" by mistake,
		// interpret it as a global switch command instead of creating a literal section named "true" or "false".
		var section = ReadString(command, "section");
		if (!string.IsNullOrWhiteSpace(section) && bool.TryParse(section, out var explicitGlobalEnabled))
		{
			DiagnosticSwitchboard.SetGlobal(explicitGlobalEnabled, persist);
			return;
		}

		Apply(command, enabled, persist);
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

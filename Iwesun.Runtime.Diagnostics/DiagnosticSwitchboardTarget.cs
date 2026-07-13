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
				return Task.FromResult(RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, "Runtime configuration files are not supported."));
			case "save":
			case "saveconfig":
				return Task.FromResult(RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, "Runtime configuration files are not supported."));
			case "enable":
				var enableSection = ReadString(command, "section");
				var previousEnable = string.IsNullOrWhiteSpace(enableSection)
					? DiagnosticSwitchboard.GlobalEnabled
					: DiagnosticSwitchboard.GetSectionEnabled(enableSection);
				ApplyWithLegacyBooleanSectionCompatibility(command, true, ReadBool(command, "persist") ?? false);
				var effectiveEnable = string.IsNullOrWhiteSpace(enableSection)
					? DiagnosticSwitchboard.GlobalEnabled
					: DiagnosticSwitchboard.GlobalEnabled && DiagnosticSwitchboard.GetSectionEnabled(enableSection);
				return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, Mutation(command, enableSection ?? "global", previousEnable, true, effectiveEnable)));
			case "disable":
				var disableSection = ReadString(command, "section");
				var previousDisable = string.IsNullOrWhiteSpace(disableSection)
					? DiagnosticSwitchboard.GlobalEnabled
					: DiagnosticSwitchboard.GetSectionEnabled(disableSection);
				ApplyWithLegacyBooleanSectionCompatibility(command, false, ReadBool(command, "persist") ?? false);
				var effectiveDisable = string.IsNullOrWhiteSpace(disableSection)
					? DiagnosticSwitchboard.GlobalEnabled
					: DiagnosticSwitchboard.GlobalEnabled && DiagnosticSwitchboard.GetSectionEnabled(disableSection);
				return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, Mutation(command, disableSection ?? "global", previousDisable, false, effectiveDisable)));
			case "set":
				var enabled = ReadBool(command, "enabled") ?? false;
				var setSection = ReadString(command, "section");
				var previousSet = string.IsNullOrWhiteSpace(setSection)
					? DiagnosticSwitchboard.GlobalEnabled
					: DiagnosticSwitchboard.GetSectionEnabled(setSection);
				Apply(command, enabled, ReadBool(command, "persist") ?? false);
				var effectiveSet = string.IsNullOrWhiteSpace(setSection)
					? DiagnosticSwitchboard.GlobalEnabled
					: DiagnosticSwitchboard.GlobalEnabled && DiagnosticSwitchboard.GetSectionEnabled(setSection);
				return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, Mutation(command, setSection ?? "global", previousSet, enabled, effectiveSet)));
			case "emittest":
				var section = ReadString(command, "section") ?? "console";
				var kind = ReadString(command, "kind") ?? "diagnostic.selftest";
				var message = ReadString(command, "message") ?? "diagnostic self-test output";
				DiagnosticSwitchboard.ReportTrace(section, kind, message, new { section, kind });
				return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, Snapshot()));
			case "setinput":
				var previousInput = DiagnosticSwitchboard.InputEnabled;
				DiagnosticSwitchboard.SetInput(ReadBool(command, "enabled") ?? false, ReadBool(command, "persist") ?? false);
				return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, Mutation(command, "input", previousInput, ReadBool(command, "enabled") ?? false, DiagnosticSwitchboard.InputEnabled)));
			case "setpipeoutput":
				var previousPipe = DiagnosticSwitchboard.PipeOutputEnabled;
				DiagnosticSwitchboard.SetPipeOutput(ReadBool(command, "enabled") ?? false, ReadBool(command, "persist") ?? false);
				return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, Mutation(command, "pipeOutput", previousPipe, ReadBool(command, "enabled") ?? false, DiagnosticSwitchboard.PipeOutputEnabled)));
			case "setfileoutput":
				var previousFile = DiagnosticSwitchboard.FileOutputEnabled;
				DiagnosticSwitchboard.SetFileOutput(ReadBool(command, "enabled") ?? false, ReadBool(command, "persist") ?? false);
				return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, Mutation(command, "fileOutput", previousFile, ReadBool(command, "enabled") ?? false, DiagnosticSwitchboard.FileOutputEnabled)));
			case "setfilepath":
				return Task.FromResult(RuntimeDiagnosticActionResult.Fail(
					TargetId,
					command.Action,
					"Runtime file path is startup-static and cannot be changed at runtime. Use startup args/JSON/source defaults."));
			case "setfifodepth":
				var previousDepth = DiagnosticSwitchboard.FifoDepth;
				DiagnosticSwitchboard.SetFifoDepth(ReadInt(command, "depth") ?? 1024, ReadBool(command, "persist") ?? false);
				return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, Mutation(command, "fifoDepth", previousDepth, ReadInt(command, "depth") ?? 1024, DiagnosticSwitchboard.FifoDepth)));
			case "querypoints":
			case "points":
				var points = DiagnosticSwitchboard.QueryOutputPoints(
					ReadString(command, "id"),
					ReadString(command, "section"),
					ReadString(command, "category"),
					ReadString(command, "source") ?? ReadString(command, "sourceLocation"),
					ReadString(command, "kind") ?? ReadString(command, "eventKind"),
					ReadString(command, "text"));
				return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action,
					RuntimePagedResult<DiagnosticOutputPointConfig>.Create(points, ReadInt(command, "offset") ?? 0, ReadInt(command, "limit") ?? 100)));
			case "setruntimediagnosticspipename":
			case "setdiagnosticspipename":
				DiagnosticSwitchboard.SetRuntimeDiagnosticsPipeName(
					ReadString(command, "name") ?? ReadString(command, "pipeName"),
					ReadBool(command, "persist") ?? false);
				return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, Snapshot()));
			case "setoutputpoint":
			case "setpoint":
				var setPointId = ReadString(command, "id") ?? ReadString(command, "outputPointId") ?? "";
				var previousPoint = PointEnabled(setPointId);
				if (!DiagnosticSwitchboard.SetOutputPoint(
					setPointId,
					ReadBool(command, "enabled") ?? false,
					ReadBool(command, "persist") ?? false))
					return Task.FromResult(RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, "OUTPUT_POINT_NOT_FOUND"));
				return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, Mutation(command, setPointId, previousPoint, ReadBool(command, "enabled") ?? false, PointEnabled(setPointId))));
			case "enableoutputpoint":
			case "enablepoint":
				var enablePointId = ReadString(command, "id") ?? ReadString(command, "outputPointId") ?? "";
				var previousEnablePoint = PointEnabled(enablePointId);
				if (!DiagnosticSwitchboard.SetOutputPoint(
					enablePointId,
					true,
					ReadBool(command, "persist") ?? false))
					return Task.FromResult(RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, "OUTPUT_POINT_NOT_FOUND"));
				return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, Mutation(command, enablePointId, previousEnablePoint, true, PointEnabled(enablePointId))));
			case "disableoutputpoint":
			case "disablepoint":
				var disablePointId = ReadString(command, "id") ?? ReadString(command, "outputPointId") ?? "";
				var previousDisablePoint = PointEnabled(disablePointId);
				if (!DiagnosticSwitchboard.SetOutputPoint(
					disablePointId,
					false,
					ReadBool(command, "persist") ?? false))
					return Task.FromResult(RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, "OUTPUT_POINT_NOT_FOUND"));
				return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, Mutation(command, disablePointId, previousDisablePoint, false, PointEnabled(disablePointId))));
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

	private RuntimeMutationResult Mutation(RuntimeDiagnosticAction command, string subject, object? previous, object? requested, object? effective) =>
		new(TargetId, command.Action, subject, previous, requested, effective, !Equals(previous, effective), DiagnosticSwitchboard.SnapshotVersion, DateTimeOffset.UtcNow);

	private static bool PointEnabled(string id) =>
		DiagnosticSwitchboard.QueryOutputPoints(id, null, null, null, null, null).FirstOrDefault()?.Enabled ?? false;

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

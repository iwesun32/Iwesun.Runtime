using System.Text.Json;
using Iwesun.Runtime.Diagnostics;

namespace Iwesun.Runtime.WebView2;

/// <summary>Runtime diagnostics target for compiled C# WebRuntime program supervision.</summary>
public sealed class WebRuntimeProgramCommandTarget(WebRuntimeProgramRegistry registry, WebRuntimeProgramHost host)
	: RuntimeDiagnosticTargetBase("webruntime.programs")
{
	public override object Snapshot() => registry.Snapshot();

	public override async Task<RuntimeDiagnosticActionResult> ExecuteAsync(RuntimeDiagnosticAction command, CancellationToken ct)
	{
		switch (Normalize(command.Action))
		{
			case "snapshot":
			case "list":
			case "status":
			case "capabilities":
				return RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, registry.Snapshot());
			case "cancel":
			{
				var executionId = ReadString(command, "executionId");
				if (string.IsNullOrWhiteSpace(executionId))
					return RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, "executionId is required.");
				var canceled = registry.Cancel(executionId, ReadString(command, "reason") ?? "runtime-command");
				return RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, new { executionId, canceled });
			}
			case "initialize":
				await host.ActivateAsync(ct).ConfigureAwait(false);
				return RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, registry.Snapshot());
			case "stop":
				var stopResult = await host.StopAsync(ct).ConfigureAwait(false);
				return RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, new { stopResult, registry = registry.Snapshot() });
			case "execute":
			{
				var programId = ReadString(command, "programId");
				var programAction = ReadString(command, "programAction") ?? ReadString(command, "command");
				if (string.IsNullOrWhiteSpace(programId) || string.IsNullOrWhiteSpace(programAction))
					return RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, "programId and programAction are required.");
				var timeoutMs = Math.Clamp(ReadInt(command, "timeoutMs") ?? 60_000, 1, 600_000);
				var request = new WebRuntimeControlRequest
				{
					ProgramId = programId,
					BackendId = ReadString(command, "backendId") ?? "",
					Action = programAction,
					Args = command.Args?.Where(item => item.Key is not ("programId" or "programAction" or "command" or "timeoutMs" or "backendId"))
						.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase)
				};
				var handle = registry.StartExecution(programId, request, TimeSpan.FromMilliseconds(timeoutMs), ct);
				await handle.Started.ConfigureAwait(false);
				var result = await handle.Completion.ConfigureAwait(false);
				return result.Success
					? RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, new { handle.ExecutionId, result.Action, result.Value })
					: RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, result.Error ?? "C# WebRuntime program failed.");
			}
			default:
				return RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, $"Unsupported action: {command.Action}");
		}
	}

	private static string Normalize(string value) => value.Trim().Replace(".", "", StringComparison.Ordinal).ToLowerInvariant();

	private static string? ReadString(RuntimeDiagnosticAction command, string name) =>
		command.Args != null && command.Args.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;

	private static int? ReadInt(RuntimeDiagnosticAction command, string name) =>
		command.Args != null && command.Args.TryGetValue(name, out var value) && value.TryGetInt32(out var number)
			? number
			: null;
}

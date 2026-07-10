using System.Linq;
using System.Text.Json;

namespace Iwesun.Runtime.Diagnostics;

public sealed class RuntimePipeRegistryTarget : RuntimeDiagnosticTargetBase
{
	public RuntimePipeRegistryTarget() : base("diagnostics.pipes")
	{
	}

	public override object Snapshot() => RuntimePipeRegistry.Snapshot(includeInactive: true);

	public override Task<RuntimeDiagnosticActionResult> ExecuteAsync(RuntimeDiagnosticAction command, CancellationToken ct)
	{
		var action = command.Action.ToLowerInvariant();
		switch (action)
		{
			case "snapshot":
			case "list":
				return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, RuntimePipeRegistry.Snapshot(ReadBool(command, "includeInactive") ?? true)));
			case "acquire":
			case "register":
				var requestedName = ReadString(command, "name") ?? ReadString(command, "requestedName") ?? ReadString(command, "branchId");
				if (string.IsNullOrWhiteSpace(requestedName))
					return Task.FromResult(RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, "name/requestedName/branchId is required."));
				var acquiredPipe = RuntimePipeRegistry.AcquirePipe(
					requestedName,
					ReadString(command, "aggregatePipeName"),
					ReadString(command, "pipePrefix"));
				var acquiredLease = RuntimePipeRegistry.Snapshot(includeInactive: true)
					.FirstOrDefault(x => x.PipeName.Equals(acquiredPipe, StringComparison.OrdinalIgnoreCase));
				return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, new
				{
					pipeName = acquiredPipe,
					lease = acquiredLease
				}));
			case "release":
			case "unregister":
				var releaseToken = ReadString(command, "name") ?? ReadString(command, "branchId") ?? ReadString(command, "id");
				if (string.IsNullOrWhiteSpace(releaseToken))
					return Task.FromResult(RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, "name/branchId/id is required."));
				return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, new
				{
					released = RuntimePipeRegistry.ReleasePipe(releaseToken),
					lease = RuntimePipeRegistry.GetLease(releaseToken)
				}));
			case "resolve":
			case "resolveaggregate":
				var resolveToken = ReadString(command, "name") ?? ReadString(command, "branchId") ?? ReadString(command, "id");
				if (string.IsNullOrWhiteSpace(resolveToken))
					return Task.FromResult(RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, "name/branchId/id is required."));
				return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, new
				{
					aggregatePipeName = RuntimePipeRegistry.ResolveAggregatePipe(resolveToken),
					lease = RuntimePipeRegistry.GetLease(resolveToken)
				}));
			case "announce":
			case "connect":
				var announceName = ReadString(command, "name") ?? ReadString(command, "branchId") ?? ReadString(command, "requestedName");
				var announcePipe = ReadString(command, "pipeName");
				if (string.IsNullOrWhiteSpace(announceName) || string.IsNullOrWhiteSpace(announcePipe))
					return Task.FromResult(RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, "name/requestedName and pipeName are required."));
				var ownerProcessId = ReadInt(command, "ownerProcessId") ?? ReadInt(command, "processId");
				var announced = RuntimePipeRegistry.AnnouncePipe(
					announceName,
					announcePipe,
					ReadString(command, "aggregatePipeName"),
					ownerProcessId);
				return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, announced));
			case "get":
				var getToken = ReadString(command, "name") ?? ReadString(command, "branchId") ?? ReadString(command, "id");
				if (string.IsNullOrWhiteSpace(getToken))
					return Task.FromResult(RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, "name/branchId/id is required."));
				return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, RuntimePipeRegistry.GetLease(getToken)));
			case "purge":
				return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, new
				{
					removed = RuntimePipeRegistry.PurgeInactive(),
					leases = RuntimePipeRegistry.Snapshot(includeInactive: true)
				}));
			default:
				return Task.FromResult(RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, $"Unsupported action: {command.Action}"));
		}
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

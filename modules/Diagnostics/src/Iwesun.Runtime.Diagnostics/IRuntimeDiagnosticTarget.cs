using System.Text.Json;

namespace Iwesun.Runtime.Diagnostics;

public interface IRuntimeDiagnosticTarget
{
	string TargetId { get; }
	object Snapshot();
	Task<RuntimeDiagnosticActionResult> ExecuteAsync(RuntimeDiagnosticAction command, CancellationToken ct);
}

public abstract class RuntimeDiagnosticTargetBase : IRuntimeDiagnosticTarget
{
	protected RuntimeDiagnosticTargetBase(string targetId)
	{
		TargetId = string.IsNullOrWhiteSpace(targetId)
			? throw new ArgumentException("Target id is required.", nameof(targetId))
			: targetId;
	}

	public string TargetId { get; }

	public abstract object Snapshot();

	public virtual Task<RuntimeDiagnosticActionResult> ExecuteAsync(RuntimeDiagnosticAction command, CancellationToken ct)
	{
		if (command.Action.Equals("snapshot", StringComparison.OrdinalIgnoreCase))
			return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, Snapshot()));

		return Task.FromResult(RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, $"Unsupported action: {command.Action}"));
	}

	protected static T? ReadValue<T>(JsonElement? value)
	{
		if (value == null)
			return default;
		return value.Value.Deserialize<T>();
	}
}

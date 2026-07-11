namespace Iwesun.Runtime.Diagnostics;

public static class RuntimeShutdownExitCodes
{
	public const int Success = 0;
	public const int Timeout = 124;
}

public sealed record RuntimeShutdownResult(
	Guid RequestId,
	int ExitCode,
	bool TimedOut,
	DateTimeOffset RequestedAt,
	DateTimeOffset CompletedAt,
	RuntimeShutdownStatus Status);

public sealed class RuntimeShutdownCoordinator
{
	private readonly RuntimeManagedRegistry _registry;
	private readonly object _gate = new();
	private Task<RuntimeShutdownResult>? _activeShutdown;
	private RuntimeShutdownResult? _lastResult;

	public RuntimeShutdownCoordinator(RuntimeManagedRegistry registry)
	{
		_registry = registry ?? throw new ArgumentNullException(nameof(registry));
	}

	public RuntimeShutdownResult? LastResult => Volatile.Read(ref _lastResult);

	public RuntimeShutdownStatus Snapshot() => _registry.EvaluateShutdownStatus();

	public Task<RuntimeShutdownResult> ShutdownAsync(
		TimeSpan timeout,
		string? payload = null,
		CancellationToken cancellationToken = default)
	{
		if (timeout <= TimeSpan.Zero)
		{
			throw new ArgumentOutOfRangeException(nameof(timeout));
		}

		lock (_gate)
		{
			_activeShutdown ??= RunShutdownAsync(timeout, payload, cancellationToken);
			return _activeShutdown;
		}
	}

	private async Task<RuntimeShutdownResult> RunShutdownAsync(
		TimeSpan timeout,
		string? payload,
		CancellationToken cancellationToken)
	{
		var requestId = Guid.NewGuid();
		var requestedAt = DateTimeOffset.UtcNow;
		_registry.RequestShutdown((int)Math.Min(int.MaxValue, timeout.TotalMilliseconds),
			RuntimeManagedPayloadInterpreter.ToJson(new { requestId, payload }));

		while (true)
		{
			var status = _registry.EvaluateShutdownStatus();
			if (status.CanExit)
			{
				var exitCode = status.TimedOut
					? RuntimeShutdownExitCodes.Timeout
					: RuntimeShutdownExitCodes.Success;
				_registry.SetGlobalLifecycleState(status.TimedOut ? "Timeout" : "Completed", status.ExitDeadlineUtc);
				var result = new RuntimeShutdownResult(
					requestId,
					exitCode,
					status.TimedOut,
					requestedAt,
					DateTimeOffset.UtcNow,
					status);
				Volatile.Write(ref _lastResult, result);
				return result;
			}

			foreach (var unit in status.PendingUnits)
			{
				var commandPayload = RuntimeManagedPayloadInterpreter.ToJson(new
				{
					requestId,
					deadlineUtc = status.ExitDeadlineUtc,
					payload
				});
				_registry.EnqueueCommand(unit.UnitId, RuntimeManagedCommandKind.Stop, commandPayload);
				_registry.EnqueueCommand(unit.UnitId, RuntimeManagedCommandKind.Wakeup, commandPayload);
			}

			await _registry.WaitForChangeAsync(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
		}
	}
}

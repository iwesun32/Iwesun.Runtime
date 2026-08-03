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

internal sealed record RuntimeShutdownOperation(
	Guid RequestId,
	TimeSpan Timeout,
	DateTimeOffset RequestedAt,
	DateTimeOffset DeadlineUtc,
	Task<RuntimeShutdownResult> Completion);

public sealed class RuntimeShutdownCoordinator
{
	private readonly RuntimeManagedRegistry _registry;
	private readonly object _gate = new();
	private RuntimeShutdownOperation? _activeShutdown;
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
		var completion = BeginShutdown(timeout, payload).Completion;
		return cancellationToken.CanBeCanceled
			? completion.WaitAsync(cancellationToken)
			: completion;
	}

	internal RuntimeShutdownOperation BeginShutdown(
		TimeSpan timeout,
		string? payload = null)
	{
		if (timeout <= TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(nameof(timeout));

		lock (_gate)
		{
			if (_activeShutdown is not null)
				return _activeShutdown;

			var requestId = Guid.NewGuid();
			var requestedAt = DateTimeOffset.UtcNow;
			var requestedDeadlineUtc = requestedAt.Add(timeout);
			var initialStatus = _registry.RequestShutdown(
				(int)Math.Min(int.MaxValue, timeout.TotalMilliseconds),
				RuntimeManagedPayloadInterpreter.ToJson(new { requestId, payload }),
				requestedDeadlineUtc);
			var deadlineUtc = initialStatus.ExitDeadlineUtc ?? requestedDeadlineUtc;
			var acceptedTimeout = deadlineUtc > requestedAt
				? deadlineUtc - requestedAt
				: TimeSpan.Zero;
			var completion = RunShutdownAsync(
				requestId,
				requestedAt,
				deadlineUtc,
				payload);
			_activeShutdown = new(
				requestId,
				acceptedTimeout,
				requestedAt,
				deadlineUtc,
				completion);
			return _activeShutdown;
		}
	}

	private async Task<RuntimeShutdownResult> RunShutdownAsync(
		Guid requestId,
		DateTimeOffset requestedAt,
		DateTimeOffset deadlineUtc,
		string? payload)
	{
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

			await _registry.WaitForChangeAsync(TimeSpan.FromMilliseconds(100), CancellationToken.None).ConfigureAwait(false);
		}
	}
}

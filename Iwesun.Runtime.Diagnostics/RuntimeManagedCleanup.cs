namespace Iwesun.Runtime.Diagnostics;

public delegate ValueTask RuntimeManagedCleanupHandler(
	object sender,
	RuntimeManagedCleanupEventArgs args,
	CancellationToken cancellationToken);

public sealed class RuntimeManagedCleanupEventArgs : EventArgs
{
	public RuntimeManagedCleanupEventArgs(long commandSequence, string? payload, DateTimeOffset? deadlineUtc)
	{
		CommandSequence = commandSequence;
		Payload = payload;
		DeadlineUtc = deadlineUtc;
	}

	public long CommandSequence { get; }
	public string? Payload { get; }
	public DateTimeOffset? DeadlineUtc { get; }
}

internal sealed class RuntimeManagedCleanup : IDisposable
{
	private readonly object _owner;
	private readonly IRManagedState _state;
	private readonly SemaphoreSlim _exitSignal = new(0, 1);
	private RuntimeManagedCleanupHandler? _handlers;
	private int _started;
	private int _signalled;

	public RuntimeManagedCleanup(object owner, IRManagedState state)
	{
		_owner = owner;
		_state = state;
	}

	public event RuntimeManagedCleanupHandler Requested
	{
		add => _handlers += value;
		remove => _handlers -= value;
	}

	public bool IsCompleted => _state.CurrentState.Key is RuntimeStateKey.StopCompleted;
	public bool IsTimedOut => _state.CurrentState.Key is RuntimeStateKey.StopTimeout;
	public bool HasStarted => Volatile.Read(ref _started) != 0;
	public int ExitCode => IsTimedOut ? RuntimeShutdownExitCodes.Timeout : RuntimeShutdownExitCodes.Success;

	public void Begin(RuntimeManagedCommand command)
	{
		if (Interlocked.Exchange(ref _started, 1) != 0)
			return;
		_ = RunAsync(command);
	}

	public Task WaitForExitSignalAsync(CancellationToken cancellationToken = default) =>
		_exitSignal.WaitAsync(cancellationToken);

	private async Task RunAsync(RuntimeManagedCommand command)
	{
		var deadline = RuntimeManagedPayloadInterpreter.GetDateTimeOffset(command.Payload, "deadlineUtc");
		_state.TransitionTo("Requested");
		var handlers = _handlers;
		if (handlers == null)
		{
			Complete(false);
			return;
		}

		_state.TransitionTo("Draining");
		var args = new RuntimeManagedCleanupEventArgs(command.Sequence, command.Payload, deadline);
		using var deadlineCts = deadline is { } value
			? new CancellationTokenSource(value - DateTimeOffset.UtcNow > TimeSpan.Zero ? value - DateTimeOffset.UtcNow : TimeSpan.Zero)
			: new CancellationTokenSource();
		var tasks = handlers.GetInvocationList()
			.Cast<RuntimeManagedCleanupHandler>()
			.Select(handler => InvokeHandlerAsync(handler, args, deadlineCts.Token))
			.ToArray();
		var timedOut = false;
		try
		{
			await Task.WhenAll(tasks).WaitAsync(deadlineCts.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (deadline.HasValue)
		{
			timedOut = true;
		}
		Complete(timedOut);
	}

	private async Task InvokeHandlerAsync(
		RuntimeManagedCleanupHandler handler,
		RuntimeManagedCleanupEventArgs args,
		CancellationToken cancellationToken)
	{
		try
		{
			await handler(_owner, args, cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			_state.SetDetail("cleanupError", ex.Message);
		}
	}

	private void Complete(bool timedOut)
	{
		_state.TransitionTo(timedOut ? "Timeout" : "Completed");
		if (Interlocked.Exchange(ref _signalled, 1) == 0)
			_exitSignal.Release();
	}

	public void Dispose() => _exitSignal.Dispose();
}

using System.Runtime.CompilerServices;
using Iwesun.Runtime.Data;

namespace Iwesun.Runtime.Diagnostics;

public sealed class RTask : IDisposable
{
	private readonly RuntimeManagedUnitBase _unit;
	private readonly RuntimeExecutionManager? _execution;
	private readonly RuntimeManagedRegistry? _managed;
	private readonly RuntimeExecutionLifetime _lifetime;
	private readonly string _category;
	private readonly string _threadId;
	private readonly string _sourceLocation;
	private readonly bool _blocksShutdown;
	private readonly RuntimeManagedCleanup _cleanup;
	private readonly Task _task;
	private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly CancellationTokenSource? _ownedStopCts;
	private readonly SemaphoreSlim _wakeupSignal = new(0, 1);
	private IDisposable? _commandRegistration;
	private int _startState;
	private int _managedRegistered;
	private int _executionRegistered;
	private int _auxiliaryResourcesDisposed;
	private int _disposed;
	private int _managedExitCode;
	private int _completionCleaned;
	private int _taskDisposed;

	/// <summary>Waits until a FIFO Wakeup command arrives or the timeout elapses.</summary>
	/// <returns><see langword="true"/> when explicitly awakened; otherwise <see langword="false"/>.</returns>
	public async ValueTask<bool> WaitForWakeupAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
	{
		if (timeout < Timeout.InfiniteTimeSpan)
			throw new ArgumentOutOfRangeException(nameof(timeout));
		return await _wakeupSignal.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
	}

	public RTask(
		Action action,
		string? unitId = null,
		string category = "task",
		string threadId = "",
		RuntimeExecutionLifetime lifetime = RuntimeExecutionLifetime.Dynamic,
		string sourceLocation = "",
		bool blocksShutdown = true)
		: this(action, CancellationToken.None, unitId, category, threadId, lifetime, sourceLocation, blocksShutdown)
	{
	}

	public RTask(
		Action action,
		CancellationToken cancellationToken,
		string? unitId = null,
		string category = "task",
		string threadId = "",
		RuntimeExecutionLifetime lifetime = RuntimeExecutionLifetime.Dynamic,
		string sourceLocation = "",
		bool blocksShutdown = true)
	{
		ArgumentNullException.ThrowIfNull(action);
		_task = new Task(action, cancellationToken);
		(UnitId, _unit, _execution, _managed, _lifetime, _category, _threadId, _sourceLocation) =
			Initialize(unitId, category, threadId, lifetime, sourceLocation);
		_blocksShutdown = blocksShutdown;
		State = _unit.State;
		_cleanup = new RuntimeManagedCleanup(this, State);
		AttachCompletion();
	}

	public RTask(
		Action<CancellationToken> action,
		string? unitId = null,
		string category = "task",
		string threadId = "",
		RuntimeExecutionLifetime lifetime = RuntimeExecutionLifetime.Dynamic,
		string sourceLocation = "",
		CancellationToken cancellationToken = default,
		bool blocksShutdown = true)
	{
		ArgumentNullException.ThrowIfNull(action);
		_ownedStopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		_task = new Task(() => action(_ownedStopCts.Token), _ownedStopCts.Token);
		(UnitId, _unit, _execution, _managed, _lifetime, _category, _threadId, _sourceLocation) =
			Initialize(unitId, category, threadId, lifetime, sourceLocation);
		_blocksShutdown = blocksShutdown;
		State = _unit.State;
		_cleanup = new RuntimeManagedCleanup(this, State);
		AttachCompletion();
	}

	public RTask(
		Action<object?> action,
		object? state,
		string? unitId = null,
		string category = "task",
		string threadId = "",
		RuntimeExecutionLifetime lifetime = RuntimeExecutionLifetime.Dynamic,
		string sourceLocation = "",
		bool blocksShutdown = true)
		: this(action, state, CancellationToken.None, unitId, category, threadId, lifetime, sourceLocation, blocksShutdown)
	{
	}

	public RTask(
		Action<object?> action,
		object? state,
		CancellationToken cancellationToken,
		string? unitId = null,
		string category = "task",
		string threadId = "",
		RuntimeExecutionLifetime lifetime = RuntimeExecutionLifetime.Dynamic,
		string sourceLocation = "",
		bool blocksShutdown = true)
	{
		ArgumentNullException.ThrowIfNull(action);
		_task = new Task(action, state, cancellationToken);
		(UnitId, _unit, _execution, _managed, _lifetime, _category, _threadId, _sourceLocation) =
			Initialize(unitId, category, threadId, lifetime, sourceLocation);
		_blocksShutdown = blocksShutdown;
		State = _unit.State;
		_cleanup = new RuntimeManagedCleanup(this, State);
		AttachCompletion();
	}

	public string UnitId { get; }
	public IRManagedState State { get; }
	public TaskStatus Status => _task.Status;
	public bool IsCompleted => _completion.Task.IsCompleted;
	public bool IsCanceled => _task.IsCanceled;
	public bool IsFaulted => _task.IsFaulted;
	public AggregateException? Exception => _task.Exception;
	public int ExitCode => _cleanup.ExitCode;
	public bool IsCleanupCompleted => _cleanup.IsCompleted;
	public event RuntimeManagedCleanupHandler CleanupRequested
	{
		add => _cleanup.Requested += value;
		remove => _cleanup.Requested -= value;
	}

	public TaskAwaiter GetAwaiter() => _completion.Task.GetAwaiter();
	public bool Wait(TimeSpan timeout) => _completion.Task.Wait(timeout);

	public void SetDetail(string key, string value) => _unit.SetDetail(key, value);
	public bool TryGetDetail(string key, out string? value) => _unit.TryGetDetail(key, out value);
	public RuntimeState TransitionTo(string stateName) => _unit.TransitionTo(stateName);
	public bool TryTransitionTo(string stateName) => _unit.TryTransitionTo(stateName);

	public void Start() => Start(TaskScheduler.Current);

	public void Start(TaskScheduler scheduler)
	{
		ArgumentNullException.ThrowIfNull(scheduler);
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
		AcquireStartOwnership();
		try
		{
			EnsureRegistered();
			MarkRunning();
			_task.Start(scheduler);
		}
		catch
		{
			RollbackFailedStart();
			throw;
		}
	}

	public static RTask Run(Action action)
	{
		var task = new RTask(action);
		task.Start(TaskScheduler.Default);
		return task;
	}

	public static RTask Run(Action action, CancellationToken cancellationToken)
	{
		var task = new RTask(action, cancellationToken);
		task.Start(TaskScheduler.Default);
		return task;
	}

	private (string UnitId, RuntimeManagedUnitBase Unit, RuntimeExecutionManager? Execution, RuntimeManagedRegistry? Managed,
		RuntimeExecutionLifetime Lifetime, string Category, string ThreadId, string SourceLocation) Initialize(
		string? unitId, string category, string threadId, RuntimeExecutionLifetime lifetime, string sourceLocation)
	{
		var resolvedUnitId = string.IsNullOrWhiteSpace(unitId) ? $"task.{Guid.NewGuid():N}" : unitId;
		var unit = new RuntimeManagedUnitBase(resolvedUnitId, RuntimeInstructionEntityKind.Task);
		return (unit.UnitId, unit, unit.Execution, unit.Managed, lifetime, category, threadId, sourceLocation);
	}

	private void EnsureRegistered()
	{
		State.TransitionTo("Start");
		_managed?.Register(
			UnitId,
			"task",
			"InternalManaged",
			State.Snapshot(),
			_blocksShutdown);
		if (_managed is not null)
			Volatile.Write(ref _managedRegistered, 1);
		_execution?.RegisterTask(
			UnitId,
			UnitId,
			_lifetime,
			category: _category,
			threadId: _threadId,
			sourceLocation: _sourceLocation,
			parentTaskId: "",
			step: "registered",
			payload: State.Snapshot());
		if (_execution is not null)
			Volatile.Write(ref _executionRegistered, 1);
		AttachCommandHandler();
	}

	private void MarkRunning()
	{
		State.TransitionTo("Working");
		_execution?.SetTaskState(UnitId, RuntimeTaskState.Running, step: "running", threadId: _threadId, payload: State.Snapshot());
		_managed?.PublishEvent(UnitId, "task-running", "Task entered running state.", State.Snapshot());
	}

	private void AttachCommandHandler()
	{
		_commandRegistration = _managed?.RegisterCommandHandler(UnitId, command =>
		{
			if (command.Kind == RuntimeManagedCommandKind.Wakeup)
			{
				if (_wakeupSignal.CurrentCount == 0)
					_wakeupSignal.Release();
				_managed?.PublishEvent(UnitId, "task-wakeup-command", "Wakeup command received.", new { command.Sequence, command.Payload });
				return;
			}
			if (command.Kind == RuntimeManagedCommandKind.Stop)
			{
				State.SetDetail("stopRequested", DateTimeOffset.UtcNow.ToString("O"));
				_cleanup.Begin(command);
				_ = SignalManagedTaskExitAsync(command.Sequence);
			}
		});
	}

	private async Task SignalManagedTaskExitAsync(long commandSequence)
	{
		await _cleanup.WaitForExitSignalAsync().ConfigureAwait(false);
		Interlocked.Exchange(ref _managedExitCode, _cleanup.ExitCode);
		if (_ownedStopCts != null)
		{
			_ownedStopCts.Cancel();
			_managed?.PublishEvent(UnitId, "task-stop-cancelled", "Cleanup completed or reached the controller deadline; the managed cancellation token was signalled.", new { commandSequence, exitCode = _cleanup.ExitCode });
		}
		else
		{
			_managed?.PublishEvent(UnitId, "task-stop-pending", "Cleanup completed, but non-token-aware work must return through its managed entry point.", new { commandSequence, exitCode = _cleanup.ExitCode });
		}
	}

	private void AttachCompletion()
	{
		_task.ContinueWith(completed =>
		{
			if (completed.IsCanceled)
			{
				State.SetDetail("error", "cancelled");
				State.TransitionTo("Stop");
				_execution?.SetTaskState(UnitId, RuntimeTaskState.Cancelled, step: "cancelled", threadId: _threadId, payload: State.Snapshot());
				_managed?.PublishEvent(UnitId, "task-cancelled", "Task cancelled.", State.Snapshot());
			}
			else if (completed.IsFaulted)
			{
				var error = completed.Exception?.GetBaseException().Message ?? "faulted";
				State.SetDetail("error", error);
				State.TransitionTo("Stop");
				_execution?.SetTaskState(UnitId, RuntimeTaskState.Faulted, step: "faulted", error: error, threadId: _threadId, payload: State.Snapshot());
				_managed?.PublishEvent(UnitId, "task-faulted", error, State.Snapshot());
			}
			else
			{
				State.TransitionTo("Stop");
				_execution?.SetTaskState(UnitId, RuntimeTaskState.Completed, step: "completed", threadId: _threadId, payload: State.Snapshot());
				_managed?.PublishEvent(UnitId, "task-completed", "Task completed.", State.Snapshot());
			}
			CompleteRegistration();
			if (completed.IsCanceled)
				_completion.TrySetCanceled();
			else if (completed.IsFaulted)
				_completion.TrySetException(completed.Exception!.InnerExceptions);
			else
				_completion.TrySetResult();
			if (Volatile.Read(ref _disposed) != 0)
				DisposeUnderlyingTask();
		}, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
	}

	private void RollbackFailedStart()
	{
		UnregisterManaged();
		_commandRegistration?.Dispose();
		_commandRegistration = null;
		Volatile.Write(ref _startState, 2);
		try
		{
			State.SetDetail("error", "start-failed");
			State.TransitionTo("Stop");
			if (Volatile.Read(ref _executionRegistered) != 0)
			{
				_execution?.SetTaskState(UnitId, RuntimeTaskState.Faulted, step: "start-failed", error: "Task start failed.", threadId: _threadId, payload: State.Snapshot());
			}
		}
		catch
		{
			// Preserve the original task start exception.
		}
		DisposeAuxiliaryResources();
	}

	private void CompleteRegistration()
	{
		if (Interlocked.Exchange(ref _completionCleaned, 1) == 1)
			return;
		Volatile.Write(ref _startState, 2);
		if (_cleanup.HasStarted)
			State.TransitionTo(Volatile.Read(ref _managedExitCode) == RuntimeShutdownExitCodes.Timeout ? "Timeout" : "Completed");
		UnregisterManaged();
		_commandRegistration?.Dispose();
		_commandRegistration = null;
		DisposeAuxiliaryResources();
	}

	private void AcquireStartOwnership()
	{
		if (Interlocked.CompareExchange(ref _startState, 1, 0) != 0)
			throw new InvalidOperationException($"Managed task '{UnitId}' can only be started once.");
	}

	private void DisposeAuxiliaryResources()
	{
		if (Interlocked.Exchange(ref _auxiliaryResourcesDisposed, 1) != 0)
			return;
		_ownedStopCts?.Dispose();
		_wakeupSignal.Dispose();
		_cleanup.Dispose();
	}

	private void UnregisterManaged()
	{
		if (Interlocked.Exchange(ref _managedRegistered, 0) != 0)
			_managed?.Unregister(UnitId);
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) == 1)
			return;
		if (_completion.Task.IsCompleted)
		{
			DisposeUnderlyingTask();
		}
		else
		{
			if (Volatile.Read(ref _startState) == 2 && _task.Status == TaskStatus.Created)
				DisposeAuxiliaryResources();
			else
				_managed?.PublishEvent(UnitId, "task-dispose-deferred", "Task disposal deferred until execution completes.", State.Snapshot());
		}
	}

	private void DisposeUnderlyingTask()
	{
		if (Interlocked.Exchange(ref _taskDisposed, 1) == 0)
			_task.Dispose();
	}
}

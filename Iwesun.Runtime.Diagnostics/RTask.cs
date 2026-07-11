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
	private readonly Task _task;
	private readonly CancellationTokenSource? _ownedStopCts;
	private IDisposable? _commandRegistration;
	private int _registered;
	private int _disposed;

	public RTask(
		Action action,
		string? unitId = null,
		string category = "task",
		string threadId = "",
		RuntimeExecutionLifetime lifetime = RuntimeExecutionLifetime.Dynamic,
		string sourceLocation = "")
		: this(action, CancellationToken.None, unitId, category, threadId, lifetime, sourceLocation)
	{
	}

	public RTask(
		Action action,
		CancellationToken cancellationToken,
		string? unitId = null,
		string category = "task",
		string threadId = "",
		RuntimeExecutionLifetime lifetime = RuntimeExecutionLifetime.Dynamic,
		string sourceLocation = "")
	{
		ArgumentNullException.ThrowIfNull(action);
		_task = new Task(action, cancellationToken);
		(UnitId, _unit, _execution, _managed, _lifetime, _category, _threadId, _sourceLocation) =
			Initialize(unitId, category, threadId, lifetime, sourceLocation);
		State = _unit.State;
		AttachCompletion();
		AttachCommandHandler();
	}

	public RTask(
		Action<CancellationToken> action,
		string? unitId = null,
		string category = "task",
		string threadId = "",
		RuntimeExecutionLifetime lifetime = RuntimeExecutionLifetime.Dynamic,
		string sourceLocation = "",
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(action);
		_ownedStopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		_task = new Task(() => action(_ownedStopCts.Token), _ownedStopCts.Token);
		(UnitId, _unit, _execution, _managed, _lifetime, _category, _threadId, _sourceLocation) =
			Initialize(unitId, category, threadId, lifetime, sourceLocation);
		State = _unit.State;
		AttachCompletion();
		AttachCommandHandler();
	}

	public RTask(
		Action<object?> action,
		object? state,
		string? unitId = null,
		string category = "task",
		string threadId = "",
		RuntimeExecutionLifetime lifetime = RuntimeExecutionLifetime.Dynamic,
		string sourceLocation = "")
		: this(action, state, CancellationToken.None, unitId, category, threadId, lifetime, sourceLocation)
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
		string sourceLocation = "")
	{
		ArgumentNullException.ThrowIfNull(action);
		_task = new Task(action, state, cancellationToken);
		(UnitId, _unit, _execution, _managed, _lifetime, _category, _threadId, _sourceLocation) =
			Initialize(unitId, category, threadId, lifetime, sourceLocation);
		State = _unit.State;
		AttachCompletion();
		AttachCommandHandler();
	}

	public string UnitId { get; }
	public IRManagedState State { get; }
	public TaskStatus Status => _task.Status;
	public bool IsCompleted => _task.IsCompleted;
	public bool IsCanceled => _task.IsCanceled;
	public bool IsFaulted => _task.IsFaulted;
	public AggregateException? Exception => _task.Exception;

	public TaskAwaiter GetAwaiter() => _task.GetAwaiter();
	public bool Wait(TimeSpan timeout) => _task.Wait(timeout);

	public void SetDetail(string key, string value) => _unit.SetDetail(key, value);
	public bool TryGetDetail(string key, out string? value) => _unit.TryGetDetail(key, out value);
	public RuntimeState TransitionTo(string stateName) => _unit.TransitionTo(stateName);
	public bool TryTransitionTo(string stateName) => _unit.TryTransitionTo(stateName);

	public void Start() => Start(TaskScheduler.Current);

	public void Start(TaskScheduler scheduler)
	{
		ArgumentNullException.ThrowIfNull(scheduler);
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
		ThrowIfGlobalStopRequested();
		EnsureRegistered();
		try
		{
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
		if (Interlocked.Exchange(ref _registered, 1) == 1)
			return;
		State.TransitionTo("Start");
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
		_managed?.Register(UnitId, "task", "InternalManaged", State.Snapshot());
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
			if (command.Kind != RuntimeManagedCommandKind.Stop)
				return;
			State.SetDetail("stopRequested", DateTimeOffset.UtcNow.ToString("O"));
			if (_ownedStopCts != null)
			{
				_ownedStopCts.Cancel();
				_managed?.PublishEvent(UnitId, "task-stop-cancelled", "Task Stop command signalled its cancellation token.", new { command.Sequence });
			}
			else
			{
				_managed?.PublishEvent(UnitId, "task-stop-pending", "Task Stop command is waiting for non-token-aware work to finish.", new { command.Sequence });
			}
		});
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
		}, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
	}

	private void ThrowIfGlobalStopRequested()
	{
		if (_managed?.IsGlobalStopOrExitRequested != true)
			return;
		State.SetDetail("error", "global-stop");
		_managed.PublishEvent(UnitId, "task-global-stop", "Task start rejected due to global stop/exit state.", State.Snapshot());
		throw new OperationCanceledException("Task cannot start after global stop/exit was requested.");
	}

	private void RollbackFailedStart()
	{
		_managed?.Unregister(UnitId);
		Interlocked.Exchange(ref _registered, 0);
		State.SetDetail("error", "start-failed");
		State.TransitionTo("Stop");
		_execution?.SetTaskState(UnitId, RuntimeTaskState.Faulted, step: "start-failed", error: "Task start failed.", threadId: _threadId, payload: State.Snapshot());
	}

	private void CompleteRegistration()
	{
		_managed?.Unregister(UnitId);
		_commandRegistration?.Dispose();
		_commandRegistration = null;
		_ownedStopCts?.Dispose();
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) == 1)
			return;
		if (_task.IsCompleted)
		{
			CompleteRegistration();
			_task.Dispose();
		}
		else
		{
			_managed?.PublishEvent(UnitId, "task-dispose-deferred", "Task disposal deferred until execution completes.", State.Snapshot());
		}
	}
}

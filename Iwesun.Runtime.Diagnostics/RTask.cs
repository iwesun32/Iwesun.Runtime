using System.Threading;
using System.Threading.Tasks;

namespace Iwesun.Runtime.Diagnostics;

public class RTask : Task
{
	private readonly RuntimeExecutionManager? _execution;
	private readonly RuntimeManagedRegistry? _managed;
	private readonly RuntimeExecutionLifetime _lifetime;
	private readonly string _category;
	private readonly string _threadId;
	private readonly string _sourceLocation;
	private int _registered;

	public RTask(
		Action action,
		string? unitId = null,
		string category = "task",
		string threadId = "",
		RuntimeExecutionLifetime lifetime = RuntimeExecutionLifetime.Dynamic,
		string sourceLocation = "")
		: base(action)
	{
		UnitId = string.IsNullOrWhiteSpace(unitId) ? $"task.{Guid.NewGuid():N}" : unitId;
		_execution = RuntimeInjectionContext.Execution;
		_managed = RuntimeInjectionContext.Managed;
		_lifetime = lifetime;
		_category = category;
		_threadId = threadId;
		_sourceLocation = sourceLocation;
		State = new RManagedState(UnitId);
		AttachCompletion();
	}

	public RTask(
		Action action,
		CancellationToken cancellationToken,
		string? unitId = null,
		string category = "task",
		string threadId = "",
		RuntimeExecutionLifetime lifetime = RuntimeExecutionLifetime.Dynamic,
		string sourceLocation = "")
		: base(action, cancellationToken)
	{
		UnitId = string.IsNullOrWhiteSpace(unitId) ? $"task.{Guid.NewGuid():N}" : unitId;
		_execution = RuntimeInjectionContext.Execution;
		_managed = RuntimeInjectionContext.Managed;
		_lifetime = lifetime;
		_category = category;
		_threadId = threadId;
		_sourceLocation = sourceLocation;
		State = new RManagedState(UnitId);
		AttachCompletion();
	}

	public RTask(
		Action<object?> action,
		object? state,
		string? unitId = null,
		string category = "task",
		string threadId = "",
		RuntimeExecutionLifetime lifetime = RuntimeExecutionLifetime.Dynamic,
		string sourceLocation = "")
		: base(action, state)
	{
		UnitId = string.IsNullOrWhiteSpace(unitId) ? $"task.{Guid.NewGuid():N}" : unitId;
		_execution = RuntimeInjectionContext.Execution;
		_managed = RuntimeInjectionContext.Managed;
		_lifetime = lifetime;
		_category = category;
		_threadId = threadId;
		_sourceLocation = sourceLocation;
		State = new RManagedState(UnitId);
		AttachCompletion();
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
		: base(action, state, cancellationToken)
	{
		UnitId = string.IsNullOrWhiteSpace(unitId) ? $"task.{Guid.NewGuid():N}" : unitId;
		_execution = RuntimeInjectionContext.Execution;
		_managed = RuntimeInjectionContext.Managed;
		_lifetime = lifetime;
		_category = category;
		_threadId = threadId;
		_sourceLocation = sourceLocation;
		State = new RManagedState(UnitId);
		AttachCompletion();
	}

	public string UnitId { get; }
	public IRManagedState State { get; }

	public void SetDetail(string key, string value) => State.SetDetail(key, value);
	public bool TryGetDetail(string key, out string? value) => State.TryGetDetail(key, out value);
	public RuntimeState TransitionTo(string stateName) => State.TransitionTo(stateName);
	public bool TryTransitionTo(string stateName) => State.TryTransitionTo(stateName);

	public new void Start()
	{
		EnsureRegistered();
		MarkRunning();
		base.Start();
	}

	public new void Start(TaskScheduler scheduler)
	{
		ArgumentNullException.ThrowIfNull(scheduler);
		EnsureRegistered();
		MarkRunning();
		base.Start(scheduler);
	}

	public static new RTask Run(Action action)
	{
		ArgumentNullException.ThrowIfNull(action);
		var task = new RTask(action);
		task.Start(TaskScheduler.Default);
		return task;
	}

	public static new RTask Run(Action action, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(action);
		var task = new RTask(action, cancellationToken);
		task.Start(TaskScheduler.Default);
		return task;
	}

	private void EnsureRegistered()
	{
		if (Interlocked.Exchange(ref _registered, 1) == 1)
		{
			return;
		}

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

	private void AttachCompletion()
	{
		ContinueWith(completedTask =>
		{
			if (completedTask.IsCanceled)
			{
				State.SetDetail("error", "cancelled");
				State.TransitionTo("Stop");
				_execution?.SetTaskState(UnitId, RuntimeTaskState.Cancelled, step: "cancelled", threadId: _threadId, payload: State.Snapshot());
				_managed?.PublishEvent(UnitId, "task-cancelled", "Task cancelled.", State.Snapshot());
				_managed?.Unregister(UnitId);
				return;
			}

			if (completedTask.IsFaulted)
			{
				State.SetDetail("error", completedTask.Exception?.GetBaseException().Message ?? "faulted");
				State.TransitionTo("Stop");
				_execution?.SetTaskState(UnitId, RuntimeTaskState.Faulted, step: "faulted", error: completedTask.Exception?.GetBaseException().Message, threadId: _threadId, payload: State.Snapshot());
				_managed?.PublishEvent(UnitId, "task-faulted", completedTask.Exception?.GetBaseException().Message ?? "faulted", State.Snapshot());
				_managed?.Unregister(UnitId);
				return;
			}

			State.TransitionTo("Stop");
			_execution?.SetTaskState(UnitId, RuntimeTaskState.Completed, step: "completed", threadId: _threadId, payload: State.Snapshot());
			_managed?.PublishEvent(UnitId, "task-completed", "Task completed.", State.Snapshot());
			_managed?.Unregister(UnitId);
		}, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
	}
}

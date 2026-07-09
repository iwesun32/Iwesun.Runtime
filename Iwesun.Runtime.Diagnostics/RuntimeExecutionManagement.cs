namespace Iwesun.Runtime.Diagnostics;

public enum RuntimeExecutionLifetime
{
	Static,
	Dynamic
}

public enum RuntimeThreadKind
{
	Host,
	Worker,
	Monitor,
	Coordinator,
	Background,
	Custom
}

public enum RuntimeThreadState
{
	Registered,
	Running,
	Draining,
	Completed,
	Faulted,
	Cancelled,
	ForcedExit
}

public enum RuntimeTaskState
{
	Registered,
	Running,
	Waiting,
	Completed,
	Faulted,
	Cancelled,
	Draining
}

public sealed record RuntimeThreadRecord
{
	public string Id { get; init; } = "";
	public string Name { get; init; } = "";
	public RuntimeExecutionLifetime Lifetime { get; init; }
	public RuntimeThreadKind Kind { get; init; } = RuntimeThreadKind.Custom;
	public string Owner { get; init; } = "";
	public string SourceLocation { get; init; } = "";
	public int ManagedThreadId { get; init; }
	public int? NativeThreadId { get; init; }
	public RuntimeThreadState State { get; init; } = RuntimeThreadState.Registered;
	public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
	public DateTimeOffset LastHeartbeatAt { get; init; } = DateTimeOffset.UtcNow;
	public DateTimeOffset? ExitedAt { get; init; }
	public string CurrentTaskId { get; init; } = "";
	public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
	public object? Payload { get; init; }
}

public sealed record RuntimeTaskRecord
{
	public string Id { get; init; } = "";
	public string Name { get; init; } = "";
	public RuntimeExecutionLifetime Lifetime { get; init; }
	public string Category { get; init; } = "";
	public string SourceLocation { get; init; } = "";
	public string ThreadId { get; init; } = "";
	public string ParentTaskId { get; init; } = "";
	public RuntimeTaskState State { get; init; } = RuntimeTaskState.Registered;
	public string Step { get; init; } = "";
	public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
	public DateTimeOffset? CompletedAt { get; init; }
	public DateTimeOffset LastHeartbeatAt { get; init; } = DateTimeOffset.UtcNow;
	public string Error { get; init; } = "";
	public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
	public object? Payload { get; init; }
}

public sealed record RuntimeExecutionSnapshot(
	IReadOnlyList<RuntimeThreadRecord> StaticThreads,
	IReadOnlyList<RuntimeThreadRecord> DynamicThreads,
	IReadOnlyList<RuntimeTaskRecord> StaticTasks,
	IReadOnlyList<RuntimeTaskRecord> DynamicTasks);

public sealed class RuntimeExecutionManager
{
	private readonly object _gate = new();
	private readonly Dictionary<string, RuntimeThreadRecord> _threads = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, RuntimeTaskRecord> _tasks = new(StringComparer.OrdinalIgnoreCase);

	public RuntimeThreadRecord RegisterThread(
		string id,
		string name,
		RuntimeExecutionLifetime lifetime,
		RuntimeThreadKind kind = RuntimeThreadKind.Custom,
		string owner = "",
		string sourceLocation = "",
		int managedThreadId = 0,
		int? nativeThreadId = null,
		IReadOnlyList<string>? tags = null,
		object? payload = null)
	{
		var now = DateTimeOffset.UtcNow;
		lock (_gate)
		{
			var current = new RuntimeThreadRecord
			{
				Id = id,
				Name = name,
				Lifetime = lifetime,
				Kind = kind,
				Owner = owner,
				SourceLocation = sourceLocation,
				ManagedThreadId = managedThreadId,
				NativeThreadId = nativeThreadId,
				State = RuntimeThreadState.Registered,
				StartedAt = now,
				LastHeartbeatAt = now,
				Tags = tags ?? Array.Empty<string>(),
				Payload = payload
			};

			_threads[id] = current;
			return current;
		}
	}

	public RuntimeThreadRecord SetThreadState(
		string id,
		RuntimeThreadState state,
		int? managedThreadId = null,
		string? currentTaskId = null,
		object? payload = null)
	{
		lock (_gate)
		{
			var current = RequireThread(id);
			var next = current with
			{
				State = state,
				ManagedThreadId = managedThreadId ?? current.ManagedThreadId,
				CurrentTaskId = currentTaskId ?? current.CurrentTaskId,
				LastHeartbeatAt = DateTimeOffset.UtcNow,
				ExitedAt = state is RuntimeThreadState.Completed or RuntimeThreadState.Cancelled or RuntimeThreadState.Faulted or RuntimeThreadState.ForcedExit ? DateTimeOffset.UtcNow : current.ExitedAt,
				Payload = payload ?? current.Payload
			};
			_threads[id] = next;
			return next;
		}
	}

	public RuntimeThreadRecord HeartbeatThread(string id, object? payload = null)
	{
		lock (_gate)
		{
			var current = RequireThread(id);
			var next = current with
			{
				LastHeartbeatAt = DateTimeOffset.UtcNow,
				Payload = payload ?? current.Payload
			};
			_threads[id] = next;
			return next;
		}
	}

	public RuntimeTaskRecord RegisterTask(
		string id,
		string name,
		RuntimeExecutionLifetime lifetime,
		string category = "",
		string threadId = "",
		string sourceLocation = "",
		string parentTaskId = "",
		string step = "",
		IReadOnlyList<string>? tags = null,
		object? payload = null)
	{
		var now = DateTimeOffset.UtcNow;
		lock (_gate)
		{
			var current = new RuntimeTaskRecord
			{
				Id = id,
				Name = name,
				Lifetime = lifetime,
				Category = category,
				ThreadId = threadId,
				SourceLocation = sourceLocation,
				ParentTaskId = parentTaskId,
				Step = step,
				State = RuntimeTaskState.Registered,
				StartedAt = now,
				LastHeartbeatAt = now,
				Tags = tags ?? Array.Empty<string>(),
				Payload = payload
			};

			_tasks[id] = current;
			return current;
		}
	}

	public RuntimeTaskRecord SetTaskState(
		string id,
		RuntimeTaskState state,
		string? step = null,
		string? error = null,
		string? threadId = null,
		object? payload = null)
	{
		lock (_gate)
		{
			var current = RequireTask(id);
			var next = current with
			{
				State = state,
				Step = step ?? current.Step,
				ThreadId = threadId ?? current.ThreadId,
				Error = error ?? current.Error,
				LastHeartbeatAt = DateTimeOffset.UtcNow,
				CompletedAt = state is RuntimeTaskState.Completed or RuntimeTaskState.Cancelled or RuntimeTaskState.Faulted or RuntimeTaskState.Draining ? DateTimeOffset.UtcNow : current.CompletedAt,
				Payload = payload ?? current.Payload
			};
			_tasks[id] = next;
			return next;
		}
	}

	public RuntimeTaskRecord HeartbeatTask(string id, object? payload = null)
	{
		lock (_gate)
		{
			var current = RequireTask(id);
			var next = current with
			{
				LastHeartbeatAt = DateTimeOffset.UtcNow,
				Payload = payload ?? current.Payload
			};
			_tasks[id] = next;
			return next;
		}
	}

	public RuntimeExecutionSnapshot Snapshot()
	{
		lock (_gate)
		{
			var threads = _threads.Values.OrderBy(thread => thread.Lifetime).ThenBy(thread => thread.Name, StringComparer.OrdinalIgnoreCase).ToArray();
			var tasks = _tasks.Values.OrderBy(task => task.Lifetime).ThenBy(task => task.Name, StringComparer.OrdinalIgnoreCase).ToArray();
			return new RuntimeExecutionSnapshot(
				threads.Where(thread => thread.Lifetime == RuntimeExecutionLifetime.Static).ToArray(),
				threads.Where(thread => thread.Lifetime == RuntimeExecutionLifetime.Dynamic).ToArray(),
				tasks.Where(task => task.Lifetime == RuntimeExecutionLifetime.Static).ToArray(),
				tasks.Where(task => task.Lifetime == RuntimeExecutionLifetime.Dynamic).ToArray());
		}
	}

	public IReadOnlyList<RuntimeThreadRecord> StaticThreads => Snapshot().StaticThreads;
	public IReadOnlyList<RuntimeThreadRecord> DynamicThreads => Snapshot().DynamicThreads;
	public IReadOnlyList<RuntimeTaskRecord> StaticTasks => Snapshot().StaticTasks;
	public IReadOnlyList<RuntimeTaskRecord> DynamicTasks => Snapshot().DynamicTasks;

	private RuntimeThreadRecord RequireThread(string id)
	{
		if (!_threads.TryGetValue(id, out var current))
			throw new KeyNotFoundException($"Thread '{id}' is not registered.");
		return current;
	}

	private RuntimeTaskRecord RequireTask(string id)
	{
		if (!_tasks.TryGetValue(id, out var current))
			throw new KeyNotFoundException($"Task '{id}' is not registered.");
		return current;
	}
}
using System.Collections.Concurrent;
using Iwesun.Runtime.Data;

namespace Iwesun.Runtime.Diagnostics;

public enum RuntimeManagedCommandKind
{
	Initialize,
	Stop,
	Wait,
	Wakeup,
	Snapshot
}

public sealed record RuntimeManagedRegistration(
	string UnitId,
	string UnitType,
	string Ownership,
	DateTimeOffset RegisteredAt,
	DateTimeOffset UpdatedAt,
	RManagedStateSnapshot State);

public sealed record RuntimeManagedCommand(
	long Sequence,
	string TargetUnitId,
	RuntimeManagedCommandKind Kind,
	string? Payload,
	DateTimeOffset EnqueuedAt);

public sealed record RuntimeManagedEvent(
	long Sequence,
	string UnitId,
	string Kind,
	string Message,
	object? Payload,
	DateTimeOffset Timestamp);

public sealed record RuntimeManagedCommandQueueSnapshot(
	string UnitId,
	int PendingCount,
	IReadOnlyList<RuntimeManagedCommand> Commands);

public enum RuntimeInstructionSendStatus
{
	Sent,
	TargetNotFound,
	QueueFull,
	TargetDisposed
}

public readonly record struct RuntimeInstructionSendResult(
	RuntimeInstructionSendStatus Status,
	RuntimeManagedCommand Command)
{
	public bool Sent => Status == RuntimeInstructionSendStatus.Sent;
}

public sealed record RuntimeShutdownPendingUnit(
	string UnitId,
	string UnitType,
	string CurrentState,
	DateTimeOffset UpdatedAt);

public sealed record RuntimeShutdownStatus(
	bool CanExit,
	bool TimedOut,
	int RecommendedExitCode,
	DateTimeOffset? ExitDeadlineUtc,
	IReadOnlyList<RuntimeShutdownPendingUnit> PendingUnits);

public sealed class RuntimeManagedRegistry : IDisposable
{
	private const int StateHistoryDepth = 128;
	private const int CommandQueueDepth = 8;
	private readonly ConcurrentDictionary<string, RuntimeManagedRegistration> _registrations = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, ConcurrentQueue<RuntimeManagedCommand>> _commandFifos = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, RuntimeUnitInbox> _unitInboxes = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, Action<RuntimeManagedCommand>> _commandHandlers = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<long, string> _commandPayloads = new();
	private readonly RuntimeSharedAtomicFifo _controllerInbox;
	private readonly RuntimeInstructionDispatcher _controllerDispatcher;
	private readonly ConcurrentQueue<RuntimeManagedCommand> _controllerCommands = new();
	private readonly ConcurrentDictionary<string, RuntimeStateSnapshot> _sharedUnitStates = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, RuntimeDList<RuntimeState>> _sharedUnitStateHistory = new(StringComparer.OrdinalIgnoreCase);
	private readonly RuntimeStateManager _globalLifecycle = new();
	private readonly RuntimeDList<RuntimeState> _globalLifecycleHistory = new();
	private DateTimeOffset? _globalExitDeadlineUtc;
	private readonly ConcurrentQueue<RuntimeManagedEvent> _eventFifo = new();
	private long _commandSequence;
	private long _eventSequence;
	private long _pendingCommandCount;
	private long _droppedCommandCount;
	private TaskCompletionSource _changed = NewChangeSource();
	private int _disposed;
	private RuntimeSharedAtomicFifo? _externalControllerInbox;
	private RuntimeSharedAtomicFifo? _externalUnitInbox;
	private RuntimeInstructionDispatcher? _externalUnitDispatcher;
	public event EventHandler<RuntimeValueInstruction>? ControllerInstructionReceived;
	public event EventHandler? Changed;
	public long PendingCommandCount => Volatile.Read(ref _pendingCommandCount);
	public long DroppedCommandCount => Volatile.Read(ref _droppedCommandCount);
	public int ControllerCommandCount => _controllerCommands.Count;

	public RuntimeStateSnapshot GlobalLifecycleState => _globalLifecycle.Snapshot();
	public DateTimeOffset? GlobalExitDeadlineUtc => _globalExitDeadlineUtc;
	public bool IsGlobalStopOrExitRequested
	{
		get
		{
			var state = _globalLifecycle.CurrentState.Name;
			return state.Equals("Stop", StringComparison.OrdinalIgnoreCase)
				|| state.Equals("Exit", StringComparison.OrdinalIgnoreCase);
		}
	}

	public RuntimeManagedRegistry()
	{
		_controllerInbox = new RuntimeSharedAtomicFifo(128);
		_controllerDispatcher = new RuntimeInstructionDispatcher(_controllerInbox, DispatchControllerInstruction);
		EnsureGlobalLifecycleStates();
		var initial = _globalLifecycle.SetCurrentByName("Initialize");
		_globalLifecycleHistory.AddLast(initial);
	}

	public RuntimeManagedRegistration Register(string unitId, string unitType, string ownership, RManagedStateSnapshot state)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(unitId);
		ArgumentException.ThrowIfNullOrWhiteSpace(unitType);
		ArgumentException.ThrowIfNullOrWhiteSpace(ownership);

		var now = DateTimeOffset.UtcNow;
		var registration = new RuntimeManagedRegistration(unitId, unitType, ownership, now, now, state);
		_registrations[unitId] = registration;
		_unitInboxes.AddOrUpdate(
			unitId,
			_ => new RuntimeUnitInbox(instruction => DispatchUnitInstruction(unitId, instruction)),
			(_, existing) => existing);
		_sharedUnitStates[unitId] = state.State;
		AppendUnitStateHistory(unitId, state.State.CurrentState);
		PublishEvent(unitId, "registered", $"{unitType} registered.", state);
		SignalChanged();
		return registration;
	}

	public bool Unregister(string unitId)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(unitId);
		var removed = _registrations.TryRemove(unitId, out var registration);
		if (removed)
		{
			_commandHandlers.TryRemove(unitId, out _);
			if (_unitInboxes.TryRemove(unitId, out var inbox))
				inbox.Dispose();
			_sharedUnitStates.TryRemove(unitId, out _);
			_sharedUnitStateHistory.TryRemove(unitId, out _);
			if (_commandFifos.TryRemove(unitId, out var queue))
			{
				while (queue.TryDequeue(out _))
				{
					Interlocked.Decrement(ref _pendingCommandCount);
				}
			}

			PublishEvent(unitId, "unregistered", $"{registration!.UnitType} unregistered.", null);
			SignalChanged();
		}

		return removed;
	}

	public bool UpdateState(string unitId, RManagedStateSnapshot state)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(unitId);
		if (!_registrations.TryGetValue(unitId, out var current))
		{
			return false;
		}

		var next = current with { UpdatedAt = DateTimeOffset.UtcNow, State = state };
		_registrations[unitId] = next;
		_sharedUnitStates[unitId] = state.State;
		AppendUnitStateHistory(unitId, state.State.CurrentState);
		PublishEvent(unitId, "state-updated", "Managed state updated.", new { state.State.CurrentState.Name, state.State.CurrentPath });
		SignalChanged();
		return true;
	}

	public RuntimeStateSnapshot SetGlobalLifecycleState(string stateName, DateTimeOffset? exitDeadlineUtc = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(stateName);
		EnsureGlobalLifecycleStates();
		var state = _globalLifecycle.SetCurrentByName(stateName);
		_globalExitDeadlineUtc = exitDeadlineUtc;
		_globalLifecycleHistory.AddLast(state);
		TrimHistory(_globalLifecycleHistory);
		PublishEvent("runtime.global", "global-state-updated", $"Global lifecycle changed to {state.Name}.", new
		{
			state.Code,
			state.Name,
			exitDeadlineUtc
		});

		return _globalLifecycle.Snapshot();
	}

	public RuntimeShutdownStatus EvaluateShutdownStatus(DateTimeOffset? nowUtc = null)
	{
		ImportSharedStates();
		var now = nowUtc ?? DateTimeOffset.UtcNow;
		var pending = SnapshotRegistrations()
			.Select(x => new RuntimeShutdownPendingUnit(
				x.UnitId,
				x.UnitType,
				x.State.State.CurrentState.Name,
				x.UpdatedAt))
			.ToArray();
		var timedOut = _globalExitDeadlineUtc.HasValue
			&& now >= _globalExitDeadlineUtc.Value
			&& pending.Length > 0;
		var canExit = pending.Length == 0 || timedOut;
		var exitCode = canExit
			? (timedOut ? 124 : 0)
			: -1;
		return new RuntimeShutdownStatus(
			CanExit: canExit,
			TimedOut: timedOut,
			RecommendedExitCode: exitCode,
			ExitDeadlineUtc: _globalExitDeadlineUtc,
			PendingUnits: pending);
	}

	public RuntimeShutdownStatus RequestShutdown(int countdownMs, string? payload = null)
	{
		countdownMs = Math.Max(100, countdownMs);
		var deadline = DateTimeOffset.UtcNow.AddMilliseconds(countdownMs);
		var controlPayload = RuntimeManagedPayloadInterpreter.ToJson(new
		{
			kind = "shutdown-request",
			deadlineUtc = deadline.ToString("O"),
			payload
		});
		EnqueueControllerCommand(RuntimeManagedCommandKind.Stop, controlPayload);
		SetGlobalLifecycleState("Stop", deadline);
		BroadcastCommand(RuntimeManagedCommandKind.Stop, controlPayload);
		BroadcastCommand(RuntimeManagedCommandKind.Wakeup, controlPayload);
		return EvaluateShutdownStatus();
	}

	public IReadOnlyList<RuntimeState> SnapshotGlobalLifecycleHistory(int take = StateHistoryDepth)
	{
		take = Math.Clamp(take, 1, StateHistoryDepth);
		var all = _globalLifecycleHistory.ToArraySnapshot();
		if (all.Count <= take)
		{
			return all;
		}

		return all.Skip(Math.Max(0, all.Count - take)).ToArray();
	}

	public bool TryGetUnitState(string unitId, out RuntimeStateSnapshot? snapshot)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(unitId);
		return _sharedUnitStates.TryGetValue(unitId, out snapshot);
	}

	public IReadOnlyList<RuntimeState> SnapshotUnitStateHistory(string unitId, int take = StateHistoryDepth)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(unitId);
		take = Math.Clamp(take, 1, StateHistoryDepth);
		if (!_sharedUnitStateHistory.TryGetValue(unitId, out var history))
		{
			return Array.Empty<RuntimeState>();
		}

		var all = history.ToArraySnapshot();
		if (all.Count <= take)
		{
			return all;
		}

		return all.Skip(Math.Max(0, all.Count - take)).ToArray();
	}

	public IReadOnlyDictionary<string, IReadOnlyList<RuntimeState>> SnapshotAllUnitStateHistories(int take = StateHistoryDepth)
	{
		take = Math.Clamp(take, 1, StateHistoryDepth);
		var result = new Dictionary<string, IReadOnlyList<RuntimeState>>(StringComparer.OrdinalIgnoreCase);
		foreach (var pair in _sharedUnitStateHistory)
		{
			var all = pair.Value.ToArraySnapshot();
			if (all.Count <= take)
			{
				result[pair.Key] = all;
			}
			else
			{
				result[pair.Key] = all.Skip(Math.Max(0, all.Count - take)).ToArray();
			}
		}

		return result;
	}

	public IReadOnlyList<RuntimeManagedRegistration> SnapshotRegistrations()
	{
		return _registrations.Values.OrderBy(x => x.UnitType, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.UnitId, StringComparer.OrdinalIgnoreCase).ToArray();
	}

	public async Task WaitForChangeAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
	{
		var observed = Volatile.Read(ref _changed).Task;
		await Task.WhenAny(observed, Task.Delay(timeout, cancellationToken)).ConfigureAwait(false);
	}

	private void SignalChanged()
	{
		var previous = Interlocked.Exchange(ref _changed, NewChangeSource());
		previous.TrySetResult();
		Changed?.Invoke(this, EventArgs.Empty);
	}

	private static TaskCompletionSource NewChangeSource() =>
		new(TaskCreationOptions.RunContinuationsAsynchronously);

	public IReadOnlyList<RuntimeManagedCommandQueueSnapshot> SnapshotCommandQueues(int perUnitCount = 100)
	{
		perUnitCount = Math.Clamp(perUnitCount, 1, 4096);
		return _commandFifos
			.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
			.Select(pair =>
			{
				var commands = pair.Value.ToArray();
				var slice = commands.Length <= perUnitCount
					? commands
					: commands[^perUnitCount..];
				return new RuntimeManagedCommandQueueSnapshot(
					pair.Key,
					commands.Length,
					slice);
			})
			.ToArray();
	}

	public IReadOnlyList<RuntimeManagedEvent> SnapshotEvents(int count = 200)
	{
		count = Math.Clamp(count, 1, 4096);
		var events = _eventFifo.ToArray();
		if (events.Length <= count)
		{
			return events;
		}

		return events[^count..];
	}

	public RuntimeManagedCommand EnqueueCommand(string targetUnitId, RuntimeManagedCommandKind kind, string? payload = null)
		=> TryEnqueueCommand(targetUnitId, kind, payload).Command;

	public RuntimeInstructionSendResult TryEnqueueCommand(string targetUnitId, RuntimeManagedCommandKind kind, string? payload = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(targetUnitId);
		var sequence = Interlocked.Increment(ref _commandSequence);
		var command = new RuntimeManagedCommand(sequence, targetUnitId, kind, payload, DateTimeOffset.UtcNow);
		if (!string.IsNullOrEmpty(payload))
		{
			_commandPayloads[sequence] = payload;
			PublishEvent(targetUnitId, "command-payload-pipe-required", "Complex command content must be transported through the diagnostics pipe.", new { command.Sequence, command.Kind });
		}

		if (!_unitInboxes.TryGetValue(targetUnitId, out var inbox))
		{
			PublishEvent(targetUnitId, "command-target-gone", "Managed unit unregistered before command delivery.", new { command.Sequence, command.Kind });
			_commandPayloads.TryRemove(sequence, out _);
			return new RuntimeInstructionSendResult(RuntimeInstructionSendStatus.TargetNotFound, command);
		}
		var instruction = new RuntimeValueInstruction(
			command.Sequence,
			Environment.ProcessId,
			Environment.CurrentManagedThreadId,
			ResolveEntityKind(targetUnitId),
			RuntimeInjectorTransportCodec.ComputeStableHash32(targetUnitId),
			(int)kind,
			0,
			0,
			0);
		try
		{
			if (!inbox.TrySend(instruction))
			{
				Interlocked.Increment(ref _droppedCommandCount);
				_commandPayloads.TryRemove(sequence, out _);
				return new RuntimeInstructionSendResult(RuntimeInstructionSendStatus.QueueFull, command);
			}
		}
		catch (ObjectDisposedException)
		{
			PublishEvent(targetUnitId, "command-target-disposed", "Managed unit inbox was disposed during command delivery.", new { command.Sequence, command.Kind });
			_commandPayloads.TryRemove(sequence, out _);
			return new RuntimeInstructionSendResult(RuntimeInstructionSendStatus.TargetDisposed, command);
		}

		PublishEvent(targetUnitId, "command-enqueued", kind.ToString(), new { command.Sequence, command.TargetUnitId, command.Kind, command.Payload });
		return new RuntimeInstructionSendResult(RuntimeInstructionSendStatus.Sent, command);
	}

	public bool TryPublishStateCode(
		string unitId,
		RuntimeInstructionEntityKind entityKind,
		int stateCode,
		long arg0 = 0,
		long arg1 = 0)
	{
		if (!_registrations.ContainsKey(unitId))
			return false;
		var instruction = new RuntimeValueInstruction(
			Interlocked.Increment(ref _commandSequence),
			Environment.ProcessId,
			Environment.CurrentManagedThreadId,
			(int)entityKind,
			RuntimeInjectorTransportCodec.ComputeStableHash32(unitId),
			stateCode,
			0,
			arg0,
			arg1);
		return (_externalControllerInbox ?? _controllerInbox).TryEnqueue(instruction);
	}

	internal void AttachExternalInstructionHandles(string unitId, RuntimeInstructionHandleDescriptor descriptor)
	{
		if (descriptor.Version != 1)
			throw new InvalidOperationException($"Unsupported instruction descriptor version: {descriptor.Version}");
		_externalUnitDispatcher?.Dispose();
		_externalUnitInbox?.Dispose();
		_externalControllerInbox?.Dispose();
		_externalControllerInbox = RuntimeSharedAtomicFifo.Attach(
			(nint)descriptor.ControllerMappingHandle,
			(nint)descriptor.ControllerEventHandle,
			descriptor.ControllerCapacity);
		_externalUnitInbox = RuntimeSharedAtomicFifo.Attach(
			(nint)descriptor.UnitMappingHandle,
			(nint)descriptor.UnitEventHandle,
			descriptor.UnitCapacity);
		_externalUnitDispatcher = new RuntimeInstructionDispatcher(_externalUnitInbox, instruction =>
		{
			var kind = (RuntimeManagedCommandKind)instruction.Value;
			if (kind == RuntimeManagedCommandKind.Stop)
				SetGlobalLifecycleState("Stop");
			DispatchUnitInstruction(unitId, instruction);
		});
	}

	public IDisposable RegisterCommandHandler(string unitId, Action<RuntimeManagedCommand> handler)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(unitId);
		ArgumentNullException.ThrowIfNull(handler);
		_commandHandlers[unitId] = handler;
		return new RuntimeCommandHandlerRegistration(_commandHandlers, unitId, handler);
	}

	internal RuntimeInstructionHandleDescriptor DuplicateInstructionHandlesToProcess(string unitId, System.Diagnostics.Process process)
	{
		if (!_unitInboxes.TryGetValue(unitId, out var inbox))
			throw new InvalidOperationException($"Managed unit inbox not found: {unitId}");
		return RuntimeInstructionHandleBootstrap.DuplicateToProcess(process, _controllerInbox, inbox.Fifo);
	}

	private void DispatchControllerInstruction(RuntimeValueInstruction instruction)
	{
		ControllerInstructionReceived?.Invoke(this, instruction);
		SignalChanged();
	}

	private void DispatchUnitInstruction(string unitId, RuntimeValueInstruction instruction)
	{
		_commandPayloads.TryRemove(instruction.Sequence, out var payload);
		var command = new RuntimeManagedCommand(
			instruction.Sequence,
			unitId,
			(RuntimeManagedCommandKind)instruction.Value,
			payload,
			DateTimeOffset.UtcNow);
		if (_commandHandlers.TryGetValue(unitId, out var handler))
			handler(command);
		else
		{
			var queue = _commandFifos.GetOrAdd(unitId, static _ => new ConcurrentQueue<RuntimeManagedCommand>());
			queue.Enqueue(command);
			Interlocked.Increment(ref _pendingCommandCount);
		}
		SignalChanged();
	}

	private int ResolveEntityKind(string unitId)
	{
		if (!_registrations.TryGetValue(unitId, out var registration))
			return (int)RuntimeInstructionEntityKind.Business;
		return registration.UnitType.ToLowerInvariant() switch
		{
			"process" => (int)RuntimeInstructionEntityKind.Process,
			"thread" => (int)RuntimeInstructionEntityKind.Thread,
			"task" => (int)RuntimeInstructionEntityKind.Task,
			_ => (int)RuntimeInstructionEntityKind.Business
		};
	}

	public RuntimeManagedCommand EnqueueControllerCommand(RuntimeManagedCommandKind kind, string? payload = null)
	{
		var sequence = Interlocked.Increment(ref _commandSequence);
		var command = new RuntimeManagedCommand(sequence, "runtime.controller", kind, payload, DateTimeOffset.UtcNow);
		_controllerCommands.Enqueue(command);
		PublishEvent("runtime.controller", "controller-command-enqueued", kind.ToString(), new { command.Sequence, command.Kind, command.Payload });
		return command;
	}

	public bool TryDequeueControllerCommand(out RuntimeManagedCommand? command)
	{
		command = null;
		if (_controllerCommands.TryDequeue(out var dequeued))
		{
			command = dequeued;
			PublishEvent("runtime.controller", "controller-command-dequeued", dequeued.Kind.ToString(), new { dequeued.Sequence, dequeued.Kind, dequeued.Payload });
			return true;
		}

		return false;
	}

	public int BroadcastCommand(RuntimeManagedCommandKind kind, string? payload = null, Func<RuntimeManagedRegistration, bool>? filter = null)
	{
		var registrations = SnapshotRegistrations();
		var count = 0;
		foreach (var registration in registrations)
		{
			if (filter != null && !filter(registration))
			{
				continue;
			}

			EnqueueCommand(registration.UnitId, kind, payload);
			count++;
		}

		PublishEvent("runtime.global", "global-command-broadcast", $"Broadcast {kind} to {count} units.", new { kind, count, payload });
		return count;
	}

	public bool TryDequeueCommand(string targetUnitId, out RuntimeManagedCommand? command)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(targetUnitId);
		ImportSharedCommands();
		ImportSharedStates();

		command = null;
		if (_commandFifos.TryGetValue(targetUnitId, out var queue) && queue.TryDequeue(out command))
		{
			Interlocked.Decrement(ref _pendingCommandCount);
			PublishEvent(targetUnitId, "command-dequeued", command.Kind.ToString(), new { command.Sequence, command.TargetUnitId, command.Kind, command.Payload });
			return true;
		}

		return false;
	}

	public RuntimeManagedEvent PublishEvent(string unitId, string kind, string message, object? payload)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(unitId);
		ArgumentException.ThrowIfNullOrWhiteSpace(kind);
		message ??= string.Empty;

		var sequence = Interlocked.Increment(ref _eventSequence);
		var evt = new RuntimeManagedEvent(sequence, unitId, kind, message, payload, DateTimeOffset.UtcNow);
		_eventFifo.Enqueue(evt);
		while (_eventFifo.Count > 4096 && _eventFifo.TryDequeue(out _)) { }
		return evt;
	}

	public IReadOnlyList<RuntimeManagedEvent> DrainEvents(int count = 200)
	{
		count = Math.Clamp(count, 1, 4096);
		var result = new List<RuntimeManagedEvent>(count);
		while (result.Count < count && _eventFifo.TryDequeue(out var evt))
		{
			result.Add(evt);
		}

		return result;
	}

	public object Snapshot(int eventCount = 100)
	{
		var shutdown = EvaluateShutdownStatus();
		return new
		{
			globalLifecycle = _globalLifecycle.Snapshot(),
			globalLifecycleHistory = SnapshotGlobalLifecycleHistory(),
			globalExitDeadlineUtc = _globalExitDeadlineUtc,
			shutdown,
			registrations = SnapshotRegistrations(),
			events = SnapshotEvents(eventCount),
			commandQueues = SnapshotCommandQueues(),
			controllerCommandCount = ControllerCommandCount,
			pendingCommands = PendingCommandCount,
			droppedCommands = DroppedCommandCount,
			commandQueueDepth = CommandQueueDepth
		};
	}

	private void ImportSharedCommands()
	{
	}

	public void ImportSharedStates()
	{
	}

	private string? ResolveUnitIdFromHash(int targetIdHash)
	{
		foreach (var key in _registrations.Keys)
		{
			if (RuntimeInjectorTransportCodec.ComputeStableHash32(key) == targetIdHash)
			{
				return key;
			}
		}

		return null;
	}

	private void AppendUnitStateHistory(string unitId, RuntimeState state)
	{
		var history = _sharedUnitStateHistory.GetOrAdd(unitId, static _ => new RuntimeDList<RuntimeState>());
		history.AddLast(state);
		TrimHistory(history);
	}

	private static void TrimHistory(RuntimeDList<RuntimeState> history)
	{
		while (history.Count > StateHistoryDepth)
		{
			var snapshot = history.ToArraySnapshot();
			if (snapshot.Count == 0)
			{
				break;
			}

			history.RemoveFirst(snapshot[0]);
		}
	}

	private void EnsureGlobalLifecycleStates()
	{
		var catalog = _globalLifecycle.Catalog;
		if (!catalog.TryGetByName("Initialize", out _))
		{
			catalog.AddRoot(1001, "Initialize", RuntimeStateGroup.Lifecycle, "Initialize");
		}

		if (!catalog.TryGetByName("Running", out _))
		{
			catalog.AddRoot(1002, "Running", RuntimeStateGroup.Lifecycle, "Running");
		}

		if (!catalog.TryGetByName("Pause", out _))
		{
			catalog.AddRoot(1003, "Pause", RuntimeStateGroup.Lifecycle, "Pause");
		}

		if (!catalog.TryGetByName("Exit", out _))
		{
			catalog.AddRoot(1004, "Exit", RuntimeStateGroup.Lifecycle, "Exit");
		}
	}

	private static string BuildPathFromCatalog(RuntimeStateCatalog catalog, RuntimeState state)
	{
		var segments = new Stack<string>();
		segments.Push(state.Name);
		var currentCode = state.ParentCode;
		while (currentCode.HasValue && catalog.TryGetByCode(currentCode.Value, out var current))
		{
			segments.Push(current.Name);
			currentCode = current.ParentCode;
		}

		return string.Join('.', segments);
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) == 1)
			return;
		foreach (var inbox in _unitInboxes.Values)
			inbox.Dispose();
		_unitInboxes.Clear();
		_controllerDispatcher.Dispose();
		_controllerInbox.Dispose();
		_externalUnitDispatcher?.Dispose();
		_externalUnitInbox?.Dispose();
		_externalControllerInbox?.Dispose();
	}

	private sealed class RuntimeCommandHandlerRegistration(
		ConcurrentDictionary<string, Action<RuntimeManagedCommand>> handlers,
		string unitId,
		Action<RuntimeManagedCommand> handler) : IDisposable
	{
		private int _disposed;

		public void Dispose()
		{
			if (Interlocked.Exchange(ref _disposed, 1) == 1)
				return;
			if (handlers.TryGetValue(unitId, out var current) && ReferenceEquals(current, handler))
				handlers.TryRemove(unitId, out _);
		}
	}

}

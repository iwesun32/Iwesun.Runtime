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

public sealed record RuntimeShutdownRequestedEventArgs(
	DateTimeOffset RequestedAtUtc,
	DateTimeOffset ExitDeadlineUtc,
	int StopBroadcastCount,
	int WakeupBroadcastCount,
	string? Payload);

public sealed class RuntimeManagedRegistry
{
	private const int StateHistoryDepth = 128;
	private const int CommandQueueDepth = 8;
	private readonly ConcurrentDictionary<string, RuntimeManagedRegistration> _registrations = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, ConcurrentQueue<RuntimeManagedCommand>> _commandFifos = new(StringComparer.OrdinalIgnoreCase);
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
	private long _importedSharedCommandCount;
	private long _publishedSharedStateCount;
	public event EventHandler<RuntimeShutdownRequestedEventArgs>? ShutdownRequested;
	public long PendingCommandCount => Volatile.Read(ref _pendingCommandCount);
	public long DroppedCommandCount => Volatile.Read(ref _droppedCommandCount);
	public long ImportedSharedCommandCount => Volatile.Read(ref _importedSharedCommandCount);
	public long PublishedSharedStateCount => Volatile.Read(ref _publishedSharedStateCount);
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
		_sharedUnitStates[unitId] = state.State;
		AppendUnitStateHistory(unitId, state.State.CurrentState);
		PublishEvent(unitId, "registered", $"{unitType} registered.", state);
		PublishSharedStateFrame(unitId, state);
		return registration;
	}

	public bool Unregister(string unitId)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(unitId);
		var removed = _registrations.TryRemove(unitId, out var registration);
		if (removed)
		{
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
		PublishSharedStateFrame(unitId, state);
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

		PublishGlobalStateFrame(state);
		return _globalLifecycle.Snapshot();
	}

	public RuntimeShutdownStatus EvaluateShutdownStatus(DateTimeOffset? nowUtc = null)
	{
		ImportSharedStates();
		var now = nowUtc ?? DateTimeOffset.UtcNow;
		var pending = SnapshotRegistrations()
			.Where(x => !IsUnitReadyToExit(x))
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
		var stopCount = BroadcastCommand(RuntimeManagedCommandKind.Stop, controlPayload);
		var wakeupCount = BroadcastCommand(RuntimeManagedCommandKind.Wakeup, controlPayload);
		ShutdownRequested?.Invoke(this, new RuntimeShutdownRequestedEventArgs(
			RequestedAtUtc: DateTimeOffset.UtcNow,
			ExitDeadlineUtc: deadline,
			StopBroadcastCount: stopCount,
			WakeupBroadcastCount: wakeupCount,
			Payload: payload));
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
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(targetUnitId);
		var sequence = Interlocked.Increment(ref _commandSequence);
		var command = new RuntimeManagedCommand(sequence, targetUnitId, kind, payload, DateTimeOffset.UtcNow);
		var queue = _commandFifos.GetOrAdd(targetUnitId, static _ => new ConcurrentQueue<RuntimeManagedCommand>());
		queue.Enqueue(command);
		Interlocked.Increment(ref _pendingCommandCount);
		while (queue.Count > CommandQueueDepth && queue.TryDequeue(out _))
		{
			Interlocked.Decrement(ref _pendingCommandCount);
			Interlocked.Increment(ref _droppedCommandCount);
		}

		var sharedFrame = new RuntimeCommandFrame(
			processId: Environment.ProcessId,
			managedThreadId: Environment.CurrentManagedThreadId,
			sequence: command.Sequence,
			targetIdHash: RuntimeInjectorTransportCodec.ComputeStableHash32(targetUnitId),
			commandKind: (int)kind,
			timestampUtcTicks: command.EnqueuedAt.UtcTicks,
			arg0: payload?.Length ?? 0,
			arg1: 0);
		DiagnosticSwitchboard.TryPutCommandFrame(sharedFrame);

		PublishEvent(targetUnitId, "command-enqueued", kind.ToString(), new { command.Sequence, command.TargetUnitId, command.Kind, command.Payload });
		return command;
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
			importedSharedCommands = ImportedSharedCommandCount,
			publishedSharedStates = PublishedSharedStateCount,
			commandQueueDepth = CommandQueueDepth
		};
	}

	private void ImportSharedCommands()
	{
		while (DiagnosticSwitchboard.TryGetCommandFrame(out var frame))
		{
			if (frame.ProcessId == Environment.ProcessId)
			{
				continue;
			}

			var targetUnitId = ResolveUnitIdFromHash(frame.TargetIdHash);
			if (targetUnitId == null)
			{
				continue;
			}

			if (!Enum.IsDefined(typeof(RuntimeManagedCommandKind), frame.CommandKind))
			{
				continue;
			}

			var queue = _commandFifos.GetOrAdd(targetUnitId, static _ => new ConcurrentQueue<RuntimeManagedCommand>());
			var sequence = frame.Sequence > 0 ? frame.Sequence : Interlocked.Increment(ref _commandSequence);
			var imported = new RuntimeManagedCommand(
				sequence,
				targetUnitId,
				(RuntimeManagedCommandKind)frame.CommandKind,
				Payload: null,
				EnqueuedAt: new DateTimeOffset(frame.TimestampUtcTicks, TimeSpan.Zero));
			queue.Enqueue(imported);
			Interlocked.Increment(ref _pendingCommandCount);
			Interlocked.Increment(ref _importedSharedCommandCount);
			while (queue.Count > CommandQueueDepth && queue.TryDequeue(out _))
			{
				Interlocked.Decrement(ref _pendingCommandCount);
				Interlocked.Increment(ref _droppedCommandCount);
			}
		}
	}

	public void ImportSharedStates()
	{
		EnsureGlobalLifecycleStates();
		while (DiagnosticSwitchboard.TryGetStateFrame(out var frame))
		{
			if (frame.ProcessId == Environment.ProcessId)
			{
				continue;
			}

			var timestamp = new DateTimeOffset(frame.TimestampUtcTicks, TimeSpan.Zero);
			if (frame.EntityKind == 0)
			{
				if (_globalLifecycle.Catalog.TryGetByCode(frame.StateKind, out var globalState))
				{
					_globalLifecycle.SetCurrentByCode(globalState.Code);
					_globalLifecycleHistory.AddLast(globalState);
					TrimHistory(_globalLifecycleHistory);
					PublishEvent("runtime.global", "global-state-imported", $"Imported global state {globalState.Name}.", new
					{
						globalState.Code,
						globalState.Name,
						frame.ProcessId,
						timestamp
					});
				}

				continue;
			}

			var targetUnitId = ResolveUnitIdFromHash(frame.EntityIdHash);
			if (targetUnitId == null)
			{
				continue;
			}

			if (!_registrations.TryGetValue(targetUnitId, out var registration))
			{
				continue;
			}

			if (!_globalLifecycle.Catalog.TryGetByCode(frame.StateKind, out var unitState))
			{
				continue;
			}

			var importedStateSnapshot = registration.State.State with
			{
				CurrentState = unitState,
				CurrentPath = BuildPathFromCatalog(_globalLifecycle.Catalog, unitState),
				UpdatedAt = timestamp
			};
			_sharedUnitStates[targetUnitId] = importedStateSnapshot;
			AppendUnitStateHistory(targetUnitId, unitState);
		}
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

	private void PublishSharedStateFrame(string unitId, RManagedStateSnapshot state)
	{
		var frame = new RuntimeStateFrame(
			processId: Environment.ProcessId,
			managedThreadId: Environment.CurrentManagedThreadId,
			sequence: Interlocked.Increment(ref _eventSequence),
			entityKind: 1,
			entityIdHash: RuntimeInjectorTransportCodec.ComputeStableHash32(unitId),
			stateKind: state.State.CurrentState.Code,
			timestampUtcTicks: DateTimeOffset.UtcNow.UtcTicks);
		if (DiagnosticSwitchboard.TryPutStateFrame(frame))
		{
			Interlocked.Increment(ref _publishedSharedStateCount);
		}
	}

	private void PublishGlobalStateFrame(RuntimeState state)
	{
		var frame = new RuntimeStateFrame(
			processId: Environment.ProcessId,
			managedThreadId: Environment.CurrentManagedThreadId,
			sequence: Interlocked.Increment(ref _eventSequence),
			entityKind: 0,
			entityIdHash: 0,
			stateKind: state.Code,
			timestampUtcTicks: DateTimeOffset.UtcNow.UtcTicks);
		if (DiagnosticSwitchboard.TryPutStateFrame(frame))
		{
			Interlocked.Increment(ref _publishedSharedStateCount);
		}
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

	private static bool IsUnitReadyToExit(RuntimeManagedRegistration registration)
	{
		var name = registration.State.State.CurrentState.Name;
		if (name.Equals("Stop", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("Exit", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("Completed", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("Timeout", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		return false;
	}
}

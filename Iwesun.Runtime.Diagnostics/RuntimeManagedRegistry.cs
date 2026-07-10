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

public sealed class RuntimeManagedRegistry
{
	private const int CommandQueueDepth = 8;
	private readonly ConcurrentDictionary<string, RuntimeManagedRegistration> _registrations = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, ConcurrentQueue<RuntimeManagedCommand>> _commandFifos = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentQueue<RuntimeManagedEvent> _eventFifo = new();
	private long _commandSequence;
	private long _eventSequence;
	private long _pendingCommandCount;
	private long _droppedCommandCount;
	private long _importedSharedCommandCount;
	private long _publishedSharedStateCount;
	public long PendingCommandCount => Volatile.Read(ref _pendingCommandCount);
	public long DroppedCommandCount => Volatile.Read(ref _droppedCommandCount);
	public long ImportedSharedCommandCount => Volatile.Read(ref _importedSharedCommandCount);
	public long PublishedSharedStateCount => Volatile.Read(ref _publishedSharedStateCount);

	public RuntimeManagedRegistration Register(string unitId, string unitType, string ownership, RManagedStateSnapshot state)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(unitId);
		ArgumentException.ThrowIfNullOrWhiteSpace(unitType);
		ArgumentException.ThrowIfNullOrWhiteSpace(ownership);

		var now = DateTimeOffset.UtcNow;
		var registration = new RuntimeManagedRegistration(unitId, unitType, ownership, now, now, state);
		_registrations[unitId] = registration;
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
		PublishEvent(unitId, "state-updated", "Managed state updated.", new { state.State.CurrentState.Name, state.State.CurrentPath });
		PublishSharedStateFrame(unitId, state);
		return true;
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

	public bool TryDequeueCommand(string targetUnitId, out RuntimeManagedCommand? command)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(targetUnitId);
		ImportSharedCommands();

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
		return new
		{
			registrations = SnapshotRegistrations(),
			events = SnapshotEvents(eventCount),
			commandQueues = SnapshotCommandQueues(),
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
			stateKind: RuntimeInjectorTransportCodec.ComputeStableHash32(state.State.CurrentState.Name),
			timestampUtcTicks: DateTimeOffset.UtcNow.UtcTicks);
		if (DiagnosticSwitchboard.TryPutStateFrame(frame))
		{
			Interlocked.Increment(ref _publishedSharedStateCount);
		}
	}
}

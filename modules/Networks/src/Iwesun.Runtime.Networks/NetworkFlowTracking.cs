using System.Collections.Concurrent;

namespace Iwesun.Runtime.Networks;

/// <summary>Four-byte process-local serial used by the active network data plane.</summary>
public readonly record struct NetworkFlowSerial
{
	internal NetworkFlowSerial(uint value) => Value = value;

	public uint Value { get; }
	public bool IsValid => Value != 0;

	public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public enum NetworkFlowLifecycleState : byte
{
	Unspecified = 0,
	Allocated = 1,
	Binding = 2,
	Active = 3,
	Draining = 4,
	Failed = 5,
	Quarantine = 6,
	Released = 7,
}

public readonly record struct NetworkFlowSnapshot(
	NetworkFlowSerial Serial,
	NetworkFlowLifecycleState State,
	NetworkExecutionIdentity Identity,
	NetworkFlowTransportBinding Transport,
	long AllocatedAtUnixMs,
	long QuarantineUntilUnixMs)
{
	public bool IsValid => Serial.IsValid &&
		State is not NetworkFlowLifecycleState.Unspecified and not NetworkFlowLifecycleState.Released;
}

public readonly record struct NetworkFlowTransportBinding(
	uint SocketGeneration,
	IpAddressValue LocalAddress,
	ushort LocalPort,
	IpAddressValue RemoteAddress,
	ushort RemotePort,
	NetworkDatagramCorrelationKind CorrelationKind)
{
	public bool IsValid => SocketGeneration > 0 && LocalAddress.Family is 4 or 6 && LocalPort > 0 &&
		RemoteAddress.Family is 4 or 6 && RemotePort > 0 &&
		CorrelationKind != NetworkDatagramCorrelationKind.Unspecified;
}

public sealed class NetworkFlowCapacityException : InvalidOperationException
{
	public NetworkFlowCapacityException()
		: base("network-flow-serial-capacity-exhausted")
	{
	}
}

/// <summary>
/// Owns the single non-generic flow serial namespace for the current Networks process.
/// A serial remains reserved through its late-response quarantine window.
/// </summary>
public static class NetworkFlowSerialAllocator
{
	private static readonly ConcurrentDictionary<uint, FlowRegistration> Registrations = new();
	private static readonly object QuarantineGate = new();
	private static readonly PriorityQueue<QuarantineRegistration, long> Quarantines = new();
	private static int _cursor;

	public static int ReservedCount
	{
		get
		{
			RemoveExpiredQuarantines();
			return Registrations.Count;
		}
	}

	public static NetworkFlowSerial GetNext()
	{
		if (TryGetNext(out var serial)) return serial;
		throw new NetworkFlowCapacityException();
	}

	public static bool TryGetNext(out NetworkFlowSerial serial)
	{
		RemoveExpiredQuarantines();
		while (Registrations.Count < int.MaxValue)
		{
			var candidate = unchecked((uint)Interlocked.Increment(ref _cursor));
			if (candidate == 0) continue;
			var registration = new FlowRegistration(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
			if (!Registrations.TryAdd(candidate, registration)) continue;
			serial = new NetworkFlowSerial(candidate);
			return true;
		}

		serial = default;
		return false;
	}

	public static bool TryGet(NetworkFlowSerial serial, out NetworkFlowSnapshot snapshot)
	{
		if (!serial.IsValid || !Registrations.TryGetValue(serial.Value, out var registration) ||
			TryReleaseExpired(serial.Value, registration))
		{
			snapshot = default;
			return false;
		}

		snapshot = registration.CreateSnapshot(serial);
		return snapshot.IsValid;
	}

	public static bool TryTransition(NetworkFlowSerial serial, NetworkFlowLifecycleState next)
	{
		if (!serial.IsValid || next is NetworkFlowLifecycleState.Unspecified or
			NetworkFlowLifecycleState.Allocated or NetworkFlowLifecycleState.Released ||
			!Registrations.TryGetValue(serial.Value, out var registration))
		{
			return false;
		}

		return registration.TryTransition(next);
	}

	public static bool TryBindIdentity(NetworkFlowSerial serial, NetworkExecutionIdentity identity) =>
		serial.IsValid && identity.HasBranch &&
		Registrations.TryGetValue(serial.Value, out var registration) && registration.TryBindIdentity(identity);

	public static bool TryBindTransport(NetworkFlowSerial serial, NetworkFlowTransportBinding transport) =>
		serial.IsValid && transport.IsValid &&
		Registrations.TryGetValue(serial.Value, out var registration) && registration.TryBindTransport(transport);

	public static bool BeginQuarantine(NetworkFlowSerial serial, TimeSpan duration)
	{
		if (!serial.IsValid || duration < TimeSpan.Zero ||
			!Registrations.TryGetValue(serial.Value, out var registration))
		{
			return false;
		}

		var until = DateTimeOffset.UtcNow.Add(duration).ToUnixTimeMilliseconds();
		if (!registration.TryBeginQuarantine(until)) return false;
		if (duration == TimeSpan.Zero)
		{
			TryReleaseExpired(serial.Value, registration);
			return true;
		}
		lock (QuarantineGate)
		{
			Quarantines.Enqueue(new QuarantineRegistration(serial.Value, registration), until);
		}
		RemoveExpiredQuarantines();
		return true;
	}

	internal static void ResetForTests(int cursor = 0)
	{
		Registrations.Clear();
		lock (QuarantineGate) Quarantines.Clear();
		Volatile.Write(ref _cursor, cursor);
	}

	private static void RemoveExpiredQuarantines()
	{
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		lock (QuarantineGate)
		{
			while (Quarantines.TryPeek(out var item, out var deadline) && deadline <= now)
			{
				Quarantines.Dequeue();
				TryReleaseExpired(item.Value, item.Registration, now);
			}
		}
	}

	private static bool TryReleaseExpired(
		uint value,
		FlowRegistration registration,
		long? observedNowUnixMs = null)
	{
		if (registration.State != NetworkFlowLifecycleState.Quarantine ||
			registration.QuarantineUntilUnixMs > (observedNowUnixMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()))
		{
			return false;
		}

		if (((ICollection<KeyValuePair<uint, FlowRegistration>>)Registrations)
			.Remove(new KeyValuePair<uint, FlowRegistration>(value, registration)))
		{
			registration.MarkReleased();
		}
		return true;
	}

	private readonly record struct QuarantineRegistration(uint Value, FlowRegistration Registration);

	private sealed class FlowRegistration
	{
		private int _state = (int)NetworkFlowLifecycleState.Allocated;
		private long _quarantineUntilUnixMs;
		private readonly object _bindingGate = new();
		private NetworkExecutionIdentity _identity;
		private NetworkFlowTransportBinding _transport;

		public FlowRegistration(long allocatedAtUnixMs) => AllocatedAtUnixMs = allocatedAtUnixMs;

		public long AllocatedAtUnixMs { get; }
		public NetworkFlowLifecycleState State => (NetworkFlowLifecycleState)Volatile.Read(ref _state);
		public long QuarantineUntilUnixMs => Volatile.Read(ref _quarantineUntilUnixMs);

		public NetworkFlowSnapshot CreateSnapshot(NetworkFlowSerial serial)
		{
			lock (_bindingGate)
			{
				return new NetworkFlowSnapshot(
					serial,
					State,
					_identity,
					_transport,
					AllocatedAtUnixMs,
					QuarantineUntilUnixMs);
			}
		}

		public bool TryBindIdentity(NetworkExecutionIdentity identity)
		{
			lock (_bindingGate)
			{
				if (_identity.HasBranch) return _identity == identity;
				_identity = identity;
				return true;
			}
		}

		public bool TryBindTransport(NetworkFlowTransportBinding transport)
		{
			lock (_bindingGate)
			{
				if (_transport.IsValid) return _transport == transport;
				_transport = transport;
				return true;
			}
		}

		public bool TryTransition(NetworkFlowLifecycleState next)
		{
			while (true)
			{
				var current = State;
				if (!CanTransition(current, next)) return false;
				if (Interlocked.CompareExchange(ref _state, (int)next, (int)current) == (int)current)
					return true;
			}
		}

		public bool TryBeginQuarantine(long untilUnixMs)
		{
			lock (_bindingGate)
			{
				var current = State;
				if (current is NetworkFlowLifecycleState.Released or NetworkFlowLifecycleState.Quarantine)
					return false;

				// Publish the deadline before the Quarantine state. Readers use the state as the
				// publication flag and must never observe Quarantine with a zero/old deadline.
				Volatile.Write(ref _quarantineUntilUnixMs, untilUnixMs);
				if (Interlocked.CompareExchange(
						ref _state,
						(int)NetworkFlowLifecycleState.Quarantine,
						(int)current) == (int)current)
					return true;

				return false;
			}
		}

		public void MarkReleased() => Volatile.Write(ref _state, (int)NetworkFlowLifecycleState.Released);

		private static bool CanTransition(NetworkFlowLifecycleState current, NetworkFlowLifecycleState next) =>
			(current, next) switch
			{
				(NetworkFlowLifecycleState.Allocated, NetworkFlowLifecycleState.Binding or
					NetworkFlowLifecycleState.Active or NetworkFlowLifecycleState.Failed) => true,
				(NetworkFlowLifecycleState.Binding, NetworkFlowLifecycleState.Active or
					NetworkFlowLifecycleState.Failed) => true,
				(NetworkFlowLifecycleState.Active, NetworkFlowLifecycleState.Draining or
					NetworkFlowLifecycleState.Failed) => true,
				(NetworkFlowLifecycleState.Draining, NetworkFlowLifecycleState.Failed) => true,
				_ => false,
			};
	}
}

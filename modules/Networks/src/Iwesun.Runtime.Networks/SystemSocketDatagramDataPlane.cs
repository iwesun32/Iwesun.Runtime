using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Iwesun.Runtime.Networks;

/// <summary>Long-lived System.Socket UDP data plane with one tokenless flow per socket slot.</summary>
public sealed class SystemSocketDatagramDataPlane : INetworkDatagramDataPlane
{
	private static int _nextDataPlaneGeneration;
	private readonly NetworkSocketExecutor _socketExecutor;
	private readonly ConcurrentDictionary<DatagramPoolKey, DatagramPool> _pools = new();
	private readonly object _poolGate = new();
	private readonly TimeSpan _lateResponseQuarantine;
	private readonly TimeSpan _idlePoolRetention;
	private readonly int _maxSocketsPerPool;
	private readonly int _maxPoolCount;
	private readonly int _poolSweepInterval;
	private readonly NetworkDataPlaneIdentity _identity;
	private long _unexpectedRemoteDatagrams;
	private long _unmatchedDatagrams;
	private long _lateDatagrams;
	private long _capacityRejections;
	private int _rentCount;
	private int _disposed;

	public SystemSocketDatagramDataPlane(
		NetworkSocketExecutor socketExecutor,
		TimeSpan? lateResponseQuarantine = null,
		int maxSocketsPerPool = 64,
		int maxPoolCount = 4096,
		TimeSpan? idlePoolRetention = null,
		int poolSweepInterval = 64)
	{
		ArgumentNullException.ThrowIfNull(socketExecutor);
		if (lateResponseQuarantine < TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(nameof(lateResponseQuarantine));
		if (idlePoolRetention < TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(nameof(idlePoolRetention));
		if (maxSocketsPerPool <= 0) throw new ArgumentOutOfRangeException(nameof(maxSocketsPerPool));
		if (maxPoolCount <= 0) throw new ArgumentOutOfRangeException(nameof(maxPoolCount));
		if (poolSweepInterval <= 0) throw new ArgumentOutOfRangeException(nameof(poolSweepInterval));
		_socketExecutor = socketExecutor;
		_lateResponseQuarantine = lateResponseQuarantine ?? TimeSpan.FromSeconds(1);
		_idlePoolRetention = idlePoolRetention ?? TimeSpan.FromMinutes(2);
		_maxSocketsPerPool = maxSocketsPerPool;
		_maxPoolCount = maxPoolCount;
		_poolSweepInterval = poolSweepInterval;
		_identity = new NetworkDataPlaneIdentity(
			"system-socket-datagram",
			1,
			NextNonZero(ref _nextDataPlaneGeneration));
	}

	public event Action<NetworkDatagramDispatchEvidence>? DatagramDispatched;

	public NetworkDataPlaneDescriptor Descriptor => new(
		_identity,
		NetworkDataPlaneCapabilities.Datagram,
		Volatile.Read(ref _disposed) == 0 ? NetworkDataPlaneState.Ready : NetworkDataPlaneState.Stopped);

	public NetworkDatagramDataPlaneCounters Counters
	{
		get
		{
			var socketSlots = 0;
			var activeSlots = 0;
			var quarantinedSlots = 0;
			var peakSlotsPerPool = 0;
			foreach (var pool in _pools.Values)
			{
				var metrics = pool.GetMetrics();
				socketSlots += metrics.SocketSlots;
				activeSlots += metrics.ActiveSlots;
				quarantinedSlots += metrics.QuarantinedSlots;
				peakSlotsPerPool = Math.Max(peakSlotsPerPool, metrics.PeakSlots);
			}
			return new NetworkDatagramDataPlaneCounters(
				Interlocked.Read(ref _unexpectedRemoteDatagrams),
				Interlocked.Read(ref _unmatchedDatagrams),
				Interlocked.Read(ref _lateDatagrams),
				Interlocked.Read(ref _capacityRejections),
				_pools.Count,
				socketSlots,
				activeSlots,
				quarantinedSlots,
				peakSlotsPerPool);
		}
	}

	public async ValueTask<NetworkSocketExecutionResult> ExecuteDatagramAsync(
		NetworkSocketExecutionRequest request,
		CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
		if (request.Operation != NetworkSocketOperation.UdpExchange)
		{
			return CreateFailure(request, default, 0, ProtocolOutcome.Rejected, NetworkAccessFailureCodes.SocketRequestInvalid);
		}
		if (!_socketExecutor.TryValidateRequest(request, out var validationFailure))
		{
			return CreateFailure(
				request, default, 0, ProtocolOutcome.Rejected, validationFailure.ReasonCode,
				validationFailure.PlatformError.IsValid
					? new BinaryNetworkError(
						3,
						validationFailure.PlatformError.NativeCode,
						validationFailure.PlatformError.HResult)
					: default);
		}

		var flowSerial = NetworkFlowSerialAllocator.GetNext();
		NetworkFlowSerialAllocator.TryBindIdentity(flowSerial, request.Identity);
		NetworkFlowSerialAllocator.TryTransition(flowSerial, NetworkFlowLifecycleState.Binding);
		DatagramSlotReservation reservation = default;
		try
		{
			var key = new DatagramPoolKey(request.Resolved.StableKey, request.RemotePort);
			reservation = RentSlot(key, request, flowSerial);
			NetworkFlowSerialAllocator.TryTransition(flowSerial, NetworkFlowLifecycleState.Active);

			using var policy = _socketExecutor.AcquireWfpPolicyIfRequired(
				reservation.Socket,
				request,
				ProtocolType.Udp,
				out var policyFailure);
			if (!string.IsNullOrEmpty(policyFailure.ReasonCode))
			{
				NetworkFlowSerialAllocator.TryTransition(flowSerial, NetworkFlowLifecycleState.Failed);
				return CreateFailure(
					request,
					flowSerial,
					reservation.SocketGeneration,
					ProtocolOutcome.Rejected,
					policyFailure.ReasonCode,
					new BinaryNetworkError(
						3,
						policyFailure.PlatformError.NativeCode,
						policyFailure.PlatformError.HResult));
			}

			var remote = new IPEndPoint(
				NetworkIpAddressInterop.ToSystemAddress(
					request.Resolved.Destination,
					request.Resolved.Interface.InterfaceIndex),
				request.RemotePort);
			await reservation.EnsureConnectedAsync(remote, cancellationToken).ConfigureAwait(false);
			await reservation.Socket.SendAsync(
				request.Payload,
				SocketFlags.None,
				cancellationToken).ConfigureAwait(false);
			var dispatched = reservation.CreateDispatchEvidence(request, remote);
			NetworkFlowSerialAllocator.TryBindTransport(flowSerial, new NetworkFlowTransportBinding(
				dispatched.SocketGeneration,
				dispatched.ActualLocalAddress,
				dispatched.ActualLocalPort,
				dispatched.ExpectedRemoteAddress,
				dispatched.ExpectedRemotePort,
				NetworkDatagramCorrelationKind.ConnectedSocket));
			PublishDispatched(dispatched);

			var received = await reservation.WaitForResponseAsync(cancellationToken).ConfigureAwait(false);
			var result = NetworkSocketExecutor.CreateSuccessResult(
				request,
				reservation.Socket,
				received.Payload,
				ProofKind.Observed,
				received.InterfaceIndex,
				received.RemoteEndPoint,
				policyEnforced: policy is not null);
			return result with
			{
				FlowSerial = flowSerial,
				SocketGeneration = reservation.SocketGeneration,
				DispatchEvidence = dispatched,
			};
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			return CreateFailure(
				request,
				flowSerial,
				reservation.SocketGeneration,
				ProtocolOutcome.Cancelled,
				"socket-operation-cancelled",
				BinaryNetworkError.FromException(null, true, false));
		}
		catch (NetworkFlowCapacityException)
		{
			Interlocked.Increment(ref _capacityRejections);
			NetworkFlowSerialAllocator.TryTransition(flowSerial, NetworkFlowLifecycleState.Failed);
			return CreateFailure(
				request,
				flowSerial,
				reservation.SocketGeneration,
				ProtocolOutcome.Rejected,
				NetworkAccessFailureCodes.NetworkFlowCapacityReached);
		}
		catch (Exception ex)
		{
			NetworkFlowSerialAllocator.TryTransition(flowSerial, NetworkFlowLifecycleState.Failed);
			return CreateFailure(
				request,
				flowSerial,
				reservation.SocketGeneration,
				ProtocolOutcome.TransportFailed,
				NetworkAccessFailureCodes.SocketOperationFailed,
				BinaryNetworkError.FromException(ex, false, false));
		}
		finally
		{
			if (reservation.IsValid) reservation.Dispose();
			NetworkFlowSerialAllocator.TryTransition(flowSerial, NetworkFlowLifecycleState.Draining);
			NetworkFlowSerialAllocator.BeginQuarantine(flowSerial, _lateResponseQuarantine);
		}
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
		lock (_poolGate)
		{
			foreach (var pool in _pools.Values) pool.Dispose();
			_pools.Clear();
		}
	}

	private DatagramSlotReservation RentSlot(
		DatagramPoolKey key,
		NetworkSocketExecutionRequest request,
		NetworkFlowSerial flowSerial)
	{
		lock (_poolGate)
		{
			ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
			if (Interlocked.Increment(ref _rentCount) % _poolSweepInterval == 0)
				SweepIdlePools(forceAvailable: false);

			if (!_pools.TryGetValue(key, out var pool))
			{
				if (_pools.Count >= _maxPoolCount)
					SweepIdlePools(forceAvailable: true);
				if (_pools.Count >= _maxPoolCount)
					throw new NetworkFlowCapacityException();
				pool = new DatagramPool(
					_socketExecutor,
					_lateResponseQuarantine,
					_maxSocketsPerPool,
					CountDroppedDatagram);
				if (!_pools.TryAdd(key, pool))
				{
					pool.Dispose();
					pool = _pools[key];
				}
			}
			return pool.Rent(request, flowSerial);
		}
	}

	private void SweepIdlePools(bool forceAvailable)
	{
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var minimumIdleMs = forceAvailable ? 0 : (long)_idlePoolRetention.TotalMilliseconds;
		foreach (var pair in _pools)
		{
			if (!pair.Value.CanRetire(now, minimumIdleMs)) continue;
			if (!_pools.TryRemove(pair.Key, out var removed)) continue;
			removed.Dispose();
		}
	}

	private void PublishDispatched(NetworkDatagramDispatchEvidence evidence)
	{
		var handlers = DatagramDispatched;
		if (handlers is null) return;
		foreach (Action<NetworkDatagramDispatchEvidence> handler in handlers.GetInvocationList())
		{
			try
			{
				handler(evidence);
			}
			catch
			{
				// Observer failures must not change network execution.
			}
		}
	}

	private void CountDroppedDatagram(NetworkDatagramDropKind kind)
	{
		switch (kind)
		{
			case NetworkDatagramDropKind.UnexpectedRemote:
				Interlocked.Increment(ref _unexpectedRemoteDatagrams);
				break;
			case NetworkDatagramDropKind.Unmatched:
				Interlocked.Increment(ref _unmatchedDatagrams);
				break;
			case NetworkDatagramDropKind.Late:
				Interlocked.Increment(ref _lateDatagrams);
				break;
		}
	}

	private static uint NextNonZero(ref int counter)
	{
		while (true)
		{
			var value = unchecked((uint)Interlocked.Increment(ref counter));
			if (value != 0) return value;
		}
	}

	private static NetworkSocketExecutionResult CreateFailure(
		NetworkSocketExecutionRequest request,
		NetworkFlowSerial flowSerial,
		uint socketGeneration,
		ProtocolOutcome outcome,
		string reason,
		BinaryNetworkError error = default) => new(
		request.Identity,
		outcome,
		AccessCompliance.EvidenceIncomplete,
		default,
		error,
		reason,
		[],
		DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
		flowSerial,
		socketGeneration,
		default);

	private readonly record struct DatagramPoolKey(NetworkAccessStableKey StableKey, ushort RemotePort);

	private sealed class DatagramPool : IDisposable
	{
		private static int _nextSocketGeneration;
		private readonly NetworkSocketExecutor _socketExecutor;
		private readonly TimeSpan _quarantine;
		private readonly int _maximumSlots;
		private readonly Action<NetworkDatagramDropKind> _dropObserver;
		private readonly object _gate = new();
		private readonly List<DatagramSlot> _slots = [];
		private int _peakSlots;
		private long _lastUsedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		private bool _disposed;

		public DatagramPool(
			NetworkSocketExecutor socketExecutor,
			TimeSpan quarantine,
			int maximumSlots,
			Action<NetworkDatagramDropKind> dropObserver)
		{
			_socketExecutor = socketExecutor;
			_quarantine = quarantine;
			_maximumSlots = maximumSlots;
			_dropObserver = dropObserver;
		}

		public DatagramSlotReservation Rent(
			NetworkSocketExecutionRequest request,
			NetworkFlowSerial flowSerial)
		{
			lock (_gate)
			{
				ObjectDisposedException.ThrowIf(_disposed, this);
				_lastUsedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
				foreach (var slot in _slots)
				{
					if (slot.TryReserve(request, flowSerial, out var existing)) return existing;
				}
				if (_slots.Count >= _maximumSlots) throw new NetworkFlowCapacityException();

				var created = new DatagramSlot(
					_socketExecutor,
					request,
					NextNonZero(ref _nextSocketGeneration),
					_quarantine,
					_dropObserver,
					Retire);
				_slots.Add(created);
				_peakSlots = Math.Max(_peakSlots, _slots.Count);
				if (!created.TryReserve(request, flowSerial, out var reservation))
					throw new InvalidOperationException("udp-datagram-slot-reservation-failed");
				return reservation;
			}
		}

		public bool CanRetire(long nowUnixMs, long minimumIdleMs)
		{
			lock (_gate)
			{
				if (_disposed || nowUnixMs - _lastUsedUnixMs < minimumIdleMs) return false;
				return _slots.All(slot => slot.GetUsage(nowUnixMs) == DatagramSlotUsage.Available);
			}
		}

		public DatagramPoolMetrics GetMetrics()
		{
			lock (_gate)
			{
				var active = 0;
				var quarantined = 0;
				var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
				foreach (var slot in _slots)
				{
					var usage = slot.GetUsage(now);
					if (usage == DatagramSlotUsage.Active) active++;
					else if (usage == DatagramSlotUsage.Quarantined) quarantined++;
				}
				return new DatagramPoolMetrics(_slots.Count, active, quarantined, _peakSlots);
			}
		}

		public void Dispose()
		{
			lock (_gate)
			{
				if (_disposed) return;
				_disposed = true;
				foreach (var slot in _slots) slot.Dispose();
				_slots.Clear();
			}
		}

		private void Retire(DatagramSlot slot)
		{
			lock (_gate)
			{
				if (!_slots.Remove(slot)) return;
			}
			slot.Dispose();
		}
	}

	private sealed class DatagramSlot : IDisposable
	{
		private readonly object _gate = new();
		private readonly CancellationTokenSource _lifetime = new();
		private readonly TimeSpan _quarantine;
		private readonly Action<NetworkDatagramDropKind> _dropObserver;
		private readonly Action<DatagramSlot> _retire;
		private readonly bool _retireOnRelease;
		private readonly bool _packetInformationEnabled;
		private Task? _receiveLoop;
		private IPEndPoint? _connectedRemote;
		private PendingDatagram? _pending;
		private long _availableAtUnixMs;
		private bool _receiveLoopFailed;
		private bool _disposed;

		public DatagramSlot(
			NetworkSocketExecutor socketExecutor,
			NetworkSocketExecutionRequest request,
			uint generation,
			TimeSpan quarantine,
			Action<NetworkDatagramDropKind> dropObserver,
			Action<DatagramSlot> retire)
		{
			_quarantine = quarantine;
			_dropObserver = dropObserver;
			_retire = retire;
			_retireOnRelease = NetworkSocketExecutor.RequiresWfpPolicy(request);
			SocketGeneration = generation;
			Socket = NetworkSocketExecutor.CreateSocket(
				request.Resolved.Destination,
				SocketType.Dgram,
				ProtocolType.Udp);
			NetworkSocketExecutor.ApplyPlan(Socket, request);
			if (Socket.LocalEndPoint is null)
			{
				Socket.Bind(new IPEndPoint(
					request.Resolved.Destination.IsIPv4 ? IPAddress.Any : IPAddress.IPv6Any,
					0));
			}
			_packetInformationEnabled = NetworkSocketExecutor.TryEnablePacketInformation(
				Socket,
				request.Resolved.Destination);
		}

		public Socket Socket { get; }
		public uint SocketGeneration { get; }

		public DatagramSlotUsage GetUsage(long nowUnixMs)
		{
			lock (_gate)
			{
				if (_pending is not null) return DatagramSlotUsage.Active;
				return nowUnixMs < _availableAtUnixMs
					? DatagramSlotUsage.Quarantined
					: DatagramSlotUsage.Available;
			}
		}

		public async ValueTask EnsureConnectedAsync(IPEndPoint remote, CancellationToken cancellationToken)
		{
			if (_connectedRemote is not null)
			{
				if (!NetworkSocketExecutor.IsExpectedUdpRemote(_connectedRemote, remote))
					throw new InvalidOperationException("udp-datagram-slot-remote-mismatch");
				return;
			}

			await Socket.ConnectAsync(remote, cancellationToken).ConfigureAwait(false);
			_connectedRemote = remote;
			_receiveLoop = ReceiveLoopAsync(_lifetime.Token);
		}

		public bool TryReserve(
			NetworkSocketExecutionRequest request,
			NetworkFlowSerial flowSerial,
			out DatagramSlotReservation reservation)
		{
			lock (_gate)
			{
				if (_disposed || _pending is not null ||
					DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < _availableAtUnixMs)
				{
					reservation = default;
					return false;
				}

				var expected = new IPEndPoint(
					NetworkIpAddressInterop.ToSystemAddress(
						request.Resolved.Destination,
						request.Resolved.Interface.InterfaceIndex),
					request.RemotePort);
				var pending = new PendingDatagram(flowSerial, expected, request.ReceiveBufferSize);
				_pending = pending;
				reservation = new DatagramSlotReservation(this, pending);
				return true;
			}
		}

		public void Release(PendingDatagram pending)
		{
			var retire = false;
			lock (_gate)
			{
				if (!ReferenceEquals(_pending, pending)) return;
				_pending = null;
				_availableAtUnixMs = DateTimeOffset.UtcNow.Add(_quarantine).ToUnixTimeMilliseconds();
				retire = _retireOnRelease || _receiveLoopFailed;
			}
			if (retire) _retire(this);
		}

		public void Dispose()
		{
			PendingDatagram? pending;
			lock (_gate)
			{
				if (_disposed) return;
				_disposed = true;
				pending = _pending;
				_pending = null;
			}
			_lifetime.Cancel();
			pending?.Completion.TrySetCanceled();
			Socket.Dispose();
			try
			{
				_receiveLoop?.Wait(TimeSpan.FromSeconds(1));
			}
			catch (AggregateException aggregate) when (
				aggregate.InnerExceptions.All(static error =>
					error is OperationCanceledException or ObjectDisposedException))
			{
			}
			_lifetime.Dispose();
		}

		private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
		{
			var buffer = new byte[ushort.MaxValue];
			while (!cancellationToken.IsCancellationRequested)
			{
				try
				{
					EndPoint sender = Socket.AddressFamily == AddressFamily.InterNetwork
						? new IPEndPoint(IPAddress.Any, 0)
						: new IPEndPoint(IPAddress.IPv6Any, 0);
					IPEndPoint actualRemote;
					int receivedBytes;
					uint interfaceIndex = 0;
					if (_packetInformationEnabled)
					{
						var received = await Socket.ReceiveMessageFromAsync(
							buffer,
							SocketFlags.None,
							sender,
							cancellationToken).ConfigureAwait(false);
						actualRemote = (IPEndPoint)received.RemoteEndPoint;
						receivedBytes = received.ReceivedBytes;
						if (received.PacketInformation.Interface > 0)
							interfaceIndex = checked((uint)received.PacketInformation.Interface);
					}
					else
					{
						var received = await Socket.ReceiveFromAsync(
							buffer,
							SocketFlags.None,
							sender,
							cancellationToken).ConfigureAwait(false);
						actualRemote = (IPEndPoint)received.RemoteEndPoint;
						receivedBytes = received.ReceivedBytes;
					}

					PendingDatagram? pending;
					lock (_gate) pending = _pending;
					if (pending is null)
					{
						_dropObserver(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() <
							Volatile.Read(ref _availableAtUnixMs)
							? NetworkDatagramDropKind.Late
							: NetworkDatagramDropKind.Unmatched);
						continue;
					}
					if (!NetworkSocketExecutor.IsExpectedUdpRemote(actualRemote, pending.ExpectedRemote))
					{
						_dropObserver(NetworkDatagramDropKind.UnexpectedRemote);
						continue;
					}

					var length = Math.Min(receivedBytes, pending.ReceiveBufferSize);
					if (!pending.Completion.TrySetResult(new ReceivedDatagram(
						buffer.AsSpan(0, length).ToArray(),
						actualRemote,
						interfaceIndex)))
					{
						_dropObserver(NetworkDatagramDropKind.Late);
					}
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					return;
				}
				catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
				{
					return;
				}
				catch (Exception ex)
				{
					PendingDatagram? pending;
					lock (_gate)
					{
						_receiveLoopFailed = true;
						pending = _pending;
					}
					pending?.Completion.TrySetException(ex);
					return;
				}
			}
		}
	}

	private sealed class PendingDatagram
	{
		public PendingDatagram(NetworkFlowSerial flowSerial, IPEndPoint expectedRemote, int receiveBufferSize)
		{
			FlowSerial = flowSerial;
			ExpectedRemote = expectedRemote;
			ReceiveBufferSize = receiveBufferSize;
		}

		public NetworkFlowSerial FlowSerial { get; }
		public IPEndPoint ExpectedRemote { get; }
		public int ReceiveBufferSize { get; }
		public TaskCompletionSource<ReceivedDatagram> Completion { get; } =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
	}

	private readonly record struct ReceivedDatagram(
		byte[] Payload,
		IPEndPoint RemoteEndPoint,
		uint InterfaceIndex);

	private enum NetworkDatagramDropKind : byte
	{
		UnexpectedRemote = 1,
		Unmatched = 2,
		Late = 3,
	}

	private enum DatagramSlotUsage : byte
	{
		Available = 0,
		Active = 1,
		Quarantined = 2,
	}

	private readonly record struct DatagramPoolMetrics(
		int SocketSlots,
		int ActiveSlots,
		int QuarantinedSlots,
		int PeakSlots);

	private readonly struct DatagramSlotReservation : IDisposable
	{
		private readonly DatagramSlot? _slot;
		private readonly PendingDatagram? _pending;

		public DatagramSlotReservation(DatagramSlot slot, PendingDatagram pending)
		{
			_slot = slot;
			_pending = pending;
		}

		public bool IsValid => _slot is not null && _pending is not null;
		public Socket Socket => _slot!.Socket;
		public uint SocketGeneration => _slot?.SocketGeneration ?? 0;

		public NetworkDatagramDispatchEvidence CreateDispatchEvidence(
			NetworkSocketExecutionRequest request,
			IPEndPoint remote)
		{
			var local = (IPEndPoint)Socket.LocalEndPoint!;
			return new NetworkDatagramDispatchEvidence(
				request.Identity,
				_pending!.FlowSerial,
				SocketGeneration,
				NetworkIpAddressInterop.FromSystemAddress(local.Address),
				checked((ushort)local.Port),
				NetworkIpAddressInterop.FromSystemAddress(remote.Address),
				checked((ushort)remote.Port),
				request.Resolved.Interface,
				DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
		}

		public async ValueTask<ReceivedDatagram> WaitForResponseAsync(CancellationToken cancellationToken) =>
			await _pending!.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

		public ValueTask EnsureConnectedAsync(IPEndPoint remote, CancellationToken cancellationToken) =>
			_slot!.EnsureConnectedAsync(remote, cancellationToken);

		public void Dispose()
		{
			if (_slot is not null && _pending is not null) _slot.Release(_pending);
		}
	}
}

using System.Collections.Concurrent;
using System.Net.Sockets;

namespace Iwesun.Runtime.Networks;

/// <summary>
/// Event-driven base class for asynchronous network endpoints.
/// Send/receive FIFO items are value types; concrete endpoints translate
/// network-specific callbacks into one or more receive FIFO values.
/// </summary>
public abstract class NetworkAsyncEndpointBase<TSend, TReceive> : IDisposable
	where TSend : struct
	where TReceive : struct
{
	private readonly ConcurrentQueue<TSend> _sendFifo = new();
	private readonly ConcurrentQueue<TReceive> _receiveFifo = new();
	private readonly AutoResetEvent _sendSignal = new(false);
	private readonly AutoResetEvent _hardwareReadySignal = new(false);
	private readonly AutoResetEvent _resetSignal = new(false);
	private readonly WaitHandle[] _wakeSignals;
	private readonly ManualResetEventSlim _startedSignal = new(false);
	private readonly object _stateGate = new();
	private readonly SynchronizationContext? _eventContext;
	private readonly int _maxSendQueueLength;
	private readonly int _maxReceiveQueueLength;
	private readonly int _sendQueueAvailableThreshold;

	private Thread? _sendThread;
	private volatile bool _disposed;
	private volatile bool _resourcesDisposed;
	private volatile bool _running;
	private volatile bool _initialized;
	private volatile bool _hardwareReady = true;
	private volatile bool _networkBusy;
	private volatile bool _bufferOverflow;
	private volatile bool _resetRequested;
	private volatile NetworkHardwareStatus _hardwareStatus = NetworkHardwareStatus.Uninitialized;
	private int _sendQueueLength;
	private int _receiveQueueLength;
	private int _sendQueueIdleWasBelowThreshold;
	private TSend _blockedSendItem;
	private bool _hasBlockedSendItem;
	private Exception? _lastHardwareException;
	private string? _lastHardwareReason;
	private TimeSpan _defaultWaitDelay = TimeSpan.Zero;

	protected NetworkAsyncEndpointBase(
		SynchronizationContext? eventContext = null,
		int maxSendQueueLength = 4096,
		int maxReceiveQueueLength = 4096,
		int sendQueueAvailableThreshold = 1)
	{
		_eventContext = eventContext ?? SynchronizationContext.Current;
		_maxSendQueueLength = maxSendQueueLength <= 0 ? int.MaxValue : maxSendQueueLength;
		_maxReceiveQueueLength = maxReceiveQueueLength <= 0 ? int.MaxValue : maxReceiveQueueLength;
		_sendQueueAvailableThreshold = NormalizeThreshold(sendQueueAvailableThreshold, _maxSendQueueLength);
		_wakeSignals = new WaitHandle[] { _sendSignal, _resetSignal, _hardwareReadySignal };
	}

	public event EventHandler<NetworkSendEventArgs<TSend>>? SendStarted;
	public event EventHandler<NetworkSendEventArgs<TSend>>? SendCompleted;
	public event EventHandler<NetworkReceiveEventArgs<TReceive>>? ReceiveCompleted;
	public event EventHandler<NetworkHardwareStateChangedEventArgs>? HardwareStateChanged;
	public event EventHandler<NetworkHardwareErrorEventArgs>? HardwareError;
	public event EventHandler<NetworkEndpointErrorEventArgs>? Error;
	public event EventHandler<NetworkQueueAvailableEventArgs>? SendQueueAvailable;

	public bool IsRunning => _running;
	public bool IsInitialized => _initialized;
	public bool IsHardwareReady => _hardwareReady;
	public bool IsNetworkBusy => _networkBusy;
	public bool IsBufferOverflow => _bufferOverflow;
	public NetworkHardwareStatus HardwareStatus => _hardwareStatus;
	public Exception? LastHardwareException => _lastHardwareException;
	public string? LastHardwareReason => _lastHardwareReason;
	public int MaxSendQueueLength => _maxSendQueueLength;
	public int MaxReceiveQueueLength => _maxReceiveQueueLength;
	public int SendQueueAvailableThreshold => _sendQueueAvailableThreshold;
	public int SendQueueLength => Volatile.Read(ref _sendQueueLength);
	public int ReceiveQueueLength => Volatile.Read(ref _receiveQueueLength);
	public int SendQueueIdle => RemainingCapacity(_maxSendQueueLength, SendQueueLength);
	public int ReceiveQueueIdle => RemainingCapacity(_maxReceiveQueueLength, ReceiveQueueLength);
	public bool IsSendQueueFull => SendQueueIdle == 0;
	public bool IsReceiveQueueFull => ReceiveQueueIdle == 0;

	/// <summary>
	/// Default wait delay used by <see cref="WaitAsync(CancellationToken)"/>.
	/// Callers can centralize endpoint-specific settle/drain timing through this property.
	/// </summary>
	public TimeSpan DefaultWaitDelay
	{
		get => _defaultWaitDelay;
		set => _defaultWaitDelay = value <= TimeSpan.Zero ? TimeSpan.Zero : value;
	}

	/// <summary>
	/// Waits by <see cref="DefaultWaitDelay"/>.
	/// </summary>
	public Task WaitAsync(CancellationToken cancellationToken = default)
	{
		return WaitAsync(DefaultWaitDelay, cancellationToken);
	}

	/// <summary>
	/// Waits by the provided delay and updates <see cref="DefaultWaitDelay"/>.
	/// </summary>
	public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken = default)
	{
		var normalized = delay <= TimeSpan.Zero ? TimeSpan.Zero : delay;
		DefaultWaitDelay = normalized;
		if (normalized == TimeSpan.Zero)
		{
			return Task.CompletedTask;
		}

		return Task.Delay(normalized, cancellationToken);
	}

	/// <summary>
	/// External send entry. The caller only writes to an in-memory FIFO and
	/// wakes the endpoint-owned sender thread.
	/// </summary>
	public void Send(TSend item)
	{
		TrySend(item);
	}

	/// <summary>
	/// Validates and enqueues one item without allowing rejected requests to enter the FIFO.
	/// </summary>
	public bool TrySend(TSend item)
	{
		ThrowIfDisposed();
		if (!OnValidateSend(item, out var reason))
		{
			RaiseError(NetworkEndpointErrorKind.SendRejected, reason ?? "send-rejected", null);
			return false;
		}
		EnsureStarted(waitForStart: false);

		if (!TryReserveSendSlot())
		{
			ReportBufferOverflow("send-fifo-overflow");
			return false;
		}

		_sendFifo.Enqueue(item);
		_sendSignal.Set();
		return true;
	}

	public int Send(TSend[] items)
	{
		ArgumentNullException.ThrowIfNull(items);
		ThrowIfDisposed();
		EnsureStarted(waitForStart: false);

		var accepted = 0;
		foreach (var item in items)
		{
			if (!OnValidateSend(item, out var reason))
			{
				RaiseError(NetworkEndpointErrorKind.SendRejected, reason ?? "send-rejected", null);
				break;
			}
			if (!TryReserveSendSlot())
			{
				ReportBufferOverflow("send-fifo-overflow");
				break;
			}

			_sendFifo.Enqueue(item);
			accepted++;
		}

		if (accepted > 0)
		{
			_sendSignal.Set();
		}

		return accepted;
	}

	public bool TryReadReceived(out TReceive item)
	{
		if (_receiveFifo.TryDequeue(out var value))
		{
			Interlocked.Decrement(ref _receiveQueueLength);
			item = value;
			return true;
		}

		item = default;
		return false;
	}

	public int TryReadReceived(Span<TReceive> buffer)
	{
		var read = 0;
		for (; read < buffer.Length; read++)
		{
			if (!_receiveFifo.TryDequeue(out var value))
			{
				break;
			}

			Interlocked.Decrement(ref _receiveQueueLength);
			buffer[read] = value;
		}

		return read;
	}

	public void SignalHardwareReady(string reason = "hardware-ready")
	{
		ThrowIfDisposed();
		SetHardwareState(NetworkHardwareStatus.Ready, true, false, false, reason, null);
		_hardwareReadySignal.Set();
		_sendSignal.Set();
	}

	public void SignalHardwareBlocked(string reason = "hardware-blocked", Exception? exception = null)
	{
		ThrowIfDisposed();
		SetHardwareState(NetworkHardwareStatus.Busy, false, true, false, reason, exception);
	}

	public void ResetHardware(string reason = "hardware-reset-requested")
	{
		ThrowIfDisposed();
		EnsureStarted(waitForStart: false);
		_resetRequested = true;
		SetHardwareState(NetworkHardwareStatus.Resetting, false, true, false, reason, null);
		_resetSignal.Set();
		_hardwareReadySignal.Set();
		_sendSignal.Set();
	}

	public void Start()
	{
		ThrowIfDisposed();
		EnsureStarted(waitForStart: true);
	}

	public void Stop()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_sendSignal.Set();
		_hardwareReadySignal.Set();
		_resetSignal.Set();
		OnStopping();

		if (_sendThread != null && _sendThread.IsAlive)
		{
			_sendThread.Join(TimeSpan.FromSeconds(2));
		}
	}

	public void Dispose()
	{
		if (_resourcesDisposed)
		{
			return;
		}

		Stop();

		_sendSignal.Dispose();
		_hardwareReadySignal.Dispose();
		_resetSignal.Dispose();
		_startedSignal.Dispose();
		_resourcesDisposed = true;
		GC.SuppressFinalize(this);
	}

	protected abstract NetworkSendResult OnSend(TSend item);

	protected virtual void OnStarted()
	{
	}

	protected virtual void OnStopping()
	{
	}

	protected virtual void OnResetHardware()
	{
	}

	protected virtual bool OnValidateSend(TSend item, out string? reason)
	{
		reason = null;
		return true;
	}

	protected virtual bool OnFilterSend(TSend item, out string? reason)
	{
		reason = null;
		return true;
	}

	protected virtual bool OnAnalyzeSend(TSend item, out TSend analyzedItem, out string? reason)
	{
		analyzedItem = item;
		reason = null;
		return true;
	}

	protected virtual bool OnFilterReceive(TReceive item, out string? reason)
	{
		reason = null;
		return true;
	}

	protected virtual bool OnAnalyzeReceive(object raw, Action<TReceive> emit, out string? reason)
	{
		if (raw is TReceive item)
		{
			emit(item);
			reason = null;
			return true;
		}

		reason = "receive-raw-type-mismatch";
		return false;
	}

	protected void PublishReceived(TReceive item)
	{
		if (_disposed)
		{
			return;
		}

		if (!OnFilterReceive(item, out var reason))
		{
			RaiseError(NetworkEndpointErrorKind.ReceiveFiltered, reason ?? "receive-filtered", null);
			return;
		}

		EnqueueReceived(item);
	}

	protected void PublishReceivedRaw(object raw)
	{
		if (_disposed)
		{
			return;
		}

		var emitted = 0;
		if (!OnAnalyzeReceive(raw, Emit, out var reason))
		{
			RaiseError(NetworkEndpointErrorKind.ReceiveAnalyzeFailed, reason ?? "receive-analyze-failed", null);
			return;
		}

		if (emitted == 0)
		{
			RaiseError(NetworkEndpointErrorKind.ReceiveAnalyzeFailed, reason ?? "receive-analyze-empty", null);
		}

		void Emit(TReceive item)
		{
			emitted++;
			PublishReceived(item);
		}
	}

	protected bool IsHardwareBlock(Exception ex)
	{
		var socketException = ex as SocketException
			?? ex.InnerException as SocketException;
		if (socketException == null)
		{
			return false;
		}

		return socketException.SocketErrorCode is
			SocketError.NetworkDown or
			SocketError.NetworkUnreachable or
			SocketError.HostDown or
			SocketError.HostUnreachable or
			SocketError.NoBufferSpaceAvailable or
			SocketError.WouldBlock or
			SocketError.IOPending;
	}

	private void EnsureStarted(bool waitForStart)
	{
		if (_running)
		{
			if (waitForStart)
			{
				_startedSignal.Wait();
			}

			return;
		}

		lock (_stateGate)
		{
			if (_running)
			{
				return;
			}

			_running = true;
			_sendThread = new Thread(SendThreadMain)
			{
				IsBackground = true,
				Name = $"{GetType().Name}.Send"
			};
			_sendThread.Start();
		}

		if (waitForStart)
		{
			_startedSignal.Wait();
		}
	}

	private void SendThreadMain()
	{
		try
		{
			OnStarted();
			_initialized = true;
			SetHardwareState(NetworkHardwareStatus.Ready, true, false, false, "endpoint-started", null);
		}
		catch (Exception ex)
		{
			_initialized = false;
			SetHardwareState(NetworkHardwareStatus.Faulted, false, false, false, "endpoint-start-failed", ex);
			RaiseHardwareError("endpoint-start-failed", ex);
		}
		finally
		{
			_startedSignal.Set();
		}

		while (!_disposed)
		{
			WaitHandle.WaitAny(_wakeSignals);
			if (_disposed)
			{
				break;
			}

			ProcessResetRequest();
			DrainSendFifo();
		}

		_running = false;
		SetHardwareState(NetworkHardwareStatus.Stopped, false, false, false, "endpoint-stopped", null);
	}

	private void ProcessResetRequest()
	{
		if (!_resetRequested)
		{
			return;
		}

		_resetRequested = false;
		try
		{
			OnResetHardware();
			SetHardwareState(NetworkHardwareStatus.Ready, true, false, false, "hardware-reset-completed", null);
			_hardwareReadySignal.Set();
		}
		catch (Exception ex)
		{
			SetHardwareState(NetworkHardwareStatus.Faulted, false, false, false, "hardware-reset-failed", ex);
			RaiseHardwareError("hardware-reset-failed", ex);
		}
	}

	private void DrainSendFifo()
	{
		while (!_disposed)
		{
			ProcessResetRequest();
			if (!_hardwareReady)
			{
				_hardwareReadySignal.WaitOne();
				continue;
			}

			if (!TryTakeSendItem(out var item))
			{
				return;
			}

			if (!OnFilterSend(item, out var filterReason))
			{
				RaiseError(NetworkEndpointErrorKind.SendFiltered, filterReason ?? "send-filtered", null);
				continue;
			}

			if (!OnAnalyzeSend(item, out var analyzedItem, out var analyzeReason))
			{
				RaiseError(NetworkEndpointErrorKind.SendAnalyzeFailed, analyzeReason ?? "send-analyze-failed", null);
				continue;
			}

			RaiseSendStarted(analyzedItem);
			var result = SendCore(analyzedItem);
			if (result.Status == NetworkSendStatus.Sent)
			{
				RaiseSendCompleted(analyzedItem);
				continue;
			}

			if (result.Status == NetworkSendStatus.HardwareBlocked)
			{
				_blockedSendItem = analyzedItem;
				_hasBlockedSendItem = true;
				SetHardwareState(NetworkHardwareStatus.Busy, false, true, false, result.Reason ?? "send-hardware-blocked", result.Exception);
				if (result.Exception != null)
				{
					RaiseHardwareError(result.Reason ?? "send-hardware-blocked", result.Exception);
				}

				continue;
			}

			RaiseError(NetworkEndpointErrorKind.SendFailed, result.Reason ?? "send-failed", result.Exception);
		}
	}

	private NetworkSendResult SendCore(TSend item)
	{
		try
		{
			return OnSend(item);
		}
		catch (Exception ex)
		{
			return IsHardwareBlock(ex)
				? NetworkSendResult.HardwareBlocked("send-hardware-blocked", ex)
				: NetworkSendResult.Failed("send-failed", ex);
		}
	}

	private bool TryTakeSendItem(out TSend item)
	{
		if (_hasBlockedSendItem)
		{
			item = _blockedSendItem;
			_blockedSendItem = default;
			_hasBlockedSendItem = false;
			return true;
		}

		if (_sendFifo.TryDequeue(out var value))
		{
			var current = Interlocked.Decrement(ref _sendQueueLength);
			ObserveSendQueueIdleEdge(current);
			item = value;
			return true;
		}

		item = default;
		return false;
	}

	private bool TryReserveSendSlot()
	{
		while (true)
		{
			var current = Volatile.Read(ref _sendQueueLength);
			if (current >= _maxSendQueueLength)
			{
				return false;
			}

			if (Interlocked.CompareExchange(ref _sendQueueLength, current + 1, current) == current)
			{
				ObserveSendQueueIdleEdge(current + 1);
				return true;
			}
		}
	}

	private bool TryReserveReceiveSlot()
	{
		while (true)
		{
			var current = Volatile.Read(ref _receiveQueueLength);
			if (current >= _maxReceiveQueueLength)
			{
				return false;
			}

			if (Interlocked.CompareExchange(ref _receiveQueueLength, current + 1, current) == current)
			{
				return true;
			}
		}
	}

	private void EnqueueReceived(TReceive item)
	{
		if (!TryReserveReceiveSlot())
		{
			ReportBufferOverflow("receive-fifo-overflow");
			return;
		}

		_receiveFifo.Enqueue(item);
		RaiseReceiveCompleted(item);
	}

	private void ReportBufferOverflow(string reason)
	{
		SetHardwareState(NetworkHardwareStatus.BufferOverflow, _hardwareReady, _networkBusy, true, reason, null);
		RaiseError(NetworkEndpointErrorKind.BufferOverflow, reason, null);
	}

	private void ObserveSendQueueIdleEdge(int sendQueueLength)
	{
		var idle = RemainingCapacity(_maxSendQueueLength, sendQueueLength);
		if (idle < _sendQueueAvailableThreshold)
		{
			Volatile.Write(ref _sendQueueIdleWasBelowThreshold, 1);
			return;
		}

		if (Interlocked.Exchange(ref _sendQueueIdleWasBelowThreshold, 0) == 1)
		{
			RaiseSendQueueAvailable(sendQueueLength, idle);
		}
	}

	private void SetHardwareState(
		NetworkHardwareStatus status,
		bool ready,
		bool busy,
		bool overflow,
		string reason,
		Exception? exception)
	{
		var changed = _hardwareStatus != status
			|| _hardwareReady != ready
			|| _networkBusy != busy
			|| _bufferOverflow != overflow;

		_hardwareStatus = status;
		_hardwareReady = ready;
		_networkBusy = busy;
		_bufferOverflow = overflow;
		_lastHardwareReason = reason;
		_lastHardwareException = exception;

		if (!changed)
		{
			return;
		}

		RaiseHardwareStateChanged(status, ready, busy, overflow, reason);
	}

	private void DispatchToEventContext(Action invoke)
	{
		if (_eventContext != null)
		{
			_eventContext.Post(_ => invoke(), null);
			return;
		}

		ThreadPool.QueueUserWorkItem(_ => invoke());
	}

	private void RaiseSendStarted(TSend item)
	{
		var handler = SendStarted;
		if (handler == null)
		{
			return;
		}

		var args = new NetworkSendEventArgs<TSend>(item, DateTime.UtcNow);
		DispatchToEventContext(() => handler.Invoke(this, args));
	}

	private void RaiseSendCompleted(TSend item)
	{
		var handler = SendCompleted;
		if (handler == null)
		{
			return;
		}

		var args = new NetworkSendEventArgs<TSend>(item, DateTime.UtcNow);
		DispatchToEventContext(() => handler.Invoke(this, args));
	}

	private void RaiseReceiveCompleted(TReceive item)
	{
		var handler = ReceiveCompleted;
		if (handler == null)
		{
			return;
		}

		var args = new NetworkReceiveEventArgs<TReceive>(item, DateTime.UtcNow);
		DispatchToEventContext(() => handler.Invoke(this, args));
	}

	private void RaiseHardwareStateChanged(NetworkHardwareStatus status, bool ready, bool busy, bool overflow, string reason)
	{
		var handler = HardwareStateChanged;
		if (handler == null)
		{
			return;
		}

		var args = new NetworkHardwareStateChangedEventArgs(status, ready, busy, overflow, reason, DateTime.UtcNow);
		DispatchToEventContext(() => handler.Invoke(this, args));
	}

	private void RaiseHardwareError(string reason, Exception exception)
	{
		var handler = HardwareError;
		if (handler == null)
		{
			return;
		}

		var args = new NetworkHardwareErrorEventArgs(reason, exception, DateTime.UtcNow);
		DispatchToEventContext(() => handler.Invoke(this, args));
	}

	private void RaiseError(NetworkEndpointErrorKind kind, string reason, Exception? exception)
	{
		var handler = Error;
		if (handler == null)
		{
			return;
		}

		var args = new NetworkEndpointErrorEventArgs(kind, reason, exception, DateTime.UtcNow);
		DispatchToEventContext(() => handler.Invoke(this, args));
	}

	private void RaiseSendQueueAvailable(int queueLength, int queueIdle)
	{
		var handler = SendQueueAvailable;
		if (handler == null)
		{
			return;
		}

		var args = new NetworkQueueAvailableEventArgs(
			queueLength,
			queueIdle,
			_maxSendQueueLength,
			_sendQueueAvailableThreshold,
			DateTime.UtcNow);
		DispatchToEventContext(() => handler.Invoke(this, args));
	}

	private void ThrowIfDisposed()
	{
		if (_disposed)
		{
			throw new ObjectDisposedException(GetType().Name);
		}
	}

	private static int RemainingCapacity(int capacity, int used)
	{
		return capacity == int.MaxValue
			? int.MaxValue
			: Math.Max(0, capacity - used);
	}

	private static int NormalizeThreshold(int threshold, int capacity)
	{
		if (threshold <= 0)
		{
			return 1;
		}

		return capacity == int.MaxValue ? threshold : Math.Min(threshold, capacity);
	}
}

public sealed class NetworkSendEventArgs<TSend> : EventArgs
	where TSend : struct
{
	public NetworkSendEventArgs(TSend item, DateTime observedAtUtc)
	{
		Item = item;
		ObservedAtUtc = observedAtUtc;
	}

	public TSend Item { get; }
	public DateTime ObservedAtUtc { get; }
}

public sealed class NetworkReceiveEventArgs<TReceive> : EventArgs
	where TReceive : struct
{
	public NetworkReceiveEventArgs(TReceive item, DateTime receivedAtUtc)
	{
		Item = item;
		ReceivedAtUtc = receivedAtUtc;
	}

	public TReceive Item { get; }
	public DateTime ReceivedAtUtc { get; }
}

public sealed class NetworkHardwareStateChangedEventArgs : EventArgs
{
	public NetworkHardwareStateChangedEventArgs(
		NetworkHardwareStatus status,
		bool ready,
		bool busy,
		bool overflow,
		string reason,
		DateTime observedAtUtc)
	{
		Status = status;
		Ready = ready;
		Busy = busy;
		Overflow = overflow;
		Reason = reason;
		ObservedAtUtc = observedAtUtc;
	}

	public NetworkHardwareStatus Status { get; }
	public bool Ready { get; }
	public bool Busy { get; }
	public bool Overflow { get; }
	public string Reason { get; }
	public DateTime ObservedAtUtc { get; }
}

public sealed class NetworkHardwareErrorEventArgs : EventArgs
{
	public NetworkHardwareErrorEventArgs(string reason, Exception exception, DateTime observedAtUtc)
	{
		Reason = reason;
		Exception = exception;
		ObservedAtUtc = observedAtUtc;
	}

	public string Reason { get; }
	public Exception Exception { get; }
	public DateTime ObservedAtUtc { get; }
}

public sealed class NetworkEndpointErrorEventArgs : EventArgs
{
	public NetworkEndpointErrorEventArgs(NetworkEndpointErrorKind kind, string reason, Exception? exception, DateTime observedAtUtc)
	{
		Kind = kind;
		Reason = reason;
		Exception = exception;
		ObservedAtUtc = observedAtUtc;
	}

	public NetworkEndpointErrorKind Kind { get; }
	public string Reason { get; }
	public Exception? Exception { get; }
	public DateTime ObservedAtUtc { get; }
}

public sealed class NetworkQueueAvailableEventArgs : EventArgs
{
	public NetworkQueueAvailableEventArgs(
		int queueLength,
		int queueIdle,
		int queueCapacity,
		int threshold,
		DateTime observedAtUtc)
	{
		QueueLength = queueLength;
		QueueIdle = queueIdle;
		QueueCapacity = queueCapacity;
		Threshold = threshold;
		ObservedAtUtc = observedAtUtc;
	}

	public int QueueLength { get; }
	public int QueueIdle { get; }
	public int QueueCapacity { get; }
	public int Threshold { get; }
	public DateTime ObservedAtUtc { get; }
}

public readonly record struct NetworkSendResult(
	NetworkSendStatus Status,
	string? Reason = null,
	Exception? Exception = null)
{
	public static NetworkSendResult Sent() => new(NetworkSendStatus.Sent);
	public static NetworkSendResult HardwareBlocked(string reason, Exception? exception = null)
		=> new(NetworkSendStatus.HardwareBlocked, reason, exception);
	public static NetworkSendResult Failed(string reason, Exception? exception = null)
		=> new(NetworkSendStatus.Failed, reason, exception);
}

public enum NetworkSendStatus
{
	Sent = 0,
	HardwareBlocked = 1,
	Failed = 2
}

public enum NetworkHardwareStatus
{
	Uninitialized = 0,
	Ready = 1,
	Busy = 2,
	BufferOverflow = 3,
	Resetting = 4,
	Faulted = 5,
	Stopped = 6
}

public enum NetworkEndpointErrorKind
{
	SendFiltered = 0,
	SendAnalyzeFailed = 1,
	SendFailed = 2,
	ReceiveFiltered = 3,
	ReceiveAnalyzeFailed = 4,
	BufferOverflow = 5,
	SendRejected = 6
}

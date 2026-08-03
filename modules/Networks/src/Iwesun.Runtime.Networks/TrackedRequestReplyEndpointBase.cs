using System.Collections.Concurrent;

namespace Iwesun.Runtime.Networks;

/// <summary>
/// Request/reply base with a Request/Attempt/Branch/Response GUID identity tree,
/// caller-key observation, send/response FIFOs, bounded retries, and per-request routing.
/// It intentionally does not inherit or modify <see cref="NetworkAsyncEndpointBase{TSend, TReceive}"/>.
/// </summary>
public abstract class TrackedRequestReplyEndpointBase<TRequest, TResponse, TKey> : IDisposable
	where TRequest : struct
	where TResponse : struct
	where TKey : notnull
{
	public const int MaximumAttemptCount = 3;
	private readonly ConcurrentQueue<QueuedAttempt> _sendFifo = new();
	private readonly ConcurrentQueue<TrackedRequestCompletion<TRequest, TResponse, TKey>> _receiveFifo = new();
	private readonly ConcurrentDictionary<Guid, PendingRequest> _pendingByRequestId = new();
	private readonly ConcurrentDictionary<Guid, ClosedRequestTrace> _closedByRequestId = new();
	private readonly ConcurrentQueue<Guid> _closedRequestOrder = new();
	private readonly AutoResetEvent _sendSignal = new(false);
	private readonly ManualResetEventSlim _startedSignal = new(false);
	private readonly ManualResetEventSlim _concurrentStartsIdle = new(true);
	private readonly CancellationTokenSource _lifetimeCts = new();
	private readonly object _lifecycleGate = new();
	private readonly int _maxSendQueueLength;
	private readonly int _maxReceiveQueueLength;
	private readonly int _maxAttemptCount;
	private readonly int _maxPendingCount;
	private readonly int _pendingSweepIntervalMs;
	private readonly SynchronizationContext? _eventContext;
	private readonly IEqualityComparer<TKey> _keyComparer;
	private Thread? _sendThread;
	private Task? _pendingSweepTask;
	private volatile bool _running;
	private volatile bool _disposed;
	private int _sendQueueLength;
	private int _receiveQueueLength;
	private int _pendingCount;
	private int _concurrentStartCount;
	private int _firstAttemptTimeoutMs;
	private int _secondAttemptTimeoutMs;
	private int _thirdAttemptTimeoutMs;

	protected TrackedRequestReplyEndpointBase(
		int maxSendQueueLength = 4096,
		int maxReceiveQueueLength = 4096,
		int maxAttemptCount = 3,
		int defaultTimeoutMs = 5000,
		int pendingSweepIntervalMs = 100,
		SynchronizationContext? eventContext = null,
		int maxPendingCount = 0)
		: this(
			EqualityComparer<TKey>.Default,
			maxSendQueueLength,
			maxReceiveQueueLength,
			maxAttemptCount,
			defaultTimeoutMs,
			pendingSweepIntervalMs,
			eventContext,
			maxPendingCount)
	{
	}

	protected TrackedRequestReplyEndpointBase(
		IEqualityComparer<TKey> keyComparer,
		int maxSendQueueLength = 4096,
		int maxReceiveQueueLength = 4096,
		int maxAttemptCount = 3,
		int defaultTimeoutMs = 5000,
		int pendingSweepIntervalMs = 100,
		SynchronizationContext? eventContext = null,
		int maxPendingCount = 0)
	{
		ArgumentNullException.ThrowIfNull(keyComparer);
		_maxSendQueueLength = maxSendQueueLength <= 0 ? int.MaxValue : maxSendQueueLength;
		_maxReceiveQueueLength = maxReceiveQueueLength <= 0 ? int.MaxValue : maxReceiveQueueLength;
		_maxAttemptCount = Math.Clamp(maxAttemptCount, 1, MaximumAttemptCount);
		_firstAttemptTimeoutMs = Math.Max(1, defaultTimeoutMs);
		_secondAttemptTimeoutMs = Math.Max(1, defaultTimeoutMs);
		_thirdAttemptTimeoutMs = Math.Max(1, defaultTimeoutMs);
		_maxPendingCount = maxPendingCount <= 0 ? _maxSendQueueLength : maxPendingCount;
		_pendingSweepIntervalMs = Math.Max(10, pendingSweepIntervalMs);
		_eventContext = eventContext ?? SynchronizationContext.Current;
		_keyComparer = keyComparer;
	}

	public event EventHandler<TrackedRequestEventArgs<TRequest, TKey>>? RequestWaiting;
	public event EventHandler<TrackedRequestRetryEventArgs<TRequest, TKey>>? RequestRetrying;
	public event EventHandler<TrackedRequestCompletedEventArgs<TRequest, TResponse, TKey>>? RequestAcknowledged;
	public event EventHandler<TrackedRequestFailedEventArgs<TRequest, TKey>>? RequestFailed;
	public event EventHandler<TrackedLateResponseEventArgs<TResponse, TKey>>? LateResponseReceived;
	public event EventHandler<TrackedRequestRejectedEventArgs<TRequest, TKey>>? RequestRejected;
	public event EventHandler<NetworkBranchTerminalEventArgs>? BranchTerminated;
	public event EventHandler<NetworkAttemptTerminalEventArgs>? AttemptTerminated;
	public event EventHandler<NetworkRequestTerminalEventArgs>? RequestTerminated;
	public event EventHandler<TrackedResponseObservedEventArgs<TResponse, TKey>>? ResponseObserved;
	public event EventHandler? SendQueueAvailable;

	public bool IsRunning => _running;
	public int SendQueueLength => Volatile.Read(ref _sendQueueLength);
	public int ReceiveQueueLength => Volatile.Read(ref _receiveQueueLength);
	public int PendingCount => Volatile.Read(ref _pendingCount);
	public int MaxAttemptCount => _maxAttemptCount;
	public int DefaultTimeoutMs
	{
		get => FirstAttemptTimeoutMs;
		set
		{
			if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
			Volatile.Write(ref _firstAttemptTimeoutMs, value);
			Volatile.Write(ref _secondAttemptTimeoutMs, value);
			Volatile.Write(ref _thirdAttemptTimeoutMs, value);
		}
	}
	public int FirstAttemptTimeoutMs
	{
		get => Volatile.Read(ref _firstAttemptTimeoutMs);
		set => SetAttemptTimeout(ref _firstAttemptTimeoutMs, value, nameof(value));
	}
	public int SecondAttemptTimeoutMs
	{
		get => Volatile.Read(ref _secondAttemptTimeoutMs);
		set => SetAttemptTimeout(ref _secondAttemptTimeoutMs, value, nameof(value));
	}
	public int ThirdAttemptTimeoutMs
	{
		get => Volatile.Read(ref _thirdAttemptTimeoutMs);
		set => SetAttemptTimeout(ref _thirdAttemptTimeoutMs, value, nameof(value));
	}
	public int MaxSendQueueLength => _maxSendQueueLength;
	public int MaxReceiveQueueLength => _maxReceiveQueueLength;
	public int MaxPendingCount => _maxPendingCount;

	public void Start()
	{
		ThrowIfDisposed();
		EnsureStarted(waitForStart: true);
	}

	public bool TrySend(TRequest request)
		=> TrySend(request, out _);

	/// <summary>Attempts to enqueue a request and returns a synchronous pre-FIFO rejection when it fails.</summary>
	public bool TrySend(TRequest request, out TrackedRequestRejection<TRequest, TKey> rejection)
	{
		ThrowIfDisposed();

		var requestId = GetRequestId(request);
		if (requestId == Guid.Empty)
		{
			var rejectionKey = GetRequestKey(request);
			rejection = new(
				requestId,
				rejectionKey,
				request,
				new(NetworkFailureKind.Rejected, 0, NetworkAccessFailureCodes.RequestIdEmpty, default),
				DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
			RaiseRequestRejected(rejection);
			return false;
		}

		EnsureStarted(waitForStart: false);
		var key = GetRequestKey(request);
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		if (_closedByRequestId.ContainsKey(requestId))
		{
			rejection = CreateRejection(requestId, key, request, NetworkAccessFailureCodes.RequestIdAlreadyFinal, now);
			RaiseRequestRejected(rejection);
			return false;
		}
		var pending = new PendingRequest(requestId, key, request, now);
		if (!TryReserve(ref _pendingCount, _maxPendingCount))
		{
			rejection = CreateRejection(requestId, key, request, NetworkAccessFailureCodes.PendingCapacityReached, now);
			RaiseRequestRejected(rejection);
			return false;
		}
		if (!_pendingByRequestId.TryAdd(requestId, pending))
		{
			Interlocked.Decrement(ref _pendingCount);
			rejection = CreateRejection(requestId, key, request, NetworkAccessFailureCodes.RequestIdDuplicate, now);
			RaiseRequestRejected(rejection);
			return false;
		}

		if (!TryEnqueueAttempt(new QueuedAttempt(requestId, request, 0, NetworkFailure.None)))
		{
			TryRemovePending(requestId, out _);
			rejection = CreateRejection(requestId, key, request, NetworkAccessFailureCodes.SendFifoOverflow, now);
			RaiseRequestRejected(rejection);
			return false;
		}

		rejection = default;
		return true;
	}

	public int Send(ReadOnlySpan<TRequest> requests)
	{
		var accepted = 0;
		foreach (var request in requests)
		{
			if (!TrySend(request)) break;
			accepted++;
		}
		return accepted;
	}

	public bool TryReadReceived(out TrackedRequestCompletion<TRequest, TResponse, TKey> completion)
	{
		if (_receiveFifo.TryDequeue(out completion))
		{
			Interlocked.Decrement(ref _receiveQueueLength);
			return true;
		}

		completion = default;
		return false;
	}

	public int TryReadReceived(Span<TrackedRequestCompletion<TRequest, TResponse, TKey>> buffer)
	{
		var count = 0;
		while (count < buffer.Length && TryReadReceived(out var completion))
			buffer[count++] = completion;
		return count;
	}

	public bool TryGetPending(Guid requestId, out TrackedPendingSnapshot<TRequest, TKey> snapshot)
	{
		if (!_pendingByRequestId.TryGetValue(requestId, out var pending))
		{
			snapshot = default;
			return false;
		}

		lock (pending.Gate)
		{
			snapshot = pending.ToSnapshot();
			return true;
		}
	}

	/// <summary>Returns immutable point-in-time snapshots of the current attempt branches.</summary>
	public TrackedBranchSnapshot[] GetCurrentBranches(Guid requestId)
	{
		if (!_pendingByRequestId.TryGetValue(requestId, out var pending)) return [];
		lock (pending.Gate)
		{
			if (pending.CurrentAttempt is null) return [];
			return pending.CurrentAttempt.Branches.Select(static branch => new TrackedBranchSnapshot(
				branch.BranchNumber,
				branch.Identity,
				branch.Terminal.HasValue,
				branch.Terminal?.State ?? NetworkExecutionTerminalState.Unspecified,
				branch.Terminal?.ProtocolOutcome ?? ProtocolOutcome.Unspecified,
				branch.Terminal?.AccessCompliance ?? AccessCompliance.Unspecified)).ToArray();
		}
	}

	/// <summary>
	/// Returns a point-in-time snapshot of all pending requests carrying the caller key.
	/// Keys are not unique and are never used to match responses or mutate pending state.
	/// </summary>
	public TrackedPendingSnapshot<TRequest, TKey>[] GetPendingByKey(TKey key)
	{
		var snapshots = new List<TrackedPendingSnapshot<TRequest, TKey>>();
		foreach (var pending in _pendingByRequestId.Values)
		{
			if (!_keyComparer.Equals(pending.Key, key)) continue;
			lock (pending.Gate)
			{
				if (!_pendingByRequestId.TryGetValue(pending.RequestId, out var current) || !ReferenceEquals(current, pending))
					continue;
				snapshots.Add(pending.ToSnapshot());
			}
		}
		return snapshots.ToArray();
	}

	/// <summary>
	/// Counts pending requests carrying the caller key. The result is an observation and may change
	/// immediately as requests complete, fail, or are added.
	/// </summary>
	public int CountPendingByKey(TKey key)
	{
		var count = 0;
		foreach (var pending in _pendingByRequestId.Values)
			if (_keyComparer.Equals(pending.Key, key)) count++;
		return count;
	}

	protected abstract Guid GetRequestId(TRequest request);
	/// <summary>Gets the non-unique caller query key carried by the request.</summary>
	protected abstract TKey GetRequestKey(TRequest request);
	protected abstract Guid GetResponseRequestId(TResponse response);
	/// <summary>Gets response-side caller data for late-response diagnostics only.</summary>
	protected abstract TKey GetResponseKey(TResponse response);
	/// <summary>Gets a response identity created by a 3.0 endpoint; legacy responses return default.</summary>
	protected virtual NetworkExecutionIdentity GetResponseIdentity(TResponse response) => default;
	protected abstract int? GetRequestTimeoutOverrideMs(TRequest request);
	protected abstract TrackedAttemptStartResult OnStartAttempt(
		TRequest request,
		byte retryCount,
		int timeoutMs,
		CancellationToken cancellationToken);

	/// <summary>Returns the number of execution branches in one attempt. Single-path endpoints use one.</summary>
	protected virtual byte GetAttemptBranchCount(
		TRequest request,
		byte retryCount) => 1;

	/// <summary>Controls whether a branch closes on its first valid response or collects a bounded response window.</summary>
	protected virtual TrackedResponseCollectionPolicy GetResponseCollectionPolicy(TRequest request) =>
		TrackedResponseCollectionPolicy.FirstValidResponse();
	/// <summary>Returns true when the protocol execution backend closes its own bounded response window.</summary>
	protected virtual bool IsResponseCollectionWindowExecutionOwned(TRequest request) => false;
	/// <summary>Returns true when the execution backend always publishes its own bounded terminal result.</summary>
	protected virtual bool IsAttemptTimeoutExecutionOwned(TRequest request) => false;
	/// <summary>
	/// Gets whether independent attempts may plan and start their I/O concurrently.
	/// Endpoints may opt in only when all planning state is request-isolated and thread-safe.
	/// </summary>
	protected virtual bool StartAttemptsConcurrently => false;

	/// <summary>
	/// Starts one concrete branch. Multi-branch endpoints override this method and publish responses
	/// with the supplied BranchId. The default bridges the single-path endpoint contract.
	/// </summary>
	protected virtual TrackedAttemptStartResult OnStartBranch(
		TRequest request,
		byte retryCount,
		byte branchNumber,
		NetworkExecutionIdentity identity,
		int timeoutMs,
		CancellationToken cancellationToken)
	{
		return branchNumber == 1
			? OnStartAttempt(request, retryCount, timeoutMs, cancellationToken)
			: TrackedAttemptStartResult.Rejected(new(
				NetworkFailureKind.Rejected,
				0,
				NetworkAccessFailureCodes.BranchStartNotSupported,
				default));
	}

	protected virtual bool IsSuccessfulResponse(TResponse response, out NetworkFailure failure)
	{
		failure = NetworkFailure.None;
		return true;
	}

	/// <summary>Returns the protocol fact carried by a response without deriving it from access compliance.</summary>
	protected virtual ProtocolOutcome GetResponseProtocolOutcome(
		TResponse response,
		bool contractSatisfied,
		NetworkFailure failure) => contractSatisfied ? ProtocolOutcome.Succeeded : MapProtocolOutcome(failure);

	/// <summary>Returns the access fact carried by a response without deriving it from protocol success.</summary>
	protected virtual AccessCompliance GetResponseAccessCompliance(
		TResponse response,
		bool contractSatisfied,
		NetworkFailure failure) => contractSatisfied ? AccessCompliance.Satisfied : AccessCompliance.EvidenceIncomplete;

	/// <summary>Returns access evidence retained when an attempt closes without a response.</summary>
	protected virtual AccessCompliance GetFailureAccessCompliance(TRequest request, NetworkFailure failure) =>
		failure.Kind == NetworkFailureKind.Rejected
			? AccessCompliance.NotApplicable
			: AccessCompliance.EvidenceIncomplete;

	protected virtual byte? GetResponseRetryCount(TResponse response) => null;

	/// <summary>
	/// Resolves the timeout for one concrete attempt. A request-level timeout overrides all three
	/// endpoint defaults; otherwise retry counts 0, 1 and 2 use the first, second and third properties.
	/// </summary>
	protected int GetAttemptTimeoutMs(TRequest request, byte retryCount)
	{
		var requestOverride = GetRequestTimeoutOverrideMs(request);
		if (requestOverride.HasValue)
		{
			if (requestOverride.Value <= 0)
				throw new ArgumentOutOfRangeException(nameof(request), "Request timeout must be positive when specified.");
			return requestOverride.Value;
		}

		return retryCount switch
		{
			0 => FirstAttemptTimeoutMs,
			1 => SecondAttemptTimeoutMs,
			_ => ThirdAttemptTimeoutMs,
		};
	}

	protected virtual bool CanRetry(TRequest request, NetworkFailure failure) => true;

	protected virtual void OnStarted()
	{
	}

	protected virtual void OnStopping()
	{
	}

	protected virtual void OnReset()
	{
	}

	/// <summary>Publishes a concrete response into the tracked completion pipeline.</summary>
	protected void PublishResponse(TResponse response) => PublishResponse(response, Guid.Empty);

	/// <summary>Publishes a response for one explicit execution branch.</summary>
	protected void PublishResponse(TResponse response, Guid branchId)
	{
		if (_disposed) return;
		var requestId = GetResponseRequestId(response);
		var responseUserKey = GetResponseKey(response);
		var responseRetryCount = GetResponseRetryCount(response);
		if (!_pendingByRequestId.TryGetValue(requestId, out var pending))
		{
			RaiseLateResponse(
				ResolveClosedResponseIdentity(requestId, responseRetryCount, branchId),
				responseUserKey,
				response,
				NetworkAccessFailureCodes.ResponseNotPending);
			return;
		}

		NetworkFailure failure;
		var succeeded = IsSuccessfulResponse(response, out failure);
		var responseOutcome = GetResponseProtocolOutcome(response, succeeded, failure);
		var responseCompliance = GetResponseAccessCompliance(response, succeeded, failure);
		failure = NormalizeResponseFailure(responseOutcome, responseCompliance, failure);
		TrackedRequestCompletion<TRequest, TResponse, TKey> completion = default;
		var completionReady = false;
		lock (pending.Gate)
		{
			if (responseRetryCount.HasValue && responseRetryCount.Value != pending.RetryCount)
			{
				RaiseLateResponse(
					ResolvePendingResponseIdentity(pending, responseRetryCount.Value, branchId),
					responseUserKey,
					response,
					NetworkAccessFailureCodes.ResponseFromOlderAttempt);
				return;
			}

			var branch = ResolveCurrentBranch(pending, branchId);
			if (branch is null)
			{
				RaiseLateResponse(
					pending.CurrentAttempt?.Identity ?? NetworkExecutionIdentity.ForRequest(requestId),
					responseUserKey,
					response,
					branchId == Guid.Empty
						? NetworkAccessFailureCodes.ResponseBranchAmbiguous
						: NetworkAccessFailureCodes.ResponseBranchUnknown);
				return;
			}

			if (branch.Terminal.HasValue || pending.Status is PendingStatus.Completed or PendingStatus.Failed)
			{
				RaiseLateResponse(
					CreateResponseIdentity(branch.Identity),
					responseUserKey,
					response,
					NetworkAccessFailureCodes.ResponseAlreadyFinal);
				return;
			}

			var suppliedIdentity = GetResponseIdentity(response);
			var responseIdentity = suppliedIdentity.HasResponse &&
				suppliedIdentity.RequestId == branch.Identity.RequestId &&
				suppliedIdentity.AttemptId == branch.Identity.AttemptId &&
				suppliedIdentity.BranchId == branch.Identity.BranchId
				? suppliedIdentity
				: CreateResponseIdentity(branch.Identity);
			RaiseResponseObserved(responseIdentity, responseUserKey, response, isLate: false);
			if (pending.ResponsePolicy.Mode == TrackedResponseCollectionMode.CollectUntilWindowEnds)
			{
				if (!succeeded)
				{
					pending.LastCollectionFailure = failure;
					pending.LastCollectionProtocolOutcome = responseOutcome;
					pending.LastCollectionAccessCompliance = responseCompliance;
					return;
				}
				pending.ValidResponseCount++;
				pending.HasValidResponse = true;
				pending.LastValidResponse = response;
				pending.LastValidResponseIdentity = responseIdentity;
				pending.LastCollectionProtocolOutcome = responseOutcome;
				pending.LastCollectionAccessCompliance = responseCompliance;
				if (pending.ValidResponseCount < pending.ResponsePolicy.MaxResponses) return;
			}
			var branchTerminal = CreateBranchTerminal(
				branch.Identity,
				succeeded ? NetworkExecutionTerminalState.Succeeded : MapResponseTerminalState(responseOutcome),
				responseOutcome,
				responseCompliance,
				failure);
			branch.Terminal = branchTerminal;
			RaiseBranchTerminated(branchTerminal);

			if (!succeeded && pending.CurrentAttempt!.Branches.Exists(static item => !item.Terminal.HasValue))
			{
				return;
			}

			if (!succeeded)
			{
				completionReady = false;
			}
			else
			{
				var completedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
				CompleteCurrentAttemptLocked(pending, NetworkFailure.None, completedAt);
				var requestTerminal = CreateRequestTerminal(
					pending,
					NetworkExecutionTerminalState.Succeeded,
					responseOutcome,
					responseCompliance,
					NetworkFailure.None);
				pending.RequestTerminal = requestTerminal;
				RaiseRequestTerminated(requestTerminal);
				completion = new TrackedRequestCompletion<TRequest, TResponse, TKey>(
					pending.RequestId,
					pending.Key,
					pending.Request,
					response,
					pending.AttemptCount,
					responseRetryCount ?? pending.RetryCount,
					pending.FirstQueuedAtUnixMs,
					completedAt,
					pending.ToAttemptHistory(),
					ClampElapsed(completedAt - pending.FirstQueuedAtUnixMs),
					responseIdentity);

				if (!TryEnqueueCompletion(completion))
				{
					pending.Status = PendingStatus.CompletionPending;
					pending.PendingCompletion = completion;
					return;
				}

				pending.Status = PendingStatus.Completed;
				completionReady = true;
			}
		}

		if (!succeeded)
		{
			ScheduleRetryOrFail(pending, failure);
			return;
		}

		if (completionReady)
		{
			TryRemovePending(requestId, out _);
			RaiseRequestAcknowledged(completion);
		}
	}

	/// <summary>Closes a branch from execution facts when no protocol response object exists.</summary>
	protected void PublishBranchTerminal(
		Guid requestId,
		Guid branchId,
		ProtocolOutcome protocolOutcome,
		AccessCompliance accessCompliance,
		NetworkFailure failure)
	{
		if (_disposed || requestId == Guid.Empty || branchId == Guid.Empty ||
			!_pendingByRequestId.TryGetValue(requestId, out var pending)) return;
		failure = NormalizeResponseFailure(protocolOutcome, accessCompliance, failure);
		var shouldFinalize = false;
		lock (pending.Gate)
		{
			if (pending.Status is PendingStatus.Completed or PendingStatus.Failed) return;
			var branch = ResolveCurrentBranch(pending, branchId);
			if (branch is null || branch.Terminal.HasValue) return;
			var terminal = CreateBranchTerminal(
				branch.Identity,
				MapResponseTerminalState(protocolOutcome),
				protocolOutcome,
				accessCompliance,
				failure);
			branch.Terminal = terminal;
			RaiseBranchTerminated(terminal);
			shouldFinalize = pending.CurrentAttempt?.Branches.TrueForAll(static item => item.Terminal.HasValue) == true;
		}
		if (shouldFinalize) ScheduleRetryOrFail(pending, failure);
	}

	/// <summary>Completes a backend-owned collection window after all available responses have been published.</summary>
	protected void CompleteResponseCollectionWindow(Guid requestId)
	{
		if (_disposed || requestId == Guid.Empty ||
			!_pendingByRequestId.TryGetValue(requestId, out var pending)) return;
		FinalizeCollectedWindow(pending);
	}

	public void Reset()
	{
		ThrowIfDisposed();
		OnReset();
		foreach (var pending in _pendingByRequestId.Values)
			ScheduleRetryOrFail(pending, new(NetworkFailureKind.Reset, 0, "endpoint-reset", default));
	}

	public void Stop()
	{
		if (_disposed) return;
		_disposed = true;
		_lifetimeCts.Cancel();
		_sendSignal.Set();
		if (_sendThread is { IsAlive: true }) _sendThread.Join(TimeSpan.FromSeconds(2));
		_concurrentStartsIdle.Wait(TimeSpan.FromSeconds(2));
		OnStopping();

		foreach (var pending in _pendingByRequestId.Values)
			FinalizeFailure(pending, new(NetworkFailureKind.Stopped, 0, "endpoint-stopped", default));
		_running = false;
	}

	public void Dispose()
	{
		Stop();
		try { _pendingSweepTask?.Wait(TimeSpan.FromSeconds(1)); } catch { }
		_sendSignal.Dispose();
		_startedSignal.Dispose();
		_concurrentStartsIdle.Dispose();
		_lifetimeCts.Dispose();
		GC.SuppressFinalize(this);
	}

	private void EnsureStarted(bool waitForStart)
	{
		if (!_running)
		{
			lock (_lifecycleGate)
			{
				if (!_running)
				{
					_running = true;
					_sendThread = new Thread(SendLoop)
					{
						IsBackground = true,
						Name = $"{GetType().Name}.TrackedSend",
					};
					_sendThread.Start();
				}
			}
		}

		if (waitForStart) _startedSignal.Wait();
	}

	private void SendLoop()
	{
		try
		{
			OnStarted();
			_pendingSweepTask = SweepPendingAsync(_lifetimeCts.Token);
		}
		finally
		{
			_startedSignal.Set();
		}

		while (!_lifetimeCts.IsCancellationRequested)
		{
			_sendSignal.WaitOne();
			while (_sendFifo.TryDequeue(out var attempt))
			{
				Interlocked.Decrement(ref _sendQueueLength);
				if (StartAttemptsConcurrently)
				{
					if (Interlocked.Increment(ref _concurrentStartCount) == 1)
						_concurrentStartsIdle.Reset();
					ThreadPool.QueueUserWorkItem(
						static state => state.Owner.ProcessConcurrentAttempt(state.Attempt),
						(Owner: this, Attempt: attempt),
						preferLocal: false);
				}
				else
					ProcessAttemptSafely(attempt);
			}
			RaiseSendQueueAvailable();
		}
	}

	private void ProcessConcurrentAttempt(QueuedAttempt attempt)
	{
		try
		{
			ProcessAttemptSafely(attempt);
		}
		finally
		{
			if (Interlocked.Decrement(ref _concurrentStartCount) == 0)
				_concurrentStartsIdle.Set();
		}
	}

	private void ProcessAttemptSafely(QueuedAttempt attempt)
	{
		try
		{
			ProcessAttempt(attempt);
		}
		catch (Exception ex)
		{
			if (_pendingByRequestId.TryGetValue(attempt.RequestId, out var pending))
			{
				CreateFallbackBranch(pending);
				FinalizeFailure(pending, new(
					NetworkFailureKind.Transport,
					0,
					NetworkAccessFailureCodes.AttemptProcessingThrew,
					BinaryNetworkError.FromException(ex, false, false)));
			}
		}
	}

	private void ProcessAttempt(QueuedAttempt attempt)
	{
		if (!_pendingByRequestId.TryGetValue(attempt.RequestId, out var pending)) return;
		TRequest request;
		int timeoutMs;
		NetworkFailure? planningFailure = null;
		lock (pending.Gate)
		{
			if (pending.Status is PendingStatus.Completed or PendingStatus.Failed) return;
			request = attempt.Request;
			pending.Request = request;
			pending.RetryCount = attempt.RetryCount;
			pending.AttemptCount++;
			var attemptIdentity = NetworkExecutionIdentity.ForRequest(pending.RequestId).StartAttempt(Guid.NewGuid());
			pending.CurrentAttempt = new AttemptState(attemptIdentity, pending.AttemptCount, attempt.RetryCount);
			pending.Attempts.Add(pending.CurrentAttempt);
			pending.Status = PendingStatus.Planning;
			pending.LastSentAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			try
			{
				pending.CurrentAttemptTimeoutMs = GetAttemptTimeoutMs(request, attempt.RetryCount);
				pending.DeadlineAtUnixMs = IsAttemptTimeoutExecutionOwned(request)
					? long.MaxValue
					: pending.LastSentAtUnixMs + pending.CurrentAttemptTimeoutMs;
				pending.ResponsePolicy = GetResponseCollectionPolicy(request);
				if (!pending.ResponsePolicy.IsValid)
					throw new InvalidOperationException(NetworkAccessFailureCodes.ResponseCollectionPolicyInvalid);
				pending.CollectionDeadlineAtUnixMs = pending.ResponsePolicy.Mode ==
					TrackedResponseCollectionMode.CollectUntilWindowEnds
					? IsResponseCollectionWindowExecutionOwned(request)
						? long.MaxValue
						: pending.LastSentAtUnixMs + pending.ResponsePolicy.WindowMs
					: 0;
				pending.ValidResponseCount = 0;
				pending.HasValidResponse = false;
				pending.LastValidResponse = default;
				pending.LastValidResponseIdentity = default;
				pending.LastCollectionFailure = NetworkFailure.None;
				pending.LastCollectionProtocolOutcome = ProtocolOutcome.Unspecified;
				pending.LastCollectionAccessCompliance = AccessCompliance.Unspecified;
				timeoutMs = pending.CurrentAttemptTimeoutMs;
			}
			catch (Exception ex)
			{
				timeoutMs = 1;
				var invalidCollectionPolicy = ex is InvalidOperationException &&
					StringComparer.Ordinal.Equals(ex.Message, NetworkAccessFailureCodes.ResponseCollectionPolicyInvalid);
				planningFailure = new(
					invalidCollectionPolicy ? NetworkFailureKind.Rejected : NetworkFailureKind.Transport,
					0,
					invalidCollectionPolicy
						? NetworkAccessFailureCodes.ResponseCollectionPolicyInvalid
						: NetworkAccessFailureCodes.AttemptPlanThrew,
					BinaryNetworkError.FromException(ex, false, false));
			}
		}
		if (planningFailure is { } failure)
		{
			CreateFallbackBranch(pending);
			ScheduleRetryOrFail(pending, failure);
			return;
		}

		byte branchCount;
		try
		{
			branchCount = GetAttemptBranchCount(request, attempt.RetryCount);
		}
		catch (Exception ex)
		{
			CreateFallbackBranch(pending);
			ScheduleRetryOrFail(pending, new(
				NetworkFailureKind.Transport,
				0,
				NetworkAccessFailureCodes.AttemptPlanThrew,
				BinaryNetworkError.FromException(ex, false, false)));
			return;
		}
		if (branchCount == 0)
		{
			CreateFallbackBranch(pending);
			ScheduleRetryOrFail(pending, new(
				NetworkFailureKind.Rejected,
				0,
				NetworkAccessFailureCodes.AttemptBranchCountZero,
				default));
			return;
		}

		var acceptedCount = 0;
		NetworkFailure lastFailure = NetworkFailure.None;
		for (var branchIndex = 1; branchIndex <= branchCount; branchIndex++)
		{
			var branchNumber = checked((byte)branchIndex);
			BranchState branch;
			lock (pending.Gate)
			{
				var identity = pending.CurrentAttempt!.Identity.StartBranch(Guid.NewGuid());
				branch = new BranchState(identity, branchNumber);
				pending.CurrentAttempt.Branches.Add(branch);
			}

			TrackedAttemptStartResult result;
			try
			{
				result = OnStartBranch(
					request,
					attempt.RetryCount,
					branchNumber,
					branch.Identity,
					timeoutMs,
					_lifetimeCts.Token);
			}
			catch (Exception ex)
			{
				result = TrackedAttemptStartResult.Rejected(new(
					NetworkFailureKind.Transport,
					0,
					"attempt-start-threw",
					BinaryNetworkError.FromException(ex, false, false)));
			}

			if (result.Status == TrackedAttemptStartStatus.Accepted)
			{
				acceptedCount++;
				continue;
			}

			lastFailure = result.Failure;
			lock (pending.Gate)
			{
				var terminal = CreateBranchTerminal(
					branch.Identity,
					NetworkExecutionTerminalState.Rejected,
					ProtocolOutcome.Rejected,
					AccessCompliance.NotApplicable,
					result.Failure);
				branch.Terminal = terminal;
				RaiseBranchTerminated(terminal);
			}
		}

		if (acceptedCount == 0)
		{
			ScheduleRetryOrFail(pending, lastFailure);
			return;
		}

		TrackedPendingSnapshot<TRequest, TKey> waitingSnapshot;
		lock (pending.Gate)
		{
			if (pending.Status != PendingStatus.Planning) return;
			pending.Status = PendingStatus.Waiting;
			waitingSnapshot = pending.ToSnapshot();
		}
		RaiseRequestWaiting(waitingSnapshot);
	}

	private async Task SweepPendingAsync(CancellationToken ct)
	{
		using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_pendingSweepIntervalMs));
		try
		{
			while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
			{
				var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
				foreach (var pending in _pendingByRequestId.Values)
				{
					var flushCompletion = false;
					var timedOut = false;
					lock (pending.Gate)
					{
						if (pending.Status == PendingStatus.CompletionPending)
						{
							flushCompletion = true;
						}
						else if (pending.Status == PendingStatus.Waiting &&
							pending.ResponsePolicy.Mode == TrackedResponseCollectionMode.CollectUntilWindowEnds &&
							pending.CollectionDeadlineAtUnixMs <= now)
						{
							if (pending.HasValidResponse) flushCompletion = true;
							else timedOut = true;
						}
						else if (pending.Status == PendingStatus.Waiting && pending.DeadlineAtUnixMs <= now)
						{
							timedOut = true;
						}
					}

					if (flushCompletion && pending.Status == PendingStatus.CompletionPending)
						TryFlushPendingCompletion(pending);
					else if (flushCompletion)
						FinalizeCollectedWindow(pending);
					else if (timedOut)
						ScheduleRetryOrFail(
							pending,
							pending.ResponsePolicy.Mode == TrackedResponseCollectionMode.CollectUntilWindowEnds &&
							pending.LastCollectionFailure.Kind != NetworkFailureKind.None
								? pending.LastCollectionFailure
								: new NetworkFailure(
									NetworkFailureKind.Timeout,
									0,
									pending.ResponsePolicy.Mode == TrackedResponseCollectionMode.CollectUntilWindowEnds
										? NetworkAccessFailureCodes.ResponseCollectionWindowEmpty
										: "response-timeout",
									default));
				}
			}
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested)
		{
		}
	}

	private void TryFlushPendingCompletion(PendingRequest pending)
	{
		TrackedRequestCompletion<TRequest, TResponse, TKey> completion;
		lock (pending.Gate)
		{
			if (pending.Status != PendingStatus.CompletionPending) return;
			completion = pending.PendingCompletion;
			if (!TryEnqueueCompletion(completion)) return;
			pending.Status = PendingStatus.Completed;
		}

		TryRemovePending(pending.RequestId, out _);
		RaiseRequestAcknowledged(completion);
	}

	private void ScheduleRetryOrFail(PendingRequest pending, NetworkFailure failure)
	{
		QueuedAttempt retry = default;
		TrackedPendingSnapshot<TRequest, TKey> retrySnapshot = default;
		var shouldRetry = false;
		lock (pending.Gate)
		{
			if (pending.Status is PendingStatus.Completed or PendingStatus.Failed or PendingStatus.RetryQueued)
				return;

			CompleteCurrentAttemptLocked(pending, failure, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

			if (failure.Kind != NetworkFailureKind.Rejected &&
				pending.AttemptCount < _maxAttemptCount &&
				CanRetry(pending.Request, failure))
			{
				var retryCount = checked((byte)(pending.RetryCount + 1));
				var request = pending.Request;
				pending.Status = PendingStatus.RetryQueued;
				retry = new QueuedAttempt(pending.RequestId, request, retryCount, failure);
				retrySnapshot = pending.ToSnapshot();
				shouldRetry = true;
			}
		}

		if (!shouldRetry)
		{
			FinalizeFailure(pending, failure);
			return;
		}

		if (!TryEnqueueAttempt(retry))
		{
			FinalizeFailure(pending, new(NetworkFailureKind.QueueOverflow, 0, "retry-send-fifo-overflow", default));
			return;
		}

		RaiseRequestRetrying(retrySnapshot, failure);
	}

	private void FinalizeFailure(PendingRequest pending, NetworkFailure failure)
	{
		TrackedRequestFailure<TRequest, TKey> final;
		lock (pending.Gate)
		{
			if (pending.Status is PendingStatus.Completed or PendingStatus.Failed) return;
			pending.Status = PendingStatus.Failed;
			var failedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			CompleteCurrentAttemptLocked(pending, failure, failedAt);
			var requestTerminal = CreateRequestTerminal(
				pending,
				pending.CurrentAttempt?.Terminal?.State ?? MapTerminalState(failure),
				pending.CurrentAttempt?.Terminal?.ProtocolOutcome ?? MapProtocolOutcome(failure),
				pending.CurrentAttempt?.Terminal?.AccessCompliance ?? GetFailureAccessCompliance(pending.Request, failure),
				failure);
			pending.RequestTerminal = requestTerminal;
			final = new(
				pending.RequestId,
				pending.Key,
				pending.Request,
				pending.AttemptCount,
				pending.RetryCount,
				failure,
				pending.FirstQueuedAtUnixMs,
				failedAt,
				pending.ToAttemptHistory(),
				ClampElapsed(failedAt - pending.FirstQueuedAtUnixMs),
				requestTerminal.Identity,
				pending.CurrentAttempt?.Terminal ?? default,
				requestTerminal);
			RaiseRequestTerminated(requestTerminal);
		}

		TryRemovePending(pending.RequestId, out _);
		RaiseRequestFailed(final);
	}

	private void FinalizeCollectedWindow(PendingRequest pending)
	{
		TrackedRequestCompletion<TRequest, TResponse, TKey> completion = default;
		var completionReady = false;
		lock (pending.Gate)
		{
			if (pending.Status != PendingStatus.Waiting || !pending.HasValidResponse ||
				pending.ResponsePolicy.Mode != TrackedResponseCollectionMode.CollectUntilWindowEnds)
				return;
			var branch = ResolveCurrentBranch(pending, pending.LastValidResponseIdentity.BranchId);
			if (branch is null || branch.Terminal.HasValue) return;
			var completedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			var branchTerminal = CreateBranchTerminal(
				branch.Identity,
				NetworkExecutionTerminalState.Succeeded,
				pending.LastCollectionProtocolOutcome == ProtocolOutcome.Unspecified
					? ProtocolOutcome.Succeeded : pending.LastCollectionProtocolOutcome,
				pending.LastCollectionAccessCompliance == AccessCompliance.Unspecified
					? AccessCompliance.Satisfied : pending.LastCollectionAccessCompliance,
				NetworkFailure.None);
			branch.Terminal = branchTerminal;
			RaiseBranchTerminated(branchTerminal);
			CompleteCurrentAttemptLocked(pending, NetworkFailure.None, completedAt);
			var requestTerminal = CreateRequestTerminal(
				pending,
				NetworkExecutionTerminalState.Succeeded,
				branchTerminal.ProtocolOutcome,
				branchTerminal.AccessCompliance,
				NetworkFailure.None);
			pending.RequestTerminal = requestTerminal;
			RaiseRequestTerminated(requestTerminal);
			completion = new TrackedRequestCompletion<TRequest, TResponse, TKey>(
				pending.RequestId,
				pending.Key,
				pending.Request,
				pending.LastValidResponse,
				pending.AttemptCount,
				pending.RetryCount,
				pending.FirstQueuedAtUnixMs,
				completedAt,
				pending.ToAttemptHistory(),
				ClampElapsed(completedAt - pending.FirstQueuedAtUnixMs),
				pending.LastValidResponseIdentity);
			if (!TryEnqueueCompletion(completion))
			{
				pending.Status = PendingStatus.CompletionPending;
				pending.PendingCompletion = completion;
				return;
			}
			pending.Status = PendingStatus.Completed;
			completionReady = true;
		}
		if (!completionReady) return;
		TryRemovePending(pending.RequestId, out _);
		RaiseRequestAcknowledged(completion);
	}

	private bool TryEnqueueAttempt(QueuedAttempt attempt)
	{
		if (!TryReserve(ref _sendQueueLength, _maxSendQueueLength)) return false;
		_sendFifo.Enqueue(attempt);
		_sendSignal.Set();
		return true;
	}

	private bool TryRemovePending(Guid requestId, out PendingRequest? pending)
	{
		if (_pendingByRequestId.TryGetValue(requestId, out var closing))
			CacheClosedTrace(closing);
		if (_pendingByRequestId.TryRemove(requestId, out pending))
		{
			Interlocked.Decrement(ref _pendingCount);
			return true;
		}

		return false;
	}

	private bool TryEnqueueCompletion(TrackedRequestCompletion<TRequest, TResponse, TKey> completion)
	{
		if (!TryReserve(ref _receiveQueueLength, _maxReceiveQueueLength)) return false;
		_receiveFifo.Enqueue(completion);
		return true;
	}

	private static bool TryReserve(ref int counter, int capacity)
	{
		while (true)
		{
			var current = Volatile.Read(ref counter);
			if (current >= capacity) return false;
			if (Interlocked.CompareExchange(ref counter, current + 1, current) == current) return true;
		}
	}

	private static void SetAttemptTimeout(ref int field, int value, string parameterName)
	{
		if (value <= 0) throw new ArgumentOutOfRangeException(parameterName);
		Volatile.Write(ref field, value);
	}

	private static int ClampElapsed(long elapsedMs)
		=> elapsedMs <= 0 ? 0 : elapsedMs >= int.MaxValue ? int.MaxValue : (int)elapsedMs;

	private void CompleteCurrentAttemptLocked(
		PendingRequest pending,
		NetworkFailure failure,
		long finishedAtUnixMs)
	{
		var attempt = pending.CurrentAttempt;
		if (attempt is null || attempt.Terminal.HasValue)
			return;

		var decisiveBranch = failure.Kind == NetworkFailureKind.None
			? attempt.Branches.FirstOrDefault(static branch =>
				branch.Terminal is { State: NetworkExecutionTerminalState.Succeeded })?.Terminal
			: null;
		var failureCompliance = failure.Kind == NetworkFailureKind.None
			? AccessCompliance.NotApplicable
			: GetFailureAccessCompliance(pending.Request, failure);
		var branchState = failure.Kind == NetworkFailureKind.None
			? NetworkExecutionTerminalState.Superseded
			: MapTerminalState(failure);
		var branchOutcome = failure.Kind == NetworkFailureKind.None
			? ProtocolOutcome.Cancelled
			: MapProtocolOutcome(failure);
		foreach (var branch in attempt.Branches)
		{
			if (branch.Terminal.HasValue) continue;
			var branchTerminal = CreateBranchTerminal(
				branch.Identity, branchState, branchOutcome, failureCompliance, failure);
			branch.Terminal = branchTerminal;
			RaiseBranchTerminated(branchTerminal);
		}

		var compliance = decisiveBranch?.AccessCompliance ?? AggregateAccessCompliance(attempt.Branches);
		var terminal = new NetworkAttemptTerminal(
			attempt.Identity,
			failure.Kind == NetworkFailureKind.None
				? NetworkExecutionTerminalState.Succeeded
				: MapTerminalState(failure),
			failure.Kind == NetworkFailureKind.None
				? decisiveBranch?.ProtocolOutcome ?? ProtocolOutcome.Succeeded
				: MapProtocolOutcome(failure),
			compliance,
			checked((ushort)attempt.Branches.Count),
			ToAccessFailure(failure));
		attempt.Terminal = terminal;
		RaiseAttemptTerminated(terminal);

		var report = new TrackedAttemptReport(
			pending.AttemptCount,
			pending.RetryCount,
			pending.CurrentAttemptTimeoutMs,
			pending.LastSentAtUnixMs,
			finishedAtUnixMs,
			ClampElapsed(finishedAtUnixMs - pending.LastSentAtUnixMs),
			failure.Kind,
			failure.Code,
			attempt.Identity,
			checked((ushort)attempt.Branches.Count));
		pending.SetAttemptReport(report);
	}

	private static TrackedRequestRejection<TRequest, TKey> CreateRejection(
		Guid requestId,
		TKey key,
		TRequest request,
		string reason,
		long rejectedAtUnixMs) => new(
			requestId,
			key,
			request,
			new(NetworkFailureKind.Rejected, 0, reason, default),
			rejectedAtUnixMs);

	private void CreateFallbackBranch(PendingRequest pending)
	{
		lock (pending.Gate)
		{
			var attempt = pending.CurrentAttempt;
			if (attempt is null || attempt.Branches.Count != 0) return;
			attempt.Branches.Add(new BranchState(attempt.Identity.StartBranch(Guid.NewGuid()), 1));
		}
	}

	private static BranchState? ResolveCurrentBranch(PendingRequest pending, Guid branchId)
	{
		var attempt = pending.CurrentAttempt;
		if (attempt is null) return null;
		if (branchId != Guid.Empty)
			return attempt.Branches.Find(item => item.Identity.BranchId == branchId);
		return attempt.Branches.Count == 1 ? attempt.Branches[0] : null;
	}

	private static NetworkExecutionIdentity ResolvePendingResponseIdentity(
		PendingRequest pending,
		byte retryCount,
		Guid branchId)
	{
		var attempt = pending.Attempts.Find(item => item.RetryCount == retryCount);
		if (attempt is null) return NetworkExecutionIdentity.ForRequest(pending.RequestId);
		var branch = branchId != Guid.Empty
			? attempt.Branches.Find(item => item.Identity.BranchId == branchId)
			: attempt.Branches.Count == 1 ? attempt.Branches[0] : null;
		return branch is null ? attempt.Identity : CreateResponseIdentity(branch.Identity);
	}

	private NetworkExecutionIdentity ResolveClosedResponseIdentity(
		Guid requestId,
		byte? retryCount,
		Guid branchId)
	{
		if (!_closedByRequestId.TryGetValue(requestId, out var trace))
			return requestId == Guid.Empty ? default : NetworkExecutionIdentity.ForRequest(requestId);
		var candidates = retryCount.HasValue
			? trace.Branches.FindAll(item => item.RetryCount == retryCount.Value)
			: trace.Branches;
		var branch = branchId != Guid.Empty
			? trace.Branches.Find(item => item.Identity.BranchId == branchId)
			: candidates.Count == 1 ? candidates[0] : default;
		return branch.Identity.HasBranch ? CreateResponseIdentity(branch.Identity) : trace.RequestIdentity;
	}

	private static NetworkExecutionIdentity CreateResponseIdentity(NetworkExecutionIdentity branchIdentity)
		=> branchIdentity.CreateResponse(Guid.NewGuid());

	private static NetworkBranchTerminal CreateBranchTerminal(
		NetworkExecutionIdentity identity,
		NetworkExecutionTerminalState state,
		ProtocolOutcome protocolOutcome,
		AccessCompliance accessCompliance,
		NetworkFailure failure) => new(
			identity,
			state,
			protocolOutcome,
			accessCompliance,
			ToAccessFailure(failure));

	private static NetworkRequestTerminal CreateRequestTerminal(
		PendingRequest pending,
		NetworkExecutionTerminalState state,
		ProtocolOutcome protocolOutcome,
		AccessCompliance accessCompliance,
		NetworkFailure failure) => new(
			NetworkExecutionIdentity.ForRequest(pending.RequestId),
			state,
			protocolOutcome,
			accessCompliance,
			pending.AttemptCount,
			ToAccessFailure(failure));

	private static NetworkExecutionTerminalState MapTerminalState(NetworkFailure failure) => failure.Kind switch
	{
		NetworkFailureKind.Rejected => NetworkExecutionTerminalState.Rejected,
		NetworkFailureKind.Timeout => NetworkExecutionTerminalState.TimedOut,
		NetworkFailureKind.Cancelled or NetworkFailureKind.Stopped => NetworkExecutionTerminalState.Cancelled,
		_ => NetworkExecutionTerminalState.Failed,
	};

	private static ProtocolOutcome MapProtocolOutcome(NetworkFailure failure) => failure.Kind switch
	{
		NetworkFailureKind.Rejected => ProtocolOutcome.Rejected,
		NetworkFailureKind.Timeout => ProtocolOutcome.TimedOut,
		NetworkFailureKind.Cancelled or NetworkFailureKind.Stopped => ProtocolOutcome.Cancelled,
		NetworkFailureKind.Protocol or NetworkFailureKind.Remote => ProtocolOutcome.ProtocolFailed,
		NetworkFailureKind.AccessEvidence or NetworkFailureKind.AccessViolation => ProtocolOutcome.Succeeded,
		_ => ProtocolOutcome.TransportFailed,
	};

	private static NetworkExecutionTerminalState MapResponseTerminalState(ProtocolOutcome outcome) => outcome switch
	{
		ProtocolOutcome.Rejected => NetworkExecutionTerminalState.Rejected,
		ProtocolOutcome.TimedOut => NetworkExecutionTerminalState.TimedOut,
		ProtocolOutcome.Cancelled => NetworkExecutionTerminalState.Cancelled,
		_ => NetworkExecutionTerminalState.Failed,
	};

	private static NetworkFailure NormalizeResponseFailure(
		ProtocolOutcome outcome,
		AccessCompliance compliance,
		NetworkFailure failure)
	{
		if (outcome != ProtocolOutcome.Succeeded || compliance == AccessCompliance.Satisfied) return failure;
		return compliance == AccessCompliance.Violated
			? new NetworkFailure(
				NetworkFailureKind.AccessViolation,
				failure.Code,
				NetworkAccessFailureCodes.AccessConstraintViolated,
				failure.Error)
			: new NetworkFailure(
				NetworkFailureKind.AccessEvidence,
				failure.Code,
				NetworkAccessFailureCodes.AccessEvidenceIncomplete,
				failure.Error);
	}

	private static AccessCompliance AggregateAccessCompliance(List<BranchState> branches)
	{
		var aggregate = AccessCompliance.NotApplicable;
		foreach (var branch in branches)
		{
			if (branch.Terminal is not { } terminal) continue;
			if (terminal.AccessCompliance == AccessCompliance.Violated) return AccessCompliance.Violated;
			if (terminal.AccessCompliance == AccessCompliance.EvidenceIncomplete)
				aggregate = AccessCompliance.EvidenceIncomplete;
			else if (terminal.AccessCompliance == AccessCompliance.Satisfied && aggregate == AccessCompliance.NotApplicable)
				aggregate = AccessCompliance.Satisfied;
		}
		return aggregate;
	}

	private static NetworkAccessFailure ToAccessFailure(NetworkFailure failure) =>
		failure.Kind == NetworkFailureKind.None
			? default
			: new(
				failure.Reason ?? "network-failure",
				new NetworkPlatformError(
					Environment.OSVersion.Platform.ToString(),
					failure.Error.Code,
					failure.Error.HResult,
					failure.Kind.ToString()));

	private void CacheClosedTrace(PendingRequest pending)
	{
		List<ClosedBranchTrace> branches;
		lock (pending.Gate)
		{
			branches = pending.Attempts.SelectMany(static attempt => attempt.Branches.Select(branch =>
				new ClosedBranchTrace(attempt.RetryCount, branch.Identity)))
				.ToList();
		}

		_closedByRequestId[pending.RequestId] = new ClosedRequestTrace(
			NetworkExecutionIdentity.ForRequest(pending.RequestId),
			branches);
		_closedRequestOrder.Enqueue(pending.RequestId);
		while (_closedByRequestId.Count > 4096 && _closedRequestOrder.TryDequeue(out var expiredRequestId))
			_closedByRequestId.TryRemove(expiredRequestId, out _);
	}

	private void RaiseRequestWaiting(TrackedPendingSnapshot<TRequest, TKey> snapshot)
	{
		var handler = RequestWaiting;
		if (handler is not null) Dispatch(() => handler(this, new(snapshot)));
	}

	private void RaiseRequestRetrying(
		TrackedPendingSnapshot<TRequest, TKey> snapshot,
		NetworkFailure failure)
	{
		var handler = RequestRetrying;
		if (handler is not null) Dispatch(() => handler(this, new(snapshot, failure)));
	}

	private void RaiseRequestAcknowledged(
		TrackedRequestCompletion<TRequest, TResponse, TKey> completion)
	{
		var handler = RequestAcknowledged;
		if (handler is not null) Dispatch(() => handler(this, new(completion)));
	}

	private void RaiseRequestFailed(TrackedRequestFailure<TRequest, TKey> failure)
	{
		var handler = RequestFailed;
		if (handler is not null) Dispatch(() => handler(this, new(failure)));
	}

	private void RaiseLateResponse(NetworkExecutionIdentity identity, TKey key, TResponse response, string reason)
	{
		if (identity.HasResponse) RaiseResponseObserved(identity, key, response, isLate: true);
		var handler = LateResponseReceived;
		if (handler is not null)
		{
			var observedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			Dispatch(() => handler(this, new(identity, key, response, reason, observedAt)));
		}
	}

	private void RaiseResponseObserved(
		NetworkExecutionIdentity identity,
		TKey key,
		TResponse response,
		bool isLate)
	{
		var handler = ResponseObserved;
		if (handler is not null)
		{
			var observedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			Dispatch(() => handler(this, new(identity, key, response, isLate, observedAt)));
		}
	}

	private void RaiseRequestRejected(TrackedRequestRejection<TRequest, TKey> rejection)
	{
		var handler = RequestRejected;
		if (handler is not null) Dispatch(() => handler(this, new(rejection)));
	}

	private void RaiseBranchTerminated(NetworkBranchTerminal terminal)
	{
		var handler = BranchTerminated;
		if (handler is not null) Dispatch(() => handler(this, new(terminal)));
	}

	private void RaiseAttemptTerminated(NetworkAttemptTerminal terminal)
	{
		var handler = AttemptTerminated;
		if (handler is not null) Dispatch(() => handler(this, new(terminal)));
	}

	private void RaiseRequestTerminated(NetworkRequestTerminal terminal)
	{
		var handler = RequestTerminated;
		if (handler is not null) Dispatch(() => handler(this, new(terminal)));
	}

	private void RaiseSendQueueAvailable()
	{
		var handler = SendQueueAvailable;
		if (handler is not null) Dispatch(() => handler(this, EventArgs.Empty));
	}

	private void Dispatch(Action action)
	{
		if (_eventContext is not null) _eventContext.Post(static state => ((Action)state!).Invoke(), action);
		else ThreadPool.QueueUserWorkItem(static state => ((Action)state!).Invoke(), action);
	}

	private void ThrowIfDisposed()
	{
		if (_disposed) throw new ObjectDisposedException(GetType().Name);
	}

	private enum PendingStatus : byte
	{
		Queued,
		Planning,
		Waiting,
		RetryQueued,
		CompletionPending,
		Completed,
		Failed,
	}

	private sealed class PendingRequest
	{
		public PendingRequest(Guid requestId, TKey key, TRequest request, long queuedAt)
		{
			RequestId = requestId;
			Key = key;
			Request = request;
			FirstQueuedAtUnixMs = queuedAt;
			Status = PendingStatus.Queued;
		}

		public object Gate { get; } = new();
		public List<AttemptState> Attempts { get; } = [];
		public Guid RequestId { get; }
		public TKey Key { get; }
		public TRequest Request;
		public TrackedRequestCompletion<TRequest, TResponse, TKey> PendingCompletion;
		public byte RetryCount;
		public byte AttemptCount;
		public long FirstQueuedAtUnixMs;
		public long LastSentAtUnixMs;
		public long DeadlineAtUnixMs;
		public long CollectionDeadlineAtUnixMs;
		public int CurrentAttemptTimeoutMs;
		public byte AttemptHistoryCount;
		public TrackedAttemptReport FirstAttempt;
		public TrackedAttemptReport SecondAttempt;
		public TrackedAttemptReport ThirdAttempt;
		public AttemptState? CurrentAttempt;
		public NetworkRequestTerminal RequestTerminal;
		public TrackedResponseCollectionPolicy ResponsePolicy;
		public ushort ValidResponseCount;
		public bool HasValidResponse;
		public TResponse LastValidResponse;
		public NetworkExecutionIdentity LastValidResponseIdentity;
		public NetworkFailure LastCollectionFailure;
		public ProtocolOutcome LastCollectionProtocolOutcome;
		public AccessCompliance LastCollectionAccessCompliance;
		public PendingStatus Status;

		public void SetAttemptReport(TrackedAttemptReport report)
		{
			switch (AttemptHistoryCount)
			{
				case 0: FirstAttempt = report; break;
				case 1: SecondAttempt = report; break;
				default: ThirdAttempt = report; break;
			}
			AttemptHistoryCount++;
		}

		public TrackedAttemptHistory ToAttemptHistory() => new(
			AttemptHistoryCount,
			FirstAttempt,
			SecondAttempt,
			ThirdAttempt);

		public TrackedPendingSnapshot<TRequest, TKey> ToSnapshot() => new(
			RequestId,
			Key,
			Request,
			AttemptCount,
			RetryCount,
			FirstQueuedAtUnixMs,
			LastSentAtUnixMs,
			DeadlineAtUnixMs,
			Status.ToString(),
			CurrentAttemptTimeoutMs,
			ToAttemptHistory(),
			ClampElapsed(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - FirstQueuedAtUnixMs),
			CurrentAttempt?.Identity ?? NetworkExecutionIdentity.ForRequest(RequestId),
			checked((ushort)(CurrentAttempt?.Branches.Count ?? 0)));
	}

	private sealed class AttemptState(
		NetworkExecutionIdentity identity,
		byte attemptNumber,
		byte retryCount)
	{
		public NetworkExecutionIdentity Identity { get; } = identity;
		public byte AttemptNumber { get; } = attemptNumber;
		public byte RetryCount { get; } = retryCount;
		public List<BranchState> Branches { get; } = [];
		public NetworkAttemptTerminal? Terminal;

	}

	private sealed class BranchState(NetworkExecutionIdentity identity, byte branchNumber)
	{
		public NetworkExecutionIdentity Identity { get; } = identity;
		public byte BranchNumber { get; } = branchNumber;
		public NetworkBranchTerminal? Terminal;
	}

	private sealed class ClosedRequestTrace(
		NetworkExecutionIdentity requestIdentity,
		List<ClosedBranchTrace> branches)
	{
		public NetworkExecutionIdentity RequestIdentity { get; } = requestIdentity;
		public List<ClosedBranchTrace> Branches { get; } = branches;
	}

	private readonly record struct ClosedBranchTrace(byte RetryCount, NetworkExecutionIdentity Identity);

	private readonly record struct QueuedAttempt(
		Guid RequestId,
		TRequest Request,
		byte RetryCount,
		NetworkFailure PreviousFailure);
}

public enum NetworkFailureKind : byte
{
	None = 0,
	Rejected = 1,
	Timeout = 2,
	Transport = 3,
	Protocol = 4,
	Remote = 5,
	QueueOverflow = 6,
	Reset = 7,
	Stopped = 8,
	Cancelled = 9,
	AccessEvidence = 10,
	AccessViolation = 11,
}

public readonly record struct NetworkFailure(
	NetworkFailureKind Kind,
	int Code,
	string? Reason,
	BinaryNetworkError Error)
{
	public static NetworkFailure None => default;
}

public enum TrackedAttemptStartStatus : byte
{
	Accepted = 0,
	Rejected = 1,
	HardwareBlocked = 2,
}

public enum TrackedResponseCollectionMode : byte
{
	Unspecified = 0,
	FirstValidResponse = 1,
	CollectUntilWindowEnds = 2,
}

/// <summary>Bounded response publication policy for one attempt.</summary>
public readonly record struct TrackedResponseCollectionPolicy(
	TrackedResponseCollectionMode Mode,
	int WindowMs,
	ushort MaxResponses)
{
	public bool IsValid => Mode switch
	{
		TrackedResponseCollectionMode.FirstValidResponse => WindowMs == 0 && MaxResponses == 1,
		TrackedResponseCollectionMode.CollectUntilWindowEnds => WindowMs > 0 && MaxResponses > 0,
		_ => false,
	};

	public static TrackedResponseCollectionPolicy FirstValidResponse() =>
		new(TrackedResponseCollectionMode.FirstValidResponse, 0, 1);

	public static TrackedResponseCollectionPolicy CollectUntilWindowEnds(int windowMs, ushort maxResponses) =>
		new(TrackedResponseCollectionMode.CollectUntilWindowEnds, windowMs, maxResponses);
}

public readonly record struct TrackedAttemptStartResult(
	TrackedAttemptStartStatus Status,
	NetworkFailure Failure)
{
	public static TrackedAttemptStartResult Accepted() => new(TrackedAttemptStartStatus.Accepted, NetworkFailure.None);
	public static TrackedAttemptStartResult Rejected(NetworkFailure failure) => new(TrackedAttemptStartStatus.Rejected, failure);
	public static TrackedAttemptStartResult HardwareBlocked(NetworkFailure failure) => new(TrackedAttemptStartStatus.HardwareBlocked, failure);
}

public readonly record struct TrackedPendingSnapshot<TRequest, TKey>(
	Guid RequestId,
	TKey Key,
	TRequest Request,
	byte AttemptCount,
	byte RetryCount,
	long FirstQueuedAtUnixMs,
	long LastSentAtUnixMs,
	long DeadlineAtUnixMs,
	string Status,
	int CurrentAttemptTimeoutMs,
	TrackedAttemptHistory AttemptHistory,
	int TotalElapsedMs,
	NetworkExecutionIdentity Identity = default,
	ushort CurrentBranchCount = 0)
	where TRequest : struct
	where TKey : notnull;

public readonly record struct TrackedBranchSnapshot(
	byte BranchNumber,
	NetworkExecutionIdentity Identity,
	bool IsTerminal,
	NetworkExecutionTerminalState State,
	ProtocolOutcome ProtocolOutcome,
	AccessCompliance AccessCompliance);

/// <summary>Measured outcome for one concrete network attempt.</summary>
public readonly record struct TrackedAttemptReport(
	byte AttemptNumber,
	byte RetryCount,
	int TimeoutMs,
	long StartedAtUnixMs,
	long FinishedAtUnixMs,
	int ElapsedMs,
	NetworkFailureKind FailureKind,
	int FailureCode,
	NetworkExecutionIdentity Identity = default,
	ushort BranchCount = 0);

/// <summary>Allocation-free history for the hard maximum of three attempts.</summary>
public readonly record struct TrackedAttemptHistory(
	byte Count,
	TrackedAttemptReport First,
	TrackedAttemptReport Second,
	TrackedAttemptReport Third)
{
	public TrackedAttemptReport GetAttempt(int attemptNumber) => attemptNumber switch
	{
		1 when Count >= 1 => First,
		2 when Count >= 2 => Second,
		3 when Count >= 3 => Third,
		_ => throw new ArgumentOutOfRangeException(nameof(attemptNumber)),
	};
}

public readonly record struct TrackedRequestCompletion<TRequest, TResponse, TKey>(
	Guid RequestId,
	TKey Key,
	TRequest Request,
	TResponse Response,
	byte AttemptCount,
	byte RetryCount,
	long FirstQueuedAtUnixMs,
	long CompletedAtUnixMs,
	TrackedAttemptHistory AttemptHistory,
	int TotalElapsedMs,
	NetworkExecutionIdentity Identity = default)
	where TRequest : struct
	where TResponse : struct
	where TKey : notnull;

public readonly record struct TrackedRequestFailure<TRequest, TKey>(
	Guid RequestId,
	TKey Key,
	TRequest Request,
	byte AttemptCount,
	byte RetryCount,
	NetworkFailure Failure,
	long FirstQueuedAtUnixMs,
	long FailedAtUnixMs,
	TrackedAttemptHistory AttemptHistory,
	int TotalElapsedMs,
	NetworkExecutionIdentity Identity = default,
	NetworkAttemptTerminal AttemptTerminal = default,
	NetworkRequestTerminal RequestTerminal = default)
	where TRequest : struct
	where TKey : notnull;

public sealed class TrackedRequestEventArgs<TRequest, TKey>(TrackedPendingSnapshot<TRequest, TKey> snapshot) : EventArgs
	where TRequest : struct where TKey : notnull
{
	public TrackedPendingSnapshot<TRequest, TKey> Snapshot { get; } = snapshot;
}

public sealed class TrackedRequestRetryEventArgs<TRequest, TKey>(
	TrackedPendingSnapshot<TRequest, TKey> snapshot,
	NetworkFailure failure) : EventArgs
	where TRequest : struct where TKey : notnull
{
	public TrackedPendingSnapshot<TRequest, TKey> Snapshot { get; } = snapshot;
	public NetworkFailure Failure { get; } = failure;
	/// <summary>Retry ordinal being queued: 1 for the second attempt, 2 for the third.</summary>
	public byte NextRetryCount { get; } = checked((byte)(snapshot.RetryCount + 1));
	/// <summary>One-based attempt number being queued: 2 or 3.</summary>
	public byte NextAttemptNumber { get; } = checked((byte)(snapshot.AttemptCount + 1));
}

public sealed class TrackedRequestCompletedEventArgs<TRequest, TResponse, TKey>(
	TrackedRequestCompletion<TRequest, TResponse, TKey> completion) : EventArgs
	where TRequest : struct where TResponse : struct where TKey : notnull
{
	public TrackedRequestCompletion<TRequest, TResponse, TKey> Completion { get; } = completion;
}

public sealed class TrackedRequestFailedEventArgs<TRequest, TKey>(TrackedRequestFailure<TRequest, TKey> failure) : EventArgs
	where TRequest : struct where TKey : notnull
{
	public TrackedRequestFailure<TRequest, TKey> Failure { get; } = failure;
}

public sealed class TrackedLateResponseEventArgs<TResponse, TKey>(
	NetworkExecutionIdentity identity,
	TKey key,
	TResponse response,
	string reason,
	long observedAtUnixMs) : EventArgs
	where TResponse : struct where TKey : notnull
{
	public NetworkExecutionIdentity Identity { get; } = identity;
	public Guid RequestId => Identity.RequestId;
	public TKey Key { get; } = key;
	public TResponse Response { get; } = response;
	public string Reason { get; } = reason;
	public long ObservedAtUnixMs { get; } = observedAtUnixMs;
}

public sealed class TrackedResponseObservedEventArgs<TResponse, TKey>(
	NetworkExecutionIdentity identity,
	TKey key,
	TResponse response,
	bool isLate,
	long observedAtUnixMs) : EventArgs
	where TResponse : struct where TKey : notnull
{
	public NetworkExecutionIdentity Identity { get; } = identity;
	public TKey Key { get; } = key;
	public TResponse Response { get; } = response;
	public bool IsLate { get; } = isLate;
	public long ObservedAtUnixMs { get; } = observedAtUnixMs;
}

public readonly record struct TrackedRequestRejection<TRequest, TKey>(
	Guid RequestId,
	TKey Key,
	TRequest Request,
	NetworkFailure Failure,
	long RejectedAtUnixMs)
	where TRequest : struct
	where TKey : notnull;

public sealed class TrackedRequestRejectedEventArgs<TRequest, TKey>(
	TrackedRequestRejection<TRequest, TKey> rejection) : EventArgs
	where TRequest : struct where TKey : notnull
{
	public TrackedRequestRejection<TRequest, TKey> Rejection { get; } = rejection;
}

public sealed class NetworkBranchTerminalEventArgs(NetworkBranchTerminal terminal) : EventArgs
{
	public NetworkBranchTerminal Terminal { get; } = terminal;
}

public sealed class NetworkAttemptTerminalEventArgs(NetworkAttemptTerminal terminal) : EventArgs
{
	public NetworkAttemptTerminal Terminal { get; } = terminal;
}

public sealed class NetworkRequestTerminalEventArgs(NetworkRequestTerminal terminal) : EventArgs
{
	public NetworkRequestTerminal Terminal { get; } = terminal;
}

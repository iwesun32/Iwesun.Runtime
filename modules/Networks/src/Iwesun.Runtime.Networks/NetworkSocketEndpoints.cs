namespace Iwesun.Runtime.Networks;

/// <summary>Networks 3.0 tracked TCP endpoint using a non-null precision access plan.</summary>
public sealed class NetworkTcpConnectEndpoint<TKey>
	: TrackedRequestReplyEndpointBase<NetworkTcpConnectRequest<TKey>, NetworkTcpConnectResponse<TKey>, TKey>
	where TKey : notnull
{
	private readonly NetworkAccessExecutionContext _context;
	protected override bool StartAttemptsConcurrently => true;

	public NetworkTcpConnectEndpoint(
		NetworkAccessExecutionContext? context = null,
		int maxSendQueueLength = 4096,
		int maxReceiveQueueLength = 4096,
		int maxAttemptCount = 3,
		int defaultTimeoutMs = 1000)
		: base(maxSendQueueLength, maxReceiveQueueLength, maxAttemptCount, defaultTimeoutMs)
	{
		_context = context ?? NetworkAccessExecutionContext.CreatePlatformDefault();
	}

	protected override Guid GetRequestId(NetworkTcpConnectRequest<TKey> request) => request.RequestId;
	protected override TKey GetRequestKey(NetworkTcpConnectRequest<TKey> request) => request.Key;
	protected override Guid GetResponseRequestId(NetworkTcpConnectResponse<TKey> response) => response.Identity.RequestId;
	protected override TKey GetResponseKey(NetworkTcpConnectResponse<TKey> response) => response.Key;
	protected override NetworkExecutionIdentity GetResponseIdentity(NetworkTcpConnectResponse<TKey> response) => response.Identity;
	protected override int? GetRequestTimeoutOverrideMs(NetworkTcpConnectRequest<TKey> request) => request.TimeoutMs;
	protected override byte? GetResponseRetryCount(NetworkTcpConnectResponse<TKey> response) => response.RetryCount;
	protected override bool CanRetry(NetworkTcpConnectRequest<TKey> request, NetworkFailure failure) => request.AllowRetry;

	protected override TrackedAttemptStartResult OnStartAttempt(
		NetworkTcpConnectRequest<TKey> request, byte retryCount,
		int timeoutMs, CancellationToken cancellationToken) => PrecisionSocketEndpointSupport.BranchIdentityRequired();

	protected override TrackedAttemptStartResult OnStartBranch(
		NetworkTcpConnectRequest<TKey> request,
		byte retryCount,
		byte branchNumber,
		NetworkExecutionIdentity identity,
		int timeoutMs,
		CancellationToken cancellationToken)
	{
		if (branchNumber != 1 || request.Port == 0 || !request.AccessPlan.TryValidate(out _))
			return PrecisionSocketEndpointSupport.Reject(NetworkAccessFailureCodes.SocketRequestInvalid);
		if (!_context.TryResolve(request.AccessPlan, request.SecurityBoundary, out var resolved, out var failure))
			return PrecisionSocketEndpointSupport.Reject(failure.ReasonCode);
		_ = ExecuteAsync(request, retryCount, identity, resolved, timeoutMs, cancellationToken);
		return TrackedAttemptStartResult.Accepted();
	}

	protected override bool IsSuccessfulResponse(NetworkTcpConnectResponse<TKey> response, out NetworkFailure failure)
		=> PrecisionSocketEndpointSupport.IsSuccessful(response.Result, out failure);
	protected override ProtocolOutcome GetResponseProtocolOutcome(
		NetworkTcpConnectResponse<TKey> response, bool contractSatisfied, NetworkFailure failure) => response.Result.Outcome;
	protected override AccessCompliance GetResponseAccessCompliance(
		NetworkTcpConnectResponse<TKey> response, bool contractSatisfied, NetworkFailure failure) => response.Result.Compliance;

	private async Task ExecuteAsync(
		NetworkTcpConnectRequest<TKey> request,
		byte retryCount,
		NetworkExecutionIdentity branchIdentity,
		ResolvedAccessPlan resolved,
		int timeoutMs,
		CancellationToken lifetimeToken)
	{
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
		timeout.CancelAfter(timeoutMs);
		var result = await _context.SocketExecutor.ExecuteAsync(new NetworkSocketExecutionRequest(
			branchIdentity,
			request.AccessPlan,
			resolved,
			request.SecurityBoundary,
			NetworkSocketOperation.TcpConnect,
			request.Port,
			[],
			0), timeout.Token).ConfigureAwait(false);
		result = PrecisionSocketEndpointSupport.NormalizeTimeout(result, timeout, lifetimeToken);
		PublishResponse(new NetworkTcpConnectResponse<TKey>(
			branchIdentity.CreateResponse(Guid.NewGuid()), request.Key, retryCount, result), branchIdentity.BranchId);
	}
}

public readonly record struct NetworkTcpConnectRequest<TKey>(
	Guid RequestId,
	TKey Key,
	RequestedAccessPlan AccessPlan,
	NetworkAccessSecurityBoundary SecurityBoundary,
	ushort Port,
	int? TimeoutMs = null,
	bool AllowRetry = true)
	where TKey : notnull;

public readonly record struct NetworkTcpConnectResponse<TKey>(
	NetworkExecutionIdentity Identity,
	TKey Key,
	byte RetryCount,
	NetworkSocketExecutionResult Result)
	where TKey : notnull;

/// <summary>Networks 3.0 tracked UDP endpoint using flow serials and a long-lived datagram data plane.</summary>
public sealed class NetworkUdpDatagramEndpoint<TKey>
	: TrackedRequestReplyEndpointBase<NetworkUdpDatagramRequest<TKey>, NetworkUdpDatagramResponse<TKey>, TKey>
	where TKey : notnull
{
	private readonly NetworkAccessExecutionContext _context;
	private readonly INetworkDatagramDataPlane _dataPlane;
	private readonly bool _ownsDataPlane;
	protected override bool StartAttemptsConcurrently => true;

	public NetworkUdpDatagramEndpoint(
		NetworkAccessExecutionContext? context = null,
		int maxSendQueueLength = 4096,
		int maxReceiveQueueLength = 4096,
		int maxAttemptCount = 3,
		int defaultTimeoutMs = 3000,
		INetworkDatagramDataPlane? dataPlane = null,
		int lateResponseQuarantineMs = 1000,
		int maxSocketsPerPool = 64,
		int maxPoolCount = 4096)
		: base(maxSendQueueLength, maxReceiveQueueLength, maxAttemptCount, defaultTimeoutMs)
	{
		if (lateResponseQuarantineMs < 0) throw new ArgumentOutOfRangeException(nameof(lateResponseQuarantineMs));
		if (maxSocketsPerPool <= 0) throw new ArgumentOutOfRangeException(nameof(maxSocketsPerPool));
		if (maxPoolCount <= 0) throw new ArgumentOutOfRangeException(nameof(maxPoolCount));
		_context = context ?? NetworkAccessExecutionContext.CreatePlatformDefault();
		_dataPlane = dataPlane ?? new SystemSocketDatagramDataPlane(
			_context.SocketExecutor,
			TimeSpan.FromMilliseconds(lateResponseQuarantineMs),
			maxSocketsPerPool,
			maxPoolCount);
		_ownsDataPlane = dataPlane is null;
		_dataPlane.DatagramDispatched += OnDatagramDispatched;
	}

	public event Action<NetworkDatagramDispatchEvidence>? DatagramDispatched;

	public NetworkDataPlaneDescriptor DataPlaneDescriptor => _dataPlane.Descriptor;
	public NetworkDatagramDataPlaneCounters DataPlaneCounters => _dataPlane.Counters;

	protected override Guid GetRequestId(NetworkUdpDatagramRequest<TKey> request) => request.RequestId;
	protected override TKey GetRequestKey(NetworkUdpDatagramRequest<TKey> request) => request.Key;
	protected override Guid GetResponseRequestId(NetworkUdpDatagramResponse<TKey> response) => response.Identity.RequestId;
	protected override TKey GetResponseKey(NetworkUdpDatagramResponse<TKey> response) => response.Key;
	protected override NetworkExecutionIdentity GetResponseIdentity(NetworkUdpDatagramResponse<TKey> response) => response.Identity;
	protected override int? GetRequestTimeoutOverrideMs(NetworkUdpDatagramRequest<TKey> request) => request.TimeoutMs;
	protected override byte? GetResponseRetryCount(NetworkUdpDatagramResponse<TKey> response) => response.RetryCount;
	protected override bool CanRetry(NetworkUdpDatagramRequest<TKey> request, NetworkFailure failure) => request.AllowRetry;

	protected override TrackedAttemptStartResult OnStartAttempt(
		NetworkUdpDatagramRequest<TKey> request, byte retryCount,
		int timeoutMs, CancellationToken cancellationToken) => PrecisionSocketEndpointSupport.BranchIdentityRequired();

	protected override TrackedAttemptStartResult OnStartBranch(
		NetworkUdpDatagramRequest<TKey> request,
		byte retryCount,
		byte branchNumber,
		NetworkExecutionIdentity identity,
		int timeoutMs,
		CancellationToken cancellationToken)
	{
		if (branchNumber != 1 || request.Port == 0 || request.Payload is not { Length: > 0 } ||
			request.ReceiveBufferSize <= 0 || !request.AccessPlan.TryValidate(out _))
		{
			return PrecisionSocketEndpointSupport.Reject(NetworkAccessFailureCodes.SocketRequestInvalid);
		}
		if (!_context.TryResolve(request.AccessPlan, request.SecurityBoundary, out var resolved, out var failure))
			return PrecisionSocketEndpointSupport.Reject(failure.ReasonCode);
		_ = ExecuteAsync(request, retryCount, identity, resolved, timeoutMs, cancellationToken);
		return TrackedAttemptStartResult.Accepted();
	}

	protected override bool IsSuccessfulResponse(NetworkUdpDatagramResponse<TKey> response, out NetworkFailure failure)
		=> PrecisionSocketEndpointSupport.IsSuccessful(response.Result, out failure);
	protected override ProtocolOutcome GetResponseProtocolOutcome(
		NetworkUdpDatagramResponse<TKey> response, bool contractSatisfied, NetworkFailure failure) => response.Result.Outcome;
	protected override AccessCompliance GetResponseAccessCompliance(
		NetworkUdpDatagramResponse<TKey> response, bool contractSatisfied, NetworkFailure failure) => response.Result.Compliance;

	protected override void OnStopping()
	{
		_dataPlane.DatagramDispatched -= OnDatagramDispatched;
		if (_ownsDataPlane) _dataPlane.Dispose();
	}

	private async Task ExecuteAsync(
		NetworkUdpDatagramRequest<TKey> request,
		byte retryCount,
		NetworkExecutionIdentity branchIdentity,
		ResolvedAccessPlan resolved,
		int timeoutMs,
		CancellationToken lifetimeToken)
	{
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
		timeout.CancelAfter(timeoutMs);
		var result = await _dataPlane.ExecuteDatagramAsync(new NetworkSocketExecutionRequest(
			branchIdentity,
			request.AccessPlan,
			resolved,
			request.SecurityBoundary,
			NetworkSocketOperation.UdpExchange,
			request.Port,
			request.Payload,
			request.ReceiveBufferSize), timeout.Token).ConfigureAwait(false);
		result = PrecisionSocketEndpointSupport.NormalizeTimeout(result, timeout, lifetimeToken);
		PublishResponse(new NetworkUdpDatagramResponse<TKey>(
			branchIdentity.CreateResponse(Guid.NewGuid()), request.Key, retryCount, result), branchIdentity.BranchId);
	}

	private void OnDatagramDispatched(NetworkDatagramDispatchEvidence evidence)
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
				// Consumer observers are isolated from each other and from transport execution.
			}
		}
	}
}

public readonly record struct NetworkUdpDatagramRequest<TKey>(
	Guid RequestId,
	TKey Key,
	RequestedAccessPlan AccessPlan,
	NetworkAccessSecurityBoundary SecurityBoundary,
	ushort Port,
	byte[] Payload,
	int ReceiveBufferSize = 65535,
	int? TimeoutMs = null,
	bool AllowRetry = false)
	where TKey : notnull;

public readonly record struct NetworkUdpDatagramResponse<TKey>(
	NetworkExecutionIdentity Identity,
	TKey Key,
	byte RetryCount,
	NetworkSocketExecutionResult Result)
	where TKey : notnull;

internal static class PrecisionSocketEndpointSupport
{
	public static TrackedAttemptStartResult BranchIdentityRequired() =>
		Reject(NetworkAccessFailureCodes.BranchStartNotSupported);

	public static TrackedAttemptStartResult Reject(string? reason) => TrackedAttemptStartResult.Rejected(new(
		NetworkFailureKind.Rejected,
		0,
		string.IsNullOrWhiteSpace(reason) ? NetworkAccessFailureCodes.ConstraintUnresolved : reason,
		default));

	public static bool IsSuccessful(NetworkSocketExecutionResult result, out NetworkFailure failure)
	{
		if (result.Outcome == ProtocolOutcome.Succeeded && result.Compliance == AccessCompliance.Satisfied)
		{
			failure = NetworkFailure.None;
			return true;
		}

		var reason = !string.IsNullOrWhiteSpace(result.ReasonCode)
			? result.ReasonCode
			: result.Compliance == AccessCompliance.Violated
				? NetworkAccessFailureCodes.AccessConstraintViolated
				: NetworkAccessFailureCodes.AccessEvidenceIncomplete;
		var kind = result.Outcome switch
		{
			ProtocolOutcome.Rejected => NetworkFailureKind.Rejected,
			ProtocolOutcome.TimedOut => NetworkFailureKind.Timeout,
			ProtocolOutcome.Cancelled => NetworkFailureKind.Cancelled,
			ProtocolOutcome.ProtocolFailed => NetworkFailureKind.Protocol,
			_ => NetworkFailureKind.Transport,
		};
		failure = new NetworkFailure(kind, result.Error.Code, reason, result.Error);
		return false;
	}

	public static NetworkSocketExecutionResult NormalizeTimeout(
		NetworkSocketExecutionResult result,
		CancellationTokenSource timeout,
		CancellationToken lifetimeToken) =>
		result.Outcome == ProtocolOutcome.Cancelled && timeout.IsCancellationRequested && !lifetimeToken.IsCancellationRequested
			? result with
			{
				Outcome = ProtocolOutcome.TimedOut,
				Error = BinaryNetworkError.FromException(null, false, true),
				ReasonCode = "socket-operation-timed-out",
			}
			: result;
}

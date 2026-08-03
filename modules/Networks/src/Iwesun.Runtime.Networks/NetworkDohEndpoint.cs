using System.Diagnostics;

namespace Iwesun.Runtime.Networks;

public enum NetworkDnsQueryType : ushort
{
	A = 1,
	AAAA = 28,
}

public enum NetworkDohResponseFormat : byte
{
	Unspecified = 0,
	WireMessage = 1,
	Json = 2,
}

/// <summary>Networks 3.0 DNS-over-HTTPS endpoint using the stable HTTP transport pool.</summary>
public sealed class NetworkDohEndpoint<TKey>
	: TrackedRequestReplyEndpointBase<NetworkDohRequest<TKey>, NetworkDohResponse<TKey>, TKey>
	where TKey : notnull
{
	private readonly NetworkAccessExecutionContext _context;
	private readonly NetworkRouteAdapterRegistry? _routeAdapters;
	private readonly System.Net.IWebProxy? _systemProxy;
	private readonly NetworkHttpConnectionPool _pool;
	protected override bool StartAttemptsConcurrently => true;

	public NetworkDohEndpoint(
		NetworkAccessExecutionContext? context = null,
		NetworkRouteAdapterRegistry? routeAdapters = null,
		System.Net.IWebProxy? systemProxy = null,
		int maxSendQueueLength = 65536,
		int maxReceiveQueueLength = 65536,
		int maxAttemptCount = 3,
		int defaultTimeoutMs = 5000,
		TimeSpan? idleConnectionRetention = null,
		int maxConnectionPoolCount = 1024,
		int connectionPoolSweepInterval = 64)
		: base(maxSendQueueLength, maxReceiveQueueLength, maxAttemptCount, defaultTimeoutMs)
	{
		_context = context ?? NetworkAccessExecutionContext.CreatePlatformDefault();
		_routeAdapters = routeAdapters;
		_systemProxy = systemProxy ?? HttpClient.DefaultProxy;
		_pool = new NetworkHttpConnectionPool(
			idleConnectionRetention ?? TimeSpan.FromMinutes(2),
			maxConnectionPoolCount,
			connectionPoolSweepInterval);
	}

	public int ConnectionPoolCount => _pool.Count;

	protected override Guid GetRequestId(NetworkDohRequest<TKey> request) => request.RequestId;
	protected override TKey GetRequestKey(NetworkDohRequest<TKey> request) => request.Key;
	protected override Guid GetResponseRequestId(NetworkDohResponse<TKey> response) => response.Identity.RequestId;
	protected override TKey GetResponseKey(NetworkDohResponse<TKey> response) => response.Key;
	protected override NetworkExecutionIdentity GetResponseIdentity(NetworkDohResponse<TKey> response) => response.Identity;
	protected override int? GetRequestTimeoutOverrideMs(NetworkDohRequest<TKey> request) => request.TimeoutMs;
	protected override byte? GetResponseRetryCount(NetworkDohResponse<TKey> response) => response.RetryCount;
	protected override bool CanRetry(NetworkDohRequest<TKey> request, NetworkFailure failure) => request.AllowRetry;

	protected override TrackedAttemptStartResult OnStartAttempt(
		NetworkDohRequest<TKey> request, byte retryCount,
		int timeoutMs, CancellationToken cancellationToken) => PrecisionSocketEndpointSupport.BranchIdentityRequired();

	protected override TrackedAttemptStartResult OnStartBranch(
		NetworkDohRequest<TKey> request,
		byte retryCount,
		byte branchNumber,
		NetworkExecutionIdentity identity,
		int timeoutMs,
		CancellationToken cancellationToken)
	{
		if (branchNumber != 1 || !Uri.TryCreate(request.UpstreamUrl, UriKind.Absolute, out var uri) ||
			uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrWhiteSpace(request.Domain) ||
			request.Format == NetworkDohResponseFormat.Unspecified || !request.Protocol.IsValid ||
			!NetworkHttpGetEndpoint<TKey>.ProtocolMatchesUri(request.Protocol, uri) ||
			!request.AccessPlan.TryValidate(out _) ||
			request.AccessPlan.Destination.Kind != NetworkSelectorKind.Exact)
		{
			return PrecisionSocketEndpointSupport.Reject(NetworkAccessFailureCodes.DohRequestInvalid);
		}
		if (request.AccessPlan.PathProvider == NetworkPathProvider.SystemProxy &&
			!NetworkHttpGetEndpoint<TKey>.SupportsSystemProxyPlan(request.AccessPlan))
			return PrecisionSocketEndpointSupport.Reject(NetworkAccessFailureCodes.SystemProxyConstraintUnsupported);
		if (request.AccessPlan.PathProvider == NetworkPathProvider.Direct &&
			request.AccessPlan.NextHop.Kind == NetworkSelectorKind.Exact && !request.AccessPlan.NextHop.IsOnLink)
		{
			if (request.Protocol.ReusePolicy != NetworkHttpConnectionReusePolicy.NoReuseRequestPolicy)
				return PrecisionSocketEndpointSupport.Reject(NetworkAccessFailureCodes.WfpPolicyConnectionReuseUnsupported);
			var capability = _context.SocketExecutor.QueryWfpCapability();
			if (!capability.Supported) return PrecisionSocketEndpointSupport.Reject(capability.ReasonCode);
		}
		if (request.AccessPlan.RouteScope.Kind == NetworkRouteScopeSelectionKind.ExactCompartment)
			return PrecisionSocketEndpointSupport.Reject(NetworkAccessFailureCodes.RouteScopeBackendUnavailable);
		var pathProviderIdentity = GetPathProviderIdentity(request.AccessPlan.PathProvider, uri);
		if (!NetworkHttpConnectionPoolKey.TryCreate(
			request.AccessPlan, request.Protocol, request.ResolverIdentity, pathProviderIdentity,
			out var poolKey, out var reason))
			return PrecisionSocketEndpointSupport.Reject(reason);
		var security = request.Protocol.ToSecurityBoundary(
			request.AccessPlan.PathProvider is NetworkPathProvider.RouteAdapter or NetworkPathProvider.SystemProxy
				? NetworkAccessLeg.ClientLeg : NetworkAccessLeg.Direct,
			request.ResolverIdentity);
		if (!_context.TryResolve(request.AccessPlan, security, out var resolved, out var failure))
			return PrecisionSocketEndpointSupport.Reject(failure.ReasonCode);
		if (request.AccessPlan.PathProvider == NetworkPathProvider.RouteAdapter &&
			(_routeAdapters is null || !_routeAdapters.TryResolve(
				request.AccessPlan.RouteAdapter,
				NetworkRouteAdapterTransportCapabilities.ByteStream,
				out _, out reason)))
			return PrecisionSocketEndpointSupport.Reject(reason);
		_ = ExecuteAsync(request, retryCount, identity, poolKey, resolved, timeoutMs, cancellationToken);
		return TrackedAttemptStartResult.Accepted();
	}

	protected override bool IsSuccessfulResponse(NetworkDohResponse<TKey> response, out NetworkFailure failure)
	{
		if (response.Succeeded)
		{
			failure = NetworkFailure.None;
			return true;
		}
		var kind = response.Outcome switch
		{
			ProtocolOutcome.TimedOut => NetworkFailureKind.Timeout,
			ProtocolOutcome.Cancelled => NetworkFailureKind.Cancelled,
			ProtocolOutcome.TransportFailed => NetworkFailureKind.Transport,
			_ when response.HttpStatusCode is < 200 or >= 300 => NetworkFailureKind.Remote,
			_ => NetworkFailureKind.Protocol,
		};
		failure = new NetworkFailure(kind, response.HttpStatusCode, response.ReasonCode, response.Error);
		return false;
	}

	protected override ProtocolOutcome GetResponseProtocolOutcome(
		NetworkDohResponse<TKey> response, bool contractSatisfied, NetworkFailure failure) => response.Outcome;

	protected override AccessCompliance GetResponseAccessCompliance(
		NetworkDohResponse<TKey> response, bool contractSatisfied, NetworkFailure failure) => response.Compliance;

	protected override void OnStopping()
	{
		_pool.Dispose();
	}

	private async Task ExecuteAsync(
		NetworkDohRequest<TKey> request,
		byte retryCount,
		NetworkExecutionIdentity identity,
		NetworkHttpConnectionPoolKey poolKey,
		ResolvedAccessPlan resolved,
		int timeoutMs,
		CancellationToken lifetimeToken)
	{
		var started = Stopwatch.GetTimestamp();
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
		timeout.CancelAfter(timeoutMs);
		try
		{
			using var lease = request.Protocol.ReusePolicy == NetworkHttpConnectionReusePolicy.NoReuseRequestPolicy
				? _pool.RentTransient(() => CreateEntry(request, identity, resolved))
				: _pool.Rent(
					poolKey,
					resolved,
					current => _context.Resolver.IsCurrent(current),
					() => CreateEntry(request, identity, resolved));
			var entry = lease.Entry;
			var normalizedDomain = request.Domain.Trim().TrimEnd('.').ToLowerInvariant();
			var uri = BuildQueryUri(request.UpstreamUrl, normalizedDomain, request.QueryType, request.Format);
			using var message = new HttpRequestMessage(HttpMethod.Get, uri);
			message.Version = new Version(request.Protocol.HttpMajorVersion, request.Protocol.HttpMajorVersion == 2 ? 0 : 1);
			message.Headers.TryAddWithoutValidation("Accept",
				request.Format == NetworkDohResponseFormat.WireMessage ? "application/dns-message" : "application/dns-json");
			if (request.Protocol.ReusePolicy == NetworkHttpConnectionReusePolicy.NoReuseRequestPolicy)
				message.Headers.ConnectionClose = true;
			using var response = await entry.Client.SendAsync(
				message, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
			var body = await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);
			var status = (int)response.StatusCode;
			uint ttl = 0;
			IpAddressValue[] addresses = [];
			if (status is >= 200 and < 300)
			{
				Span<IpAddressValue> buffer = stackalloc IpAddressValue[DnsWireCodec.MaxAnswers];
				var count = request.Format == NetworkDohResponseFormat.WireMessage
					? DnsWireCodec.ParseResponse(body, buffer, out ttl)
					: DnsWireCodec.ParseJsonResponse(body, buffer, out ttl);
				addresses = buffer[..count].ToArray();
			}
			var evidence = entry.GetEvidence(identity);
			var compliance = entry.Compliance;
			var succeeded = status is >= 200 and < 300 && addresses.Length > 0 &&
				compliance == AccessCompliance.Satisfied;
			var outcome = status is < 200 or >= 300 || addresses.Length == 0
				? ProtocolOutcome.ProtocolFailed : ProtocolOutcome.Succeeded;
			Publish(request, retryCount, identity, status, addresses, ttl, succeeded, outcome, compliance,
				evidence, default, succeeded ? string.Empty : addresses.Length == 0
					? NetworkAccessFailureCodes.DohAnswerEmpty : $"http-status-{status}", started);
		}
		catch (Exception ex)
		{
			var timedOut = timeout.IsCancellationRequested && !lifetimeToken.IsCancellationRequested;
			Publish(request, retryCount, identity, 0, [], 0, false,
				timedOut ? ProtocolOutcome.TimedOut : lifetimeToken.IsCancellationRequested
					? ProtocolOutcome.Cancelled : ProtocolOutcome.TransportFailed,
				AccessCompliance.EvidenceIncomplete, default,
				BinaryNetworkError.FromException(ex, lifetimeToken.IsCancellationRequested, timedOut),
				timedOut ? "doh-timeout" : "doh-transport-failed", started);
		}
	}

	private NetworkHttpPoolEntry CreateEntry(
		NetworkDohRequest<TKey> request,
		NetworkExecutionIdentity identity,
		ResolvedAccessPlan resolved) => new(
		identity, request.AccessPlan, resolved, request.Protocol, _context.SocketExecutor, _routeAdapters, _systemProxy);

	private void Publish(
		NetworkDohRequest<TKey> request,
		byte retryCount,
		NetworkExecutionIdentity identity,
		int status,
		IpAddressValue[] addresses,
		uint ttl,
		bool succeeded,
		ProtocolOutcome outcome,
		AccessCompliance compliance,
		NetworkHttpPathEvidence evidence,
		BinaryNetworkError error,
		string reason,
		long started) => PublishResponse(new NetworkDohResponse<TKey>(
		identity.CreateResponse(Guid.NewGuid()), request.Key, retryCount, request.Domain, request.QueryType,
		status, addresses, ttl, succeeded, outcome, compliance, evidence, error, reason,
		ClampToUInt(Stopwatch.GetElapsedTime(started).TotalMilliseconds),
		DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), identity.BranchId);

	private static Uri BuildQueryUri(
		string upstream,
		string domain,
		NetworkDnsQueryType queryType,
		NetworkDohResponseFormat format)
	{
		var builder = new UriBuilder(upstream);
		var parameter = format == NetworkDohResponseFormat.WireMessage
			? $"dns={DnsWireCodec.BuildQueryBase64Url(domain, (ushort)queryType)}"
			: $"name={Uri.EscapeDataString(domain)}&type={(ushort)queryType}";
		builder.Query = string.IsNullOrEmpty(builder.Query)
			? parameter
			: $"{builder.Query.TrimStart('?')}&{parameter}";
		return builder.Uri;
	}

	private string GetPathProviderIdentity(NetworkPathProvider provider, Uri target)
	{
		if (provider != NetworkPathProvider.SystemProxy || _systemProxy is null) return string.Empty;
		if (_systemProxy.IsBypassed(target)) return "system-proxy:bypass";
		var proxy = _systemProxy.GetProxy(target);
		return proxy is null
			? "system-proxy:unresolved"
			: $"system-proxy:{proxy.Scheme.ToLowerInvariant()}://{proxy.IdnHost.ToLowerInvariant()}:{proxy.Port}";
	}

	private static uint ClampToUInt(double value) =>
		value <= 0 ? 0 : value >= uint.MaxValue ? uint.MaxValue : (uint)Math.Round(value);
}

public readonly record struct NetworkDohRequest<TKey>(
	Guid RequestId,
	TKey Key,
	string UpstreamUrl,
	string Domain,
	NetworkDnsQueryType QueryType,
	NetworkDohResponseFormat Format,
	NetworkHttpProtocolIdentity Protocol,
	RequestedAccessPlan AccessPlan,
	string ResolverIdentity,
	int? TimeoutMs = null,
	bool AllowRetry = true)
	where TKey : notnull;

public readonly record struct NetworkDohResponse<TKey>(
	NetworkExecutionIdentity Identity,
	TKey Key,
	byte RetryCount,
	string Domain,
	NetworkDnsQueryType QueryType,
	int HttpStatusCode,
	IpAddressValue[] Addresses,
	uint Ttl,
	bool Succeeded,
	ProtocolOutcome Outcome,
	AccessCompliance Compliance,
	NetworkHttpPathEvidence PathEvidence,
	BinaryNetworkError Error,
	string ReasonCode,
	uint ElapsedMs,
	long CompletedAtUnixMs)
	where TKey : notnull;

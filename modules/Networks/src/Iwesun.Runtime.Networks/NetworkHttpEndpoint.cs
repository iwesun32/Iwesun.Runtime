using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Iwesun.Runtime.Networks;

public readonly record struct NetworkHttpExecutionResult(
	NetworkExecutionIdentity Identity,
	ProtocolOutcome Outcome,
	AccessCompliance Compliance,
	NetworkHttpPathEvidence PathEvidence,
	int StatusCode,
	byte[] Body,
	BinaryNetworkError Error,
	string ReasonCode,
	uint ElapsedMs,
	long CompletedAtUnixMs)
{
	public bool IsSuccess => Outcome == ProtocolOutcome.Succeeded && Compliance == AccessCompliance.Satisfied;
}

/// <summary>Networks 3.0 tracked HTTP GET endpoint with stable-semantic connection pooling.</summary>
public sealed class NetworkHttpGetEndpoint<TKey>
	: TrackedRequestReplyEndpointBase<NetworkHttpGetRequest<TKey>, NetworkHttpGetResponse<TKey>, TKey>
	where TKey : notnull
{
	private readonly NetworkAccessExecutionContext _context;
	private readonly NetworkRouteAdapterRegistry? _routeAdapters;
	private readonly IWebProxy? _systemProxy;
	private readonly NetworkHttpConnectionPool _pool;
	private int _activeExecutionCount;
	protected override bool StartAttemptsConcurrently => true;

	public NetworkHttpGetEndpoint(
		NetworkAccessExecutionContext? context = null,
		NetworkRouteAdapterRegistry? routeAdapters = null,
		IWebProxy? systemProxy = null,
		int maxSendQueueLength = 4096,
		int maxReceiveQueueLength = 4096,
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
	/// <summary>Active HTTP executions, including cancellation until I/O cleanup completes.</summary>
	public int ActiveExecutionCount => Volatile.Read(ref _activeExecutionCount);

	protected override Guid GetRequestId(NetworkHttpGetRequest<TKey> request) => request.RequestId;
	protected override TKey GetRequestKey(NetworkHttpGetRequest<TKey> request) => request.Key;
	protected override Guid GetResponseRequestId(NetworkHttpGetResponse<TKey> response) => response.Identity.RequestId;
	protected override TKey GetResponseKey(NetworkHttpGetResponse<TKey> response) => response.Key;
	protected override NetworkExecutionIdentity GetResponseIdentity(NetworkHttpGetResponse<TKey> response) => response.Identity;
	protected override int? GetRequestTimeoutOverrideMs(NetworkHttpGetRequest<TKey> request) => request.TimeoutMs;
	protected override byte? GetResponseRetryCount(NetworkHttpGetResponse<TKey> response) => response.RetryCount;
	protected override bool CanRetry(NetworkHttpGetRequest<TKey> request, NetworkFailure failure) =>
		request.AllowRetry && !request.CancellationToken.IsCancellationRequested &&
		failure.Kind is not (NetworkFailureKind.Cancelled or NetworkFailureKind.Stopped);

	protected override TrackedAttemptStartResult OnStartAttempt(
		NetworkHttpGetRequest<TKey> request, byte retryCount,
		int timeoutMs, CancellationToken cancellationToken) => PrecisionSocketEndpointSupport.BranchIdentityRequired();

	protected override TrackedAttemptStartResult OnStartBranch(
		NetworkHttpGetRequest<TKey> request,
		byte retryCount,
		byte branchNumber,
		NetworkExecutionIdentity identity,
		int timeoutMs,
		CancellationToken cancellationToken)
	{
		if (request.CancellationToken.IsCancellationRequested)
		{
			Publish(request, retryCount, identity, new NetworkHttpExecutionResult(identity,
				ProtocolOutcome.Cancelled, AccessCompliance.NotApplicable, default, 0, [], default,
				"http-cancelled", 0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
			return TrackedAttemptStartResult.Accepted();
		}
		if (branchNumber != 1 || !Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) ||
			!request.AccessPlan.TryValidate(out _) || !request.Protocol.IsValid ||
			!ProtocolMatchesUri(request.Protocol, uri) ||
			request.AccessPlan.Destination.Kind != NetworkSelectorKind.Exact)
		{
			return PrecisionSocketEndpointSupport.Reject(NetworkAccessFailureCodes.HttpRequestInvalid);
		}
		var pathProviderIdentity = GetPathProviderIdentity(request.AccessPlan.PathProvider, uri);
		if (!NetworkHttpConnectionPoolKey.TryCreate(
			request.AccessPlan, request.Protocol, request.ResolverIdentity, pathProviderIdentity,
			out var poolKey, out var reason))
		{
			return PrecisionSocketEndpointSupport.Reject(reason);
		}
		if (request.AccessPlan.PathProvider == NetworkPathProvider.SystemProxy &&
			!SupportsSystemProxyPlan(request.AccessPlan))
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
		var security = request.Protocol.ToSecurityBoundary(
			request.AccessPlan.PathProvider is NetworkPathProvider.RouteAdapter or NetworkPathProvider.SystemProxy
				? NetworkAccessLeg.ClientLeg
				: NetworkAccessLeg.Direct,
			request.ResolverIdentity);
		if (!_context.TryResolve(request.AccessPlan, security, out var resolved, out var failure))
			return PrecisionSocketEndpointSupport.Reject(failure.ReasonCode);
		if (request.AccessPlan.PathProvider == NetworkPathProvider.RouteAdapter &&
			(_routeAdapters is null || !_routeAdapters.TryResolve(
				request.AccessPlan.RouteAdapter,
				NetworkRouteAdapterTransportCapabilities.ByteStream,
				out _, out reason)))
		{
			return PrecisionSocketEndpointSupport.Reject(reason);
		}

		_ = ExecuteAsync(request, retryCount, identity, poolKey, resolved, timeoutMs, cancellationToken);
		return TrackedAttemptStartResult.Accepted();
	}

	protected override bool IsSuccessfulResponse(NetworkHttpGetResponse<TKey> response, out NetworkFailure failure)
	{
		if (response.Result.IsSuccess && response.Result.StatusCode is >= 200 and < 300)
		{
			failure = NetworkFailure.None;
			return true;
		}
		var kind = response.Result.Outcome switch
		{
			ProtocolOutcome.Rejected => NetworkFailureKind.Rejected,
			ProtocolOutcome.TimedOut => NetworkFailureKind.Timeout,
			ProtocolOutcome.Cancelled => NetworkFailureKind.Cancelled,
			ProtocolOutcome.TransportFailed => NetworkFailureKind.Transport,
			_ when response.Result.StatusCode is < 200 or >= 300 => NetworkFailureKind.Remote,
			_ => NetworkFailureKind.Protocol,
		};
		failure = new NetworkFailure(kind, response.Result.StatusCode,
			response.Result.ReasonCode, response.Result.Error);
		return false;
	}

	protected override ProtocolOutcome GetResponseProtocolOutcome(
		NetworkHttpGetResponse<TKey> response, bool contractSatisfied, NetworkFailure failure) => response.Result.Outcome;

	protected override AccessCompliance GetResponseAccessCompliance(
		NetworkHttpGetResponse<TKey> response, bool contractSatisfied, NetworkFailure failure) => response.Result.Compliance;

	protected override void OnStopping()
	{
		_pool.Dispose();
	}

	private async Task ExecuteAsync(
		NetworkHttpGetRequest<TKey> request,
		byte retryCount,
		NetworkExecutionIdentity identity,
		NetworkHttpConnectionPoolKey poolKey,
		ResolvedAccessPlan resolved,
		int timeoutMs,
		CancellationToken lifetimeToken)
	{
		var started = Stopwatch.GetTimestamp();
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken, request.CancellationToken);
		timeout.CancelAfter(timeoutMs);
		Interlocked.Increment(ref _activeExecutionCount);
		try
		{
			timeout.Token.ThrowIfCancellationRequested();
			using var lease = request.Protocol.ReusePolicy == NetworkHttpConnectionReusePolicy.NoReuseRequestPolicy
				? _pool.RentTransient(() => CreateEntry(request, identity, resolved))
				: _pool.Rent(
					poolKey,
					resolved,
					current => _context.Resolver.IsCurrent(current),
					() => CreateEntry(request, identity, resolved));
			var entry = lease.Entry;
			using var message = new HttpRequestMessage(HttpMethod.Get, request.Url);
			message.Version = new Version(request.Protocol.HttpMajorVersion, request.Protocol.HttpMajorVersion == 2 ? 0 : 1);
			if (!string.IsNullOrWhiteSpace(request.AcceptHeader))
				message.Headers.TryAddWithoutValidation("Accept", request.AcceptHeader);
			if (request.Headers is not null)
				foreach (var header in request.Headers) message.Headers.TryAddWithoutValidation(header.Key, header.Value);
			if (request.Protocol.ReusePolicy == NetworkHttpConnectionReusePolicy.NoReuseRequestPolicy)
				message.Headers.ConnectionClose = true;
			using var response = await entry.Client.SendAsync(
				message, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
			var body = await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);
			var status = (int)response.StatusCode;
			var outcome = status is >= 200 and < 300 ? ProtocolOutcome.Succeeded : ProtocolOutcome.ProtocolFailed;
			var evidence = entry.GetEvidence(identity);
			var compliance = entry.Compliance;
			Publish(request, retryCount, identity, new NetworkHttpExecutionResult(
				identity, outcome, compliance, evidence, status, body, default,
				status is >= 200 and < 300 ? string.Empty : $"http-status-{status}",
				ClampToUInt(Stopwatch.GetElapsedTime(started).TotalMilliseconds),
				DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
		}
		catch (Exception ex)
		{
			var cancelled = lifetimeToken.IsCancellationRequested || request.CancellationToken.IsCancellationRequested;
			var timedOut = timeout.IsCancellationRequested && !cancelled;
			var outcome = timedOut ? ProtocolOutcome.TimedOut :
				cancelled ? ProtocolOutcome.Cancelled : ProtocolOutcome.TransportFailed;
			Publish(request, retryCount, identity, new NetworkHttpExecutionResult(
				identity, outcome, AccessCompliance.EvidenceIncomplete, default, 0, [],
				BinaryNetworkError.FromException(ex, cancelled, timedOut),
				ClassifyTransportFailure(ex, timedOut, cancelled),
				ClampToUInt(Stopwatch.GetElapsedTime(started).TotalMilliseconds),
				DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
		}
		finally { Interlocked.Decrement(ref _activeExecutionCount); }
	}

	private static string ClassifyTransportFailure(
		Exception exception,
		bool timedOut,
		bool cancelled)
	{
		if (timedOut) return "http-timeout";
		if (cancelled) return "http-cancelled";
		var hasSocketFailure = false;
		for (Exception? current = exception; current is not null; current = current.InnerException)
		{
			if (current is HttpRequestException
				{
					HttpRequestError: HttpRequestError.SecureConnectionError
				})
				return "http-tls-failed";
			if (current is AuthenticationException) return "http-tls-failed";
			if (current is SocketException) hasSocketFailure = true;
		}
		return hasSocketFailure ? "http-socket-failed" : "http-transport-failed";
	}

	private NetworkHttpPoolEntry CreateEntry(
		NetworkHttpGetRequest<TKey> request,
		NetworkExecutionIdentity identity,
		ResolvedAccessPlan resolved) => new(
		identity, request.AccessPlan, resolved, request.Protocol, _context.SocketExecutor, _routeAdapters, _systemProxy);

	private void Publish(
		NetworkHttpGetRequest<TKey> request,
		byte retryCount,
		NetworkExecutionIdentity identity,
		NetworkHttpExecutionResult result) => PublishResponse(
		new NetworkHttpGetResponse<TKey>(identity.CreateResponse(Guid.NewGuid()), request.Key, retryCount, result),
		identity.BranchId);

	internal static bool ProtocolMatchesUri(NetworkHttpProtocolIdentity protocol, Uri uri)
	{
		var host = uri.IdnHost.Trim().TrimEnd('.').ToLowerInvariant();
		var port = checked((ushort)(uri.IsDefaultPort
			? uri.Scheme == Uri.UriSchemeHttps ? 443 : 80
			: uri.Port));
		if (!StringComparer.Ordinal.Equals(protocol.Scheme, uri.Scheme.ToLowerInvariant()) ||
			!StringComparer.Ordinal.Equals(protocol.LogicalHost, host) || protocol.Port != port) return false;
		return protocol.Scheme != Uri.UriSchemeHttps ||
			(StringComparer.Ordinal.Equals(protocol.ServerNameIndication, host) &&
			 StringComparer.Ordinal.Equals(protocol.CertificateValidationName, host));
	}

	internal static bool SupportsSystemProxyPlan(in RequestedAccessPlan plan) =>
		plan.Destination.Kind == NetworkSelectorKind.Exact &&
		plan.Source.Kind == NetworkSelectorKind.SystemSelected &&
		plan.Interface.Kind == NetworkSelectorKind.SystemSelected &&
		plan.NextHop.Kind == NetworkSelectorKind.SystemSelected &&
		plan.LocalEndpoint.Kind is NetworkLocalEndpointSelectionKind.Ephemeral or
			NetworkLocalEndpointSelectionKind.SystemSelected &&
		plan.RouteScope.Kind == NetworkRouteScopeSelectionKind.CurrentScope &&
		plan.IpPacketPolicy.Kind == NetworkIpPacketPolicyKind.SystemDefault;

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

public readonly record struct NetworkHttpGetRequest<TKey>(
	Guid RequestId,
	TKey Key,
	string Url,
	NetworkHttpProtocolIdentity Protocol,
	RequestedAccessPlan AccessPlan,
	string ResolverIdentity,
	string AcceptHeader = "application/json",
	IReadOnlyList<KeyValuePair<string, string>>? Headers = null,
	int? TimeoutMs = null,
	bool AllowRetry = true)
	where TKey : notnull
{
	/// <summary>Request-local cancellation. Never part of the stable connection pool identity.</summary>
	public CancellationToken CancellationToken { get; init; }
}

public readonly record struct NetworkHttpGetResponse<TKey>(
	NetworkExecutionIdentity Identity,
	TKey Key,
	byte RetryCount,
	NetworkHttpExecutionResult Result)
	where TKey : notnull;

internal sealed class NetworkHttpPoolEntry : IDisposable
{
	private readonly object _gate = new();
	private NetworkRouteLegEvidence _clientLeg;
	private NetworkRouteLegEvidence _egressLeg;
	private AccessCompliance _clientCompliance = AccessCompliance.EvidenceIncomplete;
	private readonly NetworkExecutionIdentity _creationIdentity;
	private int _leaseCount;
	private long _lastUsedTick = Environment.TickCount64;
	private bool _retired;
	private bool _disposed;

	public NetworkHttpPoolEntry(
		NetworkExecutionIdentity identity,
		RequestedAccessPlan requested,
		ResolvedAccessPlan resolved,
		NetworkHttpProtocolIdentity protocol,
		NetworkSocketExecutor socketExecutor,
		NetworkRouteAdapterRegistry? adapters,
		IWebProxy? systemProxy)
	{
		_creationIdentity = identity;
		Resolved = resolved;
		var handler = new SocketsHttpHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
			UseProxy = requested.PathProvider == NetworkPathProvider.SystemProxy,
			ConnectTimeout = TimeSpan.FromSeconds(10),
		};
		if (systemProxy is not null) handler.Proxy = systemProxy;
		if (!string.IsNullOrEmpty(protocol.CertificatePinSha256))
		{
			handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
				ValidatePinnedCertificate(certificate, errors, protocol.CertificatePinSha256);
		}
		if (requested.PathProvider is NetworkPathProvider.Automatic or NetworkPathProvider.Direct)
		handler.ConnectCallback = (context, token) => ConnectDirectAsync(
			context, token, requested, resolved, socketExecutor);
		else if (requested.PathProvider == NetworkPathProvider.RouteAdapter)
			handler.ConnectCallback = (context, token) => ConnectAdapterAsync(token, requested, resolved, protocol, adapters);
		else if (requested.PathProvider == NetworkPathProvider.SystemProxy)
		{
			handler.ConnectCallback = (context, token) =>
				ConnectSystemProxyAsync(context, token, resolved, protocol);
			_egressLeg = NetworkRouteLegEvidence.NotApplicable(
				identity, NetworkAccessLeg.EgressLeg, requested.PathProvider);
		}
		else
		{
			_clientLeg = NetworkRouteLegEvidence.NotApplicable(identity, NetworkAccessLeg.ClientLeg, requested.PathProvider);
			_egressLeg = NetworkRouteLegEvidence.NotApplicable(identity, NetworkAccessLeg.EgressLeg, requested.PathProvider);
			_clientCompliance = AccessCompliance.EvidenceIncomplete;
		}
		Client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
	}

	public HttpClient Client { get; }
	public ResolvedAccessPlan Resolved { get; }
	public long LastUsedTick
	{
		get
		{
			lock (_gate)
			{
				return _lastUsedTick;
			}
		}
	}
	public AccessCompliance Compliance
	{
		get
		{
			lock (_gate)
			{
				return _clientCompliance;
			}
		}
	}

	public NetworkHttpPathEvidence GetEvidence(NetworkExecutionIdentity identity)
	{
		lock (_gate)
		{
			return new NetworkHttpPathEvidence(
				identity,
				Reparent(_clientLeg, identity),
				Reparent(_egressLeg, identity));
		}
	}

	public bool TryAcquire(long now)
	{
		lock (_gate)
		{
			if (_retired || _disposed) return false;
			_leaseCount++;
			_lastUsedTick = now;
			return true;
		}
	}

	public bool CanRetire(long now, TimeSpan idleRetention, bool requireExpiration)
	{
		lock (_gate)
		{
			return !_retired && !_disposed && _leaseCount == 0 &&
				(!requireExpiration || now - _lastUsedTick >= idleRetention.TotalMilliseconds);
		}
	}

	public void Release()
	{
		var dispose = false;
		lock (_gate)
		{
			if (_leaseCount <= 0) return;
			_leaseCount--;
			_lastUsedTick = Environment.TickCount64;
			if (_retired && _leaseCount == 0 && !_disposed)
			{
				_disposed = true;
				dispose = true;
			}
		}
		if (dispose) Client.Dispose();
	}

	public void Retire()
	{
		var dispose = false;
		lock (_gate)
		{
			if (_retired) return;
			_retired = true;
			if (_leaseCount == 0 && !_disposed)
			{
				_disposed = true;
				dispose = true;
			}
		}
		if (dispose) Client.Dispose();
	}

	public void Dispose() => Retire();

	private static bool ValidatePinnedCertificate(
		X509Certificate? certificate,
		SslPolicyErrors errors,
		string expectedPin)
	{
		if (certificate is null ||
			(errors & (SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateNotAvailable)) != 0)
		{
			return false;
		}
		using var certificate2 = certificate as X509Certificate2 ?? new X509Certificate2(certificate);
		var actual = Convert.ToHexString(SHA256.HashData(certificate2.RawData)).ToLowerInvariant();
		return StringComparer.Ordinal.Equals(actual, expectedPin);
	}

	private async ValueTask<Stream> ConnectDirectAsync(
		SocketsHttpConnectionContext context,
		CancellationToken cancellationToken,
		RequestedAccessPlan requested,
		ResolvedAccessPlan resolved,
		NetworkSocketExecutor socketExecutor)
	{
		var socket = new Socket(
			resolved.Destination.IsIPv4 ? AddressFamily.InterNetwork : AddressFamily.InterNetworkV6,
			SocketType.Stream,
			ProtocolType.Tcp);
		INetworkWfpConnectionPolicyLease? policy = null;
		try
		{
			var executionRequest = new NetworkSocketExecutionRequest(
				_creationIdentity, requested, resolved,
				new NetworkAccessSecurityBoundary(NetworkAccessLeg.Direct, context.DnsEndPoint.Host,
					context.DnsEndPoint.Host, context.DnsEndPoint.Host, string.Empty),
				NetworkSocketOperation.TcpConnect, checked((ushort)context.DnsEndPoint.Port), [], 0);
			NetworkSocketExecutor.ApplyPlan(socket, executionRequest);
			policy = socketExecutor.AcquireWfpPolicyIfRequired(
				socket, executionRequest, ProtocolType.Tcp, out var policyFailure);
			if (!string.IsNullOrEmpty(policyFailure.ReasonCode))
				throw new InvalidOperationException(policyFailure.ReasonCode);
			var policyOwned = policy is not null;
			await socket.ConnectAsync(new IPEndPoint(
					NetworkIpAddressInterop.ToSystemAddress(
						resolved.Destination,
						resolved.Interface.InterfaceIndex),
					context.DnsEndPoint.Port),
				cancellationToken).ConfigureAwait(false);
			var execution = NetworkSocketExecutor.CreateSuccessResult(
				executionRequest, socket, [], ProofKind.Observed, policyEnforced: policyOwned);
			lock (_gate)
			{
				_clientLeg = new NetworkRouteLegEvidence(NetworkAccessLeg.Direct, execution.Evidence, "socket");
				_egressLeg = NetworkRouteLegEvidence.NotApplicable(
					_creationIdentity, NetworkAccessLeg.EgressLeg, requested.PathProvider);
				_clientCompliance = execution.Compliance;
			}
			var stream = new NetworkStream(socket, ownsSocket: true);
			if (policy is null) return stream;
			var owned = new NetworkWfpPolicyOwnedStream(stream, policy);
			policy = null;
			return owned;
		}
		catch
		{
			try
			{
				socket.Dispose();
			}
			finally
			{
				policy?.Dispose();
			}
			throw;
		}
	}

	private async ValueTask<Stream> ConnectAdapterAsync(
		CancellationToken cancellationToken,
		RequestedAccessPlan requested,
		ResolvedAccessPlan resolved,
		NetworkHttpProtocolIdentity protocol,
		NetworkRouteAdapterRegistry? adapters)
	{
		var reason = NetworkAccessFailureCodes.RouteAdapterCapabilityVersionMismatch;
		if (adapters is null || !adapters.TryResolve(
			requested.RouteAdapter, NetworkRouteAdapterTransportCapabilities.ByteStream,
			out var adapter, out reason) || adapter is null)
		{
			throw new InvalidOperationException(reason);
		}
		var connection = await adapter.ConnectAsync(
			new NetworkRouteAdapterConnectRequest(_creationIdentity, protocol, requested, resolved),
			cancellationToken).ConfigureAwait(false);
		if (!connection.IsValid) throw new InvalidOperationException(NetworkAccessFailureCodes.RouteAdapterConnectionInvalid);
		lock (_gate)
		{
			_clientLeg = connection.ClientLeg;
			_egressLeg = connection.EgressLeg;
			_clientCompliance = connection.Compliance;
		}
		return connection.Stream;
	}

	private async ValueTask<Stream> ConnectSystemProxyAsync(
		SocketsHttpConnectionContext context,
		CancellationToken cancellationToken,
		ResolvedAccessPlan resolved,
		NetworkHttpProtocolIdentity protocol)
	{
		var directTarget = StringComparer.OrdinalIgnoreCase.Equals(
			context.DnsEndPoint.Host.TrimEnd('.'), protocol.LogicalHost);
		var addresses = directTarget
			? new[]
			{
				NetworkIpAddressInterop.ToSystemAddress(
					resolved.Destination,
					resolved.Interface.InterfaceIndex)
			}
			: await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken).ConfigureAwait(false);
		Exception? lastError = null;
		foreach (var address in addresses)
		{
			var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
			try
			{
				await socket.ConnectAsync(
					new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken).ConfigureAwait(false);
				var local = (IPEndPoint)socket.LocalEndPoint!;
				var remote = (IPEndPoint)socket.RemoteEndPoint!;
				var actualInterface = ResolveInterface(local.Address);
				var interfaceProof = actualInterface.IsValid ? ProofKind.Observed : ProofKind.Unavailable;
				var interfaceCompliance = actualInterface.IsValid
					? AccessCompliance.Satisfied : AccessCompliance.EvidenceIncomplete;
				var evidence = new ActualAccessEvidence(
					_creationIdentity,
					new(NetworkPathProvider.SystemProxy, ProofKind.PolicyEnforced, AccessCompliance.Satisfied),
					new(NetworkIpAddressInterop.FromSystemAddress(remote.Address), ProofKind.Observed, AccessCompliance.Satisfied),
					new(NetworkIpAddressInterop.FromSystemAddress(local.Address), ProofKind.Observed, AccessCompliance.Satisfied),
					new(actualInterface, interfaceProof, interfaceCompliance),
					new(default, ProofKind.Unavailable, AccessCompliance.NotApplicable),
					new(checked((ushort)local.Port), ProofKind.Observed, AccessCompliance.Satisfied),
					new(0U, ProofKind.Unavailable, AccessCompliance.NotApplicable),
					new(NetworkIpPacketPolicy.NotApplicable(), ProofKind.Unavailable, AccessCompliance.NotApplicable),
					new(NetworkRouteAdapterIdentity.NotApplicable(), ProofKind.Unavailable, AccessCompliance.NotApplicable));
				lock (_gate)
				{
					_clientLeg = new NetworkRouteLegEvidence(
						NetworkAccessLeg.ClientLeg, evidence, "system-proxy");
					_clientCompliance = interfaceCompliance == AccessCompliance.Satisfied
						? AccessCompliance.Satisfied : AccessCompliance.EvidenceIncomplete;
				}
				return new NetworkStream(socket, ownsSocket: true);
			}
			catch (Exception ex)
			{
				lastError = ex;
				socket.Dispose();
			}
		}
		throw lastError ?? new SocketException((int)SocketError.HostUnreachable);
	}

	private static NetworkInterfaceIdentity ResolveInterface(IPAddress localAddress)
	{
		foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
		{
			try
			{
				var properties = networkInterface.GetIPProperties();
				if (!properties.UnicastAddresses.Any(item => item.Address.Equals(localAddress))) continue;
				var index = localAddress.AddressFamily == AddressFamily.InterNetwork
					? properties.GetIPv4Properties()?.Index
					: properties.GetIPv6Properties()?.Index;
				if (index is not > 0) continue;
				return new NetworkInterfaceIdentity(
					Guid.TryParse(networkInterface.Id, out var guid) ? guid : Guid.Empty,
					0,
					networkInterface.Name,
					checked((uint)index.Value));
			}
			catch (NetworkInformationException)
			{
			}
			catch (PlatformNotSupportedException)
			{
			}
		}
		return default;
	}

	private static NetworkRouteLegEvidence Reparent(NetworkRouteLegEvidence leg, NetworkExecutionIdentity identity) =>
		leg with { Evidence = leg.Evidence with { Identity = identity } };
}

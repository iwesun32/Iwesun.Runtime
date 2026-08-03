using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Iwesun.Runtime.Networks;

public enum NetworkDnsResolverSelectionKind : byte
{
	Unspecified = 0,
	SystemConfigured = 1,
	Exact = 2,
}

public readonly record struct NetworkDnsResolverSelection(
	NetworkDnsResolverSelectionKind Kind,
	IpAddressValue Address,
	ushort Port)
{
	public bool IsValid => Kind switch
	{
		NetworkDnsResolverSelectionKind.SystemConfigured => Address == default && Port > 0,
		NetworkDnsResolverSelectionKind.Exact => Address.Family is 4 or 6 && Port > 0,
		_ => false,
	};

	public static NetworkDnsResolverSelection SystemConfigured(ushort port = 53) =>
		new(NetworkDnsResolverSelectionKind.SystemConfigured, default, port);
	public static NetworkDnsResolverSelection Exact(IpAddressValue address, ushort port = 53) =>
		new(NetworkDnsResolverSelectionKind.Exact, address, port);
}

public enum NetworkDnsTransportKind : byte
{
	Unspecified = 0,
	Udp = 1,
	Tcp = 2,
}

public readonly record struct NetworkDnsTransportStage(
	NetworkDnsTransportKind Transport,
	NetworkSocketExecutionResult Execution)
{
	public bool IsValid => Transport != NetworkDnsTransportKind.Unspecified && Execution.Identity.HasBranch;
}

/// <summary>Networks 3.0 PTR endpoint with separate query object and resolver access plan.</summary>
public sealed class NetworkDnsReverseLookupEndpoint<TKey>
	: TrackedRequestReplyEndpointBase<NetworkDnsReverseLookupRequest<TKey>, NetworkDnsReverseLookupResponse<TKey>, TKey>
	where TKey : notnull
{
	private readonly NetworkAccessExecutionContext _context;
	protected override bool StartAttemptsConcurrently => true;

	public NetworkDnsReverseLookupEndpoint(
		NetworkAccessExecutionContext? context = null,
		int maxSendQueueLength = 4096,
		int maxReceiveQueueLength = 4096,
		int maxAttemptCount = 3,
		int defaultTimeoutMs = 3000)
		: base(maxSendQueueLength, maxReceiveQueueLength, maxAttemptCount, defaultTimeoutMs)
	{
		_context = context ?? NetworkAccessExecutionContext.CreatePlatformDefault();
	}

	protected override Guid GetRequestId(NetworkDnsReverseLookupRequest<TKey> request) => request.RequestId;
	protected override TKey GetRequestKey(NetworkDnsReverseLookupRequest<TKey> request) => request.Key;
	protected override Guid GetResponseRequestId(NetworkDnsReverseLookupResponse<TKey> response) => response.Identity.RequestId;
	protected override TKey GetResponseKey(NetworkDnsReverseLookupResponse<TKey> response) => response.Key;
	protected override NetworkExecutionIdentity GetResponseIdentity(NetworkDnsReverseLookupResponse<TKey> response) => response.Identity;
	protected override int? GetRequestTimeoutOverrideMs(NetworkDnsReverseLookupRequest<TKey> request) => request.TimeoutMs;
	protected override byte? GetResponseRetryCount(NetworkDnsReverseLookupResponse<TKey> response) => response.RetryCount;
	protected override bool CanRetry(NetworkDnsReverseLookupRequest<TKey> request, NetworkFailure failure) => request.AllowRetry;

	protected override TrackedAttemptStartResult OnStartAttempt(
		NetworkDnsReverseLookupRequest<TKey> request, byte retryCount,
		int timeoutMs, CancellationToken cancellationToken) => PrecisionSocketEndpointSupport.BranchIdentityRequired();

	protected override TrackedAttemptStartResult OnStartBranch(
		NetworkDnsReverseLookupRequest<TKey> request,
		byte retryCount,
		byte branchNumber,
		NetworkExecutionIdentity identity,
		int timeoutMs,
		CancellationToken cancellationToken)
	{
		if (branchNumber != 1 || request.QueryAddress.Family is not (4 or 6) ||
			!request.Resolver.IsValid || !request.ResolverAccessPlan.TryValidate(out _) ||
			request.ResolverAccessPlan.Destination.Kind != NetworkSelectorKind.SystemSelected ||
			request.ResolverAccessPlan.PathProvider is not NetworkPathProvider.Automatic and not NetworkPathProvider.Direct)
		{
			return PrecisionSocketEndpointSupport.Reject(NetworkAccessFailureCodes.DnsPtrRequestInvalid);
		}

		var resolver = request.Resolver.Kind == NetworkDnsResolverSelectionKind.Exact
			? request.Resolver.Address
			: ResolveSystemResolver(request.ResolverAccessPlan.Interface);
		if (resolver.Family is not (4 or 6))
			return PrecisionSocketEndpointSupport.Reject(NetworkAccessFailureCodes.DnsResolverUnavailable);
		var effectivePlan = request.ResolverAccessPlan with
		{
			Destination = NetworkDestinationSelection.Exact(resolver),
		};
		var security = request.SecurityBoundary with { ResolverIdentity = resolver.ToIPAddress().ToString() };
		if (!_context.TryResolve(effectivePlan, security, out var resolved, out var failure))
			return PrecisionSocketEndpointSupport.Reject(failure.ReasonCode);
		_ = QueryAsync(request, retryCount, identity, effectivePlan, security, resolved, timeoutMs, cancellationToken);
		return TrackedAttemptStartResult.Accepted();
	}

	protected override bool IsSuccessfulResponse(NetworkDnsReverseLookupResponse<TKey> response, out NetworkFailure failure)
	{
		if (response.Succeeded)
		{
			failure = NetworkFailure.None;
			return true;
		}
		var final = response.Stages is { Length: > 0 } ? response.Stages[^1].Execution : default;
		var kind = final.Outcome switch
		{
			ProtocolOutcome.TimedOut => NetworkFailureKind.Timeout,
			ProtocolOutcome.Cancelled => NetworkFailureKind.Cancelled,
			ProtocolOutcome.TransportFailed => NetworkFailureKind.Transport,
			_ => NetworkFailureKind.Protocol,
		};
		failure = new NetworkFailure(kind, final.Error.Code,
			string.IsNullOrEmpty(response.ReasonCode) ? NetworkAccessFailureCodes.DnsPtrResponseInvalid : response.ReasonCode,
			final.Error);
		return false;
	}

	protected override ProtocolOutcome GetResponseProtocolOutcome(
		NetworkDnsReverseLookupResponse<TKey> response, bool contractSatisfied, NetworkFailure failure)
	{
		var final = response.Stages is { Length: > 0 } ? response.Stages[^1].Execution : default;
		return response.Succeeded ? ProtocolOutcome.Succeeded
			: final.Outcome == ProtocolOutcome.Succeeded ? ProtocolOutcome.ProtocolFailed : final.Outcome;
	}

	protected override AccessCompliance GetResponseAccessCompliance(
		NetworkDnsReverseLookupResponse<TKey> response, bool contractSatisfied, NetworkFailure failure)
	{
		var final = response.Stages is { Length: > 0 } ? response.Stages[^1].Execution : default;
		return final.Compliance == AccessCompliance.Unspecified
			? AccessCompliance.NotApplicable : final.Compliance;
	}

	private async Task QueryAsync(
		NetworkDnsReverseLookupRequest<TKey> request,
		byte retryCount,
		NetworkExecutionIdentity identity,
		RequestedAccessPlan effectivePlan,
		NetworkAccessSecurityBoundary security,
		ResolvedAccessPlan resolved,
		int timeoutMs,
		CancellationToken lifetimeToken)
	{
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
		timeout.CancelAfter(timeoutMs);
		var transactionId = (ushort)Random.Shared.Next(1, 65536);
		var query = DnsWireCodec.BuildQuery(BuildPtrName(request.QueryAddress), 12, transactionId);
		var stages = new List<NetworkDnsTransportStage>(2);
		var udpRequest = new NetworkSocketExecutionRequest(
			identity, effectivePlan, resolved, security, NetworkSocketOperation.UdpExchange,
			request.Resolver.Port, query, ushort.MaxValue);
		var udp = await _context.SocketExecutor.ExecuteAsync(udpRequest, timeout.Token).ConfigureAwait(false);
		stages.Add(new NetworkDnsTransportStage(NetworkDnsTransportKind.Udp, udp));
		var payload = udp.Payload;
		var hostName = string.Empty;
		uint ttl = 0;
		var truncated = false;
		byte responseCode = 0;
		var parsed = udp.IsSuccess && DnsWireCodec.TryParsePtrResponse(
			payload, transactionId, out hostName, out ttl, out truncated, out responseCode);
		if (truncated && udp.IsSuccess)
		{
			NetworkSocketExecutionResult tcp;
			try
			{
				tcp = await ExecuteTcpStageAsync(udpRequest, query, timeout.Token).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				var cancelled = timeout.IsCancellationRequested;
				tcp = udp with
				{
					Outcome = cancelled ? ProtocolOutcome.TimedOut : ProtocolOutcome.TransportFailed,
					Compliance = AccessCompliance.EvidenceIncomplete,
					Error = BinaryNetworkError.FromException(ex, false, cancelled),
					ReasonCode = cancelled ? "dns-tcp-fallback-timed-out" : "dns-tcp-fallback-failed",
					Payload = [],
					CompletedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
				};
			}
			stages.Add(new NetworkDnsTransportStage(NetworkDnsTransportKind.Tcp, tcp));
			payload = tcp.Payload;
			parsed = tcp.IsSuccess && DnsWireCodec.TryParsePtrResponse(
				payload, transactionId, out hostName, out ttl, out _, out responseCode);
		}

		var final = stages[^1].Execution;
		var succeeded = parsed && !string.IsNullOrWhiteSpace(hostName) &&
			final.Outcome == ProtocolOutcome.Succeeded && final.Compliance == AccessCompliance.Satisfied;
		var reason = succeeded ? string.Empty
			: final.Outcome != ProtocolOutcome.Succeeded ? final.ReasonCode
			: final.Compliance != AccessCompliance.Satisfied ? NetworkAccessFailureCodes.AccessEvidenceIncomplete
			: NetworkAccessFailureCodes.DnsPtrResponseInvalid;
		PublishResponse(new NetworkDnsReverseLookupResponse<TKey>(
			identity.CreateResponse(Guid.NewGuid()), request.Key, retryCount, request.QueryAddress,
			resolved.Destination, hostName ?? string.Empty, ttl, responseCode, truncated, [.. stages],
			succeeded, reason, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), identity.BranchId);
	}

	private async ValueTask<NetworkSocketExecutionResult> ExecuteTcpStageAsync(
		NetworkSocketExecutionRequest udpRequest,
		byte[] query,
		CancellationToken cancellationToken)
	{
		if (!_context.Resolver.IsCurrent(udpRequest.Resolved))
		{
			return new NetworkSocketExecutionResult(
				udpRequest.Identity,
				ProtocolOutcome.Rejected,
				AccessCompliance.EvidenceIncomplete,
				default,
				default,
				NetworkAccessFailureCodes.ResolvedPlanStale,
				[],
				DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
		}
		using var socket = new Socket(
			udpRequest.Resolved.Destination.IsIPv4 ? AddressFamily.InterNetwork : AddressFamily.InterNetworkV6,
			SocketType.Stream,
			ProtocolType.Tcp);
		var tcpRequest = udpRequest with { Operation = NetworkSocketOperation.TcpConnect, Payload = [], ReceiveBufferSize = 0 };
		NetworkSocketExecutor.ApplyPlan(socket, tcpRequest);
		using var policy = _context.SocketExecutor.AcquireWfpPolicyIfRequired(
			socket, tcpRequest, ProtocolType.Tcp, out var policyFailure);
		if (!string.IsNullOrEmpty(policyFailure.ReasonCode))
		{
			return new NetworkSocketExecutionResult(
				tcpRequest.Identity,
				ProtocolOutcome.Rejected,
				AccessCompliance.EvidenceIncomplete,
				default,
				default,
				policyFailure.ReasonCode,
				[],
				DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
		}
		await socket.ConnectAsync(new IPEndPoint(
			NetworkIpAddressInterop.ToSystemAddress(
				udpRequest.Resolved.Destination,
				udpRequest.Resolved.Interface.InterfaceIndex),
			udpRequest.RemotePort), cancellationToken).ConfigureAwait(false);
		var framed = new byte[query.Length + 2];
		BinaryPrimitives.WriteUInt16BigEndian(framed, checked((ushort)query.Length));
		query.CopyTo(framed, 2);
		await socket.SendAsync(framed, SocketFlags.None, cancellationToken).ConfigureAwait(false);
		var prefix = new byte[2];
		await ReceiveExactlyAsync(socket, prefix, cancellationToken).ConfigureAwait(false);
		var length = BinaryPrimitives.ReadUInt16BigEndian(prefix);
		var response = new byte[length];
		await ReceiveExactlyAsync(socket, response, cancellationToken).ConfigureAwait(false);
		return NetworkSocketExecutor.CreateSuccessResult(
			tcpRequest, socket, response, ProofKind.Observed, policyEnforced: policy is not null);
	}

	private static async Task ReceiveExactlyAsync(Socket socket, Memory<byte> buffer, CancellationToken cancellationToken)
	{
		var offset = 0;
		while (offset < buffer.Length)
		{
			var count = await socket.ReceiveAsync(buffer[offset..], SocketFlags.None, cancellationToken).ConfigureAwait(false);
			if (count == 0) throw new EndOfStreamException("DNS TCP response ended before its frame was complete.");
			offset += count;
		}
	}

	private static string BuildPtrName(IpAddressValue address)
	{
		var ip = address.ToIPAddress();
		if (ip.AddressFamily == AddressFamily.InterNetwork)
			return string.Join('.', ip.GetAddressBytes().Reverse()) + ".in-addr.arpa";
		return string.Join('.', ip.GetAddressBytes().Reverse().SelectMany(static value =>
			new[] { (value & 0x0F).ToString("x"), (value >> 4).ToString("x") })) + ".ip6.arpa";
	}

	private static IpAddressValue ResolveSystemResolver(NetworkInterfaceSelection selection)
	{
		foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
		{
			try
			{
				if (adapter.OperationalStatus != OperationalStatus.Up) continue;
				var properties = adapter.GetIPProperties();
				if (selection.Kind == NetworkSelectorKind.Exact &&
					!MatchesInterface(properties, selection.ExactValue.InterfaceIndex)) continue;
				var resolver = properties.DnsAddresses.FirstOrDefault(static address =>
					address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6);
				if (resolver is not null) return NetworkIpAddressInterop.FromSystemAddress(resolver);
			}
			catch (NetworkInformationException) { }
			catch (PlatformNotSupportedException) { }
		}
		return default;
	}

	private static bool MatchesInterface(IPInterfaceProperties properties, uint index)
	{
		if (index == 0) return true;
		try { if (properties.GetIPv4Properties()?.Index == index) return true; }
		catch (NetworkInformationException) { }
		try { return properties.GetIPv6Properties()?.Index == index; }
		catch (NetworkInformationException) { return false; }
	}
}

public readonly record struct NetworkDnsReverseLookupRequest<TKey>(
	Guid RequestId,
	TKey Key,
	IpAddressValue QueryAddress,
	NetworkDnsResolverSelection Resolver,
	RequestedAccessPlan ResolverAccessPlan,
	NetworkAccessSecurityBoundary SecurityBoundary,
	int? TimeoutMs = null,
	bool AllowRetry = true)
	where TKey : notnull;

public readonly record struct NetworkDnsReverseLookupResponse<TKey>(
	NetworkExecutionIdentity Identity,
	TKey Key,
	byte RetryCount,
	IpAddressValue QueryAddress,
	IpAddressValue ResolverAddress,
	string HostName,
	uint Ttl,
	byte ResponseCode,
	bool UsedTcp,
	NetworkDnsTransportStage[] Stages,
	bool Succeeded,
	string ReasonCode,
	long CompletedAtUnixMs)
	where TKey : notnull;

using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace Iwesun.Runtime.Networks;

/// <summary>Networks 3.0 tracked ICMP endpoint for IPv4 and IPv6.</summary>
public sealed class NetworkPingEndpoint<TKey>
	: TrackedRequestReplyEndpointBase<NetworkPingRequest<TKey>, NetworkPingResponse<TKey>, TKey>
	where TKey : notnull
{
	private readonly NetworkAccessExecutionContext _context;

	public NetworkPingEndpoint(
		NetworkAccessExecutionContext? context = null,
		int maxSendQueueLength = 4096,
		int maxReceiveQueueLength = 4096,
		int maxAttemptCount = 3,
		int defaultTimeoutMs = 1000)
		: base(maxSendQueueLength, maxReceiveQueueLength, maxAttemptCount, defaultTimeoutMs)
	{
		_context = context ?? NetworkAccessExecutionContext.CreatePlatformDefault();
	}

	protected override Guid GetRequestId(NetworkPingRequest<TKey> request) => request.RequestId;
	protected override TKey GetRequestKey(NetworkPingRequest<TKey> request) => request.Key;
	protected override Guid GetResponseRequestId(NetworkPingResponse<TKey> response) => response.Identity.RequestId;
	protected override TKey GetResponseKey(NetworkPingResponse<TKey> response) => response.Key;
	protected override NetworkExecutionIdentity GetResponseIdentity(NetworkPingResponse<TKey> response) => response.Identity;
	protected override int? GetRequestTimeoutOverrideMs(NetworkPingRequest<TKey> request) => request.TimeoutMs;
	protected override byte? GetResponseRetryCount(NetworkPingResponse<TKey> response) => response.RetryCount;
	protected override bool CanRetry(NetworkPingRequest<TKey> request, NetworkFailure failure) => request.AllowRetry;
	protected override TrackedResponseCollectionPolicy GetResponseCollectionPolicy(NetworkPingRequest<TKey> request) =>
		request.ResponsePolicy.IsValid
			? request.ResponsePolicy
			: TrackedResponseCollectionPolicy.FirstValidResponse();
	protected override bool IsResponseCollectionWindowExecutionOwned(NetworkPingRequest<TKey> request) =>
		request.ResponsePolicy.Mode == TrackedResponseCollectionMode.CollectUntilWindowEnds;
	protected override bool IsAttemptTimeoutExecutionOwned(NetworkPingRequest<TKey> request) => true;
	protected override bool StartAttemptsConcurrently => true;

	protected override TrackedAttemptStartResult OnStartAttempt(
		NetworkPingRequest<TKey> request, byte retryCount,
		int timeoutMs, CancellationToken cancellationToken) => PrecisionSocketEndpointSupport.BranchIdentityRequired();

	protected override TrackedAttemptStartResult OnStartBranch(
		NetworkPingRequest<TKey> request,
		byte retryCount,
		byte branchNumber,
		NetworkExecutionIdentity identity,
		int timeoutMs,
		CancellationToken cancellationToken)
	{
		if (branchNumber != 1 || request.Payload is null || request.Payload.Length > ushort.MaxValue ||
			!request.AccessPlan.TryValidate(out _) ||
			request.AccessPlan.Destination.Kind != NetworkSelectorKind.Exact ||
			request.AccessPlan.LocalEndpoint.Kind != NetworkLocalEndpointSelectionKind.NotApplicable)
		{
			return PrecisionSocketEndpointSupport.Reject(NetworkAccessFailureCodes.PingRequestInvalid);
		}
		if (request.AccessPlan.PathProvider is not NetworkPathProvider.Automatic and not NetworkPathProvider.Direct)
			return PrecisionSocketEndpointSupport.Reject(NetworkAccessFailureCodes.CapabilityUnsupported);
		var destination = request.AccessPlan.Destination.ExactValue;
		var multipleResponses = destination.IsMulticast ||
			request.AccessPlan.IpPacketPolicy.AllowBroadcast ||
			request.AccessPlan.IpPacketPolicy.AllowMulticastLoopback;
		if (multipleResponses &&
			(request.ResponsePolicy.Mode != TrackedResponseCollectionMode.CollectUntilWindowEnds ||
			 request.ResponsePolicy.MaxResponses > 256))
			return PrecisionSocketEndpointSupport.Reject(NetworkAccessFailureCodes.ResponseCollectionPolicyInvalid);
		if (multipleResponses && !OperatingSystem.IsWindows())
			return PrecisionSocketEndpointSupport.Reject(NetworkAccessFailureCodes.PlatformNotSupported);
		if (request.AccessPlan.PathProvider == NetworkPathProvider.Direct &&
			request.AccessPlan.NextHop.Kind == NetworkSelectorKind.Exact && !request.AccessPlan.NextHop.IsOnLink)
		{
			return PrecisionSocketEndpointSupport.Reject(NetworkAccessFailureCodes.ExactNextHopBackendUnavailable);
		}
		if (request.AccessPlan.RouteScope.Kind == NetworkRouteScopeSelectionKind.ExactCompartment)
			return PrecisionSocketEndpointSupport.Reject(NetworkAccessFailureCodes.RouteScopeBackendUnavailable);
		if (request.AccessPlan.PathProvider == NetworkPathProvider.Direct &&
			request.AccessPlan.IpPacketPolicy.Kind == NetworkIpPacketPolicyKind.Explicit)
		{
			return PrecisionSocketEndpointSupport.Reject(NetworkAccessFailureCodes.PingPacketPolicyBackendUnavailable);
		}
		if (!_context.TryResolve(request.AccessPlan, request.SecurityBoundary, out var resolved, out var failure))
			return PrecisionSocketEndpointSupport.Reject(failure.ReasonCode);

		_ = multipleResponses
			? ExecuteMultipleAsync(request, retryCount, identity, resolved, timeoutMs, cancellationToken)
			: ExecuteAsync(request, retryCount, identity, resolved, timeoutMs, cancellationToken);
		return TrackedAttemptStartResult.Accepted();
	}

	private async Task ExecuteMultipleAsync(
		NetworkPingRequest<TKey> request,
		byte retryCount,
		NetworkExecutionIdentity branchIdentity,
		ResolvedAccessPlan resolved,
		int timeoutMs,
		CancellationToken lifetimeToken)
	{
		var batch = await NetworkPingExecutor.ExecuteMultipleAsync(
			branchIdentity,
			request.AccessPlan,
			resolved,
			request.Payload,
			Math.Min(timeoutMs, request.ResponsePolicy.WindowMs),
			request.ResponsePolicy.MaxResponses,
			lifetimeToken).ConfigureAwait(false);
		foreach (var result in batch.Responses)
		{
			PublishResponse(new NetworkPingResponse<TKey>(
				branchIdentity.CreateResponse(Guid.NewGuid()), request.Key, retryCount, result), branchIdentity.BranchId);
		}
		if (batch.Responses.Length > 0)
		{
			CompleteResponseCollectionWindow(request.RequestId);
			return;
		}
		if (batch.Responses.Length == 0)
		{
			var failureKind = batch.Outcome switch
			{
				ProtocolOutcome.TimedOut => NetworkFailureKind.Timeout,
				ProtocolOutcome.Cancelled => NetworkFailureKind.Cancelled,
				ProtocolOutcome.Rejected => NetworkFailureKind.Rejected,
				ProtocolOutcome.ProtocolFailed => NetworkFailureKind.Protocol,
				_ => NetworkFailureKind.Transport,
			};
			PublishBranchTerminal(
				request.RequestId,
				branchIdentity.BranchId,
				batch.Outcome,
				batch.Compliance,
				new NetworkFailure(failureKind, batch.Error.Code, batch.ReasonCode, batch.Error));
		}
	}

	protected override bool IsSuccessfulResponse(NetworkPingResponse<TKey> response, out NetworkFailure failure)
	{
		if (response.Result.Outcome == ProtocolOutcome.Succeeded &&
			response.Result.Compliance == AccessCompliance.Satisfied)
		{
			failure = NetworkFailure.None;
			return true;
		}

		var kind = response.Result.Outcome switch
		{
			ProtocolOutcome.Rejected => NetworkFailureKind.Rejected,
			ProtocolOutcome.TimedOut => NetworkFailureKind.Timeout,
			ProtocolOutcome.Cancelled => NetworkFailureKind.Cancelled,
			ProtocolOutcome.ProtocolFailed => NetworkFailureKind.Protocol,
			_ => NetworkFailureKind.Transport,
		};
		failure = new NetworkFailure(
			kind,
			response.Result.Error.Code,
			response.Result.ReasonCode,
			response.Result.Error);
		return false;
	}

	protected override ProtocolOutcome GetResponseProtocolOutcome(
		NetworkPingResponse<TKey> response, bool contractSatisfied, NetworkFailure failure) => response.Result.Outcome;

	protected override AccessCompliance GetResponseAccessCompliance(
		NetworkPingResponse<TKey> response, bool contractSatisfied, NetworkFailure failure) => response.Result.Compliance;

	private async Task ExecuteAsync(
		NetworkPingRequest<TKey> request,
		byte retryCount,
		NetworkExecutionIdentity branchIdentity,
		ResolvedAccessPlan resolved,
		int timeoutMs,
		CancellationToken lifetimeToken)
	{
		var result = await NetworkPingExecutor.ExecuteAsync(
			branchIdentity, request.AccessPlan, resolved, request.Payload, timeoutMs, lifetimeToken).ConfigureAwait(false);
		PublishResponse(new NetworkPingResponse<TKey>(
			branchIdentity.CreateResponse(Guid.NewGuid()), request.Key, retryCount, result), branchIdentity.BranchId);
	}
}

public readonly record struct NetworkPingRequest<TKey>(
	Guid RequestId,
	TKey Key,
	RequestedAccessPlan AccessPlan,
	NetworkAccessSecurityBoundary SecurityBoundary,
	byte[] Payload,
	int? TimeoutMs = null,
	bool AllowRetry = true,
	TrackedResponseCollectionPolicy ResponsePolicy = default)
	where TKey : notnull;

public readonly record struct NetworkPingResponse<TKey>(
	NetworkExecutionIdentity Identity,
	TKey Key,
	byte RetryCount,
	NetworkPingExecutionResult Result)
	where TKey : notnull;

public readonly record struct NetworkPingExecutionResult(
	NetworkExecutionIdentity Identity,
	ProtocolOutcome Outcome,
	AccessCompliance Compliance,
	ActualAccessEvidence Evidence,
	ushort StatusCode,
	uint RoundtripTimeMs,
	uint MeasuredElapsedMs,
	BinaryNetworkError Error,
	string ReasonCode,
	long CompletedAtUnixMs)
{
	/// <summary>The concrete unicast peer that produced this reply; the evidence destination remains the submitted target.</summary>
	public IpAddressValue ResponderAddress { get; init; }
}

internal readonly record struct NetworkPingMultipleExecutionResult(
	NetworkPingExecutionResult[] Responses,
	ProtocolOutcome Outcome,
	AccessCompliance Compliance,
	ActualAccessEvidence Evidence,
	BinaryNetworkError Error,
	string ReasonCode);

internal static class NetworkPingExecutor
{
	public static async ValueTask<NetworkPingMultipleExecutionResult> ExecuteMultipleAsync(
		NetworkExecutionIdentity identity,
		RequestedAccessPlan requested,
		ResolvedAccessPlan resolved,
		byte[] payload,
		int timeoutMs,
		ushort maxResponses,
		CancellationToken cancellationToken)
	{
		if (!OperatingSystem.IsWindows() || maxResponses == 0 || maxResponses > 256)
			return MultipleFailure(identity, requested, resolved, NetworkAccessFailureCodes.PlatformNotSupported);
		var started = Stopwatch.GetTimestamp();
		PingNativeBatchResult native;
		try
		{
			native = await Task.Run(
					() => SendNativeMultiple(
						resolved.Destination, resolved.Source, resolved.Interface.InterfaceIndex,
						payload, checked((uint)timeoutMs), maxResponses),
				CancellationToken.None).WaitAsync(
					GetCompletionGuard(timeoutMs), cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			return MultipleFailure(identity, requested, resolved, "ping-cancelled", ProtocolOutcome.Cancelled,
				BinaryNetworkError.FromException(null, true, false));
		}
		catch (TimeoutException ex)
		{
			return MultipleFailure(identity, requested, resolved, "ping-native-completion-timeout", ProtocolOutcome.TransportFailed,
				BinaryNetworkError.FromException(ex, false, true));
		}
		catch (Exception ex)
		{
			return MultipleFailure(identity, requested, resolved, "ping-transport-failed", ProtocolOutcome.TransportFailed,
				BinaryNetworkError.FromException(ex, false, false));
		}

		var compliance = EvaluateCompliance(requested, resolved, native.SubmissionEstablished);
		var evidence = CreateEvidence(identity, requested, resolved, native.SubmissionEstablished);
		var results = new List<NetworkPingExecutionResult>(native.Replies.Length);
		foreach (var reply in native.Replies)
		{
			if (reply.Status != IPStatus.Success) continue;
			results.Add(new NetworkPingExecutionResult(
				identity,
				ProtocolOutcome.Succeeded,
				compliance,
				evidence,
				checked((ushort)reply.Status),
				reply.RoundtripTimeMs,
				ClampToUInt(Stopwatch.GetElapsedTime(started).TotalMilliseconds),
				reply.Error,
				compliance == AccessCompliance.Satisfied
					? string.Empty : NetworkAccessFailureCodes.AccessEvidenceIncomplete,
				DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
			{
				ResponderAddress = reply.ReplyAddress,
			});
		}
		if (results.Count > 0)
			return new([.. results], ProtocolOutcome.Succeeded, compliance, evidence, native.Error, string.Empty);
		var outcome = native.Replies.Length > 0
			? ProtocolOutcome.ProtocolFailed
			: native.SubmissionEstablished ? ProtocolOutcome.TimedOut : ProtocolOutcome.TransportFailed;
		var reason = native.Replies.Length > 0
			? $"ping-{native.Replies[0].Status.ToString().ToLowerInvariant()}"
			: native.SubmissionEstablished ? "ping-response-window-empty" : "ping-native-submit-failed";
		return new([], outcome, compliance, evidence, native.Error, reason);
	}

	public static async ValueTask<NetworkPingExecutionResult> ExecuteAsync(
		NetworkExecutionIdentity identity,
		RequestedAccessPlan requested,
		ResolvedAccessPlan resolved,
		byte[] payload,
		int timeoutMs,
		CancellationToken cancellationToken)
	{
		var started = Stopwatch.GetTimestamp();
		PingNativeResult native;
		try
		{
			var requiresSourceBinding = requested.PathProvider == NetworkPathProvider.Direct &&
				(requested.Source.Kind is not NetworkSelectorKind.SystemSelected and not NetworkSelectorKind.NotApplicable ||
				 requested.Interface.Kind is not NetworkSelectorKind.SystemSelected and not NetworkSelectorKind.NotApplicable);
			if (requiresSourceBinding)
			{
				if (!OperatingSystem.IsWindows())
					return Failure(identity, resolved, requested, started, NetworkAccessFailureCodes.PlatformNotSupported);
				native = await Task.Run(
					() => SendNative(
						resolved.Destination, resolved.Source, resolved.Interface.InterfaceIndex,
						payload, checked((uint)timeoutMs)),
					CancellationToken.None).WaitAsync(
						GetCompletionGuard(timeoutMs), cancellationToken).ConfigureAwait(false);
			}
			else
			{
				using var ping = new Ping();
				var options = requested.IpPacketPolicy.Kind == NetworkIpPacketPolicyKind.Explicit
					? new PingOptions(
						requested.IpPacketPolicy.TimeToLiveOrHopLimit.HasValue
							? requested.IpPacketPolicy.TimeToLiveOrHopLimit.Value
							: 128,
						requested.IpPacketPolicy.DontFragment)
					: null;
				var reply = await ping.SendPingAsync(
					ToSystemAddress(resolved.Destination, resolved.Interface.InterfaceIndex),
					timeoutMs, payload, options).WaitAsync(
						GetCompletionGuard(timeoutMs), cancellationToken).ConfigureAwait(false);
				native = new PingNativeResult(
					reply.Status,
					reply.Status == IPStatus.Success ? checked((uint)Math.Max(0, reply.RoundtripTime)) : 0,
					BinaryNetworkError.None,
					reply.Address is null ? resolved.Destination : NetworkIpAddressInterop.FromSystemAddress(reply.Address));
			}
		}
		catch (OperationCanceledException)
		{
			return Failure(identity, resolved, requested, started, "ping-cancelled", ProtocolOutcome.Cancelled,
				BinaryNetworkError.FromException(null, true, false));
		}
		catch (TimeoutException ex)
		{
			return Failure(identity, resolved, requested, started, "ping-native-completion-timeout", ProtocolOutcome.TransportFailed,
				BinaryNetworkError.FromException(ex, false, true));
		}
		catch (Exception ex)
		{
			return Failure(identity, resolved, requested, started, "ping-transport-failed", ProtocolOutcome.TransportFailed,
				BinaryNetworkError.FromException(ex, false, false));
		}

		var outcome = native.Status switch
		{
			IPStatus.Success => ProtocolOutcome.Succeeded,
			IPStatus.TimedOut => ProtocolOutcome.TimedOut,
			_ => ProtocolOutcome.ProtocolFailed,
		};
		var submissionEstablished = native.Error == BinaryNetworkError.None || native.Status == IPStatus.TimedOut;
		var evidence = CreateEvidence(identity, requested, resolved, submissionEstablished);
		var compliance = EvaluateCompliance(requested, resolved, submissionEstablished);
		var reason = outcome == ProtocolOutcome.Succeeded
			? compliance == AccessCompliance.Satisfied ? string.Empty : NetworkAccessFailureCodes.AccessEvidenceIncomplete
			: $"ping-{native.Status.ToString().ToLowerInvariant()}";
		return new NetworkPingExecutionResult(
			identity, outcome, compliance, evidence, checked((ushort)native.Status), native.RoundtripTimeMs,
			ClampToUInt(Stopwatch.GetElapsedTime(started).TotalMilliseconds), native.Error, reason,
			DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
		{
			ResponderAddress = native.Status == IPStatus.Success ? native.ReplyAddress : default,
		};
	}

	internal static AccessCompliance EvaluateCompliance(
		RequestedAccessPlan requested,
		ResolvedAccessPlan resolved,
		bool submissionEstablished)
	{
		if (!submissionEstablished) return AccessCompliance.EvidenceIncomplete;
		var interfaceRequired = requested.Interface.Kind is not NetworkSelectorKind.SystemSelected and not NetworkSelectorKind.NotApplicable;
		var nextHopRequired = requested.NextHop.IsOnLink ||
			requested.NextHop.Kind is not NetworkSelectorKind.SystemSelected and not NetworkSelectorKind.NotApplicable;
		if (nextHopRequired) return AccessCompliance.EvidenceIncomplete;
		if (!interfaceRequired) return AccessCompliance.Satisfied;
		var exactIpv6InterfaceBinding = requested.PathProvider == NetworkPathProvider.Direct &&
			requested.Interface.Kind == NetworkSelectorKind.Exact &&
			resolved.Interface.InterfaceIndex != 0 &&
			requested.Interface.ExactValue.InterfaceIndex == resolved.Interface.InterfaceIndex &&
			resolved.Source.IsIPv6 && resolved.Destination.IsIPv6 &&
			(!RequiresIpv6Scope(resolved.Destination) || resolved.Interface.InterfaceIndex != 0);
		return exactIpv6InterfaceBinding ? AccessCompliance.Satisfied : AccessCompliance.EvidenceIncomplete;
	}

	private static TimeSpan GetCompletionGuard(int timeoutMs)
	{
		var schedulingMarginMs = Math.Max(50, checked(timeoutMs * 30 / 100));
		return TimeSpan.FromMilliseconds(checked(timeoutMs + schedulingMarginMs));
	}

	internal static ActualAccessEvidence CreateEvidence(
		NetworkExecutionIdentity identity,
		RequestedAccessPlan requested,
		ResolvedAccessPlan resolved,
		bool submissionEstablished)
	{
		var sourceBound = requested.PathProvider == NetworkPathProvider.Direct &&
			submissionEstablished &&
			(requested.Source.Kind is not NetworkSelectorKind.SystemSelected and not NetworkSelectorKind.NotApplicable ||
			 requested.Interface.Kind is not NetworkSelectorKind.SystemSelected and not NetworkSelectorKind.NotApplicable);
		var interfaceRequired = requested.Interface.Kind is not NetworkSelectorKind.SystemSelected and not NetworkSelectorKind.NotApplicable;
		var interfaceBound = EvaluateCompliance(requested, resolved, submissionEstablished) == AccessCompliance.Satisfied &&
			interfaceRequired;
		var interfaceProof = interfaceBound
			? RequiresIpv6Scope(resolved.Destination) ? ProofKind.ApiBinding : ProofKind.AdapterReported
			: ProofKind.Inferred;
		return new ActualAccessEvidence(
			identity,
			new(requested.PathProvider, submissionEstablished ? ProofKind.ApiBinding : ProofKind.Inferred,
				submissionEstablished ? AccessCompliance.Satisfied : AccessCompliance.EvidenceIncomplete),
			new(resolved.Destination,
				submissionEstablished ? ProofKind.ApiBinding : ProofKind.Inferred,
				submissionEstablished ? AccessCompliance.Satisfied : AccessCompliance.EvidenceIncomplete),
			new(resolved.Source, sourceBound ? ProofKind.ApiBinding : ProofKind.Inferred,
				sourceBound ? AccessCompliance.Satisfied : AccessCompliance.EvidenceIncomplete),
			new(resolved.Interface, interfaceProof,
				interfaceRequired
					? interfaceBound ? AccessCompliance.Satisfied : AccessCompliance.EvidenceIncomplete
					: AccessCompliance.NotApplicable),
			new(resolved.NextHop, ProofKind.Inferred, AccessCompliance.EvidenceIncomplete),
			new((ushort)0, ProofKind.Unavailable, AccessCompliance.NotApplicable),
			new(resolved.RouteCompartmentId, ProofKind.Inferred, AccessCompliance.EvidenceIncomplete),
			new(requested.IpPacketPolicy,
				requested.IpPacketPolicy.Kind == NetworkIpPacketPolicyKind.Explicit ? ProofKind.ApiBinding : ProofKind.Inferred,
				requested.IpPacketPolicy.Kind == NetworkIpPacketPolicyKind.Explicit ? AccessCompliance.Satisfied : AccessCompliance.NotApplicable),
			new(requested.RouteAdapter, ProofKind.Unavailable, AccessCompliance.NotApplicable));
	}

	private static bool RequiresIpv6Scope(IpAddressValue address) => address.RequiresInterfaceScope;

	private static NetworkPingExecutionResult Failure(
		NetworkExecutionIdentity identity,
		ResolvedAccessPlan resolved,
		RequestedAccessPlan requested,
		long started,
		string reason,
		ProtocolOutcome outcome = ProtocolOutcome.Rejected,
		BinaryNetworkError error = default) => new(
		identity, outcome, AccessCompliance.EvidenceIncomplete,
		CreateEvidence(identity, requested, resolved, false),
		unchecked((ushort)IPStatus.Unknown), 0,
		ClampToUInt(Stopwatch.GetElapsedTime(started).TotalMilliseconds), error, reason,
		DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

	private static NetworkPingMultipleExecutionResult MultipleFailure(
		NetworkExecutionIdentity identity,
		RequestedAccessPlan requested,
		ResolvedAccessPlan resolved,
		string reason,
		ProtocolOutcome outcome = ProtocolOutcome.Rejected,
		BinaryNetworkError error = default) => new(
		[], outcome, AccessCompliance.EvidenceIncomplete,
		CreateEvidence(identity, requested, resolved, false), error, reason);

	private static PingNativeResult SendNative(
		IpAddressValue target,
		IpAddressValue source,
		uint interfaceIndex,
		byte[] payload,
		uint timeoutMs) => target.IsIPv4
		? SendNativeIpv4(target, source, payload, timeoutMs)
		: SendNativeIpv6(target, source, interfaceIndex, payload, timeoutMs);

	private static PingNativeBatchResult SendNativeMultiple(
		IpAddressValue target,
		IpAddressValue source,
		uint interfaceIndex,
		byte[] payload,
		uint timeoutMs,
		ushort maxResponses) => target.IsIPv4
			? SendNativeIpv4Multiple(target, source, payload, timeoutMs, maxResponses)
			: SendNativeIpv6Multiple(target, source, interfaceIndex, payload, timeoutMs, maxResponses);

	private static PingNativeBatchResult SendNativeIpv4Multiple(
		IpAddressValue target,
		IpAddressValue source,
		byte[] payload,
		uint timeoutMs,
		ushort maxResponses)
	{
		var handle = IcmpCreateFile();
		if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return NativeBatchHandleFailure();
		var request = AllocPayload(payload);
		var structureSize = Marshal.SizeOf<IcmpEchoReply>();
		var replySize = checked(maxResponses * (structureSize + payload.Length + 32));
		var reply = Marshal.AllocHGlobal(replySize);
		try
		{
			var count = IcmpSendEcho2Ex(
				handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
				ToNativeIpv4(source), ToNativeIpv4(target), request, checked((ushort)payload.Length),
				IntPtr.Zero, reply, checked((uint)replySize), timeoutMs);
			if (count > 0) return new(ParseIpv4Replies(reply, count, maxResponses), true, BinaryNetworkError.None);
			return NativeBatchSendFailure();
		}
		finally
		{
			if (request != IntPtr.Zero) Marshal.FreeHGlobal(request);
			Marshal.FreeHGlobal(reply);
			IcmpCloseHandle(handle);
		}
	}

	private static PingNativeBatchResult SendNativeIpv6Multiple(
		IpAddressValue target,
		IpAddressValue source,
		uint interfaceIndex,
		byte[] payload,
		uint timeoutMs,
		ushort maxResponses)
	{
		var handle = Icmp6CreateFile();
		if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return NativeBatchHandleFailure();
		var sourceAddress = AllocSockaddr(source, interfaceIndex);
		var targetAddress = AllocSockaddr(target, interfaceIndex);
		var request = AllocPayload(payload);
		var structureSize = Marshal.SizeOf<IcmpV6EchoReplyLayout>();
		var replySize = checked(maxResponses * (structureSize + payload.Length + 32));
		var reply = Marshal.AllocHGlobal(replySize);
		try
		{
			var count = Icmp6SendEcho2(
				handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
				sourceAddress, targetAddress, request, checked((ushort)payload.Length), IntPtr.Zero,
				reply, checked((uint)replySize), timeoutMs);
			if (count > 0) return new(ParseIpv6Replies(reply, count, maxResponses), true, BinaryNetworkError.None);
			return NativeBatchSendFailure();
		}
		finally
		{
			Marshal.FreeHGlobal(sourceAddress);
			Marshal.FreeHGlobal(targetAddress);
			if (request != IntPtr.Zero) Marshal.FreeHGlobal(request);
			Marshal.FreeHGlobal(reply);
			IcmpCloseHandle(handle);
		}
	}

	internal static PingNativeResult[] ParseIpv4Replies(IntPtr buffer, uint count, ushort maximum)
	{
		if (buffer == IntPtr.Zero || count == 0 || maximum == 0) return [];
		var length = checked((int)Math.Min(count, maximum));
		var stride = Marshal.SizeOf<IcmpEchoReply>();
		var results = new PingNativeResult[length];
		for (var index = 0; index < length; index++)
		{
			var native = Marshal.PtrToStructure<IcmpEchoReply>(IntPtr.Add(buffer, index * stride));
			var addressBytes = new byte[4];
			BinaryPrimitives.WriteUInt32LittleEndian(addressBytes, native.Address);
			results[index] = new PingNativeResult(
				(IPStatus)native.Status,
				native.RoundtripTime,
				BinaryNetworkError.None,
				IpAddressValue.FromIPv4Bytes(addressBytes));
		}
		return results;
	}

	internal static PingNativeResult[] ParseIpv6Replies(IntPtr buffer, uint count, ushort maximum)
	{
		if (buffer == IntPtr.Zero || count == 0 || maximum == 0) return [];
		var length = checked((int)Math.Min(count, maximum));
		var stride = Marshal.SizeOf<IcmpV6EchoReplyLayout>();
		var addressOffset = checked((int)Marshal.OffsetOf<IcmpV6EchoReplyLayout>(
			nameof(IcmpV6EchoReplyLayout.AddressFirstByte)));
		var statusOffset = checked((int)Marshal.OffsetOf<IcmpV6EchoReplyLayout>(
			nameof(IcmpV6EchoReplyLayout.Status)));
		var roundtripOffset = checked((int)Marshal.OffsetOf<IcmpV6EchoReplyLayout>(
			nameof(IcmpV6EchoReplyLayout.RoundTripTime)));
		var results = new PingNativeResult[length];
		for (var index = 0; index < length; index++)
		{
			var current = IntPtr.Add(buffer, index * stride);
			var addressBytes = new byte[16];
			Marshal.Copy(IntPtr.Add(current, addressOffset), addressBytes, 0, addressBytes.Length);
			var status = (IPStatus)Marshal.ReadInt32(current, statusOffset);
			var roundtrip = unchecked((uint)Marshal.ReadInt32(current, roundtripOffset));
			results[index] = new PingNativeResult(
				status,
				roundtrip,
				BinaryNetworkError.None,
				IpAddressValue.FromIPv6Bytes(addressBytes));
		}
		return results;
	}

	// Windows IPExport.h packs IPV6_ADDRESS_EX to 26 bytes, then restores
	// default alignment for ICMPV6_ECHO_REPLY. The following ULONG fields
	// therefore start at offsets 28 and 32 and the outer structure is 36 bytes.
	[StructLayout(LayoutKind.Explicit, Size = 36)]
	internal struct IcmpV6EchoReplyLayout
	{
		[FieldOffset(6)]
		public byte AddressFirstByte;

		[FieldOffset(28)]
		public uint Status;

		[FieldOffset(32)]
		public uint RoundTripTime;
	}

	private static PingNativeResult SendNativeIpv4(
		IpAddressValue target, IpAddressValue source, byte[] payload, uint timeoutMs)
	{
		var handle = IcmpCreateFile();
		if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return NativeHandleFailure();
		var request = AllocPayload(payload);
		var reply = Marshal.AllocHGlobal(512 + payload.Length);
		try
		{
			var sent = IcmpSendEcho2Ex(handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
				ToNativeIpv4(source), ToNativeIpv4(target), request, checked((ushort)payload.Length),
				IntPtr.Zero, reply, checked((uint)(512 + payload.Length)), timeoutMs);
			if (sent == 0) return NativeSendFailure();
			var status = (IPStatus)Marshal.ReadInt32(reply, 4);
			return new(status, status == IPStatus.Success ? checked((uint)Math.Max(0, Marshal.ReadInt32(reply, 8))) : 0,
				BinaryNetworkError.None, target);
		}
		finally
		{
			if (request != IntPtr.Zero) Marshal.FreeHGlobal(request);
			Marshal.FreeHGlobal(reply);
			IcmpCloseHandle(handle);
		}
	}

	private static PingNativeResult SendNativeIpv6(
		IpAddressValue target,
		IpAddressValue source,
		uint interfaceIndex,
		byte[] payload,
		uint timeoutMs)
	{
		var handle = Icmp6CreateFile();
		if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return NativeHandleFailure();
		var sourceAddress = AllocSockaddr(source, interfaceIndex);
		var targetAddress = AllocSockaddr(target, interfaceIndex);
		var request = AllocPayload(payload);
		var reply = Marshal.AllocHGlobal(1024 + payload.Length);
		try
		{
			var sent = Icmp6SendEcho2(handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
				sourceAddress, targetAddress, request, checked((ushort)payload.Length), IntPtr.Zero,
				reply, checked((uint)(1024 + payload.Length)), timeoutMs);
			if (sent == 0) return NativeSendFailure();
			var replies = ParseIpv6Replies(reply, sent, 1);
			return replies.Length == 0 ? NativeSendFailure() : replies[0];
		}
		finally
		{
			Marshal.FreeHGlobal(sourceAddress);
			Marshal.FreeHGlobal(targetAddress);
			if (request != IntPtr.Zero) Marshal.FreeHGlobal(request);
			Marshal.FreeHGlobal(reply);
			IcmpCloseHandle(handle);
		}
	}

	private static IntPtr AllocPayload(byte[] payload)
	{
		if (payload.Length == 0) return IntPtr.Zero;
		var pointer = Marshal.AllocHGlobal(payload.Length);
		Marshal.Copy(payload, 0, pointer, payload.Length);
		return pointer;
	}

	private static IntPtr AllocSockaddr(IpAddressValue address, uint interfaceIndex)
	{
		var pointer = Marshal.AllocHGlobal(28);
		Span<byte> zero = stackalloc byte[28];
		Marshal.Copy(zero.ToArray(), 0, pointer, zero.Length);
		Marshal.WriteInt16(pointer, 0, (short)23);
		var bytes = address.ToIPAddress().GetAddressBytes();
		Marshal.Copy(bytes, 0, IntPtr.Add(pointer, 8), bytes.Length);
		Marshal.WriteInt32(
			pointer,
			24,
			address.RequiresInterfaceScope ? unchecked((int)interfaceIndex) : 0);
		return pointer;
	}

	private static IPAddress ToSystemAddress(IpAddressValue address, uint interfaceIndex) =>
		NetworkIpAddressInterop.ToSystemAddress(address, interfaceIndex);

	private static uint ToNativeIpv4(IpAddressValue address) =>
		BinaryPrimitives.ReadUInt32LittleEndian(address.ToIPAddress().GetAddressBytes());

	private static PingNativeResult NativeHandleFailure()
	{
		var error = Marshal.GetLastPInvokeError();
		return new(IPStatus.Unknown, 0, new BinaryNetworkError(3, error, Marshal.GetHRForLastWin32Error()), default);
	}

	private static PingNativeResult NativeSendFailure()
	{
		var error = Marshal.GetLastPInvokeError();
		return new(error == (int)IPStatus.TimedOut ? IPStatus.TimedOut : IPStatus.Unknown, 0,
			new BinaryNetworkError(3, error, Marshal.GetHRForLastWin32Error()), default);
	}

	private static PingNativeBatchResult NativeBatchHandleFailure()
	{
		var error = Marshal.GetLastPInvokeError();
		return new([], false, new BinaryNetworkError(3, error, Marshal.GetHRForLastWin32Error()));
	}

	private static PingNativeBatchResult NativeBatchSendFailure()
	{
		var error = Marshal.GetLastPInvokeError();
		var timedOut = error == (int)IPStatus.TimedOut;
		return new([], timedOut, new BinaryNetworkError(3, error, Marshal.GetHRForLastWin32Error()));
	}

	private static uint ClampToUInt(double value) =>
		value <= 0 ? 0 : value >= uint.MaxValue ? uint.MaxValue : (uint)Math.Round(value);

	internal readonly record struct PingNativeResult(
		IPStatus Status,
		uint RoundtripTimeMs,
		BinaryNetworkError Error,
		IpAddressValue ReplyAddress);

	private readonly record struct PingNativeBatchResult(
		PingNativeResult[] Replies,
		bool SubmissionEstablished,
		BinaryNetworkError Error);

	[StructLayout(LayoutKind.Sequential)]
	private struct IcmpEchoReply
	{
		public uint Address;
		public uint Status;
		public uint RoundtripTime;
		public ushort DataSize;
		public ushort Reserved;
		public IntPtr Data;
		public IpOptionInformation Options;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct IpOptionInformation
	{
		public byte Ttl;
		public byte Tos;
		public byte Flags;
		public byte OptionsSize;
		public IntPtr OptionsData;
	}

	[DllImport("iphlpapi.dll", SetLastError = true)] private static extern IntPtr IcmpCreateFile();
	[DllImport("iphlpapi.dll", SetLastError = true)] private static extern IntPtr Icmp6CreateFile();
	[DllImport("iphlpapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool IcmpCloseHandle(IntPtr handle);
	[DllImport("iphlpapi.dll", SetLastError = true)]
	private static extern uint IcmpSendEcho2Ex(
		IntPtr handle, IntPtr @event, IntPtr apcRoutine, IntPtr apcContext,
		uint sourceAddress, uint destinationAddress, IntPtr requestData, ushort requestSize,
		IntPtr requestOptions, IntPtr replyBuffer, uint replySize, uint timeout);
	[DllImport("iphlpapi.dll", SetLastError = true)]
	private static extern uint Icmp6SendEcho2(
		IntPtr handle, IntPtr @event, IntPtr apcRoutine, IntPtr apcContext,
		IntPtr sourceAddress, IntPtr destinationAddress, IntPtr requestData, ushort requestSize,
		IntPtr requestOptions, IntPtr replyBuffer, uint replySize, uint timeout);
}

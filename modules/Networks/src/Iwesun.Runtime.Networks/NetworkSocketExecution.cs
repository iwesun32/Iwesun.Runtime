using System.Net;
using System.Net.Sockets;

namespace Iwesun.Runtime.Networks;

public enum NetworkSocketOperation : byte
{
	Unspecified = 0,
	TcpConnect = 1,
	UdpExchange = 2,
}

public readonly record struct NetworkSocketExecutionRequest(
	NetworkExecutionIdentity Identity,
	RequestedAccessPlan Requested,
	ResolvedAccessPlan Resolved,
	NetworkAccessSecurityBoundary SecurityBoundary,
	NetworkSocketOperation Operation,
	ushort RemotePort,
	byte[] Payload,
	int ReceiveBufferSize)
{
	public bool IsValid =>
		Identity.HasBranch &&
		Requested.PathProvider is NetworkPathProvider.Automatic or NetworkPathProvider.Direct &&
		Resolved.IsValid && SecurityBoundary.IsValid &&
		Operation != NetworkSocketOperation.Unspecified &&
		RemotePort > 0 &&
		(Operation != NetworkSocketOperation.UdpExchange || Payload is { Length: > 0 }) &&
		ReceiveBufferSize >= 0;
}

public readonly record struct NetworkSocketExecutionResult(
	NetworkExecutionIdentity Identity,
	ProtocolOutcome Outcome,
	AccessCompliance Compliance,
	ActualAccessEvidence Evidence,
	BinaryNetworkError Error,
	string ReasonCode,
	byte[] Payload,
	long CompletedAtUnixMs,
	NetworkFlowSerial FlowSerial = default,
	uint SocketGeneration = 0,
	NetworkDatagramDispatchEvidence DispatchEvidence = default)
{
	public bool IsSuccess => Outcome == ProtocolOutcome.Succeeded;
}

/// <summary>Executes Automatic and Direct socket operations without changing system routes.</summary>
public sealed partial class NetworkSocketExecutor
{
	private readonly NetworkAccessConstraintResolver _resolver;
	private readonly INetworkWfpConnectionPolicyBackend _wfpBackend;

	public NetworkSocketExecutor(NetworkAccessConstraintResolver resolver)
		: this(resolver, new IsolatedNetworkWfpConnectionPolicyBackend(
			WindowsNetworkWfpConnectionPolicyBackend.CreateDefault()))
	{
	}

	internal NetworkSocketExecutor(
		NetworkAccessConstraintResolver resolver,
		INetworkWfpConnectionPolicyBackend wfpBackend)
	{
		ArgumentNullException.ThrowIfNull(resolver);
		ArgumentNullException.ThrowIfNull(wfpBackend);
		_resolver = resolver;
		_wfpBackend = wfpBackend;
	}

	internal NetworkWfpPolicyCapability QueryWfpCapability() => _wfpBackend.QueryCapability();

	public async ValueTask<NetworkSocketExecutionResult> ExecuteAsync(
		NetworkSocketExecutionRequest request,
		CancellationToken cancellationToken = default)
	{
		if (!TryValidateRequest(request, out var validationFailure))
			return Failure(
				request,
				ProtocolOutcome.Rejected,
				validationFailure.ReasonCode,
				ToBinaryError(validationFailure.PlatformError));

		try
		{
			return request.Operation switch
			{
				NetworkSocketOperation.TcpConnect => await ExecuteTcpAsync(request, cancellationToken).ConfigureAwait(false),
				NetworkSocketOperation.UdpExchange => await ExecuteUdpAsync(request, cancellationToken).ConfigureAwait(false),
				_ => Failure(request, ProtocolOutcome.Rejected, NetworkAccessFailureCodes.SocketRequestInvalid),
			};
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			return Failure(request, ProtocolOutcome.Cancelled, "socket-operation-cancelled");
		}
		catch (NetworkWfpPolicyCleanupException ex)
		{
			return Failure(
				request,
				ProtocolOutcome.TransportFailed,
				NetworkAccessFailureCodes.WfpPolicyCleanupFailed,
				new BinaryNetworkError(
					3,
					ex.PlatformError.NativeCode,
					ex.PlatformError.HResult));
		}
		catch (Exception ex)
		{
			return Failure(
				request,
				ProtocolOutcome.TransportFailed,
				NetworkAccessFailureCodes.SocketOperationFailed,
				BinaryNetworkError.FromException(ex, false, false));
		}
	}

	internal bool TryValidateRequest(
		in NetworkSocketExecutionRequest request,
		out NetworkAccessFailure failure)
	{
		if (!request.IsValid)
		{
			failure = new(NetworkAccessFailureCodes.SocketRequestInvalid, default);
			return false;
		}
		if (request.Requested.PathProvider != request.Resolved.PathProvider ||
			!_resolver.TryCreateNormalizedStableKey(
				request.Requested,
				request.SecurityBoundary,
				out var stableKey,
				out _) ||
			stableKey != request.Resolved.StableKey)
		{
			failure = new(NetworkAccessFailureCodes.ResolvedPlanMismatch, default);
			return false;
		}
		if (!_resolver.IsCurrent(request.Resolved))
		{
			failure = new(NetworkAccessFailureCodes.ResolvedPlanStale, default);
			return false;
		}
		if (RequiresWfpPolicy(request))
		{
			var capability = _wfpBackend.QueryCapability();
			if (!capability.Supported)
			{
				failure = new(capability.ReasonCode, capability.PlatformError);
				return false;
			}
		}
		if (request.Requested.RouteScope.Kind == NetworkRouteScopeSelectionKind.ExactCompartment)
		{
			failure = new(NetworkAccessFailureCodes.RouteScopeBackendUnavailable, default);
			return false;
		}

		failure = default;
		return true;
	}

	private async ValueTask<NetworkSocketExecutionResult> ExecuteTcpAsync(
		NetworkSocketExecutionRequest request,
		CancellationToken cancellationToken)
	{
		using var socket = CreateSocket(request.Resolved.Destination);
		ApplyPlan(socket, request);
		using var policy = AcquireWfpPolicyIfRequired(socket, request, ProtocolType.Tcp, out var policyFailure);
		if (!string.IsNullOrEmpty(policyFailure.ReasonCode))
			return Failure(request, ProtocolOutcome.Rejected, policyFailure.ReasonCode);
		await socket.ConnectAsync(
			new IPEndPoint(
				NetworkIpAddressInterop.ToSystemAddress(
					request.Resolved.Destination,
					request.Resolved.Interface.InterfaceIndex),
				request.RemotePort),
			cancellationToken).ConfigureAwait(false);
		return CreateSuccessResult(request, socket, default, ProofKind.Observed, policyEnforced: policy is not null);
	}

	private async ValueTask<NetworkSocketExecutionResult> ExecuteUdpAsync(
		NetworkSocketExecutionRequest request,
		CancellationToken cancellationToken)
	{
		using var socket = CreateSocket(request.Resolved.Destination, SocketType.Dgram, ProtocolType.Udp);
		ApplyPlan(socket, request);
		using var policy = AcquireWfpPolicyIfRequired(socket, request, ProtocolType.Udp, out var policyFailure);
		if (!string.IsNullOrEmpty(policyFailure.ReasonCode))
			return Failure(request, ProtocolOutcome.Rejected, policyFailure.ReasonCode);
		var packetInformationEnabled = TryEnablePacketInformation(socket, request.Resolved.Destination);
		var remote = new IPEndPoint(
			NetworkIpAddressInterop.ToSystemAddress(
				request.Resolved.Destination,
				request.Resolved.Interface.InterfaceIndex),
			request.RemotePort);
		await socket.SendToAsync(request.Payload, SocketFlags.None, remote, cancellationToken).ConfigureAwait(false);

		var buffer = new byte[Math.Max(1, request.ReceiveBufferSize)];
		EndPoint sender = request.Resolved.Destination.IsIPv4
			? new IPEndPoint(IPAddress.Any, 0)
			: new IPEndPoint(IPAddress.IPv6Any, 0);
		if (packetInformationEnabled)
		{
			while (true)
			{
				var received = await socket.ReceiveMessageFromAsync(
					buffer,
					SocketFlags.None,
					sender,
					cancellationToken).ConfigureAwait(false);
				var actualRemote = (IPEndPoint)received.RemoteEndPoint;
				if (!IsExpectedUdpRemote(actualRemote, remote)) continue;
				Array.Resize(ref buffer, received.ReceivedBytes);
				return CreateSuccessResult(
					request,
					socket,
					buffer,
					ProofKind.Observed,
					received.PacketInformation.Interface > 0 ? checked((uint)received.PacketInformation.Interface) : 0,
					actualRemote,
					policyEnforced: policy is not null);
			}
		}

		while (true)
		{
			var fallback = await socket.ReceiveFromAsync(
				buffer, SocketFlags.None, sender, cancellationToken).ConfigureAwait(false);
			var actualRemote = (IPEndPoint)fallback.RemoteEndPoint;
			if (!IsExpectedUdpRemote(actualRemote, remote)) continue;
			Array.Resize(ref buffer, fallback.ReceivedBytes);
			return CreateSuccessResult(
				request, socket, buffer, ProofKind.Observed, 0, actualRemote,
				policyEnforced: policy is not null);
		}
	}

	internal static bool IsExpectedUdpRemote(IPEndPoint actual, IPEndPoint expected) =>
		actual.Port == expected.Port && actual.Address.Equals(expected.Address);

	internal INetworkWfpConnectionPolicyLease? AcquireWfpPolicyIfRequired(
		Socket socket,
		in NetworkSocketExecutionRequest request,
		ProtocolType protocol,
		out NetworkAccessFailure failure)
	{
		if (!RequiresWfpPolicy(request))
		{
			failure = default;
			return null;
		}

		if (socket.LocalEndPoint is not IPEndPoint local || local.Port <= 0)
		{
			failure = new(NetworkAccessFailureCodes.WfpPolicyRequestInvalid, default);
			return null;
		}

		var policyRequest = new NetworkWfpConnectionPolicyRequest(
			Guid.NewGuid(),
			request.Identity,
			NetworkIpAddressInterop.FromSystemAddress(local.Address),
			request.Resolved.Interface,
			request.Resolved.NextHop,
			checked((ushort)local.Port),
			request.Resolved.Destination,
			request.RemotePort,
			protocol,
			Environment.ProcessPath ?? string.Empty);
		var result = _wfpBackend.Acquire(policyRequest);
		failure = result.Succeeded ? default : new(result.ReasonCode, result.PlatformError);
		return result.Lease;
	}

	internal static Socket CreateSocket(
		IpAddressValue destination,
		SocketType socketType = SocketType.Stream,
		ProtocolType protocolType = ProtocolType.Tcp) =>
		new(destination.IsIPv4 ? AddressFamily.InterNetwork : AddressFamily.InterNetworkV6, socketType, protocolType);

	internal static void ApplyPlan(Socket socket, NetworkSocketExecutionRequest request)
	{
		if (request.Requested.PathProvider == NetworkPathProvider.Direct)
		{
			if (RequiresExplicitBinding(request.Requested.Interface.Kind) &&
				request.Resolved.Interface.InterfaceIndex > 0)
			{
				var level = request.Resolved.Destination.IsIPv4 ? SocketOptionLevel.IP : SocketOptionLevel.IPv6;
				var value = request.Resolved.Destination.IsIPv4
					? IPAddress.HostToNetworkOrder(checked((int)request.Resolved.Interface.InterfaceIndex))
					: checked((int)request.Resolved.Interface.InterfaceIndex);
				socket.SetSocketOption(level, (SocketOptionName)31, value);
			}

			if (RequiresExplicitBinding(request.Requested.Source.Kind) ||
				request.Requested.LocalEndpoint.Kind is NetworkLocalEndpointSelectionKind.ExactPort or
					NetworkLocalEndpointSelectionKind.AllowedRange or
					NetworkLocalEndpointSelectionKind.Ephemeral)
			{
				var source = RequiresExplicitBinding(request.Requested.Source.Kind)
					? NetworkIpAddressInterop.ToSystemAddress(
						request.Resolved.Source,
						request.Resolved.Interface.InterfaceIndex)
					: request.Resolved.Destination.IsIPv4 ? IPAddress.Any : IPAddress.IPv6Any;
				socket.Bind(new IPEndPoint(source, request.Resolved.LocalPort));
			}
		}

		if (request.Requested.IpPacketPolicy.Kind == NetworkIpPacketPolicyKind.Explicit &&
			request.Requested.IpPacketPolicy.TimeToLiveOrHopLimit.HasValue)
		{
			socket.Ttl = request.Requested.IpPacketPolicy.TimeToLiveOrHopLimit.Value;
		}
		if (request.Requested.IpPacketPolicy.Kind == NetworkIpPacketPolicyKind.Explicit)
		{
			if (request.Requested.IpPacketPolicy.DontFragment) socket.DontFragment = true;
			if (request.Requested.IpPacketPolicy.AllowBroadcast) socket.EnableBroadcast = true;
			socket.MulticastLoopback = request.Requested.IpPacketPolicy.AllowMulticastLoopback;
		}
	}

	internal static NetworkSocketExecutionResult CreateSuccessResult(
		NetworkSocketExecutionRequest request,
		Socket socket,
		byte[]? payload,
		ProofKind endpointProof,
		uint observedInterfaceIndex = 0,
		IPEndPoint? observedRemoteEndpoint = null,
		bool policyEnforced = false)
	{
		var local = (IPEndPoint)socket.LocalEndPoint!;
		var remote = observedRemoteEndpoint ?? socket.RemoteEndPoint as IPEndPoint ??
			new IPEndPoint(
				NetworkIpAddressInterop.ToSystemAddress(
					request.Resolved.Destination,
					request.Resolved.Interface.InterfaceIndex),
				request.RemotePort);
		var localAddress = NetworkIpAddressInterop.FromSystemAddress(local.Address);
		var remoteAddress = NetworkIpAddressInterop.FromSystemAddress(remote.Address);
		var interfaceProof = policyEnforced
			? ProofKind.PolicyEnforced
			: observedInterfaceIndex > 0
			? ProofKind.Observed
			: request.Requested.PathProvider == NetworkPathProvider.Direct &&
				RequiresExplicitBinding(request.Requested.Interface.Kind) &&
				request.Resolved.Interface.InterfaceIndex > 0
				? ProofKind.ApiBinding
				: ProofKind.Inferred;
		var interfaceMatches = observedInterfaceIndex == 0 ||
			request.Resolved.Interface.InterfaceIndex == 0 ||
			observedInterfaceIndex == request.Resolved.Interface.InterfaceIndex;
		var actualInterface = observedInterfaceIndex > 0 && !interfaceMatches
			? new NetworkInterfaceIdentity(Guid.Empty, 0, $"if-index-{observedInterfaceIndex}", observedInterfaceIndex)
			: observedInterfaceIndex > 0
				? request.Resolved.Interface with { InterfaceIndex = observedInterfaceIndex }
			: request.Resolved.Interface;
		var interfaceCompliance = !interfaceMatches && RequiresExplicitBinding(request.Requested.Interface.Kind)
			? AccessCompliance.Violated
			: ComplianceFor(interfaceProof);
		var evidence = new ActualAccessEvidence(
			request.Identity,
			new(request.Requested.PathProvider, ProofKind.ApiBinding, AccessCompliance.Satisfied),
			new(remoteAddress, endpointProof, AccessCompliance.Satisfied),
			new(localAddress, endpointProof, AccessCompliance.Satisfied),
			new(actualInterface, interfaceProof, interfaceCompliance),
			new(
				request.Resolved.NextHop,
				policyEnforced ? ProofKind.PolicyEnforced : ProofKind.Inferred,
				policyEnforced ? AccessCompliance.Satisfied : AccessCompliance.EvidenceIncomplete),
			new(checked((ushort)local.Port), endpointProof, AccessCompliance.Satisfied),
			new(request.Resolved.RouteCompartmentId, ProofKind.Inferred, AccessCompliance.EvidenceIncomplete),
			new(
				request.Requested.IpPacketPolicy,
				request.Requested.IpPacketPolicy.Kind == NetworkIpPacketPolicyKind.Explicit
					? ProofKind.ApiBinding
					: ProofKind.Inferred,
				request.Requested.IpPacketPolicy.Kind == NetworkIpPacketPolicyKind.Explicit
					? AccessCompliance.Satisfied
					: AccessCompliance.NotApplicable),
			new(
				request.Requested.RouteAdapter,
				ProofKind.Unavailable,
				AccessCompliance.NotApplicable));
		var interfaceRequired = RequiresExplicitBinding(request.Requested.Interface.Kind);
		var nextHopRequiresExactProof = request.Requested.NextHop.IsOnLink ||
			RequiresExplicitBinding(request.Requested.NextHop.Kind);
		var compliance = interfaceCompliance == AccessCompliance.Violated
			? AccessCompliance.Violated
			: policyEnforced
			? AccessCompliance.Satisfied
			: (!interfaceRequired || interfaceProof is ProofKind.Observed or ProofKind.ApiBinding) &&
				!nextHopRequiresExactProof
			? AccessCompliance.Satisfied
			: AccessCompliance.EvidenceIncomplete;
		return new NetworkSocketExecutionResult(
			request.Identity,
			ProtocolOutcome.Succeeded,
			compliance,
			evidence,
			default,
			string.Empty,
			payload ?? [],
			DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
	}

	private static AccessCompliance ComplianceFor(ProofKind proof) => proof switch
	{
		ProofKind.Observed or ProofKind.ApiBinding or ProofKind.PolicyEnforced => AccessCompliance.Satisfied,
		_ => AccessCompliance.EvidenceIncomplete,
	};

	private static bool RequiresExplicitBinding(NetworkSelectorKind kind) =>
		kind is not NetworkSelectorKind.SystemSelected and not NetworkSelectorKind.NotApplicable;

	internal static bool RequiresWfpPolicy(in NetworkSocketExecutionRequest request) =>
		request.Requested.PathProvider == NetworkPathProvider.Direct &&
		request.Requested.NextHop.Kind == NetworkSelectorKind.Exact &&
		!request.Requested.NextHop.IsOnLink;

	internal static bool TryEnablePacketInformation(Socket socket, IpAddressValue destination)
	{
		try
		{
			socket.SetSocketOption(
				destination.IsIPv4 ? SocketOptionLevel.IP : SocketOptionLevel.IPv6,
				SocketOptionName.PacketInformation,
				true);
			return true;
		}
		catch (SocketException)
		{
			return false;
		}
		catch (PlatformNotSupportedException)
		{
			return false;
		}
	}

	private static NetworkSocketExecutionResult Failure(
		NetworkSocketExecutionRequest request,
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
			DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

	private static BinaryNetworkError ToBinaryError(NetworkPlatformError error) => error.IsValid
		? new BinaryNetworkError(3, error.NativeCode, error.HResult)
		: default;
}

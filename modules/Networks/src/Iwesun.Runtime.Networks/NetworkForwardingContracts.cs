namespace Iwesun.Runtime.Networks;

public readonly record struct NetworkTransportEndpoint(IpAddressValue Address, ushort Port)
{
	public bool IsValid => Address.Family is 4 or 6 && Port > 0;
}

public enum NetworkDatagramCorrelationKind : byte
{
	Unspecified = 0,
	DedicatedLocalEndpoint = 1,
	ConnectedSocket = 2,
	ProtocolToken = 3,
	NatTuple = 4,
	EncapsulatedToken = 5,
}

public readonly record struct NetworkFlowTerminalResult(
	NetworkFlowSerial FlowSerial,
	NetworkExecutionIdentity Identity,
	NetworkExecutionTerminalState TerminalState,
	ProtocolOutcome ProtocolOutcome,
	AccessCompliance AccessCompliance,
	string ReasonCode)
{
	public bool IsValid => FlowSerial.IsValid && Identity.HasBranch &&
		TerminalState != NetworkExecutionTerminalState.Unspecified &&
		ProtocolOutcome != ProtocolOutcome.Unspecified &&
		AccessCompliance != AccessCompliance.Unspecified && ReasonCode is not null;
}

public readonly record struct NetworkDnsProxyFlowRequest(
	NetworkFlowSerial FlowSerial,
	NetworkExecutionIdentity Identity,
	NetworkTransportEndpoint Client,
	ushort OriginalTransactionId,
	NetworkTransportEndpoint Upstream,
	ushort InternalTransactionId,
	ReadOnlyMemory<byte> Query)
{
	public bool IsValid => FlowSerial.IsValid && Identity.HasBranch && Client.IsValid && Upstream.IsValid &&
		!Query.IsEmpty;
}

public readonly record struct NetworkDnsProxyFlowResult(
	NetworkFlowTerminalResult Terminal,
	ushort OriginalTransactionId,
	ushort InternalTransactionId,
	ReadOnlyMemory<byte> Response)
{
	public bool IsValid => Terminal.IsValid;
}

public interface INetworkDnsProxyDataPlane : INetworkDataPlane
{
	ValueTask<NetworkDnsProxyFlowResult> ExchangeAsync(
		NetworkDnsProxyFlowRequest request,
		CancellationToken cancellationToken = default);
}

public enum NetworkNatMappingMode : byte
{
	Unspecified = 0,
	EndpointIndependent = 1,
	AddressDependent = 2,
	AddressAndPortDependent = 3,
}

public enum NetworkNatTranslationKind : byte
{
	Unspecified = 0,
	Source = 1,
	Destination = 2,
	Bidirectional = 3,
	Nat64 = 4,
	NptV6 = 5,
	Nat66 = 6,
}

public readonly record struct NetworkIpFlowTuple(
	byte Protocol,
	NetworkTransportEndpoint Source,
	NetworkTransportEndpoint Destination,
	uint Zone)
{
	public bool IsValid => Protocol > 0 && Source.IsValid && Destination.IsValid;
}

public readonly record struct NetworkNatMappingRequest(
	NetworkFlowSerial FlowSerial,
	NetworkExecutionIdentity Identity,
	NetworkIpFlowTuple Original,
	NetworkNatMappingMode MappingMode,
	NetworkNatTranslationKind TranslationKind,
	RequestedAccessPlan EgressPlan)
{
	public bool IsValid => FlowSerial.IsValid && Identity.HasBranch && Original.IsValid &&
		MappingMode != NetworkNatMappingMode.Unspecified &&
		TranslationKind != NetworkNatTranslationKind.Unspecified && EgressPlan.TryValidate(out _);
}

public readonly record struct NetworkNatMappingResult(
	NetworkFlowTerminalResult Terminal,
	NetworkIpFlowTuple Original,
	NetworkIpFlowTuple Translated,
	uint MappingGeneration)
{
	public bool IsValid => Terminal.IsValid && Original.IsValid && Translated.IsValid && MappingGeneration > 0;
}

public interface INetworkNatDataPlane : INetworkDataPlane
{
	ValueTask<NetworkNatMappingResult> OpenMappingAsync(
		NetworkNatMappingRequest request,
		CancellationToken cancellationToken = default);
}

public readonly record struct NetworkSocketProxyFlowRequest(
	NetworkFlowSerial FlowSerial,
	NetworkExecutionIdentity Identity,
	NetworkTransportEndpoint Client,
	NetworkTransportEndpoint Destination,
	RequestedAccessPlan EgressPlan)
{
	public bool IsValid => FlowSerial.IsValid && Identity.HasBranch && Client.IsValid && Destination.IsValid &&
		EgressPlan.TryValidate(out _);
}

public readonly record struct NetworkSocketProxyFlowResult(
	NetworkFlowTerminalResult Terminal,
	ActualAccessEvidence ClientLeg,
	ActualAccessEvidence EgressLeg)
{
	public bool IsValid => Terminal.IsValid && ClientLeg.IsValid && EgressLeg.IsValid;
}

public interface INetworkSocketProxyDataPlane : INetworkDataPlane
{
	ValueTask<NetworkSocketProxyFlowResult> ForwardAsync(
		NetworkSocketProxyFlowRequest request,
		CancellationToken cancellationToken = default);
}

public enum NetworkHttpProxyProtocol : byte
{
	Unspecified = 0,
	Http11 = 1,
	Http2 = 2,
	Http3 = 3,
	ConnectTunnel = 4,
}

public readonly record struct NetworkHttpProxyFlowRequest(
	NetworkFlowSerial FlowSerial,
	NetworkExecutionIdentity Identity,
	NetworkHttpProxyProtocol Protocol,
	long ProtocolStreamId,
	string LogicalHost,
	NetworkTransportEndpoint Destination,
	RequestedAccessPlan EgressPlan)
{
	public bool IsValid => FlowSerial.IsValid && Identity.HasBranch &&
		Protocol != NetworkHttpProxyProtocol.Unspecified && ProtocolStreamId >= 0 &&
		!string.IsNullOrWhiteSpace(LogicalHost) && Destination.IsValid && EgressPlan.TryValidate(out _);
}

public readonly record struct NetworkHttpProxyFlowResult(
	NetworkFlowTerminalResult Terminal,
	int StatusCode,
	long ProtocolStreamId,
	ActualAccessEvidence ClientLeg,
	ActualAccessEvidence EgressLeg)
{
	public bool IsValid => Terminal.IsValid && StatusCode >= 0 && ProtocolStreamId >= 0 &&
		ClientLeg.IsValid && EgressLeg.IsValid;
}

public interface INetworkHttpProxyDataPlane : INetworkDataPlane
{
	ValueTask<NetworkHttpProxyFlowResult> ForwardAsync(
		NetworkHttpProxyFlowRequest request,
		CancellationToken cancellationToken = default);
}

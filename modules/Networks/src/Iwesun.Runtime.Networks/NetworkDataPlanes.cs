namespace Iwesun.Runtime.Networks;

[Flags]
public enum NetworkDataPlaneCapabilities : ushort
{
	None = 0,
	ByteStream = 1 << 0,
	Datagram = 1 << 1,
	IpPacket = 1 << 2,
	Transparent = 1 << 3,
	External = 1 << 4,
	VirtualAdapter = 1 << 5,
	DnsProxy = 1 << 6,
	Nat = 1 << 7,
	SocketProxy = 1 << 8,
	HttpProxy = 1 << 9,
	PacketCapture = 1 << 10,
	PacketInject = 1 << 11,
}

public enum NetworkDataPlaneState : byte
{
	Unspecified = 0,
	Ready = 1,
	Degraded = 2,
	Draining = 3,
	Stopped = 4,
}

public readonly record struct NetworkDataPlaneIdentity(
	string AdapterId,
	int CapabilityVersion,
	uint Generation)
{
	public bool IsValid => !string.IsNullOrWhiteSpace(AdapterId) && CapabilityVersion > 0 && Generation > 0;
}

public readonly record struct NetworkDataPlaneDescriptor(
	NetworkDataPlaneIdentity Identity,
	NetworkDataPlaneCapabilities Capabilities,
	NetworkDataPlaneState State)
{
	public bool IsValid => Identity.IsValid && Capabilities != NetworkDataPlaneCapabilities.None &&
		State != NetworkDataPlaneState.Unspecified;
}

public readonly record struct NetworkDatagramDispatchEvidence(
	NetworkExecutionIdentity Identity,
	NetworkFlowSerial FlowSerial,
	uint SocketGeneration,
	IpAddressValue ActualLocalAddress,
	ushort ActualLocalPort,
	IpAddressValue ExpectedRemoteAddress,
	ushort ExpectedRemotePort,
	NetworkInterfaceIdentity ActualInterface,
	long DispatchedAtUnixMs)
{
	public bool IsValid => Identity.HasBranch && FlowSerial.IsValid && SocketGeneration > 0 &&
		ActualLocalAddress.Family is 4 or 6 && ActualLocalPort > 0 && ExpectedRemoteAddress.Family is 4 or 6 &&
		ExpectedRemotePort > 0 && DispatchedAtUnixMs > 0;
}

public readonly record struct NetworkDatagramDataPlaneCounters(
	long UnexpectedRemoteDatagrams,
	long UnmatchedDatagrams,
	long LateDatagrams,
	long CapacityRejections = 0,
	int PoolCount = 0,
	int SocketSlotCount = 0,
	int ActiveSlotCount = 0,
	int QuarantinedSlotCount = 0,
	int PeakSlotsPerPool = 0);

public interface INetworkDataPlane : IDisposable
{
	NetworkDataPlaneDescriptor Descriptor { get; }
}

public interface INetworkDatagramDataPlane : INetworkDataPlane
{
	event Action<NetworkDatagramDispatchEvidence>? DatagramDispatched;
	NetworkDatagramDataPlaneCounters Counters { get; }

	ValueTask<NetworkSocketExecutionResult> ExecuteDatagramAsync(
		NetworkSocketExecutionRequest request,
		CancellationToken cancellationToken = default);
}

public interface INetworkStreamDataPlane : INetworkDataPlane
{
	ValueTask<NetworkSocketExecutionResult> ExecuteStreamAsync(
		NetworkSocketExecutionRequest request,
		CancellationToken cancellationToken = default);
}

public interface INetworkPacketDataPlane : INetworkDataPlane
{
	event Func<NetworkPacketCapture, CancellationToken, ValueTask<NetworkPacketDataPlaneResult>>? PacketCaptured;

	ValueTask<NetworkPacketDataPlaneResult> ForwardAsync(
		NetworkPacketDataPlaneRequest request,
		CancellationToken cancellationToken = default);

	ValueTask<NetworkPacketDataPlaneResult> InjectAsync(
		NetworkPacketDataPlaneInjection injection,
		CancellationToken cancellationToken = default);
}

/// <summary>Packet direction at a transparent forwarding boundary.</summary>
public enum NetworkPacketDirection : byte
{
	Unspecified = 0,
	ClientToExternal = 1,
	ExternalToClient = 2,
}

/// <summary>Immutable metadata delivered with one captured packet. The packet is opaque to Runtime.</summary>
public readonly record struct NetworkPacketCapture(
	NetworkFlowSerial FlowSerial,
	NetworkExecutionIdentity Identity,
	NetworkPacketDirection Direction,
	NetworkInterfaceIdentity Interface,
	ReadOnlyMemory<byte> Packet,
	long CapturedAtUnixMs)
{
	public bool IsValid => FlowSerial.IsValid && Identity.HasBranch &&
		Direction != NetworkPacketDirection.Unspecified && Interface.Luid > 0 && !Packet.IsEmpty && CapturedAtUnixMs > 0;
}

/// <summary>One explicitly directed packet injection. A backend must not silently use system routing.</summary>
public readonly record struct NetworkPacketDataPlaneInjection(
	NetworkFlowSerial FlowSerial,
	NetworkExecutionIdentity Identity,
	NetworkPacketDirection Direction,
	NetworkInterfaceIdentity Interface,
	ReadOnlyMemory<byte> Packet)
{
	public bool IsValid => FlowSerial.IsValid && Identity.HasBranch &&
		Direction != NetworkPacketDirection.Unspecified && Interface.Luid > 0 && !Packet.IsEmpty;
}

public readonly record struct NetworkPacketDataPlaneRequest(
	NetworkFlowSerial FlowSerial,
	NetworkExecutionIdentity Identity,
	uint IngressInterfaceIndex,
	ReadOnlyMemory<byte> Packet)
{
	public bool IsValid => FlowSerial.IsValid && Identity.HasBranch && IngressInterfaceIndex > 0 && !Packet.IsEmpty;
}

public readonly record struct NetworkPacketDataPlaneResult(
	NetworkFlowSerial FlowSerial,
	NetworkExecutionIdentity Identity,
	ProtocolOutcome Outcome,
	AccessCompliance Compliance,
	string ReasonCode)
{
	public bool IsValid => FlowSerial.IsValid && Identity.HasBranch &&
		Outcome != ProtocolOutcome.Unspecified && Compliance != AccessCompliance.Unspecified &&
		ReasonCode is not null;
}

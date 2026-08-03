namespace Iwesun.Runtime.Networks;

public enum ProofKind : byte
{
	Unspecified = 0,
	Observed = 1,
	ApiBinding = 2,
	PolicyEnforced = 3,
	AdapterReported = 4,
	Inferred = 5,
	Unavailable = 6,
}

public enum ProtocolOutcome : byte
{
	Unspecified = 0,
	Succeeded = 1,
	Rejected = 2,
	TimedOut = 3,
	Cancelled = 4,
	TransportFailed = 5,
	ProtocolFailed = 6,
}

public enum AccessCompliance : byte
{
	Unspecified = 0,
	Satisfied = 1,
	Violated = 2,
	EvidenceIncomplete = 3,
	NotApplicable = 4,
}

public enum NetworkExecutionTerminalState : byte
{
	Unspecified = 0,
	Succeeded = 1,
	Rejected = 2,
	Failed = 3,
	TimedOut = 4,
	Cancelled = 5,
	Superseded = 6,
}

public readonly record struct NetworkDimensionEvidence<T>(
	T Value,
	ProofKind Proof,
	AccessCompliance Compliance,
	string DifferenceReason = "")
	where T : struct
{
	public bool IsValid => Proof != ProofKind.Unspecified &&
		Compliance != AccessCompliance.Unspecified &&
		DifferenceReason is not null;
}

/// <summary>Immutable values selected for one concrete branch before execution.</summary>
public readonly record struct ResolvedAccessPlan(
	NetworkPathProvider PathProvider,
	IpAddressValue Destination,
	IpAddressValue Source,
	NetworkInterfaceIdentity Interface,
	IpAddressValue NextHop,
	ushort LocalPort,
	uint RouteCompartmentId,
	NetworkAccessStableKey StableKey,
	long RouteSnapshotVersion = 0,
	long InterfaceSnapshotVersion = 0)
{
	public bool IsValid => PathProvider != NetworkPathProvider.Unspecified && StableKey.IsValid;
}

/// <summary>Observed or enforced path evidence owned by one branch.</summary>
public readonly record struct ActualAccessEvidence(
	NetworkExecutionIdentity Identity,
	NetworkDimensionEvidence<NetworkPathProvider> PathProvider,
	NetworkDimensionEvidence<IpAddressValue> Destination,
	NetworkDimensionEvidence<IpAddressValue> Source,
	NetworkDimensionEvidence<NetworkInterfaceIdentity> Interface,
	NetworkDimensionEvidence<IpAddressValue> NextHop,
	NetworkDimensionEvidence<ushort> LocalPort,
	NetworkDimensionEvidence<uint> RouteCompartment,
	NetworkDimensionEvidence<NetworkIpPacketPolicy> IpPacketPolicy,
	NetworkDimensionEvidence<NetworkRouteAdapterIdentity> RouteAdapter)
{
	public bool IsValid => Identity.HasBranch &&
		PathProvider.IsValid &&
		Destination.IsValid && Source.IsValid && Interface.IsValid && NextHop.IsValid &&
		LocalPort.IsValid && RouteCompartment.IsValid && IpPacketPolicy.IsValid && RouteAdapter.IsValid;
}

public readonly record struct NetworkBranchTerminal(
	NetworkExecutionIdentity Identity,
	NetworkExecutionTerminalState State,
	ProtocolOutcome ProtocolOutcome,
	AccessCompliance AccessCompliance,
	NetworkAccessFailure Failure)
{
	public bool IsValid => Identity.HasBranch &&
		State != NetworkExecutionTerminalState.Unspecified &&
		ProtocolOutcome != ProtocolOutcome.Unspecified &&
		AccessCompliance != AccessCompliance.Unspecified;
}

public readonly record struct NetworkAttemptTerminal(
	NetworkExecutionIdentity Identity,
	NetworkExecutionTerminalState State,
	ProtocolOutcome ProtocolOutcome,
	AccessCompliance AccessCompliance,
	ushort BranchCount,
	NetworkAccessFailure Failure)
{
	public bool IsValid => Identity.HasAttempt && Identity.BranchId == Guid.Empty &&
		State != NetworkExecutionTerminalState.Unspecified && BranchCount > 0;
}

public readonly record struct NetworkRequestTerminal(
	NetworkExecutionIdentity Identity,
	NetworkExecutionTerminalState State,
	ProtocolOutcome ProtocolOutcome,
	AccessCompliance AccessCompliance,
	ushort AttemptCount,
	NetworkAccessFailure Failure)
{
	public bool IsValid => Identity.HasRequest && Identity.AttemptId == Guid.Empty &&
		State != NetworkExecutionTerminalState.Unspecified;
}

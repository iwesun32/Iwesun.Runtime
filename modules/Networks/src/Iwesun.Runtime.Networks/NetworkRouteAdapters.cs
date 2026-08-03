using System.Collections.Concurrent;

namespace Iwesun.Runtime.Networks;

[Flags]
public enum NetworkRouteAdapterTransportCapabilities : byte
{
	None = 0,
	ByteStream = 1 << 0,
	Datagram = 1 << 1,
}

public readonly record struct NetworkRouteAdapterCapabilities(
	string AdapterId,
	int CapabilityVersion,
	NetworkRouteAdapterTransportCapabilities Transports,
	bool ReportsClientLeg,
	bool ReportsEgressLeg)
{
	public bool IsValid => !string.IsNullOrWhiteSpace(AdapterId) &&
		CapabilityVersion > 0 && Transports != NetworkRouteAdapterTransportCapabilities.None;
}

public readonly record struct NetworkRouteLegEvidence(
	NetworkAccessLeg Leg,
	ActualAccessEvidence Evidence,
	string ProviderIdentity)
{
	public bool IsValid => Leg is NetworkAccessLeg.ClientLeg or NetworkAccessLeg.EgressLeg or NetworkAccessLeg.Direct &&
		Evidence.IsValid && ProviderIdentity is not null;

	public static NetworkRouteLegEvidence NotApplicable(
		NetworkExecutionIdentity identity,
		NetworkAccessLeg leg,
		NetworkPathProvider provider) => new(
		leg,
		NetworkRouteAdapterEvidence.CreateUnavailable(identity, provider),
		string.Empty);
}

public readonly record struct NetworkRouteAdapterConnectRequest(
	NetworkExecutionIdentity Identity,
	NetworkHttpProtocolIdentity Protocol,
	RequestedAccessPlan Requested,
	ResolvedAccessPlan Resolved)
{
	public bool IsValid => Identity.HasBranch && Protocol.IsValid &&
		Requested.PathProvider == NetworkPathProvider.RouteAdapter && Resolved.IsValid;
}

public readonly record struct NetworkRouteAdapterConnection(
	Stream Stream,
	NetworkRouteLegEvidence ClientLeg,
	NetworkRouteLegEvidence EgressLeg,
	AccessCompliance Compliance)
{
	public bool IsValid => Stream is not null && ClientLeg.IsValid && EgressLeg.IsValid &&
		Compliance != AccessCompliance.Unspecified;
}

public interface INetworkRouteAdapter
{
	NetworkRouteAdapterCapabilities Capabilities { get; }
	ValueTask<NetworkRouteAdapterConnection> ConnectAsync(
		NetworkRouteAdapterConnectRequest request,
		CancellationToken cancellationToken);
}

public sealed class NetworkRouteAdapterRegistry
{
	private readonly ConcurrentDictionary<string, INetworkRouteAdapter> _adapters =
		new(StringComparer.OrdinalIgnoreCase);

	public bool TryRegister(INetworkRouteAdapter adapter)
	{
		ArgumentNullException.ThrowIfNull(adapter);
		if (!adapter.Capabilities.IsValid) throw new ArgumentException("Adapter capabilities are invalid.", nameof(adapter));
		return _adapters.TryAdd(adapter.Capabilities.AdapterId.Trim(), adapter);
	}

	public bool TryResolve(
		in NetworkRouteAdapterIdentity identity,
		NetworkRouteAdapterTransportCapabilities requiredTransport,
		out INetworkRouteAdapter? adapter,
		out string reason)
	{
		adapter = null;
		if (identity.Kind != NetworkRouteAdapterIdentityKind.Exact ||
			!_adapters.TryGetValue(identity.AdapterId.Trim(), out var candidate) ||
			candidate.Capabilities.CapabilityVersion != identity.CapabilityVersion)
		{
			reason = NetworkAccessFailureCodes.RouteAdapterCapabilityVersionMismatch;
			return false;
		}
		if (!candidate.Capabilities.Transports.HasFlag(requiredTransport))
		{
			reason = NetworkAccessFailureCodes.RouteAdapterTransportUnsupported;
			return false;
		}
		adapter = candidate;
		reason = string.Empty;
		return true;
	}

	public bool Remove(string adapterId) =>
		!string.IsNullOrWhiteSpace(adapterId) && _adapters.TryRemove(adapterId.Trim(), out _);
}

internal static class NetworkRouteAdapterEvidence
{
	public static ActualAccessEvidence CreateUnavailable(
		NetworkExecutionIdentity identity,
		NetworkPathProvider provider) => new(
		identity,
		new(provider, ProofKind.Unavailable, AccessCompliance.NotApplicable),
		new(default, ProofKind.Unavailable, AccessCompliance.NotApplicable),
		new(default, ProofKind.Unavailable, AccessCompliance.NotApplicable),
		new(default, ProofKind.Unavailable, AccessCompliance.NotApplicable),
		new(default, ProofKind.Unavailable, AccessCompliance.NotApplicable),
		new((ushort)0, ProofKind.Unavailable, AccessCompliance.NotApplicable),
		new(0U, ProofKind.Unavailable, AccessCompliance.NotApplicable),
		new(NetworkIpPacketPolicy.NotApplicable(), ProofKind.Unavailable, AccessCompliance.NotApplicable),
		new(NetworkRouteAdapterIdentity.NotApplicable(), ProofKind.Unavailable, AccessCompliance.NotApplicable));
}

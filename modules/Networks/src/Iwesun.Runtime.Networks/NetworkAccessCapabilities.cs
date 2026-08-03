namespace Iwesun.Runtime.Networks;

[Flags]
public enum NetworkPathProviderCapabilities : byte
{
	None = 0,
	Automatic = 1 << 0,
	Direct = 1 << 1,
	RouteAdapter = 1 << 2,
	SystemProxy = 1 << 3,
}

[Flags]
public enum NetworkAccessDimensionCapabilities : ushort
{
	None = 0,
	Destination = 1 << 0,
	Source = 1 << 1,
	Interface = 1 << 2,
	NextHop = 1 << 3,
	LocalEndpoint = 1 << 4,
	RouteScope = 1 << 5,
	IpPacketPolicy = 1 << 6,
}

[Flags]
public enum NetworkSelectorCapabilities : byte
{
	None = 0,
	SystemSelected = 1 << 0,
	Exact = 1 << 1,
	AllowedSet = 1 << 2,
	PreferredSet = 1 << 3,
	ExcludedSet = 1 << 4,
	Derived = 1 << 5,
	NotApplicable = 1 << 6,
}

public readonly record struct NetworkAccessCapabilities(
	NetworkPathProviderCapabilities PathProviders,
	NetworkAccessDimensionCapabilities Dimensions,
	bool SupportsByteStream,
	bool SupportsDatagram,
	bool SupportsMultipleResponses,
	bool RequiresElevatedPolicy)
{
	public bool IsValid => PathProviders != NetworkPathProviderCapabilities.None;

	public bool Supports(NetworkPathProvider provider) => provider switch
	{
		NetworkPathProvider.Automatic => PathProviders.HasFlag(NetworkPathProviderCapabilities.Automatic),
		NetworkPathProvider.Direct => PathProviders.HasFlag(NetworkPathProviderCapabilities.Direct),
		NetworkPathProvider.RouteAdapter => PathProviders.HasFlag(NetworkPathProviderCapabilities.RouteAdapter),
		NetworkPathProvider.SystemProxy => PathProviders.HasFlag(NetworkPathProviderCapabilities.SystemProxy),
		_ => false,
	};
}

/// <summary>Endpoint-specific selector and dimension legality matrix.</summary>
public readonly record struct NetworkEndpointAccessProfile(
	NetworkAccessCapabilities Capabilities,
	NetworkAccessDimensionCapabilities ApplicableDimensions,
	NetworkSelectorCapabilities DestinationSelectors,
	NetworkSelectorCapabilities SourceSelectors,
	NetworkSelectorCapabilities InterfaceSelectors,
	NetworkSelectorCapabilities NextHopSelectors,
	bool SupportsLocalEndpointSelection,
	bool SupportsExactRouteScope,
	bool SupportsIpPacketPolicy)
{
	public bool IsValid => Capabilities.IsValid && DestinationSelectors != NetworkSelectorCapabilities.None;
}

public static class NetworkAccessLegality
{
	public static bool TryValidate(
		in RequestedAccessPlan plan,
		in NetworkEndpointAccessProfile profile,
		out string reason)
	{
		if (!plan.TryValidate(out reason)) return false;
		if (!profile.IsValid || !profile.Capabilities.Supports(plan.PathProvider))
		{
			reason = NetworkAccessFailureCodes.CapabilityUnsupported;
			return false;
		}

		if (!SupportsSelector(plan.Destination.Kind, profile.DestinationSelectors) ||
			!SupportsSelector(plan.Source.Kind, profile.SourceSelectors) ||
			!SupportsSelector(plan.Interface.Kind, profile.InterfaceSelectors) ||
			!SupportsSelector(plan.NextHop.Kind, profile.NextHopSelectors))
		{
			reason = NetworkAccessFailureCodes.CapabilityUnsupported;
			return false;
		}

		if (!DimensionMatches(plan.Destination.Kind, NetworkAccessDimensionCapabilities.Destination, profile) ||
			!DimensionMatches(plan.Source.Kind, NetworkAccessDimensionCapabilities.Source, profile) ||
			!DimensionMatches(plan.Interface.Kind, NetworkAccessDimensionCapabilities.Interface, profile) ||
			!DimensionMatches(plan.NextHop.Kind, NetworkAccessDimensionCapabilities.NextHop, profile))
		{
			reason = NetworkAccessFailureCodes.SelectorNotApplicableUnsupported;
			return false;
		}

		if (!profile.SupportsLocalEndpointSelection &&
			plan.LocalEndpoint.Kind != NetworkLocalEndpointSelectionKind.NotApplicable)
		{
			reason = NetworkAccessFailureCodes.CapabilityUnsupported;
			return false;
		}

		if (!profile.SupportsExactRouteScope &&
			plan.RouteScope.Kind == NetworkRouteScopeSelectionKind.ExactCompartment)
		{
			reason = NetworkAccessFailureCodes.CapabilityUnsupported;
			return false;
		}

		if (!profile.SupportsIpPacketPolicy &&
			plan.IpPacketPolicy.Kind != NetworkIpPacketPolicyKind.NotApplicable)
		{
			reason = NetworkAccessFailureCodes.CapabilityUnsupported;
			return false;
		}

		reason = string.Empty;
		return true;
	}

	private static bool DimensionMatches(
		NetworkSelectorKind kind,
		NetworkAccessDimensionCapabilities dimension,
		in NetworkEndpointAccessProfile profile)
	{
		var applicable = profile.ApplicableDimensions.HasFlag(dimension);
		return applicable ? kind != NetworkSelectorKind.NotApplicable : kind == NetworkSelectorKind.NotApplicable;
	}

	private static bool SupportsSelector(NetworkSelectorKind kind, NetworkSelectorCapabilities supported)
	{
		var capability = kind switch
		{
			NetworkSelectorKind.SystemSelected => NetworkSelectorCapabilities.SystemSelected,
			NetworkSelectorKind.Exact => NetworkSelectorCapabilities.Exact,
			NetworkSelectorKind.AllowedSet => NetworkSelectorCapabilities.AllowedSet,
			NetworkSelectorKind.PreferredSet => NetworkSelectorCapabilities.PreferredSet,
			NetworkSelectorKind.ExcludedSet => NetworkSelectorCapabilities.ExcludedSet,
			NetworkSelectorKind.Derived => NetworkSelectorCapabilities.Derived,
			NetworkSelectorKind.NotApplicable => NetworkSelectorCapabilities.NotApplicable,
			_ => NetworkSelectorCapabilities.None,
		};
		return capability != NetworkSelectorCapabilities.None && supported.HasFlag(capability);
	}
}

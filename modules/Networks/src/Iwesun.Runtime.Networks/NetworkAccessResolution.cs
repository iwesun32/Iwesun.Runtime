namespace Iwesun.Runtime.Networks;

public readonly record struct NetworkInterfaceSnapshot(
	NetworkInterfaceIdentity Identity,
	NetworkAddressSet UnicastAddresses,
	bool IsUp)
{
	public bool IsValid => Identity.IsValid;
}

/// <summary>An immutable point-in-time interface catalog.</summary>
public readonly struct NetworkInterfaceCatalog
{
	private readonly NetworkInterfaceSnapshot[]? _items;

	private NetworkInterfaceCatalog(long version, NetworkInterfaceSnapshot[] items)
	{
		Version = version;
		_items = items;
	}

	public long Version { get; }
	public int Count => _items?.Length ?? 0;
	public NetworkInterfaceSnapshot this[int index] => _items![index];

	public static bool TryCreate(
		long version,
		ReadOnlySpan<NetworkInterfaceSnapshot> items,
		out NetworkInterfaceCatalog catalog,
		out string reason)
	{
		if (version <= 0 || items.IsEmpty)
		{
			catalog = default;
			reason = NetworkAccessFailureCodes.InterfaceCatalogInvalid;
			return false;
		}

		var copy = items.ToArray();
		if (Array.Exists(copy, static item => !item.IsValid))
		{
			catalog = default;
			reason = NetworkAccessFailureCodes.InterfaceCatalogInvalid;
			return false;
		}

		catalog = new NetworkInterfaceCatalog(version, copy);
		reason = string.Empty;
		return true;
	}
}

public sealed class NetworkInterfaceSnapshotProvider
{
	private const int SnapshotHistoryLimit = 4096;
	private readonly object _gate = new();
	private NetworkInterfaceCatalog _current;
	private readonly Dictionary<long, NetworkInterfaceCatalog> _snapshots = [];
	private readonly Queue<long> _snapshotOrder = [];

	public NetworkInterfaceCatalog GetSnapshot()
	{
		lock (_gate) return _current;
	}

	public bool TryReplace(
		ReadOnlySpan<NetworkInterfaceSnapshot> interfaces,
		out NetworkInterfaceCatalog snapshot,
		out string reason)
	{
		lock (_gate)
		{
			var nextVersion = Math.Max(1, _current.Version + 1);
			if (!NetworkInterfaceCatalog.TryCreate(nextVersion, interfaces, out snapshot, out reason)) return false;
			_current = snapshot;
			_snapshots[snapshot.Version] = snapshot;
			_snapshotOrder.Enqueue(snapshot.Version);
			while (_snapshotOrder.Count > SnapshotHistoryLimit)
				_snapshots.Remove(_snapshotOrder.Dequeue());
			return true;
		}
	}

	public bool ContainsVersion(long version)
	{
		lock (_gate) return version > 0 && _snapshots.ContainsKey(version);
	}
}

public sealed class NetworkInterfaceIdentityResolver
{
	public bool TryResolve(
		in NetworkInterfaceIdentity reference,
		in NetworkInterfaceCatalog catalog,
		out NetworkInterfaceIdentity identity,
		out string reason)
	{
		if (!reference.IsValid || catalog.Count == 0)
		{
			identity = default;
			reason = NetworkAccessFailureCodes.InterfaceIdentityNotFound;
			return false;
		}

		identity = default;
		var matchCount = 0;
		for (var index = 0; index < catalog.Count; index++)
		{
			var candidate = catalog[index].Identity;
			if (!Matches(reference, candidate)) continue;
			identity = candidate;
			matchCount++;
		}

		if (matchCount == 1 && identity.HasStableIdentity)
		{
			reason = string.Empty;
			return true;
		}

		identity = default;
		reason = matchCount > 1
			? NetworkAccessFailureCodes.InterfaceIdentityAmbiguous
			: NetworkAccessFailureCodes.InterfaceIdentityNotFound;
		return false;
	}

	private static bool Matches(NetworkInterfaceIdentity reference, NetworkInterfaceIdentity candidate) =>
		(reference.InterfaceGuid == Guid.Empty || reference.InterfaceGuid == candidate.InterfaceGuid) &&
		(reference.Luid == 0 || reference.Luid == candidate.Luid) &&
		(string.IsNullOrWhiteSpace(reference.Alias) ||
		 StringComparer.OrdinalIgnoreCase.Equals(reference.Alias.Trim(), candidate.Alias?.Trim())) &&
		(reference.InterfaceIndex == 0 || reference.InterfaceIndex == candidate.InterfaceIndex);
}

public readonly record struct NetworkRouteSnapshot(
	long Version,
	IpAddressValue Destination,
	IpAddressValue Source,
	NetworkInterfaceIdentity Interface,
	IpAddressValue NextHop,
	uint CompartmentId,
	long ObservedAtUnixMs)
{
	public bool IsValid =>
		Version > 0 &&
		Destination.Family is 4 or 6 &&
		Source.Family == Destination.Family &&
		Interface.IsValid &&
		(NextHop.Family == Destination.Family || NextHop == default);
}

public sealed class NetworkRouteSnapshotProvider
{
	private const int SnapshotHistoryLimit = 4096;
	private readonly object _gate = new();
	private NetworkRouteSnapshot _current;
	private readonly Dictionary<long, NetworkRouteSnapshot> _snapshots = [];
	private readonly Queue<long> _snapshotOrder = [];

	public NetworkRouteSnapshot GetSnapshot()
	{
		lock (_gate) return _current;
	}

	public bool TryReplace(
		IpAddressValue destination,
		IpAddressValue source,
		NetworkInterfaceIdentity networkInterface,
		IpAddressValue nextHop,
		uint compartmentId,
		out NetworkRouteSnapshot snapshot,
		out string reason)
	{
		lock (_gate)
		{
			snapshot = new NetworkRouteSnapshot(
				Math.Max(1, _current.Version + 1),
				destination,
				source,
				networkInterface,
				nextHop,
				compartmentId,
				DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
			if (!snapshot.IsValid)
			{
				snapshot = default;
				reason = NetworkAccessFailureCodes.RouteSnapshotInvalid;
				return false;
			}

			_current = snapshot;
			_snapshots[snapshot.Version] = snapshot;
			_snapshotOrder.Enqueue(snapshot.Version);
			while (_snapshotOrder.Count > SnapshotHistoryLimit)
				_snapshots.Remove(_snapshotOrder.Dequeue());
			reason = string.Empty;
			return true;
		}
	}

	public bool IsCurrent(in ResolvedAccessPlan plan)
	{
		lock (_gate) return plan.RouteSnapshotVersion > 0 &&
			_snapshots.ContainsKey(plan.RouteSnapshotVersion);
	}
}

public sealed class NetworkAccessPlanNormalizer(NetworkInterfaceIdentityResolver interfaceResolver)
{
	public bool TryNormalize(
		in RequestedAccessPlan plan,
		in NetworkInterfaceCatalog interfaces,
		out RequestedAccessPlan normalized,
		out string reason)
	{
		if (!plan.TryValidate(out reason))
		{
			normalized = default;
			return false;
		}

		if (!TryNormalizeInterfaceSelection(plan.Interface, interfaces, out var networkInterface, out reason))
		{
			normalized = default;
			return false;
		}

		normalized = plan with { Interface = networkInterface };
		return true;
	}

	private bool TryNormalizeInterfaceSelection(
		NetworkInterfaceSelection selection,
		in NetworkInterfaceCatalog catalog,
		out NetworkInterfaceSelection normalized,
		out string reason)
	{
		if (selection.Kind == NetworkSelectorKind.Exact)
		{
			if (!interfaceResolver.TryResolve(selection.ExactValue, catalog, out var identity, out reason))
			{
				normalized = default;
				return false;
			}

			normalized = NetworkInterfaceSelection.Exact(identity);
			return true;
		}

		if (selection.Kind is NetworkSelectorKind.AllowedSet or NetworkSelectorKind.PreferredSet or NetworkSelectorKind.ExcludedSet)
		{
			var identities = new NetworkInterfaceIdentity[selection.Set.Count];
			for (var index = 0; index < identities.Length; index++)
			{
				if (!interfaceResolver.TryResolve(selection.Set[index], catalog, out identities[index], out reason))
				{
					normalized = default;
					return false;
				}
			}

			if (!NetworkInterfaceSet.TryCreate(identities, out var set, out reason))
			{
				normalized = default;
				return false;
			}

			normalized = selection.Kind switch
			{
				NetworkSelectorKind.AllowedSet => NetworkInterfaceSelection.Allowed(set),
				NetworkSelectorKind.PreferredSet => NetworkInterfaceSelection.Preferred(set),
				_ => NetworkInterfaceSelection.Excluded(set),
			};
			return true;
		}

		normalized = selection;
		reason = string.Empty;
		return true;
	}

}

public sealed class NetworkAccessConstraintResolver(
	NetworkAccessPlanNormalizer normalizer,
	NetworkInterfaceSnapshotProvider interfaceProvider,
	NetworkRouteSnapshotProvider routeProvider)
{
	internal bool TryCreateNormalizedStableKey(
		in RequestedAccessPlan requested,
		in NetworkAccessSecurityBoundary security,
		out NetworkAccessStableKey stableKey,
		out string reason)
	{
		var interfaces = interfaceProvider.GetSnapshot();
		reason = string.Empty;
		if (interfaces.Count == 0 ||
			!normalizer.TryNormalize(requested, interfaces, out var normalized, out reason))
		{
			stableKey = default;
			if (string.IsNullOrEmpty(reason)) reason = NetworkAccessFailureCodes.RouteSnapshotInvalid;
			return false;
		}

		return NetworkAccessStableKey.TryCreate(normalized, security, out stableKey, out reason);
	}

	public bool TryResolve(
		in RequestedAccessPlan requested,
		in NetworkAccessSecurityBoundary security,
		out ResolvedAccessPlan resolved,
		out string reason)
	{
		var interfaces = interfaceProvider.GetSnapshot();
		var route = routeProvider.GetSnapshot();
		return TryResolve(requested, security, interfaces, route, out resolved, out reason);
	}

	internal bool TryResolve(
		in RequestedAccessPlan requested,
		in NetworkAccessSecurityBoundary security,
		in NetworkInterfaceCatalog interfaces,
		in NetworkRouteSnapshot route,
		out ResolvedAccessPlan resolved,
		out string reason)
	{
		if (!route.IsValid || interfaces.Count == 0)
		{
			resolved = default;
			reason = NetworkAccessFailureCodes.RouteSnapshotInvalid;
			return false;
		}

		if (!normalizer.TryNormalize(requested, interfaces, out var plan, out reason) ||
			!TrySelectAddress(plan.Destination.Kind, plan.Destination.ExactValue, plan.Destination.Set, route.Destination, out var destination) ||
			!TrySelectAddress(plan.Source.Kind, plan.Source.ExactValue, plan.Source.Set, route.Source, out var source) ||
			!TrySelectInterface(plan.Interface, interfaces, route.Interface, out var networkInterface) ||
			!TrySelectNextHop(plan.NextHop, route.NextHop, out var nextHop) ||
			!TrySelectLocalPort(plan.LocalEndpoint, out var localPort) ||
			!TrySelectCompartment(plan.RouteScope, route.CompartmentId, out var compartmentId))
		{
			resolved = default;
			if (string.IsNullOrEmpty(reason)) reason = NetworkAccessFailureCodes.ConstraintUnresolved;
			return false;
		}

		if (!ValidateDerived(plan, route, destination, source, networkInterface, nextHop))
		{
			resolved = default;
			reason = NetworkAccessFailureCodes.SelectorDerivedUnresolved;
			return false;
		}

		if (plan.Interface.Kind == NetworkSelectorKind.Exact &&
			!InterfaceOwnsAddress(interfaces, networkInterface, source))
		{
			resolved = default;
			reason = NetworkAccessFailureCodes.SourceInterfaceConflict;
			return false;
		}

		if (!NetworkAccessStableKey.TryCreate(plan, security, out var stableKey, out reason))
		{
			resolved = default;
			return false;
		}

		resolved = new ResolvedAccessPlan(
			plan.PathProvider,
			destination,
			source,
			networkInterface,
			nextHop,
			localPort,
			compartmentId,
			stableKey,
			route.Version,
			interfaces.Version);
		reason = string.Empty;
		return true;
	}

	public bool IsCurrent(in ResolvedAccessPlan plan) =>
		(plan.PathProvider is NetworkPathProvider.RouteAdapter or NetworkPathProvider.SystemProxy &&
		 plan.RouteSnapshotVersion == 0 && plan.InterfaceSnapshotVersion == 0) ||
		(routeProvider.IsCurrent(plan) && interfaceProvider.ContainsVersion(plan.InterfaceSnapshotVersion));

	private static bool TrySelectAddress(
		NetworkSelectorKind kind,
		IpAddressValue exact,
		NetworkAddressSet set,
		IpAddressValue systemValue,
		out IpAddressValue selected)
	{
		selected = kind switch
		{
			NetworkSelectorKind.Exact => exact,
			NetworkSelectorKind.SystemSelected or NetworkSelectorKind.Derived => systemValue,
			NetworkSelectorKind.AllowedSet or NetworkSelectorKind.PreferredSet => Contains(set, systemValue) ? systemValue : default,
			NetworkSelectorKind.ExcludedSet => Contains(set, systemValue) ? default : systemValue,
			NetworkSelectorKind.NotApplicable => default,
			_ => default,
		};
		return kind == NetworkSelectorKind.NotApplicable || selected.Family is 4 or 6;
	}

	private static bool TrySelectInterface(
		NetworkInterfaceSelection selection,
		in NetworkInterfaceCatalog catalog,
		NetworkInterfaceIdentity systemValue,
		out NetworkInterfaceIdentity selected)
	{
		selected = selection.Kind switch
		{
			NetworkSelectorKind.Exact => selection.ExactValue,
			NetworkSelectorKind.SystemSelected or NetworkSelectorKind.Derived => systemValue,
			NetworkSelectorKind.AllowedSet or NetworkSelectorKind.PreferredSet =>
				Contains(selection.Set, systemValue) ? systemValue : default,
			NetworkSelectorKind.ExcludedSet => Contains(selection.Set, systemValue) ? default : systemValue,
			NetworkSelectorKind.NotApplicable => default,
			_ => default,
		};
		return selection.Kind == NetworkSelectorKind.NotApplicable || selected.IsValid;
	}

	private static bool TrySelectNextHop(
		NetworkNextHopSelection selection,
		IpAddressValue systemValue,
		out IpAddressValue selected)
	{
		if (selection.IsOnLink)
		{
			selected = default;
			return true;
		}
		if (selection.Kind is NetworkSelectorKind.SystemSelected or NetworkSelectorKind.Derived && systemValue == default)
		{
			selected = default;
			return true;
		}

		return TrySelectAddress(selection.Kind, selection.ExactValue, selection.Set, systemValue, out selected);
	}

	private static bool TrySelectLocalPort(NetworkLocalEndpointSelection selection, out ushort port)
	{
		port = selection.Kind switch
		{
			NetworkLocalEndpointSelectionKind.ExactPort => selection.FirstPort,
			NetworkLocalEndpointSelectionKind.AllowedRange => selection.FirstPort,
			NetworkLocalEndpointSelectionKind.SystemSelected or NetworkLocalEndpointSelectionKind.Ephemeral or
			NetworkLocalEndpointSelectionKind.NotApplicable => 0,
			_ => 0,
		};
		return selection.Kind != NetworkLocalEndpointSelectionKind.ReservedLease;
	}

	private static bool TrySelectCompartment(NetworkRouteScopeSelection selection, uint current, out uint compartment)
	{
		compartment = selection.Kind switch
		{
			NetworkRouteScopeSelectionKind.ExactCompartment => selection.CompartmentId,
			NetworkRouteScopeSelectionKind.CurrentScope => current,
			NetworkRouteScopeSelectionKind.NotApplicable => 0,
			_ => 0,
		};
		return selection.Kind != NetworkRouteScopeSelectionKind.Unspecified;
	}

	private static bool ValidateDerived(
		in RequestedAccessPlan plan,
		in NetworkRouteSnapshot route,
		IpAddressValue destination,
		IpAddressValue source,
		NetworkInterfaceIdentity networkInterface,
		IpAddressValue nextHop)
	{
		if (plan.Source.Kind == NetworkSelectorKind.Derived && source != route.Source) return false;
		if (plan.Interface.Kind == NetworkSelectorKind.Derived && networkInterface != route.Interface) return false;
		if (plan.NextHop.Kind == NetworkSelectorKind.Derived && nextHop != route.NextHop) return false;
		return destination.Family == source.Family &&
			(nextHop == default || nextHop.Family == destination.Family);
	}

	private static bool Contains(NetworkAddressSet set, IpAddressValue value)
	{
		for (var index = 0; index < set.Count; index++) if (set[index] == value) return true;
		return false;
	}

	private static bool Contains(NetworkInterfaceSet set, NetworkInterfaceIdentity value)
	{
		for (var index = 0; index < set.Count; index++) if (set[index] == value) return true;
		return false;
	}

	private static bool InterfaceOwnsAddress(
		in NetworkInterfaceCatalog catalog,
		NetworkInterfaceIdentity identity,
		IpAddressValue address)
	{
		for (var index = 0; index < catalog.Count; index++)
		{
			var candidate = catalog[index];
			if (candidate.Identity != identity) continue;
			for (var addressIndex = 0; addressIndex < candidate.UnicastAddresses.Count; addressIndex++)
				if (candidate.UnicastAddresses[addressIndex] == address) return true;
			return false;
		}
		return false;
	}

}

public enum NetworkExecutionBackend : byte
{
	Unspecified = 0,
	SystemSelection = 1,
	SocketBinding = 2,
	WindowsFilteringPlatform = 3,
	RouteAdapter = 4,
	SystemProxy = 5,
}

public readonly record struct NetworkAccessCapabilityResult(
	bool Supported,
	string ReasonCode,
	NetworkAccessDimensionCapabilities UnsupportedDimensions,
	NetworkExecutionBackend RequiredBackend,
	bool MissingPermission,
	long SnapshotVersion);

public sealed class NetworkAccessCapabilityProvider(NetworkEndpointAccessProfile profile)
{
	private long _version = 1;

	public NetworkAccessCapabilityResult Query(in RequestedAccessPlan plan)
	{
		var legal = NetworkAccessLegality.TryValidate(plan, profile, out var reason);
		var backend = SelectBackend(plan);
		var missingPermission = legal &&
			backend == NetworkExecutionBackend.WindowsFilteringPlatform &&
			profile.Capabilities.RequiresElevatedPolicy;
		var supported = legal && !missingPermission;
		if (missingPermission) reason = NetworkAccessFailureCodes.PlatformPermissionMissing;
		return new NetworkAccessCapabilityResult(
			supported,
			reason,
			supported ? NetworkAccessDimensionCapabilities.None : FindUnsupportedDimensions(plan),
			backend,
			missingPermission,
			Volatile.Read(ref _version));
	}

	public void Invalidate() => Interlocked.Increment(ref _version);

	private NetworkAccessDimensionCapabilities FindUnsupportedDimensions(in RequestedAccessPlan plan)
	{
		var unsupported = NetworkAccessDimensionCapabilities.None;
		if (IsUnsupported(plan.Destination.Kind, NetworkAccessDimensionCapabilities.Destination, profile.DestinationSelectors)) unsupported |= NetworkAccessDimensionCapabilities.Destination;
		if (IsUnsupported(plan.Source.Kind, NetworkAccessDimensionCapabilities.Source, profile.SourceSelectors)) unsupported |= NetworkAccessDimensionCapabilities.Source;
		if (IsUnsupported(plan.Interface.Kind, NetworkAccessDimensionCapabilities.Interface, profile.InterfaceSelectors)) unsupported |= NetworkAccessDimensionCapabilities.Interface;
		if (IsUnsupported(plan.NextHop.Kind, NetworkAccessDimensionCapabilities.NextHop, profile.NextHopSelectors)) unsupported |= NetworkAccessDimensionCapabilities.NextHop;
		if (!profile.SupportsLocalEndpointSelection && plan.LocalEndpoint.Kind != NetworkLocalEndpointSelectionKind.NotApplicable) unsupported |= NetworkAccessDimensionCapabilities.LocalEndpoint;
		if (!profile.SupportsExactRouteScope && plan.RouteScope.Kind == NetworkRouteScopeSelectionKind.ExactCompartment) unsupported |= NetworkAccessDimensionCapabilities.RouteScope;
		if (!profile.SupportsIpPacketPolicy && plan.IpPacketPolicy.Kind != NetworkIpPacketPolicyKind.NotApplicable) unsupported |= NetworkAccessDimensionCapabilities.IpPacketPolicy;
		return unsupported;
	}

	private bool IsUnsupported(
		NetworkSelectorKind kind,
		NetworkAccessDimensionCapabilities dimension,
		NetworkSelectorCapabilities supported)
	{
		var applicable = profile.ApplicableDimensions.HasFlag(dimension);
		if (kind == NetworkSelectorKind.NotApplicable) return applicable;
		if (!applicable) return true;
		var required = kind switch
		{
			NetworkSelectorKind.SystemSelected => NetworkSelectorCapabilities.SystemSelected,
			NetworkSelectorKind.Exact => NetworkSelectorCapabilities.Exact,
			NetworkSelectorKind.AllowedSet => NetworkSelectorCapabilities.AllowedSet,
			NetworkSelectorKind.PreferredSet => NetworkSelectorCapabilities.PreferredSet,
			NetworkSelectorKind.ExcludedSet => NetworkSelectorCapabilities.ExcludedSet,
			NetworkSelectorKind.Derived => NetworkSelectorCapabilities.Derived,
			_ => NetworkSelectorCapabilities.None,
		};
		return required == NetworkSelectorCapabilities.None || !supported.HasFlag(required);
	}

	private static NetworkExecutionBackend SelectBackend(in RequestedAccessPlan plan) => plan.PathProvider switch
	{
		NetworkPathProvider.Automatic => NetworkExecutionBackend.SystemSelection,
		NetworkPathProvider.RouteAdapter => NetworkExecutionBackend.RouteAdapter,
		NetworkPathProvider.SystemProxy => NetworkExecutionBackend.SystemProxy,
		NetworkPathProvider.Direct when plan.NextHop.Kind == NetworkSelectorKind.Exact && !plan.NextHop.IsOnLink =>
			NetworkExecutionBackend.WindowsFilteringPlatform,
		NetworkPathProvider.Direct => NetworkExecutionBackend.SocketBinding,
		_ => NetworkExecutionBackend.Unspecified,
	};
}

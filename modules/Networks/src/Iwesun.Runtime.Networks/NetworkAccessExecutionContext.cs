namespace Iwesun.Runtime.Networks;

/// <summary>Owns the current platform snapshots, resolver, and socket executor for protocol endpoints.</summary>
public sealed class NetworkAccessExecutionContext
{
	private readonly WindowsNetworkRouteSnapshotProvider? _windowsSnapshots;

	public NetworkAccessExecutionContext(
		NetworkInterfaceSnapshotProvider interfaces,
		NetworkRouteSnapshotProvider routes,
		bool refreshWindowsSnapshots)
		: this(interfaces, routes, refreshWindowsSnapshots, null)
	{
	}

	internal NetworkAccessExecutionContext(
		NetworkInterfaceSnapshotProvider interfaces,
		NetworkRouteSnapshotProvider routes,
		bool refreshWindowsSnapshots,
		INetworkWfpConnectionPolicyBackend? wfpBackend)
	{
		Interfaces = interfaces;
		Routes = routes;
		Resolver = new NetworkAccessConstraintResolver(
			new NetworkAccessPlanNormalizer(new NetworkInterfaceIdentityResolver()),
			interfaces,
			routes);
		SocketExecutor = wfpBackend is null
			? new NetworkSocketExecutor(Resolver)
			: new NetworkSocketExecutor(Resolver, wfpBackend);
		if (refreshWindowsSnapshots)
			_windowsSnapshots = new WindowsNetworkRouteSnapshotProvider(interfaces, routes);
	}

	public NetworkInterfaceSnapshotProvider Interfaces { get; }
	public NetworkRouteSnapshotProvider Routes { get; }
	public NetworkAccessConstraintResolver Resolver { get; }
	public NetworkSocketExecutor SocketExecutor { get; }

	public static NetworkAccessExecutionContext CreatePlatformDefault() => new(
		new NetworkInterfaceSnapshotProvider(),
		new NetworkRouteSnapshotProvider(),
		refreshWindowsSnapshots: true);

	public bool TryResolve(
		in RequestedAccessPlan requested,
		in NetworkAccessSecurityBoundary securityBoundary,
		out ResolvedAccessPlan resolved,
		out NetworkAccessFailure failure)
	{
		if (requested.PathProvider is NetworkPathProvider.RouteAdapter or NetworkPathProvider.SystemProxy)
			return TryResolveDelegated(requested, securityBoundary, out resolved, out failure);

		if (_windowsSnapshots is not null)
		{
			if (!_windowsSnapshots.TryRefresh(
				requested,
				out var interfaces,
				out var route,
				out var platformError,
				out var refreshReason))
			{
				resolved = default;
				failure = new NetworkAccessFailure(refreshReason, platformError);
				return false;
			}
			if (!Resolver.TryResolve(requested, securityBoundary, interfaces, route, out resolved, out var localReason))
			{
				failure = new NetworkAccessFailure(localReason, default);
				return false;
			}

			failure = default;
			return true;
		}
		if (!Resolver.TryResolve(requested, securityBoundary, out resolved, out var reason))
		{
			failure = new NetworkAccessFailure(reason, default);
			return false;
		}

		failure = default;
		return true;
	}

	private static bool TryResolveDelegated(
		in RequestedAccessPlan requested,
		in NetworkAccessSecurityBoundary securityBoundary,
		out ResolvedAccessPlan resolved,
		out NetworkAccessFailure failure)
	{
		if (!requested.TryValidate(out var reason) ||
			requested.Destination.Kind != NetworkSelectorKind.Exact ||
			!NetworkAccessStableKey.TryCreate(requested, securityBoundary, out var stableKey, out reason))
		{
			resolved = default;
			failure = new NetworkAccessFailure(
				string.IsNullOrEmpty(reason) ? NetworkAccessFailureCodes.ConstraintUnresolved : reason,
				default);
			return false;
		}

		// Proxy and adapter selectors describe a delegated path. A local GetBestRoute2 query to the
		// final destination would describe a different direct path and must not be presented as its
		// resolved source, interface, or next hop. Exact values are preserved for the adapter to enforce;
		// delegated selections remain unset until the adapter returns per-leg evidence.
		resolved = new ResolvedAccessPlan(
			requested.PathProvider,
			requested.Destination.ExactValue,
			requested.Source.Kind == NetworkSelectorKind.Exact ? requested.Source.ExactValue : default,
			requested.Interface.Kind == NetworkSelectorKind.Exact ? requested.Interface.ExactValue : default,
			requested.NextHop.Kind == NetworkSelectorKind.Exact && !requested.NextHop.IsOnLink
				? requested.NextHop.ExactValue : default,
			requested.LocalEndpoint.Kind == NetworkLocalEndpointSelectionKind.ExactPort
				? requested.LocalEndpoint.FirstPort : (ushort)0,
			requested.RouteScope.Kind == NetworkRouteScopeSelectionKind.ExactCompartment
				? requested.RouteScope.CompartmentId : 0,
			stableKey);
		failure = default;
		return true;
	}
}

using Iwesun.Runtime.Networks;
using Xunit;

namespace Iwesun.Runtime.Networks.Tests;

public sealed class NetworkAccessResolutionTests
{
	private static readonly IpAddressValue Destination = IpAddressValue.Parse("192.0.2.10");
	private static readonly IpAddressValue Source = IpAddressValue.Parse("192.0.2.20");
	private static readonly IpAddressValue Gateway = IpAddressValue.Parse("192.0.2.1");
	private static readonly NetworkInterfaceIdentity StableInterface = new(
		Guid.Parse("52f8f83a-f20d-4e87-bcdd-4feea1123467"),
		42,
		"Ethernet 1",
		7);

	[Fact]
	public void InterfaceIndexIsNormalizedToStableIdentity()
	{
		var environment = CreateEnvironment();
		var plan = CreatePlan(
			NetworkSourceSelection.Derived(NetworkSelectionDimension.Interface),
			NetworkInterfaceSelection.Exact(new(Guid.Empty, 0, string.Empty, 7)),
			NetworkNextHopSelection.SystemSelected());

		Assert.True(environment.Resolver.TryResolve(plan, NetworkAccessSecurityBoundary.NotApplicable(), out var resolved, out var reason), reason);
		Assert.Equal(StableInterface, resolved.Interface);
		Assert.Equal(Source, resolved.Source);
		Assert.Equal(Gateway, resolved.NextHop);
		Assert.True(resolved.StableKey.IsValid);
	}

	[Fact]
	public void GatewayOnlyConstraintDerivesSourceAndInterface()
	{
		var environment = CreateEnvironment();
		var plan = CreatePlan(
			NetworkSourceSelection.Derived(NetworkSelectionDimension.NextHop),
			NetworkInterfaceSelection.Derived(NetworkSelectionDimension.NextHop),
			NetworkNextHopSelection.Exact(Gateway));

		Assert.True(environment.Resolver.TryResolve(plan, NetworkAccessSecurityBoundary.NotApplicable(), out var resolved, out var reason), reason);
		Assert.Equal(Gateway, resolved.NextHop);
		Assert.Equal(Source, resolved.Source);
		Assert.Equal(StableInterface, resolved.Interface);
	}

	[Fact]
	public void SourceOnlyConstraintDerivesInterface()
	{
		var environment = CreateEnvironment();
		var plan = CreatePlan(
			NetworkSourceSelection.Exact(Source),
			NetworkInterfaceSelection.Derived(NetworkSelectionDimension.Source),
			NetworkNextHopSelection.SystemSelected());

		Assert.True(environment.Resolver.TryResolve(plan, NetworkAccessSecurityBoundary.NotApplicable(), out var resolved, out var reason), reason);
		Assert.Equal(Source, resolved.Source);
		Assert.Equal(StableInterface, resolved.Interface);
	}

	[Fact]
	public void CompleteExactPathProducesUniqueResolution()
	{
		var environment = CreateEnvironment();
		var plan = new RequestedAccessPlan(
			NetworkPathProvider.Direct,
			NetworkDestinationSelection.Exact(Destination),
			NetworkSourceSelection.Exact(Source),
			NetworkInterfaceSelection.Exact(StableInterface),
			NetworkNextHopSelection.Exact(Gateway),
			NetworkLocalEndpointSelection.Exact(53000),
			NetworkRouteScopeSelection.Exact(3),
			NetworkIpPacketPolicy.SystemDefault(),
			NetworkRouteAdapterIdentity.NotApplicable());

		Assert.True(environment.Resolver.TryResolve(plan, NetworkAccessSecurityBoundary.NotApplicable(), out var resolved, out var reason), reason);
		Assert.Equal(Destination, resolved.Destination);
		Assert.Equal(Source, resolved.Source);
		Assert.Equal(Gateway, resolved.NextHop);
		Assert.Equal((ushort)53000, resolved.LocalPort);
		Assert.Equal((uint)3, resolved.RouteCompartmentId);
	}

	[Fact]
	public void AmbiguousInterfaceAliasIsRejected()
	{
		var interfaces = new NetworkInterfaceSnapshotProvider();
		Assert.True(interfaces.TryReplace([
			new NetworkInterfaceSnapshot(StableInterface, default, true),
			new NetworkInterfaceSnapshot(new(Guid.NewGuid(), 99, "Ethernet 1", 9), default, true),
		], out _, out _));
		var resolver = new NetworkInterfaceIdentityResolver();

		Assert.False(resolver.TryResolve(new(Guid.Empty, 0, "Ethernet 1"), interfaces.GetSnapshot(), out _, out var reason));
		Assert.Equal(NetworkAccessFailureCodes.InterfaceIdentityAmbiguous, reason);
	}

	[Fact]
	public void PureIpv6AddressUsesExactInterfaceAsSeparateScope()
	{
		var v6Interface = StableInterface with { InterfaceIndex = 7 };
		var interfaces = new NetworkInterfaceSnapshotProvider();
		Assert.True(interfaces.TryReplace([new NetworkInterfaceSnapshot(v6Interface, default, true)], out var catalog, out _));
		var plan = CreatePlan(
			NetworkSourceSelection.SystemSelected(),
			NetworkInterfaceSelection.Exact(v6Interface),
			NetworkNextHopSelection.SystemSelected()) with
		{
			Destination = NetworkDestinationSelection.Exact(IpAddressValue.FromIPv6(0xfe80_0000_0000_0000, 1)),
		};
		var normalizer = new NetworkAccessPlanNormalizer(new NetworkInterfaceIdentityResolver());

		Assert.True(normalizer.TryNormalize(plan, catalog, out var normalized, out var reason), reason);
		Assert.Equal((uint)7, normalized.Interface.ExactValue.InterfaceIndex);
		Assert.Equal(IpAddressValue.Parse("fe80::1"), normalized.Destination.ExactValue);
	}

	[Fact]
	public void ScopedIpv6SystemAddressIsRejectedAtPureValueBoundary()
	{
		Assert.Throws<ArgumentException>(() =>
			IpAddressValue.FromIPAddress(System.Net.IPAddress.Parse("fe80::1%7")));
		Assert.Throws<FormatException>(() => IpAddressValue.Parse("fe80::1%7"));
	}

	[Fact]
	public void ExactInterfaceRejectsSourceOwnedByAnotherInterface()
	{
		var environment = CreateEnvironment();
		var plan = CreatePlan(
			NetworkSourceSelection.Exact(IpAddressValue.Parse("192.0.2.21")),
			NetworkInterfaceSelection.Exact(StableInterface),
			NetworkNextHopSelection.SystemSelected());

		Assert.False(environment.Resolver.TryResolve(
			plan, NetworkAccessSecurityBoundary.NotApplicable(), out _, out var reason));
		Assert.Equal(NetworkAccessFailureCodes.SourceInterfaceConflict, reason);
	}

	[Fact]
	public void InvalidDerivedDependencyIsRejectedBeforeResolution()
	{
		var plan = CreatePlan(
			NetworkSourceSelection.Derived(NetworkSelectionDimension.RouteScope),
			NetworkInterfaceSelection.SystemSelected(),
			NetworkNextHopSelection.SystemSelected());

		Assert.False(plan.TryValidate(out var reason));
		Assert.Equal(NetworkAccessFailureCodes.SelectorDerivedSourceInvalid, reason);
	}

	[Fact]
	public void AllowedSetWithoutTheResolvedRouteCandidateIsRejected()
	{
		var environment = CreateEnvironment();
		Assert.True(NetworkAddressSet.TryCreate([IpAddressValue.Parse("192.0.2.99")], out var allowed, out _));
		var plan = CreatePlan(
			NetworkSourceSelection.SystemSelected(),
			NetworkInterfaceSelection.SystemSelected(),
			NetworkNextHopSelection.SystemSelected()) with
		{
			Destination = NetworkDestinationSelection.Allowed(allowed),
		};

		Assert.False(environment.Resolver.TryResolve(plan, NetworkAccessSecurityBoundary.NotApplicable(), out _, out var reason));
		Assert.Equal(NetworkAccessFailureCodes.ConstraintUnresolved, reason);
	}

	[Fact]
	public void ResolvedPlanRemainsValidWhileItsImmutableSnapshotsAreRetained()
	{
		var environment = CreateEnvironment();
		var plan = CreatePlan(
			NetworkSourceSelection.SystemSelected(),
			NetworkInterfaceSelection.SystemSelected(),
			NetworkNextHopSelection.SystemSelected());
		Assert.True(environment.Resolver.TryResolve(plan, NetworkAccessSecurityBoundary.NotApplicable(), out var resolved, out _));
		Assert.True(environment.Resolver.IsCurrent(resolved));

		Assert.True(environment.Routes.TryReplace(Destination, Source, StableInterface, Gateway, 1, out _, out _));
		Assert.True(environment.Resolver.IsCurrent(resolved));
		Assert.True(environment.Resolver.TryResolve(plan, NetworkAccessSecurityBoundary.NotApplicable(), out var refreshed, out _));
		Assert.NotEqual(resolved.RouteSnapshotVersion, refreshed.RouteSnapshotVersion);

		Assert.True(environment.Interfaces.TryReplace([new NetworkInterfaceSnapshot(StableInterface, default, true)], out _, out _));
		Assert.True(environment.Resolver.IsCurrent(refreshed));

		for (var index = 0; index < 4096; index++)
			Assert.True(environment.Routes.TryReplace(Destination, Source, StableInterface, Gateway, 1, out _, out _));
		Assert.False(environment.Resolver.IsCurrent(resolved));
	}

	[Fact]
	public void CapabilityQueryExplainsWfpPermissionRequirement()
	{
		var allSelectors = NetworkSelectorCapabilities.SystemSelected |
			NetworkSelectorCapabilities.Exact |
			NetworkSelectorCapabilities.AllowedSet |
			NetworkSelectorCapabilities.PreferredSet |
			NetworkSelectorCapabilities.ExcludedSet |
			NetworkSelectorCapabilities.Derived;
		var dimensions = NetworkAccessDimensionCapabilities.Destination |
			NetworkAccessDimensionCapabilities.Source |
			NetworkAccessDimensionCapabilities.Interface |
			NetworkAccessDimensionCapabilities.NextHop |
			NetworkAccessDimensionCapabilities.LocalEndpoint |
			NetworkAccessDimensionCapabilities.RouteScope |
			NetworkAccessDimensionCapabilities.IpPacketPolicy;
		var provider = new NetworkAccessCapabilityProvider(new NetworkEndpointAccessProfile(
			new NetworkAccessCapabilities(NetworkPathProviderCapabilities.Direct, dimensions, true, true, false, true),
			dimensions,
			allSelectors, allSelectors, allSelectors, allSelectors,
			true, true, true));
		var plan = CreatePlan(
			NetworkSourceSelection.SystemSelected(),
			NetworkInterfaceSelection.SystemSelected(),
			NetworkNextHopSelection.Exact(Gateway));

		var result = provider.Query(plan);
		Assert.False(result.Supported);
		Assert.True(result.MissingPermission);
		Assert.Equal(NetworkExecutionBackend.WindowsFilteringPlatform, result.RequiredBackend);
		Assert.Equal(NetworkAccessFailureCodes.PlatformPermissionMissing, result.ReasonCode);
	}

	[Theory]
	[InlineData(NetworkPathProvider.RouteAdapter)]
	[InlineData(NetworkPathProvider.SystemProxy)]
	public void DelegatedProviderDoesNotRequireOrFabricateLocalRouteSnapshot(NetworkPathProvider provider)
	{
		var context = new NetworkAccessExecutionContext(
			new NetworkInterfaceSnapshotProvider(),
			new NetworkRouteSnapshotProvider(),
			refreshWindowsSnapshots: false);
		var plan = new RequestedAccessPlan(
			provider,
			NetworkDestinationSelection.Exact(Destination),
			NetworkSourceSelection.SystemSelected(),
			NetworkInterfaceSelection.SystemSelected(),
			NetworkNextHopSelection.SystemSelected(),
			NetworkLocalEndpointSelection.Ephemeral(),
			NetworkRouteScopeSelection.Current(),
			NetworkIpPacketPolicy.SystemDefault(),
			provider == NetworkPathProvider.RouteAdapter
				? NetworkRouteAdapterIdentity.Exact("test-adapter", 1)
				: NetworkRouteAdapterIdentity.NotApplicable());

		Assert.True(context.TryResolve(
			plan, NetworkAccessSecurityBoundary.NotApplicable(), out var resolved, out var failure),
			failure.ReasonCode);
		Assert.Equal(Destination, resolved.Destination);
		Assert.Equal(default, resolved.Source);
		Assert.Equal(default, resolved.Interface);
		Assert.Equal(default, resolved.NextHop);
		Assert.Equal(0, resolved.RouteSnapshotVersion);
		Assert.Equal(0, resolved.InterfaceSnapshotVersion);
		Assert.True(context.Resolver.IsCurrent(resolved));
	}

	private static RequestedAccessPlan CreatePlan(
		NetworkSourceSelection source,
		NetworkInterfaceSelection networkInterface,
		NetworkNextHopSelection nextHop) => new(
		NetworkPathProvider.Direct,
		NetworkDestinationSelection.SystemSelected(),
		source,
		networkInterface,
		nextHop,
		NetworkLocalEndpointSelection.Ephemeral(),
		NetworkRouteScopeSelection.Current(),
		NetworkIpPacketPolicy.SystemDefault(),
		NetworkRouteAdapterIdentity.NotApplicable());

	private static TestEnvironment CreateEnvironment()
	{
		Assert.True(NetworkAddressSet.TryCreate([Source], out var addresses, out _));
		var interfaces = new NetworkInterfaceSnapshotProvider();
		Assert.True(interfaces.TryReplace([new NetworkInterfaceSnapshot(StableInterface, addresses, true)], out _, out _));
		var routes = new NetworkRouteSnapshotProvider();
		Assert.True(routes.TryReplace(Destination, Source, StableInterface, Gateway, 1, out _, out _));
		var normalizer = new NetworkAccessPlanNormalizer(new NetworkInterfaceIdentityResolver());
		return new TestEnvironment(interfaces, routes, new NetworkAccessConstraintResolver(normalizer, interfaces, routes));
	}

	private readonly record struct TestEnvironment(
		NetworkInterfaceSnapshotProvider Interfaces,
		NetworkRouteSnapshotProvider Routes,
		NetworkAccessConstraintResolver Resolver);
}

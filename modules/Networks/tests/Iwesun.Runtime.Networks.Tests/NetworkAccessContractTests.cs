using Iwesun.Runtime.Networks;
using Xunit;

namespace Iwesun.Runtime.Networks.Tests;

public sealed class NetworkAccessContractTests
{
	[Fact]
	public void PublicApiContainsOnlyTheNetworks3RequestReplyStandard()
	{
		var publicTypes = typeof(RequestedAccessPlan).Assembly.GetExportedTypes();
		var removedNames = new HashSet<string>(StringComparer.Ordinal)
		{
			"NetworkRouteDirective",
			"NetworkRouteKind",
			"NetworkRouteFlags",
			"PrecisionRouteHttpMessageHandler",
			"RouteBoundHttpMessageHandler",
			"BinaryIpAddress",
		};

		Assert.DoesNotContain(publicTypes, type =>
			type.Name.Contains("V2", StringComparison.Ordinal) ||
			type.Name.Contains("V3", StringComparison.Ordinal));
		Assert.DoesNotContain(publicTypes, type => removedNames.Contains(type.Name));
		Assert.Contains(publicTypes, type => type.Name == "INetworkRouteAdapter");
		Assert.Contains(publicTypes, type => type.Name == "NetworkRouteAdapterRegistry");
		Assert.Contains(publicTypes, type => type.Name == "HttpConnectNetworkRouteAdapter");
		Assert.Contains(publicTypes, type => type.Name == "IpAddressValue");
		Assert.Contains(publicTypes, type => type.Name == "MacAddressValue");
		Assert.Contains(publicTypes, type => type.Name == "NetworkHttpGetEndpoint`1");
		Assert.Contains(publicTypes, type => type.Name == "NetworkDohEndpoint`1");
		Assert.Contains(publicTypes, type => type.Name == "NetworkTcpConnectEndpoint`1");
		Assert.Contains(publicTypes, type => type.Name == "NetworkUdpDatagramEndpoint`1");
		Assert.Contains(publicTypes, type => type.Name == "NetworkPingEndpoint`1");
		Assert.Contains(publicTypes, type => type.Name == "NetworkDnsReverseLookupEndpoint`1");
		Assert.Contains(publicTypes, type => type.Name == "NetworkNetBiosNameEndpoint`1");
	}

	[Fact]
	public void DefaultContractsAreInvalid()
	{
		Assert.False(default(NetworkExecutionIdentity).IsValid);
		Assert.False(default(NetworkAddressSet).IsValid);
		Assert.False(default(RequestedAccessPlan).TryValidate(out _));
		Assert.False(default(NetworkAccessStableKey).IsValid);
		Assert.Equal(0, (byte)NetworkPathProvider.Unspecified);
	}

	[Fact]
	public void ExecutionIdentityPreservesCompleteParentChain()
	{
		var requestId = Guid.NewGuid();
		var attemptId = Guid.NewGuid();
		var branchId = Guid.NewGuid();
		var responseId = Guid.NewGuid();

		var identity = NetworkExecutionIdentity.ForRequest(requestId)
			.StartAttempt(attemptId)
			.StartBranch(branchId)
			.CreateResponse(responseId);

		Assert.True(identity.IsValid);
		Assert.True(identity.HasResponse);
		Assert.Equal((requestId, attemptId, branchId, responseId),
			(identity.RequestId, identity.AttemptId, identity.BranchId, identity.ResponseId));
	}

	[Fact]
	public void EmptyAddressSetIsRejected()
	{
		Assert.False(NetworkAddressSet.TryCreate([], out var set, out var reason));
		Assert.False(set.IsValid);
		Assert.Equal(NetworkAccessFailureCodes.SelectorSetEmpty, reason);
	}

	[Fact]
	public void AddressSetIsDeDuplicatedAndStableOrdered()
	{
		var first = IpAddressValue.Parse("192.0.2.1");
		var second = IpAddressValue.Parse("192.0.2.2");

		Assert.True(NetworkAddressSet.TryCreate([second, first, second], out var set, out var reason), reason);
		Assert.Equal(2, set.Count);
		Assert.Equal(first, set[0]);
		Assert.Equal(second, set[1]);
	}

	[Fact]
	public void AddressFamilyConflictIsRejected()
	{
		var plan = CreateDirectPlan(
			NetworkDestinationSelection.Exact(IpAddressValue.Parse("192.0.2.1")),
			NetworkSourceSelection.Exact(IpAddressValue.Parse("2001:db8::1")));

		Assert.False(plan.TryValidate(out var reason));
		Assert.Equal(NetworkAccessFailureCodes.SelectorAddressFamilyMismatch, reason);
	}

	[Fact]
	public void DerivedCycleIsRejected()
	{
		var plan = CreateDirectPlan(
			NetworkDestinationSelection.Exact(IpAddressValue.Parse("192.0.2.1")),
			NetworkSourceSelection.Derived(NetworkSelectionDimension.Interface)) with
		{
			Interface = NetworkInterfaceSelection.Derived(NetworkSelectionDimension.Source),
		};

		Assert.False(plan.TryValidate(out var reason));
		Assert.Equal(NetworkAccessFailureCodes.SelectorDerivedCycle, reason);
	}

	[Fact]
	public void EquivalentNormalizedSemanticsProduceSameStableKey()
	{
		var first = IpAddressValue.Parse("2001:db8::1");
		var second = IpAddressValue.Parse("2001:db8::2");
		Assert.True(NetworkAddressSet.TryCreate([first, second], out var ordered, out _));
		Assert.True(NetworkAddressSet.TryCreate([second, first, second], out var reordered, out _));
		var plan1 = CreateDirectPlan(
			NetworkDestinationSelection.Preferred(ordered),
			NetworkSourceSelection.SystemSelected());
		var plan2 = CreateDirectPlan(
			NetworkDestinationSelection.Preferred(reordered),
			NetworkSourceSelection.SystemSelected());
		var security1 = new NetworkAccessSecurityBoundary(NetworkAccessLeg.Direct, "Example.COM", "EXAMPLE.com", "example.com", "DNS-A");
		var security2 = new NetworkAccessSecurityBoundary(NetworkAccessLeg.Direct, "example.com", "example.com", "EXAMPLE.COM", "dns-a");

		Assert.True(NetworkAccessStableKey.TryCreate(plan1, security1, out var key1, out var reason1), reason1);
		Assert.True(NetworkAccessStableKey.TryCreate(plan2, security2, out var key2, out var reason2), reason2);
		Assert.Equal(key1, key2);
	}

	[Fact]
	public void DifferentPathOrSecurityBoundaryProducesDifferentStableKey()
	{
		var destination = NetworkDestinationSelection.Exact(IpAddressValue.Parse("192.0.2.1"));
		var direct = CreateDirectPlan(destination, NetworkSourceSelection.SystemSelected());
		var automatic = CreateAutomaticPlan(destination);
		var security = new NetworkAccessSecurityBoundary(NetworkAccessLeg.Direct, "example.com", "example.com", "example.com", "dns-a");
		var otherSecurity = security with { ServerNameIndication = "other.example.com" };

		Assert.True(NetworkAccessStableKey.TryCreate(direct, security, out var directKey, out _));
		Assert.True(NetworkAccessStableKey.TryCreate(automatic, security, out var automaticKey, out _));
		Assert.True(NetworkAccessStableKey.TryCreate(direct, otherSecurity, out var otherSecurityKey, out _));
		Assert.NotEqual(directKey, automaticKey);
		Assert.NotEqual(directKey, otherSecurityKey);
	}

	[Fact]
	public void NotApplicableRequiresEndpointMatrixAuthorization()
	{
		var plan = CreateDirectPlan(
			NetworkDestinationSelection.Exact(IpAddressValue.Parse("192.0.2.1")),
			NetworkSourceSelection.SystemSelected()) with
		{
			NextHop = NetworkNextHopSelection.NotApplicable(),
		};
		var allCommonSelectors =
			NetworkSelectorCapabilities.SystemSelected |
			NetworkSelectorCapabilities.Exact |
			NetworkSelectorCapabilities.AllowedSet |
			NetworkSelectorCapabilities.PreferredSet |
			NetworkSelectorCapabilities.ExcludedSet |
			NetworkSelectorCapabilities.Derived |
			NetworkSelectorCapabilities.NotApplicable;
		var profile = new NetworkEndpointAccessProfile(
			new NetworkAccessCapabilities(
				NetworkPathProviderCapabilities.Direct,
				NetworkAccessDimensionCapabilities.Destination |
				NetworkAccessDimensionCapabilities.Source |
				NetworkAccessDimensionCapabilities.Interface |
				NetworkAccessDimensionCapabilities.NextHop,
				true, true, false, false),
			NetworkAccessDimensionCapabilities.Destination |
			NetworkAccessDimensionCapabilities.Source |
			NetworkAccessDimensionCapabilities.Interface |
			NetworkAccessDimensionCapabilities.NextHop,
			allCommonSelectors,
			allCommonSelectors,
			allCommonSelectors,
			allCommonSelectors,
			true,
			false,
			true);

		Assert.False(NetworkAccessLegality.TryValidate(plan, profile, out var reason));
		Assert.Equal(NetworkAccessFailureCodes.SelectorNotApplicableUnsupported, reason);
	}

	private static RequestedAccessPlan CreateDirectPlan(
		NetworkDestinationSelection destination,
		NetworkSourceSelection source) => new(
		NetworkPathProvider.Direct,
		destination,
		source,
		NetworkInterfaceSelection.SystemSelected(),
		NetworkNextHopSelection.SystemSelected(),
		NetworkLocalEndpointSelection.Ephemeral(),
		NetworkRouteScopeSelection.Current(),
		NetworkIpPacketPolicy.SystemDefault(),
		NetworkRouteAdapterIdentity.NotApplicable());

	private static RequestedAccessPlan CreateAutomaticPlan(NetworkDestinationSelection destination) => new(
		NetworkPathProvider.Automatic,
		destination,
		NetworkSourceSelection.SystemSelected(),
		NetworkInterfaceSelection.SystemSelected(),
		NetworkNextHopSelection.SystemSelected(),
		NetworkLocalEndpointSelection.Ephemeral(),
		NetworkRouteScopeSelection.Current(),
		NetworkIpPacketPolicy.SystemDefault(),
		NetworkRouteAdapterIdentity.NotApplicable());
}

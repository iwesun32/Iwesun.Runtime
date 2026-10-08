using System.Runtime.InteropServices;
using Xunit;

namespace Iwesun.Runtime.Networks.Tests;

public sealed class WindowsNetworkRouteSnapshotIdentityTests
{
	[Fact]
	public void WindowsCatalogCarriesActualLuidForInterfacesOutsideTheBestRoute()
	{
		if (!OperatingSystem.IsWindows()) return;
		var interfaces = new NetworkInterfaceSnapshotProvider();
		var provider = new WindowsNetworkRouteSnapshotProvider(interfaces, new NetworkRouteSnapshotProvider());
		var plan = new RequestedAccessPlan(NetworkPathProvider.Direct,
			NetworkDestinationSelection.Exact(IpAddressValue.Parse("127.0.0.1")),
			NetworkSourceSelection.SystemSelected(), NetworkInterfaceSelection.SystemSelected(),
			NetworkNextHopSelection.SystemSelected(), NetworkLocalEndpointSelection.Ephemeral(),
			NetworkRouteScopeSelection.Current(), NetworkIpPacketPolicy.SystemDefault(),
			NetworkRouteAdapterIdentity.NotApplicable());
		Assert.True(provider.TryRefresh(plan, out var route, out _, out var reason), reason);
		var catalog = interfaces.GetSnapshot();
		Assert.True(catalog.Count > 0);
		var outsideRoute = 0;
		for (var index = 0; index < catalog.Count; index++)
		{
			var identity = catalog[index].Identity;
			if (ConvertInterfaceIndexToLuid(identity.InterfaceIndex, out var expected) != 0) continue;
			Assert.NotEqual(0ul, expected);
			Assert.Equal(expected, identity.Luid);
			if (identity.InterfaceIndex != route.Interface.InterfaceIndex) outsideRoute++;
		}
		// A single-interface machine can still verify the identity, while multi-NIC
		// Windows hosts also exercise entries not patched by ApplySelectedIdentity.
		if (catalog.Count > 1) Assert.True(outsideRoute > 0);
	}

	[DllImport("iphlpapi.dll", ExactSpelling = true)]
	private static extern uint ConvertInterfaceIndexToLuid(uint interfaceIndex, out ulong interfaceLuid);
}

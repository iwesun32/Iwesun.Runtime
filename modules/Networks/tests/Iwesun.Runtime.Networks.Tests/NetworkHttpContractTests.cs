using Iwesun.Runtime.Networks;
using Xunit;

namespace Iwesun.Runtime.Networks.Tests;

public sealed class NetworkHttpContractTests
{
	[Fact]
	public void PoolKeyExcludesTrackingIdentityTimeoutAndRetryState()
	{
		var plan = CreateDirectPlan(IpAddressValue.Parse("192.0.2.10"));
		Assert.True(NetworkHttpProtocolIdentity.TryCreate(
			new Uri("https://service.example:8443/path"),
			"service.example", "service.example", "service.example", 2,
			NetworkHttpConnectionReusePolicy.Reusable, out var protocol, out var reason), reason);
		Assert.True(NetworkHttpConnectionPoolKey.TryCreate(plan, protocol, "resolver-a", out var first, out reason), reason);
		var requestOne = NetworkExecutionIdentity.ForRequest(Guid.NewGuid()).StartAttempt(Guid.NewGuid()).StartBranch(Guid.NewGuid());
		var requestTwo = NetworkExecutionIdentity.ForRequest(Guid.NewGuid()).StartAttempt(Guid.NewGuid()).StartBranch(Guid.NewGuid());

		Assert.NotEqual(requestOne.RequestId, requestTwo.RequestId);
		Assert.Equal(first, NetworkHttpConnectionPoolKey.TryCreate(plan, protocol, "resolver-a", out var second, out reason)
			? second
			: throw new Xunit.Sdk.XunitException(reason));
	}

	[Fact]
	public void PoolKeySeparatesSourceSecurityNameAndAdapterVersion()
	{
		var firstPlan = CreateDirectPlan(IpAddressValue.Parse("192.0.2.10"));
		var secondPlan = CreateDirectPlan(IpAddressValue.Parse("192.0.2.11"));
		Assert.True(NetworkHttpProtocolIdentity.TryCreate(
			new Uri("https://service.example/"), null, null, null, 2,
			NetworkHttpConnectionReusePolicy.Reusable, out var firstProtocol, out _));
		Assert.True(NetworkHttpProtocolIdentity.TryCreate(
			new Uri("https://other.example/"), null, null, null, 2,
			NetworkHttpConnectionReusePolicy.Reusable, out var secondProtocol, out _));
		Assert.True(NetworkHttpConnectionPoolKey.TryCreate(firstPlan, firstProtocol, "resolver-a", out var first, out _));
		Assert.True(NetworkHttpConnectionPoolKey.TryCreate(secondPlan, firstProtocol, "resolver-a", out var differentSource, out _));
		Assert.True(NetworkHttpConnectionPoolKey.TryCreate(firstPlan, secondProtocol, "resolver-a", out var differentHost, out _));
		var capabilityOnePlan = CreateAdapterPlan(1);
		var capabilityTwoPlan = CreateAdapterPlan(2);
		Assert.True(NetworkHttpConnectionPoolKey.TryCreate(capabilityOnePlan, firstProtocol, "resolver-a", out var firstAdapter, out _));
		Assert.True(NetworkHttpConnectionPoolKey.TryCreate(capabilityTwoPlan, firstProtocol, "resolver-a", out var secondAdapter, out _));

		Assert.NotEqual(first, differentSource);
		Assert.NotEqual(first, differentHost);
		Assert.NotEqual(firstAdapter, secondAdapter);
	}

	[Fact]
	public void PoolKeySeparatesCertificatePins()
	{
		var plan = CreateDirectPlan(IpAddressValue.Parse("192.0.2.10"));
		Assert.True(NetworkHttpProtocolIdentity.TryCreate(
			new Uri("https://service.example/"), null, null, null, 2,
			NetworkHttpConnectionReusePolicy.Reusable, out var protocol, out var reason), reason);
		var firstProtocol = protocol with { CertificatePinSha256 = new string('a', 64) };
		var secondProtocol = protocol with { CertificatePinSha256 = new string('b', 64) };
		Assert.True(NetworkHttpConnectionPoolKey.TryCreate(
			plan, firstProtocol, "resolver-a", out var first, out reason), reason);
		Assert.True(NetworkHttpConnectionPoolKey.TryCreate(
			plan, secondProtocol, "resolver-a", out var second, out reason), reason);

		Assert.NotEqual(first, second);
	}

	[Fact]
	public void PoolKeySeparatesResolvedSystemProxyIdentity()
	{
		var plan = new RequestedAccessPlan(
			NetworkPathProvider.SystemProxy,
			NetworkDestinationSelection.Exact(IpAddressValue.Parse("203.0.113.20")),
			NetworkSourceSelection.SystemSelected(),
			NetworkInterfaceSelection.SystemSelected(),
			NetworkNextHopSelection.SystemSelected(),
			NetworkLocalEndpointSelection.Ephemeral(),
			NetworkRouteScopeSelection.Current(),
			NetworkIpPacketPolicy.SystemDefault(),
			NetworkRouteAdapterIdentity.NotApplicable());
		Assert.True(NetworkHttpProtocolIdentity.TryCreate(
			new Uri("https://service.example/"), null, null, null, 2,
			NetworkHttpConnectionReusePolicy.Reusable, out var protocol, out var reason), reason);
		Assert.True(NetworkHttpConnectionPoolKey.TryCreate(
			plan, protocol, "resolver-a", "system-proxy:http://proxy-a:8080",
			out var first, out reason), reason);
		Assert.True(NetworkHttpConnectionPoolKey.TryCreate(
			plan, protocol, "resolver-a", "system-proxy:http://proxy-b:8080",
			out var second, out reason), reason);

		Assert.NotEqual(first, second);
	}

	[Fact]
	public void RouteAdapterRegistryRequiresExactCapabilityVersionAndTransport()
	{
		var registry = new NetworkRouteAdapterRegistry();
		Assert.True(registry.TryRegister(new FakeAdapter()));
		Assert.True(registry.TryResolve(
			NetworkRouteAdapterIdentity.Exact("proxy-a", 3),
			NetworkRouteAdapterTransportCapabilities.ByteStream,
			out var resolved,
			out var reason), reason);
		Assert.NotNull(resolved);
		Assert.False(registry.TryResolve(
			NetworkRouteAdapterIdentity.Exact("proxy-a", 2),
			NetworkRouteAdapterTransportCapabilities.ByteStream,
			out _, out reason));
		Assert.Equal(NetworkAccessFailureCodes.RouteAdapterCapabilityVersionMismatch, reason);
		Assert.False(registry.TryResolve(
			NetworkRouteAdapterIdentity.Exact("proxy-a", 3),
			NetworkRouteAdapterTransportCapabilities.Datagram,
			out _, out reason));
		Assert.Equal(NetworkAccessFailureCodes.RouteAdapterTransportUnsupported, reason);
	}

	private static RequestedAccessPlan CreateDirectPlan(IpAddressValue source) => new(
		NetworkPathProvider.Direct,
		NetworkDestinationSelection.Exact(IpAddressValue.Parse("203.0.113.20")),
		NetworkSourceSelection.Exact(source),
		NetworkInterfaceSelection.SystemSelected(),
		NetworkNextHopSelection.SystemSelected(),
		NetworkLocalEndpointSelection.Ephemeral(),
		NetworkRouteScopeSelection.Current(),
		NetworkIpPacketPolicy.SystemDefault(),
		NetworkRouteAdapterIdentity.NotApplicable());

	private static RequestedAccessPlan CreateAdapterPlan(int version) => new(
		NetworkPathProvider.RouteAdapter,
		NetworkDestinationSelection.Exact(IpAddressValue.Parse("203.0.113.20")),
		NetworkSourceSelection.SystemSelected(),
		NetworkInterfaceSelection.SystemSelected(),
		NetworkNextHopSelection.SystemSelected(),
		NetworkLocalEndpointSelection.Ephemeral(),
		NetworkRouteScopeSelection.Current(),
		NetworkIpPacketPolicy.SystemDefault(),
		NetworkRouteAdapterIdentity.Exact("proxy-a", version));

	private sealed class FakeAdapter : INetworkRouteAdapter
	{
		public NetworkRouteAdapterCapabilities Capabilities => new(
			"proxy-a", 3, NetworkRouteAdapterTransportCapabilities.ByteStream, true, false);

		public ValueTask<NetworkRouteAdapterConnection> ConnectAsync(
			NetworkRouteAdapterConnectRequest request,
			CancellationToken cancellationToken) => throw new NotSupportedException();
	}
}

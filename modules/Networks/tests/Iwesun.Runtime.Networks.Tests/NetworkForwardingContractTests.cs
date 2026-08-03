using Iwesun.Runtime.Networks;
using Xunit;

namespace Iwesun.Runtime.Networks.Tests;

public sealed class NetworkForwardingContractTests
{
	[Fact]
	public void ForwardingCapabilitiesRemainIndependent()
	{
		var datagram = NetworkDataPlaneCapabilities.Datagram;

		Assert.True(datagram.HasFlag(NetworkDataPlaneCapabilities.Datagram));
		Assert.False(datagram.HasFlag(NetworkDataPlaneCapabilities.DnsProxy));
		Assert.False(datagram.HasFlag(NetworkDataPlaneCapabilities.Nat));
		Assert.False(datagram.HasFlag(NetworkDataPlaneCapabilities.IpPacket));
	}

	[Fact]
	public void NatMappingRequiresExplicitModeAndTranslationKind()
	{
		var request = new NetworkNatMappingRequest(
			default,
			default,
			default,
			NetworkNatMappingMode.Unspecified,
			NetworkNatTranslationKind.Unspecified,
			default);

		Assert.False(request.IsValid);
	}

	[Fact]
	public void DatagramCorrelationKindsDoNotAliasFlowSerial()
	{
		Assert.NotEqual(
			(byte)NetworkDatagramCorrelationKind.ProtocolToken,
			(byte)NetworkDatagramCorrelationKind.NatTuple);
		Assert.NotEqual(
			(byte)NetworkDatagramCorrelationKind.DedicatedLocalEndpoint,
			(byte)NetworkDatagramCorrelationKind.EncapsulatedToken);
	}
}

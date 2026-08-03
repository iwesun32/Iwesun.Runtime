using Xunit;

namespace Iwesun.Runtime.Networks.Tests;

public sealed class NetworkLocalTableValueTypeTests
{
	[Fact]
	public void ArpRecord_ShouldUseCanonicalIpAndMacValues()
	{
		var requestId = Guid.NewGuid();

		Assert.True(WindowsArpTableEndpoint.TryParseLine(
			requestId,
			"192.168.1.20  00-11-22-33-44-55  dynamic",
			out var record));
		Assert.Equal(requestId, record.RequestId);
		Assert.Equal(IpAddressValue.Parse("192.168.1.20"), record.Ip);
		Assert.Equal(MacAddressValue.Parse("00:11:22:33:44:55", null), record.Mac);
		Assert.True(record.Mac.IsValidIdentity);
	}

	[Fact]
	public void Ipv6NeighborRecord_ShouldSeparatePureAddressInterfaceAndOptionalMac()
	{
		var requestId = Guid.NewGuid();

		Assert.True(WindowsIpv6NeighborSnapshotEndpoint.TryParseCsvLine(
			requestId,
			"\"fe80::1%12\",\"00-11-22-33-44-55\",\"Reachable\",\"12\"",
			out var record));
		Assert.Equal(IpAddressValue.Parse("fe80::1"), record.Ip);
		Assert.Equal(MacAddressValue.Parse("00:11:22:33:44:55", null), record.Mac);
		Assert.Equal(12, record.InterfaceIndex);

		Assert.True(WindowsIpv6NeighborSnapshotEndpoint.TryParseCsvLine(
			requestId,
			"\"2001:db8::1\",\"\",\"Unreachable\",\"7\"",
			out var unresolved));
		Assert.True(unresolved.Mac.IsNull);
		Assert.Equal(IpAddressValue.Parse("2001:db8::1"), unresolved.Ip);
	}
}

using Xunit;

namespace Iwesun.Runtime.Networks.Tests;

public sealed class NetworkContractHardeningTests
{
	[Fact]
	public void IpAddressValueRejectsNonCanonicalIpv4State()
	{
		var nonZeroHigh = new byte[IpAddressValue.BinarySize];
		nonZeroHigh[0] = 4;
		nonZeroHigh[8] = 1;
		var oversizedLow = new byte[IpAddressValue.BinarySize];
		oversizedLow[0] = 4;
		oversizedLow[12] = 1;
		var unknownFamily = new byte[IpAddressValue.BinarySize];
		unknownFamily[0] = 5;
		var canonical = new byte[IpAddressValue.BinarySize];
		canonical[0] = 4;
		canonical[^1] = 1;

		Assert.False(IpAddressValue.TryReadBinary(nonZeroHigh, out _));
		Assert.False(IpAddressValue.TryReadBinary(oversizedLow, out _));
		Assert.False(IpAddressValue.TryReadBinary(unknownFamily, out _));
		Assert.True(IpAddressValue.TryReadBinary(canonical, out var value));
		Assert.Equal(IpAddressValue.Parse("0.0.0.1"), value);
	}

	[Fact]
	public void SpecialEndpointsRejectEmptyRequestIdBeforeFifo()
	{
		using var process = new ProcessCommandEndpoint();
		using var writeOutput = new PowerShellWriteOutputEndpoint();
		using var neighborPowerShell = new PowerShellIpv6NeighborSnapshotEndpoint();
		using var arp = new WindowsArpTableEndpoint();
		using var neighbors = new WindowsIpv6NeighborSnapshotEndpoint();

		Assert.False(process.TrySend(new ProcessCommandRequest(Guid.Empty, "cmd")));
		Assert.False(writeOutput.TrySend(PowerShellWriteOutputBinaryRequest.Alpha(Guid.Empty)));
		Assert.False(neighborPowerShell.TrySend(new PowerShellIpv6NeighborSnapshotBinaryRequest(Guid.Empty)));
		Assert.False(arp.TrySend(new WindowsArpTableRequest(Guid.Empty)));
		Assert.False(neighbors.TrySend(new WindowsIpv6NeighborSnapshotRequest(Guid.Empty)));
		Assert.Equal(0, process.SendQueueLength);
		Assert.Equal(0, writeOutput.SendQueueLength);
		Assert.Equal(0, neighborPowerShell.SendQueueLength);
		Assert.Equal(0, arp.SendQueueLength);
		Assert.Equal(0, neighbors.SendQueueLength);
	}
}

using Iwesun.Runtime.Networks;
using Xunit;

namespace Iwesun.Runtime.Networks.Tests;

public sealed class WindowsNetworkWfpConnectionPolicyBackendTests
{
	[Fact]
	public void NativeAbiLayoutsMatchTheWindowsSdkOnX64()
	{
		if (IntPtr.Size != 8) return;

		Assert.Equal([72, 16, 24, 16, 88, 40, 72, 20, 200, 152],
			WindowsNetworkWfpConnectionPolicyBackend.GetNativeLayoutSizes());
	}

	[Fact]
	public void CapabilityProbeIsReadOnlyAndReturnsAStableReasonWhenUnsupported()
	{
		var backend = WindowsNetworkWfpConnectionPolicyBackend.CreateDefault();
		var capability = backend.QueryCapability();

		if (capability.Supported)
		{
			Assert.False(capability.MissingPermission);
			Assert.Equal(string.Empty, capability.ReasonCode);
		}
		else
		{
			Assert.Contains(capability.ReasonCode, new[]
			{
				NetworkAccessFailureCodes.ExactNextHopBackendUnavailable,
				NetworkAccessFailureCodes.PlatformPermissionMissing,
			});
		}
	}
}

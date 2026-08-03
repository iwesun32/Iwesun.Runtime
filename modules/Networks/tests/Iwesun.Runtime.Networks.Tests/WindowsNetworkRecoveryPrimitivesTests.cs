using Iwesun.Runtime.Networks;
using Xunit;

namespace Iwesun.Runtime.Networks.Tests;

public sealed class WindowsNetworkRecoveryPrimitivesTests
{
	[Fact]
	public async Task PlatformSuccessAddressChangeAndConnectivityRemainIndependent()
	{
		var platform = new FakePlatform(NetworkRecoveryPlatformResult.Success());
		var primitives = new WindowsNetworkRecoveryPrimitives(platform);
		var request = new NetworkRecoveryActionRequest(
			Guid.NewGuid(), NetworkRecoveryActionKind.DhcpV6Renew, CreateInterface());

		var result = await primitives.ExecuteAsync(request, _ => ValueTask.FromResult(true));

		Assert.True(result.RequestAccepted);
		Assert.True(result.PlatformActionSucceeded);
		Assert.False(result.AddressChanged);
		Assert.Equal(NetworkConnectivityRecheckOutcome.Succeeded, result.ConnectivityRecheck);
		Assert.Equal(1, platform.RenewCount);
		Assert.True(result.Before.IsValid);
		Assert.True(result.After.IsValid);
	}

	[Fact]
	public async Task CommandFailureDoesNotOverwriteSuccessfulConnectivityRecheck()
	{
		var platformResult = new NetworkRecoveryPlatformResult(false, 5, default, "access-denied");
		var primitives = new WindowsNetworkRecoveryPrimitives(new FakePlatform(platformResult));
		var request = new NetworkRecoveryActionRequest(
			Guid.NewGuid(), NetworkRecoveryActionKind.DhcpV6Release, CreateInterface());

		var result = await primitives.ExecuteAsync(request, _ => ValueTask.FromResult(true));

		Assert.True(result.RequestAccepted);
		Assert.False(result.PlatformActionSucceeded);
		Assert.Equal(5, result.ExitCode);
		Assert.Equal("access-denied", result.ReasonCode);
		Assert.Equal(NetworkConnectivityRecheckOutcome.Succeeded, result.ConnectivityRecheck);
	}

	[Fact]
	public async Task InvalidRequestIsRejectedBeforePlatformExecution()
	{
		var platform = new FakePlatform(NetworkRecoveryPlatformResult.Success());
		var primitives = new WindowsNetworkRecoveryPrimitives(platform);
		var request = new NetworkRecoveryActionRequest(
			Guid.Empty, NetworkRecoveryActionKind.RestartInterface, CreateInterface());

		var result = await primitives.ExecuteAsync(request);

		Assert.False(result.RequestAccepted);
		Assert.False(result.PlatformActionSucceeded);
		Assert.Equal(0, platform.TotalCount);
	}

	[Fact]
	public async Task WaitIsAnIndependentPrimitiveAndDoesNotInvokePlatform()
	{
		var platform = new FakePlatform(NetworkRecoveryPlatformResult.Success());
		var primitives = new WindowsNetworkRecoveryPrimitives(platform);
		var request = new NetworkRecoveryActionRequest(
			Guid.NewGuid(), NetworkRecoveryActionKind.Wait, CreateInterface(), 10);

		var result = await primitives.ExecuteAsync(request);

		Assert.True(result.RequestAccepted);
		Assert.True(result.PlatformActionSucceeded);
		Assert.Equal(NetworkConnectivityRecheckOutcome.NotPerformed, result.ConnectivityRecheck);
		Assert.Equal(0, platform.TotalCount);
	}

	private static NetworkInterfaceIdentity CreateInterface() =>
		new(Guid.NewGuid(), 1, "recovery-test-interface", 1);

	private sealed class FakePlatform(NetworkRecoveryPlatformResult result) : INetworkRecoveryPlatformExecutor
	{
		public int RouterSolicitationCount { get; private set; }
		public int ReleaseCount { get; private set; }
		public int RenewCount { get; private set; }
		public int RestartCount { get; private set; }
		public int TotalCount => RouterSolicitationCount + ReleaseCount + RenewCount + RestartCount;

		public ValueTask<NetworkRecoveryPlatformResult> SendRouterSolicitationAsync(
			NetworkInterfaceIdentity networkInterface, CancellationToken cancellationToken)
		{
			RouterSolicitationCount++;
			return ValueTask.FromResult(result);
		}

		public ValueTask<NetworkRecoveryPlatformResult> ReleaseDhcpV6Async(
			NetworkInterfaceIdentity networkInterface, CancellationToken cancellationToken)
		{
			ReleaseCount++;
			return ValueTask.FromResult(result);
		}

		public ValueTask<NetworkRecoveryPlatformResult> RenewDhcpV6Async(
			NetworkInterfaceIdentity networkInterface, CancellationToken cancellationToken)
		{
			RenewCount++;
			return ValueTask.FromResult(result);
		}

		public ValueTask<NetworkRecoveryPlatformResult> RestartInterfaceAsync(
			NetworkInterfaceIdentity networkInterface, CancellationToken cancellationToken)
		{
			RestartCount++;
			return ValueTask.FromResult(result);
		}
	}
}

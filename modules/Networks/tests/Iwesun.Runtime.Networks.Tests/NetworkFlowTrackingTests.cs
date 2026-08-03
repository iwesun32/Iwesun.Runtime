using Iwesun.Runtime.Networks;
using Xunit;

namespace Iwesun.Runtime.Networks.Tests;

[Collection("NetworkFlowSerialAllocator")]
public sealed class NetworkFlowTrackingTests
{
	[Fact]
	public void ConcurrentAllocationReturnsUniqueNonZeroSerials()
	{
		NetworkFlowSerialAllocator.ResetForTests();
		try
		{
			var values = new uint[4096];
			Parallel.For(0, values.Length, index =>
			{
				values[index] = NetworkFlowSerialAllocator.GetNext().Value;
			});

			Assert.DoesNotContain(0U, values);
			Assert.Equal(values.Length, values.Distinct().Count());
			Assert.Equal(values.Length, NetworkFlowSerialAllocator.ReservedCount);
		}
		finally
		{
			NetworkFlowSerialAllocator.ResetForTests();
		}
	}

	[Fact]
	public async Task QuarantineKeepsSerialReservedUntilWindowEnds()
	{
		NetworkFlowSerialAllocator.ResetForTests();
		try
		{
			var serial = NetworkFlowSerialAllocator.GetNext();
			Assert.True(NetworkFlowSerialAllocator.TryTransition(serial, NetworkFlowLifecycleState.Binding));
			Assert.True(NetworkFlowSerialAllocator.TryTransition(serial, NetworkFlowLifecycleState.Active));
			Assert.True(NetworkFlowSerialAllocator.BeginQuarantine(serial, TimeSpan.FromMilliseconds(80)));
			Assert.True(NetworkFlowSerialAllocator.TryGet(serial, out var quarantined));
			Assert.Equal(NetworkFlowLifecycleState.Quarantine, quarantined.State);

			await Task.Delay(150);

			Assert.False(NetworkFlowSerialAllocator.TryGet(serial, out _));
			Assert.Equal(0, NetworkFlowSerialAllocator.ReservedCount);
		}
		finally
		{
			NetworkFlowSerialAllocator.ResetForTests();
		}
	}

	[Fact]
	public void QuarantineStatePublishesItsDeadlineAtomically()
	{
		NetworkFlowSerialAllocator.ResetForTests();
		try
		{
			var serial = NetworkFlowSerialAllocator.GetNext();
			var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

			Assert.True(NetworkFlowSerialAllocator.BeginQuarantine(serial, TimeSpan.FromSeconds(1)));
			Assert.True(NetworkFlowSerialAllocator.TryGet(serial, out var snapshot));
			Assert.Equal(NetworkFlowLifecycleState.Quarantine, snapshot.State);
			Assert.True(snapshot.QuarantineUntilUnixMs >= before + 900);
		}
		finally
		{
			NetworkFlowSerialAllocator.ResetForTests();
		}
	}

	[Fact]
	public void CounterWrapSkipsZeroAndOccupiedSerials()
	{
		NetworkFlowSerialAllocator.ResetForTests(-2);
		try
		{
			var max = NetworkFlowSerialAllocator.GetNext();
			var wrapped = NetworkFlowSerialAllocator.GetNext();

			Assert.Equal(uint.MaxValue, max.Value);
			Assert.Equal(1U, wrapped.Value);
			Assert.True(max.IsValid);
			Assert.True(wrapped.IsValid);
		}
		finally
		{
			NetworkFlowSerialAllocator.ResetForTests();
		}
	}
}

[CollectionDefinition("NetworkFlowSerialAllocator", DisableParallelization = true)]
public sealed class NetworkFlowSerialAllocatorCollection;

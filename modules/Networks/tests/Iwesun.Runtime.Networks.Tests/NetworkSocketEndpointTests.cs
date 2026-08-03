using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Collections.Concurrent;
using Iwesun.Runtime.Networks;
using Xunit;

namespace Iwesun.Runtime.Networks.Tests;

public sealed class NetworkSocketEndpointTests
{
	[Fact]
	public async Task TcpEndpointPreservesOnePublicFourLevelIdentity()
	{
		using var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var port = checked((ushort)((IPEndPoint)listener.LocalEndpoint).Port);
		var environment = CreateContext(IPAddress.Loopback);
		using var endpoint = new NetworkTcpConnectEndpoint<string>(environment.Context, maxAttemptCount: 1);
		var request = new NetworkTcpConnectRequest<string>(
			Guid.NewGuid(), "tcp-key", environment.Plan, NetworkAccessSecurityBoundary.NotApplicable(), port, 1000, false);
		var observed = new TaskCompletionSource<NetworkExecutionIdentity>(TaskCreationOptions.RunContinuationsAsynchronously);
		endpoint.ResponseObserved += (_, args) => observed.TrySetResult(args.Identity);
		var accept = listener.AcceptSocketAsync();

		Assert.True(endpoint.TrySend(request));
		using var accepted = await accept.WaitAsync(TimeSpan.FromSeconds(3));
		await WaitUntilAsync(() => endpoint.ReceiveQueueLength == 1);
		Assert.True(endpoint.TryReadReceived(out var completion));
		var observedIdentity = await observed.Task.WaitAsync(TimeSpan.FromSeconds(3));

		Assert.True(completion.Response.Result.IsSuccess, completion.Response.Result.ReasonCode);
		Assert.True(completion.Response.Identity.HasResponse);
		Assert.Equal(completion.Response.Identity, completion.Identity);
		Assert.Equal(completion.Response.Identity, observedIdentity);
		Assert.Equal("tcp-key", completion.Key);
	}

	[Fact]
	public async Task UdpEndpointReturnsPayloadAndBranchEvidence()
	{
		using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
		var port = checked((ushort)((IPEndPoint)server.Client.LocalEndPoint!).Port);
		var environment = CreateContext(IPAddress.Loopback);
		using var endpoint = new NetworkUdpDatagramEndpoint<int>(environment.Context, maxAttemptCount: 1);
		var request = new NetworkUdpDatagramRequest<int>(
			Guid.NewGuid(), 42, environment.Plan, NetworkAccessSecurityBoundary.NotApplicable(), port, [1, 2], 64, 1000, false);
		var serverTask = Task.Run(async () =>
		{
			var received = await server.ReceiveAsync();
			await server.SendAsync([9, 8], 2, received.RemoteEndPoint);
		});

		Assert.True(endpoint.TrySend(request));
		await serverTask.WaitAsync(TimeSpan.FromSeconds(3));
		await WaitUntilAsync(() => endpoint.ReceiveQueueLength == 1);
		Assert.True(endpoint.TryReadReceived(out var completion));

		Assert.Equal([9, 8], completion.Response.Result.Payload);
		Assert.Equal(AccessCompliance.Satisfied, completion.Response.Result.Compliance);
		Assert.Equal(completion.Response.Identity.BranchId, completion.Response.Result.Identity.BranchId);
		Assert.Equal(ProofKind.Observed, completion.Response.Result.Evidence.Interface.Proof);
		Assert.True(completion.Response.Result.FlowSerial.IsValid);
		Assert.True(completion.Response.Result.SocketGeneration > 0);
		Assert.True(completion.Response.Result.DispatchEvidence.IsValid);
		Assert.Equal(
			IpAddressValue.FromIPAddress(IPAddress.Loopback),
			completion.Response.Result.DispatchEvidence.ActualLocalAddress);
		Assert.True(NetworkFlowSerialAllocator.TryGet(completion.Response.Result.FlowSerial, out var flow));
		Assert.Equal(completion.Response.Identity.BranchId, flow.Identity.BranchId);
		Assert.Equal(completion.Response.Result.SocketGeneration, flow.Transport.SocketGeneration);
		Assert.Equal(NetworkDatagramCorrelationKind.ConnectedSocket, flow.Transport.CorrelationKind);
	}

	[Fact]
	public async Task DatagramDispatchObserversAreIsolated()
	{
		using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
		var port = checked((ushort)((IPEndPoint)server.Client.LocalEndPoint!).Port);
		var environment = CreateContext(IPAddress.Loopback);
		using var endpoint = new NetworkUdpDatagramEndpoint<int>(environment.Context, maxAttemptCount: 1);
		var observed = new TaskCompletionSource<NetworkDatagramDispatchEvidence>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		endpoint.DatagramDispatched += _ => throw new InvalidOperationException("observer-failure");
		endpoint.DatagramDispatched += evidence => observed.TrySetResult(evidence);
		var serverTask = Task.Run(async () =>
		{
			var received = await server.ReceiveAsync();
			await server.SendAsync([2], 1, received.RemoteEndPoint);
		});

		Assert.True(endpoint.TrySend(new NetworkUdpDatagramRequest<int>(
			Guid.NewGuid(), 1, environment.Plan, NetworkAccessSecurityBoundary.NotApplicable(),
			port, [1], 64, 1000, false)));
		var evidence = await observed.Task.WaitAsync(TimeSpan.FromSeconds(3));
		await serverTask.WaitAsync(TimeSpan.FromSeconds(3));
		await WaitUntilAsync(() => endpoint.ReceiveQueueLength == 1);

		Assert.True(evidence.IsValid);
		Assert.True(endpoint.TryReadReceived(out var completion));
		Assert.Equal(ProtocolOutcome.Succeeded, completion.Response.Result.Outcome);
	}

	[Fact]
	public async Task UdpEndpointReusesSlotAfterQuarantine()
	{
		using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
		var port = checked((ushort)((IPEndPoint)server.Client.LocalEndPoint!).Port);
		var environment = CreateContext(IPAddress.Loopback);
		using var endpoint = new NetworkUdpDatagramEndpoint<int>(
			environment.Context,
			maxAttemptCount: 1,
			lateResponseQuarantineMs: 25);
		var evidence = new ConcurrentQueue<NetworkDatagramDispatchEvidence>();
		endpoint.DatagramDispatched += evidence.Enqueue;
		var serverTask = Task.Run(async () =>
		{
			for (var index = 0; index < 2; index++)
			{
				var received = await server.ReceiveAsync();
				await server.SendAsync([(byte)(10 + index)], 1, received.RemoteEndPoint);
			}
		});

		for (var index = 0; index < 2; index++)
		{
			var request = new NetworkUdpDatagramRequest<int>(
				Guid.NewGuid(), index, environment.Plan, NetworkAccessSecurityBoundary.NotApplicable(),
				port, [(byte)index], 64, 1000, false);
			Assert.True(endpoint.TrySend(request));
			await WaitUntilAsync(() => endpoint.ReceiveQueueLength == 1);
			Assert.True(endpoint.TryReadReceived(out _));
			if (index == 0) await Task.Delay(60);
		}
		await serverTask.WaitAsync(TimeSpan.FromSeconds(3));

		var dispatched = evidence.ToArray();
		Assert.Equal(2, dispatched.Length);
		Assert.Equal(dispatched[0].SocketGeneration, dispatched[1].SocketGeneration);
		Assert.Equal(dispatched[0].ActualLocalPort, dispatched[1].ActualLocalPort);
		Assert.NotEqual(dispatched[0].FlowSerial, dispatched[1].FlowSerial);
	}

	[Fact]
	public async Task ConcurrentTokenlessUdpFlowsUseDifferentLocalPorts()
	{
		using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
		var port = checked((ushort)((IPEndPoint)server.Client.LocalEndPoint!).Port);
		var environment = CreateContext(IPAddress.Loopback);
		using var endpoint = new NetworkUdpDatagramEndpoint<int>(
			environment.Context,
			maxAttemptCount: 1,
			lateResponseQuarantineMs: 25);
		var evidence = new ConcurrentQueue<NetworkDatagramDispatchEvidence>();
		endpoint.DatagramDispatched += evidence.Enqueue;
		var serverTask = Task.Run(async () =>
		{
			var first = await server.ReceiveAsync();
			var second = await server.ReceiveAsync();
			await server.SendAsync([1], 1, first.RemoteEndPoint);
			await server.SendAsync([2], 1, second.RemoteEndPoint);
		});

		Assert.True(endpoint.TrySend(new NetworkUdpDatagramRequest<int>(
			Guid.NewGuid(), 1, environment.Plan, NetworkAccessSecurityBoundary.NotApplicable(),
			port, [1], 64, 1000, false)));
		Assert.True(endpoint.TrySend(new NetworkUdpDatagramRequest<int>(
			Guid.NewGuid(), 2, environment.Plan, NetworkAccessSecurityBoundary.NotApplicable(),
			port, [2], 64, 1000, false)));
		await serverTask.WaitAsync(TimeSpan.FromSeconds(3));
		await WaitUntilAsync(() => endpoint.ReceiveQueueLength == 2);

		var dispatched = evidence.ToArray();
		Assert.Equal(2, dispatched.Length);
		Assert.Equal(2, dispatched.Select(item => item.ActualLocalPort).Distinct().Count());
		Assert.Equal(2, dispatched.Select(item => item.FlowSerial).Distinct().Count());
	}

	[Fact]
	public async Task AutomaticUdpBatchStartsAndCompletesThreeIndependentFlows()
	{
		using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
		var port = checked((ushort)((IPEndPoint)server.Client.LocalEndPoint!).Port);
		using var endpoint = new NetworkUdpDatagramEndpoint<int>(
			maxAttemptCount: 1,
			defaultTimeoutMs: 3_000);
		var completed = new ConcurrentQueue<TrackedRequestCompletion<
			NetworkUdpDatagramRequest<int>, NetworkUdpDatagramResponse<int>, int>>();
		var failed = new ConcurrentQueue<TrackedRequestFailure<NetworkUdpDatagramRequest<int>, int>>();
		var dispatched = new ConcurrentQueue<NetworkDatagramDispatchEvidence>();
		endpoint.RequestAcknowledged += (_, args) => completed.Enqueue(args.Completion);
		endpoint.RequestFailed += (_, args) => failed.Enqueue(args.Failure);
		endpoint.DatagramDispatched += dispatched.Enqueue;
		var plan = new RequestedAccessPlan(
			NetworkPathProvider.Automatic,
			NetworkDestinationSelection.Exact(IpAddressValue.FromIPAddress(IPAddress.Loopback)),
			NetworkSourceSelection.SystemSelected(),
			NetworkInterfaceSelection.SystemSelected(),
			NetworkNextHopSelection.SystemSelected(),
			NetworkLocalEndpointSelection.Ephemeral(),
			NetworkRouteScopeSelection.Current(),
			NetworkIpPacketPolicy.SystemDefault(),
			NetworkRouteAdapterIdentity.NotApplicable());
		var requests = Enumerable.Range(1, 3)
			.Select(index => new NetworkUdpDatagramRequest<int>(
				Guid.NewGuid(), index, plan, NetworkAccessSecurityBoundary.NotApplicable(),
				port, [(byte)index], 64, 3_000, false))
			.ToArray();
		var serverTask = Task.Run(async () =>
		{
			for (var index = 0; index < requests.Length; index++)
			{
				var received = await server.ReceiveAsync();
				await server.SendAsync(received.Buffer, received.RemoteEndPoint);
			}
		});

		Assert.Equal(requests.Length, endpoint.Send(requests));
		await WaitUntilAsync(() => completed.Count + failed.Count == requests.Length);
		Assert.True(
			dispatched.Count == requests.Length,
			$"dispatched={dispatched.Count}; completed={completed.Count}; failed={failed.Count}; " +
			string.Join(" | ", failed.Select(item =>
				$"{item.Key}:{item.Failure.Kind}:{item.Failure.Reason}")));
		await serverTask.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Empty(failed);
		Assert.Equal(requests.Length, completed.Count);
		Assert.Equal(requests.Length, completed
			.Select(item => item.Response.Result.DispatchEvidence.ActualLocalPort)
			.Distinct()
			.Count());
	}

	[Fact]
	public async Task PooledUdpIgnoresUnexpectedRemoteEndpoint()
	{
		using var expectedServer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
		using var unexpectedServer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
		var port = checked((ushort)((IPEndPoint)expectedServer.Client.LocalEndPoint!).Port);
		var environment = CreateContext(IPAddress.Loopback);
		using var endpoint = new NetworkUdpDatagramEndpoint<int>(environment.Context, maxAttemptCount: 1);
		var serverTask = Task.Run(async () =>
		{
			var received = await expectedServer.ReceiveAsync();
			await unexpectedServer.SendAsync([99], 1, received.RemoteEndPoint);
			await Task.Delay(50);
			await expectedServer.SendAsync([7], 1, received.RemoteEndPoint);
		});

		Assert.True(endpoint.TrySend(new NetworkUdpDatagramRequest<int>(
			Guid.NewGuid(), 7, environment.Plan, NetworkAccessSecurityBoundary.NotApplicable(),
			port, [1], 64, 1000, false)));
		await serverTask.WaitAsync(TimeSpan.FromSeconds(3));
		await WaitUntilAsync(() => endpoint.ReceiveQueueLength == 1);
		Assert.True(endpoint.TryReadReceived(out var completion));

		Assert.Equal([7], completion.Response.Result.Payload);
	}

	[Fact]
	public async Task UdpEndpointAcceptsInjectedDataPlane()
	{
		var environment = CreateContext(IPAddress.Loopback);
		using var dataPlane = new FakeDatagramDataPlane();
		using var endpoint = new NetworkUdpDatagramEndpoint<int>(
			environment.Context,
			maxAttemptCount: 1,
			dataPlane: dataPlane);
		var request = new NetworkUdpDatagramRequest<int>(
			Guid.NewGuid(), 5, environment.Plan, NetworkAccessSecurityBoundary.NotApplicable(),
			53, [1], 64, 1000, false);

		Assert.True(endpoint.TrySend(request));
		await WaitUntilAsync(() => endpoint.ReceiveQueueLength == 1);
		Assert.True(endpoint.TryReadReceived(out var completion));

		Assert.Equal(1, dataPlane.ExecutionCount);
		Assert.Equal([42], completion.Response.Result.Payload);
		Assert.True(completion.Response.Result.FlowSerial.IsValid);
	}

	private static TestEnvironment CreateContext(IPAddress address)
	{
		var binary = IpAddressValue.FromIPAddress(address);
		var interfaceIndex = FindInterfaceIndex(address);
		var identity = new NetworkInterfaceIdentity(Guid.NewGuid(), interfaceIndex, "socket-endpoint-test", interfaceIndex);
		var interfaces = new NetworkInterfaceSnapshotProvider();
		Assert.True(interfaces.TryReplace([new NetworkInterfaceSnapshot(identity, default, true)], out _, out _));
		var routes = new NetworkRouteSnapshotProvider();
		Assert.True(routes.TryReplace(binary, binary, identity, default, 0, out _, out _));
		var context = new NetworkAccessExecutionContext(interfaces, routes, refreshWindowsSnapshots: false);
		var plan = new RequestedAccessPlan(
			NetworkPathProvider.Automatic,
			NetworkDestinationSelection.Exact(binary),
			NetworkSourceSelection.SystemSelected(),
			NetworkInterfaceSelection.SystemSelected(),
			NetworkNextHopSelection.SystemSelected(),
			NetworkLocalEndpointSelection.Ephemeral(),
			NetworkRouteScopeSelection.Current(),
			NetworkIpPacketPolicy.SystemDefault(),
			NetworkRouteAdapterIdentity.NotApplicable());
		return new TestEnvironment(context, plan);
	}

	private static uint FindInterfaceIndex(IPAddress address)
	{
		foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
		{
			var properties = networkInterface.GetIPProperties();
			if (!properties.UnicastAddresses.Any(item => item.Address.Equals(address))) continue;
			var index = address.AddressFamily == AddressFamily.InterNetwork
				? properties.GetIPv4Properties()?.Index
				: properties.GetIPv6Properties()?.Index;
			if (index is > 0) return checked((uint)index.Value);
		}
		throw new InvalidOperationException("loopback-interface-index-not-found");
	}

	private static async Task WaitUntilAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(3);
		while (!condition())
		{
			if (DateTime.UtcNow >= deadline) throw new TimeoutException("test-condition-timeout");
			await Task.Delay(10);
		}
	}

	private readonly record struct TestEnvironment(
		NetworkAccessExecutionContext Context,
		RequestedAccessPlan Plan);

	private sealed class FakeDatagramDataPlane : INetworkDatagramDataPlane
	{
		private static int _nextGeneration;
		private readonly NetworkDataPlaneIdentity _identity = new(
			"fake-datagram",
			1,
			unchecked((uint)Interlocked.Increment(ref _nextGeneration)));

		public event Action<NetworkDatagramDispatchEvidence>? DatagramDispatched;

		public int ExecutionCount { get; private set; }
		public NetworkDatagramDataPlaneCounters Counters => default;
		public NetworkDataPlaneDescriptor Descriptor => new(
			_identity,
			NetworkDataPlaneCapabilities.Datagram,
			NetworkDataPlaneState.Ready);

		public ValueTask<NetworkSocketExecutionResult> ExecuteDatagramAsync(
			NetworkSocketExecutionRequest request,
			CancellationToken cancellationToken = default)
		{
			ExecutionCount++;
			var flow = NetworkFlowSerialAllocator.GetNext();
			NetworkFlowSerialAllocator.TryTransition(flow, NetworkFlowLifecycleState.Active);
			var dispatch = new NetworkDatagramDispatchEvidence(
				request.Identity,
				flow,
				1,
				request.Resolved.Source,
				50000,
				request.Resolved.Destination,
				request.RemotePort,
				request.Resolved.Interface,
				DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
			DatagramDispatched?.Invoke(dispatch);
			var result = new NetworkSocketExecutionResult(
				request.Identity,
				ProtocolOutcome.Succeeded,
				AccessCompliance.Satisfied,
				default,
				default,
				string.Empty,
				[42],
				DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
				flow,
				1,
				dispatch);
			NetworkFlowSerialAllocator.BeginQuarantine(flow, TimeSpan.Zero);
			return ValueTask.FromResult(result);
		}

		public void Dispose()
		{
		}
	}
}

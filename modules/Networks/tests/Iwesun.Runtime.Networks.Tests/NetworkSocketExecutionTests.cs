using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Iwesun.Runtime.Networks;
using Xunit;

namespace Iwesun.Runtime.Networks.Tests;

public sealed class NetworkSocketExecutionTests
{
	[Fact]
	public async Task AutomaticTcpIpv4ReturnsObservedEndpoints()
	{
		using var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var port = (ushort)((IPEndPoint)listener.LocalEndpoint).Port;
		var environment = CreateEnvironment(IPAddress.Loopback, bindInterface: false);
		var accept = listener.AcceptSocketAsync();

		var result = await environment.Executor.ExecuteAsync(CreateRequest(environment, NetworkPathProvider.Automatic, port));
		using var accepted = await accept.WaitAsync(TimeSpan.FromSeconds(3));

		Assert.True(result.IsSuccess, result.ReasonCode);
		Assert.Equal(4, result.Evidence.Destination.Value.Family);
		Assert.Equal(4, result.Evidence.Source.Value.Family);
		Assert.Equal(ProofKind.Observed, result.Evidence.Destination.Proof);
		Assert.Equal(AccessCompliance.Satisfied, result.Compliance);
	}

	[Fact]
	public async Task DirectTcpIpv4BindsExactSourceAndInterface()
	{
		using var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var port = (ushort)((IPEndPoint)listener.LocalEndpoint).Port;
		var environment = CreateEnvironment(IPAddress.Loopback, bindInterface: true);
		var accept = listener.AcceptSocketAsync();

		var result = await environment.Executor.ExecuteAsync(CreateRequest(environment, NetworkPathProvider.Direct, port));
		using var accepted = await accept.WaitAsync(TimeSpan.FromSeconds(3));

		Assert.True(result.IsSuccess, result.ReasonCode);
		Assert.Equal(IpAddressValue.FromIPAddress(IPAddress.Loopback), result.Evidence.Source.Value);
		Assert.Equal(ProofKind.ApiBinding, result.Evidence.Interface.Proof);
		Assert.Equal(environment.Interface.InterfaceIndex, result.Evidence.Interface.Value.InterfaceIndex);
	}

	[Fact]
	public async Task DirectTcpIpv4CanBindOnlySource()
	{
		using var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var port = (ushort)((IPEndPoint)listener.LocalEndpoint).Port;
		var environment = CreateEnvironment(IPAddress.Loopback, bindInterface: false, bindSource: true);
		var accept = listener.AcceptSocketAsync();

		var result = await environment.Executor.ExecuteAsync(CreateRequest(environment, NetworkPathProvider.Direct, port));
		using var accepted = await accept.WaitAsync(TimeSpan.FromSeconds(3));

		Assert.True(result.IsSuccess, result.ReasonCode);
		Assert.Equal(IpAddressValue.FromIPAddress(IPAddress.Loopback), result.Evidence.Source.Value);
		Assert.Equal(ProofKind.Inferred, result.Evidence.Interface.Proof);
	}

	[Fact]
	public async Task DirectTcpIpv4CanBindOnlyInterface()
	{
		using var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var port = (ushort)((IPEndPoint)listener.LocalEndpoint).Port;
		var environment = CreateEnvironment(IPAddress.Loopback, bindInterface: true, bindSource: false);
		var accept = listener.AcceptSocketAsync();

		var result = await environment.Executor.ExecuteAsync(CreateRequest(environment, NetworkPathProvider.Direct, port));
		using var accepted = await accept.WaitAsync(TimeSpan.FromSeconds(3));

		Assert.True(result.IsSuccess, result.ReasonCode);
		Assert.Equal(ProofKind.ApiBinding, result.Evidence.Interface.Proof);
	}

	[Fact]
	public async Task AutomaticUdpIpv4ReturnsReplyAndObservedLocalPort()
	{
		using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
		var port = (ushort)((IPEndPoint)server.Client.LocalEndPoint!).Port;
		var environment = CreateEnvironment(IPAddress.Loopback, bindInterface: false);
		var serverTask = Task.Run(async () =>
		{
			var received = await server.ReceiveAsync();
			await server.SendAsync([7, 8, 9], 3, received.RemoteEndPoint);
		});
		var request = CreateRequest(environment, NetworkPathProvider.Automatic, port) with
		{
			Operation = NetworkSocketOperation.UdpExchange,
			Payload = [1, 2, 3],
			ReceiveBufferSize = 64,
		};

		var result = await environment.Executor.ExecuteAsync(request).AsTask().WaitAsync(TimeSpan.FromSeconds(3));
		await serverTask.WaitAsync(TimeSpan.FromSeconds(3));

		Assert.True(result.IsSuccess, result.ReasonCode);
		Assert.Equal([7, 8, 9], result.Payload);
		Assert.True(result.Evidence.LocalPort.Value > 0);
		Assert.Equal(ProofKind.Observed, result.Evidence.Interface.Proof);
	}

	[Fact]
	public async Task AutomaticUdpIgnoresDatagramFromUnexpectedRemoteEndpoint()
	{
		using var expectedServer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
		using var unexpectedServer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
		var expectedPort = (ushort)((IPEndPoint)expectedServer.Client.LocalEndPoint!).Port;
		var unexpectedPort = (ushort)((IPEndPoint)unexpectedServer.Client.LocalEndPoint!).Port;
		Assert.NotEqual(expectedPort, unexpectedPort);
		var environment = CreateEnvironment(IPAddress.Loopback, bindInterface: false);
		var serverTask = Task.Run(async () =>
		{
			var received = await expectedServer.ReceiveAsync();
			await unexpectedServer.SendAsync([1, 1, 1], 3, received.RemoteEndPoint);
			await Task.Delay(50);
			await expectedServer.SendAsync([7, 8, 9], 3, received.RemoteEndPoint);
		});
		var request = CreateRequest(environment, NetworkPathProvider.Automatic, expectedPort) with
		{
			Operation = NetworkSocketOperation.UdpExchange,
			Payload = [1, 2, 3],
			ReceiveBufferSize = 64,
		};

		var result = await environment.Executor.ExecuteAsync(request).AsTask().WaitAsync(TimeSpan.FromSeconds(3));
		await serverTask.WaitAsync(TimeSpan.FromSeconds(3));

		Assert.True(result.IsSuccess, result.ReasonCode);
		Assert.Equal([7, 8, 9], result.Payload);
		Assert.Equal(IpAddressValue.FromIPAddress(IPAddress.Loopback), result.Evidence.Destination.Value);
		Assert.True(NetworkSocketExecutor.IsExpectedUdpRemote(
			new IPEndPoint(IPAddress.Loopback, expectedPort),
			new IPEndPoint(IPAddress.Loopback, expectedPort)));
		Assert.False(NetworkSocketExecutor.IsExpectedUdpRemote(
			new IPEndPoint(IPAddress.Loopback, unexpectedPort),
			new IPEndPoint(IPAddress.Loopback, expectedPort)));
	}

	[Fact]
	public async Task AutomaticTcpIpv6WorksWhenThePlatformSupportsIt()
	{
		if (!Socket.OSSupportsIPv6) return;
		using var listener = new TcpListener(IPAddress.IPv6Loopback, 0);
		listener.Server.DualMode = false;
		listener.Start();
		var port = (ushort)((IPEndPoint)listener.LocalEndpoint).Port;
		var environment = CreateEnvironment(IPAddress.IPv6Loopback, bindInterface: false);
		var accept = listener.AcceptSocketAsync();

		var result = await environment.Executor.ExecuteAsync(CreateRequest(environment, NetworkPathProvider.Automatic, port));
		using var accepted = await accept.WaitAsync(TimeSpan.FromSeconds(3));
		Assert.True(result.IsSuccess, result.ReasonCode);
		Assert.Equal(6, result.Evidence.Source.Value.Family);
	}

	[Fact]
	public async Task DirectTcpIpv6BindsExactSourceAndInterfaceWhenSupported()
	{
		if (!Socket.OSSupportsIPv6) return;
		using var listener = new TcpListener(IPAddress.IPv6Loopback, 0);
		listener.Server.DualMode = false;
		listener.Start();
		var port = (ushort)((IPEndPoint)listener.LocalEndpoint).Port;
		var environment = CreateEnvironment(IPAddress.IPv6Loopback, bindInterface: true);
		var accept = listener.AcceptSocketAsync();

		var result = await environment.Executor.ExecuteAsync(CreateRequest(environment, NetworkPathProvider.Direct, port));
		using var accepted = await accept.WaitAsync(TimeSpan.FromSeconds(3));

		Assert.True(result.IsSuccess, result.ReasonCode);
		Assert.Equal(6, result.Evidence.Source.Value.Family);
		Assert.Equal(ProofKind.ApiBinding, result.Evidence.Interface.Proof);
	}

	[Fact]
	public async Task AutomaticUdpIpv6ReturnsReplyAndPacketEvidenceWhenSupported()
	{
		if (!Socket.OSSupportsIPv6) return;
		using var server = new UdpClient(AddressFamily.InterNetworkV6);
		server.Client.DualMode = false;
		server.Client.Bind(new IPEndPoint(IPAddress.IPv6Loopback, 0));
		var port = (ushort)((IPEndPoint)server.Client.LocalEndPoint!).Port;
		var environment = CreateEnvironment(IPAddress.IPv6Loopback, bindInterface: false);
		var serverTask = Task.Run(async () =>
		{
			var received = await server.ReceiveAsync();
			await server.SendAsync([4, 5, 6], 3, received.RemoteEndPoint);
		});
		var request = CreateRequest(environment, NetworkPathProvider.Automatic, port) with
		{
			Operation = NetworkSocketOperation.UdpExchange,
			Payload = [1],
			ReceiveBufferSize = 64,
		};

		var result = await environment.Executor.ExecuteAsync(request).AsTask().WaitAsync(TimeSpan.FromSeconds(3));
		await serverTask.WaitAsync(TimeSpan.FromSeconds(3));

		Assert.True(result.IsSuccess, result.ReasonCode);
		Assert.Equal([4, 5, 6], result.Payload);
		Assert.Equal(6, result.Evidence.Source.Value.Family);
		Assert.Equal(ProofKind.Observed, result.Evidence.Interface.Proof);
	}

	[Fact]
	public async Task ExactNextHopIsRejectedWithoutWfpBeforeSocketExecution()
	{
		var environment = CreateEnvironment(IPAddress.Loopback, bindInterface: true, exactNextHop: true);
		var executor = new NetworkSocketExecutor(
			environment.Resolver,
			new UnsupportedNetworkWfpConnectionPolicyBackend());
		var result = await executor.ExecuteAsync(CreateRequest(environment, NetworkPathProvider.Direct, 9));

		Assert.False(result.IsSuccess);
		Assert.Equal(ProtocolOutcome.Rejected, result.Outcome);
		Assert.Equal(NetworkAccessFailureCodes.ExactNextHopBackendUnavailable, result.ReasonCode);
	}

	[Fact]
	public async Task ExactNextHopUsesIsolatedPolicyAndReturnsEnforcementEvidence()
	{
		using var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var port = (ushort)((IPEndPoint)listener.LocalEndpoint).Port;
		var environment = CreateEnvironment(IPAddress.Loopback, bindInterface: true, exactNextHop: true);
		var backend = new RecordingWfpBackend();
		var executor = new NetworkSocketExecutor(
			environment.Resolver,
			new IsolatedNetworkWfpConnectionPolicyBackend(backend));
		var accept = listener.AcceptSocketAsync();

		var result = await executor.ExecuteAsync(CreateRequest(environment, NetworkPathProvider.Direct, port));
		using var accepted = await accept.WaitAsync(TimeSpan.FromSeconds(3));

		Assert.True(result.IsSuccess, result.ReasonCode);
		Assert.Equal(ProofKind.PolicyEnforced, result.Evidence.NextHop.Proof);
		Assert.Equal(ProofKind.PolicyEnforced, result.Evidence.Interface.Proof);
		Assert.Equal(AccessCompliance.Satisfied, result.Compliance);
		Assert.Single(backend.Acquired);
		Assert.Equal(port, backend.Acquired[0].RemotePort);
		Assert.True(backend.Acquired[0].LocalPort > 0);
		Assert.Equal(1, backend.DisposeCount);
	}

	[Fact]
	public void WfpIsolationRejectsAnIdenticalLiveConnectionKeyAndReleasesItOnDispose()
	{
		var inner = new RecordingWfpBackend();
		var backend = new IsolatedNetworkWfpConnectionPolicyBackend(inner);
		var request = CreatePolicyRequest(Guid.NewGuid());

		var first = backend.Acquire(request);
		var collision = backend.Acquire(request with { PolicyId = Guid.NewGuid() });

		Assert.True(first.Succeeded);
		Assert.False(collision.Succeeded);
		Assert.Equal(NetworkAccessFailureCodes.WfpPolicyIsolationCollision, collision.ReasonCode);
		Assert.Equal(1, backend.ActiveLeaseCount);

		first.Lease!.Dispose();
		var afterCleanup = backend.Acquire(request with { PolicyId = Guid.NewGuid() });
		Assert.True(afterCleanup.Succeeded);
		afterCleanup.Lease!.Dispose();
		Assert.Equal(0, backend.ActiveLeaseCount);
	}

	[Fact]
	public void WfpIsolationReleasesTheMatchKeyWhenTheNativeAcquireThrows()
	{
		var backend = new IsolatedNetworkWfpConnectionPolicyBackend(new ThrowingWfpBackend());
		var request = CreatePolicyRequest(Guid.NewGuid());

		var result = backend.Acquire(request);

		Assert.False(result.Succeeded);
		Assert.Equal(NetworkAccessFailureCodes.WfpPolicyAddFailed, result.ReasonCode);
		Assert.Equal(0, backend.ActiveLeaseCount);
	}

	[Fact]
	public void SameTargetDifferentGatewaysRemainIsolatedByTheirBoundLocalPorts()
	{
		var inner = new RecordingWfpBackend();
		var backend = new IsolatedNetworkWfpConnectionPolicyBackend(inner);
		var firstRequest = CreatePolicyRequest(Guid.NewGuid()) with
		{
			NextHop = IpAddressValue.Parse("192.0.2.1"),
			LocalPort = 51001,
		};
		var secondRequest = CreatePolicyRequest(Guid.NewGuid()) with
		{
			NextHop = IpAddressValue.Parse("192.0.2.2"),
			LocalPort = 51002,
			Destination = firstRequest.Destination,
			RemotePort = firstRequest.RemotePort,
			ApplicationPath = firstRequest.ApplicationPath,
		};

		var first = backend.Acquire(firstRequest);
		var second = backend.Acquire(secondRequest);

		Assert.True(first.Succeeded);
		Assert.True(second.Succeeded);
		Assert.Equal(2, backend.ActiveLeaseCount);
		Assert.NotEqual(first.Lease!.Request.NextHop, second.Lease!.Request.NextHop);
		first.Lease.Dispose();
		second.Lease.Dispose();
		Assert.Equal(0, backend.ActiveLeaseCount);
	}

	[Fact]
	public void WfpIsolationReleasesTheMatchKeyEvenWhenNativeCleanupThrows()
	{
		var backend = new IsolatedNetworkWfpConnectionPolicyBackend(new ThrowingCleanupWfpBackend());
		var acquired = backend.Acquire(CreatePolicyRequest(Guid.NewGuid()));

		Assert.True(acquired.Succeeded);
		Assert.Throws<NetworkWfpPolicyCleanupException>(() => acquired.Lease!.Dispose());
		Assert.Equal(0, backend.ActiveLeaseCount);
	}

	[Fact]
	public async Task ExactNextHopCancellationDisposesThePolicyLease()
	{
		using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
		var port = (ushort)((IPEndPoint)server.Client.LocalEndPoint!).Port;
		var environment = CreateEnvironment(IPAddress.Loopback, bindInterface: true, exactNextHop: true);
		var backend = new RecordingWfpBackend();
		var executor = new NetworkSocketExecutor(
			environment.Resolver,
			new IsolatedNetworkWfpConnectionPolicyBackend(backend));
		using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
		var request = CreateRequest(environment, NetworkPathProvider.Direct, port) with
		{
			Operation = NetworkSocketOperation.UdpExchange,
			Payload = [1],
			ReceiveBufferSize = 16,
		};

		var result = await executor.ExecuteAsync(request, cancellation.Token);

		Assert.Equal(ProtocolOutcome.Cancelled, result.Outcome);
		Assert.Equal(1, backend.DisposeCount);
	}

	[Fact]
	public async Task ExactRouteCompartmentIsRejectedBeforeSocketExecution()
	{
		var environment = CreateEnvironment(IPAddress.Loopback, bindInterface: false);
		var requested = environment.Requested with
		{
			RouteScope = NetworkRouteScopeSelection.Exact(7),
		};
		Assert.True(
			environment.Resolver.TryResolve(
				requested,
				NetworkAccessSecurityBoundary.NotApplicable(),
				out var resolved,
				out var reason),
			reason);
		var request = CreateRequest(environment, NetworkPathProvider.Direct, 9) with
		{
			Requested = requested,
			Resolved = resolved,
		};

		var result = await environment.Executor.ExecuteAsync(request);

		Assert.Equal(ProtocolOutcome.Rejected, result.Outcome);
		Assert.Equal(NetworkAccessFailureCodes.RouteScopeBackendUnavailable, result.ReasonCode);
	}

	[Fact]
	public async Task StaleResolutionIsRejectedBeforeSocketExecution()
	{
		var environment = CreateEnvironment(IPAddress.Loopback, bindInterface: false);
		var request = CreateRequest(environment, NetworkPathProvider.Automatic, 9);
		for (var index = 0; index < 4096; index++)
			Assert.True(environment.Routes.TryReplace(
				environment.Address,
				environment.Address,
				environment.Interface,
				default,
				0,
				out _,
				out _));
		var result = await environment.Executor.ExecuteAsync(request);

		Assert.Equal(ProtocolOutcome.Rejected, result.Outcome);
		Assert.Equal(NetworkAccessFailureCodes.ResolvedPlanStale, result.ReasonCode);
	}

	[Fact]
	public async Task RequestedAndResolvedPlansMustHaveTheSameStableSemantics()
	{
		var environment = CreateEnvironment(IPAddress.Loopback, bindInterface: false);
		var request = CreateRequest(environment, NetworkPathProvider.Automatic, 9) with
		{
			SecurityBoundary = new NetworkAccessSecurityBoundary(
				NetworkAccessLeg.Direct,
				"example.invalid",
				string.Empty,
				string.Empty,
				string.Empty),
		};

		var result = await environment.Executor.ExecuteAsync(request);

		Assert.Equal(ProtocolOutcome.Rejected, result.Outcome);
		Assert.Equal(NetworkAccessFailureCodes.ResolvedPlanMismatch, result.ReasonCode);
	}

	[Fact]
	public async Task PartialExactInterfaceIdentityMatchesItsNormalizedResolvedPlan()
	{
		var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		try
		{
			var environment = CreateEnvironment(IPAddress.Loopback, bindInterface: true);
			var requested = environment.Requested with
			{
				Interface = NetworkInterfaceSelection.Exact(new NetworkInterfaceIdentity(
					Guid.Empty,
					0,
					string.Empty,
					environment.Interface.InterfaceIndex)),
			};
			Assert.True(environment.Resolver.TryResolve(
				requested,
				NetworkAccessSecurityBoundary.NotApplicable(),
				out var resolved,
				out var reason), reason);
			var request = new NetworkSocketExecutionRequest(
				NetworkExecutionIdentity.ForRequest(Guid.NewGuid()).StartAttempt(Guid.NewGuid()).StartBranch(Guid.NewGuid()),
				requested,
				resolved,
				NetworkAccessSecurityBoundary.NotApplicable(),
				NetworkSocketOperation.TcpConnect,
				checked((ushort)((IPEndPoint)listener.LocalEndpoint).Port),
				[],
				0);

			var result = await environment.Executor.ExecuteAsync(request);

			Assert.Equal(ProtocolOutcome.Succeeded, result.Outcome);
			Assert.Equal(AccessCompliance.Satisfied, result.Compliance);
		}
		finally
		{
			listener.Stop();
		}
	}

	[Fact]
	public async Task AutomaticTcpTransportFailurePreservesBranchIdentityAndError()
	{
		var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var port = (ushort)((IPEndPoint)listener.LocalEndpoint).Port;
		listener.Stop();
		var environment = CreateEnvironment(IPAddress.Loopback, bindInterface: false);
		var request = CreateRequest(environment, NetworkPathProvider.Automatic, port);

		var result = await environment.Executor.ExecuteAsync(request);

		Assert.Equal(ProtocolOutcome.TransportFailed, result.Outcome);
		Assert.True(result.Identity.HasBranch);
		Assert.NotEqual(0, result.Error.Kind);
		Assert.Equal(NetworkAccessFailureCodes.SocketOperationFailed, result.ReasonCode);
	}

	[Fact]
	public async Task DatagramDataPlaneRejectsStaleResolvedPlanBeforeAllocatingFlow()
	{
		var environment = CreateEnvironment(IPAddress.Loopback, bindInterface: false);
		var request = CreateRequest(environment, NetworkPathProvider.Automatic, 53) with
		{
			Operation = NetworkSocketOperation.UdpExchange,
			Payload = [1],
			ReceiveBufferSize = 64,
		};
		for (var index = 0; index < 4096; index++)
			Assert.True(environment.Routes.TryReplace(
				environment.Address,
				environment.Address,
				environment.Interface,
				default,
				0,
				out _,
				out _));
		using var dataPlane = new SystemSocketDatagramDataPlane(environment.Executor);

		var result = await dataPlane.ExecuteDatagramAsync(request);

		Assert.Equal(ProtocolOutcome.Rejected, result.Outcome);
		Assert.Equal(NetworkAccessFailureCodes.ResolvedPlanStale, result.ReasonCode);
		Assert.False(result.FlowSerial.IsValid);
	}

	[Fact]
	public async Task DatagramDataPlaneRejectsFlowBeyondPoolCapacity()
	{
		using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
		var port = checked((ushort)((IPEndPoint)server.Client.LocalEndPoint!).Port);
		var environment = CreateEnvironment(IPAddress.Loopback, bindInterface: false);
		var request = CreateRequest(environment, NetworkPathProvider.Automatic, port) with
		{
			Operation = NetworkSocketOperation.UdpExchange,
			Payload = [1],
			ReceiveBufferSize = 64,
		};
		using var dataPlane = new SystemSocketDatagramDataPlane(
			environment.Executor,
			lateResponseQuarantine: TimeSpan.FromSeconds(1),
			maxSocketsPerPool: 1);
		using var firstCancellation = new CancellationTokenSource();
		var first = dataPlane.ExecuteDatagramAsync(request, firstCancellation.Token).AsTask();
		_ = await server.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(3));
		var secondRequest = request with
		{
			Identity = NetworkExecutionIdentity.ForRequest(Guid.NewGuid())
				.StartAttempt(Guid.NewGuid())
				.StartBranch(Guid.NewGuid()),
		};

		var rejected = await dataPlane.ExecuteDatagramAsync(secondRequest);

		Assert.Equal(ProtocolOutcome.Rejected, rejected.Outcome);
		Assert.Equal(NetworkAccessFailureCodes.NetworkFlowCapacityReached, rejected.ReasonCode);
		Assert.Equal(1, dataPlane.Counters.CapacityRejections);
		Assert.Equal(1, dataPlane.Counters.PeakSlotsPerPool);
		Assert.Equal(1, dataPlane.Counters.ActiveSlotCount);
		firstCancellation.Cancel();
		_ = await first;
	}

	[Fact]
	public async Task DatagramDataPlaneEvictsAvailablePoolBeforeRejectingNewKey()
	{
		using var firstServer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
		using var secondServer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
		var firstPort = checked((ushort)((IPEndPoint)firstServer.Client.LocalEndPoint!).Port);
		var secondPort = checked((ushort)((IPEndPoint)secondServer.Client.LocalEndPoint!).Port);
		var environment = CreateEnvironment(IPAddress.Loopback, bindInterface: false);
		var request = CreateRequest(environment, NetworkPathProvider.Automatic, firstPort) with
		{
			Operation = NetworkSocketOperation.UdpExchange,
			Payload = [1],
			ReceiveBufferSize = 64,
		};
		using var dataPlane = new SystemSocketDatagramDataPlane(
			environment.Executor,
			lateResponseQuarantine: TimeSpan.Zero,
			maxSocketsPerPool: 1,
			maxPoolCount: 1,
			idlePoolRetention: TimeSpan.FromHours(1));

		var firstEcho = EchoOnceAsync(firstServer);
		var first = await dataPlane.ExecuteDatagramAsync(request);
		await firstEcho;
		Assert.Equal(ProtocolOutcome.Succeeded, first.Outcome);

		var secondRequest = request with
		{
			Identity = NetworkExecutionIdentity.ForRequest(Guid.NewGuid())
				.StartAttempt(Guid.NewGuid())
				.StartBranch(Guid.NewGuid()),
			RemotePort = secondPort,
		};
		var secondEcho = EchoOnceAsync(secondServer);
		var second = await dataPlane.ExecuteDatagramAsync(secondRequest);
		await secondEcho;

		Assert.Equal(ProtocolOutcome.Succeeded, second.Outcome);
		Assert.Equal(1, dataPlane.Counters.PoolCount);
		Assert.Equal(0, dataPlane.Counters.CapacityRejections);
	}

	[Fact]
	public void WindowsGetBestRoute2RefreshesAutomaticIpv4Snapshot()
	{
		if (!OperatingSystem.IsWindows()) return;
		var interfaces = new NetworkInterfaceSnapshotProvider();
		var routes = new NetworkRouteSnapshotProvider();
		var provider = new WindowsNetworkRouteSnapshotProvider(interfaces, routes);
		var requested = CreateAutomaticPlan(IpAddressValue.FromIPAddress(IPAddress.Loopback));

		var success = provider.TryRefresh(requested, out var snapshot, out var platformError, out var reason);

		Assert.True(success, $"{reason}:{platformError.NativeCode}");
		Assert.Equal(4, snapshot.Source.Family);
		Assert.True(snapshot.Interface.InterfaceIndex > 0);
		Assert.True(interfaces.GetSnapshot().Count > 0);
	}

	[Fact]
	public void WindowsGetBestRoute2RefreshesAutomaticIpv6Snapshot()
	{
		if (!OperatingSystem.IsWindows() || !Socket.OSSupportsIPv6) return;
		var interfaces = new NetworkInterfaceSnapshotProvider();
		var routes = new NetworkRouteSnapshotProvider();
		var provider = new WindowsNetworkRouteSnapshotProvider(interfaces, routes);
		var requested = CreateAutomaticPlan(IpAddressValue.FromIPAddress(IPAddress.IPv6Loopback));

		var success = provider.TryRefresh(requested, out var snapshot, out var platformError, out var reason);

		Assert.True(success, $"{reason}:{platformError.NativeCode}");
		Assert.Equal(6, snapshot.Source.Family);
		Assert.True(snapshot.Interface.InterfaceIndex > 0);
	}

	private static NetworkSocketExecutionRequest CreateRequest(
		TestEnvironment environment,
		NetworkPathProvider provider,
		ushort port)
	{
		var requested = environment.Requested with { PathProvider = provider };
		Assert.True(
			environment.Resolver.TryResolve(
				requested,
				NetworkAccessSecurityBoundary.NotApplicable(),
				out var resolved,
				out var reason),
			reason);
		return new NetworkSocketExecutionRequest(
			NetworkExecutionIdentity.ForRequest(Guid.NewGuid()).StartAttempt(Guid.NewGuid()).StartBranch(Guid.NewGuid()),
			requested,
			resolved,
			NetworkAccessSecurityBoundary.NotApplicable(),
			NetworkSocketOperation.TcpConnect,
			port,
			[],
			0);
	}

	private static TestEnvironment CreateEnvironment(
		IPAddress address,
		bool bindInterface,
		bool? bindSource = null,
		bool exactNextHop = false)
	{
		var shouldBindSource = bindSource ?? bindInterface;
		var binary = IpAddressValue.FromIPAddress(address);
		var index = bindInterface ? FindInterfaceIndex(address) : 0U;
		var identity = new NetworkInterfaceIdentity(Guid.NewGuid(), index, "loopback-test", index);
		Assert.True(NetworkAddressSet.TryCreate([binary], out var addresses, out _));
		var interfaces = new NetworkInterfaceSnapshotProvider();
		Assert.True(interfaces.TryReplace([new NetworkInterfaceSnapshot(identity, addresses, true)], out _, out _));
		var routes = new NetworkRouteSnapshotProvider();
		var nextHop = exactNextHop ? binary : default;
		Assert.True(routes.TryReplace(binary, binary, identity, nextHop, 0, out _, out _));
		var requested = new RequestedAccessPlan(
			NetworkPathProvider.Direct,
			NetworkDestinationSelection.Exact(binary),
			shouldBindSource ? NetworkSourceSelection.Exact(binary) : NetworkSourceSelection.SystemSelected(),
			bindInterface ? NetworkInterfaceSelection.Exact(identity) : NetworkInterfaceSelection.SystemSelected(),
			exactNextHop ? NetworkNextHopSelection.Exact(binary) : NetworkNextHopSelection.SystemSelected(),
			NetworkLocalEndpointSelection.Ephemeral(),
			NetworkRouteScopeSelection.Current(),
			NetworkIpPacketPolicy.SystemDefault(),
			NetworkRouteAdapterIdentity.NotApplicable());
		var resolver = new NetworkAccessConstraintResolver(
			new NetworkAccessPlanNormalizer(new NetworkInterfaceIdentityResolver()),
			interfaces,
			routes);
		Assert.True(resolver.TryResolve(requested, NetworkAccessSecurityBoundary.NotApplicable(), out var resolved, out var reason), reason);
		return new TestEnvironment(binary, identity, requested, resolved, routes, resolver, new NetworkSocketExecutor(resolver));
	}

	private static RequestedAccessPlan CreateAutomaticPlan(IpAddressValue destination) => new(
		NetworkPathProvider.Automatic,
		NetworkDestinationSelection.Exact(destination),
		NetworkSourceSelection.SystemSelected(),
		NetworkInterfaceSelection.SystemSelected(),
		NetworkNextHopSelection.SystemSelected(),
		NetworkLocalEndpointSelection.Ephemeral(),
		NetworkRouteScopeSelection.Current(),
		NetworkIpPacketPolicy.SystemDefault(),
		NetworkRouteAdapterIdentity.NotApplicable());

	private static async Task EchoOnceAsync(UdpClient server)
	{
		var received = await server.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(3));
		await server.SendAsync(received.Buffer, received.RemoteEndPoint);
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

	private static NetworkWfpConnectionPolicyRequest CreatePolicyRequest(Guid policyId)
	{
		var address = IpAddressValue.FromIPAddress(IPAddress.Loopback);
		return new NetworkWfpConnectionPolicyRequest(
			policyId,
			NetworkExecutionIdentity.ForRequest(Guid.NewGuid()).StartAttempt(Guid.NewGuid()).StartBranch(Guid.NewGuid()),
			address,
			new NetworkInterfaceIdentity(Guid.NewGuid(), 1, "test", 1),
			address,
			51000,
			address,
			443,
			ProtocolType.Tcp,
			@"C:\test\host.exe");
	}

	private sealed class RecordingWfpBackend : INetworkWfpConnectionPolicyBackend
	{
		public List<NetworkWfpConnectionPolicyRequest> Acquired { get; } = [];
		public int DisposeCount { get; private set; }

		public NetworkWfpPolicyCapability QueryCapability() => new(true, false, string.Empty, default);

		public NetworkWfpPolicyAcquireResult Acquire(in NetworkWfpConnectionPolicyRequest request)
		{
			Acquired.Add(request);
			return new(new Lease(request, () => DisposeCount++), string.Empty, default);
		}

		private sealed class Lease(NetworkWfpConnectionPolicyRequest request, Action dispose)
			: INetworkWfpConnectionPolicyLease
		{
			private int _disposed;
			public Guid PolicyId => request.PolicyId;
			public NetworkWfpConnectionPolicyRequest Request => request;
			public void Dispose()
			{
				if (Interlocked.Exchange(ref _disposed, 1) == 0) dispose();
			}
		}
	}

	private sealed class ThrowingWfpBackend : INetworkWfpConnectionPolicyBackend
	{
		public NetworkWfpPolicyCapability QueryCapability() => new(true, false, string.Empty, default);
		public NetworkWfpPolicyAcquireResult Acquire(in NetworkWfpConnectionPolicyRequest request) =>
			throw new InvalidOperationException("simulated-native-add-failure");
	}

	private sealed class ThrowingCleanupWfpBackend : INetworkWfpConnectionPolicyBackend
	{
		public NetworkWfpPolicyCapability QueryCapability() => new(true, false, string.Empty, default);
		public NetworkWfpPolicyAcquireResult Acquire(in NetworkWfpConnectionPolicyRequest request) =>
			new(new Lease(request), string.Empty, default);

		private sealed class Lease(NetworkWfpConnectionPolicyRequest request) : INetworkWfpConnectionPolicyLease
		{
			public Guid PolicyId => request.PolicyId;
			public NetworkWfpConnectionPolicyRequest Request => request;
			public void Dispose() => throw new NetworkWfpPolicyCleanupException(
				new NetworkPlatformError("test", 1, 0, "cleanup"));
		}
	}

	private readonly record struct TestEnvironment(
		IpAddressValue Address,
		NetworkInterfaceIdentity Interface,
		RequestedAccessPlan Requested,
		ResolvedAccessPlan Resolved,
		NetworkRouteSnapshotProvider Routes,
		NetworkAccessConstraintResolver Resolver,
		NetworkSocketExecutor Executor);
}

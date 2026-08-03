using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Iwesun.Runtime.Networks;
using Xunit;

namespace Iwesun.Runtime.Networks.Tests;

public sealed class NetworkNetBiosNameEndpointTests
{
	[Fact]
	public async Task UnicastNodeStatusPublishesRecordsAndObservedPath()
	{
		using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
		var port = checked((ushort)((IPEndPoint)server.Client.LocalEndPoint!).Port);
		var environment = CreateEnvironment();
		using var endpoint = new NetworkNetBiosNameEndpoint<string>(environment.Context, maxAttemptCount: 1);
		var serverTask = Task.Run(async () =>
		{
			var received = await server.ReceiveAsync();
			var response = BuildResponse(received.Buffer);
			await server.SendAsync(response, response.Length, received.RemoteEndPoint);
		});
		var request = new NetworkNetBiosNameRequest<string>(
			Guid.NewGuid(), "nbns", environment.Plan, NetworkAccessSecurityBoundary.NotApplicable(), port, 1000, false);

		Assert.True(endpoint.TrySend(request));
		await serverTask.WaitAsync(TimeSpan.FromSeconds(3));
		await WaitUntilAsync(() => endpoint.ReceiveQueueLength == 1);
		Assert.True(endpoint.TryReadReceived(out var completion));

		Assert.Equal("TESTHOST", completion.Response.PrimaryName);
		Assert.Single(completion.Response.Records);
		Assert.Equal((byte)0x20, completion.Response.Records[0].Suffix);
		Assert.Equal(ProofKind.Observed, completion.Response.Execution.Evidence.Interface.Proof);
		Assert.Equal(completion.Response.Identity.BranchId, completion.Response.Execution.Identity.BranchId);
	}

	[Fact]
	public async Task BroadcastPolicyPublishesMultipleNodeStatusResponsesWithinOneWindow()
	{
		using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
		var port = checked((ushort)((IPEndPoint)server.Client.LocalEndPoint!).Port);
		var backend = new RecordingWfpBackend();
		var environment = CreateEnvironment(exactNextHop: true, backend);
		var broadcastPlan = environment.Plan with
		{
			IpPacketPolicy = NetworkIpPacketPolicy.Explicit(default, false, true, false),
		};
		using var endpoint = new NetworkNetBiosNameEndpoint<string>(environment.Context, maxAttemptCount: 1);
		var observations = new ConcurrentQueue<TrackedResponseObservedEventArgs<NetworkNetBiosNameResponse<string>, string>>();
		endpoint.ResponseObserved += (_, args) => observations.Enqueue(args);
		var serverTask = Task.Run(async () =>
		{
			var received = await server.ReceiveAsync();
			foreach (var name in new[] { "HOSTONE", "HOSTTWO" })
			{
				var response = BuildResponse(received.Buffer, name);
				await server.SendAsync(response, response.Length, received.RemoteEndPoint);
			}
		});
		var request = new NetworkNetBiosNameRequest<string>(
			Guid.NewGuid(), "nbns-broadcast", broadcastPlan, NetworkAccessSecurityBoundary.NotApplicable(),
			port, 1000, false, TrackedResponseCollectionPolicy.CollectUntilWindowEnds(150, 4));

		Assert.True(endpoint.TrySend(request));
		await serverTask.WaitAsync(TimeSpan.FromSeconds(3));
		await WaitUntilAsync(() => observations.Count == 2);
		Assert.Equal(0, endpoint.ReceiveQueueLength);
		await WaitUntilAsync(() => endpoint.ReceiveQueueLength == 1);
		Assert.True(endpoint.TryReadReceived(out var completion));

		var observed = observations.ToArray();
		Assert.Equal(
			new[] { "HOSTONE", "HOSTTWO" },
			observed.Select(item => item.Response.PrimaryName).OrderBy(static name => name));
		Assert.All(observed, item => Assert.True(item.Identity.HasResponse));
		Assert.NotEqual(observed[0].Identity.ResponseId, observed[1].Identity.ResponseId);
		Assert.Contains(completion.Response.PrimaryName, new[] { "HOSTONE", "HOSTTWO" });
		Assert.All(observed, item => Assert.Equal(
			ProofKind.PolicyEnforced, item.Response.Execution.Evidence.NextHop.Proof));
		Assert.Single(backend.Acquired);
		Assert.Equal(1, backend.DisposeCount);
	}

	private static TestEnvironment CreateEnvironment(
		bool exactNextHop = false,
		INetworkWfpConnectionPolicyBackend? wfpBackend = null)
	{
		var address = IpAddressValue.FromIPAddress(IPAddress.Loopback);
		var index = FindInterfaceIndex();
		var identity = new NetworkInterfaceIdentity(Guid.NewGuid(), index, "nbns-test", index);
		var interfaces = new NetworkInterfaceSnapshotProvider();
		Assert.True(interfaces.TryReplace([new NetworkInterfaceSnapshot(identity, default, true)], out _, out _));
		var routes = new NetworkRouteSnapshotProvider();
		Assert.True(routes.TryReplace(address, address, identity, exactNextHop ? address : default, 0, out _, out _));
		var context = new NetworkAccessExecutionContext(
			interfaces, routes, refreshWindowsSnapshots: false, wfpBackend);
		var plan = new RequestedAccessPlan(
			exactNextHop ? NetworkPathProvider.Direct : NetworkPathProvider.Automatic,
			NetworkDestinationSelection.Exact(address),
			NetworkSourceSelection.SystemSelected(),
			NetworkInterfaceSelection.SystemSelected(),
			exactNextHop ? NetworkNextHopSelection.Exact(address) : NetworkNextHopSelection.SystemSelected(),
			NetworkLocalEndpointSelection.Ephemeral(),
			NetworkRouteScopeSelection.Current(),
			NetworkIpPacketPolicy.SystemDefault(),
			NetworkRouteAdapterIdentity.NotApplicable());
		return new TestEnvironment(context, plan);
	}

	private static byte[] BuildResponse(byte[] query, string hostName = "TESTHOST")
	{
		var headerAndQuestion = query.ToArray();
		BinaryPrimitives.WriteUInt16BigEndian(headerAndQuestion.AsSpan(2), 0x8500);
		BinaryPrimitives.WriteUInt16BigEndian(headerAndQuestion.AsSpan(6), 1);
		var response = new List<byte>(headerAndQuestion);
		response.AddRange([0xC0, 0x0C, 0x00, 0x21, 0x00, 0x01, 0, 0, 0, 60, 0, 19, 1]);
		var name = hostName.PadRight(15, ' ');
		response.AddRange(System.Text.Encoding.ASCII.GetBytes(name));
		response.Add(0x20);
		response.Add(0);
		response.Add(0);
		return [.. response];
	}

	private static uint FindInterfaceIndex()
	{
		foreach (var item in NetworkInterface.GetAllNetworkInterfaces())
		{
			var properties = item.GetIPProperties();
			if (!properties.UnicastAddresses.Any(entry => entry.Address.Equals(IPAddress.Loopback))) continue;
			var index = properties.GetIPv4Properties()?.Index;
			if (index is > 0) return checked((uint)index.Value);
		}
		throw new InvalidOperationException("loopback-interface-not-found");
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

	private readonly record struct TestEnvironment(NetworkAccessExecutionContext Context, RequestedAccessPlan Plan);

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

		private sealed class Lease(NetworkWfpConnectionPolicyRequest request, Action disposed)
			: INetworkWfpConnectionPolicyLease
		{
			private int _disposed;
			public Guid PolicyId => request.PolicyId;
			public NetworkWfpConnectionPolicyRequest Request => request;
			public void Dispose()
			{
				if (Interlocked.Exchange(ref _disposed, 1) == 0) disposed();
			}
		}
	}
}

using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Iwesun.Runtime.Networks;
using Xunit;

namespace Iwesun.Runtime.Networks.Tests;

public sealed class NetworkPingEndpointTests
{
	[Theory]
	[InlineData("127.0.0.1")]
	[InlineData("::1")]
	public async Task AutomaticPingCompletesWithFourLevelIdentity(string addressText)
	{
		var address = IPAddress.Parse(addressText);
		if (address.AddressFamily == AddressFamily.InterNetworkV6 && !Socket.OSSupportsIPv6) return;
		var environment = CreateEnvironment(address, exactSource: false, exactInterface: false);
		using var endpoint = new NetworkPingEndpoint<string>(environment.Context, maxAttemptCount: 1);
		var request = new NetworkPingRequest<string>(
			Guid.NewGuid(), addressText, environment.Plan, NetworkAccessSecurityBoundary.NotApplicable(), [1, 2, 3], 1000, false);

		Assert.True(endpoint.TrySend(request));
		await WaitUntilAsync(() => endpoint.ReceiveQueueLength == 1);
		Assert.True(endpoint.TryReadReceived(out var completion));

		Assert.Equal(ProtocolOutcome.Succeeded, completion.Response.Result.Outcome);
		Assert.Equal(AccessCompliance.Satisfied, completion.Response.Result.Compliance);
		Assert.True(completion.Response.Identity.HasResponse);
		Assert.Equal(completion.Response.Identity, completion.Identity);
		Assert.Equal(ProofKind.ApiBinding, completion.Response.Result.Evidence.Destination.Proof);
		Assert.Equal(IpAddressValue.FromIPAddress(address), completion.Response.Result.Evidence.Destination.Value);
		Assert.Equal(IpAddressValue.FromIPAddress(address), completion.Response.Result.ResponderAddress);
	}

	[Theory]
	[InlineData("127.0.0.1")]
	[InlineData("::1")]
	public async Task DirectExactSourceUsesNativeSourceBinding(string addressText)
	{
		if (!OperatingSystem.IsWindows()) return;
		var address = IPAddress.Parse(addressText);
		if (address.AddressFamily == AddressFamily.InterNetworkV6 && !Socket.OSSupportsIPv6) return;
		var environment = CreateEnvironment(address, exactSource: true, exactInterface: false);
		using var endpoint = new NetworkPingEndpoint<int>(environment.Context, maxAttemptCount: 1);
		var request = new NetworkPingRequest<int>(
			Guid.NewGuid(), 7, environment.Plan, NetworkAccessSecurityBoundary.NotApplicable(), [], 1000, false);

		Assert.True(endpoint.TrySend(request));
		await WaitUntilAsync(() => endpoint.ReceiveQueueLength == 1);
		Assert.True(endpoint.TryReadReceived(out var completion));

		Assert.Equal(ProtocolOutcome.Succeeded, completion.Response.Result.Outcome);
		Assert.Equal(AccessCompliance.Satisfied, completion.Response.Result.Compliance);
		Assert.Equal(ProofKind.ApiBinding, completion.Response.Result.Evidence.Source.Proof);
		Assert.Equal(IpAddressValue.FromIPAddress(address), completion.Response.Result.Evidence.Source.Value);
	}

	[Fact]
	public async Task DirectExactInterfacePublishesProtocolSuccessButFailsComplianceWithoutProof()
	{
		if (!OperatingSystem.IsWindows()) return;
		var environment = CreateEnvironment(IPAddress.Loopback, exactSource: false, exactInterface: true);
		using var endpoint = new NetworkPingEndpoint<int>(environment.Context, maxAttemptCount: 1);
		var observed = new TaskCompletionSource<NetworkPingResponse<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
		var failed = new TaskCompletionSource<TrackedRequestFailure<NetworkPingRequest<int>, int>>(TaskCreationOptions.RunContinuationsAsynchronously);
		var branchTerminal = new TaskCompletionSource<NetworkBranchTerminal>(TaskCreationOptions.RunContinuationsAsynchronously);
		var attemptTerminal = new TaskCompletionSource<NetworkAttemptTerminal>(TaskCreationOptions.RunContinuationsAsynchronously);
		var requestTerminal = new TaskCompletionSource<NetworkRequestTerminal>(TaskCreationOptions.RunContinuationsAsynchronously);
		endpoint.ResponseObserved += (_, args) => observed.TrySetResult(args.Response);
		endpoint.RequestFailed += (_, args) => failed.TrySetResult(args.Failure);
		endpoint.BranchTerminated += (_, args) => branchTerminal.TrySetResult(args.Terminal);
		endpoint.AttemptTerminated += (_, args) => attemptTerminal.TrySetResult(args.Terminal);
		endpoint.RequestTerminated += (_, args) => requestTerminal.TrySetResult(args.Terminal);
		var request = new NetworkPingRequest<int>(
			Guid.NewGuid(), 9, environment.Plan, NetworkAccessSecurityBoundary.NotApplicable(), [], 1000, false);

		Assert.True(endpoint.TrySend(request));
		var response = await observed.Task.WaitAsync(TimeSpan.FromSeconds(3));
		var failure = await failed.Task.WaitAsync(TimeSpan.FromSeconds(3));
		var branch = await branchTerminal.Task.WaitAsync(TimeSpan.FromSeconds(3));
		var attempt = await attemptTerminal.Task.WaitAsync(TimeSpan.FromSeconds(3));
		var terminal = await requestTerminal.Task.WaitAsync(TimeSpan.FromSeconds(3));

		Assert.Equal(ProtocolOutcome.Succeeded, response.Result.Outcome);
		Assert.Equal(AccessCompliance.EvidenceIncomplete, response.Result.Compliance);
		Assert.Equal(ProofKind.Inferred, response.Result.Evidence.Interface.Proof);
		Assert.Equal(NetworkAccessFailureCodes.AccessEvidenceIncomplete, failure.Failure.Reason);
		Assert.Equal(NetworkFailureKind.AccessEvidence, failure.Failure.Kind);
		Assert.Equal(ProtocolOutcome.Succeeded, branch.ProtocolOutcome);
		Assert.Equal(AccessCompliance.EvidenceIncomplete, branch.AccessCompliance);
		Assert.Equal(ProtocolOutcome.Succeeded, attempt.ProtocolOutcome);
		Assert.Equal(AccessCompliance.EvidenceIncomplete, attempt.AccessCompliance);
		Assert.Equal(ProtocolOutcome.Succeeded, terminal.ProtocolOutcome);
		Assert.Equal(AccessCompliance.EvidenceIncomplete, terminal.AccessCompliance);
		Assert.Equal(0, endpoint.ReceiveQueueLength);
	}

	[Fact]
	public void Ipv4MultipleReplyParserPreservesEachResponder()
	{
		var stride = IntPtr.Size == 8 ? 40 : 28;
		var buffer = Marshal.AllocHGlobal(stride * 2);
		try
		{
			WriteIpv4Reply(buffer, 0, stride, "192.0.2.1", 7);
			WriteIpv4Reply(buffer, 1, stride, "192.0.2.2", 11);

			var replies = NetworkPingExecutor.ParseIpv4Replies(buffer, 2, 8);

			Assert.Equal(2, replies.Length);
			Assert.Equal(IpAddressValue.Parse("192.0.2.1"), replies[0].ReplyAddress);
			Assert.Equal((uint)7, replies[0].RoundtripTimeMs);
			Assert.Equal(IpAddressValue.Parse("192.0.2.2"), replies[1].ReplyAddress);
			Assert.Equal((uint)11, replies[1].RoundtripTimeMs);
		}
		finally
		{
			Marshal.FreeHGlobal(buffer);
		}
	}

	[Fact]
	public void Ipv6MultipleReplyParserPreservesEachResponderAsPureAddress()
	{
		var stride = Marshal.SizeOf<NetworkPingExecutor.IcmpV6EchoReplyLayout>();
		var buffer = Marshal.AllocHGlobal(stride * 2);
		try
		{
			WriteIpv6Reply(buffer, 0, "fe80::1", 12, 5);
			WriteIpv6Reply(buffer, 1, "fe80::2", 12, 9);

			var replies = NetworkPingExecutor.ParseIpv6Replies(buffer, 2, 8);

			Assert.Equal(2, replies.Length);
			Assert.Equal(IpAddressValue.Parse("fe80::1"), replies[0].ReplyAddress);
			Assert.Equal((uint)5, replies[0].RoundtripTimeMs);
			Assert.Equal(IpAddressValue.Parse("fe80::2"), replies[1].ReplyAddress);
			Assert.Equal((uint)9, replies[1].RoundtripTimeMs);
		}
		finally
		{
			Marshal.FreeHGlobal(buffer);
		}
	}

	[Fact]
	public void Ipv6EchoReplyLayoutMatchesWindowsIpExportAbi()
	{
		Assert.Equal(36, Marshal.SizeOf<NetworkPingExecutor.IcmpV6EchoReplyLayout>());
		Assert.Equal(6, Marshal.OffsetOf<NetworkPingExecutor.IcmpV6EchoReplyLayout>(
			nameof(NetworkPingExecutor.IcmpV6EchoReplyLayout.AddressFirstByte)).ToInt32());
		Assert.Equal(28, Marshal.OffsetOf<NetworkPingExecutor.IcmpV6EchoReplyLayout>(
			nameof(NetworkPingExecutor.IcmpV6EchoReplyLayout.Status)).ToInt32());
		Assert.Equal(32, Marshal.OffsetOf<NetworkPingExecutor.IcmpV6EchoReplyLayout>(
			nameof(NetworkPingExecutor.IcmpV6EchoReplyLayout.RoundTripTime)).ToInt32());
	}

	[Fact]
	public void ExactIpv6InterfaceSubmissionProvesBindingWithoutAReply()
	{
		var environment = CreateEnvironment(IPAddress.IPv6Loopback, exactSource: false, exactInterface: true);
		Assert.True(environment.Context.TryResolve(
			environment.Plan, NetworkAccessSecurityBoundary.NotApplicable(), out var resolved, out _));
		var multicast = IpAddressValue.Parse("ff02::1");
		var plan = environment.Plan with { Destination = NetworkDestinationSelection.Exact(multicast) };
		resolved = resolved with { Destination = multicast };
		var identity = NetworkExecutionIdentity.ForRequest(Guid.NewGuid()).StartAttempt(Guid.NewGuid()).StartBranch(Guid.NewGuid());

		var compliance = NetworkPingExecutor.EvaluateCompliance(plan, resolved, submissionEstablished: true);
		var evidence = NetworkPingExecutor.CreateEvidence(identity, plan, resolved, submissionEstablished: true);

		Assert.Equal(AccessCompliance.Satisfied, compliance);
		Assert.Equal(multicast, resolved.Destination);
		Assert.NotEqual(0U, resolved.Interface.InterfaceIndex);
		Assert.Equal(ProofKind.ApiBinding, evidence.Destination.Proof);
		Assert.Equal(resolved.Destination, evidence.Destination.Value);
		Assert.Equal(ProofKind.ApiBinding, evidence.Interface.Proof);
		Assert.Equal(AccessCompliance.Satisfied, evidence.Interface.Compliance);
	}

	[Fact]
	public void ExactIpv6GlobalSourceInterfaceUsesAdapterReportedProof()
	{
		var environment = CreateEnvironment(IPAddress.IPv6Loopback, exactSource: true, exactInterface: true);
		Assert.True(environment.Context.TryResolve(
			environment.Plan, NetworkAccessSecurityBoundary.NotApplicable(), out var resolved, out _));
		var destination = IpAddressValue.Parse("2001:db8::1");
		var plan = environment.Plan with { Destination = NetworkDestinationSelection.Exact(destination) };
		resolved = resolved with { Destination = destination };
		var identity = NetworkExecutionIdentity.ForRequest(Guid.NewGuid()).StartAttempt(Guid.NewGuid()).StartBranch(Guid.NewGuid());

		var compliance = NetworkPingExecutor.EvaluateCompliance(plan, resolved, submissionEstablished: true);
		var evidence = NetworkPingExecutor.CreateEvidence(identity, plan, resolved, submissionEstablished: true);

		Assert.Equal(AccessCompliance.Satisfied, compliance);
		Assert.Equal(ProofKind.AdapterReported, evidence.Interface.Proof);
		Assert.Equal(AccessCompliance.Satisfied, evidence.Interface.Compliance);
	}

	[Fact]
	public async Task Ipv4BroadcastRequiresExplicitCollectionWindow()
	{
		var environment = CreateEnvironment(IPAddress.Loopback, exactSource: false, exactInterface: false);
		var plan = environment.Plan with
		{
			IpPacketPolicy = NetworkIpPacketPolicy.Explicit(default, false, true, false),
		};
		using var endpoint = new NetworkPingEndpoint<int>(environment.Context, maxAttemptCount: 1);
		var failed = new TaskCompletionSource<TrackedRequestFailure<NetworkPingRequest<int>, int>>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		endpoint.RequestFailed += (_, args) => failed.TrySetResult(args.Failure);
		var request = new NetworkPingRequest<int>(
			Guid.NewGuid(), 10, plan, NetworkAccessSecurityBoundary.NotApplicable(), [], 1000, false);

		Assert.True(endpoint.TrySend(request));
		var failure = await failed.Task.WaitAsync(TimeSpan.FromSeconds(3));

		Assert.Equal(NetworkAccessFailureCodes.ResponseCollectionPolicyInvalid, failure.Failure.Reason);
	}

	[Fact]
	public async Task Ipv6MulticastRequiresExplicitCollectionWindow()
	{
		if (!Socket.OSSupportsIPv6) return;
		var environment = CreateEnvironment(IPAddress.IPv6Loopback, exactSource: false, exactInterface: false);
		var multicast = IpAddressValue.Parse("ff02::1");
		var plan = environment.Plan with { Destination = NetworkDestinationSelection.Exact(multicast) };
		using var endpoint = new NetworkPingEndpoint<int>(environment.Context, maxAttemptCount: 1);
		var failed = new TaskCompletionSource<TrackedRequestFailure<NetworkPingRequest<int>, int>>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		endpoint.RequestFailed += (_, args) => failed.TrySetResult(args.Failure);
		var request = new NetworkPingRequest<int>(
			Guid.NewGuid(), 11, plan, NetworkAccessSecurityBoundary.NotApplicable(), [], 1000, false);

		Assert.True(endpoint.TrySend(request));
		var failure = await failed.Task.WaitAsync(TimeSpan.FromSeconds(3));

		Assert.Equal(NetworkAccessFailureCodes.ResponseCollectionPolicyInvalid, failure.Failure.Reason);
	}

	private static void WriteIpv4Reply(IntPtr buffer, int index, int stride, string address, int roundtrip)
	{
		var current = IntPtr.Add(buffer, index * stride);
		var bytes = IPAddress.Parse(address).GetAddressBytes();
		Marshal.WriteInt32(current, unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes)));
		Marshal.WriteInt32(current, 4, (int)IPStatus.Success);
		Marshal.WriteInt32(current, 8, roundtrip);
	}

	private static void WriteIpv6Reply(IntPtr buffer, int index, string address, int scope, int roundtrip)
	{
		var stride = Marshal.SizeOf<NetworkPingExecutor.IcmpV6EchoReplyLayout>();
		var addressOffset = checked((int)Marshal.OffsetOf<NetworkPingExecutor.IcmpV6EchoReplyLayout>(
			nameof(NetworkPingExecutor.IcmpV6EchoReplyLayout.AddressFirstByte)));
		var statusOffset = checked((int)Marshal.OffsetOf<NetworkPingExecutor.IcmpV6EchoReplyLayout>(
			nameof(NetworkPingExecutor.IcmpV6EchoReplyLayout.Status)));
		var roundtripOffset = checked((int)Marshal.OffsetOf<NetworkPingExecutor.IcmpV6EchoReplyLayout>(
			nameof(NetworkPingExecutor.IcmpV6EchoReplyLayout.RoundTripTime)));
		var current = IntPtr.Add(buffer, index * stride);
		var bytes = IPAddress.Parse(address).GetAddressBytes();
		Marshal.Copy(bytes, 0, IntPtr.Add(current, addressOffset), bytes.Length);
		Marshal.WriteInt32(current, 22, scope);
		Marshal.WriteInt32(current, statusOffset, (int)IPStatus.Success);
		Marshal.WriteInt32(current, roundtripOffset, roundtrip);
	}

	private static TestEnvironment CreateEnvironment(IPAddress address, bool exactSource, bool exactInterface)
	{
		var binary = IpAddressValue.FromIPAddress(address);
		var index = FindInterfaceIndex(address);
		var identity = new NetworkInterfaceIdentity(Guid.NewGuid(), index, "ping-endpoint-test", index);
		Assert.True(NetworkAddressSet.TryCreate([binary], out var addresses, out _));
		var interfaces = new NetworkInterfaceSnapshotProvider();
		Assert.True(interfaces.TryReplace([new NetworkInterfaceSnapshot(identity, addresses, true)], out _, out _));
		var routes = new NetworkRouteSnapshotProvider();
		Assert.True(routes.TryReplace(binary, binary, identity, default, 0, out _, out _));
		var context = new NetworkAccessExecutionContext(interfaces, routes, refreshWindowsSnapshots: false);
		var plan = new RequestedAccessPlan(
			exactSource || exactInterface ? NetworkPathProvider.Direct : NetworkPathProvider.Automatic,
			NetworkDestinationSelection.Exact(binary),
			exactSource ? NetworkSourceSelection.Exact(binary) : NetworkSourceSelection.SystemSelected(),
			exactInterface ? NetworkInterfaceSelection.Exact(identity) : NetworkInterfaceSelection.SystemSelected(),
			NetworkNextHopSelection.SystemSelected(),
			NetworkLocalEndpointSelection.NotApplicable(),
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
}

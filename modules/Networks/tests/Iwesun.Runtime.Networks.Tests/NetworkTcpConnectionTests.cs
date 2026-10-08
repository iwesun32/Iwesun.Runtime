using System.Net;
using System.Net.Sockets;
using Xunit;

namespace Iwesun.Runtime.Networks.Tests;

public sealed partial class NetworkSocketExecutionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpenTcpKeepsPolicyAliveDuringBidirectionalIo(bool asyncDispose)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var environment = CreateEnvironment(IPAddress.Loopback, bindInterface: true, exactNextHop: true);
        var backend = new RecordingWfpBackend();
        var executor = new NetworkSocketExecutor(environment.Resolver, backend);
        var request = CreateRequest(environment, NetworkPathProvider.Direct, (ushort)((IPEndPoint)listener.LocalEndpoint).Port);
        var pending = listener.AcceptSocketAsync(timeout.Token);
        var opened = await executor.OpenTcpAsync(request, timeout.Token);
        Assert.True(opened.IsSuccess, opened.Result.ReasonCode);
        await using var connection = opened.Connection!;
        using var peer = await pending;
        Assert.Equal(request.Identity, connection.Identity);
        Assert.Equal(request.Requested, connection.Requested);
        Assert.Equal(request.Resolved, connection.Resolved);
        Assert.Single(backend.Acquired);
        Assert.Equal(0, backend.DisposeCount);
        await connection.Stream.WriteAsync(new byte[] { 1, 2, 3 }, timeout.Token);
        using var peerStream = new NetworkStream(peer, ownsSocket: false);
        var received = new byte[3];
        await peerStream.ReadExactlyAsync(received, timeout.Token);
        Assert.Equal(new byte[] { 1, 2, 3 }, received);
        await peerStream.WriteAsync(new byte[] { 4, 5, 6 }, timeout.Token);
        await connection.Stream.ReadExactlyAsync(received, timeout.Token);
        Assert.Equal(new byte[] { 4, 5, 6 }, received);
        Assert.Equal(0, backend.DisposeCount);
        if (asyncDispose) await connection.DisposeAsync(); else connection.Dispose();
        connection.Dispose();
        Assert.Equal(1, backend.DisposeCount);
        Assert.Equal(0, await peerStream.ReadAsync(received, timeout.Token));
    }

    [Fact]
    public async Task OpenTcpFailureReleasesPolicyWithoutReturningConnection()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = (ushort)((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var environment = CreateEnvironment(IPAddress.Loopback, true, exactNextHop: true);
        var backend = new RecordingWfpBackend();
        var executor = new NetworkSocketExecutor(environment.Resolver, backend);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var opened = await executor.OpenTcpAsync(CreateRequest(environment, NetworkPathProvider.Direct, port), timeout.Token);
        Assert.False(opened.IsSuccess);
        Assert.Null(opened.Connection);
        Assert.Equal(ProtocolOutcome.TransportFailed, opened.Result.Outcome);
        Assert.Single(backend.Acquired);
        Assert.Equal(1, backend.DisposeCount);
    }

    [Fact]
    public async Task OpenTcpPreCancelledDoesNotInstallPolicy()
    {
        var environment = CreateEnvironment(IPAddress.Loopback, true, exactNextHop: true);
        var backend = new RecordingWfpBackend();
        var executor = new NetworkSocketExecutor(environment.Resolver, backend);
        var opened = await executor.OpenTcpAsync(CreateRequest(environment, NetworkPathProvider.Direct, 9), new CancellationToken(true));
        Assert.Null(opened.Connection);
        Assert.Equal(ProtocolOutcome.Cancelled, opened.Result.Outcome);
        Assert.Empty(backend.Acquired);
    }

    [Fact]
    public async Task OpenTcpRejectsUdpBeforeOpeningSocket()
    {
        var environment = CreateEnvironment(IPAddress.Loopback, false);
        var request = CreateRequest(environment, NetworkPathProvider.Automatic, 9) with
        { Operation = NetworkSocketOperation.UdpExchange, Payload = [1] };
        var opened = await environment.Executor.OpenTcpAsync(request);
        Assert.Null(opened.Connection);
        Assert.Equal(ProtocolOutcome.Rejected, opened.Result.Outcome);
    }

    [Fact]
    public async Task OpenTcpCloseSurfacesPolicyCleanupFailure()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var environment = CreateEnvironment(IPAddress.Loopback, true, exactNextHop: true);
        var executor = new NetworkSocketExecutor(environment.Resolver, new ThrowingCleanupWfpBackend());
        var pending = listener.AcceptSocketAsync(timeout.Token);
        var opened = await executor.OpenTcpAsync(CreateRequest(environment, NetworkPathProvider.Direct,
            (ushort)((IPEndPoint)listener.LocalEndpoint).Port), timeout.Token);
        Assert.True(opened.IsSuccess, opened.Result.ReasonCode);
        using var peer = await pending;
        Assert.Throws<NetworkWfpPolicyCleanupException>(() => opened.Connection!.Dispose());
        opened.Connection!.Dispose();
        Assert.Equal(0, await peer.ReceiveAsync(new byte[1], SocketFlags.None, timeout.Token));
    }
}

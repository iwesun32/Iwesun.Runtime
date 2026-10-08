using System.Net;
using System.Net.Sockets;

namespace Iwesun.Runtime.Networks;

/// <summary>A connection-time result; later application I/O failures belong to the caller.</summary>
public sealed record NetworkTcpConnectionOpenResult(
    NetworkSocketExecutionResult Result,
    NetworkTcpConnection? Connection)
{
    public bool IsSuccess => Connection is not null && Result.IsSuccess && Result.Compliance == AccessCompliance.Satisfied;
}

/// <summary>Owns the connected stream and its request-scoped policy until disposal.</summary>
public sealed class NetworkTcpConnection : IDisposable, IAsyncDisposable
{
    internal NetworkTcpConnection(Stream stream, NetworkSocketExecutionRequest request, NetworkSocketExecutionResult result)
    {
        Stream = stream;
        Identity = request.Identity;
        Requested = request.Requested;
        Resolved = request.Resolved;
        ConnectResult = result;
    }

    public Stream Stream { get; }
    public NetworkExecutionIdentity Identity { get; }
    public RequestedAccessPlan Requested { get; }
    public ResolvedAccessPlan Resolved { get; }
    public NetworkSocketExecutionResult ConnectResult { get; }
    public void Dispose() => Stream.Dispose();
    public ValueTask DisposeAsync() => Stream.DisposeAsync();
}

public sealed partial class NetworkSocketExecutor
{
    /// <summary>Opens one TCP stream without proxy fallback or reconnect. The caller must dispose the connection.</summary>
    public async ValueTask<NetworkTcpConnectionOpenResult> OpenTcpAsync(
        NetworkSocketExecutionRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            if (request.Operation != NetworkSocketOperation.TcpConnect)
                return new(Failure(request, ProtocolOutcome.Rejected, NetworkAccessFailureCodes.SocketRequestInvalid), null);
            if (!TryValidateRequest(request, out var validation))
                return new(Failure(request, ProtocolOutcome.Rejected, validation.ReasonCode, ToBinaryError(validation.PlatformError)), null);
            cancellationToken.ThrowIfCancellationRequested();
            return await OpenValidatedTcpAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(Failure(request, ProtocolOutcome.Cancelled, "socket-operation-cancelled"), null);
        }
        catch (NetworkWfpPolicyCleanupException error)
        {
            return new(Failure(request, ProtocolOutcome.TransportFailed,
                NetworkAccessFailureCodes.WfpPolicyCleanupFailed, ToBinaryError(error.PlatformError)), null);
        }
        catch (Exception error)
        {
            return new(Failure(request, ProtocolOutcome.TransportFailed,
                NetworkAccessFailureCodes.SocketOperationFailed, BinaryNetworkError.FromException(error, false, false)), null);
        }
    }

    private async ValueTask<NetworkTcpConnectionOpenResult> OpenValidatedTcpAsync(
        NetworkSocketExecutionRequest request, CancellationToken cancellationToken)
    {
        Socket? socket = CreateSocket(request.Resolved.Destination);
        INetworkWfpConnectionPolicyLease? policy = null;
        try
        {
            ApplyPlan(socket, request);
            policy = AcquireWfpPolicyIfRequired(socket, request, ProtocolType.Tcp, out var failure);
            if (!string.IsNullOrEmpty(failure.ReasonCode))
                return new(Failure(request, ProtocolOutcome.Rejected, failure.ReasonCode, ToBinaryError(failure.PlatformError)), null);
            await socket.ConnectAsync(new IPEndPoint(NetworkIpAddressInterop.ToSystemAddress(
                request.Resolved.Destination, request.Resolved.Interface.InterfaceIndex), request.RemotePort), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var result = CreateSuccessResult(request, socket, null, ProofKind.Observed, policyEnforced: policy is not null);
            if (result.Compliance != AccessCompliance.Satisfied)
                return new(result with { Outcome = ProtocolOutcome.Rejected, ReasonCode = "tcp-stream-access-not-satisfied" }, null);
            Stream stream = new NetworkStream(socket, ownsSocket: true);
            if (policy is not null) stream = new NetworkWfpPolicyOwnedStream(stream, policy);
            var connection = new NetworkTcpConnection(stream, request, result);
            socket = null;
            policy = null;
            return new(result, connection);
        }
        finally
        {
            try { socket?.Dispose(); }
            finally { policy?.Dispose(); }
        }
    }
}

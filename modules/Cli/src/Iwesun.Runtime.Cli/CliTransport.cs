using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Iwesun.Runtime.Diagnostics;

namespace Iwesun.Runtime.Cli;

internal static class CliTransport
{
    public static async Task<string> SendAsync(
        ResolvedRuntimeTarget target,
        RuntimeDiagnosticFrame frame,
        CancellationToken cancellationToken,
        bool enableImpersonation = false)
    {
        var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(frame));
        await using var pipe = enableImpersonation
            ? new NamedPipeClientStream(
                target.ServerName,
                target.PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous,
                TokenImpersonationLevel.Impersonation)
            : new NamedPipeClientStream(
                target.ServerName,
                target.PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
        var started = Stopwatch.GetTimestamp();
        using (var connect = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            connect.CancelAfter(target.ConnectTimeoutMs);
            try
            {
                await pipe.ConnectAsync(connect.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && connect.IsCancellationRequested)
            {
                throw Timeout(target.ServerName == "." ? "CLI_CONNECT_TIMEOUT" : "CLI_REMOTE_CONNECT_TIMEOUT", "Timed out connecting to named pipe.", "connect", target.ConnectTimeoutMs);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw CliRemoteErrorClassifier.Classify(ex, target, "connect");
            }
        }

        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        request.CancelAfter(target.RequestTimeoutMs);
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await RunPhaseAsync("write", "CLI_WRITE_TIMEOUT", async () =>
        {
            await pipe.WriteAsync(header, request.Token);
            await pipe.WriteAsync(payload, request.Token);
            await pipe.FlushAsync(request.Token);
        });
        await RunPhaseAsync("read-header", "CLI_RESPONSE_TIMEOUT", () => ReadExactAsync(pipe, header, request.Token));
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > target.MaxResponseBytes)
            throw new CliException("CLI_PROTOCOL_RESPONSE_LENGTH", $"Invalid response length {length}.", 5);
        var response = new byte[length];
        await RunPhaseAsync("read-body", "CLI_RESPONSE_TIMEOUT", () => ReadExactAsync(pipe, response, request.Token));
        return Encoding.UTF8.GetString(response);

        CliException Timeout(string code, string message, string phase, int timeoutMs) =>
            new(code, message, 4, data: new
            {
                transport = "namedPipe",
                endpoint = target.EndpointName,
                nodeAlias = target.NodeAlias,
                serverName = target.ServerName,
                pipeName = target.PipeName,
                targetAlias = target.TargetAlias,
                phase,
                timeoutMs,
                elapsedMs = Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 2),
                retryable = true
            });

        async Task RunPhaseAsync(string phase, string code, Func<Task> operation)
        {
            try
            {
                await operation();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && request.IsCancellationRequested)
            {
                throw Timeout(code, phase == "write" ? "Timed out writing named pipe request." : "Timed out waiting for named pipe response.", phase, target.RequestTimeoutMs);
            }
            catch (CliException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw CliRemoteErrorClassifier.Classify(ex, target, phase);
            }
        }
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (read == 0)
                throw new CliException("CLI_PROTOCOL_TRUNCATED", "The pipe closed before the response completed.", 5);
            offset += read;
        }
    }
}

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

internal sealed record WfpHttpsResult(string host, int statusCode, bool httpSuccess, int bodyBytesRead,
    bool bodyComplete, bool readLimitReached, long elapsedMs,
    string certificateValidation = "platform-default", string serverPathEvidence = "not-collected");

internal static class WfpHttpsProbe
{
    // The caller retains both the socket and WFP lease until all HTTP work finishes.
    internal static Task<WfpHttpsResult> ExecuteAsync(Socket socket, string host, int port, CancellationToken cancellationToken) =>
        ExecuteAsync(new NetworkStream(socket, ownsSocket: false), host, port, cancellationToken);

    internal static async Task<WfpHttpsResult> ExecuteAsync(Stream stream, string host, int port, CancellationToken cancellationToken)
    {
        var handedOff = 0;
        using var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            UseCookies = false,
            MaxResponseHeadersLength = 32,
            ConnectCallback = (_, token) =>
            {
                token.ThrowIfCancellationRequested();
                if (Interlocked.Exchange(ref handedOff, 1) != 0)
                    throw new HttpRequestException("Reconnect is prohibited for a fixed WFP socket.");
                return ValueTask.FromResult(stream);
            },
        };
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var request = new HttpRequestMessage(HttpMethod.Get, new UriBuilder("https", host, port, "/").Uri)
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
        request.Headers.ConnectionClose = true;
        var clock = Stopwatch.StartNew();
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        const int limit = 65536;
        var buffer = new byte[4096];
        var received = 0;
        var complete = false;
        while (received < limit)
        {
            var count = await body.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, limit - received)), cancellationToken).ConfigureAwait(false);
            if (count == 0) { complete = true; break; }
            received += count;
        }
        return new(host, (int)response.StatusCode, response.IsSuccessStatusCode, received, complete,
            received == limit, clock.ElapsedMilliseconds);
    }
}

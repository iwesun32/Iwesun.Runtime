using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Iwesun.Runtime.Networks;

internal static class GatewayEndurance
{
    private sealed record Sample(string Gateway, bool Success, long TotalMs, long HttpsMs, int Bytes, string Error);

    internal static async Task RunAsync(LocalStreamValidationState state, CancellationToken token)
    {
        var samples = new List<Sample>(20);
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        long initialPrivate = process.PrivateMemorySize64, peakPrivate = initialPrivate;
        long initialWorking = process.WorkingSet64, peakWorking = initialWorking;
        int initialHandles = process.HandleCount, peakHandles = initialHandles;
        var initialCpu = process.TotalProcessorTime;
        var clock = Stopwatch.StartNew();
        var runId = Guid.NewGuid();
        void Publish(bool complete)
        {
            process.Refresh();
            peakPrivate = Math.Max(peakPrivate, process.PrivateMemorySize64);
            peakWorking = Math.Max(peakWorking, process.WorkingSet64);
            peakHandles = Math.Max(peakHandles, process.HandleCount);
            state.Summary = JsonSerializer.Serialize(new
            {
                runId, complete, elapsedSeconds = Math.Round(clock.Elapsed.TotalSeconds, 1), plannedSeconds = 600,
                attempted = samples.Count, passed = samples.Count(s => s.Success), failed = samples.Count(s => !s.Success),
                gateways = samples.GroupBy(s => s.Gateway).Select(g => new
                {
                    gateway = g.Key, attempts = g.Count(), passed = g.Count(s => s.Success), failed = g.Count(s => !s.Success),
                    meanTotalMs = Math.Round(g.Average(s => s.TotalMs), 1), maxTotalMs = g.Max(s => s.TotalMs),
                    meanSuccessfulHttpsMs = g.Any(s => s.Success) ? Math.Round(g.Where(s => s.Success).Average(s => s.HttpsMs), 1) : (double?)null,
                    bodyBytes = g.Sum(s => (long)s.Bytes),
                }).ToArray(),
                errors = samples.Where(s => !s.Success).GroupBy(s => s.Error).Select(g => new { reason = g.Key, count = g.Count() }).ToArray(),
                resources = new { initialPrivate, currentPrivate = process.PrivateMemorySize64, peakPrivate,
                    initialWorking, currentWorking = process.WorkingSet64, peakWorking,
                    initialHandles, currentHandles = process.HandleCount, peakHandles,
                    threads = process.Threads.Count, cpuSeconds = Math.Round((process.TotalProcessorTime - initialCpu).TotalSeconds, 3) },
                pathEvidence = "client-policy-enforced; server-egress-not-collected",
            });
        }
        state.Phase = "RunningEndurance";
        Publish(false);
        try
        {
            for (int index = 0; index < 20; index++)
            {
                var wait = TimeSpan.FromSeconds(index * 30) - clock.Elapsed;
                if (wait > TimeSpan.Zero) await Task.Delay(wait, token);
                string gateway = index % 2 == 0 ? "192.168.32.10" : "192.168.32.12";
                var attemptClock = Stopwatch.StartNew();
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                deadline.CancelAfter(TimeSpan.FromSeconds(15));
                try
                {
                    var context = NetworkAccessExecutionContext.CreatePlatformDefault();
                    var plan = new RequestedAccessPlan(NetworkPathProvider.Direct,
                        NetworkDestinationSelection.Exact(IpAddressValue.FromIPAddress(IPAddress.Parse("183.2.172.177"))),
                        NetworkSourceSelection.Exact(IpAddressValue.FromIPAddress(IPAddress.Parse("192.168.32.16"))),
                        NetworkInterfaceSelection.Exact(new NetworkInterfaceIdentity(Guid.Empty, 0, "", 17)),
                        NetworkNextHopSelection.Exact(IpAddressValue.FromIPAddress(IPAddress.Parse(gateway))),
                        NetworkLocalEndpointSelection.Ephemeral(), NetworkRouteScopeSelection.Current(),
                        NetworkIpPacketPolicy.SystemDefault(), NetworkRouteAdapterIdentity.NotApplicable());
                    if (!context.TryResolve(plan, NetworkAccessSecurityBoundary.NotApplicable(), out var resolved, out var failure))
                        throw new IOException(failure.ReasonCode);
                    var request = new NetworkSocketExecutionRequest(NetworkExecutionIdentity.ForRequest(Guid.NewGuid())
                        .StartAttempt(Guid.NewGuid()).StartBranch(Guid.NewGuid()), plan, resolved,
                        NetworkAccessSecurityBoundary.NotApplicable(), NetworkSocketOperation.TcpConnect, 443, [], 0);
                    var opened = await context.SocketExecutor.OpenTcpAsync(request, deadline.Token);
                    if (!opened.IsSuccess)
                    {
                        if (opened.Result.ReasonCode == NetworkAccessFailureCodes.WfpPolicyCleanupFailed)
                            throw new InvalidOperationException("cleanup-failure-stop");
                        throw new IOException(opened.Result.ReasonCode);
                    }
                    await using var connection = opened.Connection!;
                    var result = await WfpHttpsProbe.ExecuteAsync(connection.Stream, "www.baidu.com", 443, deadline.Token);
                    await connection.DisposeAsync();
                    bool success = result.httpSuccess && result.bodyComplete;
                    samples.Add(new(gateway, success, attemptClock.ElapsedMilliseconds, result.elapsedMs,
                        result.bodyBytesRead, success ? "" : $"http-{result.statusCode}-complete-{result.bodyComplete}"));
                    if (success) state.GatewayRounds++;
                }
                catch (Exception error) when (error is not NetworkWfpPolicyCleanupException and not InvalidOperationException && !token.IsCancellationRequested)
                {
                    samples.Add(new(gateway, false, attemptClock.ElapsedMilliseconds, 0, 0,
                        error is OperationCanceledException ? "timeout" : error.GetType().Name + ": " + error.Message));
                }
                Publish(false);
            }
            var remaining = TimeSpan.FromSeconds(600) - clock.Elapsed;
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining, token);
            Publish(true);
            state.Phase = samples.All(s => s.Success) ? "Passed" : "CompletedWithFailures";
        }
        catch
        {
            Publish(false);
            throw;
        }
    }
}

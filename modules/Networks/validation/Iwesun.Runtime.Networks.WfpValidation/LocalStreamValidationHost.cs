using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Iwesun.Runtime.Diagnostics;
using Iwesun.Runtime.Networks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

[assembly: DiagnosticPipePrefix("Iwesun.WfpValidation")]

#if DEBUG
internal static class WfpValidationDebugHost
{
    private static WfpValidationDebugState? _state;

    internal static async Task StageAsync(string stage, object context)
    {
        var state = Volatile.Read(ref _state);
        if (state is null) return;
        state.SetSnapshot(new { Stage = stage, Context = context });
        await RuntimeInjector.Break("wfp.validation.stage.break", () => true, context);
    }

    internal static async Task<int> RunAsync()
    {
        DiagnosticPipePrefix.InitializeFromAssembly(Assembly.GetExecutingAssembly());
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddRuntimeDiagnostics();
        builder.Services.Start(Path.Combine(AppContext.BaseDirectory, "runtime"),
            startupRuntimeDiagnosticsPipeName: DiagnosticPipePrefix.Resolve("RuntimeDiagnostics"));
        using var host = builder.Build();
        host.Services.Activate(Assembly.GetExecutingAssembly());
        var state = new WfpValidationDebugState();
        Volatile.Write(ref _state, state);
        RuntimeInjector.Data(host.Services.GetRequiredService<RuntimeDiagnosticHub>(), "wfp.validation.debug", state,
            new RuntimeDiagnosticObjectAccess
            {
                AllowReadAllPublic = false, ReadableMembers = ["Snapshot"],
                WritableMembers = [], InvokableMembers = ["RunOnce", "Stop"]
            });
        await host.StartAsync();
        var stopping = host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;
        try
        {
            while (!state.StopRequested && !stopping.IsCancellationRequested)
            {
                if (state.ConsumeRun())
                {
                    var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                        .Where(value => value.GetIPProperties().UnicastAddresses.Any(address =>
                            address.Address.Equals(IPAddress.Parse("192.168.32.16")))).ToArray();
                    if (interfaces.Length != 1) throw new InvalidOperationException("source-interface-ambiguous");
                    var index = interfaces[0].GetIPProperties().GetIPv4Properties()!.Index;
                    var result = await WfpValidationProgram.RunAsync([
                        "run", "--confirm-system-mutation", "--source", "192.168.32.16",
                        "--interface-index", index.ToString(), "--next-hop", "192.168.32.166",
                        "--target", "183.2.172.177", "--target-port", "443", "--protocol", "tcp", "--timeout-ms", "10000"]);
                    state.SetSnapshot(new { Stage = "completed", ExitCode = result });
                }
                await Task.Delay(100, stopping);
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        finally
        {
            Volatile.Write(ref _state, null);
            await host.StopAsync(CancellationToken.None);
        }
        return 0;
    }
}

internal sealed class WfpValidationDebugState
{
    private int _run;
    private int _stop;
    private object _snapshot = new { Stage = "idle" };
    public object Snapshot => Volatile.Read(ref _snapshot);
    internal bool StopRequested => Volatile.Read(ref _stop) != 0;
    public void RunOnce() => Interlocked.Exchange(ref _run, 1);
    public void Stop() => Interlocked.Exchange(ref _stop, 1);
    internal bool ConsumeRun() => Interlocked.Exchange(ref _run, 0) != 0;
    internal void SetSnapshot(object value) => Volatile.Write(ref _snapshot, value);
}
#endif

internal static class LocalStreamValidationHost
{
    internal static async Task<int> RunAsync(string[] args)
    {
        bool gatewayTest = args.Contains("--gateway-test");
        bool endurance = args.Contains("--endurance");
        if (endurance && !gatewayTest) return 64;
        if (gatewayTest && !args.Contains("--confirm-system-mutation")) return 64;
        var gatewayArguments = args.Where(a => a.StartsWith("--gateway=", StringComparison.Ordinal)).ToArray();
        if (gatewayArguments.Length > 1) return 64;
        var gateway = gatewayArguments.Length == 0 ? "192.168.32.10" : gatewayArguments[0][10..];
        if (gateway is not "192.168.32.10" and not "192.168.32.12") return 64;
        var builder = Host.CreateApplicationBuilder(args.Where(a => a is not "--gateway-test" and not "--confirm-system-mutation" and not "--endurance"
            && !a.StartsWith("--gateway=", StringComparison.Ordinal)).ToArray());
        builder.Logging.ClearProviders();
        builder.Logging.AddRuntimeDiagnostics();
        builder.Services.Start(Path.Combine(AppContext.BaseDirectory, "runtime"),
            startupRuntimeDiagnosticsPipeName: "Iwesun.WfpValidation.RuntimeDiagnostics");
        builder.Services.AddSingleton<LocalStreamValidationState>();
        builder.Services.AddSingleton(new GatewayValidationOptions(gatewayTest, gateway, endurance));
        builder.Services.AddHostedService<LocalStreamValidationWorker>();
        using var host = builder.Build();
        host.Services.Activate(Assembly.GetExecutingAssembly());
        RuntimeInjector.Data(host.Services.GetRequiredService<RuntimeDiagnosticHub>(), "wfp.local-test",
            host.Services.GetRequiredService<LocalStreamValidationState>(), new RuntimeDiagnosticObjectAccess
            {
                AllowReadAllPublic = false,
                ReadableMembers = ["Phase", "RoundsPassed", "BytesVerified", "Error", "WfpSupported", "WfpReason", "GatewayRounds", "GatewayResult", "Summary"],
                WritableMembers = [], InvokableMembers = [],
            });
        await host.RunAsync();
        return 0;
    }
}

internal sealed class LocalStreamValidationState
{
    public string Summary { get; internal set; } = "not-started";
    public int GatewayRounds { get; internal set; }
    public string GatewayResult { get; internal set; } = "not-run";
    public string Phase { get; internal set; } = "Starting";
    public int RoundsPassed { get; internal set; }
    public int BytesVerified { get; internal set; }
    public string Error { get; internal set; } = "";
    public bool WfpSupported { get; internal set; }
    public string WfpReason { get; internal set; } = "";
}

internal sealed record GatewayValidationOptions(bool Enabled, string Gateway, bool Endurance);

internal sealed class LocalStreamValidationWorker(LocalStreamValidationState state, GatewayValidationOptions options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The current managed-task API accepts Action, not Func<Task>; never pass async void.
        using var run = RuntimeInjector.CreateTask(token => RunAsync(token).GetAwaiter().GetResult(), unitId: "wfp.local-test.run",
            cancellationToken: stoppingToken, startImmediately: true);
        await run;
    }

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            var capability = new WindowsNetworkWfpConnectionPolicyBackend().QueryCapability();
            state.WfpSupported = capability.Supported;
            state.WfpReason = capability.ReasonCode;
            state.Phase = "RunningLoopback";
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            for (var round = 0; round < 3; round++)
            {
                using var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                var context = NetworkAccessExecutionContext.CreatePlatformDefault();
                var plan = new RequestedAccessPlan(NetworkPathProvider.Automatic,
                    NetworkDestinationSelection.Exact(IpAddressValue.FromIPAddress(IPAddress.Loopback)),
                    NetworkSourceSelection.SystemSelected(), NetworkInterfaceSelection.SystemSelected(),
                    NetworkNextHopSelection.SystemSelected(), NetworkLocalEndpointSelection.Ephemeral(),
                    NetworkRouteScopeSelection.Current(), NetworkIpPacketPolicy.SystemDefault(),
                    NetworkRouteAdapterIdentity.NotApplicable());
                if (!context.TryResolve(plan, NetworkAccessSecurityBoundary.NotApplicable(), out var resolved, out var failure))
                    throw new IOException(failure.ReasonCode);
                var request = new NetworkSocketExecutionRequest(NetworkExecutionIdentity.ForRequest(Guid.NewGuid())
                    .StartAttempt(Guid.NewGuid()).StartBranch(Guid.NewGuid()), plan, resolved,
                    NetworkAccessSecurityBoundary.NotApplicable(), NetworkSocketOperation.TcpConnect,
                    (ushort)((IPEndPoint)listener.LocalEndpoint).Port, [], 0);
                var opened = await context.SocketExecutor.OpenTcpAsync(request, deadline.Token);
                if (!opened.IsSuccess) throw new IOException(opened.Result.ReasonCode);
                await using var connection = opened.Connection!;
                using var peer = await listener.AcceptSocketAsync(deadline.Token);
                using var peerStream = new NetworkStream(peer, ownsSocket: false);
                var payload = new byte[] { (byte)round, 17, 32, 255 };
                var reply = new byte[payload.Length];
                await connection.Stream.WriteAsync(payload, deadline.Token);
                await peerStream.ReadExactlyAsync(reply, deadline.Token);
                if (!payload.SequenceEqual(reply)) throw new IOException("outbound-payload-mismatch");
                await peerStream.WriteAsync(reply, deadline.Token);
                await connection.Stream.ReadExactlyAsync(reply, deadline.Token);
                if (!payload.SequenceEqual(reply)) throw new IOException("inbound-payload-mismatch");
                await connection.DisposeAsync();
                if (await peerStream.ReadAsync(reply, deadline.Token) != 0) throw new IOException("socket-not-closed");
                state.BytesVerified += payload.Length * 2;
                state.RoundsPassed++;
            }
            if (options.Enabled)
            {
                if (!capability.Supported) throw new InvalidOperationException(capability.ReasonCode);
                if (options.Endurance)
                {
                    await GatewayEndurance.RunAsync(state, token);
                    return;
                }
                state.Phase = "RunningGatewayHttps";
                for (var round = 0; round < (options.Gateway == "192.168.32.12" ? 1 : 3); round++)
                {
                    using var gatewayDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                    gatewayDeadline.CancelAfter(TimeSpan.FromSeconds(15));
                    var context = NetworkAccessExecutionContext.CreatePlatformDefault();
                    var plan = new RequestedAccessPlan(NetworkPathProvider.Direct,
                        NetworkDestinationSelection.Exact(IpAddressValue.FromIPAddress(IPAddress.Parse("183.2.172.177"))),
                        NetworkSourceSelection.Exact(IpAddressValue.FromIPAddress(IPAddress.Parse("192.168.32.16"))),
                        NetworkInterfaceSelection.Exact(new NetworkInterfaceIdentity(Guid.Empty, 0, "", 17)),
                        NetworkNextHopSelection.Exact(IpAddressValue.FromIPAddress(IPAddress.Parse(options.Gateway))),
                        NetworkLocalEndpointSelection.Ephemeral(), NetworkRouteScopeSelection.Current(),
                        NetworkIpPacketPolicy.SystemDefault(), NetworkRouteAdapterIdentity.NotApplicable());
                    if (!context.TryResolve(plan, NetworkAccessSecurityBoundary.NotApplicable(), out var resolved, out var failure))
                        throw new IOException(failure.ReasonCode);
                    var request = new NetworkSocketExecutionRequest(NetworkExecutionIdentity.ForRequest(Guid.NewGuid())
                        .StartAttempt(Guid.NewGuid()).StartBranch(Guid.NewGuid()), plan, resolved,
                        NetworkAccessSecurityBoundary.NotApplicable(), NetworkSocketOperation.TcpConnect, 443, [], 0);
                    var opened = await context.SocketExecutor.OpenTcpAsync(request, gatewayDeadline.Token);
                    if (!opened.IsSuccess) throw new IOException(opened.Result.ReasonCode);
                    await using var connection = opened.Connection!;
                    var result = await WfpHttpsProbe.ExecuteAsync(connection.Stream, "www.baidu.com", 443, gatewayDeadline.Token);
                    await connection.DisposeAsync();
                    state.GatewayResult = System.Text.Json.JsonSerializer.Serialize(new { request.Identity, opened.Result.Evidence, https = result });
                    if (!result.httpSuccess || !result.bodyComplete) throw new IOException("https-not-successful-or-body-incomplete");
                    state.GatewayRounds++;
                }
            }
            state.Phase = "Passed";
        }
        catch (Exception error)
        {
            state.Error = error.GetType().Name + ": " + error.Message;
            state.Phase = error is OperationCanceledException ? "Cancelled" : "Failed";
        }
    }
}

using System.Net;
using Iwesun.Runtime.Networks;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
	PrintHelp();
	return 0;
}

if (!string.Equals(args[0], "tcp", StringComparison.OrdinalIgnoreCase) ||
	args.Length != 3 || !IPAddress.TryParse(args[1], out var target) ||
	!ushort.TryParse(args[2], out var port) || port == 0)
{
	PrintHelp();
	return 64;
}

var destination = IpAddressValue.FromIPAddress(target);
var plan = new RequestedAccessPlan(
	NetworkPathProvider.Automatic,
	NetworkDestinationSelection.Exact(destination),
	NetworkSourceSelection.SystemSelected(),
	NetworkInterfaceSelection.SystemSelected(),
	NetworkNextHopSelection.SystemSelected(),
	NetworkLocalEndpointSelection.Ephemeral(),
	NetworkRouteScopeSelection.Current(),
	NetworkIpPacketPolicy.SystemDefault(),
	NetworkRouteAdapterIdentity.NotApplicable());
var requestId = Guid.NewGuid();
using var endpoint = new NetworkTcpConnectEndpoint<string>(maxAttemptCount: 1, defaultTimeoutMs: 5000);
var completion = new TaskCompletionSource<
	TrackedRequestCompletion<NetworkTcpConnectRequest<string>, NetworkTcpConnectResponse<string>, string>>(
	TaskCreationOptions.RunContinuationsAsynchronously);
var failure = new TaskCompletionSource<TrackedRequestFailure<NetworkTcpConnectRequest<string>, string>>(
	TaskCreationOptions.RunContinuationsAsynchronously);
endpoint.RequestAcknowledged += (_, eventArgs) =>
{
	if (eventArgs.Completion.RequestId == requestId) completion.TrySetResult(eventArgs.Completion);
};
endpoint.RequestFailed += (_, eventArgs) =>
{
	if (eventArgs.Failure.RequestId == requestId) failure.TrySetResult(eventArgs.Failure);
};
endpoint.Start();
if (!endpoint.TrySend(new NetworkTcpConnectRequest<string>(
	requestId,
	"tcp-example",
	plan,
	NetworkAccessSecurityBoundary.NotApplicable(),
	port,
	5000,
	false)))
{
	Console.Error.WriteLine("request-rejected-before-enqueue");
	return 1;
}

var finished = await Task.WhenAny(completion.Task, failure.Task)
	.WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
if (finished == failure.Task)
{
	var failed = await failure.Task.ConfigureAwait(false);
	Console.Error.WriteLine($"{failed.Failure.Kind}: {failed.Failure.Reason}");
	return 2;
}

var completed = await completion.Task.ConfigureAwait(false);
var result = completed.Response.Result;
Console.WriteLine($"RequestId   : {completed.RequestId}");
Console.WriteLine($"AttemptId   : {result.Identity.AttemptId}");
Console.WriteLine($"BranchId    : {result.Identity.BranchId}");
Console.WriteLine($"Target      : {result.Evidence.Destination.Value.ToIPAddress()}:{port}");
Console.WriteLine($"Source      : {result.Evidence.Source.Value.ToIPAddress()}:{result.Evidence.LocalPort.Value}");
Console.WriteLine($"Interface   : {result.Evidence.Interface.Value.Alias} ({result.Evidence.Interface.Value.InterfaceIndex})");
Console.WriteLine($"Path        : {result.Evidence.PathProvider.Value}");
Console.WriteLine($"Compliance  : {result.Compliance}");
return result.IsSuccess ? 0 : 2;

static void PrintHelp()
{
	Console.WriteLine("Iwesun.Runtime.Networks 3.0 example");
	Console.WriteLine("  dotnet run --project modules/Networks/samples/Iwesun.Runtime.Networks.Examples/Iwesun.Runtime.Networks.Examples.csproj -- tcp <target-ip> <port>");
}

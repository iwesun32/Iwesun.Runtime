using Iwesun.Runtime.RemoteConsole;
using Iwesun.Runtime.RemoteConsole.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
if (!args.Contains("--console", StringComparer.OrdinalIgnoreCase))
	builder.Services.AddWindowsService(options => options.ServiceName = "Iwesun.Runtime.RemoteConsole");

var remoteConsoleOptions = new RemoteConsoleOptions
{
	PipeName = RemoteConsoleOptions.DefaultPipeName,
	SubmitterPrincipals = [$@"{Environment.MachineName}\IwesunAiDiag"],
	ApproverPrincipals = ["S-1-5-32-544"],
	ApprovalMode = RemoteConsoleApprovalMode.Manual
};

builder.Services.AddSingleton(remoteConsoleOptions);
builder.Services.AddSingleton(static provider =>
	RemoteConsolePrincipalResolver.Resolve(provider.GetRequiredService<RemoteConsoleOptions>()));
builder.Services.AddSingleton(static provider =>
{
	var principals = provider.GetRequiredService<RemoteConsoleResolvedPrincipals>();
	return new RemoteConsoleAuthorization(principals.SubmitterSids, principals.ApproverSids);
});
builder.Services.AddHostedService<RemoteConsolePipeServer>();

await builder.Build().RunAsync();

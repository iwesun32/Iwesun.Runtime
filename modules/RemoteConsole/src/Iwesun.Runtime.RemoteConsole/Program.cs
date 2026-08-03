using Iwesun.Runtime.RemoteConsole;
using Iwesun.Runtime.RemoteConsole.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
if (!args.Contains("--console", StringComparer.OrdinalIgnoreCase))
	builder.Services.AddWindowsService(options => options.ServiceName = "Iwesun.Runtime.RemoteConsole");

var configuredPipeName = args
	.FirstOrDefault(static argument => argument.StartsWith("--pipe=", StringComparison.OrdinalIgnoreCase))?
	[("--pipe=".Length)..];
var remoteConsoleOptions = new RemoteConsoleOptions
{
	PipeName = string.IsNullOrWhiteSpace(configuredPipeName)
		? RemoteConsoleProtocol.DefaultPipeName
		: configuredPipeName,
	SubmitterPrincipals = [$@"{Environment.MachineName}\IwesunAiDiag"],
	ApproverPrincipals = ["S-1-5-32-544"],
	ApprovalMode = RemoteConsoleApprovalMode.Manual
};

#if DEBUG
if (args.Contains("--test-current-user", StringComparer.OrdinalIgnoreCase))
{
	var currentUserSid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value
		?? throw new RemoteConsoleConfigurationException("The current Windows identity does not expose a user SID.");
	remoteConsoleOptions = new RemoteConsoleOptions
	{
		PipeName = remoteConsoleOptions.PipeName,
		SubmitterPrincipals = [currentUserSid],
		ApproverPrincipals = [currentUserSid],
		ApprovalMode = RemoteConsoleApprovalMode.Automatic
	};
}
#endif

builder.Services.AddSingleton(remoteConsoleOptions);
builder.Services.AddSingleton(static provider =>
	RemoteConsolePrincipalResolver.Resolve(provider.GetRequiredService<RemoteConsoleOptions>()));
builder.Services.AddSingleton(static provider =>
{
	var principals = provider.GetRequiredService<RemoteConsoleResolvedPrincipals>();
	return new RemoteConsoleAuthorization(principals.SubmitterSids, principals.ApproverSids);
});
builder.Services.AddSingleton(static provider =>
{
	var options = provider.GetRequiredService<RemoteConsoleOptions>();
	return new RemoteConsoleApprovalPolicy(options.ApprovalMode, options.AutoApprovePatterns, options.DenyPatterns);
});
builder.Services.AddSingleton<RemoteConsoleJobStore>();
builder.Services.AddSingleton<RemoteConsoleWorkspaceStore>();
builder.Services.AddSingleton<RemoteConsoleCommandRouter>();
builder.Services.AddSingleton<RemoteConsoleCommandExecutor>();
builder.Services.AddHostedService(static provider => provider.GetRequiredService<RemoteConsoleCommandExecutor>());
builder.Services.AddHostedService<RemoteConsoleWorkspaceCleanupService>();
builder.Services.AddHostedService<RemoteConsolePipeServer>();

await builder.Build().RunAsync();

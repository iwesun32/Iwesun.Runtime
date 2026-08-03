using Iwesun.Runtime.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Iwesun.Runtime.BinaryCompatibilityHost;

public static class LegacyConstructorConsumer
{
	public static string CreateTargets()
	{
		using var registry = new RuntimeManagedRegistry();
		var coordinator = new RuntimeShutdownCoordinator(registry);
		var hub = new RuntimeDiagnosticHub();

		_ = new DiagnosticSwitchboardTarget(coordinator, applicationLifetime: null);
		_ = new RuntimeManagedCommandTarget(registry, hub, coordinator, applicationLifetime: null);

		return "legacy-constructors-bound";
	}

	public static async Task<string> RunHostShutdownAsync()
	{
		var builder = Host.CreateApplicationBuilder();
		builder.Services.Start();
		using var host = builder.Build();
		host.Services.Activate(typeof(LegacyConstructorConsumer).Assembly);
		await host.StartAsync().ConfigureAwait(false);

		var hub = host.Services.GetRequiredService<RuntimeDiagnosticHub>();
		var response = await hub.ExecuteAsync(new RuntimeDiagnosticAction
		{
			TargetId = "diagnostics.switchboard",
			Action = "shutdown"
		}).ConfigureAwait(false);
		var result = await host.Services
			.GetRequiredService<RuntimeShutdownCoordinator>()
			.ShutdownAsync(TimeSpan.FromSeconds(5))
			.ConfigureAwait(false);
		await host.StopAsync().ConfigureAwait(false);

		return response.Success && result.ExitCode == RuntimeShutdownExitCodes.Success
			? "legacy-host-shutdown-0"
			: $"legacy-host-shutdown-failed:{response.Error}:{result.ExitCode}";
	}
}

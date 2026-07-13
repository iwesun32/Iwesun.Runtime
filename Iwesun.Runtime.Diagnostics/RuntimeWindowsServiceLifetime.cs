using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Runtime.Versioning;

namespace Iwesun.Runtime.Diagnostics;

[SupportedOSPlatform("windows")]
internal sealed class RuntimeWindowsServiceLifetime : WindowsServiceLifetime
{
	private readonly RuntimeShutdownCoordinator _shutdownCoordinator;
	private readonly RuntimeWindowsServiceOptions _runtimeOptions;

	public RuntimeWindowsServiceLifetime(
		IHostEnvironment environment,
		IHostApplicationLifetime applicationLifetime,
		ILoggerFactory loggerFactory,
		IOptions<HostOptions> hostOptions,
		IOptions<WindowsServiceLifetimeOptions> windowsServiceOptions,
		IOptions<RuntimeWindowsServiceOptions> runtimeOptions,
		RuntimeShutdownCoordinator shutdownCoordinator)
		: base(environment, applicationLifetime, loggerFactory, hostOptions, windowsServiceOptions)
	{
		_shutdownCoordinator = shutdownCoordinator;
		_runtimeOptions = runtimeOptions.Value;
		if (string.IsNullOrWhiteSpace(_runtimeOptions.ServiceName))
			throw new InvalidOperationException("Windows ServiceName must be supplied by the business application.");
		if (_runtimeOptions.ShutdownTimeout <= TimeSpan.Zero)
			throw new InvalidOperationException("Windows Service shutdown timeout must be greater than zero.");
	}

	protected override void OnStop()
	{
		RunCoordinatedShutdown("windows-service-stop");
		base.OnStop();
	}

	protected override void OnShutdown()
	{
		RunCoordinatedShutdown("windows-system-shutdown");
		base.OnShutdown();
	}

	private void RunCoordinatedShutdown(string payload)
	{
		var result = _shutdownCoordinator
			.ShutdownAsync(_runtimeOptions.ShutdownTimeout, payload, CancellationToken.None)
			.GetAwaiter()
			.GetResult();
		Environment.ExitCode = result.ExitCode;
	}
}

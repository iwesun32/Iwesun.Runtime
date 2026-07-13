using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Iwesun.Runtime.WebView2;

public static class WebRuntimeProgramServiceCollectionExtensions
{
	/// <summary>Registers the shared registry, lifecycle host and Runtime diagnostics target.</summary>
	public static IServiceCollection AddWebRuntimeProgramMonitoring(this IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);
		services.TryAddSingleton<WebRuntimeProgramRegistry>();
		services.TryAddSingleton<WebRuntimeProgramHost>(provider => new WebRuntimeProgramHost(
			provider.GetRequiredService<WebRuntimeProgramRegistry>(),
			provider.GetServices<IWebRuntimeBusinessProgram>()));
		services.TryAddSingleton<WebRuntimeProgramCommandTarget>();
		return services;
	}
}

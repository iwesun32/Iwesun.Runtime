using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Iwesun.Runtime.Diagnostics;

public static class RuntimeDiagnosticsServiceCollectionExtensions
{
	public static IServiceCollection AddRuntimeDiagnostics(
		this IServiceCollection services,
		string? runtimeDirectory = null,
		RuntimeHostScanOptions? hostScanOptions = null)
	{
		services.AddSingleton(new RuntimeDiagnosticHub(hostScanOptions));
		services.AddSingleton(new DiagnosticSwitchboardConfigStore(runtimeDirectory));
		services.AddSingleton<DiagnosticSwitchboardTarget>();
		services.AddSingleton<RuntimeDiagnosticsSelfTestState>();
		services.AddSingleton<RuntimeDiagnosticsMonitor>();
		services.AddSingleton<RuntimeDiagnosticBreakpoints>();
		services.AddSingleton<RuntimeDiagnosticHooks>();
		services.AddHostedService(provider => provider.GetRequiredService<RuntimeDiagnosticsMonitor>());
		return services;
	}

	public static void UseRuntimeDiagnostics(this IServiceProvider provider)
	{
		var hub = provider.GetRequiredService<RuntimeDiagnosticHub>();
		var configStore = provider.GetRequiredService<DiagnosticSwitchboardConfigStore>();
		var target = provider.GetRequiredService<DiagnosticSwitchboardTarget>();
		var breakpoints = provider.GetRequiredService<RuntimeDiagnosticBreakpoints>();
		var hooks = provider.GetRequiredService<RuntimeDiagnosticHooks>();

		// Initialize static state
		DiagnosticSwitchboard.Initialize(configStore);
		DiagnosticSwitchboard.Attach(hub);

		// Wire breakpoints and hooks into Hub
		hub.SetBreakpoints(breakpoints);
		hub.SetHooks(hooks);
		BreakpointHelper.SetInstance(breakpoints);

		hub.Register(target);
		hub.RegisterObject("diagnostics.selftest", provider.GetRequiredService<RuntimeDiagnosticsSelfTestState>(), new RuntimeDiagnosticObjectAccess
		{
			AllowReadAllPublic = true,
			WritableMembers = ["Counter", "Label"],
			InvokableMembers = ["Increment", "Reset"]
		});
		hub.RegisterObject("diagnostics.monitor", provider.GetRequiredService<RuntimeDiagnosticsMonitor>(), new RuntimeDiagnosticObjectAccess
		{
			AllowReadAllPublic = false,
			ReadableMembers = ["IsRunning", "PipeName"]
		});
		hub.RegisterObject("diagnostics.breakpoints", breakpoints, new RuntimeDiagnosticObjectAccess
		{
			AllowReadAllPublic = false,
			ReadableMembers = ["Snapshot"],
			InvokableMembers = ["Enable", "Disable", "Resume", "ResumeAll", "Snapshot"]
		});
	}

	/// <summary>
	/// Build registries by scanning host assembly diagnostic attributes.
	/// Call after UseRuntimeDiagnostics() in the host Program.cs.
	/// </summary>
	public static void BuildDiagnosticRegistries(this IServiceProvider provider, System.Reflection.Assembly hostAssembly)
	{
		var breakpoints = provider.GetRequiredService<RuntimeDiagnosticBreakpoints>();
		var hooks = provider.GetRequiredService<RuntimeDiagnosticHooks>();
		var hub = provider.GetRequiredService<RuntimeDiagnosticHub>();

		// Initialize pipe prefix from host assembly
		DiagnosticPipePrefix.InitializeFromAssembly(hostAssembly);

		// Build registries from assembly attributes
		var snapshot = RegistryBuilder.Build(hostAssembly, breakpoints, hooks);

		// Store snapshot in Hub for CLI queries
		hub.SetRegistrySnapshot(snapshot);
	}

	public static ILoggingBuilder AddRuntimeDiagnostics(this ILoggingBuilder builder, RuntimeDiagnosticLoggerOptions? options = null)
	{
		builder.AddProvider(new RuntimeDiagnosticLoggerProvider(options));
		return builder;
	}
}

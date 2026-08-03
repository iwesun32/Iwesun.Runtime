using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Iwesun.Runtime.Data;

namespace Iwesun.Runtime.Diagnostics;

public static class RuntimeDiagnosticsServiceCollectionExtensions
{
	public static IServiceCollection AddRuntimeDiagnostics(
		this IServiceCollection services,
		string? runtimeDirectory = null,
		RuntimeHostScanOptions? hostScanOptions = null,
		string? startupRuntimeDiagnosticsPipeName = null,
		string? startupRuntimeDiagnosticsFilePath = null,
		RuntimeNamedPipeAccessOptions? pipeAccessOptions = null)
	{
		startupRuntimeDiagnosticsPipeName ??= Environment.GetEnvironmentVariable("IWESUN_RUNTIME_DIAGNOSTICS_PIPE");
		var normalizedPipeAccess = (pipeAccessOptions ?? new RuntimeNamedPipeAccessOptions()).Normalize();
		services.AddSingleton(normalizedPipeAccess);
		services.AddSingleton(new RuntimeDiagnosticHub(hostScanOptions));
		services.AddSingleton(new DiagnosticSwitchboardConfigStore(
			runtimeDirectory,
			startupRuntimeDiagnosticsPipeName,
			startupRuntimeDiagnosticsFilePath));
		services.AddSingleton(RuntimeStateCatalog.CreateOnlineDefaults());
		services.AddSingleton<RuntimeStateManager>();
		services.AddSingleton<RuntimeExecutionManager>();
		services.AddSingleton<RuntimeManagedRegistry>();
		services.AddSingleton<RuntimeShutdownCoordinator>();
		services.AddSingleton(provider => new RuntimeManagedCommandTarget(
			provider.GetRequiredService<RuntimeManagedRegistry>(),
			provider.GetRequiredService<RuntimeDiagnosticHub>(),
			provider.GetRequiredService<RuntimeShutdownCoordinator>(),
			provider.GetService<IHostApplicationLifetime>(),
			provider.GetService<Microsoft.Extensions.Options.IOptions<RuntimeWindowsServiceOptions>>()));
		services.AddSingleton<RuntimeHostCommandTarget>();
		services.AddSingleton<RuntimePipeRegistryTarget>();
		services.AddSingleton<RuntimeFileRegistryTarget>();
		services.AddSingleton<RuntimeProxyCommandTarget>();
		services.AddSingleton(provider => new DiagnosticSwitchboardTarget(
			provider.GetRequiredService<RuntimeShutdownCoordinator>(),
			provider.GetService<IHostApplicationLifetime>(),
			provider.GetService<Microsoft.Extensions.Options.IOptions<RuntimeWindowsServiceOptions>>()));
		services.AddSingleton<RuntimeRootContainer>();
		services.AddSingleton<RuntimeRootContainerTarget>();
		services.AddSingleton<RuntimeDiagnosticsSelfTestState>();
		services.AddSingleton<RuntimeDiagnosticsMonitor>();
		#if DEBUG
		services.AddSingleton<RuntimeDiagnosticBreakpoints>();
		#endif
		services.AddSingleton<RuntimeDiagnosticHooks>();
		services.AddHostedService(provider => provider.GetRequiredService<RuntimeDiagnosticsMonitor>());
		return services;
	}

	public static void UseRuntimeDiagnostics(this IServiceProvider provider)
	{
		RuntimeStaticInjectorCatalog.ValidateOrThrow();
		DiagnosticSwitchboardCompiledConfig.ValidateStaticCatalogAlignment();

		var hub = provider.GetRequiredService<RuntimeDiagnosticHub>();
		var configStore = provider.GetRequiredService<DiagnosticSwitchboardConfigStore>();
		var stateManager = provider.GetRequiredService<RuntimeStateManager>();
		var target = provider.GetRequiredService<DiagnosticSwitchboardTarget>();
		#if DEBUG
		var breakpoints = provider.GetRequiredService<RuntimeDiagnosticBreakpoints>();
		#endif
		var hooks = provider.GetRequiredService<RuntimeDiagnosticHooks>();
		var executionManager = provider.GetRequiredService<RuntimeExecutionManager>();
		var managedTarget = provider.GetRequiredService<RuntimeManagedCommandTarget>();
		var hostTarget = provider.GetRequiredService<RuntimeHostCommandTarget>();
		var pipeRegistryTarget = provider.GetRequiredService<RuntimePipeRegistryTarget>();
		var fileRegistryTarget = provider.GetRequiredService<RuntimeFileRegistryTarget>();
		var proxyTarget = provider.GetRequiredService<RuntimeProxyCommandTarget>();
		var runtimeRootTarget = provider.GetRequiredService<RuntimeRootContainerTarget>();

		// Initialize static state
		DiagnosticSwitchboard.Initialize(configStore);
		DiagnosticSwitchboard.Attach(hub);

		// Wire debug-only breakpoints and production-safe hooks into Hub.
		#if DEBUG
		hub.SetBreakpoints(breakpoints);
		BreakpointHelper.SetInstance(breakpoints);
		#endif
		hub.SetHooks(hooks);

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
		hub.RegisterObject("runtime.state", stateManager, new RuntimeDiagnosticObjectAccess
		{
			AllowReadAllPublic = false,
			ReadableMembers = ["CurrentState", "CurrentPath", "IsStart", "IsWorking", "IsStopping", "UpdatedAt"],
			InvokableMembers = ["SetCurrent", "SetCurrentByCode", "SetCurrentByName", "SetCurrentByKey", "SetStart", "SetWorking", "SetStop", "AddRoot", "Add", "Snapshot"]
		});
		hub.RegisterObject("runtime.execution", executionManager, new RuntimeDiagnosticObjectAccess
		{
			AllowReadAllPublic = false,
			ReadableMembers = ["StaticThreads", "DynamicThreads", "StaticTasks", "DynamicTasks", "Snapshot"],
			InvokableMembers = ["RegisterThread", "SetThreadState", "HeartbeatThread", "RegisterTask", "SetTaskState", "HeartbeatTask", "Snapshot"]
		});
		hub.Register(managedTarget);
		hub.Register(hostTarget);
		hub.Register(pipeRegistryTarget);
		hub.Register(fileRegistryTarget);
		hub.Register(proxyTarget);
		hub.Register(runtimeRootTarget);
		#if DEBUG
		hub.RegisterObject("diagnostics.breakpoints", breakpoints, new RuntimeDiagnosticObjectAccess
		{
			AllowReadAllPublic = false,
			ReadableMembers = ["Snapshot"],
			InvokableMembers = ["Enable", "Disable", "Resume", "ResumeAll", "Snapshot"]
		});
		#endif
	}

	/// <summary>
	/// Build registries by scanning host assembly diagnostic attributes.
	/// Call after UseRuntimeDiagnostics() in the host Program.cs.
	/// </summary>
	public static void BuildDiagnosticRegistries(this IServiceProvider provider, System.Reflection.Assembly hostAssembly)
	{
		#if DEBUG
		var breakpoints = provider.GetRequiredService<RuntimeDiagnosticBreakpoints>();
		#endif
		var hooks = provider.GetRequiredService<RuntimeDiagnosticHooks>();
		var hub = provider.GetRequiredService<RuntimeDiagnosticHub>();

		// Initialize pipe prefix from host assembly
		DiagnosticPipePrefix.InitializeFromAssembly(hostAssembly);

		// Apply file output defaults from host assembly attribute, if not already configured via JSON
		var fileAttr = hostAssembly
			.GetCustomAttributes(typeof(DiagnosticFileOutputAttribute), inherit: false)
			.OfType<DiagnosticFileOutputAttribute>()
			.FirstOrDefault();
		if (fileAttr != null)
			DiagnosticSwitchboard.TryApplyAssemblyFileDefaults(fileAttr.FilePath, fileAttr.WriteMode, fileAttr.Format);

		// Build registries from assembly attributes
		var snapshot = RegistryBuilder.Build(hostAssembly,
			#if DEBUG
			breakpoints,
			#endif
			hooks);

		// Store snapshot in Hub for CLI queries
		hub.SetRegistrySnapshot(snapshot);
	}

	public static ILoggingBuilder AddRuntimeDiagnostics(this ILoggingBuilder builder, RuntimeDiagnosticLoggerOptions? options = null)
	{
		builder.AddProvider(new RuntimeDiagnosticLoggerProvider(options));
		return builder;
	}
}

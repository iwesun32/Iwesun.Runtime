using System.Reflection;
using System.Diagnostics;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Options;
using System.Runtime.CompilerServices;

namespace Iwesun.Runtime.Diagnostics;

public static class RuntimeHostTemplate
{
	private static readonly object StartSync = new();
	private static readonly ConditionalWeakTable<IServiceCollection, RuntimeHostStartRegistration> StartRegistrations = new();
	private static readonly object ActivationSync = new();
	private static IServiceProvider? _activeProvider;
	private static Assembly? _activeHostAssembly;
	private static bool _activationInProgress;

	public static IServiceCollection ConfigureRuntimeWindowsService(
		this IServiceCollection services,
		Action<RuntimeWindowsServiceOptions> configure)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(configure);
		services.Configure(configure);
		return services;
	}

	public static IServiceCollection StartConfiguredWindowsService(
		this IServiceCollection services,
		string? runtimeDirectory = null,
		RuntimeHostScanOptions? hostScanOptions = null,
		string? startupRuntimeDiagnosticsPipeName = null,
		string? startupRuntimeDiagnosticsFilePath = null)
	{
		ArgumentNullException.ThrowIfNull(services);
		services.Start(
			runtimeDirectory,
			hostScanOptions,
			startupRuntimeDiagnosticsPipeName,
			startupRuntimeDiagnosticsFilePath);
		services.AddWindowsService();
		services.AddSingleton<IConfigureOptions<WindowsServiceLifetimeOptions>, RuntimeWindowsServiceOptionsSetup>();
		if (OperatingSystem.IsWindows() && WindowsServiceHelpers.IsWindowsService())
			services.AddSingleton<IHostLifetime, RuntimeWindowsServiceLifetime>();
		return services;
	}

	public static IServiceCollection StartWindowsService(
		this IServiceCollection services,
		string serviceName,
		string? runtimeDirectory = null,
		TimeSpan? shutdownTimeout = null,
		RuntimeHostScanOptions? hostScanOptions = null,
		string? startupRuntimeDiagnosticsPipeName = null,
		string? startupRuntimeDiagnosticsFilePath = null)
	{
		return services.StartWindowsService(
			new RuntimeWindowsServiceOptions
			{
				ServiceName = serviceName,
				DisplayName = serviceName,
				ShutdownTimeout = shutdownTimeout ?? TimeSpan.FromSeconds(30)
			},
			runtimeDirectory,
			hostScanOptions,
			startupRuntimeDiagnosticsPipeName,
			startupRuntimeDiagnosticsFilePath);
	}

	public static IServiceCollection StartWindowsService(
		this IServiceCollection services,
		RuntimeWindowsServiceOptions serviceOptions,
		string? runtimeDirectory = null,
		RuntimeHostScanOptions? hostScanOptions = null,
		string? startupRuntimeDiagnosticsPipeName = null,
		string? startupRuntimeDiagnosticsFilePath = null)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(serviceOptions);
		ArgumentException.ThrowIfNullOrWhiteSpace(serviceOptions.ServiceName);
		if (serviceOptions.ShutdownTimeout <= TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(nameof(serviceOptions.ShutdownTimeout));
		var displayName = string.IsNullOrWhiteSpace(serviceOptions.DisplayName)
			? serviceOptions.ServiceName
			: serviceOptions.DisplayName;

		services.Start(
			runtimeDirectory,
			hostScanOptions,
			startupRuntimeDiagnosticsPipeName,
			startupRuntimeDiagnosticsFilePath);
		services.AddWindowsService(options => options.ServiceName = serviceOptions.ServiceName);
		services.Configure<RuntimeWindowsServiceOptions>(options =>
		{
			options.ServiceName = serviceOptions.ServiceName;
			options.DisplayName = displayName;
			options.Description = serviceOptions.Description ?? "";
			options.ShutdownTimeout = serviceOptions.ShutdownTimeout;
		});
		services.AddSingleton<IConfigureOptions<WindowsServiceLifetimeOptions>, RuntimeWindowsServiceOptionsSetup>();
		if (OperatingSystem.IsWindows() && WindowsServiceHelpers.IsWindowsService())
			services.AddSingleton<IHostLifetime, RuntimeWindowsServiceLifetime>();
		return services;
	}

	public static IServiceCollection Start(
		this IServiceCollection services,
		string? runtimeDirectory = null,
		RuntimeHostScanOptions? hostScanOptions = null,
		string? startupRuntimeDiagnosticsPipeName = null,
		string? startupRuntimeDiagnosticsFilePath = null)
	{
		ArgumentNullException.ThrowIfNull(services);
		var registration = new RuntimeHostStartRegistration(
			NormalizeRuntimeDirectory(runtimeDirectory),
			hostScanOptions,
			startupRuntimeDiagnosticsPipeName ?? string.Empty,
			startupRuntimeDiagnosticsFilePath ?? string.Empty);
		lock (StartSync)
		{
			if (StartRegistrations.TryGetValue(services, out var existing))
			{
				if (existing.Equals(registration))
					return services;
				throw new RuntimeHostConfigurationException(
					$"Runtime host services were already registered with different settings. Existing pipe='{existing.PipeName}', requested pipe='{registration.PipeName}'.");
			}
			StartRegistrations.Add(services, registration);
		}
		services.AddRuntimeDiagnostics(
			runtimeDirectory,
			hostScanOptions,
			startupRuntimeDiagnosticsPipeName,
			startupRuntimeDiagnosticsFilePath);
		return services;
	}

	public static IServiceProvider Activate(this IServiceProvider provider, Assembly hostAssembly)
	{
		ArgumentNullException.ThrowIfNull(provider);
		ArgumentNullException.ThrowIfNull(hostAssembly);
		lock (ActivationSync)
		{
			if (ReferenceEquals(_activeProvider, provider))
			{
				if (!ReferenceEquals(_activeHostAssembly, hostAssembly))
					throw new RuntimeHostConfigurationException("The active Runtime provider cannot be rebound to a different host assembly.");
				return provider;
			}
			if (_activeProvider != null || _activationInProgress)
				throw new RuntimeHostConfigurationException(
					$"Only one Runtime host may be activated per process. Active host='{_activeHostAssembly?.GetName().Name ?? "activating"}', requested host='{hostAssembly.GetName().Name}'.");
			_activationInProgress = true;
		}

		try
		{
			provider.UseRuntimeDiagnostics();
			provider.BuildDiagnosticRegistries(hostAssembly);
			var execution = provider.GetService<RuntimeExecutionManager>();
			var managed = provider.GetService<RuntimeManagedRegistry>();
			var hub = provider.GetService<RuntimeDiagnosticHub>();
			lock (ActivationSync)
			{
				RuntimeInjectionContext.Configure(execution);
				RuntimeInjectionContext.ConfigureManaged(managed);
				RuntimeInjectionContext.ConfigureHub(hub);
				_activeHostAssembly = hostAssembly;
				_activeProvider = provider;
				_activationInProgress = false;
			}
			return provider;
		}
		catch
		{
			lock (ActivationSync)
				_activationInProgress = false;
			throw;
		}
	}

	private static string NormalizeRuntimeDirectory(string? runtimeDirectory) =>
		string.IsNullOrWhiteSpace(runtimeDirectory) ? string.Empty : Path.GetFullPath(runtimeDirectory);

	private sealed record RuntimeHostStartRegistration(
		string RuntimeDirectory,
		RuntimeHostScanOptions? HostScanOptions,
		string PipeName,
		string FilePath);

}

internal static class RuntimeInjectionContext
{
	private static RuntimeExecutionManager? _execution;
	private static RuntimeManagedRegistry? _managed;
	private static RuntimeDiagnosticHub? _hub;

	public static RuntimeExecutionManager? Execution => Volatile.Read(ref _execution);
	public static RuntimeManagedRegistry? Managed => Volatile.Read(ref _managed);
	public static RuntimeDiagnosticHub? Hub => Volatile.Read(ref _hub);

	public static void Configure(RuntimeExecutionManager? execution)
	{
		Volatile.Write(ref _execution, execution);
	}

	public static void ConfigureManaged(RuntimeManagedRegistry? managed)
	{
		Volatile.Write(ref _managed, managed);
	}

	public static void ConfigureHub(RuntimeDiagnosticHub? hub)
	{
		Volatile.Write(ref _hub, hub);
	}
}

public static class RuntimeInjector
{
	public static void Output(string outputPointId, string section, string kind, string message, object? payload = null)
	{
		RuntimeOutput.TracePoint(outputPointId, section, kind, message, payload);
	}

	public static void Watch(string watchPointId, object target, string typeName)
	{
		RuntimeOutput.Watch(watchPointId, target, typeName);
	}

	public static Task Break(string breakpointId, Func<bool> condition, object? context = null)
	{
		ArgumentNullException.ThrowIfNull(condition);
		return context == null
			? RuntimeOutput.BreakIf(breakpointId, condition)
			: RuntimeOutput.BreakIf(breakpointId, condition, context);
	}

	public static void Data(RuntimeDiagnosticHub hub, string targetId, object instance, RuntimeDiagnosticObjectAccess? access = null)
	{
		ArgumentNullException.ThrowIfNull(hub);
		hub.RegisterObject(targetId, instance, access);
	}

	public static RuntimeThreadRecord Thread(
		RuntimeExecutionManager execution,
		string id,
		string name,
		RuntimeExecutionLifetime lifetime,
		RuntimeThreadKind kind = RuntimeThreadKind.Custom,
		string owner = "",
		string sourceLocation = "",
		int managedThreadId = 0,
		int? nativeThreadId = null,
		IReadOnlyList<string>? tags = null,
		object? payload = null)
	{
		ArgumentNullException.ThrowIfNull(execution);
		return execution.RegisterThread(id, name, lifetime, kind, owner, sourceLocation, managedThreadId, nativeThreadId, tags, payload, "Descriptive");
	}

	public static RuntimeTaskRecord Task(
		RuntimeExecutionManager execution,
		string id,
		string name,
		RuntimeExecutionLifetime lifetime,
		string category = "",
		string threadId = "",
		string sourceLocation = "",
		string parentTaskId = "",
		string step = "",
		IReadOnlyList<string>? tags = null,
		object? payload = null)
	{
		ArgumentNullException.ThrowIfNull(execution);
		return execution.RegisterTask(id, name, lifetime, category, threadId, sourceLocation, parentTaskId, step, tags, payload, "Descriptive");
	}

	public static RProcess CreateProcess(
		ProcessStartInfo startInfo,
		string? unitId = null,
		bool startImmediately = true)
	{
		ArgumentNullException.ThrowIfNull(startInfo);
		var process = string.IsNullOrWhiteSpace(unitId)
			? new RProcess()
			: new RProcess(unitId);
		process.StartInfo = startInfo;
		if (startImmediately)
		{
			process.Start();
		}

		return process;
	}

	public static RProcess CreateProcess(
		string fileName,
		string? arguments = null,
		string? unitId = null,
		bool startImmediately = true)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
		return CreateProcess(new ProcessStartInfo(fileName, arguments ?? string.Empty), unitId, startImmediately);
	}

	public static RThread CreateThread(
		ThreadStart start,
		string? unitId = null,
		string? name = null,
		RuntimeExecutionLifetime lifetime = RuntimeExecutionLifetime.Dynamic,
		RuntimeThreadKind kind = RuntimeThreadKind.Worker,
		string owner = "",
		string sourceLocation = "",
		int stopTimeoutMilliseconds = 5000,
		bool startImmediately = true)
	{
		ArgumentNullException.ThrowIfNull(start);
		var thread = new RThread(start, unitId, name, lifetime, kind, owner, sourceLocation, stopTimeoutMilliseconds);
		if (startImmediately)
		{
			thread.Start();
		}

		return thread;
	}

	public static RThread CreateThread(
		ParameterizedThreadStart start,
		string? unitId = null,
		string? name = null,
		RuntimeExecutionLifetime lifetime = RuntimeExecutionLifetime.Dynamic,
		RuntimeThreadKind kind = RuntimeThreadKind.Worker,
		string owner = "",
		string sourceLocation = "",
		int stopTimeoutMilliseconds = 5000,
		object? parameter = null,
		bool startImmediately = true)
	{
		ArgumentNullException.ThrowIfNull(start);
		var thread = new RThread(start, unitId, name, lifetime, kind, owner, sourceLocation, stopTimeoutMilliseconds);
		if (startImmediately)
		{
			thread.Start(parameter);
		}

		return thread;
	}

	public static RTask CreateTask(
		Action<CancellationToken> action,
		string? unitId = null,
		string category = "task",
		string threadId = "",
		RuntimeExecutionLifetime lifetime = RuntimeExecutionLifetime.Dynamic,
		string sourceLocation = "",
		CancellationToken cancellationToken = default,
		bool startImmediately = true,
		bool blocksShutdown = true)
	{
		ArgumentNullException.ThrowIfNull(action);
		var task = new RTask(action, unitId, category, threadId, lifetime, sourceLocation, cancellationToken, blocksShutdown);
		if (startImmediately)
			task.Start(TaskScheduler.Default);
		return task;
	}

	public static RTask CreateTask(
		Action action,
		string? unitId = null,
		string category = "task",
		string threadId = "",
		RuntimeExecutionLifetime lifetime = RuntimeExecutionLifetime.Dynamic,
		string sourceLocation = "",
		CancellationToken cancellationToken = default,
		bool startImmediately = true,
		bool blocksShutdown = true)
	{
		ArgumentNullException.ThrowIfNull(action);
		var task = new RTask(action, cancellationToken, unitId, category, threadId, lifetime, sourceLocation, blocksShutdown);
		if (startImmediately)
		{
			task.Start(TaskScheduler.Default);
		}

		return task;
	}
}

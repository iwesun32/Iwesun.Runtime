using System.Reflection;
using System.Diagnostics;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace Iwesun.Runtime.Diagnostics;

public static class RuntimeHostTemplate
{
	public static IServiceCollection Start(
		this IServiceCollection services,
		string? runtimeDirectory = null,
		RuntimeHostScanOptions? hostScanOptions = null,
		string? startupRuntimeDiagnosticsPipeName = null,
		string? startupRuntimeDiagnosticsFilePath = null)
	{
		ArgumentNullException.ThrowIfNull(services);
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
		provider.UseRuntimeDiagnostics();
		provider.BuildDiagnosticRegistries(hostAssembly);
		RuntimeInjectionContext.Configure(provider.GetService<RuntimeExecutionManager>());
		RuntimeInjectionContext.ConfigureManaged(provider.GetService<RuntimeManagedRegistry>());
		RuntimeInjectionContext.ConfigureHub(provider.GetService<RuntimeDiagnosticHub>());
		return provider;
	}

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
		return execution.RegisterThread(id, name, lifetime, kind, owner, sourceLocation, managedThreadId, nativeThreadId, tags, payload);
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
		return execution.RegisterTask(id, name, lifetime, category, threadId, sourceLocation, parentTaskId, step, tags, payload);
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
		Action action,
		string? unitId = null,
		string category = "task",
		string threadId = "",
		RuntimeExecutionLifetime lifetime = RuntimeExecutionLifetime.Dynamic,
		string sourceLocation = "",
		CancellationToken cancellationToken = default,
		bool startImmediately = true)
	{
		ArgumentNullException.ThrowIfNull(action);
		var task = new RTask(action, cancellationToken, unitId, category, threadId, lifetime, sourceLocation);
		if (startImmediately)
		{
			task.Start(TaskScheduler.Default);
		}

		return task;
	}
}

using System.Reflection;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace Iwesun.Runtime.Diagnostics;

public static class RuntimeHostTemplate
{
	public static IServiceCollection Start(
		this IServiceCollection services,
		string? runtimeDirectory = null,
		RuntimeHostScanOptions? hostScanOptions = null)
	{
		ArgumentNullException.ThrowIfNull(services);
		services.AddRuntimeDiagnostics(runtimeDirectory, hostScanOptions);
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
		return provider;
	}

	public static void Stop(
		RuntimeStateManager stateManager,
		RuntimeExecutionManager execution,
		string threadId,
		string taskId,
		bool graceful = true,
		string? step = null,
		object? payload = null)
	{
		ArgumentNullException.ThrowIfNull(stateManager);
		ArgumentNullException.ThrowIfNull(execution);

		stateManager.SetStop();
		var threadState = graceful ? RuntimeThreadState.Completed : RuntimeThreadState.Faulted;
		var taskState = graceful ? RuntimeTaskState.Completed : RuntimeTaskState.Faulted;
		execution.SetTaskState(taskId, taskState, step: step ?? (graceful ? "completed" : "faulted"), threadId: threadId, payload: payload);
		execution.SetThreadState(threadId, threadState, currentTaskId: taskId, payload: payload);
	}
}

internal static class RuntimeInjectionContext
{
	private static RuntimeExecutionManager? _execution;
	private static RuntimeManagedRegistry? _managed;

	public static RuntimeExecutionManager? Execution => Volatile.Read(ref _execution);
	public static RuntimeManagedRegistry? Managed => Volatile.Read(ref _managed);

	public static void Configure(RuntimeExecutionManager? execution)
	{
		Volatile.Write(ref _execution, execution);
	}

	public static void ConfigureManaged(RuntimeManagedRegistry? managed)
	{
		Volatile.Write(ref _managed, managed);
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
}
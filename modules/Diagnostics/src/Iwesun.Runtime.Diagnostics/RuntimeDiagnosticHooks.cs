using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Iwesun.Runtime.Data;

namespace Iwesun.Runtime.Diagnostics;

/// <summary>
/// Event hook registry with weak-reference protection.
/// Allows the CLI to dynamically attach/detach handlers to
/// events declared via [assembly: DiagnosticHookableEvent].
/// Instance events use WeakReference&lt;T&gt; to avoid blocking GC.
/// </summary>
public sealed class RuntimeDiagnosticHooks
{
	private readonly ConcurrentDictionary<string, ActiveHook> _active = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, HookableEvent> _available = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<Type, WeakReference<object>> _instances = new();
	private readonly Timer _gcCleanupTimer;

	public RuntimeDiagnosticHooks()
	{
		_gcCleanupTimer = new Timer(CheckGcCleanup, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
	}

	// ── Registration ──────────────────────────────────────

	/// <summary>Register a hookable event from assembly attributes.</summary>
	public void RegisterAvailable(string id, Type targetType, string eventName)
	{
		_available[id] = new HookableEvent(id, targetType, eventName);
	}

	public void RegisterInstance(object instance)
	{
		ArgumentNullException.ThrowIfNull(instance);
		_instances[instance.GetType()] = new WeakReference<object>(instance);
	}

	/// <summary>Snapshot of available (not yet attached) hooks.</summary>
	public IReadOnlyList<HookableEvent> AvailableHooks()
		=> _available.Values.OrderBy(h => h.Id, StringComparer.OrdinalIgnoreCase).ToArray();

	/// <summary>Snapshot of active (attached) hooks.</summary>
	public IReadOnlyList<ActiveHook> ActiveHooks()
		=> _active.Values.OrderBy(h => h.HookId, StringComparer.OrdinalIgnoreCase).ToArray();

	// ── Attach / Detach ───────────────────────────────────

	/// <summary>
	/// Attach a handler to a hookable event.
	/// </summary>
	/// <param name="hookId">Must match a registered hookable event.</param>
	/// <param name="instance">Optional instance for instance events. Null for static events.</param>
	public bool Attach(string hookId, object? instance = null)
	{
		if (!_available.TryGetValue(hookId, out var hook))
			return false;

		if (_active.ContainsKey(hookId))
			return false; // Already attached

		var flags = BindingFlags.Public | BindingFlags.NonPublic |
					BindingFlags.Instance | BindingFlags.Static;
		var eventInfo = hook.TargetType.GetEvent(hook.EventName, flags);
		if (eventInfo == null)
			return false;
		if (eventInfo.AddMethod?.IsStatic != true && instance == null)
		{
			_instances.TryGetValue(hook.TargetType, out var weakInstance);
			weakInstance?.TryGetTarget(out instance);
			if (instance == null)
				return false;
		}

		var handler = BuildHandler(hook.Id, hook.EventName, hook.TargetType.FullName!, eventInfo.EventHandlerType!);
		eventInfo.AddMethod!.Invoke(instance, new[] { handler });

		_active[hookId] = new ActiveHook
		{
			HookId = hook.Id,
			EventName = hook.EventName,
			TargetTypeName = hook.TargetType.FullName!,
			IsStatic = eventInfo.AddMethod?.IsStatic == true,
			WeakTarget = instance != null ? new WeakReference<object>(instance) : null,
			Handler = handler,
			AttachedAt = DateTimeOffset.UtcNow
		};

		DiagnosticSwitchboard.ReportPoint(
			RuntimeStaticInjectorCatalog.ComposePrefixedId(RuntimeInjectorIdPatterns.HookAttachedPrefix, hookId), "hooks", "attach",
			$"Hook attached: {hookId}", new { hookId, hook.EventName, hook.TargetType.FullName, isStatic = instance == null });

		return true;
	}

	/// <summary>Detach a previously attached hook.</summary>
	public bool Detach(string hookId)
	{
		if (!_active.TryRemove(hookId, out var active))
			return false;

		if (!_available.TryGetValue(hookId, out var hook))
			return false;

		var flags = BindingFlags.Public | BindingFlags.NonPublic |
					BindingFlags.Instance | BindingFlags.Static;
		var eventInfo = hook.TargetType.GetEvent(hook.EventName, flags);
		if (eventInfo == null)
			return false;

		object? instance = null;
		active.WeakTarget?.TryGetTarget(out instance);

		eventInfo.RemoveMethod!.Invoke(instance, new[] { active.Handler });

		DiagnosticSwitchboard.ReportPoint(
			RuntimeStaticInjectorCatalog.ComposePrefixedId(RuntimeInjectorIdPatterns.HookDetachedPrefix, hookId), "hooks", "detach",
			$"Hook detached: {hookId}", new { hookId });

		return true;
	}

	// ── Handler building ──────────────────────────────────

	private Delegate BuildHandler(string hookId, string eventName, string typeName, Type delegateType)
	{
		// Build a handler that forwards the event through the switchboard.
		// Supports both EventHandler and EventHandler<TEventArgs>.
		var invokeMethod = delegateType.GetMethod("Invoke")!;
		var parameters = invokeMethod.GetParameters();

		// We build: (sender, args) => OnHookFired(hookId, sender, args)
		var senderParam = Expression.Parameter(typeof(object), "sender");
		var argsParam = Expression.Parameter(typeof(object), "args");
		var hookIdExpr = Expression.Constant(hookId);
		var eventNameExpr = Expression.Constant(eventName);
		var typeNameExpr = Expression.Constant(typeName);

		var onHookFiredMethod = typeof(RuntimeDiagnosticHooks).GetMethod(
			nameof(OnHookFired), BindingFlags.NonPublic | BindingFlags.Static)!;

		var callExpr = Expression.Call(onHookFiredMethod, hookIdExpr, eventNameExpr, typeNameExpr, senderParam, argsParam);

		// Cast parameters to match the delegate signature
		var castSender = Expression.Convert(senderParam, parameters[0].ParameterType);
		var castArgs = parameters.Length > 1
			? Expression.Convert(argsParam, parameters[1].ParameterType)
			: null;

		var lambdaParams = new List<ParameterExpression> { senderParam };
		if (parameters.Length > 1)
			lambdaParams.Add(argsParam);

		// We need to call OnHookFired before the delegate body
		// For simplicity, build a block: call OnHookFired; ignore return
		Expression body = Expression.Block(
			callExpr,
			parameters.Length > 1
				? Expression.Empty()
				: Expression.Empty());

		return Expression.Lambda(delegateType, body, lambdaParams).Compile();
	}

	private static void OnHookFired(string hookId, string eventName, string typeName, object sender, object args)
	{
		if (!RuntimeOutputSwitch.Enabled)
			return;

		DiagnosticSwitchboard.ReportPoint(
			RuntimeStaticInjectorCatalog.ComposePrefixedId(RuntimeInjectorIdPatterns.HookFiredPrefix, hookId), "hooks", "fired",
			$"Hook fired: {hookId} ({typeName}.{eventName})",
			new
			{
				hookId,
				eventName,
				typeName,
				senderType = sender?.GetType().FullName,
				sender = TrySnapshot(sender),
				args = TrySnapshot(args)
			});
	}

	private static object? TrySnapshot(object? target)
	{
		if (target == null) return null;
		try
		{
			var type = target.GetType();
			var values = new SortedDictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
			foreach (var prop in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
			{
				if (prop.GetIndexParameters().Length == 0)
				{
					try { values[prop.Name] = prop.GetValue(target); }
					catch { values[prop.Name] = "<error>"; }
				}
			}
			return new { type = type.FullName, values };
		}
		catch
		{
			return new { type = target.GetType().FullName, error = "Snapshot failed" };
		}
	}

	// ── GC cleanup ────────────────────────────────────────

	private void CheckGcCleanup(object? _)
	{
		foreach (var (id, active) in _active)
		{
			if (!active.IsStatic && active.WeakTarget != null && !active.WeakTarget.TryGetTarget(out _))
			{
				Detach(id);
				DiagnosticSwitchboard.ReportPoint(
					RuntimeStaticOutputPoint.HookGcCleaned, "hooks", "gc",
					$"Hook auto-detached (target GC'd): {id}", new { hookId = id });
			}
		}
	}

	public void Dispose()
	{
		_gcCleanupTimer.Dispose();
		foreach (var id in _active.Keys.ToArray())
		{
			Detach(id);
		}
	}
}

// ── Hook data types ───────────────────────────────────────

public sealed class HookableEvent
{
	public string Id { get; }
	public Type TargetType { get; }
	public string EventName { get; }

	public HookableEvent(string id, Type targetType, string eventName)
	{
		Id = id;
		TargetType = targetType;
		EventName = eventName;
	}
}

public sealed class ActiveHook
{
	public string HookId { get; init; } = "";
	public string EventName { get; init; } = "";
	public string TargetTypeName { get; init; } = "";
	public bool IsStatic { get; init; }
	public WeakReference<object>? WeakTarget { get; init; }
	public Delegate? Handler { get; init; }
	public long HitCount;
	public DateTimeOffset AttachedAt { get; init; } = DateTimeOffset.UtcNow;
	public DateTimeOffset? LastFiredAt;
}

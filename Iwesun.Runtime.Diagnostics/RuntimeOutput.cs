namespace Iwesun.Runtime.Diagnostics;

/// <summary>
/// Runtime diagnostic output facade for direct probes and future formal log paths.
/// </summary>
public static class RuntimeOutput
{
	public static bool Enabled => RuntimeOutputSwitch.Enabled;

	public static void Log(RuntimeOutputTextHandler message)
	{
		if (!RuntimeOutputSwitch.Enabled)
			return;

		DiagnosticSwitchboard.ReportConsole(message.ToString());
	}

	public static void Log(string message)
	{
		if (!RuntimeOutputSwitch.Enabled)
			return;

		DiagnosticSwitchboard.ReportConsole(message);
	}

	public static void Log(Func<string> messageFactory)
	{
		if (!RuntimeOutputSwitch.Enabled)
			return;

		DiagnosticSwitchboard.ReportConsole(messageFactory());
	}

	public static void Trace(string section, string kind, string message, object? payload = null)
	{
		if (!RuntimeOutputSwitch.Enabled)
			return;

		DiagnosticSwitchboard.ReportTrace(section, kind, message, payload);
	}

	public static void TracePoint(string outputPointId, string section, string kind, string message, object? payload = null)
	{
		if (!RuntimeOutputSwitch.Enabled)
			return;

		DiagnosticSwitchboard.ReportPoint(outputPointId, section, kind, message, payload);
	}

	public static void Error(string message, object? payload = null)
	{
		if (!RuntimeOutputSwitch.Enabled)
			return;

		DiagnosticSwitchboard.ReportPoint("runtime.error", "error", "error", message, payload);
	}

	// ── Breakpoints (new) ─────────────────────────────────────────────

	/// <summary>
	/// Unconditional logical breakpoint. If the breakpoint is not enabled, returns immediately.
	/// If enabled, pauses the current call chain until CLI sends a resume signal.
	/// </summary>
	public static Task BreakIf(string breakpointId)
	{
		return BreakpointHelper.BreakIfAsync(breakpointId);
	}

	/// <summary>
	/// Conditional logical breakpoint. The condition is only evaluated when the breakpoint is enabled.
	/// </summary>
	public static Task BreakIf(string breakpointId, Func<bool> condition)
	{
		return BreakpointHelper.BreakIfAsync(breakpointId, condition);
	}

	/// <summary>
	/// Conditional logical breakpoint with context snapshot sent to CLI on hit.
	/// </summary>
	public static Task BreakIf(string breakpointId, Func<bool> condition, object context)
	{
		return BreakpointHelper.BreakIfAsync(breakpointId, condition, context);
	}

	/// <summary>
	/// Object watch point — captures the current object state and routes it
	/// through the diagnostic pipeline. Use when event hooks are not feasible.
	/// </summary>
	public static void Watch(string watchPointId, object target, string typeName)
	{
		if (!RuntimeOutputSwitch.Enabled)
			return;

		DiagnosticSwitchboard.ReportPoint(
			watchPointId,
			"watchpoints",
			"watch",
			$"Watch point: {watchPointId}",
			new { type = typeName, snapshot = TrySnapshot(target) });
	}

	private static object? TrySnapshot(object target)
	{
		try
		{
			var type = target.GetType();
			var values = new SortedDictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
			foreach (var prop in type.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
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
}

/// <summary>
/// Internal helper that bridges the static RuntimeOutput.BreakIf to the
/// singleton RuntimeDiagnosticBreakpoints instance.  This indirection keeps
/// the static API testable.
/// </summary>
internal static class BreakpointHelper
{
	private static RuntimeDiagnosticBreakpoints? _breakpoints;

	public static void SetInstance(RuntimeDiagnosticBreakpoints breakpoints)
	{
		_breakpoints = breakpoints ?? throw new ArgumentNullException(nameof(breakpoints));
	}

	public static Task BreakIfAsync(string breakpointId, Func<bool>? condition = null, object? context = null)
	{
		if (_breakpoints == null)
			return Task.CompletedTask;
		return _breakpoints.WaitAsync(breakpointId, condition, context);
	}
}

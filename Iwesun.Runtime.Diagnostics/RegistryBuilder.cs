using System.Reflection;

namespace Iwesun.Runtime.Diagnostics;

/// <summary>
/// Scans host assembly attributes at startup and builds the three
/// diagnostic registries (watch points, breakpoints, hookable events) in memory.
/// </summary>
public static class RegistryBuilder
{
	/// <summary>
	/// Build all registries from the host assembly's diagnostic attributes.
	/// </summary>
	public static RegistrySnapshot Build(
		Assembly hostAssembly,
		RuntimeDiagnosticBreakpoints? breakpoints = null,
		RuntimeDiagnosticHooks? hooks = null)
	{
		var watchPoints = new List<DiagnosticWatchPointAttribute>();
		var bpAttrs = new List<DiagnosticBreakpointAttribute>();
		var hookAttrs = new List<DiagnosticHookableEventAttribute>();

		foreach (var attr in hostAssembly.GetCustomAttributes())
		{
			switch (attr)
			{
				case DiagnosticWatchPointAttribute wp:
					watchPoints.Add(wp);
					break;
				case DiagnosticBreakpointAttribute bp:
					bpAttrs.Add(bp);
					if (breakpoints != null)
					{
						breakpoints.Register(new BreakpointState(bp.Id)
						{
							Section = bp.Section,
							Description = bp.Description,
							SourceLocation = bp.SourceLocation,
							TimeoutMs = bp.TimeoutMs,
							HitCountTarget = bp.HitCountTarget,
							Enabled = false
						});
					}
					break;
				case DiagnosticHookableEventAttribute he:
					hookAttrs.Add(he);
					if (hooks != null)
					{
						hooks.RegisterAvailable(
							he.Id, he.TargetType!, he.EventName);
					}
					break;
			}
		}

		return new RegistrySnapshot(
			watchPoints.Select(wp => new WatchPointEntry(
				wp.Id, wp.Section, wp.Kind, wp.Description, wp.SourceLocation, false)).ToArray(),
			bpAttrs.Select(bp => new BreakpointEntry(
				bp.Id, bp.Section, bp.Description, bp.SourceLocation,
				bp.TimeoutMs, bp.HitCountTarget, false)).ToArray(),
			hookAttrs.Select(he => new HookEntry(
				he.Id, he.EventName, he.TargetType?.FullName ?? "", false)).ToArray()
		);
	}

	/// <summary>
	/// Build registries and also initialize the DiagnosticPipePrefix from the host assembly.
	/// </summary>
	public static RegistrySnapshot BuildAndInitialize(Assembly hostAssembly,
		RuntimeDiagnosticBreakpoints? breakpoints = null,
		RuntimeDiagnosticHooks? hooks = null)
	{
		DiagnosticPipePrefix.InitializeFromAssembly(hostAssembly);
		return Build(hostAssembly, breakpoints, hooks);
	}
}

// ── Registry Snapshot ───────────────────────────────────────

public sealed record RegistrySnapshot(
	IReadOnlyList<WatchPointEntry> WatchPoints,
	IReadOnlyList<BreakpointEntry> Breakpoints,
	IReadOnlyList<HookEntry> Hooks);

public sealed record WatchPointEntry(
	string Id, string Section, string Kind, string Description,
	string SourceLocation, bool Enabled);

public sealed record BreakpointEntry(
	string Id, string Section, string Description, string SourceLocation,
	int TimeoutMs, int HitCountTarget, bool Enabled);

public sealed record HookEntry(
	string HookId, string EventName, string TargetTypeName, bool IsAttached);
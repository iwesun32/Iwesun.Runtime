using System.Collections.Concurrent;
using System.IO.MemoryMappedFiles;
using System.Runtime.Versioning;
using Iwesun.Runtime.Data;

#if DEBUG
namespace Iwesun.Runtime.Diagnostics;

/// <summary>
/// Collaborative logical breakpoint registry.
/// DEBUG builds: each breakpoint uses a named OS Semaphore (cross-process) and a
/// MemoryMappedFile for shared state (Enabled / IsWaiting / HitCount).
/// Release builds: registration and Wait methods are no-ops.
/// Not a debugger breakpoint — only the calling call-chain is paused (await), other threads run freely.
/// </summary>
public sealed class RuntimeDiagnosticBreakpoints
{
	private readonly ConcurrentDictionary<string, BreakpointState> _breakpoints = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, RuntimeNumericBindingState> _numericBindings = new(StringComparer.OrdinalIgnoreCase);

	public RuntimeDiagnosticBreakpoints() { }

	// ── Registration ──────────────────────────────────────

	public void Register(BreakpointState breakpoint)
	{
		_breakpoints[breakpoint.Id] = breakpoint;
	}

	public bool TryGet(string id, out BreakpointState breakpoint)
	{
		return _breakpoints.TryGetValue(id, out breakpoint!);
	}

	public bool SetNumericBinding(string breakpointId, string predicateId)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(breakpointId);
		ArgumentException.ThrowIfNullOrWhiteSpace(predicateId);
		if (!RuntimeNumericPredicateCatalog.TryGet(predicateId, out _))
		{
			return false;
		}

		var state = new RuntimeNumericBindingState(
			Mode: RuntimeNumericBindingMode.StaticPredicate,
			PredicateId: predicateId.Trim(),
			Threshold1: 0,
			Threshold2: 0,
			UpdatedAt: DateTimeOffset.UtcNow);
		_numericBindings[breakpointId.Trim()] = state;
		return true;
	}

	public bool SetNumericThresholdBinding(string breakpointId, string operatorId, double threshold1, double threshold2 = 0)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(breakpointId);
		ArgumentException.ThrowIfNullOrWhiteSpace(operatorId);
		var normalizedOperator = RuntimeNumericThresholdOperators.Normalize(operatorId);
		if (normalizedOperator == null)
		{
			return false;
		}

		var state = new RuntimeNumericBindingState(
			Mode: RuntimeNumericBindingMode.Threshold,
			PredicateId: normalizedOperator,
			Threshold1: threshold1,
			Threshold2: threshold2,
			UpdatedAt: DateTimeOffset.UtcNow);
		_numericBindings[breakpointId.Trim()] = state;
		return true;
	}

	public bool RemoveNumericBinding(string breakpointId)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(breakpointId);
		return _numericBindings.TryRemove(breakpointId.Trim(), out _);
	}

	public IReadOnlyList<RuntimeNumericBindingSnapshot> SnapshotNumericBindings()
	{
		return _numericBindings
			.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
			.Select(x => new RuntimeNumericBindingSnapshot(x.Key, x.Value.Mode.ToString(), x.Value.PredicateId, x.Value.Threshold1, x.Value.Threshold2, x.Value.UpdatedAt))
			.ToArray();
	}

	// ── Business code entry point ─────────────────────────

	// ── Business code entry point ─────────────────────────────────────────────────────────────

	/// <summary>
	/// Called by business code (DEBUG only). If the breakpoint is disabled or condition is false,
	/// returns immediately (zero overhead). If enabled and condition met, waits for CLI resume signal.
	/// Uses a named OS Semaphore so the resume can come from any process.
	/// </summary>
	public async Task WaitAsync(string id, Func<bool>? condition = null, object? context = null, CancellationToken ct = default)
	{
#if DEBUG
		if (!RuntimeOutputSwitch.Enabled)
			return;

		if (!_breakpoints.TryGetValue(id, out var bp) || !bp.SharedState.EnabledBool)
			return;

		// Check hit count target
		var hitCount = Interlocked.Increment(ref bp.HitCount);
		bp.SharedState.HitCount = (int)bp.HitCount;   // sync to MMF
		if (bp.HitCountTarget > 0 && hitCount < bp.HitCountTarget)
			return;

		// Evaluate condition (only when breakpoint is enabled)
		if (condition != null && !condition())
			return;

		// Mark as waiting and capture snapshot
		bp.SharedState.IsWaiting = 1;
		bp.LastHitAt = DateTimeOffset.UtcNow;
		bp.LastContext = context;

		// Publish breakpoint hit event through the switchboard
		DiagnosticSwitchboard.ReportPoint(
			RuntimeStaticInjectorCatalog.ComposePrefixedId(RuntimeInjectorIdPatterns.BreakpointHitPrefix, id),
			"breakpoints",
			"hit",
			$"Breakpoint hit: {id}",
			new
			{
				breakpointId = id,
				bp.Section,
				bp.Description,
				hitCount = bp.HitCount,
				context
			});

		// Wait indefinitely for manual resume signal (named OS Semaphore — cross-process capable)
		try
		{
			await Task.Run(() => bp.Signal.WaitOne(), ct).ConfigureAwait(false);
		}
		catch (OperationCanceledException) { }

		bp.SharedState.IsWaiting = 0;
#else
		await Task.CompletedTask;
#endif
	}

	public Task WaitNumericAsync(string id, double value1, double value2, double value3 = 0, object? context = null, CancellationToken ct = default)
	{
		if (!_numericBindings.TryGetValue(id, out var binding))
		{
			return Task.CompletedTask;
		}

		if (!RuntimeNumericPredicateCatalog.TryGet(binding.PredicateId, out var predicate))
		{
			if (!RuntimeNumericBindingEvaluator.TryGetPredicate(binding, out predicate))
			{
				return Task.CompletedTask;
			}
		}

		var numericContext = new
		{
			value1,
			value2,
			value3,
			predicateId = binding.PredicateId,
			context
		};
		return WaitAsync(id, () => predicate(value1, value2, value3), numericContext, ct);
	}

	// ── CLI control ───────────────────────────────────────

	public bool Enable(string id)
	{
		if (!_breakpoints.TryGetValue(id, out var bp))
			return false;
		bp.SharedState.Enabled = 1;
		return true;
	}

	public bool Disable(string id)
	{
		if (!_breakpoints.TryGetValue(id, out var bp))
			return false;
		bp.SharedState.Enabled = 0;
		// If currently waiting, release immediately
		if (bp.SharedState.IsWaiting == 1)
			Resume(id);
		return true;
	}

	public bool Resume(string id)
	{
		if (!_breakpoints.TryGetValue(id, out var bp) || bp.SharedState.IsWaiting == 0)
			return false;
#if DEBUG
		try { bp.Signal.Release(); } catch (SemaphoreFullException) { }
#endif
		return true;
	}

	public void ResumeAll()
	{
		foreach (var (_, bp) in _breakpoints)
		{
			if (bp.SharedState.IsWaiting == 1)
			{
#if DEBUG
				try { bp.Signal.Release(); } catch (SemaphoreFullException) { }
#endif
			}
		}
	}

	// ── Snapshot ──────────────────────────────────────────

	public IReadOnlyList<BreakpointSnapshot> Snapshot()
	{
		return _breakpoints.Values
			.OrderBy(b => b.Id, StringComparer.OrdinalIgnoreCase)
			.Select(b => new BreakpointSnapshot(
				b.Id, b.Section, b.Description, b.SourceLocation,
				b.SharedState.EnabledBool, b.HitCountTarget,
				b.HitCount, b.SharedState.IsWaiting == 1, b.LastHitAt))
			.ToArray();
	}

	// ── Timeout management ────────────────────────────────

	// ── Cleanup

	public void Dispose()
	{
		ResumeAll();
		foreach (var (_, bp) in _breakpoints)
		{
			bp.Dispose();
		}
	}
}

// ── Breakpoint State ─────────────────────────────────────

// ── Shared-memory layout (one MMF per breakpoint, 16 bytes) ──────────────────────────────────
//
//  Offset  Size  Field
//  0       4     Enabled   (int32, 0=disabled 1=enabled) — writable by any process
//  4       4     IsWaiting (int32, 0=idle 1=waiting)
//  8       4     HitCount  (int32)
//  12      4     reserved
//
// Named Semaphore: "Local\iwrt.bp.<safe-id>"
// Named MMF:       "Local\iwrt.bpstate.<safe-id>"

/// <summary>
/// Provides typed, safe access to the MemoryMappedFile shared state for a single breakpoint.
/// Any process that opens the same MMF name gets a consistent view.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class BreakpointSharedState : IDisposable
{
	private const int OffsetEnabled   = 0;
	private const int OffsetIsWaiting = 4;
	private const int OffsetHitCount  = 8;
	private const long MmfSize        = 16;

	private readonly MemoryMappedFile _mmf;
	private readonly MemoryMappedViewAccessor _view;

	internal BreakpointSharedState(string mmfName)
	{
		_mmf  = MemoryMappedFile.CreateNew(null, MmfSize);
		_view = _mmf.CreateViewAccessor(0, MmfSize);
	}

	public int Enabled
	{
		get => _view.ReadInt32(OffsetEnabled);
		set => _view.Write(OffsetEnabled, value);
	}

	public int IsWaiting
	{
		get => _view.ReadInt32(OffsetIsWaiting);
		set => _view.Write(OffsetIsWaiting, value);
	}

	public int HitCount
	{
		get => _view.ReadInt32(OffsetHitCount);
		set => _view.Write(OffsetHitCount, value);
	}

	/// <summary>Helper: Enabled != 0</summary>
	public bool EnabledBool => Enabled != 0;

	public void Dispose()
	{
		_view.Dispose();
		_mmf.Dispose();
	}
}

// ── Breakpoint State ─────────────────────────────────────────────────────────────────────────

[SupportedOSPlatform("windows")]
public sealed class BreakpointState : IDisposable
{
	public string Id { get; }
	public string Section { get; init; } = "general";
	public string Description { get; init; } = "";
	public string SourceLocation { get; init; } = "";
	public int HitCountTarget { get; init; }  // 0 = every hit
	public long HitCount;
	public DateTimeOffset? LastHitAt;
	public object? LastContext;

	/// <summary>
	/// Shared memory state — readable/writable from any process that opens the same MMF.
	/// Contains Enabled, IsWaiting, HitCount.
	/// </summary>
	public BreakpointSharedState SharedState { get; }

#if DEBUG
	/// <summary>Named OS Semaphore — cross-process resume signal.</summary>
	public Semaphore Signal { get; }
#endif

	private static string SafeName(string id)
		=> id.Replace('\\', '_').Replace('/', '_').Replace(' ', '_');

	public BreakpointState(string id)
	{
		Id = id ?? throw new ArgumentNullException(nameof(id));
		SharedState = new BreakpointSharedState(id);
#if DEBUG
		Signal = new Semaphore(0, 1);
#endif
	}

	public void Dispose()
	{
		SharedState.Dispose();
#if DEBUG
		Signal.Dispose();
#endif
	}
}

// ── Snapshot ─────────────────────────────────────────────

public sealed record BreakpointSnapshot(
	string Id,
	string Section,
	string Description,
	string SourceLocation,
	bool Enabled,
	int HitCountTarget,
	long HitCount,
	bool IsWaiting,
	DateTimeOffset? LastHitAt);

internal enum RuntimeNumericBindingMode
{
	StaticPredicate,
	Threshold
}

internal sealed record RuntimeNumericBindingState(
	RuntimeNumericBindingMode Mode,
	string PredicateId,
	double Threshold1,
	double Threshold2,
	DateTimeOffset UpdatedAt);

internal static class RuntimeNumericBindingEvaluator
{
	public static bool TryGetPredicate(RuntimeNumericBindingState binding, out RuntimeNumericPredicate predicate)
	{
		if (binding.Mode == RuntimeNumericBindingMode.StaticPredicate)
		{
			if (RuntimeNumericPredicateCatalog.TryGet(binding.PredicateId, out var staticPredicate))
			{
				predicate = staticPredicate;
				return true;
			}

			predicate = default!;
			return false;
		}

		predicate = binding.PredicateId.Trim().ToLowerInvariant() switch
		{
			"gt" or "greater-than" => (v1, _, _) => v1 > binding.Threshold1,
			"ge" or "gte" or "greater-than-or-equal" => (v1, _, _) => v1 >= binding.Threshold1,
			"lt" or "less-than" => (v1, _, _) => v1 < binding.Threshold1,
			"le" or "lte" or "less-than-or-equal" => (v1, _, _) => v1 <= binding.Threshold1,
			"between" => (v1, _, _) => v1 >= Math.Min(binding.Threshold1, binding.Threshold2) && v1 <= Math.Max(binding.Threshold1, binding.Threshold2),
			"outside" => (v1, _, _) => v1 < Math.Min(binding.Threshold1, binding.Threshold2) || v1 > Math.Max(binding.Threshold1, binding.Threshold2),
			"delta-le" or "within" => (v1, v2, _) => Math.Abs(v1 - v2) <= Math.Abs(binding.Threshold1),
			"delta-gt" or "beyond" => (v1, v2, _) => Math.Abs(v1 - v2) > Math.Abs(binding.Threshold1),
			_ => null!
		};

		return predicate != null;
	}
}

internal static class RuntimeNumericThresholdOperators
{
	public static IReadOnlyList<string> SupportedOperators { get; } =
	[
		"gt", "ge", "lt", "le", "between", "outside", "delta-le", "delta-gt"
	];

	public static string? Normalize(string operatorId)
	{
		switch (operatorId.Trim().ToLowerInvariant())
		{
			case "gt":
			case "greater-than":
				return "gt";
			case "ge":
			case "gte":
			case "greater-than-or-equal":
				return "ge";
			case "lt":
			case "less-than":
				return "lt";
			case "le":
			case "lte":
			case "less-than-or-equal":
				return "le";
			case "between":
				return "between";
			case "outside":
				return "outside";
			case "delta-le":
			case "within":
				return "delta-le";
			case "delta-gt":
			case "beyond":
				return "delta-gt";
			default:
				return null;
		}
	}
}
#endif

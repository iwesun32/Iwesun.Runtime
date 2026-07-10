using System.Collections.Concurrent;
using Iwesun.Runtime.Data;

namespace Iwesun.Runtime.Diagnostics;

/// <summary>
/// Collaborative logical breakpoint registry.
/// Each breakpoint has its own SemaphoreSlim signal for one-to-one await/resume.
/// Not a debugger breakpoint — only the calling call-chain is paused (await), other threads run freely.
/// </summary>
public sealed class RuntimeDiagnosticBreakpoints
{
	private readonly ConcurrentDictionary<string, BreakpointState> _breakpoints = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, RuntimeNumericBindingState> _numericBindings = new(StringComparer.OrdinalIgnoreCase);
	private readonly Timer _timeoutTimer;
	private readonly TimeSpan _timeoutCheckInterval = TimeSpan.FromSeconds(5);

	public RuntimeDiagnosticBreakpoints()
	{
		_timeoutTimer = new Timer(CheckTimeouts, null, _timeoutCheckInterval, _timeoutCheckInterval);
	}

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

	/// <summary>
	/// Called by business code. If the breakpoint is disabled or condition is false, returns immediately (zero overhead).
	/// If enabled and condition met, waits for CLI resume signal.
	/// </summary>
	public async Task WaitAsync(string id, Func<bool>? condition = null, object? context = null, CancellationToken ct = default)
	{
		if (!RuntimeOutputSwitch.Enabled)
			return;

		if (!_breakpoints.TryGetValue(id, out var bp) || !bp.Enabled)
			return;

		// Check hit count target
		var hitCount = Interlocked.Increment(ref bp.HitCount);
		if (bp.HitCountTarget > 0 && hitCount < bp.HitCountTarget)
			return;

		// Evaluate condition (only when breakpoint is enabled)
		if (condition != null && !condition())
			return;

		// Mark as waiting and capture snapshot
		bp.IsWaiting = true;
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

		// Wait for resume signal or timeout
		try
		{
			using var linkedCts = ct != default
				? CancellationTokenSource.CreateLinkedTokenSource(ct, bp.TimeoutToken)
				: CancellationTokenSource.CreateLinkedTokenSource(bp.TimeoutToken);

			await bp.Signal.WaitAsync(linkedCts.Token);
		}
		catch (OperationCanceledException)
		{
			// Timeout or cancellation — auto-resume
			DiagnosticSwitchboard.ReportPoint(
				RuntimeStaticInjectorCatalog.ComposePrefixedId(RuntimeInjectorIdPatterns.BreakpointTimeoutPrefix, id),
				"breakpoints",
				"timeout",
				$"Breakpoint timed out: {id}",
				new { breakpointId = id, bp.TimeoutMs });
		}
		finally
		{
			bp.IsWaiting = false;
			bp.ResetTimeoutToken();
		}
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
		bp.Enabled = true;
		return true;
	}

	public bool Disable(string id)
	{
		if (!_breakpoints.TryGetValue(id, out var bp))
			return false;
		bp.Enabled = false;
		// If currently waiting, release immediately
		if (bp.IsWaiting)
			Resume(id);
		return true;
	}

	public bool Resume(string id)
	{
		if (!_breakpoints.TryGetValue(id, out var bp) || !bp.IsWaiting)
			return false;
		try { bp.Signal.Release(); } catch (SemaphoreFullException) { }
		return true;
	}

	public void ResumeAll()
	{
		foreach (var (id, bp) in _breakpoints)
		{
			if (bp.IsWaiting)
			{
				try { bp.Signal.Release(); } catch (SemaphoreFullException) { }
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
				b.Enabled, b.HitCountTarget, b.TimeoutMs, b.AutoResume,
				b.HitCount, b.IsWaiting, b.LastHitAt))
			.ToArray();
	}

	// ── Timeout management ────────────────────────────────

	private void CheckTimeouts(object? _)
	{
		foreach (var (_, bp) in _breakpoints)
		{
			if (bp.IsWaiting && bp.TimeoutMs > 0 && bp.LastHitAt != null)
			{
				var elapsed = (DateTimeOffset.UtcNow - bp.LastHitAt.Value).TotalMilliseconds;
				if (elapsed >= bp.TimeoutMs)
				{
					bp.CancelTimeout();
				}
			}
		}
	}

	// ── Cleanup ───────────────────────────────────────────

	public void Dispose()
	{
		_timeoutTimer.Dispose();
		ResumeAll();
		foreach (var (_, bp) in _breakpoints)
		{
			bp.Signal.Dispose();
			bp.CancelTimeout();
		}
	}
}

// ── Breakpoint State ─────────────────────────────────────

public sealed class BreakpointState
{
	public string Id { get; }
	public string Section { get; init; } = "general";
	public string Description { get; init; } = "";
	public string SourceLocation { get; init; } = "";
	public bool Enabled { get; set; }
	public int HitCountTarget { get; init; }  // 0 = every hit
	public int TimeoutMs { get; init; } = 30_000;
	public bool AutoResume { get; init; } = true;
	public long HitCount;
	public bool IsWaiting;
	public DateTimeOffset? LastHitAt;
	public object? LastContext;
	public SemaphoreSlim Signal { get; } = new(0, 1);
	public CancellationToken TimeoutToken => _timeoutCts?.Token ?? CancellationToken.None;

	private CancellationTokenSource? _timeoutCts;

	public BreakpointState(string id)
	{
		Id = id ?? throw new ArgumentNullException(nameof(id));
		ResetTimeoutToken();
	}

	public void ResetTimeoutToken()
	{
		_timeoutCts?.Cancel();
		_timeoutCts?.Dispose();
		_timeoutCts = TimeoutMs > 0
			? new CancellationTokenSource(TimeoutMs)
			: new CancellationTokenSource();
	}

	public void CancelTimeout()
	{
		try { _timeoutCts?.Cancel(); } catch (ObjectDisposedException) { }
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
	int TimeoutMs,
	bool AutoResume,
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
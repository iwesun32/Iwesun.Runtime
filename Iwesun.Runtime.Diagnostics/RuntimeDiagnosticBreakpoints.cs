using System.Collections.Concurrent;

namespace Iwesun.Runtime.Diagnostics;

/// <summary>
/// Collaborative logical breakpoint registry.
/// Each breakpoint has its own SemaphoreSlim signal for one-to-one await/resume.
/// Not a debugger breakpoint — only the calling call-chain is paused (await), other threads run freely.
/// </summary>
public sealed class RuntimeDiagnosticBreakpoints
{
	private readonly ConcurrentDictionary<string, BreakpointState> _breakpoints = new(StringComparer.OrdinalIgnoreCase);
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
			$"breakpoint.hit.{id}",
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
				$"breakpoint.timeout.{id}",
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
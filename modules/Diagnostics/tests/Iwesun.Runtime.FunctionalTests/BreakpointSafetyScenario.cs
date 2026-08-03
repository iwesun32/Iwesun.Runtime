using System.Text.Json;
using Iwesun.Runtime.Diagnostics;

#if DEBUG
#pragma warning disable CA1416 // The scenario intentionally exercises the Windows-only Debug breakpoint implementation.
#endif
internal static class BreakpointSafetyScenario
{
	public static async Task<FunctionalScenarioResult> RunAsync()
	{
#if !DEBUG
		return FunctionalScenarioResult.Fail(
			"breakpoint-safety",
			Array.Empty<string>(),
			["Breakpoint safety probes require a Debug build."]);
#else
		var checks = new List<string>();
		var failures = new List<string>();
		RuntimeOutputSwitch.Enabled = true;
		try
		{
			await VerifyConcurrentResumeAsync(checks, failures);
			await VerifyBurstResumeHasNoStaleSignalAsync(checks, failures);
			await VerifyDisableReleasesAllAsync(checks, failures);
			await VerifyCancellationReleasesWaiterAsync(checks, failures);
			await VerifyOutputGateIndependenceAsync(checks, failures);
			await VerifyContextIsolationAsync(checks, failures);
			VerifyDuplicateRegistration(checks, failures);
			await VerifyDisposableReleaseAsync(checks, failures);
			await VerifyUnknownIdIsAnErrorAsync(checks, failures);
		}
		finally
		{
			RuntimeOutputSwitch.Enabled = false;
		}

		return failures.Count == 0
			? FunctionalScenarioResult.Pass("breakpoint-safety", checks.ToArray())
			: FunctionalScenarioResult.Fail("breakpoint-safety", checks, failures);
#endif
	}

#if DEBUG
	private static async Task VerifyConcurrentResumeAsync(List<string> checks, List<string> failures)
	{
		using var state = new BreakpointState("safety.concurrent");
		var registry = new RuntimeDiagnosticBreakpoints();
		registry.Register(state);
		registry.Enable(state.Id);
		var first = registry.WaitAsync(state.Id);
		var second = registry.WaitAsync(state.Id);
		await WaitUntilAsync(() => state.HitCount == 2, TimeSpan.FromSeconds(2));
		var firstResume = registry.Resume(state.Id);
		await Task.WhenAny(first, second).WaitAsync(TimeSpan.FromSeconds(2));
		var secondResume = registry.Resume(state.Id);
		var bothCompleted = await CompletesAsync(Task.WhenAll(first, second), TimeSpan.FromSeconds(1));
		Expect(firstResume && secondResume && bothCompleted, "concurrent-waiters-resume-independently", checks, failures);
		if (!bothCompleted)
		{
			try { state.Signal.Release(); } catch (SemaphoreFullException) { }
			await Task.WhenAny(Task.WhenAll(first, second), Task.Delay(1000));
		}
		registry.Dispose();
	}

	private static async Task VerifyOutputGateIndependenceAsync(List<string> checks, List<string> failures)
	{
		using var state = new BreakpointState("safety.output-gate");
		var registry = new RuntimeDiagnosticBreakpoints();
		registry.Register(state);
		registry.Enable(state.Id);
		RuntimeOutputSwitch.Enabled = false;
		var wait = registry.WaitAsync(state.Id);
		await Task.Delay(50);
		var blocked = !wait.IsCompleted && state.HitCount == 1;
		RuntimeOutputSwitch.Enabled = true;
		var resumed = registry.Resume(state.Id);
		var completed = await CompletesAsync(wait, TimeSpan.FromSeconds(1));
		Expect(blocked && resumed && completed, "breakpoint-independent-from-output-gate", checks, failures);
		registry.Dispose();
	}

	private static async Task VerifyBurstResumeHasNoStaleSignalAsync(List<string> checks, List<string> failures)
	{
		using var state = new BreakpointState("safety.burst");
		using var registry = new RuntimeDiagnosticBreakpoints();
		registry.Register(state);
		registry.Enable(state.Id);
		const int waiterCount = 32;
		var waiters = Enumerable.Range(0, waiterCount)
			.Select(_ => registry.WaitAsync(state.Id))
			.ToArray();
		await WaitUntilAsync(() => state.HitCount == waiterCount, TimeSpan.FromSeconds(2));
		var resumeResults = Enumerable.Range(0, waiterCount)
			.Select(_ => registry.Resume(state.Id))
			.ToArray();
		var extraResumeRejected = !registry.Resume(state.Id);
		var allCompleted = await CompletesAsync(Task.WhenAll(waiters), TimeSpan.FromSeconds(2));

		var nextWait = registry.WaitAsync(state.Id);
		await Task.Delay(50);
		var noStaleSignal = !nextWait.IsCompleted;
		var finalResume = registry.Resume(state.Id);
		var nextCompleted = await CompletesAsync(nextWait, TimeSpan.FromSeconds(1));
		Expect(
			resumeResults.All(static result => result) && extraResumeRejected && allCompleted && noStaleSignal && finalResume && nextCompleted,
			"burst-resume-has-no-stale-signal",
			checks,
			failures);
	}

	private static async Task VerifyDisableReleasesAllAsync(List<string> checks, List<string> failures)
	{
		using var state = new BreakpointState("safety.disable-all");
		using var registry = new RuntimeDiagnosticBreakpoints();
		registry.Register(state);
		registry.Enable(state.Id);
		var waiters = Enumerable.Range(0, 8)
			.Select(_ => registry.WaitAsync(state.Id))
			.ToArray();
		await WaitUntilAsync(() => state.HitCount == waiters.Length, TimeSpan.FromSeconds(2));
		var disabled = registry.Disable(state.Id);
		var allCompleted = await CompletesAsync(Task.WhenAll(waiters), TimeSpan.FromSeconds(2));
		Expect(disabled && allCompleted && !state.SharedState.EnabledBool, "disable-releases-all-waiters", checks, failures);
	}

	private static async Task VerifyContextIsolationAsync(List<string> checks, List<string> failures)
	{
		using var state = new BreakpointState("safety.context");
		var registry = new RuntimeDiagnosticBreakpoints();
		registry.Register(state);
		registry.Enable(state.Id);
		var wait = registry.WaitAsync(state.Id, context: new CyclicContext());
		await Task.Delay(50);
		var waitingWithoutFault = !wait.IsFaulted && !wait.IsCompleted && state.SharedState.IsWaiting == 1;
		var resumed = registry.Resume(state.Id);
		var completed = await CompletesAsync(wait, TimeSpan.FromSeconds(1));
		Expect(waitingWithoutFault && resumed && completed, "cyclic-context-does-not-fault-business-call", checks, failures);
		registry.Dispose();
	}

	private static async Task VerifyCancellationReleasesWaiterAsync(List<string> checks, List<string> failures)
	{
		using var state = new BreakpointState("safety.cancel");
		using var registry = new RuntimeDiagnosticBreakpoints();
		using var cancellation = new CancellationTokenSource();
		registry.Register(state);
		registry.Enable(state.Id);
		var wait = registry.WaitAsync(state.Id, ct: cancellation.Token);
		await WaitUntilAsync(() => state.SharedState.IsWaiting == 1, TimeSpan.FromSeconds(2));
		cancellation.Cancel();
		var canceledWaitCompleted = await CompletesAsync(wait, TimeSpan.FromSeconds(1));

		var nextWait = registry.WaitAsync(state.Id);
		await Task.Delay(50);
		var noStaleSignal = !nextWait.IsCompleted;
		var resumed = registry.Resume(state.Id);
		var nextCompleted = await CompletesAsync(nextWait, TimeSpan.FromSeconds(1));
		Expect(canceledWaitCompleted && noStaleSignal && resumed && nextCompleted, "cancellation-releases-without-stale-signal", checks, failures);
	}

	private static void VerifyDuplicateRegistration(List<string> checks, List<string> failures)
	{
		using var first = new BreakpointState("safety.duplicate");
		using var second = new BreakpointState("safety.duplicate");
		var registry = new RuntimeDiagnosticBreakpoints();
		registry.Register(first);
		var rejected = false;
		try
		{
			registry.Register(second);
		}
		catch (InvalidOperationException)
		{
			rejected = true;
		}
		Expect(rejected, "duplicate-registration-rejected", checks, failures);
		registry.Dispose();
	}

	private static async Task VerifyDisposableReleaseAsync(List<string> checks, List<string> failures)
	{
		var state = new BreakpointState("safety.dispose");
		var registry = new RuntimeDiagnosticBreakpoints();
		registry.Register(state);
		registry.Enable(state.Id);
		var wait = registry.WaitAsync(state.Id);
		await WaitUntilAsync(() => state.SharedState.IsWaiting == 1, TimeSpan.FromSeconds(2));
		var participatesInDiDisposal = registry is IDisposable;
		registry.Dispose();
		var completed = await CompletesAsync(wait, TimeSpan.FromSeconds(1));
		Expect(participatesInDiDisposal && completed, "dispose-contract-releases-waiters", checks, failures);
	}

	private static async Task VerifyUnknownIdIsAnErrorAsync(List<string> checks, List<string> failures)
	{
		using var registry = new RuntimeDiagnosticBreakpoints();
		var hub = new RuntimeDiagnosticHub();
		hub.SetBreakpoints(registry);
		var operations = new[] { "enable", "disable", "resume" };
		var allRejected = true;
		foreach (var operation in operations)
		{
			var result = await hub.ExecuteAsync(new RuntimeDiagnosticAction
			{
				TargetId = "diagnostics.breakpoints",
				Action = operation,
				Args = new Dictionary<string, JsonElement>
				{
					["id"] = JsonSerializer.SerializeToElement("safety.missing")
				}
			});
			allRejected &= !result.Success && result.ErrorCode == "NOT_FOUND";
		}

		Expect(allRejected, "unknown-breakpoint-id-is-structured-error", checks, failures);
	}

	private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
	{
		var deadline = DateTimeOffset.UtcNow + timeout;
		while (!condition() && DateTimeOffset.UtcNow < deadline)
			await Task.Delay(10);
	}

	private static async Task<bool> CompletesAsync(Task task, TimeSpan timeout)
	{
		var winner = await Task.WhenAny(task, Task.Delay(timeout));
		if (winner != task)
			return false;
		try
		{
			await task;
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static void Expect(bool condition, string name, List<string> checks, List<string> failures)
	{
		if (condition)
			checks.Add(name);
		else
			failures.Add(name);
	}

	private sealed class CyclicContext
	{
		public CyclicContext Self => this;
	}
#endif
}
#if DEBUG
#pragma warning restore CA1416
#endif

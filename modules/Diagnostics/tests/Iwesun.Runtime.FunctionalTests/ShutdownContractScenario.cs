using System.Text.Json;
using System.Reflection;
using Iwesun.Runtime.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

internal static class ShutdownContractScenario
{
	public static async Task<FunctionalScenarioResult> RunAsync(IServiceProvider provider)
	{
		var checks = new List<string>();
		var failures = new List<string>();

		await VerifyStartOwnershipAsync(provider, checks, failures);
		await VerifyShutdownOwnershipAsync(checks, failures);
		await VerifyBoundedShutdownDispatchAsync(checks, failures);
		VerifyTerminalAdmission(provider, checks, failures);

		return failures.Count == 0
			? FunctionalScenarioResult.Pass("shutdown-contract", checks.ToArray())
			: FunctionalScenarioResult.Fail("shutdown-contract", checks, failures);
	}

	private static async Task VerifyBoundedShutdownDispatchAsync(List<string> checks, List<string> failures)
	{
		using var registry = new RuntimeManagedRegistry();
		var state = new RManagedState("shutdown.contract.dispatch.blocker");
		registry.Register(state.UnitId, "task", "test", state.Snapshot());
		var stopCount = 0;
		var wakeCount = 0;
		using var handler = registry.RegisterCommandHandler(state.UnitId, command =>
		{
			if (command.Kind == RuntimeManagedCommandKind.Stop)
				Interlocked.Increment(ref stopCount);
			if (command.Kind == RuntimeManagedCommandKind.Wakeup)
				Interlocked.Increment(ref wakeCount);
			registry.UpdateState(state.UnitId, state.Snapshot());
		});
		var coordinator = new RuntimeShutdownCoordinator(registry);
		var result = await coordinator.ShutdownAsync(TimeSpan.FromMilliseconds(500));
		Check(result.ExitCode == RuntimeShutdownExitCodes.Timeout && result.Status.PendingUnits.Count == 1,
			"shutdown-timeout-preserves-live-owner", "Timeout removed or accepted the still registered owner.", checks, failures);
		Check(Volatile.Read(ref stopCount) is > 0 and <= 2 && Volatile.Read(ref wakeCount) is > 0 and <= 2,
			"shutdown-state-change-does-not-redispatch", "State changes caused repeated successful Stop/Wakeup delivery.", checks, failures);
		registry.Unregister(state.UnitId);

		using var retryRegistry = new RuntimeManagedRegistry();
		var retryState = new RManagedState("shutdown.contract.dispatch.retry");
		retryRegistry.Register(retryState.UnitId, "task", "test", retryState.Snapshot());
		using var entered = new ManualResetEventSlim();
		using var release = new ManualResetEventSlim();
		var receivedStop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var retryHandler = retryRegistry.RegisterCommandHandler(retryState.UnitId, command =>
		{
			if (command.Kind == RuntimeManagedCommandKind.Snapshot)
			{
				entered.Set();
				release.Wait(TimeSpan.FromSeconds(5));
			}
			if (command.Kind == RuntimeManagedCommandKind.Stop)
				receivedStop.TrySetResult();
		});
		retryRegistry.EnqueueCommand(retryState.UnitId, RuntimeManagedCommandKind.Snapshot);
		Check(entered.Wait(TimeSpan.FromSeconds(2)), "shutdown-retry-handler-entered", "Retry fixture handler did not start.", checks, failures);
		for (var index = 0; index < 256; index++)
		{
			if (!retryRegistry.TryEnqueueCommand(retryState.UnitId, RuntimeManagedCommandKind.Wait).Sent)
				break;
		}
		var retryShutdown = new RuntimeShutdownCoordinator(retryRegistry).ShutdownAsync(TimeSpan.FromSeconds(3));
		Check(retryRegistry.DroppedCommandCount > 0, "shutdown-retry-full-inbox", "Retry fixture did not fill the instruction inbox.", checks, failures);
		release.Set();
		try
		{
			await receivedStop.Task.WaitAsync(TimeSpan.FromSeconds(2));
			checks.Add("shutdown-retries-rejected-stop");
		}
		catch (TimeoutException)
		{
			failures.Add("shutdown-retries-rejected-stop: no Stop was delivered after the inbox drained.");
		}
		retryRegistry.Unregister(retryState.UnitId);
		var retryResult = await retryShutdown;
		Check(retryResult.ExitCode == RuntimeShutdownExitCodes.Success, "shutdown-retry-real-drain", "Retried shutdown did not finish after real unregister.", checks, failures);
	}

	private static async Task VerifyStartOwnershipAsync(IServiceProvider provider, List<string> checks, List<string> failures)
	{
		var registry = provider.GetRequiredService<RuntimeManagedRegistry>();
		var duplicateState = new RManagedState("shutdown.contract.duplicate");
		registry.Register(duplicateState.UnitId, "task", "test", duplicateState.Snapshot());
		ExpectRejected("duplicate-unit-id-rejected", () =>
			registry.Register(duplicateState.UnitId, "task", "test", duplicateState.Snapshot()), checks, failures);
		Check(registry.SnapshotRegistrations().Count(x => x.UnitId == duplicateState.UnitId) == 1,
			"duplicate-unit-id-preserves-owner", "Duplicate registration replaced or removed the existing owner.", checks, failures);
		registry.Unregister(duplicateState.UnitId);

		using var taskGate = new ManualResetEventSlim();
		using var task = new RTask(() => taskGate.Wait(TimeSpan.FromSeconds(5)), "shutdown.contract.single-start.task");
		task.Start(TaskScheduler.Default);
		ExpectRejected("rtask-second-start-rejected", task.Start, checks, failures);
		Check(registry.SnapshotRegistrations().Any(x => x.UnitId == task.UnitId),
			"rtask-second-start-preserves-owner", "Second RTask.Start removed the active registration.", checks, failures);
		taskGate.Set();
		await task;

		using var threadGate = new ManualResetEventSlim();
		using var thread = new RThread(() => threadGate.Wait(TimeSpan.FromSeconds(5)), "shutdown.contract.single-start.thread")
		{
			BlocksShutdown = false
		};
		thread.Start();
		Check(registry.SnapshotRegistrations().Single(x => x.UnitId == thread.UnitId).BlocksShutdown == false,
			"rthread-observer-does-not-block-shutdown", "RThread BlocksShutdown=false was not preserved.", checks, failures);
		ExpectRejected("rthread-second-start-rejected", thread.Start, checks, failures);
		Check(registry.SnapshotRegistrations().Any(x => x.UnitId == thread.UnitId),
			"rthread-second-start-preserves-owner", "Second RThread.Start removed the active registration.", checks, failures);
		threadGate.Set();
		thread.Join(TimeSpan.FromSeconds(5));

		var functionalDll = Assembly.GetExecutingAssembly().Location;
		using var process = new RProcess("shutdown.contract.single-start.process")
		{
			BlocksShutdown = false,
			StartInfo = new System.Diagnostics.ProcessStartInfo("dotnet", $"\"{functionalDll}\" --child --scenario probe")
			{
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			}
		};
		Check(process.Start(), "rprocess-first-start", "First RProcess.Start returned false.", checks, failures);
		Check(registry.SnapshotRegistrations().Single(x => x.UnitId == process.UnitId).BlocksShutdown == false,
			"rprocess-observer-does-not-block-shutdown", "RProcess BlocksShutdown=false was not preserved.", checks, failures);
		ExpectRejected("rprocess-second-start-rejected", () => process.Start(), checks, failures);
		Check(registry.SnapshotRegistrations().Any(x => x.UnitId == process.UnitId),
			"rprocess-second-start-preserves-owner", "Second RProcess.Start removed the active registration.", checks, failures);
		await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
	}

	private static async Task VerifyShutdownOwnershipAsync(List<string> checks, List<string> failures)
	{
		using (var adoptedRegistry = new RuntimeManagedRegistry())
		{
			var adoptedState = new RManagedState("shutdown.contract.adopted.blocker");
			adoptedRegistry.Register(adoptedState.UnitId, "task", "test", adoptedState.Snapshot());
			var frozenDeadline = DateTimeOffset.UtcNow.AddMilliseconds(1_200);
			adoptedRegistry.RequestShutdown(1_200, "registry-first", frozenDeadline);
			var adoptedCoordinator = new RuntimeShutdownCoordinator(adoptedRegistry);
			var adoptedTarget = new RuntimeManagedCommandTarget(
				adoptedRegistry,
				new RuntimeDiagnosticHub(),
				adoptedCoordinator);
			var adoptedResponse = await adoptedTarget.ExecuteAsync(
				Command("lifecycle.shutdown", ("timeoutMs", 4_000)),
				CancellationToken.None);
			var adoptedValue = JsonSerializer.SerializeToElement(adoptedResponse.Value);
			Check(adoptedValue.GetProperty("deadlineUtc").GetDateTimeOffset() == frozenDeadline,
				"shutdown-coordinator-adopts-frozen-deadline",
				"Coordinator replaced a deadline already frozen by the registry.", checks, failures);
			var adoptedTimeoutMs = adoptedValue.GetProperty("timeoutMs").GetInt32();
			Check(adoptedTimeoutMs is > 0 and <= 1_200,
				"shutdown-coordinator-reports-remaining-timeout",
				$"Coordinator reported an invalid adopted timeout: {adoptedTimeoutMs} ms.", checks, failures);
			adoptedRegistry.Unregister(adoptedState.UnitId);
			var adoptedResult = await adoptedCoordinator.ShutdownAsync(TimeSpan.FromSeconds(5));
			Check(adoptedResult.ExitCode == RuntimeShutdownExitCodes.Success,
				"shutdown-coordinator-completes-adopted-operation",
				$"Adopted shutdown completed with exit code {adoptedResult.ExitCode}.", checks, failures);
		}

		using var registry = new RuntimeManagedRegistry();
		var blockerState = new RManagedState("shutdown.contract.blocker");
		registry.Register(blockerState.UnitId, "task", "test", blockerState.Snapshot());
		var coordinator = new RuntimeShutdownCoordinator(registry);
		var options = Options.Create(new RuntimeWindowsServiceOptions { ShutdownTimeout = TimeSpan.FromMilliseconds(900) });
		var target = new RuntimeManagedCommandTarget(registry, new RuntimeDiagnosticHub(), coordinator, null, options);

		var first = await target.ExecuteAsync(Command("lifecycle.shutdown", ("timeoutMs", 700), ("payload", "first")), CancellationToken.None);
		var second = await target.ExecuteAsync(Command("lifecycle.shutdown", ("timeoutMs", 4_000), ("payload", "second")), CancellationToken.None);
		var firstValue = JsonSerializer.SerializeToElement(first.Value);
		var secondValue = JsonSerializer.SerializeToElement(second.Value);

		var sameRequest = firstValue.GetProperty("requestId").GetGuid() == secondValue.GetProperty("requestId").GetGuid();
		var sameTimeout = firstValue.GetProperty("timeoutMs").GetInt32() == 700 && secondValue.GetProperty("timeoutMs").GetInt32() == 700;
		var sameDeadline = firstValue.GetProperty("deadlineUtc").GetDateTimeOffset() == secondValue.GetProperty("deadlineUtc").GetDateTimeOffset();
		Check(sameRequest, "shutdown-first-request-id-owned", "Repeated shutdown changed the accepted RequestId.", checks, failures);
		Check(sameTimeout, "shutdown-first-timeout-owned", "Repeated shutdown changed the accepted timeout.", checks, failures);
		Check(sameDeadline, "shutdown-first-deadline-owned", "Repeated shutdown changed the accepted deadline.", checks, failures);
		var acceptedDeadline = firstValue.GetProperty("deadlineUtc").GetDateTimeOffset();
		registry.RequestShutdown(4_000, "direct-repeat");
		registry.SetGlobalLifecycleState("Exit", acceptedDeadline.AddSeconds(10));
		Check(registry.GlobalExitDeadlineUtc == acceptedDeadline,
			"shutdown-deadline-immutable", "A direct Registry or lifecycle call changed the accepted deadline.", checks, failures);

		registry.Unregister(blockerState.UnitId);
		var result = await coordinator.ShutdownAsync(TimeSpan.FromSeconds(5));
		Check(result.ExitCode == RuntimeShutdownExitCodes.Success, "shutdown-completes-after-drain", $"Shutdown completed with exit code {result.ExitCode}.", checks, failures);

		using var concurrentRegistry = new RuntimeManagedRegistry();
		var concurrentState = new RManagedState("shutdown.contract.concurrent.blocker");
		concurrentRegistry.Register(concurrentState.UnitId, "task", "test", concurrentState.Snapshot());
		var concurrentCoordinator = new RuntimeShutdownCoordinator(concurrentRegistry);
		var concurrentTarget = new RuntimeManagedCommandTarget(
			concurrentRegistry,
			new RuntimeDiagnosticHub(),
			concurrentCoordinator);
		var concurrentResponses = await Task.WhenAll(Enumerable.Range(0, 16).Select(index =>
			concurrentTarget.ExecuteAsync(Command("lifecycle.shutdown", ("timeoutMs", 1_000 + index)), CancellationToken.None)));
		var concurrentValues = concurrentResponses.Select(response => JsonSerializer.SerializeToElement(response.Value)).ToArray();
		Check(concurrentValues.Select(value => value.GetProperty("requestId").GetGuid()).Distinct().Count() == 1,
			"concurrent-shutdown-single-request", "Concurrent shutdown requests created more than one operation.", checks, failures);
		Check(concurrentValues.Select(value => value.GetProperty("timeoutMs").GetInt32()).Distinct().Count() == 1,
			"concurrent-shutdown-single-timeout", "Concurrent shutdown responses reported different active timeouts.", checks, failures);
		concurrentRegistry.Unregister(concurrentState.UnitId);
		await concurrentCoordinator.ShutdownAsync(TimeSpan.FromSeconds(5));

		using var defaultRegistry = new RuntimeManagedRegistry();
		var defaultCoordinator = new RuntimeShutdownCoordinator(defaultRegistry);
		var defaultTarget = new DiagnosticSwitchboardTarget(defaultCoordinator, null, options);
		var defaultResponse = await defaultTarget.ExecuteAsync(Command("shutdown"), CancellationToken.None);
		var defaultValue = JsonSerializer.SerializeToElement(defaultResponse.Value);
		Check(defaultValue.GetProperty("timeoutMs").GetInt32() == 900, "shutdown-host-default-timeout", "Host ShutdownTimeout was not used as the default.", checks, failures);

		using var cancelRegistry = new RuntimeManagedRegistry();
		var cancelState = new RManagedState("shutdown.contract.cancel.blocker");
		cancelRegistry.Register(cancelState.UnitId, "task", "test", cancelState.Snapshot());
		var cancelCoordinator = new RuntimeShutdownCoordinator(cancelRegistry);
		using var callerCts = new CancellationTokenSource();
		var callerWait = cancelCoordinator.ShutdownAsync(TimeSpan.FromSeconds(2), "cancel-wait", callerCts.Token);
		callerCts.Cancel();
		try
		{
			await callerWait;
			failures.Add("shutdown-caller-cancellation: caller wait was not cancelled.");
		}
		catch (OperationCanceledException)
		{
			checks.Add("shutdown-caller-wait-cancelled");
		}
		cancelRegistry.Unregister(cancelState.UnitId);
		var continuedResult = await cancelCoordinator.ShutdownAsync(TimeSpan.FromSeconds(10));
		Check(continuedResult.ExitCode == RuntimeShutdownExitCodes.Success,
			"shutdown-continues-after-caller-cancel", "Caller cancellation aborted the accepted shutdown operation.", checks, failures);

		using var forceRegistry = new RuntimeManagedRegistry();
		var forceTarget = new DiagnosticSwitchboardTarget(new RuntimeShutdownCoordinator(forceRegistry));
		var forceResponse = await forceTarget.ExecuteAsync(Command("shutdown", ("graceful", false)), CancellationToken.None);
		Check(!forceResponse.Success && forceResponse.ErrorCode == "NON_GRACEFUL_SHUTDOWN_NOT_SUPPORTED",
			"non-graceful-shutdown-rejected", "graceful=false was accepted without a defined force-termination contract.", checks, failures);
	}

	private static void VerifyTerminalAdmission(IServiceProvider provider, List<string> checks, List<string> failures)
	{
		var registry = provider.GetRequiredService<RuntimeManagedRegistry>();
		var preservedState = new RManagedState("shutdown.contract.preserved");
		registry.Register(preservedState.UnitId, "task", "test", preservedState.Snapshot());
		registry.SetGlobalLifecycleState("Stop", DateTimeOffset.UtcNow.AddSeconds(5));

		ExpectRejected("direct-register-after-stop", () =>
		{
			var state = new RManagedState("shutdown.contract.direct");
			registry.Register(state.UnitId, "task", "test", state.Snapshot());
		}, checks, failures);

		using var task = new RTask(() => { }, "shutdown.contract.task");
		ExpectRejected("rtask-start-after-stop", task.Start, checks, failures);
		using var collidingTask = new RTask(() => { }, preservedState.UnitId);
		ExpectRejected("rtask-collision-after-stop", collidingTask.Start, checks, failures);
		Check(registry.SnapshotRegistrations().Any(x => x.UnitId == preservedState.UnitId),
			"failed-start-preserves-existing-registration", "Rejected wrapper rollback removed an existing registration with the same UnitId.", checks, failures);
		registry.Unregister(preservedState.UnitId);

		using var thread = new RThread(() => { }, "shutdown.contract.thread");
		ExpectRejected("rthread-start-after-stop", thread.Start, checks, failures);

		using var process = new RProcess("shutdown.contract.process")
		{
			StartInfo = new System.Diagnostics.ProcessStartInfo("runtime-shutdown-contract-process-must-not-start")
			{
				UseShellExecute = false
			}
		};
		ExpectRejected("rprocess-start-after-stop", () => process.Start(), checks, failures);

		foreach (var terminal in new[] { "Exit", "Completed", "Timeout" })
		{
			registry.SetGlobalLifecycleState(terminal, registry.GlobalExitDeadlineUtc);
			ExpectRejected($"direct-register-after-{terminal.ToLowerInvariant()}", () =>
			{
				var state = new RManagedState($"shutdown.contract.{terminal.ToLowerInvariant()}");
				registry.Register(state.UnitId, "task", "test", state.Snapshot());
			}, checks, failures);
		}

		ExpectRejected("terminal-state-cannot-reopen", () => registry.SetGlobalLifecycleState("Running"), checks, failures);
		Check(registry.SnapshotRegistrations().All(x => !x.UnitId.StartsWith("shutdown.contract.", StringComparison.Ordinal)),
			"failed-start-rolls-back-registration", "A rejected wrapper start left a managed registration behind.", checks, failures);
	}

	private static RuntimeDiagnosticAction Command(string action, params (string Name, object Value)[] args) =>
		new()
		{
			TargetId = "runtime.managed",
			Action = action,
			Args = args.ToDictionary(x => x.Name, x => JsonSerializer.SerializeToElement(x.Value), StringComparer.OrdinalIgnoreCase)
		};

	private static void ExpectRejected(string check, Action action, List<string> checks, List<string> failures)
	{
		try
		{
			action();
			failures.Add($"{check}: operation was accepted.");
		}
		catch (InvalidOperationException)
		{
			checks.Add(check);
		}
	}

	private static void Check(bool condition, string check, string failure, List<string> checks, List<string> failures)
	{
		if (condition)
			checks.Add(check);
		else
			failures.Add($"{check}: {failure}");
	}
}

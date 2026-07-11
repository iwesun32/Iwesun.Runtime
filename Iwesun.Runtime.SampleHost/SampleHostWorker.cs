using Iwesun.Runtime.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Iwesun.Runtime.SampleHost;

public sealed class SampleHostWorker : BackgroundService
{
	private readonly ILogger<SampleHostWorker> _logger;
	private readonly RuntimeStateManager _runtimeStateManager;
	private readonly RuntimeExecutionManager _runtimeExecutionManager;
	private readonly RuntimeManagedRegistry _managedRegistry;
	private readonly SampleHostProfile _profile;
	private readonly SampleHostState _state;
	private readonly SampleHostRandomState _randomState;
	private readonly RManagedState _coordinatorState;
	private readonly RManagedState _workerState;
	private readonly RManagedState _monitorState;

	public SampleHostWorker(
		ILogger<SampleHostWorker> logger,
		RuntimeStateManager runtimeStateManager,
		RuntimeExecutionManager runtimeExecutionManager,
		RuntimeManagedRegistry managedRegistry,
		SampleHostProfile profile,
		SampleHostState state,
		SampleHostRandomState randomState)
	{
		_logger = logger;
		_runtimeStateManager = runtimeStateManager;
		_runtimeExecutionManager = runtimeExecutionManager;
		_managedRegistry = managedRegistry;
		_profile = profile;
		_state = state;
		_randomState = randomState;
		_coordinatorState = new RManagedState("sample-host.coordinator");
		_workerState = new RManagedState("sample-host.worker");
		_monitorState = new RManagedState("sample-host.monitor");
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		_runtimeStateManager.SetStart();
		_runtimeStateManager.SetWorking();
		RegisterLoop("sample-host.coordinator", "thread", _coordinatorState);
		RegisterLoop("sample-host.worker", "thread", _workerState);
		RegisterLoop("sample-host.monitor", "thread", _monitorState);
		_logger.LogInformation("Sample host started.");
		var faulted = false;

		try
		{
			await Task.WhenAll(
				RunCoordinatorLoopAsync(stoppingToken),
				RunWorkerLoopAsync(stoppingToken),
				RunMonitorLoopAsync(stoppingToken));
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
			_logger.LogInformation("Sample host cancellation requested.");
		}
		catch (Exception ex)
		{
			faulted = true;
			_logger.LogError(ex, "Sample host faulted.");
			throw;
		}
		finally
		{
			if (faulted)
			{
				_runtimeStateManager.SetStop();
			}
			_logger.LogInformation("Sample host stopped.");
		}
	}

	private async Task RunCoordinatorLoopAsync(CancellationToken stoppingToken)
	{
		const string threadId = "sample-host.coordinator";
		const string taskId = "sample-host.coordinator.loop";
		_runtimeExecutionManager.SetThreadState(threadId, RuntimeThreadState.Running, managedThreadId: Environment.CurrentManagedThreadId, currentTaskId: taskId);

		try
		{
			while (!stoppingToken.IsCancellationRequested && !ShouldStop("sample-host.coordinator"))
			{
				_runtimeExecutionManager.SetTaskState(taskId, RuntimeTaskState.Running, step: "coordinating", threadId: threadId);
				_runtimeExecutionManager.HeartbeatThread(threadId);
				_runtimeExecutionManager.HeartbeatTask(taskId);

				RuntimeInjector.Output(
					"sample.host.execution",
					"sample-host",
					"coordinator",
					"Coordinator loop snapshot.",
					_runtimeExecutionManager.Snapshot());

				RuntimeInjector.Watch("sample.host.profile", _profile, nameof(SampleHostProfile));
				await Task.Delay(_profile.CoordinatorInterval, stoppingToken);
			}
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
			_runtimeExecutionManager.SetTaskState(taskId, RuntimeTaskState.Draining, step: "cancelling", threadId: threadId);
			_runtimeExecutionManager.SetThreadState(threadId, RuntimeThreadState.Draining, currentTaskId: taskId);
		}
		finally
		{
			CompleteAndUnregister("sample-host.coordinator", _coordinatorState);
			var terminalState = stoppingToken.IsCancellationRequested ? RuntimeThreadState.Cancelled : RuntimeThreadState.Completed;
			var terminalTaskState = stoppingToken.IsCancellationRequested ? RuntimeTaskState.Cancelled : RuntimeTaskState.Completed;
			_runtimeExecutionManager.SetTaskState(taskId, terminalTaskState, step: terminalTaskState.ToString().ToLowerInvariant(), threadId: threadId);
			_runtimeExecutionManager.SetThreadState(threadId, terminalState, currentTaskId: taskId);
		}
	}

	private async Task RunWorkerLoopAsync(CancellationToken stoppingToken)
	{
		const string threadId = "sample-host.worker";
		const string taskId = "sample-host.worker.loop";
		_runtimeExecutionManager.SetThreadState(threadId, RuntimeThreadState.Running, managedThreadId: Environment.CurrentManagedThreadId, currentTaskId: taskId);

		try
		{
			while (!stoppingToken.IsCancellationRequested && !ShouldStop("sample-host.worker"))
			{
				_runtimeExecutionManager.SetTaskState(taskId, RuntimeTaskState.Running, step: "looping", threadId: threadId);
				_state.Advance();
				var snapshot = _state.Snapshot();
				var iteration = snapshot.Iteration;
				var randomValue = Random.Shared.Next(0, 101);
				_randomState.Record(randomValue);
				var randomSnapshot = _randomState.Snapshot();

				var batchTaskId = $"sample-host.batch.{iteration}";
				_runtimeExecutionManager.RegisterTask(
					batchTaskId,
					$"Sample Batch {iteration}",
					RuntimeExecutionLifetime.Dynamic,
					category: "batch",
					threadId: threadId,
					sourceLocation: "Iwesun.Runtime.SampleHost/SampleHostWorker.cs",
					parentTaskId: taskId,
					step: "registered",
					payload: snapshot);
				_runtimeExecutionManager.SetTaskState(batchTaskId, RuntimeTaskState.Running, step: "running", threadId: threadId, payload: snapshot);

				RuntimeInjector.Output(
					"sample.host.loop",
					"sample-host",
					"tick",
					"Worker loop tick.",
					new
					{
						snapshot.CurrentPhase,
						snapshot.Iteration,
						snapshot.IsPaused,
						snapshot.FaultRequested,
						snapshot.UpdatedAt
					});

				RuntimeInjector.Watch("sample.host.session", snapshot, nameof(SampleHostStateSnapshot));
				RuntimeInjector.Output(
					"sample.host.random",
					"sample-host",
					"random",
					"Periodic random sample.",
					randomSnapshot);
				RuntimeInjector.Watch("sample.host.random", randomSnapshot, nameof(SampleHostRandomSnapshot));

#if DEBUG
				await RuntimeInjector.Break("sample.host.random.initial-enabled", () => randomSnapshot.SampleCount == 1, randomSnapshot);
				await RuntimeInjector.Break("sample.host.random.dynamic", () => true, randomSnapshot);
				await RuntimeOutput.BreakIfNumbers(
					"sample.host.random.numeric",
					randomSnapshot.CurrentValue,
					randomSnapshot.PreviousValue,
					0,
					randomSnapshot);
				await RuntimeInjector.Break("sample.host.pause", () => snapshot.IsPaused, snapshot);
				await RuntimeInjector.Break("sample.host.batch-gate", () => snapshot.Iteration % _profile.BatchGate == 0, snapshot);
#endif

				if (_profile.EnableFailureInjection && snapshot.FaultRequested)
				{
					_runtimeExecutionManager.SetTaskState(batchTaskId, RuntimeTaskState.Faulted, step: "faulted", error: "Fault requested from diagnostics surface", threadId: threadId, payload: snapshot);
					_runtimeExecutionManager.SetTaskState(taskId, RuntimeTaskState.Faulted, step: "faulted", error: "Fault requested from diagnostics surface", threadId: threadId, payload: snapshot);
					_runtimeExecutionManager.SetThreadState(threadId, RuntimeThreadState.Faulted, currentTaskId: batchTaskId, payload: snapshot);
					throw new InvalidOperationException("Sample fault requested through the diagnostics surface.");
				}

				_runtimeExecutionManager.HeartbeatThread(threadId, snapshot);
				_runtimeExecutionManager.HeartbeatTask(taskId, snapshot);
				_runtimeExecutionManager.SetTaskState(batchTaskId, RuntimeTaskState.Completed, step: "completed", threadId: threadId, payload: snapshot);
				await Task.Delay(_profile.WorkerInterval, stoppingToken);
			}
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
			_runtimeExecutionManager.SetTaskState(taskId, RuntimeTaskState.Draining, step: "cancelling", threadId: threadId);
			_runtimeExecutionManager.SetThreadState(threadId, RuntimeThreadState.Draining, currentTaskId: taskId);
		}
		finally
		{
			CompleteAndUnregister("sample-host.worker", _workerState);
			var terminalState = stoppingToken.IsCancellationRequested ? RuntimeThreadState.Cancelled : RuntimeThreadState.Completed;
			var terminalTaskState = stoppingToken.IsCancellationRequested ? RuntimeTaskState.Cancelled : RuntimeTaskState.Completed;
			_runtimeExecutionManager.SetTaskState(taskId, terminalTaskState, step: terminalTaskState.ToString().ToLowerInvariant(), threadId: threadId);
			_runtimeExecutionManager.SetThreadState(threadId, terminalState, currentTaskId: taskId);
		}
	}

	private async Task RunMonitorLoopAsync(CancellationToken stoppingToken)
	{
		const string threadId = "sample-host.monitor";
		const string taskId = "sample-host.monitor.loop";
		_runtimeExecutionManager.SetThreadState(threadId, RuntimeThreadState.Running, managedThreadId: Environment.CurrentManagedThreadId, currentTaskId: taskId);

		try
		{
			while (!stoppingToken.IsCancellationRequested && !ShouldStop("sample-host.monitor"))
			{
				_runtimeExecutionManager.SetTaskState(taskId, RuntimeTaskState.Running, step: "monitoring", threadId: threadId);
				var snapshot = _runtimeExecutionManager.Snapshot();
				RuntimeInjector.Output(
					"sample.host.execution",
					"sample-host",
					"monitor",
					"Execution monitor snapshot.",
					snapshot);
				RuntimeInjector.Watch("sample.host.execution", snapshot, nameof(RuntimeExecutionSnapshot));
				_runtimeExecutionManager.HeartbeatThread(threadId, snapshot);
				_runtimeExecutionManager.HeartbeatTask(taskId, snapshot);
				await Task.Delay(_profile.MonitorInterval, stoppingToken);
			}
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
			_runtimeExecutionManager.SetTaskState(taskId, RuntimeTaskState.Draining, step: "cancelling", threadId: threadId);
			_runtimeExecutionManager.SetThreadState(threadId, RuntimeThreadState.Draining, currentTaskId: taskId);
		}
		finally
		{
			CompleteAndUnregister("sample-host.monitor", _monitorState);
			var terminalState = stoppingToken.IsCancellationRequested ? RuntimeThreadState.Cancelled : RuntimeThreadState.Completed;
			var terminalTaskState = stoppingToken.IsCancellationRequested ? RuntimeTaskState.Cancelled : RuntimeTaskState.Completed;
			_runtimeExecutionManager.SetTaskState(taskId, terminalTaskState, step: terminalTaskState.ToString().ToLowerInvariant(), threadId: threadId);
			_runtimeExecutionManager.SetThreadState(threadId, terminalState, currentTaskId: taskId);
		}
	}

	private void RegisterLoop(string unitId, string unitType, RManagedState state)
	{
		state.TransitionTo("Start");
		state.TransitionTo("Working");
		_managedRegistry.Register(unitId, unitType, "SampleHost", state.Snapshot());
	}

	private bool ShouldStop(string unitId)
	{
		if (_managedRegistry.IsGlobalStopOrExitRequested)
		{
			return true;
		}

		while (_managedRegistry.TryDequeueCommand(unitId, out var command) && command is not null)
		{
			if (command.Kind == RuntimeManagedCommandKind.Stop)
			{
				return true;
			}
		}

		return false;
	}

	private void CompleteAndUnregister(string unitId, RManagedState state)
	{
		try
		{
			state.SetDetail("shutdownStage", "draining");
			state.TryAppendSubTaskState("Draining");
			state.TryTransitionTo("Stop");
			state.SetDetail("shutdownStage", "completed");
		}
		finally
		{
			_managedRegistry.Unregister(unitId);
		}
	}
}

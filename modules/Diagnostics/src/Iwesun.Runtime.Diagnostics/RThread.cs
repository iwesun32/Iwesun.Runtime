using System.Threading;
using System.Threading.Tasks;
using System.Runtime.Versioning;
using Iwesun.Runtime.Data;

namespace Iwesun.Runtime.Diagnostics;

public sealed class RThread : IDisposable
{
	private const int NormalExitCode = 0;
	private readonly Thread _inner;
	private readonly RuntimeManagedUnitBase _unit;
	private readonly RuntimeExecutionManager? _execution;
	private readonly RuntimeManagedRegistry? _managed;
	private readonly RuntimeExecutionLifetime _lifetime;
	private readonly RuntimeThreadKind _kind;
	private readonly string _owner;
	private readonly string _sourceLocation;
	private readonly DelegateRuntimeManagedCommandHandler _commandHandler;
	private readonly RuntimeManagedCleanup _cleanup;
	private IDisposable? _commandRegistration;
	private readonly CancellationTokenSource _guardianCts = new();
	private Task? _guardianLoop;
	private int _startState;
	private int _managedRegistered;
	private int _executionRegistered;
	private int _guardianStarted;
	private int _exitCode = NormalExitCode;
	private int _exitCompletedRaised;
	private int _globalStopHandled;
	private int _disposed;
	private int _cleanupCompleted;

	public RThread(
		ThreadStart start,
		string? unitId = null,
		string? name = null,
		RuntimeExecutionLifetime lifetime = RuntimeExecutionLifetime.Dynamic,
		RuntimeThreadKind kind = RuntimeThreadKind.Worker,
		string owner = "",
		string sourceLocation = "",
		int stopTimeoutMilliseconds = 5000)
	{
		ArgumentNullException.ThrowIfNull(start);
		var resolvedUnitId = string.IsNullOrWhiteSpace(unitId) ? $"thread.{Guid.NewGuid():N}" : unitId;
		_unit = new RuntimeManagedUnitBase(resolvedUnitId, RuntimeInstructionEntityKind.Thread);
		UnitId = _unit.UnitId;
		_lifetime = lifetime;
		_kind = kind;
		_owner = owner;
		_sourceLocation = sourceLocation;
		_ = stopTimeoutMilliseconds; // Retained for source compatibility; shutdown uses only the controller deadline.
		_execution = _unit.Execution;
		_managed = _unit.Managed;
		State = _unit.State;
		_cleanup = new RuntimeManagedCleanup(this, State);
		_inner = new Thread(() => Execute(start));
		_commandHandler = new DelegateRuntimeManagedCommandHandler(UnitId, _managed)
		{
			OnStopAction = HandleStopCommand,
			OnWakeupAction = cmd => _managed?.PublishEvent(UnitId, "thread-wakeup-command", "Wakeup command received.", new { cmd.Sequence, cmd.Payload }),
			OnSnapshotAction = _ => _managed?.PublishEvent(UnitId, "thread-snapshot-command", "Snapshot command received.", State.Snapshot())
		};
		if (!string.IsNullOrWhiteSpace(name))
		{
			_inner.Name = name;
		}
	}

	public RThread(
		ParameterizedThreadStart start,
		string? unitId = null,
		string? name = null,
		RuntimeExecutionLifetime lifetime = RuntimeExecutionLifetime.Dynamic,
		RuntimeThreadKind kind = RuntimeThreadKind.Worker,
		string owner = "",
		string sourceLocation = "",
		int stopTimeoutMilliseconds = 5000)
	{
		ArgumentNullException.ThrowIfNull(start);
		var resolvedUnitId = string.IsNullOrWhiteSpace(unitId) ? $"thread.{Guid.NewGuid():N}" : unitId;
		_unit = new RuntimeManagedUnitBase(resolvedUnitId, RuntimeInstructionEntityKind.Thread);
		UnitId = _unit.UnitId;
		_lifetime = lifetime;
		_kind = kind;
		_owner = owner;
		_sourceLocation = sourceLocation;
		_ = stopTimeoutMilliseconds; // Retained for source compatibility; shutdown uses only the controller deadline.
		_execution = _unit.Execution;
		_managed = _unit.Managed;
		State = _unit.State;
		_cleanup = new RuntimeManagedCleanup(this, State);
		_inner = new Thread(parameter => Execute(start, parameter));
		_commandHandler = new DelegateRuntimeManagedCommandHandler(UnitId, _managed)
		{
			OnStopAction = HandleStopCommand,
			OnWakeupAction = cmd => _managed?.PublishEvent(UnitId, "thread-wakeup-command", "Wakeup command received.", new { cmd.Sequence, cmd.Payload }),
			OnSnapshotAction = _ => _managed?.PublishEvent(UnitId, "thread-snapshot-command", "Snapshot command received.", State.Snapshot())
		};
		if (!string.IsNullOrWhiteSpace(name))
		{
			_inner.Name = name;
		}
	}

	public string UnitId { get; }
	public IRManagedState State { get; }
	public bool BlocksShutdown { get; init; } = true;
	public int ExitCode => Volatile.Read(ref _exitCode);
	public event EventHandler<RThreadExitRequestedEventArgs>? ExitRequested;
	public event EventHandler<RThreadExitResultEventArgs>? ExitCompleted;
	public event EventHandler<RThreadExitRequestedEventArgs>? ExitHandlingRequested;
	public event RuntimeManagedCleanupHandler CleanupRequested
	{
		add => _cleanup.Requested += value;
		remove => _cleanup.Requested -= value;
	}
	public bool IsCleanupCompleted => _cleanup.IsCompleted;

	public string? Name
	{
		get => _inner.Name;
		set => _inner.Name = value;
	}

	public bool IsBackground
	{
		get => _inner.IsBackground;
		set => _inner.IsBackground = value;
	}

	public ThreadPriority Priority
	{
		get => _inner.Priority;
		set => _inner.Priority = value;
	}

	public bool IsAlive => _inner.IsAlive;
	public ThreadState ThreadState => _inner.ThreadState;
	public int ManagedThreadId => _inner.ManagedThreadId;
	[SupportedOSPlatform("windows")]
	public void SetApartmentState(ApartmentState state) => _inner.SetApartmentState(state);

	public void SetDetail(string key, string value) => _unit.SetDetail(key, value);
	public bool TryGetDetail(string key, out string? value) => _unit.TryGetDetail(key, out value);
	public RuntimeState TransitionTo(string stateName) => _unit.TransitionTo(stateName);
	public bool TryTransitionTo(string stateName) => _unit.TryTransitionTo(stateName);

	public void Start()
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
		AcquireStartOwnership();
		try
		{
			EnsureRegistered();
			StartGuardianLoop();
			_inner.Start();
		}
		catch
		{
			RollbackFailedStart();
			throw;
		}
	}

	public void Start(object? parameter)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
		AcquireStartOwnership();
		try
		{
			EnsureRegistered();
			StartGuardianLoop();
			_inner.Start(parameter);
		}
		catch
		{
			RollbackFailedStart();
			throw;
		}
	}

	public void Join() => _inner.Join();
	public bool Join(TimeSpan timeout) => _inner.Join(timeout);
	public bool Join(int millisecondsTimeout) => _inner.Join(millisecondsTimeout);
	public void Interrupt() => _inner.Interrupt();

	public static RThread Current => new(Thread.CurrentThread);

	private RThread(Thread thread)
	{
		_inner = thread;
		_unit = new RuntimeManagedUnitBase($"thread.current.{thread.ManagedThreadId}", RuntimeInstructionEntityKind.Thread);
		UnitId = _unit.UnitId;
		State = _unit.State;
		_cleanup = new RuntimeManagedCleanup(this, State);
		_execution = _unit.Execution;
		_managed = _unit.Managed;
		_lifetime = RuntimeExecutionLifetime.Dynamic;
		_kind = RuntimeThreadKind.Custom;
		_owner = "";
		_sourceLocation = "";
		_commandHandler = new DelegateRuntimeManagedCommandHandler(UnitId, _managed)
		{
			OnStopAction = HandleStopCommand,
			OnWakeupAction = cmd => _managed?.PublishEvent(UnitId, "thread-wakeup-command", "Wakeup command received.", new { cmd.Sequence, cmd.Payload }),
			OnSnapshotAction = _ => _managed?.PublishEvent(UnitId, "thread-snapshot-command", "Snapshot command received.", State.Snapshot())
		};
	}

	private void EnsureRegistered()
	{
		State.TransitionTo("Start");
		_managed?.Register(UnitId, "thread", "InternalManaged", State.Snapshot(), BlocksShutdown);
		if (_managed is not null)
			Volatile.Write(ref _managedRegistered, 1);
		_execution?.RegisterThread(
			UnitId,
			Name ?? UnitId,
			_lifetime,
			_kind,
			owner: _owner,
			sourceLocation: _sourceLocation,
			managedThreadId: 0,
			payload: State.Snapshot());
		if (_execution is not null)
			Volatile.Write(ref _executionRegistered, 1);
		_commandRegistration = _managed?.RegisterCommandHandler(
			UnitId,
			command => _commandHandler.Handle(command));
	}

	private void RollbackFailedStart()
	{
		try { _guardianCts.Cancel(); } catch (ObjectDisposedException) { }
		_commandRegistration?.Dispose();
		_commandRegistration = null;
		UnregisterManaged();
		Volatile.Write(ref _startState, 2);
		try
		{
			State.SetDetail("error", "start-failed");
			State.TransitionTo("Stop");
			if (Volatile.Read(ref _executionRegistered) != 0)
			{
				_execution?.SetThreadState(
					UnitId,
					RuntimeThreadState.Faulted,
					payload: State.Snapshot());
			}
		}
		catch
		{
			// Preserve the original thread start exception.
		}
		CompleteLifetime();
	}

	private void AcquireStartOwnership()
	{
		if (Interlocked.CompareExchange(ref _startState, 1, 0) != 0)
			throw new InvalidOperationException($"Managed thread '{UnitId}' can only be started once.");
	}

	private void StartGuardianLoop()
	{
		if (_managed == null)
		{
			return;
		}

		if (Interlocked.Exchange(ref _guardianStarted, 1) == 1)
		{
			return;
		}

		_guardianLoop = Task.Run(() => GuardianLoopAsync(_guardianCts.Token));
	}

	private async Task GuardianLoopAsync(CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			if (_managed?.IsGlobalStopOrExitRequested == true && Interlocked.Exchange(ref _globalStopHandled, 1) == 0)
			{
				HandleStopCommand(new RuntimeManagedCommand(0, UnitId, RuntimeManagedCommandKind.Stop, "global-stop", DateTimeOffset.UtcNow));
			}

			if (_managed?.TryDequeueCommand(UnitId, out var command) == true && command is not null)
			{
				_commandHandler.Handle(command);
			}

			try
			{
				await Task.Delay(30, cancellationToken);
			}
			catch (OperationCanceledException)
			{
				break;
			}
		}
	}

	private void HandleStopCommand(RuntimeManagedCommand command)
	{
		State.SetDetail("stopCommandSeq", command.Sequence.ToString());
		_cleanup.Begin(command);
		_ = WakeForManagedExitAsync();
		_ = PublishLegacyExitNotificationsAsync(command);
		_managed?.PublishEvent(UnitId, "thread-exit-requested", "Stop command received by guardian loop.", new { command.Sequence, command.Payload });
	}

	private Task PublishLegacyExitNotificationsAsync(RuntimeManagedCommand command) => Task.Run(() =>
	{
		var args = new RThreadExitRequestedEventArgs(command.Sequence, command.Payload);
		InvokeLegacyHandlers(ExitRequested, args);
		InvokeLegacyHandlers(ExitHandlingRequested, args);
	});

	private void InvokeLegacyHandlers(EventHandler<RThreadExitRequestedEventArgs>? handlers, RThreadExitRequestedEventArgs args)
	{
		if (handlers == null)
			return;
		foreach (EventHandler<RThreadExitRequestedEventArgs> handler in handlers.GetInvocationList())
		{
			try { handler(this, args); }
			catch (Exception ex) { _managed?.PublishEvent(UnitId, "thread-legacy-exit-handler-faulted", ex.Message, null); }
		}
	}

	private async Task WakeForManagedExitAsync()
	{
		await _cleanup.WaitForExitSignalAsync().ConfigureAwait(false);
		Interlocked.Exchange(ref _exitCode, _cleanup.ExitCode);
		if (!_inner.IsAlive)
			return;
		try
		{
			_inner.Interrupt();
			_managed?.PublishEvent(UnitId, "thread-stop-wakeup", "Cleanup completed or reached the controller deadline; the managed wait was interrupted.", State.Snapshot());
		}
		catch { }
	}

	private void Execute(ThreadStart start)
	{
		State.TransitionTo("Working");
		_execution?.SetThreadState(UnitId, RuntimeThreadState.Running, managedThreadId: Environment.CurrentManagedThreadId, payload: State.Snapshot());
		_managed?.PublishEvent(UnitId, "thread-running", "Thread entered running state.", State.Snapshot());
		try
		{
			start();
			State.TransitionTo("Stop");
			_execution?.SetThreadState(UnitId, RuntimeThreadState.Completed, managedThreadId: Environment.CurrentManagedThreadId, payload: State.Snapshot());
			_managed?.PublishEvent(UnitId, "thread-completed", "Thread completed.", State.Snapshot());
		}
		catch (Exception ex) when (ex is OperationCanceledException or ThreadInterruptedException)
		{
			State.SetDetail("error", "cancelled");
			State.TransitionTo("Stop");
			_execution?.SetThreadState(UnitId, RuntimeThreadState.Cancelled, managedThreadId: Environment.CurrentManagedThreadId, payload: State.Snapshot());
			_managed?.PublishEvent(UnitId, "thread-cancelled", "Thread cancelled.", State.Snapshot());
		}
		catch (Exception ex)
		{
			State.SetDetail("error", ex.Message);
			State.TransitionTo("Stop");
			_execution?.SetThreadState(UnitId, RuntimeThreadState.Faulted, managedThreadId: Environment.CurrentManagedThreadId, payload: State.Snapshot());
			_managed?.PublishEvent(UnitId, "thread-faulted", ex.Message, State.Snapshot());
			throw;
		}
		finally
		{
			RestoreCleanupTerminalState();
			ConsumePendingInterrupt();
			if (Interlocked.Exchange(ref _exitCode, ExitCode) == ExitCode)
			{
				// Preserve current exit code; just ensuring the field is observed before final completion.
			}

			if (Volatile.Read(ref _exitCompletedRaised) == 0)
			{
				RaiseExitCompletedOnce(new RThreadExitResultEventArgs(Volatile.Read(ref _exitCode), false, "Thread stopped."));
			}

			CompleteLifetime();
		}
	}

	private void Execute(ParameterizedThreadStart start, object? parameter)
	{
		State.TransitionTo("Working");
		_execution?.SetThreadState(UnitId, RuntimeThreadState.Running, managedThreadId: Environment.CurrentManagedThreadId, payload: State.Snapshot());
		_managed?.PublishEvent(UnitId, "thread-running", "Thread entered running state.", State.Snapshot());
		try
		{
			start(parameter);
			State.TransitionTo("Stop");
			_execution?.SetThreadState(UnitId, RuntimeThreadState.Completed, managedThreadId: Environment.CurrentManagedThreadId, payload: State.Snapshot());
			_managed?.PublishEvent(UnitId, "thread-completed", "Thread completed.", State.Snapshot());
		}
		catch (Exception ex) when (ex is OperationCanceledException or ThreadInterruptedException)
		{
			State.SetDetail("error", "cancelled");
			State.TransitionTo("Stop");
			_execution?.SetThreadState(UnitId, RuntimeThreadState.Cancelled, managedThreadId: Environment.CurrentManagedThreadId, payload: State.Snapshot());
			_managed?.PublishEvent(UnitId, "thread-cancelled", "Thread cancelled.", State.Snapshot());
		}
		catch (Exception ex)
		{
			State.SetDetail("error", ex.Message);
			State.TransitionTo("Stop");
			_execution?.SetThreadState(UnitId, RuntimeThreadState.Faulted, managedThreadId: Environment.CurrentManagedThreadId, payload: State.Snapshot());
			_managed?.PublishEvent(UnitId, "thread-faulted", ex.Message, State.Snapshot());
			throw;
		}
		finally
		{
			RestoreCleanupTerminalState();
			ConsumePendingInterrupt();
			if (Volatile.Read(ref _exitCompletedRaised) == 0)
			{
				RaiseExitCompletedOnce(new RThreadExitResultEventArgs(Volatile.Read(ref _exitCode), false, "Thread stopped."));
			}

			CompleteLifetime();
		}
	}

	private void RaiseExitCompletedOnce(RThreadExitResultEventArgs args)
	{
		if (Interlocked.Exchange(ref _exitCompletedRaised, 1) == 1)
		{
			return;
		}

		ExitCompleted?.Invoke(this, args);
	}

	private void RestoreCleanupTerminalState()
	{
		if (!_cleanup.HasStarted)
			return;
		State.TransitionTo(Volatile.Read(ref _exitCode) == RuntimeShutdownExitCodes.Timeout ? "Timeout" : "Completed");
	}

	private static void ConsumePendingInterrupt()
	{
		try { Thread.Sleep(0); }
		catch (ThreadInterruptedException) { }
	}

	private void CompleteLifetime()
	{
		if (Interlocked.Exchange(ref _cleanupCompleted, 1) == 1)
			return;
		Volatile.Write(ref _startState, 2);
		try { _guardianCts.Cancel(); } catch (ObjectDisposedException) { }
		_commandRegistration?.Dispose();
		UnregisterManaged();
		_guardianCts.Dispose();
		_cleanup.Dispose();
	}

	private void UnregisterManaged()
	{
		if (Interlocked.Exchange(ref _managedRegistered, 0) != 0)
			_managed?.Unregister(UnitId);
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) == 1)
		{
			return;
		}

		if (_inner.IsAlive)
		{
			HandleStopCommand(new RuntimeManagedCommand(0, UnitId, RuntimeManagedCommandKind.Stop, "dispose", DateTimeOffset.UtcNow));
			if (_inner.IsAlive)
				return;
		}
		else if (Interlocked.CompareExchange(ref _startState, 2, 0) != 0
			&& Volatile.Read(ref _startState) != 2)
		{
			return;
		}

		CompleteLifetime();
	}
}

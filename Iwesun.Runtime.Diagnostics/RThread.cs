using System.Threading;
using System.Threading.Tasks;
using Iwesun.Runtime.Data;

namespace Iwesun.Runtime.Diagnostics;

public sealed class RThread : IDisposable
{
	private const int NormalExitCode = 0;
	private const int TimeoutExitCode = 124;
	private readonly Thread _inner;
	private readonly RuntimeManagedUnitBase _unit;
	private readonly RuntimeExecutionManager? _execution;
	private readonly RuntimeManagedRegistry? _managed;
	private readonly RuntimeExecutionLifetime _lifetime;
	private readonly RuntimeThreadKind _kind;
	private readonly string _owner;
	private readonly string _sourceLocation;
	private readonly DelegateRuntimeManagedCommandHandler _commandHandler;
	private readonly IDisposable? _commandRegistration;
	private readonly int _stopTimeoutMilliseconds;
	private readonly CancellationTokenSource _guardianCts = new();
	private Task? _guardianLoop;
	private int _registered;
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
		_stopTimeoutMilliseconds = Math.Max(100, stopTimeoutMilliseconds);
		_execution = _unit.Execution;
		_managed = _unit.Managed;
		State = _unit.State;
		_inner = new Thread(() => Execute(start));
		_commandHandler = new DelegateRuntimeManagedCommandHandler(UnitId, _managed)
		{
			OnStopAction = HandleStopCommand,
			OnWakeupAction = cmd => _managed?.PublishEvent(UnitId, "thread-wakeup-command", "Wakeup command received.", new { cmd.Sequence, cmd.Payload }),
			OnSnapshotAction = _ => _managed?.PublishEvent(UnitId, "thread-snapshot-command", "Snapshot command received.", State.Snapshot())
		};
		_commandRegistration = _managed?.RegisterCommandHandler(UnitId, command => _commandHandler.Handle(command));
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
		_stopTimeoutMilliseconds = Math.Max(100, stopTimeoutMilliseconds);
		_execution = _unit.Execution;
		_managed = _unit.Managed;
		State = _unit.State;
		_inner = new Thread(parameter => Execute(start, parameter));
		_commandHandler = new DelegateRuntimeManagedCommandHandler(UnitId, _managed)
		{
			OnStopAction = HandleStopCommand,
			OnWakeupAction = cmd => _managed?.PublishEvent(UnitId, "thread-wakeup-command", "Wakeup command received.", new { cmd.Sequence, cmd.Payload }),
			OnSnapshotAction = _ => _managed?.PublishEvent(UnitId, "thread-snapshot-command", "Snapshot command received.", State.Snapshot())
		};
		_commandRegistration = _managed?.RegisterCommandHandler(UnitId, command => _commandHandler.Handle(command));
		if (!string.IsNullOrWhiteSpace(name))
		{
			_inner.Name = name;
		}
	}

	public string UnitId { get; }
	public IRManagedState State { get; }
	public int ExitCode => Volatile.Read(ref _exitCode);
	public event EventHandler<RThreadExitRequestedEventArgs>? ExitRequested;
	public event EventHandler<RThreadExitResultEventArgs>? ExitCompleted;
	public event EventHandler<RThreadExitRequestedEventArgs>? ExitHandlingRequested;

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

	public void SetDetail(string key, string value) => _unit.SetDetail(key, value);
	public bool TryGetDetail(string key, out string? value) => _unit.TryGetDetail(key, out value);
	public RuntimeState TransitionTo(string stateName) => _unit.TransitionTo(stateName);
	public bool TryTransitionTo(string stateName) => _unit.TryTransitionTo(stateName);

	public void Start()
	{
		EnsureRegistered();
		StartGuardianLoop();
		_inner.Start();
	}

	public void Start(object? parameter)
	{
		EnsureRegistered();
		StartGuardianLoop();
		_inner.Start(parameter);
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
		_execution = _unit.Execution;
		_managed = _unit.Managed;
		_stopTimeoutMilliseconds = 5000;
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
		_commandRegistration = _managed?.RegisterCommandHandler(UnitId, command => _commandHandler.Handle(command));
	}

	private void EnsureRegistered()
	{
		if (Interlocked.Exchange(ref _registered, 1) == 1)
		{
			return;
		}

		State.TransitionTo("Start");
		_execution?.RegisterThread(
			UnitId,
			Name ?? UnitId,
			_lifetime,
			_kind,
			owner: _owner,
			sourceLocation: _sourceLocation,
			managedThreadId: 0,
			payload: State.Snapshot());
		_managed?.Register(UnitId, "thread", "InternalManaged", State.Snapshot());
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
		var args = new RThreadExitRequestedEventArgs(command.Sequence, command.Payload);
		ExitRequested?.Invoke(this, args);
		ExitHandlingRequested?.Invoke(this, args);
		_managed?.PublishEvent(UnitId, "thread-exit-requested", "Stop command received by guardian loop.", new { command.Sequence, command.Payload });

		try
		{
			if (_inner.IsAlive)
			{
				try
				{
					_inner.Interrupt();
				}
				catch
				{
					// Best effort only.
				}

				if (!_inner.Join(_stopTimeoutMilliseconds))
				{
					Interlocked.Exchange(ref _exitCode, TimeoutExitCode);
					State.SetDetail("timeout", _stopTimeoutMilliseconds.ToString());
					State.SetDetail("error", "timeout");
					_execution?.SetThreadState(UnitId, RuntimeThreadState.ForcedExit, managedThreadId: _inner.ManagedThreadId, payload: State.Snapshot());
					_managed?.PublishEvent(UnitId, "thread-stop-timeout", $"Thread stop timed out after {_stopTimeoutMilliseconds}ms.", State.Snapshot());
					RaiseExitCompletedOnce(new RThreadExitResultEventArgs(TimeoutExitCode, true, "Thread stop timeout."));
					throw new TimeoutException($"Thread stop timed out after {_stopTimeoutMilliseconds}ms.");
				}
			}

			Interlocked.Exchange(ref _exitCode, NormalExitCode);
			_managed?.PublishEvent(UnitId, "thread-stop-completed", "Thread stop completed.", State.Snapshot());
			RaiseExitCompletedOnce(new RThreadExitResultEventArgs(NormalExitCode, false, "Thread stopped."));
		}
		catch (TimeoutException ex)
		{
			_managed?.PublishEvent(UnitId, "thread-stop-timeout-exception", ex.Message, State.Snapshot());
		}
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
			ConsumePendingInterrupt();
			if (Interlocked.Exchange(ref _exitCode, ExitCode) == ExitCode)
			{
				// Preserve current exit code; just ensuring the field is observed before final completion.
			}

			if (Volatile.Read(ref _exitCompletedRaised) == 0)
			{
				var timedOut = Volatile.Read(ref _exitCode) == TimeoutExitCode;
				var message = timedOut ? "Thread stop timeout." : "Thread stopped.";
				RaiseExitCompletedOnce(new RThreadExitResultEventArgs(Volatile.Read(ref _exitCode), timedOut, message));
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
			ConsumePendingInterrupt();
			if (Volatile.Read(ref _exitCompletedRaised) == 0)
			{
				var timedOut = Volatile.Read(ref _exitCode) == TimeoutExitCode;
				var message = timedOut ? "Thread stop timeout." : "Thread stopped.";
				RaiseExitCompletedOnce(new RThreadExitResultEventArgs(Volatile.Read(ref _exitCode), timedOut, message));
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

	private static void ConsumePendingInterrupt()
	{
		try { Thread.Sleep(0); }
		catch (ThreadInterruptedException) { }
	}

	private void CompleteLifetime()
	{
		if (Interlocked.Exchange(ref _cleanupCompleted, 1) == 1)
			return;
		try { _guardianCts.Cancel(); } catch (ObjectDisposedException) { }
		_commandRegistration?.Dispose();
		_managed?.Unregister(UnitId);
		_guardianCts.Dispose();
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

		CompleteLifetime();
	}
}

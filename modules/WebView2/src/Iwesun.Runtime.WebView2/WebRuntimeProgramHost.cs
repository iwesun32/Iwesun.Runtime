namespace Iwesun.Runtime.WebView2;

public enum WebRuntimeProgramHostState { Created, Starting, Running, Stopping, Stopped, StoppedWithErrors, TimedOut, Faulted, Disposed }

public sealed record WebRuntimeProgramStopResult(
	WebRuntimeProgramHostState State,
	IReadOnlyList<string> Errors,
	bool TimedOut)
{
	public bool Success => State == WebRuntimeProgramHostState.Stopped;
}

/// <summary>Owns discovery, registration and lifecycle of compiled C# WebRuntime programs.</summary>
public sealed class WebRuntimeProgramHost : IAsyncDisposable
{
	private readonly WebRuntimeProgramRegistry _registry;
	private readonly IReadOnlyList<IWebRuntimeBusinessProgram> _programs;
	private readonly List<IDisposable> _registrations = [];
	private readonly List<IWebRuntimeBusinessProgramLifecycle> _initialized = [];
	private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
	private int _active;
	private int _disposed;
	private int _state = (int)WebRuntimeProgramHostState.Created;

	public WebRuntimeProgramHost(
		WebRuntimeProgramRegistry registry,
		IEnumerable<IWebRuntimeBusinessProgram> programs)
	{
		ArgumentNullException.ThrowIfNull(registry);
		ArgumentNullException.ThrowIfNull(programs);
		_registry = registry;
		_programs = programs.ToArray();
	}

	public bool IsActive => Volatile.Read(ref _active) != 0;
	public WebRuntimeProgramHostState State => (WebRuntimeProgramHostState)Volatile.Read(ref _state);
	public WebRuntimeProgramStopResult? LastStopResult { get; private set; }
	public WebRuntimeProgramCommandTarget CommandTarget => new(_registry, this);

	public async Task ActivateAsync(CancellationToken ct)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
		await _lifecycleGate.WaitAsync(ct).ConfigureAwait(false);
		try
		{
			if (IsActive)
				return;
			Volatile.Write(ref _state, (int)WebRuntimeProgramHostState.Starting);
			try
			{
				foreach (var program in _programs)
					_registrations.Add(_registry.Register(program));
				foreach (var lifecycle in _programs.OfType<IWebRuntimeBusinessProgramLifecycle>())
				{
					await lifecycle.InitializeAsync(ct).ConfigureAwait(false);
					_initialized.Add(lifecycle);
				}
				_registry.AllowExecutions();
				Volatile.Write(ref _active, 1);
				Volatile.Write(ref _state, (int)WebRuntimeProgramHostState.Running);
			}
			catch (Exception activationError)
			{
				var rollbackErrors = await RollbackInitializedAsync().ConfigureAwait(false);
				DisposeRegistrations();
				Volatile.Write(ref _state, (int)WebRuntimeProgramHostState.Faulted);
				if (rollbackErrors.Count == 0)
					throw;
				throw new AggregateException("WebRuntime activation failed and rollback reported errors.", [activationError, .. rollbackErrors]);
			}
		}
		finally
		{
			_lifecycleGate.Release();
		}
	}

	public async Task<WebRuntimeProgramStopResult> StopAsync(CancellationToken ct)
	{
		await _lifecycleGate.WaitAsync(ct).ConfigureAwait(false);
		try
		{
			if (!IsActive && _registrations.Count == 0)
				return LastStopResult ?? new WebRuntimeProgramStopResult(State, [], State == WebRuntimeProgramHostState.TimedOut);
			Volatile.Write(ref _state, (int)WebRuntimeProgramHostState.Stopping);
			var errors = new List<Exception>();
			try { await _registry.StopAllAsync(ct).ConfigureAwait(false); }
			catch (Exception ex) { errors.Add(ex); }
			for (var index = _initialized.Count - 1; index >= 0; index--)
			{
				try
				{
					await _initialized[index].StopAsync(ct).ConfigureAwait(false);
				}
				catch (Exception ex) { errors.Add(ex); }
				finally { _initialized.RemoveAt(index); }
			}
			Volatile.Write(ref _active, 0);
			DisposeRegistrations();
			var timedOut = errors.Any(error => error is OperationCanceledException) && ct.IsCancellationRequested;
			var finalState = timedOut
				? WebRuntimeProgramHostState.TimedOut
				: errors.Count == 0 ? WebRuntimeProgramHostState.Stopped : WebRuntimeProgramHostState.StoppedWithErrors;
			Volatile.Write(ref _state, (int)finalState);
			LastStopResult = new WebRuntimeProgramStopResult(finalState, errors.Select(error => error.Message).ToArray(), timedOut);
			return LastStopResult;
		}
		finally
		{
			_lifecycleGate.Release();
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
			return;
		try { await StopAsync(CancellationToken.None).ConfigureAwait(false); }
		finally
		{
			_initialized.Clear();
			DisposeRegistrations();
			Volatile.Write(ref _state, (int)WebRuntimeProgramHostState.Disposed);
			_lifecycleGate.Dispose();
		}
	}

	private async Task<IReadOnlyList<Exception>> RollbackInitializedAsync()
	{
		var errors = new List<Exception>();
		for (var index = _initialized.Count - 1; index >= 0; index--)
		{
			try { await _initialized[index].StopAsync(CancellationToken.None).ConfigureAwait(false); }
			catch (Exception ex) { errors.Add(ex); }
		}
		_initialized.Clear();
		return errors;
	}

	private void DisposeRegistrations()
	{
		for (var index = _registrations.Count - 1; index >= 0; index--)
			_registrations[index].Dispose();
		_registrations.Clear();
	}
}

using Iwesun.Runtime.Diagnostics;

namespace Iwesun.Runtime.WebView2;

public sealed class WebAmbientInputOptions
{
	public TimeSpan MouseIntervalMin { get; init; } = TimeSpan.FromMilliseconds(700);
	public TimeSpan MouseIntervalMax { get; init; } = TimeSpan.FromMilliseconds(1800);
	public TimeSpan KeyboardIntervalMin { get; init; } = TimeSpan.FromSeconds(12);
	public TimeSpan KeyboardIntervalMax { get; init; } = TimeSpan.FromSeconds(32);
}

public enum WebAmbientInputState { Created, Starting, Running, Stopping, Stopped, Faulted, Disposed }

/// <summary>Owns managed mouse and keyboard ambient-input loops for one WebRuntime session.</summary>
public sealed class WebAmbientInputController : IAsyncDisposable
{
	private readonly string _sessionId;
	private readonly Func<CancellationToken, Task> _mousePulse;
	private readonly Func<CancellationToken, Task> _keyboardPulse;
	private readonly WebAmbientInputOptions _options;
	private readonly object _gate = new();
	private CancellationTokenSource? _stop;
	private RTask? _mouseTask;
	private RTask? _keyboardTask;
	private int _state = (int)WebAmbientInputState.Created;
	private int _disposed;
	private string? _lastError;

	public WebAmbientInputState State => (WebAmbientInputState)Volatile.Read(ref _state);
	public bool IsStarted => State is WebAmbientInputState.Starting or WebAmbientInputState.Running;
	public bool IsStopped => State is WebAmbientInputState.Stopped or WebAmbientInputState.Faulted or WebAmbientInputState.Disposed;
	public string? LastError => Volatile.Read(ref _lastError);

	public WebAmbientInputController(
		string sessionId,
		Func<CancellationToken, Task> mousePulse,
		Func<CancellationToken, Task> keyboardPulse,
		WebAmbientInputOptions? options = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
		ArgumentNullException.ThrowIfNull(mousePulse);
		ArgumentNullException.ThrowIfNull(keyboardPulse);
		_sessionId = sessionId;
		_mousePulse = mousePulse;
		_keyboardPulse = keyboardPulse;
		_options = options ?? new WebAmbientInputOptions();
		ValidateRange(_options.MouseIntervalMin, _options.MouseIntervalMax, nameof(options));
		ValidateRange(_options.KeyboardIntervalMin, _options.KeyboardIntervalMax, nameof(options));
	}

	public void Start(bool mouse, bool keyboard)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
		lock (_gate)
		{
			if (State is WebAmbientInputState.Starting or WebAmbientInputState.Running)
				return;
			if (State == WebAmbientInputState.Stopping)
				throw new InvalidOperationException("Ambient input is stopping.");
			if (State == WebAmbientInputState.Faulted && (_mouseTask is not null || _keyboardTask is not null || _stop is not null))
				throw new InvalidOperationException("Ambient input faulted. Call StopAsync before restarting it.");
			Volatile.Write(ref _state, (int)WebAmbientInputState.Starting);
			Volatile.Write(ref _lastError, null);
			_stop = new CancellationTokenSource();
			_mouseTask = mouse ? CreateLoopTask("mouse", _mousePulse, _options.MouseIntervalMin, _options.MouseIntervalMax, _stop.Token) : null;
			_keyboardTask = keyboard ? CreateLoopTask("keyboard", _keyboardPulse, _options.KeyboardIntervalMin, _options.KeyboardIntervalMax, _stop.Token) : null;
			Volatile.Write(ref _state, (int)WebAmbientInputState.Running);
		}
	}

	public async Task StopAsync()
	{
		CancellationTokenSource? stop;
		RTask? mouseTask;
		RTask? keyboardTask;
		lock (_gate)
		{
			if (State is WebAmbientInputState.Created or WebAmbientInputState.Stopped or WebAmbientInputState.Disposed)
			{
				if (State != WebAmbientInputState.Disposed)
					Volatile.Write(ref _state, (int)WebAmbientInputState.Stopped);
				return;
			}
			Volatile.Write(ref _state, (int)WebAmbientInputState.Stopping);
			stop = _stop;
			mouseTask = _mouseTask;
			keyboardTask = _keyboardTask;
		}
		if (stop is not null)
			await stop.CancelAsync().ConfigureAwait(false);
		await AwaitCompletionAsync(mouseTask).ConfigureAwait(false);
		await AwaitCompletionAsync(keyboardTask).ConfigureAwait(false);
		lock (_gate)
		{
			mouseTask?.Dispose();
			keyboardTask?.Dispose();
			stop?.Dispose();
			_mouseTask = null;
			_keyboardTask = null;
			_stop = null;
			if (State != WebAmbientInputState.Faulted)
				Volatile.Write(ref _state, (int)WebAmbientInputState.Stopped);
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
			return;
		await StopAsync().ConfigureAwait(false);
		Volatile.Write(ref _state, (int)WebAmbientInputState.Disposed);
	}

	private RTask CreateLoopTask(string inputKind, Func<CancellationToken, Task> pulse, TimeSpan min, TimeSpan max, CancellationToken cancellationToken) =>
		RuntimeInjector.CreateTask(
			runtimeToken => RunLoopGuardedAsync(pulse, min, max, runtimeToken).GetAwaiter().GetResult(),
			unitId: $"webruntime.task.ambient-{inputKind}.{_sessionId}",
			category: $"webruntime-ambient-{inputKind}",
			sourceLocation: nameof(WebAmbientInputController),
			cancellationToken: cancellationToken,
			blocksShutdown: false);

	private async Task RunLoopGuardedAsync(Func<CancellationToken, Task> pulse, TimeSpan min, TimeSpan max, CancellationToken ct)
	{
		try { await RunLoopAsync(pulse, min, max, ct).ConfigureAwait(false); }
		catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
		catch (Exception ex)
		{
			Volatile.Write(ref _lastError, ex.Message);
			Volatile.Write(ref _state, (int)WebAmbientInputState.Faulted);
		}
	}

	private static async Task RunLoopAsync(
		Func<CancellationToken, Task> pulse,
		TimeSpan min,
		TimeSpan max,
		CancellationToken ct)
	{
		while (!ct.IsCancellationRequested)
		{
			await Task.Delay(RandomDelay(min, max), ct).ConfigureAwait(false);
			await pulse(ct).ConfigureAwait(false);
		}
	}

	private static TimeSpan RandomDelay(TimeSpan min, TimeSpan max)
	{
		var minMilliseconds = checked((int)Math.Ceiling(min.TotalMilliseconds));
		var maxMilliseconds = checked((int)Math.Ceiling(max.TotalMilliseconds));
		return TimeSpan.FromMilliseconds(Random.Shared.Next(minMilliseconds, maxMilliseconds + 1));
	}

	private static async Task AwaitCompletionAsync(RTask? task)
	{
		if (task is null)
			return;
		try
		{
			await task;
		}
		catch (OperationCanceledException)
		{
		}
	}

	private static void ValidateRange(TimeSpan min, TimeSpan max, string parameterName)
	{
		if (min <= TimeSpan.Zero || max < min || max.TotalMilliseconds > int.MaxValue - 1)
			throw new ArgumentOutOfRangeException(parameterName, "Ambient input intervals must be positive and ordered.");
	}
}

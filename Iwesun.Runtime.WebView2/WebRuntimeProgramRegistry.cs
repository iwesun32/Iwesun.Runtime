using System.Collections.Concurrent;
using Iwesun.Runtime.Diagnostics;

namespace Iwesun.Runtime.WebView2;

public sealed record WebRuntimeProgramActionDescriptor(string Action, string Description, bool MutatesState = false);

public sealed record WebRuntimeProgramDescriptor(
	string ProgramId,
	string DisplayName,
	string Version,
	IReadOnlyList<WebRuntimeProgramActionDescriptor> Actions,
	string Description = "");

/// <summary>A prepared C# business program compiled with the host application.</summary>
public interface IWebRuntimeBusinessProgram
{
	WebRuntimeProgramDescriptor Descriptor { get; }
	string ProgramId => Descriptor.ProgramId;
	Task<WebRuntimeProgramResult> ExecuteAsync(WebRuntimeProgramContext context, CancellationToken ct);
}

public interface IWebRuntimeBusinessProgramLifecycle
{
	Task InitializeAsync(CancellationToken ct);
	Task StopAsync(CancellationToken ct);
}

public sealed record WebRuntimeProgramContext(
	string ExecutionId,
	string ProgramId,
	WebRuntimeControlRequest Request,
	DateTimeOffset StartedUtc);

public sealed record WebRuntimeProgramResult(
	bool Success,
	string Action,
	object? Value,
	string? Error,
	string Code = "OK",
	bool Retryable = false)
{
	public static WebRuntimeProgramResult Ok(string action, object? value = null) => new(true, action, value, null);
	public static WebRuntimeProgramResult Fail(string action, string error, string code = "WEBRUNTIME_PROGRAM_ERROR", bool retryable = false) =>
		new(false, action, null, error, code, retryable);
}

public sealed record WebRuntimeProgramExecutionEvent(
	string ExecutionId,
	string ProgramId,
	string Action,
	string State,
	DateTimeOffset TimestampUtc,
	string? Error = null);

public sealed record WebRuntimeProgramExecutionSnapshot(
	string ExecutionId,
	string ProgramId,
	string Action,
	string State,
	DateTimeOffset StartedUtc,
	DateTimeOffset? CompletedUtc,
	string? Error);

public sealed record WebRuntimePlatformDescriptor(
	string PlatformVersion,
	string ProtocolSchema,
	string Domain,
	string WireFormat,
	bool SupportsCompiledInterception,
	bool SupportsRequiredInterception,
	bool ExecutesSourceCode,
	bool PageJavaScriptUnaffected,
	IReadOnlyList<string> Features);

public sealed record WebRuntimeProgramRegistrySnapshot(
	WebRuntimePlatformDescriptor Platform,
	IReadOnlyList<WebRuntimeProgramDescriptor> Programs,
	IReadOnlyList<WebRuntimeProgramExecutionSnapshot> ActiveExecutions,
	IReadOnlyList<WebRuntimeProgramExecutionSnapshot> RecentExecutions,
	long CompletedCount,
	long FailedCount,
	long TimedOutCount,
	long CanceledCount,
	long ObserverFailureCount,
	IReadOnlyList<WebRuntimeProgramActionMetricSnapshot> ActionMetrics);

public sealed record WebRuntimeProgramActionMetricSnapshot(
	string ProgramId,
	string Action,
	long StartedCount,
	long CompletedCount,
	long FailedCount,
	long TimedOutCount,
	long CanceledCount);

public sealed record WebRuntimeProgramExecutionHandle(
	string ExecutionId,
	Task Started,
	Task<WebRuntimeProgramResult> Completion);

/// <summary>Registers, supervises, cancels and monitors compiled C# WebRuntime programs.</summary>
public sealed class WebRuntimeProgramRegistry
{
	private const int HistoryLimit = 128;
	private readonly ConcurrentDictionary<string, IWebRuntimeBusinessProgram> _programs = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, ActiveExecution> _active = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentQueue<WebRuntimeProgramExecutionSnapshot> _history = new();
	private readonly ConcurrentDictionary<string, ActionMetrics> _actionMetrics = new(StringComparer.OrdinalIgnoreCase);
	private readonly object _executionGate = new();
	private bool _stopping;
	private long _completedCount;
	private long _failedCount;
	private long _timedOutCount;
	private long _canceledCount;
	private long _observerFailureCount;

	public event EventHandler<WebRuntimeProgramExecutionEvent>? ExecutionChanged;
	public IReadOnlyCollection<string> ProgramIds => _programs.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();

	public IDisposable Register(IWebRuntimeBusinessProgram program)
	{
		ArgumentNullException.ThrowIfNull(program);
		ValidateDescriptor(program.Descriptor);
		if (!_programs.TryAdd(program.ProgramId, program))
			throw new InvalidOperationException($"A WebRuntime C# program is already registered as '{program.ProgramId}'.");
		return new Registration(this, program.ProgramId, program);
	}

	public bool TryResolve(string programId, out IWebRuntimeBusinessProgram? program) => _programs.TryGetValue(programId, out program);

	internal async Task InitializeAllAsync(CancellationToken ct)
	{
		foreach (var program in _programs.Values.OfType<IWebRuntimeBusinessProgramLifecycle>())
			await program.InitializeAsync(ct).ConfigureAwait(false);
		AllowExecutions();
	}

	internal void AllowExecutions()
	{
		lock (_executionGate)
			_stopping = false;
	}

	internal async Task StopAllAsync(CancellationToken ct)
	{
		ActiveExecution[] executions;
		lock (_executionGate)
		{
			_stopping = true;
			executions = _active.Values.ToArray();
		}
		foreach (var execution in executions)
			execution.Cancel("registry-stop");
		if (executions.Length > 0)
			await Task.WhenAll(executions.Select(execution => execution.Completion)).WaitAsync(ct).ConfigureAwait(false);
	}

	public Task<WebRuntimeProgramResult> ExecuteAsync(string programId, WebRuntimeControlRequest request, TimeSpan timeout, CancellationToken ct) =>
		StartExecution(programId, request, timeout, ct).Completion;

	public WebRuntimeProgramExecutionHandle StartExecution(
		string programId,
		WebRuntimeControlRequest request,
		TimeSpan timeout,
		CancellationToken ct)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(programId);
		ArgumentNullException.ThrowIfNull(request);
		if (timeout <= TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(nameof(timeout));

		lock (_executionGate)
		{
			if (_stopping)
				throw new InvalidOperationException("WebRuntime program registry is stopping and does not accept new executions.");
			var executionId = Guid.NewGuid().ToString("N");
			var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			var completion = RunExecutionAsync(executionId, programId, request, timeout, ct, started);
			return new WebRuntimeProgramExecutionHandle(executionId, started.Task, completion);
		}
	}

	public bool Cancel(string executionId, string reason = "canceled") =>
		_active.TryGetValue(executionId, out var execution) && execution.Cancel(reason);

	public WebRuntimeProgramRegistrySnapshot Snapshot() => new(
		new WebRuntimePlatformDescriptor(
			"1.0",
			RuntimeDiagnosticProtocol.V2Schema,
			WebRuntimeProtocol.Domain,
			"int32-le length + UTF-8 JSON",
			SupportsCompiledInterception: true,
			SupportsRequiredInterception: true,
			ExecutesSourceCode: false,
			PageJavaScriptUnaffected: true,
			[
				"program.catalog", "program.lifecycle", "program.execute", "program.cancel",
				"execution.active", "execution.history", "execution.metrics", "runtime.watch", "runtime.hook",
				"input.mouse", "input.keyboard", "input.ambient", "csharp.devtools.dom"
			]),
		_programs.Values.Select(program => program.Descriptor).OrderBy(item => item.ProgramId, StringComparer.OrdinalIgnoreCase).ToArray(),
		_active.Values.Select(item => item.Snapshot("Running")).OrderBy(item => item.StartedUtc).ToArray(),
		_history.ToArray(),
		Interlocked.Read(ref _completedCount),
		Interlocked.Read(ref _failedCount),
		Interlocked.Read(ref _timedOutCount),
		Interlocked.Read(ref _canceledCount),
		Interlocked.Read(ref _observerFailureCount),
		_actionMetrics.Values.Select(item => item.Snapshot()).OrderBy(item => item.ProgramId, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Action, StringComparer.OrdinalIgnoreCase).ToArray());

	private async Task<WebRuntimeProgramResult> RunExecutionAsync(
		string executionId,
		string programId,
		WebRuntimeControlRequest request,
		TimeSpan timeout,
		CancellationToken externalToken,
		TaskCompletionSource started)
	{
		if (!_programs.TryGetValue(programId, out var program))
		{
			started.TrySetResult();
			Interlocked.Increment(ref _failedCount);
			return WebRuntimeProgramResult.Fail(request.Action, $"WebRuntime C# program '{programId}' is not registered.");
		}
		if (!program.Descriptor.Actions.Any(item => item.Action.Equals(request.Action, StringComparison.OrdinalIgnoreCase)))
		{
			started.TrySetResult();
			Interlocked.Increment(ref _failedCount);
			return WebRuntimeProgramResult.Fail(request.Action, $"C# program '{programId}' does not declare action '{request.Action}'.");
		}

		var context = new WebRuntimeProgramContext(executionId, programId, request, DateTimeOffset.UtcNow);
		using var timeoutCts = new CancellationTokenSource(timeout);
		using var executionCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken, timeoutCts.Token);
		var active = new ActiveExecution(context, executionCts);
		var metrics = _actionMetrics.GetOrAdd($"{programId}\n{request.Action}", _ => new ActionMetrics(programId, request.Action));
		metrics.IncrementStarted();
		if (!_active.TryAdd(executionId, active))
			throw new InvalidOperationException($"Duplicate WebRuntime execution id '{executionId}'.");
		try
		{
			Publish(context, "Started");
			started.TrySetResult();

			WebRuntimeProgramResult result;
			string state;
			try
			{
				result = await program.ExecuteAsync(context, executionCts.Token).ConfigureAwait(false);
				state = result.Success ? "Completed" : "Failed";
				Interlocked.Increment(ref result.Success ? ref _completedCount : ref _failedCount);
				if (result.Success) metrics.IncrementCompleted(); else metrics.IncrementFailed();
			}
			catch (OperationCanceledException) when (active.CancelReason is not null || externalToken.IsCancellationRequested)
			{
				state = "Canceled";
				Interlocked.Increment(ref _canceledCount);
				metrics.IncrementCanceled();
				result = WebRuntimeProgramResult.Fail(request.Action, active.CancelReason ?? "WebRuntime C# program execution was canceled.", "WEBRUNTIME_PROGRAM_CANCELED");
			}
			catch (OperationCanceledException)
			{
				state = "TimedOut";
				Interlocked.Increment(ref _timedOutCount);
				metrics.IncrementTimedOut();
				result = WebRuntimeProgramResult.Fail(request.Action, "WebRuntime C# program execution timed out.", "WEBRUNTIME_PROGRAM_TIMEOUT", retryable: true);
			}
			catch (Exception ex)
			{
				state = "Faulted";
				Interlocked.Increment(ref _failedCount);
				metrics.IncrementFailed();
				result = WebRuntimeProgramResult.Fail(request.Action, ex.Message);
			}

			var completed = active.Snapshot(state, result.Error);
			_history.Enqueue(completed);
			while (_history.Count > HistoryLimit && _history.TryDequeue(out _)) { }
			Publish(context, state, result.Error);
			return result;
		}
		finally
		{
			started.TrySetResult();
			_active.TryRemove(executionId, out _);
			active.Complete();
		}
	}

	private void Publish(WebRuntimeProgramContext context, string state, string? error = null)
	{
		var notification = new WebRuntimeProgramExecutionEvent(
			context.ExecutionId, context.ProgramId, context.Request.Action, state, DateTimeOffset.UtcNow, error);
		foreach (EventHandler<WebRuntimeProgramExecutionEvent> handler in ExecutionChanged?.GetInvocationList() ?? [])
		{
			try { handler(this, notification); }
			catch { Interlocked.Increment(ref _observerFailureCount); }
		}
	}

	private static void ValidateDescriptor(WebRuntimeProgramDescriptor descriptor)
	{
		ArgumentNullException.ThrowIfNull(descriptor);
		ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.ProgramId);
		if (descriptor.Actions.GroupBy(action => action.Action, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
			throw new ArgumentException("WebRuntime C# program action names must be unique.", nameof(descriptor));
	}

	private sealed class ActiveExecution(WebRuntimeProgramContext context, CancellationTokenSource cancellation)
	{
		private string? _cancelReason;
		private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public string? CancelReason => Volatile.Read(ref _cancelReason);
		public Task Completion => _completion.Task;
		public bool Cancel(string reason)
		{
			if (Interlocked.CompareExchange(ref _cancelReason, reason, null) is not null)
				return false;
			cancellation.Cancel();
			return true;
		}
		public void Complete() => _completion.TrySetResult();
		public WebRuntimeProgramExecutionSnapshot Snapshot(string state, string? error = null) => new(
			context.ExecutionId, context.ProgramId, context.Request.Action, state, context.StartedUtc,
			state == "Running" ? null : DateTimeOffset.UtcNow, error);
	}

	private sealed class Registration(WebRuntimeProgramRegistry owner, string programId, IWebRuntimeBusinessProgram program) : IDisposable
	{
		private int _disposed;
		public void Dispose()
		{
			if (Interlocked.Exchange(ref _disposed, 1) == 0)
				owner._programs.TryRemove(new KeyValuePair<string, IWebRuntimeBusinessProgram>(programId, program));
		}
	}

	private sealed class ActionMetrics(string programId, string action)
	{
		private long _started;
		private long _completed;
		private long _failed;
		private long _timedOut;
		private long _canceled;
		public void IncrementStarted() => Interlocked.Increment(ref _started);
		public void IncrementCompleted() => Interlocked.Increment(ref _completed);
		public void IncrementFailed() => Interlocked.Increment(ref _failed);
		public void IncrementTimedOut() => Interlocked.Increment(ref _timedOut);
		public void IncrementCanceled() => Interlocked.Increment(ref _canceled);
		public WebRuntimeProgramActionMetricSnapshot Snapshot() => new(
			programId, action,
			Interlocked.Read(ref _started), Interlocked.Read(ref _completed), Interlocked.Read(ref _failed),
			Interlocked.Read(ref _timedOut), Interlocked.Read(ref _canceled));
	}
}

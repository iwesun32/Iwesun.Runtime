using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Iwesun.Runtime.Networks;

/// <summary>
/// Event-driven process adapter. It starts one process per send item and
/// translates process events into one receive FIFO value.
/// </summary>
public class ProcessCommandEndpoint : NetworkAsyncEndpointBase<ProcessCommandRequest, ProcessCommandResult>
{
	private readonly ConcurrentDictionary<Guid, ActiveProcess> _active = new();

	public ProcessCommandEndpoint(
		SynchronizationContext? eventContext = null,
		int maxSendQueueLength = 256,
		int maxReceiveQueueLength = 256,
		int sendQueueAvailableThreshold = 1)
		: base(eventContext, maxSendQueueLength, maxReceiveQueueLength, sendQueueAvailableThreshold)
	{
	}

	protected override bool OnValidateSend(ProcessCommandRequest item, out string? reason)
	{
		reason = item.RequestId == Guid.Empty
			? NetworkAccessFailureCodes.RequestIdEmpty
			: null;
		return reason is null;
	}

	protected override bool OnFilterSend(ProcessCommandRequest item, out string? reason)
	{
		if (string.IsNullOrWhiteSpace(item.FileName))
		{
			reason = "process-file-empty";
			return false;
		}

		reason = null;
		return true;
	}

	protected override bool OnAnalyzeSend(
		ProcessCommandRequest item,
		out ProcessCommandRequest analyzedItem,
		out string? reason)
	{
		analyzedItem = item with
		{
			TimeoutMs = item.TimeoutMs <= 0 ? 3000 : item.TimeoutMs
		};
		reason = null;
		return true;
	}

	protected override NetworkSendResult OnSend(ProcessCommandRequest item)
	{
		try
		{
			var startInfo = BuildStartInfo(item);
			var process = new Process
			{
				StartInfo = startInfo,
				EnableRaisingEvents = true
			};

			var active = new ActiveProcess(item, process);
			_active[item.RequestId] = active;

			process.OutputDataReceived += (_, e) =>
			{
				if (e.Data == null)
				{
					if (active.MarkStdOutCompleted() && active.IsReadyToFinalize() && active.TryBeginFinalization())
					{
						CompleteProcess(item.RequestId, timedOut: false, null);
					}

					return;
				}

				active.AppendOutput(e.Data);
			};

			process.ErrorDataReceived += (_, e) =>
			{
				if (e.Data == null)
				{
					if (active.MarkStdErrCompleted() && active.IsReadyToFinalize() && active.TryBeginFinalization())
					{
						CompleteProcess(item.RequestId, timedOut: false, null);
					}

					return;
				}

				active.AppendError(e.Data);
			};

			process.Exited += (_, _) =>
			{
				if (active.MarkExited() && active.IsReadyToFinalize() && active.TryBeginFinalization())
				{
					CompleteProcess(item.RequestId, timedOut: false, null);
				}
			};

			if (!process.Start())
			{
				_active.TryRemove(item.RequestId, out _);
				process.Dispose();
				return NetworkSendResult.Failed("process-start-returned-false");
			}

			process.BeginOutputReadLine();
			process.BeginErrorReadLine();
			active.StartTimeoutTimer(() => CompleteProcess(item.RequestId, timedOut: true, null));
			return NetworkSendResult.Sent();
		}
		catch (Exception ex)
		{
			_active.TryRemove(item.RequestId, out _);
			return IsHardwareProcessException(ex)
				? NetworkSendResult.HardwareBlocked("process-start-hardware-blocked", ex)
				: NetworkSendResult.Failed("process-start-failed", ex);
		}
	}

	protected override bool OnAnalyzeReceive(object raw, Action<ProcessCommandResult> emit, out string? reason)
	{
		if (raw is not ProcessCommandRaw completed)
		{
			return base.OnAnalyzeReceive(raw, emit, out reason);
		}

		emit(new ProcessCommandResult(
			completed.Request.RequestId,
			completed.Request.FileName,
			completed.Request.Arguments,
			completed.ExitCode,
			completed.TimedOut,
			completed.StandardOutput,
			completed.StandardError,
			completed.Error,
			DateTime.UtcNow));

		reason = null;
		return true;
	}

	protected override void OnStopping()
	{
		ClearActiveProcesses();
	}

	protected override void OnResetHardware()
	{
		ClearActiveProcesses();
	}

	protected virtual ProcessStartInfo BuildStartInfo(ProcessCommandRequest item)
	{
		var startInfo = new ProcessStartInfo(item.FileName, item.Arguments ?? string.Empty)
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true
		};

		if (!string.IsNullOrWhiteSpace(item.WorkingDirectory))
		{
			startInfo.WorkingDirectory = item.WorkingDirectory;
		}

		return startInfo;
	}

	private void CompleteProcess(Guid requestId, bool timedOut, Exception? error)
	{
		if (!_active.TryRemove(requestId, out var active))
		{
			return;
		}

		active.DisposeTimer();

		var exitCode = -1;
		try
		{
			if (timedOut && !active.Process.HasExited)
			{
				active.Process.Kill(entireProcessTree: true);
			}

			if (active.Process.HasExited)
			{
				exitCode = active.Process.ExitCode;
			}
		}
		catch (Exception ex)
		{
			error ??= ex;
		}
		finally
		{
			active.Process.Dispose();
		}

		PublishReceivedRaw(new ProcessCommandRaw(
			active.Request,
			exitCode,
			timedOut,
			active.GetOutput(),
			active.GetError(),
			error));
	}

	private void ClearActiveProcesses()
	{
		foreach (var requestId in _active.Keys)
		{
			CompleteProcess(requestId, timedOut: true, null);
		}

		_active.Clear();
	}

	private static bool IsHardwareProcessException(Exception ex)
	{
		return ex is Win32Exception win32 && win32.NativeErrorCode is 8 or 1450 or 1455;
	}

	private sealed class ActiveProcess
	{
		private readonly StringBuilder _standardOutput = new();
		private readonly StringBuilder _standardError = new();
		private readonly object _outputGate = new();
		private Timer? _timeoutTimer;
		private int _stdoutCompleted;
		private int _stderrCompleted;
		private int _exited;
		private int _finalizationStarted;

		public ActiveProcess(ProcessCommandRequest request, Process process)
		{
			Request = request;
			Process = process;
		}

		public ProcessCommandRequest Request { get; }
		public Process Process { get; }

		public void StartTimeoutTimer(Action callback)
		{
			_timeoutTimer = new Timer(_ => callback(), null, Request.TimeoutMs, Timeout.Infinite);
		}

		public void DisposeTimer()
		{
			_timeoutTimer?.Dispose();
			_timeoutTimer = null;
		}

		public bool MarkStdOutCompleted()
		{
			Interlocked.Exchange(ref _stdoutCompleted, 1);
			return true;
		}

		public bool MarkStdErrCompleted()
		{
			Interlocked.Exchange(ref _stderrCompleted, 1);
			return true;
		}

		public bool MarkExited()
		{
			Interlocked.Exchange(ref _exited, 1);
			return true;
		}

		public bool IsReadyToFinalize()
		{
			return Volatile.Read(ref _stdoutCompleted) == 1
				&& Volatile.Read(ref _stderrCompleted) == 1
				&& Volatile.Read(ref _exited) == 1;
		}

		public bool TryBeginFinalization()
		{
			return Interlocked.CompareExchange(ref _finalizationStarted, 1, 0) == 0;
		}

		public void AppendOutput(string? line)
		{
			if (line == null)
			{
				return;
			}

			lock (_outputGate)
			{
				_standardOutput.AppendLine(line);
			}
		}

		public void AppendError(string? line)
		{
			if (line == null)
			{
				return;
			}

			lock (_outputGate)
			{
				_standardError.AppendLine(line);
			}
		}

		public string GetOutput()
		{
			lock (_outputGate)
			{
				return _standardOutput.ToString();
			}
		}

		public string GetError()
		{
			lock (_outputGate)
			{
				return _standardError.ToString();
			}
		}
	}

	private readonly record struct ProcessCommandRaw(
		ProcessCommandRequest Request,
		int ExitCode,
		bool TimedOut,
		string StandardOutput,
		string StandardError,
		Exception? Error);
}

public readonly record struct ProcessCommandRequest(
	Guid RequestId,
	string FileName,
	string Arguments = "",
	string? WorkingDirectory = null,
	int TimeoutMs = 3000);

public readonly record struct ProcessCommandResult(
	Guid RequestId,
	string FileName,
	string Arguments,
	int ExitCode,
	bool TimedOut,
	string StandardOutput,
	string StandardError,
	Exception? Error,
	DateTime CompletedAtUtc);

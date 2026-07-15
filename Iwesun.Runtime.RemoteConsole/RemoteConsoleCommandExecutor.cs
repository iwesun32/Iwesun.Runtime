using System.Collections.Concurrent;
using System.Diagnostics;
using Iwesun.Runtime.RemoteConsole.Protocol;
using Microsoft.Extensions.Hosting;

namespace Iwesun.Runtime.RemoteConsole;

public sealed class RemoteConsoleCommandExecutor : IHostedService, IDisposable
{
	private readonly RemoteConsoleOptions _options;
	private readonly RemoteConsoleJobStore _jobs;
	private readonly ConcurrentDictionary<string, RemoteConsoleOutputBuffer> _outputs = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, byte> _runningJobs = new(StringComparer.OrdinalIgnoreCase);
	private readonly CancellationTokenSource _stopping = new();

	public RemoteConsoleCommandExecutor(RemoteConsoleOptions options, RemoteConsoleJobStore jobs)
	{
		_options = options ?? throw new ArgumentNullException(nameof(options));
		_jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(_options.MaxOutputBytesPerJob);
	}

	public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public Task StopAsync(CancellationToken cancellationToken)
	{
		_stopping.Cancel();
		foreach (var jobId in _runningJobs.Keys)
			_jobs.Interrupt(jobId, "RemoteConsole service stopped; the operating-system child process was not terminated.");
		return Task.CompletedTask;
	}

	public async Task ExecuteAsync(string jobId, string workspacePath, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
		ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
		var descriptor = _jobs.GetExecution(jobId)
			?? throw new KeyNotFoundException($"RemoteConsole job '{jobId}' was not found.");
		if (descriptor.State != RemoteConsoleJobState.Starting)
			throw new InvalidOperationException($"RemoteConsole job '{jobId}' is not in Starting state.");

		var output = _outputs.GetOrAdd(jobId, _ => new RemoteConsoleOutputBuffer(_options.MaxOutputBytesPerJob));
		var startInfo = BuildStartInfo(descriptor.Request, workspacePath);
		using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
		try
		{
			if (!process.Start())
				throw new InvalidOperationException("The configured shell process did not start.");
		}
		catch (Exception ex)
		{
			_jobs.FailStart(jobId, ex.Message);
			return;
		}

		var running = _jobs.MarkRunning(jobId);
		if (!running.Ok)
			return;
		_runningJobs[jobId] = 0;
		using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopping.Token);
		try
		{
			var stdout = PumpAsync(process.StandardOutput, output, RemoteConsoleOutputStream.Stdout, linked.Token);
			var stderr = PumpAsync(process.StandardError, output, RemoteConsoleOutputStream.Stderr, linked.Token);
			await Task.WhenAll(process.WaitForExitAsync(linked.Token), stdout, stderr).ConfigureAwait(false);
			_jobs.Complete(jobId, process.ExitCode);
		}
		catch (OperationCanceledException) when (linked.IsCancellationRequested)
		{
			_jobs.Interrupt(jobId, "RemoteConsole output monitoring stopped; the operating-system child process was not terminated.");
		}
		finally
		{
			_runningJobs.TryRemove(jobId, out _);
		}
	}

	public async Task<RemoteConsoleFollowResult> FollowAsync(
		string jobId,
		long afterSequence,
		int limit,
		int waitMs,
		CancellationToken cancellationToken)
	{
		var status = _jobs.Get(jobId);
		if (!status.Ok || status.Job is null)
			throw new KeyNotFoundException($"RemoteConsole job '{jobId}' was not found.");
		var terminal = IsTerminal(status.Job.State);
		var buffer = _outputs.GetOrAdd(jobId, _ => new RemoteConsoleOutputBuffer(_options.MaxOutputBytesPerJob));
		var read = await buffer.ReadAsync(
			afterSequence,
			limit,
			terminal ? 0 : Math.Min(waitMs, RemoteConsoleProtocol.MaxFollowWaitMs),
			cancellationToken).ConfigureAwait(false);
		status = _jobs.Get(jobId);
		return new RemoteConsoleFollowResult(
			jobId,
			status.Job!.State,
			read.Chunks,
			read.EarliestAvailableSequence,
			read.Truncated,
			status.Job.ExitCode);
	}

	public void Dispose() => _stopping.Dispose();

	private ProcessStartInfo BuildStartInfo(RemoteConsoleSubmitRequest request, string workspacePath)
	{
		var workspaceRoot = Path.GetFullPath(workspacePath);
		var workingDirectory = Path.GetFullPath(Path.Combine(workspaceRoot, request.WorkingDirectory));
		if (!workingDirectory.Equals(workspaceRoot, StringComparison.OrdinalIgnoreCase)
			&& !workingDirectory.StartsWith(workspaceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException("The command working directory is outside the workspace.");

		var startInfo = request.Shell.ToLowerInvariant() switch
		{
			"powershell" or "pwsh" => CreatePowerShellStartInfo(request.Command),
			"cmd" => CreateCommandPromptStartInfo(request.Command),
			_ => throw new InvalidOperationException($"Shell '{request.Shell}' is not configured.")
		};
		startInfo.WorkingDirectory = workingDirectory;
		foreach (var pair in request.Environment)
			startInfo.Environment[pair.Key] = pair.Value;
		return startInfo;
	}

	private ProcessStartInfo CreatePowerShellStartInfo(string command)
	{
		var startInfo = CreateBaseStartInfo(_options.PowerShellPath);
		startInfo.ArgumentList.Add("-NoLogo");
		startInfo.ArgumentList.Add("-NonInteractive");
		startInfo.ArgumentList.Add("-Command");
		startInfo.ArgumentList.Add(command);
		return startInfo;
	}

	private ProcessStartInfo CreateCommandPromptStartInfo(string command)
	{
		var startInfo = CreateBaseStartInfo(_options.CommandPromptPath);
		startInfo.ArgumentList.Add("/D");
		startInfo.ArgumentList.Add("/S");
		startInfo.ArgumentList.Add("/C");
		startInfo.ArgumentList.Add(command);
		return startInfo;
	}

	private static ProcessStartInfo CreateBaseStartInfo(string executable) =>
		new(executable)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};

	private static async Task PumpAsync(
		StreamReader reader,
		RemoteConsoleOutputBuffer output,
		RemoteConsoleOutputStream stream,
		CancellationToken cancellationToken)
	{
		while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
			output.Append(stream, line);
	}

	private static bool IsTerminal(RemoteConsoleJobState state) =>
		state is RemoteConsoleJobState.Completed
			or RemoteConsoleJobState.Failed
			or RemoteConsoleJobState.Rejected
			or RemoteConsoleJobState.Cancelled
			or RemoteConsoleJobState.Interrupted;
}

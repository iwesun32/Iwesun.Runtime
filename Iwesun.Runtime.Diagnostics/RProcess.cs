using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using Iwesun.Runtime.Data;

namespace Iwesun.Runtime.Diagnostics;

public class RProcess : Process
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly RuntimeManagedUnitBase _unit;
    private readonly RuntimeExecutionManager? _execution;
    private readonly RuntimeManagedRegistry? _managed;
    private readonly RuntimeDiagnosticHub? _hub;
    private readonly DelegateRuntimeManagedCommandHandler _commandHandler;
	private readonly IDisposable? _commandRegistration;
    private readonly CancellationTokenSource _pipeGuardianCts = new();
    private int _registered;
    private int _globalStopHandled;
	private int _disposed;
    private Task? _pipeGuardianTask;
	private Task? _instructionBootstrapTask;
    private string? _branchPipeName;

    public RProcess(string? unitId = null)
    {
        var resolvedUnitId = string.IsNullOrWhiteSpace(unitId) ? $"process.{Guid.NewGuid():N}" : unitId;
        _unit = new RuntimeManagedUnitBase(resolvedUnitId, RuntimeInstructionEntityKind.Process);
        UnitId = _unit.UnitId;
        State = _unit.State;
        _execution = _unit.Execution;
        _managed = _unit.Managed;
        _hub = RuntimeInjectionContext.Hub;
        _commandHandler = new DelegateRuntimeManagedCommandHandler(UnitId, _managed)
        {
            OnStopAction = command => TryHandleGlobalStop(command.Payload),
            OnWakeupAction = command => _managed?.PublishEvent(UnitId, "process-guardian-wakeup", "Process guardian received wakeup command.", new { command.Sequence, command.Payload }),
            OnSnapshotAction = _ => _managed?.PublishEvent(UnitId, "process-guardian-snapshot", "Process guardian snapshot requested.", State.Snapshot())
        };
		_commandRegistration = _managed?.RegisterCommandHandler(UnitId, command => _commandHandler.Handle(command));
        EnableRaisingEvents = true;
        Exited += OnExited;
    }

    public string UnitId { get; }
    public string RuntimeProcessId => UnitId;
    public IRManagedState State { get; }

    public void SetDetail(string key, string value) => _unit.SetDetail(key, value);
    public bool TryGetDetail(string key, out string? value) => _unit.TryGetDetail(key, out value);
    public RuntimeState TransitionTo(string stateName) => _unit.TransitionTo(stateName);
    public bool TryTransitionTo(string stateName) => _unit.TryTransitionTo(stateName);

    public new bool Start()
    {
        EnsureRegistered();
		if (!StartInfo.UseShellExecute && !string.IsNullOrWhiteSpace(_branchPipeName))
			StartInfo.Environment["IWESUN_RUNTIME_DIAGNOSTICS_PIPE"] = _branchPipeName;
        var started = base.Start();
        if (started)
        {
            State.TransitionTo("Working");
            _execution?.SetTaskState(UnitId, RuntimeTaskState.Running, step: "running", payload: State.Snapshot());
            _managed?.PublishEvent(UnitId, "process-running", "Process started.", State.Snapshot());
            StartPipeGuardian();
			_instructionBootstrapTask = Task.Run(() => BootstrapInstructionHandlesAsync(_pipeGuardianCts.Token));
        }
        else
        {
            State.SetDetail("error", "start-failed");
            State.TransitionTo("Stop");
            _execution?.SetTaskState(UnitId, RuntimeTaskState.Faulted, step: "start-failed", error: "Process start failed.", payload: State.Snapshot());
            _managed?.PublishEvent(UnitId, "process-start-failed", "Process start failed.", State.Snapshot());
            _managed?.Unregister(UnitId);
        }

        return started;
    }

    public new void Kill()
    {
        base.Kill();
        StopPipeGuardian();
        RuntimePipeRegistry.ReleasePipe(UnitId);
        UnregisterReflectionTarget();
        State.SetDetail("error", "killed");
        State.TransitionTo("Stop");
        _execution?.SetTaskState(UnitId, RuntimeTaskState.Cancelled, step: "killed", error: "Killed by caller.", payload: State.Snapshot());
        _managed?.PublishEvent(UnitId, "process-killed", "Process killed by caller.", State.Snapshot());
    }

    public new void Kill(bool entireProcessTree)
    {
        base.Kill(entireProcessTree);
        StopPipeGuardian();
        RuntimePipeRegistry.ReleasePipe(UnitId);
        UnregisterReflectionTarget();
        State.SetDetail("error", entireProcessTree ? "killed-tree" : "killed");
        State.TransitionTo("Stop");
        _execution?.SetTaskState(UnitId, RuntimeTaskState.Cancelled, step: "killed", error: entireProcessTree ? "Killed process tree." : "Killed by caller.", payload: State.Snapshot());
        _managed?.PublishEvent(UnitId, "process-killed", entireProcessTree ? "Process tree killed by caller." : "Process killed by caller.", State.Snapshot());
    }

    public async Task<string> ExchangeWithMonitorAsync(
        string pipeName,
        string payload,
        int connectTimeoutMilliseconds = 2000,
        int ioTimeoutMilliseconds = 5000,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        payload ??= string.Empty;
        connectTimeoutMilliseconds = Math.Max(100, connectTimeoutMilliseconds);
        ioTimeoutMilliseconds = Math.Max(100, ioTimeoutMilliseconds);

        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectCts.CancelAfter(connectTimeoutMilliseconds);

        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(connectCts.Token).ConfigureAwait(false);

            using var ioCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            ioCts.CancelAfter(ioTimeoutMilliseconds);

            var requestBytes = Encoding.UTF8.GetBytes(payload);
            var requestLength = BitConverter.GetBytes(requestBytes.Length);
            await client.WriteAsync(requestLength, ioCts.Token).ConfigureAwait(false);
            await client.WriteAsync(requestBytes, ioCts.Token).ConfigureAwait(false);
            await client.FlushAsync(ioCts.Token).ConfigureAwait(false);

            var responseLengthBuffer = new byte[4];
            if (!await ReadExactAsync(client, responseLengthBuffer, ioCts.Token).ConfigureAwait(false))
                throw new IOException("Monitor pipe closed before response length was read.");

            var responseLength = BitConverter.ToInt32(responseLengthBuffer, 0);
            if (responseLength < 0 || responseLength > 1024 * 1024)
                throw new InvalidOperationException($"Invalid monitor response length: {responseLength}");

            var responseBuffer = new byte[responseLength];
            if (!await ReadExactAsync(client, responseBuffer, ioCts.Token).ConfigureAwait(false))
                throw new IOException("Monitor pipe closed before full response was read.");

            var response = Encoding.UTF8.GetString(responseBuffer);
            State.SetDetail("monitorPipe", pipeName);
            _managed?.PublishEvent(UnitId, "process-monitor-exchange", "Process monitor exchange completed.", new { pipeName, requestBytes = requestBytes.Length, responseBytes = responseBuffer.Length });
            return response;
        }
        catch (OperationCanceledException ex)
        {
            State.SetDetail("error", "monitor-timeout");
            _managed?.PublishEvent(UnitId, "process-monitor-timeout", ex.Message, new { pipeName, connectTimeoutMilliseconds, ioTimeoutMilliseconds });
            throw;
        }
        catch (Exception ex)
        {
            State.SetDetail("error", ex.Message);
            _managed?.PublishEvent(UnitId, "process-monitor-faulted", ex.Message, new { pipeName });
            throw;
        }
    }

    public static new RProcess Start(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        var process = new RProcess
        {
            StartInfo = startInfo
        };
        process.Start();
        return process;
    }

    public static new RProcess Start(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        return Start(new ProcessStartInfo(fileName));
    }

    public static new RProcess Start(string fileName, string arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        return Start(new ProcessStartInfo(fileName, arguments ?? string.Empty));
    }

    private void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1)
        {
            return;
        }

        State.TransitionTo("Start");
        _execution?.RegisterTask(
            UnitId,
            $"Process:{StartInfo.FileName}",
            RuntimeExecutionLifetime.Dynamic,
            category: "process",
            threadId: "",
            sourceLocation: StartInfo.FileName,
            parentTaskId: "",
            step: "registered",
            payload: State.Snapshot());
        _managed?.Register(UnitId, "process", "InternalManaged", State.Snapshot());
        RegisterReflectionTarget();
        _branchPipeName = RuntimePipeRegistry.AcquirePipe(UnitId, DiagnosticSwitchboard.Snapshot().RuntimeDiagnosticsPipeName);
        State.SetDetail("branchPipe", _branchPipeName);
        _managed?.PublishEvent(UnitId, "process-branch-pipe-acquired", "Process branch pipe acquired.", new { unitId = UnitId, branchPipe = _branchPipeName });
    }

    private void OnExited(object? sender, EventArgs e)
    {
        StopPipeGuardian();
        RuntimePipeRegistry.ReleasePipe(UnitId);
        UnregisterReflectionTarget();
        State.SetDetail("exitCode", ExitCode.ToString());
        State.TransitionTo("Stop");
        var state = ExitCode == 0 ? RuntimeTaskState.Completed : RuntimeTaskState.Faulted;
        _execution?.SetTaskState(UnitId, state, step: "exited", error: ExitCode == 0 ? null : $"ExitCode={ExitCode}", payload: State.Snapshot());
        _managed?.PublishEvent(UnitId, "process-exited", ExitCode == 0 ? "Process exited successfully." : $"Process exited with code {ExitCode}.", State.Snapshot());
        _managed?.Unregister(UnitId);
    }

    private void StartPipeGuardian()
    {
        if (_pipeGuardianTask is { IsCompleted: false })
        {
            return;
        }
        _pipeGuardianTask = Task.Run(() => PipeGuardianLoopAsync(_pipeGuardianCts.Token));
    }

    private void RegisterReflectionTarget()
    {
        if (_hub == null)
        {
            return;
        }

        var targetId = GetReflectionTargetId(UnitId);
        _hub.Unregister(targetId);
        _hub.RegisterObject(targetId, this, BuildReflectionAccess());
        _managed?.PublishEvent(UnitId, "process-reflection-registered", "Process reflection target registered.", new { targetId });
    }

    private void UnregisterReflectionTarget()
    {
        if (_hub == null)
        {
            return;
        }

        var targetId = GetReflectionTargetId(UnitId);
        if (_hub.Unregister(targetId))
        {
            _managed?.PublishEvent(UnitId, "process-reflection-unregistered", "Process reflection target unregistered.", new { targetId });
        }
    }

    private static RuntimeDiagnosticObjectAccess BuildReflectionAccess()
    {
        return new RuntimeDiagnosticObjectAccess
        {
            AllowReadAllPublic = true,
            ReadableMembers =
            [
                nameof(UnitId),
                nameof(RuntimeProcessId),
                nameof(State),
                nameof(StartInfo),
                nameof(EnableRaisingEvents),
                "_branchPipeName",
                "_registered"
            ]
        };
    }

    private static string GetReflectionTargetId(string unitId) => $"runtime.process.{unitId}";

    private void StopPipeGuardian()
    {
        try
        {
            _pipeGuardianCts.Cancel();
        }
        catch
        {
            // ignore cancellation races
        }
    }

    private async Task PipeGuardianLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (_managed?.IsGlobalStopOrExitRequested == true && Interlocked.Exchange(ref _globalStopHandled, 1) == 0)
            {
                TryHandleGlobalStop();
            }

            if (_managed?.TryDequeueCommand(UnitId, out var command) == true && command is not null)
            {
                _commandHandler.Handle(command);
            }

            if (HasProcessExitedSafely())
            {
                return;
            }

            try
            {
                if (await AnnounceToAggregateAsync(cancellationToken).ConfigureAwait(false))
                {
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _managed?.PublishEvent(UnitId, "process-guardian-announce-failed", ex.Message, new { unitId = UnitId, branchPipe = _branchPipeName });
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }
    }

    private void TryHandleGlobalStop(string? commandPayload = null)
    {
        try
        {
            var deadline = RuntimeManagedPayloadInterpreter.GetDateTimeOffset(commandPayload, "deadlineUtc");
            _managed?.PublishEvent(UnitId, "process-global-stop", "Global stop/exit state observed by process guardian.", new { unitId = UnitId, deadlineUtc = deadline });
            if (HasExited)
            {
                return;
            }

            if (CloseMainWindow())
            {
                if (!WaitForExit(1500))
                {
                    Kill(entireProcessTree: true);
                }

                return;
            }

            Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            _managed?.PublishEvent(UnitId, "process-global-stop-faulted", ex.Message, new { unitId = UnitId });
        }
    }

    private bool HasProcessExitedSafely()
    {
        try
        {
            return HasExited;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> AnnounceToAggregateAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_branchPipeName))
        {
            return false;
        }

        var aggregatePipe = RuntimePipeRegistry.ResolveAggregatePipe(UnitId)
            ?? DiagnosticSwitchboard.Snapshot().RuntimeDiagnosticsPipeName;
        if (string.IsNullOrWhiteSpace(aggregatePipe))
        {
            return false;
        }

        var requestFrame = new RuntimeDiagnosticFrame
        {
            Header = new RuntimeDiagnosticFrameHeader
            {
                Schema = RuntimeDiagnosticProtocol.V2Schema,
                FrameType = "request",
                Category = "instruction",
                Operation = "announce",
                RequestId = Guid.NewGuid().ToString("N")
            },
            Command = new RuntimeDiagnosticFrameCommand
            {
                Domain = "diagnostics",
                Target = "diagnostics.pipes",
                Action = "announce",
                Args = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                {
                    ["name"] = JsonSerializer.SerializeToElement(UnitId),
                    ["pipeName"] = JsonSerializer.SerializeToElement(_branchPipeName),
                    ["aggregatePipeName"] = JsonSerializer.SerializeToElement(aggregatePipe),
                    ["ownerProcessId"] = JsonSerializer.SerializeToElement(Environment.ProcessId)
                }
            }
        };

        var response = await SendFrameAsync(aggregatePipe, requestFrame, cancellationToken).ConfigureAwait(false);
        if (response.Status?.Ok == true)
        {
            State.SetDetail("aggregatePipe", aggregatePipe);
            _managed?.PublishEvent(UnitId, "process-guardian-announced", "Process guardian announced branch pipe to aggregate.", new
            {
                unitId = UnitId,
                branchPipe = _branchPipeName,
                aggregatePipe
            });
            return true;
        }

        return false;
    }

    private static async Task<RuntimeDiagnosticFrame> SendFrameAsync(string pipeName, RuntimeDiagnosticFrame request, CancellationToken cancellationToken)
    {
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectCts.CancelAfter(TimeSpan.FromSeconds(1));
        await client.ConnectAsync(connectCts.Token).ConfigureAwait(false);

        using var ioCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ioCts.CancelAfter(TimeSpan.FromSeconds(2));

        var requestJson = JsonSerializer.Serialize(request, JsonOptions);
        var requestBytes = Encoding.UTF8.GetBytes(requestJson);
        var requestLength = BitConverter.GetBytes(requestBytes.Length);
        await client.WriteAsync(requestLength, ioCts.Token).ConfigureAwait(false);
        await client.WriteAsync(requestBytes, ioCts.Token).ConfigureAwait(false);
        await client.FlushAsync(ioCts.Token).ConfigureAwait(false);

        var responseLengthBuffer = new byte[4];
        if (!await ReadExactAsync(client, responseLengthBuffer, ioCts.Token).ConfigureAwait(false))
            throw new IOException("Aggregate diagnostics pipe closed before response length.");
        var responseLength = BitConverter.ToInt32(responseLengthBuffer, 0);
        if (responseLength <= 0 || responseLength > 1024 * 1024)
            throw new InvalidOperationException($"Invalid aggregate response length: {responseLength}");

        var responseBuffer = new byte[responseLength];
        if (!await ReadExactAsync(client, responseBuffer, ioCts.Token).ConfigureAwait(false))
            throw new IOException("Aggregate diagnostics pipe closed before full response.");
        var responseJson = Encoding.UTF8.GetString(responseBuffer);
        return JsonSerializer.Deserialize<RuntimeDiagnosticFrame>(responseJson, JsonOptions)
            ?? throw new InvalidOperationException("Aggregate diagnostics response frame was null.");
    }

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return false;
            }

            totalRead += read;
        }

        return true;
    }

	private async Task BootstrapInstructionHandlesAsync(CancellationToken cancellationToken)
	{
		if (_managed == null || string.IsNullOrWhiteSpace(_branchPipeName))
			return;
		RuntimeInstructionHandleDescriptor descriptor;
		try
		{
			descriptor = _managed.DuplicateInstructionHandlesToProcess(UnitId, this);
		}
		catch (Exception ex)
		{
			_managed.PublishEvent(UnitId, "process-instruction-handles-failed", ex.Message, null);
			return;
		}

		var frame = new RuntimeDiagnosticFrame
		{
			Header = new RuntimeDiagnosticFrameHeader
			{
				Schema = RuntimeDiagnosticProtocol.V2Schema,
				FrameType = "request",
				Category = "instruction",
				Operation = "instruction.handles.attach",
				RequestId = Guid.NewGuid().ToString("N")
			},
			Command = new RuntimeDiagnosticFrameCommand
			{
				Domain = "diagnostics",
				Target = "runtime.managed",
				Action = "instruction.handles.attach",
				Args = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
				{
					["unitId"] = JsonSerializer.SerializeToElement(UnitId),
					["version"] = JsonSerializer.SerializeToElement(descriptor.Version),
					["controllerMappingHandle"] = JsonSerializer.SerializeToElement(descriptor.ControllerMappingHandle),
					["controllerEventHandle"] = JsonSerializer.SerializeToElement(descriptor.ControllerEventHandle),
					["controllerCapacity"] = JsonSerializer.SerializeToElement(descriptor.ControllerCapacity),
					["unitMappingHandle"] = JsonSerializer.SerializeToElement(descriptor.UnitMappingHandle),
					["unitEventHandle"] = JsonSerializer.SerializeToElement(descriptor.UnitEventHandle),
					["unitCapacity"] = JsonSerializer.SerializeToElement(descriptor.UnitCapacity)
				}
			}
		};

		for (var attempt = 0; attempt < 40 && !cancellationToken.IsCancellationRequested; attempt++)
		{
			try
			{
				var response = await SendFrameAsync(_branchPipeName, frame, cancellationToken).ConfigureAwait(false);
				if (response.Status?.Ok == true)
				{
					_managed.PublishEvent(UnitId, "process-instruction-handles-attached", "Child process attached anonymous instruction handles.", null);
					return;
				}
			}
			catch (Exception) when (attempt < 39)
			{
			}
			await Task.Delay(100, cancellationToken).ConfigureAwait(false);
		}
	}

	protected override void Dispose(bool disposing)
	{
		if (Interlocked.Exchange(ref _disposed, 1) == 0)
		{
			StopPipeGuardian();
			RuntimePipeRegistry.ReleasePipe(UnitId);
			UnregisterReflectionTarget();
			_managed?.Unregister(UnitId);
			if (disposing)
			{
				_commandRegistration?.Dispose();
				_pipeGuardianCts.Dispose();
			}
		}

		base.Dispose(disposing);
	}
}

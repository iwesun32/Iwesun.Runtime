using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace Iwesun.Runtime.Diagnostics;

public class RProcess : Process
{
    private readonly RuntimeExecutionManager? _execution;
    private readonly RuntimeManagedRegistry? _managed;
    private int _registered;

    public RProcess(string? unitId = null)
    {
        UnitId = string.IsNullOrWhiteSpace(unitId) ? $"process.{Guid.NewGuid():N}" : unitId;
        State = new RManagedState(UnitId);
        _execution = RuntimeInjectionContext.Execution;
        _managed = RuntimeInjectionContext.Managed;
        EnableRaisingEvents = true;
        Exited += OnExited;
    }

    public string UnitId { get; }
    public string RuntimeProcessId => UnitId;
    public IRManagedState State { get; }

    public void SetDetail(string key, string value) => State.SetDetail(key, value);
    public bool TryGetDetail(string key, out string? value) => State.TryGetDetail(key, out value);
    public RuntimeState TransitionTo(string stateName) => State.TransitionTo(stateName);
    public bool TryTransitionTo(string stateName) => State.TryTransitionTo(stateName);

    public new bool Start()
    {
        EnsureRegistered();
        var started = base.Start();
        if (started)
        {
            State.TransitionTo("Working");
            _execution?.SetTaskState(UnitId, RuntimeTaskState.Running, step: "running", payload: State.Snapshot());
            _managed?.PublishEvent(UnitId, "process-running", "Process started.", State.Snapshot());
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
        State.SetDetail("error", "killed");
        State.TransitionTo("Stop");
        _execution?.SetTaskState(UnitId, RuntimeTaskState.Cancelled, step: "killed", error: "Killed by caller.", payload: State.Snapshot());
        _managed?.PublishEvent(UnitId, "process-killed", "Process killed by caller.", State.Snapshot());
    }

    public new void Kill(bool entireProcessTree)
    {
        base.Kill(entireProcessTree);
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
    }

    private void OnExited(object? sender, EventArgs e)
    {
        State.SetDetail("exitCode", ExitCode.ToString());
        State.TransitionTo("Stop");
        var state = ExitCode == 0 ? RuntimeTaskState.Completed : RuntimeTaskState.Faulted;
        _execution?.SetTaskState(UnitId, state, step: "exited", error: ExitCode == 0 ? null : $"ExitCode={ExitCode}", payload: State.Snapshot());
        _managed?.PublishEvent(UnitId, "process-exited", ExitCode == 0 ? "Process exited successfully." : $"Process exited with code {ExitCode}.", State.Snapshot());
        _managed?.Unregister(UnitId);
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
}

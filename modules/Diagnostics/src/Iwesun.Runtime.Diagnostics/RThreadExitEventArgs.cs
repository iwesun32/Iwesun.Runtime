namespace Iwesun.Runtime.Diagnostics;

public sealed class RThreadExitRequestedEventArgs : EventArgs
{
    public RThreadExitRequestedEventArgs(long commandSequence, string? payload)
    {
        CommandSequence = commandSequence;
        Payload = payload;
    }

    public long CommandSequence { get; }
    public string? Payload { get; }
}

public sealed class RThreadExitResultEventArgs : EventArgs
{
    public RThreadExitResultEventArgs(int exitCode, bool timedOut, string? message = null)
    {
        ExitCode = exitCode;
        TimedOut = timedOut;
        Message = message;
    }

    public int ExitCode { get; }
    public bool TimedOut { get; }
    public string? Message { get; }
}

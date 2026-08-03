namespace Iwesun.Runtime.Diagnostics;

public sealed class RuntimeDiagnosticsSelfTestState
{
	public int Counter { get; set; }
	public string Label { get; set; } = "ready";
	public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

	public int Increment()
	{
		Counter++;
		UpdatedAt = DateTimeOffset.UtcNow;
		return Counter;
	}

	public void Reset()
	{
		Counter = 0;
		Label = "ready";
		UpdatedAt = DateTimeOffset.UtcNow;
	}
}

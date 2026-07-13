namespace Iwesun.Runtime.Diagnostics;

public sealed class RuntimeWindowsServiceOptions
{
	public string ServiceName { get; set; } = "";
	public string DisplayName { get; set; } = "";
	public string Description { get; set; } = "";
	public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

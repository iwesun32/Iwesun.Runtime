namespace Iwesun.Runtime.Diagnostics;

/// <summary>
/// Fast-path runtime output gate. Source code should read only this single mutable
/// boolean before building diagnostic payloads.
/// </summary>
public static class RuntimeOutputSwitch
{
	public static volatile bool Enabled;
}

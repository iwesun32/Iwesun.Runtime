namespace Iwesun.Runtime.Diagnostics;

public sealed class RuntimeDiagnosticOptions
{
	public IReadOnlyList<RuntimeDiagnosticHookRule> Hooks { get; init; } = Array.Empty<RuntimeDiagnosticHookRule>();
	public IReadOnlyList<RuntimeDiagnosticObjectRule> Objects { get; init; } = Array.Empty<RuntimeDiagnosticObjectRule>();
}

public sealed class RuntimeDiagnosticHookRule
{
	public string Id { get; init; } = "";
	public string Source { get; init; } = "";
	public string Provider { get; init; } = "";
	public bool Enabled { get; init; } = true;
	public IReadOnlyList<string> Include { get; init; } = Array.Empty<string>();
	public IReadOnlyList<string> Exclude { get; init; } = Array.Empty<string>();
	public int MaxPayloadChars { get; init; } = 4096;
}

public sealed class RuntimeDiagnosticObjectRule
{
	public string TargetId { get; init; } = "";
	public string Description { get; init; } = "";
	public RuntimeDiagnosticObjectAccess Access { get; init; } = new();
}

public sealed class RuntimeDiagnosticObjectAccess
{
	public IReadOnlyList<string> ReadableMembers { get; init; } = Array.Empty<string>();
	public IReadOnlyList<string> WritableMembers { get; init; } = Array.Empty<string>();
	public IReadOnlyList<string> InvokableMembers { get; init; } = Array.Empty<string>();
	public bool AllowReadAllPublic { get; init; } = true;
}

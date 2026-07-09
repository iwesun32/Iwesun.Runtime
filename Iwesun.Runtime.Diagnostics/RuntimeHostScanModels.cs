namespace Iwesun.Runtime.Diagnostics;

public sealed class RuntimeHostScanOptions
{
	public IReadOnlyList<string> IncludeAssemblyPrefixes { get; init; } = Array.Empty<string>();
	public IReadOnlyList<string> ExcludeAssemblyPrefixes { get; init; } = new[]
	{
		"System.",
		"Microsoft.",
		"WindowsBase",
		"Presentation",
		"mscorlib",
		"netstandard"
	};

	public int MaxTypesPerAssembly { get; init; } = 2048;
	public bool IncludeNonPublicTypes { get; init; }
}

public sealed class RuntimeHostSnapshot
{
	public DateTimeOffset ScannedAt { get; init; } = DateTimeOffset.UtcNow;
	public string ProcessName { get; init; } = "";
	public int ProcessId { get; init; }
	public string RuntimeVersion { get; init; } = "";
	public string BaseDirectory { get; init; } = "";
	public IReadOnlyList<RuntimeAssemblySnapshot> Assemblies { get; init; } = Array.Empty<RuntimeAssemblySnapshot>();
	public IReadOnlyList<string> RegisteredTargets { get; init; } = Array.Empty<string>();
}

public sealed class RuntimeAssemblySnapshot
{
	public string Name { get; init; } = "";
	public string FullName { get; init; } = "";
	public string? Location { get; init; }
	public int TypeCount { get; init; }
	public IReadOnlyList<RuntimeTypeSnapshot> Types { get; init; } = Array.Empty<RuntimeTypeSnapshot>();
	public string? Error { get; init; }
}

public sealed class RuntimeTypeSnapshot
{
	public string Name { get; init; } = "";
	public string FullName { get; init; } = "";
	public string Namespace { get; init; } = "";
	public string Kind { get; init; } = "";
	public bool IsPublic { get; init; }
	public bool IsAbstract { get; init; }
	public bool IsGeneric { get; init; }
	public int PublicPropertyCount { get; init; }
	public int PublicFieldCount { get; init; }
	public int PublicMethodCount { get; init; }
	public int PublicEventCount { get; init; }
	public IReadOnlyList<string> Interfaces { get; init; } = Array.Empty<string>();
}

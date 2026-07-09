using System.Reflection;

namespace Iwesun.Runtime.Diagnostics;

/// <summary>
/// Host-injectable pipe name prefix. The diagnostics DLL is pre-compiled; the
/// host assembly specifies the pipe name prefix at compile time via an assembly
/// attribute.  Pipe names become "{prefix}.Control", "{prefix}.Data", etc.
/// </summary>
public static class DiagnosticPipePrefix
{
	private static string _prefix = "Iwesun.Runtime";

	/// <summary>
	/// Current pipe name prefix (e.g. "DdnsSnap").
	/// Default is "Iwesun.Runtime".
	/// </summary>
	public static string Prefix => _prefix;

	/// <summary>
	/// Read the prefix from the host assembly's
	/// <see cref="DiagnosticPipePrefixAttribute"/>.
	/// </summary>
	public static void InitializeFromAssembly(Assembly hostAssembly)
	{
		var attr = hostAssembly.GetCustomAttribute<DiagnosticPipePrefixAttribute>();
		if (attr != null)
			_prefix = attr.Prefix;
	}

	/// <summary>
	/// Explicitly set the prefix (alternative to the assembly attribute).
	/// </summary>
	public static void SetPrefix(string prefix)
	{
		_prefix = prefix ?? throw new ArgumentNullException(nameof(prefix));
	}

	/// <summary>
	/// Resolve a full pipe name: "{prefix}.{channel}"
	/// </summary>
	public static string Resolve(string channel)
		=> $"{_prefix}.{channel}";

	/// <summary>
	/// Resolve the control channel pipe name.
	/// </summary>
	public static string ControlPipe => Resolve("Control");

	/// <summary>
	/// Resolve the data channel pipe name.
	/// </summary>
	public static string DataPipe => Resolve("Data");

	/// <summary>
	/// Resolve the events channel pipe name.
	/// </summary>
	public static string EventsPipe => Resolve("Events");
}

// Note: DiagnosticPipePrefixAttribute is defined in DiagnosticAssemblyAttributes.cs
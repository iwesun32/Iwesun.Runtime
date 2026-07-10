using System.Reflection;

namespace Iwesun.Runtime.Diagnostics;

// ── Assembly-level diagnostic attributes ─────────────────────
//   Host assemblies place these in AssemblyInfo.cs to declare
//   watch points, breakpoints, hookable events, and pipe prefix.
//   The RegistryBuilder scans these at runtime.

/// <summary>Declare a diagnostic watch point (data output probe).</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class DiagnosticWatchPointAttribute : Attribute
{
	public string Id { get; }
	public string Section { get; }
	public string Kind { get; }
	public string Description { get; }
	public string SourceLocation { get; }

	public DiagnosticWatchPointAttribute(string id, string section, string kind, string description, string sourceLocation)
	{
		Id = id;
		Section = section;
		Kind = kind;
		Description = description;
		SourceLocation = sourceLocation;
	}
}

/// <summary>Declare a logical breakpoint.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class DiagnosticBreakpointAttribute : Attribute
{
	public string Id { get; }
	public string Section { get; }
	public string Description { get; }
	public string SourceLocation { get; }
	public int TimeoutMs { get; init; } = 30_000;
	public int HitCountTarget { get; init; }

	public DiagnosticBreakpointAttribute(string id, string section, string description, string sourceLocation)
	{
		Id = id;
		Section = section;
		Description = description;
		SourceLocation = sourceLocation;
	}
}

/// <summary>
/// Declare a default numeric threshold binding for a logical breakpoint.
/// This is loaded from assembly metadata at startup by RegistryBuilder.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class DiagnosticNumericBreakpointAttribute : Attribute
{
	public string BreakpointId { get; }
	public string Operator { get; }
	public double Threshold1 { get; }
	public double Threshold2 { get; }

	public DiagnosticNumericBreakpointAttribute(string breakpointId, string @operator, double threshold1 = 0, double threshold2 = 0)
	{
		BreakpointId = breakpointId;
		Operator = @operator;
		Threshold1 = threshold1;
		Threshold2 = threshold2;
	}
}

/// <summary>Declare an event that can be hooked at runtime.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class DiagnosticHookableEventAttribute : Attribute
{
	public string Id { get; }
	public Type TargetType { get; }
	public string EventName { get; }

	public DiagnosticHookableEventAttribute(string id, Type targetType, string eventName)
	{
		Id = id;
		TargetType = targetType;
		EventName = eventName;
	}
}

/// <summary>Specify the pipe name prefix for this host process.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class DiagnosticPipePrefixAttribute : Attribute
{
	public string Prefix { get; }
	public DiagnosticPipePrefixAttribute(string prefix) => Prefix = prefix;
}
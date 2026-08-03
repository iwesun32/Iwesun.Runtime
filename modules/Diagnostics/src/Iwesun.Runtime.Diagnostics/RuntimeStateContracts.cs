using System.Globalization;

namespace Iwesun.Runtime.Diagnostics;

public enum RuntimeStateGroup
{
	Lifecycle,
	WorkPhase,
	ShutdownPhase,
	Custom
}

public interface IRuntimeStateExactMatch<in TState>
{
	bool ExactEquals(TState other);
}

public interface IRuntimeStateHierarchy<in TState>
{
	bool Is(TState other);
}

public interface IRuntimeStateExtension<TState>
{
	TState AddRoot(int code, string name, RuntimeStateGroup group = RuntimeStateGroup.Custom, string? displayName = null);
	TState Add(int code, TState parent, string name, RuntimeStateGroup group = RuntimeStateGroup.Custom, string? displayName = null);
}

public interface IRuntimeStateTextConverter<TState>
{
	string ToDisplayString(TState state, CultureInfo? culture = null);
	bool TryGetDisplayName(TState state, CultureInfo culture, out string displayName);
	bool TryParse(string text, out TState state, CultureInfo? culture = null);
}

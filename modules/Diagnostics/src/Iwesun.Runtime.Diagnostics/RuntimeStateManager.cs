using System.Globalization;

namespace Iwesun.Runtime.Diagnostics;

public sealed class RuntimeStateManager : IRuntimeStateExtension<RuntimeState>
,
	IRuntimeStateTextConverter<RuntimeState>
{
	private readonly object _gate = new();
	private RuntimeState _currentState;

	public RuntimeStateManager(RuntimeStateCatalog? catalog = null)
	{
		Catalog = catalog ?? RuntimeStateCatalog.CreateOnlineDefaults();
		_currentState = Catalog.RequireByName("Start");
		UpdatedAt = DateTimeOffset.UtcNow;
	}

	public RuntimeStateCatalog Catalog { get; }
	public DateTimeOffset UpdatedAt { get; private set; }

	public RuntimeState CurrentState
	{
		get
		{
			lock (_gate)
			{
				return _currentState;
			}
		}
	}

	public string CurrentPath => BuildPath(CurrentState);
	public bool IsStart => CurrentState.Is(Catalog.RequireByName("Start"));
	public bool IsWorking => CurrentState.Is(Catalog.RequireByName("Working"));
	public bool IsStopping => CurrentState.Is(Catalog.RequireByName("Stop"));

	public RuntimeStateSnapshot Snapshot()
	{
		lock (_gate)
		{
			return new RuntimeStateSnapshot(_currentState, BuildPath(_currentState), UpdatedAt, Catalog.Snapshot());
		}
	}

	public RuntimeState SetCurrent(RuntimeState state) => SetCurrentByCode(state.Code);

	public RuntimeState SetCurrentByCode(int code)
	{
		var state = Catalog.RequireByCode(code);
		lock (_gate)
		{
			_currentState = state;
			UpdatedAt = DateTimeOffset.UtcNow;
			return _currentState;
		}
	}

	public bool TryTransitionToByCode(int code)
	{
		var target = Catalog.RequireByCode(code);
		lock (_gate)
		{
			if (!RuntimeStateTransitionEngine.CanTransition(_currentState, target, Catalog))
			{
				return false;
			}

			_currentState = target;
			UpdatedAt = DateTimeOffset.UtcNow;
			return true;
		}
	}

	public RuntimeState SetCurrentByName(string name)
	{
		var state = Catalog.RequireByName(name);
		lock (_gate)
		{
			_currentState = state;
			UpdatedAt = DateTimeOffset.UtcNow;
			return _currentState;
		}
	}

	public bool TryTransitionToByName(string name)
	{
		var target = Catalog.RequireByName(name);
		lock (_gate)
		{
			if (!RuntimeStateTransitionEngine.CanTransition(_currentState, target, Catalog))
			{
				return false;
			}

			_currentState = target;
			UpdatedAt = DateTimeOffset.UtcNow;
			return true;
		}
	}

	public RuntimeState TransitionToByCode(int code)
	{
		var target = Catalog.RequireByCode(code);
		lock (_gate)
		{
			if (!RuntimeStateTransitionEngine.CanTransition(_currentState, target, Catalog))
			{
				throw new InvalidOperationException($"Invalid state transition: {_currentState.Name} -> {target.Name}.");
			}

			_currentState = target;
			UpdatedAt = DateTimeOffset.UtcNow;
			return _currentState;
		}
	}

	public RuntimeState TransitionToByName(string name)
	{
		var target = Catalog.RequireByName(name);
		lock (_gate)
		{
			if (!RuntimeStateTransitionEngine.CanTransition(_currentState, target, Catalog))
			{
				throw new InvalidOperationException($"Invalid state transition: {_currentState.Name} -> {target.Name}.");
			}

			_currentState = target;
			UpdatedAt = DateTimeOffset.UtcNow;
			return _currentState;
		}
	}

	public RuntimeState SetStart() => SetCurrentByName("Start");
	public RuntimeState SetWorking() => SetCurrentByName("Working");
	public RuntimeState SetStop() => SetCurrentByName("Stop");

	public RuntimeState SetCurrentByKey(RuntimeStateKey key)
	{
		var state = Catalog.RequireByKey(key);
		lock (_gate)
		{
			_currentState = state;
			UpdatedAt = DateTimeOffset.UtcNow;
			return _currentState;
		}
	}

	public string ToDisplayString(RuntimeState state, CultureInfo? culture = null)
		=> Catalog.ToDisplayString(state, culture);

	public bool TryGetDisplayName(RuntimeState state, CultureInfo culture, out string displayName)
		=> Catalog.TryGetDisplayName(state, culture, out displayName);

	public bool TryParse(string text, out RuntimeState state, CultureInfo? culture = null)
		=> Catalog.TryParse(text, out state, culture);

	public RuntimeState AddRoot(int code, string name, RuntimeStateGroup group = RuntimeStateGroup.Custom, string? displayName = null)
		=> Catalog.AddRoot(code, name, group, displayName);

	public RuntimeState AddRoot(int code, string name, RuntimeStateGroup group, string? displayName, RuntimeStateKey? key)
		=> Catalog.AddRoot(code, name, group, displayName, key);

	public RuntimeState Add(int code, RuntimeState parent, string name, RuntimeStateGroup group = RuntimeStateGroup.Custom, string? displayName = null)
		=> Catalog.Add(code, parent, name, group, displayName);

	public RuntimeState Add(int code, RuntimeState parent, string name, RuntimeStateGroup group, string? displayName, RuntimeStateKey? key)
		=> Catalog.Add(code, parent, name, group, displayName, key);

	private string BuildPath(RuntimeState state)
	{
		var segments = new Stack<string>();
		segments.Push(state.Name);

		var currentCode = state.ParentCode;
		while (currentCode.HasValue && Catalog.TryGetByCode(currentCode.Value, out var current))
		{
			segments.Push(current.Name);
			currentCode = current.ParentCode;
		}

		return string.Join('.', segments);
	}
}

public sealed record RuntimeStateSnapshot(
	RuntimeState CurrentState,
	string CurrentPath,
	DateTimeOffset UpdatedAt,
	IReadOnlyList<RuntimeState> States);

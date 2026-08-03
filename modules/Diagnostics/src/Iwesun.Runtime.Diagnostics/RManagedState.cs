using System.Collections.Concurrent;
using Iwesun.Runtime.Data;

namespace Iwesun.Runtime.Diagnostics;

public interface IRManagedState
{
    string UnitId { get; }
    RuntimeStateManager StateManager { get; }
    RuntimeState CurrentState { get; }
    IReadOnlyDictionary<string, string> Details { get; }
    IReadOnlyList<RuntimeState> SubTaskStates { get; }
    bool TryTransitionTo(string stateName);
    RuntimeState TransitionTo(string stateName);
    bool TryAppendSubTaskState(string stateName);
    RuntimeState AppendSubTaskState(string stateName);
    void ClearSubTaskStates();
    void SetDetail(string key, string value);
    bool TryGetDetail(string key, out string? value);
    RManagedStateSnapshot Snapshot();
    void Sync();
}

public sealed class RManagedState : IRManagedState
{
    private readonly ConcurrentDictionary<string, string> _details = new(StringComparer.OrdinalIgnoreCase);
    private readonly RuntimeStateHistory _subTaskStates = new();
    private readonly RuntimeManagedRegistry? _managed;
	private readonly RuntimeInstructionEntityKind _entityKind;

    public RManagedState(
		string unitId,
		RuntimeStateCatalog? catalog = null,
		RuntimeInstructionEntityKind entityKind = RuntimeInstructionEntityKind.Business)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(unitId);
        UnitId = unitId;
        StateManager = new RuntimeStateManager(catalog);
        _managed = RuntimeInjectionContext.Managed;
		_entityKind = entityKind;
    }

    public string UnitId { get; }
    public RuntimeStateManager StateManager { get; }
    public RuntimeState CurrentState => StateManager.CurrentState;
    public IReadOnlyDictionary<string, string> Details => _details;
    public IReadOnlyList<RuntimeState> SubTaskStates
    {
        get
        {
            return _subTaskStates.Snapshot();
        }
    }

    public bool TryTransitionTo(string stateName)
    {
        var ok = StateManager.TryTransitionToByName(stateName);
        if (ok)
        {
            Sync();
			_managed?.TryPublishStateCode(UnitId, _entityKind, CurrentState.Code);
        }

        return ok;
    }

    public RuntimeState TransitionTo(string stateName)
    {
        var state = StateManager.TransitionToByName(stateName);
        Sync();
		_managed?.TryPublishStateCode(UnitId, _entityKind, state.Code);
        return state;
    }

    public bool TryAppendSubTaskState(string stateName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateName);
        if (!StateManager.Catalog.TryGetByName(stateName, out var state))
        {
            return false;
        }

        _subTaskStates.Append(state);

        Sync();
        return true;
    }

    public RuntimeState AppendSubTaskState(string stateName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateName);
        var state = StateManager.Catalog.RequireByName(stateName);
        _subTaskStates.Append(state);

        Sync();
        return state;
    }

    public void ClearSubTaskStates()
    {
        _subTaskStates.Clear();

        Sync();
    }

    public void SetDetail(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        value ??= string.Empty;
        _details[key] = value;
        Sync();
    }

    public bool TryGetDetail(string key, out string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _details.TryGetValue(key, out value);
    }

    public RManagedStateSnapshot Snapshot()
    {
        var subTaskStates = _subTaskStates.Snapshot();

        return new RManagedStateSnapshot(UnitId, StateManager.Snapshot(), _details.ToDictionary(), subTaskStates);
    }

    public void Sync()
    {
        _managed?.UpdateState(UnitId, Snapshot());
    }

}

public sealed record RManagedStateSnapshot(
    string UnitId,
    RuntimeStateSnapshot State,
    IReadOnlyDictionary<string, string> Details,
    IReadOnlyList<RuntimeState>? SubTaskStates = null);

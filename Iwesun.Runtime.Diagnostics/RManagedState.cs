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
    private readonly RuntimeDList<RuntimeState> _subTaskStates = new();
    private readonly object _subTaskGate = new();
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
            lock (_subTaskGate)
            {
                return _subTaskStates.ToArraySnapshot();
            }
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

        lock (_subTaskGate)
        {
            _subTaskStates.AddLast(state);
            TrimSubTaskStateHistory();
        }

        Sync();
        return true;
    }

    public RuntimeState AppendSubTaskState(string stateName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateName);
        var state = StateManager.Catalog.RequireByName(stateName);
        lock (_subTaskGate)
        {
            _subTaskStates.AddLast(state);
            TrimSubTaskStateHistory();
        }

        Sync();
        return state;
    }

    public void ClearSubTaskStates()
    {
        lock (_subTaskGate)
        {
            _subTaskStates.Clear();
        }

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
        IReadOnlyList<RuntimeState> subTaskStates;
        lock (_subTaskGate)
        {
            subTaskStates = _subTaskStates.ToArraySnapshot();
        }

        return new RManagedStateSnapshot(UnitId, StateManager.Snapshot(), _details.ToDictionary(), subTaskStates);
    }

    public void Sync()
    {
        _managed?.UpdateState(UnitId, Snapshot());
    }

    private void TrimSubTaskStateHistory()
    {
        while (_subTaskStates.Count > 128)
        {
            var snapshot = _subTaskStates.ToArraySnapshot();
            if (snapshot.Count == 0)
            {
                break;
            }

            _subTaskStates.RemoveFirst(snapshot[0]);
        }
    }
}

public sealed record RManagedStateSnapshot(
    string UnitId,
    RuntimeStateSnapshot State,
    IReadOnlyDictionary<string, string> Details,
    IReadOnlyList<RuntimeState>? SubTaskStates = null);

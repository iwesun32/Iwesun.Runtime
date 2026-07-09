using System.Collections.Concurrent;

namespace Iwesun.Runtime.Diagnostics;

public interface IRManagedState
{
    string UnitId { get; }
    RuntimeStateManager StateManager { get; }
    RuntimeState CurrentState { get; }
    IReadOnlyDictionary<string, string> Details { get; }
    bool TryTransitionTo(string stateName);
    RuntimeState TransitionTo(string stateName);
    void SetDetail(string key, string value);
    bool TryGetDetail(string key, out string? value);
    RManagedStateSnapshot Snapshot();
    void Sync();
}

public sealed class RManagedState : IRManagedState
{
    private readonly ConcurrentDictionary<string, string> _details = new(StringComparer.OrdinalIgnoreCase);
    private readonly RuntimeManagedRegistry? _managed;

    public RManagedState(string unitId, RuntimeStateCatalog? catalog = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(unitId);
        UnitId = unitId;
        StateManager = new RuntimeStateManager(catalog);
        _managed = RuntimeInjectionContext.Managed;
    }

    public string UnitId { get; }
    public RuntimeStateManager StateManager { get; }
    public RuntimeState CurrentState => StateManager.CurrentState;
    public IReadOnlyDictionary<string, string> Details => _details;

    public bool TryTransitionTo(string stateName)
    {
        var ok = StateManager.TryTransitionToByName(stateName);
        if (ok)
        {
            Sync();
        }

        return ok;
    }

    public RuntimeState TransitionTo(string stateName)
    {
        var state = StateManager.TransitionToByName(stateName);
        Sync();
        return state;
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
        return new RManagedStateSnapshot(UnitId, StateManager.Snapshot(), _details.ToDictionary());
    }

    public void Sync()
    {
        _managed?.UpdateState(UnitId, Snapshot());
    }
}

public sealed record RManagedStateSnapshot(
    string UnitId,
    RuntimeStateSnapshot State,
    IReadOnlyDictionary<string, string> Details);

namespace Iwesun.Runtime.Diagnostics;

internal sealed class RuntimeManagedUnitBase
{
	public RuntimeManagedUnitBase(string unitId, RuntimeStateCatalog? catalog = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(unitId);
		UnitId = unitId;
		Execution = RuntimeInjectionContext.Execution;
		Managed = RuntimeInjectionContext.Managed;
		State = new RManagedState(unitId, catalog);
	}

	public string UnitId { get; }
	public RuntimeExecutionManager? Execution { get; }
	public RuntimeManagedRegistry? Managed { get; }
	public IRManagedState State { get; }

	public void SetDetail(string key, string value) => State.SetDetail(key, value);
	public bool TryGetDetail(string key, out string? value) => State.TryGetDetail(key, out value);
	public RuntimeState TransitionTo(string stateName) => State.TransitionTo(stateName);
	public bool TryTransitionTo(string stateName) => State.TryTransitionTo(stateName);
	public RManagedStateSnapshot Snapshot() => State.Snapshot();
}

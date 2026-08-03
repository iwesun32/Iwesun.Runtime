namespace Iwesun.Runtime.Diagnostics;

internal abstract class RuntimeManagedCommandHandlerBase
{
	protected RuntimeManagedCommandHandlerBase(string unitId, RuntimeManagedRegistry? managed)
	{
		UnitId = unitId;
		Managed = managed;
	}

	protected string UnitId { get; }
	protected RuntimeManagedRegistry? Managed { get; }

	public bool Handle(RuntimeManagedCommand command)
	{
		switch (command.Kind)
		{
			case RuntimeManagedCommandKind.Initialize:
				OnInitialize(command);
				Publish("command-initialize", "Initialize command handled.", new { command.Sequence, command.Payload });
				return true;
			case RuntimeManagedCommandKind.Stop:
				OnStop(command);
				Publish("command-stop", "Stop command handled.", new { command.Sequence, command.Payload });
				return true;
			case RuntimeManagedCommandKind.Wait:
				OnWait(command);
				Publish("command-wait", "Wait command handled.", new { command.Sequence, command.Payload });
				return true;
			case RuntimeManagedCommandKind.Wakeup:
				OnWakeup(command);
				Publish("command-wakeup", "Wakeup command handled.", new { command.Sequence, command.Payload });
				return true;
			case RuntimeManagedCommandKind.Snapshot:
				OnSnapshot(command);
				Publish("command-snapshot", "Snapshot command handled.", new { command.Sequence, command.Payload });
				return true;
			default:
				Publish("command-ignored", "Command ignored by dispatcher.", new { command.Sequence, kind = command.Kind.ToString(), command.Payload });
				return false;
		}
	}

	protected virtual void OnInitialize(RuntimeManagedCommand command) { }
	protected virtual void OnStop(RuntimeManagedCommand command) { }
	protected virtual void OnWait(RuntimeManagedCommand command) { }
	protected virtual void OnWakeup(RuntimeManagedCommand command) { }
	protected virtual void OnSnapshot(RuntimeManagedCommand command) { }

	protected void Publish(string kind, string message, object? payload)
	{
		Managed?.PublishEvent(UnitId, kind, message, payload);
	}
}

internal sealed class DelegateRuntimeManagedCommandHandler : RuntimeManagedCommandHandlerBase
{
	public DelegateRuntimeManagedCommandHandler(string unitId, RuntimeManagedRegistry? managed)
		: base(unitId, managed)
	{
	}

	public Action<RuntimeManagedCommand>? OnInitializeAction { get; init; }
	public Action<RuntimeManagedCommand>? OnStopAction { get; init; }
	public Action<RuntimeManagedCommand>? OnWaitAction { get; init; }
	public Action<RuntimeManagedCommand>? OnWakeupAction { get; init; }
	public Action<RuntimeManagedCommand>? OnSnapshotAction { get; init; }

	protected override void OnInitialize(RuntimeManagedCommand command) => OnInitializeAction?.Invoke(command);
	protected override void OnStop(RuntimeManagedCommand command) => OnStopAction?.Invoke(command);
	protected override void OnWait(RuntimeManagedCommand command) => OnWaitAction?.Invoke(command);
	protected override void OnWakeup(RuntimeManagedCommand command) => OnWakeupAction?.Invoke(command);
	protected override void OnSnapshot(RuntimeManagedCommand command) => OnSnapshotAction?.Invoke(command);
}

namespace Iwesun.Runtime.Web;

public abstract class ElementSpaceSlottedProperty<TValue> :
	ElementSlottedProperty<TValue>
{
	public ElementSpaceSlotKind SlotKind { get; }

	protected ElementSpaceSlottedProperty(
		string name,
		ElementSpaceSlotKind slotKind,
		ElementPropertyValueSource valueSource,
		ElementPropertySlot<TValue> sourceInitialization,
		ElementPropertyLink sourceLink,
		ElementPropertySlot<TValue> sourceRuntime,
		ElementPropertySlot<TValue> xamlInitialization,
		ElementPropertyLink xamlLink,
		ElementPropertySlot<TValue> xamlRuntime,
		ElementPropertyValueSource xamlValueSource) :
		base(
			name,
			ElementSlotCategory.Space,
			valueSource,
			sourceInitialization,
			sourceLink,
			sourceRuntime,
			xamlInitialization,
			xamlLink,
			xamlRuntime,
			xamlValueSource)
	{
		SlotKind = slotKind;
	}
}

public abstract class ElementStyleSlottedProperty<TValue> :
	ElementSlottedProperty<TValue>
{
	public ElementStyleSlotKind SlotKind { get; }

	protected ElementStyleSlottedProperty(
		string name,
		ElementStyleSlotKind slotKind,
		ElementPropertyValueSource valueSource,
		ElementPropertySlot<TValue> sourceInitialization,
		ElementPropertyLink sourceLink,
		ElementPropertySlot<TValue> sourceRuntime,
		ElementPropertySlot<TValue> xamlInitialization,
		ElementPropertyLink xamlLink,
		ElementPropertySlot<TValue> xamlRuntime,
		ElementPropertyValueSource xamlValueSource) :
		base(
			name,
			ElementSlotCategory.Style,
			valueSource,
			sourceInitialization,
			sourceLink,
			sourceRuntime,
			xamlInitialization,
			xamlLink,
			xamlRuntime,
			xamlValueSource)
	{
		SlotKind = slotKind;
	}
}

public abstract class ElementEffectSlottedProperty<TValue> :
	ElementSlottedProperty<TValue>
{
	public ElementEffectSlotKind SlotKind { get; }

	protected ElementEffectSlottedProperty(
		string name,
		ElementEffectSlotKind slotKind,
		ElementPropertyValueSource valueSource,
		ElementPropertySlot<TValue> sourceInitialization,
		ElementPropertyLink sourceLink,
		ElementPropertySlot<TValue> sourceRuntime,
		ElementPropertySlot<TValue> xamlInitialization,
		ElementPropertyLink xamlLink,
		ElementPropertySlot<TValue> xamlRuntime,
		ElementPropertyValueSource xamlValueSource) :
		base(
			name,
			ElementSlotCategory.Effect,
			valueSource,
			sourceInitialization,
			sourceLink,
			sourceRuntime,
			xamlInitialization,
			xamlLink,
			xamlRuntime,
			xamlValueSource)
	{
		SlotKind = slotKind;
	}
}

public abstract class ElementActionSlottedProperty<TValue> :
	ElementSlottedProperty<TValue>
{
	public ElementActionSlotKind SlotKind { get; }

	protected ElementActionSlottedProperty(
		string name,
		ElementActionSlotKind slotKind,
		ElementPropertyValueSource valueSource,
		ElementPropertySlot<TValue> sourceInitialization,
		ElementPropertyLink sourceLink,
		ElementPropertySlot<TValue> sourceRuntime,
		ElementPropertySlot<TValue> xamlInitialization,
		ElementPropertyLink xamlLink,
		ElementPropertySlot<TValue> xamlRuntime,
		ElementPropertyValueSource xamlValueSource) :
		base(
			name,
			ElementSlotCategory.Action,
			valueSource,
			sourceInitialization,
			sourceLink,
			sourceRuntime,
			xamlInitialization,
			xamlLink,
			xamlRuntime,
			xamlValueSource)
	{
		SlotKind = slotKind;
	}
}

public abstract class ElementDataOrganizationSlottedProperty<TValue> :
	ElementSlottedProperty<TValue>
{
	public ElementDataOrganizationSlotKind SlotKind { get; }

	protected ElementDataOrganizationSlottedProperty(
		string name,
		ElementDataOrganizationSlotKind slotKind,
		ElementPropertyValueSource valueSource,
		ElementPropertySlot<TValue> sourceInitialization,
		ElementPropertyLink sourceLink,
		ElementPropertySlot<TValue> sourceRuntime,
		ElementPropertySlot<TValue> xamlInitialization,
		ElementPropertyLink xamlLink,
		ElementPropertySlot<TValue> xamlRuntime,
		ElementPropertyValueSource xamlValueSource) :
		base(
			name,
			ElementSlotCategory.DataOrganization,
			valueSource,
			sourceInitialization,
			sourceLink,
			sourceRuntime,
			xamlInitialization,
			xamlLink,
			xamlRuntime,
			xamlValueSource)
	{
		SlotKind = slotKind;
	}
}

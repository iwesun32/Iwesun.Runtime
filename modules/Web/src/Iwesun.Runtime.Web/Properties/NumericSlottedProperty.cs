namespace Iwesun.Runtime.Web;

public sealed class ElementNumericProperty :
	ElementSpaceSlottedProperty<double>,
	INumericElementPropertyAudit
{
	public ElementPropertyTraits Traits { get; }
	public NumericPropertyAuditDefinition AuditDefinition { get; }

	public override bool RequiresAudit =>
		AuditDefinition.RequiresStaticAudit
		|| AuditDefinition.RequiresRuntimeAudit;

	public ElementNumericProperty(
		string name,
		ElementSpaceSlotKind slotKind,
		ElementPropertyTraits traits,
		ElementPropertySlot<double> sourceInitialization,
		ElementPropertyLink sourceLink,
		ElementPropertySlot<double> sourceRuntime,
		ElementPropertyValueSource valueSource,
		ElementPropertySlot<double> xamlInitialization,
		ElementPropertyLink xamlLink,
		ElementPropertySlot<double> xamlRuntime,
		ElementPropertyValueSource xamlValueSource,
		NumericPropertyAuditDefinition auditDefinition) :
		base(
			name,
			slotKind,
			valueSource,
			sourceInitialization,
			sourceLink,
			sourceRuntime,
			xamlInitialization,
			xamlLink,
			xamlRuntime,
			xamlValueSource)
	{
		ArgumentNullException.ThrowIfNull(auditDefinition);
		if (traits.ValueKind is not (
			PropertyValueKind.Number
			or PropertyValueKind.Length
			or PropertyValueKind.Coordinate
			or PropertyValueKind.Size))
			throw new ArgumentException(
				"ElementNumericProperty 只能包装数值类属性。",
				nameof(traits));
		RequireFinite(sourceInitialization, nameof(sourceInitialization));
		RequireFinite(sourceRuntime, nameof(sourceRuntime));
		RequireFinite(xamlInitialization, nameof(xamlInitialization));
		RequireFinite(xamlRuntime, nameof(xamlRuntime));
		Traits = traits;
		AuditDefinition = auditDefinition;
	}

	public override ElementPropertyAuditResult<double> Audit() =>
		NumericElementPropertyAuditor.Audit(this);

	public ElementNumericProperty WithXamlSlots(
		ElementPropertySlot<double> xamlInitialization,
		ElementPropertyLink xamlLink,
		ElementPropertySlot<double> xamlRuntime,
		ElementPropertyValueSource xamlValueSource) =>
		new(
			Name,
			SlotKind,
			Traits,
			SourceInitialization,
			SourceLink,
			SourceRuntime,
			SourceValueSource,
			xamlInitialization,
			xamlLink,
			xamlRuntime,
			xamlValueSource,
			AuditDefinition);

	public ElementNumericProperty WithSourceSlots(
		ElementPropertySlot<double> sourceInitialization,
		ElementPropertyLink sourceLink,
		ElementPropertySlot<double> sourceRuntime,
		ElementPropertyValueSource sourceValueSource) =>
		new(
			Name,
			SlotKind,
			Traits,
			sourceInitialization,
			sourceLink,
			sourceRuntime,
			sourceValueSource,
			XamlInitialization,
			XamlLink,
			XamlRuntime,
			XamlValueSource,
			AuditDefinition);

	private static void RequireFinite(
		ElementPropertySlot<double> slot,
		string parameter)
	{
		if (slot.IsSet && !double.IsFinite(slot.Value))
			throw new ArgumentOutOfRangeException(
				parameter,
				"数值属性槽位必须包含有限数。");
	}
}

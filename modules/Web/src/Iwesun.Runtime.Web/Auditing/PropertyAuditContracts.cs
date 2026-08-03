namespace Iwesun.Runtime.Web;

public enum ElementPropertyAuditPhase
{
	StaticInitialization,
	Runtime
}

public enum ElementPropertyAuditStatus
{
	NotRequired,
	Passed,
	MissingSourceValue,
	MissingXamlValue,
	UnexpectedXamlValue,
	SourceUnitMismatch,
	XamlUnitMismatch,
	ValueMismatch,
	UnsupportedComparison,
	ManualReviewRequired,
	AuditExecutionFailed
}

public sealed record ElementPropertyAuditPhaseResult<TValue>(
	ElementPropertyAuditPhase Phase,
	ElementPropertyAuditStatus Status,
	ElementPropertySlot<TValue> Source,
	ElementPropertySlot<TValue> ExpectedXaml,
	ElementPropertySlot<TValue> ActualXaml,
	double? NumericDifference,
	double? NumericAllowedTolerance,
	string Message)
{
	public bool Passed =>
		Status is
			ElementPropertyAuditStatus.NotRequired
			or ElementPropertyAuditStatus.Passed;

	public ElementPropertyAuditPhaseSummary ToSummary() =>
		new(
			Phase,
			Status,
			Describe(Source),
			Describe(ExpectedXaml),
			Describe(ActualXaml),
			NumericDifference,
			NumericAllowedTolerance,
			Message);

	private static string Describe(ElementPropertySlot<TValue> slot) =>
		slot.IsSet
			? Convert.ToString(
				slot.Value,
				System.Globalization.CultureInfo.InvariantCulture) ?? "<null>"
			: "<unset>";
}

public sealed record ElementPropertyAuditPhaseSummary(
	ElementPropertyAuditPhase Phase,
	ElementPropertyAuditStatus Status,
	string SourceValue,
	string ExpectedXamlValue,
	string ActualXamlValue,
	double? NumericDifference,
	double? NumericAllowedTolerance,
	string Message)
{
	public bool Passed =>
		Status is
			ElementPropertyAuditStatus.NotRequired
			or ElementPropertyAuditStatus.Passed;
}

public abstract record ElementPropertyAuditResultBase(
	string PropertyName,
	ElementSlotCategory Category,
	bool Passed)
{
	public abstract ElementPropertyAuditPhaseSummary StaticSummary { get; }

	public abstract ElementPropertyAuditPhaseSummary RuntimeSummary { get; }
}

public sealed record ElementPropertyAuditResult<TValue>(
	string PropertyName,
	ElementSlotCategory Category,
	bool Passed,
	ElementPropertyAuditPhaseResult<TValue> StaticInitialization,
	ElementPropertyAuditPhaseResult<TValue> Runtime) :
	ElementPropertyAuditResultBase(PropertyName, Category, Passed)
{
	public override ElementPropertyAuditPhaseSummary StaticSummary =>
		StaticInitialization.ToSummary();

	public override ElementPropertyAuditPhaseSummary RuntimeSummary =>
		Runtime.ToSummary();
}

public interface IElementPropertyAudit
{
	string Name { get; }

	ElementSlotCategory Category { get; }

	bool RequiresAudit { get; }

	ElementPropertyAuditResultBase Audit();
}

public interface IElementPropertyAudit<TValue> : IElementPropertyAudit
{
	ElementPropertySlot<TValue> SourceInitialization { get; }

	ElementPropertySlot<TValue> SourceRuntime { get; }

	ElementPropertySlot<TValue> XamlInitialization { get; }

	ElementPropertySlot<TValue> XamlRuntime { get; }

	new ElementPropertyAuditResult<TValue> Audit();
}

public interface INumericElementPropertyAudit : IElementPropertyAudit<double>
{
	NumericPropertyAuditDefinition AuditDefinition { get; }
}

public static class ElementSlottedPropertyClassifiers
{
	public static bool IsSpaceProperty<TValue>(
		this ElementSlottedProperty<TValue> property) =>
		property.Category == ElementSlotCategory.Space;

	public static bool IsStyleProperty<TValue>(
		this ElementSlottedProperty<TValue> property) =>
		property.Category == ElementSlotCategory.Style;

	public static bool IsEffectProperty<TValue>(
		this ElementSlottedProperty<TValue> property) =>
		property.Category == ElementSlotCategory.Effect;

	public static bool IsActionProperty<TValue>(
		this ElementSlottedProperty<TValue> property) =>
		property.Category == ElementSlotCategory.Action;

	public static bool IsDataOrganizationProperty<TValue>(
		this ElementSlottedProperty<TValue> property) =>
		property.Category == ElementSlotCategory.DataOrganization;

	public static bool IsAuditRequired(this object property) =>
		property is IElementPropertyAudit { RequiresAudit: true };

	public static bool IsAuditable(this object property) =>
		property is IElementPropertyAudit;

	public static bool IsNumericAudit(this object property) =>
		property is INumericElementPropertyAudit;

	public static bool IsStaticAuditRequired(this object property) =>
		property is INumericElementPropertyAudit
		{
			AuditDefinition.RequiresStaticAudit: true
		};

	public static bool IsRuntimeAuditRequired(this object property) =>
		property is INumericElementPropertyAudit
		{
			AuditDefinition.RequiresRuntimeAudit: true
		};
}

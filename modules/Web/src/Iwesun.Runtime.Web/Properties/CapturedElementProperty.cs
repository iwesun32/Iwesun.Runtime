namespace Iwesun.Runtime.Web;

public sealed class CapturedElementProperty<TValue> :
	ElementSlottedProperty<TValue>
{
	public CapturedElementProperty(
		string name,
		ElementSlotCategory category,
		ElementPropertyTraits traits,
		ElementPropertyValueSource sourceValueSource,
		ElementPropertySlot<TValue> sourceInitialization,
		ElementPropertyLink sourceLink,
		ElementPropertySlot<TValue> sourceRuntime,
		ElementPropertyValueSource xamlValueSource,
		ElementPropertySlot<TValue> xamlInitialization,
		ElementPropertyLink xamlLink,
		ElementPropertySlot<TValue> xamlRuntime) :
		base(
			name,
			category,
			sourceValueSource,
			sourceInitialization,
			sourceLink,
			sourceRuntime,
			xamlInitialization,
			xamlLink,
			xamlRuntime,
			xamlValueSource)
	{
		Traits = traits;
	}

	public ElementPropertyTraits Traits { get; }

	public override bool RequiresAudit =>
		Traits.Translation != PropertyTranslationKind.NotApplicable;

	public override ElementPropertyAuditResult<TValue> Audit()
	{
		var staticResult = AuditPhase(
			ElementPropertyAuditPhase.StaticInitialization,
			SourceInitialization,
			XamlInitialization);
		var runtimeResult = AuditPhase(
			ElementPropertyAuditPhase.Runtime,
			SourceRuntime,
			XamlRuntime);
		return new(
			Name,
			Category,
			staticResult.Passed && runtimeResult.Passed,
			staticResult,
			runtimeResult);
	}

	public CapturedElementProperty<TValue> WithXamlSlots(
		ElementPropertyValueSource xamlValueSource,
		ElementPropertySlot<TValue> xamlInitialization,
		ElementPropertyLink xamlLink,
		ElementPropertySlot<TValue> xamlRuntime) =>
		new(
			Name,
			Category,
			Traits,
			SourceValueSource,
			SourceInitialization,
			SourceLink,
			SourceRuntime,
			xamlValueSource,
			xamlInitialization,
			xamlLink,
			xamlRuntime);

	private ElementPropertyAuditPhaseResult<TValue> AuditPhase(
		ElementPropertyAuditPhase phase,
		ElementPropertySlot<TValue> source,
		ElementPropertySlot<TValue> xaml)
	{
		if (Traits.Translation == PropertyTranslationKind.NotApplicable)
		{
			return Result(
				phase,
				ElementPropertyAuditStatus.NotRequired,
				source,
				xaml,
				"The property is not applicable to the target representation.");
		}

		if (!source.IsSet)
		{
			return Result(
				phase,
				xaml.IsSet
					? ElementPropertyAuditStatus.UnexpectedXamlValue
					: ElementPropertyAuditStatus.NotRequired,
				source,
				xaml,
				xaml.IsSet
					? "XAML contains a value that is absent from the source."
					: "This phase has no captured source or XAML value.");
		}

		if (Traits.Translation == PropertyTranslationKind.Unsupported
			|| Traits.Comparison == PropertyComparisonKind.ManualReview)
		{
			return Result(
				phase,
				ElementPropertyAuditStatus.ManualReviewRequired,
				source,
				xaml,
				"The property is preserved but requires manual translation review.");
		}

		if (!xaml.IsSet)
		{
			return Result(
				phase,
				ElementPropertyAuditStatus.MissingXamlValue,
				source,
				xaml,
				"The source value has no XAML value.");
		}

		var equal = ValuesEqual(source, xaml);
		return Result(
			phase,
			equal
				? ElementPropertyAuditStatus.Passed
				: ElementPropertyAuditStatus.ValueMismatch,
			source,
			xaml,
			equal ? "The values match." : "The values differ.");
	}

	private bool ValuesEqual(
		ElementPropertySlot<TValue> source,
		ElementPropertySlot<TValue> xaml)
	{
		if (source.Value is string sourceText
			&& xaml.Value is string xamlText)
		{
			return ElementPropertySemanticComparer.Compare(
				sourceText,
				source.Unit,
				xamlText,
				xaml.Unit,
				Traits).Equivalent;
		}
		if (Traits.Comparison is
			PropertyComparisonKind.NumericTolerance
			or PropertyComparisonKind.GeometryTolerance
			&& source.Value is IConvertible sourceNumber
			&& xaml.Value is IConvertible xamlNumber)
		{
			var sourceTextValue = sourceNumber.ToString(
				System.Globalization.CultureInfo.InvariantCulture);
			var xamlTextValue = xamlNumber.ToString(
				System.Globalization.CultureInfo.InvariantCulture);
			return ElementPropertySemanticComparer.Compare(
				sourceTextValue,
				source.Unit,
				xamlTextValue,
				xaml.Unit,
				Traits).Equivalent;
		}

		return EqualityComparer<TValue>.Default.Equals(
			source.Value,
			xaml.Value);
	}

	private static ElementPropertyAuditPhaseResult<TValue> Result(
		ElementPropertyAuditPhase phase,
		ElementPropertyAuditStatus status,
		ElementPropertySlot<TValue> source,
		ElementPropertySlot<TValue> xaml,
		string message) =>
		new(
			phase,
			status,
			source,
			source,
			xaml,
			null,
			null,
			message);
}

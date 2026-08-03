namespace Iwesun.Runtime.Web;

public sealed record ElementAuditStatistics(
	long ElementsVisited,
	long ElementsPassed,
	long ElementsFailed,
	long PropertiesVisited,
	long PropertiesAudited,
	long PropertiesSkipped,
	long PropertiesPassed,
	long PropertiesFailed,
	long NumericProperties,
	long StaticPassed,
	long StaticFailed,
	long StaticNotRequired,
	long RuntimePassed,
	long RuntimeFailed,
	long RuntimeNotRequired,
	long MissingSourceValues,
	long MissingXamlValues,
	long UnitMismatches,
	long ValueMismatches,
	long UnsupportedComparisons,
	long ManualReviews,
	long ExecutionFailures,
	double TotalNumericDifference,
	double MaximumNumericDifference,
	double TotalAllowedTolerance,
	IReadOnlyDictionary<ElementSlotCategory, long> CategoryAudits,
	IReadOnlyDictionary<ElementSlotCategory, long> CategoryFailures);

public sealed class ElementAuditStatisticsAccumulator
{
	private readonly object _gate = new();
	private readonly Dictionary<ElementSlotCategory, long> _categoryAudits = [];
	private readonly Dictionary<ElementSlotCategory, long> _categoryFailures = [];
	private long _elementsVisited;
	private long _elementsPassed;
	private long _elementsFailed;
	private long _propertiesVisited;
	private long _propertiesAudited;
	private long _propertiesSkipped;
	private long _propertiesPassed;
	private long _propertiesFailed;
	private long _numericProperties;
	private long _staticPassed;
	private long _staticFailed;
	private long _staticNotRequired;
	private long _runtimePassed;
	private long _runtimeFailed;
	private long _runtimeNotRequired;
	private long _missingSourceValues;
	private long _missingXamlValues;
	private long _unitMismatches;
	private long _valueMismatches;
	private long _unsupportedComparisons;
	private long _manualReviews;
	private long _executionFailures;
	private double _totalNumericDifference;
	private double _maximumNumericDifference;
	private double _totalAllowedTolerance;

	public void RecordVisitedProperty()
	{
		lock (_gate)
			_propertiesVisited++;
	}

	public void RecordSkippedProperty()
	{
		lock (_gate)
			_propertiesSkipped++;
	}

	public void AccumulateProperty(
		IElementPropertyAudit property,
		ElementPropertyAuditResultBase result)
	{
		ArgumentNullException.ThrowIfNull(property);
		ArgumentNullException.ThrowIfNull(result);
		lock (_gate)
		{
			_propertiesAudited++;
			if (property.IsNumericAudit())
				_numericProperties++;
			Increment(_categoryAudits, result.Category);
			if (result.Passed)
				_propertiesPassed++;
			else
			{
				_propertiesFailed++;
				Increment(_categoryFailures, result.Category);
			}
			AccumulatePhase(result.StaticSummary, isStatic: true);
			AccumulatePhase(result.RuntimeSummary, isStatic: false);
		}
	}

	public void AccumulateElement(bool passed)
	{
		lock (_gate)
		{
			_elementsVisited++;
			if (passed)
				_elementsPassed++;
			else
				_elementsFailed++;
		}
	}

	public ElementAuditStatistics Snapshot()
	{
		lock (_gate)
			return new(
				_elementsVisited,
				_elementsPassed,
				_elementsFailed,
				_propertiesVisited,
				_propertiesAudited,
				_propertiesSkipped,
				_propertiesPassed,
				_propertiesFailed,
				_numericProperties,
				_staticPassed,
				_staticFailed,
				_staticNotRequired,
				_runtimePassed,
				_runtimeFailed,
				_runtimeNotRequired,
				_missingSourceValues,
				_missingXamlValues,
				_unitMismatches,
				_valueMismatches,
				_unsupportedComparisons,
				_manualReviews,
				_executionFailures,
				_totalNumericDifference,
				_maximumNumericDifference,
				_totalAllowedTolerance,
				new Dictionary<ElementSlotCategory, long>(_categoryAudits),
				new Dictionary<ElementSlotCategory, long>(_categoryFailures));
	}

	private void AccumulatePhase(
		ElementPropertyAuditPhaseSummary phase,
		bool isStatic)
	{
		if (phase.Status == ElementPropertyAuditStatus.NotRequired)
		{
			if (isStatic)
				_staticNotRequired++;
			else
				_runtimeNotRequired++;
			return;
		}
		if (phase.Passed)
		{
			if (isStatic)
				_staticPassed++;
			else
				_runtimePassed++;
		}
		else if (isStatic)
			_staticFailed++;
		else
			_runtimeFailed++;
		switch (phase.Status)
		{
			case ElementPropertyAuditStatus.MissingSourceValue:
				_missingSourceValues++;
				break;
			case ElementPropertyAuditStatus.MissingXamlValue:
				_missingXamlValues++;
				break;
			case ElementPropertyAuditStatus.SourceUnitMismatch:
			case ElementPropertyAuditStatus.XamlUnitMismatch:
				_unitMismatches++;
				break;
			case ElementPropertyAuditStatus.ValueMismatch:
				_valueMismatches++;
				break;
			case ElementPropertyAuditStatus.UnsupportedComparison:
				_unsupportedComparisons++;
				break;
			case ElementPropertyAuditStatus.ManualReviewRequired:
				_manualReviews++;
				break;
			case ElementPropertyAuditStatus.AuditExecutionFailed:
				_executionFailures++;
				break;
		}
		if (phase.NumericDifference is double difference)
		{
			_totalNumericDifference += difference;
			_maximumNumericDifference = Math.Max(_maximumNumericDifference, difference);
		}
		if (phase.NumericAllowedTolerance is double tolerance)
			_totalAllowedTolerance += tolerance;
	}

	private static void Increment(
		Dictionary<ElementSlotCategory, long> values,
		ElementSlotCategory category) =>
		values[category] = values.GetValueOrDefault(category) + 1;
}

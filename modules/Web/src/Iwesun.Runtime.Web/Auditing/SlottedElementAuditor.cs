using System.Globalization;
using System.Text;

namespace Iwesun.Runtime.Web;

public sealed record SlottedElementAuditSubject
{
	public string ElementIdentity { get; }
	public string ElementType { get; }
	public IReadOnlyList<IElementPropertyAudit> Properties { get; }

	public SlottedElementAuditSubject(
		string elementIdentity,
		string elementType,
		IEnumerable<IElementPropertyAudit> properties)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(elementIdentity);
		ArgumentException.ThrowIfNullOrWhiteSpace(elementType);
		ArgumentNullException.ThrowIfNull(properties);
		var materialized = properties.ToArray();
		if (materialized.Any(static property => property is null))
			throw new ArgumentException("元素审核属性集合不能包含 null。", nameof(properties));
		ElementIdentity = elementIdentity;
		ElementType = elementType;
		Properties = materialized;
	}
}

public sealed record SlottedElementAuditReport(
	string ElementIdentity,
	string ElementType,
	bool Passed,
	IReadOnlyList<ElementPropertyAuditResultBase> PropertyResults,
	IReadOnlyList<string> SkippedProperties,
	ElementAuditStatistics Statistics,
	string TextReport)
{
	public void WriteText(TextWriter writer)
	{
		ArgumentNullException.ThrowIfNull(writer);
		writer.Write(TextReport);
	}
}

public static class SlottedElementAuditor
{
	public static SlottedElementAuditReport Audit(
		SlottedElementAuditSubject element,
		ElementAuditStatisticsAccumulator? aggregate = null)
	{
		ArgumentNullException.ThrowIfNull(element);
		var local = new ElementAuditStatisticsAccumulator();
		var results = new List<ElementPropertyAuditResultBase>();
		var skipped = new List<string>();
		foreach (var property in element.Properties)
		{
			local.RecordVisitedProperty();
			aggregate?.RecordVisitedProperty();
			if (!property.IsAuditRequired())
			{
				local.RecordSkippedProperty();
				aggregate?.RecordSkippedProperty();
				skipped.Add(property.GetType().Name);
				continue;
			}
			var result = AuditProperty(property);
			results.Add(result);
			local.AccumulateProperty(property, result);
			aggregate?.AccumulateProperty(property, result);
		}
		var passed = results.All(static result => result.Passed);
		local.AccumulateElement(passed);
		aggregate?.AccumulateElement(passed);
		var statistics = local.Snapshot();
		return new(
			element.ElementIdentity,
			element.ElementType,
			passed,
			results,
			skipped,
			statistics,
			ElementAuditTextFormatter.Format(
				element,
				passed,
				results,
				skipped,
				statistics));
	}

	private static ElementPropertyAuditResultBase AuditProperty(
		IElementPropertyAudit property)
	{
		try
		{
			return property.Audit();
		}
		catch (Exception exception) when (
			exception is not StackOverflowException
			and not OutOfMemoryException)
		{
			return ElementPropertyAuditFailureResult.Create(
				property.Name,
				property.Category,
				exception);
		}
	}
}

internal sealed record ElementPropertyAuditFailureResult(
	string FailedPropertyName,
	ElementSlotCategory FailedCategory,
	ElementPropertyAuditPhaseSummary StaticFailure,
	ElementPropertyAuditPhaseSummary RuntimeFailure) :
	ElementPropertyAuditResultBase(
		FailedPropertyName,
		FailedCategory,
		false)
{
	public override ElementPropertyAuditPhaseSummary StaticSummary => StaticFailure;
	public override ElementPropertyAuditPhaseSummary RuntimeSummary => RuntimeFailure;

	public static ElementPropertyAuditFailureResult Create(
		string propertyName,
		ElementSlotCategory category,
		Exception exception)
	{
		var staticResult = new ElementPropertyAuditPhaseSummary(
			ElementPropertyAuditPhase.StaticInitialization,
			ElementPropertyAuditStatus.NotRequired,
			"<unavailable>",
			"<unavailable>",
			"<unavailable>",
			null,
			null,
			"审核方法在运行阶段执行失败。");
		var runtimeResult = new ElementPropertyAuditPhaseSummary(
			ElementPropertyAuditPhase.Runtime,
			ElementPropertyAuditStatus.AuditExecutionFailed,
			"<unavailable>",
			"<unavailable>",
			"<unavailable>",
			null,
			null,
			$"{exception.GetType().Name}: {exception.Message}");
		return new(propertyName, category, staticResult, runtimeResult);
	}
}

public static class ElementAuditTextFormatter
{
	public static string Format(
		SlottedElementAuditSubject element,
		bool passed,
		IReadOnlyList<ElementPropertyAuditResultBase> results,
		IReadOnlyList<string> skipped,
		ElementAuditStatistics statistics)
	{
		var text = new StringBuilder();
		text.Append("Element ")
			.Append(element.ElementIdentity)
			.Append(" [")
			.Append(element.ElementType)
			.Append("] ")
			.AppendLine(passed ? "PASSED" : "FAILED");
		foreach (var result in results)
		{
			text.Append("  ")
				.Append(result.Category)
				.Append('.')
				.Append(result.PropertyName)
				.AppendLine(result.Passed ? " PASSED" : " FAILED");
			AppendPhase(text, result.StaticSummary);
			AppendPhase(text, result.RuntimeSummary);
		}
		foreach (var property in skipped)
			text.Append("  SKIPPED ").AppendLine(property);
		text.Append("Summary: properties=")
			.Append(statistics.PropertiesAudited.ToString(CultureInfo.InvariantCulture))
			.Append(", passed=")
			.Append(statistics.PropertiesPassed.ToString(CultureInfo.InvariantCulture))
			.Append(", failed=")
			.Append(statistics.PropertiesFailed.ToString(CultureInfo.InvariantCulture))
			.Append(", numericDifference=")
			.AppendLine(statistics.TotalNumericDifference.ToString(
				"0.########",
				CultureInfo.InvariantCulture));
		return text.ToString();
	}

	private static void AppendPhase(
		StringBuilder text,
		ElementPropertyAuditPhaseSummary phase)
	{
		text.Append("    ")
			.Append(phase.Phase)
			.Append(": ")
			.Append(phase.Status)
			.Append("; source=")
			.Append(phase.SourceValue)
			.Append("; expectedXaml=")
			.Append(phase.ExpectedXamlValue)
			.Append("; actualXaml=")
			.Append(phase.ActualXamlValue);
		if (phase.NumericDifference is double difference)
			text.Append("; difference=")
				.Append(difference.ToString("0.########", CultureInfo.InvariantCulture));
		if (phase.NumericAllowedTolerance is double tolerance)
			text.Append("; tolerance=")
				.Append(tolerance.ToString("0.########", CultureInfo.InvariantCulture));
		text.Append("; ").AppendLine(phase.Message);
	}
}

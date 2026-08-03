namespace Iwesun.Runtime.Web;

public sealed record EndToEndReconciliationIssue(
	string ElementIdentity,
	string Stage,
	string Description);

public sealed record EndToEndReconciliationReport(
	bool Passed,
	int ElementsExpected,
	int DomElementsFilled,
	int XamlElementsFilled,
	int XamlObjectsMaterialized,
	IReadOnlyList<DomElementAuditReport> ElementReports,
	IReadOnlyList<EndToEndReconciliationIssue> Issues);

/// <summary>
/// Reconciles the complete in-memory DOM fill, XAML materialization,
/// XAML runtime fill, and bidirectional slot-audit pipeline.
/// </summary>
public static class EndToEndReconciliationAuditor
{
	public static EndToEndReconciliationReport Audit(
		IReadOnlyList<DomElement> documentRoots,
		IReadOnlyList<DomElementFillResult> domFillResults,
		IReadOnlyList<XamlElementFillResult> xamlFillResults)
	{
		ArgumentNullException.ThrowIfNull(documentRoots);
		ArgumentNullException.ThrowIfNull(domFillResults);
		ArgumentNullException.ThrowIfNull(xamlFillResults);
		if (documentRoots.Count == 0)
			throw new ArgumentException(
				"At least one document root is required.",
				nameof(documentRoots));

		var issues = new List<EndToEndReconciliationIssue>();
		var elements = documentRoots
			.SelectMany(EnumerateElements)
			.ToArray();
		var elementMap = ToUniqueMap(
			elements,
			static element => Identity(element),
			"element tree",
			issues);
		var domMap = ToUniqueMap(
			domFillResults.SelectMany(EnumerateDomResults),
			static result => result.ElementIdentity,
			"DOM Fill",
			issues);
		var xamlMap = ToUniqueMap(
			xamlFillResults.SelectMany(EnumerateXamlResults),
			static result => result.ElementIdentity,
			"XAML Fill",
			issues);
		CompareIdentities(elementMap.Keys, domMap.Keys, "DOM Fill", issues);
		CompareIdentities(elementMap.Keys, xamlMap.Keys, "XAML Fill", issues);

		foreach (var (identity, fill) in domMap)
		{
			foreach (var slot in fill.Slots)
			{
				if (slot.QueryStatus == DomPropertyQueryStatus.SourceUnsupported)
				{
					issues.Add(new(
						identity,
						"DOM Fill",
						$"{slot.PropertyName}/{slot.Slot} is SourceUnsupported: "
							+ slot.QueryDescription));
				}
				if (!slot.IsCorrect)
				{
					issues.Add(new(
						identity,
						"DOM Fill",
						$"{slot.PropertyName}/{slot.Slot} was not stored exactly."));
				}
			}
		}
		foreach (var (identity, fill) in xamlMap)
		{
			foreach (var slot in fill.Slots)
			{
				if (slot.QueryStatus == XamlPropertyQueryStatus.TargetUnsupported)
				{
					issues.Add(new(
						identity,
						"XAML Fill",
						$"{slot.PropertyName}/{slot.Slot} is TargetUnsupported: "
							+ slot.QueryDescription));
				}
			}
		}

		var materialized = 0;
		foreach (var (identity, element) in elementMap)
		{
			if (element.XamlSupport == XamlConversionSupport.NonVisual)
				continue;
			if (element.XamlElement is null)
			{
				issues.Add(new(
					identity,
					"BuildXaml",
					"Visual element has no materialized XAML object."));
			}
			else
			{
				materialized++;
			}
			if (xamlMap.TryGetValue(identity, out var fill)
				&& fill.QueriedSlotCount == 0)
			{
				issues.Add(new(
					identity,
					"XAML Fill",
					"Visual element did not query any XAML slot."));
			}
		}

		var reports = new List<DomElementAuditReport>(documentRoots.Count);
		foreach (var root in documentRoots)
		{
			try
			{
				var report = root.Audit();
				reports.Add(report);
				if (!report.Passed)
				{
					issues.Add(new(
						Identity(root),
						"Audit",
						"Recursive source/target slot audit failed."));
				}
			}
			catch (Exception exception) when (
				exception is not StackOverflowException
					and not OutOfMemoryException)
			{
				issues.Add(new(
					Identity(root),
					"Audit",
					$"{exception.GetType().Name}: {exception.Message}"));
			}
		}

		return new(
			issues.Count == 0,
			elementMap.Count,
			domMap.Count,
			xamlMap.Count,
			materialized,
			reports,
			issues);
	}

	private static Dictionary<string, T> ToUniqueMap<T>(
		IEnumerable<T> values,
		Func<T, string> identity,
		string stage,
		ICollection<EndToEndReconciliationIssue> issues)
		where T : notnull
	{
		var map = new Dictionary<string, T>(StringComparer.Ordinal);
		foreach (var value in values)
		{
			var key = identity(value);
			if (!map.TryAdd(key, value))
				issues.Add(new(key, stage, "Duplicate strong element identity."));
		}
		return map;
	}

	private static void CompareIdentities(
		IEnumerable<string> expected,
		IEnumerable<string> actual,
		string stage,
		ICollection<EndToEndReconciliationIssue> issues)
	{
		var expectedSet = expected.ToHashSet(StringComparer.Ordinal);
		var actualSet = actual.ToHashSet(StringComparer.Ordinal);
		foreach (var identity in expectedSet.Except(actualSet, StringComparer.Ordinal))
			issues.Add(new(identity, stage, "Element result is missing."));
		foreach (var identity in actualSet.Except(expectedSet, StringComparer.Ordinal))
			issues.Add(new(identity, stage, "Unexpected element result exists."));
	}

	private static IEnumerable<DomElement> EnumerateElements(DomElement element)
	{
		yield return element;
		foreach (var child in element.Children)
		foreach (var descendant in EnumerateElements(child))
			yield return descendant;
	}

	private static IEnumerable<DomElementFillResult> EnumerateDomResults(
		DomElementFillResult result)
	{
		yield return result;
		foreach (var child in result.Children)
		foreach (var descendant in EnumerateDomResults(child))
			yield return descendant;
	}

	private static IEnumerable<XamlElementFillResult> EnumerateXamlResults(
		XamlElementFillResult result)
	{
		yield return result;
		foreach (var child in result.Children)
		foreach (var descendant in EnumerateXamlResults(child))
			yield return descendant;
	}

	private static string Identity(DomElement element) =>
		$"{element.DocumentScope}::{element.XPath}";
}

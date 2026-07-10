namespace Iwesun.Runtime.Diagnostics;

public delegate bool RuntimeNumericPredicate(double value1, double value2, double value3);

public sealed record RuntimeNumericBindingSnapshot(
	string BreakpointId,
	string Mode,
	string PredicateId,
	double Threshold1,
	double Threshold2,
	DateTimeOffset UpdatedAt);

public static class RuntimeNumericPredicateCatalog
{
	private static readonly Dictionary<string, RuntimeNumericPredicate> Predicates = new(StringComparer.OrdinalIgnoreCase)
	{
		["eq2"] = static (v1, v2, _) => v1 == v2,
		["ne2"] = static (v1, v2, _) => v1 != v2,
		["gt2"] = static (v1, v2, _) => v1 > v2,
		["ge2"] = static (v1, v2, _) => v1 >= v2,
		["lt2"] = static (v1, v2, _) => v1 < v2,
		["le2"] = static (v1, v2, _) => v1 <= v2,
		["between3"] = static (value, min, max) => value >= Math.Min(min, max) && value <= Math.Max(min, max),
		["outside3"] = static (value, min, max) => value < Math.Min(min, max) || value > Math.Max(min, max),
		["delta-le3"] = static (left, right, maxDelta) => Math.Abs(left - right) <= Math.Abs(maxDelta),
		["delta-gt3"] = static (left, right, minDelta) => Math.Abs(left - right) > Math.Abs(minDelta)
	};

	public static IReadOnlyList<string> SupportedPredicateIds => Predicates.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();

	public static bool TryGet(string predicateId, out RuntimeNumericPredicate predicate)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(predicateId);
		return Predicates.TryGetValue(predicateId.Trim(), out predicate!);
	}
}

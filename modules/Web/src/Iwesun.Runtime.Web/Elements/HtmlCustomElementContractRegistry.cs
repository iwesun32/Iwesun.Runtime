namespace Iwesun.Runtime.Web;

public static class HtmlCustomElementContractRegistry
{
	private static readonly Dictionary<string, HtmlXamlStrongTypeContract>
		Contracts = new(StringComparer.OrdinalIgnoreCase);
	private static readonly object Gate = new();

	public static void Register(
		string tagName,
		string decisionBasis,
		params string[] allowedElementNames)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(tagName);
		ArgumentException.ThrowIfNullOrWhiteSpace(decisionBasis);
		ArgumentNullException.ThrowIfNull(allowedElementNames);
		if (!tagName.Contains('-', StringComparison.Ordinal))
		{
			throw new ArgumentException(
				"Custom HTML element names must contain a hyphen.",
				nameof(tagName));
		}
		if (HtmlDomElementTypeCatalog.HtmlTags.Contains(
			tagName,
			StringComparer.OrdinalIgnoreCase))
		{
			throw new ArgumentException(
				"Standard HTML elements cannot use the custom-element registry.",
				nameof(tagName));
		}
		if (allowedElementNames.Length == 0
			|| allowedElementNames.Any(string.IsNullOrWhiteSpace))
		{
			throw new ArgumentException(
				"At least one non-empty XAML target is required.",
				nameof(allowedElementNames));
		}
		var contract = new HtmlXamlStrongTypeContract(
			tagName,
			allowedElementNames.ToHashSet(StringComparer.Ordinal),
			decisionBasis);
		lock (Gate)
		{
			if (!Contracts.TryAdd(tagName, contract))
			{
				throw new InvalidOperationException(
					$"Custom element '{tagName}' is already registered.");
			}
		}
	}

	public static bool TryGet(
		string tagName,
		out HtmlXamlStrongTypeContract? contract)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(tagName);
		lock (Gate)
			return Contracts.TryGetValue(tagName, out contract);
	}
}

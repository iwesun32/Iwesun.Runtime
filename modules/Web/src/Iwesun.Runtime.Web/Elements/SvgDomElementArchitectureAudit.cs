using System.Reflection;

namespace Iwesun.Runtime.Web;

public sealed record SvgDomElementArchitectureAudit(
	string TagName,
	string RuntimeType,
	bool UsesConcreteType,
	IReadOnlyList<string> MissingStandardAttributes,
	IReadOnlyList<string> UnexpectedStandardAttributes,
	IReadOnlyList<string> InvalidPipelineProperties,
	IReadOnlyList<string> MissingStandardEventHandlers,
	IReadOnlyList<string> UnexpectedStandardEventHandlers,
	IReadOnlyList<string> MissingOwnedXamlConversionProperties,
	IReadOnlyList<string> MissingDesignProperties,
	bool Passed)
{
	public static SvgDomElementArchitectureAudit Inspect(SvgDomElementDefinition element)
	{
		ArgumentNullException.ThrowIfNull(element);
		var reflected = ElementPropertyTraitsReflector
			.GetAttributes(element.GetType());
		var declared = reflected
			.Where(static traits =>
				traits.IsHtmlDefinedProperty && traits.IsSlottedProperty)
			.Select(traits => element.GetType().GetProperty(traits.PropertyName)?.GetValue(element))
			.OfType<SvgDomAttributeProperty>()
			.Select(static property => property.Name)
			.ToHashSet(StringComparer.Ordinal);
		var expected = SvgElementAttributeCatalog.GetSupportedAttributes(element.TagName);
		var missing = expected
			.Except(declared, StringComparer.Ordinal)
			.Order(StringComparer.Ordinal)
			.ToArray();
		var unexpected = declared
			.Except(expected, StringComparer.Ordinal)
			.Order(StringComparer.Ordinal)
			.ToArray();
		var invalidPipeline = reflected
			.Where(static traits => traits.IsHtmlDefinedProperty)
			.Where(static traits =>
				(!traits.IsSlottedProperty && !traits.IsSlottedPropertyCollection)
				|| !traits.IsFillRequired
				|| !traits.IsXamlFillRequired
				|| !traits.IsAuditRequired)
			.Select(static traits => traits.PropertyName)
			.Order(StringComparer.Ordinal)
			.ToArray();
		var expectedEvents = HtmlEventHandlerCatalog.GetSupported(element.TagName)
			.Select(static item => item.AttributeName)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		var declaredEvents = element.SupportedEventHandlers
			.Select(static item => item.AttributeName)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		var missingEvents = expectedEvents
			.Except(declaredEvents, StringComparer.OrdinalIgnoreCase)
			.Order(StringComparer.Ordinal)
			.ToArray();
		var unexpectedEvents = declaredEvents
			.Except(expectedEvents, StringComparer.OrdinalIgnoreCase)
			.Order(StringComparer.Ordinal)
			.ToArray();
		var specialized = element.GetType()
			.GetProperties(
				BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
			.Where(static property =>
				property.GetCustomAttribute<SvgElementPropertyAttribute>() is not null)
			.Select(static property => property.Name)
			.Order(StringComparer.Ordinal)
			.ToArray();
		var handling = element.GetElementSpecificXamlHandling();
		var conversionMethod = element.GetType().GetMethod(
			"BuildXamlAttributes",
			BindingFlags.Instance | BindingFlags.NonPublic);
		var missingOwnedConversion = specialized
			.Where(propertyName => !handling.ContainsKey(propertyName))
			.Concat(
				handling
					.Where(static item =>
						item.Value == SvgElementAttributeXamlHandling.InlineXaml)
					.Where(_ => conversionMethod?.DeclaringType != element.GetType())
					.Select(static item => item.Key))
			.Distinct(StringComparer.Ordinal)
			.Order(StringComparer.Ordinal)
			.ToArray();
		var concrete = element.GetType().IsSealed;
		var missingDesign = DomElementDesignPropertyCatalog.FindMissing(
			element.GetType());
		return new(
			element.TagName,
			element.GetType().FullName ?? element.GetType().Name,
			concrete,
			missing,
			unexpected,
			invalidPipeline,
			missingEvents,
			unexpectedEvents,
			missingOwnedConversion,
			missingDesign,
			concrete
				&& missing.Length == 0
				&& unexpected.Length == 0
				&& invalidPipeline.Length == 0
				&& missingEvents.Length == 0
				&& unexpectedEvents.Length == 0
				&& missingOwnedConversion.Length == 0
				&& missingDesign.Count == 0);
	}
}

using System.Reflection;

namespace Iwesun.Runtime.Web;

public sealed record HtmlDomElementArchitectureAudit(
	string TagName,
	string RuntimeType,
	bool UsesConcreteType,
	IReadOnlyList<string> MissingStandardAttributes,
	IReadOnlyList<string> UnexpectedStandardAttributes,
	IReadOnlyList<string> UnclassifiedStandardAttributes,
	IReadOnlyList<string> InvalidPipelineProperties,
	IReadOnlyList<string> MissingStandardEventHandlers,
	IReadOnlyList<string> UnexpectedStandardEventHandlers,
	IReadOnlyList<string> MissingOwnedXamlConversionProperties,
	IReadOnlyList<string> MissingDesignProperties,
	bool OwnsStrongXamlCreation,
	IReadOnlyList<string> XamlObjectContractErrors,
	bool Passed)
{
	public static HtmlDomElementArchitectureAudit Inspect(
		HtmlDomElementDefinition element)
	{
		ArgumentNullException.ThrowIfNull(element);
		var declaredValues = ElementPropertyTraitsReflector
			.GetAttributes(element.GetType())
			.Where(static traits => traits.IsHtmlDefinedProperty)
			.Select(traits => element.GetType().GetProperty(traits.PropertyName))
			.Where(static property => property is not null)
			.Select(property => property!.GetValue(element))
			.ToArray();
		var declared = declaredValues
			.OfType<DomElementStringProperty>()
			.Select(static property => property.Name)
			.Concat(declaredValues
				.OfType<IEnumerable<DomElementStringProperty>>()
				.SelectMany(static properties => properties)
				.Select(static property => property.Name))
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		var missing = HtmlElementAttributeCatalog
			.GetSupportedAttributes(element.TagName)
			.Where(attribute => !declared.Contains(attribute))
			.OrderBy(static attribute => attribute, StringComparer.Ordinal)
			.ToArray();
		var expectedAttributes = HtmlElementAttributeCatalog
			.GetSupportedAttributes(element.TagName)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		var unexpected = declaredValues
			.OfType<DomElementStringProperty>()
			.Select(static property => property.Name)
			.Where(attribute => !expectedAttributes.Contains(attribute))
			.OrderBy(static attribute => attribute, StringComparer.Ordinal)
			.ToArray();
		var classified = declaredValues
			.OfType<HtmlDomAttributeProperty>()
			.Concat(declaredValues
				.OfType<IEnumerable<DomElementStringProperty>>()
				.SelectMany(static properties => properties)
				.OfType<HtmlDomAttributeProperty>())
			.Select(static property => property.Name)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		var unclassified = HtmlElementAttributeCatalog
			.GetSupportedAttributes(element.TagName)
			.Where(attribute => !classified.Contains(attribute))
			.OrderBy(static attribute => attribute, StringComparer.Ordinal)
			.ToArray();
		var invalidPipeline = ElementPropertyTraitsReflector
			.GetAttributes(element.GetType())
			.Where(static traits => traits.IsHtmlDefinedProperty)
			.Where(static traits =>
				(!traits.IsSlottedProperty && !traits.IsSlottedPropertyCollection)
				|| !traits.IsFillRequired
				|| !traits.IsXamlFillRequired
				|| !traits.IsAuditRequired)
			.Select(static traits => traits.PropertyName)
			.OrderBy(static name => name, StringComparer.Ordinal)
			.ToArray();
		var expectedEvents = HtmlEventHandlerCatalog
			.GetSupported(element.TagName)
			.Select(static item => item.AttributeName)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		var declaredEvents = element.SupportedEventHandlers
			.Select(static item => item.AttributeName)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		var missingEvents = expectedEvents
			.Except(declaredEvents, StringComparer.OrdinalIgnoreCase)
			.OrderBy(static name => name, StringComparer.Ordinal)
			.ToArray();
		var unexpectedEvents = declaredEvents
			.Except(expectedEvents, StringComparer.OrdinalIgnoreCase)
			.OrderBy(static name => name, StringComparer.Ordinal)
			.ToArray();
		var declaredSpecializedProperties = element.XamlSupport is not
			(XamlConversionSupport.Direct or XamlConversionSupport.Composite)
			? []
			: element.GetType()
			.GetProperties(
				BindingFlags.Instance
					| BindingFlags.Public)
			.Where(static property =>
				property.GetCustomAttribute<HtmlElementPropertyAttribute>() is not null)
			.Where(property =>
				property.GetValue(element) is HtmlDomAttributeProperty attribute
				&& attribute.Definition.XamlStrategy is
					HtmlAttributeXamlStrategy.Semantic
					or HtmlAttributeXamlStrategy.Composite)
			.Select(static property => property.Name)
			.OrderBy(static name => name, StringComparer.Ordinal)
			.ToArray();
		var conversionMethod = element.GetType().GetMethod(
			"BuildXamlAttributes",
			BindingFlags.Instance | BindingFlags.NonPublic);
		var handling = element.GetElementSpecificXamlHandling();
		var missingOwnedConversion = declaredSpecializedProperties
			.Where(propertyName => !handling.ContainsKey(propertyName))
			.Concat(
				handling
					.Where(static item =>
						item.Value == HtmlElementAttributeXamlHandling.InlineXaml)
					.Where(_ => conversionMethod?.DeclaringType != element.GetType())
					.Select(static item => item.Key))
			.Distinct(StringComparer.Ordinal)
			.OrderBy(static name => name, StringComparer.Ordinal)
			.ToArray();
		var concrete = element is not HtmlGenericDomElement;
		var registeredCustom = !concrete
			&& HtmlCustomElementContractRegistry.TryGet(
				element.TagName,
				out _);
		var missingDesign = DomElementDesignPropertyCatalog.FindMissing(
			element.GetType());
		var createXamlMethod = element.GetType().GetMethod(
			"CreateXaml",
			BindingFlags.Instance | BindingFlags.NonPublic);
		var ownsStrongXamlCreation =
			createXamlMethod?.DeclaringType == element.GetType();
		var xamlObjectContractErrors =
			InspectXamlObjectContract(element).ToArray();
		return new(
			element.TagName,
			element.GetType().FullName ?? element.GetType().Name,
			concrete,
			missing,
			unexpected,
			unclassified,
			invalidPipeline,
			missingEvents,
			unexpectedEvents,
			missingOwnedConversion,
			missingDesign,
			ownsStrongXamlCreation,
			xamlObjectContractErrors,
			(concrete || registeredCustom)
				&& missing.Length == 0
				&& unexpected.Length == 0
				&& unclassified.Length == 0
				&& invalidPipeline.Length == 0
				&& missingEvents.Length == 0
				&& unexpectedEvents.Length == 0
				&& missingOwnedConversion.Length == 0
				&& missingDesign.Count == 0
				&& (ownsStrongXamlCreation || registeredCustom)
				&& xamlObjectContractErrors.Length == 0);
	}

	private static IEnumerable<string> InspectXamlObjectContract(
		HtmlDomElementDefinition element)
	{
		var registeredCustom =
			HtmlCustomElementContractRegistry.TryGet(
				element.TagName,
				out var customContract);
		if (!HtmlXamlStrongTypeContractCatalog.Contracts.TryGetValue(
				element.TagName,
				out var strongTypeContract)
			&& !registeredCustom)
		{
			yield return "No strong XAML type contract is registered.";
			yield break;
		}
		strongTypeContract ??= customContract
			?? throw new InvalidOperationException(
				"Registered custom element has no XAML contract.");
		var plans = element.BuildXamlObjectPlans();
		if (element.XamlSupport == XamlConversionSupport.NonVisual)
		{
			if (!strongTypeContract.AllowedElementNames.Contains(""))
				yield return "Nonvisual mapping is outside the strong type contract.";
			if (plans.Count > 0)
				yield return "Nonvisual element produced a XAML object.";
			yield break;
		}
		if (plans.Count != 1)
		{
			yield return $"Visual element produced {plans.Count} XAML roots.";
			yield break;
		}
		var plan = plans[0];
		if (!strongTypeContract.AllowedElementNames.Contains(
			plan.Mapping.ElementName))
		{
			yield return $"{plan.Mapping.ElementName} is outside the strong "
				+ $"type contract ({strongTypeContract.DecisionBasis}).";
		}
		if (string.IsNullOrWhiteSpace(plan.Mapping.ElementName))
			yield return "Visual element selected an empty XAML type.";
		if (plan.Mapping.Kind == XamlElementMappingKind.ConservativeContainer
			&& !registeredCustom)
			yield return "Standard element selected ConservativeContainer.";
		if (!CanOwnPlacement(
			plan.Mapping.ElementName,
			plan.ChildPlacement))
		{
			yield return $"{plan.Mapping.ElementName} cannot own "
				+ $"{plan.ChildPlacement} children.";
		}
	}

	private static bool CanOwnPlacement(
		string elementName,
		ElementXamlChildPlacementKind placement) =>
		HtmlXamlStrongTypeContractCatalog.CanOwnPlacement(
			elementName,
			placement);
}

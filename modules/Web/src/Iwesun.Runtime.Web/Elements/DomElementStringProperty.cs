namespace Iwesun.Runtime.Web;

/// <summary>
/// A string-valued element property whose DOM and XAML slots are filled in place.
/// Concrete element classes expose instances of this type as reflected properties.
/// </summary>
public class DomElementStringProperty :
	ElementDataOrganizationSlottedProperty<string>,
	IDomPropertySlotOwner,
	IXamlPropertySlotOwner,
	IXamlAttributeValueProvider,
	IXamlPropertyExecutionOwner,
	IDomElementSlotAuditOwner
{
	public DomElementStringProperty(
		string name,
		ElementDataOrganizationSlotKind slotKind) :
		base(
			name,
			slotKind,
			ElementPropertyValueSource.Unspecified,
			ElementPropertySlot<string>.Unset,
			ElementPropertyLink.None,
			ElementPropertySlot<string>.Unset,
			ElementPropertySlot<string>.Unset,
			ElementPropertyLink.None,
			ElementPropertySlot<string>.Unset,
			ElementPropertyValueSource.Unspecified)
	{
	}

	public virtual XamlPropertyExecutionDescriptor XamlExecution =>
		DomXamlPropertyExecutionCatalog.Resolve(Name);

	public ElementPropertySlot<string> TargetInitialization => XamlInitialization;

	public ElementPropertyLink TargetLink => XamlLink;

	public ElementPropertySlot<string> TargetRuntime => XamlRuntime;

	public IReadOnlyList<DomPropertyDataSlot> DomSlots { get; } =
	[
		DomPropertyDataSlot.Initialization,
		DomPropertyDataSlot.Link,
		DomPropertyDataSlot.Runtime
	];

	public IReadOnlyList<XamlPropertyDataSlot> XamlSlots { get; } =
	[
		XamlPropertyDataSlot.Initialization,
		XamlPropertyDataSlot.Link,
		XamlPropertyDataSlot.Runtime
	];

	public DomQueryApplicationResult ApplyDomQueryResult(
		DomPropertyDataSlot slot,
		DomPropertyQueryResult result)
	{
		ArgumentNullException.ThrowIfNull(result);
		if (result.Status == DomPropertyQueryStatus.ConfirmedAbsent)
		{
			switch (slot)
			{
				case DomPropertyDataSlot.Initialization:
					ClearSourceInitialization();
					break;
				case DomPropertyDataSlot.Link:
					ClearSourceLink();
					break;
				case DomPropertyDataSlot.Runtime:
					ClearSourceRuntime();
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(slot));
			}
			return DomQueryApplicationResult.NotApplied(result.Description);
		}
		if (!result.HasValue)
			return DomQueryApplicationResult.NotApplied(result.Description);
		switch (slot)
		{
			case DomPropertyDataSlot.Initialization:
				SetSourceInitialization(
					ElementPropertySlot<string>.FromValue(result.Value),
					result.ValueSource);
				break;
			case DomPropertyDataSlot.Link:
				SetSourceLink(result.Link, result.ValueSource);
				break;
			case DomPropertyDataSlot.Runtime:
				SetSourceRuntime(
					ElementPropertySlot<string>.FromValue(result.Value),
					result.ValueSource);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(slot));
		}
		return slot switch
		{
			DomPropertyDataSlot.Initialization => new(
				SourceInitialization.IsSet,
				SourceInitialization.IsSet && SourceInitialization.Value == result.Value,
				SourceInitialization.IsSet ? SourceInitialization.Value ?? string.Empty : string.Empty,
				"DOM initialization slot application."),
			DomPropertyDataSlot.Link => new(
				SourceLink.IsSet,
				SourceLink == result.Link,
				SourceLink.Description,
				"DOM link slot application."),
			DomPropertyDataSlot.Runtime => new(
				SourceRuntime.IsSet,
				SourceRuntime.IsSet && SourceRuntime.Value == result.Value,
				SourceRuntime.IsSet ? SourceRuntime.Value ?? string.Empty : string.Empty,
				"DOM runtime slot application."),
			_ => throw new ArgumentOutOfRangeException(nameof(slot))
		};
	}

	public void ApplyXamlQueryResult(
		XamlPropertyDataSlot slot,
		XamlPropertyQueryResult result)
	{
		ArgumentNullException.ThrowIfNull(result);
		if (result.Status == XamlPropertyQueryStatus.ConfirmedAbsent)
		{
			switch (slot)
			{
				case XamlPropertyDataSlot.Initialization:
					ClearXamlInitialization();
					break;
				case XamlPropertyDataSlot.Link:
					ClearXamlLink();
					break;
				case XamlPropertyDataSlot.Runtime:
					ClearXamlRuntime();
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(slot));
			}
			return;
		}
		if (!result.HasValue)
			return;
		switch (slot)
		{
			case XamlPropertyDataSlot.Initialization:
				SetXamlInitialization(
					ElementPropertySlot<string>.FromValue(result.Value),
					result.ValueSource);
				break;
			case XamlPropertyDataSlot.Link:
				SetXamlLink(result.Link, result.ValueSource);
				break;
			case XamlPropertyDataSlot.Runtime:
				SetXamlRuntime(
					ElementPropertySlot<string>.FromValue(result.Value),
					result.ValueSource);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(slot));
		}
	}

	public IReadOnlyList<XamlAttributeValueCandidate>
		GetXamlAttributeValueCandidates() =>
	[
		new(
			XamlValueResolutionPriority.SpecifiedCalculation,
			SourceLink.IsSet
				? SourceLink.Description
				: XamlLink.IsSet ? XamlLink.Description : null,
			"DOM source binding or generated XAML binding."),
		new(
			XamlValueResolutionPriority.LocalLayout,
			SourceValueSource == ElementPropertyValueSource.ContainerAutomaticLayout
				&& SourceInitialization.IsSet
					? SourceInitialization.Value
					: XamlValueSource == ElementPropertyValueSource.ContainerAutomaticLayout
						&& XamlInitialization.IsSet
							? XamlInitialization.Value
							: null,
			"DOM or generated XAML container-layout initialization."),
		new(
			XamlValueResolutionPriority.AbsoluteConstant,
			SourceInitialization.IsSet
				? SourceInitialization.Value
				: XamlInitialization.IsSet ? XamlInitialization.Value : null,
			"DOM or generated XAML initialization value.")
	];

	public override ElementPropertyAuditResult<string> Audit() =>
		new(
			Name,
			Category,
			AuditPair(SourceInitialization, XamlInitialization).Passed
				&& AuditPair(SourceRuntime, XamlRuntime).Passed,
			AuditPair(SourceInitialization, XamlInitialization),
			AuditPair(SourceRuntime, XamlRuntime));

	public virtual DomElementSlottedPropertyAuditResult AuditSlots()
	{
		var traits = new ElementPropertyTraits(
			GetType(),
			Name,
			PropertyValueKind.Text,
			PropertyValueStage.DomRuntime,
			PropertyCoordinateMode.None,
			PropertyReferenceSpace.None,
			PropertyAxis.None,
			PropertyUnit.None,
			PropertyTranslationKind.Direct,
			PropertyComparisonKind.Exact,
			PropertyInheritanceKind.NotInherited,
			true,
			0,
			string.Empty,
			"Fallback exact string comparison.");
		return AuditSlots(traits);
	}

	public virtual DomElementSlottedPropertyAuditResult AuditSlots(
		ElementPropertyTraits traits)
	{
		traits = DomRuntimePropertyComparisonTraits.Resolve(
			Name,
			Category,
			traits);
		if (!XamlExecution.IsSupported
			|| traits.Translation is PropertyTranslationKind.NotApplicable
				or PropertyTranslationKind.Unsupported)
		{
			return new(
				Name,
				Category,
				AuditUnsupportedSlot(
					SourceInitialization,
					XamlInitialization),
				AuditUnsupportedSlot(SourceRuntime, XamlRuntime),
				AuditUnsupportedLink(SourceLink, XamlLink),
				ElementSlotFeatureAuditResult.NotRequired(
					"No container-layout contract."),
				"DOM-only property evidence is preserved without inventing "
					+ "a same-named XAML value.");
		}
		var initialization = ElementPropertySemanticComparer.AuditSlots(
			SourceInitialization,
			XamlInitialization,
			traits);
		var runtime = ElementPropertySemanticComparer.AuditSlots(
			SourceRuntime,
			XamlRuntime,
			traits);
		var link = ElementPropertySemanticComparer.AuditLinks(
			SourceLink,
			XamlLink,
			traits);
		return new(
			Name,
			Category,
			initialization,
			runtime,
			link,
			ElementSlotFeatureAuditResult.NotRequired("No container-layout contract."),
			"String property audit.");
	}

	private static ElementSlotFeatureAuditResult AuditUnsupportedSlot(
		ElementPropertySlot<string> source,
		ElementPropertySlot<string> target) =>
		target.IsSet
			? new(
				ElementSlotFeatureAuditStatus.UnexpectedXamlValue,
				source.Value ?? string.Empty,
				target.Value ?? string.Empty,
				"XAML contains a value for a DOM-only property.")
			: ElementSlotFeatureAuditResult.NotRequired(
				"The DOM property has no same-element XAML execution contract.");

	private static ElementSlotFeatureAuditResult AuditUnsupportedLink(
		ElementPropertyLink source,
		ElementPropertyLink target) =>
		target.IsSet
			? new(
				ElementSlotFeatureAuditStatus.UnexpectedXamlValue,
				source.Description,
				target.Description,
				"XAML contains a link for a DOM-only property.")
			: ElementSlotFeatureAuditResult.NotRequired(
				"The DOM property has no same-element XAML link contract.");

	private static ElementSlotFeatureAuditResult AuditFeature(
		ElementPropertySlot<string> source,
		ElementPropertySlot<string> target)
	{
		if (!source.IsSet && !target.IsSet)
			return ElementSlotFeatureAuditResult.NotRequired("DOM value is absent.");
		if (!source.IsSet)
			return new(
				ElementSlotFeatureAuditStatus.UnexpectedXamlValue,
				string.Empty,
				target.Value ?? string.Empty,
				"XAML contains a value that is absent from the DOM.");
		if (!target.IsSet)
			return new(
				ElementSlotFeatureAuditStatus.MissingXamlValue,
				source.Value ?? string.Empty,
				string.Empty,
				"XAML value is missing.");
		return string.Equals(source.Value, target.Value, StringComparison.Ordinal)
			? Passed("Values match.")
			: new(
				ElementSlotFeatureAuditStatus.ValueMismatch,
				source.Value ?? string.Empty,
				target.Value ?? string.Empty,
				"Values differ.");
	}

	private static ElementSlotFeatureAuditResult Passed(string description) =>
		new(ElementSlotFeatureAuditStatus.Passed, string.Empty, string.Empty, description);

	private static ElementPropertyAuditPhaseResult<string> AuditPair(
		ElementPropertySlot<string> source,
		ElementPropertySlot<string> target) =>
		new(
			ElementPropertyAuditPhase.Runtime,
			!source.IsSet && !target.IsSet
				? ElementPropertyAuditStatus.NotRequired
				: !source.IsSet
					? ElementPropertyAuditStatus.UnexpectedXamlValue
				: target.IsSet && string.Equals(source.Value, target.Value, StringComparison.Ordinal)
					? ElementPropertyAuditStatus.Passed
					: ElementPropertyAuditStatus.ValueMismatch,
			source,
			target,
			target,
			null,
			null,
			"String slot comparison.");
}

namespace Iwesun.Runtime.Web;

/// <summary>
/// A mutable string representation of a live DOM layout, style, state, or effect value.
/// The category is fixed when the concrete element constructs the property.
/// </summary>
public class DomElementRuntimeProperty :
	ElementSlottedProperty<string>,
	IDomPropertySlotOwner,
	IXamlPropertySlotOwner,
	IXamlAttributeValueProvider,
	IXamlPropertyExecutionOwner,
	IDomElementSlotAuditOwner
{
	public DomElementRuntimeProperty(string name, ElementSlotCategory category) :
		this(
			name,
			category,
			DomElementRuntimePropertyCatalog.ResolveEvidenceKind(name))
	{
	}

	public DomElementRuntimeProperty(
		string name,
		ElementSlotCategory category,
		ElementEvidenceKind evidenceKind) :
		base(
			name,
			category,
			ElementPropertyValueSource.Unspecified,
			ElementPropertySlot<string>.Unset,
			ElementPropertyLink.None,
			ElementPropertySlot<string>.Unset,
			ElementPropertySlot<string>.Unset,
			ElementPropertyLink.None,
			ElementPropertySlot<string>.Unset,
			ElementPropertyValueSource.Unspecified)
	{
		if (evidenceKind == ElementEvidenceKind.None)
		{
			throw new ArgumentOutOfRangeException(
				nameof(evidenceKind),
				"Runtime DOM properties require explicit evidence semantics.");
		}
		EvidenceKind = evidenceKind;
	}

	public ElementEvidenceKind EvidenceKind { get; }

	public XamlPropertyExecutionDescriptor XamlExecution =>
		DomXamlPropertyExecutionCatalog.Resolve(Name);

	public ElementPropertySlot<string> TargetInitialization => XamlInitialization;

	public ElementPropertyLink TargetLink => XamlLink;

	public ElementPropertySlot<string> TargetRuntime => XamlRuntime;

	public virtual IReadOnlyList<DomPropertyDataSlot> DomSlots { get; } =
	[
		DomPropertyDataSlot.Initialization,
		DomPropertyDataSlot.Link,
		DomPropertyDataSlot.Runtime
	];

	public virtual IReadOnlyList<XamlPropertyDataSlot> XamlSlots { get; } =
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
			Compare(SourceRuntime, XamlRuntime).Passed,
			Compare(SourceInitialization, XamlInitialization),
			Compare(SourceRuntime, XamlRuntime));

	public DomElementSlottedPropertyAuditResult AuditSlots()
	{
		var traits = new ElementPropertyTraits(
			GetType(),
			Name,
			PropertyValueKind.State,
			PropertyValueStage.DomRuntime,
			PropertyCoordinateMode.None,
			PropertyReferenceSpace.None,
			PropertyAxis.None,
			PropertyUnit.None,
			PropertyTranslationKind.RuntimeCalculated,
			PropertyComparisonKind.Exact,
			PropertyInheritanceKind.NotInherited,
			true,
			0,
			XamlExecution.TargetProperty,
			"Fallback exact runtime comparison.");
		return AuditSlots(traits);
	}

	public DomElementSlottedPropertyAuditResult AuditSlots(
		ElementPropertyTraits traits)
	{
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
				"DOM runtime evidence remains available to global services "
					+ "without fabricating a XAML target value.");
		}
		var effectiveTraits =
			DomRuntimePropertyComparisonTraits.Resolve(Name, Category, traits);
		var initialization = ElementPropertySemanticComparer.AuditSlots(
			SourceInitialization,
			XamlInitialization,
			effectiveTraits);
		var runtime = ElementPropertySemanticComparer.AuditSlots(
			SourceRuntime,
			XamlRuntime,
			effectiveTraits);
		var link = ElementPropertySemanticComparer.AuditLinks(
			SourceLink,
			XamlLink,
			effectiveTraits);
		return new(
			Name,
			Category,
			initialization,
			runtime,
			link,
			ElementSlotFeatureAuditResult.NotRequired("No container-layout contract."),
			"Live runtime property audit.");
	}

	private static ElementSlotFeatureAuditResult AuditUnsupportedSlot(
		ElementPropertySlot<string> source,
		ElementPropertySlot<string> target) =>
		target.IsSet
			? new(
				ElementSlotFeatureAuditStatus.UnexpectedXamlValue,
				source.Value ?? string.Empty,
				target.Value ?? string.Empty,
				"XAML contains a value for an unsupported runtime projection.")
			: ElementSlotFeatureAuditResult.NotRequired(
				"The runtime property has no same-element XAML execution contract.");

	private static ElementSlotFeatureAuditResult AuditUnsupportedLink(
		ElementPropertyLink source,
		ElementPropertyLink target) =>
		target.IsSet
			? new(
				ElementSlotFeatureAuditStatus.UnexpectedXamlValue,
				source.Description,
				target.Description,
				"XAML contains a link for an unsupported runtime projection.")
			: ElementSlotFeatureAuditResult.NotRequired(
				"The runtime property has no same-element XAML link contract.");

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
		{
			return new(
				ElementSlotFeatureAuditStatus.MissingXamlValue,
				source.Value ?? string.Empty,
				string.Empty,
				"XAML value is missing.");
		}
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

	private static ElementPropertyAuditPhaseResult<string> Compare(
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
			"Runtime string slot comparison.");
}

/// <summary>
/// One immutable-axis runtime geometry contract. DOM geometry and rendered
/// XAML geometry occupy their respective Runtime slots; initialization and
/// link slots are intentionally not part of this property.
/// </summary>
public sealed class DomElementRuntimeGeometryProperty :
	DomElementRuntimeProperty,
	IRuntimeOnlySlotOwner
{
	internal DomElementRuntimeGeometryProperty(string name) :
		base(
			name,
			ElementSlotCategory.Space,
			ElementEvidenceKind.LocalLayout
				| ElementEvidenceKind.DomRuntimeGeometry)
	{
		if (name is not
			("rect.x" or "rect.y" or "rect.width" or "rect.height"))
		{
			throw new ArgumentOutOfRangeException(
				nameof(name),
				name,
				"Runtime geometry supports only x, y, width, and height.");
		}
	}

	public override IReadOnlyList<DomPropertyDataSlot> DomSlots { get; } =
		[DomPropertyDataSlot.Runtime];

	public override IReadOnlyList<XamlPropertyDataSlot> XamlSlots { get; } =
		[XamlPropertyDataSlot.Runtime];
}

/// <summary>
/// Fixed runtime-geometry slot group shared by every DOM element. This is a
/// compile-time member of <see cref="DomElement"/>, not a page-derived list of
/// dynamically attached properties.
/// </summary>
public sealed class DomElementRuntimeGeometry :
	IReadOnlyList<DomElementRuntimeGeometryProperty>
{
	private readonly DomElementRuntimeGeometryProperty[] _properties;

	public DomElementRuntimeGeometry()
	{
		X = new("rect.x");
		Y = new("rect.y");
		Width = new("rect.width");
		Height = new("rect.height");
		_properties = [X, Y, Width, Height];
	}

	public DomElementRuntimeGeometryProperty X { get; }

	public DomElementRuntimeGeometryProperty Y { get; }

	public DomElementRuntimeGeometryProperty Width { get; }

	public DomElementRuntimeGeometryProperty Height { get; }

	public int Count => _properties.Length;

	public DomElementRuntimeGeometryProperty this[int index] =>
		_properties[index];

	public IEnumerator<DomElementRuntimeGeometryProperty> GetEnumerator() =>
		((IEnumerable<DomElementRuntimeGeometryProperty>)_properties)
			.GetEnumerator();

	System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
		_properties.GetEnumerator();
}

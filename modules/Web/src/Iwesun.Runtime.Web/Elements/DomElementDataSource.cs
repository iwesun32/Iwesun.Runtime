namespace Iwesun.Runtime.Web;

public enum DomDataSourceKind
{
	Unspecified,
	TextContent,
	InnerText,
	Attribute,
	FormValue,
	CheckedState,
	SelectedState,
	SelectedValue,
	OptionItems,
	ChildElementItems,
	Url,
	Resource,
	Custom
}

public enum DomDataSourceDomain
{
	UiResource,
	BusinessInput
}

public enum XamlControlDataTargetKind
{
	Unspecified,
	DataContext,
	Content,
	Text,
	ItemsSource,
	SelectedItem,
	SelectedValue,
	Value,
	IsChecked,
	IsIndeterminate,
	IsEnabled,
	IsReadOnly,
	TabIndex,
	IsValid,
	WillValidate,
	ValidationMessage,
	Source,
	NavigateUri,
	CommandParameter,
	PlaceholderText,
	Header,
	Custom
}

public abstract class DomElementDataSource :
	ElementDataOrganizationSlottedProperty<string>,
	IDomPropertySlotOwner,
	IXamlPropertySlotOwner,
	IXamlAttributeValueProvider,
	IXamlPropertyExecutionOwner,
	IDomElementSlotAuditOwner
{
	protected DomElementDataSource(
		string name,
		ElementDataOrganizationSlotKind slotKind,
		DomDataSourceKind domSourceKind,
		DomDataSourceDomain domain,
		XamlControlDataTargetKind xamlTargetKind,
		ElementPropertyValueSource domValueSource,
		ElementPropertySlot<string> domInitialization,
		ElementPropertyLink domLink,
		ElementPropertySlot<string> domRuntime,
		ElementPropertyValueSource xamlValueSource,
		ElementPropertySlot<string> xamlInitialization,
		ElementPropertyLink xamlLink,
		ElementPropertySlot<string> xamlRuntime) :
		base(
			name,
			slotKind,
			domValueSource,
			domInitialization,
			domLink,
			domRuntime,
			xamlInitialization,
			xamlLink,
			xamlRuntime,
			xamlValueSource)
	{
		DomSourceKind = domSourceKind;
		Domain = domain;
		XamlTargetKind = xamlTargetKind;
	}

	public DomDataSourceKind DomSourceKind { get; }

	public DomDataSourceDomain Domain { get; }

	public XamlControlDataTargetKind XamlTargetKind { get; }

	public XamlPropertyExecutionDescriptor XamlExecution =>
		DomXamlPropertyExecutionCatalog.Resolve(Name, XamlTargetKind);

	public ElementPropertySlot<string> TargetInitialization => XamlInitialization;

	public ElementPropertyLink TargetLink => XamlLink;

	public ElementPropertySlot<string> TargetRuntime => XamlRuntime;

	public IReadOnlyList<DomPropertyDataSlot> DomSlots { get; } =
	[
		DomPropertyDataSlot.Initialization,
		DomPropertyDataSlot.Link,
		DomPropertyDataSlot.Runtime
	];

	public abstract DomQueryApplicationResult ApplyDomQueryResult(
		DomPropertyDataSlot slot,
		DomPropertyQueryResult result);

	public IReadOnlyList<XamlPropertyDataSlot> XamlSlots { get; } =
	[
		XamlPropertyDataSlot.Initialization,
		XamlPropertyDataSlot.Link,
		XamlPropertyDataSlot.Runtime
	];

	public abstract void ApplyXamlQueryResult(
		XamlPropertyDataSlot slot,
		XamlPropertyQueryResult result);

	public IReadOnlyList<XamlAttributeValueCandidate>
		GetXamlAttributeValueCandidates()
	{
		var candidates = new List<XamlAttributeValueCandidate>(3);
		if ((XamlValueSource is
				ElementPropertyValueSource.LinkedCalculation
				or ElementPropertyValueSource.LinkedConstant)
			&& XamlLink.IsSet)
		{
			candidates.Add(new(
				XamlValueResolutionPriority.SpecifiedCalculation,
				XamlLink.Description,
				"使用指定的 XAML 计算或常量链接。"));
		}
		if (XamlValueSource == ElementPropertyValueSource.ContainerAutomaticLayout)
		{
			candidates.Add(new(
				XamlValueResolutionPriority.LocalLayout,
				XamlInitialization.IsSet
					? XamlInitialization.Value
					: null,
				"使用局部容器布局计算值。"));
		}
		candidates.Add(new(
			XamlValueResolutionPriority.AbsoluteConstant,
			XamlInitialization.IsSet
				? XamlInitialization.Value
				: null,
			"使用 XAML 初始化绝对常量。"));
		return candidates;
	}

	public abstract DomElementSlottedPropertyAuditResult AuditSlots();
}

public sealed class DomElementStringDataSource :
	DomElementDataSource
{
	public DomElementStringDataSource(
		string name,
		ElementDataOrganizationSlotKind slotKind,
		DomDataSourceKind domSourceKind,
		DomDataSourceDomain domain,
		XamlControlDataTargetKind xamlTargetKind) :
		base(
			name,
			slotKind,
			domSourceKind,
			domain,
			xamlTargetKind,
			ElementPropertyValueSource.Unspecified,
			ElementPropertySlot<string>.Unset,
			ElementPropertyLink.None,
			ElementPropertySlot<string>.Unset,
			ElementPropertyValueSource.Unspecified,
			ElementPropertySlot<string>.Unset,
			ElementPropertyLink.None,
			ElementPropertySlot<string>.Unset)
	{
	}

	public override DomQueryApplicationResult ApplyDomQueryResult(
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
				"DOM data initialization slot application."),
			DomPropertyDataSlot.Link => new(
				SourceLink.IsSet,
				SourceLink == result.Link,
				SourceLink.Description,
				"DOM data link slot application."),
			DomPropertyDataSlot.Runtime => new(
				SourceRuntime.IsSet,
				SourceRuntime.IsSet && SourceRuntime.Value == result.Value,
				SourceRuntime.IsSet ? SourceRuntime.Value ?? string.Empty : string.Empty,
				"DOM data runtime slot application."),
			_ => throw new ArgumentOutOfRangeException(nameof(slot))
		};
	}

	public override void ApplyXamlQueryResult(
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

	public override ElementPropertyAuditResult<string> Audit()
	{
		var initialization = Compare(SourceInitialization, XamlInitialization);
		var runtime = Compare(SourceRuntime, XamlRuntime);
		return new(
			Name,
			Category,
			initialization.Passed && runtime.Passed,
			initialization,
			runtime);
	}

	public override DomElementSlottedPropertyAuditResult AuditSlots()
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
			PropertyTranslationKind.Semantic,
			PropertyComparisonKind.Exact,
			PropertyInheritanceKind.NotInherited,
			true,
			0,
			XamlExecution.TargetProperty,
			"Fallback exact data-source comparison.");
		return AuditSlots(traits);
	}

	public DomElementSlottedPropertyAuditResult AuditSlots(
		ElementPropertyTraits traits)
	{
		traits = DomRuntimePropertyComparisonTraits.Resolve(
			Name,
			Category,
			traits);
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
			ElementSlotFeatureAuditResult.NotRequired(
				"Data sources do not use a container-layout contract."),
			$"{DomSourceKind} -> {XamlTargetKind} data-source audit.");
	}

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
				"XAML contains a data-source value that is absent from the DOM.");
		if (!target.IsSet)
		{
			return new(
				ElementSlotFeatureAuditStatus.MissingXamlValue,
				source.Value ?? string.Empty,
				string.Empty,
				"XAML data-source value is missing.");
		}
		return string.Equals(source.Value, target.Value, StringComparison.Ordinal)
			? Passed("Data-source values match.")
			: new(
				ElementSlotFeatureAuditStatus.ValueMismatch,
				source.Value ?? string.Empty,
				target.Value ?? string.Empty,
				"Data-source values differ.");
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
				: target.IsSet && string.Equals(
					source.Value,
					target.Value,
					StringComparison.Ordinal)
					? ElementPropertyAuditStatus.Passed
					: ElementPropertyAuditStatus.ValueMismatch,
			source,
			target,
			target,
			null,
			null,
			"String data-source slot comparison.");
}

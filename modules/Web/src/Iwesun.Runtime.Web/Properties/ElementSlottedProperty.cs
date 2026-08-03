namespace Iwesun.Runtime.Web;

public enum ElementSlotCategory
{
	Unspecified = -1,
	Space,
	Style,
	Effect,
	Action,
	DataOrganization
}

public enum ElementPropertyValueSource
{
	Unspecified,
	ContainerAutomaticLayout,
	LinkedCalculation,
	LinkedConstant,
	DirectConstant
}

public enum ElementPropertyLinkKind
{
	None,
	DomDescription,
	XPath,
	Url,
	CssExpression,
	LayoutExpression,
	ContainerLayout,
	XamlBinding,
	ConstantReference,
	CustomString
}

public readonly record struct ElementPropertySlot<TValue>
{
	public bool IsSet { get; }
	public TValue? Value { get; }
	public PropertyUnit Unit { get; }

	private ElementPropertySlot(TValue value, PropertyUnit unit)
	{
		ArgumentNullException.ThrowIfNull(value);
		IsSet = true;
		Value = value;
		Unit = unit;
	}

	public static ElementPropertySlot<TValue> Unset => default;

	public static ElementPropertySlot<TValue> FromValue(
		TValue value,
		PropertyUnit unit = PropertyUnit.None) =>
		new(value, unit);
}

public readonly record struct ElementPropertyLink
{
	public ElementPropertyLinkKind Kind { get; }
	public string Description { get; }
	public ContainerLayoutBinding? ContainerLayout { get; }
	public bool IsSet => Kind != ElementPropertyLinkKind.None;

	public ElementPropertyLink(
		ElementPropertyLinkKind kind,
		string? description = null,
		ContainerLayoutBinding? containerLayout = null)
	{
		if (kind != ElementPropertyLinkKind.None
			&& string.IsNullOrWhiteSpace(description))
			throw new ArgumentException(
				"有效链接必须包含 DOM、CSS、布局或 XAML 描述。",
				nameof(description));
		if (kind == ElementPropertyLinkKind.ContainerLayout
			&& containerLayout is null)
			throw new ArgumentException(
				"ContainerLayout 链接必须包含强类型容器布局绑定。",
				nameof(containerLayout));
		if (kind != ElementPropertyLinkKind.ContainerLayout
			&& containerLayout is not null)
			throw new ArgumentException(
				"只有 ContainerLayout 链接可以携带容器布局绑定。",
				nameof(containerLayout));
		Kind = kind;
		Description = description ?? string.Empty;
		ContainerLayout = containerLayout;
	}

	public static ElementPropertyLink None => default;

	public static ElementPropertyLink FromContainerLayout(
		ContainerLayoutBinding binding)
	{
		ArgumentNullException.ThrowIfNull(binding);
		return new(
			ElementPropertyLinkKind.ContainerLayout,
			binding.SourceDescription,
			binding);
	}
}

public interface IElementSlotConsistencyOwner
{
	void ValidateSlotConsistency();
}

public abstract class ElementSlottedProperty<TValue> :
	IElementPropertyAudit<TValue>,
	IElementSlotConsistencyOwner
{
	private ElementPropertyValueSource _sourceValueSource;
	private ElementPropertyValueSource _sourceInitializationValueSource;
	private ElementPropertyValueSource _sourceLinkValueSource;
	private ElementPropertyValueSource _sourceRuntimeValueSource;
	private ElementPropertySlot<TValue> _sourceInitialization;
	private ElementPropertyLink _sourceLink;
	private ElementPropertySlot<TValue> _sourceRuntime;
	private ElementPropertySlot<TValue> _xamlInitialization;
	private ElementPropertyLink _xamlLink;
	private ElementPropertySlot<TValue> _xamlRuntime;
	private ElementPropertyValueSource _xamlValueSource;
	private ElementPropertyValueSource _xamlInitializationValueSource;
	private ElementPropertyValueSource _xamlLinkValueSource;
	private ElementPropertyValueSource _xamlRuntimeValueSource;

	public string Name { get; }
	public ElementSlotCategory Category { get; }
	public ElementPropertyValueSource SourceValueSource => _sourceValueSource;
	public ElementPropertyValueSource SourceInitializationValueSource =>
		_sourceInitializationValueSource;
	public ElementPropertyValueSource SourceLinkValueSource =>
		_sourceLinkValueSource;
	public ElementPropertyValueSource SourceRuntimeValueSource =>
		_sourceRuntimeValueSource;
	public ElementPropertySlot<TValue> SourceInitialization => _sourceInitialization;
	public ElementPropertyLink SourceLink => _sourceLink;
	public ElementPropertySlot<TValue> SourceRuntime => _sourceRuntime;
	public ElementPropertySlot<TValue> XamlInitialization => _xamlInitialization;
	public ElementPropertyLink XamlLink => _xamlLink;
	public ElementPropertySlot<TValue> XamlRuntime => _xamlRuntime;
	public ElementPropertyValueSource XamlValueSource => _xamlValueSource;
	public ElementPropertyValueSource XamlInitializationValueSource =>
		_xamlInitializationValueSource;
	public ElementPropertyValueSource XamlLinkValueSource =>
		_xamlLinkValueSource;
	public ElementPropertyValueSource XamlRuntimeValueSource =>
		_xamlRuntimeValueSource;
	public virtual bool RequiresAudit => true;

	private protected ElementSlottedProperty(
		string name,
		ElementSlotCategory category,
		ElementPropertyValueSource sourceValueSource,
		ElementPropertySlot<TValue> sourceInitialization,
		ElementPropertyLink sourceLink,
		ElementPropertySlot<TValue> sourceRuntime,
		ElementPropertySlot<TValue> xamlInitialization,
		ElementPropertyLink xamlLink,
		ElementPropertySlot<TValue> xamlRuntime,
		ElementPropertyValueSource xamlValueSource)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ValidateValueSource(sourceValueSource, sourceLink, nameof(sourceValueSource));
		ValidateValueSource(xamlValueSource, xamlLink, nameof(xamlValueSource));
		Name = name;
		Category = category;
		_sourceValueSource = sourceValueSource;
		_sourceInitializationValueSource =
			sourceInitialization.IsSet ? sourceValueSource : ElementPropertyValueSource.Unspecified;
		_sourceLinkValueSource =
			sourceLink.IsSet ? sourceValueSource : ElementPropertyValueSource.Unspecified;
		_sourceRuntimeValueSource =
			sourceRuntime.IsSet ? sourceValueSource : ElementPropertyValueSource.Unspecified;
		_sourceInitialization = sourceInitialization;
		_sourceLink = sourceLink;
		_sourceRuntime = sourceRuntime;
		_xamlInitialization = xamlInitialization;
		_xamlLink = xamlLink;
		_xamlRuntime = xamlRuntime;
		_xamlValueSource = xamlValueSource;
		_xamlInitializationValueSource =
			xamlInitialization.IsSet ? xamlValueSource : ElementPropertyValueSource.Unspecified;
		_xamlLinkValueSource =
			xamlLink.IsSet ? xamlValueSource : ElementPropertyValueSource.Unspecified;
		_xamlRuntimeValueSource =
			xamlRuntime.IsSet ? xamlValueSource : ElementPropertyValueSource.Unspecified;
	}

	public abstract ElementPropertyAuditResult<TValue> Audit();

	protected void SetSourceValueSource(ElementPropertyValueSource value) =>
		_sourceValueSource = value;

	protected void SetSourceInitialization(ElementPropertySlot<TValue> value) =>
		_sourceInitialization = value;

	protected void SetSourceInitialization(
		ElementPropertySlot<TValue> value,
		ElementPropertyValueSource valueSource)
	{
		_sourceInitialization = value;
		_sourceInitializationValueSource = valueSource;
		RecomputeSourceValueSource();
	}

	protected void ClearSourceInitialization()
	{
		_sourceInitialization = ElementPropertySlot<TValue>.Unset;
		_sourceInitializationValueSource = ElementPropertyValueSource.Unspecified;
		RecomputeSourceValueSource();
	}

	protected void SetSourceLink(ElementPropertyLink value) =>
		_sourceLink = value;

	protected void SetSourceLink(
		ElementPropertyLink value,
		ElementPropertyValueSource valueSource)
	{
		_sourceLink = value;
		_sourceLinkValueSource = valueSource;
		RecomputeSourceValueSource();
	}

	protected void ClearSourceLink()
	{
		_sourceLink = ElementPropertyLink.None;
		_sourceLinkValueSource = ElementPropertyValueSource.Unspecified;
		RecomputeSourceValueSource();
	}

	protected void SetSourceRuntime(ElementPropertySlot<TValue> value) =>
		_sourceRuntime = value;

	protected void SetSourceRuntime(
		ElementPropertySlot<TValue> value,
		ElementPropertyValueSource valueSource)
	{
		_sourceRuntime = value;
		_sourceRuntimeValueSource = valueSource;
		RecomputeSourceValueSource();
	}

	protected void ClearSourceRuntime()
	{
		_sourceRuntime = ElementPropertySlot<TValue>.Unset;
		_sourceRuntimeValueSource = ElementPropertyValueSource.Unspecified;
		RecomputeSourceValueSource();
	}

	protected void SetXamlInitialization(ElementPropertySlot<TValue> value) =>
		_xamlInitialization = value;

	protected void SetXamlInitialization(
		ElementPropertySlot<TValue> value,
		ElementPropertyValueSource valueSource)
	{
		_xamlInitialization = value;
		_xamlInitializationValueSource = valueSource;
		RecomputeXamlValueSource();
	}

	protected void ClearXamlInitialization()
	{
		_xamlInitialization = ElementPropertySlot<TValue>.Unset;
		_xamlInitializationValueSource = ElementPropertyValueSource.Unspecified;
		RecomputeXamlValueSource();
	}

	protected void SetXamlLink(ElementPropertyLink value) =>
		_xamlLink = value;

	protected void SetXamlLink(
		ElementPropertyLink value,
		ElementPropertyValueSource valueSource)
	{
		_xamlLink = value;
		_xamlLinkValueSource = valueSource;
		RecomputeXamlValueSource();
	}

	protected void ClearXamlLink()
	{
		_xamlLink = ElementPropertyLink.None;
		_xamlLinkValueSource = ElementPropertyValueSource.Unspecified;
		RecomputeXamlValueSource();
	}

	protected void SetXamlRuntime(ElementPropertySlot<TValue> value) =>
		_xamlRuntime = value;

	protected void SetXamlRuntime(
		ElementPropertySlot<TValue> value,
		ElementPropertyValueSource valueSource)
	{
		_xamlRuntime = value;
		_xamlRuntimeValueSource = valueSource;
		RecomputeXamlValueSource();
	}

	protected void ClearXamlRuntime()
	{
		_xamlRuntime = ElementPropertySlot<TValue>.Unset;
		_xamlRuntimeValueSource = ElementPropertyValueSource.Unspecified;
		RecomputeXamlValueSource();
	}

	protected void SetXamlValueSource(ElementPropertyValueSource value) =>
		_xamlValueSource = value;

	public void ValidateSlotConsistency()
	{
		ValidateSlotValueSource(
			_sourceInitialization.IsSet,
			_sourceInitializationValueSource,
			_sourceLink,
			"DOM initialization");
		ValidateSlotValueSource(
			_sourceRuntime.IsSet,
			_sourceRuntimeValueSource,
			_sourceLink,
			"DOM runtime");
		ValidateSlotValueSource(
			_xamlInitialization.IsSet,
			_xamlInitializationValueSource,
			_xamlLink,
			"XAML initialization");
		ValidateSlotValueSource(
			_xamlRuntime.IsSet,
			_xamlRuntimeValueSource,
			_xamlLink,
			"XAML runtime");
	}

	private void RecomputeSourceValueSource() =>
		_sourceValueSource = _sourceLink.IsSet
			? _sourceLinkValueSource
			: _sourceRuntime.IsSet
				? _sourceRuntimeValueSource
				: _sourceInitialization.IsSet
					? _sourceInitializationValueSource
					: ElementPropertyValueSource.Unspecified;

	private void RecomputeXamlValueSource() =>
		_xamlValueSource = _xamlLink.IsSet
			? _xamlLinkValueSource
			: _xamlRuntime.IsSet
				? _xamlRuntimeValueSource
				: _xamlInitialization.IsSet
					? _xamlInitializationValueSource
					: ElementPropertyValueSource.Unspecified;

	private static void ValidateSlotValueSource(
		bool isSet,
		ElementPropertyValueSource valueSource,
		ElementPropertyLink link,
		string slotName)
	{
		if (isSet && valueSource == ElementPropertyValueSource.Unspecified)
			throw new InvalidOperationException($"{slotName} has no value source.");
		if (!isSet && valueSource != ElementPropertyValueSource.Unspecified)
			throw new InvalidOperationException($"{slotName} has a value source but no value.");
		var requiresLink = valueSource is
			ElementPropertyValueSource.ContainerAutomaticLayout
				or ElementPropertyValueSource.LinkedCalculation
				or ElementPropertyValueSource.LinkedConstant;
		if (isSet && requiresLink && !link.IsSet)
			throw new InvalidOperationException($"{slotName} requires a link.");
	}

	ElementPropertyAuditResultBase IElementPropertyAudit.Audit() =>
		Audit();

	private static void ValidateValueSource(
		ElementPropertyValueSource valueSource,
		ElementPropertyLink link,
		string parameterName)
	{
		var requiresLink = valueSource is
			ElementPropertyValueSource.ContainerAutomaticLayout
			or ElementPropertyValueSource.LinkedCalculation
			or ElementPropertyValueSource.LinkedConstant;
		if (requiresLink != link.IsSet)
			throw new ArgumentException(
				requiresLink
					? $"{valueSource} 必须设置 SourceLink。"
					: $"{valueSource} 不能设置 SourceLink。",
				parameterName);
		if (valueSource == ElementPropertyValueSource.ContainerAutomaticLayout
			&& link.Kind != ElementPropertyLinkKind.ContainerLayout)
			throw new ArgumentException(
				"ContainerAutomaticLayout 必须使用强类型 ContainerLayout 链接。",
				parameterName);
		if (valueSource == ElementPropertyValueSource.LinkedConstant
			&& link.Kind != ElementPropertyLinkKind.ConstantReference)
			throw new ArgumentException(
				"LinkedConstant 必须使用 ConstantReference 链接。",
				parameterName);
	}
}

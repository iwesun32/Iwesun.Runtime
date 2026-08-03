namespace Iwesun.Runtime.Web;

public enum XamlPropertyExecutionKind
{
	Unsupported,
	GlobalService,
	RuntimeGeometry,
	FrameworkDimension,
	FrameworkSpacing,
	ContainerLayout,
	Visibility,
	Appearance,
	TextAppearance,
	ShapeAppearance,
	Overflow,
	Interaction,
	Transform,
	Transition,
	Content,
	State,
	Resource,
	Effect,
	SemanticStyle,
	SemanticMetadata,
	Event
}

public enum DomXamlSynchronizationDirection
{
	DomToXaml,
	XamlToDom,
	TwoWay
}

public enum ElementSlotOwnerKind
{
	Property,
	Attribute,
	ExtensionAttribute,
	RuntimeProperty,
	DataSource,
	Event
}

public sealed record ElementSlotTraversalDescriptor(
	string DocumentScope,
	string XPath,
	string TagName,
	string ReflectedPropertyName,
	string PropertyName,
	ElementSlotOwnerKind OwnerKind,
	ElementSlotCategory Category,
	ElementEvidenceKind EvidenceKind,
	ElementPropertyTraits Traits)
{
	public string ElementIdentity =>
		$"{DocumentScope}::{XPath}";

	public string StrongPropertyIdentity =>
		$"{ReflectedPropertyName}/{OwnerKind}/{PropertyName}";

	public void Validate()
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(DocumentScope);
		ArgumentException.ThrowIfNullOrWhiteSpace(XPath);
		ArgumentException.ThrowIfNullOrWhiteSpace(TagName);
		ArgumentException.ThrowIfNullOrWhiteSpace(ReflectedPropertyName);
		ArgumentException.ThrowIfNullOrWhiteSpace(PropertyName);
		if (Category == ElementSlotCategory.Unspecified)
			throw new InvalidOperationException(
				$"{ElementIdentity}/{StrongPropertyIdentity} has no category.");
		if (EvidenceKind == ElementEvidenceKind.None)
			throw new InvalidOperationException(
				$"{ElementIdentity}/{StrongPropertyIdentity} has no evidence route.");
	}
}

public sealed record XamlPropertyExecutionDescriptor(
	string SourcePropertyName,
	string TargetProperty,
	XamlPropertyExecutionKind Kind,
	bool SupportsInitialization,
	bool SupportsLink,
	bool SupportsRuntime)
{
	public bool IsSupported => Kind != XamlPropertyExecutionKind.Unsupported;

	public DomXamlSynchronizationDirection SynchronizationDirection { get; init; } =
		DomXamlSynchronizationDirection.DomToXaml;

	public bool TargetCanMutateLocally { get; init; }

	public bool SupportsDomWriteBack => SynchronizationDirection is
		DomXamlSynchronizationDirection.XamlToDom
		or DomXamlSynchronizationDirection.TwoWay;

	public string MarkupAttributeName { get; init; } = string.Empty;
}

public sealed record XamlPropertyQueryContext(
	string DocumentScope,
	string XPath,
	string TagName,
	DomElement Element,
	object? XamlElement,
	XamlElementMappingDecision ElementMapping,
	string ReflectedPropertyName,
	string PropertyName,
	ElementSlotOwnerKind OwnerKind,
	ElementSlotCategory Category,
	ElementEvidenceKind EvidenceKind,
	XamlPropertyDataSlot Slot,
	XamlPropertyExecutionDescriptor Execution,
	XamlSourcePropertyEvidence Source,
	XamlTargetPropertyEvidence Target)
{
	public IXamlFillSlotOwner? SlotOwner { get; init; }

	public ElementSlotTraversalDescriptor Traversal =>
		new(
			DocumentScope,
			XPath,
			TagName,
			ReflectedPropertyName,
			PropertyName,
			OwnerKind,
			Category,
			EvidenceKind,
			ElementPropertyTraitsReflector.GetAttribute(
				Element.GetType(),
				ReflectedPropertyName));
}

public sealed record XamlSourcePropertyEvidence(
	ElementPropertySlot<string> Initialization,
	ElementPropertyLink Link,
	ElementPropertySlot<string> Runtime)
{
	public static XamlSourcePropertyEvidence Empty { get; } =
		new(
			ElementPropertySlot<string>.Unset,
			ElementPropertyLink.None,
			ElementPropertySlot<string>.Unset);
}

public sealed record XamlTargetPropertyEvidence(
	ElementPropertySlot<string> Initialization,
	ElementPropertyLink Link,
	ElementPropertySlot<string> Runtime)
{
	public static XamlTargetPropertyEvidence Empty { get; } =
		new(
			ElementPropertySlot<string>.Unset,
			ElementPropertyLink.None,
			ElementPropertySlot<string>.Unset);
}

public interface IXamlPropertyQueryEngine
{
	ValueTask<XamlPropertyQueryResult> QueryAsync(
		XamlPropertyQueryContext context,
		CancellationToken cancellationToken = default);
}

public enum DomElementQuerySpecialization
{
	General,
	Layout,
	Text,
	Interactive,
	FormControl,
	Media,
	Metadata,
	Svg
}

/// <summary>
/// DOM 属性查询上下文，传递给 IDomPropertyQueryEngine。
/// </summary>
public sealed record DomPropertyQueryContext(
	string DocumentScope,
	string XPath,
	string TagName,
	string ReflectedPropertyName,
	string PropertyName,
	ElementSlotOwnerKind OwnerKind,
	ElementSlotCategory Category,
	ElementEvidenceKind EvidenceKind,
	DomPropertyDataSlot Slot,
	DomElement Element)
{
	public DomElementQuerySpecialization Specialization =>
		Element.QuerySpecialization;

	public ElementSlotTraversalDescriptor Traversal =>
		new(
			DocumentScope,
			XPath,
			TagName,
			ReflectedPropertyName,
			PropertyName,
			OwnerKind,
			Category,
			EvidenceKind,
			ElementPropertyTraitsReflector.GetAttribute(
				Element.GetType(),
				ReflectedPropertyName));
}

/// <summary>
/// DOM 属性查询引擎接口，用于从 WebView2 实时 DOM 中查询属性值。
/// 消费项目（如 Iwesun.Runtime.WebView2）实现此接口，直接调用 WebView2 API。
/// 每个元素可以设置自己的引擎，子类也可以通过重写 CreateDefaultDomQueryDelegate 提供。
/// </summary>
public interface IDomPropertyQueryEngine
{
	/// <summary>
	/// 查询指定元素的属性值。
	/// </summary>
	/// <param name="context">查询上下文，包含元素身份、属性名、槽位类型</param>
	/// <param name="cancellationToken">取消令牌</param>
	/// <returns>查询结果，包含值、来源、链接描述</returns>
	ValueTask<DomPropertyQueryResult> QueryAsync(
		DomPropertyQueryContext context,
		CancellationToken cancellationToken = default);
}

public interface IXamlPropertyExecutionOwner
{
	XamlPropertyExecutionDescriptor XamlExecution { get; }

	ElementPropertySlot<string> SourceInitialization { get; }

	ElementPropertyLink SourceLink { get; }

	ElementPropertySlot<string> SourceRuntime { get; }

	ElementPropertySlot<string> TargetInitialization { get; }

	ElementPropertyLink TargetLink { get; }

	ElementPropertySlot<string> TargetRuntime { get; }
}

public static class DomXamlPropertyExecutionCatalog
{
	public static XamlPropertyExecutionDescriptor ResolveHtmlAttribute(
		string propertyName,
		HtmlAttributeDefinition definition)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
		ArgumentNullException.ThrowIfNull(definition);
		var native = Resolve(propertyName);
		if (native.IsSupported)
			return native;
		var descriptor = Descriptor(
			propertyName,
			$"HtmlAttributeState.{ToPascalCase(propertyName)}",
			XamlPropertyExecutionKind.SemanticMetadata,
			initialization: true,
			link: true,
			runtime: true);
		return IsLocallyMutableHtmlState(propertyName)
			? descriptor with { TargetCanMutateLocally = true }
			: descriptor;
	}

	public static XamlPropertyExecutionDescriptor ResolveSvgAttribute(
		string propertyName,
		SvgAttributeDefinition definition)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
		ArgumentNullException.ThrowIfNull(definition);
		var native = Resolve(propertyName);
		if (native.IsSupported)
			return native;
		return Descriptor(
			propertyName,
			$"SvgAttributeState.{ToPascalCase(propertyName)}",
			XamlPropertyExecutionKind.SemanticMetadata,
			initialization: true,
			link: true,
			runtime: true);
	}

	public static XamlPropertyExecutionDescriptor Resolve(
		string propertyName,
		XamlControlDataTargetKind targetKind)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
		var target = targetKind switch
		{
			XamlControlDataTargetKind.DataContext => "FrameworkElement.DataContext",
			XamlControlDataTargetKind.Content => "ContentControl.Content",
			XamlControlDataTargetKind.Text => "TextBlock.Text",
			XamlControlDataTargetKind.ItemsSource => "ItemsControl.ItemsSource",
			XamlControlDataTargetKind.SelectedItem => "Selector.SelectedItem",
			XamlControlDataTargetKind.SelectedValue => "Selector.SelectedValue",
			XamlControlDataTargetKind.Value => "RangeBase.Value",
			XamlControlDataTargetKind.IsChecked => "ToggleButton.IsChecked",
			XamlControlDataTargetKind.IsIndeterminate => "ToggleButton.IsChecked",
			XamlControlDataTargetKind.IsEnabled => "Control.IsEnabled",
			XamlControlDataTargetKind.IsReadOnly => "TextBox.IsReadOnly",
			XamlControlDataTargetKind.TabIndex => "Control.TabIndex",
			XamlControlDataTargetKind.IsValid => "HtmlValidation.IsValid",
			XamlControlDataTargetKind.WillValidate => "HtmlValidation.WillValidate",
			XamlControlDataTargetKind.ValidationMessage => "HtmlValidation.ValidationMessage",
			XamlControlDataTargetKind.Source => "Image.Source",
			XamlControlDataTargetKind.NavigateUri => "HyperlinkButton.NavigateUri",
			XamlControlDataTargetKind.CommandParameter => "ButtonBase.CommandParameter",
			XamlControlDataTargetKind.PlaceholderText => "TextBox.PlaceholderText",
			XamlControlDataTargetKind.Header => "ContentControl.Header",
			_ => string.Empty
		};
		if (target.Length == 0)
			return Resolve(propertyName);
		var kind = targetKind switch
		{
			XamlControlDataTargetKind.IsChecked
				or XamlControlDataTargetKind.IsIndeterminate
				or XamlControlDataTargetKind.IsEnabled
				or XamlControlDataTargetKind.IsReadOnly
				or XamlControlDataTargetKind.TabIndex
				or XamlControlDataTargetKind.IsValid
				or XamlControlDataTargetKind.WillValidate
				or XamlControlDataTargetKind.ValidationMessage
				or XamlControlDataTargetKind.SelectedItem
				or XamlControlDataTargetKind.SelectedValue
				or XamlControlDataTargetKind.Value =>
				XamlPropertyExecutionKind.State,
			XamlControlDataTargetKind.Source
				or XamlControlDataTargetKind.NavigateUri =>
				XamlPropertyExecutionKind.Resource,
			_ => XamlPropertyExecutionKind.Content
		};
		var descriptor = Descriptor(
			propertyName,
			target,
			kind,
			initialization: true,
			link: true,
			runtime: true) with
		{
			MarkupAttributeName = target[(target.LastIndexOf('.') + 1)..],
			TargetCanMutateLocally = targetKind is
				XamlControlDataTargetKind.Text
				or XamlControlDataTargetKind.SelectedItem
				or XamlControlDataTargetKind.SelectedValue
				or XamlControlDataTargetKind.Value
				or XamlControlDataTargetKind.IsChecked
				or XamlControlDataTargetKind.IsIndeterminate
				or XamlControlDataTargetKind.IsEnabled
				or XamlControlDataTargetKind.IsReadOnly
				or XamlControlDataTargetKind.TabIndex
				or XamlControlDataTargetKind.IsValid
				or XamlControlDataTargetKind.WillValidate
				or XamlControlDataTargetKind.ValidationMessage
		};
		return descriptor;
	}

	public static XamlPropertyExecutionDescriptor Resolve(string propertyName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
		if (propertyName is "service.globalLayout" or "service.globalStyle")
			return Descriptor(
				propertyName,
				propertyName == "service.globalLayout"
					? "CSharp.LayoutService"
					: "CSharp.StyleService",
				XamlPropertyExecutionKind.GlobalService,
				initialization: false,
				link: true,
				runtime: false);
		if (propertyName.StartsWith("rect.", StringComparison.Ordinal))
			return Descriptor(
				propertyName,
				"FrameworkElement.ActualBounds",
				XamlPropertyExecutionKind.RuntimeGeometry,
				initialization: false,
				link: true,
				runtime: true);
		if (propertyName.StartsWith("style.", StringComparison.Ordinal))
			return ResolveCss(propertyName, ToCssProperty(propertyName["style.".Length..]));
		if (propertyName.StartsWith("content.", StringComparison.Ordinal))
			return Descriptor(
				propertyName,
				$"HtmlContentState.{propertyName["content.".Length..]}",
				XamlPropertyExecutionKind.Content,
				true,
				true,
				true);
		if (propertyName is "id")
			return Descriptor(
				propertyName,
				"AutomationProperties.AutomationId",
				XamlPropertyExecutionKind.Content,
				initialization: true,
				link: false,
				runtime: true) with
			{
				MarkupAttributeName = "AutomationProperties.AutomationId"
			};
		if (propertyName is "tabindex")
			return Descriptor(
				propertyName,
				"HtmlElement.TabIndex",
				XamlPropertyExecutionKind.Interaction,
				initialization: true,
				link: false,
				runtime: true) with
			{
				MarkupAttributeName = "TabIndex"
			};
		if (propertyName is "src")
			return Descriptor(
				propertyName,
				"FrameworkElement.Source",
				XamlPropertyExecutionKind.Resource,
				initialization: true,
				link: false,
				runtime: true) with
			{
				MarkupAttributeName = "Source"
			};
		if (propertyName.StartsWith("state.", StringComparison.Ordinal))
			return Descriptor(
				propertyName,
				$"HtmlControlState.{propertyName["state.".Length..]}",
				XamlPropertyExecutionKind.State,
				true,
				true,
				true) with
			{
				TargetCanMutateLocally = true
			};
		if (propertyName.StartsWith("resource.", StringComparison.Ordinal))
			return Descriptor(
				propertyName,
				"FrameworkElement.Resource",
				XamlPropertyExecutionKind.Resource,
				true,
				true,
				true);
		if (propertyName.StartsWith("event.", StringComparison.Ordinal))
			return Descriptor(
				propertyName,
				"UIElement.RoutedEvent",
				XamlPropertyExecutionKind.Event,
				true,
				true,
				true);
		if (propertyName.StartsWith("effect.", StringComparison.Ordinal))
			return Descriptor(
				propertyName,
				$"HtmlEffectState.{propertyName["effect.".Length..]}",
				XamlPropertyExecutionKind.Effect,
				true,
				true,
				true);
		return Descriptor(
			propertyName,
			string.Empty,
			XamlPropertyExecutionKind.Unsupported,
			false,
			false,
			false);
	}

	private static XamlPropertyExecutionDescriptor ResolveCss(
		string sourcePropertyName,
		string cssProperty)
	{
		var (target, kind) = cssProperty switch
		{
			"width" => ("FrameworkElement.Width", XamlPropertyExecutionKind.FrameworkDimension),
			"height" => ("FrameworkElement.Height", XamlPropertyExecutionKind.FrameworkDimension),
			"min-width" => ("FrameworkElement.MinWidth", XamlPropertyExecutionKind.FrameworkDimension),
			"max-width" => ("FrameworkElement.MaxWidth", XamlPropertyExecutionKind.FrameworkDimension),
			"min-height" => ("FrameworkElement.MinHeight", XamlPropertyExecutionKind.FrameworkDimension),
			"max-height" => ("FrameworkElement.MaxHeight", XamlPropertyExecutionKind.FrameworkDimension),
			"left" or "top" or "right" or "bottom" or "inset"
				or "margin-left" or "margin-top" or "margin-right" or "margin-bottom"
				or "padding-left" or "padding-top" or "padding-right" or "padding-bottom"
				=> ("FrameworkElement.Spacing", XamlPropertyExecutionKind.FrameworkSpacing),
			"box-sizing" or "position"
				or "flex" or "flex-grow" or "flex-shrink" or "flex-basis"
				or "flex-direction" or "flex-wrap" or "order"
				or "justify-content" or "align-content" or "align-items" or "align-self"
				or "gap" or "row-gap" or "column-gap"
				or "grid-template-columns" or "grid-template-rows"
				or "grid-auto-flow" or "grid-auto-columns" or "grid-auto-rows"
				=> ("Panel.Layout", XamlPropertyExecutionKind.ContainerLayout),
			"display" or "visibility" or "opacity" or "z-index"
				=> ("UIElement.Visibility", XamlPropertyExecutionKind.Visibility),
			"background-color" or "color"
				or "border-top-left-radius" or "border-top-right-radius"
				or "border-bottom-right-radius" or "border-bottom-left-radius"
				or "border-top-width" or "border-right-width"
				or "border-bottom-width" or "border-left-width"
				or "border-top-color" or "border-right-color"
				or "border-bottom-color" or "border-left-color"
				=> ("Control.Appearance", XamlPropertyExecutionKind.Appearance),
			"font-size" or "font-weight" or "font-style" or "font-family"
				or "font-stretch"
				or "line-height" or "letter-spacing" or "text-align"
				or "text-decoration-line" or "text-overflow" or "white-space"
				or "direction"
				=> ("TextElement.Appearance", XamlPropertyExecutionKind.TextAppearance),
			"fill" or "stroke" or "stroke-width"
				or "stroke-linecap" or "stroke-linejoin"
				=> ("Shape.Appearance", XamlPropertyExecutionKind.ShapeAppearance),
			"overflow-x" or "overflow-y"
				=> ("ScrollViewer.Overflow", XamlPropertyExecutionKind.Overflow),
			"pointer-events" or "object-fit"
				=> ("UIElement.Interaction", XamlPropertyExecutionKind.Interaction),
			"transform" or "transform-origin"
				=> ("UIElement.RenderTransform", XamlPropertyExecutionKind.Transform),
			"transition-property" or "transition-duration"
				or "transition-delay" or "transition-timing-function"
				=> ("UIElement.Transition", XamlPropertyExecutionKind.Transition),
			_ when DomElementRuntimePropertyCatalog.Contains(sourcePropertyName)
				=> ($"HtmlStyle.{ToPascalCase(cssProperty)}",
					XamlPropertyExecutionKind.SemanticStyle),
			_ => (string.Empty, XamlPropertyExecutionKind.Unsupported)
		};
		var descriptor = Descriptor(
			sourcePropertyName,
			target,
			kind,
			initialization: kind != XamlPropertyExecutionKind.Unsupported,
			link: kind != XamlPropertyExecutionKind.Unsupported,
			runtime: kind != XamlPropertyExecutionKind.Unsupported);
		return descriptor with
		{
			MarkupAttributeName = cssProperty switch
			{
				"width" => "Width",
				"height" => "Height",
				"min-width" => "MinWidth",
				"max-width" => "MaxWidth",
				"min-height" => "MinHeight",
				"max-height" => "MaxHeight",
				"box-shadow" => "HtmlBoxShadow.Value",
				"filter" => "HtmlFilter.Filter",
				"backdrop-filter" => "HtmlBackdropFilter.Filter",
				"text-transform" => "HtmlTextTransform.Rule",
				"text-shadow" => "HtmlTextShadow.Value",
				"content-visibility" => "HtmlContentVisibility.Rule",
				"clip-path" => "HtmlClipPath.Rule",
				"color-scheme" => "HtmlColorScheme.Rule",
				"transition-property" => "HtmlTransition.Property",
				"transition-duration" => "HtmlTransition.Duration",
				"transition-delay" => "HtmlTransition.Delay",
				"transition-timing-function" => "HtmlTransition.TimingFunction",
				"transform" => "HtmlTransform.Value",
				"transform-origin" => "HtmlTransform.Origin",
				"left" => "HtmlPosition.Left",
				"top" => "HtmlPosition.Top",
				"right" => "HtmlPosition.Right",
				"bottom" => "HtmlPosition.Bottom",
				_ => string.Empty
			}
		};
	}

	private static XamlPropertyExecutionDescriptor Descriptor(
		string sourcePropertyName,
		string targetProperty,
		XamlPropertyExecutionKind kind,
		bool initialization,
		bool link,
		bool runtime) =>
		new(
			sourcePropertyName,
			targetProperty,
			kind,
			initialization,
			link,
			runtime);

	private static bool IsLocallyMutableHtmlState(string propertyName) =>
		propertyName is "open" or "checked" or "selected" or "value";

	private static string ToCssProperty(string propertyName)
	{
		var builder = new System.Text.StringBuilder(propertyName.Length + 8);
		foreach (var character in propertyName)
		{
			if (char.IsUpper(character))
			{
				builder.Append('-');
				builder.Append(char.ToLowerInvariant(character));
			}
			else
				builder.Append(character);
		}
		return builder.ToString();
	}

	private static string ToPascalCase(string cssProperty)
	{
		var builder = new System.Text.StringBuilder(cssProperty.Length);
		var upper = true;
		foreach (var character in cssProperty)
		{
			if (character == '-')
			{
				upper = true;
				continue;
			}
			builder.Append(upper
				? char.ToUpperInvariant(character)
				: character);
			upper = false;
		}
		return builder.ToString();
	}
}

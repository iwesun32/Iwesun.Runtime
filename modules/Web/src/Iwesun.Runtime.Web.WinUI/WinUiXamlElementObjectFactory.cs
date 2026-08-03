using System.Globalization;
using System.Text.RegularExpressions;
using Iwesun.Runtime.Diagnostics;
using Iwesun.Runtime.Web;
using Iwesun.Runtime.WebView2;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Media.Core;
using Windows.UI;
using FontWeight = Windows.UI.Text.FontWeight;

namespace Iwesun.Runtime.Web.WinUI;

public sealed partial class WinUiXamlElementObjectFactory
	: IXamlElementObjectFactory, IXamlGlobalRelationshipBinder
{
	private readonly HtmlRuntimeStyleManager? _styles;
	private readonly HtmlRuntimeLayoutManager? _layout;
	private readonly HtmlRuntimeDesignRuntime? _designRuntime;
	private readonly WinUiEventRuntimeEvidenceRegistry? _eventEvidenceRegistry;
	private readonly IReadOnlyDictionary<
		(string DocumentScope, string XPath),
		IReadOnlyList<HtmlRuntimeStyleBinding>> _semanticStyleBindings;
	private readonly Dictionary<IXamlPropertySlotOwner, DependencyObject>
		_materializedPropertyTargets = new();

	internal IReadOnlyDictionary<IXamlPropertySlotOwner, DependencyObject>
		MaterializedPropertyTargets => _materializedPropertyTargets;

	internal WinUiXamlElementObjectFactory(
		HtmlRuntimeDesignRuntime? designRuntime = null,
		WinUiEventRuntimeEvidenceRegistry? eventEvidenceRegistry = null)
	{
		_designRuntime = designRuntime;
		_eventEvidenceRegistry = eventEvidenceRegistry;
		_styles = designRuntime?.Styles;
		_layout = designRuntime?.Layout;
		_semanticStyleBindings = designRuntime?.Styles.Bindings
			.GroupBy(
				static binding => (
					binding.DocumentScope,
					binding.XPath))
			.ToDictionary(
				static group => group.Key,
				static group =>
					(IReadOnlyList<HtmlRuntimeStyleBinding>)group.ToArray())
			?? new Dictionary<
				(string DocumentScope, string XPath),
				IReadOnlyList<HtmlRuntimeStyleBinding>>();
	}

	public object CreateSyntheticElement(
		XamlElementObjectCreationContext context)
	{
		ArgumentNullException.ThrowIfNull(context);
		if (context.Plan.SourceElement is not null)
		{
			throw new InvalidOperationException(
				"A DOM-backed XAML object must use its concrete strong dispatch overload.");
		}
		return CreateSyntheticElement(context.Plan);
	}

	private static bool TryReadActiveString(
		DomElement source,
		string propertyName,
		out string value)
	{
		value = source.HtmlRoot?.ResolveGlobalStyleValue(
			source,
			propertyName,
			DomPropertyDataSlot.Runtime)
			?? source.HtmlRoot?.ResolveGlobalStyleValue(
				source,
				propertyName,
				DomPropertyDataSlot.Initialization)
			?? string.Empty;
		if (value.Length != 0)
			return true;
		return source.TryGetDomStringSlotValue(
				propertyName, DomPropertyDataSlot.Runtime, out value)
			|| source.TryGetDomStringSlotValue(
				propertyName, DomPropertyDataSlot.Initialization, out value);
	}

	private static bool IsVerticalWritingMode(DomElement source)
	{
		var found = TryReadWritingMode(
			source,
			DomPropertyDataSlot.Runtime,
			out var value)
			|| TryReadWritingMode(
				source,
				DomPropertyDataSlot.Initialization,
				out value);
		return found && value.Trim().StartsWith(
			"vertical-",
			StringComparison.OrdinalIgnoreCase);
	}

	private static bool TryReadWritingMode(
		DomElement source,
		DomPropertyDataSlot slot,
		out string value)
	{
		value = source.HtmlRoot?.ResolveGlobalStyleValue(
			source,
			"style.writingMode",
			slot) ?? string.Empty;
		if (value.Length != 0)
			return true;
		return source.TryGetDomStringSlotValue(
				"style.writingMode", slot, out value)
			|| source.TryGetDomStringSlotValue(
				"writingMode", slot, out value);
	}

	public void FillElementProperties(
		XamlElementObjectPropertyFillContext context)
	{
		ArgumentNullException.ThrowIfNull(context);
		if (context.Element is not DependencyObject element)
		{
			throw new InvalidOperationException(
				$"Cannot fill XAML properties on "
					+ $"{context.Element.GetType().FullName}.");
		}
		var visualTarget = element is HtmlFilteredElementHost filtered
			? filtered.InnerElement
			: element;
		// CSS layout preserves fractional coordinates. WinUI's default layout
		// rounding can move a valid half-DIP CSS edge by one physical pixel at
		// 200% scaling, so every DOM-backed visual must retain subpixel layout.
		if (element is FrameworkElement frameworkElement)
			frameworkElement.UseLayoutRounding = false;
		if (visualTarget is FrameworkElement propertyFrameworkElement)
			propertyFrameworkElement.UseLayoutRounding = false;
		foreach (var attribute in context.Plan.InitializationAttributes)
		{
			var attributeTarget = ResolveStrongAttributeTarget(
				element,
				visualTarget,
				attribute.Name);
			if (TryApplyCompositeStyleAttribute(
					attributeTarget,
					attribute,
					context.Plan))
			{
				RegisterMaterializedPropertyTargets(attribute, attributeTarget);
				continue;
			}
			var resolution = ResolveStyle(attribute, context.Plan);
			var effective = resolution is not null
				&& (ContainsUnresolvedCssExpression(attribute.Value)
					|| attribute.Value.Equals(
						resolution.Expression,
						StringComparison.Ordinal))
					? attribute with { Value = resolution.ConcreteValue }
					: attribute;
			if (ContainsUnresolvedCssExpression(effective.Value))
			{
				var source = context.Plan.SourceElement
					?? context.Plan.OwnerElement;
				throw new InvalidDataException(
					$"CSS expression '{effective.Value}' for "
					+ $"{element.GetType().Name}.{attribute.Name} was not "
					+ "resolved to a concrete runtime value. "
					+ $"source={source?.DocumentScope}:{source?.XPath}; "
					+ $"property={attribute.Source?.Name ?? "<none>"}; "
					+ $"bindingExpression="
					+ $"{resolution?.Expression ?? "<none>"}; "
					+ $"bindingConcrete="
					+ $"{resolution?.ConcreteValue ?? "<none>"}; "
					+ $"dataPoint={resolution?.DataPointId ?? "<none>"}.");
			}
#if DEBUG
			if (ContainsUnresolvedCssExpression(effective.Value))
			{
				var source = context.Plan.SourceElement
					?? context.Plan.OwnerElement;
				RuntimeInjector.Watch(
				"runtime.web.winui.xaml.style-resolution",
					new
					{
						source?.DocumentScope,
						source?.XPath,
						TargetType = element.GetType().FullName,
						TargetProperty = attribute.Name,
						SourceProperty = attribute.Source?.Name,
						attribute.Value,
						ResolutionExpression = resolution?.Expression,
						ResolutionConcreteValue = resolution?.ConcreteValue,
						ResolutionDataPoint = resolution?.DataPointId
					},
					"UnresolvedCssStyleAssignment");
			}
#endif
			ApplyAttribute(attributeTarget, effective, context.Plan);
			RegisterMaterializedPropertyTargets(attribute, attributeTarget);
			if (resolution is not null
				&& !IsLayoutPlanDerivedAttribute(attribute.Name))
			{
				_styles!.RegisterXamlTarget(
					resolution,
					attributeTarget,
					attribute.Name);
			}
		}
		ApplyStrongLayoutPlacement(
			element,
			context.Plan);
		AttachSemanticStyleState(context.Plan, element);
		if ((context.Plan.SourceElement ?? context.Plan.OwnerElement)
			is { } semanticSource)
		{
			WinUiTargetSemanticProperties.Materialize(
				element,
				semanticSource);
		}
		RegisterLayoutTarget(context.Plan, element);
		RegisterAnimationTarget(context.Plan, element);
	}

	private void RegisterMaterializedPropertyTargets(
		GeneratedXamlAttribute attribute,
		DependencyObject target)
	{
		foreach (var source in attribute.Sources)
		{
			if (_materializedPropertyTargets.TryGetValue(source, out var existing)
				&& !ReferenceEquals(existing, target))
			{
				throw new InvalidDataException(
					$"XAML slot owner '{source.Name}' was materialized on both "
						+ $"{existing.GetType().Name} and {target.GetType().Name}.");
			}
			_materializedPropertyTargets[source] = target;
		}
	}

	private static DependencyObject ResolveStrongAttributeTarget(
		DependencyObject layoutCarrier,
		DependencyObject visualTarget,
		string attributeName)
	{
		if (ReferenceEquals(layoutCarrier, visualTarget))
			return visualTarget;
		return IsFilteredHostLayoutCarrierAttribute(attributeName)
			? layoutCarrier
			: visualTarget;
	}

	internal static bool IsFilteredHostLayoutCarrierAttribute(string name) =>
		name is
			"Grid.Row"
			or "Grid.Column"
			or "Grid.RowSpan"
			or "Grid.ColumnSpan"
			or "Canvas.Left"
			or "Canvas.Top"
			or "Canvas.ZIndex"
			or "HtmlTable.ColumnSpan"
			or "HtmlTable.RowSpan"
			or "HtmlCssBoxGrid.FlexGrow"
			or "HtmlCssBoxGrid.FlexShrink"
			or "HtmlCssBoxGrid.FlexBasis"
			or "HtmlCssBoxGrid.AlignSelf"
			or "HtmlCssBoxGrid.Order"
			or "HtmlCssBoxGrid.GridRowExpression"
			or "HtmlCssBoxGrid.GridColumnExpression"
			or "HtmlCssBoxGrid.GridAreaExpression"
			or "Width"
			or "Height"
			or "MinWidth"
			or "MinHeight"
			or "MaxWidth"
			or "MaxHeight"
			or "Margin"
			or "HorizontalAlignment"
			or "VerticalAlignment"
			or "Visibility"
			or "Opacity"
			or "IsHitTestVisible"
			or "HtmlPosition.Left"
			or "HtmlPosition.Top"
			or "HtmlPosition.Right"
			or "HtmlPosition.Bottom"
			or "HtmlTransform.Value"
			or "HtmlTransform.Origin"
			or "HtmlContentVisibility.Rule";

	private static bool IsLayoutPlanDerivedAttribute(string name) =>
		name is
			"Grid.Row"
			or "Grid.Column"
			or "Grid.RowSpan"
			or "Grid.ColumnSpan";

	private void ApplyStrongLayoutPlacement(
		DependencyObject target,
		XamlElementObjectPlan plan)
	{
		var placement = plan.LayoutPlacement;
		if (target is not FrameworkElement element || placement is null)
		{
			return;
		}
		if (placement.IsOutOfFlow)
		{
			ApplyOutOfFlowPlacement(element, placement, plan);
			return;
		}
		ApplyMainAxisOffset(element, placement);
		if (placement.CrossAxis == XamlElementCrossAxis.Horizontal)
		{
			element.HorizontalAlignment = placement.CrossAlignment switch
			{
				XamlElementCrossAlignment.Center => HorizontalAlignment.Center,
				XamlElementCrossAlignment.Far => HorizontalAlignment.Right,
				XamlElementCrossAlignment.Stretch => HorizontalAlignment.Stretch,
				_ => HorizontalAlignment.Left
			};
			return;
		}
		element.VerticalAlignment = placement.CrossAlignment switch
		{
			XamlElementCrossAlignment.Center => VerticalAlignment.Center,
			XamlElementCrossAlignment.Far => VerticalAlignment.Bottom,
			XamlElementCrossAlignment.Stretch => VerticalAlignment.Stretch,
			_ => VerticalAlignment.Top
		};
	}

	private void ApplyMainAxisOffset(
		FrameworkElement element,
		XamlElementLayoutPlacement placement)
	{
		if (placement.MainAxisOffset is null)
			return;
		var mainAxisIsVertical =
			placement.CrossAxis == XamlElementCrossAxis.Horizontal;
		var offset = MaterializeInset(
			placement.MainAxisOffset,
			mainAxisIsVertical);
		var translation = element.Translation;
		element.Translation = mainAxisIsVertical
			? new(translation.X, translation.Y + (float)offset, translation.Z)
			: new(translation.X + (float)offset, translation.Y, translation.Z);
	}

	private void ApplyOutOfFlowPlacement(
		FrameworkElement element,
		XamlElementLayoutPlacement placement,
		XamlElementObjectPlan plan)
	{
		_ = plan.SourceElement ?? plan.OwnerElement
			?? throw new InvalidDataException(
				"An out-of-flow DOM plan has no source element.");
		var hasLeft = placement.Left is not null;
		var hasRight = placement.Right is not null;
		var hasTop = placement.Top is not null;
		var hasBottom = placement.Bottom is not null;
		var left = MaterializeInset(placement.Left, vertical: false);
		var right = MaterializeInset(placement.Right, vertical: false);
		var top = MaterializeInset(placement.Top, vertical: true);
		var bottom = MaterializeInset(placement.Bottom, vertical: true);

		element.HorizontalAlignment = hasLeft && hasRight
			&& !placement.HasDefiniteWidth
				? HorizontalAlignment.Stretch
				: hasRight && !hasLeft
					? HorizontalAlignment.Right
					: HorizontalAlignment.Left;
		element.VerticalAlignment = hasTop && hasBottom
			&& !placement.HasDefiniteHeight
				? VerticalAlignment.Stretch
				: hasBottom && !hasTop
					? VerticalAlignment.Bottom
					: VerticalAlignment.Top;
		if (placement.ContainingBlock
			== XamlElementContainingBlockKind.PaddingBox)
		{
			HtmlOutOfFlowPlacementState.Set(
				element,
				new(
					hasLeft,
					left,
					hasTop,
					top,
					hasRight,
					right,
					hasBottom,
					bottom,
					placement.HasDefiniteWidth,
					placement.HasDefiniteHeight));
			return;
		}

		var margin = element.Margin;
		element.Margin = new(
			margin.Left + (hasLeft ? left : 0),
			margin.Top + (hasTop ? top : 0),
			margin.Right + (hasRight ? right : 0),
			margin.Bottom + (hasBottom ? bottom : 0));
	}

	private double MaterializeInset(LayoutLength? inset, bool vertical) =>
		inset switch
		{
			null => 0,
			LayoutLength.Constant constant
				when constant.Unit == LayoutLengthUnit.Dip => constant.Value,
			LayoutLength.Constant constant
				when constant.Unit == LayoutLengthUnit.CssPixel =>
					ScaleCssPixel(constant.Value, vertical),
			_ => throw new InvalidDataException(
				$"CSS inset {inset} cannot be materialized as a WinUI offset.")
		};

	private bool TryApplyCompositeStyleAttribute(
		DependencyObject element,
		GeneratedXamlAttribute attribute,
		XamlElementObjectPlan plan)
	{
		if (_styles is null
			|| attribute.CompositeValueKind == XamlCompositeValueKind.None)
		{
			return false;
		}
		if (attribute.Sources.Count < 2)
		{
			throw new InvalidDataException(
				$"Composite {attribute.Name} declares "
					+ $"{attribute.CompositeValueKind} but has fewer than two sources.");
		}
		var source = plan.SourceElement ?? plan.OwnerElement
			?? throw new InvalidDataException(
				$"Composite {attribute.Name} has no owning DOM element.");
		var resolutions = new HtmlRuntimeStyleResolution[attribute.Sources.Count];
		var components = new string[resolutions.Length];
		for (var index = 0; index < resolutions.Length; index++)
		{
			var slotSource = attribute.Sources[index];
			var resolution = _styles.ResolveForXaml(
				source.DocumentScope,
				source.XPath,
				slotSource.Name);
			if (resolution is null)
			{
				throw new InvalidDataException(
					$"Composite {attribute.Name} source "
						+ $"'{slotSource.Name}' has no CSS style resolution.");
			}
			resolutions[index] = resolution;
			components[index] = attribute.CompositeValueKind
				== XamlCompositeValueKind.UniformValue
					? resolution.ConcreteValue
					: NormalizeCompositeLength(
						resolution.ConcreteValue,
						attribute.Name,
						slotSource.Name);
		}
		var effective = attribute with
		{
			Value = attribute.CompositeValueKind switch
			{
				XamlCompositeValueKind.SumLengths =>
					components.Sum(ParseDouble).ToString(
						"R",
						CultureInfo.InvariantCulture),
				XamlCompositeValueKind.Thickness
					or XamlCompositeValueKind.CornerRadius =>
					string.Join(",", components),
				XamlCompositeValueKind.UniformValue when components.All(
					value => value.Equals(components[0], StringComparison.Ordinal)) =>
					components[0],
				XamlCompositeValueKind.UniformValue =>
					throw new InvalidDataException(
						$"Composite {attribute.Name} requires uniform source values."),
				_ => throw new InvalidDataException(
					$"Unsupported composite value kind "
						+ $"{attribute.CompositeValueKind}.")
			}
		};
		ApplyAttribute(element, effective, plan);
		var compositeTargetId =
			$"{source.DocumentScope}::{source.XPath}::{attribute.Name}";
		for (var index = 0; index < resolutions.Length; index++)
		{
			_styles.RegisterXamlTarget(
				resolutions[index],
				element,
				attribute.Name,
				compositeTargetId,
				index,
				resolutions.Length,
				attribute.CompositeValueKind);
		}
		return true;
	}

	private static string NormalizeCompositeLength(
		string value,
		string targetProperty,
		string sourceProperty)
	{
		try
		{
			return ParseDouble(value).ToString(
				"R",
				CultureInfo.InvariantCulture);
		}
		catch (FormatException exception)
		{
			throw new InvalidDataException(
				$"Cannot materialize {sourceProperty}='{value}' as a "
					+ $"{targetProperty} component.",
				exception);
		}
	}

	private void AttachSemanticStyleState(
		XamlElementObjectPlan plan,
		DependencyObject element)
	{
		if ((plan.SourceElement ?? plan.OwnerElement) is not { } source
			|| !_semanticStyleBindings.TryGetValue(
				(source.DocumentScope, source.XPath),
				out var bindings))
		{
			return;
		}
		WinUiCssSemantic.SetState(
			element,
			new WinUiCssSemanticState(bindings));
	}

	private void RegisterAnimationTarget(
		XamlElementObjectPlan plan,
		DependencyObject element)
	{
		if (_designRuntime is null
			|| (plan.SourceElement ?? plan.OwnerElement) is not { } source)
		{
			return;
		}
		var binding = _designRuntime.Animations.Find(
			source.DocumentScope,
			source.XPath);
		if (binding is null)
			return;
		HtmlAnimationState.ApplyTimeline(
			element,
			binding.Runtime?.Value);
		_designRuntime.Animations.RegisterXamlTarget(binding, element);
	}

	private void RegisterLayoutTarget(
		XamlElementObjectPlan plan,
		DependencyObject element)
	{
		if (_layout is null
			|| !plan.Mapping.RequiresRuntimeLayoutContract
			|| (plan.SourceElement ?? plan.OwnerElement) is not { } source)
		{
			return;
		}
		var materialization = ResolveLayoutMaterialization(plan, element);
		_layout.RegisterXamlTarget(
			source.DocumentScope,
			source.XPath,
			element,
			plan.Mapping.Kind,
			materialization.IsMaterialized,
			materialization.Mechanism);
	}

	internal static (bool IsMaterialized, string Mechanism)
		ResolveLayoutMaterialization(
		XamlElementObjectPlan plan,
		DependencyObject element)
	{
		if (element is HtmlFilteredElementHost filtered)
			return ResolveLayoutMaterialization(plan, filtered.InnerElement);
		if (plan.Mapping.Kind == XamlElementMappingKind.BlockFlow
			&& IsGridBacked(element)
			&& plan.Children.Count != 0
			&& plan.RowDefinitions.Count == 0
			&& plan.Children.All(static child =>
				child.SourceElement is { } source
				&& IsOutOfFlow(source)))
		{
			return (
				true,
				"WinUI Grid out-of-flow host with C# runtime positioning");
		}
		return plan.Mapping.Kind switch
		{
			XamlElementMappingKind.ViewportRoot when IsGridBacked(element) =>
				(true, "WinUI root CSS box with Grid stretch measurement"),
			XamlElementMappingKind.FlexLayout
				when element is HtmlCssBoxGrid =>
				(true, "Strong HtmlCssBoxGrid C# flex layout executor"),
			XamlElementMappingKind.FlexLayout
				when IsGridBacked(element)
					&& (plan.Children.Count == 0
						|| plan.RowDefinitions.Count
							+ plan.ColumnDefinitions.Count > 0) =>
				(true, "WinUI Grid flex tracks"),
			XamlElementMappingKind.PositionedLayout when element is Canvas =>
				(true, "WinUI Canvas positioned layout"),
			XamlElementMappingKind.PositionedLayout
				when element is HtmlSvgViewport =>
				(true, "Strong SVG viewBox coordinate-space layout executor"),
			XamlElementMappingKind.PositionedLayout
				when element is HtmlDialogControl
					or HtmlImageMapComposite
					or HtmlSpanBoxControl =>
				(true, "Strong HTML positioned semantic control"),
			XamlElementMappingKind.GridLayout
				when IsGridBacked(element)
					&& plan.RowDefinitions.Count
						+ plan.ColumnDefinitions.Count > 0 =>
				(true, "WinUI Grid CSS-grid tracks"),
			XamlElementMappingKind.TableLayout
				when IsGridBacked(element)
					&& plan.RowDefinitions.Count
						+ plan.ColumnDefinitions.Count > 0 =>
				(true, "WinUI Grid table tracks"),
			XamlElementMappingKind.TableLayout
				when element is HtmlTablePanelBase or Border =>
				(true, "Strong HTML table panel/cell layout"),
			XamlElementMappingKind.TypeDefault
				when element is HtmlEmbeddedDocumentHost =>
				(true, "Embedded document ContentControl measure/arrange"),
			XamlElementMappingKind.TypeDefault
				when element is HtmlObjectContentHost =>
				(true, "Embedded object Grid primary/fallback layout"),
			XamlElementMappingKind.TypeDefault
				when element is HtmlEmbeddedContentHost =>
				(true, "Embedded content ContentControl measure/arrange"),
			XamlElementMappingKind.BlockFlow
				when element is HtmlCssBoxGrid =>
				(true, "Strong HtmlCssBoxGrid block-flow row executor"),
			XamlElementMappingKind.BlockFlow
				when element is HtmlFieldSetPanel =>
				(true, "Strong HTML fieldset block-flow panel"),
			_ => (false, "No executable WinUI layout mechanism")
		};
	}

	private static bool IsGridBacked(DependencyObject element) =>
		element is Grid or HtmlCssBoxGrid or HtmlInteractiveFlexPanel;

	private static bool IsOutOfFlow(DomElement element)
	{
		var value = element.HtmlRoot?.ResolveGlobalStyleValue(
			element,
			"style.position",
			DomPropertyDataSlot.Runtime)
			?? element.HtmlRoot?.ResolveGlobalStyleValue(
				element,
				"style.position",
				DomPropertyDataSlot.Initialization);
		return value is "absolute" or "fixed";
	}

	private HtmlRuntimeStyleResolution? ResolveStyle(
		GeneratedXamlAttribute attribute,
		XamlElementObjectPlan plan)
	{
		if (_styles is null
			|| (plan.SourceElement ?? plan.OwnerElement) is not { } source
			|| attribute.Source is not { } property
			|| !property.Name.StartsWith("style.", StringComparison.Ordinal))
		{
			return null;
		}
		return _styles.ResolveForXaml(
			source.DocumentScope,
			source.XPath,
			property.Name);
	}

	private static bool ContainsUnresolvedCssExpression(string value) =>
		ContainsCssFunction(value, "var")
		|| ContainsCssFunction(value, "calc")
		|| ContainsCssFunction(value, "min")
		|| ContainsCssFunction(value, "max")
		|| ContainsCssFunction(value, "clamp");

	private static bool ContainsCssFunction(string value, string functionName)
	{
		var token = functionName + "(";
		var start = 0;
		while ((start = value.IndexOf(
			token,
			start,
			StringComparison.OrdinalIgnoreCase)) >= 0)
		{
			if (start == 0
				|| !(char.IsLetterOrDigit(value[start - 1])
					|| value[start - 1] is '-' or '_'))
			{
				return true;
			}
			start += token.Length;
		}
		return false;
	}

	public void AttachChild(
		XamlElementObjectAttachmentContext context)
	{
		ArgumentNullException.ThrowIfNull(context);
		if (context.Parent is HtmlFilteredElementHost filtered)
		{
			AttachChild(context with { Parent = filtered.InnerElement });
			return;
		}
		if (context.Placement == ElementXamlChildPlacementKind.DirectChildren
			&& context.Parent is HtmlSvgViewport svgViewport
			&& context.Child is UIElement svgChild)
		{
			svgViewport.CoordinateSurface.Children.Add(svgChild);
			return;
		}
		if (context.Placement == ElementXamlChildPlacementKind.DirectChildren
			&& context.Parent is HtmlInlineFlowPanel inlineFlow
			&& context.Child is UIElement inlineChild)
		{
			inlineFlow.Children.Add(inlineChild);
			return;
		}
		if (context.Placement == ElementXamlChildPlacementKind.DirectChildren
			&& context.Parent is HtmlInteractiveFlexPanel interactiveFlex
			&& context.Child is UIElement interactiveChild)
		{
			ApplyFlexMainAxisAlignment(
				context.ParentPlan,
				context.ChildPlan,
				interactiveChild);
			interactiveFlex.AddChild(
				interactiveChild,
				context.ChildPlan.LayoutPlacement);
			return;
		}
		if (context.Placement == ElementXamlChildPlacementKind.DirectChildren
			&& context.Parent is HtmlCssBoxGrid cssBox
			&& context.Child is UIElement cssBoxChild)
		{
			// Runtime Web owns the complete block/flex/grid plan. Its plan already
			// supplies tracks and each child's attached Grid.Row/Grid.Column values.
			// Adding tracks here duplicates the public plan and shifts every block
			// child by the number of pre-existing rows.
			ApplyInheritedTextStyles(cssBox, cssBoxChild);
			ApplyFlexMainAxisAlignment(
				context.ParentPlan,
				context.ChildPlan,
				cssBoxChild);
			cssBox.AddChild(
				cssBoxChild,
				context.ChildPlan.LayoutPlacement);
			return;
		}
		if (context.Placement == ElementXamlChildPlacementKind.DirectChildren
			&& context.Parent is Panel panel
			&& context.Child is UIElement panelChild)
		{
			panel.Children.Add(panelChild);
			return;
		}
		if (context.Placement == ElementXamlChildPlacementKind.Inlines
			&& context.Parent is TextBlock textBlock
			&& context.Child is Inline inline)
		{
			textBlock.Inlines.Add(inline);
			return;
		}
		if (context.Placement == ElementXamlChildPlacementKind.Items
			&& context.Parent is ItemsControl items)
		{
			items.Items.Add(context.Child);
			return;
		}
		if (context.Placement == ElementXamlChildPlacementKind.Content
			&& context.Parent is ContentControl content)
		{
			if (content.Content is not null)
			{
				throw new InvalidOperationException(
					$"{context.ParentPlan.Mapping.ElementName} already has content.");
			}
			content.Content = context.Child;
			return;
		}
		if (context.Placement == ElementXamlChildPlacementKind.Content
			&& context.Parent is Border border
			&& context.Child is UIElement borderChild)
		{
			if (border.Child is not null)
			{
				throw new InvalidOperationException(
					"Border already has a child.");
			}
			border.Child = borderChild;
			return;
		}
		if (context.Placement == ElementXamlChildPlacementKind.Content
			&& context.Parent is Expander expander)
		{
			if (expander.Content is not null)
			{
				throw new InvalidOperationException(
					"Expander already has content.");
			}
			expander.Content = context.Child;
			return;
		}
		throw new InvalidOperationException(
			$"Cannot attach {context.ChildPlan.Mapping.ElementName} to "
			+ $"{context.ParentPlan.Mapping.ElementName} through "
			+ $"{context.Placement}.");
	}

	private static void ApplyFlexMainAxisAlignment(
		XamlElementObjectPlan parentPlan,
		XamlElementObjectPlan childPlan,
		UIElement child)
	{
		if (parentPlan.Mapping.Kind != XamlElementMappingKind.FlexLayout
			|| childPlan.LayoutPlacement?.IsOutOfFlow == true
			|| child is not FrameworkElement element)
		{
			return;
		}
		if (parentPlan.ColumnDefinitions.Count > 0
			&& parentPlan.RowDefinitions.Count == 0)
		{
			element.HorizontalAlignment = HorizontalAlignment.Left;
		}
		else if (parentPlan.RowDefinitions.Count > 0
			&& parentPlan.ColumnDefinitions.Count == 0)
		{
			element.VerticalAlignment = VerticalAlignment.Top;
		}
	}

	private static void ApplyInheritedTextStyles(
		HtmlCssBoxGrid parent,
		UIElement child)
	{
		if (child is not FrameworkElement element)
			return;
		element.FlowDirection = parent.FlowDirection;
		if (element is Control control)
			control.FontStretch = parent.FontStretch;
		if (element is not TextBlock text)
			return;
		if (parent.Foreground is not null)
			text.Foreground = parent.Foreground;
		text.FontSize = parent.FontSize;
		text.FontWeight = parent.FontWeight;
		text.FontStyle = parent.FontStyle;
		text.FontStretch = parent.FontStretch;
		if (parent.FontFamily is not null)
			text.FontFamily = parent.FontFamily;
		text.LineHeight = parent.TextLineHeight;
		text.TextAlignment = parent.TextAlignment;
		text.TextDecorations = parent.TextDecorations;
		text.TextWrapping = parent.WhiteSpace is "pre-wrap" or "pre-line"
			? TextWrapping.Wrap
			: TextWrapping.NoWrap;
		text.TextTrimming = parent.TextOverflow.Equals(
			"ellipsis",
			StringComparison.OrdinalIgnoreCase)
			? TextTrimming.CharacterEllipsis
			: TextTrimming.None;
	}

	public void ApplyGridTracks(
		object element,
		IReadOnlyList<XamlGridTrackDefinition> rowDefinitions,
		IReadOnlyList<XamlGridTrackDefinition> columnDefinitions)
	{
		ArgumentNullException.ThrowIfNull(element);
		if (rowDefinitions.Count == 0 && columnDefinitions.Count == 0)
			return;
		var grid = element switch
		{
			HtmlFilteredElementHost filtered => filtered.InnerElement switch
			{
				Grid innerGrid => innerGrid,
				HtmlInteractiveFlexPanel innerInteractive =>
					innerInteractive.LayoutRoot,
				_ => null
			},
			Grid directGrid => directGrid,
			HtmlInteractiveFlexPanel interactiveFlex =>
				interactiveFlex.LayoutRoot,
			_ => null
		};
		if (grid is null)
		{
			throw new InvalidOperationException(
				"Grid tracks can only be applied to a Grid-backed element.");
		}
		foreach (var definition in rowDefinitions)
		{
			grid.RowDefinitions.Add(new()
			{
				Height = MaterializeGridLength(definition.StrongLength, vertical: true),
				MinHeight = MaterializeTrackLimit(definition.StrongMinimum, vertical: true) ?? 0,
				MaxHeight = MaterializeTrackLimit(definition.StrongMaximum, vertical: true)
					?? double.PositiveInfinity
			});
		}
		foreach (var definition in columnDefinitions)
		{
			grid.ColumnDefinitions.Add(new()
			{
				Width = MaterializeGridLength(definition.StrongLength, vertical: false),
				MinWidth = MaterializeTrackLimit(definition.StrongMinimum, vertical: false) ?? 0,
				MaxWidth = MaterializeTrackLimit(definition.StrongMaximum, vertical: false)
					?? double.PositiveInfinity
			});
		}
		HtmlContainingBlockChildHost.SynchronizeTrackSpans(grid);
	}

	private static Microsoft.UI.Xaml.Controls.Grid CreateHtmlRoot(
		HtmlRootDomElement element,
		XamlElementObjectPlan plan)
	{
		ArgumentNullException.ThrowIfNull(element);
		RequireObjectType(plan, XamlElementObjectType.Grid);
		return new Microsoft.UI.Xaml.Controls.Grid();
	}

	private static Microsoft.UI.Xaml.Controls.Grid CreateHtmlBody(
		HtmlBodyDomElement element,
		XamlElementObjectPlan plan)
	{
		ArgumentNullException.ThrowIfNull(element);
		RequireObjectType(plan, XamlElementObjectType.Grid);
		return new Microsoft.UI.Xaml.Controls.Grid();
	}

	private static Microsoft.UI.Xaml.Controls.Grid CreateHtmlDiv(
		HtmlDivDomElement element,
		XamlElementObjectPlan plan)
	{
		ArgumentNullException.ThrowIfNull(element);
		RequireObjectType(plan, XamlElementObjectType.Grid);
		return new Microsoft.UI.Xaml.Controls.Grid();
	}

	private static Microsoft.UI.Xaml.Controls.Grid CreateHtmlMain(
		HtmlMainDomElement element,
		XamlElementObjectPlan plan)
	{
		ArgumentNullException.ThrowIfNull(element);
		RequireObjectType(plan, XamlElementObjectType.Grid);
		return new Microsoft.UI.Xaml.Controls.Grid();
	}

	private static Microsoft.UI.Xaml.Controls.Grid CreateHtmlNav(
		HtmlNavDomElement element,
		XamlElementObjectPlan plan)
	{
		ArgumentNullException.ThrowIfNull(element);
		RequireObjectType(plan, XamlElementObjectType.Grid);
		return new Microsoft.UI.Xaml.Controls.Grid();
	}

	private static Microsoft.UI.Xaml.Controls.Grid CreateHtmlSection(
		HtmlSectionDomElement element,
		XamlElementObjectPlan plan)
	{
		ArgumentNullException.ThrowIfNull(element);
		RequireObjectType(plan, XamlElementObjectType.Grid);
		return new Microsoft.UI.Xaml.Controls.Grid();
	}

	private static Microsoft.UI.Xaml.Controls.Grid CreateHtmlAside(
		HtmlAsideDomElement element,
		XamlElementObjectPlan plan)
	{
		ArgumentNullException.ThrowIfNull(element);
		RequireObjectType(plan, XamlElementObjectType.Grid);
		return new Microsoft.UI.Xaml.Controls.Grid();
	}

	private static HtmlEmbeddedDocumentHost
		CreateHtmlIframe(
		HtmlIframeDomElement element,
		XamlElementObjectPlan plan)
	{
		ArgumentNullException.ThrowIfNull(element);
		RequireObjectType(
			plan,
			XamlElementObjectType.HtmlEmbeddedDocumentHost);
		return new HtmlEmbeddedDocumentHost();
	}

	private static DependencyObject CreateHtmlSpan(
		HtmlSpanDomElement element,
		XamlElementObjectPlan plan)
	{
		ArgumentNullException.ThrowIfNull(element);
		return plan.Mapping.ObjectType switch
		{
			XamlElementObjectType.TextBlock =>
				new Microsoft.UI.Xaml.Controls.TextBlock(),
			XamlElementObjectType.HtmlInlineFlowPanel =>
				new HtmlInlineFlowPanel(),
			XamlElementObjectType.HtmlSpanBoxControl =>
				new HtmlSpanBoxControl(),
			_ => throw new InvalidDataException(
				$"HtmlSpanDomElement requires TextBlock or "
					+ $"HtmlInlineFlowPanel, but selected "
					+ $"{plan.Mapping.ObjectType?.ToString() ?? "<none>"}.")
		};
	}

	private static Microsoft.UI.Xaml.Controls.TextBox CreateHtmlTextArea(
		HtmlTextAreaDomElement element,
		XamlElementObjectPlan plan)
	{
		ArgumentNullException.ThrowIfNull(element);
		RequireObjectType(plan, XamlElementObjectType.TextBox);
		return new HtmlCursorTextBox();
	}

	private static void RequireObjectType(
		XamlElementObjectPlan plan,
		XamlElementObjectType expected)
	{
		if (plan.Mapping.ObjectType != expected)
		{
			throw new InvalidDataException(
				$"{plan.SourceElement?.GetType().Name ?? "Synthetic"} "
					+ $"requires {expected}, but mapping selected "
					+ $"{plan.Mapping.ObjectType?.ToString() ?? "<none>"}.");
		}
	}

	private static DependencyObject CreateSyntheticElement(
		XamlElementObjectPlan plan) =>
		(plan.Mapping.ObjectType
			?? throw new InvalidOperationException(
				"Synthetic nonvisual mappings cannot create a WinUI object."))
		switch
		{
			XamlElementObjectType.Grid => new Grid(),
			XamlElementObjectType.HtmlCssBoxGrid => new HtmlCssBoxGrid(),
			XamlElementObjectType.HtmlCssHorizontalFlexLineGrid =>
				new HtmlCssFlexLineGrid(vertical: false),
			XamlElementObjectType.HtmlCssVerticalFlexLineGrid =>
				new HtmlCssFlexLineGrid(vertical: true),
			XamlElementObjectType.HtmlSvgViewport => new HtmlSvgViewport(),
			XamlElementObjectType.Canvas => new HtmlCanvasPanel(),
			XamlElementObjectType.StackPanel => new StackPanel(),
			XamlElementObjectType.HtmlInlineFlowPanel => new HtmlInlineFlowPanel(),
			XamlElementObjectType.HtmlFormLabelPanel => new HtmlFormLabelPanel(),
			XamlElementObjectType.HtmlDatalistInputControl => new HtmlDatalistInputControl(),
			XamlElementObjectType.HtmlBidiIsolationTextBlock => new HtmlBidiIsolationTextBlock(),
			XamlElementObjectType.HtmlBidiOverrideTextBlock => new HtmlBidiOverrideTextBlock(),
			XamlElementObjectType.HtmlBidiIsolationPanel => new HtmlBidiIsolationPanel(),
			XamlElementObjectType.HtmlBidiOverridePanel => new HtmlBidiOverridePanel(),
			XamlElementObjectType.HtmlMediaElementControl => new HtmlMediaElementControl(),
			XamlElementObjectType.HtmlLineBreak => new HtmlLineBreakElement(),
			XamlElementObjectType.HtmlWordBreakOpportunity => new HtmlWordBreakOpportunityElement(),
			XamlElementObjectType.HtmlSubscriptTextBlock => new HtmlSubscriptTextBlock(),
			XamlElementObjectType.HtmlSuperscriptTextBlock => new HtmlSuperscriptTextBlock(),
			XamlElementObjectType.HtmlSubscriptPanel => new HtmlSubscriptPanel(),
			XamlElementObjectType.HtmlSuperscriptPanel => new HtmlSuperscriptPanel(),
			XamlElementObjectType.HtmlRubyPanel => new HtmlRubyPanel(),
			XamlElementObjectType.HtmlRubyAnnotationTextBlock =>
				new HtmlRubyAnnotationTextBlock(),
			XamlElementObjectType.HtmlRubyAnnotationPanel => new HtmlRubyAnnotationPanel(),
			XamlElementObjectType.HtmlTablePanel => new HtmlTablePanel(),
			XamlElementObjectType.HtmlTableSectionPanel => new HtmlTableSectionPanel(),
			XamlElementObjectType.HtmlTableRowPanel => new HtmlTableRowPanel(),
			XamlElementObjectType.TextBlock => new TextBlock(),
			XamlElementObjectType.Button => new HtmlCursorButton(),
			XamlElementObjectType.HyperlinkButton =>
				new HtmlCursorHyperlinkButton(),
			XamlElementObjectType.TextBox => new HtmlCursorTextBox(),
			XamlElementObjectType.PasswordBox => new HtmlCursorPasswordBoxHost(),
			XamlElementObjectType.CheckBox => new CheckBox(),
			XamlElementObjectType.RadioButton => new RadioButton(),
			XamlElementObjectType.Slider => new Slider(),
			XamlElementObjectType.NumberBox => new NumberBox(),
			XamlElementObjectType.HtmlDateInputControl => new HtmlDateInputControl(),
			XamlElementObjectType.HtmlMonthInputControl => new HtmlMonthInputControl(),
			XamlElementObjectType.HtmlWeekInputControl => new HtmlWeekInputControl(),
			XamlElementObjectType.HtmlTimeInputControl => new HtmlTimeInputControl(),
			XamlElementObjectType.HtmlDateTimeLocalInputControl =>
				new HtmlDateTimeLocalInputControl(),
			XamlElementObjectType.HtmlFileInputControl => new HtmlFileInputControl(),
			XamlElementObjectType.HtmlFormButton => new HtmlFormButton(),
			XamlElementObjectType.HtmlInteractiveFlexPanel =>
				new HtmlInteractiveFlexPanel(),
			XamlElementObjectType.HtmlImageSubmitButton => new HtmlImageSubmitButton(),
			XamlElementObjectType.CalendarDatePicker => new CalendarDatePicker(),
			XamlElementObjectType.DatePicker => new DatePicker(),
			XamlElementObjectType.TimePicker => new TimePicker(),
			XamlElementObjectType.ColorPicker => new ColorPicker(),
			XamlElementObjectType.ComboBox => new ComboBox(),
			XamlElementObjectType.ComboBoxItem => new ComboBoxItem(),
			XamlElementObjectType.ListView => new ListView(),
			XamlElementObjectType.ListViewItem => new ListViewItem(),
			XamlElementObjectType.Expander => new Expander(),
			XamlElementObjectType.HtmlMeterControl => new HtmlMeterControl(),
			XamlElementObjectType.HtmlEmbeddedContentHost => new HtmlEmbeddedContentHost(),
			XamlElementObjectType.HtmlEmbeddedDocumentHost => new HtmlEmbeddedDocumentHost(),
			XamlElementObjectType.HtmlObjectContentHost => new HtmlObjectContentHost(),
			XamlElementObjectType.HtmlCanvasSurface => new HtmlCanvasSurface(),
			XamlElementObjectType.HtmlImageMapComposite => new HtmlImageMapComposite(),
			XamlElementObjectType.HtmlImageMapOverlay => new HtmlImageMapOverlay(),
			XamlElementObjectType.HtmlImageMapHotspot => new HtmlImageMapHotspot(),
			XamlElementObjectType.HtmlFieldSetPanel => new HtmlFieldSetPanel(),
			XamlElementObjectType.HtmlDialogControl => new HtmlDialogControl(),
			XamlElementObjectType.ProgressBar => new ProgressBar(),
			XamlElementObjectType.Image => new Image(),
			XamlElementObjectType.MediaPlayerElement => new MediaPlayerElement(),
			XamlElementObjectType.ContentControl => new ContentControl(),
			XamlElementObjectType.Border => new Border(),
			XamlElementObjectType.Path => new Microsoft.UI.Xaml.Shapes.Path(),
			XamlElementObjectType.Ellipse => new Ellipse(),
			XamlElementObjectType.Rectangle => new Rectangle(),
			XamlElementObjectType.Line => new Line(),
			XamlElementObjectType.Polygon => new Polygon(),
			XamlElementObjectType.Polyline => new Polyline(),
			_ => throw new NotSupportedException(
				$"Unsupported synthetic WinUI element type "
					+ $"'{plan.Mapping.ObjectType}'.")
		};

	private static void ApplyAttribute(
		DependencyObject element,
		GeneratedXamlAttribute attribute,
		XamlElementObjectPlan plan) =>
		ApplyAttribute(
			element,
			attribute,
			plan.SourceElement,
			plan.Mapping.ElementName);

	private static void ApplyAttribute(
		DependencyObject element,
		GeneratedXamlAttribute attribute,
		DomElement? sourceElement,
		string targetDescription) =>
		ApplyStrongProperty(
			element,
			attribute,
			sourceElement,
			targetDescription);
	private static Uri ResolveNavigationUri(
		string value,
		DomElement? source)
	{
		if (source?.HtmlRoot is HtmlRuntimeDocumentRoot runtimeRoot
			&& value.StartsWith("//", StringComparison.Ordinal)
			&& Uri.TryCreate(
				$"{runtimeRoot.NavigationUrl.Scheme}:{value}",
				UriKind.Absolute,
				out var absolute))
		{
			return absolute;
		}
		if (Uri.TryCreate(value, UriKind.Absolute, out absolute))
			return absolute;
		if (source?.HtmlRoot is HtmlRuntimeDocumentRoot runtimeDocumentRoot
			&& Uri.TryCreate(
				runtimeDocumentRoot.NavigationUrl,
				value,
				out absolute))
		{
			return absolute;
		}
		throw new InvalidDataException(
			$"Relative navigation URI '{value}' has no absolute document base.");
	}

	private static bool IsCssAutomaticSize(string value) =>
		value.Equals("auto", StringComparison.OrdinalIgnoreCase);

	private static bool IsCssUnboundedMaximum(string value) =>
		IsCssAutomaticSize(value)
		|| value.Equals("none", StringComparison.OrdinalIgnoreCase);

	internal static void ApplyRuntimeStyleAttribute(
		object target,
		string targetProperty,
		string value)
	{
		if (target is not DependencyObject dependencyObject)
		{
			throw new InvalidOperationException(
				$"Runtime style target {target.GetType().FullName} "
					+ "is not a DependencyObject.");
		}
		ApplyAttribute(
			dependencyObject,
			new(targetProperty, value, null),
			null,
			target.GetType().FullName ?? target.GetType().Name);
	}

	private static Color ParseColor(string value)
	{
		var brush = ParseBrush(value) as SolidColorBrush
			?? throw new FormatException($"Invalid color '{value}'.");
		return brush.Color;
	}

	private GridLength MaterializeGridLength(LayoutLength value, bool vertical) =>
		value switch
		{
			LayoutLength.Automatic => GridLength.Auto,
			LayoutLength.Constant constant when constant.Unit is LayoutLengthUnit.Dip =>
					new(constant.Value, GridUnitType.Pixel),
			LayoutLength.Constant constant when constant.Unit is LayoutLengthUnit.CssPixel =>
					new(ScaleCssPixel(constant.Value, vertical), GridUnitType.Pixel),
			LayoutLength.Fraction fraction =>
				new(fraction.Value, GridUnitType.Star),
			_ => throw new InvalidOperationException(
				$"Layout length {value.GetType().Name} cannot materialize "
					+ "as a WinUI GridLength.")
		};

	private double? MaterializeTrackLimit(LayoutLength? value, bool vertical) =>
		value switch
		{
			null => null,
			LayoutLength.Constant constant when constant.Unit is LayoutLengthUnit.Dip =>
				constant.Value,
			LayoutLength.Constant constant when constant.Unit is LayoutLengthUnit.CssPixel =>
				ScaleCssPixel(constant.Value, vertical),
			LayoutLength.Automatic or LayoutLength.Fraction => null,
			_ => throw new InvalidOperationException(
				$"Layout limit {value.GetType().Name} cannot materialize "
					+ "as a WinUI track limit.")
		};

	private double ScaleCssPixel(double value, bool vertical) =>
		_layout?.ScaleCssPixelToRuntime(value, vertical) ?? value;

	private static Thickness ParseThickness(string value)
	{
		var values = ParseNumbers(value);
		return values.Length switch
		{
			1 => new(values[0]),
			2 => new(values[0], values[1], values[0], values[1]),
			4 => new(values[0], values[1], values[2], values[3]),
			_ => throw new FormatException(
				$"Invalid CSS thickness '{value}'.")
		};
	}

	private static CornerRadius ParseCornerRadius(string value)
	{
		var values = ParseNumbers(value);
		return values.Length switch
		{
			1 => new(values[0]),
			4 => new(values[0], values[1], values[2], values[3]),
			_ => throw new FormatException(
				$"Invalid corner radius '{value}'.")
		};
	}

	private static double[] ParseNumbers(string value) =>
		value.Split(
				[' ', ','],
				StringSplitOptions.RemoveEmptyEntries
					| StringSplitOptions.TrimEntries)
			.Select(static item => ParseDouble(
				item.EndsWith("px", StringComparison.OrdinalIgnoreCase)
					? item[..^2]
					: item))
			.ToArray();

	internal static Brush ParseBrush(string value)
	{
		if (value.Equals("transparent", StringComparison.OrdinalIgnoreCase))
			return new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
		var rgb = Regex.Match(
			value,
			@"^rgba?\(\s*(\d+)(?:\s*,\s*|\s+)(\d+)"
			+ @"(?:\s*,\s*|\s+)(\d+)"
			+ @"(?:\s*(?:,|/)\s*([0-9.]+)%?)?\s*\)$",
			RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
		if (rgb.Success)
		{
			var rgbAlpha = rgb.Groups[4].Success
				? double.Parse(
					rgb.Groups[4].Value,
					CultureInfo.InvariantCulture)
				: 1;
			if (value.Contains('%', StringComparison.Ordinal))
				rgbAlpha /= 100;
			return new SolidColorBrush(Color.FromArgb(
				(byte)Math.Round(
					Math.Clamp(rgbAlpha, 0, 1) * byte.MaxValue,
					MidpointRounding.AwayFromZero),
				byte.Parse(rgb.Groups[1].Value, CultureInfo.InvariantCulture),
				byte.Parse(rgb.Groups[2].Value, CultureInfo.InvariantCulture),
				byte.Parse(rgb.Groups[3].Value, CultureInfo.InvariantCulture)));
		}
		var oklab = Regex.Match(
			value,
			@"^oklab\(\s*([-+0-9.eE]+)(%)?\s+"
			+ @"([-+0-9.eE]+)\s+([-+0-9.eE]+)"
			+ @"(?:\s*/\s*([-+0-9.eE]+)(%)?)?\s*\)$",
			RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
		if (oklab.Success)
		{
			var lightness = double.Parse(
				oklab.Groups[1].Value,
				CultureInfo.InvariantCulture);
			if (oklab.Groups[2].Success)
				lightness /= 100;
			var axisA = double.Parse(
				oklab.Groups[3].Value,
				CultureInfo.InvariantCulture);
			var axisB = double.Parse(
				oklab.Groups[4].Value,
				CultureInfo.InvariantCulture);
			var alphaValue = oklab.Groups[5].Success
				? double.Parse(
					oklab.Groups[5].Value,
					CultureInfo.InvariantCulture)
				: 1;
			if (oklab.Groups[6].Success)
				alphaValue /= 100;
			var lRoot = lightness
				+ 0.3963377774 * axisA
				+ 0.2158037573 * axisB;
			var mRoot = lightness
				- 0.1055613458 * axisA
				- 0.0638541728 * axisB;
			var sRoot = lightness
				- 0.0894841775 * axisA
				- 1.2914855480 * axisB;
			var l = lRoot * lRoot * lRoot;
			var m = mRoot * mRoot * mRoot;
			var s = sRoot * sRoot * sRoot;
			return new SolidColorBrush(Color.FromArgb(
				(byte)Math.Round(
					Math.Clamp(alphaValue, 0, 1) * byte.MaxValue),
				LinearSrgbByte(
					4.0767416621 * l
					- 3.3077115913 * m
					+ 0.2309699292 * s),
				LinearSrgbByte(
					-1.2684380046 * l
					+ 2.6097574011 * m
					- 0.3413193965 * s),
				LinearSrgbByte(
					-0.0041960863 * l
					- 0.7034186147 * m
					+ 1.7076147010 * s)));
		}
		if (!value.StartsWith('#'))
		{
			throw new FormatException(
				$"Unsupported color '{value}'.");
		}
		var hex = value[1..];
		if (hex.Length == 3)
			hex = string.Concat(hex.Select(static character => $"{character}{character}"));
		if (hex.Length == 4)
			hex = string.Concat(hex.Select(static character => $"{character}{character}"));
		if (hex.Length != 6 && hex.Length != 8)
			throw new FormatException($"Invalid color '{value}'.");
		var alpha = hex.Length == 8
			? byte.Parse(hex[..2], NumberStyles.HexNumber)
			: byte.MaxValue;
		var colorOffset = hex.Length == 8 ? 2 : 0;
		var red = byte.Parse(
			hex.Substring(colorOffset, 2),
			NumberStyles.HexNumber);
		var green = byte.Parse(
			hex.Substring(colorOffset + 2, 2),
			NumberStyles.HexNumber);
		var blue = byte.Parse(
			hex.Substring(colorOffset + 4, 2),
			NumberStyles.HexNumber);
		return new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
	}

	private static byte LinearSrgbByte(double linear)
	{
		var encoded = linear <= 0.0031308
			? 12.92 * linear
			: 1.055 * Math.Pow(linear, 1d / 2.4) - 0.055;
		return (byte)Math.Round(Math.Clamp(encoded, 0, 1) * byte.MaxValue);
	}

	private static PointCollection ParsePointCollection(string value)
	{
		var numbers = ParseNumbers(value);
		if (numbers.Length % 2 != 0)
			throw new FormatException($"Invalid point collection '{value}'.");
		var points = new PointCollection();
		for (var index = 0; index < numbers.Length; index += 2)
			points.Add(new(numbers[index], numbers[index + 1]));
		return points;
	}

	private static DoubleCollection ParseDoubleCollection(string value)
	{
		var collection = new DoubleCollection();
		foreach (var number in ParseNumbers(value))
			collection.Add(number);
		return collection;
	}

	private static FontWeight ParseFontWeight(string value) =>
		value.ToLowerInvariant() switch
		{
			"normal" or "400" => FontWeights.Normal,
			"semibold" or "600" => FontWeights.SemiBold,
			"bold" or "700" => FontWeights.Bold,
			_ => new FontWeight
			{
				Weight = checked((ushort)ParseInt(value))
			}
		};

	private static int ParseInt(string value) =>
		int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);

	private static bool ParseBool(string value) =>
		value.Equals("true", StringComparison.OrdinalIgnoreCase)
			|| value == "1";

	private static double ParseDouble(string value) =>
		double.Parse(
			value.EndsWith("px", StringComparison.OrdinalIgnoreCase)
				? value[..^2]
				: value,
			NumberStyles.Float,
			CultureInfo.InvariantCulture);

	private static FrameworkElement RequireFrameworkElement(
		DependencyObject element,
		string propertyName) =>
		element as FrameworkElement
			?? throw new InvalidOperationException(
				$"{propertyName} requires a FrameworkElement.");

	private static UIElement RequireUiElement(
		DependencyObject element,
		string propertyName) =>
		element as UIElement
			?? throw new InvalidOperationException(
				$"{propertyName} requires a UIElement.");
}

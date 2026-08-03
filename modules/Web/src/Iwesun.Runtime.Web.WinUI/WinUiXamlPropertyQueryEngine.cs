using System.Globalization;
using Iwesun.Runtime.Web;
using Iwesun.Runtime.WebView2;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;

namespace Iwesun.Runtime.Web.WinUI;

public sealed class WinUiXamlPropertyQueryEngine(
	FrameworkElement root,
	IReadOnlyDictionary<string, FrameworkElement> elements,
	WinUiEventRuntimeEvidenceRegistry? eventEvidenceRegistry = null,
	HtmlRuntimeDesignRuntime? designRuntime = null,
	Windows.Foundation.Size? geometryEvidenceScale = null,
	IReadOnlyDictionary<string, FrameworkElement>? geometryRootsByDocumentScope = null,
	IReadOnlyDictionary<IXamlPropertySlotOwner, DependencyObject>?
		materializedPropertyTargets = null) :
	IXamlPropertyQueryEngine
{
	private readonly Windows.Foundation.Size _geometryEvidenceScale =
		geometryEvidenceScale is
		{
			Width: > 0,
			Height: > 0
		} scale
			? scale
			: new Windows.Foundation.Size(1, 1);
	private readonly IReadOnlyDictionary<string, FrameworkElement>
		_geometryRootsByDocumentScope = geometryRootsByDocumentScope
			?? new Dictionary<string, FrameworkElement>(StringComparer.Ordinal);
	private readonly IReadOnlyDictionary<IXamlPropertySlotOwner, DependencyObject>
		_materializedPropertyTargets = materializedPropertyTargets
			?? new Dictionary<IXamlPropertySlotOwner, DependencyObject>();

	public ValueTask<XamlPropertyQueryResult> QueryAsync(
		XamlPropertyQueryContext context,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (!RequiresSourceValue(context))
		{
			return ValueTask.FromResult(
				XamlPropertyQueryResult.ConfirmedAbsent(
					$"DOM source has no {context.Slot} value."));
		}
		if (!context.Execution.IsSupported)
		{
			return ValueTask.FromResult(
				XamlPropertyQueryResult.TargetUnsupported(
					$"{context.PropertyName} is preserved as DOM-only "
					+ "evidence and has no same-element XAML property."));
		}
		if (!SupportsSlot(context.Execution, context.Slot))
		{
			return ValueTask.FromResult(
				XamlPropertyQueryResult.TargetUnsupported(
					$"{context.Execution.Kind} does not support {context.Slot}."));
		}
		var elementPath = context.DocumentScope.Equals(
			"document",
			StringComparison.Ordinal)
				? context.XPath
				: $"{context.DocumentScope}::{context.XPath}";
		if (!elements.TryGetValue(elementPath, out var element))
		{
			return ValueTask.FromResult(
				XamlPropertyQueryResult.TargetUnsupported(
					$"Generated FrameworkElement is missing: {elementPath}."));
		}
		if (context.XamlElement is FrameworkElement contextualElement)
		{
			if (!ReferenceEquals(contextualElement, element))
			{
				return ValueTask.FromResult(
					XamlPropertyQueryResult.TargetUnsupported(
						$"XAML object identity mismatch for {elementPath}."));
			}
			element = contextualElement;
		}
		else if (context.XamlElement is not null)
		{
			return ValueTask.FromResult(
				XamlPropertyQueryResult.TargetUnsupported(
					$"XAML object for {elementPath} is not a FrameworkElement."));
		}
		else
		{
			context.Element.XamlElement = element;
		}
		if (context.SlotOwner is IXamlPropertySlotOwner propertyOwner
			&& _materializedPropertyTargets.TryGetValue(
				propertyOwner,
				out var materializedTarget))
		{
			if (materializedTarget is not FrameworkElement propertyElement)
			{
				return ValueTask.FromResult(
					XamlPropertyQueryResult.TargetUnsupported(
						$"Materialized target for {context.PropertyName} is not "
							+ "a FrameworkElement."));
			}
			element = propertyElement;
		}
		if (element is HtmlFilteredElementHost filtered
			&& ShouldReadFilteredInnerElement(context))
		{
			element = filtered.InnerElement;
		}
		if (context.Execution.Kind == XamlPropertyExecutionKind.Event)
		{
			return ValueTask.FromResult(
				eventEvidenceRegistry?.Query(
					elementPath,
					context.PropertyName,
					context.Slot)
				?? XamlPropertyQueryResult.TargetUnsupported(
					"The WinUI event evidence registry is unavailable."));
		}
		if (context.Slot == XamlPropertyDataSlot.Initialization)
		{
#if LEGACY_XAML_FILL_STYLE_EXPRESSION_FALLBACK
			if (TryReadAppliedStyleInitialization(
				context,
				element,
				out var appliedStyleValue))
			{
				return ValueTask.FromResult(
					XamlPropertyQueryResult.DirectConstant(
						appliedStyleValue,
						"Read from a target-side WinUI style binding "
						+ "registered only after its concrete property "
						+ "was successfully applied."));
			}
#endif
			if (TryReadValue(
				context,
				elementPath,
				element,
				out var initializedValue))
			{
				return ValueTask.FromResult(
					XamlPropertyQueryResult.DirectConstant(
						initializedValue,
						"Read directly from the live WinUI target after "
						+ "generated initialization was applied."));
			}
			return ValueTask.FromResult(
				XamlPropertyQueryResult.TargetUnsupported(
					$"No live initialization reader exists for "
					+ $"{elementPath}.{context.PropertyName} "
					+ $"({context.Execution.TargetProperty})."));
		}
		if (context.Slot == XamlPropertyDataSlot.Link)
			return ValueTask.FromResult(ReadLink(context, elementPath, element));
		if (!TryReadValue(context, elementPath, element, out var value))
		{
			return ValueTask.FromResult(
				XamlPropertyQueryResult.TargetUnsupported(
					$"No WinUI reader is registered for "
					+ $"{context.Execution.Kind}:{context.PropertyName}."));
		}
		return ValueTask.FromResult(
			XamlPropertyQueryResult.DirectConstant(
				value,
				$"Read directly from {element.GetType().Name} "
				+ $"through {context.Execution.TargetProperty}."));
	}

	private static bool ShouldReadFilteredInnerElement(
		XamlPropertyQueryContext context)
	{
		if (context.PropertyName == "style.filter"
			|| context.PropertyName.StartsWith(
				"style.transition",
				StringComparison.Ordinal))
			return false;
		return context.Execution.Kind is
			XamlPropertyExecutionKind.Appearance
			or XamlPropertyExecutionKind.TextAppearance
			or XamlPropertyExecutionKind.Content
			or XamlPropertyExecutionKind.State
			or XamlPropertyExecutionKind.Interaction
			or XamlPropertyExecutionKind.Resource
			or XamlPropertyExecutionKind.ShapeAppearance
			or XamlPropertyExecutionKind.SemanticStyle
			or XamlPropertyExecutionKind.SemanticMetadata;
	}

	[Obsolete(
		"Style-manager expressions are source-side evidence, not live WinUI "
			+ "initialization readback.",
		error: true)]
	private bool TryReadAppliedStyleInitialization(
		XamlPropertyQueryContext context,
		FrameworkElement element,
		out string value)
	{
		value = string.Empty;
		if (designRuntime is null
			|| !context.PropertyName.StartsWith(
				"style.",
				StringComparison.Ordinal))
		{
			return false;
		}
		var target = designRuntime.Styles.XamlTargets.LastOrDefault(
			binding =>
				ReferenceEquals(binding.Target, element)
				&& binding.Source.DocumentScope.Equals(
					context.DocumentScope,
					StringComparison.Ordinal)
				&& binding.Source.XPath.Equals(
					context.XPath,
					StringComparison.Ordinal)
				&& binding.Source.PropertyName.Equals(
					context.PropertyName,
					StringComparison.Ordinal));
		if (target is null)
			return false;
		value = target.Expression;
		return true;
	}

	private XamlPropertyQueryResult ReadLink(
		XamlPropertyQueryContext context,
		string elementPath,
		FrameworkElement element)
	{
		if (context.Execution.Kind
				== XamlPropertyExecutionKind.SemanticMetadata
			&& WinUiTargetSemanticProperties.TryReadLink(
				element,
				context.PropertyName,
				out var semanticLink))
		{
			return XamlPropertyQueryResult.Captured(
				semanticLink.Description,
				ElementPropertyValueSource.LinkedConstant,
				semanticLink,
				"Read from a materialized attached dependency property "
					+ "on the live WinUI target.");
		}
		var proven = context.Execution.Kind switch
		{
			XamlPropertyExecutionKind.GlobalService =>
				context.PropertyName.Equals(
					"service.globalLayout",
					StringComparison.Ordinal)
					? designRuntime?.Layout is not null
					: context.PropertyName.Equals(
						"service.globalStyle",
						StringComparison.Ordinal)
						&& designRuntime?.Styles is not null,
			XamlPropertyExecutionKind.RuntimeGeometry =>
				VisualTreeHelper.GetParent(element) is not null
					|| element.Parent is not null
					|| HasDesignRuntimeLink(context, element),
			_ => HasDesignRuntimeLink(context, element)
		};
		if (!proven)
		{
			return XamlPropertyQueryResult.TargetUnsupported(
				$"No active XAML link backs {elementPath}.{context.PropertyName}.");
		}
		return XamlPropertyQueryResult.Captured(
			context.Source.Link.Description,
			context.Execution.Kind == XamlPropertyExecutionKind.GlobalService
				? ElementPropertyValueSource.LinkedConstant
				: ElementPropertyValueSource.LinkedCalculation,
			context.Source.Link,
			"Verified against the live WinUI controller/property relationship.");
	}

	private bool TryReadValue(
		XamlPropertyQueryContext context,
		string elementPath,
		FrameworkElement element,
		out string value)
	{
		value = string.Empty;
		switch (context.Execution.Kind)
		{
			case XamlPropertyExecutionKind.RuntimeGeometry:
				return TryReadGeometry(context, element, out value);
			case XamlPropertyExecutionKind.FrameworkDimension:
				return TryReadFrameworkDimension(context, element, out value)
					|| TryReadNativeCssSemantic(
						context,
						element,
						out value);
			case XamlPropertyExecutionKind.FrameworkSpacing:
			case XamlPropertyExecutionKind.ContainerLayout:
			case XamlPropertyExecutionKind.Visibility:
			case XamlPropertyExecutionKind.Appearance:
			case XamlPropertyExecutionKind.TextAppearance:
			case XamlPropertyExecutionKind.ShapeAppearance:
			case XamlPropertyExecutionKind.Overflow:
			case XamlPropertyExecutionKind.Interaction:
			case XamlPropertyExecutionKind.Transition:
			case XamlPropertyExecutionKind.SemanticStyle:
				return TryReadNativeCssSemantic(context, element, out value);
			case XamlPropertyExecutionKind.Transform:
				return context.PropertyName == "style.transformOrigin"
					? TryReadTransformOrigin(element, out value)
					: TryReadComposedTransform(context, element, out value);
			case XamlPropertyExecutionKind.Content:
				return TryReadContent(context, element, out value);
			case XamlPropertyExecutionKind.State:
				return TryReadState(context, element, out value);
			case XamlPropertyExecutionKind.Resource:
				return TryReadResource(context, elementPath, element, out value);
			case XamlPropertyExecutionKind.Effect:
				return TryReadEffect(context, elementPath, element, out value);
			case XamlPropertyExecutionKind.SemanticMetadata:
				return WinUiTargetSemanticProperties.TryRead(
					element,
					context.PropertyName,
					context.Slot,
					out value);
			case XamlPropertyExecutionKind.Event:
				return false;
			default:
				return false;
		}
	}

	private bool HasDesignRuntimeLink(
		XamlPropertyQueryContext context,
		FrameworkElement element)
	{
		if (designRuntime is null)
			return false;
		var directTarget = designRuntime.Styles.XamlTargets.Any(binding =>
			ReferenceEquals(binding.Target, element)
			&& binding.Source.DocumentScope.Equals(
				context.DocumentScope,
				StringComparison.Ordinal)
			&& binding.Source.XPath.Equals(
				context.XPath,
				StringComparison.Ordinal)
			&& binding.Source.PropertyName.Equals(
				context.PropertyName,
				StringComparison.Ordinal));
		if (directTarget)
			return true;
		var layoutBinding = designRuntime.Layout.Find(
			context.DocumentScope,
			context.XPath,
			context.PropertyName);
		return layoutBinding is not null
			&& designRuntime.Layout.XamlTargets.Any(binding =>
				ReferenceEquals(binding.Target, element)
				&& binding.Owner.DocumentScope.Equals(
					context.DocumentScope,
					StringComparison.Ordinal)
				&& binding.Owner.XPath.Equals(
					context.XPath,
					StringComparison.Ordinal)
				&& binding.IsMaterialized);
	}

	private bool TryReadNativeCssSemantic(
		XamlPropertyQueryContext context,
		FrameworkElement element,
		out string value)
	{
		value = string.Empty;
		switch (context.PropertyName)
		{
			case "style.display":
				if (element.Visibility != Visibility.Collapsed)
					return TryReadMaterializedDisplay(
						context,
						element,
						out value);
				value = "none";
				return true;
			case "style.position":
				if (WinUiCssSemantic.GetPositioningState(element) is
					{ } positioning)
				{
					value = positioning.Position;
					return true;
				}
				return false;
			case "style.visibility":
				return WinUiVisibilityBehavior.TryRead(element, out value);
			case "style.opacity":
				if (!WinUiVisibilityBehavior.TryReadOpacity(
					element,
					out var actualOpacity))
				{
					return false;
				}
				value = actualOpacity.ToString(
					"R",
					CultureInfo.InvariantCulture);
				return true;
			case "style.zIndex":
				value = element.ReadLocalValue(Canvas.ZIndexProperty)
					== DependencyProperty.UnsetValue
						? "auto"
						: Canvas.GetZIndex(element).ToString(
							CultureInfo.InvariantCulture);
				return true;
			case "style.pointerEvents":
				value = element.IsHitTestVisible ? "auto" : "none";
				return true;
			case "tabindex":
				if (element is Control tabTarget)
				{
					value = tabTarget.TabIndex.ToString(
						CultureInfo.InvariantCulture);
					return true;
				}
				if (element is HtmlCssBoxGrid cssBoxTabTarget)
				{
					value = cssBoxTabTarget.HtmlTabIndex.ToString(
						CultureInfo.InvariantCulture);
					return true;
				}
				return false;
			case "style.boxSizing":
				value = HtmlCssBoxGrid.GetBoxSizing(element);
				return true;
			case "style.margin":
				value = CssThicknessShorthand(element.Margin);
				return true;
			case "style.padding":
				value = CssThicknessShorthand(ReadPadding(element));
				return true;
			case "style.borderWidth":
				value = CssThicknessShorthand(ReadBorderThickness(element));
				return true;
			case "style.borderRadius":
				value = CssCornerRadiusShorthand(ReadCornerRadius(element));
				return true;
			case "style.borderColor":
				return TryReadBrush(ReadBorderBrush(element), out value);
			case "style.borderStyle":
				value = CssQuadShorthand(
					ReadBorderSideStyle(element, BorderSide.Top),
					ReadBorderSideStyle(element, BorderSide.Right),
					ReadBorderSideStyle(element, BorderSide.Bottom),
					ReadBorderSideStyle(element, BorderSide.Left));
				return true;
			case "style.borderTopStyle":
				value = ReadBorderSideStyle(element, BorderSide.Top);
				return true;
			case "style.borderRightStyle":
				value = ReadBorderSideStyle(element, BorderSide.Right);
				return true;
			case "style.borderBottomStyle":
				value = ReadBorderSideStyle(element, BorderSide.Bottom);
				return true;
			case "style.borderLeftStyle":
				value = ReadBorderSideStyle(element, BorderSide.Left);
				return true;
			case "style.borderTop":
				return TryReadBorderSide(element, BorderSide.Top, out value);
			case "style.borderRight":
				return TryReadBorderSide(element, BorderSide.Right, out value);
			case "style.borderBottom":
				return TryReadBorderSide(element, BorderSide.Bottom, out value);
			case "style.borderLeft":
				return TryReadBorderSide(element, BorderSide.Left, out value);
			case "style.border":
				return TryReadUniformBorder(element, out value);
			case "style.borderTopWidth":
				value = CssPixels(
					ReadBorderThickness(element).Top,
					horizontal: false);
				return true;
			case "style.borderRightWidth":
				value = CssPixels(
					ReadBorderThickness(element).Right,
					horizontal: true);
				return true;
			case "style.borderBottomWidth":
				value = CssPixels(
					ReadBorderThickness(element).Bottom,
					horizontal: false);
				return true;
			case "style.borderLeftWidth":
				value = CssPixels(
					ReadBorderThickness(element).Left,
					horizontal: true);
				return true;
			case "style.borderTopLeftRadius":
				value = CssPixels(
					ReadCornerRadius(element).TopLeft,
					horizontal: true);
				return true;
			case "style.borderTopRightRadius":
				value = CssPixels(
					ReadCornerRadius(element).TopRight,
					horizontal: true);
				return true;
			case "style.borderBottomRightRadius":
				value = CssPixels(
					ReadCornerRadius(element).BottomRight,
					horizontal: true);
				return true;
			case "style.borderBottomLeftRadius":
				value = CssPixels(
					ReadCornerRadius(element).BottomLeft,
					horizontal: true);
				return true;
			case "style.backgroundColor"
				when ReadBackground(element) is null:
				value = "rgba(0, 0, 0, 0)";
				return true;
			case "style.backgroundColor":
				return TryReadBrush(ReadBackground(element), out value);
			case "style.backgroundImage":
				if (!HasNoBackgroundImage(element))
					return false;
				value = "none";
				return true;
			case "style.backgroundPosition":
				if (!HasNoBackgroundImage(element))
					return false;
				value = "0% 0%";
				return true;
			case "style.backgroundSize":
				if (!HasNoBackgroundImage(element))
					return false;
				value = "auto";
				return true;
			case "style.backgroundRepeat":
				if (!HasNoBackgroundImage(element))
					return false;
				value = "repeat";
				return true;
			case "style.background":
				return TryReadSolidBackgroundShorthand(element, out value);
			case "style.colorScheme":
				value = element.RequestedTheme switch
				{
					ElementTheme.Light => "light",
					ElementTheme.Dark => "dark",
					_ => "normal"
				};
				return true;
			case "style.color"
				or "style.fontFamily"
				or "style.fontSize"
				or "style.fontWeight"
				or "style.fontStyle"
				or "style.fontStretch"
				or "style.lineHeight"
				or "style.letterSpacing"
				or "style.textAlign"
				or "style.textDecorationLine"
				or "style.textOverflow"
				or "style.whiteSpace"
				or "style.direction":
				return TryReadActualTextStyle(
					element,
					context.PropertyName,
					out value);
			case "style.textTransform":
				return WinUiTextTransformBehavior.TryRead(element, out value);
			case "style.overflowWrap" or "style.wordBreak":
				return WinUiLineBreakingBehavior.TryRead(
					element,
					context.PropertyName,
					out value);
			case "style.writingMode":
				return TryReadAdvancedTextLayout(
					element,
					WinUiAdvancedTextLayoutBehavior.WritingModeProperty,
					out value);
			case "style.textIndent":
				return TryReadAdvancedTextLayout(
					element,
					WinUiAdvancedTextLayoutBehavior.TextIndentProperty,
					out value);
			case "style.wordSpacing":
				return TryReadAdvancedTextLayout(
					element,
					WinUiAdvancedTextLayoutBehavior.WordSpacingProperty,
					out value);
			case "style.contain":
				return WinUiContainBehavior.TryRead(element, out value);
			case "style.contentVisibility":
				return WinUiContentVisibilityBehavior.TryRead(
					element,
					out value);
			case "style.cursor":
				return HtmlCursorContract.TryReadEffective(element, out value);
			case "style.textDecoration":
				if (!TryReadActualTextStyle(
					element,
					"style.textDecorationLine",
					out var decorationLine)
					|| !TryReadActualTextStyle(
						element,
						"style.color",
						out var decorationColor))
				{
					return false;
				}
				value = $"{decorationLine} solid {decorationColor}";
				return true;
			case "style.font":
				return TryReadFontShorthand(element, out value);
			case "style.clipPath":
				return WinUiClipPathBehavior.TryRead(element, out value);
			case "style.boxShadow":
				return WinUiBoxShadowBehavior.TryRead(element, out value);
			case "style.textShadow":
				return WinUiTextShadowBehavior.TryRead(element, out value);
			case "style.filter":
				return WinUiFilterBehavior.TryRead(element, out value);
			case "style.backdropFilter":
				return WinUiBackdropFilterBehavior.TryRead(element, out value);
			case "style.fill":
				return element is Shape fillShape
					&& TryReadBrush(fillShape.Fill, out value);
			case "style.stroke":
				return element is Shape strokeShape
					&& TryReadBrush(strokeShape.Stroke, out value);
			case "style.strokeWidth":
				if (element is not Shape widthShape)
					return false;
				value = CssPixels(
					widthShape.StrokeThickness,
					horizontal: true);
				return true;
			case "style.strokeLinecap":
				if (element is not Shape capShape
					|| capShape.StrokeStartLineCap != capShape.StrokeEndLineCap
					|| capShape.StrokeStartLineCap != capShape.StrokeDashCap)
				{
					return false;
				}
				value = capShape.StrokeStartLineCap switch
				{
					PenLineCap.Round => "round",
					PenLineCap.Square => "square",
					_ => "butt"
				};
				return true;
			case "style.strokeLinejoin":
				if (element is not Shape joinShape)
					return false;
				value = joinShape.StrokeLineJoin switch
				{
					PenLineJoin.Round => "round",
					PenLineJoin.Bevel => "bevel",
					_ => "miter"
				};
				return true;
			case "style.transition"
				or "style.transitionProperty"
				or "style.transitionDuration"
				or "style.transitionTimingFunction"
				or "style.transitionDelay":
				return WinUiCssTransitionBehavior.TryRead(
					element,
					context.PropertyName,
					out value);
			case "style.animation"
				or "style.animationName"
				or "style.animationDuration"
				or "style.animationTimingFunction"
				or "style.animationDelay"
				or "style.animationIterationCount"
				or "style.animationDirection"
				or "style.animationFillMode"
				or "style.animationPlayState":
				return HtmlAnimationState.TryReadCssProperty(
					element,
					context.PropertyName,
					out value);
			case "style.flexDirection":
				return TryReadFlexDirection(context, element, out value);
			case "style.flexFlow":
				if (!TryReadFlexDirection(context, element, out var direction)
					|| !TryReadFlexWrap(context, element, out var wrap))
				{
					return false;
				}
				value = $"{direction} {wrap}";
				return true;
			case "style.flexWrap":
				return TryReadFlexWrap(context, element, out value);
			case "style.flexGrow":
				return TryReadFlexGrow(element, out value);
			case "style.flexShrink":
				return TryReadFlexShrink(element, out value);
			case "style.flexBasis":
				return TryReadFlexBasis(element, out value);
			case "style.flex":
				return TryReadFlex(element, out value);
			case "style.alignSelf":
				return TryReadAlignSelf(element, out value);
			case "style.justifyContent":
				return TryReadFlexContainerValue(
					context,
					element,
					static box => box.JustifyContent,
					out value);
			case "style.alignItems":
				return TryReadFlexContainerValue(
					context,
					element,
					static box => box.AlignItems,
					out value);
			case "style.alignContent":
				return TryReadAlignContent(context, element, out value);
			case "style.order":
				return TryReadFlexOrder(element, out value);
			case "style.rowGap":
				return TryReadGridGap(element, horizontal: false, out value);
			case "style.columnGap":
				return TryReadGridGap(element, horizontal: true, out value);
			case "style.gap":
				if (!TryReadGridGap(element, horizontal: false, out var rowGap)
					|| !TryReadGridGap(element, horizontal: true, out var columnGap))
				{
					return false;
				}
				value = rowGap == columnGap
					? rowGap
					: $"{rowGap} {columnGap}";
				return true;
			case "style.gridTemplateColumns":
				return TryReadGridTemplate(
					context,
					element,
					horizontal: true,
					out value);
			case "style.gridTemplateRows":
				return TryReadGridTemplate(
					context,
					element,
					horizontal: false,
					out value);
			case "style.gridTemplateAreas":
				return TryReadGridAutoValue(
					context,
					element,
					static box => box.GridTemplateAreas,
					out value);
			case "style.gridTemplate":
				return TryReadGridTemplateShorthand(
					context,
					element,
					out value);
			case "style.grid":
				return TryReadGridShorthand(
					context,
					element,
					out value);
			case "style.gridAutoColumns":
				return TryReadGridAutoValue(
					context,
					element,
					static box => box.GridAutoColumns,
					out value);
			case "style.gridAutoRows":
				return TryReadGridAutoValue(
					context,
					element,
					static box => box.GridAutoRows,
					out value);
			case "style.gridAutoFlow":
				return TryReadGridAutoValue(
					context,
					element,
					static box => box.GridAutoFlow,
					out value);
			case "style.gridRow":
				return TryReadGridPlacement(element, vertical: true, out value);
			case "style.gridColumn":
				return TryReadGridPlacement(element, vertical: false, out value);
			case "style.gridArea":
				if (element.Parent is not Grid)
				{
					return false;
				}
				var areaExpression =
					HtmlCssBoxGrid.GetGridAreaExpression(element);
				if (areaExpression.Length != 0)
				{
					value = areaExpression;
					return true;
				}
				var rowStart = Grid.GetRow(element) + 1;
				var columnStart = Grid.GetColumn(element) + 1;
				var rowEnd = rowStart + Grid.GetRowSpan(element);
				var columnEnd = columnStart + Grid.GetColumnSpan(element);
				value = $"{rowStart} / {columnStart} / {rowEnd} / {columnEnd}";
				return true;
			case "style.overflowX" or "style.overflowY":
				return WinUiOverflowClipBehavior.TryRead(
					element,
					context.PropertyName,
					out value);
			case "style.overflow":
				if (!WinUiOverflowClipBehavior.TryRead(
					element,
					"style.overflowX",
					out var overflowX)
					|| !WinUiOverflowClipBehavior.TryRead(
						element,
						"style.overflowY",
						out var overflowY))
				{
					return false;
				}
				value = overflowX == overflowY
					? overflowX
					: $"{overflowX} {overflowY}";
				return true;
			case "style.objectFit":
				if (element is HtmlImageView imageView)
				{
					if (imageView.TryReadObjectFit(out value))
						return true;
					return TryReadImageStretch(
						imageView.Image,
						out value);
				}
				if (element is not Image image)
					return false;
				return TryReadImageStretch(image, out value);
			case "style.objectPosition":
				if (element is HtmlImageView positionedImage)
					return positionedImage.TryReadObjectPosition(out value);
				return false;
			case "style.borderTopColor"
				or "style.borderRightColor"
				or "style.borderBottomColor"
				or "style.borderLeftColor":
				return TryReadBrush(ReadBorderBrush(element), out value);
			case "style.maxWidth":
				var cssMaxWidth = element is HtmlCssBoxGrid widthBox
					? widthBox.CssMaxWidth
					: element.MaxWidth;
				value = double.IsPositiveInfinity(cssMaxWidth)
					? "none"
					: cssMaxWidth.ToString(
						"R",
						CultureInfo.InvariantCulture);
				return true;
			case "style.maxHeight":
				var cssMaxHeight = element is HtmlCssBoxGrid heightBox
					? heightBox.CssMaxHeight
					: element.MaxHeight;
				value = double.IsPositiveInfinity(cssMaxHeight)
					? "none"
					: cssMaxHeight.ToString(
						"R",
						CultureInfo.InvariantCulture);
				return true;
			case "style.top" or "style.right" or "style.bottom" or "style.left":
				return TryReadPositionInset(
					element,
					context.PropertyName,
					out value);
			case "style.inset":
				return TryReadPositionInsetShorthand(element, out value);
			case "style.marginTop":
				value = CssPixels(element.Margin.Top, horizontal: false);
				return true;
			case "style.marginRight":
				value = CssPixels(element.Margin.Right, horizontal: true);
				return true;
			case "style.marginBottom":
				value = CssPixels(element.Margin.Bottom, horizontal: false);
				return true;
			case "style.marginLeft":
				value = CssPixels(element.Margin.Left, horizontal: true);
				return true;
			case "style.paddingTop":
				value = CssPixels(
					ReadPadding(element).Top,
					horizontal: false);
				return true;
			case "style.paddingRight":
				value = CssPixels(
					ReadPadding(element).Right,
					horizontal: true);
				return true;
			case "style.paddingBottom":
				value = CssPixels(
					ReadPadding(element).Bottom,
					horizontal: false);
				return true;
			case "style.paddingLeft":
				value = CssPixels(
					ReadPadding(element).Left,
					horizontal: true);
				return true;
			default:
				return false;
		}
	}

	private static bool TryReadImageStretch(
		Image image,
		out string value)
	{
		ArgumentNullException.ThrowIfNull(image);
		value = image.Stretch switch
		{
			Stretch.Uniform => "contain",
			Stretch.UniformToFill => "cover",
			Stretch.None => "none",
			Stretch.Fill => "fill",
			_ => string.Empty
		};
		return value.Length != 0;
	}

	private static bool TryReadAdvancedTextLayout(
		FrameworkElement root,
		DependencyProperty property,
		out string value)
	{
		if (WinUiAdvancedTextLayoutBehavior.TryRead(root, property, out value))
			return true;
		var pending = new Queue<DependencyObject>();
		pending.Enqueue(root);
		while (pending.Count > 0)
		{
			var current = pending.Dequeue();
			var count = VisualTreeHelper.GetChildrenCount(current);
			for (var index = 0; index < count; index++)
			{
				var child = VisualTreeHelper.GetChild(current, index);
				if (child is FrameworkElement candidate
					&& WinUiAdvancedTextLayoutBehavior.TryRead(candidate, property, out value))
					return true;
				pending.Enqueue(child);
			}
		}
		value = string.Empty;
		return false;
	}

	private bool TryReadFlexDirection(
		XamlPropertyQueryContext context,
		FrameworkElement element,
		out string value)
	{
		value = string.Empty;
		if (!TryGetMaterializedLayoutTarget(
			context,
			element,
			XamlElementMappingKind.FlexLayout))
		{
			return false;
		}
		if (element is HtmlCssBoxGrid box)
		{
			value = box.FlexDirection;
			return value.Length != 0;
		}
		var grid = ResolveLayoutGrid(element);
		if (grid is null)
			return false;
		if (grid.ColumnDefinitions.Count > 0
			&& grid.RowDefinitions.Count <= 1)
		{
			value = "row";
			return true;
		}
		if (grid.RowDefinitions.Count > 0
			&& grid.ColumnDefinitions.Count <= 1)
		{
			value = "column";
			return true;
		}
		return false;
	}

	private bool TryReadGridTemplateShorthand(
		XamlPropertyQueryContext context,
		FrameworkElement element,
		out string value)
	{
		value = string.Empty;
		if (element is not HtmlCssBoxGrid box
			|| !TryGetMaterializedLayoutTarget(
				context,
				element,
				XamlElementMappingKind.GridLayout)
			|| box.GridTemplateAreas != "none"
			|| !TryReadGridTemplate(
				context,
				element,
				horizontal: false,
				out var rows)
			|| !TryReadGridTemplate(
				context,
				element,
				horizontal: true,
				out var columns))
		{
			return false;
		}
		value = $"{rows} / {columns}";
		return true;
	}

	private bool TryReadGridShorthand(
		XamlPropertyQueryContext context,
		FrameworkElement element,
		out string value)
	{
		value = string.Empty;
		if (element is not HtmlCssBoxGrid box
			|| !TryGetMaterializedLayoutTarget(
				context,
				element,
				XamlElementMappingKind.GridLayout)
			|| !TryReadGridTemplate(
				context,
				element,
				horizontal: false,
				out var rows)
			|| !TryReadGridTemplate(
				context,
				element,
				horizontal: true,
				out var columns))
		{
			return false;
		}
		var flow = box.GridAutoFlow.Trim().ToLowerInvariant();
		var dense = flow.Contains("dense", StringComparison.Ordinal)
			? " dense"
			: string.Empty;
		if (flow.StartsWith("column", StringComparison.Ordinal))
		{
			value = $"{rows} / auto-flow{dense} {box.GridAutoColumns}";
			return true;
		}
		if (flow.StartsWith("row", StringComparison.Ordinal))
		{
			value = $"auto-flow{dense} {box.GridAutoRows} / {columns}";
			return true;
		}
		return false;
	}

	private bool TryReadFlexWrap(
		XamlPropertyQueryContext context,
		FrameworkElement element,
		out string value)
	{
		value = string.Empty;
		if (!TryReadFlexDirection(context, element, out var direction))
			return false;
		if (element is HtmlCssBoxGrid box)
		{
			value = box.FlexWrap;
			return value.Length != 0;
		}
		var grid = ResolveLayoutGrid(element);
		if (grid is null)
			return false;
		var children = grid.Children.OfType<FrameworkElement>().ToArray();
		var isWrapped = direction.StartsWith(
			"row",
			StringComparison.OrdinalIgnoreCase)
			? children.Select(Grid.GetRow).Distinct().Skip(1).Any()
			: children.Select(Grid.GetColumn).Distinct().Skip(1).Any();
		value = isWrapped ? "wrap" : "nowrap";
		return true;
	}

	private static bool TryReadFlexGrow(
		FrameworkElement element,
		out string value)
	{
		if (!HasLocalFlexItemValue(
			element,
			HtmlCssBoxGrid.FlexGrowProperty))
		{
			value = string.Empty;
			return false;
		}
		value = HtmlCssBoxGrid.GetFlexGrow(element).ToString(
			"R",
			CultureInfo.InvariantCulture);
		return true;
	}

	private static bool TryReadFlexShrink(
		FrameworkElement element,
		out string value)
	{
		if (!HasLocalFlexItemValue(
			element,
			HtmlCssBoxGrid.FlexShrinkProperty))
		{
			value = string.Empty;
			return false;
		}
		value = HtmlCssBoxGrid.GetFlexShrink(element).ToString(
			"R",
			CultureInfo.InvariantCulture);
		return true;
	}

	private static bool TryReadFlexBasis(
		FrameworkElement element,
		out string value)
	{
		if (!HasLocalFlexItemValue(
			element,
			HtmlCssBoxGrid.FlexBasisProperty))
		{
			value = string.Empty;
			return false;
		}
		value = HtmlCssBoxGrid.GetFlexBasis(element);
		return true;
	}

	private static bool TryReadFlex(
		FrameworkElement element,
		out string value)
	{
		if (!TryReadFlexGrow(element, out var grow)
			|| !TryReadFlexShrink(element, out var shrink)
			|| !TryReadFlexBasis(element, out var basis))
		{
			value = string.Empty;
			return false;
		}
		value = $"{grow} {shrink} {basis}";
		return true;
	}

	private static bool TryReadAlignSelf(
		FrameworkElement element,
		out string value)
	{
		if (!HasLocalFlexItemValue(
			element,
			HtmlCssBoxGrid.AlignSelfProperty))
		{
			value = string.Empty;
			return false;
		}
		value = HtmlCssBoxGrid.GetAlignSelf(element);
		return true;
	}

	private static bool TryReadFlexOrder(
		FrameworkElement element,
		out string value)
	{
		if (!HasLocalFlexItemValue(element, HtmlCssBoxGrid.OrderProperty))
		{
			value = string.Empty;
			return false;
		}
		value = HtmlCssBoxGrid.GetOrder(element).ToString(
			CultureInfo.InvariantCulture);
		return true;
	}

	private bool TryReadFlexContainerValue(
		XamlPropertyQueryContext context,
		FrameworkElement element,
		Func<HtmlCssBoxGrid, string> selector,
		out string value)
	{
		value = string.Empty;
		if (element is not HtmlCssBoxGrid box
			|| !TryGetMaterializedLayoutTarget(
				context,
				element,
				XamlElementMappingKind.FlexLayout))
		{
			return false;
		}
		value = selector(box);
		return value.Length != 0;
	}

	private bool TryReadAlignContent(
		XamlPropertyQueryContext context,
		FrameworkElement element,
		out string value)
	{
		value = string.Empty;
		if (element is HtmlCssBoxGrid
			{
				FlexWrap: not "nowrap",
				FlexDirection: var direction
			} wrapped
			&& direction.StartsWith(
				"column",
				StringComparison.OrdinalIgnoreCase)
			&& !wrapped.LayoutRoot.Children
				.OfType<HtmlCssFlexLineGrid>()
				.Any())
		{
			return false;
		}
		return TryReadFlexContainerValue(
			context,
			element,
			static box => box.AlignContent,
			out value);
	}

	private static bool HasLocalFlexItemValue(
		DependencyObject element,
		DependencyProperty property) =>
		element.ReadLocalValue(property) != DependencyProperty.UnsetValue;

	private bool TryReadGridGap(
		FrameworkElement element,
		bool horizontal,
		out string value)
	{
		if (element is HtmlCssBoxGrid box)
		{
			value = CssPixels(
				horizontal ? box.ColumnGap : box.RowGap,
				horizontal);
			return true;
		}
		var grid = ResolveLayoutGrid(element);
		if (grid is null)
		{
			value = string.Empty;
			return false;
		}
		value = CssPixels(
			horizontal ? grid.ColumnSpacing : grid.RowSpacing,
			horizontal);
		return true;
	}

	private bool TryReadGridTemplate(
		XamlPropertyQueryContext context,
		FrameworkElement element,
		bool horizontal,
		out string value)
	{
		value = string.Empty;
		if (!TryGetMaterializedLayoutTarget(
			context,
			element,
			XamlElementMappingKind.GridLayout))
		{
			return false;
		}
		if (element is HtmlCssBoxGrid box)
		{
			var expression = horizontal
				? box.GridTemplateColumns
				: box.GridTemplateRows;
			if (!expression.Equals("none", StringComparison.OrdinalIgnoreCase))
			{
				value = expression;
				return true;
			}
		}
		var grid = ResolveLayoutGrid(element);
		if (grid is null)
			return false;
		var tracks = horizontal
			? grid.ColumnDefinitions.Select(static item => item.Width).ToArray()
			: grid.RowDefinitions.Select(static item => item.Height).ToArray();
		if (tracks.Length == 0)
		{
			value = "none";
			return true;
		}
		value = string.Join(
			' ',
			tracks.Select(track => FormatGridLength(track, horizontal)));
		return true;
	}

	private bool TryReadGridAutoValue(
		XamlPropertyQueryContext context,
		FrameworkElement element,
		Func<HtmlCssBoxGrid, string> selector,
		out string value)
	{
		value = string.Empty;
		if (element is not HtmlCssBoxGrid { LayoutMode: "grid" } box
			|| !TryGetMaterializedLayoutTarget(
				context,
				element,
				XamlElementMappingKind.GridLayout))
		{
			return false;
		}
		value = selector(box);
		return value.Length != 0;
	}

	private static bool TryReadGridPlacement(
		FrameworkElement element,
		bool vertical,
		out string value)
	{
		if (element.Parent is not Grid)
		{
			value = string.Empty;
			return false;
		}
		var expression = vertical
			? HtmlCssBoxGrid.GetGridRowExpression(element)
			: HtmlCssBoxGrid.GetGridColumnExpression(element);
		if (expression.Length != 0)
		{
			value = expression;
			return true;
		}
		var start = (vertical ? Grid.GetRow(element) : Grid.GetColumn(element)) + 1;
		var span = vertical ? Grid.GetRowSpan(element) : Grid.GetColumnSpan(element);
		var autoStart = vertical
			? HtmlCssBoxGrid.GetGridRowAuto(element)
			: HtmlCssBoxGrid.GetGridColumnAuto(element);
		value = autoStart
			? span > 1 ? $"span {span}" : "auto"
			: span > 1
				? $"{start} / span {span}"
				: start.ToString(CultureInfo.InvariantCulture);
		return true;
	}

	private string FormatGridLength(GridLength track, bool horizontal) =>
		track.GridUnitType switch
		{
			GridUnitType.Auto => "auto",
			GridUnitType.Star =>
				track.Value.ToString("R", CultureInfo.InvariantCulture) + "fr",
			_ => CssPixels(track.Value, horizontal)
		};

	private bool TryGetMaterializedLayoutTarget(
		XamlPropertyQueryContext context,
		FrameworkElement element,
		XamlElementMappingKind mappingKind) =>
		designRuntime?.Layout.XamlTargets.Any(binding =>
			ReferenceEquals(binding.Target, element)
			&& binding.Owner.DocumentScope.Equals(
				context.DocumentScope,
				StringComparison.Ordinal)
			&& binding.Owner.XPath.Equals(
				context.XPath,
				StringComparison.Ordinal)
			&& binding.MappingKind == mappingKind
			&& binding.IsMaterialized) == true;

	private static Grid? ResolveLayoutGrid(FrameworkElement element) =>
		element switch
		{
			HtmlCssBoxGrid box => box.LayoutRoot,
			Grid grid => grid,
			_ => null
		};

	private bool TryReadActualTextStyle(
		FrameworkElement element,
		string propertyName,
		out string value)
	{
		value = string.Empty;
		var text = element as TextBlock;
		var control = element as Control;
		var canvas = element as HtmlCanvasPanel;
		switch (propertyName)
		{
			case "style.color":
				var foreground = text?.Foreground ?? control?.Foreground ?? canvas?.Foreground;
				if (foreground is null)
					WinUiInheritedTextStyleBehavior.TryGetForeground(element, out foreground);
				return TryReadBrush(foreground, out value);
			case "style.fontFamily":
				var fontFamily = text?.FontFamily ?? control?.FontFamily ?? canvas?.FontFamily;
				if (fontFamily is null)
					WinUiInheritedTextStyleBehavior.TryGetFontFamily(element, out fontFamily);
				value = fontFamily?.Source ?? string.Empty;
				return value.Length != 0;
			case "style.fontSize":
				double? fontSize = null;
				if (WinUiInheritedTextStyleBehavior.TryGetFontSize(
					element,
					out var hostedFontSize))
				{
					fontSize = hostedFontSize;
				}
				else
				{
					fontSize = text?.FontSize
						?? control?.FontSize
						?? canvas?.FontSize;
				}
				if (fontSize is not { } size || !double.IsFinite(size))
					return false;
				value = CssPixels(size, horizontal: false);
				return true;
			case "style.fontWeight":
				var weight = text?.FontWeight.Weight
					?? control?.FontWeight.Weight
					?? canvas?.FontWeight.Weight;
				if (weight is null
					&& WinUiInheritedTextStyleBehavior.TryGetFontWeight(element, out var hostedWeight))
					weight = hostedWeight.Weight;
				if (weight is not { } numericWeight)
					return false;
				value = numericWeight.ToString(CultureInfo.InvariantCulture);
				return true;
			case "style.fontStyle":
				var fontStyle = text?.FontStyle ?? control?.FontStyle ?? canvas?.FontStyle;
				if (fontStyle is null
					&& WinUiInheritedTextStyleBehavior.TryGetFontStyle(element, out var hostedStyle))
					fontStyle = hostedStyle;
				if (fontStyle is not { } actualStyle)
					return false;
				value = actualStyle.ToString().ToLowerInvariant();
				return true;
			case "style.fontStretch":
				var fontStretch = text?.FontStretch ?? control?.FontStretch ?? canvas?.FontStretch;
				if (fontStretch is null
					&& WinUiInheritedTextStyleBehavior.TryGetFontStretch(element, out var hostedStretch))
					fontStretch = hostedStretch;
				if (fontStretch is not { } actualStretch)
					return false;
				value = ToCssFontStretch(actualStretch);
				return true;
			case "style.lineHeight":
				var lineHeight = text?.LineHeight
					?? (element as HtmlCssBoxGrid)?.TextLineHeight
					?? (element as HtmlVerticalTextControl)?.TextLineHeight
					?? canvas?.TextLineHeight;
				if (lineHeight is null
					&& WinUiInheritedTextStyleBehavior.TryGetLineHeight(element, out var hostedLineHeight))
					lineHeight = hostedLineHeight;
				if (lineHeight is not { } actualLineHeight)
					return false;
				value = actualLineHeight <= 0
					? "normal"
					: CssPixels(actualLineHeight, horizontal: false);
				return true;
			case "style.letterSpacing":
				var characterSpacing = text?.CharacterSpacing
					?? control?.CharacterSpacing
					?? canvas?.CharacterSpacing;
				var actualFontSize = text?.FontSize ?? control?.FontSize ?? canvas?.FontSize;
				if (characterSpacing is null
					&& WinUiInheritedTextStyleBehavior.TryGetCharacterSpacing(element, out var hostedSpacing))
					characterSpacing = hostedSpacing;
				if (actualFontSize is null
					&& WinUiInheritedTextStyleBehavior.TryGetFontSize(element, out var hostedSpacingFontSize))
					actualFontSize = hostedSpacingFontSize;
				if (characterSpacing is not { } spacing
					|| actualFontSize is not { } spacingFontSize)
				{
					return false;
				}
				value = CssPixels(
					spacing / 1000d * spacingFontSize,
					horizontal: false);
				return true;
			case "style.textAlign":
				var alignment = text?.TextAlignment
					?? (control as TextBox)?.TextAlignment
					?? (element as HtmlCssBoxGrid)?.TextAlignment
					?? (element as HtmlVerticalTextControl)?.TextAlignment
					?? canvas?.TextAlignment;
				if (alignment is null
					&& WinUiInheritedTextStyleBehavior.TryGetTextAlignment(element, out var hostedAlignment))
					alignment = hostedAlignment;
				if (alignment is not { } actualAlignment)
					return false;
				value = actualAlignment.ToString().ToLowerInvariant();
				return true;
			case "style.textDecorationLine":
				var decoration = text?.TextDecorations
					?? (element as HtmlCssBoxGrid)?.TextDecorations
					?? (element as HtmlVerticalTextControl)?.TextDecorations
					?? canvas?.TextDecorations;
				if (decoration is null
					&& WinUiInheritedTextStyleBehavior.TryGetTextDecorations(element, out var hostedDecoration))
					decoration = hostedDecoration;
				if (decoration is not { } actualDecoration)
					return false;
				var parts = new List<string>(2);
				if ((actualDecoration
					& Windows.UI.Text.TextDecorations.Underline) != 0)
				{
					parts.Add("underline");
				}
				if ((actualDecoration
					& Windows.UI.Text.TextDecorations.Strikethrough) != 0)
				{
					parts.Add("line-through");
				}
				value = parts.Count == 0 ? "none" : string.Join(' ', parts);
				return true;
			case "style.whiteSpace":
				if (text is not null)
					value = text.TextWrapping == TextWrapping.NoWrap
						? "nowrap"
						: "normal";
				else if (element is HtmlCssBoxGrid whiteSpaceBox)
					value = whiteSpaceBox.WhiteSpace;
				else if (element is HtmlVerticalTextControl verticalWhiteSpace)
					value = verticalWhiteSpace.WhiteSpace;
				else if (canvas is not null)
					value = canvas.WhiteSpace;
				else if (WinUiInheritedTextStyleBehavior.TryGetWhiteSpace(element, out var hostedWhiteSpace))
					value = hostedWhiteSpace ?? string.Empty;
				else
					return false;
				return true;
			case "style.textOverflow":
				if (text is not null)
				{
					value = text.TextTrimming == TextTrimming.CharacterEllipsis
						? "ellipsis"
						: "clip";
				}
				else if (element is HtmlCssBoxGrid overflowBox)
					value = overflowBox.TextOverflow;
				else if (element is HtmlVerticalTextControl verticalOverflow)
					value = verticalOverflow.TextOverflow;
				else if (canvas is not null)
					value = canvas.TextOverflow;
				else if (WinUiInheritedTextStyleBehavior.TryGetTextOverflow(element, out var hostedTextOverflow))
					value = hostedTextOverflow ?? string.Empty;
				else
					return false;
				return true;
			case "style.direction":
				value = element.FlowDirection == FlowDirection.RightToLeft
					? "rtl"
					: "ltr";
				return true;
			default:
				return false;
		}
	}

	private bool TryReadFontShorthand(
		FrameworkElement element,
		out string value)
	{
		value = string.Empty;
		if (!TryReadActualTextStyle(element, "style.fontStyle", out var style)
			|| !TryReadActualTextStyle(element, "style.fontWeight", out var weight)
			|| !TryReadActualTextStyle(element, "style.fontStretch", out var stretch)
			|| !TryReadActualTextStyle(element, "style.fontSize", out var size)
			|| !TryReadActualTextStyle(element, "style.lineHeight", out var lineHeight)
			|| !TryReadActualTextStyle(element, "style.fontFamily", out var family))
		{
			return false;
		}
		var parts = new List<string>(5);
		if (style != "normal")
			parts.Add(style);
		if (weight != "400")
			parts.Add(weight);
		if (stretch != "normal")
			parts.Add(stretch);
		parts.Add(lineHeight == "normal" ? size : $"{size} / {lineHeight}");
		parts.Add(QuoteCssFontFamilyIfRequired(family));
		value = string.Join(' ', parts);
		return true;
	}

	private static string QuoteCssFontFamilyIfRequired(string family) =>
		family.Any(char.IsWhiteSpace)
			? $"\"{family.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
			: family;

	private static bool TryReadAutomaticInset(
		FrameworkElement element,
		DependencyProperty property,
		out string value)
	{
		var local = element.ReadLocalValue(property);
		if (local == DependencyProperty.UnsetValue
			|| local is double number && double.IsNaN(number))
		{
			value = "auto";
			return true;
		}
		value = string.Empty;
		return false;
	}

	private string CssThicknessShorthand(Thickness thickness) =>
		CompressCssBoxValues(
			CssPixels(thickness.Top, horizontal: false),
			CssPixels(thickness.Right, horizontal: true),
			CssPixels(thickness.Bottom, horizontal: false),
			CssPixels(thickness.Left, horizontal: true));

	private string CssCornerRadiusShorthand(CornerRadius radius) =>
		CompressCssBoxValues(
			CssPixels(radius.TopLeft, horizontal: true),
			CssPixels(radius.TopRight, horizontal: true),
			CssPixels(radius.BottomRight, horizontal: true),
			CssPixels(radius.BottomLeft, horizontal: true));

	private static string CompressCssBoxValues(
		string top,
		string right,
		string bottom,
		string left) =>
		top == right && top == bottom && top == left
			? top
			: top == bottom && right == left
				? $"{top} {right}"
				: right == left
					? $"{top} {right} {bottom}"
					: $"{top} {right} {bottom} {left}";

	private bool TryReadPositionInset(
		FrameworkElement element,
		string propertyName,
		out string value)
	{
		value = string.Empty;
		var state = WinUiCssSemantic.GetPositioningState(element);
		if (state is null)
			return false;
		if (Math.Abs(element.Translation.X - state.X) > .001
			|| Math.Abs(element.Translation.Y - state.Y) > .001)
		{
			return false;
		}
		var inset = propertyName switch
		{
			"style.top" => state.Top,
			"style.right" => state.Right,
			"style.bottom" => state.Bottom,
			"style.left" => state.Left,
			_ => null
		};
		if (inset is null)
		{
			value = "auto";
			return true;
		}
		value = CssPixels(
			inset.Value,
			horizontal: propertyName is "style.left" or "style.right");
		return true;
	}

	private bool TryReadPositionInsetShorthand(
		FrameworkElement element,
		out string value)
	{
		value = string.Empty;
		if (!TryReadPositionInset(element, "style.top", out var top)
			|| !TryReadPositionInset(element, "style.right", out var right)
			|| !TryReadPositionInset(element, "style.bottom", out var bottom)
			|| !TryReadPositionInset(element, "style.left", out var left))
		{
			return false;
		}
		value = CompressCssBoxValues(top, right, bottom, left);
		return true;
	}

	private static string ToCssFontStretch(
		Windows.UI.Text.FontStretch stretch) =>
		stretch switch
		{
			Windows.UI.Text.FontStretch.UltraCondensed => "ultra-condensed",
			Windows.UI.Text.FontStretch.ExtraCondensed => "extra-condensed",
			Windows.UI.Text.FontStretch.Condensed => "condensed",
			Windows.UI.Text.FontStretch.SemiCondensed => "semi-condensed",
			Windows.UI.Text.FontStretch.SemiExpanded => "semi-expanded",
			Windows.UI.Text.FontStretch.Expanded => "expanded",
			Windows.UI.Text.FontStretch.ExtraExpanded => "extra-expanded",
			Windows.UI.Text.FontStretch.UltraExpanded => "ultra-expanded",
			_ => "normal"
		};

	private bool TryReadMaterializedDisplay(
		XamlPropertyQueryContext context,
		FrameworkElement element,
		out string value)
	{
		value = string.Empty;
		var target = designRuntime?.Layout.XamlTargets.SingleOrDefault(
			binding =>
				ReferenceEquals(binding.Target, element)
				&& binding.Owner.DocumentScope.Equals(
					context.DocumentScope,
					StringComparison.Ordinal)
				&& binding.Owner.XPath.Equals(
					context.XPath,
					StringComparison.Ordinal)
				&& binding.IsMaterialized);
		if (target is null)
			return false;
		value = target.MappingKind switch
		{
			XamlElementMappingKind.FlexLayout => "flex",
			XamlElementMappingKind.GridLayout => "grid",
			XamlElementMappingKind.TableLayout => "table",
			XamlElementMappingKind.ViewportRoot
				or XamlElementMappingKind.BlockFlow
				or XamlElementMappingKind.ConservativeContainer
				or XamlElementMappingKind.PositionedLayout
				or XamlElementMappingKind.TypeDefault => "block",
			_ => string.Empty
		};
		return value.Length != 0;
	}

	private string CssPixels(double value, bool horizontal)
	{
		var scale = 1d;
		if (designRuntime is not null)
		{
			var viewport = designRuntime.Layout.Viewport;
			var runtime = horizontal
				? viewport.RuntimeWidth
				: viewport.RuntimeHeight;
			var css = horizontal
				? viewport.CssRuntimeWidth
				: viewport.CssRuntimeHeight;
			if (runtime > 0 && css > 0)
				scale = runtime / css;
		}
		return (value / scale).ToString(
			"R",
			CultureInfo.InvariantCulture) + "px";
	}

	private static Thickness ReadBorderThickness(FrameworkElement element) =>
		element switch
		{
			Grid grid => grid.BorderThickness,
			Control control => control.BorderThickness,
			Border border => border.BorderThickness,
			_ => new()
		};

	private static Thickness ReadPadding(FrameworkElement element) =>
		element switch
		{
			Control control => control.Padding,
			Border border => border.Padding,
			Grid => new(),
			_ => new()
		};

	private static CornerRadius ReadCornerRadius(FrameworkElement element) =>
		element switch
		{
			Grid grid => grid.CornerRadius,
			Control control => control.CornerRadius,
			Border border => border.CornerRadius,
			_ => new()
		};

	private static Brush? ReadBackground(FrameworkElement element) =>
		element switch
		{
			Panel panel => panel.Background,
			Control control => control.Background,
			Border border => border.Background,
			_ => null
		};

	private static bool HasNoBackgroundImage(FrameworkElement element) =>
		ReadBackground(element) is null or SolidColorBrush;

	private static bool TryReadSolidBackgroundShorthand(
		FrameworkElement element,
		out string value)
	{
		value = string.Empty;
		if (!HasNoBackgroundImage(element))
			return false;
		var background = ReadBackground(element);
		var color = "rgba(0, 0, 0, 0)";
		if (background is not null && !TryReadBrush(background, out color))
			return false;
		value = $"{color} none repeat scroll 0% 0% / auto padding-box border-box";
		return true;
	}

	private static Brush? ReadBorderBrush(FrameworkElement element) =>
		element switch
		{
			Grid grid => grid.BorderBrush,
			Control control => control.BorderBrush,
			Border border => border.BorderBrush,
			_ => null
		};

	private enum BorderSide
	{
		Top,
		Right,
		Bottom,
		Left
	}

	private static double ReadBorderSideWidth(
		FrameworkElement element,
		BorderSide side)
	{
		var border = ReadBorderThickness(element);
		return side switch
		{
			BorderSide.Top => border.Top,
			BorderSide.Right => border.Right,
			BorderSide.Bottom => border.Bottom,
			BorderSide.Left => border.Left,
			_ => throw new ArgumentOutOfRangeException(nameof(side))
		};
	}

	private static string ReadBorderSideStyle(
		FrameworkElement element,
		BorderSide side) =>
		ReadBorderSideWidth(element, side) > 0 ? "solid" : "none";

	private bool TryReadBorderSide(
		FrameworkElement element,
		BorderSide side,
		out string value)
	{
		value = string.Empty;
		if (!TryReadBrush(ReadBorderBrush(element), out var color))
			return false;
		var width = CssPixels(
			ReadBorderSideWidth(element, side),
			horizontal: side is BorderSide.Left or BorderSide.Right);
		value = $"{width} {ReadBorderSideStyle(element, side)} {color}";
		return true;
	}

	private bool TryReadUniformBorder(
		FrameworkElement element,
		out string value)
	{
		value = string.Empty;
		var border = ReadBorderThickness(element);
		if (border.Top != border.Right
			|| border.Top != border.Bottom
			|| border.Top != border.Left)
		{
			return false;
		}
		return TryReadBorderSide(element, BorderSide.Top, out value);
	}

	private static string CssQuadShorthand(
		string top,
		string right,
		string bottom,
		string left)
	{
		if (top == right && top == bottom && top == left)
			return top;
		if (top == bottom && right == left)
			return $"{top} {right}";
		if (right == left)
			return $"{top} {right} {bottom}";
		return $"{top} {right} {bottom} {left}";
	}

	private static bool TryReadBrush(Brush? brush, out string value)
	{
		if (brush is not SolidColorBrush solid)
		{
			value = string.Empty;
			return false;
		}
		var color = solid.Color;
		value = color.A == byte.MaxValue
			? $"rgb({color.R}, {color.G}, {color.B})"
			: $"rgba({color.R}, {color.G}, {color.B}, "
				+ $"{(color.A / 255d).ToString(
					"R",
					CultureInfo.InvariantCulture)})";
		return true;
	}

	private bool TryReadFrameworkDimension(
		XamlPropertyQueryContext context,
		FrameworkElement element,
		out string value)
	{
		if (context.Slot == XamlPropertyDataSlot.Initialization
			&& TryReadFullParentDimension(
				context.PropertyName,
				element,
				out value))
		{
			return true;
		}
		double number;
		if (context.Slot == XamlPropertyDataSlot.Runtime)
		{
			if (TryReadComputedLayoutDimension(
				context.PropertyName,
				element,
				out number))
			{
				value = number.ToString(
					"R",
					CultureInfo.InvariantCulture);
				return true;
			}
			number = context.PropertyName switch
			{
				"style.width" => ReadRuntimeBorderBoxDimension(
					element,
					horizontal: true),
				"style.height" => ReadRuntimeBorderBoxDimension(
					element,
					horizontal: false),
				"style.minWidth" => element is HtmlCssBoxGrid runtimeWidthBox
					? runtimeWidthBox.CssMinWidth
					: element.MinWidth,
				"style.maxWidth" => element is HtmlCssBoxGrid runtimeMaxWidthBox
					? runtimeMaxWidthBox.CssMaxWidth
					: element.MaxWidth,
				"style.minHeight" => element is HtmlCssBoxGrid runtimeHeightBox
					? runtimeHeightBox.CssMinHeight
					: element.MinHeight,
				"style.maxHeight" => element is HtmlCssBoxGrid runtimeMaxHeightBox
					? runtimeMaxHeightBox.CssMaxHeight
					: element.MaxHeight,
				_ => double.NaN
			};
		}
		else
		{
			number = context.PropertyName switch
			{
				"style.width" => element.Width,
				"style.height" => element.Height,
				"style.minWidth" => element is HtmlCssBoxGrid widthBox
					? widthBox.CssMinWidth
					: element.MinWidth,
				"style.maxWidth" => element is HtmlCssBoxGrid maxWidthBox
					? maxWidthBox.CssMaxWidth
					: element.MaxWidth,
				"style.minHeight" => element is HtmlCssBoxGrid heightBox
					? heightBox.CssMinHeight
					: element.MinHeight,
				"style.maxHeight" => element is HtmlCssBoxGrid maxHeightBox
					? maxHeightBox.CssMaxHeight
					: element.MaxHeight,
				_ => double.NaN
			};
		}
		if (context.PropertyName is "style.width" or "style.height"
			&& double.IsFinite(number))
		{
			number = ReadCssBoxDimension(
				element,
				number,
				horizontal: context.PropertyName == "style.width");
		}
		value = double.IsFinite(number)
			? number.ToString("R", CultureInfo.InvariantCulture)
			: context.Slot == XamlPropertyDataSlot.Initialization
				&& (context.PropertyName is "style.width" or "style.height")
				&& double.IsNaN(number)
					? "auto"
					: context.Slot == XamlPropertyDataSlot.Initialization
						&& (context.PropertyName is
							"style.maxWidth" or "style.maxHeight")
						&& double.IsPositiveInfinity(number)
							? "none"
							: string.Empty;
		return value.Length != 0;
	}

	private bool TryReadFullParentDimension(
		string propertyName,
		FrameworkElement element,
		out string value)
	{
		value = string.Empty;
		var horizontal = propertyName == "style.width";
		if (!horizontal && propertyName != "style.height")
			return false;
		if (horizontal
			? !double.IsNaN(element.Width)
				|| element.HorizontalAlignment
					!= HorizontalAlignment.Stretch
			: !double.IsNaN(element.Height)
				|| element.VerticalAlignment
					!= VerticalAlignment.Stretch)
		{
			return false;
		}
		if (ReferenceEquals(element, root))
		{
			value = "100%";
			return true;
		}
		var parent = VisualTreeHelper.GetParent(element) as FrameworkElement
			?? element.Parent as FrameworkElement;
		if (parent is null)
			return false;
		var elementExtent = horizontal
			? element.ActualWidth
			: element.ActualHeight;
		var parentExtent = horizontal
			? parent.ActualWidth
			: parent.ActualHeight;
		if (parentExtent <= 0
			|| Math.Abs(elementExtent - parentExtent) > 0.01)
		{
			return false;
		}
		value = "100%";
		return true;
	}

	private static bool IsCssExpression(string value) =>
		value.Contains("var(", StringComparison.OrdinalIgnoreCase)
		|| value.Contains("calc(", StringComparison.OrdinalIgnoreCase)
		|| value.Contains("min(", StringComparison.OrdinalIgnoreCase)
		|| value.Contains("max(", StringComparison.OrdinalIgnoreCase)
		|| value.Contains("clamp(", StringComparison.OrdinalIgnoreCase);

	private bool TryReadComputedLayoutDimension(
		string propertyName,
		FrameworkElement element,
		out double value)
	{
		value = double.NaN;
		if (designRuntime is null)
			return false;
		var viewport = designRuntime.Layout.Viewport;
		var runtimeWidth = ReadCssBoxDimension(
			element,
			ReadRuntimeBorderBoxDimension(element, horizontal: true),
			horizontal: true);
		var runtimeHeight = ReadCssBoxDimension(
			element,
			ReadRuntimeBorderBoxDimension(element, horizontal: false),
			horizontal: false);
		switch (propertyName)
		{
			case "style.width"
				when viewport.RuntimeWidth > 0
					&& viewport.CssRuntimeWidth > 0:
				value = runtimeWidth
					* viewport.CssRuntimeWidth
					/ viewport.RuntimeWidth;
				break;
			case "style.height"
				when viewport.RuntimeHeight > 0
					&& viewport.CssRuntimeHeight > 0:
				value = runtimeHeight
					* viewport.CssRuntimeHeight
					/ viewport.RuntimeHeight;
				break;
			case "style.minWidth"
				or "style.maxWidth"
				when viewport.RuntimeWidth > 0
					&& viewport.CssRuntimeWidth > 0:
				value = (propertyName == "style.minWidth"
						? element.MinWidth
						: element.MaxWidth)
					* viewport.CssRuntimeWidth
					/ viewport.RuntimeWidth;
				break;
			case "style.minHeight"
				or "style.maxHeight"
				when viewport.RuntimeHeight > 0
					&& viewport.CssRuntimeHeight > 0:
				value = (propertyName == "style.minHeight"
						? element.MinHeight
						: element.MaxHeight)
					* viewport.CssRuntimeHeight
					/ viewport.RuntimeHeight;
				break;
		}
		return double.IsFinite(value);
	}

	private static double ReadRuntimeBorderBoxDimension(
		FrameworkElement element,
		bool horizontal)
	{
		var explicitSize = horizontal ? element.Width : element.Height;
		if (double.IsFinite(explicitSize))
			return Math.Max(0, explicitSize);
		var slot = Microsoft.UI.Xaml.Controls.Primitives.LayoutInformation
			.GetLayoutSlot(element);
		var stretches = horizontal
			? element.HorizontalAlignment == HorizontalAlignment.Stretch
			: element.VerticalAlignment == VerticalAlignment.Stretch;
		if (stretches)
		{
			var margin = horizontal
				? element.Margin.Left + element.Margin.Right
				: element.Margin.Top + element.Margin.Bottom;
			var allocated = (horizontal ? slot.Width : slot.Height) - margin;
			if (double.IsFinite(allocated) && allocated >= 0)
				return allocated;
		}
		return horizontal ? element.ActualWidth : element.ActualHeight;
	}

	private static double ReadCssBoxDimension(
		FrameworkElement element,
		double borderBoxLength,
		bool horizontal)
	{
		if (!HtmlCssBoxGrid.GetBoxSizing(element).Equals(
			"content-box",
			StringComparison.OrdinalIgnoreCase))
		{
			return borderBoxLength;
		}
		var padding = ReadPadding(element);
		var border = ReadBorderThickness(element);
		var chrome = horizontal
			? padding.Left + padding.Right + border.Left + border.Right
			: padding.Top + padding.Bottom + border.Top + border.Bottom;
		return Math.Max(0, borderBoxLength - chrome);
	}

	private bool TryReadGeometry(
		XamlPropertyQueryContext context,
		FrameworkElement element,
		out string value)
	{
		// Runtime geometry belongs to the DOM element's primary XAML box. A
		// materialized slot target may be an inner TextBlock, filtered visual, or
		// composite-property receiver; using it here changes element identity and
		// can report a detached (0,0) box for a correctly attached outer carrier.
		if (context.Element.XamlElement is FrameworkElement primaryElement)
			element = primaryElement;
		var geometryRoot = _geometryRootsByDocumentScope.TryGetValue(
			context.DocumentScope,
			out var scopedRoot)
				? scopedRoot
				: root;
		var bounds = new Windows.Foundation.Rect(
			0,
			0,
			element.ActualWidth,
			element.ActualHeight);
		var origin = new Windows.Foundation.Point(0, 0);
		try
		{
			var transform = element.TransformToVisual(geometryRoot);
			origin = transform.TransformPoint(origin);
			bounds = transform.TransformBounds(bounds);
			if (element.ActualWidth == 0
				&& element.ActualHeight == 0
				&& origin.X == 0
				&& origin.Y == 0
				&& !ReferenceEquals(element, geometryRoot))
			{
				origin = TryReadDetachedGridSlotOrigin(
					context.Element,
					element,
					geometryRoot,
					out var gridOrigin)
						? gridOrigin
						: ReadActualOffsetToRoot(element, geometryRoot);
			}
		}
		catch (ArgumentException)
		{
			value = string.Empty;
			return false;
		}
		var number = context.PropertyName switch
		{
			"rect.x" => (element.ActualWidth == 0 && element.ActualHeight == 0
				? origin.X
				: bounds.X) * _geometryEvidenceScale.Width,
			"rect.y" => (element.ActualWidth == 0 && element.ActualHeight == 0
				? origin.Y
				: bounds.Y) * _geometryEvidenceScale.Height,
			"rect.width" => bounds.Width * _geometryEvidenceScale.Width,
			"rect.height" => bounds.Height * _geometryEvidenceScale.Height,
			_ => double.NaN
		};
		value = double.IsFinite(number)
			? number.ToString("R", CultureInfo.InvariantCulture)
			: string.Empty;
		return value.Length > 0;
	}

	private static bool TryReadDetachedGridSlotOrigin(
		DomElement source,
		FrameworkElement element,
		FrameworkElement geometryRoot,
		out Windows.Foundation.Point origin)
	{
		origin = default;
		if (source.Parent?.XamlElement is not DependencyObject owner)
			return false;
		if (owner is HtmlFilteredElementHost filtered)
			owner = filtered.InnerElement;
		var layoutRoot = owner switch
		{
			HtmlCssBoxGrid cssBox => cssBox.LayoutRoot,
			HtmlInteractiveFlexPanel interactive => interactive.LayoutRoot,
			Grid grid => grid,
			_ => null
		};
		if (layoutRoot is null)
		{
			return false;
		}
		var layoutCarrier = layoutRoot.Children
			.OfType<FrameworkElement>()
			.FirstOrDefault(child =>
				ReferenceEquals(child, element)
				|| child is HtmlFilteredElementHost filteredChild
					&& ReferenceEquals(filteredChild.InnerElement, element));
		if (layoutCarrier is null)
			return false;
		if (!TryReadLayoutRootOrigin(owner, layoutRoot, geometryRoot, out var rootOrigin))
			return false;
		var row = Math.Clamp(Grid.GetRow(layoutCarrier), 0,
			Math.Max(0, layoutRoot.RowDefinitions.Count - 1));
		var column = Math.Clamp(Grid.GetColumn(layoutCarrier), 0,
			Math.Max(0, layoutRoot.ColumnDefinitions.Count - 1));
		var rowSpan = Math.Max(1, Grid.GetRowSpan(layoutCarrier));
		var columnSpan = Math.Max(1, Grid.GetColumnSpan(layoutCarrier));
		var slotX = layoutRoot.ColumnDefinitions.Count == 0
			? 0
			: layoutRoot.ColumnDefinitions.Take(column)
				.Sum(static definition => definition.ActualWidth);
		var slotY = layoutRoot.RowDefinitions.Count == 0
			? 0
			: layoutRoot.RowDefinitions.Take(row)
				.Sum(static definition => definition.ActualHeight);
		var slotWidth = layoutRoot.ColumnDefinitions.Count == 0
			? layoutRoot.ActualWidth
			: layoutRoot.ColumnDefinitions.Skip(column).Take(columnSpan)
				.Sum(static definition => definition.ActualWidth);
		var slotHeight = layoutRoot.RowDefinitions.Count == 0
			? layoutRoot.ActualHeight
			: layoutRoot.RowDefinitions.Skip(row).Take(rowSpan)
				.Sum(static definition => definition.ActualHeight);
		var margin = layoutCarrier.Margin;
		var x = layoutCarrier.HorizontalAlignment switch
		{
			HorizontalAlignment.Right =>
				slotX + slotWidth - margin.Right - layoutCarrier.ActualWidth,
			HorizontalAlignment.Center =>
				slotX + (slotWidth - layoutCarrier.ActualWidth) / 2
					+ (margin.Left - margin.Right) / 2,
			_ => slotX + margin.Left
		};
		var y = layoutCarrier.VerticalAlignment switch
		{
			VerticalAlignment.Bottom =>
				slotY + slotHeight - margin.Bottom - layoutCarrier.ActualHeight,
			VerticalAlignment.Center =>
				slotY + (slotHeight - layoutCarrier.ActualHeight) / 2
					+ (margin.Top - margin.Bottom) / 2,
			_ => slotY + margin.Top
		};
		origin = new(rootOrigin.X + x, rootOrigin.Y + y);
		return true;
	}

	private static bool TryReadLayoutRootOrigin(
		DependencyObject owner,
		Grid layoutRoot,
		FrameworkElement geometryRoot,
		out Windows.Foundation.Point origin)
	{
		origin = default;
		if (layoutRoot.IsLoaded)
		{
			try
			{
				origin = layoutRoot.TransformToVisual(geometryRoot)
					.TransformPoint(new Windows.Foundation.Point(0, 0));
				return true;
			}
			catch (ArgumentException)
			{
				// Continue with the loaded strong owner. A zero-size child can be
				// excluded from the visual tree while its parent's CSS grid still
				// has fully measured rows and columns.
			}
		}
		if (owner is not FrameworkElement ownerElement || !ownerElement.IsLoaded)
			return false;
		try
		{
			var ownerOrigin = ownerElement.TransformToVisual(geometryRoot)
				.TransformPoint(new Windows.Foundation.Point(0, 0));
			var inset = owner is Control control
				? new Windows.Foundation.Point(
					control.BorderThickness.Left + control.Padding.Left,
					control.BorderThickness.Top + control.Padding.Top)
				: new Windows.Foundation.Point(0, 0);
			origin = new(ownerOrigin.X + inset.X, ownerOrigin.Y + inset.Y);
			return true;
		}
		catch (ArgumentException)
		{
			return false;
		}
	}

	private static Windows.Foundation.Point ReadActualOffsetToRoot(
		FrameworkElement element,
		FrameworkElement rootElement)
	{
		double x = 0;
		double y = 0;
		DependencyObject? current = element;
		while (current is UIElement visual
			&& !ReferenceEquals(current, rootElement))
		{
			x += visual.ActualOffset.X;
			y += visual.ActualOffset.Y;
			current = VisualTreeHelper.GetParent(current);
		}
		// WinUI may terminate VisualTreeHelper parent traversal at an internal
		// ContentPresenter boundary even though every ActualOffset in the walked
		// chain is live layout evidence. Keep the accumulated WinUI offsets; a
		// genuinely detached zero-size element naturally remains (0, 0) and is
		// still reported as a mismatch by the auditor.
		return new(x, y);
	}

	private static bool TryReadContent(
		XamlPropertyQueryContext context,
		FrameworkElement element,
		out string value)
	{
		if (context.PropertyName == "content.ownText"
			&& TryReadRenderedText(context, element, out value))
		{
			return true;
		}
		if (context.Execution.TargetProperty
			== "AutomationProperties.Name")
		{
			value = AutomationProperties.GetName(element) ?? string.Empty;
			return true;
		}
		if (context.Execution.TargetProperty
			== "AutomationProperties.AutomationId")
		{
			value = AutomationProperties.GetAutomationId(element)
				?? string.Empty;
			return true;
		}
		if (context.Execution.TargetProperty == "ContentControl.Header")
		{
			var headerProperty = element.GetType().GetProperty("Header");
			if (headerProperty is not null)
			{
				value = Invariant(headerProperty.GetValue(element));
				return true;
			}
		}
		switch (context.Execution.TargetProperty)
		{
			case "FrameworkElement.DataContext":
				value = Invariant(element.DataContext);
				return true;
			case "TextBox.Text" when element is TextBox targetTextBox:
				value = targetTextBox.Text ?? string.Empty;
				return true;
			case "TextBlock.Text" when element is TextBlock targetTextBlock:
				value = targetTextBlock.Text ?? string.Empty;
				return true;
			case "TextBox.PlaceholderText" when element is TextBox placeholder:
				value = placeholder.PlaceholderText ?? string.Empty;
				return true;
			case "ItemsControl.ItemsSource" when element is ItemsControl items:
				value = Invariant(items.ItemsSource);
				return true;
			case "ContentControl.Content" when element is ContentControl target:
				value = Invariant(target.Content);
				return true;
			case "ButtonBase.CommandParameter" when element is ButtonBase button:
				value = Invariant(button.CommandParameter);
				return true;
		}
		switch (element)
		{
			case HtmlEmbeddedContentHost embedded
				when context.PropertyName is "src" or "data":
				value = embedded.Source ?? string.Empty;
				return true;
			case HtmlTemporalInputControl temporal
				when context.PropertyName is "content.value" or "value":
				value = temporal.Value ?? string.Empty;
				return true;
			case HtmlSpanBoxControl span:
				value = span.Text;
				return true;
			case HtmlVerticalTextControl vertical:
				value = vertical.Text;
				return true;
			case TextBlock text:
				value = text.Text ?? string.Empty;
				return true;
			case TextBox text:
				value = text.Text ?? string.Empty;
				return true;
			case ContentControl content:
				value = content.Content as string ?? string.Empty;
				return true;
		}
		var marker =
			$"generated-own-text:{context.DocumentScope}::{context.XPath}";
		var generatedText = Enumerable.Range(
				0,
				VisualTreeHelper.GetChildrenCount(element))
			.Select(index => VisualTreeHelper.GetChild(element, index))
			.OfType<TextBlock>()
			.FirstOrDefault(text => string.Equals(
				text.Tag as string,
				marker,
				StringComparison.Ordinal));
		if (generatedText is not null)
		{
			value = generatedText.Text ?? string.Empty;
			return true;
		}
		value = string.Empty;
		return false;
	}

	private static bool TryReadRenderedText(
		XamlPropertyQueryContext context,
		FrameworkElement element,
		out string value)
	{
		value = element switch
		{
			TextBlock text => text.Text ?? string.Empty,
			TextBox textBox => textBox.Text ?? string.Empty,
			ContentControl { Content: string content } => content,
			_ => string.Empty
		};
		if (element is TextBlock
			or TextBox
			or ContentControl { Content: string })
		{
			return true;
		}
		var marker = $"generated-own-text:{context.DocumentScope}::{context.XPath}";
		var generated = DirectVisualChildren(element)
			.OfType<FrameworkElement>()
			.FirstOrDefault(child => child is TextBlock
				&& string.Equals(
					child.Tag?.ToString(),
					marker,
					StringComparison.Ordinal));
		if (generated is not TextBlock generatedText)
			return false;
		value = generatedText.Text ?? string.Empty;
		return true;
	}

	private static IEnumerable<DependencyObject> DirectVisualChildren(
		FrameworkElement element) => element switch
	{
		HtmlInteractiveFlexPanel interactive => interactive.LayoutRoot.Children,
		Panel panel => panel.Children,
		ContentControl { Content: DependencyObject content } => [content],
		Border { Child: DependencyObject child } => [child],
		_ => []
	};

	private static bool TryReadState(
		XamlPropertyQueryContext context,
		FrameworkElement element,
		out string value)
	{
		var propertyName = context.PropertyName;
		if (propertyName is "state.disabled")
		{
			value = element switch
			{
				Control control when !control.IsEnabled => "true",
				HtmlInteractiveFlexPanel interactive
					when !interactive.IsEnabled => "true",
				HtmlTemporalInputControl temporal when !temporal.IsEnabled =>
					"true",
				_ => "false"
			};
			return true;
		}
		if (propertyName is "state.valid")
		{
			value = HtmlValidation.GetIsValid(element)
				? "true"
				: "false";
			return true;
		}
		if (propertyName is "state.willValidate")
		{
			value = HtmlValidation.GetWillValidate(element)
				? "true"
				: "false";
			return true;
		}
		if (propertyName is "state.validationMessage")
		{
			value = HtmlValidation.GetValidationMessage(element);
			return true;
		}
		if (propertyName is "state.readOnly" && element is TextBox textBox)
		{
			value = textBox.IsReadOnly ? "true" : "false";
			return true;
		}
		if (propertyName is "state.readOnly"
			&& element is HtmlTemporalInputControl temporalReadOnly)
		{
			value = temporalReadOnly.IsReadOnly ? "true" : "false";
			return true;
		}
		if (propertyName is "state.readOnly"
			&& element is PasswordBox passwordReadOnly)
		{
			value = passwordReadOnly.IsHitTestVisible ? "false" : "true";
			return true;
		}
		if (propertyName is "state.readOnly"
			&& element is HtmlCursorPasswordBoxHost passwordHostReadOnly)
		{
			value = passwordHostReadOnly.IsReadOnly ? "true" : "false";
			return true;
		}
		if (propertyName is "state.checked")
		{
			if (element is ToggleButton toggleButton)
			{
				value = toggleButton.IsChecked switch
				{
					true => "true",
					_ => "false"
				};
				return true;
			}
			if (element is ToggleSwitch toggleSwitch)
			{
				value = toggleSwitch.IsOn ? "true" : "false";
				return true;
			}
		}
		if (propertyName is "state.indeterminate"
			&& element is ToggleButton indeterminateToggle)
		{
			value = indeterminateToggle.IsThreeState
				&& indeterminateToggle.IsChecked is null
					? "true"
					: "false";
			return true;
		}
		if (propertyName is "state.tabIndex" && element is Control tabStop)
		{
			value = tabStop.TabIndex.ToString(CultureInfo.InvariantCulture);
			return true;
		}
		if (propertyName is "state.tabIndex"
			&& element is HtmlCssBoxGrid cssBoxTabStop)
		{
			value = cssBoxTabStop.HtmlTabIndex.ToString(
				CultureInfo.InvariantCulture);
			return true;
		}
		if (propertyName is "state.contentEditable"
			&& element is TextBox editableText)
		{
			value = editableText.IsReadOnly ? "false" : "true";
			return true;
		}
		if (context.Execution.TargetProperty == "ToggleButton.IsChecked"
			&& element is ToggleButton targetToggle)
		{
			value = targetToggle.IsChecked switch
			{
				true => "true",
				_ => "false"
			};
			return true;
		}
		if (context.Execution.TargetProperty == "Selector.SelectedItem"
			&& element is Selector targetSelector)
		{
			value = Invariant(targetSelector.SelectedItem);
			return true;
		}
		if (context.Execution.TargetProperty == "Selector.SelectedValue"
			&& element is Selector targetSelectedValue)
		{
			value = Invariant(targetSelectedValue.SelectedValue);
			return true;
		}
		if (context.Execution.TargetProperty == "RangeBase.Value"
			&& element is RangeBase targetRange)
		{
			value = targetRange.Value.ToString(
				"R",
				CultureInfo.InvariantCulture);
			return true;
		}
		if (propertyName is "state.selected"
			&& element is ListViewBase listView)
		{
			value = listView.SelectedItems.Count > 0 ? "true" : "false";
			return true;
		}
		if (propertyName is "state.selected"
			&& element is SelectorItem selectorItem)
		{
			value = selectorItem.IsSelected ? "true" : "false";
			return true;
		}
		if (propertyName is "state.selectedItem"
			&& element is Selector selector)
		{
			value = Invariant(selector.SelectedItem);
			return true;
		}
		if (propertyName is "state.selectedValue"
			&& element is Selector selectedValue)
		{
			value = Invariant(selectedValue.SelectedValue);
			return true;
		}
		if (propertyName is "state.selectedIndex"
			&& element is Selector selectedIndex)
		{
			value = selectedIndex.SelectedIndex.ToString(
				CultureInfo.InvariantCulture);
			return true;
		}
		if (propertyName is "state.value")
		{
			switch (element)
			{
				case HtmlTemporalInputControl temporal:
					value = temporal.Value ?? string.Empty;
					return true;
				case TextBox valueTextBox:
					value = valueTextBox.Text ?? string.Empty;
					return true;
				case PasswordBox passwordBox:
					value = passwordBox.Password ?? string.Empty;
					return true;
				case HtmlCursorPasswordBoxHost passwordHost:
					value = passwordHost.Password;
					return true;
				case RangeBase range:
					value = range.Value.ToString(
						"R",
						CultureInfo.InvariantCulture);
					return true;
			}
		}
		if (propertyName is "state.minimum"
			&& element is RangeBase minimum)
		{
			value = minimum.Minimum.ToString("R", CultureInfo.InvariantCulture);
			return true;
		}
		if (propertyName is "state.minimum"
			&& element is HtmlTemporalInputControl temporalMinimum)
		{
			value = temporalMinimum.Minimum ?? string.Empty;
			return true;
		}
		if (propertyName is "state.maximum"
			&& element is RangeBase maximum)
		{
			value = maximum.Maximum.ToString("R", CultureInfo.InvariantCulture);
			return true;
		}
		if (propertyName is "state.maximum"
			&& element is HtmlTemporalInputControl temporalMaximum)
		{
			value = temporalMaximum.Maximum ?? string.Empty;
			return true;
		}
		if (propertyName is "state.placeholder")
		{
			switch (element)
			{
				case TextBox placeholderTextBox:
					value = placeholderTextBox.PlaceholderText ?? string.Empty;
					return true;
				case PasswordBox placeholderPasswordBox:
					value = placeholderPasswordBox.PlaceholderText ?? string.Empty;
					return true;
				case HtmlCursorPasswordBoxHost placeholderPasswordHost:
					value = placeholderPasswordHost.PlaceholderText;
					return true;
			}
		}
		value = string.Empty;
		return false;
	}

	private bool TryReadResource(
		XamlPropertyQueryContext context,
		string elementPath,
		FrameworkElement element,
		out string value)
	{
		switch (element)
		{
			case HtmlEmbeddedContentHost embedded
				when context.PropertyName is
					"src"
					or "resource.embeddedSourceUrl"
					or
					"resource.imageSourceUrl"
					or "resource.mediaSourceUrl"
					or "resource.canvasCommandStream"
					or "resource.references":
				value = embedded.Source ?? string.Empty;
				return value.Length > 0;
		case HtmlCanvasSurface canvas
				when context.PropertyName == "resource.canvasCommandStream":
				value = canvas.ReplayEvidenceJson;
				return value.Length > 0;
			case HtmlImageView imageView
				when imageView.Source is BitmapImage viewBitmap:
				value = viewBitmap.UriSource?.ToString() ?? string.Empty;
				return true;
			case HtmlImageView imageView:
				value = imageView.Source?.ToString() ?? string.Empty;
				return true;
			case Image { Source: BitmapImage bitmap }:
				value = bitmap.UriSource?.ToString() ?? string.Empty;
				return true;
			case Image image:
				value = image.Source?.ToString() ?? string.Empty;
				return true;
			case HyperlinkButton hyperlink:
				value = hyperlink.NavigateUri?.ToString() ?? string.Empty;
				return true;
			case HtmlInteractiveFlexPanel interactive:
				value = interactive.NavigateUri?.ToString() ?? string.Empty;
				return true;
		}
		const string resourcePrefix = "resource.";
		if (!context.PropertyName.StartsWith(
			resourcePrefix,
			StringComparison.Ordinal)
			|| context.PropertyName.Length == resourcePrefix.Length)
		{
			value = string.Empty;
			return false;
		}
		var key = context.PropertyName[resourcePrefix.Length..];
		if (element.Resources.TryGetValue(key, out var resource))
		{
			value = Invariant(resource);
			return true;
		}
		value = string.Empty;
		return false;
	}

	private bool TryReadEffect(
		XamlPropertyQueryContext context,
		string elementPath,
		FrameworkElement element,
		out string value)
	{
		value = context.PropertyName switch
		{
			"effect.opacity" => element.Opacity.ToString(
				"R",
				CultureInfo.InvariantCulture),
			"effect.shadow" => element.Shadow?.GetType().FullName ?? string.Empty,
			"effect.clip" => element.Clip?.ToString() ?? string.Empty,
			"effect.projection" =>
				element.Projection?.GetType().FullName ?? string.Empty,
			"effect.renderTransform" =>
				element.RenderTransform?.ToString() ?? string.Empty,
			"effect.translation" => FormattableString.Invariant(
				$"{element.Translation.X:R},{element.Translation.Y:R},{element.Translation.Z:R}"),
			"effect.rotation" => element.Rotation.ToString(
				"R",
				CultureInfo.InvariantCulture),
			"effect.scale" => FormattableString.Invariant(
				$"{element.Scale.X:R},{element.Scale.Y:R},{element.Scale.Z:R}"),
			"effect.transitions" =>
				(element.Transitions?.Count ?? 0).ToString(
					CultureInfo.InvariantCulture),
			"effect.animations" =>
				HtmlAnimationState.CaptureRuntimeTimelineJson(element),
			_ => string.Empty
		};
		return value.Length > 0
			|| context.PropertyName is
				"effect.shadow"
				or "effect.clip"
				or "effect.projection"
				or "effect.renderTransform"
				or "effect.animations";
	}

	private bool TryReadComposedTransform(
		XamlPropertyQueryContext context,
		FrameworkElement element,
		out string value)
	{
		value = string.Empty;
		if (!context.PropertyName.Equals(
			"style.transform",
			StringComparison.Ordinal))
		{
			return false;
		}
		if (element.RenderTransform is MatrixTransform matrixTransform)
		{
			var matrix = matrixTransform.Matrix;
			if (Math.Abs(matrix.M11 - 1) <= .000001
				&& Math.Abs(matrix.M12) <= .000001
				&& Math.Abs(matrix.M21) <= .000001
				&& Math.Abs(matrix.M22 - 1) <= .000001
				&& Math.Abs(matrix.OffsetX) <= .000001
				&& Math.Abs(matrix.OffsetY) <= .000001)
			{
				value = "none";
				return true;
			}
			var scaleX = 1d;
			var scaleY = 1d;
			if (designRuntime is not null)
			{
				var viewport = designRuntime.Layout.Viewport;
				if (viewport.RuntimeWidth > 0
					&& viewport.CssRuntimeWidth > 0)
				{
					scaleX =
						viewport.RuntimeWidth / viewport.CssRuntimeWidth;
				}
				if (viewport.RuntimeHeight > 0
					&& viewport.CssRuntimeHeight > 0)
				{
					scaleY =
						viewport.RuntimeHeight / viewport.CssRuntimeHeight;
				}
			}
			value = string.Create(
				CultureInfo.InvariantCulture,
				$"matrix({matrix.M11:R}, {matrix.M12:R}, {matrix.M21:R}, {matrix.M22:R}, {matrix.OffsetX / scaleX:R}, {matrix.OffsetY / scaleY:R})");
			return true;
		}
		var visual = ElementCompositionPreview.GetElementVisual(element);
		var hasLayoutTranslation =
			WinUiCssSemantic.GetPositioningState(element) is not null;
		if ((hasLayoutTranslation
				|| visual.Offset.X == 0
					&& visual.Offset.Y == 0
					&& visual.Offset.Z == 0)
			&& visual.Scale.X == 1
			&& visual.Scale.Y == 1
			&& visual.Scale.Z == 1
			&& visual.RotationAngleInDegrees == 0
			&& Math.Abs(
				visual.Opacity
					- WinUiFilterBehavior.ExpectedVisualOpacity(element)) < .0001f
			&& IsIdentityTransform(element.RenderTransform))
		{
			value = "none";
			return true;
		}
		value = FormattableString.Invariant(
			$"offset({visual.Offset.X:R},{visual.Offset.Y:R},{visual.Offset.Z:R}) scale({visual.Scale.X:R},{visual.Scale.Y:R},{visual.Scale.Z:R}) rotation({visual.RotationAngleInDegrees:R})");
		return true;
	}

	private static bool TryReadTransitionProperty(
		FrameworkElement element,
		out string value)
	{
		var properties = new List<string>(2);
		if (element.OpacityTransition is not null)
			properties.Add("opacity");
		var hasTransform = element.TranslationTransition is not null
			&& element.ScaleTransition is not null
			&& element.RotationTransition is not null;
		if (hasTransform)
			properties.Add("transform");
		value = string.Join(", ", properties);
		return properties.Count > 0;
	}

	private static bool TryReadTransitionDuration(
		FrameworkElement element,
		out string value)
	{
		var durations = new List<TimeSpan>(4);
		if (element.OpacityTransition is { } opacity)
			durations.Add(opacity.Duration);
		if (element.TranslationTransition is { } translation)
			durations.Add(translation.Duration);
		if (element.ScaleTransition is { } scale)
			durations.Add(scale.Duration);
		if (element.RotationTransition is { } rotation)
			durations.Add(rotation.Duration);
		if (durations.Count == 0
			|| durations.Any(duration => duration != durations[0]))
		{
			value = string.Empty;
			return false;
		}
		value = durations[0].TotalSeconds.ToString(
			"R",
			CultureInfo.InvariantCulture) + "s";
		return true;
	}

	private bool TryReadTransformOrigin(
		FrameworkElement element,
		out string value)
	{
		var origin = element.RenderTransformOrigin;
		value = string.Create(
			CultureInfo.InvariantCulture,
			$"{CssPixels(origin.X * element.ActualWidth, true)} "
				+ $"{CssPixels(origin.Y * element.ActualHeight, false)}");
		return true;
	}

	private static bool IsIdentityTransform(Transform? transform)
	{
		if (transform is null)
			return true;
		var origin = transform.TransformPoint(new(0, 0));
		var horizontal = transform.TransformPoint(new(1, 0));
		var vertical = transform.TransformPoint(new(0, 1));
		return Math.Abs(origin.X) < .000001
			&& Math.Abs(origin.Y) < .000001
			&& Math.Abs(horizontal.X - 1) < .000001
			&& Math.Abs(horizontal.Y) < .000001
			&& Math.Abs(vertical.X) < .000001
			&& Math.Abs(vertical.Y - 1) < .000001;
	}

	private static string Invariant(object? value) =>
		value switch
		{
			null => string.Empty,
			IFormattable formattable => formattable.ToString(
				null,
				CultureInfo.InvariantCulture),
			_ => value.ToString() ?? string.Empty
		};

	private static bool RequiresSourceValue(XamlPropertyQueryContext context) =>
		context.OwnerKind == ElementSlotOwnerKind.Event
			||
		context.Slot switch
		{
			XamlPropertyDataSlot.Initialization =>
				context.Source.Initialization.IsSet,
			XamlPropertyDataSlot.Link => context.Source.Link.IsSet,
			XamlPropertyDataSlot.Runtime => context.Source.Runtime.IsSet,
			_ => false
		};

	private static bool SupportsSlot(
		XamlPropertyExecutionDescriptor execution,
		XamlPropertyDataSlot slot) =>
		slot switch
		{
			XamlPropertyDataSlot.Initialization => execution.SupportsInitialization,
			XamlPropertyDataSlot.Link => execution.SupportsLink,
			XamlPropertyDataSlot.Runtime => execution.SupportsRuntime,
			_ => false
		};
}

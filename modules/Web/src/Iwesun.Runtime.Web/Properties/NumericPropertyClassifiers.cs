namespace Iwesun.Runtime.Web;

public static class ElementNumericPropertyExtensions
{
	public static bool HasSourceInitialization(this ElementNumericProperty property) =>
		property.SourceInitialization.IsSet;

	public static bool HasSourceLink(this ElementNumericProperty property) =>
		property.SourceLink.IsSet;

	public static bool HasSourceRuntime(this ElementNumericProperty property) =>
		property.SourceRuntime.IsSet;

	public static bool HasXamlInitialization(this ElementNumericProperty property) =>
		property.XamlInitialization.IsSet;

	public static bool HasXamlLink(this ElementNumericProperty property) =>
		property.XamlLink.IsSet;

	public static bool HasXamlRuntime(this ElementNumericProperty property) =>
		property.XamlRuntime.IsSet;

	public static bool HasRuntimePair(this ElementNumericProperty property) =>
		property.HasSourceRuntime() && property.HasXamlRuntime();

	public static bool IsContainerAutomaticLayoutSource(this ElementNumericProperty property) =>
		property.SourceValueSource == ElementPropertyValueSource.ContainerAutomaticLayout;

	public static bool IsLinkedCalculationSource(this ElementNumericProperty property) =>
		property.SourceValueSource == ElementPropertyValueSource.LinkedCalculation;

	public static bool IsLinkedConstantSource(this ElementNumericProperty property) =>
		property.SourceValueSource == ElementPropertyValueSource.LinkedConstant;

	public static bool IsDirectConstantSource(this ElementNumericProperty property) =>
		property.SourceValueSource == ElementPropertyValueSource.DirectConstant;

	public static bool IsXamlContainerAutomaticLayout(this ElementNumericProperty property) =>
		property.XamlValueSource == ElementPropertyValueSource.ContainerAutomaticLayout;

	public static bool IsXamlLinkedCalculation(this ElementNumericProperty property) =>
		property.XamlValueSource == ElementPropertyValueSource.LinkedCalculation;

	public static bool IsXamlLinkedConstant(this ElementNumericProperty property) =>
		property.XamlValueSource == ElementPropertyValueSource.LinkedConstant;

	public static bool IsXamlDirectConstant(this ElementNumericProperty property) =>
		property.XamlValueSource == ElementPropertyValueSource.DirectConstant;

	public static bool IsUrlLink(this ElementPropertyLink link) =>
		link.Kind == ElementPropertyLinkKind.Url;

	public static bool IsDomDescriptionLink(this ElementPropertyLink link) =>
		link.Kind is ElementPropertyLinkKind.DomDescription or ElementPropertyLinkKind.XPath;

	public static bool IsExpressionLink(this ElementPropertyLink link) =>
		link.Kind is
			ElementPropertyLinkKind.CssExpression
			or ElementPropertyLinkKind.LayoutExpression
			or ElementPropertyLinkKind.XamlBinding;

	public static bool IsContainerLayoutLink(this ElementPropertyLink link) =>
		link.Kind == ElementPropertyLinkKind.ContainerLayout
		&& link.ContainerLayout is not null;
}

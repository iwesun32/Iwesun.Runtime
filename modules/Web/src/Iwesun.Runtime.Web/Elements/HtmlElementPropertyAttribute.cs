namespace Iwesun.Runtime.Web;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class HtmlElementPropertyAttribute : ElementPropertyAttribute
{
	public HtmlElementPropertyAttribute(
		PropertyValueKind valueKind = PropertyValueKind.Text,
		PropertyTranslationKind translation = PropertyTranslationKind.Composite,
		PropertyComparisonKind comparison = PropertyComparisonKind.Semantic) :
		base(valueKind, PropertyValueStage.HtmlInitialization)
	{
		Translation = translation;
		Comparison = comparison;
		IsHtmlDefinedProperty = true;
		IsSlottedProperty = true;
		IsFillRequired = true;
		IsXamlFillRequired = true;
		IsAuditRequired = true;
	}
}

namespace Iwesun.Runtime.Web;

public static class ElementPropertyTraitExtensions
{
	public static bool IsTypeIntrinsic(this ElementPropertyTraits traits) =>
		traits.ValueStage == PropertyValueStage.TypeIntrinsic;

	public static bool IsInitializationValue(this ElementPropertyTraits traits) =>
		traits.ValueStage is
			PropertyValueStage.HtmlInitialization
			or PropertyValueStage.CssInitialization
			or PropertyValueStage.XamlInitialization;

	public static bool IsRuntimeValue(this ElementPropertyTraits traits) =>
		traits.ValueStage is
			PropertyValueStage.DomRuntime
			or PropertyValueStage.WinUiRuntime;

	public static bool IsRuntimeAbsolute(this ElementPropertyTraits traits) =>
		traits.IsRuntimeValue()
		&& traits.CoordinateMode == PropertyCoordinateMode.Absolute;

	public static bool IsInitializationRelative(this ElementPropertyTraits traits) =>
		traits.IsInitializationValue()
		&& traits.CoordinateMode == PropertyCoordinateMode.Relative;

	public static bool IsCoordinate(this ElementPropertyTraits traits) =>
		traits.ValueKind == PropertyValueKind.Coordinate;

	public static bool IsSize(this ElementPropertyTraits traits) =>
		traits.ValueKind == PropertyValueKind.Size;

	public static bool IsHorizontal(this ElementPropertyTraits traits) =>
		traits.Axis is PropertyAxis.X or PropertyAxis.Horizontal;

	public static bool IsVertical(this ElementPropertyTraits traits) =>
		traits.Axis is PropertyAxis.Y or PropertyAxis.Vertical;

	public static bool IsDirectlyTranslatable(this ElementPropertyTraits traits) =>
		traits.Translation == PropertyTranslationKind.Direct;

	public static bool RequiresRuntimeCalculation(this ElementPropertyTraits traits) =>
		traits.Translation == PropertyTranslationKind.RuntimeCalculated;

	public static bool RequiresManualReview(this ElementPropertyTraits traits) =>
		traits.Translation == PropertyTranslationKind.Unsupported
		|| traits.Comparison == PropertyComparisonKind.ManualReview;

	public static bool IsAutomaticallyComparable(this ElementPropertyTraits traits) =>
		!traits.RequiresManualReview()
		&& traits.Translation != PropertyTranslationKind.NotApplicable;

	public static bool IsInheritedProperty(this ElementPropertyTraits traits) =>
		traits.Inheritance == PropertyInheritanceKind.Inherited;

}

public static class ElementPropertyTraitClassifier
{
	public static bool IsRuntimeAbsolute<TElement>(string propertyName) =>
		ElementPropertyTraitsReflector.GetAttribute<TElement>(propertyName).IsRuntimeAbsolute();

	public static bool IsInitializationRelative<TElement>(string propertyName) =>
		ElementPropertyTraitsReflector.GetAttribute<TElement>(propertyName).IsInitializationRelative();

	public static bool IsTypeIntrinsic<TElement>(string propertyName) =>
		ElementPropertyTraitsReflector.GetAttribute<TElement>(propertyName).IsTypeIntrinsic();

	public static bool IsReadOnly<TElement>(string propertyName) =>
		ElementPropertyTraitsReflector.GetAttribute<TElement>(propertyName).IsReadOnly;

	public static bool IsDirectlyTranslatable<TElement>(string propertyName) =>
		ElementPropertyTraitsReflector.GetAttribute<TElement>(propertyName).IsDirectlyTranslatable();

	public static bool RequiresManualReview<TElement>(string propertyName) =>
		ElementPropertyTraitsReflector.GetAttribute<TElement>(propertyName).RequiresManualReview();

	public static bool IsHtmlDefinedProperty<TElement>(string propertyName) =>
		ElementPropertyTraitsReflector.GetAttribute<TElement>(propertyName).IsHtmlDefinedProperty;

	public static bool IsElementDesignProperty<TElement>(string propertyName) =>
		ElementPropertyTraitsReflector.GetAttribute<TElement>(propertyName).IsElementDesignProperty;

	public static bool IsManagementProperty<TElement>(string propertyName) =>
		ElementPropertyTraitsReflector.GetAttribute<TElement>(propertyName).IsManagementProperty;

	public static bool IsObjectTreeRelationshipProperty<TElement>(string propertyName) =>
		ElementPropertyTraitsReflector
			.GetAttribute<TElement>(propertyName)
			.IsObjectTreeRelationshipProperty;

	public static bool IsDomMappingProperty<TElement>(string propertyName) =>
		ElementPropertyTraitsReflector.GetAttribute<TElement>(propertyName).IsDomMappingProperty;

	public static bool IsEventCollectionProperty<TElement>(string propertyName) =>
		ElementPropertyTraitsReflector.GetAttribute<TElement>(propertyName).IsEventCollectionProperty;

	public static bool IsDataSourceCollectionProperty<TElement>(string propertyName) =>
		ElementPropertyTraitsReflector
			.GetAttribute<TElement>(propertyName)
			.IsDataSourceCollectionProperty;

	public static bool IsSlottedProperty<TElement>(string propertyName) =>
		ElementPropertyTraitsReflector.GetAttribute<TElement>(propertyName).IsSlottedProperty;

	public static bool IsSlottedPropertyCollection<TElement>(string propertyName) =>
		ElementPropertyTraitsReflector.GetAttribute<TElement>(propertyName).IsSlottedPropertyCollection;

	public static bool IsXamlOutputProperty<TElement>(string propertyName) =>
		ElementPropertyTraitsReflector.GetAttribute<TElement>(propertyName).IsXamlOutputProperty;

	public static bool IsXamlFillRequired<TElement>(string propertyName) =>
		ElementPropertyTraitsReflector.GetAttribute<TElement>(propertyName).IsXamlFillRequired;

	public static bool IsAuditRequired<TElement>(string propertyName) =>
		ElementPropertyTraitsReflector.GetAttribute<TElement>(propertyName).IsAuditRequired;

	public static bool IsConstructionRequired<TElement>(string propertyName) =>
		ElementPropertyTraitsReflector.GetAttribute<TElement>(propertyName).IsConstructionRequired;

	public static bool IsFillRequired<TElement>(string propertyName) =>
		ElementPropertyTraitsReflector.GetAttribute<TElement>(propertyName).IsFillRequired;

	public static bool IsRuntimeDerivedProperty<TElement>(string propertyName) =>
		ElementPropertyTraitsReflector.GetAttribute<TElement>(propertyName).IsRuntimeDerivedProperty;
}

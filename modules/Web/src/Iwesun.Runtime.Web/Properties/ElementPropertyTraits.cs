using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace Iwesun.Runtime.Web;

public enum PropertyValueStage
{
	TypeIntrinsic,
	HtmlInitialization,
	CssInitialization,
	CssComputed,
	DomRuntime,
	XamlInitialization,
	WinUiRuntime
}

public enum PropertyValueKind
{
	Identity,
	Boolean,
	Enumeration,
	Text,
	Number,
	Length,
	Coordinate,
	Size,
	Color,
	Geometry,
	Transform,
	Animation,
	Resource,
	State
}

public enum PropertyCoordinateMode
{
	None,
	Relative,
	Absolute
}

public enum PropertyReferenceSpace
{
	None,
	Element,
	Parent,
	ContainingBlock,
	Document,
	Viewport,
	Screen,
	XamlParent,
	XamlRoot
}

public enum PropertyAxis
{
	None,
	X,
	Y,
	Horizontal,
	Vertical
}

public enum PropertyUnit
{
	None,
	Unitless,
	CssPixel,
	Dip,
	PhysicalPixel,
	Percent,
	Degree,
	Millisecond
}

public enum PropertyTranslationKind
{
	Direct,
	Semantic,
	Composite,
	RuntimeCalculated,
	NotApplicable,
	Unsupported
}

public enum PropertyComparisonKind
{
	Exact,
	NumericTolerance,
	Semantic,
	NormalizedColor,
	GeometryTolerance,
	ResourceIdentity,
	StructuredAnimation,
	CanvasReplay,
	ManualReview
}

public enum PropertyInheritanceKind
{
	NotInherited,
	Inherited,
	ContextDependent
}

public enum ElementTraversalDeclarationKind
{
	OwnerResolved,
	Static
}

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public class ElementPropertyAttribute(
	PropertyValueKind valueKind,
	PropertyValueStage valueStage) : Attribute
{
	public PropertyValueKind ValueKind { get; } = valueKind;
	public PropertyValueStage ValueStage { get; } = valueStage;
	public PropertyCoordinateMode CoordinateMode { get; set; }
	public PropertyReferenceSpace ReferenceSpace { get; set; }
	public PropertyAxis Axis { get; set; }
	public PropertyUnit Unit { get; set; }
	public PropertyTranslationKind Translation { get; set; } = PropertyTranslationKind.Direct;
	public PropertyComparisonKind Comparison { get; set; } = PropertyComparisonKind.Exact;
	public PropertyInheritanceKind Inheritance { get; set; }
	public bool IsReadOnly { get; set; } = true;
	public bool IsHtmlDefinedProperty { get; set; }
	public bool IsElementDesignProperty { get; set; }
	public bool IsManagementProperty { get; set; }
	public bool IsObjectTreeRelationshipProperty { get; set; }
	public bool IsDomMappingProperty { get; set; }
	public bool IsEventCollectionProperty { get; set; }
	public bool IsDataSourceCollectionProperty { get; set; }
	public bool IsSlottedProperty { get; set; }
	public bool IsSlottedPropertyCollection { get; set; }
	public bool IsXamlOutputProperty { get; set; }
	public bool IsXamlFillRequired { get; set; }
	public bool IsAuditRequired { get; set; }
	public bool IsConstructionRequired { get; set; }
	public bool IsFillRequired { get; set; }
	public bool IsRuntimeDerivedProperty { get; set; }
	public ElementTraversalDeclarationKind TraversalDeclaration { get; set; } =
		ElementTraversalDeclarationKind.OwnerResolved;
	public ElementEvidenceKind FillEvidenceKind { get; set; }
	public ElementFillSlot FillSlot { get; set; } = ElementFillSlot.Unspecified;
	public ElementSlotCategory FillCategory { get; set; } = ElementSlotCategory.Unspecified;
	public string FillQueryName { get; set; } = string.Empty;
	public double Tolerance { get; set; }
	public string TargetProperty { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
}

public readonly record struct ElementPropertyTraits(
	Type DeclaringType,
	string PropertyName,
	PropertyValueKind ValueKind,
	PropertyValueStage ValueStage,
	PropertyCoordinateMode CoordinateMode,
	PropertyReferenceSpace ReferenceSpace,
	PropertyAxis Axis,
	PropertyUnit Unit,
	PropertyTranslationKind Translation,
	PropertyComparisonKind Comparison,
	PropertyInheritanceKind Inheritance,
	bool IsReadOnly,
	double Tolerance,
	string TargetProperty,
	string Description)
{
	public bool IsHtmlDefinedProperty { get; init; }
	public bool IsElementDesignProperty { get; init; }
	public bool IsManagementProperty { get; init; }
	public bool IsObjectTreeRelationshipProperty { get; init; }
	public bool IsDomMappingProperty { get; init; }
	public bool IsEventCollectionProperty { get; init; }
	public bool IsDataSourceCollectionProperty { get; init; }
	public bool IsSlottedProperty { get; init; }
	public bool IsSlottedPropertyCollection { get; init; }
	public bool IsXamlOutputProperty { get; init; }
	public bool IsXamlFillRequired { get; init; }
	public bool IsAuditRequired { get; init; }
	public bool IsConstructionRequired { get; init; }
	public bool IsFillRequired { get; init; }
	public bool IsRuntimeDerivedProperty { get; init; }
	public ElementTraversalDeclarationKind TraversalDeclaration { get; init; }
	public ElementEvidenceKind FillEvidenceKind { get; init; }
	public ElementFillSlot FillSlot { get; init; }
	public ElementSlotCategory FillCategory { get; init; }
	public string FillQueryName { get; init; } = string.Empty;
	public bool UsesOwnerResolvedTraversal =>
		(IsSlottedProperty || IsSlottedPropertyCollection)
			&& TraversalDeclaration
				== ElementTraversalDeclarationKind.OwnerResolved;
}

public static class ElementPropertyTraitsReflector
{
	private static readonly ConcurrentDictionary<(Type Type, string Name), ElementPropertyTraits> Cache = new();
	private static readonly ConcurrentDictionary<Type, IReadOnlyList<ElementPropertyTraits>> TypeCache = new();

	public static ElementPropertyTraits GetAttribute<TElement>(string propertyName) =>
		GetAttribute(typeof(TElement), propertyName);

	public static ElementPropertyTraits GetAttribute<TElement, TValue>(
		Expression<Func<TElement, TValue>> selector) =>
		GetAttribute(ReadProperty(selector));

	public static ElementPropertyTraits GetAttribute(Type elementType, string propertyName)
	{
		ArgumentNullException.ThrowIfNull(elementType);
		ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
		return Cache.GetOrAdd((elementType, propertyName), static key =>
		{
			var property = key.Type.GetProperty(
				key.Name,
				BindingFlags.Instance | BindingFlags.Public)
				?? throw new InvalidOperationException(
					$"{key.Type.FullName} 没有公开属性 {key.Name}。");
			return Create(property);
		});
	}

	public static ElementPropertyTraits GetAttribute(PropertyInfo property)
	{
		ArgumentNullException.ThrowIfNull(property);
		return Cache.GetOrAdd(
			(property.DeclaringType ?? throw new InvalidOperationException("属性缺少声明类型。"), property.Name),
			_ => Create(property));
	}

	public static bool TryGetAttribute(
		Type elementType,
		string propertyName,
		out ElementPropertyTraits traits)
	{
		try
		{
			traits = GetAttribute(elementType, propertyName);
			return true;
		}
		catch (InvalidOperationException)
		{
			traits = default;
			return false;
		}
	}

	public static IReadOnlyList<ElementPropertyTraits> GetAttributes(Type elementType)
	{
		ArgumentNullException.ThrowIfNull(elementType);
		return TypeCache.GetOrAdd(
			elementType,
			static type => type
				.GetProperties(BindingFlags.Instance | BindingFlags.Public)
				.Where(static property => FindAttribute(property) is not null)
				.Select(GetAttribute)
				.OrderBy(static traits => traits.PropertyName, StringComparer.Ordinal)
				.ToArray());
	}

	private static ElementPropertyTraits Create(PropertyInfo property)
	{
		var attribute = FindAttribute(property)
			?? throw new InvalidOperationException(
				$"{property.DeclaringType?.FullName}.{property.Name} 没有 ElementPropertyAttribute。");
		Validate(property, attribute);
		return new(
			property.DeclaringType ?? throw new InvalidOperationException("属性缺少声明类型。"),
			property.Name,
			attribute.ValueKind,
			attribute.ValueStage,
			attribute.CoordinateMode,
			attribute.ReferenceSpace,
			attribute.Axis,
			attribute.Unit,
			attribute.Translation,
			attribute.Comparison,
			attribute.Inheritance,
			attribute.IsReadOnly,
			attribute.Tolerance,
			attribute.TargetProperty,
			attribute.Description)
		{
			IsHtmlDefinedProperty = attribute.IsHtmlDefinedProperty,
			IsElementDesignProperty = attribute.IsElementDesignProperty,
			IsManagementProperty = attribute.IsManagementProperty,
			IsObjectTreeRelationshipProperty = attribute.IsObjectTreeRelationshipProperty,
			IsDomMappingProperty = attribute.IsDomMappingProperty,
			IsEventCollectionProperty = attribute.IsEventCollectionProperty,
			IsDataSourceCollectionProperty = attribute.IsDataSourceCollectionProperty,
			IsSlottedProperty = attribute.IsSlottedProperty,
			IsSlottedPropertyCollection = attribute.IsSlottedPropertyCollection,
			IsXamlOutputProperty = attribute.IsXamlOutputProperty,
			IsXamlFillRequired = attribute.IsXamlFillRequired,
			IsAuditRequired = attribute.IsAuditRequired,
			IsConstructionRequired = attribute.IsConstructionRequired,
			IsFillRequired = attribute.IsFillRequired,
			IsRuntimeDerivedProperty = attribute.IsRuntimeDerivedProperty,
			TraversalDeclaration = attribute.TraversalDeclaration,
			FillEvidenceKind = attribute.FillEvidenceKind,
			FillSlot = attribute.FillSlot,
			FillCategory = attribute.FillCategory,
			FillQueryName = attribute.FillQueryName
		};
	}

	private static ElementPropertyAttribute? FindAttribute(
		PropertyInfo property)
	{
		var direct = property.GetCustomAttribute<ElementPropertyAttribute>(
			inherit: false);
		if (direct is not null)
			return direct;
		var accessor = property.GetMethod ?? property.SetMethod;
		if (accessor is null)
			return null;
		var baseAccessor = accessor.GetBaseDefinition();
		if (ReferenceEquals(baseAccessor, accessor)
			|| baseAccessor.DeclaringType is null)
		{
			return null;
		}
		var baseProperty = baseAccessor.DeclaringType
			.GetProperties(
				BindingFlags.Instance
				| BindingFlags.Public
				| BindingFlags.NonPublic
				| BindingFlags.DeclaredOnly)
			.FirstOrDefault(candidate =>
				candidate.GetMethod == baseAccessor
				|| candidate.SetMethod == baseAccessor);
		return baseProperty?.GetCustomAttribute<ElementPropertyAttribute>(
			inherit: false);
	}

	private static void Validate(
		PropertyInfo property,
		ElementPropertyAttribute attribute)
	{
		if (property.DeclaringType is null
			|| !typeof(DomElement).IsAssignableFrom(property.DeclaringType))
		{
			return;
		}
		var identity = $"{property.DeclaringType?.FullName}.{property.Name}";
		if (attribute.IsElementDesignProperty
			&& (attribute.IsFillRequired
				|| attribute.IsXamlFillRequired
				|| attribute.IsXamlOutputProperty
				|| attribute.IsAuditRequired))
		{
			throw new InvalidOperationException(
				$"{identity}: element design properties cannot participate in instance pipelines.");
		}
		if (attribute.IsConstructionRequired && attribute.IsFillRequired)
		{
			throw new InvalidOperationException(
				$"{identity}: construction-required properties cannot be filled later.");
		}
		if (attribute.IsSlottedProperty && attribute.IsSlottedPropertyCollection)
		{
			throw new InvalidOperationException(
				$"{identity}: a property cannot be both a slot and a slot collection.");
		}
		var usesSlots = attribute.IsFillRequired
			|| attribute.IsXamlFillRequired
			|| attribute.IsXamlOutputProperty
			|| attribute.IsAuditRequired;
		if (usesSlots
			&& !attribute.IsSlottedProperty
			&& !attribute.IsSlottedPropertyCollection)
		{
			throw new InvalidOperationException(
				$"{identity}: pipeline properties must be slots or slot collections.");
		}
		if (attribute.IsXamlOutputProperty
			&& string.IsNullOrWhiteSpace(attribute.TargetProperty))
		{
			throw new InvalidOperationException(
				$"{identity}: XAML output requires TargetProperty.");
		}
		if (attribute.IsXamlOutputProperty
			&& attribute.Translation is
				PropertyTranslationKind.NotApplicable
				or PropertyTranslationKind.Unsupported)
		{
			throw new InvalidOperationException(
				$"{identity}: non-translatable properties cannot be emitted as XAML.");
		}
		var hasStaticQueryRoute =
			attribute.TraversalDeclaration
				== ElementTraversalDeclarationKind.Static;
		if (hasStaticQueryRoute
			&& (attribute.FillEvidenceKind == ElementEvidenceKind.None
				|| attribute.FillSlot == ElementFillSlot.Unspecified
				|| attribute.FillCategory == ElementSlotCategory.Unspecified))
		{
			throw new InvalidOperationException(
				$"{identity}: static traversal routes require evidence kind, slot, category, and query name.");
		}
		if (!hasStaticQueryRoute
			&& (attribute.FillEvidenceKind != ElementEvidenceKind.None
				|| attribute.FillSlot != ElementFillSlot.Unspecified
				|| attribute.FillCategory != ElementSlotCategory.Unspecified))
		{
			throw new InvalidOperationException(
				$"{identity}: owner-resolved traversal routes cannot contain partial static routing metadata.");
		}
	}

	private static PropertyInfo ReadProperty<TElement, TValue>(
		Expression<Func<TElement, TValue>> selector) =>
		selector.Body is MemberExpression { Member: PropertyInfo property }
			? property
			: throw new ArgumentException("表达式必须直接选择一个属性。", nameof(selector));
}

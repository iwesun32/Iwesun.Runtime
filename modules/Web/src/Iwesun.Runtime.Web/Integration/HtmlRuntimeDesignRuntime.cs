using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Iwesun.Runtime.WebView2;

namespace Iwesun.Runtime.Web;

/// <summary>
/// Owns the in-memory design graph reconstructed from DOM initialization,
/// source links, and runtime evidence. Runtime geometry is retained only as
/// acceptance evidence and never becomes an initialization value.
/// </summary>
public sealed class HtmlRuntimeDesignRuntime
{
	public HtmlRuntimeDesignRuntime()
	{
		Styles = new HtmlRuntimeStyleManager();
		Layout = new HtmlRuntimeLayoutManager(Styles);
		State = new HtmlRuntimeStateManager();
		Animations = new HtmlRuntimeAnimationManager();
	}

	public HtmlRuntimeStyleManager Styles { get; }

	public HtmlRuntimeLayoutManager Layout { get; }

	public HtmlRuntimeStateManager State { get; }

	public HtmlRuntimeAnimationManager Animations { get; }

	public void BeginXamlMaterialization()
	{
		Styles.ClearXamlTargets();
		Layout.ClearXamlTargets();
		Animations.ClearXamlTargets();
	}

	public void Observe(WebRuntimeDomIndexedProperty property)
	{
		Styles.Observe(property);
		Layout.Observe(property);
		State.Observe(property);
		Animations.Observe(property);
	}

	public void Observe(
		DomPropertyQueryContext context,
		DomPropertyQueryResult result)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(result);
		var status = result.Status switch
		{
			DomPropertyQueryStatus.Captured =>
				WebRuntimeDomPropertyStatus.Captured,
			DomPropertyQueryStatus.ConfirmedAbsent =>
				WebRuntimeDomPropertyStatus.ConfirmedAbsent,
			DomPropertyQueryStatus.SourceUnsupported =>
				WebRuntimeDomPropertyStatus.SourceUnsupported,
			_ => throw new InvalidDataException(
				$"Unsupported DOM query status {result.Status}.")
		};
		var valueSource = result.ValueSource switch
		{
			ElementPropertyValueSource.Unspecified =>
				WebRuntimeDomValueSource.Unspecified,
			ElementPropertyValueSource.ContainerAutomaticLayout =>
				WebRuntimeDomValueSource.ContainerAutomaticLayout,
			ElementPropertyValueSource.LinkedCalculation =>
				WebRuntimeDomValueSource.LinkedCalculation,
			ElementPropertyValueSource.LinkedConstant =>
				WebRuntimeDomValueSource.LinkedConstant,
			ElementPropertyValueSource.DirectConstant =>
				WebRuntimeDomValueSource.DirectConstant,
			_ => throw new InvalidDataException(
				$"Unsupported DOM value source {result.ValueSource}.")
		};
		var linkKind = result.Link.Kind switch
		{
			ElementPropertyLinkKind.None => WebRuntimeDomLinkKind.None,
			ElementPropertyLinkKind.DomDescription =>
				WebRuntimeDomLinkKind.DomDescription,
			ElementPropertyLinkKind.XPath => WebRuntimeDomLinkKind.XPath,
			ElementPropertyLinkKind.Url => WebRuntimeDomLinkKind.Url,
			ElementPropertyLinkKind.CssExpression =>
				WebRuntimeDomLinkKind.CssExpression,
			ElementPropertyLinkKind.LayoutExpression =>
				WebRuntimeDomLinkKind.LayoutExpression,
			ElementPropertyLinkKind.XamlBinding =>
				WebRuntimeDomLinkKind.XamlBinding,
			ElementPropertyLinkKind.ConstantReference =>
				WebRuntimeDomLinkKind.ConstantReference,
			ElementPropertyLinkKind.CustomString =>
				WebRuntimeDomLinkKind.CustomString,
			_ => throw new InvalidDataException(
				$"Unsupported DOM link kind {result.Link.Kind}.")
		};
		Observe(new(
			new(
				context.DocumentScope,
				context.XPath,
				context.TagName,
				context.PropertyName,
				context.ReflectedPropertyName,
				ConvertOwnerKind(context.OwnerKind),
				ConvertCategory(context.Category),
				ConvertEvidence(context.EvidenceKind),
				ConvertSlot(context.Slot)),
			status,
			status == WebRuntimeDomPropertyStatus.Captured
				? result.Value ?? string.Empty
				: string.Empty,
			status == WebRuntimeDomPropertyStatus.Captured
				? valueSource
				: WebRuntimeDomValueSource.Unspecified,
			status == WebRuntimeDomPropertyStatus.Captured
				? linkKind
				: WebRuntimeDomLinkKind.None,
			status == WebRuntimeDomPropertyStatus.Captured
				? result.Link.Description ?? string.Empty
				: string.Empty,
			result.Description));
	}

	private static WebRuntimeDomOwnerKind ConvertOwnerKind(
		ElementSlotOwnerKind value) => value switch
	{
		ElementSlotOwnerKind.Property => WebRuntimeDomOwnerKind.Property,
		ElementSlotOwnerKind.Attribute => WebRuntimeDomOwnerKind.Attribute,
		ElementSlotOwnerKind.ExtensionAttribute =>
			WebRuntimeDomOwnerKind.ExtensionAttribute,
		ElementSlotOwnerKind.RuntimeProperty =>
			WebRuntimeDomOwnerKind.RuntimeProperty,
		ElementSlotOwnerKind.DataSource => WebRuntimeDomOwnerKind.DataSource,
		ElementSlotOwnerKind.Event => WebRuntimeDomOwnerKind.Event,
		_ => throw new InvalidDataException($"Unsupported owner kind {value}.")
	};

	private static WebRuntimeDomSlotCategory ConvertCategory(
		ElementSlotCategory value) => value switch
	{
		ElementSlotCategory.Unspecified =>
			WebRuntimeDomSlotCategory.Unspecified,
		ElementSlotCategory.Space => WebRuntimeDomSlotCategory.Space,
		ElementSlotCategory.Style => WebRuntimeDomSlotCategory.Style,
		ElementSlotCategory.Effect => WebRuntimeDomSlotCategory.Effect,
		ElementSlotCategory.Action => WebRuntimeDomSlotCategory.Action,
		ElementSlotCategory.DataOrganization =>
			WebRuntimeDomSlotCategory.DataOrganization,
		_ => throw new InvalidDataException($"Unsupported category {value}.")
	};

	private static WebRuntimeDomDataSlot ConvertSlot(
		DomPropertyDataSlot value) => value switch
	{
		DomPropertyDataSlot.Initialization =>
			WebRuntimeDomDataSlot.Initialization,
		DomPropertyDataSlot.Link => WebRuntimeDomDataSlot.Link,
		DomPropertyDataSlot.Runtime => WebRuntimeDomDataSlot.Runtime,
		_ => throw new InvalidDataException($"Unsupported data slot {value}.")
	};

	private static WebRuntimeDomEvidenceKind ConvertEvidence(
		ElementEvidenceKind value)
	{
		var result = WebRuntimeDomEvidenceKind.None;
		Add(ElementEvidenceKind.Identity, WebRuntimeDomEvidenceKind.Identity);
		Add(
			ElementEvidenceKind.TreeRelationships,
			WebRuntimeDomEvidenceKind.TreeRelationships);
		Add(ElementEvidenceKind.Attributes, WebRuntimeDomEvidenceKind.Attributes);
		Add(
			ElementEvidenceKind.CssDeclarations,
			WebRuntimeDomEvidenceKind.CssDeclarations);
		Add(
			ElementEvidenceKind.ComputedStyles,
			WebRuntimeDomEvidenceKind.ComputedStyles);
		Add(ElementEvidenceKind.LocalLayout, WebRuntimeDomEvidenceKind.LocalLayout);
		Add(
			ElementEvidenceKind.DomRuntimeGeometry,
			WebRuntimeDomEvidenceKind.DomRuntimeGeometry);
		Add(
			ElementEvidenceKind.VisualEffects,
			WebRuntimeDomEvidenceKind.VisualEffects);
		Add(ElementEvidenceKind.Animations, WebRuntimeDomEvidenceKind.Animations);
		Add(ElementEvidenceKind.Events, WebRuntimeDomEvidenceKind.Events);
		Add(ElementEvidenceKind.Resources, WebRuntimeDomEvidenceKind.Resources);
		Add(
			ElementEvidenceKind.TextContent,
			WebRuntimeDomEvidenceKind.TextContent);
		Add(ElementEvidenceKind.FormState, WebRuntimeDomEvidenceKind.FormState);
		Add(ElementEvidenceKind.ScrollState, WebRuntimeDomEvidenceKind.ScrollState);
		return result;

		void Add(
			ElementEvidenceKind source,
			WebRuntimeDomEvidenceKind target)
		{
			if ((value & source) != 0)
				result |= target;
		}
	}
}

public sealed record HtmlRuntimePropertyEvidence(
	WebRuntimeDomDataSlot Slot,
	WebRuntimeDomPropertyStatus Status,
	string Value,
	WebRuntimeDomValueSource ValueSource,
	WebRuntimeDomLinkKind LinkKind,
	string LinkIdentity,
	string Description);

public sealed class HtmlRuntimeStyleBinding
{
	private readonly ConcurrentDictionary<
		WebRuntimeDomDataSlot,
		HtmlRuntimePropertyEvidence> _evidence = new();
	private readonly object _slotOwnerGate = new();
	private DomElementRuntimeProperty? _slotOwner;

	internal HtmlRuntimeStyleBinding(
		string documentScope,
		string xpath,
		string propertyName)
	{
		DocumentScope = documentScope;
		XPath = xpath;
		PropertyName = propertyName;
	}

	public string DocumentScope { get; }

	public string XPath { get; }

	public string PropertyName { get; }

	public IReadOnlyDictionary<WebRuntimeDomDataSlot, HtmlRuntimePropertyEvidence>
		Evidence => _evidence;

	public HtmlRuntimePropertyEvidence? Initialization =>
		_evidence.GetValueOrDefault(WebRuntimeDomDataSlot.Initialization);

	public HtmlRuntimePropertyEvidence? Link =>
		_evidence.GetValueOrDefault(WebRuntimeDomDataSlot.Link);

	public HtmlRuntimePropertyEvidence? Runtime =>
		_evidence.GetValueOrDefault(WebRuntimeDomDataSlot.Runtime);

	public IReadOnlyList<string> VariableReferences =>
		Initialization?.Status == WebRuntimeDomPropertyStatus.Captured
			? CssVariableReference.Matches(Initialization.Value)
				.Select(static match => match.Groups[1].Value)
				.Distinct(StringComparer.Ordinal)
				.ToArray()
			: [];

	public bool UsesBrowserDefault =>
		Initialization?.Status == WebRuntimeDomPropertyStatus.ConfirmedAbsent
		&& Link?.Status == WebRuntimeDomPropertyStatus.ConfirmedAbsent
		&& Runtime?.Status == WebRuntimeDomPropertyStatus.Captured;

	internal void Set(HtmlRuntimePropertyEvidence evidence)
	{
		_evidence[evidence.Slot] = evidence;
		lock (_slotOwnerGate)
		{
			if (_slotOwner is not null)
				ApplyEvidence(_slotOwner, evidence);
		}
	}

	internal DomElementRuntimeProperty GetSlotOwner()
	{
		lock (_slotOwnerGate)
		{
			if (_slotOwner is not null)
				return _slotOwner;
			var definition = DomElementRuntimePropertyCatalog.Standard
				.FirstOrDefault(candidate => candidate.Name.Equals(
					PropertyName,
					StringComparison.Ordinal))
				?? throw new InvalidDataException(
					$"Global style property '{PropertyName}' has no static "
						+ "Runtime Web definition.");
			var owner = new DomElementRuntimeProperty(
				definition.Name,
				definition.Category,
				definition.EvidenceKind);
			foreach (var evidence in _evidence.Values
				.OrderBy(static item => item.Slot))
			{
				ApplyEvidence(owner, evidence);
			}
			_slotOwner = owner;
			return owner;
		}
	}

	private static void ApplyEvidence(
		DomElementRuntimeProperty owner,
		HtmlRuntimePropertyEvidence evidence)
	{
		var result = evidence.Status switch
		{
			WebRuntimeDomPropertyStatus.Captured =>
				DomPropertyQueryResult.Captured(
					evidence.Value,
					ConvertValueSource(evidence.ValueSource),
					evidence.LinkKind == WebRuntimeDomLinkKind.None
						? ElementPropertyLink.None
						: new(
							ConvertLinkKind(evidence.LinkKind),
							evidence.LinkIdentity),
					evidence.Description),
			WebRuntimeDomPropertyStatus.ConfirmedAbsent =>
				DomPropertyQueryResult.ConfirmedAbsent(evidence.Description),
			WebRuntimeDomPropertyStatus.SourceUnsupported =>
				DomPropertyQueryResult.SourceUnsupported(evidence.Description),
			_ => throw new InvalidDataException(
				$"Unsupported style evidence status {evidence.Status}.")
		};
		owner.ApplyDomQueryResult(
			(DomPropertyDataSlot)(int)evidence.Slot,
			result);
	}

	private static ElementPropertyValueSource ConvertValueSource(
		WebRuntimeDomValueSource value) => value switch
	{
		WebRuntimeDomValueSource.Unspecified =>
			ElementPropertyValueSource.Unspecified,
		WebRuntimeDomValueSource.ContainerAutomaticLayout =>
			ElementPropertyValueSource.ContainerAutomaticLayout,
		WebRuntimeDomValueSource.LinkedCalculation =>
			ElementPropertyValueSource.LinkedCalculation,
		WebRuntimeDomValueSource.LinkedConstant =>
			ElementPropertyValueSource.LinkedConstant,
		WebRuntimeDomValueSource.DirectConstant =>
			ElementPropertyValueSource.DirectConstant,
		_ => throw new InvalidDataException(
			$"Unsupported style value source {value}.")
	};

	private static ElementPropertyLinkKind ConvertLinkKind(
		WebRuntimeDomLinkKind value) => value switch
	{
		WebRuntimeDomLinkKind.DomDescription =>
			ElementPropertyLinkKind.DomDescription,
		WebRuntimeDomLinkKind.XPath => ElementPropertyLinkKind.XPath,
		WebRuntimeDomLinkKind.Url => ElementPropertyLinkKind.Url,
		WebRuntimeDomLinkKind.CssExpression =>
			ElementPropertyLinkKind.CssExpression,
		WebRuntimeDomLinkKind.LayoutExpression =>
			ElementPropertyLinkKind.LayoutExpression,
		WebRuntimeDomLinkKind.XamlBinding =>
			ElementPropertyLinkKind.XamlBinding,
		WebRuntimeDomLinkKind.ConstantReference =>
			ElementPropertyLinkKind.ConstantReference,
		WebRuntimeDomLinkKind.CustomString =>
			ElementPropertyLinkKind.CustomString,
		_ => throw new InvalidDataException(
			$"Unsupported style link kind {value}.")
	};

	private static readonly Regex CssVariableReference = new(
		@"var\(\s*(--[A-Za-z0-9_-]+)",
		RegexOptions.CultureInvariant);
}

/// <summary>
/// Catalogues element-to-CSS data-point bindings without flattening runtime
/// computed values into authored initialization values.
/// </summary>
public sealed class HtmlRuntimeStyleManager
{
	private readonly ConcurrentDictionary<
		HtmlRuntimeBindingIdentity,
		HtmlRuntimeStyleBinding> _bindings = new();
	private readonly ConcurrentDictionary<string, byte> _dataPointIds =
		new(StringComparer.Ordinal);
	private readonly ConcurrentBag<HtmlRuntimeXamlStyleTargetBinding>
		_xamlTargets = [];

	public IReadOnlyCollection<HtmlRuntimeStyleBinding> Bindings =>
		_bindings.Values.ToArray();

	public IReadOnlyCollection<string> DataPointIds => _dataPointIds.Keys.ToArray();

	public IReadOnlyCollection<HtmlRuntimeXamlStyleTargetBinding> XamlTargets =>
		_xamlTargets.ToArray();

	public HtmlRuntimeStyleBinding? Find(
		string documentScope,
		string xpath,
		string propertyName) =>
		_bindings.GetValueOrDefault(
			new(documentScope, xpath, propertyName));

	public HtmlRuntimeStyleResolution? ResolveForXaml(
		string documentScope,
		string xpath,
		string propertyName)
	{
		var binding = Find(documentScope, xpath, propertyName);
		if (binding is null)
			return null;
		var initialization = binding.Initialization;
		var runtime = binding.Runtime;
		var hasInitialization =
			initialization?.Status == WebRuntimeDomPropertyStatus.Captured;
		var hasRuntimeValue =
			runtime?.Status == WebRuntimeDomPropertyStatus.Captured;
		if (!hasInitialization && !hasRuntimeValue)
			return null;
		var expression = hasInitialization
			? initialization!.Value
			: runtime!.Value;
		var runtimeDiffersFromInitialization = hasRuntimeValue
			&& hasInitialization
			&& !runtime!.Value.Equals(expression, StringComparison.Ordinal);
		var requiresRuntimeResolution =
			!hasInitialization
			|| binding.Link?.Status == WebRuntimeDomPropertyStatus.Captured
			|| binding.VariableReferences.Count != 0
			|| runtimeDiffersFromInitialization
			|| expression.Contains("calc(", StringComparison.OrdinalIgnoreCase)
			|| expression.Contains("min(", StringComparison.OrdinalIgnoreCase)
			|| expression.Contains("max(", StringComparison.OrdinalIgnoreCase)
			|| expression.Contains("clamp(", StringComparison.OrdinalIgnoreCase);
		var concreteValue = hasRuntimeValue
				? runtime!.Value
				: expression;
		return new(
			binding,
			expression,
			concreteValue,
			binding.Link?.LinkIdentity ?? string.Empty,
			requiresRuntimeResolution);
	}

	public void RegisterXamlTarget(
		HtmlRuntimeStyleResolution resolution,
		object target,
		string targetProperty,
		string? compositeTargetId = null,
		int compositeIndex = -1,
		int compositePartCount = 0,
		XamlCompositeValueKind compositeValueKind = XamlCompositeValueKind.None)
	{
		ArgumentNullException.ThrowIfNull(resolution);
		ArgumentNullException.ThrowIfNull(target);
		ArgumentException.ThrowIfNullOrWhiteSpace(targetProperty);
		_xamlTargets.Add(new(
			resolution.Binding,
			target,
			targetProperty,
			resolution.Expression,
			resolution.ConcreteValue,
			resolution.DataPointId,
			resolution.RequiresRuntimeResolution,
			compositeTargetId,
			compositeIndex,
			compositePartCount,
			compositeValueKind));
	}

	internal void ClearXamlTargets() => _xamlTargets.Clear();

	internal void Observe(WebRuntimeDomIndexedProperty property)
	{
		if (!property.Identity.PropertyName.StartsWith(
			"style.",
			StringComparison.Ordinal))
		{
			return;
		}
		var binding = _bindings.GetOrAdd(
			new(
				property.Identity.DocumentScope,
				property.Identity.XPath,
				property.Identity.PropertyName),
			static identity => new(
				identity.DocumentScope,
				identity.XPath,
				identity.PropertyName));
		binding.Set(ToEvidence(property));
		if (property.Status == WebRuntimeDomPropertyStatus.Captured
			&& property.LinkKind == WebRuntimeDomLinkKind.CssExpression)
		{
			_dataPointIds.TryAdd(property.LinkIdentity, 0);
		}
	}

	internal static HtmlRuntimePropertyEvidence ToEvidence(
		WebRuntimeDomIndexedProperty property) =>
		new(
			property.Identity.Slot,
			property.Status,
			property.Value,
			property.ValueSource,
			property.LinkKind,
			property.LinkIdentity,
			property.Description);
}

public sealed record HtmlRuntimeStyleResolution(
	HtmlRuntimeStyleBinding Binding,
	string Expression,
	string ConcreteValue,
	string DataPointId,
	bool RequiresRuntimeResolution);

/// <summary>
/// Describes a CSS dimension keyword that must actively replace an authored
/// XAML initialization value.  A runtime keyword is never equivalent to
/// "skip assignment": doing so would leave a stale authored width constraint.
/// </summary>
public enum HtmlRuntimeDimensionOverrideKind
{
	Unsupported,
	AutomaticSize,
	ZeroMinimum,
	UnboundedMaximum
}

public static class HtmlRuntimeDimensionOverride
{
	public static HtmlRuntimeDimensionOverrideKind Resolve(
		string targetProperty,
		string concreteCssValue)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(targetProperty);
		ArgumentException.ThrowIfNullOrWhiteSpace(concreteCssValue);
		var keyword = concreteCssValue.Trim();
		if (targetProperty is "Width" or "Height"
			&& keyword.Equals("auto", StringComparison.OrdinalIgnoreCase))
		{
			return HtmlRuntimeDimensionOverrideKind.AutomaticSize;
		}
		if (targetProperty is "MinWidth" or "MinHeight"
			&& keyword.Equals("auto", StringComparison.OrdinalIgnoreCase))
		{
			return HtmlRuntimeDimensionOverrideKind.ZeroMinimum;
		}
		if (targetProperty is "MaxWidth" or "MaxHeight"
			&& keyword.Equals("none", StringComparison.OrdinalIgnoreCase))
		{
			return HtmlRuntimeDimensionOverrideKind.UnboundedMaximum;
		}
		return HtmlRuntimeDimensionOverrideKind.Unsupported;
	}
}

public sealed record HtmlRuntimeXamlStyleTargetBinding(
	HtmlRuntimeStyleBinding Source,
	object Target,
	string TargetProperty,
	string Expression,
	string InitialConcreteValue,
	string DataPointId,
	bool RequiresRuntimeResolution,
	string? CompositeTargetId = null,
	int CompositeIndex = -1,
	int CompositePartCount = 0,
	XamlCompositeValueKind CompositeValueKind = XamlCompositeValueKind.None);

public sealed class HtmlRuntimeLayoutBinding
{
	private readonly ConcurrentDictionary<
		WebRuntimeDomDataSlot,
		HtmlRuntimePropertyEvidence> _evidence = new();

	internal HtmlRuntimeLayoutBinding(
		HtmlRuntimeBindingIdentity identity,
		HtmlRuntimeStyleBinding? style)
	{
		Identity = identity;
		Style = style;
	}

	public HtmlRuntimeBindingIdentity Identity { get; }

	public HtmlRuntimeStyleBinding? Style { get; }

	public IReadOnlyDictionary<WebRuntimeDomDataSlot, HtmlRuntimePropertyEvidence>
		Evidence => _evidence;

	public HtmlRuntimePropertyEvidence? RuntimeAcceptance =>
		_evidence.GetValueOrDefault(WebRuntimeDomDataSlot.Runtime);

	internal void Set(HtmlRuntimePropertyEvidence evidence) =>
		_evidence[evidence.Slot] = evidence;
}

/// <summary>
/// Preserves container/style inputs separately from post-layout rectangle
/// evidence. A parent XPath identity alone is not considered a complete layout
/// formula.
/// </summary>
public sealed class HtmlRuntimeLayoutManager
{
	private static readonly HashSet<string> LayoutStyleProperties = new(
		[
			"style.display", "style.position", "style.width", "style.height",
			"style.minWidth", "style.minHeight", "style.maxWidth",
			"style.maxHeight", "style.boxSizing", "style.margin",
			"style.marginTop", "style.marginRight", "style.marginBottom",
			"style.marginLeft", "style.padding", "style.paddingTop",
			"style.paddingRight", "style.paddingBottom", "style.paddingLeft",
			"style.flex", "style.flexDirection",
			"style.flexWrap", "style.flexGrow", "style.flexShrink",
			"style.flexBasis", "style.order", "style.gap",
			"style.rowGap", "style.columnGap", "style.justifyContent",
			"style.alignContent", "style.alignItems", "style.alignSelf",
			"style.grid", "style.gridTemplateColumns",
			"style.gridTemplateRows", "style.gridAutoFlow"
		],
		StringComparer.Ordinal);
	private readonly HtmlRuntimeStyleManager _styles;
	private readonly ConcurrentDictionary<
		HtmlRuntimeBindingIdentity,
		HtmlRuntimeLayoutBinding> _bindings = new();
	private readonly ConcurrentBag<HtmlRuntimeXamlLayoutTargetBinding>
		_xamlTargets = [];
	private readonly object _viewportGate = new();
	private HtmlRuntimeViewportState _viewport = new(
		0,
		0,
		0,
		0,
		0);

	internal HtmlRuntimeLayoutManager(HtmlRuntimeStyleManager styles) =>
		_styles = styles;

	public IReadOnlyCollection<HtmlRuntimeLayoutBinding> Bindings =>
		_bindings.Values.ToArray();

	public IReadOnlyCollection<HtmlRuntimeXamlLayoutTargetBinding> XamlTargets =>
		_xamlTargets.ToArray();

	public HtmlRuntimeViewportState Viewport
	{
		get
		{
			lock (_viewportGate)
				return _viewport;
		}
	}

	public void ConfigureDesignViewport(double width, double height)
	{
		RequireViewport(width, height);
		lock (_viewportGate)
		{
			_viewport = _viewport with
			{
				DesignWidth = width,
				DesignHeight = height,
				Revision = _viewport.Revision + 1
			};
		}
	}

	public void ApplyRuntimeViewport(double width, double height)
	{
		RequireViewport(width, height);
		lock (_viewportGate)
		{
			_viewport = _viewport with
			{
				RuntimeWidth = width,
				RuntimeHeight = height,
				Revision = _viewport.Revision + 1
			};
		}
	}

	public void ApplyCssRuntimeViewport(double width, double height)
	{
		RequireViewport(width, height);
		lock (_viewportGate)
		{
			_viewport = _viewport with
			{
				CssRuntimeWidth = width,
				CssRuntimeHeight = height,
				Revision = _viewport.Revision + 1
			};
		}
	}

	public double ScaleCssPixelToRuntime(double value, bool vertical)
	{
		if (!double.IsFinite(value))
			throw new ArgumentOutOfRangeException(nameof(value));
		var viewport = Viewport;
		var cssExtent = vertical
			? viewport.CssRuntimeHeight
			: viewport.CssRuntimeWidth;
		var runtimeExtent = vertical
			? viewport.RuntimeHeight
			: viewport.RuntimeWidth;
		if (cssExtent <= 0 || runtimeExtent <= 0)
			return value;
		return value * runtimeExtent / cssExtent;
	}

	public HtmlRuntimeLayoutBinding? Find(
		string documentScope,
		string xpath,
		string propertyName) =>
		_bindings.GetValueOrDefault(
			new(documentScope, xpath, propertyName));

	internal void Observe(WebRuntimeDomIndexedProperty property)
	{
		var name = property.Identity.PropertyName;
		if (!name.StartsWith("rect.", StringComparison.Ordinal)
			&& !LayoutStyleProperties.Contains(name))
		{
			return;
		}
		var identity = new HtmlRuntimeBindingIdentity(
			property.Identity.DocumentScope,
			property.Identity.XPath,
			name);
		var binding = _bindings.GetOrAdd(
			identity,
			key => new(
				key,
				_styles.Find(
					key.DocumentScope,
					key.XPath,
					key.PropertyName)));
		binding.Set(HtmlRuntimeStyleManager.ToEvidence(property));
	}

	public void RegisterXamlTarget(
		string documentScope,
		string xpath,
		object target,
		XamlElementMappingKind mappingKind,
		bool isMaterialized,
		string mechanism)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(documentScope);
		ArgumentException.ThrowIfNullOrWhiteSpace(xpath);
		ArgumentNullException.ThrowIfNull(target);
		ArgumentException.ThrowIfNullOrWhiteSpace(mechanism);
		_xamlTargets.Add(new(
			new(documentScope, xpath, "layout.container"),
			target,
			mappingKind,
			isMaterialized,
			mechanism));
	}

	internal void ClearXamlTargets() => _xamlTargets.Clear();

	private static void RequireViewport(double width, double height)
	{
		if (!double.IsFinite(width)
			|| !double.IsFinite(height)
			|| width <= 0
			|| height <= 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(width),
				"Layout viewport dimensions must be finite and positive.");
		}
	}
}

public sealed record HtmlRuntimeViewportState(
	double DesignWidth,
	double DesignHeight,
	double RuntimeWidth,
	double RuntimeHeight,
	long Revision,
	double CssRuntimeWidth = 0,
	double CssRuntimeHeight = 0);

public sealed record HtmlRuntimeXamlLayoutTargetBinding(
	HtmlRuntimeBindingIdentity Owner,
	object Target,
	XamlElementMappingKind MappingKind,
	bool IsMaterialized,
	string Mechanism);

public sealed class HtmlRuntimeStateManager
{
	private readonly ConcurrentDictionary<
		HtmlRuntimeBindingIdentity,
		HtmlRuntimePropertyEvidence> _runtimeStates = new();

	public IReadOnlyDictionary<
		HtmlRuntimeBindingIdentity,
		HtmlRuntimePropertyEvidence> RuntimeStates => _runtimeStates;

	public HtmlRuntimePropertyEvidence? Find(
		string documentScope,
		string xpath,
		string propertyName) =>
		_runtimeStates.GetValueOrDefault(
			new(documentScope, xpath, propertyName));

	internal void Observe(WebRuntimeDomIndexedProperty property)
	{
		if (property.Identity.Slot != WebRuntimeDomDataSlot.Runtime
			|| (property.Identity.EvidenceKind
				& (WebRuntimeDomEvidenceKind.FormState
					| WebRuntimeDomEvidenceKind.ScrollState)) == 0)
		{
			return;
		}
		_runtimeStates[new(
			property.Identity.DocumentScope,
			property.Identity.XPath,
			property.Identity.PropertyName)] =
			HtmlRuntimeStyleManager.ToEvidence(property);
	}
}

public readonly record struct HtmlRuntimeBindingIdentity(
	string DocumentScope,
	string XPath,
	string PropertyName);

public sealed class HtmlRuntimeStyleConnection(
	HtmlRuntimeStyleManager manager) : IHtmlRuntimePropertyConnection
{
	private readonly HtmlRuntimeStyleManager _manager =
		manager ?? throw new ArgumentNullException(nameof(manager));

	public string Name => "html-runtime.style-manager";

	public ElementEvidenceKind EvidenceKind =>
		ElementEvidenceKind.CssDeclarations
		| ElementEvidenceKind.ComputedStyles;

	public bool IsConnected => true;

	public ValueTask<DomPropertyQueryResult> QueryAsync(
		DomPropertyQueryContext context,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (context.PropertyName.Equals(
			"service.globalStyle",
			StringComparison.Ordinal))
		{
			return ValueTask.FromResult(
				CreateServiceConnectionResult(context, Name));
		}
		var binding = _manager.Find(
			context.DocumentScope,
			context.XPath,
			context.PropertyName);
		return ValueTask.FromResult(
			HtmlRuntimeConnectionResult.FromEvidence(
				binding?.Evidence.GetValueOrDefault(
					ToRuntimeSlot(context.Slot)),
				"Style manager has no evidence for this reflected slot."));
	}

	private static WebRuntimeDomDataSlot ToRuntimeSlot(
		DomPropertyDataSlot slot) => slot switch
	{
		DomPropertyDataSlot.Initialization =>
			WebRuntimeDomDataSlot.Initialization,
		DomPropertyDataSlot.Link => WebRuntimeDomDataSlot.Link,
		DomPropertyDataSlot.Runtime => WebRuntimeDomDataSlot.Runtime,
		_ => throw new ArgumentOutOfRangeException(nameof(slot))
	};

	private static DomPropertyQueryResult CreateServiceConnectionResult(
		DomPropertyQueryContext context,
		string connectionName) =>
		context.Slot == DomPropertyDataSlot.Link
			? DomPropertyQueryResult.Captured(
				connectionName,
				ElementPropertyValueSource.LinkedConstant,
				new(
					ElementPropertyLinkKind.ConstantReference,
					connectionName),
				"Element is linked to the HTML root style manager.")
			: DomPropertyQueryResult.ConfirmedAbsent(
				"Root style service has no DOM initialization/runtime value.");
}

public sealed class HtmlRuntimeLayoutConnection(
	HtmlRuntimeLayoutManager manager) : IHtmlRuntimePropertyConnection
{
	private readonly HtmlRuntimeLayoutManager _manager =
		manager ?? throw new ArgumentNullException(nameof(manager));

	public string Name => "html-runtime.layout-manager";

	public ElementEvidenceKind EvidenceKind =>
		ElementEvidenceKind.LocalLayout
		| ElementEvidenceKind.DomRuntimeGeometry;

	public bool IsConnected => true;

	public ValueTask<DomPropertyQueryResult> QueryAsync(
		DomPropertyQueryContext context,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (context.PropertyName.Equals(
			"service.globalLayout",
			StringComparison.Ordinal))
		{
			return ValueTask.FromResult(
				context.Slot == DomPropertyDataSlot.Link
					? DomPropertyQueryResult.Captured(
						Name,
						ElementPropertyValueSource.LinkedConstant,
						new(
							ElementPropertyLinkKind.ConstantReference,
							Name),
						"Element is linked to the HTML root layout manager.")
					: DomPropertyQueryResult.ConfirmedAbsent(
						"Root layout service has no DOM initialization/runtime value."));
		}
		var binding = _manager.Find(
			context.DocumentScope,
			context.XPath,
			context.PropertyName);
		var slot = context.Slot switch
		{
			DomPropertyDataSlot.Initialization =>
				WebRuntimeDomDataSlot.Initialization,
			DomPropertyDataSlot.Link => WebRuntimeDomDataSlot.Link,
			DomPropertyDataSlot.Runtime => WebRuntimeDomDataSlot.Runtime,
			_ => throw new ArgumentOutOfRangeException(nameof(context))
		};
		return ValueTask.FromResult(
			HtmlRuntimeConnectionResult.FromEvidence(
				binding?.Evidence.GetValueOrDefault(slot),
				"Layout manager has no evidence for this reflected slot."));
	}
}

public sealed class HtmlRuntimeStateConnection(
	HtmlRuntimeStateManager manager) : IHtmlRuntimePropertyConnection
{
	private readonly HtmlRuntimeStateManager _manager =
		manager ?? throw new ArgumentNullException(nameof(manager));

	public string Name => "html-runtime.state-manager";

	public ElementEvidenceKind EvidenceKind =>
		ElementEvidenceKind.FormState
		| ElementEvidenceKind.ScrollState;

	public bool IsConnected => true;

	public ValueTask<DomPropertyQueryResult> QueryAsync(
		DomPropertyQueryContext context,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var evidence = context.Slot == DomPropertyDataSlot.Runtime
			? _manager.Find(
				context.DocumentScope,
				context.XPath,
				context.PropertyName)
			: null;
		return ValueTask.FromResult(
			HtmlRuntimeConnectionResult.FromEvidence(
				evidence,
				"State manager has no evidence for this reflected slot."));
	}
}

internal static class HtmlRuntimeConnectionResult
{
	internal static DomPropertyQueryResult FromEvidence(
		HtmlRuntimePropertyEvidence? evidence,
		string missingDescription)
	{
		if (evidence is null)
			return DomPropertyQueryResult.ConfirmedAbsent(missingDescription);
		return evidence.Status switch
		{
			WebRuntimeDomPropertyStatus.ConfirmedAbsent =>
				DomPropertyQueryResult.ConfirmedAbsent(evidence.Description),
			WebRuntimeDomPropertyStatus.SourceUnsupported =>
				DomPropertyQueryResult.SourceUnsupported(evidence.Description),
			WebRuntimeDomPropertyStatus.Captured =>
				DomPropertyQueryResult.Captured(
					evidence.Value,
					ToValueSource(evidence.ValueSource),
					ToLink(evidence),
					evidence.Description),
			_ => throw new ArgumentOutOfRangeException(nameof(evidence))
		};
	}

	private static ElementPropertyValueSource ToValueSource(
		WebRuntimeDomValueSource source) => source switch
	{
		WebRuntimeDomValueSource.ContainerAutomaticLayout =>
			ElementPropertyValueSource.ContainerAutomaticLayout,
		WebRuntimeDomValueSource.LinkedCalculation =>
			ElementPropertyValueSource.LinkedCalculation,
		WebRuntimeDomValueSource.LinkedConstant =>
			ElementPropertyValueSource.LinkedConstant,
		WebRuntimeDomValueSource.DirectConstant =>
			ElementPropertyValueSource.DirectConstant,
		_ => throw new InvalidDataException(
			"Captured manager evidence has no value source.")
	};

	private static ElementPropertyLink ToLink(
		HtmlRuntimePropertyEvidence evidence) =>
		evidence.LinkKind switch
		{
			WebRuntimeDomLinkKind.None => ElementPropertyLink.None,
			WebRuntimeDomLinkKind.DomDescription =>
				new(ElementPropertyLinkKind.DomDescription, evidence.LinkIdentity),
			WebRuntimeDomLinkKind.XPath =>
				new(ElementPropertyLinkKind.XPath, evidence.LinkIdentity),
			WebRuntimeDomLinkKind.Url =>
				new(ElementPropertyLinkKind.Url, evidence.LinkIdentity),
			WebRuntimeDomLinkKind.CssExpression =>
				new(ElementPropertyLinkKind.CssExpression, evidence.LinkIdentity),
			WebRuntimeDomLinkKind.LayoutExpression =>
				new(ElementPropertyLinkKind.LayoutExpression, evidence.LinkIdentity),
			WebRuntimeDomLinkKind.XamlBinding =>
				new(ElementPropertyLinkKind.XamlBinding, evidence.LinkIdentity),
			WebRuntimeDomLinkKind.ConstantReference =>
				new(ElementPropertyLinkKind.ConstantReference, evidence.LinkIdentity),
			WebRuntimeDomLinkKind.CustomString =>
				new(ElementPropertyLinkKind.CustomString, evidence.LinkIdentity),
			_ => throw new ArgumentOutOfRangeException(nameof(evidence))
		};
}

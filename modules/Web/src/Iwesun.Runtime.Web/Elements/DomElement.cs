using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Iwesun.Runtime.Web;

public enum DomEmbeddingOwnerKind
{
	Iframe,
	ShadowHost
}

public sealed record GeneratedXamlAttribute(
	string Name,
	string Value,
	IXamlPropertySlotOwner? Source,
	IReadOnlyList<IXamlPropertySlotOwner>? ContributingSources = null,
	XamlCompositeValueKind CompositeValueKind = XamlCompositeValueKind.None)
{
	public IReadOnlyList<IXamlPropertySlotOwner> Sources =>
		ContributingSources
		?? (Source is null
			? []
			: [Source]);
}

public enum XamlCompositeValueKind
{
	None,
	Thickness,
	CornerRadius,
	SumLengths,
	UniformValue
}

public sealed record XamlGridTrackDefinition(
	string Length,
	string? Minimum,
	string? Maximum)
{
	public LayoutLength StrongLength { get; } = Parse(Length, allowFraction: true);

	public LayoutLength? StrongMinimum { get; } = ParseOptional(Minimum);

	public LayoutLength? StrongMaximum { get; } = ParseOptional(Maximum);

	private static LayoutLength? ParseOptional(string? value) =>
		string.IsNullOrWhiteSpace(value)
			? null
			: Parse(value, allowFraction: true);

	private static LayoutLength Parse(string value, bool allowFraction)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(value);
		var normalized = value.Trim();
		if (normalized.Equals("Auto", StringComparison.OrdinalIgnoreCase))
			return new LayoutLength.Automatic();
		if (allowFraction && normalized.EndsWith('*'))
		{
			var coefficient = normalized.Length == 1
				? 1
				: double.Parse(
					normalized[..^1],
					NumberStyles.Float,
					CultureInfo.InvariantCulture);
			return new LayoutLength.Fraction(coefficient);
		}
		if (double.TryParse(
			normalized,
			NumberStyles.Float,
			CultureInfo.InvariantCulture,
			out var pixels))
		{
			// Track numbers are produced by the CSS layout plan after resolving
			// CSS lengths. They remain CSS pixels until the target adapter maps
			// them through HtmlRuntimeLayoutManager's runtime viewport contract.
			return new LayoutLength.Constant(pixels, LayoutLengthUnit.CssPixel);
		}
		throw new ArgumentException(
			$"XAML grid track length '{value}' has no strong layout representation.",
			nameof(value));
	}
}

/// <summary>
/// HTML、SVG、MathML 和自定义 DOM 元素的抽象根类型。
/// 本阶段只定义元素类型的固有设计规范，不定义页面实例数据。
/// </summary>
public abstract class DomElement
{
	private readonly List<DomElement> _children = [];
	private readonly List<object> _xamlOwnedObjects = [];
	private IReadOnlyList<DomElementDataSource>? _dataSources;
	private int? _maximumHierarchyLevel;
	private bool _hasCompletedDomFill;

	protected DomElement(DomElementMapping mapping)
	{
		ArgumentNullException.ThrowIfNull(mapping);
		ArgumentException.ThrowIfNullOrWhiteSpace(mapping.DocumentScope);
		if (string.IsNullOrWhiteSpace(mapping.XPath)
			|| !mapping.XPath.StartsWith("/", StringComparison.Ordinal))
		{
			throw new ArgumentException(
				"DOM element XPath must be absolute.",
				nameof(mapping));
		}
		if (mapping.NodeId < 0 || mapping.BackendNodeId < 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(mapping),
				"CDP node identities cannot be negative.");
		}

		DocumentScope = mapping.DocumentScope;
		XPath = mapping.XPath;
		NodeId = mapping.NodeId;
		BackendNodeId = mapping.BackendNodeId;
		ParentXPath = mapping.ParentXPath;
		LeftSiblingXPath = mapping.LeftSiblingXPath;
		RightSiblingXPath = mapping.RightSiblingXPath;
		CapturedOwnText = mapping.CapturedOwnText;
		CapturedOwnTextElementInsertionIndex =
			mapping.CapturedOwnTextElementInsertionIndex;
		CapturedTextContent = mapping.CapturedTextContent;
		CapturedAttributeValues =
			new Dictionary<string, string>(
				mapping.CapturedAttributeValues,
				StringComparer.OrdinalIgnoreCase);
	}

	internal abstract object CreateXamlObject(
		IXamlElementObjectFactory factory,
		XamlElementObjectPlan plan);

	internal abstract void FillXamlObjectProperties(
		IXamlElementObjectFactory factory,
		object xamlElement,
		XamlElementObjectPlan plan);

	[ElementProperty(
		PropertyValueKind.Identity,
		PropertyValueStage.TypeIntrinsic,
		Translation = PropertyTranslationKind.NotApplicable,
		IsElementDesignProperty = true,
		Description = "规范标签名称，用于确定具体元素类型。")]
	public abstract string TagName { get; }

	public bool HasCompletedDomFill => _hasCompletedDomFill;

	[ElementProperty(
		PropertyValueKind.Enumeration,
		PropertyValueStage.TypeIntrinsic,
		Translation = PropertyTranslationKind.NotApplicable,
		IsElementDesignProperty = true,
		Description = "元素所属的 HTML、SVG、MathML 或自定义命名空间。")]
	public abstract ElementNamespace ElementNamespace { get; }

	[ElementProperty(
		PropertyValueKind.Enumeration,
		PropertyValueStage.TypeIntrinsic,
		Translation = PropertyTranslationKind.NotApplicable,
		IsElementDesignProperty = true,
		Description = "元素在规范中的主要职责分类。")]
	public abstract ElementCategory Category { get; }

	[ElementProperty(
		PropertyValueKind.Enumeration,
		PropertyValueStage.TypeIntrinsic,
		Translation = PropertyTranslationKind.NotApplicable,
		IsElementDesignProperty = true,
		Description = "元素固有的视觉表达形态，不表示当前是否可见。")]
	public abstract ElementVisualKind VisualKind { get; }

	[ElementProperty(
		PropertyValueKind.Enumeration,
		PropertyValueStage.TypeIntrinsic,
		Translation = PropertyTranslationKind.NotApplicable,
		IsElementDesignProperty = true,
		Description = "元素按规范允许承载的内容类型。")]
	public abstract ElementContentModel ContentModel { get; }

	[ElementProperty(
		PropertyValueKind.Enumeration,
		PropertyValueStage.TypeIntrinsic,
		Translation = PropertyTranslationKind.NotApplicable,
		IsElementDesignProperty = true,
		Description = "元素是非可视、开放容器、封闭叶节点还是替换控件。")]
	public abstract ElementClosure Closure { get; }

	[ElementProperty(
		PropertyValueKind.Enumeration,
		PropertyValueStage.TypeIntrinsic,
		Translation = PropertyTranslationKind.NotApplicable,
		IsElementDesignProperty = true,
		Description = "元素使用普通、void、原始文本或外部命名空间语法。")]
	public abstract ElementSyntax Syntax { get; }

	[ElementProperty(
		PropertyValueKind.Enumeration,
		PropertyValueStage.TypeIntrinsic,
		Translation = PropertyTranslationKind.NotApplicable,
		IsElementDesignProperty = true,
		Description = "元素转换为 XAML 的固有支持等级。")]
	public abstract XamlConversionSupport XamlSupport { get; }

	[ElementProperty(
		PropertyValueKind.Enumeration,
		PropertyValueStage.TypeIntrinsic,
		Translation = PropertyTranslationKind.NotApplicable,
		IsElementDesignProperty = true,
		Description = "元素默认对应的 XAML 控件族。")]
	public abstract XamlControlFamily XamlControlFamily { get; }

	[ElementProperty(
		PropertyValueKind.Identity,
		PropertyValueStage.TypeIntrinsic,
		Translation = PropertyTranslationKind.NotApplicable,
		IsElementDesignProperty = true,
		Description = "元素默认翻译成的精确 XAML 元素名称。")]
	public abstract string XamlElementName { get; }

	[ElementProperty(
		PropertyValueKind.Enumeration,
		PropertyValueStage.TypeIntrinsic,
		Translation = PropertyTranslationKind.NotApplicable,
		IsElementDesignProperty = true,
		Description = "没有页面样式时，元素规范定义的默认显示方式。")]
	public abstract ElementDefaultDisplay DefaultDisplay { get; }

	[ElementProperty(
		PropertyValueKind.Enumeration,
		PropertyValueStage.TypeIntrinsic,
		Translation = PropertyTranslationKind.NotApplicable,
		IsElementDesignProperty = true,
		Description = "元素设计上支持的交互职责。")]
	public abstract ElementInteractionKind InteractionKind { get; }

	[ElementProperty(
		PropertyValueKind.Enumeration,
		PropertyValueStage.TypeIntrinsic,
		Translation = PropertyTranslationKind.NotApplicable,
		IsElementDesignProperty = true,
		Description = "子元素生成 XAML 后在目标控件中的安放位置。")]
	public abstract ElementXamlChildPlacementKind XamlChildPlacement { get; }

	public virtual DomElementQuerySpecialization QuerySpecialization =>
		DomElementQuerySpecialization.General;

	[ElementProperty(
		PropertyValueKind.Identity,
		PropertyValueStage.DomRuntime,
		Translation = PropertyTranslationKind.NotApplicable,
		IsManagementProperty = true,
		IsDomMappingProperty = true,
		IsConstructionRequired = true,
		IsRuntimeDerivedProperty = true,
		Description = "DOM 文档作用域；与 XPath 共同构成元素的稳定查询身份。")]
	public string DocumentScope { get; }

	[ElementProperty(
		PropertyValueKind.Identity,
		PropertyValueStage.DomRuntime,
		Translation = PropertyTranslationKind.NotApplicable,
		IsManagementProperty = true,
		IsDomMappingProperty = true,
		IsConstructionRequired = true,
		IsRuntimeDerivedProperty = true,
		Description = "当前元素在所属文档作用域内的绝对 XPath。")]
	public string XPath { get; }

	[ElementProperty(
		PropertyValueKind.Identity,
		PropertyValueStage.DomRuntime,
		Translation = PropertyTranslationKind.NotApplicable,
		IsManagementProperty = true,
		IsDomMappingProperty = true,
		IsConstructionRequired = true,
		IsRuntimeDerivedProperty = true,
		Description = "当前 CDP DOM 会话中的前端 nodeId；零表示该来源不提供 CDP 身份。")]
	public int NodeId { get; }

	[ElementProperty(
		PropertyValueKind.Identity,
		PropertyValueStage.DomRuntime,
		Translation = PropertyTranslationKind.NotApplicable,
		IsManagementProperty = true,
		IsDomMappingProperty = true,
		IsConstructionRequired = true,
		IsRuntimeDerivedProperty = true,
		Description = "跨 CDP DOM/DOMSnapshot 结果关联使用的 backendNodeId；零表示不可用。")]
	public int BackendNodeId { get; }

	[ElementProperty(
		PropertyValueKind.Identity,
		PropertyValueStage.DomRuntime,
		Translation = PropertyTranslationKind.NotApplicable,
		IsManagementProperty = true,
		IsDomMappingProperty = true,
		IsConstructionRequired = true,
		IsRuntimeDerivedProperty = true,
		Description = "创建元素对象时捕获的直接父元素 XPath；根元素允许为空。")]
	public string? ParentXPath { get; }

	[ElementProperty(
		PropertyValueKind.Identity,
		PropertyValueStage.DomRuntime,
		Translation = PropertyTranslationKind.NotApplicable,
		IsManagementProperty = true,
		IsDomMappingProperty = true,
		IsConstructionRequired = true,
		IsRuntimeDerivedProperty = true,
		Description = "创建元素对象时捕获的左侧相邻兄弟 XPath；不存在时为空。")]
	public string? LeftSiblingXPath { get; }

	[ElementProperty(
		PropertyValueKind.Identity,
		PropertyValueStage.DomRuntime,
		Translation = PropertyTranslationKind.NotApplicable,
		IsManagementProperty = true,
		IsDomMappingProperty = true,
		IsConstructionRequired = true,
		IsRuntimeDerivedProperty = true,
		Description = "创建元素对象时捕获的右侧相邻兄弟 XPath；不存在时为空。")]
	public string? RightSiblingXPath { get; }

	public string CapturedOwnText { get; }

	public int CapturedOwnTextElementInsertionIndex { get; }

	public string CapturedTextContent { get; }

	public IReadOnlyDictionary<string, string> CapturedAttributeValues
	{
		get;
	}

	[ElementProperty(
		PropertyValueKind.State,
		PropertyValueStage.DomRuntime,
		Translation = PropertyTranslationKind.NotApplicable,
		IsManagementProperty = true,
		IsObjectTreeRelationshipProperty = true,
		IsRuntimeDerivedProperty = true,
		Description = "当前元素在 DOM 对象树中的直接父元素。")]
	public DomElement? Parent { get; private set; }

	[ElementProperty(
		PropertyValueKind.State,
		PropertyValueStage.DomRuntime,
		Translation = PropertyTranslationKind.NotApplicable,
		IsManagementProperty = true,
		IsObjectTreeRelationshipProperty = true,
		IsRuntimeDerivedProperty = true,
		Description = "Element that owns this detached iframe or author-shadow DOM scope.")]
	public DomElement? EmbeddingOwner { get; private set; }

	[ElementProperty(
		PropertyValueKind.State,
		PropertyValueStage.DomRuntime,
		Translation = PropertyTranslationKind.NotApplicable,
		IsManagementProperty = true,
		IsObjectTreeRelationshipProperty = true,
		IsRuntimeDerivedProperty = true,
		Description = "Strong kind of the detached DOM scope ownership relationship.")]
	public DomEmbeddingOwnerKind? EmbeddingOwnerKind { get; private set; }

	[ElementProperty(
		PropertyValueKind.State,
		PropertyValueStage.DomRuntime,
		Translation = PropertyTranslationKind.NotApplicable,
		IsManagementProperty = true,
		IsObjectTreeRelationshipProperty = true,
		IsRuntimeDerivedProperty = true,
		Description = "当前元素直接拥有的、有序 DOM 子元素列表。")]
	public IReadOnlyList<DomElement> Children => _children;

	[ElementProperty(
		PropertyValueKind.State,
		PropertyValueStage.TypeIntrinsic,
		Translation = PropertyTranslationKind.NotApplicable,
		IsElementDesignProperty = true,
		Description = "One-based hierarchy level within the owning document tree.")]
	public int HierarchyLevel =>
		Parent is not null
			? Parent.HierarchyLevel + 1
			: EmbeddingOwner is not null
				? EmbeddingOwner.HierarchyLevel + 1
				: 1;

	[ElementProperty(
		PropertyValueKind.State,
		PropertyValueStage.TypeIntrinsic,
		Translation = PropertyTranslationKind.NotApplicable,
		IsElementDesignProperty = true,
		Description = "Maximum hierarchy level recursively processed from this document root.")]
	public int? MaximumHierarchyLevel
	{
		get => _maximumHierarchyLevel;
		set
		{
			if (value is <= 0)
			{
				throw new ArgumentOutOfRangeException(
					nameof(value),
					"Maximum hierarchy level must be positive.");
			}
			if (Parent is not null)
			{
				throw new InvalidOperationException(
					"Maximum hierarchy level can only be assigned to a document root.");
			}
			_maximumHierarchyLevel = value;
		}
	}

	public int? EffectiveMaximumHierarchyLevel
	{
		get
		{
			var root = this;
			while (root.Parent is not null)
				root = root.Parent;
			return root.MaximumHierarchyLevel
				?? root.EmbeddingOwner?.EffectiveMaximumHierarchyLevel;
		}
	}

	[ElementProperty(
		PropertyValueKind.State,
		PropertyValueStage.DomRuntime,
		Translation = PropertyTranslationKind.NotApplicable,
		IsManagementProperty = true,
		IsObjectTreeRelationshipProperty = true,
		IsRuntimeDerivedProperty = true,
		Description = "当前元素在父元素有序子列表中的左侧相邻兄弟。")]
	public DomElement? LeftSibling => GetSibling(-1);

	[ElementProperty(
		PropertyValueKind.State,
		PropertyValueStage.DomRuntime,
		Translation = PropertyTranslationKind.NotApplicable,
		IsManagementProperty = true,
		IsObjectTreeRelationshipProperty = true,
		IsRuntimeDerivedProperty = true,
		Description = "当前元素在父元素有序子列表中的右侧相邻兄弟。")]
	public DomElement? RightSibling => GetSibling(1);

	/// <summary>
	/// 运行时生成的 UI 框架元素引用（WinUI FrameworkElement、WPF Control 等）。
	/// 类型为 object 以保持 UI 框架中立，消费项目负责转换为具体框架类型。
	/// 在 ToXaml() 生成控件树后由消费代码设置，用于后续属性填充、事件挂载。
	/// 子元素的 XAML 引用在完整树建立后由消费代码遍历 DomElement 树统一填充，
	/// XAML 控件自身的 Children/Content 关系由 UI 框架维护，不在这里重复存储。
	/// </summary>
	[ElementProperty(
		PropertyValueKind.State,
		PropertyValueStage.WinUiRuntime,
		Translation = PropertyTranslationKind.NotApplicable,
		IsManagementProperty = true,
		IsObjectTreeRelationshipProperty = true,
		IsRuntimeDerivedProperty = true,
		Description = "Primary live XAML object projected from this DOM element.")]
	public object? XamlElement { get; set; }

	[ElementProperty(
		PropertyValueKind.State,
		PropertyValueStage.WinUiRuntime,
		Translation = PropertyTranslationKind.NotApplicable,
		IsManagementProperty = true,
		IsObjectTreeRelationshipProperty = true,
		IsRuntimeDerivedProperty = true,
		Description = "Primary XAML build node, including the actual projected child-object tree.")]
	public XamlElementObjectBuildNode? XamlObjectNode { get; private set; }

	[ElementProperty(
		PropertyValueKind.State,
		PropertyValueStage.WinUiRuntime,
		Translation = PropertyTranslationKind.NotApplicable,
		IsManagementProperty = true,
		IsObjectTreeRelationshipProperty = true,
		IsRuntimeDerivedProperty = true,
		Description = "All primary and synthetic XAML objects owned by this DOM element.")]
	public IReadOnlyList<object> XamlOwnedObjects => _xamlOwnedObjects;

	[ElementProperty(
		PropertyValueKind.State,
		PropertyValueStage.DomRuntime,
		Translation = PropertyTranslationKind.NotApplicable,
		IsManagementProperty = true,
		IsObjectTreeRelationshipProperty = true,
		IsRuntimeDerivedProperty = true,
		Description = "HTML document root that owns this live element instance.")]
	public HtmlDocumentRoot? HtmlRoot { get; private set; }

	/// <summary>
	/// DOM 属性查询引擎，用于从 WebView2 实时 DOM 中查询属性值。
	/// 子类可以重写此属性提供元素特有的查询逻辑，或在运行时替换为具体实现。
	/// 为 null 时 DomFillAsync 使用传入的委托参数。
	/// </summary>
	public IDomPropertyQueryEngine? DomQueryEngine { get; set; }

	/// <summary>
	/// XAML 属性查询引擎，用于从已生成的 XAML 控件树中查询运行时值。
	/// 子类可以重写此属性提供元素特有的查询逻辑，或在运行时替换为具体实现。
	/// 为 null 时 XamlFillAsync 使用传入的委托参数。
	/// </summary>
	public IXamlPropertyQueryEngine? XamlQueryEngine { get; set; }

	/// <summary>
	/// 返回当前元素默认的 DOM 属性查询委托。
	/// 子类可以重写此方法提供元素特有的属性查询逻辑（如 button 的 disabled 特殊处理）。
	/// 默认实现使用 DomQueryEngine（如果设置），否则返回 null 表示使用外部传入的委托。
	/// </summary>
	protected virtual DomPropertyQueryDelegate? CreateDefaultDomQueryDelegate() =>
		DomQueryEngine is not null
			? (scope, xpath, name, slot) => DomQueryEngine.QueryAsync(
				new DomPropertyQueryContext(
					scope,
					xpath,
					TagName,
					name,
					name,
					ElementSlotOwnerKind.Property,
					ElementSlotCategory.DataOrganization,
					ElementEvidenceKind.None,
					slot,
					this),
				CancellationToken.None)
			: null;

	/// <summary>
	/// 返回当前元素默认的 XAML 属性查询委托。
	/// 子类可以重写此方法提供元素特有的 XAML 查询逻辑。
	/// 默认实现使用 XamlQueryEngine（如果设置），否则返回 null 表示使用外部传入的委托。
	/// </summary>
	protected virtual XamlPropertyQueryDelegate? CreateDefaultXamlQueryDelegate() =>
		XamlQueryEngine is not null
			? (scope, xpath, name, slot) => XamlQueryEngine.QueryAsync(
				new XamlPropertyQueryContext(
					scope,
					xpath,
					TagName,
					this,
					XamlElement,
					GetXamlElementMapping(),
					name,
					name,
					ElementSlotOwnerKind.Property,
					ElementSlotCategory.DataOrganization,
					ElementEvidenceKind.None,
					slot,
					new XamlPropertyExecutionDescriptor(name, string.Empty, XamlPropertyExecutionKind.Unsupported, false, false, false),
					XamlSourcePropertyEvidence.Empty,
					XamlTargetPropertyEvidence.Empty),
				CancellationToken.None)
			: null;

	/// <summary>
	/// 使用默认或传入的委托填充 DOM 端槽位。
	/// 如果元素设置了 DomQueryEngine 或子类重写了 CreateDefaultDomQueryDelegate，使用默认委托；
	/// 否则使用传入的 query 委托。
	/// </summary>
	public ValueTask<DomElementFillResult> DomFillAsync(
		CancellationToken cancellationToken = default)
	{
		if (HtmlRoot is not null)
			return DomFillFromHtmlRootAsync(cancellationToken);
		if (DomQueryEngine is not null)
			return DomFillAsync(DomQueryEngine, cancellationToken);
		var defaultQuery = CreateDefaultDomQueryDelegate();
		return defaultQuery is not null
			? DomFillAsync(defaultQuery, cancellationToken)
			: throw new InvalidOperationException(
				$"元素 {TagName} 未设置 DomQueryEngine 且未传入查询委托。请设置 DomQueryEngine 或传入 query 参数。");
	}

	/// <summary>
	/// Fills only this element's reflected DOM slots. This is the representative
	/// case inspection entry point; it uses the same mounted HTML-root query
	/// engine as full-tree fill and deliberately does not traverse children.
	/// </summary>
	public async ValueTask<DomElementFillResult> DomFillOwnAsync(
		CancellationToken cancellationToken = default)
	{
		if (HtmlRoot is null)
		{
			throw new InvalidOperationException(
				$"Element {DocumentScope}::{XPath} must be mounted before "
				+ "representative DOM fill.");
		}
		var slots = await FillOwnFromHtmlRootAsync(cancellationToken);
		return new(
			$"{DocumentScope}::{XPath}",
			slots,
			[]);
	}

	private async ValueTask<DomElementFillResult> DomFillFromHtmlRootAsync(
		CancellationToken cancellationToken)
	{
		var slots = await FillOwnFromHtmlRootAsync(cancellationToken);
		var children = EnumerateDomChildren().ToArray();
		var childResults = new List<DomElementFillResult>(children.Length);
		foreach (var child in children)
		{
			cancellationToken.ThrowIfCancellationRequested();
			childResults.Add(await child.DomFillAsync(cancellationToken));
		}
		return new(
			$"{DocumentScope}::{XPath}",
			slots,
			childResults);
	}

	private async ValueTask<IReadOnlyList<DomPropertyFillTrace>>
		FillOwnFromHtmlRootAsync(CancellationToken cancellationToken)
	{
		var htmlRoot = HtmlRoot
			?? throw new InvalidOperationException(
				$"Element {DocumentScope}::{XPath} is not mounted.");
		if (htmlRoot.EvidenceSnapshot is { } snapshot)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return ApplyPreparedDomEvidence(
				htmlRoot,
				snapshot.ReadElement(this));
		}
		var pending = CreateDomFillRequests();
		var queryResults = await htmlRoot.QueryManyAsync(
			pending.Select(static item => item.Context).ToArray(),
			cancellationToken);
		if (queryResults.Count != pending.Count)
		{
			throw new InvalidDataException(
				$"HTML root returned {queryResults.Count} results for "
				+ $"{pending.Count} requested slots on {DocumentScope}::{XPath}.");
		}
		return ApplyDomEvidence(
			htmlRoot,
			pending,
			queryResults);
	}

	private IReadOnlyList<DomPropertyFillTrace> ApplyPreparedDomEvidence(
		HtmlDocumentRoot htmlRoot,
		IReadOnlyList<DomEvidenceSnapshotEntry> entries)
	{
		var pending = new DomElementFillRequest[entries.Count];
		var results = new DomPropertyQueryResult[entries.Count];
		for (var index = 0; index < entries.Count; index++)
		{
			var entry = entries[index];
			var owner = entry.Owner
				?? throw new InvalidDataException(
					$"Prepared DOM evidence for {DocumentScope}::{XPath} "
						+ "does not retain its strong slot owner.");
			pending[index] = new(owner, entry.Context);
			results[index] = entry.Result;
		}
		return ApplyDomEvidence(htmlRoot, pending, results);
	}

	private IReadOnlyList<DomPropertyFillTrace> ApplyDomEvidence(
		HtmlDocumentRoot htmlRoot,
		IReadOnlyList<DomElementFillRequest> pending,
		IReadOnlyList<DomPropertyQueryResult> queryResults)
	{
		var slots = new List<DomPropertyFillTrace>(pending.Count);
		var consistencyOwners = new HashSet<IElementSlotConsistencyOwner>(
			ReferenceEqualityComparer.Instance);
		for (var index = 0; index < pending.Count; index++)
		{
			var owner = pending[index].Owner;
			var context = pending[index].Context;
			var result = queryResults[index]
				?? throw new InvalidDataException(
					$"DOM result {index} was not materialized.");
			var application = owner.ApplyDomQueryResult(
				context.Slot,
				result);
			slots.Add(new(
				owner.Name,
				context.Slot,
				result.Status,
				result.Value,
				result.ValueSource,
				result.Link,
				result.Description,
				application,
				context.ReflectedPropertyName,
				context.OwnerKind,
				context.Category,
				context.EvidenceKind));
			if (owner is IElementSlotConsistencyOwner consistencyOwner)
				consistencyOwners.Add(consistencyOwner);
		}
		foreach (var owner in consistencyOwners)
			owner.ValidateSlotConsistency();
		_hasCompletedDomFill = true;
		htmlRoot.ReportDomFillElementCompleted(this);
		return slots;
	}


	internal IReadOnlyList<DomElementFillRequest> CreateDomFillRequests()
	{
		ValidateOwnTraversalContract();
		var pending = new List<DomElementFillRequest>();
		foreach (var traits in ElementPropertyTraitsReflector
			.GetAttributes(GetType())
			.Where(static traits => traits.IsFillRequired))
		{
			var property = GetType().GetProperty(traits.PropertyName)
				?? throw new InvalidOperationException(
					$"{GetType().FullName}.{traits.PropertyName} 不存在。");
			foreach (var owner in EnumerateDomFillOwners(
				property.GetValue(this)))
			{
				var category = ResolveSlotCategory(owner);
				var ownerKind = ResolveSlotOwnerKind(
					owner,
					traits.PropertyName);
				var evidenceKind = ResolveEvidenceKind(
					ownerKind,
					category,
					owner);
				foreach (var slot in owner.DomSlots)
				{
					var context = new DomPropertyQueryContext(
						DocumentScope,
						XPath,
						TagName,
						traits.PropertyName,
						owner.Name,
						ownerKind,
						category,
						evidenceKind,
						slot,
						this);
					context.Traversal.Validate();
					pending.Add(new(owner, context));
				}
			}
		}
		return pending;
	}

	public bool TryGetDomStringSlotValue(
		string propertyName,
		DomPropertyDataSlot slot,
		out string value)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
		foreach (var traits in ElementPropertyTraitsReflector.GetAttributes(GetType())
			.Where(static traits => traits.IsFillRequired))
		{
			var property = GetType().GetProperty(traits.PropertyName)
				?? throw new InvalidOperationException(
					$"{GetType().FullName}.{traits.PropertyName} does not exist.");
			foreach (var owner in EnumerateDomFillOwners(property.GetValue(this)))
			{
				if (!owner.Name.Equals(propertyName, StringComparison.Ordinal)
					|| owner is not ElementSlottedProperty<string> slotted)
					continue;
				var source = slot switch
				{
					DomPropertyDataSlot.Initialization => slotted.SourceInitialization,
					DomPropertyDataSlot.Runtime => slotted.SourceRuntime,
					_ => ElementPropertySlot<string>.Unset
				};
				if (source.IsSet)
				{
					value = source.Value ?? string.Empty;
					return true;
				}
			}
		}
		value = string.Empty;
		return false;
	}

	/// <summary>
	/// 使用默认或传入的委托填充 XAML 端槽位。
	/// 如果元素设置了 XamlQueryEngine 或子类重写了 CreateDefaultXamlQueryDelegate，使用默认委托；
	/// 否则使用传入的 query 委托。
	/// </summary>
	public ValueTask<XamlElementFillResult> XamlFillAsync(
		CancellationToken cancellationToken = default)
	{
		if (XamlQueryEngine is not null)
			return XamlFillAsync(XamlQueryEngine, cancellationToken);
		var defaultQuery = CreateDefaultXamlQueryDelegate();
		return defaultQuery is not null
			? XamlFillAsync(defaultQuery, cancellationToken)
			: throw new InvalidOperationException(
				$"元素 {TagName} 未设置 XamlQueryEngine 且未传入查询委托。请设置 XamlQueryEngine 或传入 query 参数。");
	}

	[ElementProperty(
		PropertyValueKind.State,
		PropertyValueStage.DomRuntime,
		Translation = PropertyTranslationKind.Composite,
		Comparison = PropertyComparisonKind.Semantic,
		IsSlottedProperty = true,
		IsFillRequired = true,
		IsXamlFillRequired = true,
		IsAuditRequired = true,
		Description = "Link to the inherited global responsive-layout service.")]
	public DomElementStringProperty GlobalLayoutService { get; } =
		new("service.globalLayout", ElementDataOrganizationSlotKind.Custom);

	[ElementProperty(
		PropertyValueKind.State,
		PropertyValueStage.DomRuntime,
		Translation = PropertyTranslationKind.Composite,
		Comparison = PropertyComparisonKind.Semantic,
		IsSlottedProperty = true,
		IsFillRequired = true,
		IsXamlFillRequired = true,
		IsAuditRequired = true,
		Description = "Link to the inherited global computed-style service.")]
	public DomElementStringProperty GlobalStyleService { get; } =
		new("service.globalStyle", ElementDataOrganizationSlotKind.Custom);

	[ElementProperty(
		PropertyValueKind.State,
		PropertyValueStage.DomRuntime,
		Translation = PropertyTranslationKind.Composite,
		IsEventCollectionProperty = true,
		IsFillRequired = true,
		IsSlottedPropertyCollection = true,
		IsXamlFillRequired = true,
		IsAuditRequired = true,
		Description = "当前元素拥有的 DOM/XAML 双端事件槽位集合。")]
	public List<DomElementEvent> Events { get; } = [];

	[ElementProperty(
		PropertyValueKind.Geometry,
		PropertyValueStage.DomRuntime,
		Translation = PropertyTranslationKind.Direct,
		Comparison = PropertyComparisonKind.NumericTolerance,
		IsSlottedPropertyCollection = true,
		IsFillRequired = true,
		IsXamlFillRequired = true,
		IsAuditRequired = true,
		Description = "DOM and rendered XAML runtime geometry acceptance slots.")]
	public DomElementRuntimeGeometry RuntimeGeometry { get; } = new();

	[ElementProperty(
		PropertyValueKind.State,
		PropertyValueStage.DomRuntime,
		Translation = PropertyTranslationKind.Composite,
		IsDataSourceCollectionProperty = true,
		IsFillRequired = true,
		IsSlottedPropertyCollection = true,
		IsXamlFillRequired = true,
		IsAuditRequired = true,
		Description = "当前元素向 XAML 控件提供的强类型双端数据源槽位集合。")]
	public IReadOnlyList<DomElementDataSource> DataSources =>
		_dataSources ??= CreateDataSources();

	protected virtual IReadOnlyList<DomElementDataSource> CreateDataSources() =>
		[];

	public void AddChild(DomElement child)
	{
		ArgumentNullException.ThrowIfNull(child);
		if (ReferenceEquals(this, child))
			throw new InvalidOperationException("An element cannot contain itself.");
		for (DomElement? ancestor = this; ancestor is not null; ancestor = ancestor.Parent)
		{
			if (ReferenceEquals(ancestor, child))
				throw new InvalidOperationException("The DOM tree cannot contain cycles.");
		}
		if (child.Parent is not null)
			throw new InvalidOperationException("The child already belongs to a parent.");
		if (!string.Equals(DocumentScope, child.DocumentScope, StringComparison.Ordinal))
			throw new InvalidOperationException("Parent and child must use the same document scope.");
		if (child.ParentXPath is not null
			&& !string.Equals(child.ParentXPath, XPath, StringComparison.Ordinal))
		{
			throw new InvalidOperationException(
				"The child's captured parent XPath does not match the parent element.");
		}
		if (HtmlRoot is not null)
			child.AttachHtmlRoot(HtmlRoot);
		else if (child.HtmlRoot is not null)
		{
			throw new InvalidOperationException(
				"A mounted element cannot be attached below an unmounted parent.");
		}
		child.Parent = this;
		_children.Add(child);
	}

	internal void AttachHtmlRoot(HtmlDocumentRoot htmlRoot)
	{
		ArgumentNullException.ThrowIfNull(htmlRoot);
		if (HtmlRoot is not null && !ReferenceEquals(HtmlRoot, htmlRoot))
		{
			throw new InvalidOperationException(
				$"Element {DocumentScope}::{XPath} already belongs to another HTML root.");
		}
		HtmlRoot = htmlRoot;
		foreach (var child in _children)
			child.AttachHtmlRoot(htmlRoot);
	}

	internal void AttachEmbeddingOwner(
		DomElement owner,
		DomEmbeddingOwnerKind ownerKind)
	{
		ArgumentNullException.ThrowIfNull(owner);
		if (Parent is not null)
		{
			throw new InvalidOperationException(
				"Only a detached DOM scope root can have an embedding owner.");
		}
		if (ownerKind == DomEmbeddingOwnerKind.Iframe
			&& !owner.TagName.Equals(
			"iframe",
			StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException(
				"A nested document root must be owned by an iframe element.");
		}
		if (EmbeddingOwner is not null)
		{
			throw new InvalidOperationException(
				"The detached DOM scope root already has an embedding owner.");
		}
		EmbeddingOwner = owner;
		EmbeddingOwnerKind = ownerKind;
	}

	public void ValidateTraversalContract()
	{
		ValidateOwnTraversalContract();
		foreach (var child in EnumerateDomChildren())
			child.ValidateTraversalContract();
	}

	private void ValidateOwnTraversalContract()
	{
		var type = GetType();
		var reflectedTraits = ElementPropertyTraitsReflector.GetAttributes(type);
		var traitNames = reflectedTraits
			.Select(static traits => traits.PropertyName)
			.ToHashSet(StringComparer.Ordinal);
		var publicProperties = type.GetProperties(
			BindingFlags.Instance | BindingFlags.Public);
		var missingTraits = publicProperties
			.Where(static property => IsSlotOwnerPropertyType(property.PropertyType))
			.Where(property => !traitNames.Contains(property.Name))
			.Select(static property => property.Name)
			.Order(StringComparer.Ordinal)
			.ToArray();
		if (missingTraits.Length != 0)
		{
			throw new InvalidOperationException(
				$"{type.FullName} has public slot-owner properties without "
				+ $"{nameof(ElementPropertyAttribute)}: "
				+ string.Join(", ", missingTraits));
		}

		var owners = new List<(string ReflectedPropertyName, IDomFillSlotOwner Owner)>();
		foreach (var traits in reflectedTraits.Where(static traits =>
			traits.IsFillRequired
				|| traits.IsXamlFillRequired
				|| traits.IsAuditRequired))
		{
			var property = type.GetProperty(traits.PropertyName)
				?? throw new InvalidOperationException(
					$"{type.FullName}.{traits.PropertyName} is missing.");
			var value = property.GetValue(this);
			owners.AddRange(EnumerateDomFillOwners(value)
				.Select(owner => (traits.PropertyName, owner)));
		}

		var duplicateNames = owners
			.GroupBy(
				item => (
					item.ReflectedPropertyName,
					OwnerKind: ResolveSlotOwnerKind(
						item.Owner,
						item.ReflectedPropertyName),
					item.Owner.Name))
			.Where(static group => group.Count() > 1)
			.Select(static group =>
				$"{group.Key.ReflectedPropertyName}/"
				+ $"{group.Key.OwnerKind}/{group.Key.Name}")
			.Order(StringComparer.Ordinal)
			.ToArray();
		if (duplicateNames.Length != 0)
		{
			throw new InvalidOperationException(
				$"{DocumentScope}::{XPath} has duplicate strong slot identities: "
				+ string.Join(", ", duplicateNames));
		}

		foreach (var item in owners)
		{
			DomPropertyDataSlot[] expectedDomSlots =
				item.Owner is IRuntimeOnlySlotOwner
				? [DomPropertyDataSlot.Runtime]
				:
				[
					DomPropertyDataSlot.Initialization,
					DomPropertyDataSlot.Link,
					DomPropertyDataSlot.Runtime
				];
			if (!item.Owner.DomSlots.SequenceEqual(expectedDomSlots))
			{
				throw new InvalidOperationException(
					$"{item.ReflectedPropertyName}/{item.Owner.Name} does not "
					+ "match its compile-time DOM slot shape.");
			}
			XamlPropertyDataSlot[] expectedXamlSlots =
				item.Owner is IRuntimeOnlySlotOwner
				? [XamlPropertyDataSlot.Runtime]
				:
				[
					XamlPropertyDataSlot.Initialization,
					XamlPropertyDataSlot.Link,
					XamlPropertyDataSlot.Runtime
				];
			if (item.Owner is not IXamlFillSlotOwner xamlOwner
				|| !xamlOwner.XamlSlots.SequenceEqual(expectedXamlSlots))
			{
				throw new InvalidOperationException(
					$"{item.ReflectedPropertyName}/{item.Owner.Name} must expose all XAML slots exactly once.");
			}
			if (item.Owner is not IDomElementSlotAuditOwner auditOwner
				|| auditOwner.Category == ElementSlotCategory.Unspecified)
			{
				throw new InvalidOperationException(
					$"{item.ReflectedPropertyName}/{item.Owner.Name} has no unambiguous slot category.");
			}
		}
	}

	private static bool IsSlotOwnerPropertyType(Type propertyType)
	{
		if (typeof(IDomFillSlotOwner).IsAssignableFrom(propertyType)
			|| typeof(IXamlFillSlotOwner).IsAssignableFrom(propertyType))
		{
			return true;
		}
		return propertyType
			.GetInterfaces()
			.Append(propertyType)
			.Where(static type => type.IsGenericType)
			.Where(static type =>
				type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
			.Select(static type => type.GetGenericArguments()[0])
			.Any(static itemType =>
				typeof(IDomFillSlotOwner).IsAssignableFrom(itemType)
					|| typeof(IXamlFillSlotOwner).IsAssignableFrom(itemType));
	}

	private static IEnumerable<IDomFillSlotOwner> EnumerateDomFillOwners(
		object? value)
	{
		if (value is IDomFillSlotOwner owner)
			return [owner];
		if (value is System.Collections.IEnumerable collection)
			return collection.Cast<object>().OfType<IDomFillSlotOwner>();
		throw new InvalidOperationException(
			$"Pipeline properties must implement {nameof(IDomFillSlotOwner)}.");
	}

	public DomElementAuditReport Audit()
	{
		ValidateOwnTraversalContract();
		var children = EnumerateDomChildren().ToArray();
		var childReports = new List<DomElementAuditReport>(children.Length);
		foreach (var child in children)
			childReports.Add(child.Audit());

		var ownResults = new List<DomElementSlottedPropertyAuditResult>();
		var ownAuditTraits = XamlSupport == XamlConversionSupport.NonVisual
			? []
			: ElementPropertyTraitsReflector
				.GetAttributes(GetType())
				.Where(static traits => traits.IsAuditRequired)
				.ToArray();
		foreach (var traits in ownAuditTraits)
		{
			var property = GetType().GetProperty(traits.PropertyName)
				?? throw new InvalidOperationException(
					$"{GetType().FullName}.{traits.PropertyName} 不存在。");
			foreach (var owner in EnumerateAuditOwners(property.GetValue(this)))
			{
				try
				{
					ownResults.Add(AuditSlottedProperty(owner, traits));
				}
				catch (Exception exception)
				{
					ownResults.Add(
						DomElementSlottedPropertyAuditResult.ExecutionFailure(
							owner.Name,
							owner.Category,
							exception));
				}
			}
		}

		var ownPassed = (ownResults.Count > 0
				|| XamlSupport == XamlConversionSupport.NonVisual)
			&& ownResults.All(static result => result.Passed);
		var passed = ownPassed
			&& childReports.All(static result => result.Passed);
		var statistics = DomElementAuditStatistics.Create(
			passed,
			ownResults,
			childReports);
		var text = new System.Text.StringBuilder();
		foreach (var child in childReports)
			text.Append(child.TextReport);
		text.Append("Element ")
			.Append(DocumentScope)
			.Append("::")
			.Append(XPath)
			.Append(' ')
			.Append(passed ? "PASSED" : "FAILED")
			.AppendLine();
		foreach (var result in ownResults)
		{
			text.Append("  ")
				.Append(result.Category)
				.Append('.')
				.Append(result.PropertyName)
				.Append(' ')
				.Append(result.Passed ? "PASSED" : "FAILED")
				.Append("; initialization=")
				.Append(result.Initialization.Status)
				.Append("; runtime=")
				.Append(result.Runtime.Status)
				.Append("; link=")
				.Append(result.Link.Status)
				.Append("; layout=")
				.Append(result.Layout.Status)
				.Append("; ")
				.AppendLine(result.Description);
		}
		if (ownResults.Count == 0)
		{
			text.AppendLine(
				XamlSupport == XamlConversionSupport.NonVisual
					? "  PASSED: nonvisual element requires no own slot audit."
					: "  FAILED: no slotted property was audited.");
		}
		return new(
			$"{DocumentScope}::{XPath}",
			passed,
			ownResults,
			childReports,
			statistics,
			text.ToString());
	}

	protected virtual DomElementSlottedPropertyAuditResult AuditSlottedProperty(
		IDomElementSlotAuditOwner owner)
	{
		ArgumentNullException.ThrowIfNull(owner);
		return owner.AuditSlots();
	}

	protected virtual DomElementSlottedPropertyAuditResult AuditSlottedProperty(
		IDomElementSlotAuditOwner owner,
		ElementPropertyTraits traits)
	{
		ArgumentNullException.ThrowIfNull(owner);
		return owner.AuditSlots(traits);
	}

	private static IEnumerable<IDomElementSlotAuditOwner> EnumerateAuditOwners(
		object? value)
	{
		if (value is IDomElementSlotAuditOwner owner)
			return [owner];
		if (value is IEnumerable<IDomElementSlotAuditOwner> collection)
			return collection;
		throw new InvalidOperationException(
			$"标记 IsSlottedProperty 的属性必须实现 "
			+ $"{nameof(IDomElementSlotAuditOwner)}。");
	}

	public virtual string ToXaml(TextWriter output)
	{
		ArgumentNullException.ThrowIfNull(output);
		ValidateTraversalContract();
		var builder = new System.Text.StringBuilder();
		ToXaml(builder, depth: 0, isDocumentRoot: true);
		var xaml = builder.ToString();
		output.Write(xaml);
		return xaml;
	}

	public XamlElementObjectBuildResult BuildXamlObjectTree(
		IXamlElementObjectFactory factory)
	{
		ArgumentNullException.ThrowIfNull(factory);
		ValidateTraversalContract();
		ValidateXamlBuildReadinessRecursive();
		var plans = BuildXamlObjectPlans();
		var roots = plans
			.Select(plan => BuildXamlObjectNode(factory, plan))
			.ToArray();
		return new(
			roots.Select(static node => node.Element).ToArray(),
			roots);
	}

	protected virtual void ValidateXamlBuildReadiness()
	{
	}

	private void ValidateXamlBuildReadinessRecursive()
	{
		ValidateXamlBuildReadiness();
		foreach (var child in EnumerateDomChildren())
			child.ValidateXamlBuildReadinessRecursive();
	}

	/// <summary>
	/// Releases only the materialized XAML projection so the same strongly
	/// typed DOM tree can be projected again after a runtime layout-state
	/// change. DOM evidence and element identity remain intact.
	/// </summary>
	public void ResetXamlObjectProjection()
	{
		XamlElement = null;
		XamlObjectNode = null;
		_xamlOwnedObjects.Clear();
		foreach (var child in Children)
			child.ResetXamlObjectProjection();
	}

	protected internal virtual IReadOnlyList<XamlElementObjectPlan>
		BuildXamlObjectPlans()
	{
		if (XamlSupport == XamlConversionSupport.Unsupported)
		{
			throw new InvalidOperationException(
				$"Element {TagName} does not support XAML conversion.");
		}
		if (XamlSupport == XamlConversionSupport.NonVisual)
			return BuildDirectDomChildObjectPlans();
		var projection = ResolveXamlObjectProjection();
		var mapping = projection.Mapping;
		if (string.IsNullOrWhiteSpace(mapping.ElementName))
		{
			throw new InvalidOperationException(
				$"Element {TagName} has no XAML element name.");
		}
		var rowDefinitions = Array.Empty<XamlGridTrackDefinition>();
		var columnDefinitions = Array.Empty<XamlGridTrackDefinition>();
		if (TryBuildXamlWrappedFlexLayout(
			out var wrappedVertical,
			out var lineDefinitions,
			out _))
		{
			if (wrappedVertical)
				columnDefinitions = lineDefinitions.ToArray();
			else
				rowDefinitions = lineDefinitions.ToArray();
		}
		else if (TryBuildXamlGridLayout(
			out var gridRows,
			out var gridColumns))
		{
			rowDefinitions = gridRows.ToArray();
			columnDefinitions = gridColumns.ToArray();
		}
		else if (TryBuildXamlTableLayout(
			out var tableRows,
			out var tableColumns))
		{
			rowDefinitions = tableRows.ToArray();
			columnDefinitions = tableColumns.ToArray();
		}
		else if (TryBuildXamlLayoutTracks(
			out var vertical,
			out var definitions))
		{
			var ordered = IsReverseFlexDirection()
				? definitions.Reverse().ToArray()
				: definitions.ToArray();
			if (vertical)
				rowDefinitions = ordered;
			else
				columnDefinitions = ordered;
		}
		var shell = CreateXamlObjectShell(
			projection,
			rowDefinitions,
			columnDefinitions,
			BuildXamlObjectChildPlans());
		return [FillXamlProperties(shell)];
	}

	private XamlElementObjectPlan CreateXamlObjectShell(
		XamlElementObjectProjectionDecision projection,
		IReadOnlyList<XamlGridTrackDefinition> rowDefinitions,
		IReadOnlyList<XamlGridTrackDefinition> columnDefinitions,
		IReadOnlyList<XamlElementObjectPlan> children) =>
		new(
			this,
			projection.Mapping,
			[],
			projection.ChildPlacement,
			rowDefinitions,
			columnDefinitions,
			children,
			$"XAML object shell for {DocumentScope}::{XPath}.",
			this,
			projection.ContentProjection,
			BuildXamlLayoutPlacement());

	private XamlElementLayoutPlacement? BuildXamlLayoutPlacement()
	{
		if (Parent is null)
		{
			return null;
		}
		var position = ActiveDomValue(this, "style.position");
		var isOutOfFlow = position is "absolute" or "fixed";
		var left = isOutOfFlow
			? ParseComputedInset("style.left")
			: null;
		var top = isOutOfFlow
			? ParseComputedInset("style.top")
			: null;
		var right = isOutOfFlow
			? ParseComputedInset("style.right")
			: null;
		var bottom = isOutOfFlow
			? ParseComputedInset("style.bottom")
			: null;
		var hasDefiniteWidth = HasDefiniteInitialSize("style.width");
		var hasDefiniteHeight = HasDefiniteInitialSize("style.height");
		var isFlex = Parent.TryBuildXamlFlexLayout(out var vertical, out _);
		if (!isFlex)
			isFlex = Parent.TryBuildXamlWrappedFlexLayout(out vertical, out _, out _);
		if (!isFlex)
		{
			if (isOutOfFlow)
			{
				return new(
					true,
					XamlElementCrossAxis.Horizontal,
					XamlElementCrossAlignment.Near,
					false,
					position == "fixed"
						? XamlElementContainingBlockKind.Viewport
						: XamlElementContainingBlockKind.PaddingBox,
					left,
					top,
					right,
					bottom,
					HasDefiniteWidth: hasDefiniteWidth,
					HasDefiniteHeight: hasDefiniteHeight);
			}
			if (!Parent.TryBuildXamlBlockLayout(out _))
				return null;
			var width = FindRuntimeProperty("style.width");
			var initialWidth = width is null
				? null
				: SourceInitialization(width)?.Trim();
			var hasDefiniteBlockWidth = !string.IsNullOrWhiteSpace(initialWidth)
				&& !initialWidth.Equals("auto", StringComparison.OrdinalIgnoreCase)
				&& !initialWidth.Equals("none", StringComparison.OrdinalIgnoreCase);
			var leftMargin = ActiveDomValue(this, "style.marginLeft");
			var rightMargin = ActiveDomValue(this, "style.marginRight");
			var blockAlignment = leftMargin == "auto" && rightMargin == "auto"
				? XamlElementCrossAlignment.Center
				: leftMargin == "auto"
					? XamlElementCrossAlignment.Far
					: hasDefiniteBlockWidth
						? XamlElementCrossAlignment.Near
						: XamlElementCrossAlignment.Stretch;
			return new(
				false,
				XamlElementCrossAxis.Horizontal,
				blockAlignment,
				hasDefiniteBlockWidth,
				XamlElementContainingBlockKind.ContentBox);
		}
		var alignment = ActiveDomValue(this, "style.alignSelf");
		var mainAxisOffset = isOutOfFlow
			? null
			: Parent.ResolveXamlFlexMainAxisOffset(this, vertical);
		if (string.IsNullOrWhiteSpace(alignment)
			|| alignment.Equals("auto", StringComparison.OrdinalIgnoreCase))
		{
			alignment = ActiveDomValue(Parent, "style.alignItems") ?? "stretch";
		}
		var crossSizeName = vertical ? "style.width" : "style.height";
		var crossSize = FindRuntimeProperty(crossSizeName);
		var initialCrossSize = crossSize is null
			? null
			: SourceInitialization(crossSize)?.Trim();
		var hasDefiniteCrossSize = !string.IsNullOrWhiteSpace(initialCrossSize)
			&& !initialCrossSize.Equals("auto", StringComparison.OrdinalIgnoreCase)
			&& !initialCrossSize.Equals("none", StringComparison.OrdinalIgnoreCase);
		var resolved = alignment.Trim().ToLowerInvariant() switch
		{
			"center" => XamlElementCrossAlignment.Center,
			"end" or "flex-end" or "self-end" =>
				XamlElementCrossAlignment.Far,
			"baseline" or "first baseline" or "last baseline" =>
				XamlElementCrossAlignment.Baseline,
			"stretch" when !hasDefiniteCrossSize =>
				XamlElementCrossAlignment.Stretch,
			_ => XamlElementCrossAlignment.Near
		};
		return new(
			isOutOfFlow,
			vertical
				? XamlElementCrossAxis.Horizontal
				: XamlElementCrossAxis.Vertical,
			resolved,
			hasDefiniteCrossSize,
			position switch
			{
				"absolute" => XamlElementContainingBlockKind.PaddingBox,
				"fixed" => XamlElementContainingBlockKind.Viewport,
				_ => XamlElementContainingBlockKind.ContentBox
			},
			left,
			top,
			right,
			bottom,
			mainAxisOffset,
			hasDefiniteWidth,
			hasDefiniteHeight);
	}

	private bool HasDefiniteInitialSize(string propertyName)
	{
		var property = FindRuntimeProperty(propertyName);
		var value = property is null
			? null
			: SourceInitialization(property)?.Trim();
		return !string.IsNullOrWhiteSpace(value)
			&& !value.Equals("auto", StringComparison.OrdinalIgnoreCase)
			&& !value.Equals("none", StringComparison.OrdinalIgnoreCase)
			&& !value.Equals("fit-content", StringComparison.OrdinalIgnoreCase)
			&& !value.Equals("max-content", StringComparison.OrdinalIgnoreCase)
			&& !value.Equals("min-content", StringComparison.OrdinalIgnoreCase);
	}

	private LayoutLength? ParseComputedInset(string propertyName)
	{
		var value = ActiveDomValue(this, propertyName);
		if (string.IsNullOrWhiteSpace(value)
			|| value.Equals("auto", StringComparison.OrdinalIgnoreCase)
			|| value.Equals("none", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		var normalized = value.Trim();
		if (normalized.EndsWith("px", StringComparison.OrdinalIgnoreCase))
			normalized = normalized[..^2].Trim();
		if (double.TryParse(
			normalized,
			NumberStyles.Float,
			CultureInfo.InvariantCulture,
			out var pixels))
		{
			return new LayoutLength.Constant(
				pixels,
				LayoutLengthUnit.CssPixel);
		}
		throw new InvalidDataException(
			$"Computed CSS inset {propertyName}='{value}' on "
				+ $"{DocumentScope}::{XPath} has no strong length representation.");
	}

	private static string? ActiveDomValue(DomElement element, string name)
	{
		var property = element.FindRuntimeProperty(name);
		if (property?.SourceRuntime.IsSet == true)
			return property.SourceRuntime.Value?.Trim().ToLowerInvariant();
		return property?.SourceInitialization.IsSet == true
			? property.SourceInitialization.Value?.Trim().ToLowerInvariant()
			: null;
	}

	private XamlElementObjectPlan FillXamlProperties(
		XamlElementObjectPlan shell) =>
		shell with
		{
			InitializationAttributes = BuildResolvedXamlAttributes(),
			Description =
				$"Filled XAML object plan for {DocumentScope}::{XPath}."
		};

	protected virtual XamlElementObjectProjectionDecision
		ResolveXamlObjectProjection() =>
		new(
			CreateXaml(),
			XamlChildPlacement,
			ResolveXamlContentProjectionKind());

	public XamlElementObjectProjectionDecision GetXamlObjectProjection() =>
		ResolveXamlObjectProjection();

	protected virtual XamlElementContentProjectionKind
		ResolveXamlContentProjectionKind()
	{
		var generated = HasGeneratedXamlContent();
		var hasChildren = EnumerateDomChildren().Any(
			static child => child.HasXamlOutput());
		if (generated && hasChildren)
			return XamlElementContentProjectionKind.Composite;
		if (generated)
			return XamlElementContentProjectionKind.GeneratedContent;
		return XamlChildPlacement switch
		{
			ElementXamlChildPlacementKind.DirectChildren =>
				XamlElementContentProjectionKind.DirectChildren,
			ElementXamlChildPlacementKind.Content =>
				XamlElementContentProjectionKind.Content,
			ElementXamlChildPlacementKind.Inlines =>
				XamlElementContentProjectionKind.Inlines,
			ElementXamlChildPlacementKind.Items =>
				XamlElementContentProjectionKind.Items,
			_ => XamlElementContentProjectionKind.None
		};
	}

	protected virtual IReadOnlyList<XamlElementObjectPlan>
		BuildXamlObjectChildPlans()
	{
		if (HasGeneratedXamlContent())
		{
			throw new InvalidOperationException(
				$"Element {TagName} declares generated XAML content but "
				+ "does not provide an object construction plan.");
		}
		return BuildDirectDomChildObjectPlans();
	}

	protected IReadOnlyList<XamlElementObjectPlan>
		BuildDirectDomChildObjectPlans() =>
		EnumerateDomChildren()
			.Where(static child => child.HasXamlOutput())
			.SelectMany(static child => child.BuildXamlObjectPlans())
			.ToArray();

	protected XamlElementObjectPlan CreateSyntheticXamlObjectPlan(
		XamlElementObjectType objectType,
		IReadOnlyList<GeneratedXamlAttribute> initializationAttributes,
		ElementXamlChildPlacementKind childPlacement,
		IReadOnlyList<XamlElementObjectPlan>? children,
		string description)
	{
		foreach (var attribute in initializationAttributes)
		{
			attribute.Source?.ApplyXamlQueryResult(
				XamlPropertyDataSlot.Initialization,
				XamlPropertyQueryResult.DirectConstant(
					attribute.Value,
					$"ToXaml wrote synthetic '{objectType}.{attribute.Name}'."));
		}
		return new(
			null,
			new(
				objectType,
				XamlElementMappingKind.ConservativeContainer,
				RequiresRuntimeLayoutContract: false,
				description),
			initializationAttributes,
			childPlacement,
			[],
			[],
			children ?? [],
			description,
			this,
			children is { Count: > 0 }
				? XamlElementContentProjectionKind.Composite
				: XamlElementContentProjectionKind.GeneratedContent);
	}

	private static XamlElementObjectBuildNode BuildXamlObjectNode(
		IXamlElementObjectFactory factory,
		XamlElementObjectPlan plan)
	{
		var element = plan.SourceElement is { } sourceElement
			? sourceElement.CreateXamlObject(factory, plan)
			: factory.CreateSyntheticElement(new(plan));
		element = element
			?? throw new InvalidOperationException(
				$"The XAML object factory returned null for "
					+ $"{plan.Mapping.ElementName}.");
		if (plan.SourceElement is { } source)
		{
			if (source.XamlElement is not null)
			{
				throw new InvalidOperationException(
					$"Element {source.DocumentScope}::{source.XPath} "
					+ "already owns a XAML object.");
			}
			source.XamlElement = element;
		}
		plan.OwnerElement?.RegisterOwnedXamlObject(element);
		if (plan.SourceElement is { } propertySource)
		{
			propertySource.FillXamlObjectProperties(
				factory,
				element,
				plan);
		}
		else
		{
			factory.FillElementProperties(new(element, plan));
		}
		factory.ApplyGridTracks(
			element,
			plan.RowDefinitions,
			plan.ColumnDefinitions);
		var children = AttachXamlChildren(factory, plan, element);
		var node = new XamlElementObjectBuildNode(plan, element, children);
		if (plan.SourceElement is { } nodeSource)
			nodeSource.XamlObjectNode = node;
		return node;
	}

	private static IReadOnlyList<XamlElementObjectBuildNode>
		AttachXamlChildren(
			IXamlElementObjectFactory factory,
			XamlElementObjectPlan plan,
			object element)
	{
		var children = new List<XamlElementObjectBuildNode>(
			plan.Children.Count);
		foreach (var childPlan in plan.Children)
		{
			var child = BuildXamlObjectNode(factory, childPlan);
			factory.AttachChild(
				new(
					element,
					child.Element,
					plan,
					childPlan,
					plan.ChildPlacement));
			children.Add(child);
		}
		return children;
	}

	private void RegisterOwnedXamlObject(object element)
	{
		if (_xamlOwnedObjects.Contains(
			element,
			ReferenceEqualityComparer.Instance))
		{
			throw new InvalidOperationException(
				$"Element {DocumentScope}::{XPath} already owns this XAML object.");
		}
		_xamlOwnedObjects.Add(element);
	}

	protected virtual void ToXaml(
		System.Text.StringBuilder output,
		int depth,
		bool isDocumentRoot)
	{
		WriteOwnXaml(output, depth, isDocumentRoot);
	}

	protected virtual void WriteOwnXaml(
		System.Text.StringBuilder output,
		int depth,
		bool isDocumentRoot)
	{
		ArgumentNullException.ThrowIfNull(output);
		if (XamlSupport == XamlConversionSupport.Unsupported)
		{
			throw new InvalidOperationException(
				$"元素 {TagName} 明确不支持 XAML 转换。");
		}
		if (XamlSupport == XamlConversionSupport.NonVisual)
		{
			WriteChildrenXaml(output, depth, isDocumentRoot);
			return;
		}
		var mappingDecision = CreateXaml();
		var outputElementName = mappingDecision.ElementName;
		if (string.IsNullOrWhiteSpace(outputElementName))
			throw new InvalidOperationException($"元素 {TagName} 没有 XAML 元素名称。");
		output.Append(' ', depth * 2)
			.Append('<')
			.Append(outputElementName);
		if (isDocumentRoot)
		{
			output.Append(
				" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"");
		}

		foreach (var attribute in BuildResolvedXamlAttributes())
		{
			output.Append(' ')
				.Append(attribute.Name)
				.Append("=\"")
				.Append(EscapeXamlAttribute(attribute.Value))
				.Append('"');
		}

		if (!HasGeneratedXamlContent()
			&& !EnumerateDomChildren().Any(static child => child.HasXamlOutput()))
		{
			WriteChildrenXaml(output, depth + 1, isDocumentRoot: false);
			output.Append(" />").AppendLine();
			return;
		}

		output.Append('>').AppendLine();
		WriteChildrenXaml(output, depth + 1, isDocumentRoot: false);
		output.Append(' ', depth * 2)
			.Append("</")
			.Append(outputElementName)
			.Append('>')
			.AppendLine();
	}

	protected virtual bool HasGeneratedXamlContent() => false;

	public XamlElementMappingDecision GetXamlElementMapping() =>
		CreateXaml();

	protected abstract XamlElementMappingDecision CreateXaml();

	protected virtual IReadOnlyList<GeneratedXamlAttribute> BuildXamlAttributes()
	{
		var attributes = new Dictionary<string, GeneratedXamlAttribute>(
			StringComparer.Ordinal)
		{
			["AutomationProperties.AutomationId"] = new(
				"AutomationProperties.AutomationId",
				XPath,
				null),
			["Tag"] = new("Tag", $"{DocumentScope}::{XPath}", null)
		};
		AddRuntimeLength(attributes, "style.width", "Width");
		AddRuntimeLength(attributes, "style.height", "Height");
		AddRuntimeLength(attributes, "style.minWidth", "MinWidth", omitZero: true);
		AddRuntimeLength(attributes, "style.maxWidth", "MaxWidth");
		AddRuntimeLength(attributes, "style.minHeight", "MinHeight", omitZero: true);
		AddRuntimeLength(attributes, "style.maxHeight", "MaxHeight");
		AddRuntimeValue(attributes, "style.opacity", "Opacity", static value =>
			value == "1" ? null : value);
		AddRuntimeValue(
			attributes,
			"style.zIndex",
			"Canvas.ZIndex",
			static value => value.Equals(
				"auto",
				StringComparison.OrdinalIgnoreCase)
					? null
					: value);
		AddRuntimeValue(attributes, "style.pointerEvents", "IsHitTestVisible",
			static value => value.Equals("none", StringComparison.OrdinalIgnoreCase)
				? "False" : null);
		AddCollapsedVisibility(attributes);
		AddDataSourceAttributes(attributes);
		AddParentLayoutIndex(attributes);
		return attributes.Values
			.OrderBy(static attribute => attribute.Name, StringComparer.Ordinal)
			.ToArray();
	}

	internal IReadOnlyList<GeneratedXamlAttribute>
		BuildResolvedXamlAttributes()
	{
		var attributes = new Dictionary<string, GeneratedXamlAttribute>(
			StringComparer.Ordinal);
		foreach (var attribute in BuildXamlAttributes())
			attributes.TryAdd(attribute.Name, attribute);
		foreach (var traits in ElementPropertyTraitsReflector
			.GetAttributes(GetType())
			.Where(static traits => traits.IsXamlOutputProperty))
		{
			if (!traits.IsSlottedProperty
				|| string.IsNullOrWhiteSpace(traits.TargetProperty))
			{
				throw new InvalidOperationException(
					$"{traits.DeclaringType.FullName}.{traits.PropertyName} "
					+ "has an incomplete XAML output contract.");
			}
			var property = GetType().GetProperty(traits.PropertyName)
				?? throw new InvalidOperationException(
					$"{GetType().FullName}.{traits.PropertyName} is missing.");
			if (property.GetValue(this)
				is not IXamlAttributeValueProvider provider
				|| provider is not IXamlPropertySlotOwner slotOwner)
			{
				throw new InvalidOperationException(
					$"{GetType().FullName}.{traits.PropertyName} must provide "
					+ "XAML attribute candidates and own XAML slots.");
			}
			var resolvedValue = ResolveXamlPropertySlotValue(
				traits,
				provider.GetXamlAttributeValueCandidates());
			if (resolvedValue is not null)
			{
				var candidate = new GeneratedXamlAttribute(
						traits.TargetProperty,
						resolvedValue,
						slotOwner);
				if (!attributes.TryGetValue(
						traits.TargetProperty,
						out var existing)
					|| existing.Source is null)
				{
					attributes[traits.TargetProperty] = candidate;
				}
			}
		}
		foreach (var runtimeProperty in XamlRuntimeProperties)
		{
			if (!ShouldEmitXamlRuntimeProperty(runtimeProperty))
				continue;
			var execution = runtimeProperty.XamlExecution;
			if (isOutOfFlowPositionAttribute(execution.MarkupAttributeName))
				continue;
			var sourceValue = runtimeProperty.SourceInitialization.IsSet
				? runtimeProperty.SourceInitialization.Value
				: runtimeProperty.SourceRuntime.IsSet
					? runtimeProperty.SourceRuntime.Value
					: null;
			if (!execution.SupportsInitialization
				|| string.IsNullOrWhiteSpace(execution.MarkupAttributeName)
				|| sourceValue is null)
			{
				continue;
			}
			var resolvedValue = ResolveExecutionMarkupValue(
				execution,
				sourceValue);
			if (resolvedValue is not null)
			{
				var candidate = new GeneratedXamlAttribute(
						execution.MarkupAttributeName,
						resolvedValue,
						runtimeProperty);
				if (!attributes.TryGetValue(
						execution.MarkupAttributeName,
						out var existing)
					|| existing.Source is null)
				{
					attributes[execution.MarkupAttributeName] = candidate;
				}
			}
		}
		var resolved = attributes.Values
			.OrderBy(static attribute => XamlAttributeApplicationOrder(attribute.Name))
			.ThenBy(static attribute => attribute.Name, StringComparer.Ordinal)
			.ToArray();
		foreach (var attribute in resolved)
		{
			foreach (var source in attribute.Sources)
			{
				source.ApplyXamlQueryResult(
					XamlPropertyDataSlot.Initialization,
					XamlPropertyQueryResult.DirectConstant(
						attribute.Value,
						$"ToXaml wrote the '{attribute.Name}' initialization value."));
			}
		}
		return resolved;

		bool isOutOfFlowPositionAttribute(string name) =>
			ActiveDomValue(this, "style.position") is "absolute" or "fixed"
			&& name is
				"HtmlPosition.Left"
					or "HtmlPosition.Top"
					or "HtmlPosition.Right"
					or "HtmlPosition.Bottom";
	}

	private static int XamlAttributeApplicationOrder(string name) => name switch
	{
		"Text" or "Content" or "ItemsSource" or "Source" => 0,
		"Width" or "Height" or "MinWidth" or "MinHeight"
			or "MaxWidth" or "MaxHeight" => 10,
		"HtmlTextShadow.Value" or "HtmlBoxShadow.Value"
			or "HtmlFilter.Filter" or "HtmlBackdropFilter.Filter"
			or "HtmlTransform.Value" or "HtmlTransform.Origin" => 100,
		_ => 50
	};

	private void AddDataSourceAttributes(
		IDictionary<string, GeneratedXamlAttribute> attributes)
	{
		var elementName = CreateXaml().ElementName;
		foreach (var dataSource in DataSources)
		{
			var execution = dataSource.XamlExecution;
			if (!dataSource.SourceInitialization.IsSet
				|| string.IsNullOrWhiteSpace(execution.MarkupAttributeName)
				|| !CanSetDataTarget(
					elementName,
					dataSource.XamlTargetKind))
			{
				continue;
			}
			SetXamlAttribute(
				attributes,
				execution.MarkupAttributeName,
				dataSource.SourceInitialization.Value,
				dataSource);
		}
	}

	private static bool CanSetDataTarget(
		string elementName,
		XamlControlDataTargetKind targetKind) =>
		targetKind switch
		{
			XamlControlDataTargetKind.Content =>
				elementName is "Button" or "HyperlinkButton" or "ComboBoxItem"
					or "ListViewItem" or "ContentControl",
			XamlControlDataTargetKind.Text =>
				elementName is "TextBlock" or "TextBox",
			XamlControlDataTargetKind.Value =>
				elementName is "Slider" or "ProgressBar",
			XamlControlDataTargetKind.IsChecked =>
				elementName is "CheckBox" or "RadioButton" or "ToggleButton",
			XamlControlDataTargetKind.Source =>
				elementName is "Image" or "MediaPlayerElement",
			XamlControlDataTargetKind.NavigateUri =>
				elementName == "HyperlinkButton",
			XamlControlDataTargetKind.PlaceholderText =>
				elementName == "TextBox",
			XamlControlDataTargetKind.ItemsSource =>
				elementName is "ComboBox" or "ListView",
			XamlControlDataTargetKind.SelectedItem
				or XamlControlDataTargetKind.SelectedValue =>
				elementName is "ComboBox" or "ListView",
			XamlControlDataTargetKind.CommandParameter =>
				elementName is "Button" or "HyperlinkButton",
			_ => false
		};

	protected virtual IReadOnlyList<DomElementRuntimeProperty> XamlRuntimeProperties => [];

	protected virtual bool ShouldEmitXamlRuntimeProperty(
		DomElementRuntimeProperty property) => true;

	protected static string? SourceInitialization(
		ElementSlottedProperty<string> property) =>
		property.SourceInitialization.IsSet
			? NullIfCssUnset(property.SourceInitialization.Value)
			: null;

	protected static string? NormalizeXamlLength(string? value)
	{
		value = NullIfCssUnset(value);
		if (value is null || value.EndsWith('%'))
			return null;
		var candidate = value.EndsWith("px", StringComparison.OrdinalIgnoreCase)
			? value[..^2]
			: value;
		return double.TryParse(
			candidate,
			NumberStyles.Float,
			CultureInfo.InvariantCulture,
			out var number)
			&& double.IsFinite(number)
				? number.ToString("R", CultureInfo.InvariantCulture)
				: null;
	}

	protected static string? NormalizeXamlColor(string? value)
	{
		value = NullIfCssUnset(value);
		if (value is null
			|| value.Equals("transparent", StringComparison.OrdinalIgnoreCase)
			|| value.Equals("currentColor", StringComparison.OrdinalIgnoreCase)
			|| value.Equals("none", StringComparison.OrdinalIgnoreCase)
			|| value.Equals("inherit", StringComparison.OrdinalIgnoreCase)
			|| value.Equals("initial", StringComparison.OrdinalIgnoreCase)
			|| value.Equals("unset", StringComparison.OrdinalIgnoreCase)
			|| value.Equals("revert", StringComparison.OrdinalIgnoreCase)
			|| value.Equals("revert-layer", StringComparison.OrdinalIgnoreCase))
			return null;
		if (value.StartsWith('#'))
		{
			var hex = value[1..];
			return hex.Length switch
			{
				4 => $"#{hex[3]}{hex[3]}{hex[0]}{hex[0]}"
					+ $"{hex[1]}{hex[1]}{hex[2]}{hex[2]}",
				8 => $"#{hex[6..8]}{hex[..6]}",
				_ => value
			};
		}
		var match = Regex.Match(
			value,
			@"^rgba?\(\s*(\d+)(?:\s*,\s*|\s+)(\d+)(?:\s*,\s*|\s+)(\d+)"
			+ @"(?:\s*(?:,|/)\s*([0-9.]+)%?)?\s*\)$",
			RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
		if (!match.Success)
			return value;
		var alpha = match.Groups[4].Success
			? double.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture)
			: 1;
		if (value.Contains('%', StringComparison.Ordinal))
			alpha /= 100;
		return $"#{(byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255):X2}"
			+ $"{byte.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture):X2}"
			+ $"{byte.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture):X2}"
			+ $"{byte.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture):X2}";
	}

	private static string? ResolveExecutionMarkupValue(
		XamlPropertyExecutionDescriptor execution,
		string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
			return null;
		if (execution.Kind == XamlPropertyExecutionKind.FrameworkDimension)
		{
			var normalized = NormalizeXamlLength(value);
			if (normalized == "0"
				&& execution.MarkupAttributeName is "MinWidth" or "MinHeight")
			{
				return null;
			}
			return normalized;
		}
		if (execution.Kind == XamlPropertyExecutionKind.FrameworkSpacing
			&& execution.MarkupAttributeName.StartsWith(
				"HtmlPosition.",
				StringComparison.Ordinal))
		{
			return NormalizeXamlLength(value);
		}
		return value.Trim();
	}

	protected static void SetXamlAttribute(
		IDictionary<string, GeneratedXamlAttribute> attributes,
		string targetName,
		string? value,
		IXamlPropertySlotOwner? source)
	{
		if (string.IsNullOrWhiteSpace(value))
			return;
		attributes[targetName] = new(targetName, value, source);
		source?.ApplyXamlQueryResult(
			XamlPropertyDataSlot.Initialization,
			XamlPropertyQueryResult.DirectConstant(
				value,
				$"ToXaml wrote the '{targetName}' initialization value."));
	}

	protected static void SetCompositeXamlAttribute(
		IDictionary<string, GeneratedXamlAttribute> attributes,
		string targetName,
		string? value,
		IReadOnlyList<IXamlPropertySlotOwner> sources,
		XamlCompositeValueKind compositeValueKind)
	{
		ArgumentNullException.ThrowIfNull(sources);
		if (compositeValueKind == XamlCompositeValueKind.None)
		{
			throw new ArgumentOutOfRangeException(
				nameof(compositeValueKind),
				"Composite XAML attributes require an explicit value kind.");
		}
		if (string.IsNullOrWhiteSpace(value) || sources.Count == 0)
			return;
		attributes[targetName] = new(
			targetName,
			value,
			sources[0],
			sources,
			compositeValueKind);
		foreach (var source in sources)
		{
			source.ApplyXamlQueryResult(
				XamlPropertyDataSlot.Initialization,
				XamlPropertyQueryResult.DirectConstant(
					value,
					$"ToXaml wrote the composite '{targetName}' initialization value."));
		}
	}

	private void AddRuntimeLength(
		IDictionary<string, GeneratedXamlAttribute> attributes,
		string sourceName,
		string targetName,
		bool omitZero = false)
	{
		var property = FindRuntimeProperty(sourceName);
		var value = property is null
			? null
			: NormalizeXamlLength(SourceInitialization(property));
		if (omitZero && value == "0")
			value = null;
		SetXamlAttribute(attributes, targetName, value, property);
	}

	private void AddRuntimeValue(
		IDictionary<string, GeneratedXamlAttribute> attributes,
		string sourceName,
		string targetName,
		Func<string, string?>? convert = null)
	{
		var property = FindRuntimeProperty(sourceName);
		var value = property is null ? null : SourceInitialization(property);
		var translated = value is null
			? null
			: convert is null ? value : convert(value);
		SetXamlAttribute(
			attributes,
			targetName,
			translated,
			property);
	}

	private void AddCollapsedVisibility(
		IDictionary<string, GeneratedXamlAttribute> attributes)
	{
		var property = FindRuntimeProperty("style.display");
		var value = RawActiveValue(property);
		if (value?.Equals("none", StringComparison.OrdinalIgnoreCase) != true)
		{
			property = FindRuntimeProperty("style.visibility");
			value = RawActiveValue(property);
		}
		if (value is not null
			&& (value.Equals("none", StringComparison.OrdinalIgnoreCase)
				|| value.Equals("hidden", StringComparison.OrdinalIgnoreCase)
				|| value.Equals("collapse", StringComparison.OrdinalIgnoreCase)))
		{
			SetXamlAttribute(attributes, "Visibility", "Collapsed", property);
		}
	}

	private static string? RawActiveValue(DomElementRuntimeProperty? property) =>
		property?.SourceRuntime.IsSet == true
			? property.SourceRuntime.Value?.Trim()
			: property?.SourceInitialization.IsSet == true
				? property.SourceInitialization.Value?.Trim()
				: null;

	private void AddParentLayoutIndex(
		IDictionary<string, GeneratedXamlAttribute> attributes)
	{
		if (Parent is null)
			return;
		if (Parent.TryResolveXamlGridPlacement(
			this,
			out var row,
			out var column,
			out var rowSpan,
			out var columnSpan))
		{
			var rowSource = FindRuntimeProperty("style.gridRow")
				?? FindRuntimeProperty("style.gridArea");
			var columnSource = FindRuntimeProperty("style.gridColumn")
				?? FindRuntimeProperty("style.gridArea");
			SetXamlAttribute(attributes, "Grid.Row", row.ToString(CultureInfo.InvariantCulture), rowSource);
			SetXamlAttribute(attributes, "Grid.Column", column.ToString(CultureInfo.InvariantCulture), columnSource);
			if (rowSpan > 1)
				SetXamlAttribute(attributes, "Grid.RowSpan", rowSpan.ToString(CultureInfo.InvariantCulture), rowSource);
			if (columnSpan > 1)
				SetXamlAttribute(attributes, "Grid.ColumnSpan", columnSpan.ToString(CultureInfo.InvariantCulture), columnSource);
			return;
		}
		if (Parent.CreateXaml().Kind
			== XamlElementMappingKind.PositionedLayout)
		{
			AddParentCanvasOffset(attributes, "rect.x", "Canvas.Left");
			AddParentCanvasOffset(attributes, "rect.y", "Canvas.Top");
			return;
		}
		if (!Parent.TryResolveXamlLayoutTrackIndex(
			this,
			out var vertical,
			out var targetIndex))
			return;
		var source = FindRuntimeProperty("style.order");
		SetXamlAttribute(
			attributes,
			vertical ? "Grid.Row" : "Grid.Column",
			targetIndex.ToString(CultureInfo.InvariantCulture),
			source);
	}

	private void AddParentCanvasOffset(
		IDictionary<string, GeneratedXamlAttribute> attributes,
		string sourceName,
		string targetName)
	{
		var current = FindRuntimeProperty(sourceName);
		var parent = Parent?.FindRuntimeProperty(sourceName);
		if (current is null
			|| parent is null
			|| !double.TryParse(
				SourceInitialization(current),
				NumberStyles.Float,
				CultureInfo.InvariantCulture,
				out var currentValue)
			|| !double.TryParse(
				SourceInitialization(parent),
				NumberStyles.Float,
				CultureInfo.InvariantCulture,
				out var parentValue))
		{
			return;
		}
		SetXamlAttribute(
			attributes,
			targetName,
			(currentValue - parentValue).ToString("R", CultureInfo.InvariantCulture),
			current);
	}

	private DomElementRuntimeProperty? FindRuntimeProperty(string name) =>
		RuntimeGeometry
			.Cast<DomElementRuntimeProperty>()
			.Concat(XamlRuntimeProperties)
			.FirstOrDefault(property => property.Name == name)
		?? HtmlRoot?.ResolveGlobalStyleProperty(this, name);

	private static string? NullIfCssUnset(string? value) =>
		string.IsNullOrWhiteSpace(value)
			|| value is "auto" or "none" or "normal" or "initial" or "inherit"
				? null
				: value.Trim();

	protected virtual bool TryBuildXamlFlexLayout(
		out bool vertical,
		out IReadOnlyList<XamlGridTrackDefinition> definitions)
	{
		vertical = false;
		definitions = [];
		return false;
	}

	protected virtual LayoutLength? ResolveXamlFlexMainAxisOffset(
		DomElement child,
		bool vertical) => null;

	protected virtual bool TryBuildXamlBlockLayout(
		out IReadOnlyList<XamlGridTrackDefinition> definitions)
	{
		definitions = [];
		return false;
	}

	protected virtual bool TryBuildXamlGridLayout(
		out IReadOnlyList<XamlGridTrackDefinition> rowDefinitions,
		out IReadOnlyList<XamlGridTrackDefinition> columnDefinitions)
	{
		rowDefinitions = [];
		columnDefinitions = [];
		return false;
	}

	protected virtual bool TryBuildXamlWrappedFlexLayout(
		out bool vertical,
		out IReadOnlyList<XamlGridTrackDefinition> lineDefinitions,
		out IReadOnlyList<IReadOnlyList<DomElement>> lines)
	{
		vertical = false;
		lineDefinitions = [];
		lines = [];
		return false;
	}

	protected virtual bool TryBuildXamlTableLayout(
		out IReadOnlyList<XamlGridTrackDefinition> rowDefinitions,
		out IReadOnlyList<XamlGridTrackDefinition> columnDefinitions)
	{
		rowDefinitions = [];
		columnDefinitions = [];
		return false;
	}

	protected virtual bool TryResolveXamlGridPlacement(
		DomElement child,
		out int row,
		out int column,
		out int rowSpan,
		out int columnSpan)
	{
		row = column = 0;
		rowSpan = columnSpan = 1;
		return false;
	}

	private bool TryBuildXamlLayoutTracks(
		out bool vertical,
		out IReadOnlyList<XamlGridTrackDefinition> definitions)
	{
		if (TryBuildXamlFlexLayout(out vertical, out definitions))
			return true;
		if (TryBuildXamlGridLayout(out var rows, out var columns))
		{
			if (columns.Count > 0)
			{
				vertical = false;
				definitions = columns;
			}
			else
			{
				vertical = true;
				definitions = rows;
			}
			return definitions.Count > 0;
		}
		vertical = true;
		return TryBuildXamlBlockLayout(out definitions);
	}

	protected virtual bool TryResolveXamlLayoutTrackIndex(
		DomElement child,
		out bool vertical,
		out int index)
	{
		if (!TryBuildXamlLayoutTracks(out vertical, out var definitions))
		{
			index = -1;
			return false;
		}
		var sourceIndex = Array.IndexOf(Children.ToArray(), child);
		if (sourceIndex < 0 || sourceIndex >= definitions.Count)
		{
			index = -1;
			return false;
		}
		index = IsReverseFlexDirection()
			? definitions.Count - sourceIndex - 1
			: sourceIndex;
		return true;
	}

	protected bool TryGetParentXamlFlexLayout(
		out bool vertical,
		out IReadOnlyList<XamlGridTrackDefinition> definitions)
	{
		if (Parent is not null)
			return Parent.TryBuildXamlFlexLayout(out vertical, out definitions);
		vertical = false;
		definitions = [];
		return false;
	}

	protected virtual void WriteChildrenXaml(
		System.Text.StringBuilder output,
		int depth,
		bool isDocumentRoot)
	{
		ArgumentNullException.ThrowIfNull(output);
		if (TryBuildXamlTableLayout(
			out var tableRows,
			out var tableColumns))
		{
			WriteTrackDefinitions(output, depth, true, tableRows);
			WriteTrackDefinitions(output, depth, false, tableColumns);
		}
		else if (TryBuildXamlLayoutTracks(
			out var vertical,
			out var definitions))
		{
			var resolvedDefinitions = IsReverseFlexDirection()
				? definitions.Reverse()
				: definitions;
			WriteTrackDefinitions(
				output,
				depth,
				vertical,
				resolvedDefinitions);
		}
		var rootPending = isDocumentRoot;
		foreach (var child in EnumerateDomChildren())
		{
			if (!child.HasXamlOutput())
				continue;
			child.ToXaml(output, depth, rootPending);
			rootPending = false;
		}
	}

	private static void WriteTrackDefinitions(
		System.Text.StringBuilder output,
		int depth,
		bool vertical,
		IEnumerable<XamlGridTrackDefinition> definitions)
	{
		var resolved = definitions.ToArray();
		if (resolved.Length == 0)
			return;
		var axisName = vertical ? "Row" : "Column";
		var dimensionName = vertical ? "Height" : "Width";
		output.Append(' ', depth * 2)
			.Append("<Grid.")
			.Append(axisName)
			.Append("Definitions>")
			.AppendLine();
		foreach (var definition in resolved)
		{
			output.Append(' ', (depth + 1) * 2)
				.Append('<')
				.Append(axisName)
				.Append("Definition ")
				.Append(dimensionName)
				.Append("=\"")
				.Append(EscapeXamlAttribute(definition.Length))
				.Append('"');
			if (definition.Minimum is not null && definition.Minimum != "0")
			{
				output.Append(" Min")
					.Append(dimensionName)
					.Append("=\"")
					.Append(EscapeXamlAttribute(definition.Minimum))
					.Append('"');
			}
			if (definition.Maximum is not null)
			{
				output.Append(" Max")
					.Append(dimensionName)
					.Append("=\"")
					.Append(EscapeXamlAttribute(definition.Maximum))
					.Append('"');
			}
			output.Append(" />").AppendLine();
		}
		output.Append(' ', depth * 2)
			.Append("</Grid.")
			.Append(axisName)
			.Append("Definitions>")
			.AppendLine();
	}

	private bool IsReverseFlexDirection()
	{
		var global = HtmlRoot?.ResolveGlobalStyleValue(
				this,
				"style.flexDirection",
				DomPropertyDataSlot.Runtime)
			?? HtmlRoot?.ResolveGlobalStyleValue(
				this,
				"style.flexDirection",
				DomPropertyDataSlot.Initialization);
		if (!string.IsNullOrWhiteSpace(global))
		{
			return global.EndsWith(
				"-reverse",
				StringComparison.OrdinalIgnoreCase);
		}
		var runtime = this switch
		{
			HtmlDomElementDefinition html => html.RuntimeProperties,
			SvgDomElementDefinition svg => svg.RuntimeProperties,
			_ => []
		};
		return runtime.FirstOrDefault(
			static property => property.Name == "style.flexDirection")
			is { } direction
			? (direction.SourceRuntime.IsSet
				? direction.SourceRuntime.Value
				: direction.SourceInitialization.Value)
			?.EndsWith("-reverse", StringComparison.OrdinalIgnoreCase) == true
			: false;
	}

	protected static string EscapeXamlAttribute(string value) =>
		System.Security.SecurityElement.Escape(value) ?? string.Empty;

	protected virtual string? ResolveXamlPropertySlotValue(
		ElementPropertyTraits traits,
		IReadOnlyList<XamlAttributeValueCandidate> candidates)
	{
		ArgumentNullException.ThrowIfNull(candidates);
		if (!traits.IsSlottedProperty)
		{
			throw new ArgumentException(
				$"{traits.DeclaringType.FullName}.{traits.PropertyName} "
				+ "不是槽位属性。",
				nameof(traits));
		}
		var available = candidates
			.Where(static candidate => candidate.Value is not null)
			.Where(candidate =>
				!traits.IsRuntimeAbsolute()
				|| candidate.Priority != XamlValueResolutionPriority.AbsoluteConstant)
			.OrderByDescending(static candidate => candidate.Priority);
		return available
			.Select(static candidate => candidate.Value)
			.FirstOrDefault();
	}

	protected virtual bool HasXamlOutput() =>
		XamlSupport != XamlConversionSupport.NonVisual
		|| EnumerateDomChildren().Any(static child => child.HasXamlOutput());

	public async ValueTask<DomElementFillResult> DomFillAsync(
		DomPropertyQueryDelegate query,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);
		ValidateOwnTraversalContract();
		var slots = new List<DomPropertyFillTrace>();
		foreach (var traits in ElementPropertyTraitsReflector
			.GetAttributes(GetType())
			.Where(static traits => traits.IsFillRequired))
		{
			cancellationToken.ThrowIfCancellationRequested();
			var property = GetType().GetProperty(traits.PropertyName)
				?? throw new InvalidOperationException(
					$"{GetType().FullName}.{traits.PropertyName} 不存在。");
			var value = property.GetValue(this);
			slots.AddRange(await FillPropertyValueAsync(
				value,
				query,
				cancellationToken));
		}
		_hasCompletedDomFill = true;

		var children = EnumerateDomChildren().ToArray();
		var childResults = new List<DomElementFillResult>(children.Length);
		foreach (var child in children)
		{
			cancellationToken.ThrowIfCancellationRequested();
			childResults.Add(await child.DomFillAsync(query, cancellationToken));
		}
		return new(
			$"{DocumentScope}::{XPath}",
			slots,
			childResults);
	}

	public async ValueTask<DomElementFillResult> DomFillAsync(
		IDomPropertyQueryEngine queryEngine,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(queryEngine);
		ValidateOwnTraversalContract();
		var slots = new List<DomPropertyFillTrace>();
		foreach (var traits in ElementPropertyTraitsReflector
			.GetAttributes(GetType())
			.Where(static traits => traits.IsFillRequired))
		{
			cancellationToken.ThrowIfCancellationRequested();
			var property = GetType().GetProperty(traits.PropertyName)
				?? throw new InvalidOperationException(
					$"{GetType().FullName}.{traits.PropertyName} 不存在。");
			slots.AddRange(await FillDomPropertyValueAsync(
				property.GetValue(this),
				traits.PropertyName,
				queryEngine,
				cancellationToken));
		}
		_hasCompletedDomFill = true;

		var children = EnumerateDomChildren().ToArray();
		var childResults = new List<DomElementFillResult>(children.Length);
		foreach (var child in children)
		{
			cancellationToken.ThrowIfCancellationRequested();
			childResults.Add(await child.DomFillAsync(queryEngine, cancellationToken));
		}
		return new(
			$"{DocumentScope}::{XPath}",
			slots,
			childResults);
	}

	public async ValueTask<DomElementFillResult> RefreshDomRuntimeAsync(
		IDomPropertyQueryEngine queryEngine,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(queryEngine);
		if (queryEngine is HtmlDocumentRoot htmlRoot
			&& ReferenceEquals(HtmlRoot, htmlRoot)
			&& htmlRoot.EvidenceSnapshot is { } snapshot)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var preparedSlots = ApplyPreparedDomEvidence(
				htmlRoot,
				snapshot.ReadElement(this));
			var preparedChildren = new List<DomElementFillResult>(
				Children.Count);
			foreach (var child in EnumerateDomChildren())
			{
				cancellationToken.ThrowIfCancellationRequested();
				preparedChildren.Add(await child.RefreshDomRuntimeAsync(
					htmlRoot,
					cancellationToken));
			}
			return new(
				$"{DocumentScope}::{XPath}",
				preparedSlots,
				preparedChildren);
		}
		ValidateOwnTraversalContract();
		var slots = new List<DomPropertyFillTrace>();
		foreach (var traits in ElementPropertyTraitsReflector
			.GetAttributes(GetType())
			.Where(static traits => traits.IsFillRequired))
		{
			cancellationToken.ThrowIfCancellationRequested();
			var property = GetType().GetProperty(traits.PropertyName)
				?? throw new InvalidOperationException(
					$"{GetType().FullName}.{traits.PropertyName} 不存在。");
			slots.AddRange(await FillDomPropertyValueAsync(
				property.GetValue(this),
				traits.PropertyName,
				queryEngine,
				cancellationToken,
				DomPropertyDataSlot.Link));
			slots.AddRange(await FillDomPropertyValueAsync(
				property.GetValue(this),
				traits.PropertyName,
				queryEngine,
				cancellationToken,
				DomPropertyDataSlot.Runtime));
		}
		var children = EnumerateDomChildren().ToArray();
		var childResults = new List<DomElementFillResult>(children.Length);
		foreach (var child in children)
		{
			cancellationToken.ThrowIfCancellationRequested();
			childResults.Add(await child.RefreshDomRuntimeAsync(
				queryEngine,
				cancellationToken));
		}
		return new(
			$"{DocumentScope}::{XPath}",
			slots,
			childResults);
	}

	public async ValueTask<XamlElementFillResult> XamlFillAsync(
		XamlPropertyQueryDelegate query,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);
		ValidateOwnTraversalContract();
		var queriedSlots = new List<XamlPropertyFillTrace>();
		var ownFillTraits = XamlSupport == XamlConversionSupport.NonVisual
			? []
			: ElementPropertyTraitsReflector
				.GetAttributes(GetType())
				.Where(static traits => traits.IsXamlFillRequired)
				.ToArray();
		foreach (var traits in ownFillTraits)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var property = GetType().GetProperty(traits.PropertyName)
				?? throw new InvalidOperationException(
					$"{GetType().FullName}.{traits.PropertyName} 不存在。");
			var value = property.GetValue(this);
			queriedSlots.AddRange(await FillXamlPropertyValueAsync(
				value,
				query,
				cancellationToken));
		}

		var children = EnumerateDomChildren().ToArray();
		var childResults = new List<XamlElementFillResult>(children.Length);
		foreach (var child in children)
		{
			cancellationToken.ThrowIfCancellationRequested();
			childResults.Add(await child.XamlFillAsync(query, cancellationToken));
		}
		return new(
			$"{DocumentScope}::{XPath}",
			queriedSlots,
			childResults);
	}

	public async ValueTask<XamlElementFillResult> XamlFillAsync(
		IXamlPropertyQueryEngine queryEngine,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(queryEngine);
		ValidateOwnTraversalContract();
		var queriedSlots = new List<XamlPropertyFillTrace>();
		var ownFillTraits = XamlSupport == XamlConversionSupport.NonVisual
			? []
			: ElementPropertyTraitsReflector
				.GetAttributes(GetType())
				.Where(static traits => traits.IsXamlFillRequired)
				.ToArray();
		foreach (var traits in ownFillTraits)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var property = GetType().GetProperty(traits.PropertyName)
				?? throw new InvalidOperationException(
					$"{GetType().FullName}.{traits.PropertyName} 不存在。");
			queriedSlots.AddRange(await FillXamlPropertyValueAsync(
				property.GetValue(this),
				traits.PropertyName,
				queryEngine,
				cancellationToken));
		}

		var children = EnumerateDomChildren().ToArray();
		var childResults = new List<XamlElementFillResult>(children.Length);
		foreach (var child in children)
		{
			cancellationToken.ThrowIfCancellationRequested();
			childResults.Add(await child.XamlFillAsync(
				queryEngine,
				cancellationToken));
		}
		return new(
			$"{DocumentScope}::{XPath}",
			queriedSlots,
			childResults);
	}

	private async ValueTask<IReadOnlyList<DomPropertyFillTrace>> FillPropertyValueAsync(
		object? value,
		DomPropertyQueryDelegate query,
		CancellationToken cancellationToken)
	{
		if (value is IDomPropertySlotOwner propertySlots)
			return await FillSlotOwnerAsync(propertySlots, query, cancellationToken);
		if (value is IDomEventSlotOwner eventSlots)
			return await FillSlotOwnerAsync(eventSlots, query, cancellationToken);
		if (value is IEnumerable<IDomPropertySlotOwner> propertyCollection)
		{
			var traces = new List<DomPropertyFillTrace>();
			foreach (var item in propertyCollection)
				traces.AddRange(await FillSlotOwnerAsync(item, query, cancellationToken));
			return traces;
		}
		if (value is IEnumerable<IDomEventSlotOwner> eventCollection)
		{
			var traces = new List<DomPropertyFillTrace>();
			foreach (var item in eventCollection)
				traces.AddRange(await FillSlotOwnerAsync(item, query, cancellationToken));
			return traces;
		}
		throw new InvalidOperationException(
			$"标记 IsFillRequired 的属性必须实现 {nameof(IDomFillSlotOwner)}。");
	}

	private async ValueTask<IReadOnlyList<DomPropertyFillTrace>> FillDomPropertyValueAsync(
		object? value,
		string reflectedPropertyName,
		IDomPropertyQueryEngine queryEngine,
		CancellationToken cancellationToken,
		DomPropertyDataSlot? onlySlot = null)
	{
		if (value is IDomPropertySlotOwner propertySlots)
			return await FillDomSlotOwnerAsync(
				propertySlots,
				reflectedPropertyName,
				queryEngine,
				cancellationToken,
				onlySlot);
		if (value is IDomEventSlotOwner eventSlots)
			return await FillDomSlotOwnerAsync(
				eventSlots,
				reflectedPropertyName,
				queryEngine,
				cancellationToken,
				onlySlot);
		if (value is IEnumerable<IDomPropertySlotOwner> propertyCollection)
		{
			var traces = new List<DomPropertyFillTrace>();
			foreach (var item in propertyCollection)
			{
				traces.AddRange(await FillDomSlotOwnerAsync(
					item,
					reflectedPropertyName,
					queryEngine,
					cancellationToken,
					onlySlot));
			}
			return traces;
		}
		if (value is IEnumerable<IDomEventSlotOwner> eventCollection)
		{
			var traces = new List<DomPropertyFillTrace>();
			foreach (var item in eventCollection)
			{
				traces.AddRange(await FillDomSlotOwnerAsync(
					item,
					reflectedPropertyName,
					queryEngine,
					cancellationToken,
					onlySlot));
			}
			return traces;
		}
		throw new InvalidOperationException(
			$"标记 IsFillRequired 的属性必须实现 {nameof(IDomFillSlotOwner)}。");
	}

	private async ValueTask<IReadOnlyList<DomPropertyFillTrace>> FillDomSlotOwnerAsync(
		IDomFillSlotOwner owner,
		string reflectedPropertyName,
		IDomPropertyQueryEngine queryEngine,
		CancellationToken cancellationToken,
		DomPropertyDataSlot? onlySlot = null)
	{
		var traces = new List<DomPropertyFillTrace>(owner.DomSlots.Count);
		var category = ResolveSlotCategory(owner);
		var ownerKind = ResolveSlotOwnerKind(owner, reflectedPropertyName);
		var evidenceKind = ResolveEvidenceKind(ownerKind, category, owner);
		foreach (var slot in owner.DomSlots.Where(
			slot => onlySlot is null || slot == onlySlot))
		{
			cancellationToken.ThrowIfCancellationRequested();
			var context = new DomPropertyQueryContext(
				DocumentScope,
				XPath,
				TagName,
				reflectedPropertyName,
				owner.Name,
				ownerKind,
				category,
				evidenceKind,
				slot,
				this);
			context.Traversal.Validate();
			var result = await queryEngine.QueryAsync(
				context,
				cancellationToken);
			var application = owner.ApplyDomQueryResult(slot, result);
			traces.Add(new(
				owner.Name,
				slot,
				result.Status,
				result.Value,
				result.ValueSource,
				result.Link,
				result.Description,
				application,
				reflectedPropertyName,
				ownerKind,
				category,
				evidenceKind));
		}
		if (owner is IElementSlotConsistencyOwner consistencyOwner)
			consistencyOwner.ValidateSlotConsistency();
		return traces;
	}

	private async ValueTask<IReadOnlyList<DomPropertyFillTrace>> FillSlotOwnerAsync(
		IDomFillSlotOwner owner,
		DomPropertyQueryDelegate query,
		CancellationToken cancellationToken)
	{
		var traces = new List<DomPropertyFillTrace>(owner.DomSlots.Count);
		foreach (var slot in owner.DomSlots)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var result = await query(DocumentScope, XPath, owner.Name, slot);
			var application = owner.ApplyDomQueryResult(slot, result);
			traces.Add(new(
				owner.Name,
				slot,
				result.Status,
				result.Value,
				result.ValueSource,
				result.Link,
				result.Description,
				application));
		}
		if (owner is IElementSlotConsistencyOwner consistencyOwner)
			consistencyOwner.ValidateSlotConsistency();
		return traces;
	}

	private async ValueTask<IReadOnlyList<XamlPropertyFillTrace>> FillXamlPropertyValueAsync(
		object? value,
		XamlPropertyQueryDelegate query,
		CancellationToken cancellationToken)
	{
		if (value is IXamlPropertySlotOwner propertySlots)
			return await FillXamlSlotOwnerAsync(propertySlots, query, cancellationToken);
		if (value is IXamlEventSlotOwner eventSlots)
			return await FillXamlSlotOwnerAsync(eventSlots, query, cancellationToken);
		if (value is IEnumerable<IXamlPropertySlotOwner> propertyCollection)
		{
			var traces = new List<XamlPropertyFillTrace>();
			foreach (var item in propertyCollection)
				traces.AddRange(await FillXamlSlotOwnerAsync(item, query, cancellationToken));
			return traces;
		}
		if (value is IEnumerable<IXamlEventSlotOwner> eventCollection)
		{
			var traces = new List<XamlPropertyFillTrace>();
			foreach (var item in eventCollection)
				traces.AddRange(await FillXamlSlotOwnerAsync(item, query, cancellationToken));
			return traces;
		}
		throw new InvalidOperationException(
			$"标记 IsSlottedProperty 的属性必须实现 {nameof(IXamlFillSlotOwner)}。");
	}

	private async ValueTask<IReadOnlyList<XamlPropertyFillTrace>> FillXamlSlotOwnerAsync(
		IXamlFillSlotOwner owner,
		XamlPropertyQueryDelegate query,
		CancellationToken cancellationToken)
	{
		var traces = new List<XamlPropertyFillTrace>();
		foreach (var slot in owner.XamlSlots)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var result = await query(DocumentScope, XPath, owner.Name, slot);
			owner.ApplyXamlQueryResult(slot, result);
			traces.Add(new(
				owner.Name,
				slot,
				result.Status,
				result.Description,
				QueriedValue: result.Value,
				ValueSource: result.ValueSource,
				Link: result.Link));
		}
		if (owner is IElementSlotConsistencyOwner consistencyOwner)
			consistencyOwner.ValidateSlotConsistency();
		return traces;
	}

	private async ValueTask<IReadOnlyList<XamlPropertyFillTrace>> FillXamlPropertyValueAsync(
		object? value,
		string reflectedPropertyName,
		IXamlPropertyQueryEngine queryEngine,
		CancellationToken cancellationToken)
	{
		if (value is IXamlPropertySlotOwner propertySlots)
			return await FillXamlSlotOwnerAsync(
				propertySlots,
				reflectedPropertyName,
				queryEngine,
				cancellationToken);
		if (value is IXamlEventSlotOwner eventSlots)
			return await FillXamlSlotOwnerAsync(
				eventSlots,
				reflectedPropertyName,
				queryEngine,
				cancellationToken);
		if (value is IEnumerable<IXamlPropertySlotOwner> propertyCollection)
		{
			var traces = new List<XamlPropertyFillTrace>();
			foreach (var item in propertyCollection)
			{
				traces.AddRange(await FillXamlSlotOwnerAsync(
					item,
					reflectedPropertyName,
					queryEngine,
					cancellationToken));
			}
			return traces;
		}
		if (value is IEnumerable<IXamlEventSlotOwner> eventCollection)
		{
			var traces = new List<XamlPropertyFillTrace>();
			foreach (var item in eventCollection)
			{
				traces.AddRange(await FillXamlSlotOwnerAsync(
					item,
					reflectedPropertyName,
					queryEngine,
					cancellationToken));
			}
			return traces;
		}
		throw new InvalidOperationException(
			$"标记 IsSlottedProperty 的属性必须实现 {nameof(IXamlFillSlotOwner)}。");
	}

	private async ValueTask<IReadOnlyList<XamlPropertyFillTrace>> FillXamlSlotOwnerAsync(
		IXamlFillSlotOwner owner,
		string reflectedPropertyName,
		IXamlPropertyQueryEngine queryEngine,
		CancellationToken cancellationToken)
	{
		var execution = owner is IXamlPropertyExecutionOwner executionOwner
			? executionOwner.XamlExecution
			: DomXamlPropertyExecutionCatalog.Resolve(owner.Name);
		var source = owner is IXamlPropertyExecutionOwner sourceOwner
			? new XamlSourcePropertyEvidence(
				sourceOwner.SourceInitialization,
				sourceOwner.SourceLink,
				sourceOwner.SourceRuntime)
			: XamlSourcePropertyEvidence.Empty;
		var target = owner is IXamlPropertyExecutionOwner targetOwner
			? new XamlTargetPropertyEvidence(
				targetOwner.TargetInitialization,
				targetOwner.TargetLink,
				targetOwner.TargetRuntime)
			: XamlTargetPropertyEvidence.Empty;
		var traces = new List<XamlPropertyFillTrace>();
		var category = ResolveSlotCategory(owner);
		var ownerKind = ResolveSlotOwnerKind(owner, reflectedPropertyName);
		var evidenceKind = ResolveEvidenceKind(ownerKind, category, owner);
		foreach (var slot in owner.XamlSlots)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var context = new XamlPropertyQueryContext(
				DocumentScope,
				XPath,
				TagName,
				this,
				XamlElement,
				CreateXaml(),
				reflectedPropertyName,
				owner.Name,
				ownerKind,
				category,
				evidenceKind,
				slot,
				execution,
				source,
				target)
			{
				SlotOwner = owner
			};
			context.Traversal.Validate();
			var result = await queryEngine.QueryAsync(
				context,
				cancellationToken);
			owner.ApplyXamlQueryResult(slot, result);
			traces.Add(new(
				owner.Name,
				slot,
				result.Status,
				result.Description,
				reflectedPropertyName,
				ownerKind,
				category,
				evidenceKind,
				result.Value,
				result.ValueSource,
				result.Link));
		}
		if (owner is IElementSlotConsistencyOwner consistencyOwner)
			consistencyOwner.ValidateSlotConsistency();
		return traces;
	}

	private static ElementSlotCategory ResolveSlotCategory(object owner) =>
		owner is IDomElementSlotAuditOwner auditOwner
			? auditOwner.Category
			: ElementSlotCategory.DataOrganization;

	private static ElementSlotOwnerKind ResolveSlotOwnerKind(
		object owner,
		string reflectedPropertyName) =>
		owner switch
		{
			DomElementEvent => ElementSlotOwnerKind.Event,
			DomElementDataSource => ElementSlotOwnerKind.DataSource,
			DomElementRuntimeProperty => ElementSlotOwnerKind.RuntimeProperty,
			HtmlDomAttributeProperty or SvgDomAttributeProperty =>
				reflectedPropertyName == "ExtensionAttributes"
					? ElementSlotOwnerKind.ExtensionAttribute
					: ElementSlotOwnerKind.Attribute,
			_ => ElementSlotOwnerKind.Property
		};

	private static ElementEvidenceKind ResolveEvidenceKind(
		ElementSlotOwnerKind ownerKind,
		ElementSlotCategory category,
		object owner) =>
		ownerKind switch
		{
			ElementSlotOwnerKind.Attribute
				or ElementSlotOwnerKind.ExtensionAttribute =>
				ElementEvidenceKind.Attributes,
			ElementSlotOwnerKind.Event => ElementEvidenceKind.Events,
			ElementSlotOwnerKind.DataSource =>
				ElementEvidenceKind.TextContent
					| ElementEvidenceKind.FormState
					| ElementEvidenceKind.Resources,
			ElementSlotOwnerKind.RuntimeProperty
				when owner is DomElementRuntimeProperty runtime =>
					runtime.EvidenceKind,
			_ when owner is IDomFillSlotOwner
			{ Name: "service.globalLayout" } =>
				ElementEvidenceKind.LocalLayout,
			_ when owner is IDomFillSlotOwner
			{ Name: "service.globalStyle" } =>
				ElementEvidenceKind.ComputedStyles,
			_ => ElementEvidenceKind.Attributes
		};

	private DomElement? GetSibling(int offset)
	{
		if (Parent is null)
			return null;
		var index = Parent._children.IndexOf(this);
		var siblingIndex = index + offset;
		return index >= 0
			&& siblingIndex >= 0
			&& siblingIndex < Parent._children.Count
				? Parent._children[siblingIndex]
				: null;
	}

	protected virtual IEnumerable<DomElement> EnumerateDomChildren()
	{
		if (EffectiveMaximumHierarchyLevel is { } limit
			&& HierarchyLevel >= limit)
		{
			return [];
		}
		return Children;
	}
}

public sealed record DomElementMapping(
	string DocumentScope,
	string XPath,
	string? ParentXPath,
	IReadOnlyList<string> ChildXPaths,
	string? LeftSiblingXPath,
	string? RightSiblingXPath,
	string CapturedOwnText = "",
	string CapturedTextContent = "",
	int NodeId = 0,
	int BackendNodeId = 0,
	int CapturedOwnTextElementInsertionIndex = -1)
{
	public IReadOnlyDictionary<string, string> CapturedAttributeValues
	{
		get;
		init;
	} = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public enum DomPropertyDataSlot
{
	Initialization,
	Link,
	Runtime
}

public enum DomPropertyQueryStatus
{
	Captured,
	ConfirmedAbsent,
	SourceUnsupported
}

internal sealed record DomElementFillRequest(
	IDomFillSlotOwner Owner,
	DomPropertyQueryContext Context);

public sealed record DomPropertyQueryResult
{
	private DomPropertyQueryResult(
		DomPropertyQueryStatus status,
		string value,
		ElementPropertyValueSource valueSource,
		ElementPropertyLink link,
		string description)
	{
		ValidateValueSource(status, valueSource, link);
		Status = status;
		Value = value;
		ValueSource = valueSource;
		Link = link;
		Description = description;
	}

	public DomPropertyQueryStatus Status { get; }

	public string Value { get; }

	public ElementPropertyValueSource ValueSource { get; }

	public ElementPropertyLink Link { get; }

	public string Description { get; }

	public bool HasValue => Status == DomPropertyQueryStatus.Captured;

	public static DomPropertyQueryResult Captured(
		string value,
		ElementPropertyValueSource valueSource,
		ElementPropertyLink link,
		string description = "") =>
		new(
			DomPropertyQueryStatus.Captured,
			value ?? string.Empty,
			valueSource,
			link,
			description);

	public static DomPropertyQueryResult DirectConstant(
		string value,
		string description = "") =>
		Captured(
			value,
			ElementPropertyValueSource.DirectConstant,
			ElementPropertyLink.None,
			description);

	public static DomPropertyQueryResult ConfirmedAbsent(string description) =>
		new(
			DomPropertyQueryStatus.ConfirmedAbsent,
			string.Empty,
			ElementPropertyValueSource.Unspecified,
			ElementPropertyLink.None,
			description);

	public static DomPropertyQueryResult SourceUnsupported(string description) =>
		new(
			DomPropertyQueryStatus.SourceUnsupported,
			string.Empty,
			ElementPropertyValueSource.Unspecified,
			ElementPropertyLink.None,
			description);

	private static void ValidateValueSource(
		DomPropertyQueryStatus status,
		ElementPropertyValueSource valueSource,
		ElementPropertyLink link)
	{
		if (status != DomPropertyQueryStatus.Captured)
		{
			if (valueSource != ElementPropertyValueSource.Unspecified || link.IsSet)
			{
				throw new ArgumentException(
					"未捕获值不能声明值来源或链接。");
			}
			return;
		}

		if (valueSource == ElementPropertyValueSource.Unspecified)
			throw new ArgumentException("已捕获值必须声明明确的值来源。");
		var requiresLink = valueSource is
			ElementPropertyValueSource.ContainerAutomaticLayout
			or ElementPropertyValueSource.LinkedCalculation
			or ElementPropertyValueSource.LinkedConstant;
		if (requiresLink != link.IsSet)
		{
			throw new ArgumentException(
				requiresLink
					? $"{valueSource} 必须携带链接。"
					: $"{valueSource} 不能携带链接。");
		}
		if (valueSource == ElementPropertyValueSource.ContainerAutomaticLayout
			&& link.Kind != ElementPropertyLinkKind.ContainerLayout)
		{
			throw new ArgumentException(
				"容器自动布局必须携带强类型 ContainerLayout 链接。");
		}
		if (valueSource == ElementPropertyValueSource.LinkedConstant
			&& link.Kind != ElementPropertyLinkKind.ConstantReference)
		{
			throw new ArgumentException(
				"链接常量必须使用 ConstantReference 链接。");
		}
	}
}

public delegate ValueTask<DomPropertyQueryResult> DomPropertyQueryDelegate(
	string documentScope,
	string xpath,
	string propertyName,
	DomPropertyDataSlot slot);

public sealed record DomQueryApplicationResult(
	bool Applied,
	bool StoredValueMatches,
	string StoredEvidence,
	string Description)
{
	public static DomQueryApplicationResult NotApplied(string description) =>
		new(false, false, string.Empty, description);
}

public sealed record DomPropertyFillTrace(
	string PropertyName,
	DomPropertyDataSlot Slot,
	DomPropertyQueryStatus QueryStatus,
	string QueriedValue,
	ElementPropertyValueSource ValueSource,
	ElementPropertyLink Link,
	string QueryDescription,
	DomQueryApplicationResult Application,
	string ReflectedPropertyName = "",
	ElementSlotOwnerKind? OwnerKind = null,
	ElementSlotCategory Category = ElementSlotCategory.Unspecified,
	ElementEvidenceKind EvidenceKind = ElementEvidenceKind.None)
{
	public bool IsCorrect =>
		QueryStatus == DomPropertyQueryStatus.Captured
			? Application.Applied && Application.StoredValueMatches
			: !Application.Applied;
}

public enum XamlPropertyDataSlot
{
	Initialization,
	Link,
	Runtime
}

public enum XamlPropertyQueryStatus
{
	Captured,
	ConfirmedAbsent,
	TargetUnsupported
}

public sealed record XamlPropertyQueryResult
{
	private XamlPropertyQueryResult(
		XamlPropertyQueryStatus status,
		string value,
		ElementPropertyValueSource valueSource,
		ElementPropertyLink link,
		string description)
	{
		ValidateValueSource(status, valueSource, link);
		Status = status;
		Value = value;
		ValueSource = valueSource;
		Link = link;
		Description = description;
	}

	public XamlPropertyQueryStatus Status { get; }

	public string Value { get; }

	public ElementPropertyValueSource ValueSource { get; }

	public ElementPropertyLink Link { get; }

	public string Description { get; }

	public bool HasValue => Status == XamlPropertyQueryStatus.Captured;

	public static XamlPropertyQueryResult Captured(
		string value,
		ElementPropertyValueSource valueSource,
		ElementPropertyLink link,
		string description = "") =>
		new(
			XamlPropertyQueryStatus.Captured,
			value ?? string.Empty,
			valueSource,
			link,
			description);

	public static XamlPropertyQueryResult DirectConstant(
		string value,
		string description = "") =>
		Captured(
			value,
			ElementPropertyValueSource.DirectConstant,
			ElementPropertyLink.None,
			description);

	public static XamlPropertyQueryResult ConfirmedAbsent(string description) =>
		new(
			XamlPropertyQueryStatus.ConfirmedAbsent,
			string.Empty,
			ElementPropertyValueSource.Unspecified,
			ElementPropertyLink.None,
			description);

	public static XamlPropertyQueryResult TargetUnsupported(string description) =>
		new(
			XamlPropertyQueryStatus.TargetUnsupported,
			string.Empty,
			ElementPropertyValueSource.Unspecified,
			ElementPropertyLink.None,
			description);

	private static void ValidateValueSource(
		XamlPropertyQueryStatus status,
		ElementPropertyValueSource valueSource,
		ElementPropertyLink link)
	{
		if (status != XamlPropertyQueryStatus.Captured)
		{
			if (valueSource != ElementPropertyValueSource.Unspecified || link.IsSet)
				throw new ArgumentException("未捕获 XAML 值不能声明值来源或链接。");
			return;
		}

		if (valueSource == ElementPropertyValueSource.Unspecified)
			throw new ArgumentException("已捕获 XAML 值必须声明明确的值来源。");
		var requiresLink = valueSource is
			ElementPropertyValueSource.ContainerAutomaticLayout
			or ElementPropertyValueSource.LinkedCalculation
			or ElementPropertyValueSource.LinkedConstant;
		if (requiresLink != link.IsSet)
		{
			throw new ArgumentException(
				requiresLink
					? $"{valueSource} 必须携带 XAML 链接。"
					: $"{valueSource} 不能携带 XAML 链接。");
		}
		if (valueSource == ElementPropertyValueSource.ContainerAutomaticLayout
			&& link.Kind != ElementPropertyLinkKind.ContainerLayout)
		{
			throw new ArgumentException(
				"XAML 容器自动布局必须携带强类型 ContainerLayout 链接。");
		}
		if (valueSource == ElementPropertyValueSource.LinkedConstant
			&& link.Kind != ElementPropertyLinkKind.ConstantReference)
		{
			throw new ArgumentException(
				"XAML 链接常量必须使用 ConstantReference 链接。");
		}
	}
}

public delegate ValueTask<XamlPropertyQueryResult> XamlPropertyQueryDelegate(
	string documentScope,
	string xpath,
	string propertyName,
	XamlPropertyDataSlot slot);

public interface IDomFillSlotOwner
{
	string Name { get; }

	IReadOnlyList<DomPropertyDataSlot> DomSlots { get; }

	DomQueryApplicationResult ApplyDomQueryResult(
		DomPropertyDataSlot slot,
		DomPropertyQueryResult result);
}

public interface IDomPropertySlotOwner : IDomFillSlotOwner;

public interface IRuntimeOnlySlotOwner;

public interface IDomEventSlotOwner : IDomFillSlotOwner;

public interface IXamlFillSlotOwner
{
	string Name { get; }

	IReadOnlyList<XamlPropertyDataSlot> XamlSlots { get; }

	void ApplyXamlQueryResult(
		XamlPropertyDataSlot slot,
		XamlPropertyQueryResult result);
}

public interface IXamlPropertySlotOwner : IXamlFillSlotOwner;

public interface IXamlEventSlotOwner : IXamlFillSlotOwner;

public interface IXamlAttributeValueProvider
{
	IReadOnlyList<XamlAttributeValueCandidate>
		GetXamlAttributeValueCandidates();
}

public interface IDomElementSlotAuditOwner
{
	string Name { get; }

	ElementSlotCategory Category { get; }

	DomElementSlottedPropertyAuditResult AuditSlots();

	DomElementSlottedPropertyAuditResult AuditSlots(
		ElementPropertyTraits traits) =>
		AuditSlots();
}

public delegate DomElementSlottedPropertyAuditResult
	DomElementSlotAuditDelegate(IDomElementSlotAuditOwner owner);

public enum XamlValueResolutionPriority
{
	AbsoluteConstant = 100,
	LocalLayout = 200,
	SpecifiedCalculation = 300
}

public sealed record XamlAttributeValueCandidate(
	XamlValueResolutionPriority Priority,
	string? Value,
	string Description);

public delegate string? XamlPropertySlotValueResolver(
	ElementPropertyTraits traits,
	IReadOnlyList<XamlAttributeValueCandidate> candidates);

public enum ElementSlotFeatureAuditStatus
{
	NotRequired,
	Passed,
	MissingDomValue,
	MissingXamlValue,
	UnexpectedXamlValue,
	ValueMismatch,
	LinkMismatch,
	LayoutMismatch,
	ManualReviewRequired,
	ExecutionFailed
}

public sealed record ElementSlotFeatureAuditResult(
	ElementSlotFeatureAuditStatus Status,
	string DomEvidence,
	string XamlEvidence,
	string Description)
{
	public bool Passed =>
		Status is
			ElementSlotFeatureAuditStatus.NotRequired
			or ElementSlotFeatureAuditStatus.Passed;

	public static ElementSlotFeatureAuditResult NotRequired(string description) =>
		new(
			ElementSlotFeatureAuditStatus.NotRequired,
			string.Empty,
			string.Empty,
			description);
}

public sealed record DomElementSlottedPropertyAuditResult(
	string PropertyName,
	ElementSlotCategory Category,
	ElementSlotFeatureAuditResult Initialization,
	ElementSlotFeatureAuditResult Runtime,
	ElementSlotFeatureAuditResult Link,
	ElementSlotFeatureAuditResult Layout,
	string Description)
{
	public bool Passed =>
		Initialization.Passed
		&& Runtime.Passed
		&& Link.Passed
		&& Layout.Passed;

	public static DomElementSlottedPropertyAuditResult ExecutionFailure(
		string propertyName,
		ElementSlotCategory category,
		Exception exception)
	{
		ArgumentNullException.ThrowIfNull(exception);
		var notRequired = ElementSlotFeatureAuditResult.NotRequired(
			"审核执行失败，其他特征未继续审核。");
		return new(
			propertyName,
			category,
			notRequired,
			new(
				ElementSlotFeatureAuditStatus.ExecutionFailed,
				exception.GetType().FullName ?? exception.GetType().Name,
				string.Empty,
				exception.Message),
			notRequired,
			notRequired,
			$"审核执行异常：{exception.GetType().Name}: {exception.Message}");
	}
}

public sealed record DomElementAuditStatistics(
	int ElementsVisited,
	int ElementsPassed,
	int ElementsFailed,
	int PropertiesAudited,
	int PropertiesPassed,
	int PropertiesFailed,
	int InitializationFailures,
	int RuntimeFailures,
	int LinkFailures,
	int LayoutFailures)
{
	public static DomElementAuditStatistics Create(
		bool currentPassed,
		IReadOnlyList<DomElementSlottedPropertyAuditResult> ownResults,
		IReadOnlyList<DomElementAuditReport> children) =>
		new(
			1 + children.Sum(static child => child.Statistics.ElementsVisited),
			(currentPassed ? 1 : 0)
				+ children.Sum(static child => child.Statistics.ElementsPassed),
			(currentPassed ? 0 : 1)
				+ children.Sum(static child => child.Statistics.ElementsFailed),
			ownResults.Count
				+ children.Sum(static child => child.Statistics.PropertiesAudited),
			ownResults.Count(static result => result.Passed)
				+ children.Sum(static child => child.Statistics.PropertiesPassed),
			ownResults.Count(static result => !result.Passed)
				+ children.Sum(static child => child.Statistics.PropertiesFailed),
			ownResults.Count(static result => !result.Initialization.Passed)
				+ children.Sum(static child => child.Statistics.InitializationFailures),
			ownResults.Count(static result => !result.Runtime.Passed)
				+ children.Sum(static child => child.Statistics.RuntimeFailures),
			ownResults.Count(static result => !result.Link.Passed)
				+ children.Sum(static child => child.Statistics.LinkFailures),
			ownResults.Count(static result => !result.Layout.Passed)
				+ children.Sum(static child => child.Statistics.LayoutFailures));
}

public sealed record DomElementAuditReport(
	string ElementIdentity,
	bool Passed,
	IReadOnlyList<DomElementSlottedPropertyAuditResult> OwnProperties,
	IReadOnlyList<DomElementAuditReport> Children,
	DomElementAuditStatistics Statistics,
	string TextReport);

public sealed record DomElementFillResult(
	string ElementIdentity,
	IReadOnlyList<DomPropertyFillTrace> Slots,
	IReadOnlyList<DomElementFillResult> Children)
{
	public int QueriedSlotCount => Slots.Count;

	public int TotalQueriedSlotCount =>
		QueriedSlotCount
		+ Children.Sum(static child => child.TotalQueriedSlotCount);

	public int TotalIncorrectSlotCount =>
		Slots.Count(static slot => !slot.IsCorrect)
		+ Children.Sum(static child => child.TotalIncorrectSlotCount);
}

public sealed record XamlPropertyFillTrace(
	string PropertyName,
	XamlPropertyDataSlot Slot,
	XamlPropertyQueryStatus QueryStatus,
	string QueryDescription,
	string ReflectedPropertyName = "",
	ElementSlotOwnerKind? OwnerKind = null,
	ElementSlotCategory Category = ElementSlotCategory.Unspecified,
	ElementEvidenceKind EvidenceKind = ElementEvidenceKind.None,
	string QueriedValue = "",
	ElementPropertyValueSource ValueSource = ElementPropertyValueSource.Unspecified,
	ElementPropertyLink Link = default);

public sealed record XamlElementFillResult(
	string ElementIdentity,
	IReadOnlyList<XamlPropertyFillTrace> Slots,
	IReadOnlyList<XamlElementFillResult> Children)
{
	public int QueriedSlotCount => Slots.Count;

	public int TotalQueriedSlotCount =>
		QueriedSlotCount
		+ Children.Sum(static child => child.TotalQueriedSlotCount);
}

using Iwesun.Runtime.Web;
using Iwesun.Runtime.WebView2;
using Iwesun.Runtime.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using System.Reflection;

namespace Iwesun.Runtime.Web.WinUI;

public sealed record WinUiHtmlRuntimeSessionResult(
	HtmlRuntimeXamlObjectTree ObjectTree,
	HtmlRuntimeXamlDisplayResult Display,
	HtmlRuntimeXamlAuditResult Audit,
	WinUiXamlQueryStatistics QueryStatistics,
	XamlLayerAuditSummary LayerAudit,
	XamlLayerArchitectureSummary LayerArchitecture,
	WinUiRuntimeLayoutApplication LiveLayout);

public sealed record WinUiRuntimeLayoutApplication(
	bool Applied,
	int FlexContainersApplied,
	int BlockContainersApplied,
	string Mechanism);

public sealed class WinUiHtmlRuntimeSession
{
	private Window? _displayWindow;
	private Windows.Foundation.Size? _displayViewportSize;
	private WinUiEventRuntimeEvidenceRegistry? _eventEvidenceRegistry;

	public async ValueTask<int> RefreshRuntimeAndApplyAsync(
		HtmlRuntimeDocumentRoot htmlRoot,
		Windows.Foundation.Size runtimeViewport,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(htmlRoot);
		await htmlRoot.RefreshRuntimeAsync(cancellationToken);
		var hasCssRuntimeViewport = TryReadCssRuntimeViewport(
			htmlRoot,
			out var cssWidth,
			out var cssHeight);
		var currentViewport = htmlRoot.DesignRuntime.Layout.Viewport;
		if (hasCssRuntimeViewport
			&& NearlyEqual(currentViewport.RuntimeWidth, runtimeViewport.Width)
			&& NearlyEqual(currentViewport.RuntimeHeight, runtimeViewport.Height)
			&& NearlyEqual(currentViewport.CssRuntimeWidth, cssWidth)
			&& NearlyEqual(currentViewport.CssRuntimeHeight, cssHeight))
		{
			return 0;
		}
		htmlRoot.DesignRuntime.Layout.ApplyRuntimeViewport(
			runtimeViewport.Width,
			runtimeViewport.Height);
		if (hasCssRuntimeViewport)
		{
			htmlRoot.DesignRuntime.Layout.ApplyCssRuntimeViewport(
				cssWidth,
				cssHeight);
		}
		var window = _displayWindow
			?? throw new InvalidOperationException(
				"The live XAML tree has not been displayed by this session.");
		var factory = new WinUiXamlElementObjectFactory(
			htmlRoot.DesignRuntime,
			_eventEvidenceRegistry);
		var tree = htmlRoot.RebuildXaml(factory);
		factory.RebindRuntimeMutableData(
			htmlRoot.XamlGlobalRelationships
				?? throw new InvalidOperationException(
					"XAML global relationships were not composed during rebuild."));
		var viewport = ReadRuntimeViewport(htmlRoot);
		await new WinUiHtmlRuntimeXamlPresenter(
			window,
			activateWindow: false,
			viewport).DisplayAsync(
				tree,
				cancellationToken);
		_displayViewportSize = viewport;
		return WinUiRuntimeStyleUpdater.Apply(htmlRoot.DesignRuntime);
	}

	private static bool TryReadCssRuntimeViewport(
		HtmlRuntimeDocumentRoot root,
		out double width,
		out double height)
	{
		width = 0;
		height = 0;
		var html = root.DocumentRoots.FirstOrDefault();
		if (html is null)
			return false;
		return TryCssPixels("style.width", out width)
			&& TryCssPixels("style.height", out height);

		bool TryCssPixels(string name, out double value)
		{
			value = 0;
			var text = root.ResolveGlobalStyleValue(
				html,
				name,
				DomPropertyDataSlot.Runtime);
			if (text?.EndsWith(
				"px",
				StringComparison.OrdinalIgnoreCase) == true)
			{
				text = text[..^2];
			}
			return double.TryParse(
				text,
				System.Globalization.NumberStyles.Float,
				System.Globalization.CultureInfo.InvariantCulture,
				out value)
				&& value > 0;
		}
	}

	private static bool NearlyEqual(double left, double right) =>
		Math.Abs(left - right) <= 0.000_001;

	public async ValueTask<WinUiHtmlRuntimeSessionResult>
		BuildDisplayAndAuditAsync(
			HtmlRuntimeDocumentRoot htmlRoot,
			Window window,
			WinUiEventRuntimeEvidenceRegistry? eventEvidenceRegistry = null,
		bool activateWindow = true,
		bool breakAtStages = false,
		Windows.Foundation.Size? viewportSize = null,
		Windows.Foundation.Size? geometryEvidenceScale = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(htmlRoot);
		ArgumentNullException.ThrowIfNull(window);
		_displayWindow = window;
		if (viewportSize is { } runtimeViewport)
		{
			htmlRoot.DesignRuntime.Layout.ApplyRuntimeViewport(
				runtimeViewport.Width,
				runtimeViewport.Height);
		}
		_displayViewportSize = ReadRuntimeViewport(htmlRoot);
		var activeEventEvidenceRegistry = eventEvidenceRegistry
			?? new WinUiEventRuntimeEvidenceRegistry();
		_eventEvidenceRegistry = activeEventEvidenceRegistry;
		var factory = new WinUiXamlElementObjectFactory(
			htmlRoot.DesignRuntime,
			activeEventEvidenceRegistry);
		var objectTree = htmlRoot.BuildXaml(factory);
		WinUiRuntimeStyleUpdater.Apply(htmlRoot.DesignRuntime);
		var liveLayout = new WinUiRuntimeLayoutApplication(
			Applied: false,
			FlexContainersApplied: 0,
			BlockContainersApplied: 0,
			Mechanism: "Runtime Web strong object plans; no application-level element rewrite.");
		factory.RebindRuntimeMutableData(
			htmlRoot.XamlGlobalRelationships
				?? throw new InvalidOperationException(
					"XAML global relationships were not composed during build."));
		EnsureNestedDocumentAttachments(objectTree);
		var objectArchitecture = AuditXamlObjectArchitecture(htmlRoot, objectTree);
#if DEBUG
		await RuntimeInjector.Break(
			"runtime.web.winui.xaml.built",
			() => breakAtStages,
			new
			{
				htmlRoot.Stage,
				DocumentCount = objectTree.Documents.Count,
				RootObjectCount = objectTree.Documents.Sum(
					static document => document.RootObjects.Count),
				MappedElementCount = EnumerateAll(htmlRoot).Count(
					static element => element.XamlElement is not null),
				XamlStyleTargetCount =
					htmlRoot.DesignRuntime.Styles.XamlTargets.Count,
				GlobalRelationships = new
				{
					DataBindings = htmlRoot.XamlGlobalRelationships?.DataBindings.Count ?? 0,
					MaterializedDataBindings = htmlRoot.XamlGlobalRelationships?.DataBindings
						.Count(static binding => binding.IsMaterialized) ?? 0,
					EventBindings = htmlRoot.XamlGlobalRelationships?.EventBindings.Count ?? 0,
					MaterializedEventBindings = htmlRoot.XamlGlobalRelationships?.EventBindings
						.Count(static binding => binding.IsMaterialized) ?? 0,
					UnmaterializedDataSamples = htmlRoot.XamlGlobalRelationships?.DataBindings
						.Where(static binding => !binding.IsMaterialized)
						.Take(12)
						.Select(static binding => new
						{
							binding.Source.DynamicValue.Identity,
							binding.Source.Domain,
							binding.Source.ContentKind,
							binding.TargetProperty,
							binding.Source.Element.DocumentScope,
							binding.Source.Element.XPath
						})
						.ToArray() ?? []
				},
				ObjectArchitecture = objectArchitecture
			});
#endif
		if (objectArchitecture.Failures.Count != 0)
		{
			throw new InvalidDataException(
				"XAML object architecture is incomplete: "
				+ string.Join(" | ", objectArchitecture.Failures.Take(10)));
		}
		var display = await htmlRoot.DisplayXamlAsync(
			new WinUiHtmlRuntimeXamlPresenter(
				window,
				activateWindow,
				_displayViewportSize),
			cancellationToken);
		var primary = objectTree.Documents[0].RootObjects[0]
			as FrameworkElement
			?? throw new InvalidDataException(
				"The primary XAML object is not a FrameworkElement.");
		var elements = BuildElementIndex(htmlRoot);
#if DEBUG
		await RuntimeInjector.Break(
			"runtime.web.winui.xaml.displayed",
			() => breakAtStages,
			new
			{
				htmlRoot.Stage,
				primary.ActualWidth,
				primary.ActualHeight,
				IndexedElementCount = elements.Count
			});
#endif
		var engine = new WinUiXamlPropertyQueryEngine(
			primary,
			elements,
			activeEventEvidenceRegistry,
			htmlRoot.DesignRuntime,
			geometryEvidenceScale,
			htmlRoot.DocumentRoots.ToDictionary(
				static document => document.DocumentScope,
				static document => document.XamlElement as FrameworkElement
					?? throw new InvalidDataException(
						"A document root has no FrameworkElement for scoped geometry."),
				StringComparer.Ordinal),
			factory.MaterializedPropertyTargets);
		var trackingEngine = new TrackingXamlPropertyQueryEngine(engine);
		var fills = await htmlRoot.FillXamlAsync(
			trackingEngine,
			cancellationToken);
		var queryStatistics = trackingEngine.Snapshot();
#if DEBUG
		await RuntimeInjector.Break(
			"runtime.web.winui.xaml.filled",
			() => breakAtStages,
			new
			{
				htmlRoot.Stage,
				FilledSlotCount = fills.Sum(
					static fill => fill.TotalQueriedSlotCount),
				queryStatistics.Total,
				queryStatistics.Captured,
				queryStatistics.ConfirmedAbsent,
				queryStatistics.TargetUnsupported,
				queryStatistics.PreexistingTargetValues,
				queryStatistics.FirstUnsupported,
				queryStatistics.UnsupportedSamples
			});
#endif
		var classifiedQueryCount =
			queryStatistics.Captured
			+ queryStatistics.ConfirmedAbsent
			+ queryStatistics.TargetUnsupported;
		var filledSlotCount = fills.Sum(
			static fill => fill.TotalQueriedSlotCount);
		if (classifiedQueryCount != queryStatistics.Total
			|| filledSlotCount != queryStatistics.Total)
		{
			throw new InvalidDataException(
				"XamlFill traversal accounting is incomplete: "
				+ $"filled={filledSlotCount}; queried={queryStatistics.Total}; "
				+ $"classified={classifiedQueryCount}.");
		}
		if (queryStatistics.TargetUnsupported != 0)
		{
			throw new InvalidDataException(
				"XamlFill has no real WinUI reader for "
					+ $"{queryStatistics.TargetUnsupported} requested slots: "
					+ string.Join(
						" | ",
						queryStatistics.UnsupportedSamples));
		}
		var audit = htmlRoot.AuditXaml();
		var statistics = audit.Reports
			.Select(static report => report.Statistics)
			.ToArray();
		var layerAudit = BuildLayerAudit(htmlRoot, audit.Reports);
		var layerArchitecture = BuildLayerArchitecture(
			htmlRoot,
			objectArchitecture,
			primary);
#if DEBUG
		await RuntimeInjector.Break(
			"runtime.web.winui.audit.completed",
			() => breakAtStages,
			new
			{
				htmlRoot.Stage,
				PropertiesAudited = statistics.Sum(
					static item => item.PropertiesAudited),
				PropertiesFailed = statistics.Sum(
					static item => item.PropertiesFailed),
				ElementsFailed = statistics.Sum(
					static item => item.ElementsFailed),
				LayerArchitecture = new
				{
					VisibleElementCount =
						layerArchitecture.VisibleElements.Count,
					ArchitectureFailureCount =
						layerArchitecture.ArchitectureFailures.Count,
					FirstArchitectureFailures =
						layerArchitecture.ArchitectureFailures.Take(20)
							.ToArray(),
					RepresentativeVisibleElements =
						layerArchitecture.VisibleElements.Take(20)
							.ToArray()
				},
				LiveLayout = liveLayout,
				LayerPropertySummary = layerAudit.Levels,
				LayerPropertyFailures = layerAudit.FirstFailures.Take(40)
					.ToArray()
			});
#endif
		if (layerArchitecture.ArchitectureFailures.Count != 0)
		{
			throw new InvalidDataException(
				"Layered XAML architecture audit failed: "
					+ string.Join(
						" | ",
						layerArchitecture.ArchitectureFailures.Take(10)));
		}
		return new(
			objectTree,
			display,
			audit,
			queryStatistics,
			layerAudit,
			layerArchitecture,
			liveLayout);
	}

	private static void EnsureNestedDocumentAttachments(
		HtmlRuntimeXamlObjectTree tree)
	{
		foreach (var document in tree.Documents.Skip(1))
		{
			if (document.Roots.Count != 1
				|| document.Roots[0] is not { } rootNode
				|| rootNode.Element is not FrameworkElement frameRoot
				|| rootNode.Plan.SourceElement is not { } sourceRoot
				|| sourceRoot.EmbeddingOwner?.XamlElement
					is not ContentControl frameOwner)
			{
				throw new InvalidDataException(
					"A nested document has no strong iframe-owner object link.");
			}
			if (frameOwner.Visibility == Visibility.Collapsed)
				continue;
			if (!ReferenceEquals(frameOwner.Content, frameRoot))
			{
				throw new InvalidDataException(
					frameOwner.Content is null
						? "The nested document root was not attached by "
							+ "the formal Runtime Web object-tree builder."
						: "The iframe owner contains a different object.");
			}
		}
	}

	private static Windows.Foundation.Size ReadRuntimeViewport(
		HtmlRuntimeDocumentRoot htmlRoot)
	{
		var viewport = htmlRoot.DesignRuntime.Layout.Viewport;
		if (viewport.RuntimeWidth <= 0 || viewport.RuntimeHeight <= 0)
		{
			throw new InvalidOperationException(
				"The global layout manager has no live runtime viewport.");
		}
		return new(viewport.RuntimeWidth, viewport.RuntimeHeight);
	}

	private static IReadOnlyDictionary<string, FrameworkElement>
		BuildElementIndex(HtmlRuntimeDocumentRoot htmlRoot)
	{
		var elements = new Dictionary<string, FrameworkElement>(
			StringComparer.Ordinal);
		foreach (var element in htmlRoot.DocumentRoots.SelectMany(
			EnumeratePreOrder))
		{
			if (element.XamlElement is not FrameworkElement frameworkElement)
				continue;
			var identity = element.DocumentScope.Equals(
				htmlRoot.DocumentScope,
				StringComparison.Ordinal)
					? element.XPath
					: $"{element.DocumentScope}::{element.XPath}";
			if (!elements.TryAdd(identity, frameworkElement))
			{
				throw new InvalidDataException(
					$"Duplicate WinUI element identity '{identity}'.");
			}
		}
		if (elements.Count == 0)
			throw new InvalidDataException("The WinUI element index is empty.");
		return elements;
	}

	private static XamlLayerArchitectureSummary BuildLayerArchitecture(
		HtmlRuntimeDocumentRoot htmlRoot,
		XamlObjectArchitectureAudit objectArchitecture,
		FrameworkElement xamlRoot)
	{
		var entries = EnumerateAll(htmlRoot)
			.Where(static element =>
				element.XamlSupport != XamlConversionSupport.NonVisual)
			.OrderBy(static element => element.HierarchyLevel)
			.ThenBy(static element => element.DocumentScope, StringComparer.Ordinal)
			.ThenBy(static element => element.XPath, StringComparer.Ordinal)
			.Select(element => BuildLayerArchitectureEntry(
				htmlRoot,
				element,
				xamlRoot))
			.ToArray();
		var failures = entries
			.SelectMany(static entry => entry.ArchitectureIssues.Select(
				issue =>
					$"{entry.Identity}: {issue} "
					+ $"[dom={entry.DomType};xaml={entry.XamlType};"
					+ $"mapping={entry.MappingKind};"
					+ $"display={entry.DisplayRuntime};"
					+ $"position={entry.PositionRuntime};"
					+ $"domChildren={entry.DomChildCount};"
					+ $"xamlChildren={entry.XamlChildCount};"
					+ $"actualParent={entry.ActualXamlParentType};"
					+ $"rows={entry.RowDefinitionCount};"
					+ $"columns={entry.ColumnDefinitionCount};"
					+ $"layoutBindings={entry.LayoutBindingCount};"
					+ $"layoutTargets={entry.RegisteredLayoutTargetCount};"
					+ $"materialized={entry.MaterializedLayoutTargetCount}]"))
			.Concat(objectArchitecture.Failures)
			.ToArray();
		return new(entries, failures);
	}

	private static XamlLayerArchitectureEntry BuildLayerArchitectureEntry(
		HtmlRuntimeDocumentRoot htmlRoot,
		DomElement element,
		FrameworkElement xamlRoot)
	{
		var projection = element.GetXamlObjectProjection();
		var mappingDeclaration = element.GetType().GetMethod(
			"CreateXaml",
			BindingFlags.Instance | BindingFlags.NonPublic)
			?.DeclaringType;
		var expectedParent = FindNearestProjectedParent(element.Parent);
		var actualParent = element.XamlElement is DependencyObject dependencyObject
			? ReadActualParent(dependencyObject)
			: null;
		var styleBindings = htmlRoot.DesignRuntime.Styles.Bindings
			.Where(binding =>
				binding.DocumentScope.Equals(
					element.DocumentScope,
					StringComparison.Ordinal)
				&& binding.XPath.Equals(element.XPath, StringComparison.Ordinal))
			.ToArray();
		var layoutBindings = htmlRoot.DesignRuntime.Layout.Bindings
			.Where(binding =>
				binding.Identity.DocumentScope.Equals(
					element.DocumentScope,
					StringComparison.Ordinal)
				&& binding.Identity.XPath.Equals(
					element.XPath,
					StringComparison.Ordinal))
			.ToArray();
		var styleTargets = htmlRoot.DesignRuntime.Styles.XamlTargets
			.Where(binding =>
				binding.Source.DocumentScope.Equals(
					element.DocumentScope,
					StringComparison.Ordinal)
				&& binding.Source.XPath.Equals(
					element.XPath,
					StringComparison.Ordinal))
			.ToArray();
		var layoutTargets = htmlRoot.DesignRuntime.Layout.XamlTargets
			.Where(binding =>
				binding.Owner.DocumentScope.Equals(
					element.DocumentScope,
					StringComparison.Ordinal)
				&& binding.Owner.XPath.Equals(
					element.XPath,
					StringComparison.Ordinal))
			.ToArray();
		var displayInitialization = ReadGlobalStyleSlot(
			element,
			"style.display",
			DomPropertyDataSlot.Initialization);
		var runtimeDisplay = ReadGlobalStyleSlot(
			element,
			"style.display",
			DomPropertyDataSlot.Runtime);
		var runtimePosition = ReadGlobalStyleSlot(
			element,
			"style.position",
			DomPropertyDataSlot.Runtime);
		var runtimeFlexDirection = ReadGlobalStyleSlot(
			element,
			"style.flexDirection",
			DomPropertyDataSlot.Runtime);
		var runtimeFlexWrap = ReadGlobalStyleSlot(
			element,
			"style.flexWrap",
			DomPropertyDataSlot.Runtime);
		var xamlPosition = element.XamlElement is FrameworkElement frameworkElement
			? ReadXamlPosition(frameworkElement, xamlRoot)
			: (X: double.NaN, Y: double.NaN);
		var liveFrameworkElement = element.XamlElement as FrameworkElement;
		var liveLayoutSlot = liveFrameworkElement is null
			? string.Empty
			: Microsoft.UI.Xaml.Controls.Primitives.LayoutInformation
				.GetLayoutSlot(liveFrameworkElement).ToString();
		var liveRowDefinitions = liveFrameworkElement is Grid liveGrid
			? string.Join(",", liveGrid.RowDefinitions.Select(
				static (definition, index) =>
					$"{index}:{definition.Height}/"
					+ $"{definition.ActualHeight:R}/"
					+ $"{definition.MinHeight:R}/"
					+ $"{definition.MaxHeight:R}"))
			: string.Empty;
		var liveColumnDefinitions = liveFrameworkElement is Grid columnGrid
			? string.Join(",", columnGrid.ColumnDefinitions.Select(
				static (definition, index) =>
					$"{index}:{definition.Width}/"
					+ $"{definition.ActualWidth:R}/"
					+ $"{definition.MinWidth:R}/"
					+ $"{definition.MaxWidth:R}"))
			: string.Empty;
		var issues = new List<string>();
		var belongsToInactiveFrame = htmlRoot.DocumentRoots
			.FirstOrDefault(root => root.DocumentScope.Equals(
				element.DocumentScope,
				StringComparison.Ordinal))
			?.EmbeddingOwner?.XamlElement
				is FrameworkElement
		{
			Visibility: Visibility.Collapsed
		};
		if (element.Parent is not null
			&& element.HierarchyLevel != element.Parent.HierarchyLevel + 1)
		{
			issues.Add("Hierarchy level does not equal parent level + 1.");
		}
		if (mappingDeclaration != element.GetType())
		{
			issues.Add(
				"CreateXaml is inherited from "
					+ $"{mappingDeclaration?.Name ?? "unknown"} instead of "
					+ "being declared by the concrete DOM element type.");
		}
		if (element.XamlElement is null)
		{
			issues.Add("Visual DOM element has no XAML object.");
		}
		else if (!IsMaterializedTypeCompatible(
			element,
			projection.Mapping.ElementName,
			element.XamlElement))
		{
			issues.Add(
				$"XAML type {element.XamlElement.GetType().Name} does not match "
					+ $"mapping {projection.Mapping.ElementName}.");
		}
		if (element.XamlElement is FrameworkElement clippedElement
			&& clippedElement.ActualWidth > 0
			&& clippedElement.ActualHeight > 0
			&& clippedElement.Clip is RectangleGeometry clip
			&& (clip.Rect.Width <= 0 || clip.Rect.Height <= 0))
		{
			issues.Add(
				"Live WinUI clip is empty for a non-empty arranged element.");
		}
		if (element.XamlElement is DependencyObject actualChild
			&& expectedParent?.XamlElement is DependencyObject expectedParentObject
			&& !IsVisualOwner(
				expectedParentObject,
				actualParent,
				actualChild)
			&& !expectedParent.XamlOwnedObjects.Any(candidate =>
				candidate is DependencyObject owned
				&& IsVisualOwner(
					owned,
					actualParent,
					actualChild))
			&& !belongsToInactiveFrame)
		{
			issues.Add(
				"Actual XAML parent is not the nearest projected DOM parent.");
		}
		if (runtimeDisplay is "flex" or "inline-flex"
			&& projection.Mapping.Kind != XamlElementMappingKind.FlexLayout)
		{
			issues.Add("Computed flex display was not projected as FlexLayout.");
		}
		if (runtimeDisplay is "grid" or "inline-grid"
			&& projection.Mapping.Kind != XamlElementMappingKind.GridLayout)
		{
			issues.Add("Computed grid display was not projected as GridLayout.");
		}
		if (projection.Mapping.RequiresRuntimeLayoutContract
			&& layoutBindings.Length == 0)
		{
			issues.Add(
				"Mapping requires a runtime layout contract but no layout "
					+ "binding was reconstructed.");
		}
		if (projection.Mapping.RequiresRuntimeLayoutContract
			&& layoutTargets.Length == 0)
		{
			issues.Add(
				"Mapping requires a runtime layout contract but no XAML "
					+ "layout target was registered.");
		}
		else if (projection.Mapping.RequiresRuntimeLayoutContract
			&& !layoutTargets.Any(static target => target.IsMaterialized))
		{
			issues.Add(
				"Runtime layout target exists but has no executable WinUI "
					+ "layout mechanism.");
		}
		return new(
			element.HierarchyLevel,
			Identity(element),
			element.Parent is null ? string.Empty : Identity(element.Parent),
			element.GetType().Name,
			element.TagName,
			element.QuerySpecialization.ToString(),
			mappingDeclaration?.Name ?? string.Empty,
			projection.Mapping.ElementName,
			projection.Mapping.Kind.ToString(),
			projection.Mapping.RequiresRuntimeLayoutContract,
			projection.Mapping.Reason,
			projection.ContentProjection.ToString(),
			projection.ChildPlacement.ToString(),
			element.Children.Count,
			element.XamlObjectNode?.Children.Count ?? 0,
			actualParent?.GetType().Name ?? string.Empty,
			liveFrameworkElement?.IsLoaded == true,
			liveFrameworkElement?.Visibility.ToString() ?? string.Empty,
			liveFrameworkElement?.HorizontalAlignment.ToString() ?? string.Empty,
			liveFrameworkElement?.VerticalAlignment.ToString() ?? string.Empty,
			liveFrameworkElement is null ? 0 : Grid.GetRow(liveFrameworkElement),
			liveFrameworkElement is null ? 0 : Grid.GetColumn(liveFrameworkElement),
			liveFrameworkElement?.Translation.ToString() ?? string.Empty,
			displayInitialization,
			runtimeDisplay,
			runtimePosition,
			runtimeFlexDirection,
			runtimeFlexWrap,
			ReadGlobalStyleSlot(
				element,
				"style.width",
				DomPropertyDataSlot.Initialization),
			ReadGlobalStyleSlot(
				element,
				"style.width",
				DomPropertyDataSlot.Runtime),
			ReadGlobalStyleSlot(
				element,
				"style.height",
				DomPropertyDataSlot.Initialization),
			ReadGlobalStyleSlot(
				element,
				"style.height",
				DomPropertyDataSlot.Runtime),
			ReadGlobalStyleSlot(
				element,
				"style.minWidth",
				DomPropertyDataSlot.Initialization),
			ReadGlobalStyleSlot(
				element,
				"style.minWidth",
				DomPropertyDataSlot.Runtime),
			ReadGlobalStyleSlot(
				element,
				"style.minHeight",
				DomPropertyDataSlot.Initialization),
			ReadGlobalStyleSlot(
				element,
				"style.minHeight",
				DomPropertyDataSlot.Runtime),
			ReadGlobalStyleSlot(
				element,
				"style.boxSizing",
				DomPropertyDataSlot.Runtime),
			ReadRuntimeBoxEdges(element, "margin"),
			ReadRuntimeBoxEdges(element, "padding"),
			ReadRuntimeBoxEdges(element, "border", "Width"),
			ReadGlobalStyleSlot(
				element,
				"style.alignItems",
				DomPropertyDataSlot.Runtime),
			ReadGlobalStyleSlot(
				element,
				"style.justifyContent",
				DomPropertyDataSlot.Runtime),
			SlotValue(FindRuntimeProperty(element, "rect.x")?.SourceRuntime),
			SlotValue(FindRuntimeProperty(element, "rect.y")?.SourceRuntime),
			SlotValue(FindRuntimeProperty(element, "rect.width")?.SourceRuntime),
			SlotValue(FindRuntimeProperty(element, "rect.height")?.SourceRuntime),
			xamlPosition.X,
			xamlPosition.Y,
			liveFrameworkElement?.ActualWidth ?? double.NaN,
			liveFrameworkElement?.ActualHeight ?? double.NaN,
			liveFrameworkElement?.Width ?? double.NaN,
			liveFrameworkElement?.Height ?? double.NaN,
			liveFrameworkElement?.MinWidth ?? double.NaN,
			liveFrameworkElement?.MinHeight ?? double.NaN,
			liveFrameworkElement?.MaxWidth ?? double.NaN,
			liveFrameworkElement?.MaxHeight ?? double.NaN,
			liveFrameworkElement?.DesiredSize.Width ?? double.NaN,
			liveFrameworkElement?.DesiredSize.Height ?? double.NaN,
			liveLayoutSlot,
			liveRowDefinitions,
			liveColumnDefinitions,
			(element.XamlElement as Control)?.BorderThickness.ToString()
				?? string.Empty,
			element.XamlObjectNode?.Plan.RowDefinitions.Count ?? 0,
			element.XamlObjectNode?.Plan.ColumnDefinitions.Count ?? 0,
			styleBindings.Length,
			layoutBindings.Length,
			styleTargets.Length,
			string.Join(",", styleTargets
				.Where(static target => target.TargetProperty is
					"Width" or "Height" or "Padding" or "BorderThickness")
				.Select(static target =>
					$"{target.Source.PropertyName}->{target.TargetProperty}"
					+ $"[{target.CompositeIndex}/{target.CompositePartCount};"
					+ $"{target.CompositeValueKind};"
					+ $"{target.CompositeTargetId ?? "single"}]")),
			layoutTargets.Length,
			layoutTargets.Count(static target => target.IsMaterialized),
			issues);
	}

	private static bool IsMaterializedTypeCompatible(
		DomElement source,
		string expectedType,
		object actual)
	{
		if (actual.GetType().Name.Equals(expectedType, StringComparison.Ordinal))
			return true;
		if (expectedType == "TextBox" && actual is TextBox)
			return true;
		if (expectedType == "Canvas" && actual is Canvas)
			return true;
		if (expectedType == "Canvas" && actual is HtmlSvgViewport)
			return true;
		if (expectedType == "Image" && actual is HtmlImageView)
			return true;
		if (actual is HtmlFilteredElementHost filtered)
		{
			return TryReadStyleValue(
					source,
					"style.filter",
					out var filter)
				&& filter != "none"
				&& WinUiFilterBehavior.RequiresIsolation(filter)
				&& IsMaterializedTypeCompatible(
					source,
					expectedType,
					filtered.InnerElement);
		}
		if (expectedType != "TextBlock"
			|| actual is not HtmlVerticalTextControl)
		{
			return false;
		}
		return TryReadStyleValue(
				source,
				"style.writingMode",
				out var mode)
			&& mode is "vertical-rl" or "vertical-lr";
	}

	private static string ReadRuntimeBoxEdges(
		DomElement element,
		string boxName,
		string suffix = "") =>
		string.Join(
			",",
			new[] { "Top", "Right", "Bottom", "Left" }
				.Select(edge => ReadGlobalStyleSlot(
					element,
					$"style.{boxName}{edge}{suffix}",
					DomPropertyDataSlot.Runtime)));

	private static bool IsVisualOwner(
		DependencyObject expectedOwner,
		DependencyObject? actualParent,
		DependencyObject actualChild)
	{
		if (IsStrongObjectOwner(expectedOwner, actualChild))
			return true;
		if (actualParent is null)
			return false;
		if (ReferenceEquals(expectedOwner, actualParent))
			return true;
		var contentOwner = expectedOwner is HtmlFilteredElementHost filtered
			? filtered.InnerElement
			: expectedOwner;
		if (ReferenceEquals(contentOwner, actualParent))
			return true;
		for (var ancestor = actualParent; ancestor is not null;)
		{
			if (ReferenceEquals(ancestor, expectedOwner)
				|| ReferenceEquals(ancestor, contentOwner))
			{
				return true;
			}
			ancestor = ReadActualParent(ancestor);
		}
		var layoutRoot = contentOwner switch
		{
			HtmlCssBoxGrid cssBox => cssBox.LayoutRoot,
			HtmlInteractiveFlexPanel interactive => interactive.LayoutRoot,
			_ => null
		};
		if (layoutRoot is null)
			return false;
		if (ReferenceEquals(layoutRoot, actualParent))
			return true;
		return actualParent is HtmlCssFlexLineGrid line
			&& ReferenceEquals(
				ReadActualParent(line),
				layoutRoot);
	}

	private static bool IsStrongObjectOwner(
		DependencyObject owner,
		DependencyObject child)
	{
		if (owner is HtmlFilteredElementHost filtered)
			owner = filtered.InnerElement;
		if (owner is HtmlCssBoxGrid cssBox)
			return ContainsDirectOrFlexLineChild(cssBox.LayoutRoot, child);
		if (owner is HtmlInteractiveFlexPanel interactive)
			return ContainsDirectOrFlexLineChild(interactive.LayoutRoot, child);
		if (owner is HtmlSvgViewport svgViewport)
			return svgViewport.CoordinateSurface.Children.Any(candidate =>
				ReferenceEquals(candidate, child));
		if (owner is Panel panel)
			return ContainsDirectOrFlexLineChild(panel, child);
		if (owner is Border border)
			return ReferenceEquals(border.Child, child);
		if (owner is ContentControl content)
			return ReferenceEquals(content.Content, child);
		if (owner is ItemsControl items)
			return items.Items.Cast<object>().Any(item =>
				ReferenceEquals(item, child));
		if (owner is TextBlock text && child is Inline inline)
			return text.Inlines.Contains(inline);
		return false;
	}

	private static bool ContainsDirectOrFlexLineChild(
		Panel panel,
		DependencyObject child) => panel.Children.Any(candidate =>
			ReferenceEquals(candidate, child)
			|| candidate is HtmlCssFlexLineGrid line
				&& line.Children.Any(lineChild =>
					ReferenceEquals(lineChild, child))
			|| candidate is Panel hosted
				&& HtmlContainingBlockChildHost.ContainsHostedChild(
					hosted,
					child));

	private static DependencyObject? ReadActualParent(
		DependencyObject element) => element is FrameworkElement
		{
			Parent: { } logicalParent
		}
			? logicalParent
			: VisualTreeHelper.GetParent(element);

	private static bool TryReadStyleValue(
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
		return value.Length != 0;
	}

	private static string ReadGlobalStyleSlot(
		DomElement source,
		string propertyName,
		DomPropertyDataSlot slot) =>
		source.HtmlRoot?.ResolveGlobalStyleValue(
			source,
			propertyName,
			slot)
		?? string.Empty;

	private static (double X, double Y) ReadXamlPosition(
		FrameworkElement element,
		FrameworkElement root)
	{
		try
		{
			var point = element.TransformToVisual(root)
				.TransformPoint(new Windows.Foundation.Point());
			return (point.X, point.Y);
		}
		catch (InvalidOperationException)
		{
			return (double.NaN, double.NaN);
		}
	}

	private static DomElement? FindNearestProjectedParent(DomElement? element)
	{
		while (element is not null
			&& element.XamlSupport == XamlConversionSupport.NonVisual)
		{
			element = element.Parent;
		}
		return element;
	}

	private static DomElementRuntimeProperty? FindRuntimeProperty(
		DomElement element,
		string propertyName)
	{
		var specialized = element switch
		{
			HtmlDomElementDefinition html => html.RuntimeProperties,
			SvgDomElementDefinition svg => svg.RuntimeProperties,
			_ => []
		};
		return element.RuntimeGeometry
			.Cast<DomElementRuntimeProperty>()
			.Concat(specialized)
			.FirstOrDefault(property =>
			property.Name.Equals(propertyName, StringComparison.Ordinal));
	}

	private static string SlotValue(ElementPropertySlot<string>? slot) =>
		slot is { IsSet: true } value
			? value.Value ?? string.Empty
			: string.Empty;

	private static IEnumerable<DomElement> EnumeratePreOrder(DomElement root)
	{
		yield return root;
		if (root.EffectiveMaximumHierarchyLevel is { } limit
			&& root.HierarchyLevel >= limit)
		{
			yield break;
		}
		foreach (var child in root.Children)
		{
			foreach (var descendant in EnumeratePreOrder(child))
				yield return descendant;
		}
	}

	private static IEnumerable<DomElement> EnumerateAll(
		HtmlRuntimeDocumentRoot htmlRoot) =>
		htmlRoot.DocumentRoots.SelectMany(EnumeratePreOrder);

	private static XamlLayerAuditSummary BuildLayerAudit(
		HtmlRuntimeDocumentRoot htmlRoot,
		IReadOnlyList<DomElementAuditReport> reports)
	{
		var hierarchyLevels = EnumerateAll(htmlRoot).ToDictionary(
			Identity,
			static element => element.HierarchyLevel,
			StringComparer.Ordinal);
		var entries = reports
			.SelectMany(static report => EnumerateAuditReports(report))
			.Select(entry => entry with
			{
				Depth = hierarchyLevels.TryGetValue(
					entry.Report.ElementIdentity,
					out var hierarchyLevel)
						? hierarchyLevel
						: throw new InvalidDataException(
							$"Audit report identity "
							+ $"'{entry.Report.ElementIdentity}' has no "
							+ "element-tree hierarchy level.")
			})
			.ToArray();
		var levels = entries
			.GroupBy(static entry => entry.Depth)
			.OrderBy(static group => group.Key)
			.Select(static group => new XamlLayerAuditLevel(
				group.Key,
				group.Count(),
				group.Count(static entry => !entry.Report.Passed),
				group.Sum(static entry => entry.Report.OwnProperties.Count),
				group.Sum(static entry => entry.Report.OwnProperties.Count(
					static property => !property.Passed)),
				group.Sum(static entry => entry.Report.OwnProperties.Count(
					static property => property.Category ==
						ElementSlotCategory.Space && !property.Passed)),
				group.Sum(static entry => entry.Report.OwnProperties.Count(
					static property => !property.Layout.Passed))))
			.ToArray();
		var firstFailures = entries
			.SelectMany(static entry => entry.Report.OwnProperties
				.Where(static property => !property.Passed)
				.SelectMany(property => EnumeratePropertyFailures(
					entry.Depth,
					entry.Report.ElementIdentity,
					property)))
			.OrderBy(static failure => failure.Depth)
			.ThenBy(
				static failure => failure.ElementIdentity,
				StringComparer.Ordinal)
			.ThenBy(
				static failure => failure.PropertyName,
				StringComparer.Ordinal)
			.ThenBy(static failure => failure.Slot, StringComparer.Ordinal)
			.Take(100)
			.ToArray();
		return new(levels, firstFailures);
	}

	private static IEnumerable<XamlLayerAuditEntry> EnumerateAuditReports(
		DomElementAuditReport report)
	{
		yield return new(0, report);
		foreach (var child in report.Children)
		{
			foreach (var entry in EnumerateAuditReports(child))
				yield return entry;
		}
	}

	private static IEnumerable<XamlLayerAuditFailure> EnumeratePropertyFailures(
		int depth,
		string elementIdentity,
		DomElementSlottedPropertyAuditResult property)
	{
		var slots = new[]
		{
			("Initialization", property.Initialization),
			("Runtime", property.Runtime),
			("Link", property.Link),
			("Layout", property.Layout)
		};
		foreach (var (slot, result) in slots)
		{
			if (result.Passed)
				continue;
			yield return new(
				depth,
				elementIdentity,
				property.PropertyName,
				property.Category.ToString(),
				slot,
				result.Status.ToString(),
				TruncateDiagnosticEvidence(result.DomEvidence),
				TruncateDiagnosticEvidence(result.XamlEvidence),
				TruncateDiagnosticEvidence(result.Description));
		}
	}

	private static string TruncateDiagnosticEvidence(string? value)
	{
		const int maximumLength = 240;
		if (string.IsNullOrEmpty(value))
			return string.Empty;
		return value.Length <= maximumLength
			? value
			: string.Concat(value.AsSpan(0, maximumLength), "…");
	}

	private static XamlObjectArchitectureAudit AuditXamlObjectArchitecture(
		HtmlRuntimeDocumentRoot htmlRoot,
		HtmlRuntimeXamlObjectTree objectTree)
	{
		var elements = EnumerateAll(htmlRoot).ToArray();
		var failures = new List<string>();
		var primaryObjects = new HashSet<object>(
			ReferenceEqualityComparer.Instance);
		var ownedObjects = new HashSet<object>(
			ReferenceEqualityComparer.Instance);
		foreach (var element in elements)
		{
			if (element.XamlElement is null)
			{
				if (element.XamlSupport != XamlConversionSupport.NonVisual)
					failures.Add($"Missing primary object: {Identity(element)}");
				if (element.XamlObjectNode is not null
					|| element.XamlOwnedObjects.Count != 0)
					failures.Add($"Null primary has owned objects: {Identity(element)}");
				continue;
			}
			if (!primaryObjects.Add(element.XamlElement))
				failures.Add($"Duplicate primary object: {Identity(element)}");
			if (element.XamlObjectNode is null)
				failures.Add($"Missing build node: {Identity(element)}");
			else
			{
				if (!ReferenceEquals(
					element.XamlObjectNode.Element,
					element.XamlElement))
				{
					failures.Add($"Primary/build-node mismatch: {Identity(element)}");
				}
				if (!ReferenceEquals(
					element.XamlObjectNode.Plan.SourceElement,
					element))
				{
					failures.Add($"Build-node source mismatch: {Identity(element)}");
				}
				ValidateProjection(element, element.XamlObjectNode.Plan, failures);
			}
			if (!element.XamlOwnedObjects.Contains(
				element.XamlElement,
				ReferenceEqualityComparer.Instance))
			{
				failures.Add($"Primary object is not owned: {Identity(element)}");
			}
			foreach (var owned in element.XamlOwnedObjects)
			{
				if (!ownedObjects.Add(owned))
					failures.Add($"XAML object has multiple DOM owners: {Identity(element)}");
			}
		}
		var builtNodes = objectTree.Documents
			.SelectMany(static document => document.Roots)
			.SelectMany(EnumerateObjectNodes)
			.ToArray();
		var builtObjects = new HashSet<object>(
			ReferenceEqualityComparer.Instance);
		var sourceNodeCounts = new Dictionary<DomElement, int>(
			ReferenceEqualityComparer.Instance);
		foreach (var node in builtNodes)
		{
			if (!builtObjects.Add(node.Element))
			{
				failures.Add(
					$"XAML object occurs more than once in tree: "
						+ $"{node.Plan.Mapping.ElementName}");
			}
			if (node.Plan.OwnerElement is null)
				failures.Add($"Ownerless XAML object: {node.Plan.Mapping.ElementName}");
			if (!ownedObjects.Contains(node.Element))
			{
				failures.Add(
					$"Built object missing from owner: {node.Plan.Mapping.ElementName}");
			}
			var duplicateAttributes = node.Plan.InitializationAttributes
				.GroupBy(static attribute => attribute.Name, StringComparer.Ordinal)
				.FirstOrDefault(static group => group.Count() > 1);
			if (duplicateAttributes is not null)
			{
				failures.Add(
					$"Duplicate XAML initialization attribute "
						+ $"{duplicateAttributes.Key}: "
						+ $"{node.Plan.Description}");
			}
			if (node.Plan.SourceElement is not { } source)
				continue;
			sourceNodeCounts[source] = sourceNodeCounts.GetValueOrDefault(source) + 1;
			if (!ReferenceEquals(node.Plan.OwnerElement, source))
			{
				failures.Add(
					$"Primary plan owner/source mismatch: {Identity(source)}");
			}
			if (!ReferenceEquals(node.Element, source.XamlElement))
			{
				failures.Add(
					$"Tree/source primary mismatch: {Identity(source)}");
			}
			var currentProjection = source.GetXamlObjectProjection();
			if (node.Plan.Mapping.ElementName !=
					currentProjection.Mapping.ElementName
				|| node.Plan.Mapping.Kind != currentProjection.Mapping.Kind
				|| node.Plan.Mapping.RequiresRuntimeLayoutContract !=
					currentProjection.Mapping.RequiresRuntimeLayoutContract
				|| node.Plan.ChildPlacement != currentProjection.ChildPlacement
				|| node.Plan.ContentProjection !=
					currentProjection.ContentProjection)
			{
				failures.Add(
					$"Stale XAML projection decision: {Identity(source)}");
			}
			ValidateProjectedChildren(source, node, failures);
		}
		if (builtObjects.Count != ownedObjects.Count
			|| !builtObjects.SetEquals(ownedObjects))
		{
			failures.Add(
				$"Built/owned XAML object sets differ: "
					+ $"built={builtObjects.Count}, owned={ownedObjects.Count}.");
		}
		foreach (var element in elements)
		{
			var count = sourceNodeCounts.GetValueOrDefault(element);
			var expected = element.XamlSupport ==
				XamlConversionSupport.NonVisual ? 0 : 1;
			if (count != expected)
			{
				failures.Add(
					$"DOM/XAML primary cardinality mismatch: "
						+ $"{Identity(element)}, expected={expected}, actual={count}.");
			}
		}
		var representativePaths = new HashSet<string>(
			[
				"/html/body/div[1]/div/div",
				"/html/body/div[1]/div/div/div/nav/div",
				"/html/body/div[1]/div/div/main",
				"/html/body/div[1]/div/div/main/div/div[2]",
				"/html/body/div[1]/div/div/div/nav/div/div[7]/div[2]/div[2]/div[1]/a/div[1]"
			],
			StringComparer.Ordinal);
		var representatives = elements
			.Where(element => representativePaths.Contains(element.XPath))
			.Select(static element => new XamlObjectRepresentative(
				Identity(element),
				element.GetType().Name,
				element.TagName,
				element.XamlElement?.GetType().Name,
				element.XamlObjectNode?.Plan.Mapping.Kind,
				element.XamlObjectNode?.Plan.ContentProjection,
				element.XamlObjectNode?.Plan.ChildPlacement,
				element.Children.Count,
				element.XamlObjectNode?.Children.Count ?? 0,
				element.XamlOwnedObjects.Count,
				element.XamlObjectNode?.Plan.InitializationAttributes
					.Select(static attribute => attribute.Name)
					.ToArray() ?? []))
			.ToArray();
		return new(
			elements.Length,
			primaryObjects.Count,
			ownedObjects.Count,
			builtNodes.Length,
			elements.Count(static element =>
				element.XamlSupport == XamlConversionSupport.NonVisual),
			CountBy(
				builtNodes,
				static node => node.Plan.Mapping.ElementName),
			CountBy(
				builtNodes,
				static node => node.Plan.Mapping.Kind.ToString()),
			CountBy(
				builtNodes,
				static node => node.Plan.ContentProjection.ToString()),
			failures,
			representatives);
	}

	private static void ValidateProjectedChildren(
		DomElement source,
		XamlElementObjectBuildNode sourceNode,
		ICollection<string> failures)
	{
		var expected = source.Children
			.SelectMany(EnumerateProjectedDomChildren)
			.ToArray();
		var actual = sourceNode.Children
			.SelectMany(EnumerateFirstSourceChildren)
			.ToArray();
		if (expected.Length == actual.Length
			&& expected.Zip(
				actual,
				static (left, right) => ReferenceEquals(left, right))
				.All(static equal => equal))
		{
			return;
		}
		failures.Add(
			$"Projected child sequence mismatch: {Identity(source)}, "
				+ $"expected={expected.Length}, actual={actual.Length}.");
	}

	private static IEnumerable<DomElement> EnumerateProjectedDomChildren(
		DomElement element)
	{
		if (element.XamlSupport == XamlConversionSupport.NonVisual)
		{
			foreach (var child in element.Children)
			{
				foreach (var descendant in EnumerateProjectedDomChildren(child))
					yield return descendant;
			}
			yield break;
		}
		if (element.XamlSupport != XamlConversionSupport.Unsupported)
			yield return element;
	}

	private static IEnumerable<DomElement> EnumerateFirstSourceChildren(
		XamlElementObjectBuildNode node)
	{
		if (node.Plan.SourceElement is { } source)
		{
			yield return source;
			yield break;
		}
		foreach (var child in node.Children)
		{
			foreach (var projected in EnumerateFirstSourceChildren(child))
				yield return projected;
		}
	}

	private static IReadOnlyList<XamlObjectArchitectureCount> CountBy(
		IEnumerable<XamlElementObjectBuildNode> nodes,
		Func<XamlElementObjectBuildNode, string> keySelector) =>
		nodes.GroupBy(keySelector, StringComparer.Ordinal)
			.OrderByDescending(static group => group.Count())
			.ThenBy(static group => group.Key, StringComparer.Ordinal)
			.Select(static group => new XamlObjectArchitectureCount(
				group.Key,
				group.Count()))
			.ToArray();

	private static void ValidateProjection(
		DomElement element,
		XamlElementObjectPlan plan,
		ICollection<string> failures)
	{
		var expectedPlacement = plan.ContentProjection switch
		{
			XamlElementContentProjectionKind.DirectChildren =>
				ElementXamlChildPlacementKind.DirectChildren,
			XamlElementContentProjectionKind.Content =>
				ElementXamlChildPlacementKind.Content,
			XamlElementContentProjectionKind.Inlines =>
				ElementXamlChildPlacementKind.Inlines,
			XamlElementContentProjectionKind.Items =>
				ElementXamlChildPlacementKind.Items,
			_ => plan.ChildPlacement
		};
		if (expectedPlacement != plan.ChildPlacement)
		{
			failures.Add(
				$"Content/placement mismatch: {Identity(element)} "
				+ $"{plan.ContentProjection}/{plan.ChildPlacement}");
		}
	}

	private static IEnumerable<XamlElementObjectBuildNode> EnumerateObjectNodes(
		XamlElementObjectBuildNode root)
	{
		yield return root;
		foreach (var child in root.Children)
		{
			foreach (var descendant in EnumerateObjectNodes(child))
				yield return descendant;
		}
	}

	private static string Identity(DomElement element) =>
		$"{element.DocumentScope}::{element.XPath}";
}

public sealed record XamlObjectArchitectureAudit(
	int DomElementCount,
	int PrimaryObjectCount,
	int OwnedObjectCount,
	int BuiltObjectCount,
	int NonVisualElementCount,
	IReadOnlyList<XamlObjectArchitectureCount> XamlTypeCounts,
	IReadOnlyList<XamlObjectArchitectureCount> MappingKindCounts,
	IReadOnlyList<XamlObjectArchitectureCount> ContentProjectionCounts,
	IReadOnlyList<string> Failures,
	IReadOnlyList<XamlObjectRepresentative> Representatives);

public sealed record XamlObjectArchitectureCount(
	string Name,
	int Count);

public sealed record XamlLayerArchitectureSummary(
	IReadOnlyList<XamlLayerArchitectureEntry> VisibleElements,
	IReadOnlyList<string> ArchitectureFailures);

public sealed record XamlLayerArchitectureEntry(
	int HierarchyLevel,
	string Identity,
	string ParentIdentity,
	string DomType,
	string TagName,
	string QuerySpecialization,
	string MappingDeclarationType,
	string XamlType,
	string MappingKind,
	bool RequiresRuntimeLayoutContract,
	string MappingReason,
	string ContentProjection,
	string ChildPlacement,
	int DomChildCount,
	int XamlChildCount,
	string ActualXamlParentType,
	bool XamlIsLoaded,
	string XamlVisibility,
	string XamlHorizontalAlignment,
	string XamlVerticalAlignment,
	int XamlGridRow,
	int XamlGridColumn,
	string XamlTranslation,
	string DisplayInitialization,
	string DisplayRuntime,
	string PositionRuntime,
	string FlexDirectionRuntime,
	string FlexWrapRuntime,
	string WidthInitialization,
	string WidthRuntime,
	string HeightInitialization,
	string HeightRuntime,
	string MinWidthInitialization,
	string MinWidthRuntime,
	string MinHeightInitialization,
	string MinHeightRuntime,
	string BoxSizingRuntime,
	string MarginRuntime,
	string PaddingRuntime,
	string BorderRuntime,
	string AlignItemsRuntime,
	string JustifyContentRuntime,
	string DomX,
	string DomY,
	string DomWidth,
	string DomHeight,
	double XamlX,
	double XamlY,
	double XamlWidth,
	double XamlHeight,
	double XamlDeclaredWidth,
	double XamlDeclaredHeight,
	double XamlMinWidth,
	double XamlMinHeight,
	double XamlMaxWidth,
	double XamlMaxHeight,
	double XamlDesiredWidth,
	double XamlDesiredHeight,
	string XamlLayoutSlot,
	string XamlRowDefinitions,
	string XamlColumnDefinitions,
	string XamlBorderThickness,
	int RowDefinitionCount,
	int ColumnDefinitionCount,
	int StyleBindingCount,
	int LayoutBindingCount,
	int RegisteredXamlTargetCount,
	string StyleTargetSummary,
	int RegisteredLayoutTargetCount,
	int MaterializedLayoutTargetCount,
	IReadOnlyList<string> ArchitectureIssues);

public sealed record XamlLayerAuditSummary(
	IReadOnlyList<XamlLayerAuditLevel> Levels,
	IReadOnlyList<XamlLayerAuditFailure> FirstFailures);

public sealed record XamlLayerAuditLevel(
	int Depth,
	int ElementCount,
	int FailedElementCount,
	int PropertyCount,
	int FailedPropertyCount,
	int SpaceFailureCount,
	int LayoutFailureCount);

public sealed record XamlLayerAuditFailure(
	int Depth,
	string ElementIdentity,
	string PropertyName,
	string Category,
	string Slot,
	string Status,
	string DomEvidence,
	string XamlEvidence,
	string Description);

public sealed record XamlLayerAuditEntry(
	int Depth,
	DomElementAuditReport Report);

public sealed record XamlObjectRepresentative(
	string Identity,
	string DomType,
	string TagName,
	string? XamlType,
	XamlElementMappingKind? MappingKind,
	XamlElementContentProjectionKind? ContentProjection,
	ElementXamlChildPlacementKind? ChildPlacement,
	int DomChildCount,
	int XamlChildCount,
	int OwnedObjectCount,
	IReadOnlyList<string> InitializationAttributes);

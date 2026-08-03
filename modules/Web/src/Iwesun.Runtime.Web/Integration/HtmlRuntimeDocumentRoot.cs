using Iwesun.Runtime.WebView2;

namespace Iwesun.Runtime.Web;

public class HtmlRuntimeDocumentRoot : HtmlDocumentRoot
{
	private readonly SemaphoreSlim _gate = new(1, 1);
	private IReadOnlySet<string> _lastSourceElementIdentities =
		new HashSet<string>(StringComparer.Ordinal);
	private readonly HtmlRuntimeGlobalRelationshipOrchestrator
		_globalRelationshipOrchestrator = new();
	private readonly HtmlRuntimeGlobalDomEvidenceProcessor
		_globalDomEvidenceProcessor = new();

	public HtmlRuntimeDocumentRoot(
		string documentScope,
		HtmlRuntimeWebView2Context webView2,
		IHtmlRuntimePropertyConnection? cssConnection = null,
		IHtmlRuntimePropertyConnection? layoutConnection = null,
		IHtmlRuntimePropertyConnection? runtimeStateConnection = null,
		IHtmlRuntimeEventConnection? eventConnection = null)
		: base(documentScope)
	{
		ArgumentNullException.ThrowIfNull(webView2);
		WebView2 = webView2;
		CssConnection = cssConnection
			?? new HtmlRuntimeStyleConnection(webView2.DesignRuntime.Styles);
		LayoutConnection = layoutConnection
			?? new HtmlRuntimeLayoutConnection(webView2.DesignRuntime.Layout);
		RuntimeStateConnection = runtimeStateConnection
			?? new HtmlRuntimeStateConnection(webView2.DesignRuntime.State);
		EventConnection = eventConnection
			?? CreateDefaultEventConnection(webView2);
		CreatedAt = DateTimeOffset.UtcNow;
		LastStageChangedAt = CreatedAt;
	}

	public HtmlRuntimeWebView2Context WebView2 { get; }

	public HtmlRuntimeDesignRuntime DesignRuntime => WebView2.DesignRuntime;

	public Uri NavigationUrl => WebView2.NavigationUrl;

	public IReadOnlyList<HtmlRuntimeNavigationOperation> NavigationOperations =>
		WebView2.NavigationOperations;

	public override string? ResolveGlobalStyleValue(
		DomElement element,
		string propertyName)
	{
		var value = DesignRuntime.Styles.ResolveForXaml(
			element.DocumentScope,
			element.XPath,
			propertyName)?.ConcreteValue;
		return string.IsNullOrWhiteSpace(value) ? null : value;
	}

	internal override DomElementRuntimeProperty? ResolveGlobalStyleProperty(
		DomElement element,
		string propertyName) =>
		DesignRuntime.Styles.Find(
			element.DocumentScope,
			element.XPath,
			propertyName)?.GetSlotOwner();

	internal override IReadOnlyList<DomElementRuntimeProperty>
		ResolveGlobalStyleProperties(DomElement element) =>
		DomElementRuntimePropertyCatalog.Standard
			.Select(definition => DesignRuntime.Styles.Find(
				element.DocumentScope,
				element.XPath,
				definition.Name))
			.Where(static binding => binding is not null)
			.Select(static binding => binding!.GetSlotOwner())
			.ToArray();

	public override string? ResolveGlobalRuntimeStateValue(
		DomElement element,
		string propertyName)
	{
		var value = DesignRuntime.State.Find(
			element.DocumentScope,
			element.XPath,
			propertyName)?.Value;
		return string.IsNullOrWhiteSpace(value) ? null : value;
	}

	public override string? ResolveGlobalStyleValue(
		DomElement element,
		string propertyName,
		DomPropertyDataSlot slot)
	{
		var binding = DesignRuntime.Styles.Find(
			element.DocumentScope,
			element.XPath,
			propertyName);
		var value = slot switch
		{
			DomPropertyDataSlot.Initialization => binding?.Initialization?.Value,
			DomPropertyDataSlot.Runtime => binding?.Runtime?.Value,
			DomPropertyDataSlot.Link => binding?.Link?.Value,
			_ => throw new ArgumentOutOfRangeException(nameof(slot))
		};
		return string.IsNullOrWhiteSpace(value) ? null : value;
	}

	public IHtmlRuntimePropertyConnection CssConnection { get; }

	public IHtmlRuntimePropertyConnection LayoutConnection { get; }

	public IHtmlRuntimePropertyConnection RuntimeStateConnection { get; }

	public HtmlRuntimeAnimationManager AnimationConnection =>
		DesignRuntime.Animations;

	public IHtmlRuntimeEventConnection? EventConnection { get; }

	public IReadOnlyList<HtmlRuntimeEventEvidence> DocumentEventEvidence
	{
		get;
		private set;
	} = [];

	public IReadOnlyList<HtmlRuntimeEventEvidence> DeferredElementEventEvidence
	{
		get;
		private set;
	} = [];

	private static IHtmlRuntimeEventConnection? CreateDefaultEventConnection(
		HtmlRuntimeWebView2Context context)
	{
		if (context.Scripts is null
			|| context.DomTree is not WebRuntimeLiveDomTreeSession liveTree)
		{
			return null;
		}
		return new WebRuntimeInstrumentedEventConnection(
			context.Scripts,
			() => liveTree.LastSnapshot
				?? throw new InvalidOperationException(
					"The live DOM tree must be built before event discovery."));
	}

	public IReadOnlyList<HtmlRuntimeNavigationResult> NavigationResults
	{
		get;
		private set;
	} = [];

	public IReadOnlyList<DomElementFillResult> DomFillResults
	{
		get;
		private set;
	} = [];

	public HtmlRuntimeGlobalRelationshipGraph? GlobalRelationships
	{
		get;
		private set;
	}

	public long UpdateBusinessDataSource(
		string documentScope,
		string xpath,
		string sourceName,
		string value)
	{
		if (GlobalRelationships is null)
		{
			throw new InvalidOperationException(
				"Global relationships must be resolved before business data can be updated.");
		}
		return _globalRelationshipOrchestrator.UpdateBusinessInput(
			documentScope,
			xpath,
			sourceName,
			value);
	}

	public HtmlRuntimeXamlObjectTree? XamlObjectTree { get; private set; }

	public HtmlRuntimeXamlGlobalRelationshipGraph? XamlGlobalRelationships
	{
		get;
		private set;
	}

	public HtmlRuntimeXamlDisplayResult? DisplayResult { get; private set; }

	public IReadOnlyList<XamlElementFillResult> XamlFillResults
	{
		get;
		private set;
	} = [];

	public HtmlRuntimeXamlAuditResult? AuditResult { get; private set; }

	public IReadOnlyList<string> WrittenXamlPaths { get; private set; } = [];

	public long SourceDomTreeRevision { get; private set; }

	public DateTimeOffset SourceDomTreeCapturedAt { get; private set; }

	public int SourceDomTreeElementCount { get; private set; }

	public int SourceDomDocumentCount { get; private set; }

	public HtmlRuntimeDocumentStage Stage { get; private set; } =
		HtmlRuntimeDocumentStage.Created;

	public DateTimeOffset CreatedAt { get; }

	public DateTimeOffset LastStageChangedAt { get; private set; }

	public Exception? Failure { get; private set; }

	public async ValueTask<IReadOnlyList<HtmlRuntimeNavigationResult>>
		NavigateAsync(CancellationToken cancellationToken = default)
	{
		await EnterAsync(HtmlRuntimeDocumentStage.Created, cancellationToken);
		try
		{
			await WebView2.Navigator.NavigateAsync(
				NavigationUrl,
				cancellationToken);
			await WebView2.Navigator.WaitForStableDocumentAsync(cancellationToken);
			var results = new List<HtmlRuntimeNavigationResult>(
				NavigationOperations.Count);
			foreach (var operation in NavigationOperations)
			{
				try
				{
					await WebView2.Navigator.ClickAsync(operation, cancellationToken);
					if (operation.DelayAfter is { } delay && delay > TimeSpan.Zero)
						await Task.Delay(delay, cancellationToken);
					await WebView2.Navigator.WaitForStableDocumentAsync(
						cancellationToken);
					results.Add(new(
						operation,
						true,
						string.Empty,
						DateTimeOffset.UtcNow));
				}
				catch (Exception exception)
					when (!operation.Required
						&& exception is not OperationCanceledException)
				{
					results.Add(new(
						operation,
						false,
						exception.Message,
						DateTimeOffset.UtcNow));
				}
			}
			NavigationResults = results;
			ChangeStage(HtmlRuntimeDocumentStage.Navigated);
			return results;
		}
		catch (Exception exception)
		{
			Fail(exception);
			throw;
		}
		finally
		{
			_gate.Release();
		}
	}

	public async ValueTask<HtmlRuntimeElementTreeBuildResult> BuildAsync(
		CancellationToken cancellationToken = default)
	{
		await EnterAsync(HtmlRuntimeDocumentStage.Navigated, cancellationToken);
		try
		{
			var result = await BuildElementTreeAsync(cancellationToken);
			ValidateBuildResult(result);
			MountElementTree(result.HtmlRootElement, result.DocumentRoots);
			ChangeStage(HtmlRuntimeDocumentStage.Built);
			return result;
		}
		catch (Exception exception)
		{
			Fail(exception);
			throw;
		}
		finally
		{
			_gate.Release();
		}
	}

	public async ValueTask<IReadOnlyList<DomElementFillResult>> FillAsync(
		CancellationToken cancellationToken = default)
	{
		await EnterAsync(HtmlRuntimeDocumentStage.Built, cancellationToken);
		try
		{
			var activeElements = DocumentRoots
				.SelectMany(EnumeratePreOrder)
				.Where(element =>
					MaximumHierarchyLevel is not { } limit
					|| element.HierarchyLevel <= limit)
				.ToArray();
			BeginDomFillProgress(activeElements);
			await PrepareDomFillAsync(cancellationToken);
			var results = new List<DomElementFillResult>(DocumentRoots.Count);
			foreach (var root in DocumentRoots)
				results.Add(await root.DomFillAsync(cancellationToken));
			DomFillResults = results;
			ChangeStage(HtmlRuntimeDocumentStage.Filled);
			await _globalDomEvidenceProcessor.CaptureAsync(
				this,
				cancellationToken);
			await AttachEventEvidenceAsync(cancellationToken);
			GlobalRelationships = _globalRelationshipOrchestrator.ResolveDom(
				DocumentRoots,
				DesignRuntime);
			ChangeStage(HtmlRuntimeDocumentStage.GlobalRelationshipsResolved);
			return results;
		}
		catch (Exception exception)
		{
			Fail(exception);
			throw;
		}
		finally
		{
			_gate.Release();
		}
	}

	public async ValueTask<IReadOnlyList<DomElementFillResult>>
		RefreshRuntimeAsync(
			CancellationToken cancellationToken = default)
	{
		await EnterRuntimeRefreshAsync(cancellationToken);
		try
		{
			await ReprepareDomRuntimeAsync(cancellationToken);
			var results = new List<DomElementFillResult>(DocumentRoots.Count);
			foreach (var root in DocumentRoots)
			{
				results.Add(await root.RefreshDomRuntimeAsync(
					this,
					cancellationToken));
			}
			GlobalRelationships = _globalRelationshipOrchestrator.ResolveDom(
				DocumentRoots,
				DesignRuntime);
			return results;
		}
		catch (Exception exception)
		{
			Fail(exception);
			throw;
		}
		finally
		{
			_gate.Release();
		}
	}

	public HtmlRuntimeXamlObjectTree BuildXaml(
		IXamlElementObjectFactory factory)
	{
		ArgumentNullException.ThrowIfNull(factory);
		if (!_gate.Wait(0))
		{
			throw new InvalidOperationException(
				"Another HTML runtime stage is already executing.");
		}
		try
		{
			EnsureStage(HtmlRuntimeDocumentStage.GlobalRelationshipsResolved);
			var result = new HtmlRuntimeXamlObjectTree(
				BuildXamlObjectTrees(factory));
			XamlObjectTree = result;
			ChangeStage(HtmlRuntimeDocumentStage.XamlBuilt);
			XamlGlobalRelationships = _globalRelationshipOrchestrator.ComposeXaml(
				GlobalRelationships
					?? throw new InvalidOperationException(
						"Global DOM relationships have not been resolved."),
				DesignRuntime);
			if (factory is IXamlGlobalRelationshipBinder relationshipBinder)
				relationshipBinder.BindGlobalRelationships(XamlGlobalRelationships);
			ChangeStage(
				HtmlRuntimeDocumentStage.XamlGlobalRelationshipsComposed);
			return result;
		}
		catch (Exception exception)
		{
			Fail(exception);
			throw;
		}
		finally
		{
			_gate.Release();
		}
	}

	public HtmlRuntimeXamlObjectTree RebuildXaml(
		IXamlElementObjectFactory factory)
	{
		ArgumentNullException.ThrowIfNull(factory);
		if (!_gate.Wait(0))
		{
			throw new InvalidOperationException(
				"Another HTML runtime stage is already executing.");
		}
		try
		{
			if (Stage < HtmlRuntimeDocumentStage.Filled
				|| Stage == HtmlRuntimeDocumentStage.Failed)
			{
				throw new InvalidOperationException(
					"XAML projection rebuild requires a filled or later live "
						+ $"stage, but is {Stage}.");
			}
			foreach (var root in DocumentRoots)
				root.ResetXamlObjectProjection();
			DesignRuntime.BeginXamlMaterialization();
			var result = new HtmlRuntimeXamlObjectTree(
				BuildXamlObjectTrees(factory));
			XamlObjectTree = result;
			XamlGlobalRelationships = _globalRelationshipOrchestrator.ComposeXaml(
				GlobalRelationships
					?? throw new InvalidOperationException(
						"Global DOM relationships have not been resolved."),
				DesignRuntime);
			if (factory is IXamlGlobalRelationshipBinder relationshipBinder)
				relationshipBinder.BindGlobalRelationships(XamlGlobalRelationships);
			return result;
		}
		catch (Exception exception)
		{
			Fail(exception);
			throw;
		}
		finally
		{
			_gate.Release();
		}
	}

	public async ValueTask<HtmlRuntimeXamlDisplayResult> DisplayXamlAsync(
		IHtmlRuntimeXamlPresenter presenter,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(presenter);
		await EnterAsync(
			HtmlRuntimeDocumentStage.XamlGlobalRelationshipsComposed,
			cancellationToken);
		try
		{
			var result = await presenter.DisplayAsync(
				XamlObjectTree
					?? throw new InvalidOperationException(
						"The XAML object tree has not been built."),
				cancellationToken);
			ArgumentNullException.ThrowIfNull(result.DisplayIdentity);
			DisplayResult = result;
			ChangeStage(HtmlRuntimeDocumentStage.XamlDisplayed);
			return result;
		}
		catch (Exception exception)
		{
			Fail(exception);
			throw;
		}
		finally
		{
			_gate.Release();
		}
	}

	public async ValueTask<HtmlRuntimeXamlAuditResult> AuditXamlAsync(
		IXamlPropertyQueryEngine queryEngine,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(queryEngine);
		await FillXamlAsync(queryEngine, cancellationToken);
		return AuditXaml();
	}

	public async ValueTask<IReadOnlyList<XamlElementFillResult>> FillXamlAsync(
		IXamlPropertyQueryEngine queryEngine,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(queryEngine);
		await EnterAsync(HtmlRuntimeDocumentStage.XamlDisplayed, cancellationToken);
		try
		{
			var fills = new List<XamlElementFillResult>(DocumentRoots.Count);
			foreach (var root in DocumentRoots)
				fills.Add(await root.XamlFillAsync(queryEngine, cancellationToken));
			XamlFillResults = fills;
			ChangeStage(HtmlRuntimeDocumentStage.XamlFilled);
			return fills;
		}
		catch (Exception exception)
		{
			Fail(exception);
			throw;
		}
		finally
		{
			_gate.Release();
		}
	}

	public HtmlRuntimeXamlAuditResult AuditXaml()
	{
		if (!_gate.Wait(0))
		{
			throw new InvalidOperationException(
				"Another HTML runtime stage is already executing.");
		}
		try
		{
			EnsureStage(HtmlRuntimeDocumentStage.XamlFilled);
			var reports = DocumentRoots
				.Select(static root => root.Audit())
				.ToArray();
			var reconciliation = EndToEndReconciliationAuditor.Audit(
				DocumentRoots,
				DomFillResults,
				XamlFillResults);
			var result = new HtmlRuntimeXamlAuditResult(
				XamlFillResults,
				reports,
				reconciliation);
			AuditResult = result;
			ChangeStage(HtmlRuntimeDocumentStage.Audited);
			return result;
		}
		catch (Exception exception)
		{
			Fail(exception);
			throw;
		}
		finally
		{
			_gate.Release();
		}
	}

	public async ValueTask<IReadOnlyList<string>> WriteXamlAsync(
		IHtmlRuntimeXamlDocumentWriter writer,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(writer);
		await _gate.WaitAsync(cancellationToken);
		try
		{
			EnsureStage(HtmlRuntimeDocumentStage.Audited);
			if (AuditResult is not { Passed: true })
			{
				throw new InvalidOperationException(
					"XAML export is blocked because end-to-end reconciliation failed.");
			}
			var documents = DocumentRoots
				.Select(static root => new HtmlRuntimeXamlDocument(
					root.DocumentScope,
					root.XPath,
					root.ToXaml(new StringWriter())))
				.ToArray();
			var paths = await writer.WriteAsync(documents, cancellationToken);
			if (paths.Count != documents.Length
				|| paths.Any(string.IsNullOrWhiteSpace))
			{
				throw new InvalidDataException(
					"The XAML writer did not persist every document.");
			}
			WrittenXamlPaths = paths.ToArray();
			return WrittenXamlPaths;
		}
		catch (Exception exception)
		{
			Fail(exception);
			throw;
		}
		finally
		{
			_gate.Release();
		}
	}

	protected virtual async ValueTask<HtmlRuntimeElementTreeBuildResult>
		BuildElementTreeAsync(CancellationToken cancellationToken)
	{
		var snapshot = await WebView2.DomTree.ReadDomTreeAsync(cancellationToken);
		SourceDomTreeRevision = snapshot.Revision;
		SourceDomTreeCapturedAt = snapshot.CapturedAt;
		SourceDomTreeElementCount = snapshot.Elements.Count;
		SourceDomDocumentCount = snapshot.Documents.Count;
		if (!HasSameOrigin(snapshot.PageUri, NavigationUrl))
		{
			throw new InvalidDataException(
				"The DOM tree snapshot does not belong to the navigated origin.");
		}
		_lastSourceElementIdentities = snapshot.Elements
			.Select(node => node.DocumentScope.Equals(
				DocumentScope,
				StringComparison.Ordinal)
					? node.XPath
					: $"{node.DocumentScope}::{node.XPath}")
			.ToHashSet(StringComparer.Ordinal);
		var tree = DomElementTreeBuilder.Build(
			DocumentScope,
			snapshot.Elements.Select(static node =>
				new DomElementConstructionNode(
					node.DocumentScope,
					node.XPath,
					node.TagName,
					node.ParentXPath,
					node.ChildXPaths,
					node.LeftSiblingXPath,
					node.RightSiblingXPath)
				{
					AttributeNames = node.AttributeNames,
					AttributeValues = node.AttributeValues,
					NodeId = node.NodeId,
					BackendNodeId = node.BackendNodeId,
					OwnText = node.OwnText,
					OwnTextElementInsertionIndex =
						node.OwnTextElementInsertionIndex,
					TextContent = node.TextContent
				}).ToArray(),
			MaximumHierarchyLevel);
		return new(tree.HtmlRootElement, tree.DocumentRoots);
	}

	private static bool HasSameOrigin(Uri actual, Uri expected) =>
		actual.IsAbsoluteUri
		&& expected.IsAbsoluteUri
		&& actual.Scheme.Equals(expected.Scheme, StringComparison.OrdinalIgnoreCase)
		&& actual.Host.Equals(expected.Host, StringComparison.OrdinalIgnoreCase)
		&& actual.Port == expected.Port;

	protected sealed override async ValueTask<
		IReadOnlyList<DomPropertyQueryResult>>
		QueryDomPropertiesAsync(
			IReadOnlyList<DomPropertyQueryContext> contexts,
			CancellationToken cancellationToken)
	{
		IReadOnlyList<DomPropertyQueryResult> direct;
		if (WebView2.UseSinglePropertyQueries)
		{
			if (WebView2.DomQueries
				is not IWebRuntimeSingleDomPropertyQuerySession single)
			{
				throw new InvalidOperationException(
					"The configured DOM query session has no single-property API.");
			}
			var individual = new List<DomPropertyQueryResult>(contexts.Count);
			for (var index = 0; index < contexts.Count; index++)
			{
				individual.Add(await WebRuntimeDomQueryBridge.QueryAsync(
					single,
					contexts[index],
					index.ToString(
						System.Globalization.CultureInfo.InvariantCulture),
					cancellationToken));
			}
			direct = individual;
		}
		else
		{
			direct = await WebRuntimeDomQueryBridge.QueryAsync(
				WebView2.DomQueries,
				contexts,
				cancellationToken);
		}
		var results = direct.ToArray();
		for (var index = 0; index < contexts.Count; index++)
		{
			var context = contexts[index];
			var connection = ResolvePropertyConnection(context);
			if (connection is not { IsConnected: true })
				continue;
			var shouldQueryConnection =
				context.Slot == DomPropertyDataSlot.Link
				|| results[index].Status
					== DomPropertyQueryStatus.SourceUnsupported
				|| context.Slot == DomPropertyDataSlot.Initialization
					&& context.PropertyName.StartsWith(
						"style.",
						StringComparison.Ordinal);
			if (!shouldQueryConnection)
				continue;
			var connected = await connection.QueryAsync(
				context,
				cancellationToken);
			if (connected.Status == DomPropertyQueryStatus.Captured
				|| context.Slot == DomPropertyDataSlot.Link
					&& connected.Status
						== DomPropertyQueryStatus.ConfirmedAbsent
					&& results[index].Status
						!= DomPropertyQueryStatus.Captured
				|| results[index].Status
					== DomPropertyQueryStatus.SourceUnsupported)
			{
				results[index] = connected;
			}
		}
		for (var index = 0; index < contexts.Count; index++)
			DesignRuntime.Observe(contexts[index], results[index]);
		return results;
	}

	private IHtmlRuntimePropertyConnection? ResolvePropertyConnection(
		DomPropertyQueryContext context)
	{
		if (context.PropertyName.Equals(
			"service.globalLayout",
			StringComparison.Ordinal))
		{
			return LayoutConnection;
		}
		if (context.PropertyName.Equals(
			"service.globalStyle",
			StringComparison.Ordinal))
		{
			return CssConnection;
		}
		if (context.PropertyName.StartsWith("rect.", StringComparison.Ordinal)
			|| context.EvidenceKind.HasFlag(
				ElementEvidenceKind.DomRuntimeGeometry))
		{
			return LayoutConnection;
		}
		if (context.PropertyName.StartsWith("style.", StringComparison.Ordinal))
			return CssConnection;
		if (context.EvidenceKind.HasFlag(ElementEvidenceKind.FormState)
			|| context.EvidenceKind.HasFlag(ElementEvidenceKind.ScrollState))
		{
			return RuntimeStateConnection;
		}
		return null;
	}

	private async ValueTask AttachEventEvidenceAsync(
		CancellationToken cancellationToken)
	{
		if (EventConnection is not { IsConnected: true } connection)
			return;
		var elements = DocumentRoots
			.SelectMany(EnumeratePreOrder)
			.ToDictionary(
				ElementIdentity,
				StringComparer.Ordinal);
		var evidence = await connection.ReadAsync(cancellationToken);
		var duplicates = evidence
			.GroupBy(
				static item =>
					$"{item.DocumentScope}::{item.XPath}/{item.PropertyName}",
				StringComparer.Ordinal)
			.Where(static group => group.Count() != 1)
			.Select(static group => group.Key)
			.ToArray();
		if (duplicates.Length != 0)
		{
			throw new InvalidDataException(
				"Runtime event evidence contains duplicate element/property identities.");
		}
		var documentEvents = new List<HtmlRuntimeEventEvidence>();
		var deferredElementEvents = new List<HtmlRuntimeEventEvidence>();
		foreach (var item in evidence)
		{
			if (item.XPath.StartsWith("/@", StringComparison.Ordinal))
			{
				documentEvents.Add(item);
				continue;
			}
			var identity = item.DocumentScope.Equals(
				DocumentScope,
				StringComparison.Ordinal)
					? item.XPath
					: $"{item.DocumentScope}::{item.XPath}";
			if (!elements.TryGetValue(identity, out var element))
			{
				if (MaximumHierarchyLevel is not null
					&& _lastSourceElementIdentities.Contains(identity))
				{
					deferredElementEvents.Add(item);
					continue;
				}
				throw new InvalidDataException(
					$"Runtime event target '{identity}' is not in the built DOM tree.");
			}
			var normalized = item.EventName.Replace(
				"-",
				string.Empty,
				StringComparison.Ordinal);
			var kind = item.EventName.Equals(
				"dblclick",
				StringComparison.OrdinalIgnoreCase)
					? DomEventKind.DoubleClick
					: Enum.TryParse<DomEventKind>(
						normalized,
						ignoreCase: true,
						out var parsed)
							? parsed
							: DomEventKind.Custom;
			element.Events.Add(new CapturedDomElementEvent(
				item.PropertyName,
				kind,
				item.EventName));
		}
		DocumentEventEvidence = documentEvents;
		DeferredElementEventEvidence = deferredElementEvents;
	}

	private async ValueTask EnterAsync(
		HtmlRuntimeDocumentStage required,
		CancellationToken cancellationToken)
	{
		await _gate.WaitAsync(cancellationToken);
		if (Stage != required)
		{
			_gate.Release();
			throw new InvalidOperationException(
				$"HTML runtime stage must be {required}, but is {Stage}.");
		}
	}

	private async ValueTask EnterRuntimeRefreshAsync(
		CancellationToken cancellationToken)
	{
		await _gate.WaitAsync(cancellationToken);
		if (Stage < HtmlRuntimeDocumentStage.Filled
			|| Stage == HtmlRuntimeDocumentStage.Failed)
		{
			_gate.Release();
			throw new InvalidOperationException(
				"HTML runtime refresh requires a filled or later live stage, "
					+ $"but is {Stage}.");
		}
	}

	private void EnsureStage(HtmlRuntimeDocumentStage required)
	{
		if (Stage != required)
		{
			throw new InvalidOperationException(
				$"HTML runtime stage must be {required}, but is {Stage}.");
		}
	}

	private void ChangeStage(HtmlRuntimeDocumentStage stage)
	{
		Stage = stage;
		LastStageChangedAt = DateTimeOffset.UtcNow;
	}

	private void Fail(Exception exception)
	{
		Failure = exception;
		ChangeStage(HtmlRuntimeDocumentStage.Failed);
	}

	private static void ValidateBuildResult(
		HtmlRuntimeElementTreeBuildResult result)
	{
		ArgumentNullException.ThrowIfNull(result);
		if (result.DocumentRoots.Count == 0
			|| !result.DocumentRoots
				.SelectMany(EnumeratePreOrder)
				.Contains(
					result.HtmlRootElement,
					ReferenceEqualityComparer.Instance))
		{
			throw new InvalidDataException(
				"The HTML root element must belong to the built document trees.");
		}
	}

	private static IEnumerable<DomElement> EnumeratePreOrder(DomElement root)
	{
		yield return root;
		foreach (var child in root.Children)
		{
			foreach (var descendant in EnumeratePreOrder(child))
				yield return descendant;
		}
	}

	private string ElementIdentity(DomElement element) =>
		element.DocumentScope.Equals(DocumentScope, StringComparison.Ordinal)
			? element.XPath
			: $"{element.DocumentScope}::{element.XPath}";
}

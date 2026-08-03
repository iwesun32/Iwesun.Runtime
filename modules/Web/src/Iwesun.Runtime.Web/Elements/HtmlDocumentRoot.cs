namespace Iwesun.Runtime.Web;

using Iwesun.Runtime.Diagnostics;

public sealed record DomEvidenceSnapshotEntry
{
	public DomEvidenceSnapshotEntry(
		DomPropertyQueryContext context,
		DomPropertyQueryResult result)
		: this(null, context, result)
	{
	}

	internal DomEvidenceSnapshotEntry(
		IDomFillSlotOwner? owner,
		DomPropertyQueryContext context,
		DomPropertyQueryResult result)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(result);
		Owner = owner;
		Context = context;
		Result = result;
	}

	public DomPropertyQueryContext Context { get; }

	public DomPropertyQueryResult Result { get; }

	internal IDomFillSlotOwner? Owner { get; }
}

public sealed class DomEvidenceSnapshot
{
	private readonly IReadOnlyDictionary<
		DomPropertyQueryContext,
		DomPropertyQueryResult> _results;
	private readonly IReadOnlyDictionary<
		DomElement,
		IReadOnlyList<DomEvidenceSnapshotEntry>> _entriesByElement;

	internal DomEvidenceSnapshot(
		long revision,
		DateTimeOffset capturedAt,
		IReadOnlyList<DomElementFillRequest> requests,
		IReadOnlyList<DomPropertyQueryResult> results)
	{
		if (revision <= 0)
			throw new ArgumentOutOfRangeException(nameof(revision));
		ArgumentNullException.ThrowIfNull(requests);
		ArgumentNullException.ThrowIfNull(results);
		if (requests.Count != results.Count)
		{
			throw new ArgumentException(
				"DOM evidence requests and results must have equal lengths.");
		}
		Revision = revision;
		CapturedAt = capturedAt;
		Entries = requests
			.Select((request, index) =>
				new DomEvidenceSnapshotEntry(
					request.Owner,
					request.Context,
					results[index]))
			.ToArray();
		_results = Entries.ToDictionary(
			static entry => entry.Context,
			static entry => entry.Result,
			DomPropertyQueryContextIdentityComparer.Instance);
		_entriesByElement = Entries
			.GroupBy(
				static entry => entry.Context.Element,
				(IEqualityComparer<DomElement>)ReferenceEqualityComparer.Instance)
			.ToDictionary(
				static group => group.Key,
				static group =>
					(IReadOnlyList<DomEvidenceSnapshotEntry>)group.ToArray(),
				(IEqualityComparer<DomElement>)ReferenceEqualityComparer.Instance);
	}

	public long Revision { get; }

	public DateTimeOffset CapturedAt { get; }

	public IReadOnlyList<DomEvidenceSnapshotEntry> Entries { get; }

	public int Count => Entries.Count;

	internal DomPropertyQueryResult Read(DomPropertyQueryContext context) =>
		_results.TryGetValue(context, out var result)
			? result
			: throw new InvalidDataException(
				$"DOM evidence revision {Revision} is missing "
					+ $"{context.DocumentScope}::{context.XPath}/"
					+ $"{context.ReflectedPropertyName}/"
					+ $"{context.PropertyName}/{context.Slot}.");

	internal IReadOnlyList<DomEvidenceSnapshotEntry> ReadElement(
		DomElement element)
	{
		ArgumentNullException.ThrowIfNull(element);
		return _entriesByElement.TryGetValue(element, out var entries)
			? entries
			: throw new InvalidDataException(
				$"DOM evidence revision {Revision} contains no slots for "
					+ $"{element.DocumentScope}::{element.XPath}.");
	}

}

internal sealed class DomPropertyQueryContextIdentityComparer
	: IEqualityComparer<DomPropertyQueryContext>
{
	internal static DomPropertyQueryContextIdentityComparer Instance { get; } =
		new();

	public bool Equals(
		DomPropertyQueryContext? left,
		DomPropertyQueryContext? right) =>
		ReferenceEquals(left, right)
		|| (left is not null
			&& right is not null
			&& ReferenceEquals(left.Element, right.Element)
			&& left.ReflectedPropertyName.Equals(
				right.ReflectedPropertyName,
				StringComparison.Ordinal)
			&& left.PropertyName.Equals(
				right.PropertyName,
				StringComparison.Ordinal)
			&& left.Slot == right.Slot);

	public int GetHashCode(DomPropertyQueryContext context)
	{
		ArgumentNullException.ThrowIfNull(context);
		var hash = new HashCode();
		hash.Add(
			System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(
				context.Element));
		hash.Add(context.ReflectedPropertyName, StringComparer.Ordinal);
		hash.Add(context.PropertyName, StringComparer.Ordinal);
		hash.Add(context.Slot);
		return hash.ToHashCode();
	}
}

public sealed record HtmlElementTraversalProgress(
	string Operation,
	int ProcessedElements,
	int TotalElements,
	int CurrentHierarchyLevel,
	int MaximumHierarchyLevel,
	string DocumentScope,
	string XPath)
{
	public double Percentage =>
		TotalElements == 0
			? 100
			: ProcessedElements * 100d / TotalElements;
}

/// <summary>
/// Owns one live HTML document reconstruction session and provides the
/// runtime DOM access used directly by every mounted element.
/// </summary>
public abstract class HtmlDocumentRoot : IDomPropertyQueryEngine
{
	private readonly List<DomElement> _documentRoots = [];
	private int? _maximumHierarchyLevel;
	private long _nextDomEvidenceRevision;
	private int _domFillProcessedElements;

	protected HtmlDocumentRoot(string documentScope)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(documentScope);
		DocumentScope = documentScope;
	}

	public string DocumentScope { get; }

	public DomElement? HtmlRootElement { get; private set; }

	public IReadOnlyList<DomElement> DocumentRoots => _documentRoots;

	public bool IsElementTreeMounted => HtmlRootElement is not null;

	public DomEvidenceSnapshot? EvidenceSnapshot { get; private set; }

	/// <summary>
	/// Resolves document-owned CSS evidence for a mounted element without
	/// dynamically adding CSS names to the element's static property contract.
	/// </summary>
	public virtual string? ResolveGlobalStyleValue(
		DomElement element,
		string propertyName)
	{
		ArgumentNullException.ThrowIfNull(element);
		ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
		return null;
	}

	public virtual string? ResolveGlobalStyleValue(
		DomElement element,
		string propertyName,
		DomPropertyDataSlot slot) =>
		ResolveGlobalStyleValue(element, propertyName);

	internal virtual DomElementRuntimeProperty? ResolveGlobalStyleProperty(
		DomElement element,
		string propertyName)
	{
		ArgumentNullException.ThrowIfNull(element);
		ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
		return null;
	}

	internal virtual IReadOnlyList<DomElementRuntimeProperty>
		ResolveGlobalStyleProperties(DomElement element)
	{
		ArgumentNullException.ThrowIfNull(element);
		return [];
	}

	/// <summary>
	/// Resolves document-owned live form/scroll state without adding the
	/// browser-wide runtime-state catalog to every concrete element type.
	/// </summary>
	public virtual string? ResolveGlobalRuntimeStateValue(
		DomElement element,
		string propertyName)
	{
		ArgumentNullException.ThrowIfNull(element);
		ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
		return null;
	}

	public HtmlElementTraversalProgress? DomFillProgress { get; private set; }

	/// <summary>
	/// Gets or sets the inclusive maximum DOM hierarchy level processed by
	/// this HTML root. A null value means unlimited depth.
	/// </summary>
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
			_maximumHierarchyLevel = value;
		}
	}

	public void MountElementTree(
		DomElement htmlRootElement,
		IEnumerable<DomElement> documentRoots)
	{
		ArgumentNullException.ThrowIfNull(htmlRootElement);
		ArgumentNullException.ThrowIfNull(documentRoots);
		if (IsElementTreeMounted)
		{
			throw new InvalidOperationException(
				"The HTML document root already owns an element tree.");
		}
		var roots = documentRoots.ToArray();
		if (roots.Length == 0
			|| !roots.SelectMany(EnumeratePreOrder).Contains(
				htmlRootElement,
				ReferenceEqualityComparer.Instance))
		{
			throw new InvalidDataException(
				"The HTML root element must belong to the mounted tree.");
		}
		foreach (var root in roots)
		{
			if (MaximumHierarchyLevel is { } limit)
				root.MaximumHierarchyLevel = limit;
			root.AttachHtmlRoot(this);
		}
		_documentRoots.AddRange(roots);
		HtmlRootElement = htmlRootElement;
	}

	public ValueTask<DomPropertyQueryResult> QueryAsync(
		DomPropertyQueryContext context,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(context);
		return QuerySingleAsync(context, cancellationToken);
	}

	public ValueTask<IReadOnlyList<DomPropertyQueryResult>> QueryManyAsync(
		IReadOnlyList<DomPropertyQueryContext> contexts,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(contexts);
		foreach (var context in contexts)
		{
			ArgumentNullException.ThrowIfNull(context);
			ValidateOwnership(context);
		}
		if (EvidenceSnapshot is not null)
		{
			IReadOnlyList<DomPropertyQueryResult> prepared = contexts
				.Select(EvidenceSnapshot.Read)
				.ToArray();
			return ValueTask.FromResult(prepared);
		}
		return QueryDomPropertiesAsync(contexts, cancellationToken);
	}

	public ValueTask PrepareDomFillAsync(
		CancellationToken cancellationToken = default) =>
		PrepareDomEvidenceAsync(
			runtimeRefreshOnly: false,
			cancellationToken);

	private async ValueTask PrepareDomEvidenceAsync(
		bool runtimeRefreshOnly,
		CancellationToken cancellationToken)
	{
		if (!IsElementTreeMounted)
		{
			throw new InvalidOperationException(
				"An element tree must be mounted before DOM Fill preparation.");
		}
		if (EvidenceSnapshot is not null)
		{
			throw new InvalidOperationException(
				"DOM Fill has already been prepared for this HTML root.");
		}
		var activeElements = _documentRoots
			.SelectMany(EnumerateActivePreOrder)
			.ToArray();
		var requestBuffer = new List<DomElementFillRequest>();
		ReportDomFillPhaseProgress(
			runtimeRefreshOnly
				? "PrepareRuntimeRequests"
				: "PrepareRequests",
			0,
			activeElements.Length,
			null);
		for (var index = 0; index < activeElements.Length; index++)
		{
			var element = activeElements[index];
			requestBuffer.AddRange(
				element.CreateDomFillRequests()
					.Where(request =>
						!runtimeRefreshOnly
							|| request.Context.Slot is
								DomPropertyDataSlot.Link
								or DomPropertyDataSlot.Runtime));
			ReportDomFillPhaseProgress(
				runtimeRefreshOnly
					? "PrepareRuntimeRequests"
					: "PrepareRequests",
				index + 1,
				activeElements.Length,
				element);
		}
		var requests = requestBuffer.ToArray();
		var contexts = requests
			.Select(static request => request.Context)
			.ToArray();
		var uniqueContexts = new HashSet<DomPropertyQueryContext>(
			DomPropertyQueryContextIdentityComparer.Instance);
		if (contexts.Any(context => !uniqueContexts.Add(context)))
		{
			throw new InvalidDataException(
				"The mounted tree produced duplicate DOM query identities.");
		}
		var prepared = new DomPropertyQueryResult?[contexts.Length];
		var remoteContexts = new List<DomPropertyQueryContext>(contexts.Length);
		var remoteIndexes = new List<int>(contexts.Length);
		for (var index = 0; index < contexts.Length; index++)
		{
			if (TryReadTreeInitialization(contexts[index], out var local))
			{
				prepared[index] = local;
				continue;
			}
			remoteIndexes.Add(index);
			remoteContexts.Add(contexts[index]);
		}
		ReportDomFillPhaseProgress(
			runtimeRefreshOnly
				? "CaptureRuntimeEvidence"
				: "CaptureEvidence",
			0,
			activeElements.Length,
			null);
		var remoteResults = await QueryDomPropertiesAsync(
			remoteContexts,
			cancellationToken);
		ReportDomFillPhaseProgress(
			runtimeRefreshOnly
				? "CaptureRuntimeEvidence"
				: "CaptureEvidence",
			activeElements.Length,
			activeElements.Length,
			activeElements.LastOrDefault());
		if (remoteResults.Count != remoteContexts.Count)
		{
			throw new InvalidDataException(
				$"The HTML root returned {remoteResults.Count} results for "
				+ $"{remoteContexts.Count} remote DOM evidence queries.");
		}
		for (var index = 0; index < remoteIndexes.Count; index++)
			prepared[remoteIndexes[index]] = remoteResults[index];
		var results = prepared
			.Select(static result =>
				result ?? throw new InvalidDataException(
					"A prepared DOM evidence slot was not materialized."))
			.ToArray();
		EvidenceSnapshot = new DomEvidenceSnapshot(
			Interlocked.Increment(ref _nextDomEvidenceRevision),
			DateTimeOffset.UtcNow,
			requests,
			results);
	}

	private static bool TryReadTreeInitialization(
		DomPropertyQueryContext context,
		out DomPropertyQueryResult? result)
	{
		if (context.Slot != DomPropertyDataSlot.Initialization)
		{
			result = null;
			return false;
		}
		var value = context.PropertyName switch
		{
			"content.ownText" => context.Element.CapturedOwnText,
			"content.textContent" => context.Element.CapturedTextContent,
			_ => null
		};
		if (value is null)
		{
			result = null;
			return false;
		}
		result = string.IsNullOrWhiteSpace(value)
			? DomPropertyQueryResult.ConfirmedAbsent(
				"The same-revision DOM tree contains no text for this slot.")
			: DomPropertyQueryResult.DirectConstant(
				value,
				"Text captured with the same-revision typed DOM tree.");
		return true;
	}

	public async ValueTask ReprepareDomFillAsync(
		CancellationToken cancellationToken = default)
	{
		if (!IsElementTreeMounted)
		{
			throw new InvalidOperationException(
				"An element tree must be mounted before DOM Fill preparation.");
		}
		EvidenceSnapshot = null;
		await PrepareDomFillAsync(cancellationToken);
	}

	public async ValueTask ReprepareDomRuntimeAsync(
		CancellationToken cancellationToken = default)
	{
		if (!IsElementTreeMounted)
		{
			throw new InvalidOperationException(
				"An element tree must be mounted before DOM runtime refresh.");
		}
		EvidenceSnapshot = null;
		_domFillProcessedElements = 0;
		await PrepareDomEvidenceAsync(
			runtimeRefreshOnly: true,
			cancellationToken);
	}

	public IReadOnlyList<XamlElementObjectBuildResult>
		BuildXamlObjectTrees(IXamlElementObjectFactory factory)
	{
		ArgumentNullException.ThrowIfNull(factory);
		if (!IsElementTreeMounted)
		{
			throw new InvalidOperationException(
				"An element tree must be mounted before XAML object construction.");
		}
		var results = _documentRoots
			.Select(root => root.BuildXamlObjectTree(factory))
			.ToArray();
		AttachFrameDocumentRoots(factory, results);
		return results;
	}

	private void AttachFrameDocumentRoots(
		IXamlElementObjectFactory factory,
		IReadOnlyList<XamlElementObjectBuildResult> results)
	{
		var builtNodes =
			new Dictionary<DomElement, XamlElementObjectBuildNode>(
				ReferenceEqualityComparer.Instance);
		foreach (var node in results
			.SelectMany(static result => result.Roots)
			.SelectMany(EnumerateBuildNodes))
		{
			if (node.Plan.SourceElement is { } source)
				builtNodes.Add(source, node);
		}
		for (var index = 0; index < _documentRoots.Count; index++)
		{
			var documentRoot = _documentRoots[index];
			if (documentRoot.DocumentScope.Equals(
				DocumentScope,
				StringComparison.Ordinal))
			{
				continue;
			}
			var frameOwners =
				documentRoot.EmbeddingOwner is { } directOwner
				&& builtNodes.TryGetValue(directOwner, out var directOwnerNode)
					? [directOwnerNode]
					: builtNodes
						.Where(pair =>
							pair.Key.TagName.Equals(
								"iframe",
								StringComparison.OrdinalIgnoreCase)
							&& ElementIdentity(pair.Key).Equals(
								documentRoot.DocumentScope,
								StringComparison.Ordinal))
						.Select(static pair => pair.Value)
						.ToArray();
			if (frameOwners.Length != 1
				|| results[index].Roots.Count != 1)
			{
				throw new InvalidDataException(
					$"Frame document scope '{documentRoot.DocumentScope}' "
					+ "does not have one unambiguous XAML attachment point.");
			}
			var frameOwner = frameOwners[0];
			var frameRoot = results[index].Roots[0];
			factory.AttachChild(
				new(
					frameOwner.Element,
					frameRoot.Element,
					frameOwner.Plan,
					frameRoot.Plan,
					frameOwner.Plan.ChildPlacement));
		}
	}

	protected abstract ValueTask<IReadOnlyList<DomPropertyQueryResult>>
		QueryDomPropertiesAsync(
			IReadOnlyList<DomPropertyQueryContext> contexts,
			CancellationToken cancellationToken);

	private async ValueTask<DomPropertyQueryResult> QuerySingleAsync(
		DomPropertyQueryContext context,
		CancellationToken cancellationToken)
	{
		var results = await QueryManyAsync(
			[context],
			cancellationToken);
		if (results.Count != 1)
		{
			throw new InvalidDataException(
				"The HTML root query API did not return exactly one result.");
		}
		return results[0];
	}

	private void ValidateOwnership(DomPropertyQueryContext context)
	{
		if (!ReferenceEquals(context.Element.HtmlRoot, this))
		{
			throw new InvalidOperationException(
				"The queried element is not mounted on this HTML root.");
		}
	}

	private static IEnumerable<DomElement> EnumeratePreOrder(
		DomElement root)
	{
		yield return root;
		foreach (var child in root.Children)
		{
			foreach (var descendant in EnumeratePreOrder(child))
				yield return descendant;
		}
	}

	private IEnumerable<DomElement> EnumerateActivePreOrder(
		DomElement root)
	{
		if (MaximumHierarchyLevel is { } limit
			&& root.HierarchyLevel > limit)
		{
			yield break;
		}
		yield return root;
		if (MaximumHierarchyLevel is { } activeLimit
			&& root.HierarchyLevel >= activeLimit)
		{
			yield break;
		}
		foreach (var child in root.Children)
		{
			foreach (var descendant in EnumerateActivePreOrder(child))
				yield return descendant;
		}
	}

	internal void ReportDomFillElementCompleted(DomElement element)
	{
		ArgumentNullException.ThrowIfNull(element);
		var previous = DomFillProgress;
		if (previous is null)
		{
			_domFillProcessedElements = 0;
			previous = new(
				"DomFillOwn",
				0,
				1,
				0,
				element.HierarchyLevel,
				string.Empty,
				string.Empty);
		}
		var current = Interlocked.Increment(ref _domFillProcessedElements);
		DomFillProgress = previous with
		{
			Operation = "ApplySlots",
			ProcessedElements = current,
			CurrentHierarchyLevel = element.HierarchyLevel,
			DocumentScope = element.DocumentScope,
			XPath = element.XPath
		};
		TraceDomFillProgress(DomFillProgress);
	}

	protected void BeginDomFillProgress(
		IReadOnlyList<DomElement> activeElements)
	{
		ArgumentNullException.ThrowIfNull(activeElements);
		_domFillProcessedElements = 0;
		DomFillProgress = new(
			"Start",
			0,
			activeElements.Count,
			0,
			activeElements.Count == 0
				? 0
				: activeElements.Max(static element =>
					element.HierarchyLevel),
			string.Empty,
			string.Empty);
		TraceDomFillProgress(DomFillProgress);
	}

	private void ReportDomFillPhaseProgress(
		string operation,
		int processedElements,
		int totalElements,
		DomElement? element)
	{
		var maximumLevel = DomFillProgress?.MaximumHierarchyLevel
			?? (element?.HierarchyLevel ?? 0);
		DomFillProgress = new(
			operation,
			processedElements,
			totalElements,
			element?.HierarchyLevel ?? 0,
			maximumLevel,
			element?.DocumentScope ?? string.Empty,
			element?.XPath ?? string.Empty);
		TraceDomFillProgress(DomFillProgress);
	}

	private static void TraceDomFillProgress(
		HtmlElementTraversalProgress progress)
	{
		if (!RuntimeOutput.Enabled)
			return;
		RuntimeOutput.TracePoint(
			"log.pipeline",
			"pipeline",
			"log.html-reconstruction.fill-progress",
			$"DOM Fill [{progress.Operation}] "
				+ $"{progress.ProcessedElements}/"
				+ $"{progress.TotalElements} "
				+ $"L{progress.CurrentHierarchyLevel}/"
				+ $"{progress.MaximumHierarchyLevel} "
				+ $"{progress.Percentage:F2}%",
			progress);
	}

	private static IEnumerable<XamlElementObjectBuildNode> EnumerateBuildNodes(
		XamlElementObjectBuildNode root)
	{
		yield return root;
		foreach (var child in root.Children)
		{
			foreach (var descendant in EnumerateBuildNodes(child))
				yield return descendant;
		}
	}

	private string ElementIdentity(DomElement element) =>
		element.DocumentScope.Equals(DocumentScope, StringComparison.Ordinal)
			? element.XPath
			: $"{element.DocumentScope}::{element.XPath}";
}

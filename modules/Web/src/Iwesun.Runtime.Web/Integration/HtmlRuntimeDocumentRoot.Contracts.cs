using Iwesun.Runtime.WebView2;

namespace Iwesun.Runtime.Web;

public enum HtmlRuntimeDocumentStage
{
	Created,
	Navigated,
	Built,
	Filled,
	GlobalRelationshipsResolved,
	XamlBuilt,
	XamlGlobalRelationshipsComposed,
	XamlDisplayed,
	XamlFilled,
	Audited,
	Failed
}

public sealed record HtmlRuntimeNavigationOperation(
	string Name,
	string DocumentScope,
	string XPath,
	bool Required = true,
	TimeSpan? DelayAfter = null);

public sealed record HtmlRuntimeNavigationResult(
	HtmlRuntimeNavigationOperation Operation,
	bool Success,
	string Error,
	DateTimeOffset CompletedAt);

public interface IHtmlRuntimeNavigator
{
	ValueTask NavigateAsync(
		Uri url,
		CancellationToken cancellationToken = default);

	ValueTask WaitForStableDocumentAsync(
		CancellationToken cancellationToken = default);

	ValueTask ClickAsync(
		HtmlRuntimeNavigationOperation operation,
		CancellationToken cancellationToken = default);
}

public sealed class HtmlRuntimeWebView2Context : IAsyncDisposable
{
	private readonly IAsyncDisposable? _lifetime;

	public HtmlRuntimeWebView2Context(
		IWebRuntimeDomTreeSession domTree,
		IWebRuntimeDomQuerySession domQueries,
		IHtmlRuntimeNavigator navigator,
		Uri navigationUrl,
		IWebRuntimeScriptSession? scripts = null,
		IReadOnlyList<HtmlRuntimeNavigationOperation>? navigationOperations = null,
		bool useSinglePropertyQueries = false,
		HtmlRuntimeDesignRuntime? designRuntime = null,
		HtmlRuntimeEvidenceTransport evidenceTransport =
			HtmlRuntimeEvidenceTransport.ExplicitSessions,
		WebRuntimeCdpDomAccess? cdpDomAccess = null,
		IAsyncDisposable? lifetime = null)
	{
		ArgumentNullException.ThrowIfNull(domTree);
		ArgumentNullException.ThrowIfNull(domQueries);
		ArgumentNullException.ThrowIfNull(navigator);
		ArgumentNullException.ThrowIfNull(navigationUrl);
		if (!navigationUrl.IsAbsoluteUri)
			throw new ArgumentException("Navigation URL must be absolute.", nameof(navigationUrl));
		DomTree = domTree;
		DomQueries = domQueries;
		Navigator = navigator;
		NavigationUrl = navigationUrl;
		DesignRuntime = designRuntime ?? new HtmlRuntimeDesignRuntime();
		Scripts = scripts;
		UseSinglePropertyQueries = useSinglePropertyQueries;
		EvidenceTransport = evidenceTransport;
		CdpDomAccess = cdpDomAccess;
		_lifetime = lifetime;
		NavigationOperations = navigationOperations?.ToArray() ?? [];
		ValidateNavigationOperations(NavigationOperations);
	}

	public IWebRuntimeDomTreeSession DomTree { get; }

	public IWebRuntimeDomQuerySession DomQueries { get; }

	public IWebRuntimeScriptSession? Scripts { get; }

	public bool UseSinglePropertyQueries { get; }

	public HtmlRuntimeEvidenceTransport EvidenceTransport { get; }

	public WebRuntimeCdpDomAccess? CdpDomAccess { get; }

	public IHtmlRuntimeNavigator Navigator { get; }

	public Uri NavigationUrl { get; }

	public HtmlRuntimeDesignRuntime DesignRuntime { get; }

	public IReadOnlyList<HtmlRuntimeNavigationOperation> NavigationOperations
	{
		get;
	}

	public ValueTask DisposeAsync() =>
		_lifetime?.DisposeAsync() ?? ValueTask.CompletedTask;

	/// <summary>
	/// Creates the formal live reconstruction context. DOM construction and all
	/// reflected property evidence are acquired through CDP only.
	/// </summary>
	public static HtmlRuntimeWebView2Context CreateCdp(
		IWebRuntimeDevToolsSession devTools,
		IHtmlRuntimeNavigator navigator,
		Uri navigationUrl,
		IReadOnlyList<HtmlRuntimeNavigationOperation>? navigationOperations = null)
	{
		ArgumentNullException.ThrowIfNull(devTools);
		var designRuntime = new HtmlRuntimeDesignRuntime();
		var treeSession = new WebRuntimeTrackedCdpDomTreeSession(devTools);
		WebRuntimeDomTreeSnapshot? pinnedEvidenceTree = null;
		WebRuntimeDomTreeSnapshot CurrentTree()
		{
			var pinned = Volatile.Read(ref pinnedEvidenceTree);
			if (pinned is not null)
				return pinned;
			var complete = treeSession.Current?.Snapshot
				?? treeSession.LastCompleteSnapshot
				?? throw new InvalidOperationException(
					"The CDP DOM tree must be read before DOM Fill.");
			return Interlocked.CompareExchange(
				ref pinnedEvidenceTree,
				complete,
				null) ?? complete;
		}
		var querySession = new WebRuntimeLiveDomQuerySession(
			new WebRuntimeCdpDomEvidenceReader(devTools, CurrentTree),
			retainQueryIndex: false);
		var domAccess = new WebRuntimeCdpDomAccess(devTools, treeSession);
		return new(
			treeSession,
			querySession,
			navigator,
			navigationUrl,
			scripts: null,
			navigationOperations,
			useSinglePropertyQueries: false,
			designRuntime,
			HtmlRuntimeEvidenceTransport.Cdp,
			domAccess,
			treeSession);
	}

	private static void ValidateNavigationOperations(
		IReadOnlyList<HtmlRuntimeNavigationOperation> operations)
	{
		if (operations.Any(static operation =>
			string.IsNullOrWhiteSpace(operation.Name)
				|| string.IsNullOrWhiteSpace(operation.DocumentScope)
				|| string.IsNullOrWhiteSpace(operation.XPath)
				|| !operation.XPath.StartsWith("/", StringComparison.Ordinal))
			|| operations
				.GroupBy(static operation => operation.Name, StringComparer.Ordinal)
				.Any(static group => group.Count() != 1))
		{
			throw new ArgumentException(
				"Navigation operations require unique names, a document scope, "
				+ "and an absolute DOM XPath.",
				nameof(operations));
		}
	}
}

public enum HtmlRuntimeEvidenceTransport
{
	ExplicitSessions,
	Cdp
}

public interface IHtmlRuntimePropertyConnection
{
	string Name { get; }

	ElementEvidenceKind EvidenceKind { get; }

	bool IsConnected { get; }

	ValueTask<DomPropertyQueryResult> QueryAsync(
		DomPropertyQueryContext context,
		CancellationToken cancellationToken = default);
}

public sealed record HtmlRuntimeEventEvidence(
	string DocumentScope,
	string XPath,
	string EventName,
	string PropertyName,
	string HandlerIdentity,
	string Description);

public interface IHtmlRuntimeEventConnection
{
	string Name { get; }

	bool IsConnected { get; }

	ValueTask<IReadOnlyList<HtmlRuntimeEventEvidence>> ReadAsync(
		CancellationToken cancellationToken = default);
}

public sealed record HtmlRuntimeElementTreeBuildResult(
	DomElement HtmlRootElement,
	IReadOnlyList<DomElement> DocumentRoots);

public sealed record HtmlRuntimeXamlObjectTree(
	IReadOnlyList<XamlElementObjectBuildResult> Documents);

public sealed record HtmlRuntimeXamlDisplayResult(
	object DisplayIdentity,
	DateTimeOffset DisplayedAt);

public interface IHtmlRuntimeXamlPresenter
{
	ValueTask<HtmlRuntimeXamlDisplayResult> DisplayAsync(
		HtmlRuntimeXamlObjectTree tree,
		CancellationToken cancellationToken = default);
}

public sealed record HtmlRuntimeXamlDocument(
	string DocumentScope,
	string RootXPath,
	string Xaml);

public interface IHtmlRuntimeXamlDocumentWriter
{
	ValueTask<IReadOnlyList<string>> WriteAsync(
		IReadOnlyList<HtmlRuntimeXamlDocument> documents,
		CancellationToken cancellationToken = default);
}

public sealed record HtmlRuntimeXamlAuditResult(
	IReadOnlyList<XamlElementFillResult> FillResults,
	IReadOnlyList<DomElementAuditReport> Reports,
	EndToEndReconciliationReport Reconciliation)
{
	public bool Passed => Reconciliation.Passed;
}

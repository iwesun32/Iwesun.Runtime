namespace Iwesun.Runtime.WebView2;

public sealed record WebRuntimeDomDocumentScope(
	string DocumentScope,
	Uri DocumentUri,
	int? FrameIndex);

public sealed record WebRuntimeDomTreeSnapshot(
	long Revision,
	Uri PageUri,
	DateTimeOffset CapturedAt,
	IReadOnlyList<WebRuntimeDomTreeElement> Elements,
	IReadOnlyList<WebRuntimeDomDocumentScope> Documents);

public sealed record WebRuntimeDomTreeElement(
	string DocumentScope,
	string XPath,
	string TagName,
	string? ParentXPath,
	IReadOnlyList<string> ChildXPaths,
	string? LeftSiblingXPath,
	string? RightSiblingXPath)
{
	public IReadOnlyList<string> AttributeNames { get; init; } = [];
	public IReadOnlyDictionary<string, string> AttributeValues { get; init; } =
		new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
	public int NodeId { get; init; }
	public int BackendNodeId { get; init; }
	public string OwnText { get; init; } = string.Empty;
	public int OwnTextElementInsertionIndex { get; init; } = -1;
	public string TextContent { get; init; } = string.Empty;
}

public interface IWebRuntimeDomTreeSession
{
	ValueTask<WebRuntimeDomTreeSnapshot> ReadDomTreeAsync(
		CancellationToken cancellationToken = default);
}

public interface IWebRuntimeDomTreeReader
{
	ValueTask<WebRuntimeDomTreeSnapshot> ReadAsync(
		CancellationToken cancellationToken = default);
}

public sealed class WebRuntimeLiveDomTreeSession(
	IWebRuntimeDomTreeReader reader) : IWebRuntimeDomTreeSession
{
	private readonly IWebRuntimeDomTreeReader _reader =
		reader ?? throw new ArgumentNullException(nameof(reader));

	public WebRuntimeDomTreeSnapshot? LastSnapshot { get; private set; }

	public async ValueTask<WebRuntimeDomTreeSnapshot> ReadDomTreeAsync(
		CancellationToken cancellationToken = default)
	{
		var snapshot = await _reader.ReadAsync(cancellationToken);
		Validate(snapshot);
		LastSnapshot = snapshot;
		return snapshot;
	}

	private static void Validate(WebRuntimeDomTreeSnapshot snapshot)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		if (snapshot.Revision <= 0
			|| !snapshot.PageUri.IsAbsoluteUri
			|| snapshot.CapturedAt == default
			|| snapshot.Elements.Count == 0
			|| snapshot.Documents.Count == 0
			|| snapshot.Documents[0].FrameIndex is not null)
		{
			throw new InvalidDataException(
				"The live DOM tree snapshot is incomplete.");
		}
		var identities = snapshot.Elements.ToDictionary(static element =>
			(element.DocumentScope, element.XPath));
		foreach (var element in snapshot.Elements)
		{
			if (element.ParentXPath is not null
				&& !identities.ContainsKey(
					(element.DocumentScope, element.ParentXPath)))
			{
				throw new InvalidDataException(
					$"DOM parent is missing for {element.DocumentScope}::"
					+ element.XPath);
			}
			if (element.ChildXPaths.Any(child =>
				!identities.ContainsKey((element.DocumentScope, child))))
			{
				throw new InvalidDataException(
					$"DOM child is missing for {element.DocumentScope}::"
					+ element.XPath);
			}
		}
	}
}

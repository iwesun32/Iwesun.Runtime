using Iwesun.Runtime.WebView2;

namespace Iwesun.Runtime.Web;

/// <summary>
/// Captures document-wide CSS, layout, and visual-effect evidence after the
/// strongly typed element slots have been filled. These values belong to the
/// HTML document graph; they never mutate an element type's static property
/// contract.
/// </summary>
internal sealed class HtmlRuntimeGlobalDomEvidenceProcessor
{
	public async ValueTask CaptureAsync(
		HtmlRuntimeDocumentRoot root,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(root);
		var elements = root.DocumentRoots
			.SelectMany(EnumeratePreOrder)
			.Where(element => root.MaximumHierarchyLevel is not { } limit
				|| element.HierarchyLevel <= limit)
			.ToArray();
		var requests = elements
			.SelectMany(CreateRequests)
			.Select((request, index) => request with
			{
				QueryId = $"global:{index}"
			})
			.ToArray();
		var elementsByIdentity = elements.ToDictionary(
			static element => (element.DocumentScope, element.XPath));
		if (requests.Length == 0)
			return;
		var results = await root.WebView2.DomQueries.QueryDomPropertiesAsync(
			requests,
			cancellationToken).ConfigureAwait(false);
		var resultsById = results.ToDictionary(
			static result => result.QueryId,
			StringComparer.Ordinal);
		if (resultsById.Count != requests.Length)
		{
			throw new InvalidDataException(
				"The global DOM evidence batch is incomplete or has duplicate identities.");
		}
		foreach (var request in requests)
		{
			if (!resultsById.Remove(request.QueryId, out var result))
			{
				throw new InvalidDataException(
					$"The global DOM evidence batch omitted {request.QueryId}.");
			}
			var element = elementsByIdentity[(
				request.DocumentScope,
				request.XPath)];
			result = await ResolveConnectedEvidenceAsync(
				root,
				element,
				request,
				result,
				cancellationToken).ConfigureAwait(false);
			root.DesignRuntime.Observe(new(
				WebRuntimeDomPropertyIdentity.From(request),
				result.Status,
				result.Value,
				result.ValueSource,
				result.LinkKind,
				result.LinkIdentity,
				result.Description));
		}
	}

	private static async ValueTask<WebRuntimeDomPropertyResult>
		ResolveConnectedEvidenceAsync(
			HtmlRuntimeDocumentRoot root,
			DomElement element,
			WebRuntimeDomPropertyRequest request,
			WebRuntimeDomPropertyResult direct,
			CancellationToken cancellationToken)
	{
		if (!request.PropertyName.StartsWith("style.", StringComparison.Ordinal)
			|| !root.CssConnection.IsConnected)
		{
			return direct;
		}
		var context = new DomPropertyQueryContext(
			request.DocumentScope,
			request.XPath,
			request.TagName,
			nameof(DomElement.GlobalStyleService),
			request.PropertyName,
			ElementSlotOwnerKind.RuntimeProperty,
			(ElementSlotCategory)(int)request.Category,
			(ElementEvidenceKind)(int)request.EvidenceKind,
			(DomPropertyDataSlot)(int)request.Slot,
			element);
		var connected = await root.CssConnection.QueryAsync(
			context,
			cancellationToken).ConfigureAwait(false);
		if (connected.Status != DomPropertyQueryStatus.Captured)
			return direct;
		return new(
			direct.QueryId,
			WebRuntimeDomPropertyStatus.Captured,
			connected.Value ?? string.Empty,
			(WebRuntimeDomValueSource)(int)connected.ValueSource,
			(WebRuntimeDomLinkKind)(int)connected.Link.Kind,
			connected.Link.Description ?? string.Empty,
			connected.Description ?? string.Empty);
	}

	private static IEnumerable<WebRuntimeDomPropertyRequest> CreateRequests(
		DomElement element)
	{
		foreach (var definition in DomElementRuntimePropertyCatalog.Standard)
		{
			if (!IsGlobalEvidence(definition))
				continue;
			foreach (var slot in GetSlots(definition))
			{
				yield return new(
					string.Empty,
					element.DocumentScope,
					element.XPath,
					element.TagName,
					definition.Name,
					nameof(HtmlRuntimeGlobalDomEvidenceProcessor),
					WebRuntimeDomOwnerKind.RuntimeProperty,
					(WebRuntimeDomSlotCategory)(int)definition.Category,
					(WebRuntimeDomEvidenceKind)(int)definition.EvidenceKind,
					slot,
					(WebRuntimeDomElementSpecialization)(int)
						element.QuerySpecialization,
					element.NodeId,
					element.BackendNodeId,
					element.HierarchyLevel);
			}
		}
	}

	private static bool IsGlobalEvidence(
		DomElementRuntimePropertyDefinition definition) =>
		definition.Category is ElementSlotCategory.Style
			or ElementSlotCategory.Effect;

	private static IReadOnlyList<WebRuntimeDomDataSlot> GetSlots(
		DomElementRuntimePropertyDefinition definition) =>
		[
				WebRuntimeDomDataSlot.Initialization,
				WebRuntimeDomDataSlot.Link,
				WebRuntimeDomDataSlot.Runtime
			];

	private static IEnumerable<DomElement> EnumeratePreOrder(DomElement root)
	{
		yield return root;
		foreach (var child in root.Children)
		{
			foreach (var descendant in EnumeratePreOrder(child))
				yield return descendant;
		}
	}
}

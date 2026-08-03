using Iwesun.Runtime.WebView2;

namespace Iwesun.Runtime.Web;

public static class WebRuntimeDomQueryBridge
{
	public static async ValueTask<IReadOnlyList<DomPropertyQueryResult>>
		QueryAsync(
			IWebRuntimeDomQuerySession session,
			IReadOnlyList<DomPropertyQueryContext> contexts,
			CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(contexts);
		var requests = contexts
			.Select((context, index) => CreateRequest(index, context))
			.ToArray();
		var runtimeResults = await session.QueryDomPropertiesAsync(
			requests,
			cancellationToken);
		if (runtimeResults.Count != requests.Length)
		{
			throw new InvalidDataException(
				$"Runtime WebView2 returned {runtimeResults.Count} results for "
					+ $"{requests.Length} DOM queries.");
		}
		var results = new DomPropertyQueryResult[requests.Length];
		for (var index = 0; index < requests.Length; index++)
		{
			var runtimeResult = runtimeResults[index];
			if (!runtimeResult.QueryId.Equals(
				requests[index].QueryId,
				StringComparison.Ordinal))
			{
				throw new InvalidDataException(
					$"Runtime WebView2 result {index} has query id "
						+ $"'{runtimeResult.QueryId}' instead of "
						+ $"'{requests[index].QueryId}'.");
			}
			results[index] = ConvertResult(runtimeResult);
		}
		return results;
	}

	public static async ValueTask<DomPropertyQueryResult> QueryAsync(
		IWebRuntimeSingleDomPropertyQuerySession session,
		DomPropertyQueryContext context,
		string queryId,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(context);
		ArgumentException.ThrowIfNullOrWhiteSpace(queryId);
		var result = await session.QueryDomPropertyAsync(
			CreateRequest(queryId, context),
			cancellationToken);
		if (!result.QueryId.Equals(queryId, StringComparison.Ordinal))
		{
			throw new InvalidDataException(
				"The direct DOM query returned a different query identity.");
		}
		return ConvertResult(result);
	}

	private static WebRuntimeDomPropertyRequest CreateRequest(
		int index,
		DomPropertyQueryContext context) =>
		CreateRequest(
			index.ToString(System.Globalization.CultureInfo.InvariantCulture),
			context);

	private static WebRuntimeDomPropertyRequest CreateRequest(
		string queryId,
		DomPropertyQueryContext context) =>
		new(
			queryId,
			context.DocumentScope,
			context.XPath,
			context.TagName,
			context.PropertyName,
			context.ReflectedPropertyName,
			ConvertOwnerKind(context.OwnerKind),
			ConvertCategory(context.Category),
			(WebRuntimeDomEvidenceKind)(int)context.EvidenceKind,
			ConvertSlot(context.Slot),
			(WebRuntimeDomElementSpecialization)(int)context.Specialization,
			context.Element.NodeId,
			context.Element.BackendNodeId,
			context.Element.HierarchyLevel);

	private static DomPropertyQueryResult ConvertResult(
		WebRuntimeDomPropertyResult result) =>
		result.Status switch
		{
			WebRuntimeDomPropertyStatus.Captured =>
				DomPropertyQueryResult.Captured(
					result.Value,
					ConvertValueSource(result.ValueSource),
					CreateLink(result),
					result.Description),
			WebRuntimeDomPropertyStatus.ConfirmedAbsent =>
				DomPropertyQueryResult.ConfirmedAbsent(result.Description),
			WebRuntimeDomPropertyStatus.SourceUnsupported =>
				DomPropertyQueryResult.SourceUnsupported(result.Description),
			_ => throw new InvalidDataException(
				$"Unsupported Runtime WebView2 DOM status {result.Status}.")
		};

	private static ElementPropertyLink CreateLink(
		WebRuntimeDomPropertyResult result)
	{
		var kind = result.LinkKind switch
		{
			WebRuntimeDomLinkKind.None => ElementPropertyLinkKind.None,
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
				$"Unsupported Runtime WebView2 link kind {result.LinkKind}.")
		};
		return kind == ElementPropertyLinkKind.None
			? ElementPropertyLink.None
			: new(kind, result.LinkIdentity);
	}

	private static WebRuntimeDomOwnerKind ConvertOwnerKind(
		ElementSlotOwnerKind ownerKind) =>
		ownerKind switch
		{
			ElementSlotOwnerKind.Property => WebRuntimeDomOwnerKind.Property,
			ElementSlotOwnerKind.Attribute => WebRuntimeDomOwnerKind.Attribute,
			ElementSlotOwnerKind.ExtensionAttribute =>
				WebRuntimeDomOwnerKind.ExtensionAttribute,
			ElementSlotOwnerKind.RuntimeProperty =>
				WebRuntimeDomOwnerKind.RuntimeProperty,
			ElementSlotOwnerKind.DataSource => WebRuntimeDomOwnerKind.DataSource,
			ElementSlotOwnerKind.Event => WebRuntimeDomOwnerKind.Event,
			_ => throw new InvalidDataException(
				$"Unsupported DOM owner kind {ownerKind}.")
		};

	private static WebRuntimeDomSlotCategory ConvertCategory(
		ElementSlotCategory category) =>
		category switch
		{
			ElementSlotCategory.Unspecified =>
				WebRuntimeDomSlotCategory.Unspecified,
			ElementSlotCategory.Space => WebRuntimeDomSlotCategory.Space,
			ElementSlotCategory.Style => WebRuntimeDomSlotCategory.Style,
			ElementSlotCategory.Effect => WebRuntimeDomSlotCategory.Effect,
			ElementSlotCategory.Action => WebRuntimeDomSlotCategory.Action,
			ElementSlotCategory.DataOrganization =>
				WebRuntimeDomSlotCategory.DataOrganization,
			_ => throw new InvalidDataException(
				$"Unsupported DOM slot category {category}.")
		};

	private static WebRuntimeDomDataSlot ConvertSlot(
		DomPropertyDataSlot slot) =>
		slot switch
		{
			DomPropertyDataSlot.Initialization =>
				WebRuntimeDomDataSlot.Initialization,
			DomPropertyDataSlot.Link => WebRuntimeDomDataSlot.Link,
			DomPropertyDataSlot.Runtime => WebRuntimeDomDataSlot.Runtime,
			_ => throw new InvalidDataException(
				$"Unsupported DOM data slot {slot}.")
		};

	private static ElementPropertyValueSource ConvertValueSource(
		WebRuntimeDomValueSource source) =>
		source switch
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
				$"Unsupported DOM value source {source}.")
		};
}

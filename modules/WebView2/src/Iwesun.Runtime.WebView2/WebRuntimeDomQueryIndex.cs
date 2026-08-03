using System.Collections.Frozen;

namespace Iwesun.Runtime.WebView2;

public readonly record struct WebRuntimeDomPropertyIdentity
{
	public WebRuntimeDomPropertyIdentity(
		string documentScope,
		string xpath,
		string tagName,
		string propertyName,
		string reflectedPropertyName,
		WebRuntimeDomOwnerKind ownerKind,
		WebRuntimeDomSlotCategory category,
		WebRuntimeDomEvidenceKind evidenceKind,
		WebRuntimeDomDataSlot slot)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(documentScope);
		ArgumentException.ThrowIfNullOrWhiteSpace(xpath);
		ArgumentException.ThrowIfNullOrWhiteSpace(tagName);
		ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
		ArgumentException.ThrowIfNullOrWhiteSpace(reflectedPropertyName);
		if (!xpath.StartsWith("/", StringComparison.Ordinal))
			throw new ArgumentException("XPath must be absolute.", nameof(xpath));
		DocumentScope = documentScope;
		XPath = xpath;
		TagName = tagName;
		PropertyName = propertyName;
		ReflectedPropertyName = reflectedPropertyName;
		OwnerKind = ownerKind;
		Category = category;
		EvidenceKind = evidenceKind;
		Slot = slot;
	}

	public string DocumentScope { get; }

	public string XPath { get; }

	public string TagName { get; }

	public string PropertyName { get; }

	public string ReflectedPropertyName { get; }

	public WebRuntimeDomOwnerKind OwnerKind { get; }

	public WebRuntimeDomSlotCategory Category { get; }

	public WebRuntimeDomEvidenceKind EvidenceKind { get; }

	public WebRuntimeDomDataSlot Slot { get; }

	public static WebRuntimeDomPropertyIdentity From(
		WebRuntimeDomPropertyRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		return new(
			request.DocumentScope,
			request.XPath,
			request.TagName,
			request.PropertyName,
			request.ReflectedPropertyName,
			request.OwnerKind,
			request.Category,
			request.EvidenceKind,
			request.Slot);
	}
}

public sealed record WebRuntimeDomIndexedProperty
{
	public WebRuntimeDomIndexedProperty(
		WebRuntimeDomPropertyIdentity identity,
		WebRuntimeDomPropertyStatus status,
		string value,
		WebRuntimeDomValueSource valueSource,
		WebRuntimeDomLinkKind linkKind,
		string linkIdentity,
		string description)
	{
		ArgumentNullException.ThrowIfNull(value);
		ArgumentNullException.ThrowIfNull(linkIdentity);
		ArgumentNullException.ThrowIfNull(description);
		if (status == WebRuntimeDomPropertyStatus.Captured)
		{
			if (valueSource == WebRuntimeDomValueSource.Unspecified)
			{
				throw new ArgumentException(
					"Captured DOM properties require a value source.",
					nameof(valueSource));
			}
			if (linkKind != WebRuntimeDomLinkKind.None
				&& string.IsNullOrWhiteSpace(linkIdentity))
			{
				throw new ArgumentException(
					"Linked DOM properties require a link identity.",
					nameof(linkIdentity));
			}
		}
		else if (value.Length != 0
			|| valueSource != WebRuntimeDomValueSource.Unspecified
			|| linkKind != WebRuntimeDomLinkKind.None
			|| linkIdentity.Length != 0)
		{
			throw new ArgumentException(
				"Absent or unsupported DOM properties cannot carry values or links.");
		}
		Identity = identity;
		Status = status;
		Value = value;
		ValueSource = valueSource;
		LinkKind = linkKind;
		LinkIdentity = linkIdentity;
		Description = description;
	}

	public WebRuntimeDomPropertyIdentity Identity { get; }

	public WebRuntimeDomPropertyStatus Status { get; }

	public string Value { get; }

	public WebRuntimeDomValueSource ValueSource { get; }

	public WebRuntimeDomLinkKind LinkKind { get; }

	public string LinkIdentity { get; }

	public string Description { get; }
}

public sealed class WebRuntimeDomQueryIndex
{
	private readonly FrozenDictionary<
		WebRuntimeDomPropertyIdentity,
		WebRuntimeDomIndexedProperty> _properties;

	public WebRuntimeDomQueryIndex(
		long revision,
		Uri pageUrl,
		DateTimeOffset capturedAt,
		IEnumerable<WebRuntimeDomIndexedProperty> properties)
	{
		if (revision <= 0)
			throw new ArgumentOutOfRangeException(nameof(revision));
		ArgumentNullException.ThrowIfNull(pageUrl);
		ArgumentNullException.ThrowIfNull(properties);
		if (!pageUrl.IsAbsoluteUri)
			throw new ArgumentException(
				"Page URL must be absolute.",
				nameof(pageUrl));
		var materialized = properties.ToArray();
		var duplicates = materialized
			.GroupBy(static property => property.Identity)
			.Where(static group => group.Count() > 1)
			.Select(static group => group.Key)
			.ToArray();
		if (duplicates.Length != 0)
		{
			throw new InvalidDataException(
				"The DOM query index contains duplicate property identities.");
		}
		Revision = revision;
		PageUrl = pageUrl;
		CapturedAt = capturedAt;
		_properties = materialized.ToFrozenDictionary(
			static property => property.Identity);
	}

	public long Revision { get; }

	public Uri PageUrl { get; }

	public DateTimeOffset CapturedAt { get; }

	public int PropertyCount => _properties.Count;

	internal bool TryGet(
		WebRuntimeDomPropertyIdentity identity,
		out WebRuntimeDomIndexedProperty property) =>
		_properties.TryGetValue(identity, out property!);
}

public sealed class WebRuntimeIndexedDomQuerySession
	: IWebRuntimeDomQuerySession
{
	private readonly object _replaceGate = new();
	private WebRuntimeDomQueryIndex _index;

	public WebRuntimeIndexedDomQuerySession(WebRuntimeDomQueryIndex index)
	{
		ArgumentNullException.ThrowIfNull(index);
		_index = index;
	}

	public WebRuntimeDomQueryIndex CurrentIndex =>
		Volatile.Read(ref _index);

	public void ReplaceIndex(WebRuntimeDomQueryIndex index)
	{
		ArgumentNullException.ThrowIfNull(index);
		lock (_replaceGate)
		{
			if (index.Revision <= _index.Revision)
			{
				throw new InvalidOperationException(
					"DOM query index revisions must increase.");
			}
			Volatile.Write(ref _index, index);
		}
	}

	public ValueTask<IReadOnlyList<WebRuntimeDomPropertyResult>>
		QueryDomPropertiesAsync(
			IReadOnlyList<WebRuntimeDomPropertyRequest> requests,
			CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(requests);
		var duplicateQueryIds = requests
			.GroupBy(static request => request.QueryId, StringComparer.Ordinal)
			.Where(static group => group.Count() > 1)
			.Select(static group => group.Key)
			.ToArray();
		if (requests.Any(static request =>
				string.IsNullOrWhiteSpace(request.QueryId))
			|| duplicateQueryIds.Length != 0)
		{
			throw new ArgumentException(
				"DOM query IDs must be non-empty and unique.",
				nameof(requests));
		}
		var snapshot = CurrentIndex;
		var results = new WebRuntimeDomPropertyResult[requests.Count];
		for (var index = 0; index < requests.Count; index++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var request = requests[index];
			var identity = WebRuntimeDomPropertyIdentity.From(request);
			if (!snapshot.TryGet(identity, out var property))
			{
				throw new InvalidDataException(
					$"DOM index revision {snapshot.Revision} is missing "
					+ $"{request.DocumentScope}::{request.XPath}/"
					+ $"{request.ReflectedPropertyName}/"
					+ $"{request.PropertyName}/{request.Slot}.");
			}
			results[index] = new(
				request.QueryId,
				property.Status,
				property.Value,
				property.ValueSource,
				property.LinkKind,
				property.LinkIdentity,
				property.Description);
		}
		return ValueTask.FromResult<IReadOnlyList<WebRuntimeDomPropertyResult>>(
			results);
	}
}

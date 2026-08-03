namespace Iwesun.Runtime.WebView2;

/// <summary>
/// Builds a complete immutable DOM query index from an explicit whole-tree
/// request plan. Every planned identity must receive exactly one result.
/// </summary>
public sealed class WebRuntimeDomQueryIndexBuilder
{
	private readonly Dictionary<
		WebRuntimeDomPropertyIdentity,
		WebRuntimeDomIndexedProperty?> _properties;

	public WebRuntimeDomQueryIndexBuilder(
		long revision,
		Uri pageUrl,
		DateTimeOffset capturedAt,
		IEnumerable<WebRuntimeDomPropertyRequest> requests)
	{
		if (revision <= 0)
			throw new ArgumentOutOfRangeException(nameof(revision));
		ArgumentNullException.ThrowIfNull(pageUrl);
		ArgumentNullException.ThrowIfNull(requests);
		if (!pageUrl.IsAbsoluteUri)
			throw new ArgumentException(
				"Page URL must be absolute.",
				nameof(pageUrl));
		var identities = requests
			.Select(WebRuntimeDomPropertyIdentity.From)
			.ToArray();
		var duplicates = identities
			.GroupBy(static identity => identity)
			.Where(static group => group.Count() > 1)
			.Select(static group => group.Key)
			.ToArray();
		if (duplicates.Length != 0)
		{
			throw new InvalidDataException(
				"The DOM query plan contains duplicate property identities.");
		}
		Revision = revision;
		PageUrl = pageUrl;
		CapturedAt = capturedAt;
		_properties = identities.ToDictionary(
			static identity => identity,
			static _ => (WebRuntimeDomIndexedProperty?)null);
	}

	public long Revision { get; }

	public Uri PageUrl { get; }

	public DateTimeOffset CapturedAt { get; }

	public int PlannedCount => _properties.Count;

	public int CompletedCount => _properties.Count(
		static item => item.Value is not null);

	public int PendingCount => PlannedCount - CompletedCount;

	public void SetCaptured(
		WebRuntimeDomPropertyIdentity identity,
		string value,
		WebRuntimeDomValueSource valueSource,
		WebRuntimeDomLinkKind linkKind = WebRuntimeDomLinkKind.None,
		string linkIdentity = "",
		string description = "") =>
		Set(new(
			identity,
			WebRuntimeDomPropertyStatus.Captured,
			value,
			valueSource,
			linkKind,
			linkIdentity,
			description));

	public void SetConfirmedAbsent(
		WebRuntimeDomPropertyIdentity identity,
		string description) =>
		Set(new(
			identity,
			WebRuntimeDomPropertyStatus.ConfirmedAbsent,
			string.Empty,
			WebRuntimeDomValueSource.Unspecified,
			WebRuntimeDomLinkKind.None,
			string.Empty,
			description));

	public void SetSourceUnsupported(
		WebRuntimeDomPropertyIdentity identity,
		string description) =>
		Set(new(
			identity,
			WebRuntimeDomPropertyStatus.SourceUnsupported,
			string.Empty,
			WebRuntimeDomValueSource.Unspecified,
			WebRuntimeDomLinkKind.None,
			string.Empty,
			description));

	public void Record(WebRuntimeDomIndexedProperty property)
	{
		ArgumentNullException.ThrowIfNull(property);
		Set(property);
	}

	public WebRuntimeDomQueryIndex Build()
	{
		var pending = _properties
			.Where(static item => item.Value is null)
			.Select(static item => item.Key)
			.Take(10)
			.ToArray();
		if (pending.Length != 0)
		{
			throw new InvalidDataException(
				$"The DOM query index is incomplete: {PendingCount} of "
				+ $"{PlannedCount} planned properties have no explicit result. "
				+ "First missing identities: "
				+ string.Join(
					", ",
					pending.Select(static identity =>
						$"{identity.DocumentScope}::{identity.XPath}/"
						+ $"{identity.ReflectedPropertyName}/"
						+ $"{identity.PropertyName}/{identity.Slot}")));
		}
		return new(
			Revision,
			PageUrl,
			CapturedAt,
			_properties.Values.Select(static property =>
				property
				?? throw new InvalidOperationException(
					"DOM query index completion changed during Build.")));
	}

	private void Set(WebRuntimeDomIndexedProperty property)
	{
		if (!_properties.TryGetValue(property.Identity, out var current))
		{
			throw new InvalidDataException(
				"The DOM query result does not belong to the planned request set.");
		}
		if (current is not null)
		{
			throw new InvalidOperationException(
				"The DOM query identity already has a result.");
		}
		_properties[property.Identity] = property;
	}
}

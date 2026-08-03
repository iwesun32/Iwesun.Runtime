namespace Iwesun.Runtime.WebView2;

/// <summary>
/// Reads live page evidence for one whole-tree request batch. Implementations
/// are platform adapters and return strong objects, never JSON documents.
/// </summary>
public interface IWebRuntimeDomEvidenceReader
{
	Uri CurrentPageUrl { get; }

	ValueTask<IReadOnlyList<WebRuntimeDomIndexedProperty>>
		ReadDomPropertiesAsync(
			IReadOnlyList<WebRuntimeDomPropertyRequest> requests,
			CancellationToken cancellationToken = default);
}

public interface IWebRuntimeSingleDomEvidenceReader
{
	ValueTask<WebRuntimeDomIndexedProperty> ReadDomPropertyAsync(
		WebRuntimeDomPropertyRequest request,
		CancellationToken cancellationToken = default);
}

/// <summary>
/// Converts one live whole-tree read into a validated immutable query index.
/// </summary>
public sealed class WebRuntimeLiveDomQuerySession
	: IWebRuntimeDomQuerySession,
	IWebRuntimeSingleDomPropertyQuerySession
{
	private readonly IWebRuntimeDomEvidenceReader _reader;
	private readonly bool _retainQueryIndex;
	private readonly SemaphoreSlim _readGate = new(1, 1);
	private WebRuntimeDomQueryIndex? _lastIndex;
	private long _revision;

	public WebRuntimeLiveDomQuerySession(
		IWebRuntimeDomEvidenceReader reader,
		bool retainQueryIndex = true)
	{
		ArgumentNullException.ThrowIfNull(reader);
		_reader = reader;
		_retainQueryIndex = retainQueryIndex;
	}

	public WebRuntimeDomQueryIndex? LastIndex =>
		Volatile.Read(ref _lastIndex);

	public async ValueTask<IReadOnlyList<WebRuntimeDomPropertyResult>>
		QueryDomPropertiesAsync(
			IReadOnlyList<WebRuntimeDomPropertyRequest> requests,
			CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(requests);
		await _readGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			var pageUrl = _reader.CurrentPageUrl;
			if (!pageUrl.IsAbsoluteUri)
			{
				throw new InvalidDataException(
					"The live DOM evidence reader returned a non-absolute page URL.");
			}
			var properties = await _reader.ReadDomPropertiesAsync(
				requests,
				cancellationToken).ConfigureAwait(false);
			ArgumentNullException.ThrowIfNull(properties);
			if (!_retainQueryIndex)
			{
				return MaterializeDirectResults(requests, properties);
			}
			var revision = Interlocked.Increment(ref _revision);
			var builder = new WebRuntimeDomQueryIndexBuilder(
				revision,
				pageUrl,
				DateTimeOffset.UtcNow,
				requests);
			foreach (var property in properties)
			{
				cancellationToken.ThrowIfCancellationRequested();
				builder.Record(property);
			}
			var index = builder.Build();
			Volatile.Write(ref _lastIndex, index);
			var indexedSession = new WebRuntimeIndexedDomQuerySession(index);
			return await indexedSession.QueryDomPropertiesAsync(
				requests,
				cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			_readGate.Release();
		}
	}

	private static IReadOnlyList<WebRuntimeDomPropertyResult>
		MaterializeDirectResults(
			IReadOnlyList<WebRuntimeDomPropertyRequest> requests,
			IReadOnlyList<WebRuntimeDomIndexedProperty> properties)
	{
		if (properties.Count != requests.Count)
		{
			throw new InvalidDataException(
				$"The DOM evidence reader returned {properties.Count} properties "
					+ $"for {requests.Count} requests.");
		}
		var results = new WebRuntimeDomPropertyResult[requests.Count];
		for (var index = 0; index < requests.Count; index++)
		{
			var request = requests[index];
			var property = properties[index];
			if (property.Identity != WebRuntimeDomPropertyIdentity.From(request))
			{
				throw new InvalidDataException(
					$"DOM evidence result {index} does not match query "
						+ $"'{request.QueryId}'.");
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
		return results;
	}

	public async ValueTask<WebRuntimeDomPropertyResult> QueryDomPropertyAsync(
		WebRuntimeDomPropertyRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		if (_reader is not IWebRuntimeSingleDomEvidenceReader singleReader)
		{
			throw new NotSupportedException(
				"The configured DOM evidence reader has no single-property API.");
		}
		await _readGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			var property = await singleReader.ReadDomPropertyAsync(
				request,
				cancellationToken).ConfigureAwait(false);
			if (property.Identity != WebRuntimeDomPropertyIdentity.From(request))
			{
				throw new InvalidDataException(
					"The single-property reader returned a different identity.");
			}
			return new(
				request.QueryId,
				property.Status,
				property.Value,
				property.ValueSource,
				property.LinkKind,
				property.LinkIdentity,
				property.Description);
		}
		finally
		{
			_readGate.Release();
		}
	}
}

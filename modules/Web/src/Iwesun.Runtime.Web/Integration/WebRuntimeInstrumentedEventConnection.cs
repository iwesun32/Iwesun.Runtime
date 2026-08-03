using System.Text.Json;
using Iwesun.Runtime.WebView2;

namespace Iwesun.Runtime.Web;

public sealed class WebRuntimeInstrumentedEventConnection(
	IWebRuntimeScriptSession scripts,
	Func<WebRuntimeDomTreeSnapshot> currentTree) :
	IHtmlRuntimeEventConnection
{
	private const string ReadExpression =
		"(()=>JSON.stringify(globalThis.__iwesunEventRegistry?.readAll()??[]))()";
	private readonly IWebRuntimeScriptSession _scripts =
		scripts ?? throw new ArgumentNullException(nameof(scripts));
	private readonly Func<WebRuntimeDomTreeSnapshot> _currentTree =
		currentTree ?? throw new ArgumentNullException(nameof(currentTree));

	public string Name => "instrumented-dom-events";

	public bool IsConnected => true;

	public async ValueTask<IReadOnlyList<HtmlRuntimeEventEvidence>> ReadAsync(
		CancellationToken cancellationToken = default)
	{
		var tree = _currentTree();
		var results = new List<HtmlRuntimeEventEvidence>();
		foreach (var document in tree.Documents)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var json = await EvaluateAsync(document, cancellationToken);
			using var parsed = JsonDocument.Parse(json);
			if (parsed.RootElement.ValueKind != JsonValueKind.Array)
			{
				throw new InvalidDataException(
					"Instrumented event registry did not return an array.");
			}
			foreach (var item in parsed.RootElement.EnumerateArray())
			{
				var xpath = RequiredString(item, "xpath");
				if (string.IsNullOrWhiteSpace(xpath))
					continue;
				results.Add(new(
					document.DocumentScope,
					xpath,
					RequiredString(item, "eventName"),
					RequiredString(item, "propertyName"),
					RequiredString(item, "handlerIdentity"),
					RequiredString(item, "description")));
			}
		}
		return results;
	}

	private Task<string> EvaluateAsync(
		WebRuntimeDomDocumentScope document,
		CancellationToken cancellationToken)
	{
		if (_scripts is IWebRuntimeDocumentScopeScriptSession scoped)
		{
			return scoped.EvaluateStringInDocumentAsync(
				document.DocumentScope,
				document.DocumentUri,
				ReadExpression,
				cancellationToken);
		}
		if (document.FrameIndex is { } frameIndex
			&& _scripts is IWebRuntimeIndexedFrameScriptSession indexed)
		{
			return indexed.EvaluateStringInFrameAsync(
				frameIndex,
				ReadExpression,
				cancellationToken);
		}
		if (document.FrameIndex is null)
			return _scripts.EvaluateStringAsync(ReadExpression, cancellationToken);
		throw new NotSupportedException(
			$"Document scope '{document.DocumentScope}' cannot be queried.");
	}

	private static string RequiredString(
		JsonElement element,
		string propertyName) =>
		element.TryGetProperty(propertyName, out var value)
			&& value.ValueKind == JsonValueKind.String
				? value.GetString()
					?? throw new InvalidDataException(
						$"Event evidence '{propertyName}' is null.")
				: throw new InvalidDataException(
					$"Event evidence is missing '{propertyName}'.");
}

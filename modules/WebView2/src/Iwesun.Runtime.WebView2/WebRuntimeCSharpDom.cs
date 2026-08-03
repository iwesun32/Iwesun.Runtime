using System.Text.Json;

namespace Iwesun.Runtime.WebView2;

/// <summary>Provides compiled C# access to the DevTools protocol without accepting script text.</summary>
public interface IWebRuntimeDevToolsSession
{
	string CurrentUrl { get; }
	Task<string> CallDevToolsProtocolMethodAsync(string method, string parametersJson, CancellationToken ct);

	IDisposable SubscribeDevToolsProtocolEvent(
		string eventName,
		EventHandler<WebRuntimeDevToolsProtocolEventArgs> handler) =>
		throw new NotSupportedException(
			"The DevTools session does not provide CDP event subscriptions.");
}

public sealed class WebRuntimeDevToolsProtocolEventArgs(string parameterJson)
	: EventArgs
{
	public string ParameterJson { get; } =
		parameterJson ?? throw new ArgumentNullException(nameof(parameterJson));
}

public sealed record WebRuntimeDomNode(int NodeId, int BackendNodeId = 0, string NodeName = "");

/// <summary>Strongly typed C# DOM operations backed only by DevTools DOM commands.</summary>
public static class WebRuntimeCSharpDom
{
	public static async Task<WebRuntimeDomNode> QuerySelectorAsync(
		IWebRuntimeDevToolsSession session,
		string selector,
		CancellationToken ct)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentException.ThrowIfNullOrWhiteSpace(selector);
		var documentJson = await session.CallDevToolsProtocolMethodAsync(
			"DOM.getDocument", "{\"depth\":1,\"pierce\":true}", ct).ConfigureAwait(false);
		using var document = JsonDocument.Parse(documentJson);
		var rootNodeId = document.RootElement.GetProperty("root").GetProperty("nodeId").GetInt32();
		var queryJson = await session.CallDevToolsProtocolMethodAsync(
			"DOM.querySelector",
			JsonSerializer.Serialize(new { nodeId = rootNodeId, selector }),
			ct).ConfigureAwait(false);
		using var query = JsonDocument.Parse(queryJson);
		var nodeId = query.RootElement.GetProperty("nodeId").GetInt32();
		return new WebRuntimeDomNode(nodeId);
	}

	public static async Task<string> GetOuterHtmlAsync(
		IWebRuntimeDevToolsSession session,
		int nodeId,
		CancellationToken ct)
	{
		if (nodeId <= 0)
			return "";
		var json = await session.CallDevToolsProtocolMethodAsync(
			"DOM.getOuterHTML", JsonSerializer.Serialize(new { nodeId }), ct).ConfigureAwait(false);
		using var document = JsonDocument.Parse(json);
		return document.RootElement.TryGetProperty("outerHTML", out var html) ? html.GetString() ?? "" : "";
	}
}

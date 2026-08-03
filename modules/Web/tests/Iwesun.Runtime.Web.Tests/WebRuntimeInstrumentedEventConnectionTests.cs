using Iwesun.Runtime.Web;
using Iwesun.Runtime.WebView2;
using Xunit;

namespace Iwesun.Runtime.WebView2.Tests;

public sealed class WebRuntimeInstrumentedEventConnectionTests
{
	[Fact]
	public async Task ReadAsync_ConvertsRegistryRecordsToStrongEvidence()
	{
		var page = new Uri("https://example.test/");
		var snapshot = new WebRuntimeDomTreeSnapshot(
			1,
			page,
			DateTimeOffset.UtcNow,
			[
				new(
					"document",
					"/html",
					"html",
					null,
					[],
					null,
					null)
			],
			[new("document", page, null)]);
		var connection = new WebRuntimeInstrumentedEventConnection(
			new EventScriptSession(),
			() => snapshot);

		var evidence = await connection.ReadAsync();

		var item = Assert.Single(evidence);
		Assert.Equal("document", item.DocumentScope);
		Assert.Equal("/html/body/button[1]", item.XPath);
		Assert.Equal("click", item.EventName);
		Assert.Equal("event.click.0", item.PropertyName);
	}

	private sealed class EventScriptSession : IWebRuntimeScriptSession
	{
		public Task<string> EvaluateStringAsync(
			string expression,
			CancellationToken ct) =>
			Task.FromResult(
				"""
				[{
				  "xpath":"/html/body/button[1]",
				  "eventName":"click",
				  "propertyName":"event.click.0",
				  "handlerIdentity":"listener:click:0",
				  "description":"instrumented"
				}]
				""");

		public Task<string> EvaluateStringInFrameAsync(
			string sourceUrlContains,
			string expression,
			CancellationToken ct) =>
			throw new NotSupportedException();
	}
}

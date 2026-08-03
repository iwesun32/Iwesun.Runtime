using Xunit;

namespace Iwesun.Runtime.WebView2.Tests;

public sealed class WebRuntimeDomQueryIndexTests
{
	[Fact]
	public async Task QueryDomPropertiesAsync_PreservesBatchOrder()
	{
		var first = Identity("/html/body/div[1]", "id");
		var second = Identity("/html/body/div[2]", "class");
		var session = new WebRuntimeIndexedDomQuerySession(
			Index(
				1,
				Captured(first, "first"),
				Captured(second, "second")));

		var results = await session.QueryDomPropertiesAsync(
			[
				Request("b", second),
				Request("a", first)
			]);

		Assert.Equal(["b", "a"], results.Select(static item => item.QueryId));
		Assert.Equal(
			["second", "first"],
			results.Select(static item => item.Value));
	}

	[Fact]
	public async Task QueryDomPropertiesAsync_MissingIdentity_Throws()
	{
		var stored = Identity("/html/body/div", "id");
		var missing = Identity("/html/body/span", "id");
		var session = new WebRuntimeIndexedDomQuerySession(
			Index(1, Captured(stored, "stored")));

		await Assert.ThrowsAsync<InvalidDataException>(
			() => session.QueryDomPropertiesAsync(
				[Request("missing", missing)]).AsTask());
	}

	[Fact]
	public async Task ReplaceIndex_AtomicallyExposesNewRevision()
	{
		var identity = Identity("/html/body/div", "id");
		var session = new WebRuntimeIndexedDomQuerySession(
			Index(1, Captured(identity, "old")));

		session.ReplaceIndex(Index(2, Captured(identity, "new")));
		var results = await session.QueryDomPropertiesAsync(
			[Request("id", identity)]);

		Assert.Equal(2, session.CurrentIndex.Revision);
		Assert.Equal("new", Assert.Single(results).Value);
	}

	[Fact]
	public void ReplaceIndex_WithStaleRevision_Throws()
	{
		var identity = Identity("/html/body/div", "id");
		var session = new WebRuntimeIndexedDomQuerySession(
			Index(2, Captured(identity, "current")));

		Assert.Throws<InvalidOperationException>(
			() => session.ReplaceIndex(
				Index(2, Captured(identity, "stale"))));
	}

	[Fact]
	public void IndexBuilder_WithPendingIdentity_RejectsBuild()
	{
		var first = Identity("/html/body/div[1]", "id");
		var second = Identity("/html/body/div[2]", "id");
		var builder = Builder(first, second);
		builder.SetCaptured(
			first,
			"first",
			WebRuntimeDomValueSource.DirectConstant);

		Assert.Throws<InvalidDataException>(() => builder.Build());
		Assert.Equal(1, builder.CompletedCount);
		Assert.Equal(1, builder.PendingCount);
	}

	[Fact]
	public void IndexBuilder_WithDuplicateResult_RejectsSecondWrite()
	{
		var identity = Identity("/html/body/div", "id");
		var builder = Builder(identity);
		builder.SetConfirmedAbsent(identity, "absent");

		Assert.Throws<InvalidOperationException>(
			() => builder.SetConfirmedAbsent(identity, "again"));
	}

	[Fact]
	public void IndexBuilder_WithIdentityOutsidePlan_RejectsWrite()
	{
		var planned = Identity("/html/body/div", "id");
		var outside = Identity("/html/body/span", "id");
		var builder = Builder(planned);

		Assert.Throws<InvalidDataException>(
			() => builder.SetSourceUnsupported(outside, "unsupported"));
	}

	[Fact]
	public void IndexBuilder_WithCompleteExplicitResults_Builds()
	{
		var captured = Identity("/html/body/div[1]", "id");
		var absent = Identity("/html/body/div[2]", "id");
		var unsupported = Identity("/html/body/div[3]", "id");
		var builder = Builder(captured, absent, unsupported);
		builder.SetCaptured(
			captured,
			"app",
			WebRuntimeDomValueSource.DirectConstant);
		builder.SetConfirmedAbsent(absent, "attribute absent");
		builder.SetSourceUnsupported(unsupported, "source unsupported");

		var index = builder.Build();

		Assert.Equal(3, index.PropertyCount);
		Assert.Equal(0, builder.PendingCount);
	}

	[Fact]
	public async Task LiveSession_ReadsWholeBatchAndPublishesIndex()
	{
		var first = Identity("/html/body/div[1]", "id");
		var second = Identity("/html/body/div[2]", "id");
		var reader = new TestEvidenceReader(
			Captured(first, "first"),
			Captured(second, "second"));
		var session = new WebRuntimeLiveDomQuerySession(reader);

		var results = await session.QueryDomPropertiesAsync(
			[
				Request("first", first),
				Request("second", second)
			]);

		Assert.Equal(1, reader.ReadCount);
		Assert.Equal(2, results.Count);
		Assert.Equal(2, session.LastIndex?.PropertyCount);
	}

	[Fact]
	public async Task LiveSession_DirectMode_ReturnsValidatedResultsWithoutIndex()
	{
		var first = Identity("/html/body/div[1]", "id");
		var second = Identity("/html/body/div[2]", "id");
		var reader = new TestEvidenceReader(
			Captured(first, "first"),
			Captured(second, "second"));
		var session = new WebRuntimeLiveDomQuerySession(
			reader,
			retainQueryIndex: false);

		var results = await session.QueryDomPropertiesAsync(
			[
				Request("first", first),
				Request("second", second)
			]);

		Assert.Equal(["first", "second"],
			results.Select(static result => result.Value));
		Assert.Null(session.LastIndex);
		Assert.Equal(1, reader.ReadCount);
	}

	[Fact]
	public async Task LiveSession_WithIncompleteReaderResult_RejectsIndex()
	{
		var first = Identity("/html/body/div[1]", "id");
		var second = Identity("/html/body/div[2]", "id");
		var reader = new TestEvidenceReader(Captured(first, "first"));
		var session = new WebRuntimeLiveDomQuerySession(reader);

		await Assert.ThrowsAsync<InvalidDataException>(
			() => session.QueryDomPropertiesAsync(
				[
					Request("first", first),
					Request("second", second)
				]).AsTask());

		Assert.Null(session.LastIndex);
	}

	private static WebRuntimeDomQueryIndex Index(
		long revision,
		params WebRuntimeDomIndexedProperty[] properties) =>
		new(
			revision,
			new Uri("https://www.doubao.com/"),
			DateTimeOffset.UtcNow,
			properties);

	private static WebRuntimeDomIndexedProperty Captured(
		WebRuntimeDomPropertyIdentity identity,
		string value) =>
		new(
			identity,
			WebRuntimeDomPropertyStatus.Captured,
			value,
			WebRuntimeDomValueSource.DirectConstant,
			WebRuntimeDomLinkKind.None,
			string.Empty,
			"test");

	private static WebRuntimeDomPropertyIdentity Identity(
		string xpath,
		string propertyName) =>
		new(
			"document",
			xpath,
			"div",
			propertyName,
			propertyName,
			WebRuntimeDomOwnerKind.Attribute,
			WebRuntimeDomSlotCategory.DataOrganization,
			WebRuntimeDomEvidenceKind.Attributes,
			WebRuntimeDomDataSlot.Initialization);

	private static WebRuntimeDomPropertyRequest Request(
		string queryId,
		WebRuntimeDomPropertyIdentity identity) =>
		new(
			queryId,
			identity.DocumentScope,
			identity.XPath,
			identity.TagName,
			identity.PropertyName,
			identity.ReflectedPropertyName,
			identity.OwnerKind,
			identity.Category,
			identity.EvidenceKind,
			identity.Slot);

	private static WebRuntimeDomQueryIndexBuilder Builder(
		params WebRuntimeDomPropertyIdentity[] identities) =>
		new(
			1,
			new Uri("https://www.doubao.com/"),
			DateTimeOffset.UtcNow,
			identities.Select((identity, index) =>
				Request(index.ToString(
					System.Globalization.CultureInfo.InvariantCulture),
					identity)));

	private sealed class TestEvidenceReader(
		params WebRuntimeDomIndexedProperty[] properties)
		: IWebRuntimeDomEvidenceReader
	{
		public Uri CurrentPageUrl { get; } =
			new("https://www.doubao.com/");

		public int ReadCount { get; private set; }

		public ValueTask<IReadOnlyList<WebRuntimeDomIndexedProperty>>
			ReadDomPropertiesAsync(
				IReadOnlyList<WebRuntimeDomPropertyRequest> requests,
				CancellationToken cancellationToken = default)
		{
			ReadCount++;
			return ValueTask.FromResult<
				IReadOnlyList<WebRuntimeDomIndexedProperty>>(properties);
		}
	}
}

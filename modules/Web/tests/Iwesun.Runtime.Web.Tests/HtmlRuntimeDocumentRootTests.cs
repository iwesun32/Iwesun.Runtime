using Iwesun.Runtime.Web;
using Xunit;

namespace Iwesun.Runtime.WebView2.Tests;

public sealed class HtmlRuntimeDocumentRootTests
{
	[Fact]
	public async Task CanvasTracker_InstallsSameFixedScriptForNewAndCurrentDocuments()
	{
		string? startupScript = null;
		string? currentScript = null;

		await WebRuntimeCanvasCommandTracker.InstallAsync(
			script =>
			{
				startupScript = script;
				return Task.CompletedTask;
			},
			script =>
			{
				currentScript = script;
				return Task.CompletedTask;
			});

		Assert.NotNull(startupScript);
		Assert.Equal(startupScript, currentScript);
		Assert.Contains(
			"CanvasRenderingContext2D",
			startupScript,
			StringComparison.Ordinal);
		Assert.Contains(
			"maximumCommands = 10000",
			startupScript,
			StringComparison.Ordinal);
		Assert.Contains(
			"createLinearGradient",
			startupScript,
			StringComparison.Ordinal);
		Assert.Contains(
			"gradient:addColorStop",
			startupScript,
			StringComparison.Ordinal);
		Assert.Contains(
			"resourceId",
			startupScript,
			StringComparison.Ordinal);
		Assert.Contains(
			"maximumImageDataBytes = 4 * 1024 * 1024",
			startupScript,
			StringComparison.Ordinal);
		Assert.Contains(
			"rgbaBase64",
			startupScript,
			StringComparison.Ordinal);
		Assert.Contains(
			"Array.isArray(value)",
			startupScript,
			StringComparison.Ordinal);
		Assert.Contains(
			"lineDashOffset",
			startupScript,
			StringComparison.Ordinal);
	}

	[Fact]
	public async Task DomQueryBridge_MapsXamlBindingWithoutEnumOrdinalLeak()
	{
		var element = new HtmlDivDomElement(new(
			"document",
			"/html/body/div",
			null,
			[],
			null,
			null));
		var context = new DomPropertyQueryContext(
			"document",
			element.XPath,
			element.TagName,
			nameof(DomElement.TagName),
			"id",
			ElementSlotOwnerKind.Attribute,
			ElementSlotCategory.DataOrganization,
			ElementEvidenceKind.Attributes,
			DomPropertyDataSlot.Link,
			element);
		var results = await WebRuntimeDomQueryBridge.QueryAsync(
			new XamlBindingDomSession(),
			[context]);

		var result = Assert.Single(results);
		Assert.Equal(ElementPropertyLinkKind.XamlBinding, result.Link.Kind);
		Assert.Equal(
			ElementPropertyValueSource.LinkedCalculation,
			result.ValueSource);
	}

	[Fact]
	public async Task Lifecycle_UsesDirectDomApiAndElementOwnedXamlPipeline()
	{
		var domSession = new ConfirmedAbsentDomSession();
		var domTreeSession = new RecordingDomTreeSession();
		var navigator = new RecordingNavigator();
		var webView2 = new HtmlRuntimeWebView2Context(
			domTreeSession,
			domSession,
			navigator,
			new Uri("https://example.test/page"),
			navigationOperations:
			[
				new("open-history", "document", "/html/body/nav/button")
			]);
		var root = new HtmlRuntimeDocumentRoot(
			"document",
			webView2,
			eventConnection: new RecordingEventConnection());

		var navigation = await root.NavigateAsync();
		var built = await root.BuildAsync();
		var domFill = await root.FillAsync();
		var domRelationshipStage = root.Stage;
		var domRelationships = root.GlobalRelationships;
		var xaml = root.BuildXaml(new RecordingObjectFactory());
		var xamlRelationshipStage = root.Stage;
		var xamlRelationships = root.XamlGlobalRelationships;
		var firstXamlElement = root.HtmlRootElement?
			.Children[0].Children[0].XamlElement;
		var displayed = await root.DisplayXamlAsync(new RecordingPresenter());
		var audited = await root.AuditXamlAsync(
			new ConfirmedAbsentXamlQueryEngine());
		var paths = await root.WriteXamlAsync(new RecordingWriter());
		var refresh = await root.RefreshRuntimeAsync();
		var rebuilt = root.RebuildXaml(new RecordingObjectFactory());

		Assert.Single(navigation);
		Assert.True(navigation[0].Success);
		Assert.Equal(1, navigator.NavigationCount);
		Assert.Equal(1, navigator.ClickCount);
		Assert.Equal("html", built.HtmlRootElement.TagName);
		var element = built.HtmlRootElement.Children[0].Children[0];
		Assert.Equal("div", element.TagName);
		Assert.Contains(
			element.Events,
			static domEvent => domEvent.Name == "event.click");
		Assert.Equal(
			"html-runtime.layout-manager",
			element.GlobalLayoutService.SourceLink.Description);
		Assert.Equal(
			"html-runtime.style-manager",
			element.GlobalStyleService.SourceLink.Description);
		Assert.Contains(
			root.DocumentEventEvidence,
			static item => item.XPath == "/@document"
				&& item.EventName == "visibilitychange");
		Assert.NotEmpty(domFill);
		Assert.Equal(
			HtmlRuntimeDocumentStage.GlobalRelationshipsResolved,
			domRelationshipStage);
		Assert.NotNull(domRelationships);
		Assert.Equal(
			HtmlRuntimeDocumentStage.XamlGlobalRelationshipsComposed,
			xamlRelationshipStage);
		Assert.NotNull(xamlRelationships);
		Assert.Equal(1, domTreeSession.ReadCount);
		Assert.True(domSession.RequestCount > 0);
		Assert.Equal(
			[WebRuntimeDomElementSpecialization.Layout],
			domSession.Specializations);
		Assert.Single(xaml.Documents);
		Assert.NotNull(displayed.DisplayIdentity);
		Assert.Single(audited.FillResults);
		Assert.Single(audited.Reports);
		Assert.Single(paths);
		Assert.Single(rebuilt.Documents);
		Assert.Equal(HtmlRuntimeDocumentStage.Audited, root.Stage);
		Assert.Contains(
			refresh.SelectMany(FlattenFillResults)
				.SelectMany(static result => result.Slots),
			static trace => trace.Slot == DomPropertyDataSlot.Link);
		Assert.Contains(
			refresh.SelectMany(FlattenFillResults)
				.SelectMany(static result => result.Slots),
			static trace => trace.Slot == DomPropertyDataSlot.Runtime);
		Assert.Same(root, element.HtmlRoot);
		Assert.NotNull(element.XamlElement);
		Assert.NotSame(firstXamlElement, element.XamlElement);
	}

	private static IEnumerable<DomElementFillResult> FlattenFillResults(
		DomElementFillResult result)
	{
		yield return result;
		foreach (var child in result.Children)
		{
			foreach (var descendant in FlattenFillResults(child))
				yield return descendant;
		}
	}

	[Fact]
	public async Task FillAsync_RoutesStyleInitializationThroughCssConnection()
	{
		var webView2 = new HtmlRuntimeWebView2Context(
			new RecordingDomTreeSession(),
			new ConfirmedAbsentDomSession(),
			new RecordingNavigator(),
			new Uri("https://example.test/page"));
		var root = new HtmlRuntimeDocumentRoot(
			"document",
			webView2,
			cssConnection: new WidthCssConnection());

		await root.NavigateAsync();
		await root.BuildAsync();
		await root.FillAsync();

		var progress = Assert.IsType<HtmlElementTraversalProgress>(
			root.DomFillProgress);
		Assert.Equal(3, progress.ProcessedElements);
		Assert.Equal(3, progress.TotalElements);
		Assert.Equal(3, progress.MaximumHierarchyLevel);
		Assert.Equal(100, progress.Percentage);
		var html = Assert.IsType<HtmlRootDomElement>(root.HtmlRootElement);
		Assert.Empty(html.RuntimeProperties);
		var width = Assert.IsType<HtmlRuntimeStyleBinding>(
			root.DesignRuntime.Styles.Find(
				html.DocumentScope,
				html.XPath,
				"style.width"));
		Assert.Equal("100px", width.Initialization?.Value);
		var widthOwner = width.GetSlotOwner();
		Assert.Equal("100px", widthOwner.SourceInitialization.Value);
		Assert.Same(
			widthOwner,
			root.ResolveGlobalStyleProperty(html, "style.width"));
		var opacity = Assert.IsType<HtmlRuntimeStyleBinding>(
			root.DesignRuntime.Styles.Find(
				html.DocumentScope,
				html.XPath,
				"style.opacity"));
		var opacityOwner = opacity.GetSlotOwner();
		var xamlOpacity = Assert.Single(
			html.BuildResolvedXamlAttributes(),
			static attribute => attribute.Name == "Opacity");
		Assert.Equal("0.5", xamlOpacity.Value);
		Assert.Same(opacityOwner, xamlOpacity.Source);
	}

	[Fact]
	public async Task BuildAsync_WhenDepthLimited_DefersKnownDeeperElementEvents()
	{
		var root = new HtmlRuntimeDocumentRoot(
			"document",
			new HtmlRuntimeWebView2Context(
				new RecordingDomTreeSession(),
				new ConfirmedAbsentDomSession(),
				new RecordingNavigator(),
				new Uri("https://example.test/page")),
			eventConnection: new RecordingEventConnection())
		{
			MaximumHierarchyLevel = 2
		};

		await root.NavigateAsync();
		await root.BuildAsync();
		await root.FillAsync();

		Assert.DoesNotContain(
			root.DocumentRoots.SelectMany(static root =>
				root.Children.SelectMany(static child => child.Children)),
			static element => element.XPath == "/html/body/div");
		Assert.Contains(
			root.DeferredElementEventEvidence,
			static item => item.XPath == "/html/body/div"
				&& item.EventName == "click");
		Assert.Equal(
			HtmlRuntimeDocumentStage.GlobalRelationshipsResolved,
			root.Stage);
	}

	[Fact]
	public void LayoutManager_RecordsWhetherXamlLayoutMechanismWasMaterialized()
	{
		var runtime = new HtmlRuntimeDesignRuntime();
		var target = new object();

		runtime.Layout.RegisterXamlTarget(
			"document",
			"/html/body/div",
			target,
			XamlElementMappingKind.FlexLayout,
			isMaterialized: true,
			"WinUI Grid flex tracks");

		var binding = Assert.Single(runtime.Layout.XamlTargets);
		Assert.Equal("document", binding.Owner.DocumentScope);
		Assert.Equal("/html/body/div", binding.Owner.XPath);
		Assert.Same(target, binding.Target);
		Assert.Equal(XamlElementMappingKind.FlexLayout, binding.MappingKind);
		Assert.True(binding.IsMaterialized);
		Assert.Equal("WinUI Grid flex tracks", binding.Mechanism);
	}

	private sealed class RecordingDomTreeSession : IWebRuntimeDomTreeSession
	{
		public int ReadCount { get; private set; }

		public ValueTask<WebRuntimeDomTreeSnapshot> ReadDomTreeAsync(
			CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			ReadCount++;
			return ValueTask.FromResult(
				new WebRuntimeDomTreeSnapshot(
					1,
					new Uri("https://example.test/page"),
					DateTimeOffset.UtcNow,
					[
						new(
							"document",
							"/html",
							"html",
							null,
							["/html/body"],
							null,
							null),
						new(
							"document",
							"/html/body",
							"body",
							"/html",
							["/html/body/div"],
							null,
							null),
						new(
							"document",
							"/html/body/div",
							"div",
							"/html/body",
							[],
							null,
							null)
					],
					[
						new(
							"document",
							new Uri("https://example.test/page"),
							null)
					]));
		}
	}

	private sealed class ConfirmedAbsentDomSession
		: IWebRuntimeDomQuerySession
	{
		public int RequestCount { get; private set; }

		public IReadOnlyList<WebRuntimeDomElementSpecialization>
			Specializations => _specializations.Order().ToArray();

		private readonly HashSet<WebRuntimeDomElementSpecialization>
			_specializations = [];

		public ValueTask<IReadOnlyList<WebRuntimeDomPropertyResult>>
			QueryDomPropertiesAsync(
				IReadOnlyList<WebRuntimeDomPropertyRequest> requests,
				CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			RequestCount += requests.Count;
			foreach (var request in requests)
				_specializations.Add(request.Specialization);
			IReadOnlyList<WebRuntimeDomPropertyResult> results = requests
				.Select(static request => new WebRuntimeDomPropertyResult(
					request.QueryId,
					WebRuntimeDomPropertyStatus.ConfirmedAbsent,
					string.Empty,
					WebRuntimeDomValueSource.Unspecified,
					WebRuntimeDomLinkKind.None,
					string.Empty,
					"Not present in the live DOM."))
				.ToArray();
			return ValueTask.FromResult(results);
		}
	}

	private sealed class XamlBindingDomSession : IWebRuntimeDomQuerySession
	{
		public ValueTask<IReadOnlyList<WebRuntimeDomPropertyResult>>
			QueryDomPropertiesAsync(
				IReadOnlyList<WebRuntimeDomPropertyRequest> requests,
				CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			IReadOnlyList<WebRuntimeDomPropertyResult> results =
			[
				new(
					requests[0].QueryId,
					WebRuntimeDomPropertyStatus.Captured,
					"binding-value",
					WebRuntimeDomValueSource.LinkedCalculation,
					WebRuntimeDomLinkKind.XamlBinding,
					"Binding Path=Value",
					"Live binding evidence.")
			];
			return ValueTask.FromResult(results);
		}
	}

	private sealed class RecordingNavigator : IHtmlRuntimeNavigator
	{
		public int NavigationCount { get; private set; }

		public int ClickCount { get; private set; }

		public ValueTask NavigateAsync(
			Uri url,
			CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			NavigationCount++;
			return ValueTask.CompletedTask;
		}

		public ValueTask WaitForStableDocumentAsync(
			CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return ValueTask.CompletedTask;
		}

		public ValueTask ClickAsync(
			HtmlRuntimeNavigationOperation operation,
			CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			ClickCount++;
			return ValueTask.CompletedTask;
		}
	}

	private sealed class RecordingEventConnection
		: IHtmlRuntimeEventConnection
	{
		public string Name => "self-test-events";

		public bool IsConnected => true;

		public ValueTask<IReadOnlyList<HtmlRuntimeEventEvidence>> ReadAsync(
			CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			IReadOnlyList<HtmlRuntimeEventEvidence> evidence =
			[
				new(
					"document",
					"/html/body/div",
					"click",
					"event.click",
					"listener-1",
					"Captured listener."),
				new(
					"document",
					"/@document",
					"visibilitychange",
					"event.visibilitychange",
					"listener-2",
					"Captured document listener.")
			];
			return ValueTask.FromResult(evidence);
		}
	}

	private sealed class WidthCssConnection : IHtmlRuntimePropertyConnection
	{
		public string Name => "self-test-css";

		public ElementEvidenceKind EvidenceKind =>
			ElementEvidenceKind.CssDeclarations;

		public bool IsConnected => true;

		public ValueTask<DomPropertyQueryResult> QueryAsync(
			DomPropertyQueryContext context,
			CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return ValueTask.FromResult(
				context.PropertyName == "style.width"
					&& context.Slot == DomPropertyDataSlot.Initialization
						? DomPropertyQueryResult.DirectConstant("100px")
					: context.PropertyName == "style.opacity"
						&& context.Slot == DomPropertyDataSlot.Initialization
							? DomPropertyQueryResult.DirectConstant("0.5")
						: DomPropertyQueryResult.ConfirmedAbsent(
							"No authored CSS value."));
		}
	}

	private sealed class RecordingObjectFactory : TestXamlElementObjectFactory
	{
		protected override object CreateElementCore(XamlElementObjectPlan plan) =>
			new ObjectNode(plan.Mapping.ElementName);

		public override void FillElementProperties(
			XamlElementObjectPropertyFillContext context)
		{
		}

		public override void AttachChild(XamlElementObjectAttachmentContext context)
		{
			((ObjectNode)context.Parent).Children.Add((ObjectNode)context.Child);
		}

		public override void ApplyGridTracks(
			object element,
			IReadOnlyList<XamlGridTrackDefinition> rowDefinitions,
			IReadOnlyList<XamlGridTrackDefinition> columnDefinitions)
		{
		}
	}

	private sealed class ObjectNode(string typeName)
	{
		public string TypeName { get; } = typeName;

		public List<ObjectNode> Children { get; } = [];
	}

	private sealed class RecordingPresenter : IHtmlRuntimeXamlPresenter
	{
		public ValueTask<HtmlRuntimeXamlDisplayResult> DisplayAsync(
			HtmlRuntimeXamlObjectTree tree,
			CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return ValueTask.FromResult(
				new HtmlRuntimeXamlDisplayResult(
					tree.Documents[0].RootObjects[0],
					DateTimeOffset.UtcNow));
		}
	}

	private sealed class ConfirmedAbsentXamlQueryEngine
		: IXamlPropertyQueryEngine
	{
		public ValueTask<XamlPropertyQueryResult> QueryAsync(
			XamlPropertyQueryContext context,
			CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (context.Execution.Kind
					== XamlPropertyExecutionKind.GlobalService
				&& context.Slot == XamlPropertyDataSlot.Link)
			{
				return ValueTask.FromResult(
					XamlPropertyQueryResult.Captured(
						context.Source.Link.Description,
						ElementPropertyValueSource.LinkedConstant,
						context.Source.Link,
						"Test root service connection."));
			}
			return ValueTask.FromResult(
				XamlPropertyQueryResult.ConfirmedAbsent(
					"DOM source slot is absent."));
		}
	}

	private sealed class RecordingWriter : IHtmlRuntimeXamlDocumentWriter
	{
		public ValueTask<IReadOnlyList<string>> WriteAsync(
			IReadOnlyList<HtmlRuntimeXamlDocument> documents,
			CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			IReadOnlyList<string> paths = documents
				.Select(static (_, index) => $"GeneratedSnapshot.{index}.xaml")
				.ToArray();
			return ValueTask.FromResult(paths);
		}
	}
}

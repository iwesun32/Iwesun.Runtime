using Iwesun.Runtime.Web;
using Xunit;

namespace Iwesun.Runtime.Web.Tests;

public sealed class SemanticComparisonTests
{
	[Fact]
	public void Compare_NormalizesCssAndXamlColorFormats()
	{
		var result = ElementPropertySemanticComparer.Compare(
			"rgba(255, 0, 0, 0.5)",
			PropertyUnit.None,
			"#80ff0000",
			PropertyUnit.None,
			Traits(
				PropertyValueKind.Color,
				PropertyComparisonKind.NormalizedColor));

		Assert.True(result.Equivalent);
		Assert.Equal("#80ff0000", result.NormalizedSource);
	}

	[Fact]
	public void Compare_AppliesCssPixelToDipGeometryTolerance()
	{
		var result = ElementPropertySemanticComparer.Compare(
			"100.25px",
			PropertyUnit.CssPixel,
			"100.5",
			PropertyUnit.Dip,
			Traits(
				PropertyValueKind.Coordinate,
				PropertyComparisonKind.GeometryTolerance,
				tolerance: .5));

		Assert.True(result.Equivalent);
		Assert.Equal(.25, result.NumericDifference);
	}

	[Fact]
	public void Compare_NormalizesEquivalentResourceUris()
	{
		var result = ElementPropertySemanticComparer.Compare(
			"https://example.test/a%20b/icon.svg",
			PropertyUnit.None,
			"https://example.test/a b/icon.svg",
			PropertyUnit.None,
			Traits(
				PropertyValueKind.Resource,
				PropertyComparisonKind.ResourceIdentity));

		Assert.True(result.Equivalent);
	}

	[Fact]
	public void Compare_MatchesRootRelativeResourceToResolvedAbsoluteUri()
	{
		var result = ElementPropertySemanticComparer.Compare(
			"/chat/38435325191836930",
			PropertyUnit.None,
			"https://www.doubao.com/chat/38435325191836930",
			PropertyUnit.None,
			Traits(
				PropertyValueKind.Resource,
				PropertyComparisonKind.ResourceIdentity));

		Assert.True(result.Equivalent);
	}

	[Fact]
	public void StringPropertyAudit_UsesResourceIdentityForHref()
	{
		var property = new HtmlDomAttributeProperty("href", "a");
		property.ApplyDomQueryResult(
			DomPropertyDataSlot.Initialization,
			DomPropertyQueryResult.DirectConstant(
				"/chat/38435325191836930"));
		property.ApplyXamlQueryResult(
			XamlPropertyDataSlot.Initialization,
			XamlPropertyQueryResult.DirectConstant(
				"https://www.doubao.com/chat/38435325191836930"));

		var result = property.AuditSlots(Traits(
			PropertyValueKind.Text,
			PropertyComparisonKind.Exact));

		Assert.Equal(
			ElementSlotFeatureAuditStatus.Passed,
			result.Initialization.Status);

		var dataSource = new DomElementStringDataSource(
			"href",
			ElementDataOrganizationSlotKind.NavigateUri,
			DomDataSourceKind.Url,
			DomDataSourceDomain.BusinessInput,
			XamlControlDataTargetKind.NavigateUri);
		dataSource.ApplyDomQueryResult(
			DomPropertyDataSlot.Initialization,
			DomPropertyQueryResult.DirectConstant(
				"/chat/38435325191836930"));
		dataSource.ApplyXamlQueryResult(
			XamlPropertyDataSlot.Initialization,
			XamlPropertyQueryResult.DirectConstant(
				"https://www.doubao.com/chat/38435325191836930"));

		Assert.Equal(
			ElementSlotFeatureAuditStatus.Passed,
			dataSource.AuditSlots().Initialization.Status);
	}

	[Fact]
	public void AuditSlots_PassesExactlyEqualManualReviewEvidence()
	{
		var result = ElementPropertySemanticComparer.AuditSlots(
			ElementPropertySlot<string>.FromValue("page"),
			ElementPropertySlot<string>.FromValue("page"),
			Traits(
				PropertyValueKind.Text,
				PropertyComparisonKind.ManualReview));

		Assert.Equal(ElementSlotFeatureAuditStatus.Passed, result.Status);
	}

	[Fact]
	public void Compare_RequiresCompleteStructuredAnimationMaterialization()
	{
		var source =
			"{\"animations\":[{\"animationName\":\"pulse\","
			+ "\"timing\":{\"duration\":100},\"keyframes\":["
			+ "{\"computedOffset\":0,\"opacity\":\"0\",\"width\":\"10px\"},"
			+ "{\"computedOffset\":1,\"opacity\":\"1\",\"width\":\"20px\"}]}]}";
		var xaml =
			"{\"animations\":[{\"animationName\":\"pulse\",\"duration\":100,"
			+ "\"properties\":[\"opacity\",\"width\"],"
			+ "\"unsupportedProperties\":[]}]}";

		var result = ElementPropertySemanticComparer.Compare(
			source,
			PropertyUnit.None,
			xaml,
			PropertyUnit.None,
			Traits(
				PropertyValueKind.Animation,
				PropertyComparisonKind.StructuredAnimation));

		Assert.True(result.Equivalent);
	}

	[Fact]
	public void Compare_RejectsPartiallyExecutedCanvasCommands()
	{
		var source =
			"[{\"name\":\"fillRect\",\"args\":[0,0,10,10]},"
			+ "{\"name\":\"clip\",\"args\":[]}]";
		var xaml =
			"{\"executed\":{\"fillRect\":1},\"partial\":{},"
			+ "\"unsupported\":{\"clip\":1}}";

		var result = ElementPropertySemanticComparer.Compare(
			source,
			PropertyUnit.None,
			xaml,
			PropertyUnit.None,
			Traits(
				PropertyValueKind.Resource,
				PropertyComparisonKind.CanvasReplay));

		Assert.False(result.Equivalent);
	}

	[Fact]
	public async Task QueryContext_ExposesValidatedRuntimeTraversalDescriptor()
	{
		var element = new HtmlDivDomElement(
			new("document", "/html/body/div", null, [], null, null));
		var engine = new RecordingEngine();

		await element.DomFillAsync(engine);

		var context = Assert.Single(
			engine.Contexts,
			item => item.ReflectedPropertyName == "GlobalLayoutService"
				&& item.Slot == DomPropertyDataSlot.Initialization);
		context.Traversal.Validate();
		Assert.Equal(
			"GlobalLayoutService/Property/service.globalLayout",
			context.Traversal.StrongPropertyIdentity);
		Assert.True(context.Traversal.Traits.UsesOwnerResolvedTraversal);
	}

	private static ElementPropertyTraits Traits(
		PropertyValueKind valueKind,
		PropertyComparisonKind comparison,
		double tolerance = 0) =>
		new(
			typeof(SemanticComparisonTests),
			"Test",
			valueKind,
			PropertyValueStage.DomRuntime,
			PropertyCoordinateMode.None,
			PropertyReferenceSpace.None,
			PropertyAxis.None,
			PropertyUnit.None,
			PropertyTranslationKind.Direct,
			comparison,
			PropertyInheritanceKind.NotInherited,
			true,
			tolerance,
			string.Empty,
			"Test comparison traits.");

	private sealed class RecordingEngine : IDomPropertyQueryEngine
	{
		public List<DomPropertyQueryContext> Contexts { get; } = [];

		public ValueTask<DomPropertyQueryResult> QueryAsync(
			DomPropertyQueryContext context,
			CancellationToken cancellationToken = default)
		{
			Contexts.Add(context);
			return ValueTask.FromResult(
				DomPropertyQueryResult.ConfirmedAbsent("test"));
		}
	}
}

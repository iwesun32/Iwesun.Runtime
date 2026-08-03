using Iwesun.Runtime.Web;
using Xunit;

namespace Iwesun.Runtime.Web.Tests;

public sealed class HtmlDomElementArchitectureAuditTests
{
	[Fact]
	public void Inspect_WhenConcreteElementUsesGlobalContract_Passes()
	{
		var element = new HtmlDivDomElement(Mapping("/html/body/div"));

		var result = HtmlDomElementArchitectureAudit.Inspect(element);

		Assert.True(result.UsesConcreteType);
		Assert.Empty(result.MissingStandardAttributes);
		Assert.Empty(result.UnexpectedStandardAttributes);
		Assert.Empty(result.UnclassifiedStandardAttributes);
		Assert.Empty(result.InvalidPipelineProperties);
		Assert.Empty(result.MissingStandardEventHandlers);
		Assert.Empty(result.UnexpectedStandardEventHandlers);
		Assert.True(result.Passed);
	}

	[Fact]
	public void Inspect_WhenGenericShellIsUsed_FailsArchitectureGate()
	{
		var element = new HtmlGenericDomElement(
			Mapping("/html/body/unknown"),
			"unknown");

		var result = HtmlDomElementArchitectureAudit.Inspect(element);

		Assert.False(result.UsesConcreteType);
		Assert.False(result.Passed);
	}

	[Fact]
	public void Inspect_WhenCustomElementIsExplicitlyRegistered_PassesArchitectureGate()
	{
		HtmlCustomElementContractRegistry.Register(
			"iwesun-audit-fixture",
			"Test-only explicit custom element contract.",
			"HtmlCssBoxGrid");
		var element = new HtmlGenericDomElement(
			Mapping("/html/body/iwesun-audit-fixture"),
			"iwesun-audit-fixture");

		var result = HtmlDomElementArchitectureAudit.Inspect(element);

		Assert.False(result.UsesConcreteType);
		Assert.True(result.Passed);
		Assert.Empty(result.XamlObjectContractErrors);
	}

	[Fact]
	public void Inspect_WhenEveryStandardHtmlTypeIsRegistered_HasCompleteAttributes()
	{
		var tags = HtmlDomElementTypeCatalog.HtmlTags;
		var failures = new List<string>();

		foreach (var tag in tags)
		{
			var element = Assert.IsAssignableFrom<HtmlDomElementDefinition>(
				HtmlDomElementTypeCatalog.Create(tag, Mapping($"/html/body/{tag}")));
			var result = HtmlDomElementArchitectureAudit.Inspect(element);
			if (!result.Passed
				|| result.MissingOwnedXamlConversionProperties.Count > 0)
			{
				failures.Add(
					$"{tag}: missing=[{string.Join(",", result.MissingStandardAttributes)}], "
				+ $"unexpected=[{string.Join(",", result.UnexpectedStandardAttributes)}], "
				+ $"unclassified=[{string.Join(",", result.UnclassifiedStandardAttributes)}], "
				+ $"invalid=[{string.Join(",", result.InvalidPipelineProperties)}], "
				+ $"missingEvents=[{string.Join(",", result.MissingStandardEventHandlers)}], "
				+ $"unexpectedEvents=[{string.Join(",", result.UnexpectedStandardEventHandlers)}], "
				+ $"missingOwnedXaml=[{string.Join(",", result.MissingOwnedXamlConversionProperties)}], "
				+ $"ownsCreateXaml={result.OwnsStrongXamlCreation}, "
				+ $"xamlContract=[{string.Join(",", result.XamlObjectContractErrors)}]");
			}
		}
		Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
	}

	[Fact]
	public void CreateXaml_WhenEveryStandardHtmlTypeUsesDefaultState_NeverUsesRawGrid()
	{
		var failures = new List<string>();
		foreach (var tag in HtmlDomElementTypeCatalog.HtmlTags)
		{
			var element = Assert.IsAssignableFrom<HtmlDomElementDefinition>(
				HtmlDomElementTypeCatalog.Create(
					tag,
					Mapping($"/html/body/{tag}")));
			var mapping = element.GetXamlElementMapping();
			if (mapping.ObjectType == XamlElementObjectType.Grid)
				failures.Add($"{tag}:{mapping.Kind}");
		}
		Assert.Empty(failures);
	}

	[Fact]
	public async Task CreateXaml_WhenEveryStandardHtmlTypeReceivesFormattingStates_StaysInsideStrongContract()
	{
		var failures = new List<string>();
		var states = new[]
		{
			new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["style.display"] = "block"
			},
			new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["style.display"] = "flex",
				["style.flexDirection"] = "row",
				["style.flexWrap"] = "nowrap"
			},
			new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["style.display"] = "flex",
				["style.flexDirection"] = "column",
				["style.flexWrap"] = "wrap"
			},
			new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["style.display"] = "grid",
				["style.gridTemplateColumns"] = "1fr 2fr",
				["style.gridTemplateRows"] = "auto"
			}
		};
		foreach (var tag in HtmlDomElementTypeCatalog.HtmlTags)
		{
			var contract = HtmlXamlStrongTypeContractCatalog.Contracts[tag];
			foreach (var state in states)
			{
				var element = Assert.IsAssignableFrom<HtmlDomElementDefinition>(
					HtmlDomElementTypeCatalog.Create(
						tag,
						Mapping($"/html/body/{tag}")));
				foreach (var propertyName in state.Keys)
					element.AddRuntimeProperty(propertyName, ElementSlotCategory.Style);
				await element.DomFillAsync((_, _, name, slot) =>
					ValueTask.FromResult(
						slot != DomPropertyDataSlot.Link
							&& state.TryGetValue(name, out var value)
								? DomPropertyQueryResult.DirectConstant(value)
								: DomPropertyQueryResult.ConfirmedAbsent(
									"Formatting-state audit fixture.")));
				var mapping = element.GetXamlElementMapping();
				if (!contract.AllowedElementNames.Contains(mapping.ElementName))
				{
					failures.Add(
						$"{tag}:{state["style.display"]}:"
							+ $"{mapping.ElementName} outside "
							+ $"[{string.Join(',', contract.AllowedElementNames)}]");
				}
			}
		}

		Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
	}

	[Fact]
	public void StrongTypeContracts_MatchEveryStandardHtmlTagExactly()
	{
		var tags = HtmlDomElementTypeCatalog.HtmlTags
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		var contracts = HtmlXamlStrongTypeContractCatalog.Contracts.Keys
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		Assert.Equal(113, tags.Count);
		Assert.Equal(
			[],
			tags.Except(contracts, StringComparer.OrdinalIgnoreCase)
				.Order(StringComparer.Ordinal));
		Assert.Equal(
			[],
			contracts.Except(tags, StringComparer.OrdinalIgnoreCase)
				.Order(StringComparer.Ordinal));
		Assert.All(
			HtmlXamlStrongTypeContractCatalog.Contracts.Values,
			static contract =>
			{
				Assert.NotEmpty(contract.AllowedElementNames);
				Assert.False(string.IsNullOrWhiteSpace(contract.DecisionBasis));
			});
	}

	[Fact]
	public void BuildXamlObjectTree_RejectsEveryHtmlTypeBeforeDomFill()
	{
		var failures = new List<string>();
		foreach (var tag in HtmlDomElementTypeCatalog.HtmlTags)
		{
			var element = Assert.IsAssignableFrom<HtmlDomElementDefinition>(
				HtmlDomElementTypeCatalog.Create(
					tag,
					Mapping($"/html/body/{tag}")));
			var exception = Record.Exception(
				() => element.BuildXamlObjectTree(new UnusedFactory()));
			if (exception is not InvalidOperationException
				|| !exception.Message.Contains(
					"must complete DOM Fill",
					StringComparison.Ordinal))
			{
				failures.Add(
					$"{tag}: {exception?.GetType().Name ?? "no exception"} "
						+ $"{exception?.Message ?? string.Empty}");
			}
		}

		Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
	}

	[Fact]
	public void StrongTypeContracts_ExposeConsumerTypesAndAttachedProperties()
	{
		Assert.Contains(
			"HtmlMediaElementControl",
			HtmlXamlStrongTypeContractCatalog.RequiredConsumerTypes);
		Assert.Contains(
			"HtmlEmbeddedDocumentHost",
			HtmlXamlStrongTypeContractCatalog.RequiredConsumerTypes);
		Assert.Contains(
			"HtmlFormState.InitialValue",
			HtmlXamlStrongTypeContractCatalog.RequiredAttachedProperties);
		Assert.Contains(
			"HtmlTable.ColumnSpan",
			HtmlXamlStrongTypeContractCatalog.RequiredAttachedProperties);
		Assert.True(HtmlXamlStrongTypeContractCatalog.CanOwnPlacement(
			"HtmlEmbeddedDocumentHost",
			ElementXamlChildPlacementKind.Content));
	}

	[Fact]
	public void Inspect_WhenBodyIsCreated_IncludesEveryWindowEventHandler()
	{
		var element = new HtmlBodyDomElement(Mapping("/html/body"));

		var result = HtmlDomElementArchitectureAudit.Inspect(element);

		Assert.True(result.Passed);
		Assert.Contains(
			element.SupportedEventHandlers,
			static item => item.AttributeName == "onpageswap"
				&& item.BodyOnly);
		Assert.Contains(
			element.SupportedEventHandlers,
			static item => item.AttributeName == "onlanguagechange"
				&& item.BodyOnly);
	}

	[Fact]
	public void Inspect_WhenEveryStandardSvgTypeIsRegistered_HasOwnedConversion()
	{
		var failures = new List<string>();
		foreach (var tag in HtmlDomElementTypeCatalog.SvgTags)
		{
			var element = Assert.IsAssignableFrom<SvgDomElementDefinition>(
				HtmlDomElementTypeCatalog.Create(
					tag,
					Mapping($"/html/body/svg/{tag}")));
			var result = SvgDomElementArchitectureAudit.Inspect(element);
			if (!result.Passed)
			{
				failures.Add(
					$"{tag}: missing=[{string.Join(",", result.MissingStandardAttributes)}], "
					+ $"unexpected=[{string.Join(",", result.UnexpectedStandardAttributes)}], "
					+ $"missingOwnedXaml=[{string.Join(",", result.MissingOwnedXamlConversionProperties)}]");
			}
		}
		Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
	}

	private static DomElementMapping Mapping(string xpath) =>
		new("document", xpath, null, [], null, null);

	private sealed class UnusedFactory : TestXamlElementObjectFactory
	{
		protected override object CreateElementCore(XamlElementObjectPlan plan) =>
			throw new InvalidOperationException("Factory must not be reached.");

		public override void FillElementProperties(
			XamlElementObjectPropertyFillContext context) =>
			throw new InvalidOperationException("Factory must not be reached.");

		public override void AttachChild(XamlElementObjectAttachmentContext context) =>
			throw new InvalidOperationException("Factory must not be reached.");

		public override void ApplyGridTracks(
			object element,
			IReadOnlyList<XamlGridTrackDefinition> rowDefinitions,
			IReadOnlyList<XamlGridTrackDefinition> columnDefinitions) =>
			throw new InvalidOperationException("Factory must not be reached.");
	}
}

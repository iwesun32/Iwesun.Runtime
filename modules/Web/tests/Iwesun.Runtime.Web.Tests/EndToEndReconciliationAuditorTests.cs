using Iwesun.Runtime.Web;
using Xunit;

namespace Iwesun.Runtime.Web.Tests;

public sealed class EndToEndReconciliationAuditorTests
{
	[Fact]
	public void Audit_WhenPipelineStagesAreMissing_FailsWithStageEvidence()
	{
		var element = new HtmlDivDomElement(
			new(
				"document",
				"/html/body/div",
				null,
				[],
				null,
				null));

		var report = EndToEndReconciliationAuditor.Audit(
			[element],
			[],
			[]);

		Assert.False(report.Passed);
		Assert.Equal(1, report.ElementsExpected);
		Assert.Contains(
			report.Issues,
			static issue => issue.Stage == "DOM Fill"
				&& issue.Description == "Element result is missing.");
		Assert.Contains(
			report.Issues,
			static issue => issue.Stage == "XAML Fill"
				&& issue.Description == "Element result is missing.");
		Assert.Contains(
			report.Issues,
			static issue => issue.Stage == "BuildXaml");
	}

	[Fact]
	public void Audit_WhenDomIdentityIsDuplicated_ReportsHardFailure()
	{
		var element = new HtmlDivDomElement(
			new(
				"document",
				"/html/body/div",
				null,
				[],
				null,
				null));
		var fill = new DomElementFillResult(
			"document::/html/body/div",
			[],
			[]);

		var report = EndToEndReconciliationAuditor.Audit(
			[element],
			[fill, fill],
			[]);

		Assert.Contains(
			report.Issues,
			static issue => issue.Stage == "DOM Fill"
				&& issue.Description == "Duplicate strong element identity.");
	}

	[Fact]
	public void Audit_WhenXamlSlotIsTargetUnsupported_ReportsHardFailure()
	{
		var element = new HtmlDivDomElement(
			new(
				"document",
				"/html/body/div",
				null,
				[],
				null,
				null));
		var identity = "document::/html/body/div";
		var xamlFill = new XamlElementFillResult(
			identity,
			[
				new(
					"style.filter",
					XamlPropertyDataSlot.Runtime,
					XamlPropertyQueryStatus.TargetUnsupported,
					"No physical target reader.")
			],
			[]);

		var report = EndToEndReconciliationAuditor.Audit(
			[element],
			[new DomElementFillResult(identity, [], [])],
			[xamlFill]);

		Assert.False(report.Passed);
		Assert.Contains(
			report.Issues,
			static issue => issue.Stage == "XAML Fill"
				&& issue.Description.Contains(
					"style.filter/Runtime is TargetUnsupported",
					StringComparison.Ordinal));
	}
}

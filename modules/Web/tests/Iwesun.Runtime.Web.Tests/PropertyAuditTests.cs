using Iwesun.Runtime.Web;
using Xunit;

namespace Iwesun.Runtime.Web.Tests;

public sealed class ElementPropertyAuditTests
{
	[Fact]
	public void Audit_ComparesStaticAndRuntimePairsWithinTolerance()
	{
		var property = Create(
			sourceInitialization: Slot(100, PropertyUnit.CssPixel),
			xamlInitialization: Slot(100.2, PropertyUnit.Dip),
			sourceRuntime: Slot(320, PropertyUnit.CssPixel),
			xamlRuntime: Slot(320.4, PropertyUnit.Dip));

		var result = property.Audit();

		Assert.True(result.Passed);
		Assert.Equal(
			ElementPropertyAuditStatus.Passed,
			result.StaticInitialization.Status);
		Assert.Equal(ElementPropertyAuditStatus.Passed, result.Runtime.Status);
		Assert.Equal(PropertyUnit.CssPixel, result.Runtime.Source.Unit);
		Assert.Equal(PropertyUnit.Dip, result.Runtime.ActualXaml.Unit);
		Assert.True(property.IsAuditRequired());
		Assert.True(property.IsAuditable());
		Assert.True(property.IsNumericAudit());
		Assert.True(property.IsStaticAuditRequired());
		Assert.True(property.IsRuntimeAuditRequired());
		Assert.True(property.IsSpaceProperty());
	}

	[Fact]
	public void Audit_ValueOutsideToleranceFails()
	{
		var property = Create(
			sourceInitialization: Slot(100, PropertyUnit.CssPixel),
			xamlInitialization: Slot(103, PropertyUnit.Dip),
			sourceRuntime: Slot(320, PropertyUnit.CssPixel),
			xamlRuntime: Slot(320, PropertyUnit.Dip));

		var result = property.Audit();

		Assert.False(result.Passed);
		Assert.Equal(
			ElementPropertyAuditStatus.ValueMismatch,
			result.StaticInitialization.Status);
		Assert.Equal(3, result.StaticInitialization.NumericDifference);
		Assert.Equal(.5, result.StaticInitialization.NumericAllowedTolerance);
	}

	[Fact]
	public void Audit_MissingRequiredXamlValueFails()
	{
		var property = Create(
			sourceInitialization: Slot(100, PropertyUnit.CssPixel),
			xamlInitialization: ElementPropertySlot<double>.Unset,
			sourceRuntime: Slot(320, PropertyUnit.CssPixel),
			xamlRuntime: Slot(320, PropertyUnit.Dip));

		var result = property.Audit();

		Assert.False(result.Passed);
		Assert.Equal(
			ElementPropertyAuditStatus.MissingXamlValue,
			result.StaticInitialization.Status);
	}

	[Fact]
	public void Audit_UnexpectedUnitFailsBeforeValueComparison()
	{
		var property = Create(
			sourceInitialization: Slot(100, PropertyUnit.Percent),
			xamlInitialization: Slot(100, PropertyUnit.Dip),
			sourceRuntime: Slot(320, PropertyUnit.CssPixel),
			xamlRuntime: Slot(320, PropertyUnit.Dip));

		var result = property.Audit();

		Assert.False(result.Passed);
		Assert.Equal(
			ElementPropertyAuditStatus.SourceUnitMismatch,
			result.StaticInitialization.Status);
		Assert.Null(result.StaticInitialization.NumericDifference);
	}

	[Fact]
	public void NonNumericStyleProperty_OverridesDetailedAudit()
	{
		var property = new ExactStringStyleProperty("#fff", "#fff");

		Assert.True(property.IsStyleProperty());
		Assert.True(property.IsAuditRequired());
		Assert.True(property.IsAuditable());
		Assert.False(property.IsNumericAudit());
		Assert.True(property.Audit().Passed);
	}

	[Fact]
	public void RuntimeGeometry_MissingRenderedXamlRuntimeValueFails()
	{
		var geometry = new DomElementRuntimeGeometry();
		geometry.Width.ApplyDomQueryResult(
			DomPropertyDataSlot.Runtime,
			DomPropertyQueryResult.DirectConstant("320"));

		var result = geometry.Width.AuditSlots();

		Assert.False(result.Passed);
		Assert.Equal(
			ElementSlotFeatureAuditStatus.MissingXamlValue,
			result.Runtime.Status);
		Assert.Equal(ElementSlotFeatureAuditStatus.NotRequired,
			result.Initialization.Status);
		Assert.Equal(ElementSlotFeatureAuditStatus.NotRequired,
			result.Link.Status);
	}

	private static ElementNumericProperty Create(
		ElementPropertySlot<double> sourceInitialization,
		ElementPropertySlot<double> xamlInitialization,
		ElementPropertySlot<double> sourceRuntime,
		ElementPropertySlot<double> xamlRuntime)
	{
		var policy = new NumericAuditPolicy(
			PropertyUnit.CssPixel,
			PropertyUnit.Dip,
			absoluteTolerance: .5);
		return new(
			"X",
			ElementSpaceSlotKind.X,
			ElementPropertyTraitsReflector.GetAttribute<
				NumericTestElement>(nameof(NumericTestElement.X)),
			sourceInitialization,
			ElementPropertyLink.None,
			sourceRuntime,
			ElementPropertyValueSource.DirectConstant,
			xamlInitialization,
			ElementPropertyLink.None,
			xamlRuntime,
			ElementPropertyValueSource.DirectConstant,
			new NumericPropertyAuditDefinition(policy, policy));
	}

	private static ElementPropertySlot<double> Slot(
		double value,
		PropertyUnit unit) =>
		ElementPropertySlot<double>.FromValue(value, unit);

	private sealed class ExactStringStyleProperty :
		ElementStyleSlottedProperty<string>
	{
		public ExactStringStyleProperty(string source, string xaml) :
			base(
				"ForegroundColor",
				ElementStyleSlotKind.ForegroundColor,
				ElementPropertyValueSource.DirectConstant,
				ElementPropertySlot<string>.FromValue(source),
				ElementPropertyLink.None,
				ElementPropertySlot<string>.Unset,
				ElementPropertySlot<string>.FromValue(xaml),
				ElementPropertyLink.None,
				ElementPropertySlot<string>.Unset,
				ElementPropertyValueSource.DirectConstant)
		{
		}

		public override ElementPropertyAuditResult<string> Audit()
		{
			var passed = string.Equals(
				SourceInitialization.Value,
				XamlInitialization.Value,
				StringComparison.Ordinal);
			var staticResult = new ElementPropertyAuditPhaseResult<string>(
				ElementPropertyAuditPhase.StaticInitialization,
				passed
					? ElementPropertyAuditStatus.Passed
					: ElementPropertyAuditStatus.ValueMismatch,
				SourceInitialization,
				SourceInitialization,
				XamlInitialization,
				null,
				null,
				passed ? "颜色文本一致。" : "颜色文本不一致。");
			var runtimeResult = new ElementPropertyAuditPhaseResult<string>(
				ElementPropertyAuditPhase.Runtime,
				ElementPropertyAuditStatus.NotRequired,
				default,
				default,
				default,
				null,
				null,
				"该属性没有运行值。");
			return new(
				Name,
				Category,
				staticResult.Passed,
				staticResult,
				runtimeResult);
		}
	}

	private sealed class NumericTestElement
	{
		[ElementProperty(
			PropertyValueKind.Coordinate,
			PropertyValueStage.DomRuntime)]
		public double X { get; }
	}
}

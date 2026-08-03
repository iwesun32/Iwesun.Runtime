using Iwesun.Runtime.Web;
using Xunit;

namespace Iwesun.Runtime.Web.Tests;

public sealed class ElementSlotCategoryTests
{
	[Fact]
	public void CategoryBases_AreAbstractAndRequireConcreteAudit()
	{
		Assert.True(typeof(ElementSpaceSlottedProperty<>).IsAbstract);
		Assert.True(typeof(ElementStyleSlottedProperty<>).IsAbstract);
		Assert.True(typeof(ElementEffectSlottedProperty<>).IsAbstract);
		Assert.True(typeof(ElementActionSlottedProperty<>).IsAbstract);
		Assert.True(typeof(ElementDataOrganizationSlottedProperty<>).IsAbstract);

		var auditMethod = typeof(ElementSlottedProperty<>).GetMethod(
			nameof(IElementPropertyAudit<int>.Audit));
		Assert.NotNull(auditMethod);
		Assert.True(auditMethod.IsAbstract);
	}

	[Fact]
	public void GenericProperty_SupportsScalarAndCompositeValueTypes()
	{
		var x = new ExactSpaceProperty<int>(
			ElementSpaceSlotKind.X,
			120,
			120);
		var xy = new ExactSpaceProperty<TestPoint>(
			ElementSpaceSlotKind.Position,
			new TestPoint(120, 80),
			new TestPoint(120, 80));

		Assert.Equal(120, x.SourceInitialization.Value);
		Assert.Equal(new TestPoint(120, 80), xy.SourceInitialization.Value);
		Assert.True(x.Audit().Passed);
		Assert.True(xy.Audit().Passed);
		Assert.True(x.IsSpaceProperty());
		Assert.True(x.IsAuditable());
		Assert.Equal(
			ElementPropertyValueSource.DirectConstant,
			x.SourceValueSource);
		Assert.Equal(
			ElementPropertyValueSource.DirectConstant,
			x.XamlValueSource);
	}

	private sealed class ExactSpaceProperty<TValue> :
		ElementSpaceSlottedProperty<TValue>
	{
		public ExactSpaceProperty(
			ElementSpaceSlotKind slotKind,
			TValue source,
			TValue xaml) :
			base(
				slotKind.ToString(),
				slotKind,
				ElementPropertyValueSource.DirectConstant,
				ElementPropertySlot<TValue>.FromValue(source),
				ElementPropertyLink.None,
				ElementPropertySlot<TValue>.Unset,
				ElementPropertySlot<TValue>.FromValue(xaml),
				ElementPropertyLink.None,
				ElementPropertySlot<TValue>.Unset,
				ElementPropertyValueSource.DirectConstant)
		{
		}

		public override ElementPropertyAuditResult<TValue> Audit()
		{
			var passed = EqualityComparer<TValue>.Default.Equals(
				SourceInitialization.Value,
				XamlInitialization.Value);
			var staticResult = new ElementPropertyAuditPhaseResult<TValue>(
				ElementPropertyAuditPhase.StaticInitialization,
				passed
					? ElementPropertyAuditStatus.Passed
					: ElementPropertyAuditStatus.ValueMismatch,
				SourceInitialization,
				SourceInitialization,
				XamlInitialization,
				null,
				null,
				passed ? "精确值一致。" : "精确值不一致。");
			var runtimeResult = new ElementPropertyAuditPhaseResult<TValue>(
				ElementPropertyAuditPhase.Runtime,
				ElementPropertyAuditStatus.NotRequired,
				default,
				default,
				default,
				null,
				null,
				"该测试属性没有运行槽位。");
			return new(
				Name,
				Category,
				staticResult.Passed,
				staticResult,
				runtimeResult);
		}
	}

	private readonly record struct TestPoint(int X, int Y);
}

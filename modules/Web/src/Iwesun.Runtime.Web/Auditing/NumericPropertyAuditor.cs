namespace Iwesun.Runtime.Web;

public static class NumericElementPropertyAuditor
{
	public static ElementPropertyAuditResult<double> Audit(
		ElementNumericProperty property)
	{
		ArgumentNullException.ThrowIfNull(property);
		var staticResult = Compare(
			ElementPropertyAuditPhase.StaticInitialization,
			property.SourceInitialization,
			property.XamlInitialization,
			property.AuditDefinition.StaticInitialization);
		var runtimeResult = Compare(
			ElementPropertyAuditPhase.Runtime,
			property.SourceRuntime,
			property.XamlRuntime,
			property.AuditDefinition.Runtime);
		return new(
			property.Name,
			property.Category,
			staticResult.Passed && runtimeResult.Passed,
			staticResult,
			runtimeResult);
	}

	private static ElementPropertyAuditPhaseResult<double> Compare(
		ElementPropertyAuditPhase phase,
		ElementPropertySlot<double> source,
		ElementPropertySlot<double> xaml,
		NumericAuditPolicy? policy)
	{
		if (policy is null)
			return Result(
				phase,
				ElementPropertyAuditStatus.NotRequired,
				message: "该阶段未启用审核。");
		if (!source.IsSet)
			return Result(
				phase,
				ElementPropertyAuditStatus.MissingSourceValue,
				actualXaml: xaml,
				message: "缺少 HTML/DOM 源值。");
		if (!xaml.IsSet)
			return Result(
				phase,
				ElementPropertyAuditStatus.MissingXamlValue,
				source,
				message: "缺少 XAML/WinUI 值。");
		if (source.Unit != policy.SourceUnit)
			return Result(
				phase,
				ElementPropertyAuditStatus.SourceUnitMismatch,
				source,
				actualXaml: xaml,
				message: $"源单位应为 {policy.SourceUnit}，实际为 {source.Unit}。");
		if (xaml.Unit != policy.XamlUnit)
			return Result(
				phase,
				ElementPropertyAuditStatus.XamlUnitMismatch,
				source,
				actualXaml: xaml,
				message: $"XAML 单位应为 {policy.XamlUnit}，实际为 {xaml.Unit}。");

		var expected = source.Value * policy.SourceToXamlScale;
		var difference = Math.Abs(xaml.Value - expected);
		var tolerance = Math.Max(
			policy.AbsoluteTolerance,
			Math.Max(Math.Abs(expected), Math.Abs(xaml.Value))
				* policy.RelativeTolerance);
		var status = difference <= tolerance
			? ElementPropertyAuditStatus.Passed
			: ElementPropertyAuditStatus.ValueMismatch;
		return Result(
			phase,
			status,
			source,
			ElementPropertySlot<double>.FromValue(expected, policy.XamlUnit),
			xaml,
			difference,
			tolerance,
			status == ElementPropertyAuditStatus.Passed
				? "数值在允许误差内。"
				: "数值超过允许误差。");
	}

	private static ElementPropertyAuditPhaseResult<double> Result(
		ElementPropertyAuditPhase phase,
		ElementPropertyAuditStatus status,
		ElementPropertySlot<double> source = default,
		ElementPropertySlot<double> expectedXaml = default,
		ElementPropertySlot<double> actualXaml = default,
		double? difference = null,
		double? tolerance = null,
		string message = "") =>
		new(
			phase,
			status,
			source,
			expectedXaml,
			actualXaml,
			difference,
			tolerance,
			message);
}

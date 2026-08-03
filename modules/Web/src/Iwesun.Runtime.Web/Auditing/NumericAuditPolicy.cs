namespace Iwesun.Runtime.Web;

public sealed record NumericAuditPolicy
{
	public PropertyUnit SourceUnit { get; }
	public PropertyUnit XamlUnit { get; }
	public double SourceToXamlScale { get; }
	public double AbsoluteTolerance { get; }
	public double RelativeTolerance { get; }

	public NumericAuditPolicy(
		PropertyUnit sourceUnit,
		PropertyUnit xamlUnit,
		double absoluteTolerance,
		double relativeTolerance = 0,
		double sourceToXamlScale = 1)
	{
		RequireFiniteNonNegative(absoluteTolerance, nameof(absoluteTolerance));
		RequireFiniteNonNegative(relativeTolerance, nameof(relativeTolerance));
		if (!double.IsFinite(sourceToXamlScale) || sourceToXamlScale <= 0)
			throw new ArgumentOutOfRangeException(
				nameof(sourceToXamlScale),
				"源值到 XAML 值的比例必须是有限正数。");
		SourceUnit = sourceUnit;
		XamlUnit = xamlUnit;
		SourceToXamlScale = sourceToXamlScale;
		AbsoluteTolerance = absoluteTolerance;
		RelativeTolerance = relativeTolerance;
	}

	private static void RequireFiniteNonNegative(double value, string parameter)
	{
		if (!double.IsFinite(value) || value < 0)
			throw new ArgumentOutOfRangeException(
				parameter,
				"审核误差必须是有限非负数。");
	}
}

public sealed record NumericPropertyAuditDefinition
{
	public NumericAuditPolicy? StaticInitialization { get; }
	public NumericAuditPolicy? Runtime { get; }

	public bool RequiresStaticAudit => StaticInitialization is not null;
	public bool RequiresRuntimeAudit => Runtime is not null;

	public NumericPropertyAuditDefinition(
		NumericAuditPolicy? staticInitialization,
		NumericAuditPolicy? runtime)
	{
		if (staticInitialization is null && runtime is null)
			throw new ArgumentException("至少必须启用一种数值审核。");
		StaticInitialization = staticInitialization;
		Runtime = runtime;
	}

	public static NumericPropertyAuditDefinition RuntimeCssPixelToDip(
		double absoluteTolerance,
		double relativeTolerance = 0) =>
		new(
			null,
			new NumericAuditPolicy(
				PropertyUnit.CssPixel,
				PropertyUnit.Dip,
				absoluteTolerance,
				relativeTolerance));
}

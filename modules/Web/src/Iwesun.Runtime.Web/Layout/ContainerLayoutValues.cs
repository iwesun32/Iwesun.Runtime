namespace Iwesun.Runtime.Web;

public abstract record LayoutLength
{
	private LayoutLength()
	{
	}

	public sealed record Automatic : LayoutLength;

	public sealed record Stretch : LayoutLength;

	public sealed record Intrinsic : LayoutLength
	{
		public LayoutIntrinsicSize Kind { get; }

		public Intrinsic(LayoutIntrinsicSize kind)
		{
			Kind = kind;
		}
	}

	public sealed record FitContent : LayoutLength
	{
		public double Limit { get; }
		public LayoutLengthUnit Unit { get; }

		public FitContent(double limit, LayoutLengthUnit unit)
		{
			LayoutNumber.RequireFiniteNonNegative(limit, nameof(limit));
			Limit = limit;
			Unit = unit;
		}
	}

	public sealed record Constant : LayoutLength
	{
		public double Value { get; }
		public LayoutLengthUnit Unit { get; }

		public Constant(double value, LayoutLengthUnit unit)
		{
			LayoutNumber.RequireFinite(value, nameof(value));
			Value = value;
			Unit = unit;
		}
	}

	public sealed record Percentage : LayoutLength
	{
		public double Value { get; }
		public LayoutPercentageBasis Basis { get; }

		public Percentage(double value, LayoutPercentageBasis basis)
		{
			LayoutNumber.RequireFinite(value, nameof(value));
			Value = value;
			Basis = basis;
		}
	}

	public sealed record Fraction : LayoutLength
	{
		public double Value { get; }

		public Fraction(double value)
		{
			LayoutNumber.RequireFinite(value, nameof(value));
			if (value <= 0)
				throw new ArgumentOutOfRangeException(
					nameof(value),
					"网格分数必须大于零。");
			Value = value;
		}
	}

	public sealed record Calculation : LayoutLength
	{
		public LayoutCalculationExpression Expression { get; }

		public Calculation(LayoutCalculationExpression expression)
		{
			ArgumentNullException.ThrowIfNull(expression);
			Expression = expression;
		}
	}
}

public abstract record LayoutCalculationExpression
{
	private LayoutCalculationExpression()
	{
	}

	public sealed record ConstantTerm : LayoutCalculationExpression
	{
		public double Value { get; }
		public LayoutLengthUnit Unit { get; }

		public ConstantTerm(double value, LayoutLengthUnit unit)
		{
			LayoutNumber.RequireFinite(value, nameof(value));
			Value = value;
			Unit = unit;
		}
	}

	public sealed record PercentageTerm : LayoutCalculationExpression
	{
		public double Value { get; }
		public LayoutPercentageBasis Basis { get; }

		public PercentageTerm(double value, LayoutPercentageBasis basis)
		{
			LayoutNumber.RequireFinite(value, nameof(value));
			Value = value;
			Basis = basis;
		}
	}

	public sealed record Binary : LayoutCalculationExpression
	{
		public LayoutBinaryOperator Operator { get; }
		public LayoutCalculationExpression Left { get; }
		public LayoutCalculationExpression Right { get; }

		public Binary(
			LayoutBinaryOperator @operator,
			LayoutCalculationExpression left,
			LayoutCalculationExpression right)
		{
			ArgumentNullException.ThrowIfNull(left);
			ArgumentNullException.ThrowIfNull(right);
			Operator = @operator;
			Left = left;
			Right = right;
		}
	}
}

internal static class LayoutNumber
{
	public static void RequireFinite(double value, string parameter)
	{
		if (!double.IsFinite(value))
			throw new ArgumentOutOfRangeException(parameter, "布局数值必须是有限数。");
	}

	public static void RequireFiniteNonNegative(double? value, string parameter)
	{
		if (value is null)
			return;
		RequireFinite(value.Value, parameter);
		if (value.Value < 0)
			throw new ArgumentOutOfRangeException(parameter, "布局数值不能为负数。");
	}
}

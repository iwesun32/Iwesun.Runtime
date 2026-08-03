using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Iwesun.Runtime.Web.WinUI;

internal sealed class HtmlMeterControl : HtmlCursorProgressBar
{
	public double Low
	{
		get => (double)GetValue(LowProperty);
		set => SetValue(LowProperty, value);
	}

	public static readonly DependencyProperty LowProperty =
		DependencyProperty.Register(
			nameof(Low),
			typeof(double),
			typeof(HtmlMeterControl),
			new PropertyMetadata(0d));

	public double High
	{
		get => (double)GetValue(HighProperty);
		set => SetValue(HighProperty, value);
	}

	public static readonly DependencyProperty HighProperty =
		DependencyProperty.Register(
			nameof(High),
			typeof(double),
			typeof(HtmlMeterControl),
			new PropertyMetadata(1d));

	public double Optimum
	{
		get => (double)GetValue(OptimumProperty);
		set => SetValue(OptimumProperty, value);
	}

	public static readonly DependencyProperty OptimumProperty =
		DependencyProperty.Register(
			nameof(Optimum),
			typeof(double),
			typeof(HtmlMeterControl),
			new PropertyMetadata(0.5d));
}

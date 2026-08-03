using Iwesun.Runtime.Web;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI.Text;

namespace Iwesun.Runtime.Web.WinUI;

internal sealed class HtmlCssBoxGrid : Grid, IHtmlCursorTarget
{
	private string _cursorRule = "auto";
	private bool _containLayout;
	private bool _containSize;
	private bool _containInlineSize;
	private bool _containStyle;
	private double _cssWidth = double.NaN;
	private double _cssHeight = double.NaN;
	private double _cssMinWidth;
	private double _cssMinHeight;
	private double _cssMaxWidth = double.PositiveInfinity;
	private double _cssMaxHeight = double.PositiveInfinity;
	internal static readonly DependencyProperty LayoutModeProperty =
		Register(nameof(LayoutMode), "block");
	internal static readonly DependencyProperty FlexDirectionProperty =
		Register(nameof(FlexDirection), "row");
	internal static readonly DependencyProperty FlexWrapProperty =
		Register(nameof(FlexWrap), "nowrap");
	internal static readonly DependencyProperty JustifyContentProperty =
		Register(nameof(JustifyContent), "normal");
	internal static readonly DependencyProperty AlignContentProperty =
		Register(nameof(AlignContent), "normal");
	internal static readonly DependencyProperty AlignItemsProperty =
		Register(nameof(AlignItems), "normal");
	internal static readonly DependencyProperty GridAutoFlowProperty =
		Register(nameof(GridAutoFlow), "row");
	internal static readonly DependencyProperty GridAutoRowsProperty =
		Register(nameof(GridAutoRows), "auto");
	internal static readonly DependencyProperty GridAutoColumnsProperty =
		Register(nameof(GridAutoColumns), "auto");
	internal static readonly DependencyProperty GridTemplateAreasProperty =
		Register(nameof(GridTemplateAreas), "none");
	internal static readonly DependencyProperty GridTemplateRowsProperty =
		Register(nameof(GridTemplateRows), "none");
	internal static readonly DependencyProperty GridTemplateColumnsProperty =
		Register(nameof(GridTemplateColumns), "none");
	internal static readonly DependencyProperty TextLineHeightProperty =
		DependencyProperty.Register(
			nameof(TextLineHeight),
			typeof(double),
			typeof(HtmlCssBoxGrid),
			new PropertyMetadata(0d));
	internal static readonly DependencyProperty TextAlignmentProperty =
		DependencyProperty.Register(
			nameof(TextAlignment),
			typeof(TextAlignment),
			typeof(HtmlCssBoxGrid),
			new PropertyMetadata(TextAlignment.Left));
	internal static readonly DependencyProperty TextDecorationsProperty =
		DependencyProperty.Register(
			nameof(TextDecorations),
			typeof(TextDecorations),
			typeof(HtmlCssBoxGrid),
			new PropertyMetadata(TextDecorations.None));
	internal static readonly DependencyProperty WhiteSpaceProperty =
		Register(nameof(WhiteSpace), "normal");
	internal static readonly DependencyProperty ForegroundProperty =
		DependencyProperty.Register(
			nameof(Foreground),
			typeof(Brush),
			typeof(HtmlCssBoxGrid),
			new PropertyMetadata(null));
	internal static readonly DependencyProperty FontSizeProperty =
		DependencyProperty.Register(
			nameof(FontSize),
			typeof(double),
			typeof(HtmlCssBoxGrid),
			new PropertyMetadata(14d));
	internal static readonly DependencyProperty FontWeightProperty =
		DependencyProperty.Register(
			nameof(FontWeight),
			typeof(FontWeight),
			typeof(HtmlCssBoxGrid),
			new PropertyMetadata(new FontWeight { Weight = 400 }));
	internal static readonly DependencyProperty FontStyleProperty =
		DependencyProperty.Register(
			nameof(FontStyle),
			typeof(FontStyle),
			typeof(HtmlCssBoxGrid),
			new PropertyMetadata(FontStyle.Normal));
	internal static readonly DependencyProperty FontStretchProperty =
		DependencyProperty.Register(
			nameof(FontStretch),
			typeof(FontStretch),
			typeof(HtmlCssBoxGrid),
			new PropertyMetadata(FontStretch.Normal));
	internal static readonly DependencyProperty FontFamilyProperty =
		DependencyProperty.Register(
			nameof(FontFamily),
			typeof(FontFamily),
			typeof(HtmlCssBoxGrid),
			new PropertyMetadata(null));
	internal static readonly DependencyProperty HtmlIsEnabledProperty =
		DependencyProperty.Register(
			nameof(HtmlIsEnabled),
			typeof(bool),
			typeof(HtmlCssBoxGrid),
			new PropertyMetadata(true));
	internal static readonly DependencyProperty HtmlIsTabStopProperty =
		DependencyProperty.Register(
			nameof(HtmlIsTabStop),
			typeof(bool),
			typeof(HtmlCssBoxGrid),
			new PropertyMetadata(false));
	internal static readonly DependencyProperty HtmlTabIndexProperty =
		DependencyProperty.Register(
			nameof(HtmlTabIndex),
			typeof(int),
			typeof(HtmlCssBoxGrid),
			new PropertyMetadata(0));
	internal static readonly DependencyProperty HtmlHorizontalContentAlignmentProperty =
		DependencyProperty.Register(
			nameof(HtmlHorizontalContentAlignment),
			typeof(HorizontalAlignment),
			typeof(HtmlCssBoxGrid),
			new PropertyMetadata(HorizontalAlignment.Stretch));
	internal static readonly DependencyProperty HtmlVerticalContentAlignmentProperty =
		DependencyProperty.Register(
			nameof(HtmlVerticalContentAlignment),
			typeof(VerticalAlignment),
			typeof(HtmlCssBoxGrid),
			new PropertyMetadata(VerticalAlignment.Stretch));
	internal static readonly DependencyProperty TextOverflowProperty =
		Register(nameof(TextOverflow), "clip");
	internal static readonly DependencyProperty BoxSizingProperty =
		RegisterAttached("BoxSizing", "content-box");
	internal static readonly DependencyProperty RowGapProperty =
		Register(nameof(RowGap), 0d);
	internal static readonly DependencyProperty ColumnGapProperty =
		Register(nameof(ColumnGap), 0d);
	internal static readonly DependencyProperty FlexGrowProperty =
		RegisterAttached("FlexGrow", 0d);
	internal static readonly DependencyProperty FlexShrinkProperty =
		RegisterAttached("FlexShrink", 1d);
	internal static readonly DependencyProperty FlexBasisProperty =
		RegisterAttached("FlexBasis", "auto");
	internal static readonly DependencyProperty AlignSelfProperty =
		RegisterAttached("AlignSelf", "auto");
	internal static readonly DependencyProperty OrderProperty =
		RegisterAttached("Order", 0);
	internal static readonly DependencyProperty GridRowAutoProperty =
		RegisterAttached("GridRowAuto", false);
	internal static readonly DependencyProperty GridColumnAutoProperty =
		RegisterAttached("GridColumnAuto", false);
	internal static readonly DependencyProperty GridRowExpressionProperty =
		RegisterAttached("GridRowExpression", string.Empty);
	internal static readonly DependencyProperty GridColumnExpressionProperty =
		RegisterAttached("GridColumnExpression", string.Empty);
	internal static readonly DependencyProperty GridAreaExpressionProperty =
		RegisterAttached("GridAreaExpression", string.Empty);

	internal static void SetBoxSizing(DependencyObject target, string value) =>
		target.SetValue(BoxSizingProperty, value);

	internal static string GetBoxSizing(DependencyObject target) =>
		(string)target.GetValue(BoxSizingProperty);

	internal HtmlCssBoxGrid()
	{
		MinWidth = 0;
		MinHeight = 0;
	}

	internal Grid LayoutRoot => this;

	internal double CssMinWidth => _cssMinWidth;
	internal double CssMinHeight => _cssMinHeight;
	internal double CssMaxWidth => _cssMaxWidth;
	internal double CssMaxHeight => _cssMaxHeight;

	internal void ApplyCssWidth(double value)
	{
		_cssWidth = value;
		ApplyCssHorizontalSizing();
	}

	internal void ApplyCssHeight(double value)
	{
		_cssHeight = value;
		ApplyCssVerticalSizing();
	}

	internal void ApplyCssMinWidth(double value)
	{
		_cssMinWidth = value;
		ApplyCssHorizontalSizing();
	}

	internal void ApplyCssMinHeight(double value)
	{
		_cssMinHeight = value;
		ApplyCssVerticalSizing();
	}

	internal void ApplyCssMaxWidth(double value)
	{
		_cssMaxWidth = value;
		ApplyCssHorizontalSizing();
	}

	internal void ApplyCssMaxHeight(double value)
	{
		_cssMaxHeight = value;
		ApplyCssVerticalSizing();
	}

	private void ApplyCssHorizontalSizing()
	{
		var used = ResolveDefiniteCssSize(
			_cssWidth,
			_cssMinWidth,
			_cssMaxWidth);
		Width = used;
		MinWidth = double.IsFinite(used) ? used : _cssMinWidth;
		MaxWidth = double.IsFinite(used) ? used : _cssMaxWidth;
	}

	private void ApplyCssVerticalSizing()
	{
		var used = ResolveDefiniteCssSize(
			_cssHeight,
			_cssMinHeight,
			_cssMaxHeight);
		Height = used;
		MinHeight = double.IsFinite(used) ? used : _cssMinHeight;
		MaxHeight = double.IsFinite(used) ? used : _cssMaxHeight;
	}

	private static double ResolveDefiniteCssSize(
		double preferred,
		double minimum,
		double maximum)
	{
		if (!double.IsFinite(preferred))
			return double.NaN;
		var bounded = double.IsFinite(maximum)
			? Math.Min(preferred, maximum)
			: preferred;
		return Math.Max(minimum, bounded);
	}

	internal void ApplyCssBorderThickness(Thickness value)
	{
		BorderThickness = value;
	}

	internal bool ContainLayout => _containLayout;
	internal bool ContainSize => _containSize;
	internal bool ContainInlineSize => _containInlineSize;
	internal bool ContainStyle => _containStyle;

	internal void ApplyContainment(
		bool layout,
		bool size,
		bool inlineSize,
		bool style)
	{
		_containLayout = layout;
		_containSize = size;
		_containInlineSize = inlineSize;
		_containStyle = style;
		InvalidateMeasure();
	}

	protected override Windows.Foundation.Size MeasureOverride(
		Windows.Foundation.Size availableSize)
	{
		var measured = base.MeasureOverride(availableSize);
		if (!_containSize && !_containInlineSize)
			return measured;
		var chromeWidth = Padding.Left + Padding.Right
			+ BorderThickness.Left + BorderThickness.Right;
		var chromeHeight = Padding.Top + Padding.Bottom
			+ BorderThickness.Top + BorderThickness.Bottom;
		return new(
			_containSize || _containInlineSize ? chromeWidth : measured.Width,
			_containSize ? chromeHeight : measured.Height);
	}

	public void ApplyCursor(string rule)
	{
		var cursor = HtmlCursorContract.Create(rule);
		_cursorRule = cursor.Rule;
		ProtectedCursor = cursor.Cursor;
	}

	public bool TryReadCursor(out string value)
	{
		value = string.Empty;
		if (!HtmlCursorContract.Matches(
			_cursorRule,
			ProtectedCursor))
			return false;
		value = _cursorRule;
		return true;
	}

	internal string LayoutMode
	{
		get => (string)GetValue(LayoutModeProperty);
		set => SetValue(LayoutModeProperty, value);
	}

	internal string FlexDirection
	{
		get => (string)GetValue(FlexDirectionProperty);
		set => SetValue(FlexDirectionProperty, value);
	}

	internal string FlexWrap
	{
		get => (string)GetValue(FlexWrapProperty);
		set => SetValue(FlexWrapProperty, value);
	}

	internal string JustifyContent
	{
		get => (string)GetValue(JustifyContentProperty);
		set => SetValue(JustifyContentProperty, value);
	}

	internal string AlignContent
	{
		get => (string)GetValue(AlignContentProperty);
		set => SetValue(AlignContentProperty, value);
	}

	internal string AlignItems
	{
		get => (string)GetValue(AlignItemsProperty);
		set => SetValue(AlignItemsProperty, value);
	}

	internal string GridAutoFlow
	{
		get => (string)GetValue(GridAutoFlowProperty);
		set => SetValue(GridAutoFlowProperty, value);
	}

	internal string GridAutoRows
	{
		get => (string)GetValue(GridAutoRowsProperty);
		set => SetValue(GridAutoRowsProperty, value);
	}

	internal string GridAutoColumns
	{
		get => (string)GetValue(GridAutoColumnsProperty);
		set => SetValue(GridAutoColumnsProperty, value);
	}

	internal string GridTemplateAreas
	{
		get => (string)GetValue(GridTemplateAreasProperty);
		set => SetValue(GridTemplateAreasProperty, value);
	}

	internal string GridTemplateRows
	{
		get => (string)GetValue(GridTemplateRowsProperty);
		set => SetValue(GridTemplateRowsProperty, value);
	}

	internal string GridTemplateColumns
	{
		get => (string)GetValue(GridTemplateColumnsProperty);
		set => SetValue(GridTemplateColumnsProperty, value);
	}

	internal double TextLineHeight
	{
		get => (double)GetValue(TextLineHeightProperty);
		set => SetValue(TextLineHeightProperty, value);
	}

	internal TextAlignment TextAlignment
	{
		get => (TextAlignment)GetValue(TextAlignmentProperty);
		set => SetValue(TextAlignmentProperty, value);
	}

	internal TextDecorations TextDecorations
	{
		get => (TextDecorations)GetValue(TextDecorationsProperty);
		set => SetValue(TextDecorationsProperty, value);
	}

	internal string WhiteSpace
	{
		get => (string)GetValue(WhiteSpaceProperty);
		set => SetValue(WhiteSpaceProperty, value);
	}

	internal Brush? Foreground
	{
		get => (Brush?)GetValue(ForegroundProperty);
		set => SetValue(ForegroundProperty, value);
	}

	internal double FontSize
	{
		get => (double)GetValue(FontSizeProperty);
		set => SetValue(FontSizeProperty, value);
	}

	internal FontWeight FontWeight
	{
		get => (FontWeight)GetValue(FontWeightProperty);
		set => SetValue(FontWeightProperty, value);
	}

	internal FontStyle FontStyle
	{
		get => (FontStyle)GetValue(FontStyleProperty);
		set => SetValue(FontStyleProperty, value);
	}

	internal FontStretch FontStretch
	{
		get => (FontStretch)GetValue(FontStretchProperty);
		set => SetValue(FontStretchProperty, value);
	}

	internal FontFamily? FontFamily
	{
		get => (FontFamily?)GetValue(FontFamilyProperty);
		set => SetValue(FontFamilyProperty, value);
	}

	internal bool HtmlIsEnabled
	{
		get => (bool)GetValue(HtmlIsEnabledProperty);
		set => SetValue(HtmlIsEnabledProperty, value);
	}

	internal bool HtmlIsTabStop
	{
		get => (bool)GetValue(HtmlIsTabStopProperty);
		set => SetValue(HtmlIsTabStopProperty, value);
	}

	internal int HtmlTabIndex
	{
		get => (int)GetValue(HtmlTabIndexProperty);
		set => SetValue(HtmlTabIndexProperty, value);
	}

	internal HorizontalAlignment HtmlHorizontalContentAlignment
	{
		get => (HorizontalAlignment)GetValue(HtmlHorizontalContentAlignmentProperty);
		set => SetValue(HtmlHorizontalContentAlignmentProperty, value);
	}

	internal VerticalAlignment HtmlVerticalContentAlignment
	{
		get => (VerticalAlignment)GetValue(HtmlVerticalContentAlignmentProperty);
		set => SetValue(HtmlVerticalContentAlignmentProperty, value);
	}

	internal string TextOverflow
	{
		get => (string)GetValue(TextOverflowProperty);
		set => SetValue(TextOverflowProperty, value);
	}

	internal double RowGap
	{
		get => (double)GetValue(RowGapProperty);
		set => SetValue(RowGapProperty, value);
	}

	internal double ColumnGap
	{
		get => (double)GetValue(ColumnGapProperty);
		set => SetValue(ColumnGapProperty, value);
	}

	internal void AddChild(
		UIElement child,
		XamlElementLayoutPlacement? placement)
	{
		ArgumentNullException.ThrowIfNull(child);
		HtmlContainingBlockChildHost.Attach(
			LayoutRoot,
			this,
			child,
			placement);
	}

	internal static double GetFlexGrow(DependencyObject element) =>
		(double)element.GetValue(FlexGrowProperty);

	internal static void SetFlexGrow(DependencyObject element, double value) =>
		element.SetValue(FlexGrowProperty, value);

	internal static double GetFlexShrink(DependencyObject element) =>
		(double)element.GetValue(FlexShrinkProperty);

	internal static void SetFlexShrink(DependencyObject element, double value) =>
		element.SetValue(FlexShrinkProperty, value);

	internal static string GetFlexBasis(DependencyObject element) =>
		(string)element.GetValue(FlexBasisProperty);

	internal static void SetFlexBasis(DependencyObject element, string value) =>
		element.SetValue(FlexBasisProperty, value);

	internal static string GetAlignSelf(DependencyObject element) =>
		(string)element.GetValue(AlignSelfProperty);

	internal static void SetAlignSelf(DependencyObject element, string value) =>
		element.SetValue(AlignSelfProperty, value);

	internal static int GetOrder(DependencyObject element) =>
		(int)element.GetValue(OrderProperty);

	internal static void SetOrder(DependencyObject element, int value) =>
		element.SetValue(OrderProperty, value);

	internal static bool GetGridRowAuto(DependencyObject element) =>
		(bool)element.GetValue(GridRowAutoProperty);

	internal static void SetGridRowAuto(DependencyObject element, bool value) =>
		element.SetValue(GridRowAutoProperty, value);

	internal static bool GetGridColumnAuto(DependencyObject element) =>
		(bool)element.GetValue(GridColumnAutoProperty);

	internal static void SetGridColumnAuto(
		DependencyObject element,
		bool value) => element.SetValue(GridColumnAutoProperty, value);

	internal static string GetGridRowExpression(DependencyObject element) =>
		(string)element.GetValue(GridRowExpressionProperty);

	internal static void SetGridRowExpression(
		DependencyObject element,
		string value) => element.SetValue(GridRowExpressionProperty, value);

	internal static string GetGridColumnExpression(DependencyObject element) =>
		(string)element.GetValue(GridColumnExpressionProperty);

	internal static void SetGridColumnExpression(
		DependencyObject element,
		string value) => element.SetValue(GridColumnExpressionProperty, value);

	internal static string GetGridAreaExpression(DependencyObject element) =>
		(string)element.GetValue(GridAreaExpressionProperty);

	internal static void SetGridAreaExpression(
		DependencyObject element,
		string value) => element.SetValue(GridAreaExpressionProperty, value);

	private static DependencyProperty Register(string name, string value) =>
		DependencyProperty.Register(
			name,
			typeof(string),
			typeof(HtmlCssBoxGrid),
			new PropertyMetadata(value));

	private static DependencyProperty Register(string name, double value) =>
		DependencyProperty.Register(
			name,
			typeof(double),
			typeof(HtmlCssBoxGrid),
			new PropertyMetadata(value));

	private static DependencyProperty RegisterAttached(
		string name,
		object value) =>
		DependencyProperty.RegisterAttached(
			name,
			value.GetType(),
			typeof(HtmlCssBoxGrid),
			new PropertyMetadata(value));
}

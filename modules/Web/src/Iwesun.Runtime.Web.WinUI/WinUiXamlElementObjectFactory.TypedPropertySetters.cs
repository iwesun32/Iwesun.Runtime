using Iwesun.Runtime.Web;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Media.Core;

namespace Iwesun.Runtime.Web.WinUI;

public sealed partial class WinUiXamlElementObjectFactory
{
	private delegate void StrongPropertySetter(
		DependencyObject target,
		string value,
		DomElement? source);

	private static readonly IReadOnlyDictionary<
		string,
		IReadOnlyList<StrongPropertySetter>> StrongPropertySetters =
		CreateStrongPropertySetters();

	private static IReadOnlyDictionary<
		string,
		IReadOnlyList<StrongPropertySetter>> CreateStrongPropertySetters()
	{
		var setters = new Dictionary<
			string,
			List<StrongPropertySetter>>(StringComparer.Ordinal);

		void Add<T>(string name, Action<T, string, DomElement?> setter)
			where T : DependencyObject
		{
			if (!setters.TryGetValue(name, out var candidates))
			{
				candidates = [];
				setters.Add(name, candidates);
			}
			candidates.Add((target, value, source) =>
			{
				if (target is not T typed)
					throw new StrongSetterTargetMismatchException();
				setter(typed, value, source);
			});
		}

		Add<DependencyObject>("AutomationProperties.AutomationId",
			static (target, value, _) =>
				AutomationProperties.SetAutomationId(target, value));
		Add<DependencyObject>("AutomationProperties.Name",
			static (target, value, _) => AutomationProperties.SetName(target, value));
		Add<DependencyObject>("AutomationProperties.ItemStatus",
			static (target, value, _) =>
				AutomationProperties.SetItemStatus(target, value));
		Add<DependencyObject>("ToolTipService.ToolTip",
			static (target, value, _) => ToolTipService.SetToolTip(target, value));
		Add<FrameworkElement>("Grid.Row",
			static (target, value, _) => Grid.SetRow(target, ParseInt(value)));
		Add<FrameworkElement>("Grid.Column",
			static (target, value, _) => Grid.SetColumn(target, ParseInt(value)));
		Add<FrameworkElement>("Grid.RowSpan",
			static (target, value, _) => Grid.SetRowSpan(target, ParseInt(value)));
		Add<FrameworkElement>("Grid.ColumnSpan",
			static (target, value, _) => Grid.SetColumnSpan(target, ParseInt(value)));
		Add<UIElement>("Canvas.Left",
			static (target, value, _) => Canvas.SetLeft(target, ParseDouble(value)));
		Add<UIElement>("Canvas.Top",
			static (target, value, _) => Canvas.SetTop(target, ParseDouble(value)));
		Add<UIElement>("Canvas.ZIndex",
			static (target, value, _) =>
			{
				if (!value.Equals("auto", StringComparison.OrdinalIgnoreCase))
					Canvas.SetZIndex(target, ParseInt(value));
			});
		Add<DependencyObject>("HtmlTable.ColumnSpan",
			static (target, value, _) =>
				HtmlTableRowPanel.SetColumnSpan(target, ParseInt(value)));
		Add<DependencyObject>("HtmlTable.RowSpan",
			static (target, value, _) =>
				HtmlTableRowPanel.SetRowSpan(target, ParseInt(value)));
		Add<DependencyObject>("HtmlValidation.IsValid",
			static (target, value, _) =>
				HtmlValidation.SetIsValid(target, ParseBool(value)));
		Add<DependencyObject>("HtmlValidation.WillValidate",
			static (target, value, _) =>
				HtmlValidation.SetWillValidate(target, ParseBool(value)));
		Add<DependencyObject>("HtmlValidation.ValidationMessage",
			static (target, value, _) =>
				HtmlValidation.SetValidationMessage(target, value));
		Add<DependencyObject>("HtmlFormState.IsFormOwner",
			static (target, value, _) =>
				HtmlFormState.SetIsFormOwner(target, ParseBool(value)));
		Add<DependencyObject>("HtmlFormState.FormOwnerId",
			static (target, value, _) => HtmlFormState.SetFormOwnerId(target, value));
		Add<DependencyObject>("HtmlFormState.InitialValue",
			static (target, value, _) => HtmlFormState.SetInitialValue(target, value));
		Add<DependencyObject>("HtmlFormState.InitialChecked",
			static (target, value, _) =>
				HtmlFormState.SetInitialChecked(target, ParseBool(value)));
		Add<DependencyObject>("HtmlFormState.InitialSelected",
			static (target, value, _) =>
				HtmlFormState.SetInitialSelected(target, ParseBool(value)));

		Add<FrameworkElement>("Width", static (target, value, _) =>
		{
			var parsed = IsCssAutomaticSize(value) ? double.NaN : ParseDouble(value);
			if (target is HtmlCssBoxGrid box)
				box.ApplyCssWidth(parsed);
			else
				target.Width = parsed;
		});
		Add<FrameworkElement>("Height", static (target, value, _) =>
		{
			var parsed = IsCssAutomaticSize(value) ? double.NaN : ParseDouble(value);
			if (target is HtmlCssBoxGrid box)
				box.ApplyCssHeight(parsed);
			else
				target.Height = parsed;
		});
		Add<FrameworkElement>("MinWidth", static (target, value, _) =>
		{
			var parsed = IsCssAutomaticSize(value) ? 0 : ParseDouble(value);
			if (target is HtmlCssBoxGrid box)
				box.ApplyCssMinWidth(parsed);
			else
				target.MinWidth = parsed;
		});
		Add<FrameworkElement>("MinHeight", static (target, value, _) =>
		{
			var parsed = IsCssAutomaticSize(value) ? 0 : ParseDouble(value);
			if (target is HtmlCssBoxGrid box)
				box.ApplyCssMinHeight(parsed);
			else
				target.MinHeight = parsed;
		});
		Add<FrameworkElement>("MaxWidth", static (target, value, _) =>
		{
			var parsed = IsCssUnboundedMaximum(value)
				? double.PositiveInfinity
				: ParseDouble(value);
			if (target is HtmlCssBoxGrid box)
				box.ApplyCssMaxWidth(parsed);
			else
				target.MaxWidth = parsed;
		});
		Add<FrameworkElement>("MaxHeight", static (target, value, _) =>
		{
			var parsed = IsCssUnboundedMaximum(value)
				? double.PositiveInfinity
				: ParseDouble(value);
			if (target is HtmlCssBoxGrid box)
				box.ApplyCssMaxHeight(parsed);
			else
				target.MaxHeight = parsed;
		});
		Add<FrameworkElement>("Tag", static (target, value, _) => target.Tag = value);
		Add<FrameworkElement>("DataContext", static (target, value, _) =>
			target.DataContext = value);
		Add<FrameworkElement>("Margin", static (target, value, _) =>
			target.Margin = ParseThickness(value));
		Add<FrameworkElement>("HorizontalAlignment", static (target, value, _) =>
			target.HorizontalAlignment = Enum.Parse<HorizontalAlignment>(value, true));
		Add<FrameworkElement>("VerticalAlignment", static (target, value, _) =>
			target.VerticalAlignment = Enum.Parse<VerticalAlignment>(value, true));
		Add<FrameworkElement>("Language", static (target, value, _) =>
			target.Language = value);
		Add<FrameworkElement>("FlowDirection", static (target, value, _) =>
			target.FlowDirection = Enum.Parse<FlowDirection>(value, true));
		Add<UIElement>("Opacity", static (target, value, _) =>
			target.Opacity = ParseDouble(value));
		Add<UIElement>("Visibility", static (target, value, _) =>
			target.Visibility = Enum.Parse<Visibility>(value, true));
		Add<UIElement>("IsHitTestVisible", static (target, value, _) =>
			target.IsHitTestVisible = ParseBool(value));
		Add<FrameworkElement>("HtmlBoxShadow.Value", static (target, value, _) =>
			WinUiBoxShadowBehavior.Apply(target, value));
		Add<FrameworkElement>("HtmlFilter.Filter", static (target, value, _) =>
			WinUiFilterBehavior.Apply(target, value));
		Add<FrameworkElement>("HtmlBackdropFilter.Filter", static (target, value, _) =>
			WinUiBackdropFilterBehavior.Apply(target, value));
		Add<FrameworkElement>("HtmlTextTransform.Rule", static (target, value, source) =>
			WinUiTextTransformBehavior.Apply(
				target,
				value,
				GeneratedOwnTextMarker(source)));
		Add<FrameworkElement>("HtmlTextShadow.Value", static (target, value, source) =>
			WinUiTextShadowBehavior.Apply(
				target,
				value,
				GeneratedOwnTextMarker(source)));
		Add<FrameworkElement>("HtmlContentVisibility.Rule", static (target, value, _) =>
			WinUiContentVisibilityBehavior.Apply(target, value));
		Add<FrameworkElement>("HtmlClipPath.Rule", static (target, value, _) =>
			WinUiClipPathBehavior.Apply(target, value, 1, 1));
		Add<FrameworkElement>("HtmlColorScheme.Rule", static (target, value, _) =>
			target.RequestedTheme = value.Trim().ToLowerInvariant() switch
			{
				"dark" => ElementTheme.Dark,
				"light" => ElementTheme.Light,
				_ => ElementTheme.Default
			});
		Add<FrameworkElement>("HtmlTransition.Property", static (target, value, _) =>
			WinUiCssTransitionBehavior.ApplyMetadata(target, "transition-property", value));
		Add<FrameworkElement>("HtmlTransition.Duration", static (target, value, _) =>
			WinUiCssTransitionBehavior.ApplyMetadata(target, "transition-duration", value));
		Add<FrameworkElement>("HtmlTransition.Delay", static (target, value, _) =>
			WinUiCssTransitionBehavior.ApplyMetadata(target, "transition-delay", value));
		Add<FrameworkElement>("HtmlTransition.TimingFunction", static (target, value, _) =>
			WinUiCssTransitionBehavior.ApplyMetadata(
				target,
				"transition-timing-function",
				value));
		Add<FrameworkElement>("HtmlTransform.Value", static (target, value, source) =>
			WinUiTransformStyleBehavior.ApplyTransform(target, value, source));
		Add<FrameworkElement>("HtmlTransform.Origin", static (target, value, source) =>
			WinUiTransformStyleBehavior.ApplyOrigin(target, value, source));
		Add<FrameworkElement>("HtmlPosition.Left", static (target, value, source) =>
			WinUiTransformStyleBehavior.ApplyRelativeOffset(
				target,
				value,
				source,
				horizontal: true));
		Add<FrameworkElement>("HtmlPosition.Top", static (target, value, source) =>
			WinUiTransformStyleBehavior.ApplyRelativeOffset(
				target,
				value,
				source,
				horizontal: false));
		Add<FrameworkElement>("HtmlPosition.Right", static (target, value, source) =>
			WinUiTransformStyleBehavior.ApplyRelativeOffset(
				target,
				value,
				source,
				horizontal: true,
				reverse: true));
		Add<FrameworkElement>("HtmlPosition.Bottom", static (target, value, source) =>
			WinUiTransformStyleBehavior.ApplyRelativeOffset(
				target,
				value,
				source,
				horizontal: false,
				reverse: true));

		Add<Control>("IsEnabled", static (target, value, _) =>
			target.IsEnabled = ParseBool(value));
		Add<Control>("IsTabStop", static (target, value, _) =>
			target.IsTabStop = ParseBool(value));
		Add<Control>("TabIndex", static (target, value, _) =>
			target.TabIndex = ParseInt(value));
		Add<Control>("Background", static (target, value, _) =>
			target.Background = ParseBrush(value));
		Add<Control>("BorderBrush", static (target, value, _) =>
			target.BorderBrush = ParseBrush(value));
		Add<Control>("BorderThickness", static (target, value, _) =>
			target.BorderThickness = ParseThickness(value));
		Add<Control>("CornerRadius", static (target, value, _) =>
			target.CornerRadius = ParseCornerRadius(value));
		Add<Control>("Foreground", static (target, value, _) =>
			target.Foreground = ParseBrush(value));
		Add<Control>("FontSize", static (target, value, _) =>
			target.FontSize = ParseDouble(value));
		Add<Control>("FontWeight", static (target, value, _) =>
			target.FontWeight = ParseFontWeight(value));
		Add<Control>("FontStyle", static (target, value, _) =>
			target.FontStyle = Enum.Parse<Windows.UI.Text.FontStyle>(value, true));
		Add<Control>("FontStretch", static (target, value, _) =>
			target.FontStretch = Enum.Parse<Windows.UI.Text.FontStretch>(value, true));
		Add<Control>("FontFamily", static (target, value, _) =>
			target.FontFamily = new FontFamily(value));
		Add<Control>("Padding", static (target, value, _) =>
			target.Padding = ParseThickness(value));
		Add<Control>("HorizontalContentAlignment", static (target, value, _) =>
			target.HorizontalContentAlignment = Enum.Parse<HorizontalAlignment>(value, true));
		Add<Control>("VerticalContentAlignment", static (target, value, _) =>
			target.VerticalContentAlignment = Enum.Parse<VerticalAlignment>(value, true));

		Add<Panel>("Background", static (target, value, _) =>
			target.Background = ParseBrush(value));
		Add<HtmlCssBoxGrid>("BorderBrush", static (target, value, _) =>
			target.BorderBrush = ParseBrush(value));
		Add<HtmlCssBoxGrid>("BorderThickness", static (target, value, _) =>
			target.ApplyCssBorderThickness(ParseThickness(value)));
		Add<HtmlCssBoxGrid>("CornerRadius", static (target, value, _) =>
			target.CornerRadius = ParseCornerRadius(value));
		Add<HtmlCssBoxGrid>("Padding", static (target, value, _) =>
			target.Padding = ParseThickness(value));
		Add<HtmlCssBoxGrid>("Foreground", static (target, value, _) =>
			target.Foreground = ParseBrush(value));
		Add<HtmlCssBoxGrid>("FontSize", static (target, value, _) =>
			target.FontSize = ParseDouble(value));
		Add<HtmlCssBoxGrid>("FontWeight", static (target, value, _) =>
			target.FontWeight = ParseFontWeight(value));
		Add<HtmlCssBoxGrid>("FontStyle", static (target, value, _) =>
			target.FontStyle = Enum.Parse<Windows.UI.Text.FontStyle>(value, true));
		Add<HtmlCssBoxGrid>("FontStretch", static (target, value, _) =>
			target.FontStretch = Enum.Parse<Windows.UI.Text.FontStretch>(value, true));
		Add<HtmlCssBoxGrid>("FontFamily", static (target, value, _) =>
			target.FontFamily = new FontFamily(value));
		Add<HtmlCssBoxGrid>("IsEnabled", static (target, value, _) =>
			target.HtmlIsEnabled = ParseBool(value));
		Add<HtmlCssBoxGrid>("IsTabStop", static (target, value, _) =>
			target.HtmlIsTabStop = ParseBool(value));
		Add<HtmlCssBoxGrid>("TabIndex", static (target, value, _) =>
			target.HtmlTabIndex = ParseInt(value));
		Add<HtmlCssBoxGrid>("HorizontalContentAlignment", static (target, value, _) =>
			target.HtmlHorizontalContentAlignment =
				Enum.Parse<HorizontalAlignment>(value, true));
		Add<HtmlCssBoxGrid>("VerticalContentAlignment", static (target, value, _) =>
			target.HtmlVerticalContentAlignment =
				Enum.Parse<VerticalAlignment>(value, true));
		Add<Border>("Background", static (target, value, _) =>
			target.Background = ParseBrush(value));
		Add<Border>("BorderBrush", static (target, value, _) =>
			target.BorderBrush = ParseBrush(value));
		Add<Border>("BorderThickness", static (target, value, _) =>
			target.BorderThickness = ParseThickness(value));
		Add<Border>("CornerRadius", static (target, value, _) =>
			target.CornerRadius = ParseCornerRadius(value));
		Add<Border>("Padding", static (target, value, _) =>
			target.Padding = ParseThickness(value));
		Add<Grid>("RowSpacing", static (target, value, _) =>
			target.RowSpacing = ParseDouble(value));
		Add<Grid>("ColumnSpacing", static (target, value, _) =>
			target.ColumnSpacing = ParseDouble(value));
		Add<StackPanel>("Spacing", static (target, value, _) =>
			target.Spacing = ParseDouble(value));
		Add<StackPanel>("Orientation", static (target, value, _) =>
			target.Orientation = Enum.Parse<Orientation>(value, true));
		Add<HtmlCssBoxGrid>("RowGap", static (target, value, _) =>
			target.RowGap = ParseDouble(value));
		Add<HtmlCssBoxGrid>("ColumnGap", static (target, value, _) =>
			target.ColumnGap = ParseDouble(value));
		Add<HtmlCssBoxGrid>("LayoutMode", static (target, value, _) =>
			target.LayoutMode = value);
		Add<HtmlCssBoxGrid>("FlexDirection", static (target, value, _) =>
			target.FlexDirection = value);
		Add<HtmlCssBoxGrid>("FlexWrap", static (target, value, _) =>
			target.FlexWrap = value);
		Add<HtmlCssBoxGrid>("JustifyContent", static (target, value, _) =>
			target.JustifyContent = value);
		Add<HtmlCssBoxGrid>("AlignContent", static (target, value, _) =>
			target.AlignContent = value);
		Add<HtmlCssBoxGrid>("AlignItems", static (target, value, _) =>
			target.AlignItems = value);
		Add<HtmlCssBoxGrid>("GridAutoFlow", static (target, value, _) =>
			target.GridAutoFlow = value);
		Add<HtmlCssBoxGrid>("GridAutoRows", static (target, value, _) =>
			target.GridAutoRows = value);
		Add<HtmlCssBoxGrid>("GridAutoColumns", static (target, value, _) =>
			target.GridAutoColumns = value);
		Add<HtmlCssBoxGrid>("GridTemplateAreas", static (target, value, _) =>
			target.GridTemplateAreas = value);
		Add<HtmlCssBoxGrid>("GridTemplateRows", static (target, value, _) =>
			target.GridTemplateRows = value);
		Add<HtmlCssBoxGrid>("GridTemplateColumns", static (target, value, _) =>
			target.GridTemplateColumns = value);
		Add<HtmlCssBoxGrid>("TextLineHeight", static (target, value, _) =>
			target.TextLineHeight = ParseDouble(value));
		Add<HtmlCssBoxGrid>("TextAlignment", static (target, value, _) =>
			target.TextAlignment = Enum.Parse<TextAlignment>(value, true));
		Add<HtmlCssBoxGrid>("TextDecorations", static (target, value, _) =>
			target.TextDecorations = Enum.Parse<Windows.UI.Text.TextDecorations>(value, true));
		Add<HtmlCssBoxGrid>("WhiteSpace", static (target, value, _) =>
			target.WhiteSpace = value);
		Add<HtmlCssBoxGrid>("TextOverflow", static (target, value, _) =>
			target.TextOverflow = value);
		Add<DependencyObject>("HtmlCssBoxGrid.FlexGrow", static (target, value, _) =>
			HtmlCssBoxGrid.SetFlexGrow(target, ParseDouble(value)));
		Add<DependencyObject>("HtmlCssBoxGrid.FlexShrink", static (target, value, _) =>
			HtmlCssBoxGrid.SetFlexShrink(target, ParseDouble(value)));
		Add<DependencyObject>("HtmlCssBoxGrid.FlexBasis", static (target, value, _) =>
			HtmlCssBoxGrid.SetFlexBasis(target, value));
		Add<DependencyObject>("HtmlCssBoxGrid.AlignSelf", static (target, value, _) =>
			HtmlCssBoxGrid.SetAlignSelf(target, value));
		Add<DependencyObject>("HtmlCssBoxGrid.Order", static (target, value, _) =>
			HtmlCssBoxGrid.SetOrder(target, ParseInt(value)));
		Add<DependencyObject>("HtmlCssBoxGrid.GridRowExpression", static (target, value, _) =>
			HtmlCssBoxGrid.SetGridRowExpression(target, value));
		Add<DependencyObject>("HtmlCssBoxGrid.GridColumnExpression", static (target, value, _) =>
			HtmlCssBoxGrid.SetGridColumnExpression(target, value));
		Add<DependencyObject>("HtmlCssBoxGrid.GridAreaExpression", static (target, value, _) =>
			HtmlCssBoxGrid.SetGridAreaExpression(target, value));
		Add<HtmlTablePanelBase>("RowSpacing", static (target, value, _) =>
			target.RowSpacing = ParseDouble(value));
		Add<HtmlTablePanelBase>("ColumnSpacing", static (target, value, _) =>
			target.ColumnSpacing = ParseDouble(value));

		Add<TextBlock>("Text", static (target, value, _) => target.Text = value);
		Add<TextBlock>("TextWrapping", static (target, value, _) =>
			target.TextWrapping = Enum.Parse<TextWrapping>(value, true));
		Add<TextBlock>("TextAlignment", static (target, value, _) =>
			target.TextAlignment = Enum.Parse<TextAlignment>(value, true));
		Add<TextBlock>("Foreground", static (target, value, _) =>
			target.Foreground = ParseBrush(value));
		Add<TextBlock>("FontSize", static (target, value, _) =>
			target.FontSize = ParseDouble(value));
		Add<TextBlock>("FontWeight", static (target, value, _) =>
			target.FontWeight = ParseFontWeight(value));
		Add<TextBlock>("FontStyle", static (target, value, _) =>
			target.FontStyle = Enum.Parse<Windows.UI.Text.FontStyle>(value, true));
		Add<TextBlock>("FontStretch", static (target, value, _) =>
			target.FontStretch = Enum.Parse<Windows.UI.Text.FontStretch>(value, true));
		Add<TextBlock>("FontFamily", static (target, value, _) =>
			target.FontFamily = new FontFamily(value));
		Add<TextBlock>("LineHeight", static (target, value, _) =>
			target.LineHeight = ParseDouble(value));
		Add<TextBlock>("TextDecorations", static (target, value, _) =>
			target.TextDecorations = Enum.Parse<Windows.UI.Text.TextDecorations>(value, true));
		Add<TextBlock>("TextTrimming", static (target, value, _) =>
			target.TextTrimming = Enum.Parse<TextTrimming>(value, true));
		Add<HtmlVerticalTextControl>("Text", static (target, value, _) =>
			target.Text = value);
		Add<HtmlVerticalTextControl>("WritingMode", static (target, value, _) =>
			target.WritingMode = value);
		Add<HtmlVerticalTextControl>("TextIndent", static (target, value, _) =>
			target.TextIndent = ParseDouble(value));
		Add<HtmlVerticalTextControl>("WordSpacing", static (target, value, _) =>
			target.WordSpacing = ParseDouble(value));
		Add<HtmlVerticalTextControl>("TextLineHeight", static (target, value, _) =>
			target.TextLineHeight = ParseDouble(value));
		Add<HtmlVerticalTextControl>("TextAlignment", static (target, value, _) =>
			target.TextAlignment = Enum.Parse<TextAlignment>(value, true));
		Add<HtmlVerticalTextControl>("TextDecorations", static (target, value, _) =>
			target.TextDecorations = Enum.Parse<Windows.UI.Text.TextDecorations>(value, true));
		Add<HtmlVerticalTextControl>("WhiteSpace", static (target, value, _) =>
			target.WhiteSpace = value);
		Add<HtmlVerticalTextControl>("TextOverflow", static (target, value, _) =>
			target.TextOverflow = value);

		Add<TextBox>("Text", static (target, value, _) => target.Text = value);
		Add<TextBox>("PlaceholderText", static (target, value, _) =>
			target.PlaceholderText = value);
		Add<TextBox>("AcceptsReturn", static (target, value, _) =>
			target.AcceptsReturn = ParseBool(value));
		Add<TextBox>("IsReadOnly", static (target, value, _) =>
			target.IsReadOnly = ParseBool(value));
		Add<TextBox>("MaxLength", static (target, value, _) =>
			target.MaxLength = ParseInt(value));
		Add<TextBox>("TextWrapping", static (target, value, _) =>
			target.TextWrapping = Enum.Parse<TextWrapping>(value, true));
		Add<TextBox>("TextAlignment", static (target, value, _) =>
			target.TextAlignment = Enum.Parse<TextAlignment>(value, true));
		Add<PasswordBox>("Password", static (target, value, _) =>
			target.Password = value);
		Add<PasswordBox>("PlaceholderText", static (target, value, _) =>
			target.PlaceholderText = value);
		Add<PasswordBox>("MaxLength", static (target, value, _) =>
			target.MaxLength = ParseInt(value));
		Add<HtmlCursorPasswordBoxHost>("Password", static (target, value, _) =>
			target.Password = value);
		Add<HtmlCursorPasswordBoxHost>("PlaceholderText", static (target, value, _) =>
			target.PlaceholderText = value);
		Add<HtmlCursorPasswordBoxHost>("MaxLength", static (target, value, _) =>
			target.MaxLength = ParseInt(value));
		Add<ContentControl>("Content", static (target, value, _) =>
			target.Content = value);
		Add<Expander>("Header", static (target, value, _) =>
			target.Header = value);
		Add<ItemsControl>("ItemsSource", static (target, value, _) =>
			target.ItemsSource = value);
		Add<Selector>("SelectedItem", static (target, value, _) =>
			target.SelectedItem = value);
		Add<Selector>("SelectedValue", static (target, value, _) =>
			target.SelectedValue = value);
		Add<RangeBase>("Value", static (target, value, _) =>
			target.Value = ParseDouble(value));
		Add<RangeBase>("Minimum", static (target, value, _) =>
			target.Minimum = ParseDouble(value));
		Add<RangeBase>("Maximum", static (target, value, _) =>
			target.Maximum = ParseDouble(value));
		Add<RangeBase>("SmallChange", static (target, value, _) =>
			target.SmallChange = ParseDouble(value));
		Add<Slider>("StepFrequency", static (target, value, _) =>
			target.StepFrequency = ParseDouble(value));
		Add<ToggleButton>("IsChecked", static (target, value, _) =>
			target.IsChecked = value.Equals("null", StringComparison.OrdinalIgnoreCase)
				? null : ParseBool(value));
		Add<CheckBox>("IsThreeState", static (target, value, _) =>
			target.IsThreeState = ParseBool(value));
		Add<SelectorItem>("IsSelected", static (target, value, _) =>
			target.IsSelected = ParseBool(value));
		Add<ListViewBase>("SelectionMode", static (target, value, _) =>
			target.SelectionMode = Enum.Parse<ListViewSelectionMode>(value, true));
		Add<ProgressBar>("IsIndeterminate", static (target, value, _) =>
			target.IsIndeterminate = ParseBool(value));
		Add<Expander>("IsExpanded", static (target, value, _) =>
			target.IsExpanded = ParseBool(value));
		Add<ColorPicker>("Color", static (target, value, _) =>
			target.Color = ParseColor(value));

		Add<Image>("Source", static (target, value, source) =>
			target.Source = new BitmapImage(ResolveNavigationUri(value, source)));
		Add<Image>("Stretch", static (target, value, _) =>
			target.Stretch = Enum.Parse<Stretch>(value, true));
		Add<HtmlImageView>("Source", static (target, value, source) =>
			target.Source = new BitmapImage(ResolveNavigationUri(value, source)));
		Add<HtmlImageView>("Stretch", static (target, value, _) =>
			target.ApplyStretch(Enum.Parse<Stretch>(value, true)));
		Add<MediaPlayerElement>("Source", static (target, value, source) =>
			target.Source = MediaSource.CreateFromUri(ResolveNavigationUri(value, source)));
		Add<MediaPlayerElement>("AutoPlay", static (target, value, _) =>
			target.AutoPlay = ParseBool(value));
		Add<MediaPlayerElement>("AreTransportControlsEnabled",
			static (target, value, _) =>
				target.AreTransportControlsEnabled = ParseBool(value));
		Add<HyperlinkButton>("NavigateUri", static (target, value, source) =>
			target.NavigateUri = ResolveNavigationUri(value, source));
		Add<HtmlInteractiveFlexPanel>("NavigateUri",
			static (target, value, source) =>
				target.NavigateUri = ResolveNavigationUri(value, source));
		Add<HtmlInteractiveFlexPanel>("CommandKind",
			static (target, value, _) => target.CommandKind = value);
		Add<HtmlFormButton>("CommandKind",
			static (target, value, _) => target.CommandKind = value);
		Add<HtmlInteractiveFlexPanel>("FormOwnerId",
			static (target, value, _) => target.FormOwnerId = value);
		Add<HtmlInteractiveFlexPanel>("FormAction",
			static (target, value, _) => target.FormAction = value);
		Add<HtmlFormButton>("FormOwnerId",
			static (target, value, _) => target.FormOwnerId = value);
		Add<HtmlFormButton>("FormAction",
			static (target, value, _) => target.FormAction = value);

		Add<Shape>("Fill", static (target, value, _) =>
			target.Fill = ParseBrush(value));
		Add<Shape>("Stroke", static (target, value, _) =>
			target.Stroke = ParseBrush(value));
		Add<Shape>("StrokeThickness", static (target, value, _) =>
			target.StrokeThickness = ParseDouble(value));
		Add<Shape>("StrokeDashArray", static (target, value, _) =>
			target.StrokeDashArray = ParseDoubleCollection(value));
		Add<Shape>("StrokeStartLineCap", static (target, value, _) =>
			target.StrokeStartLineCap = Enum.Parse<PenLineCap>(value, true));
		Add<Shape>("StrokeEndLineCap", static (target, value, _) =>
			target.StrokeEndLineCap = Enum.Parse<PenLineCap>(value, true));
		Add<Shape>("StrokeDashCap", static (target, value, _) =>
			target.StrokeDashCap = Enum.Parse<PenLineCap>(value, true));
		Add<Shape>("StrokeLineJoin", static (target, value, _) =>
			target.StrokeLineJoin = Enum.Parse<PenLineJoin>(value, true));
		Add<Shape>("Stretch", static (target, value, _) =>
			target.Stretch = Enum.Parse<Stretch>(value, true));
		Add<Microsoft.UI.Xaml.Shapes.Path>("Data", static (target, value, _) =>
		{
			var geometry = SvgPathGeometryParser.Parse(value);
			target.Data = geometry;
			if (!ApplySvgPathGeometryBounds(target))
			{
				RoutedEventHandler? loaded = null;
				loaded = (sender, _) =>
				{
					var path = (Microsoft.UI.Xaml.Shapes.Path)sender;
					path.Loaded -= loaded;
					ApplySvgPathGeometryBounds(path);
				};
				target.Loaded += loaded;
			}
		});
		Add<HtmlSvgViewport>("ViewBox", static (target, value, _) =>
			target.ViewBox = value);
		Add<HtmlSvgViewport>("PreserveAspectRatio", static (target, value, _) =>
			target.PreserveAspectRatio = value);
		Add<Polygon>("Points", static (target, value, _) =>
			target.Points = ParsePointCollection(value));
		Add<Polyline>("Points", static (target, value, _) =>
			target.Points = ParsePointCollection(value));
		Add<Line>("X1", static (target, value, _) => target.X1 = ParseDouble(value));
		Add<Line>("Y1", static (target, value, _) => target.Y1 = ParseDouble(value));
		Add<Line>("X2", static (target, value, _) => target.X2 = ParseDouble(value));
		Add<Line>("Y2", static (target, value, _) => target.Y2 = ParseDouble(value));

		Add<HtmlMeterControl>("Low", static (target, value, _) =>
			target.Low = ParseDouble(value));
		Add<HtmlMeterControl>("High", static (target, value, _) =>
			target.High = ParseDouble(value));
		Add<HtmlMeterControl>("Optimum", static (target, value, _) =>
			target.Optimum = ParseDouble(value));
		Add<HtmlMediaElementControl>("Source", static (target, value, _) =>
			target.Source = value);
		Add<HtmlMediaElementControl>("Poster", static (target, value, _) =>
			target.Poster = value);
		Add<HtmlMediaElementControl>("TrackSources", static (target, value, _) =>
			target.TrackSources = value);
		Add<HtmlMediaElementControl>("AutoPlay", static (target, value, _) =>
			target.AutoPlay = ParseBool(value));
		Add<HtmlMediaElementControl>("AreTransportControlsEnabled",
			static (target, value, _) =>
				target.AreTransportControlsEnabled = ParseBool(value));
		Add<HtmlEmbeddedContentHost>("Source", static (target, value, _) =>
			target.Source = value);
		Add<HtmlEmbeddedContentHost>("MediaType", static (target, value, _) =>
			target.MediaType = value);
		Add<HtmlEmbeddedDocumentHost>("SourceDocument", static (target, value, _) =>
			target.SourceDocument = value);
		Add<HtmlEmbeddedDocumentHost>("Sandbox", static (target, value, _) =>
			target.Sandbox = value);
		Add<HtmlEmbeddedDocumentHost>("Allow", static (target, value, _) =>
			target.Allow = value);
		Add<HtmlObjectContentHost>("Source", static (target, value, _) =>
			target.Source = value);
		Add<HtmlObjectContentHost>("MediaType", static (target, value, _) =>
			target.MediaType = value);
		Add<HtmlCanvasSurface>("DrawingConnectionKey", static (target, value, _) =>
			target.DrawingConnectionKey = value);
		Add<HtmlCanvasSurface>("CommandStream", static (target, value, _) =>
			target.CommandStream = value);
		Add<HtmlFileInputControl>("Accept", static (target, value, _) =>
			target.Accept = value);
		Add<HtmlFileInputControl>("AllowsMultiple", static (target, value, _) =>
			target.AllowsMultiple = ParseBool(value));
		Add<HtmlTemporalInputControl>("Value", static (target, value, _) =>
			target.Value = value);
		Add<HtmlTemporalInputControl>("Minimum", static (target, value, _) =>
			target.Minimum = value);
		Add<HtmlTemporalInputControl>("Maximum", static (target, value, _) =>
			target.Maximum = value);
		Add<HtmlTemporalInputControl>("Step", static (target, value, _) =>
			target.Step = value);
		Add<HtmlTemporalInputControl>("IsReadOnly", static (target, value, _) =>
			target.IsReadOnly = ParseBool(value));
		Add<HtmlTemporalInputControl>("IsEnabled", static (target, value, _) =>
			target.IsEnabled = ParseBool(value));
		Add<HtmlDatalistInputControl>("SuggestionValues", static (target, value, _) =>
			target.SuggestionValues = value);
		Add<HtmlFormLabelPanel>("TargetId", static (target, value, _) =>
			target.TargetId = value);
		Add<HtmlDialogControl>("IsOpen", static (target, value, _) =>
			target.IsOpen = ParseBool(value));
		Add<HtmlDialogControl>("ClosedBy", static (target, value, _) =>
			target.ClosedBy = value);

		return setters.ToDictionary(
			static pair => pair.Key,
			static pair => (IReadOnlyList<StrongPropertySetter>)pair.Value,
			StringComparer.Ordinal);
	}

	private static bool ApplySvgPathGeometryBounds(
		Microsoft.UI.Xaml.Shapes.Path target)
	{
		if (target.Data is not Geometry geometry)
			return false;
		var bounds = geometry.Bounds;
		if (bounds.Width <= 0 || bounds.Height <= 0)
			return false;
		target.Width = bounds.Width;
		target.Height = bounds.Height;
		target.Stretch = Stretch.Fill;
		Canvas.SetLeft(target, bounds.X);
		Canvas.SetTop(target, bounds.Y);
		return true;
	}

	private static void ApplyStrongProperty(
		DependencyObject target,
		GeneratedXamlAttribute attribute,
		DomElement? source,
		string targetDescription)
	{
		if (!StrongPropertySetters.TryGetValue(attribute.Name, out var setters))
		{
			throw new NotSupportedException(
				$"{targetDescription}.{attribute.Name} has no compile-time "
					+ "registered WinUI setter.");
		}
		foreach (var setter in setters)
		{
			try
			{
				setter(target, attribute.Value, source);
				return;
			}
			catch (StrongSetterTargetMismatchException)
			{
			}
		}
		throw new NotSupportedException(
			$"{targetDescription}.{attribute.Name} is not valid for the "
				+ $"strong target type {target.GetType().FullName}.");
	}

	private sealed class StrongSetterTargetMismatchException : Exception;

	private static string GeneratedOwnTextMarker(DomElement? source) =>
		source is null
			? string.Empty
			: $"generated-own-text:{source.DocumentScope}::{source.XPath}";
}

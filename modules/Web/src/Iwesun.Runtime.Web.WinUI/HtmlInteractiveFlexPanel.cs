using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;
using FontStyle = Windows.UI.Text.FontStyle;
using FontWeight = Windows.UI.Text.FontWeight;

namespace Iwesun.Runtime.Web.WinUI;

/// <summary>
/// Strong WinUI projection for interactive HTML elements whose runtime CSS
/// establishes a multi-child flex formatting context.
/// </summary>
// Retained only as source history while the project-wide no-delete rule is active.
// The live factory never creates this legacy Grid-derived implementation.
internal sealed class LegacyHtmlInteractiveFlexPanel : Grid
{
	public static readonly DependencyProperty IsEnabledProperty =
		DependencyProperty.Register(
			nameof(IsEnabled),
			typeof(bool),
			typeof(LegacyHtmlInteractiveFlexPanel),
			new PropertyMetadata(true));

	public static readonly DependencyProperty NavigateUriProperty =
		DependencyProperty.Register(
			nameof(NavigateUri),
			typeof(Uri),
			typeof(LegacyHtmlInteractiveFlexPanel),
			new PropertyMetadata(null));

	public static readonly DependencyProperty CommandKindProperty =
		DependencyProperty.Register(
			nameof(CommandKind),
			typeof(string),
			typeof(LegacyHtmlInteractiveFlexPanel),
			new PropertyMetadata("submit"));

	public static readonly DependencyProperty FormOwnerIdProperty =
		DependencyProperty.Register(
			nameof(FormOwnerId),
			typeof(string),
			typeof(LegacyHtmlInteractiveFlexPanel),
			new PropertyMetadata(null));

	public static readonly DependencyProperty FormActionProperty =
		DependencyProperty.Register(
			nameof(FormAction),
			typeof(string),
			typeof(LegacyHtmlInteractiveFlexPanel),
			new PropertyMetadata(null));

	public static readonly DependencyProperty FontFamilyProperty =
		DependencyProperty.Register(
			nameof(FontFamily),
			typeof(FontFamily),
			typeof(LegacyHtmlInteractiveFlexPanel),
			new PropertyMetadata(null));

	public static readonly DependencyProperty FontSizeProperty =
		DependencyProperty.Register(
			nameof(FontSize),
			typeof(double),
			typeof(LegacyHtmlInteractiveFlexPanel),
			new PropertyMetadata(14d));

	public static readonly DependencyProperty FontWeightProperty =
		DependencyProperty.Register(
			nameof(FontWeight),
			typeof(FontWeight),
			typeof(LegacyHtmlInteractiveFlexPanel),
			new PropertyMetadata(FontWeights.Normal));

	public static readonly DependencyProperty FontStyleProperty =
		DependencyProperty.Register(
			nameof(FontStyle),
			typeof(FontStyle),
			typeof(LegacyHtmlInteractiveFlexPanel),
			new PropertyMetadata(FontStyle.Normal));

	public static readonly DependencyProperty ForegroundProperty =
		DependencyProperty.Register(
			nameof(Foreground),
			typeof(Brush),
			typeof(LegacyHtmlInteractiveFlexPanel),
			new PropertyMetadata(null));

	public bool IsEnabled
	{
		get => (bool)GetValue(IsEnabledProperty);
		set
		{
			SetValue(IsEnabledProperty, value);
			IsHitTestVisible = value;
			Opacity = value ? 1 : 0.55;
		}
	}

	public Uri? NavigateUri
	{
		get => (Uri?)GetValue(NavigateUriProperty);
		set => SetValue(NavigateUriProperty, value);
	}

	public string? CommandKind
	{
		get => (string?)GetValue(CommandKindProperty);
		set => SetValue(CommandKindProperty, value);
	}

	public string? FormOwnerId
	{
		get => (string?)GetValue(FormOwnerIdProperty);
		set => SetValue(FormOwnerIdProperty, value);
	}

	public string? FormAction
	{
		get => (string?)GetValue(FormActionProperty);
		set => SetValue(FormActionProperty, value);
	}

	public FontFamily? FontFamily
	{
		get => (FontFamily?)GetValue(FontFamilyProperty);
		set => SetValue(FontFamilyProperty, value);
	}

	public double FontSize
	{
		get => (double)GetValue(FontSizeProperty);
		set => SetValue(FontSizeProperty, value);
	}

	public FontWeight FontWeight
	{
		get => (FontWeight)GetValue(FontWeightProperty);
		set => SetValue(FontWeightProperty, value);
	}

	public FontStyle FontStyle
	{
		get => (FontStyle)GetValue(FontStyleProperty);
		set => SetValue(FontStyleProperty, value);
	}

	public Brush? Foreground
	{
		get => (Brush?)GetValue(ForegroundProperty);
		set => SetValue(ForegroundProperty, value);
	}
}

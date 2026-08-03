using Microsoft.UI.Xaml;

namespace Iwesun.Runtime.Web.WinUI;

internal static class HtmlValidation
{
	public static readonly DependencyProperty IsValidProperty =
		DependencyProperty.RegisterAttached(
			"IsValid",
			typeof(bool),
			typeof(HtmlValidation),
			new PropertyMetadata(true));

	public static readonly DependencyProperty WillValidateProperty =
		DependencyProperty.RegisterAttached(
			"WillValidate",
			typeof(bool),
			typeof(HtmlValidation),
			new PropertyMetadata(false));

	public static readonly DependencyProperty ValidationMessageProperty =
		DependencyProperty.RegisterAttached(
			"ValidationMessage",
			typeof(string),
			typeof(HtmlValidation),
			new PropertyMetadata(string.Empty));

	public static bool GetIsValid(DependencyObject element) =>
		(bool)element.GetValue(IsValidProperty);

	public static void SetIsValid(
		DependencyObject element,
		bool value) =>
		element.SetValue(IsValidProperty, value);

	public static bool GetWillValidate(DependencyObject element) =>
		(bool)element.GetValue(WillValidateProperty);

	public static void SetWillValidate(
		DependencyObject element,
		bool value) =>
		element.SetValue(WillValidateProperty, value);

	public static string GetValidationMessage(DependencyObject element) =>
		(string)element.GetValue(ValidationMessageProperty);

	public static void SetValidationMessage(
		DependencyObject element,
		string value) =>
		element.SetValue(ValidationMessageProperty, value);
}

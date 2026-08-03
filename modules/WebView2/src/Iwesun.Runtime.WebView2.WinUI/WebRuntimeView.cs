using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Iwesun.Runtime.WebView2;

/// <summary>
/// WinUI host control for the Iwesun WebRuntime API. The browser surface is
/// created with the control tree and remains the owner of display, focus and
/// pointer input for its complete lifetime.
/// </summary>
public sealed class WebRuntimeView : UserControl
{
	private readonly Microsoft.UI.Xaml.Controls.WebView2 _view = new()
	{
		HorizontalAlignment = HorizontalAlignment.Stretch,
		VerticalAlignment = VerticalAlignment.Stretch,
		DefaultBackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255)
	};

	public WebRuntimeView()
	{
		HorizontalContentAlignment = HorizontalAlignment.Stretch;
		VerticalContentAlignment = VerticalAlignment.Stretch;
		Content = _view;
	}

	public CoreWebView2? CoreWebView2 => _view.CoreWebView2;

	public Uri? Source
	{
		get => _view.Source;
		set => _view.Source = value;
	}

	public double BrowserActualWidth => _view.ActualWidth;

	public double BrowserActualHeight => _view.ActualHeight;

	public async Task EnsureCoreWebView2Async(
		CoreWebView2Environment environment)
	{
		ArgumentNullException.ThrowIfNull(environment);
		await _view.EnsureCoreWebView2Async(environment);
	}

	public void Close() => _view.Close();
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System.Runtime.InteropServices;

namespace Iwesun.Runtime.WebView2;

/// <summary>
/// WinUI host control for the Iwesun WebRuntime API. The browser surface is
/// created with the control tree and remains the owner of display, focus and
/// pointer input for its complete lifetime.
/// </summary>
public sealed class WebRuntimeView : UserControl
{
	private CoreWebView2Controller? _nativeController;
	private IntPtr _nativeControllerHost;
	private double _runtimeViewportWidth;
	private double _runtimeViewportHeight;
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

	public CoreWebView2? CoreWebView2 =>
		_nativeController?.CoreWebView2 ?? _view.CoreWebView2;

	public Uri? Source
	{
		get => _view.Source;
		set => _view.Source = value;
	}

	public double BrowserActualWidth => _nativeController is null
		? _view.ActualWidth
		: _nativeController.Bounds.Width;

	public double BrowserActualHeight => _nativeController is null
		? _view.ActualHeight
		: _nativeController.Bounds.Height;

	public double RequestedViewportWidth => _runtimeViewportWidth;

	public double RequestedViewportHeight => _runtimeViewportHeight;

	public int NativeHostClientWidth => ReadNativeHostClientSize().Width;

	public int NativeHostClientHeight => ReadNativeHostClientSize().Height;

	public double NativeRasterizationScale =>
		_nativeController?.RasterizationScale ?? 1d;

	/// <summary>
	/// Applies the current physical application client area to the native
	/// controller as a raw physical surface. Browser zoom remains exactly 100%;
	/// monitor DPI, text and icon rasterization remain entirely Windows-owned.
	/// </summary>
	public void ApplyRuntimeViewport(double width, double height)
	{
		if (!double.IsFinite(width)
			|| !double.IsFinite(height)
			|| width <= 0
			|| height <= 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(width),
				"The WebRuntime viewport must be finite and positive.");
		}

		_runtimeViewportWidth = width;
		_runtimeViewportHeight = height;
		ApplyNativeControllerBounds();
	}

	public async Task EnsureCoreWebView2Async(
		CoreWebView2Environment environment)
	{
		ArgumentNullException.ThrowIfNull(environment);
		await _view.EnsureCoreWebView2Async(environment);
	}

	/// <summary>
	/// Creates a raw-pixel CoreWebView2Controller owned by the supplied window.
	/// Bounds and CSS viewport units remain one-to-one with the current client
	/// area. Browser zoom stays at 100%; Windows still owns final glyph and icon
	/// rasterization, but monitor scale must not divide the document viewport.
	/// </summary>
	public async Task EnsureCoreWebView2Async(
		CoreWebView2Environment environment,
		IntPtr parentWindow)
	{
		ArgumentNullException.ThrowIfNull(environment);
		if (parentWindow == IntPtr.Zero)
			throw new ArgumentException("A parent HWND is required.", nameof(parentWindow));
		if (_runtimeViewportWidth <= 0 || _runtimeViewportHeight <= 0)
		{
			throw new InvalidOperationException(
				"ApplyRuntimeViewport must run before native WebView2 creation.");
		}
		if (_nativeController is not null)
			return;
		_nativeControllerHost = CreateWindowExW(
			0,
			"STATIC",
			null,
			WindowStyleChild | WindowStyleVisible
				| WindowStyleClipChildren | WindowStyleClipSiblings,
			0,
			0,
			1,
			1,
			parentWindow,
			IntPtr.Zero,
			IntPtr.Zero,
			IntPtr.Zero);
		if (_nativeControllerHost == IntPtr.Zero)
		{
			throw new InvalidOperationException(
				"The native WebView2 host window could not be created.",
				new System.ComponentModel.Win32Exception(
					Marshal.GetLastWin32Error()));
		}
		_nativeController = await environment.CreateCoreWebView2ControllerAsync(
			CoreWebView2ControllerWindowReference.CreateFromWindowHandle(
				unchecked((ulong)_nativeControllerHost.ToInt64())));
		// The native controller owns the full physical client rectangle and browser
		// zoom remains exactly 100%. WebView2 must track the monitor scale itself so
		// Windows can rasterize browser text, icons, menus, and scrollbars correctly.
		// The application never writes a custom RasterizationScale.
		_nativeController.BoundsMode = CoreWebView2BoundsMode.UseRawPixels;
		_nativeController.ZoomFactor = 1d;
		_nativeController.ShouldDetectMonitorScaleChanges = true;
		_nativeController.IsVisible = true;
		_view.Visibility = Visibility.Collapsed;
		ApplyNativeControllerBounds();
	}

	private void ApplyNativeControllerBounds()
	{
		if (_nativeController is null)
			return;
		var width = checked((int)Math.Ceiling(_runtimeViewportWidth));
		var height = checked((int)Math.Ceiling(_runtimeViewportHeight));
		if (!SetWindowPos(
			_nativeControllerHost,
			IntPtr.Zero,
			0,
			0,
			width,
			height,
			SetWindowPositionShowWindow))
		{
			throw new InvalidOperationException(
				"The native WebView2 host window could not be resized.",
				new System.ComponentModel.Win32Exception(
					Marshal.GetLastWin32Error()));
		}
		_nativeController.BoundsMode = CoreWebView2BoundsMode.UseRawPixels;
		_nativeController.SetBoundsAndZoomFactor(
			new Windows.Foundation.Rect(
				0,
				0,
				_runtimeViewportWidth,
				_runtimeViewportHeight),
			1d);
	}

	private (int Width, int Height) ReadNativeHostClientSize()
	{
		if (_nativeControllerHost == IntPtr.Zero)
			return (0, 0);
		if (!GetClientRect(_nativeControllerHost, out var rectangle))
		{
			throw new InvalidOperationException(
				"The native WebView2 host client rectangle could not be read.",
				new System.ComponentModel.Win32Exception(
					Marshal.GetLastWin32Error()));
		}
		return (rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top);
	}

	public void Close()
	{
		_nativeController?.Close();
		_nativeController = null;
		if (_nativeControllerHost != IntPtr.Zero)
		{
			DestroyWindow(_nativeControllerHost);
			_nativeControllerHost = IntPtr.Zero;
		}
		_view.Close();
	}

	private const uint WindowStyleChild = 0x40000000;
	private const uint WindowStyleVisible = 0x10000000;
	private const uint WindowStyleClipSiblings = 0x04000000;
	private const uint WindowStyleClipChildren = 0x02000000;
	private const uint SetWindowPositionShowWindow = 0x0040;

	[DllImport("user32.dll", EntryPoint = "CreateWindowExW",
		SetLastError = true, CharSet = CharSet.Unicode)]
	private static extern IntPtr CreateWindowExW(
		uint extendedStyle,
		string className,
		string? windowName,
		uint style,
		int x,
		int y,
		int width,
		int height,
		IntPtr parent,
		IntPtr menu,
		IntPtr instance,
		IntPtr parameter);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool SetWindowPos(
		IntPtr window,
		IntPtr insertAfter,
		int x,
		int y,
		int width,
		int height,
		uint flags);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool DestroyWindow(IntPtr window);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetClientRect(
		IntPtr window,
		out NativeRectangle rectangle);

	[StructLayout(LayoutKind.Sequential)]
	private struct NativeRectangle
	{
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;
	}
}

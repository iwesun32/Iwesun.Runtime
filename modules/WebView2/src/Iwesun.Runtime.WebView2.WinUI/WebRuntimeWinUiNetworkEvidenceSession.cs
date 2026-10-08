using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Web.WebView2.Core;

namespace Iwesun.Runtime.WebView2;

/// <summary>
/// Captures original response bodies from the WinUI WebView2 projection and
/// exposes them as immutable in-memory reconstruction evidence.
/// </summary>
public sealed class WebRuntimeWinUiNetworkEvidenceSession :
	IAsyncDisposable,
	IWebRuntimeCapturedHttpBodySource
{
	private const long MaximumBodyBytes = 128 * 1024 * 1024;
	private readonly CoreWebView2 _browser;
	private readonly ConcurrentDictionary<string, CapturedBody> _bodies =
		new(StringComparer.Ordinal);
	private int _activeCopies;
	private int _disposed;

	private WebRuntimeWinUiNetworkEvidenceSession(CoreWebView2 browser)
	{
		_browser = browser;
		_browser.WebResourceResponseReceived += OnResponseReceived;
	}

	public static Task<WebRuntimeWinUiNetworkEvidenceSession> StartAsync(
		CoreWebView2 browser)
	{
		ArgumentNullException.ThrowIfNull(browser);
		return Task.FromResult(
			new WebRuntimeWinUiNetworkEvidenceSession(browser));
	}

	public bool TryReadCapturedBody(
		string url,
		out WebRuntimeCapturedHttpBody? body)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(url);
		if (!_bodies.TryGetValue(NormalizeUrl(url), out var captured))
		{
			body = null;
			return false;
		}

		body = new WebRuntimeCapturedHttpBody(
			captured.Url,
			captured.ContentType,
			captured.Content,
			captured.Sha256,
			"winui-web-resource-response");
		return true;
	}

	public async Task WaitForPendingBodiesAsync(
		TimeSpan timeout,
		CancellationToken cancellationToken = default)
	{
		var deadline = DateTimeOffset.UtcNow + timeout;
		while (Volatile.Read(ref _activeCopies) > 0)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (DateTimeOffset.UtcNow >= deadline)
				throw new TimeoutException(
					"Timed out while capturing WinUI HTTP response bodies.");
			await Task.Delay(25, cancellationToken).ConfigureAwait(false);
		}
	}

	public ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) == 0)
			_browser.WebResourceResponseReceived -= OnResponseReceived;
		return ValueTask.CompletedTask;
	}

	private async void OnResponseReceived(
		CoreWebView2 sender,
		CoreWebView2WebResourceResponseReceivedEventArgs args)
	{
		if (Volatile.Read(ref _disposed) != 0)
			return;
		Interlocked.Increment(ref _activeCopies);
		try
		{
			var url = args.Request.Uri;
			var contentType = ReadContentType(args.Response.Headers);
			if (!ShouldCapture(url, contentType))
				return;
			var randomAccessInput = await args.Response.GetContentAsync();
			await using var input = randomAccessInput.AsStreamForRead();
			using var output = new MemoryStream();
			await CopyBodyAsync(input, output).ConfigureAwait(false);
			var content = output.ToArray();
			var sha256 = Convert.ToHexString(
				SHA256.HashData(content)).ToLowerInvariant();
			_bodies[NormalizeUrl(url)] = new CapturedBody(
				url,
				contentType,
				content,
				sha256);
		}
		catch (Exception) when (Volatile.Read(ref _disposed) != 0)
		{
		}
		finally
		{
			Interlocked.Decrement(ref _activeCopies);
		}
	}

	private static string ReadContentType(
		CoreWebView2HttpResponseHeaders headers)
	{
		try
		{
			return headers.GetHeader("Content-Type");
		}
		catch (Exception exception) when (
			exception is ArgumentException
				or System.Runtime.InteropServices.COMException)
		{
			return string.Empty;
		}
	}

	private static bool ShouldCapture(string url, string contentType)
	{
		var mediaType = contentType.Split(';', 2)[0].Trim();
		var extension = Path.GetExtension(
			Uri.TryCreate(url, UriKind.Absolute, out var uri)
				? uri.AbsolutePath
				: url);
		return mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
			|| mediaType.StartsWith("font/", StringComparison.OrdinalIgnoreCase)
			|| mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
			|| mediaType.Contains("json", StringComparison.OrdinalIgnoreCase)
			|| mediaType.Contains("javascript", StringComparison.OrdinalIgnoreCase)
			|| new[]
			{
				".svg", ".png", ".jpg", ".jpeg", ".gif", ".webp",
				".avif", ".ico", ".bmp", ".woff", ".woff2", ".ttf",
				".otf", ".css", ".js", ".mjs", ".json", ".html"
			}.Contains(extension, StringComparer.OrdinalIgnoreCase);
	}

	private static async Task CopyBodyAsync(Stream input, Stream output)
	{
		var buffer = new byte[81920];
		long total = 0;
		while (true)
		{
			var read = await input.ReadAsync(buffer).ConfigureAwait(false);
			if (read == 0)
				return;
			total += read;
			if (total > MaximumBodyBytes)
				throw new InvalidDataException(
					$"Response body exceeds {MaximumBodyBytes} bytes.");
			await output.WriteAsync(
				buffer.AsMemory(0, read)).ConfigureAwait(false);
		}
	}

	private static string NormalizeUrl(string value)
	{
		if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
			return value;
		var builder = new UriBuilder(uri) { Fragment = string.Empty };
		return builder.Uri.AbsoluteUri;
	}

	private sealed record CapturedBody(
		string Url,
		string ContentType,
		byte[] Content,
		string Sha256);
}

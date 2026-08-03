using Microsoft.Web.WebView2.Core;

namespace Iwesun.Runtime.Web;

/// <summary>
/// 扩展方法，方便将 WebView2DomPropertyFiller 绑定到 DomElement 树
/// </summary>
public static class WebRuntimeDomPropertyFillerExtensions
{
	/// <summary>
	/// 为元素及其所有子元素设置 WebView2 DOM 查询引擎
	/// </summary>
	public static void BindWebView2Filler(this DomElement root, CoreWebView2 webView2)
	{
		ArgumentNullException.ThrowIfNull(root);
		ArgumentNullException.ThrowIfNull(webView2);

		var filler = new WebView2DomPropertyFiller(webView2);
		BindFillerRecursive(root, filler);
	}

	/// <summary>
	/// 为元素及其所有子元素设置 WebView2 DOM 查询引擎（使用已有的 filler 实例）
	/// </summary>
	public static void BindWebView2Filler(this DomElement root, WebView2DomPropertyFiller filler)
	{
		ArgumentNullException.ThrowIfNull(root);
		ArgumentNullException.ThrowIfNull(filler);

		BindFillerRecursive(root, filler);
	}

	private static void BindFillerRecursive(DomElement element, WebView2DomPropertyFiller filler)
	{
		element.DomQueryEngine = filler;
		foreach (var child in element.Children)
		{
			BindFillerRecursive(child, filler);
		}
	}
}

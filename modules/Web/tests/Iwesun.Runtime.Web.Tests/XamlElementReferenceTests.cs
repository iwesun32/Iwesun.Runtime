using Iwesun.Runtime.Web;
using Xunit;

namespace Iwesun.Runtime.Web.Tests;

/// <summary>
/// 验证 XamlElement 挂载引用属性的功能
/// </summary>
public sealed class XamlElementReferenceTests
{
	[Fact]
	public void XamlElement_DefaultIsNull()
	{
		var element = new HtmlDivDomElement(Mapping("/html/body/div"));

		Assert.Null(element.XamlElement);
	}

	[Fact]
	public void XamlElement_CanBeSetAndRetrieved()
	{
		var element = new HtmlDivDomElement(Mapping("/html/body/div"));
		var mockControl = new object(); // 模拟 FrameworkElement

		element.XamlElement = mockControl;

		Assert.Same(mockControl, element.XamlElement);
	}

	[Fact]
	public void XamlElement_CanBeSetToNull()
	{
		var element = new HtmlDivDomElement(Mapping("/html/body/div"));
		element.XamlElement = new object();

		element.XamlElement = null;

		Assert.Null(element.XamlElement);
	}

	[Fact]
	public void XamlElement_WorksWithAllElementTypes()
	{
		// 测试不同类型的元素都能设置 XAML 引用
		var elements = new DomElement[]
		{
			new HtmlAnchorDomElement(Mapping("/html/body/a")),
			new HtmlButtonDomElement(Mapping("/html/body/button")),
			new HtmlInputDomElement(Mapping("/html/body/input")),
			new HtmlVideoDomElement(Mapping("/html/body/video")),
			new HtmlTableHeaderCellDomElement(Mapping("/html/body/table/tr/th")),
			new SvgPathDomElement(Mapping("/html/body/svg/path"))
		};

		foreach (var element in elements)
		{
			var control = new object();
			element.XamlElement = control;
			Assert.Same(control, element.XamlElement);
		}
	}

	[Fact]
	public void XamlElement_DoesNotInterfereWithChildren()
	{
		// XamlElement 只是挂载引用，不影响 DomElement 的 Children 管理
		var parent = new HtmlDivDomElement(Mapping("/html/body/div"));
		var child = new HtmlSpanDomElement(Mapping("/html/body/div/span"));

		parent.AddChild(child);

		parent.XamlElement = new object();
		child.XamlElement = new object();

		Assert.Single(parent.Children);
		Assert.Same(child, parent.Children[0]);
		Assert.Same(parent, child.Parent);
	}

	private static DomElementMapping Mapping(string xpath) =>
		new("document", xpath, null, [], null, null);
}

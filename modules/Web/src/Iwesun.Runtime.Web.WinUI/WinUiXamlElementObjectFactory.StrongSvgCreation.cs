using Iwesun.Runtime.Web;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;

namespace Iwesun.Runtime.Web.WinUI;

public sealed partial class WinUiXamlElementObjectFactory
{
	private static DependencyObject CreateStrong(
		SvgRootDomElement element,
		XamlElementObjectPlan plan) =>
		plan.Mapping.ObjectType == XamlElementObjectType.HtmlSvgViewport
			? new HtmlSvgViewport()
			: throw StrongTypeMismatch(element, plan);

	private static DependencyObject CreateStrong(
		SvgGroupDomElement element,
		XamlElementObjectPlan plan) =>
		plan.Mapping.ObjectType == XamlElementObjectType.Canvas
			? new HtmlCanvasPanel()
			: throw StrongTypeMismatch(element, plan);

	private static DependencyObject CreateStrong(
		SvgPathDomElement element,
		XamlElementObjectPlan plan) =>
		plan.Mapping.ObjectType == XamlElementObjectType.Path
			? new Microsoft.UI.Xaml.Shapes.Path()
			: throw StrongTypeMismatch(element, plan);

	private static DependencyObject CreateStrong(
		SvgUseDomElement element,
		XamlElementObjectPlan plan) =>
		plan.Mapping.ObjectType == XamlElementObjectType.Path
			? new Microsoft.UI.Xaml.Shapes.Path()
			: throw StrongTypeMismatch(element, plan);

	private static DependencyObject CreateStrong(
		SvgCircleDomElement element,
		XamlElementObjectPlan plan) =>
		plan.Mapping.ObjectType == XamlElementObjectType.Ellipse
			? new Ellipse()
			: throw StrongTypeMismatch(element, plan);

	private static DependencyObject CreateStrong(
		SvgRectangleDomElement element,
		XamlElementObjectPlan plan) =>
		plan.Mapping.ObjectType == XamlElementObjectType.Rectangle
			? new Rectangle()
			: throw StrongTypeMismatch(element, plan);

	private static DependencyObject CreateStrong(
		SvgLineDomElement element,
		XamlElementObjectPlan plan) =>
		plan.Mapping.ObjectType == XamlElementObjectType.Line
			? new Line()
			: throw StrongTypeMismatch(element, plan);

	private static DependencyObject CreateStrong(
		SvgPolygonDomElement element,
		XamlElementObjectPlan plan) =>
		plan.Mapping.ObjectType == XamlElementObjectType.Polygon
			? new Polygon()
			: throw StrongTypeMismatch(element, plan);

	private static DependencyObject CreateStrong(
		SvgPolylineDomElement element,
		XamlElementObjectPlan plan) =>
		plan.Mapping.ObjectType == XamlElementObjectType.Polyline
			? new Polyline()
			: throw StrongTypeMismatch(element, plan);

	private static DependencyObject CreateStrong(
		SvgEllipseDomElement element,
		XamlElementObjectPlan plan) =>
		plan.Mapping.ObjectType == XamlElementObjectType.Ellipse
			? new Ellipse()
			: throw StrongTypeMismatch(element, plan);

	private static DependencyObject CreateStrong(
		SvgDefinitionsDomElement element,
		XamlElementObjectPlan plan) =>
		plan.Mapping.ObjectType == XamlElementObjectType.Canvas
			? new HtmlCanvasPanel()
			: throw StrongTypeMismatch(element, plan);

	private static DependencyObject CreateStrong(
		SvgClipPathDomElement element,
		XamlElementObjectPlan plan) =>
		plan.Mapping.ObjectType == XamlElementObjectType.Canvas
			? new HtmlCanvasPanel()
			: throw StrongTypeMismatch(element, plan);

	private static DependencyObject CreateStrong(
		SvgMaskDomElement element,
		XamlElementObjectPlan plan) =>
		plan.Mapping.ObjectType == XamlElementObjectType.Canvas
			? new HtmlCanvasPanel()
			: throw StrongTypeMismatch(element, plan);

	private static DependencyObject CreateStrong(
		SvgTextDomElement element,
		XamlElementObjectPlan plan) =>
		plan.Mapping.ObjectType == XamlElementObjectType.TextBlock
			? new TextBlock()
			: throw StrongTypeMismatch(element, plan);
}

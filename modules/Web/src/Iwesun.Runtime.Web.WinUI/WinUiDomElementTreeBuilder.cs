using Iwesun.Runtime.Web;
using Microsoft.UI.Xaml;

namespace Iwesun.Runtime.Web.WinUI;

internal sealed record WinUiDomElementTreeBuildResult(
	IReadOnlyList<FrameworkElement> DocumentRoots,
	IReadOnlyList<XamlElementObjectBuildResult> BuildResults);

internal sealed class WinUiDomElementTreeBuilder
{
	private readonly WinUiXamlElementObjectFactory _factory = new();

	internal WinUiDomElementTreeBuildResult Build(
		HtmlDocumentRoot htmlRoot)
	{
		ArgumentNullException.ThrowIfNull(htmlRoot);
		var buildResults = htmlRoot.BuildXamlObjectTrees(_factory);
		var documentRoots = new List<FrameworkElement>(
			buildResults.Count);
		foreach (var result in buildResults)
		{
			if (result.RootObjects.Count != 1)
			{
				throw new InvalidDataException(
					"Each mounted document scope must produce exactly one "
					+ "WinUI root object.");
			}
			if (result.RootObjects[0] is not FrameworkElement element)
			{
				throw new InvalidDataException(
					"The generated WinUI document root is not a FrameworkElement.");
			}
			documentRoots.Add(element);
		}
		if (documentRoots.Count == 0)
		{
			throw new InvalidDataException(
				"The HTML root produced no WinUI document roots.");
		}
		return new(documentRoots, buildResults);
	}

	internal FrameworkElement BuildPrimary(HtmlDocumentRoot htmlRoot)
	{
		var result = Build(htmlRoot);
		return result.DocumentRoots[0];
	}
}

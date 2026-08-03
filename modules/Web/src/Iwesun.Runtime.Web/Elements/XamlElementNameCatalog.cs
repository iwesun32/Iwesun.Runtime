namespace Iwesun.Runtime.Web;

public static class XamlElementNameCatalog
{
	public static string Resolve(
		string tagName,
		XamlControlFamily controlFamily)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(tagName);
		return tagName.ToLowerInvariant() switch
		{
			"a" => "HyperlinkButton",
			"button" => "Button",
			"input" or "textarea" => "TextBox",
			"select" => "ComboBox",
			"option" => "ComboBoxItem",
			"meter" or "progress" => "ProgressBar",
			"img" => "Image",
			"video" or "audio" => "MediaPlayerElement",
			"iframe" or "embed" or "object" => "ContentControl",
			"svg" or "g" or "defs" or "clippath" or "mask" => "Canvas",
			"path" or "use" => "Path",
			"circle" => "Ellipse",
			"rect" => "Rectangle",
			"line" => "Line",
			"polygon" => "Polygon",
			"polyline" => "Polyline",
			"ellipse" => "Ellipse",
			_ => ResolveFamily(controlFamily)
		};
	}

	private static string ResolveFamily(XamlControlFamily controlFamily) =>
		controlFamily switch
		{
			XamlControlFamily.None => string.Empty,
			XamlControlFamily.Panel => "Grid",
			XamlControlFamily.Text => "TextBlock",
			XamlControlFamily.Button => "Button",
			XamlControlFamily.Input => "TextBox",
			XamlControlFamily.Selector => "ComboBox",
			XamlControlFamily.Image => "Image",
			XamlControlFamily.Media => "MediaPlayerElement",
			XamlControlFamily.Shape => "Path",
			XamlControlFamily.EmbeddedHost => "ContentControl",
			XamlControlFamily.Custom => "ContentControl",
			_ => throw new ArgumentOutOfRangeException(nameof(controlFamily))
		};
}

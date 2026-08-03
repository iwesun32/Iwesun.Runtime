namespace Iwesun.Runtime.Web;

public enum HtmlElementAttributeSupport
{
	Standard,
	Extension
}

public sealed record HtmlElementAttributeValue(
	string Name,
	string Value,
	HtmlElementAttributeSupport Support);

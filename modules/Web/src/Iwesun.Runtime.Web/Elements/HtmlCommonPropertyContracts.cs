namespace Iwesun.Runtime.Web;

public interface IHtmlHyperlinkCommonProperties
{
	DomElementStringProperty Href { get; }
	DomElementStringProperty Target { get; }
	DomElementStringProperty Download { get; }
	DomElementStringProperty Ping { get; }
	DomElementStringProperty Rel { get; }
	DomElementStringProperty ReferrerPolicy { get; }
}

public interface IHtmlMediaCommonProperties
{
	DomElementStringProperty Source { get; }
	DomElementStringProperty CrossOrigin { get; }
	DomElementStringProperty Preload { get; }
	DomElementStringProperty AutoPlay { get; }
	DomElementStringProperty Loop { get; }
	DomElementStringProperty Muted { get; }
	DomElementStringProperty Controls { get; }
	DomElementStringProperty Loading { get; }
}

public interface IHtmlModificationCommonProperties
{
	DomElementStringProperty Cite { get; }
	DomElementStringProperty DateTime { get; }
}

public interface IHtmlQuoteCommonProperties
{
	DomElementStringProperty Cite { get; }
}

public interface IHtmlTableCellCommonProperties
{
	DomElementStringProperty ColumnSpan { get; }
	DomElementStringProperty RowSpan { get; }
	DomElementStringProperty Headers { get; }
}

public interface IHtmlFormOwnerProperties
{
	DomElementStringProperty Form { get; }
}

public interface IHtmlNamedFormOwnerProperties :
	IHtmlFormOwnerProperties
{
	DomElementStringProperty Name { get; }
}

public interface IHtmlFormControlOwnerProperties :
	IHtmlNamedFormOwnerProperties
{
	DomElementStringProperty Disabled { get; }
}

public interface IHtmlSubmitterCommonProperties
{
	DomElementStringProperty FormAction { get; }
	DomElementStringProperty FormEncodingType { get; }
	DomElementStringProperty FormMethod { get; }
	DomElementStringProperty FormNoValidate { get; }
	DomElementStringProperty FormTarget { get; }
}

internal sealed class HtmlQuoteCommonPropertySet(string tagName)
{
	public DomElementStringProperty Cite { get; } =
		new HtmlDomAttributeProperty("cite", tagName);
}

internal sealed class HtmlFormOwnerPropertySet(string tagName)
{
	public DomElementStringProperty Form { get; } =
		new HtmlDomAttributeProperty("form", tagName);
}

internal sealed class HtmlNamedFormOwnerPropertySet(string tagName)
{
	private readonly HtmlFormOwnerPropertySet _formOwner = new(tagName);

	public DomElementStringProperty Form => _formOwner.Form;
	public DomElementStringProperty Name { get; } =
		new HtmlDomAttributeProperty("name", tagName);
}

internal sealed class HtmlFormControlOwnerPropertySet(string tagName)
{
	private readonly HtmlNamedFormOwnerPropertySet _namedFormOwner =
		new(tagName);

	public DomElementStringProperty Disabled { get; } =
		new HtmlDomAttributeProperty("disabled", tagName);
	public DomElementStringProperty Form => _namedFormOwner.Form;
	public DomElementStringProperty Name => _namedFormOwner.Name;
}

internal sealed class HtmlSubmitterCommonPropertySet(string tagName)
{
	public DomElementStringProperty FormAction { get; } =
		new HtmlDomAttributeProperty("formaction", tagName);
	public DomElementStringProperty FormEncodingType { get; } =
		new HtmlDomAttributeProperty("formenctype", tagName);
	public DomElementStringProperty FormMethod { get; } =
		new HtmlDomAttributeProperty("formmethod", tagName);
	public DomElementStringProperty FormNoValidate { get; } =
		new HtmlDomAttributeProperty("formnovalidate", tagName);
	public DomElementStringProperty FormTarget { get; } =
		new HtmlDomAttributeProperty("formtarget", tagName);
}

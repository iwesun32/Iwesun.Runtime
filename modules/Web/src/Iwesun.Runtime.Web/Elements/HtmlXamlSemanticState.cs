namespace Iwesun.Runtime.Web;

public enum HtmlInputTypeState
{
	Text,
	Search,
	Telephone,
	Url,
	Email,
	Password,
	Date,
	Month,
	Week,
	Time,
	DateTimeLocal,
	Number,
	Range,
	Color,
	Checkbox,
	Radio,
	File,
	Submit,
	Image,
	Reset,
	Button,
	Hidden
}

public enum HtmlSelectPresentationState
{
	DropDownSingleSelection,
	VisibleSingleSelectionList,
	VisibleMultipleSelectionList
}

public enum HtmlCssFormattingContext
{
	NotRendered,
	Contents,
	Inline,
	InlineBlock,
	BlockFlow,
	FlexRow,
	FlexColumn,
	Grid,
	Table,
	TableRowGroup,
	TableRow,
	TableCell,
	ListItem,
	Unknown
}

public static class HtmlXamlSemanticState
{
	public static HtmlInputTypeState ParseInputType(string? value) =>
		value?.Trim().ToLowerInvariant() switch
		{
			null or "" or "text" => HtmlInputTypeState.Text,
			"search" => HtmlInputTypeState.Search,
			"tel" => HtmlInputTypeState.Telephone,
			"url" => HtmlInputTypeState.Url,
			"email" => HtmlInputTypeState.Email,
			"password" => HtmlInputTypeState.Password,
			"date" => HtmlInputTypeState.Date,
			"month" => HtmlInputTypeState.Month,
			"week" => HtmlInputTypeState.Week,
			"time" => HtmlInputTypeState.Time,
			"datetime-local" => HtmlInputTypeState.DateTimeLocal,
			"number" => HtmlInputTypeState.Number,
			"range" => HtmlInputTypeState.Range,
			"color" => HtmlInputTypeState.Color,
			"checkbox" => HtmlInputTypeState.Checkbox,
			"radio" => HtmlInputTypeState.Radio,
			"file" => HtmlInputTypeState.File,
			"submit" => HtmlInputTypeState.Submit,
			"image" => HtmlInputTypeState.Image,
			"reset" => HtmlInputTypeState.Reset,
			"button" => HtmlInputTypeState.Button,
			"hidden" => HtmlInputTypeState.Hidden,
			_ => HtmlInputTypeState.Text
		};

	public static HtmlSelectPresentationState ResolveSelectPresentation(
		bool multiple,
		int? size) =>
		multiple
			? HtmlSelectPresentationState.VisibleMultipleSelectionList
			: size is > 1
				? HtmlSelectPresentationState.VisibleSingleSelectionList
				: HtmlSelectPresentationState.DropDownSingleSelection;

	public static HtmlCssFormattingContext ParseFormattingContext(
		string? display,
		string? flexDirection = null)
	{
		var direction = flexDirection?.Trim().ToLowerInvariant();
		return display?.Trim().ToLowerInvariant() switch
		{
			"none" => HtmlCssFormattingContext.NotRendered,
			"contents" => HtmlCssFormattingContext.Contents,
			"inline" => HtmlCssFormattingContext.Inline,
			"inline-block" => HtmlCssFormattingContext.InlineBlock,
			"block" or "flow-root" => HtmlCssFormattingContext.BlockFlow,
			"flex" or "inline-flex" when direction is "column" or "column-reverse" =>
				HtmlCssFormattingContext.FlexColumn,
			"flex" or "inline-flex" => HtmlCssFormattingContext.FlexRow,
			"grid" or "inline-grid" => HtmlCssFormattingContext.Grid,
			"table" or "inline-table" => HtmlCssFormattingContext.Table,
			"table-header-group" or "table-row-group" or "table-footer-group" =>
				HtmlCssFormattingContext.TableRowGroup,
			"table-row" => HtmlCssFormattingContext.TableRow,
			"table-cell" => HtmlCssFormattingContext.TableCell,
			"list-item" => HtmlCssFormattingContext.ListItem,
			null or "" => HtmlCssFormattingContext.Unknown,
			_ => HtmlCssFormattingContext.Unknown
		};
	}
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Iwesun.Runtime.Web.WinUI;

internal abstract class HtmlTablePanelBase : HtmlCursorPanel
{
	public double RowSpacing { get; set; }

	public double ColumnSpacing { get; set; }
}

internal sealed class HtmlTablePanel : HtmlTablePanelBase
{
	protected override Size MeasureOverride(Size availableSize)
	{
		var rows = EnumerateRows().ToArray();
		foreach (var row in rows)
		{
			row.RowSpacing = RowSpacing;
			row.ColumnSpacing = ColumnSpacing;
			row.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
		}
		foreach (var section in Children.OfType<HtmlTableSectionPanel>())
		{
			section.RowSpacing = RowSpacing;
			section.ColumnSpacing = ColumnSpacing;
		}
		var columnWidths = ResolveColumnWidths(rows, availableSize.Width);
		foreach (var row in rows)
			row.ColumnWidths = columnWidths;

		var width = 0d;
		var height = 0d;
		foreach (var child in Children)
		{
			child.Measure(new(availableSize.Width, double.PositiveInfinity));
			width = Math.Max(width, child.DesiredSize.Width);
			if (height > 0)
				height += RowSpacing;
			height += child.DesiredSize.Height;
		}
		return new(
			double.IsFinite(availableSize.Width)
				? Math.Min(availableSize.Width, width)
				: width,
			height);
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		var y = 0d;
		foreach (var child in Children)
		{
			var height = child.DesiredSize.Height;
			child.Arrange(new(0, y, finalSize.Width, height));
			y += height + RowSpacing;
		}
		return finalSize;
	}

	private IEnumerable<HtmlTableRowPanel> EnumerateRows()
	{
		foreach (var child in Children)
		{
			if (child is HtmlTableRowPanel row)
			{
				yield return row;
				continue;
			}
			if (child is not HtmlTableSectionPanel section)
				continue;
			foreach (var sectionRow in section.Children
				.OfType<HtmlTableRowPanel>())
			{
				yield return sectionRow;
			}
		}
	}

	private IReadOnlyList<double> ResolveColumnWidths(
		IReadOnlyList<HtmlTableRowPanel> rows,
		double availableWidth)
	{
		var columnCount = rows.Count == 0
			? 0
			: rows.Max(static row =>
				row.Children.Sum(HtmlTableRowPanel.GetColumnSpan));
		var widths = new double[columnCount];
		foreach (var row in rows)
		{
			var column = 0;
			foreach (var child in row.Children)
			{
				var span = HtmlTableRowPanel.GetColumnSpan(child);
				var perColumn = child.DesiredSize.Width / span;
				for (var offset = 0;
					offset < span && column + offset < widths.Length;
					offset++)
				{
					widths[column + offset] = Math.Max(
						widths[column + offset],
						perColumn);
				}
				column += span;
			}
		}
		if (widths.Length == 0 || !double.IsFinite(availableWidth))
			return widths;
		var spacing = Math.Max(0, widths.Length - 1) * ColumnSpacing;
		var contentWidth = Math.Max(0, availableWidth - spacing);
		var measured = widths.Sum();
		if (measured <= 0)
			return Enumerable.Repeat(
				contentWidth / widths.Length,
				widths.Length).ToArray();
		var scale = contentWidth / measured;
		for (var index = 0; index < widths.Length; index++)
			widths[index] *= scale;
		return widths;
	}
}

internal sealed class HtmlTableSectionPanel : HtmlTablePanelBase
{
	protected override Size MeasureOverride(Size availableSize)
	{
		var width = 0d;
		var height = 0d;
		foreach (var child in Children)
		{
			child.Measure(new(availableSize.Width, double.PositiveInfinity));
			width = Math.Max(width, child.DesiredSize.Width);
			if (height > 0)
				height += RowSpacing;
			height += child.DesiredSize.Height;
		}
		return new(width, height);
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		var rows = Children.OfType<HtmlTableRowPanel>().ToArray();
		var rowHeights = rows.Select(
			static row => row.DesiredSize.Height).ToArray();
		var y = 0d;
		var rowIndex = 0;
		for (var index = 0; index < Children.Count; index++)
		{
			var child = Children[index];
			var height = child.DesiredSize.Height;
			if (child is HtmlTableRowPanel row)
			{
				row.SpannedRowHeights = rowHeights.Skip(rowIndex).ToArray();
				rowIndex++;
			}
			child.Arrange(new(0, y, finalSize.Width, height));
			y += height + RowSpacing;
		}
		return finalSize;
	}
}

internal sealed class HtmlTableRowPanel : HtmlTablePanelBase
{
	internal IReadOnlyList<double>? ColumnWidths { get; set; }

	internal IReadOnlyList<double>? SpannedRowHeights { get; set; }

	protected override Size MeasureOverride(Size availableSize)
	{
		var width = 0d;
		var height = 0d;
		var column = 0;
		foreach (var child in Children)
		{
			var span = GetColumnSpan(child);
			var assignedWidth = ColumnWidths is null
				? double.PositiveInfinity
				: SumColumns(ColumnWidths, column, span)
					+ ColumnSpacing * Math.Max(0, span - 1);
			child.Measure(new(assignedWidth, availableSize.Height));
			width += double.IsFinite(assignedWidth)
				? assignedWidth
				: child.DesiredSize.Width;
			if (column > 0)
				width += ColumnSpacing;
			height = Math.Max(height, child.DesiredSize.Height);
			column += span;
		}
		return new(width, height);
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		if (Children.Count == 0)
			return finalSize;
		var x = 0d;
		var column = 0;
		foreach (var child in Children)
		{
			var columnSpan = GetColumnSpan(child);
			var width = ColumnWidths is null
				? finalSize.Width / Children.Count
				: SumColumns(ColumnWidths, column, columnSpan)
					+ ColumnSpacing * Math.Max(0, columnSpan - 1);
			var rowSpan = GetRowSpan(child);
			var height = SpannedRowHeights is null
				? finalSize.Height
				: SpannedRowHeights.Take(rowSpan).Sum()
					+ RowSpacing * Math.Max(0, rowSpan - 1);
			child.Arrange(new(x, 0, width, height));
			x += width + ColumnSpacing;
			column += columnSpan;
		}
		return finalSize;
	}

	private static double SumColumns(
		IReadOnlyList<double> widths,
		int start,
		int count)
	{
		var total = 0d;
		for (var index = start;
			index < start + count && index < widths.Count;
			index++)
		{
			total += widths[index];
		}
		return total;
	}

	internal static readonly DependencyProperty ColumnSpanProperty =
		DependencyProperty.RegisterAttached(
			"ColumnSpan",
			typeof(int),
			typeof(HtmlTableRowPanel),
			new PropertyMetadata(1));

	internal static void SetColumnSpan(DependencyObject element, int value) =>
		element.SetValue(ColumnSpanProperty, Math.Max(1, value));

	internal static int GetColumnSpan(DependencyObject element) =>
		(int)element.GetValue(ColumnSpanProperty);

	internal static readonly DependencyProperty RowSpanProperty =
		DependencyProperty.RegisterAttached(
			"RowSpan",
			typeof(int),
			typeof(HtmlTableRowPanel),
			new PropertyMetadata(1));

	internal static void SetRowSpan(DependencyObject element, int value) =>
		element.SetValue(RowSpanProperty, Math.Max(1, value));

	internal static int GetRowSpan(DependencyObject element) =>
		(int)element.GetValue(RowSpanProperty);
}

using System.Globalization;
using System.Text.RegularExpressions;

namespace Iwesun.Runtime.Web;

public abstract partial class HtmlLayoutDomElementDefinition
{
	private sealed record StrongGridPlacement(
		DomElement Child,
		int Row,
		int Column,
		int RowSpan,
		int ColumnSpan);

	private sealed record StrongGridPlan(
		IReadOnlyList<XamlGridTrackDefinition> Rows,
		IReadOnlyList<XamlGridTrackDefinition> Columns,
		IReadOnlyList<StrongGridPlacement> Placements);

	private sealed record AxisPlacement(int? Start, int Span);

	protected override bool TryResolveXamlGridPlacement(
		DomElement child,
		out int row,
		out int column,
		out int rowSpan,
		out int columnSpan)
	{
		if (TryBuildStrongGridPlan(out var plan)
			&& plan.Placements.FirstOrDefault(item => ReferenceEquals(item.Child, child))
				is { } placement)
		{
			row = placement.Row;
			column = placement.Column;
			rowSpan = placement.RowSpan;
			columnSpan = placement.ColumnSpan;
			return true;
		}
		row = column = 0;
		rowSpan = columnSpan = 1;
		return false;
	}

	private bool TryBuildStrongGridPlan(out StrongGridPlan plan)
	{
		if (CreateXaml().Kind != XamlElementMappingKind.GridLayout)
		{
			plan = new([], [], []);
			return false;
		}

		var children = Children.Where(static child => !IsOutOfFlow(child)).ToArray();
		var rowExpression = RuntimeValue(this, "style.gridTemplateRows");
		var columnExpression = RuntimeValue(this, "style.gridTemplateColumns");
		var rows = ParseGridTracks(rowExpression, vertical: true).ToList();
		var columns = ParseGridTracks(columnExpression, vertical: false).ToList();
		var areas = ParseGridAreas(RuntimeValue(this, "style.gridTemplateAreas"));
		var rowNames = ParseGridLineNames(rowExpression);
		var columnNames = ParseGridLineNames(columnExpression);
		var areaRowCount = areas.Count == 0 ? 0 : areas.Values.Max(static area => area.Row + area.RowSpan);
		var areaColumnCount = areas.Count == 0 ? 0 : areas.Values.Max(static area => area.Column + area.ColumnSpan);
		var rowCount = Math.Max(rows.Count, areaRowCount);
		var columnCount = Math.Max(columns.Count, areaColumnCount);
		var flow = RuntimeValue(this, "style.gridAutoFlow") ?? "row";
		var columnFlow = flow.StartsWith("column", StringComparison.OrdinalIgnoreCase);
		var dense = flow.Contains("dense", StringComparison.OrdinalIgnoreCase);
		if (rowCount == 0)
			rowCount = columnFlow ? Math.Max(1, children.Length) : 1;
		if (columnCount == 0)
			columnCount = columnFlow ? 1 : Math.Max(1, children.Length);

		var specifications = children.Select(child =>
		{
			var areaName = RuntimeValue(child, "style.gridArea");
			if (!string.IsNullOrWhiteSpace(areaName)
				&& areas.TryGetValue(areaName.Trim(), out var area))
			{
				return (Child: child,
					Row: new AxisPlacement(area.Row, area.RowSpan),
					Column: new AxisPlacement(area.Column, area.ColumnSpan));
			}
			return (Child: child,
				Row: ParseGridAxis(RuntimeValue(child, "style.gridRow"), rowCount, rowNames),
				Column: ParseGridAxis(RuntimeValue(child, "style.gridColumn"), columnCount, columnNames));
		}).ToArray();

		var occupied = new HashSet<(int Row, int Column)>();
		var placements = new List<StrongGridPlacement>(children.Length);
		var cursorRow = 0;
		var cursorColumn = 0;
		foreach (var item in specifications)
		{
			var rowSpan = Math.Max(1, item.Row.Span);
			var columnSpan = Math.Max(1, item.Column.Span);
			var row = item.Row.Start;
			var column = item.Column.Start;
			FindAvailableGridCell(
				occupied,
				columnFlow,
				dense,
				ref row,
				ref column,
				rowSpan,
				columnSpan,
				Math.Max(1, rowCount),
				Math.Max(1, columnCount),
				ref cursorRow,
				ref cursorColumn);
			var resolvedRow = row ?? 0;
			var resolvedColumn = column ?? 0;
			OccupyGridCells(occupied, resolvedRow, resolvedColumn, rowSpan, columnSpan);
			rowCount = Math.Max(rowCount, resolvedRow + rowSpan);
			columnCount = Math.Max(columnCount, resolvedColumn + columnSpan);
			placements.Add(new(item.Child, resolvedRow, resolvedColumn, rowSpan, columnSpan));
		}

		AppendImplicitGridTracks(rows, rowCount, "style.gridAutoRows", "style.height", "style.rowGap");
		AppendImplicitGridTracks(columns, columnCount, "style.gridAutoColumns", "style.width", "style.columnGap");
		plan = new(rows, columns, placements);
		return true;
	}

	private static void FindAvailableGridCell(
		HashSet<(int Row, int Column)> occupied,
		bool columnFlow,
		bool dense,
		ref int? row,
		ref int? column,
		int rowSpan,
		int columnSpan,
		int rowCount,
		int columnCount,
		ref int cursorRow,
		ref int cursorColumn)
	{
		if (row is not null && column is not null)
			return;
		var fixedRow = row is not null;
		var fixedColumn = column is not null;
		var startRow = dense ? 0 : cursorRow;
		var startColumn = dense ? 0 : cursorColumn;
		for (var index = 0; index < 100000; index++)
		{
			var candidateRow = row ?? startRow;
			var candidateColumn = column ?? startColumn;
			var withinExplicitMinorAxis = columnFlow
				? fixedRow || candidateRow + rowSpan <= rowCount
				: fixedColumn || candidateColumn + columnSpan <= columnCount;
			if (withinExplicitMinorAxis
				&& IsGridAreaFree(occupied, candidateRow, candidateColumn, rowSpan, columnSpan))
			{
				row = candidateRow;
				column = candidateColumn;
				if (!dense)
				{
					cursorRow = candidateRow;
					cursorColumn = candidateColumn;
					AdvanceGridCursor(columnFlow, ref cursorRow, ref cursorColumn, rowCount, columnCount);
				}
				return;
			}
			if (row is not null)
				startColumn++;
			else if (column is not null)
				startRow++;
			else
				AdvanceGridCursor(columnFlow, ref startRow, ref startColumn, rowCount, columnCount);
		}
		throw new InvalidOperationException("CSS grid auto placement exceeded its bounded search space.");
	}

	private static void AdvanceGridCursor(
		bool columnFlow,
		ref int row,
		ref int column,
		int rowCount,
		int columnCount)
	{
		if (columnFlow)
		{
			if (++row >= rowCount)
			{
				row = 0;
				column++;
			}
		}
		else if (++column >= columnCount)
		{
			column = 0;
			row++;
		}
	}

	private static bool IsGridAreaFree(
		HashSet<(int Row, int Column)> occupied,
		int row,
		int column,
		int rowSpan,
		int columnSpan)
	{
		for (var y = row; y < row + rowSpan; y++)
			for (var x = column; x < column + columnSpan; x++)
				if (occupied.Contains((y, x)))
					return false;
		return true;
	}

	private static void OccupyGridCells(
		HashSet<(int Row, int Column)> occupied,
		int row,
		int column,
		int rowSpan,
		int columnSpan)
	{
		for (var y = row; y < row + rowSpan; y++)
			for (var x = column; x < column + columnSpan; x++)
				occupied.Add((y, x));
	}

	private AxisPlacement ParseGridAxis(
		string? expression,
		int trackCount,
		IReadOnlyDictionary<string, IReadOnlyList<int>> lineNames)
	{
		if (string.IsNullOrWhiteSpace(expression)
			|| expression.Equals("auto", StringComparison.OrdinalIgnoreCase))
			return new(null, 1);
		var parts = expression.Split('/', StringSplitOptions.TrimEntries);
		if (parts[0].StartsWith("span ", StringComparison.OrdinalIgnoreCase))
			return new(null, ParseGridSpan(parts[0]));
		var start = ResolveGridLine(parts[0], trackCount, lineNames);
		if (parts.Length == 1)
			return new(start, 1);
		if (parts[1].StartsWith("span ", StringComparison.OrdinalIgnoreCase))
			return new(start, ParseGridSpan(parts[1]));
		var end = ResolveGridLine(parts[1], trackCount, lineNames);
		return new(start, start is not null && end is not null ? Math.Max(1, end.Value - start.Value) : 1);
	}

	private static int ParseGridSpan(string value)
	{
		var token = value[5..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
		return int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var span)
			? Math.Max(1, span)
			: 1;
	}

	private static int? ResolveGridLine(
		string token,
		int trackCount,
		IReadOnlyDictionary<string, IReadOnlyList<int>> lineNames)
	{
		var parts = token.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length == 0 || parts[0].Equals("auto", StringComparison.OrdinalIgnoreCase))
			return null;
		if (int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
			return numeric > 0 ? numeric - 1 : Math.Max(0, trackCount + 1 + numeric);
		if (!lineNames.TryGetValue(parts[0], out var matches) || matches.Count == 0)
			return null;
		var occurrence = parts.Length > 1
			&& int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
				? parsed
				: 1;
		var index = occurrence > 0 ? occurrence - 1 : matches.Count + occurrence;
		return index >= 0 && index < matches.Count ? matches[index] : null;
	}

	private static IReadOnlyDictionary<string, IReadOnlyList<int>> ParseGridLineNames(string? expression)
	{
		var result = new Dictionary<string, List<int>>(StringComparer.Ordinal);
		if (string.IsNullOrWhiteSpace(expression))
			return result.ToDictionary(static pair => pair.Key, static pair => (IReadOnlyList<int>)pair.Value, StringComparer.Ordinal);
		var line = 0;
		foreach (var token in SplitGridTrackTokens(expression))
		{
			if (token.StartsWith("[", StringComparison.Ordinal) && token.EndsWith("]", StringComparison.Ordinal))
			{
				foreach (var name in token[1..^1].Split(' ', StringSplitOptions.RemoveEmptyEntries))
				{
					if (!result.TryGetValue(name, out var indexes))
						result[name] = indexes = [];
					indexes.Add(line);
				}
			}
			else
				line++;
		}
		return result.ToDictionary(static pair => pair.Key, static pair => (IReadOnlyList<int>)pair.Value, StringComparer.Ordinal);
	}

	private static IReadOnlyDictionary<string, (int Row, int Column, int RowSpan, int ColumnSpan)> ParseGridAreas(string? expression)
	{
		var rows = string.IsNullOrWhiteSpace(expression)
			? []
			: Regex.Matches(expression, "[\"']([^\"']+)[\"']")
				.Select(static match => match.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
				.ToArray();
		var cells = new Dictionary<string, List<(int Row, int Column)>>(StringComparer.Ordinal);
		for (var row = 0; row < rows.Length; row++)
			for (var column = 0; column < rows[row].Length; column++)
				if (rows[row][column] != ".")
				{
					if (!cells.TryGetValue(rows[row][column], out var values))
						cells[rows[row][column]] = values = [];
					values.Add((row, column));
				}
		return cells.ToDictionary(
			static pair => pair.Key,
			static pair =>
			{
				var minRow = pair.Value.Min(static cell => cell.Row);
				var maxRow = pair.Value.Max(static cell => cell.Row);
				var minColumn = pair.Value.Min(static cell => cell.Column);
				var maxColumn = pair.Value.Max(static cell => cell.Column);
				return (minRow, minColumn, maxRow - minRow + 1, maxColumn - minColumn + 1);
			},
			StringComparer.Ordinal);
	}

	private void AppendImplicitGridTracks(
		List<XamlGridTrackDefinition> tracks,
		int requiredCount,
		string automaticProperty,
		string extentProperty,
		string gapProperty)
	{
		var automatic = ParseGridTracks(RuntimeValue(this, automaticProperty), vertical: automaticProperty.EndsWith("Rows", StringComparison.Ordinal)).FirstOrDefault()
			?? new XamlGridTrackDefinition("Auto", null, null);
		if (automatic.Length == "Auto")
		{
			var extent = ParseGridPixels(RuntimeValue(this, extentProperty));
			var gap = ParseGridPixels(RuntimeValue(this, gapProperty));
			if (extent > 0 && requiredCount > 0)
				automatic = new(Math.Max(0, (extent - gap * (requiredCount - 1)) / requiredCount).ToString("R", CultureInfo.InvariantCulture), null, null);
		}
		while (tracks.Count < requiredCount)
			tracks.Add(automatic);
	}
}

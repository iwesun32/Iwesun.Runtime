using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Iwesun.Runtime.Web.WinUI;

internal static partial class SvgPathGeometryParser
{
	internal static PathGeometry Parse(string data)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(data);
		var tokens = TokenPattern().Matches(data)
			.Select(static match => match.Value)
			.ToArray();
		var reader = new TokenReader(tokens);
		var geometry = new PathGeometry();
		PathFigure? figure = null;
		var current = new Point();
		var subpathStart = new Point();
		var lastCubicControl = new Point();
		var lastQuadraticControl = new Point();
		var previousCommand = '\0';
		var command = '\0';
		while (reader.HasMore)
		{
			if (reader.IsCommand)
				command = reader.ReadCommand();
			else if (command == '\0')
				throw new FormatException("SVG path data must begin with a command.");
			var relative = char.IsLower(command);
			var normalized = char.ToUpperInvariant(command);
			switch (normalized)
			{
				case 'M':
				current = ReadPoint(reader, current, relative);
				figure = new PathFigure { StartPoint = current };
				geometry.Figures.Add(figure);
				subpathStart = current;
				while (reader.HasNumber)
				AddLine(RequireFigure(figure), ref current, ReadPoint(reader, current, relative));
				command = relative ? 'l' : 'L';
				break;
			case 'L':
				do
				{
					AddLine(RequireFigure(figure), ref current, ReadPoint(reader, current, relative));
				}
				while (reader.HasNumber);
				break;
			case 'H':
				do
				{
					var x = reader.ReadNumber();
					current = new(
						relative ? current.X + x : x,
						current.Y);
					RequireFigure(figure).Segments.Add(
						new LineSegment { Point = current });
				}
				while (reader.HasNumber);
				break;
			case 'V':
				do
				{
					var y = reader.ReadNumber();
					current = new(
						current.X,
						relative ? current.Y + y : y);
					RequireFigure(figure).Segments.Add(
						new LineSegment { Point = current });
				}
				while (reader.HasNumber);
				break;
			case 'C':
				do
				{
					var first = ReadPoint(reader, current, relative);
					var second = ReadPoint(reader, current, relative);
					var end = ReadPoint(reader, current, relative);
					RequireFigure(figure).Segments.Add(new BezierSegment
					{
						Point1 = first,
						Point2 = second,
						Point3 = end
					});
					lastCubicControl = second;
					current = end;
				}
				while (reader.HasNumber);
				break;
			case 'S':
				var reflectCubic = previousCommand is 'C' or 'S';
				do
				{
					var first = reflectCubic
						? Reflect(lastCubicControl, current)
						: current;
					var second = ReadPoint(reader, current, relative);
					var end = ReadPoint(reader, current, relative);
					RequireFigure(figure).Segments.Add(new BezierSegment
					{
						Point1 = first,
						Point2 = second,
						Point3 = end
					});
					lastCubicControl = second;
					current = end;
					reflectCubic = true;
				}
				while (reader.HasNumber);
				break;
			case 'Q':
				do
				{
					var control = ReadPoint(reader, current, relative);
					var end = ReadPoint(reader, current, relative);
					RequireFigure(figure).Segments.Add(
						new QuadraticBezierSegment
						{
							Point1 = control,
							Point2 = end
						});
					lastQuadraticControl = control;
					current = end;
				}
				while (reader.HasNumber);
				break;
			case 'T':
				var reflectQuadratic = previousCommand is 'Q' or 'T';
				do
				{
					var control = reflectQuadratic
						? Reflect(lastQuadraticControl, current)
						: current;
					var end = ReadPoint(reader, current, relative);
					RequireFigure(figure).Segments.Add(
						new QuadraticBezierSegment
						{
							Point1 = control,
							Point2 = end
						});
					lastQuadraticControl = control;
					current = end;
					reflectQuadratic = true;
				}
				while (reader.HasNumber);
				break;
			case 'A':
				do
				{
					var radiusX = reader.ReadNumber();
					var radiusY = reader.ReadNumber();
					var rotation = reader.ReadNumber();
					var largeArc = reader.ReadFlag();
					var sweep = reader.ReadFlag();
					var end = ReadPoint(reader, current, relative);
					RequireFigure(figure).Segments.Add(new ArcSegment
					{
						Size = new(
							Math.Abs(radiusX),
							Math.Abs(radiusY)),
						RotationAngle = rotation,
						IsLargeArc = largeArc,
						SweepDirection = sweep
							? SweepDirection.Clockwise
							: SweepDirection.Counterclockwise,
						Point = end
					});
					current = end;
				}
				while (reader.HasNumber);
				break;
			case 'Z':
				RequireFigure(figure).IsClosed = true;
				current = subpathStart;
				command = '\0';
				break;
			default:
				throw new FormatException(
					$"Unsupported SVG path command '{command}'.");
			}
			previousCommand = normalized;
		}
		return geometry;
	}

	private static void AddLine(
		PathFigure figure,
		ref Point current,
		Point end)
	{
		figure.Segments.Add(new LineSegment { Point = end });
		current = end;
	}

	private static Point ReadPoint(
		TokenReader reader,
		Point origin,
		bool relative)
	{
		var x = reader.ReadNumber();
		var y = reader.ReadNumber();
		return relative
			? new(origin.X + x, origin.Y + y)
			: new(x, y);
	}

	private static Point Reflect(Point control, Point around) =>
		new(
			2 * around.X - control.X,
			2 * around.Y - control.Y);

	private static PathFigure RequireFigure(PathFigure? figure) =>
		figure
			?? throw new FormatException(
				"SVG path segment appears before a move command.");

	[GeneratedRegex(
		@"[AaCcHhLlMmQqSsTtVvZz]|[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?",
		RegexOptions.CultureInvariant)]
	private static partial Regex TokenPattern();

	private sealed class TokenReader(IReadOnlyList<string> tokens)
	{
		private int _index;

		internal bool HasMore => _index < tokens.Count;

		internal bool IsCommand =>
			HasMore && tokens[_index].Length == 1
				&& char.IsLetter(tokens[_index][0]);

		internal bool HasNumber => HasMore && !IsCommand;

		internal char ReadCommand()
		{
			if (!IsCommand)
				throw new FormatException("Expected an SVG path command.");
			return tokens[_index++][0];
		}

		internal double ReadNumber()
		{
			if (!HasNumber)
				throw new FormatException("Expected an SVG path number.");
			return double.Parse(
				tokens[_index++],
				NumberStyles.Float,
				CultureInfo.InvariantCulture);
		}

		internal bool ReadFlag()
		{
			var value = ReadNumber();
			return value switch
			{
				0 => false,
				1 => true,
				_ => throw new FormatException(
					"SVG arc flags must be 0 or 1.")
			};
		}
	}
}

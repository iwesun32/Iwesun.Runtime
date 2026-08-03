using System.Globalization;

namespace Iwesun.Runtime.Web;

public static class HtmlListMarkerFormatter
{
	public static string FormatOrdered(int value, string? type) =>
		type switch
		{
			"a" => $"{ToAlphabetic(value, upper: false)}.",
			"A" => $"{ToAlphabetic(value, upper: true)}.",
			"i" => $"{ToRoman(value).ToLowerInvariant()}.",
			"I" => $"{ToRoman(value)}.",
			_ => $"{value.ToString(CultureInfo.InvariantCulture)}."
		};

	private static string ToAlphabetic(int value, bool upper)
	{
		if (value <= 0)
			return value.ToString(CultureInfo.InvariantCulture);
		var result = string.Empty;
		while (value > 0)
		{
			value--;
			var character = (char)((upper ? 'A' : 'a') + value % 26);
			result = character + result;
			value /= 26;
		}
		return result;
	}

	private static string ToRoman(int value)
	{
		if (value is <= 0 or > 3999)
			return value.ToString(CultureInfo.InvariantCulture);
		var symbols = new (int Value, string Text)[]
		{
			(1000, "M"), (900, "CM"), (500, "D"), (400, "CD"),
			(100, "C"), (90, "XC"), (50, "L"), (40, "XL"),
			(10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")
		};
		var result = string.Empty;
		foreach (var symbol in symbols)
		{
			while (value >= symbol.Value)
			{
				result += symbol.Text;
				value -= symbol.Value;
			}
		}
		return result;
	}
}

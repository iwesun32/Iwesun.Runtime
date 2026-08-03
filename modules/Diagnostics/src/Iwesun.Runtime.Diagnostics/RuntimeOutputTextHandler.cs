using System.Runtime.CompilerServices;
using System.Text;

namespace Iwesun.Runtime.Diagnostics;

[InterpolatedStringHandler]
public ref struct RuntimeOutputTextHandler
{
	private StringBuilder? _builder;

	public RuntimeOutputTextHandler(int literalLength, int formattedCount, out bool shouldAppend)
	{
		shouldAppend = RuntimeOutputSwitch.Enabled;
		_builder = shouldAppend ? new StringBuilder(literalLength) : null;
	}

	public void AppendLiteral(string value)
	{
		_builder?.Append(value);
	}

	public void AppendFormatted<T>(T value)
	{
		_builder?.Append(value);
	}

	public void AppendFormatted<T>(T value, string? format)
	{
		if (_builder is null)
			return;

		if (value is IFormattable formattable)
			_builder.Append(formattable.ToString(format, null));
		else
			_builder.Append(value);
	}

	public override string ToString() => _builder?.ToString() ?? "";
}

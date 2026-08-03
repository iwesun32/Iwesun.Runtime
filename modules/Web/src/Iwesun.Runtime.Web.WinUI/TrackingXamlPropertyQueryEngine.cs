using Iwesun.Runtime.Web;

namespace Iwesun.Runtime.Web.WinUI;

public sealed record WinUiXamlQueryStatistics(
	int Total,
	int Captured,
	int ConfirmedAbsent,
	int TargetUnsupported,
	int PreexistingTargetValues,
	string FirstUnsupported,
	IReadOnlyList<string> UnsupportedSamples);

internal sealed class TrackingXamlPropertyQueryEngine(
	IXamlPropertyQueryEngine inner) : IXamlPropertyQueryEngine
{
	private int _total;
	private int _captured;
	private int _confirmedAbsent;
	private int _targetUnsupported;
	private int _preexistingTargetValues;
	private string _firstUnsupported = string.Empty;
	private readonly List<string> _unsupportedSamples = [];

	internal WinUiXamlQueryStatistics Snapshot() =>
		new(
			_total,
			_captured,
			_confirmedAbsent,
			_targetUnsupported,
			_preexistingTargetValues,
			_firstUnsupported,
			_unsupportedSamples.ToArray());

	public async ValueTask<XamlPropertyQueryResult> QueryAsync(
		XamlPropertyQueryContext context,
		CancellationToken cancellationToken = default)
	{
		if (TargetWasAlreadySet(context))
			_preexistingTargetValues++;
		var result = await inner.QueryAsync(context, cancellationToken);
		_total++;
		switch (result.Status)
		{
			case XamlPropertyQueryStatus.Captured:
				_captured++;
				break;
			case XamlPropertyQueryStatus.ConfirmedAbsent:
				_confirmedAbsent++;
				break;
			case XamlPropertyQueryStatus.TargetUnsupported:
				_targetUnsupported++;
				var diagnostic =
					$"{context.DocumentScope}::{context.XPath}; "
					+ $"{context.PropertyName}; {context.Slot}; "
					+ $"{context.Execution.Kind}; "
					+ $"{context.Execution.TargetProperty}; "
					+ result.Description;
				if (_unsupportedSamples.Count < 20)
					_unsupportedSamples.Add(diagnostic);
				if (_firstUnsupported.Length == 0)
				{
					_firstUnsupported = diagnostic;
				}
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(result.Status));
		}
		return result;
	}

	private static bool TargetWasAlreadySet(XamlPropertyQueryContext context) =>
		context.Slot switch
		{
			XamlPropertyDataSlot.Initialization =>
				context.Target.Initialization.IsSet,
			XamlPropertyDataSlot.Link => context.Target.Link.IsSet,
			XamlPropertyDataSlot.Runtime => context.Target.Runtime.IsSet,
			_ => throw new ArgumentOutOfRangeException(nameof(context.Slot))
		};
}

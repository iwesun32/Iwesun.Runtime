using System.Collections.Concurrent;

namespace Iwesun.Runtime.Web.WinUI;

public sealed record PaddingContainingBlockSnapshot(
	string OwnerType,
	string ChildType,
	double OwnerActualWidth,
	double OwnerActualHeight,
	double LayoutActualWidth,
	double LayoutActualHeight,
	double HostActualWidth,
	double HostActualHeight,
	double HostOffsetX,
	double HostOffsetY,
	double ChildOffsetX,
	double ChildOffsetY,
	int HostRowSpan,
	int HostColumnSpan,
	int LayoutRows,
	int LayoutColumns,
	int ChildGridRow,
	int ChildGridColumn,
	int ChildGridRowSpan,
	int ChildGridColumnSpan,
	bool IsOutOfFlow,
	bool HasLeftInset,
	bool HasTopInset,
	bool HasRightInset,
	bool HasBottomInset,
	string ChildIdentity,
	string HostMargin,
	string ChildMargin,
	string ChildHorizontalAlignment,
	string ChildVerticalAlignment,
	string ChildTranslation);

public static class WinUiLayoutDiagnostics
{
	private const int Capacity = 256;
	private static readonly ConcurrentQueue<PaddingContainingBlockSnapshot>
		PaddingContainingBlocks = new();

	public static IReadOnlyList<PaddingContainingBlockSnapshot>
		SnapshotPaddingContainingBlocks() =>
		PaddingContainingBlocks.ToArray();

	internal static void Record(
		PaddingContainingBlockSnapshot snapshot)
	{
		PaddingContainingBlocks.Enqueue(snapshot);
		while (PaddingContainingBlocks.Count > Capacity)
			PaddingContainingBlocks.TryDequeue(out _);
	}
}

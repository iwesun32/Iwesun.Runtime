namespace Iwesun.Runtime.Diagnostics;

public sealed record RuntimeMutationResult(
    string TargetId,
    string Action,
    string Subject,
    object? PreviousValue,
    object? RequestedValue,
    object? EffectiveValue,
    bool Changed,
    long SnapshotVersion,
    DateTimeOffset GeneratedAt);

namespace Iwesun.Runtime.Networks;

/// <summary>Transient execution policy excluded from stable access identity.</summary>
public readonly record struct NetworkAttemptPolicy(
	int TimeoutMs,
	byte MaximumAttempts,
	byte MaximumConcurrentBranches,
	int BranchStartIntervalMs)
{
	public bool IsValid =>
		TimeoutMs > 0 &&
		MaximumAttempts > 0 &&
		MaximumConcurrentBranches > 0 &&
		BranchStartIntervalMs >= 0;
}

namespace Iwesun.Runtime.Networks;

/// <summary>Identifies one request and its optional execution descendants.</summary>
public readonly record struct NetworkExecutionIdentity(
	Guid RequestId,
	Guid AttemptId,
	Guid BranchId,
	Guid ResponseId)
{
	public bool HasRequest => RequestId != Guid.Empty;
	public bool HasAttempt => HasRequest && AttemptId != Guid.Empty;
	public bool HasBranch => HasAttempt && BranchId != Guid.Empty;
	public bool HasResponse => HasBranch && ResponseId != Guid.Empty;

	public bool IsValid =>
		HasRequest &&
		(AttemptId != Guid.Empty || (BranchId == Guid.Empty && ResponseId == Guid.Empty)) &&
		(BranchId != Guid.Empty || ResponseId == Guid.Empty);

	public static NetworkExecutionIdentity ForRequest(Guid requestId)
	{
		ArgumentOutOfRangeException.ThrowIfEqual(requestId, Guid.Empty);
		return new NetworkExecutionIdentity(requestId, Guid.Empty, Guid.Empty, Guid.Empty);
	}

	public NetworkExecutionIdentity StartAttempt(Guid attemptId)
	{
		if (!HasRequest || AttemptId != Guid.Empty || BranchId != Guid.Empty || ResponseId != Guid.Empty)
		{
			throw new InvalidOperationException("An attempt can only be created directly below a request identity.");
		}

		ArgumentOutOfRangeException.ThrowIfEqual(attemptId, Guid.Empty);
		return this with { AttemptId = attemptId };
	}

	public NetworkExecutionIdentity StartBranch(Guid branchId)
	{
		if (!HasAttempt || BranchId != Guid.Empty || ResponseId != Guid.Empty)
		{
			throw new InvalidOperationException("A branch can only be created directly below an attempt identity.");
		}

		ArgumentOutOfRangeException.ThrowIfEqual(branchId, Guid.Empty);
		return this with { BranchId = branchId };
	}

	public NetworkExecutionIdentity CreateResponse(Guid responseId)
	{
		if (!HasBranch || ResponseId != Guid.Empty)
		{
			throw new InvalidOperationException("A response can only be created directly below a branch identity.");
		}

		ArgumentOutOfRangeException.ThrowIfEqual(responseId, Guid.Empty);
		return this with { ResponseId = responseId };
	}
}

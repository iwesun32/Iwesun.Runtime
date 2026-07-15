namespace Iwesun.Runtime.RemoteConsole.Protocol;

public enum RemoteConsoleApprovalMode
{
	Manual,
	Guarded,
	Automatic
}

public enum RemoteConsoleJobState
{
	Submitted,
	AwaitingApproval,
	Starting,
	Running,
	Completed,
	Failed,
	Rejected,
	Cancelled,
	Interrupted
}

public enum RemoteConsoleOutputStream
{
	Stdout,
	Stderr
}

public sealed record RemoteConsoleSubmitRequest(
	string RequestId,
	string Shell,
	string Command,
	string WorkspaceId,
	IReadOnlyDictionary<string, string> Environment,
	string RiskHint,
	string WorkingDirectory = "");

public sealed record RemoteConsoleJobRequest(string JobId);

public sealed record RemoteConsoleFollowRequest(
	string JobId,
	long AfterSequence,
	int Limit,
	int WaitMs);

public sealed record RemoteConsoleApproveRequest(string JobId);

public sealed record RemoteConsoleRejectRequest(string JobId, string Reason);

public sealed record RemoteConsoleCancelRequest(string JobId);

public sealed record RemoteConsolePolicySetRequest(RemoteConsoleApprovalMode Mode);

public sealed record RemoteConsoleWorkspaceCreateRequest(string? Description);

public sealed record RemoteConsoleWorkspaceRequest(string WorkspaceId);

public sealed record RemoteConsoleFileListRequest(string WorkspaceId, string RelativePath);

public sealed record RemoteConsoleUploadBeginRequest(
	string WorkspaceId,
	string RelativePath,
	long Length,
	string Sha256);

public sealed record RemoteConsoleUploadChunkRequest(
	string UploadId,
	int Sequence,
	string Base64Data);

public sealed record RemoteConsoleUploadCommitRequest(string UploadId);

public sealed record RemoteConsoleOutputChunk(
	long Sequence,
	RemoteConsoleOutputStream Stream,
	DateTimeOffset Timestamp,
	string Text);

public sealed record RemoteConsoleFollowResult(
	string JobId,
	RemoteConsoleJobState State,
	IReadOnlyList<RemoteConsoleOutputChunk> Chunks,
	long EarliestAvailableSequence,
	bool Truncated,
	int? ExitCode);

public sealed record RemoteConsoleJobSnapshot(
	string JobId,
	string RequestId,
	string SubmitterSid,
	RemoteConsoleJobState State,
	string Shell,
	string Command,
	string WorkspaceId,
	string ContentHash,
	DateTimeOffset SubmittedAt,
	DateTimeOffset? StartedAt,
	DateTimeOffset? CompletedAt,
	int? ExitCode,
	string? DecisionBySid,
	string? DecisionReason);

public sealed record RemoteConsoleWorkspaceSnapshot(
	string WorkspaceId,
	string Description,
	DateTimeOffset CreatedAt,
	DateTimeOffset LastAccessedAt,
	long Bytes);

public sealed record RemoteConsoleFileSnapshot(
	string RelativePath,
	long Length,
	DateTimeOffset LastWriteTime);

public sealed record RemoteConsoleUploadSessionSnapshot(
	string UploadId,
	string WorkspaceId,
	string RelativePath,
	long ExpectedLength,
	long ReceivedLength,
	int NextSequence);

public sealed record RemoteConsolePolicySnapshot(
	RemoteConsoleApprovalMode Mode,
	IReadOnlyList<string> AutoApprovePatterns,
	IReadOnlyList<string> DenyPatterns);

public sealed record RemoteConsoleValidationResult(bool Ok, string Code, string Message)
{
	public static RemoteConsoleValidationResult Success() => new(true, "OK", "");

	public static RemoteConsoleValidationResult Fail(string message) =>
		new(false, RemoteConsoleErrorCodes.InvalidRequest, message);
}

public sealed record RemoteConsoleOperationResult<T>(
	bool Ok,
	string Code,
	string Message,
	T? Data);

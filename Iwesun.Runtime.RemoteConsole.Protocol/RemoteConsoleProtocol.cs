using System.Text.Json;
using Iwesun.Runtime.Diagnostics;

namespace Iwesun.Runtime.RemoteConsole.Protocol;

public static class RemoteConsoleActions
{
	public const string ServerInfo = "server.info";
	public const string JobSubmit = "job.submit";
	public const string JobStatus = "job.status";
	public const string JobFollow = "job.follow";
	public const string JobPending = "job.pending";
	public const string JobApprove = "job.approve";
	public const string JobReject = "job.reject";
	public const string JobCancel = "job.cancel";
	public const string PolicyGet = "policy.get";
	public const string PolicySet = "policy.set";
	public const string WorkspaceCreate = "workspace.create";
	public const string WorkspaceList = "workspace.list";
	public const string WorkspaceShow = "workspace.show";
	public const string WorkspaceRemove = "workspace.remove";
	public const string FileList = "file.list";
	public const string UploadBegin = "file.upload.begin";
	public const string UploadChunk = "file.upload.chunk";
	public const string UploadCommit = "file.upload.commit";

	public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		ServerInfo,
		JobSubmit,
		JobStatus,
		JobFollow,
		JobPending,
		JobApprove,
		JobReject,
		JobCancel,
		PolicyGet,
		PolicySet,
		WorkspaceCreate,
		WorkspaceList,
		WorkspaceShow,
		WorkspaceRemove,
		FileList,
		UploadBegin,
		UploadChunk,
		UploadCommit
	};
}

public static class RemoteConsoleErrorCodes
{
	public const string InvalidRequest = "RC_INVALID_REQUEST";
	public const string AccessDenied = "RC_ACCESS_DENIED";
	public const string SelfApprovalDenied = "RC_SELF_APPROVAL_DENIED";
	public const string ApprovalRequired = "RC_APPROVAL_REQUIRED";
	public const string JobNotFound = "RC_JOB_NOT_FOUND";
	public const string JobStateConflict = "RC_JOB_STATE_CONFLICT";
	public const string WorkspaceNotFound = "RC_WORKSPACE_NOT_FOUND";
	public const string PathOutsideWorkspace = "RC_PATH_OUTSIDE_WORKSPACE";
	public const string UploadNotFound = "RC_UPLOAD_NOT_FOUND";
	public const string UploadSequenceConflict = "RC_UPLOAD_SEQUENCE_CONFLICT";
	public const string UploadHashMismatch = "RC_UPLOAD_HASH_MISMATCH";
	public const string QuotaExceeded = "RC_QUOTA_EXCEEDED";
	public const string CommandStartFailed = "RC_COMMAND_START_FAILED";
	public const string OutputTruncated = "RC_OUTPUT_TRUNCATED";
	public const string RemoteUnreachable = "RC_REMOTE_UNREACHABLE";
}

public static class RemoteConsoleProtocol
{
	public const string Domain = "remote.console";
	public const string ServerTarget = "remote.console.server";
	public const int MaxUploadChunkBytes = 64 * 1024;
	public const int MaxFollowLimit = 4096;
	public const int MaxFollowWaitMs = 30_000;
	private const string RequestArgument = "request";
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	public static bool IsKnownAction(string? action) =>
		!string.IsNullOrWhiteSpace(action) && RemoteConsoleActions.All.Contains(action);

	public static bool IsKnownState(RemoteConsoleJobState state) => Enum.IsDefined(state);

	public static RemoteConsoleValidationResult Validate(RemoteConsoleSubmitRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		if (string.IsNullOrWhiteSpace(request.RequestId))
			return RemoteConsoleValidationResult.Fail("requestId is required.");
		if (string.IsNullOrWhiteSpace(request.Shell))
			return RemoteConsoleValidationResult.Fail("shell is required.");
		if (string.IsNullOrWhiteSpace(request.Command))
			return RemoteConsoleValidationResult.Fail("command is required.");
		if (string.IsNullOrWhiteSpace(request.WorkspaceId))
			return RemoteConsoleValidationResult.Fail("workspaceId is required.");
		if (request.Environment is null)
			return RemoteConsoleValidationResult.Fail("environment is required.");
		return RemoteConsoleValidationResult.Success();
	}

	public static RemoteConsoleValidationResult Validate(RemoteConsoleFollowRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		if (string.IsNullOrWhiteSpace(request.JobId))
			return RemoteConsoleValidationResult.Fail("jobId is required.");
		if (request.AfterSequence < 0)
			return RemoteConsoleValidationResult.Fail("afterSequence cannot be negative.");
		if (request.Limit <= 0 || request.Limit > MaxFollowLimit)
			return RemoteConsoleValidationResult.Fail($"limit must be between 1 and {MaxFollowLimit}.");
		if (request.WaitMs < 0 || request.WaitMs > MaxFollowWaitMs)
			return RemoteConsoleValidationResult.Fail($"waitMs must be between 0 and {MaxFollowWaitMs}.");
		return RemoteConsoleValidationResult.Success();
	}

	public static RemoteConsoleValidationResult Validate(RemoteConsoleUploadChunkRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		if (string.IsNullOrWhiteSpace(request.UploadId))
			return RemoteConsoleValidationResult.Fail("uploadId is required.");
		if (request.Sequence < 0)
			return RemoteConsoleValidationResult.Fail("sequence cannot be negative.");
		if (string.IsNullOrWhiteSpace(request.Base64Data))
			return RemoteConsoleValidationResult.Fail("base64Data is required.");

		var maxEncodedChars = ((MaxUploadChunkBytes + 2) / 3) * 4;
		if (request.Base64Data.Length > maxEncodedChars)
			return RemoteConsoleValidationResult.Fail($"Decoded upload chunk exceeds {MaxUploadChunkBytes} bytes.");

		var decoded = new byte[MaxUploadChunkBytes];
		if (!Convert.TryFromBase64String(request.Base64Data, decoded, out var bytesWritten))
			return RemoteConsoleValidationResult.Fail("base64Data is invalid.");
		if (bytesWritten > MaxUploadChunkBytes)
			return RemoteConsoleValidationResult.Fail($"Decoded upload chunk exceeds {MaxUploadChunkBytes} bytes.");
		return RemoteConsoleValidationResult.Success();
	}

	public static RuntimeDiagnosticFrame CreateRequest<T>(string action, string requestId, T request)
	{
		if (!IsKnownAction(action))
			throw new ArgumentException($"Unknown RemoteConsole action: {action}", nameof(action));
		ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
		ArgumentNullException.ThrowIfNull(request);

		return new RuntimeDiagnosticFrame
		{
			Header = new RuntimeDiagnosticFrameHeader
			{
				Schema = RuntimeDiagnosticProtocol.V2Schema,
				FrameType = "request",
				Category = "control",
				Operation = action,
				RequestId = requestId,
				Source = "runtime-cli",
				Destination = ServerTarget
			},
			Command = new RuntimeDiagnosticFrameCommand
			{
				Domain = Domain,
				Target = ServerTarget,
				Action = action,
				Args = new Dictionary<string, JsonElement>
				{
					[RequestArgument] = JsonSerializer.SerializeToElement(request, JsonOptions)
				}
			}
		};
	}

	public static T ParseRequest<T>(RuntimeDiagnosticFrame frame, string expectedAction)
	{
		ArgumentNullException.ThrowIfNull(frame);
		var command = frame.Command;
		if (command is null
			|| !string.Equals(command.Domain, Domain, StringComparison.OrdinalIgnoreCase)
			|| !string.Equals(command.Target, ServerTarget, StringComparison.OrdinalIgnoreCase)
			|| !string.Equals(command.Action, expectedAction, StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException("RemoteConsole frame route does not match the expected action.");
		if (command.Args is null || !command.Args.TryGetValue(RequestArgument, out var request))
			throw new InvalidDataException("RemoteConsole frame request argument is missing.");
		return request.Deserialize<T>(JsonOptions)
			?? throw new InvalidDataException("RemoteConsole frame request payload was null.");
	}
}

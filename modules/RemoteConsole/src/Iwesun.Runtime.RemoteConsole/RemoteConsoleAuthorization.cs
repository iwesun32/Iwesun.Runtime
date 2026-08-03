using Iwesun.Runtime.RemoteConsole.Protocol;

namespace Iwesun.Runtime.RemoteConsole;

public sealed record RemoteConsoleAuthorizationResult(bool Ok, string Code, string Message)
{
	public static RemoteConsoleAuthorizationResult Allow() => new(true, "OK", "");

	public static RemoteConsoleAuthorizationResult Deny(string code, string message) => new(false, code, message);
}

public sealed class RemoteConsoleAuthorization
{
	private static readonly IReadOnlySet<string> SubmitterActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		RemoteConsoleActions.ServerInfo,
		RemoteConsoleActions.JobSubmit,
		RemoteConsoleActions.JobStatus,
		RemoteConsoleActions.JobFollow,
		RemoteConsoleActions.JobCancel,
		RemoteConsoleActions.WorkspaceCreate,
		RemoteConsoleActions.WorkspaceList,
		RemoteConsoleActions.WorkspaceShow,
		RemoteConsoleActions.WorkspaceRemove,
		RemoteConsoleActions.FileList,
		RemoteConsoleActions.UploadBegin,
		RemoteConsoleActions.UploadChunk,
		RemoteConsoleActions.UploadCommit
	};

	private static readonly IReadOnlySet<string> ApproverActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		RemoteConsoleActions.ServerInfo,
		RemoteConsoleActions.JobStatus,
		RemoteConsoleActions.JobFollow,
		RemoteConsoleActions.JobPending,
		RemoteConsoleActions.JobApprove,
		RemoteConsoleActions.JobReject,
		RemoteConsoleActions.PolicyGet,
		RemoteConsoleActions.PolicySet
	};

	private readonly HashSet<string> _submitterSids;
	private readonly HashSet<string> _approverSids;

	public RemoteConsoleAuthorization(
		IEnumerable<string> submitterSids,
		IEnumerable<string> approverSids)
	{
		ArgumentNullException.ThrowIfNull(submitterSids);
		ArgumentNullException.ThrowIfNull(approverSids);
		_submitterSids = Normalize(submitterSids);
		_approverSids = Normalize(approverSids);
	}

	public RemoteConsoleAuthorizationResult Authorize(
		string identitySid,
		string action,
		string? resourceOwnerSid = null)
	{
		return Authorize(identitySid, [identitySid], action, resourceOwnerSid);
	}

	internal RemoteConsoleAuthorizationResult Authorize(
		string userSid,
		IEnumerable<string> principalSids,
		string action,
		string? resourceOwnerSid = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(userSid);
		ArgumentNullException.ThrowIfNull(principalSids);
		if (!RemoteConsoleProtocol.IsKnownAction(action))
			return RemoteConsoleAuthorizationResult.Deny(RemoteConsoleErrorCodes.InvalidRequest, "Unknown RemoteConsole action.");

		var identities = principalSids.ToHashSet(StringComparer.OrdinalIgnoreCase);
		var isSubmitter = identities.Overlaps(_submitterSids);
		var isApprover = identities.Overlaps(_approverSids);
		if (!isSubmitter && !isApprover)
			return RemoteConsoleAuthorizationResult.Deny(RemoteConsoleErrorCodes.AccessDenied, "The Windows identity is not authorized.");

		if ((string.Equals(action, RemoteConsoleActions.JobApprove, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(action, RemoteConsoleActions.JobReject, StringComparison.OrdinalIgnoreCase))
			&& string.Equals(userSid, resourceOwnerSid, StringComparison.OrdinalIgnoreCase))
		{
			return RemoteConsoleAuthorizationResult.Deny(
				RemoteConsoleErrorCodes.SelfApprovalDenied,
				"The command submitter cannot approve or reject the same command.");
		}

		if (isApprover && ApproverActions.Contains(action))
			return RemoteConsoleAuthorizationResult.Allow();

		if (isSubmitter && SubmitterActions.Contains(action))
		{
			if (RequiresOwnedResource(action)
				&& !string.IsNullOrWhiteSpace(resourceOwnerSid)
				&& !string.Equals(userSid, resourceOwnerSid, StringComparison.OrdinalIgnoreCase))
			{
				return RemoteConsoleAuthorizationResult.Deny(
					RemoteConsoleErrorCodes.AccessDenied,
					"Submitters may access only their own jobs.");
			}
			return RemoteConsoleAuthorizationResult.Allow();
		}

		return RemoteConsoleAuthorizationResult.Deny(
			RemoteConsoleErrorCodes.AccessDenied,
			"The Windows identity is not authorized for this action.");
	}

	private static bool RequiresOwnedResource(string action) =>
		string.Equals(action, RemoteConsoleActions.JobStatus, StringComparison.OrdinalIgnoreCase)
		|| string.Equals(action, RemoteConsoleActions.JobFollow, StringComparison.OrdinalIgnoreCase)
		|| string.Equals(action, RemoteConsoleActions.JobCancel, StringComparison.OrdinalIgnoreCase);

	private static HashSet<string> Normalize(IEnumerable<string> values) =>
		values
			.Where(static value => !string.IsNullOrWhiteSpace(value))
			.Select(static value => value.Trim())
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
}

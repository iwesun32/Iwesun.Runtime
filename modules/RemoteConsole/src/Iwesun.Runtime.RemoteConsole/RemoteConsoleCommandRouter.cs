using System.Text.Json;
using Iwesun.Runtime.Diagnostics;
using Iwesun.Runtime.RemoteConsole.Protocol;

namespace Iwesun.Runtime.RemoteConsole;

internal sealed class RemoteConsoleCommandRouter
{
	private readonly RemoteConsoleOptions _options;
	private readonly RemoteConsoleAuthorization _authorization;
	private readonly RemoteConsoleApprovalPolicy _policy;
	private readonly RemoteConsoleJobStore _jobs;
	private readonly RemoteConsoleWorkspaceStore _workspaces;
	private readonly RemoteConsoleCommandExecutor _executor;

	public RemoteConsoleCommandRouter(
		RemoteConsoleOptions options,
		RemoteConsoleAuthorization authorization,
		RemoteConsoleApprovalPolicy policy,
		RemoteConsoleJobStore jobs,
		RemoteConsoleWorkspaceStore workspaces,
		RemoteConsoleCommandExecutor executor)
	{
		_options = options ?? throw new ArgumentNullException(nameof(options));
		_authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
		_policy = policy ?? throw new ArgumentNullException(nameof(policy));
		_jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
		_workspaces = workspaces ?? throw new ArgumentNullException(nameof(workspaces));
		_executor = executor ?? throw new ArgumentNullException(nameof(executor));
	}

	public async Task<RuntimeDiagnosticFrame> DispatchAsync(
		RemoteConsoleClientIdentity identity,
		RuntimeDiagnosticFrame request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(identity);
		ArgumentNullException.ThrowIfNull(request);
		var action = request.Command?.Action ?? "";
		if (!RemoteConsoleProtocol.IsKnownAction(action))
			return Response(request, false, RemoteConsoleErrorCodes.InvalidRequest, "Unknown RemoteConsole action.", null);

		try
		{
			return action.ToLowerInvariant() switch
			{
				RemoteConsoleActions.ServerInfo => ServerInfo(identity, request),
				RemoteConsoleActions.JobSubmit => Submit(identity, request),
				RemoteConsoleActions.JobStatus => Status(identity, request),
				RemoteConsoleActions.JobFollow => await FollowAsync(identity, request, cancellationToken).ConfigureAwait(false),
				RemoteConsoleActions.JobPending => Pending(identity, request),
				RemoteConsoleActions.JobApprove => Approve(identity, request),
				RemoteConsoleActions.JobReject => Reject(identity, request),
				RemoteConsoleActions.JobCancel => Cancel(identity, request),
				RemoteConsoleActions.PolicyGet => GetPolicy(identity, request),
				RemoteConsoleActions.PolicySet => SetPolicy(identity, request),
				RemoteConsoleActions.WorkspaceCreate => CreateWorkspace(identity, request),
				RemoteConsoleActions.WorkspaceList => ListWorkspaces(identity, request),
				RemoteConsoleActions.WorkspaceShow => ShowWorkspace(identity, request),
				RemoteConsoleActions.WorkspaceRemove => RemoveWorkspace(identity, request),
				RemoteConsoleActions.FileList => ListFiles(identity, request),
				RemoteConsoleActions.UploadBegin => BeginUpload(identity, request),
				RemoteConsoleActions.UploadChunk => await AppendChunkAsync(identity, request, cancellationToken).ConfigureAwait(false),
				RemoteConsoleActions.UploadCommit => await CommitUploadAsync(identity, request, cancellationToken).ConfigureAwait(false),
				_ => Response(request, false, RemoteConsoleErrorCodes.InvalidRequest, $"Action '{action}' is not implemented yet.", null)
			};
		}
		catch (Exception ex) when (ex is InvalidDataException or JsonException or ArgumentException)
		{
			return Response(request, false, RemoteConsoleErrorCodes.InvalidRequest, ex.Message, null);
		}
	}

	private RuntimeDiagnosticFrame ServerInfo(RemoteConsoleClientIdentity identity, RuntimeDiagnosticFrame request)
	{
		var access = Authorize(identity, RemoteConsoleActions.ServerInfo);
		return access.Ok
			? Response(request, true, "OK", "", new { pipeName = _options.PipeName, approvalMode = _policy.Mode.ToString() })
			: Denied(request, access);
	}

	private RuntimeDiagnosticFrame Submit(RemoteConsoleClientIdentity identity, RuntimeDiagnosticFrame frame)
	{
		var access = Authorize(identity, RemoteConsoleActions.JobSubmit);
		if (!access.Ok)
			return Denied(frame, access);
		var request = RemoteConsoleProtocol.ParseRequest<RemoteConsoleSubmitRequest>(frame, RemoteConsoleActions.JobSubmit);
		var result = _jobs.Submit(request, identity.UserSid, _policy);
		StartIfReady(result);
		return Mutation(frame, result);
	}

	private RuntimeDiagnosticFrame Status(RemoteConsoleClientIdentity identity, RuntimeDiagnosticFrame frame)
	{
		var request = RemoteConsoleProtocol.ParseRequest<RemoteConsoleJobRequest>(frame, RemoteConsoleActions.JobStatus);
		var job = _jobs.Get(request.JobId);
		if (!job.Ok)
			return Mutation(frame, job);
		var access = Authorize(identity, RemoteConsoleActions.JobStatus, job.Job?.SubmitterSid);
		return access.Ok ? Mutation(frame, job) : Denied(frame, access);
	}

	private RuntimeDiagnosticFrame Pending(RemoteConsoleClientIdentity identity, RuntimeDiagnosticFrame frame)
	{
		var access = Authorize(identity, RemoteConsoleActions.JobPending);
		return access.Ok
			? Response(frame, true, "OK", "", _jobs.GetPending())
			: Denied(frame, access);
	}

	private async Task<RuntimeDiagnosticFrame> FollowAsync(
		RemoteConsoleClientIdentity identity,
		RuntimeDiagnosticFrame frame,
		CancellationToken cancellationToken)
	{
		var request = RemoteConsoleProtocol.ParseRequest<RemoteConsoleFollowRequest>(frame, RemoteConsoleActions.JobFollow);
		var validation = RemoteConsoleProtocol.Validate(request);
		if (!validation.Ok)
			return Response(frame, false, validation.Code, validation.Message, null);
		var job = _jobs.Get(request.JobId);
		if (!job.Ok)
			return Mutation(frame, job);
		var access = Authorize(identity, RemoteConsoleActions.JobFollow, job.Job?.SubmitterSid);
		if (!access.Ok)
			return Denied(frame, access);
		var result = await _executor.FollowAsync(
			request.JobId,
			request.AfterSequence,
			request.Limit,
			request.WaitMs,
			cancellationToken).ConfigureAwait(false);
		return Response(frame, true, result.Truncated ? RemoteConsoleErrorCodes.OutputTruncated : "OK", "", result);
	}

	private RuntimeDiagnosticFrame Approve(RemoteConsoleClientIdentity identity, RuntimeDiagnosticFrame frame)
	{
		var request = RemoteConsoleProtocol.ParseRequest<RemoteConsoleApproveRequest>(frame, RemoteConsoleActions.JobApprove);
		return Decide(identity, frame, request.JobId, RemoteConsoleActions.JobApprove,
			() => _jobs.Approve(request.JobId, identity.UserSid), startWhenReady: true);
	}

	private RuntimeDiagnosticFrame Reject(RemoteConsoleClientIdentity identity, RuntimeDiagnosticFrame frame)
	{
		var request = RemoteConsoleProtocol.ParseRequest<RemoteConsoleRejectRequest>(frame, RemoteConsoleActions.JobReject);
		return Decide(identity, frame, request.JobId, RemoteConsoleActions.JobReject,
			() => _jobs.Reject(request.JobId, identity.UserSid, request.Reason));
	}

	private RuntimeDiagnosticFrame Cancel(RemoteConsoleClientIdentity identity, RuntimeDiagnosticFrame frame)
	{
		var request = RemoteConsoleProtocol.ParseRequest<RemoteConsoleCancelRequest>(frame, RemoteConsoleActions.JobCancel);
		var job = _jobs.Get(request.JobId);
		if (!job.Ok)
			return Mutation(frame, job);
		var access = Authorize(identity, RemoteConsoleActions.JobCancel, job.Job?.SubmitterSid);
		return access.Ok ? Mutation(frame, _jobs.Cancel(request.JobId, identity.UserSid)) : Denied(frame, access);
	}

	private RuntimeDiagnosticFrame GetPolicy(RemoteConsoleClientIdentity identity, RuntimeDiagnosticFrame frame)
	{
		var access = Authorize(identity, RemoteConsoleActions.PolicyGet);
		return access.Ok
			? Response(frame, true, "OK", "", new RemoteConsolePolicySnapshot(_policy.Mode, _policy.AutoApprovePatterns, _policy.DenyPatterns))
			: Denied(frame, access);
	}

	private RuntimeDiagnosticFrame SetPolicy(RemoteConsoleClientIdentity identity, RuntimeDiagnosticFrame frame)
	{
		var access = Authorize(identity, RemoteConsoleActions.PolicySet);
		if (!access.Ok)
			return Denied(frame, access);
		var request = RemoteConsoleProtocol.ParseRequest<RemoteConsolePolicySetRequest>(frame, RemoteConsoleActions.PolicySet);
		_policy.SetMode(request.Mode);
		return GetPolicy(identity, frame);
	}

	private RuntimeDiagnosticFrame CreateWorkspace(RemoteConsoleClientIdentity identity, RuntimeDiagnosticFrame frame)
	{
		var access = Authorize(identity, RemoteConsoleActions.WorkspaceCreate);
		if (!access.Ok)
			return Denied(frame, access);
		var request = RemoteConsoleProtocol.ParseRequest<RemoteConsoleWorkspaceCreateRequest>(frame, RemoteConsoleActions.WorkspaceCreate);
		return Operation(frame, _workspaces.Create(request.Description));
	}

	private RuntimeDiagnosticFrame ListWorkspaces(RemoteConsoleClientIdentity identity, RuntimeDiagnosticFrame frame)
	{
		var access = Authorize(identity, RemoteConsoleActions.WorkspaceList);
		return access.Ok ? Response(frame, true, "OK", "", _workspaces.List()) : Denied(frame, access);
	}

	private RuntimeDiagnosticFrame ShowWorkspace(RemoteConsoleClientIdentity identity, RuntimeDiagnosticFrame frame)
	{
		var access = Authorize(identity, RemoteConsoleActions.WorkspaceShow);
		if (!access.Ok)
			return Denied(frame, access);
		var request = RemoteConsoleProtocol.ParseRequest<RemoteConsoleWorkspaceRequest>(frame, RemoteConsoleActions.WorkspaceShow);
		return Operation(frame, _workspaces.Show(request.WorkspaceId));
	}

	private RuntimeDiagnosticFrame RemoveWorkspace(RemoteConsoleClientIdentity identity, RuntimeDiagnosticFrame frame)
	{
		var access = Authorize(identity, RemoteConsoleActions.WorkspaceRemove);
		if (!access.Ok)
			return Denied(frame, access);
		var request = RemoteConsoleProtocol.ParseRequest<RemoteConsoleWorkspaceRequest>(frame, RemoteConsoleActions.WorkspaceRemove);
		return Operation(frame, _workspaces.Remove(request.WorkspaceId));
	}

	private RuntimeDiagnosticFrame ListFiles(RemoteConsoleClientIdentity identity, RuntimeDiagnosticFrame frame)
	{
		var access = Authorize(identity, RemoteConsoleActions.FileList);
		if (!access.Ok)
			return Denied(frame, access);
		var request = RemoteConsoleProtocol.ParseRequest<RemoteConsoleFileListRequest>(frame, RemoteConsoleActions.FileList);
		return Operation(frame, _workspaces.ListFiles(request.WorkspaceId, request.RelativePath));
	}

	private RuntimeDiagnosticFrame BeginUpload(RemoteConsoleClientIdentity identity, RuntimeDiagnosticFrame frame)
	{
		var access = Authorize(identity, RemoteConsoleActions.UploadBegin);
		if (!access.Ok)
			return Denied(frame, access);
		var request = RemoteConsoleProtocol.ParseRequest<RemoteConsoleUploadBeginRequest>(frame, RemoteConsoleActions.UploadBegin);
		return Operation(frame, _workspaces.BeginUpload(request));
	}

	private async Task<RuntimeDiagnosticFrame> AppendChunkAsync(
		RemoteConsoleClientIdentity identity,
		RuntimeDiagnosticFrame frame,
		CancellationToken cancellationToken)
	{
		var access = Authorize(identity, RemoteConsoleActions.UploadChunk);
		if (!access.Ok)
			return Denied(frame, access);
		var request = RemoteConsoleProtocol.ParseRequest<RemoteConsoleUploadChunkRequest>(frame, RemoteConsoleActions.UploadChunk);
		return Operation(frame, await _workspaces.AppendChunkAsync(request, cancellationToken).ConfigureAwait(false));
	}

	private async Task<RuntimeDiagnosticFrame> CommitUploadAsync(
		RemoteConsoleClientIdentity identity,
		RuntimeDiagnosticFrame frame,
		CancellationToken cancellationToken)
	{
		var access = Authorize(identity, RemoteConsoleActions.UploadCommit);
		if (!access.Ok)
			return Denied(frame, access);
		var request = RemoteConsoleProtocol.ParseRequest<RemoteConsoleUploadCommitRequest>(frame, RemoteConsoleActions.UploadCommit);
		return Operation(frame, await _workspaces.CommitUploadAsync(request, cancellationToken).ConfigureAwait(false));
	}

	private RuntimeDiagnosticFrame Decide(
		RemoteConsoleClientIdentity identity,
		RuntimeDiagnosticFrame frame,
		string jobId,
		string action,
		Func<RemoteConsoleJobMutationResult> decision,
		bool startWhenReady = false)
	{
		var job = _jobs.Get(jobId);
		if (!job.Ok)
			return Mutation(frame, job);
		var access = Authorize(identity, action, job.Job?.SubmitterSid);
		if (!access.Ok)
			return Denied(frame, access);
		var result = decision();
		if (startWhenReady)
			StartIfReady(result);
		return Mutation(frame, result);
	}

	private void StartIfReady(RemoteConsoleJobMutationResult result)
	{
		if (!result.Ok || !result.Changed || result.Job?.State != RemoteConsoleJobState.Starting)
			return;
		if (!_workspaces.TryResolveWorkspace(result.Job.WorkspaceId, out var workspacePath))
		{
			_jobs.FailStart(result.Job.JobId, "The command workspace was not found.");
			return;
		}
		_ = _executor.ExecuteAsync(result.Job.JobId, workspacePath, CancellationToken.None);
	}

	private RemoteConsoleAuthorizationResult Authorize(
		RemoteConsoleClientIdentity identity,
		string action,
		string? ownerSid = null) =>
		_authorization.Authorize(identity.UserSid, identity.PrincipalSids, action, ownerSid);

	private static RuntimeDiagnosticFrame Mutation(RuntimeDiagnosticFrame request, RemoteConsoleJobMutationResult result) =>
		Response(request, result.Ok, result.Code, result.Message, result.Job);

	private static RuntimeDiagnosticFrame Operation<T>(RuntimeDiagnosticFrame request, RemoteConsoleOperationResult<T> result) =>
		Response(request, result.Ok, result.Code, result.Message, result.Data);

	private static RuntimeDiagnosticFrame Denied(RuntimeDiagnosticFrame request, RemoteConsoleAuthorizationResult result) =>
		Response(request, false, result.Code, result.Message, null);

	private static RuntimeDiagnosticFrame Response(
		RuntimeDiagnosticFrame request,
		bool ok,
		string code,
		string message,
		object? data) =>
		new()
		{
			Header = new RuntimeDiagnosticFrameHeader
			{
				Schema = RuntimeDiagnosticProtocol.V2Schema,
				FrameType = "response",
				Category = request.Header.Category,
				Operation = request.Header.Operation,
				RequestId = request.Header.RequestId,
				CorrelationId = request.Header.RequestId ?? request.Header.CorrelationId,
				Source = RemoteConsoleProtocol.ServerTarget,
				Destination = request.Header.Source
			},
			Status = new RuntimeDiagnosticFrameStatus { Ok = ok, Code = code, Message = message },
			Data = data is null ? null : JsonSerializer.SerializeToElement(data)
		};
}

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Iwesun.Runtime.RemoteConsole.Protocol;

namespace Iwesun.Runtime.RemoteConsole;

public sealed record RemoteConsoleJobMutationResult(
	bool Ok,
	string Code,
	string Message,
	RemoteConsoleJobSnapshot? Job)
{
	public static RemoteConsoleJobMutationResult Success(RemoteConsoleJobSnapshot job) =>
		new(true, "OK", "", job);

	public static RemoteConsoleJobMutationResult Fail(string code, string message, RemoteConsoleJobSnapshot? job = null) =>
		new(false, code, message, job);
}

internal sealed class RemoteConsoleJob
{
	public required string JobId { get; init; }
	public required RemoteConsoleSubmitRequest Request { get; init; }
	public required string SubmitterSid { get; init; }
	public required string ContentHash { get; init; }
	public required DateTimeOffset SubmittedAt { get; init; }
	public object SyncRoot { get; } = new();
	public RemoteConsoleJobState State { get; set; }
	public DateTimeOffset? StartedAt { get; set; }
	public DateTimeOffset? CompletedAt { get; set; }
	public int? ExitCode { get; set; }
	public string? DecisionBySid { get; set; }
	public string? DecisionReason { get; set; }
}

internal sealed record RemoteConsoleExecutionDescriptor(
	string JobId,
	RemoteConsoleSubmitRequest Request,
	RemoteConsoleJobState State);

public sealed class RemoteConsoleJobStore
{
	private readonly ConcurrentDictionary<string, RemoteConsoleJob> _jobs = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, string> _requestIds = new(StringComparer.OrdinalIgnoreCase);
	private readonly object _submitSync = new();

	public RemoteConsoleJobMutationResult Submit(
		RemoteConsoleSubmitRequest request,
		string submitterSid,
		RemoteConsoleApprovalPolicy policy)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentException.ThrowIfNullOrWhiteSpace(submitterSid);
		ArgumentNullException.ThrowIfNull(policy);
		var validation = RemoteConsoleProtocol.Validate(request);
		if (!validation.Ok)
			return RemoteConsoleJobMutationResult.Fail(validation.Code, validation.Message);

		var immutableRequest = CopyRequest(request);
		var contentHash = ComputeContentHash(immutableRequest);
		lock (_submitSync)
		{
			if (_requestIds.TryGetValue(immutableRequest.RequestId, out var existingJobId)
				&& _jobs.TryGetValue(existingJobId, out var existing))
			{
				lock (existing.SyncRoot)
				{
					return string.Equals(existing.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase)
						&& string.Equals(existing.SubmitterSid, submitterSid, StringComparison.OrdinalIgnoreCase)
						? RemoteConsoleJobMutationResult.Success(Snapshot(existing))
						: Conflict(existing, "requestId is already bound to different immutable content.");
				}
			}

			var decision = policy.Evaluate(immutableRequest);
			var job = new RemoteConsoleJob
			{
				JobId = Guid.NewGuid().ToString("N"),
				Request = immutableRequest,
				SubmitterSid = submitterSid,
				ContentHash = contentHash,
				SubmittedAt = DateTimeOffset.UtcNow,
				State = decision switch
				{
					RemoteConsoleApprovalDecision.AwaitApproval => RemoteConsoleJobState.AwaitingApproval,
					RemoteConsoleApprovalDecision.Start => RemoteConsoleJobState.Starting,
					RemoteConsoleApprovalDecision.Reject => RemoteConsoleJobState.Rejected,
					_ => throw new InvalidOperationException("Unknown approval decision.")
				},
				CompletedAt = decision == RemoteConsoleApprovalDecision.Reject ? DateTimeOffset.UtcNow : null,
				DecisionReason = decision == RemoteConsoleApprovalDecision.Reject
					? "Rejected by the configured automatic deny policy."
					: null
			};
			_jobs[job.JobId] = job;
			_requestIds[immutableRequest.RequestId] = job.JobId;
			return RemoteConsoleJobMutationResult.Success(Snapshot(job));
		}
	}

	public RemoteConsoleJobMutationResult Get(string jobId) =>
		WithJob(jobId, static job => RemoteConsoleJobMutationResult.Success(Snapshot(job)));

	public IReadOnlyList<RemoteConsoleJobSnapshot> GetPending() =>
		_jobs.Values
			.Select(job => WithJob(job.JobId, static value => RemoteConsoleJobMutationResult.Success(Snapshot(value))).Job)
			.Where(static job => job?.State == RemoteConsoleJobState.AwaitingApproval)
			.Cast<RemoteConsoleJobSnapshot>()
			.OrderBy(static job => job.SubmittedAt)
			.ToArray();

	public RemoteConsoleJobMutationResult Approve(string jobId, string approverSid) =>
		WithJob(jobId, job =>
		{
			if (string.Equals(job.SubmitterSid, approverSid, StringComparison.OrdinalIgnoreCase))
				return RemoteConsoleJobMutationResult.Fail(RemoteConsoleErrorCodes.SelfApprovalDenied, "A submitter cannot approve the same job.", Snapshot(job));
			if (job.State != RemoteConsoleJobState.AwaitingApproval)
				return Conflict(job, "Only an awaiting-approval job may be approved.");
			if (!string.Equals(job.ContentHash, ComputeContentHash(job.Request), StringComparison.OrdinalIgnoreCase))
				return Conflict(job, "The immutable job content hash no longer matches.");
			job.State = RemoteConsoleJobState.Starting;
			job.DecisionBySid = approverSid;
			return RemoteConsoleJobMutationResult.Success(Snapshot(job));
		});

	public RemoteConsoleJobMutationResult Reject(string jobId, string approverSid, string reason) =>
		WithJob(jobId, job =>
		{
			if (string.Equals(job.SubmitterSid, approverSid, StringComparison.OrdinalIgnoreCase))
				return RemoteConsoleJobMutationResult.Fail(RemoteConsoleErrorCodes.SelfApprovalDenied, "A submitter cannot reject the same job.", Snapshot(job));
			if (job.State != RemoteConsoleJobState.AwaitingApproval)
				return Conflict(job, "Only an awaiting-approval job may be rejected.");
			job.State = RemoteConsoleJobState.Rejected;
			job.CompletedAt = DateTimeOffset.UtcNow;
			job.DecisionBySid = approverSid;
			job.DecisionReason = reason;
			return RemoteConsoleJobMutationResult.Success(Snapshot(job));
		});

	public RemoteConsoleJobMutationResult Cancel(string jobId, string submitterSid) =>
		WithJob(jobId, job =>
		{
			if (!string.Equals(job.SubmitterSid, submitterSid, StringComparison.OrdinalIgnoreCase))
				return RemoteConsoleJobMutationResult.Fail(RemoteConsoleErrorCodes.AccessDenied, "Only the submitter may cancel this job.", Snapshot(job));
			if (job.State != RemoteConsoleJobState.AwaitingApproval)
				return Conflict(job, "Only an awaiting-approval job may be cancelled.");
			job.State = RemoteConsoleJobState.Cancelled;
			job.CompletedAt = DateTimeOffset.UtcNow;
			return RemoteConsoleJobMutationResult.Success(Snapshot(job));
		});

	public RemoteConsoleJobMutationResult MarkRunning(string jobId) =>
		Transition(jobId, RemoteConsoleJobState.Starting, RemoteConsoleJobState.Running, job => job.StartedAt = DateTimeOffset.UtcNow);

	public RemoteConsoleJobMutationResult Complete(string jobId, int exitCode) =>
		WithJob(jobId, job =>
		{
			if (job.State != RemoteConsoleJobState.Running)
				return Conflict(job, "Only a running job may complete.");
			job.State = exitCode == 0 ? RemoteConsoleJobState.Completed : RemoteConsoleJobState.Failed;
			job.ExitCode = exitCode;
			job.CompletedAt = DateTimeOffset.UtcNow;
			return RemoteConsoleJobMutationResult.Success(Snapshot(job));
		});

	public RemoteConsoleJobMutationResult Interrupt(string jobId, string reason) =>
		WithJob(jobId, job =>
		{
			if (job.State is not (RemoteConsoleJobState.Starting or RemoteConsoleJobState.Running))
				return Conflict(job, "Only a starting or running job may be interrupted.");
			job.State = RemoteConsoleJobState.Interrupted;
			job.CompletedAt = DateTimeOffset.UtcNow;
			job.DecisionReason = reason;
			return RemoteConsoleJobMutationResult.Success(Snapshot(job));
		});

	internal RemoteConsoleExecutionDescriptor? GetExecution(string jobId)
	{
		if (!_jobs.TryGetValue(jobId, out var job))
			return null;
		lock (job.SyncRoot)
			return new RemoteConsoleExecutionDescriptor(job.JobId, job.Request, job.State);
	}

	internal RemoteConsoleJobMutationResult FailStart(string jobId, string message) =>
		WithJob(jobId, job =>
		{
			if (job.State != RemoteConsoleJobState.Starting)
				return Conflict(job, "Only a starting job may report a launch failure.");
			job.State = RemoteConsoleJobState.Failed;
			job.CompletedAt = DateTimeOffset.UtcNow;
			job.DecisionReason = message;
			return RemoteConsoleJobMutationResult.Success(Snapshot(job));
		});

	private RemoteConsoleJobMutationResult Transition(
		string jobId,
		RemoteConsoleJobState expected,
		RemoteConsoleJobState next,
		Action<RemoteConsoleJob> update) =>
		WithJob(jobId, job =>
		{
			if (job.State != expected)
				return Conflict(job, $"Expected state {expected}; current state is {job.State}.");
			job.State = next;
			update(job);
			return RemoteConsoleJobMutationResult.Success(Snapshot(job));
		});

	private RemoteConsoleJobMutationResult WithJob(
		string jobId,
		Func<RemoteConsoleJob, RemoteConsoleJobMutationResult> action)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
		if (!_jobs.TryGetValue(jobId, out var job))
			return RemoteConsoleJobMutationResult.Fail(RemoteConsoleErrorCodes.JobNotFound, "RemoteConsole job was not found.");
		lock (job.SyncRoot)
			return action(job);
	}

	private static RemoteConsoleJobMutationResult Conflict(RemoteConsoleJob job, string message) =>
		RemoteConsoleJobMutationResult.Fail(RemoteConsoleErrorCodes.JobStateConflict, message, Snapshot(job));

	private static RemoteConsoleSubmitRequest CopyRequest(RemoteConsoleSubmitRequest request) =>
		request with
		{
			RequestId = request.RequestId.Trim(),
			Shell = request.Shell.Trim(),
			WorkspaceId = request.WorkspaceId.Trim(),
			WorkingDirectory = request.WorkingDirectory.Trim(),
			Environment = request.Environment
				.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
				.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal)
		};

	private static string ComputeContentHash(RemoteConsoleSubmitRequest request)
	{
		var canonical = JsonSerializer.Serialize(new
		{
			request.Shell,
			request.Command,
			request.WorkspaceId,
			request.WorkingDirectory,
			environment = request.Environment.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
		});
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
	}

	private static RemoteConsoleJobSnapshot Snapshot(RemoteConsoleJob job) =>
		new(
			job.JobId,
			job.Request.RequestId,
			job.SubmitterSid,
			job.State,
			job.Request.Shell,
			job.Request.Command,
			job.Request.WorkspaceId,
			job.ContentHash,
			job.SubmittedAt,
			job.StartedAt,
			job.CompletedAt,
			job.ExitCode,
			job.DecisionBySid,
			job.DecisionReason);
}

using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Iwesun.Runtime.Diagnostics;
using Iwesun.Runtime.RemoteConsole;
using Iwesun.Runtime.RemoteConsole.Protocol;

internal static class RemoteConsoleScenario
{
	public static Task<FunctionalScenarioResult> RunProtocolAsync()
	{
		var checks = new List<string>();
		var failures = new List<string>();
		var invalidSubmit = new RemoteConsoleSubmitRequest(
			"",
			"",
			"",
			"",
			new Dictionary<string, string>(),
			"");

		if (!RemoteConsoleProtocol.Validate(invalidSubmit).Ok)
			checks.Add("empty-submit-rejected");
		else
			failures.Add("Empty submit request was accepted.");

		if (!RemoteConsoleProtocol.Validate(new RemoteConsoleFollowRequest("job", -1, 10, 100)).Ok)
			checks.Add("negative-follow-sequence-rejected");
		else
			failures.Add("Negative follow sequence was accepted.");

		var oversizedChunk = new RemoteConsoleUploadChunkRequest(
			"upload",
			0,
			Convert.ToBase64String(new byte[RemoteConsoleProtocol.MaxUploadChunkBytes + 1]));
		if (!RemoteConsoleProtocol.Validate(oversizedChunk).Ok)
			checks.Add("oversized-upload-chunk-rejected");
		else
			failures.Add("Oversized upload chunk was accepted.");

		if (!RemoteConsoleProtocol.IsKnownAction("unknown.action"))
			checks.Add("unknown-action-rejected");
		else
			failures.Add("Unknown action was accepted.");

		if (!RemoteConsoleProtocol.IsKnownState((RemoteConsoleJobState)int.MaxValue))
			checks.Add("unknown-state-rejected");
		else
			failures.Add("Unknown job state was accepted.");

		var frame = RemoteConsoleProtocol.CreateRequest(
			RemoteConsoleActions.JobSubmit,
			"protocol-frame",
			new RemoteConsoleSubmitRequest(
				"protocol-frame",
				"powershell",
				"Get-Date",
				"workspace",
				new Dictionary<string, string>(),
				"read-only"));
		if (frame.Command?.Domain == RemoteConsoleProtocol.Domain
			&& frame.Command.Target == RemoteConsoleProtocol.ServerTarget
			&& frame.Command.Action == RemoteConsoleActions.JobSubmit)
			checks.Add("frame-route-isolated");
		else
			failures.Add("Remote console frame route mismatch.");

		return Task.FromResult(failures.Count == 0
			? FunctionalScenarioResult.Pass("remote-console-protocol", checks.ToArray())
			: FunctionalScenarioResult.Fail("remote-console-protocol", checks, failures));
	}

	public static Task<FunctionalScenarioResult> RunAuthorizationAsync()
	{
		const string submitterSid = "S-1-5-21-100-200-300-1001";
		const string approverSid = "S-1-5-21-100-200-300-1002";
		const string unknownSid = "S-1-5-21-100-200-300-1003";
		var checks = new List<string>();
		var failures = new List<string>();
		var authorization = new RemoteConsoleAuthorization([submitterSid], [approverSid]);

		Check(authorization.Authorize(submitterSid, RemoteConsoleActions.JobSubmit), true, "submitter-can-submit");
		Check(authorization.Authorize(submitterSid, RemoteConsoleActions.JobStatus, submitterSid), true, "submitter-can-read-own-job");
		Check(authorization.Authorize(submitterSid, RemoteConsoleActions.JobApprove, submitterSid), false, "submitter-cannot-approve");
		Check(authorization.Authorize(approverSid, RemoteConsoleActions.JobApprove, submitterSid), true, "approver-can-approve");
		Check(authorization.Authorize(approverSid, RemoteConsoleActions.PolicySet), true, "approver-can-set-policy");
		Check(authorization.Authorize(unknownSid, RemoteConsoleActions.JobSubmit), false, "unknown-identity-denied");

		var selfApproval = new RemoteConsoleAuthorization([submitterSid], [submitterSid])
			.Authorize(submitterSid, RemoteConsoleActions.JobApprove, submitterSid);
		if (!selfApproval.Ok && selfApproval.Code == RemoteConsoleErrorCodes.SelfApprovalDenied)
			checks.Add("self-approval-denied");
		else
			failures.Add("Self approval did not return RC_SELF_APPROVAL_DENIED.");

		return Task.FromResult(failures.Count == 0
			? FunctionalScenarioResult.Pass("remote-console-authorization", checks.ToArray())
			: FunctionalScenarioResult.Fail("remote-console-authorization", checks, failures));

		void Check(RemoteConsoleAuthorizationResult result, bool expected, string name)
		{
			if (result.Ok == expected)
				checks.Add(name);
			else
				failures.Add($"{name}: expected Ok={expected}, actual Ok={result.Ok}, code={result.Code}.");
		}
	}

	public static async Task<FunctionalScenarioResult> RunApprovalAsync()
	{
		const string submitterSid = "S-1-5-21-100-200-300-2001";
		const string approverSid = "S-1-5-21-100-200-300-2002";
		var checks = new List<string>();
		var failures = new List<string>();
		var environment = new Dictionary<string, string> { ["MODE"] = "test" };
		var request = new RemoteConsoleSubmitRequest(
			"approval-manual",
			"powershell",
			"Get-Date",
			"workspace",
			environment,
			"read-only");
		var manualPolicy = new RemoteConsoleApprovalPolicy(RemoteConsoleApprovalMode.Manual, [], []);
		var store = new RemoteConsoleJobStore();

		var first = store.Submit(request, submitterSid, manualPolicy);
		var duplicate = store.Submit(request, submitterSid, manualPolicy);
		Expect(first.Ok && first.Job?.State == RemoteConsoleJobState.AwaitingApproval, "manual-awaits-approval");
		Expect(duplicate.Ok && duplicate.Job?.JobId == first.Job?.JobId, "duplicate-request-idempotent");
		Expect(!duplicate.Changed, "duplicate-request-does-not-relaunch");

		var originalHash = first.Job?.ContentHash;
		environment["MODE"] = "mutated";
		Expect(store.Get(first.Job!.JobId).Job?.ContentHash == originalHash, "content-hash-immutable");

		var selfApproval = store.Approve(first.Job.JobId, submitterSid);
		Expect(!selfApproval.Ok && selfApproval.Code == RemoteConsoleErrorCodes.SelfApprovalDenied, "self-approval-denied-by-store");

		var raceRequest = request with { RequestId = "approval-race" };
		var race = store.Submit(raceRequest, submitterSid, manualPolicy);
		var decisions = await Task.WhenAll(
			Task.Run(() => store.Approve(race.Job!.JobId, approverSid)),
			Task.Run(() => store.Reject(race.Job!.JobId, approverSid, "not approved")));
		Expect(decisions.Count(static result => result.Ok) == 1, "approval-race-single-winner");

		var cancel = store.Submit(request with { RequestId = "approval-cancel" }, submitterSid, manualPolicy);
		Expect(store.Cancel(cancel.Job!.JobId, submitterSid).Job?.State == RemoteConsoleJobState.Cancelled, "pending-cancelled");

		var guardedPolicy = new RemoteConsoleApprovalPolicy(
			RemoteConsoleApprovalMode.Guarded,
			["^powershell\\nGet-Date\\n$"],
			[]);
		var guarded = store.Submit(request with { RequestId = "approval-guarded" }, submitterSid, guardedPolicy);
		Expect(guarded.Job?.State == RemoteConsoleJobState.Starting, "guarded-rule-auto-approved");
		var running = store.MarkRunning(guarded.Job!.JobId);
		Expect(running.Job?.State == RemoteConsoleJobState.Running, "starting-transitions-running");
		Expect(!store.Cancel(guarded.Job.JobId, submitterSid).Ok, "running-cancel-rejected");

		var automaticPolicy = new RemoteConsoleApprovalPolicy(
			RemoteConsoleApprovalMode.Automatic,
			[],
			["^.*Remove-Item.*$"]);
		var denied = store.Submit(
			request with { RequestId = "approval-denied", Command = "Remove-Item important.txt" },
			submitterSid,
			automaticPolicy);
		Expect(denied.Job?.State == RemoteConsoleJobState.Rejected, "automatic-deny-rule-rejected");
		var automatic = store.Submit(request with { RequestId = "approval-automatic" }, submitterSid, automaticPolicy);
		Expect(automatic.Job?.State == RemoteConsoleJobState.Starting, "automatic-command-starting");

		return failures.Count == 0
			? FunctionalScenarioResult.Pass("remote-console-approval", checks.ToArray())
			: FunctionalScenarioResult.Fail("remote-console-approval", checks, failures);

		void Expect(bool condition, string name)
		{
			if (condition)
				checks.Add(name);
			else
				failures.Add(name);
		}
	}

	public static async Task<FunctionalScenarioResult> RunCommandAsync()
	{
		const string submitterSid = "S-1-5-21-100-200-300-3001";
		var checks = new List<string>();
		var failures = new List<string>();
		var workspace = Path.Combine(Path.GetTempPath(), "iwesun-remote-console-command", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(workspace);
		try
		{
			var options = new RemoteConsoleOptions
			{
				PowerShellPath = "pwsh.exe",
				MaxOutputBytesPerJob = 1024 * 1024
			};
			var store = new RemoteConsoleJobStore();
			var policy = new RemoteConsoleApprovalPolicy(RemoteConsoleApprovalMode.Automatic, [], []);
			var submit = store.Submit(
				new RemoteConsoleSubmitRequest(
					"command-exit-7",
					"powershell",
					"Write-Output 'stdout-line'; [Console]::Error.WriteLine('stderr-line'); exit 7",
					"workspace",
					new Dictionary<string, string>(),
					"test"),
				submitterSid,
				policy);
			var executor = new RemoteConsoleCommandExecutor(options, store);
			await executor.ExecuteAsync(submit.Job!.JobId, workspace, CancellationToken.None);

			var status = store.Get(submit.Job.JobId).Job!;
			Expect(status.State == RemoteConsoleJobState.Failed && status.ExitCode == 7, "nonzero-exit-recorded");
			var follow = await executor.FollowAsync(submit.Job.JobId, 0, 100, 0, CancellationToken.None);
			Expect(follow.Chunks.Any(static chunk => chunk.Stream == RemoteConsoleOutputStream.Stdout && chunk.Text.Contains("stdout-line", StringComparison.Ordinal)), "stdout-captured");
			Expect(follow.Chunks.Any(static chunk => chunk.Stream == RemoteConsoleOutputStream.Stderr && chunk.Text.Contains("stderr-line", StringComparison.Ordinal)), "stderr-captured");
			Expect(follow.Chunks.Select(static chunk => chunk.Sequence).SequenceEqual(follow.Chunks.Select(static chunk => chunk.Sequence).Order()), "output-sequence-ordered");

			var smallBuffer = new RemoteConsoleOutputBuffer(12);
			smallBuffer.Append(RemoteConsoleOutputStream.Stdout, "first-line");
			smallBuffer.Append(RemoteConsoleOutputStream.Stdout, "second-line");
			var truncated = await smallBuffer.ReadAsync(0, 10, 0, CancellationToken.None);
			Expect(truncated.Truncated && truncated.EarliestAvailableSequence > 1, "bounded-output-truncated");
		}
		finally
		{
			Directory.Delete(workspace, recursive: true);
		}

		return failures.Count == 0
			? FunctionalScenarioResult.Pass("remote-console-command", checks.ToArray())
			: FunctionalScenarioResult.Fail("remote-console-command", checks, failures);

		void Expect(bool condition, string name)
		{
			if (condition)
				checks.Add(name);
			else
				failures.Add(name);
		}
	}

	public static async Task<FunctionalScenarioResult> RunWorkspaceAsync()
	{
		var checks = new List<string>();
		var failures = new List<string>();
		var root = Path.Combine(Path.GetTempPath(), "iwesun-remote-console-workspace", Guid.NewGuid().ToString("N"));
		var options = new RemoteConsoleOptions
		{
			WorkspaceRoot = root,
			MaxFileBytes = 2048,
			MaxWorkspaceBytes = 2048,
			MaxTotalWorkspaceBytes = 4096
		};
		var store = new RemoteConsoleWorkspaceStore(options, new RemoteConsoleJobStore());
		try
		{
			var workspace = store.Create("functional test");
			Expect(workspace.Ok && workspace.Data is not null, "workspace-created");
			var workspaceId = workspace.Data!.WorkspaceId;
			Expect(!store.BeginUpload(new RemoteConsoleUploadBeginRequest(workspaceId, "..\\escape.txt", 1, new string('0', 64))).Ok, "parent-path-rejected");
			Expect(!store.BeginUpload(new RemoteConsoleUploadBeginRequest(workspaceId, "C:\\escape.txt", 1, new string('0', 64))).Ok, "drive-path-rejected");
			Expect(!store.BeginUpload(new RemoteConsoleUploadBeginRequest(workspaceId, "\\\\server\\share\\escape.txt", 1, new string('0', 64))).Ok, "unc-path-rejected");
			Expect(!store.BeginUpload(new RemoteConsoleUploadBeginRequest(workspaceId, "file.txt:stream", 1, new string('0', 64))).Ok, "alternate-stream-rejected");
			Expect(!store.BeginUpload(new RemoteConsoleUploadBeginRequest(workspaceId, "large.bin", 4096, new string('0', 64))).Ok, "file-quota-rejected");

			var content = Encoding.UTF8.GetBytes("remote-console-upload");
			var sha256 = Convert.ToHexString(SHA256.HashData(content));
			var begin = store.BeginUpload(new RemoteConsoleUploadBeginRequest(workspaceId, "nested\\payload.txt", content.Length, sha256));
			Expect(begin.Ok && begin.Data is not null, "upload-begun");
			var finalPath = Path.Combine(root, workspaceId, "nested", "payload.txt");
			Expect(!File.Exists(finalPath), "final-file-hidden-before-commit");
			var chunk = await store.AppendChunkAsync(
				new RemoteConsoleUploadChunkRequest(begin.Data!.UploadId, 0, Convert.ToBase64String(content)),
				CancellationToken.None);
			Expect(chunk.Ok, "upload-chunk-accepted");
			var outOfOrder = await store.AppendChunkAsync(
				new RemoteConsoleUploadChunkRequest(begin.Data.UploadId, 2, Convert.ToBase64String([0])),
				CancellationToken.None);
			Expect(!outOfOrder.Ok && outOfOrder.Code == RemoteConsoleErrorCodes.UploadSequenceConflict, "upload-sequence-enforced");
			var commit = await store.CommitUploadAsync(new RemoteConsoleUploadCommitRequest(begin.Data.UploadId), CancellationToken.None);
			Expect(commit.Ok && File.Exists(finalPath) && await File.ReadAllTextAsync(finalPath) == "remote-console-upload", "upload-committed-atomically");
			Expect(!store.BeginUpload(new RemoteConsoleUploadBeginRequest(workspaceId, "workspace-full.bin", 2040, new string('0', 64))).Ok, "workspace-quota-rejected");

			var mismatch = store.BeginUpload(new RemoteConsoleUploadBeginRequest(workspaceId, "bad.txt", content.Length, new string('0', 64)));
			await store.AppendChunkAsync(new RemoteConsoleUploadChunkRequest(mismatch.Data!.UploadId, 0, Convert.ToBase64String(content)), CancellationToken.None);
			var mismatchCommit = await store.CommitUploadAsync(new RemoteConsoleUploadCommitRequest(mismatch.Data.UploadId), CancellationToken.None);
			Expect(!mismatchCommit.Ok && mismatchCommit.Code == RemoteConsoleErrorCodes.UploadHashMismatch, "upload-hash-mismatch-rejected");
		}
		finally
		{
			store.Dispose();
			if (Directory.Exists(root))
				Directory.Delete(root, recursive: true);
		}

		return failures.Count == 0
			? FunctionalScenarioResult.Pass("remote-console-workspace", checks.ToArray())
			: FunctionalScenarioResult.Fail("remote-console-workspace", checks, failures);

		void Expect(bool condition, string name)
		{
			if (condition)
				checks.Add(name);
			else
				failures.Add(name);
		}
	}

	public static async Task<FunctionalScenarioResult> RunCliAsync()
	{
		var checks = new List<string>();
		var failures = new List<string>();
		var repositoryRoot = FindRepositoryRoot();
		var cliProject = Path.Combine(repositoryRoot, "Iwesun.Runtime.Cli", "Iwesun.Runtime.Cli.csproj");
		var info = await RunCliAsync(["--server=.", "--pipe=remote-console-not-running", "console.info"]);
		Expect(!info.Error.Contains("CLI_COMMAND_UNKNOWN", StringComparison.Ordinal), "console-info-grammar-recognized");
		var submit = await RunCliAsync([
			"--server=.",
			"--pipe=remote-console-not-running",
			"console.submit",
			"--workspace",
			"workspace",
			"--shell",
			"powershell",
			"--",
			"Write-Output 'ok'"]);
		Expect(!submit.Error.Contains("CLI_OPTION_UNKNOWN", StringComparison.Ordinal)
			&& !submit.Error.Contains("CLI_COMMAND_UNKNOWN", StringComparison.Ordinal), "console-submit-flags-recognized");

		var pipeName = $"Iwesun.Runtime.RemoteConsole.Functional.{Guid.NewGuid():N}";
		var serviceProject = Path.Combine(repositoryRoot, "Iwesun.Runtime.RemoteConsole", "Iwesun.Runtime.RemoteConsole.csproj");
		var serviceStart = new ProcessStartInfo("dotnet")
		{
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true,
			WorkingDirectory = repositoryRoot
		};
		serviceStart.ArgumentList.Add("run");
		serviceStart.ArgumentList.Add("--project");
		serviceStart.ArgumentList.Add(serviceProject);
		serviceStart.ArgumentList.Add("-c");
		serviceStart.ArgumentList.Add("Debug");
		serviceStart.ArgumentList.Add("--");
		serviceStart.ArgumentList.Add("--console");
		serviceStart.ArgumentList.Add("--test-current-user");
		serviceStart.ArgumentList.Add($"--pipe={pipeName}");
		using var service = Process.Start(serviceStart) ?? throw new InvalidOperationException("Failed to start RemoteConsole service test host.");
		var serviceOutput = service.StandardOutput.ReadToEndAsync();
		var serviceError = service.StandardError.ReadToEndAsync();
		var tempFile = Path.Combine(Path.GetTempPath(), $"remote-console-cli-{Guid.NewGuid():N}.txt");
		try
		{
			(int ExitCode, string Output, string Error) ready = default;
			for (var attempt = 0; attempt < 20; attempt++)
			{
				ready = await RunCliAsync(["--server=.", $"--pipe={pipeName}", "console.info"]);
				if (ready.ExitCode == 0)
					break;
				await Task.Delay(250);
			}
			Expect(ready.ExitCode == 0, "remote-console-service-connected");
			if (ready.ExitCode == 0)
			{
				var shell = await RunCliAsync(
					["--interactive"],
					$"console target add service local {pipeName}{Environment.NewLine}" +
					"console target use service" + Environment.NewLine +
					"console target current" + Environment.NewLine +
					"console.info" + Environment.NewLine +
					"exit" + Environment.NewLine);
				Expect(shell.ExitCode == 0
					&& shell.Output.Contains("service=local/", StringComparison.Ordinal)
					&& shell.Output.Contains(pipeName, StringComparison.Ordinal), "cli-shell-remote-target-context");
				var afterShellExit = await RunCliAsync(["--server=.", $"--pipe={pipeName}", "console.info"]);
				Expect(afterShellExit.ExitCode == 0, "cli-shell-exit-does-not-stop-service");
				var create = await RunCliAsync(["--server=.", $"--pipe={pipeName}", "workspace.create", "cli-test"]);
				var workspaceId = ReadDataString(create.Output, "workspaceId");
				Expect(create.ExitCode == 0 && !string.IsNullOrWhiteSpace(workspaceId), "cli-workspace-created");
				await File.WriteAllTextAsync(tempFile, "remote-console-cli-payload");
				var upload = await RunCliAsync(["--server=.", $"--pipe={pipeName}", "file.upload", workspaceId!, tempFile, "payload.txt"]);
				Expect(upload.ExitCode == 0, "cli-file-uploaded");
				var submitJob = await RunCliAsync([
					"--server=.",
					$"--pipe={pipeName}",
					"console.submit",
					"--workspace",
					workspaceId!,
					"--shell",
					"powershell",
					"--",
					"Get-Content payload.txt"]);
				var jobId = ReadDataString(submitJob.Output, "jobId");
				Expect(submitJob.ExitCode == 0 && !string.IsNullOrWhiteSpace(jobId), "cli-command-submitted");
				var followJob = await RunCliAsync(["--server=.", $"--pipe={pipeName}", "console.follow", jobId!]);
				Expect(followJob.ExitCode == 0 && followJob.Output.Contains("remote-console-cli-payload", StringComparison.Ordinal), "cli-output-followed");
			}
		}
		finally
		{
			if (!service.HasExited)
				service.Kill(entireProcessTree: true);
			await service.WaitForExitAsync();
			_ = await serviceOutput;
			_ = await serviceError;
			if (File.Exists(tempFile))
				File.Delete(tempFile);
		}

		return failures.Count == 0
			? FunctionalScenarioResult.Pass("remote-console-cli", checks.ToArray())
			: FunctionalScenarioResult.Fail("remote-console-cli", checks, failures);

		async Task<(int ExitCode, string Output, string Error)> RunCliAsync(string[] arguments, string? standardInput = null)
		{
			var startInfo = new ProcessStartInfo("dotnet")
			{
				UseShellExecute = false,
				RedirectStandardInput = standardInput is not null,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true,
				WorkingDirectory = repositoryRoot
			};
			startInfo.ArgumentList.Add("run");
			startInfo.ArgumentList.Add("--project");
			startInfo.ArgumentList.Add(cliProject);
			startInfo.ArgumentList.Add("-c");
			startInfo.ArgumentList.Add("Debug");
			startInfo.ArgumentList.Add("--");
			foreach (var argument in arguments)
				startInfo.ArgumentList.Add(argument);
			using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start CLI grammar test.");
			if (standardInput is not null)
			{
				await process.StandardInput.WriteAsync(standardInput);
				process.StandardInput.Close();
			}
			var output = process.StandardOutput.ReadToEndAsync();
			var error = process.StandardError.ReadToEndAsync();
			await process.WaitForExitAsync();
			return (process.ExitCode, await output, await error);
		}

		void Expect(bool condition, string name)
		{
			if (condition)
				checks.Add(name);
			else
				failures.Add(name);
		}

		static string FindRepositoryRoot()
		{
			var current = new DirectoryInfo(AppContext.BaseDirectory);
			while (current is not null)
			{
				if (File.Exists(Path.Combine(current.FullName, "Iwesun.Runtime.slnx")))
					return current.FullName;
				current = current.Parent;
			}
			throw new DirectoryNotFoundException("Runtime repository root was not found.");
		}

		static string? ReadDataString(string json, string propertyName)
		{
			using var document = JsonDocument.Parse(json);
			if (!document.RootElement.TryGetProperty("data", out var data))
				return null;
			foreach (var property in data.EnumerateObject())
			{
				if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
					return property.Value.GetString();
			}
			return null;
		}
	}

	public static async Task<FunctionalScenarioResult> RunFrameCodecAsync()
	{
		var checks = new List<string>();
		var failures = new List<string>();
		var frame = new RuntimeDiagnosticFrame
		{
			Header = new RuntimeDiagnosticFrameHeader
			{
				Schema = RuntimeDiagnosticProtocol.V2Schema,
				FrameType = "request",
				RequestId = "codec-fragmented"
			}
		};

		await using (var stream = new FragmentingDuplexStream([1, 2, 3, 5]))
		{
			await RuntimeFramePipeCodec.WriteAsync(stream, frame, CancellationToken.None);
			stream.Rewind();
			var decoded = await RuntimeFramePipeCodec.ReadAsync(stream, 1024 * 1024, CancellationToken.None);
			if (decoded.Header.RequestId == "codec-fragmented")
				checks.Add("fragmented-frame-round-trip");
			else
				failures.Add("Fragmented frame requestId mismatch.");
		}

		await using (var oversized = new FragmentingDuplexStream([1]))
		{
			var header = new byte[sizeof(int)];
			BinaryPrimitives.WriteInt32LittleEndian(header, 1025);
			await oversized.WriteAsync(header);
			oversized.Rewind();
			try
			{
				await RuntimeFramePipeCodec.ReadAsync(oversized, 1024, CancellationToken.None);
				failures.Add("Oversized frame was accepted.");
			}
			catch (InvalidDataException)
			{
				checks.Add("oversized-frame-rejected");
			}
		}

		return failures.Count == 0
			? FunctionalScenarioResult.Pass("remote-console-frame-codec", checks.ToArray())
			: FunctionalScenarioResult.Fail("remote-console-frame-codec", checks, failures);
	}

	private sealed class FragmentingDuplexStream(IReadOnlyList<int> fragmentSizes) : Stream
	{
		private readonly MemoryStream _inner = new();
		private int _fragmentIndex;

		public override bool CanRead => true;
		public override bool CanSeek => true;
		public override bool CanWrite => true;
		public override long Length => _inner.Length;
		public override long Position { get => _inner.Position; set => _inner.Position = value; }

		public void Rewind()
		{
			_inner.Position = 0;
			_fragmentIndex = 0;
		}

		public override int Read(byte[] buffer, int offset, int count) =>
			_inner.Read(buffer, offset, Math.Min(count, NextFragmentSize()));

		public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
			_inner.ReadAsync(buffer[..Math.Min(buffer.Length, NextFragmentSize())], cancellationToken);

		public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

		public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
			_inner.WriteAsync(buffer, cancellationToken);

		public override void Flush() => _inner.Flush();
		public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);
		public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
		public override void SetLength(long value) => _inner.SetLength(value);

		protected override void Dispose(bool disposing)
		{
			if (disposing)
				_inner.Dispose();
			base.Dispose(disposing);
		}

		public override async ValueTask DisposeAsync()
		{
			await _inner.DisposeAsync();
			GC.SuppressFinalize(this);
		}

		private int NextFragmentSize()
		{
			if (fragmentSizes.Count == 0)
				return 1;
			var size = fragmentSizes[_fragmentIndex % fragmentSizes.Count];
			_fragmentIndex++;
			return Math.Max(1, size);
		}
	}
}

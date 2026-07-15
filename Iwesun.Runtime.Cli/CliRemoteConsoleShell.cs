using System.Security.Cryptography;
using System.Text.Json;
using Iwesun.Runtime.RemoteConsole.Protocol;

namespace Iwesun.Runtime.Cli;

internal static class CliRemoteConsoleShell
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
	private static readonly HashSet<string> Commands = new(StringComparer.OrdinalIgnoreCase)
	{
		"console.info",
		"console.submit",
		"console.status",
		"console.follow",
		"console.cancel",
		"console.pending",
		"console.approve",
		"console.reject",
		"console.policy.get",
		"console.policy.set",
		"workspace.create",
		"workspace.list",
		"workspace.show",
		"workspace.remove",
		"console.file.list",
		"file.upload"
	};

	public static bool IsCommand(string? command) =>
		!string.IsNullOrWhiteSpace(command) && Commands.Contains(command);

	public static async Task<int> ExecuteAsync(
		CliConfiguration configuration,
		CliOptions options,
		CancellationToken cancellationToken)
	{
		var arguments = options.CommandArguments;
		var name = arguments[0].ToLowerInvariant();
		var endpoint = configuration.Endpoints.GetValueOrDefault("remote-console")
			?? new CliEndpoint
			{
				Transport = "namedPipe",
				PipeName = RemoteConsoleProtocol.DefaultPipeName,
				ConnectTimeoutMs = 5000,
				RequestTimeoutMs = 60000,
				MaxResponseBytes = 16 * 1024 * 1024
			};
		var target = CliRuntimeTargetResolver.Resolve(
			configuration,
			endpoint,
			"remote-console",
			options.TargetName,
			options.ServerName,
			options.PipeName,
			options.TimeoutMs);
		var client = new CliRemoteConsoleClient(target);

		return name switch
		{
			"console.info" => await SendAndRenderAsync(client, RemoteConsoleActions.ServerInfo, new { }, cancellationToken),
			"console.submit" => await SubmitAsync(client, arguments, cancellationToken),
			"console.status" => await JobAsync(client, arguments, RemoteConsoleActions.JobStatus, cancellationToken),
			"console.follow" => await FollowAsync(client, arguments, cancellationToken),
			"console.cancel" => await CancelAsync(client, arguments, cancellationToken),
			"console.pending" => await SendAndRenderAsync(client, RemoteConsoleActions.JobPending, new { }, cancellationToken),
			"console.approve" => await ApproveAsync(client, arguments, cancellationToken),
			"console.reject" => await RejectAsync(client, arguments, cancellationToken),
			"console.policy.get" => await SendAndRenderAsync(client, RemoteConsoleActions.PolicyGet, new { }, cancellationToken),
			"console.policy.set" => await SetPolicyAsync(client, arguments, cancellationToken),
			"workspace.create" => await CreateWorkspaceAsync(client, arguments, cancellationToken),
			"workspace.list" => await SendAndRenderAsync(client, RemoteConsoleActions.WorkspaceList, new { }, cancellationToken),
			"workspace.show" => await WorkspaceAsync(client, arguments, RemoteConsoleActions.WorkspaceShow, cancellationToken),
			"workspace.remove" => await WorkspaceAsync(client, arguments, RemoteConsoleActions.WorkspaceRemove, cancellationToken),
			"console.file.list" => await ListFilesAsync(client, arguments, cancellationToken),
			"file.upload" => await UploadAsync(client, arguments, cancellationToken),
			_ => throw new CliException("CLI_COMMAND_UNKNOWN", $"Unknown RemoteConsole command '{name}'.", 2, name)
		};
	}

	private static async Task<int> SubmitAsync(
		CliRemoteConsoleClient client,
		IReadOnlyList<string> arguments,
		CancellationToken cancellationToken)
	{
		var separator = IndexOf(arguments, "--");
		if (separator < 0 || separator == arguments.Count - 1)
			throw Usage("console.submit --workspace <id> --shell <powershell|cmd> [--working-directory <relative>] -- <command>");
		var workspace = ReadOption(arguments, "--workspace", separator);
		var shell = ReadOption(arguments, "--shell", separator);
		var workingDirectory = ReadOption(arguments, "--working-directory", separator, required: false) ?? "";
		var command = string.Join(' ', arguments.Skip(separator + 1));
		var requestId = Guid.NewGuid().ToString("N");
		return await SendAndRenderAsync(
			client,
			RemoteConsoleActions.JobSubmit,
			new RemoteConsoleSubmitRequest(requestId, shell!, command, workspace!, new Dictionary<string, string>(), "", workingDirectory),
			cancellationToken,
			requestId);
	}

	private static Task<int> JobAsync(CliRemoteConsoleClient client, IReadOnlyList<string> arguments, string action, CancellationToken cancellationToken)
	{
		RequireCount(arguments, 2, $"{arguments[0]} <jobId>");
		return SendAndRenderAsync(client, action, new RemoteConsoleJobRequest(arguments[1]), cancellationToken);
	}

	private static async Task<int> FollowAsync(CliRemoteConsoleClient client, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
	{
		RequireCount(arguments, 2, "console.follow <jobId>");
		long afterSequence = 0;
		while (true)
		{
			var response = await client.SendAsync(
				RemoteConsoleProtocol.CreateRequest(
					RemoteConsoleActions.JobFollow,
					Guid.NewGuid().ToString("N"),
					new RemoteConsoleFollowRequest(arguments[1], afterSequence, 256, 5000)),
				cancellationToken).ConfigureAwait(false);
			if (response.Frame.Status?.Ok != true || response.Frame.Data is null)
			{
				Console.WriteLine(response.Json);
				return 6;
			}
			var follow = response.Frame.Data.Value.Deserialize<RemoteConsoleFollowResult>(JsonOptions)
				?? throw new CliException("CLI_PROTOCOL_RESPONSE_NULL", "RemoteConsole follow data was null.", 5);
			foreach (var chunk in follow.Chunks)
			{
				if (chunk.Stream == RemoteConsoleOutputStream.Stderr)
					Console.Error.WriteLine(chunk.Text);
				else
					Console.Out.WriteLine(chunk.Text);
				afterSequence = Math.Max(afterSequence, chunk.Sequence);
			}
			if (IsTerminal(follow.State))
				return follow.State == RemoteConsoleJobState.Completed ? 0 : NormalizeExitCode(follow.ExitCode);
		}
	}

	private static Task<int> CancelAsync(CliRemoteConsoleClient client, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
	{
		RequireCount(arguments, 2, "console.cancel <jobId>");
		return SendAndRenderAsync(client, RemoteConsoleActions.JobCancel, new RemoteConsoleCancelRequest(arguments[1]), cancellationToken);
	}

	private static Task<int> ApproveAsync(CliRemoteConsoleClient client, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
	{
		RequireCount(arguments, 2, "console.approve <jobId>");
		return SendAndRenderAsync(client, RemoteConsoleActions.JobApprove, new RemoteConsoleApproveRequest(arguments[1]), cancellationToken);
	}

	private static Task<int> RejectAsync(CliRemoteConsoleClient client, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
	{
		if (arguments.Count < 3)
			throw Usage("console.reject <jobId> <reason>");
		return SendAndRenderAsync(client, RemoteConsoleActions.JobReject, new RemoteConsoleRejectRequest(arguments[1], string.Join(' ', arguments.Skip(2))), cancellationToken);
	}

	private static Task<int> SetPolicyAsync(CliRemoteConsoleClient client, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
	{
		RequireCount(arguments, 2, "console.policy.set <Manual|Guarded|Automatic>");
		if (!Enum.TryParse<RemoteConsoleApprovalMode>(arguments[1], true, out var mode) || !Enum.IsDefined(mode))
			throw Usage("console.policy.set <Manual|Guarded|Automatic>");
		return SendAndRenderAsync(client, RemoteConsoleActions.PolicySet, new RemoteConsolePolicySetRequest(mode), cancellationToken);
	}

	private static Task<int> CreateWorkspaceAsync(CliRemoteConsoleClient client, IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
		SendAndRenderAsync(client, RemoteConsoleActions.WorkspaceCreate, new RemoteConsoleWorkspaceCreateRequest(arguments.Count > 1 ? string.Join(' ', arguments.Skip(1)) : null), cancellationToken);

	private static Task<int> WorkspaceAsync(CliRemoteConsoleClient client, IReadOnlyList<string> arguments, string action, CancellationToken cancellationToken)
	{
		RequireCount(arguments, 2, $"{arguments[0]} <workspaceId>");
		return SendAndRenderAsync(client, action, new RemoteConsoleWorkspaceRequest(arguments[1]), cancellationToken);
	}

	private static Task<int> ListFilesAsync(CliRemoteConsoleClient client, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
	{
		if (arguments.Count is < 2 or > 3)
			throw Usage("console.file.list <workspaceId> [relativePath]");
		return SendAndRenderAsync(client, RemoteConsoleActions.FileList, new RemoteConsoleFileListRequest(arguments[1], arguments.Count == 3 ? arguments[2] : ""), cancellationToken);
	}

	private static async Task<int> UploadAsync(CliRemoteConsoleClient client, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
	{
		if (arguments.Count is < 3 or > 4)
			throw Usage("file.upload <workspaceId> <localFile> [remoteRelativePath]");
		var localPath = Path.GetFullPath(arguments[2]);
		if (!File.Exists(localPath))
			throw new CliException("CLI_UPLOAD_FILE_NOT_FOUND", $"Local upload file '{localPath}' was not found.", 2);
		var relativePath = arguments.Count == 4 ? arguments[3] : Path.GetFileName(localPath);
		await using var file = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read, RemoteConsoleProtocol.MaxUploadChunkBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
		var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false));
		file.Position = 0;
		var begin = await client.SendAsync(
			RemoteConsoleProtocol.CreateRequest(
				RemoteConsoleActions.UploadBegin,
				Guid.NewGuid().ToString("N"),
				new RemoteConsoleUploadBeginRequest(arguments[1], relativePath, file.Length, hash)),
			cancellationToken).ConfigureAwait(false);
		if (begin.Frame.Status?.Ok != true || begin.Frame.Data is null)
		{
			Console.WriteLine(begin.Json);
			return 6;
		}
		var session = begin.Frame.Data.Value.Deserialize<RemoteConsoleUploadSessionSnapshot>(JsonOptions)
			?? throw new CliException("CLI_PROTOCOL_RESPONSE_NULL", "RemoteConsole upload session was null.", 5);
		var buffer = new byte[RemoteConsoleProtocol.MaxUploadChunkBytes];
		var sequence = 0;
		while (await file.ReadAsync(buffer, cancellationToken).ConfigureAwait(false) is var read && read > 0)
		{
			var chunk = await client.SendAsync(
				RemoteConsoleProtocol.CreateRequest(
					RemoteConsoleActions.UploadChunk,
					Guid.NewGuid().ToString("N"),
					new RemoteConsoleUploadChunkRequest(session.UploadId, sequence++, Convert.ToBase64String(buffer, 0, read))),
				cancellationToken).ConfigureAwait(false);
			if (chunk.Frame.Status?.Ok != true)
			{
				Console.WriteLine(chunk.Json);
				return 6;
			}
		}
		return await SendAndRenderAsync(client, RemoteConsoleActions.UploadCommit, new RemoteConsoleUploadCommitRequest(session.UploadId), cancellationToken);
	}

	private static async Task<int> SendAndRenderAsync<T>(
		CliRemoteConsoleClient client,
		string action,
		T request,
		CancellationToken cancellationToken,
		string? requestId = null)
	{
		var response = await client.SendAsync(
			RemoteConsoleProtocol.CreateRequest(action, requestId ?? Guid.NewGuid().ToString("N"), request),
			cancellationToken).ConfigureAwait(false);
		Console.WriteLine(response.Json);
		return response.Frame.Status?.Ok == true ? 0 : 6;
	}

	private static string? ReadOption(IReadOnlyList<string> arguments, string name, int beforeIndex, bool required = true)
	{
		for (var index = 1; index < beforeIndex; index++)
		{
			if (arguments[index].Equals(name, StringComparison.OrdinalIgnoreCase) && index + 1 < beforeIndex)
				return arguments[index + 1];
		}
		if (required)
			throw Usage($"console.submit requires {name} <value>.");
		return null;
	}

	private static int IndexOf(IReadOnlyList<string> values, string expected)
	{
		for (var index = 0; index < values.Count; index++)
			if (values[index].Equals(expected, StringComparison.Ordinal))
				return index;
		return -1;
	}

	private static void RequireCount(IReadOnlyCollection<string> arguments, int count, string usage)
	{
		if (arguments.Count != count)
			throw Usage(usage);
	}

	private static CliException Usage(string usage) => new("CLI_REMOTE_CONSOLE_USAGE", $"Usage: {usage}", 2);

	private static bool IsTerminal(RemoteConsoleJobState state) =>
		state is RemoteConsoleJobState.Completed or RemoteConsoleJobState.Failed or RemoteConsoleJobState.Rejected or RemoteConsoleJobState.Cancelled or RemoteConsoleJobState.Interrupted;

	private static int NormalizeExitCode(int? exitCode) => exitCode is > 0 and <= 255 ? exitCode.Value : 6;
}

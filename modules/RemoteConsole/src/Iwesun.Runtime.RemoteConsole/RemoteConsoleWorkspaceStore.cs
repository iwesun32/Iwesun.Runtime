using System.Collections.Concurrent;
using Iwesun.Runtime.RemoteConsole.Protocol;
using Microsoft.Extensions.Hosting;

namespace Iwesun.Runtime.RemoteConsole;

public sealed class RemoteConsoleWorkspaceStore : IDisposable
{
	private readonly RemoteConsoleOptions _options;
	private readonly RemoteConsoleJobStore _jobs;
	private readonly string _root;
	private readonly ConcurrentDictionary<string, RemoteConsoleWorkspaceSnapshot> _workspaces = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, RemoteConsoleUploadSession> _uploads = new(StringComparer.OrdinalIgnoreCase);
	private readonly object _quotaSync = new();

	public RemoteConsoleWorkspaceStore(RemoteConsoleOptions options, RemoteConsoleJobStore jobs)
	{
		_options = options ?? throw new ArgumentNullException(nameof(options));
		_jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
		if (_options.MaxFileBytes <= 0 || _options.MaxWorkspaceBytes <= 0 || _options.MaxTotalWorkspaceBytes <= 0)
			throw new RemoteConsoleConfigurationException("Workspace quotas must be positive.");
		_root = Path.GetFullPath(_options.WorkspaceRoot);
		Directory.CreateDirectory(_root);
		LoadExistingWorkspaces();
	}

	public RemoteConsoleOperationResult<RemoteConsoleWorkspaceSnapshot> Create(string? description)
	{
		var workspaceId = Guid.NewGuid().ToString("N");
		var path = Path.Combine(_root, workspaceId);
		Directory.CreateDirectory(path);
		var now = DateTimeOffset.UtcNow;
		var snapshot = new RemoteConsoleWorkspaceSnapshot(workspaceId, description?.Trim() ?? "", now, now, 0);
		_workspaces[workspaceId] = snapshot;
		return Ok(snapshot);
	}

	public IReadOnlyList<RemoteConsoleWorkspaceSnapshot> List() =>
		_workspaces.Values.OrderBy(static workspace => workspace.CreatedAt).ToArray();

	public RemoteConsoleOperationResult<RemoteConsoleWorkspaceSnapshot> Show(string workspaceId)
	{
		if (!TryGetWorkspace(workspaceId, out var workspace))
			return Fail<RemoteConsoleWorkspaceSnapshot>(RemoteConsoleErrorCodes.WorkspaceNotFound, "Workspace was not found.");
		return Ok(RefreshSnapshot(workspace));
	}

	public RemoteConsoleOperationResult<RemoteConsoleWorkspaceSnapshot> Remove(string workspaceId)
	{
		if (!TryGetWorkspace(workspaceId, out var workspace))
			return Fail<RemoteConsoleWorkspaceSnapshot>(RemoteConsoleErrorCodes.WorkspaceNotFound, "Workspace was not found.");
		if (_uploads.Values.Any(upload => string.Equals(upload.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase))
			|| _jobs.HasActiveJobs(workspaceId))
		{
			return Fail<RemoteConsoleWorkspaceSnapshot>(RemoteConsoleErrorCodes.JobStateConflict, "Workspace has an active upload or command.");
		}

		var path = WorkspacePath(workspaceId);
		Directory.Delete(path, recursive: true);
		_workspaces.TryRemove(workspaceId, out _);
		return Ok(workspace);
	}

	public RemoteConsoleOperationResult<IReadOnlyList<RemoteConsoleFileSnapshot>> ListFiles(
		string workspaceId,
		string relativePath)
	{
		if (!TryGetWorkspace(workspaceId, out _))
			return Fail<IReadOnlyList<RemoteConsoleFileSnapshot>>(RemoteConsoleErrorCodes.WorkspaceNotFound, "Workspace was not found.");
		var pathResult = string.IsNullOrWhiteSpace(relativePath)
			? Ok(WorkspacePath(workspaceId))
			: ResolveRelativePath(workspaceId, relativePath);
		if (!pathResult.Ok || pathResult.Data is null || !Directory.Exists(pathResult.Data))
			return Fail<IReadOnlyList<RemoteConsoleFileSnapshot>>(RemoteConsoleErrorCodes.PathOutsideWorkspace, "File list path is not a contained directory.");
		var workspaceRoot = WorkspacePath(workspaceId);
		var files = Directory.EnumerateFiles(pathResult.Data, "*", SearchOption.TopDirectoryOnly)
			.Where(static path => !path.EndsWith(".upload", StringComparison.OrdinalIgnoreCase))
			.Select(path =>
			{
				var info = new FileInfo(path);
				return new RemoteConsoleFileSnapshot(
					Path.GetRelativePath(workspaceRoot, path),
					info.Length,
					info.LastWriteTimeUtc);
			})
			.OrderBy(static file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
			.ToArray();
		return Ok<IReadOnlyList<RemoteConsoleFileSnapshot>>(files);
	}

	public RemoteConsoleOperationResult<RemoteConsoleUploadSessionSnapshot> BeginUpload(RemoteConsoleUploadBeginRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		if (!TryGetWorkspace(request.WorkspaceId, out _))
			return Fail<RemoteConsoleUploadSessionSnapshot>(RemoteConsoleErrorCodes.WorkspaceNotFound, "Workspace was not found.");
		if (request.Length < 0 || request.Length > _options.MaxFileBytes)
			return Fail<RemoteConsoleUploadSessionSnapshot>(RemoteConsoleErrorCodes.QuotaExceeded, "File length exceeds the configured quota.");
		if (!IsSha256(request.Sha256))
			return Fail<RemoteConsoleUploadSessionSnapshot>(RemoteConsoleErrorCodes.InvalidRequest, "sha256 must contain 64 hexadecimal characters.");

		var pathResult = ResolveRelativePath(request.WorkspaceId, request.RelativePath);
		if (!pathResult.Ok || pathResult.Data is null)
			return Fail<RemoteConsoleUploadSessionSnapshot>(pathResult.Code, pathResult.Message);
		lock (_quotaSync)
		{
			var activeWorkspaceBytes = _uploads.Values
				.Where(upload => string.Equals(upload.WorkspaceId, request.WorkspaceId, StringComparison.OrdinalIgnoreCase))
				.Sum(static upload => upload.ExpectedLength);
			if (DirectoryBytes(WorkspacePath(request.WorkspaceId)) + activeWorkspaceBytes + request.Length > _options.MaxWorkspaceBytes
				|| DirectoryBytes(_root) + _uploads.Values.Sum(static upload => upload.ExpectedLength) + request.Length > _options.MaxTotalWorkspaceBytes)
			{
				return Fail<RemoteConsoleUploadSessionSnapshot>(RemoteConsoleErrorCodes.QuotaExceeded, "Workspace quota would be exceeded.");
			}

			var uploadId = Guid.NewGuid().ToString("N");
			var uploadDirectory = Path.Combine(WorkspacePath(request.WorkspaceId), ".uploads");
			Directory.CreateDirectory(uploadDirectory);
			var tempPath = Path.Combine(uploadDirectory, uploadId + ".upload");
			var session = new RemoteConsoleUploadSession
			{
				UploadId = uploadId,
				WorkspaceId = request.WorkspaceId,
				RelativePath = NormalizeRelativePath(request.RelativePath),
				TempPath = tempPath,
				FinalPath = pathResult.Data,
				ExpectedLength = request.Length,
				ExpectedSha256 = request.Sha256.ToUpperInvariant(),
				Stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 64 * 1024, FileOptions.Asynchronous)
			};
			_uploads[uploadId] = session;
			return Ok(Snapshot(session));
		}
	}

	public async Task<RemoteConsoleOperationResult<RemoteConsoleUploadSessionSnapshot>> AppendChunkAsync(
		RemoteConsoleUploadChunkRequest request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		var validation = RemoteConsoleProtocol.Validate(request);
		if (!validation.Ok)
			return Fail<RemoteConsoleUploadSessionSnapshot>(validation.Code, validation.Message);
		if (!_uploads.TryGetValue(request.UploadId, out var session))
			return Fail<RemoteConsoleUploadSessionSnapshot>(RemoteConsoleErrorCodes.UploadNotFound, "Upload session was not found.");

		await session.Sync.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			if (request.Sequence != session.NextSequence)
				return Fail<RemoteConsoleUploadSessionSnapshot>(RemoteConsoleErrorCodes.UploadSequenceConflict, "Upload chunk sequence is not the next expected value.");
			var bytes = Convert.FromBase64String(request.Base64Data);
			if (session.ReceivedLength + bytes.Length > session.ExpectedLength)
				return Fail<RemoteConsoleUploadSessionSnapshot>(RemoteConsoleErrorCodes.QuotaExceeded, "Upload exceeds its declared length.");
			await session.Stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
			session.ReceivedLength += bytes.Length;
			session.NextSequence++;
			return Ok(Snapshot(session));
		}
		finally
		{
			session.Sync.Release();
		}
	}

	public async Task<RemoteConsoleOperationResult<RemoteConsoleWorkspaceSnapshot>> CommitUploadAsync(
		RemoteConsoleUploadCommitRequest request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		if (!_uploads.TryGetValue(request.UploadId, out var session))
			return Fail<RemoteConsoleWorkspaceSnapshot>(RemoteConsoleErrorCodes.UploadNotFound, "Upload session was not found.");

		await session.Sync.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			if (session.ReceivedLength != session.ExpectedLength)
				return Fail<RemoteConsoleWorkspaceSnapshot>(RemoteConsoleErrorCodes.UploadSequenceConflict, "Upload length is incomplete.");
			await session.Stream.FlushAsync(cancellationToken).ConfigureAwait(false);
			var actualHash = await session.ComputeSha256Async(cancellationToken).ConfigureAwait(false);
			if (!string.Equals(actualHash, session.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
			{
				RemoveFailedSession(session);
				return Fail<RemoteConsoleWorkspaceSnapshot>(RemoteConsoleErrorCodes.UploadHashMismatch, "Uploaded file SHA-256 does not match.");
			}

			session.Stream.Dispose();
			Directory.CreateDirectory(Path.GetDirectoryName(session.FinalPath)!);
			File.Move(session.TempPath, session.FinalPath, overwrite: false);
			_uploads.TryRemove(session.UploadId, out _);
			var workspace = RefreshSnapshot(_workspaces[session.WorkspaceId]);
			return Ok(workspace);
		}
		finally
		{
			if (_uploads.ContainsKey(session.UploadId))
				session.Sync.Release();
			else
				session.Sync.Dispose();
		}
	}

	internal bool TryResolveWorkspace(string workspaceId, out string path)
	{
		if (TryGetWorkspace(workspaceId, out _))
		{
			path = WorkspacePath(workspaceId);
			return true;
		}
		path = "";
		return false;
	}

	internal void CleanupExpired(DateTimeOffset now)
	{
		foreach (var workspace in _workspaces.Values)
		{
			if (now - workspace.LastAccessedAt >= _options.WorkspaceRetention)
				Remove(workspace.WorkspaceId);
		}
	}

	public void Dispose()
	{
		foreach (var upload in _uploads.Values)
			upload.Dispose();
		_uploads.Clear();
	}

	private RemoteConsoleOperationResult<string> ResolveRelativePath(string workspaceId, string relativePath)
	{
		if (string.IsNullOrWhiteSpace(relativePath)
			|| Path.IsPathRooted(relativePath)
			|| relativePath.Contains(':')
			|| relativePath.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries).Any(static part => part == ".."))
		{
			return Fail<string>(RemoteConsoleErrorCodes.PathOutsideWorkspace, "Only a contained relative workspace path is allowed.");
		}

		var workspaceRoot = WorkspacePath(workspaceId);
		var candidate = Path.GetFullPath(Path.Combine(workspaceRoot, NormalizeRelativePath(relativePath)));
		if (!candidate.StartsWith(workspaceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
			return Fail<string>(RemoteConsoleErrorCodes.PathOutsideWorkspace, "Path resolves outside the workspace.");
		if (ContainsReparsePoint(workspaceRoot, candidate))
			return Fail<string>(RemoteConsoleErrorCodes.PathOutsideWorkspace, "Path contains a reparse point.");
		return Ok(candidate);
	}

	private static bool ContainsReparsePoint(string root, string candidate)
	{
		var current = root;
		var relative = Path.GetRelativePath(root, candidate);
		foreach (var part in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
		{
			current = Path.Combine(current, part);
			if (!File.Exists(current) && !Directory.Exists(current))
				continue;
			if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
				return true;
		}
		return false;
	}

	private void RemoveFailedSession(RemoteConsoleUploadSession session)
	{
		session.Stream.Dispose();
		_uploads.TryRemove(session.UploadId, out _);
		if (File.Exists(session.TempPath))
			File.Delete(session.TempPath);
	}

	private void LoadExistingWorkspaces()
	{
		foreach (var directory in Directory.EnumerateDirectories(_root))
		{
			var id = Path.GetFileName(directory);
			var created = new DateTimeOffset(Directory.GetCreationTimeUtc(directory), TimeSpan.Zero);
			_workspaces[id] = new RemoteConsoleWorkspaceSnapshot(id, "", created, created, DirectoryBytes(directory));
		}
	}

	private bool TryGetWorkspace(string workspaceId, out RemoteConsoleWorkspaceSnapshot workspace)
	{
		if (!string.IsNullOrWhiteSpace(workspaceId) && _workspaces.TryGetValue(workspaceId, out workspace!))
			return true;
		workspace = null!;
		return false;
	}

	private RemoteConsoleWorkspaceSnapshot RefreshSnapshot(RemoteConsoleWorkspaceSnapshot workspace)
	{
		var refreshed = workspace with { LastAccessedAt = DateTimeOffset.UtcNow, Bytes = DirectoryBytes(WorkspacePath(workspace.WorkspaceId)) };
		_workspaces[workspace.WorkspaceId] = refreshed;
		return refreshed;
	}

	private string WorkspacePath(string workspaceId) => Path.Combine(_root, workspaceId);

	private static string NormalizeRelativePath(string path) =>
		path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);

	private static long DirectoryBytes(string path) =>
		Directory.Exists(path)
			? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(static file => new FileInfo(file).Length)
			: 0;

	private static bool IsSha256(string value) =>
		value.Length == 64 && value.All(Uri.IsHexDigit);

	private static RemoteConsoleUploadSessionSnapshot Snapshot(RemoteConsoleUploadSession session) =>
		new(session.UploadId, session.WorkspaceId, session.RelativePath, session.ExpectedLength, session.ReceivedLength, session.NextSequence);

	private static RemoteConsoleOperationResult<T> Ok<T>(T data) => new(true, "OK", "", data);

	private static RemoteConsoleOperationResult<T> Fail<T>(string code, string message) => new(false, code, message, default);
}

internal sealed class RemoteConsoleWorkspaceCleanupService(RemoteConsoleWorkspaceStore store) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		while (!stoppingToken.IsCancellationRequested)
		{
			await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken).ConfigureAwait(false);
			store.CleanupExpired(DateTimeOffset.UtcNow);
		}
	}
}

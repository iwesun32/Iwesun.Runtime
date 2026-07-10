using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Linq;
using System.Text.RegularExpressions;

namespace Iwesun.Runtime.Diagnostics;

public sealed record RuntimePipeLeaseSnapshot(
	int InternalId,
	string BranchId,
	string RequestedName,
	string PipeName,
	string AggregatePipeName,
	int OwnerProcessId,
	bool Active,
	DateTimeOffset CreatedAt,
	DateTimeOffset UpdatedAt);

public static class RuntimePipeRegistry
{
	private const string DefaultPipePrefix = "Iwesun.Runtime.Branch";
	private static readonly ConcurrentDictionary<string, RuntimePipeLeaseState> Leases = new(StringComparer.OrdinalIgnoreCase);
	private static readonly ConcurrentDictionary<int, RuntimePipeLeaseState> LeasesById = new();
	private static readonly Regex BranchSanitizer = new("[^a-zA-Z0-9_.-]+", RegexOptions.Compiled);
	private static int _nextInternalId;

	public static string AcquirePipe(
		string requestedName,
		string? aggregatePipeName = null,
		string? pipePrefix = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(requestedName);
		var normalizedRequest = NormalizeName(requestedName);
		var effectiveAggregate = string.IsNullOrWhiteSpace(aggregatePipeName)
			? DiagnosticSwitchboardCompiledConfig.DefaultRuntimeDiagnosticsPipeName
			: aggregatePipeName.Trim();
		var effectivePrefix = string.IsNullOrWhiteSpace(pipePrefix)
			? DefaultPipePrefix
			: pipePrefix.Trim();
		var now = DateTimeOffset.UtcNow;
		var currentPid = Environment.ProcessId;
		var branchId = BuildUniqueBranchId(normalizedRequest);
		var internalId = Interlocked.Increment(ref _nextInternalId);
		var pipeName = BuildPipeName(branchId, effectivePrefix);
		EnsurePipeCreatable(pipeName);
		var lease = RuntimePipeLeaseState.Create(
			internalId,
			branchId,
			normalizedRequest,
			pipeName,
			effectiveAggregate,
			currentPid,
			now);
		Leases[branchId] = lease;
		LeasesById[internalId] = lease;
		return lease.PipeName;
	}

	public static RuntimePipeLeaseSnapshot AnnouncePipe(
		string requestedName,
		string pipeName,
		string? aggregatePipeName = null,
		int? ownerProcessId = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(requestedName);
		ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
		var normalizedRequest = NormalizeName(requestedName);
		var normalizedPipeName = pipeName.Trim();
		var effectiveAggregate = string.IsNullOrWhiteSpace(aggregatePipeName)
			? DiagnosticSwitchboardCompiledConfig.DefaultRuntimeDiagnosticsPipeName
			: aggregatePipeName.Trim();
		var now = DateTimeOffset.UtcNow;
		var effectiveOwnerProcessId = ownerProcessId ?? Environment.ProcessId;

		var existing = Leases.Values.FirstOrDefault(x =>
			x.RequestedName.Equals(normalizedRequest, StringComparison.OrdinalIgnoreCase)
			&& x.PipeName.Equals(normalizedPipeName, StringComparison.OrdinalIgnoreCase));
		if (existing is not null)
		{
			existing.AggregatePipeName = effectiveAggregate;
			existing.OwnerProcessId = effectiveOwnerProcessId;
			existing.Active = true;
			existing.UpdatedAt = now;
			return existing.ToSnapshot();
		}

		var branchId = BuildUniqueBranchId(normalizedRequest);
		var internalId = Interlocked.Increment(ref _nextInternalId);
		var lease = RuntimePipeLeaseState.Create(
			internalId,
			branchId,
			normalizedRequest,
			normalizedPipeName,
			effectiveAggregate,
			effectiveOwnerProcessId,
			now);
		Leases[branchId] = lease;
		LeasesById[internalId] = lease;
		return lease.ToSnapshot();
	}

	public static bool ReleasePipe(string branchOrId)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(branchOrId);
		if (!TryResolve(branchOrId, out var lease))
		{
			return false;
		}

		lease.Active = false;
		lease.UpdatedAt = DateTimeOffset.UtcNow;
		return true;
	}

	public static string? ResolveAggregatePipe(string branchOrId)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(branchOrId);
		if (!TryResolve(branchOrId, out var lease) || !lease.Active)
		{
			return null;
		}

		return lease.AggregatePipeName;
	}

	public static RuntimePipeLeaseSnapshot? GetLease(string branchOrId)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(branchOrId);
		return TryResolve(branchOrId, out var lease)
			? lease.ToSnapshot()
			: null;
	}

	public static IReadOnlyList<RuntimePipeLeaseSnapshot> Snapshot(bool includeInactive = true)
	{
		return Leases.Values
			.Where(x => includeInactive || x.Active)
			.OrderBy(x => x.BranchId, StringComparer.OrdinalIgnoreCase)
			.Select(x => x.ToSnapshot())
			.ToArray();
	}

	public static int PurgeInactive()
	{
		var removed = 0;
		foreach (var (key, lease) in Leases)
		{
			if (!lease.Active && Leases.TryRemove(key, out _))
			{
				LeasesById.TryRemove(lease.InternalId, out _);
				removed++;
			}
		}

		return removed;
	}

	private static string NormalizeName(string value)
	{
		var normalized = BranchSanitizer.Replace(value.Trim(), "_");
		if (string.IsNullOrWhiteSpace(normalized))
		{
			throw new ArgumentException("Name cannot be empty after normalization.", nameof(value));
		}

		return normalized;
	}

	private static string BuildUniqueBranchId(string normalizedRequest)
	{
		var suffix = 0;
		while (true)
		{
			var branchId = suffix == 0
				? normalizedRequest
				: $"{normalizedRequest}-{suffix:00}";
			if (!Leases.ContainsKey(branchId))
			{
				return branchId;
			}

			suffix++;
		}
	}

	private static string BuildPipeName(string branchId, string prefix)
	{
		return $"{prefix}.{branchId}";
	}

	private static void EnsurePipeCreatable(string pipeName)
	{
		using var _ = new NamedPipeServerStream(
			pipeName,
			PipeDirection.InOut,
			1,
			PipeTransmissionMode.Byte,
			PipeOptions.Asynchronous);
	}

	private static bool TryResolve(string branchOrId, out RuntimePipeLeaseState lease)
	{
		lease = null!;
		if (int.TryParse(branchOrId.Trim(), out var internalId))
		{
			return LeasesById.TryGetValue(internalId, out lease!);
		}

		return Leases.TryGetValue(NormalizeName(branchOrId), out lease!);
	}

	private sealed class RuntimePipeLeaseState
	{
		private RuntimePipeLeaseState(
			int internalId,
			string branchId,
			string requestedName,
			string pipeName,
			string aggregatePipeName,
			int ownerProcessId,
			DateTimeOffset createdAt,
			DateTimeOffset updatedAt,
			bool active)
		{
			InternalId = internalId;
			BranchId = branchId;
			RequestedName = requestedName;
			PipeName = pipeName;
			AggregatePipeName = aggregatePipeName;
			OwnerProcessId = ownerProcessId;
			CreatedAt = createdAt;
			UpdatedAt = updatedAt;
			Active = active;
		}

		public int InternalId { get; }
		public string BranchId { get; }
		public string RequestedName { get; }
		public string PipeName { get; }
		public string AggregatePipeName { get; set; }
		public int OwnerProcessId { get; set; }
		public bool Active { get; set; }
		public DateTimeOffset CreatedAt { get; }
		public DateTimeOffset UpdatedAt { get; set; }

		public RuntimePipeLeaseSnapshot ToSnapshot()
		{
			return new RuntimePipeLeaseSnapshot(
				InternalId,
				BranchId,
				RequestedName,
				PipeName,
				AggregatePipeName,
				OwnerProcessId,
				Active,
				CreatedAt,
				UpdatedAt);
		}

		public static RuntimePipeLeaseState Create(
			int internalId,
			string branchId,
			string requestedName,
			string pipeName,
			string aggregatePipeName,
			int ownerProcessId,
			DateTimeOffset now)
		{
			return new RuntimePipeLeaseState(
				internalId,
				branchId,
				requestedName,
				pipeName,
				aggregatePipeName,
				ownerProcessId,
				now,
				now,
				active: true);
		}
	}
}

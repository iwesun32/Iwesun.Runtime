using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Linq;
using System.Text.RegularExpressions;

namespace Iwesun.Runtime.Diagnostics;

public sealed record RuntimePipeLeaseSnapshot(
	int InternalId,
	string Name,
	string RequestedPipeName,
	string ResolvedPipeName,
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
		string requestedPipeName,
		string? aggregatePipeName = null,
		string? pipePrefix = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(requestedPipeName);
		var normalizedRequest = NormalizeName(requestedPipeName);
		var effectiveAggregate = string.IsNullOrWhiteSpace(aggregatePipeName)
			? DiagnosticSwitchboardCompiledConfig.DefaultRuntimeDiagnosticsPipeName
			: aggregatePipeName.Trim();
		var effectivePrefix = string.IsNullOrWhiteSpace(pipePrefix)
			? DefaultPipePrefix
			: pipePrefix.Trim();
		var now = DateTimeOffset.UtcNow;
		var currentPid = Environment.ProcessId;
		var name = BuildUniqueName(normalizedRequest);
		var internalId = Interlocked.Increment(ref _nextInternalId);
		var resolvedPipeName = ResolvePipeName(name, effectivePrefix);
		EnsurePipeCreatable(resolvedPipeName);
		var lease = RuntimePipeLeaseState.Create(
			internalId,
			name,
			normalizedRequest,
			resolvedPipeName,
			effectiveAggregate,
			currentPid,
			now);
		Leases[name] = lease;
		LeasesById[internalId] = lease;
		return lease.ResolvedPipeName;
	}

	public static RuntimePipeLeaseSnapshot AnnouncePipe(
		string requestedPipeName,
		string resolvedPipeName,
		string? aggregatePipeName = null,
		int? ownerProcessId = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(requestedPipeName);
		ArgumentException.ThrowIfNullOrWhiteSpace(resolvedPipeName);
		var normalizedRequest = NormalizeName(requestedPipeName);
		var normalizedPipeName = resolvedPipeName.Trim();
		var effectiveAggregate = string.IsNullOrWhiteSpace(aggregatePipeName)
			? DiagnosticSwitchboardCompiledConfig.DefaultRuntimeDiagnosticsPipeName
			: aggregatePipeName.Trim();
		var now = DateTimeOffset.UtcNow;
		var effectiveOwnerProcessId = ownerProcessId ?? Environment.ProcessId;

		var existing = Leases.Values.FirstOrDefault(x =>
			x.RequestedPipeName.Equals(normalizedRequest, StringComparison.OrdinalIgnoreCase)
			&& x.ResolvedPipeName.Equals(normalizedPipeName, StringComparison.OrdinalIgnoreCase));
		if (existing is not null)
		{
			existing.AggregatePipeName = effectiveAggregate;
			existing.OwnerProcessId = effectiveOwnerProcessId;
			existing.Active = true;
			existing.UpdatedAt = now;
			return existing.ToSnapshot();
		}

		var name = BuildUniqueName(normalizedRequest);
		var internalId = Interlocked.Increment(ref _nextInternalId);
		var lease = RuntimePipeLeaseState.Create(
			internalId,
			name,
			normalizedRequest,
			normalizedPipeName,
			effectiveAggregate,
			effectiveOwnerProcessId,
			now);
		Leases[name] = lease;
		LeasesById[internalId] = lease;
		return lease.ToSnapshot();
	}

	public static bool ReleasePipe(string nameOrId)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(nameOrId);
		if (!TryResolve(nameOrId, out var lease))
		{
			return false;
		}

		lease.Active = false;
		lease.UpdatedAt = DateTimeOffset.UtcNow;
		return true;
	}

	public static string? ResolveAggregatePipe(string nameOrId)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(nameOrId);
		if (!TryResolve(nameOrId, out var lease) || !lease.Active)
		{
			return null;
		}

		return lease.AggregatePipeName;
	}

	public static RuntimePipeLeaseSnapshot? GetLease(string nameOrId)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(nameOrId);
		return TryResolve(nameOrId, out var lease)
			? lease.ToSnapshot()
			: null;
	}

	public static IReadOnlyList<RuntimePipeLeaseSnapshot> Snapshot(bool includeInactive = true)
	{
		return Leases.Values
			.Where(x => includeInactive || x.Active)
			.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
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

	private static string BuildUniqueName(string normalizedRequest)
	{
		var suffix = 0;
		while (true)
		{
			var name = suffix == 0
				? normalizedRequest
				: $"{normalizedRequest}_{suffix:000}";
			if (!Leases.ContainsKey(name))
			{
				return name;
			}

			suffix++;
		}
	}

	private static string ResolvePipeName(string name, string prefix)
	{
		return $"{prefix}.{name}";
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

	private static bool TryResolve(string nameOrId, out RuntimePipeLeaseState lease)
	{
		lease = null!;
		if (int.TryParse(nameOrId.Trim(), out var internalId))
		{
			return LeasesById.TryGetValue(internalId, out lease!);
		}

		return Leases.TryGetValue(NormalizeName(nameOrId), out lease!);
	}

	private sealed class RuntimePipeLeaseState
	{
		private RuntimePipeLeaseState(
			int internalId,
			string name,
			string requestedPipeName,
			string resolvedPipeName,
			string aggregatePipeName,
			int ownerProcessId,
			DateTimeOffset createdAt,
			DateTimeOffset updatedAt,
			bool active)
		{
			InternalId = internalId;
			Name = name;
			RequestedPipeName = requestedPipeName;
			ResolvedPipeName = resolvedPipeName;
			AggregatePipeName = aggregatePipeName;
			OwnerProcessId = ownerProcessId;
			CreatedAt = createdAt;
			UpdatedAt = updatedAt;
			Active = active;
		}

		public int InternalId { get; }
		public string Name { get; }
		public string RequestedPipeName { get; }
		public string ResolvedPipeName { get; }
		public string AggregatePipeName { get; set; }
		public int OwnerProcessId { get; set; }
		public bool Active { get; set; }
		public DateTimeOffset CreatedAt { get; }
		public DateTimeOffset UpdatedAt { get; set; }

		public RuntimePipeLeaseSnapshot ToSnapshot()
		{
			return new RuntimePipeLeaseSnapshot(
				InternalId,
				Name,
				RequestedPipeName,
				ResolvedPipeName,
				AggregatePipeName,
				OwnerProcessId,
				Active,
				CreatedAt,
				UpdatedAt);
		}

		public static RuntimePipeLeaseState Create(
			int internalId,
			string name,
			string requestedPipeName,
			string resolvedPipeName,
			string aggregatePipeName,
			int ownerProcessId,
			DateTimeOffset now)
		{
			return new RuntimePipeLeaseState(
				internalId,
				name,
				requestedPipeName,
				resolvedPipeName,
				aggregatePipeName,
				ownerProcessId,
				now,
				now,
				active: true);
		}
	}
}

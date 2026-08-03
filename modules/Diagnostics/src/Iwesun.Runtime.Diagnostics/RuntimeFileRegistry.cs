using System.Collections.Concurrent;

namespace Iwesun.Runtime.Diagnostics;

public sealed record RuntimeFileLeaseSnapshot(
    int InternalId,
    string Name,
    string TemplatePath,
    string ResolvedPath,
    string WriteMode,
    int OwnerProcessId,
    bool Active,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Central registry for diagnostic file output paths.
/// Mirrors the pattern of <see cref="RuntimePipeRegistry"/> for named pipes.
/// Each registration tracks the logical name, template path (from config/args),
/// resolved/actual path (after timestamp suffix or mode resolution), and write mode.
/// </summary>
public static class RuntimeFileRegistry
{
    private static readonly ConcurrentDictionary<string, RuntimeFileLeaseState> Leases = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<int, RuntimeFileLeaseState> LeasesById = new();
    private static int _nextInternalId;

    /// <summary>
    /// Register a file output path.
    /// If a registration with the same <paramref name="name"/> already exists, it is updated in-place.
    /// </summary>
    public static RuntimeFileLeaseSnapshot Register(
        string name,
        string templatePath,
        string resolvedPath,
        FileWriteMode writeMode,
        int? ownerProcessId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(templatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(resolvedPath);

        var normalizedName = name.Trim();
        var effectiveOwner = ownerProcessId ?? Environment.ProcessId;
        var now = DateTimeOffset.UtcNow;

        if (Leases.TryGetValue(normalizedName, out var existing))
        {
            existing.TemplatePath = templatePath.Trim();
            existing.ResolvedPath = resolvedPath.Trim();
            existing.WriteMode = writeMode.ToString();
            existing.OwnerProcessId = effectiveOwner;
            existing.Active = true;
            existing.UpdatedAt = now;
            return existing.ToSnapshot();
        }

        var internalId = Interlocked.Increment(ref _nextInternalId);
        var lease = RuntimeFileLeaseState.Create(
            internalId,
            normalizedName,
            templatePath.Trim(),
            resolvedPath.Trim(),
            writeMode.ToString(),
            effectiveOwner,
            now);
        Leases[normalizedName] = lease;
        LeasesById[internalId] = lease;
        return lease.ToSnapshot();
    }

    /// <summary>Marks a registration as inactive without removing it from the registry.</summary>
    public static bool Release(string nameOrId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nameOrId);
        if (!TryResolve(nameOrId, out var lease))
            return false;

        lease.Active = false;
        lease.UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }

    /// <summary>Returns the snapshot for the given name or internal ID, or null if not found.</summary>
    public static RuntimeFileLeaseSnapshot? GetRegistration(string nameOrId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nameOrId);
        return TryResolve(nameOrId, out var lease) ? lease.ToSnapshot() : null;
    }

    /// <summary>Returns the resolved (actual) file path for the given name or internal ID.</summary>
    public static string? GetResolvedPath(string nameOrId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nameOrId);
        return TryResolve(nameOrId, out var lease) && lease.Active ? lease.ResolvedPath : null;
    }

    /// <summary>Returns all registrations, optionally including inactive ones.</summary>
    public static IReadOnlyList<RuntimeFileLeaseSnapshot> Snapshot(bool includeInactive = true)
    {
        return Leases.Values
            .Where(x => includeInactive || x.Active)
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.ToSnapshot())
            .ToArray();
    }

    /// <summary>Removes all inactive registrations. Returns the count removed.</summary>
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

    private static bool TryResolve(string nameOrId, out RuntimeFileLeaseState lease)
    {
        lease = null!;
        if (int.TryParse(nameOrId.Trim(), out var internalId))
            return LeasesById.TryGetValue(internalId, out lease!);

        return Leases.TryGetValue(nameOrId.Trim(), out lease!);
    }

    private sealed class RuntimeFileLeaseState
    {
        private RuntimeFileLeaseState(
            int internalId,
            string name,
            string templatePath,
            string resolvedPath,
            string writeMode,
            int ownerProcessId,
            DateTimeOffset createdAt,
            DateTimeOffset updatedAt,
            bool active)
        {
            InternalId = internalId;
            Name = name;
            TemplatePath = templatePath;
            ResolvedPath = resolvedPath;
            WriteMode = writeMode;
            OwnerProcessId = ownerProcessId;
            CreatedAt = createdAt;
            UpdatedAt = updatedAt;
            Active = active;
        }

        public int InternalId { get; }
        public string Name { get; }
        public string TemplatePath { get; set; }
        public string ResolvedPath { get; set; }
        public string WriteMode { get; set; }
        public int OwnerProcessId { get; set; }
        public bool Active { get; set; }
        public DateTimeOffset CreatedAt { get; }
        public DateTimeOffset UpdatedAt { get; set; }

        public RuntimeFileLeaseSnapshot ToSnapshot() => new(
            InternalId,
            Name,
            TemplatePath,
            ResolvedPath,
            WriteMode,
            OwnerProcessId,
            Active,
            CreatedAt,
            UpdatedAt);

        public static RuntimeFileLeaseState Create(
            int internalId,
            string name,
            string templatePath,
            string resolvedPath,
            string writeMode,
            int ownerProcessId,
            DateTimeOffset now) =>
            new(internalId, name, templatePath, resolvedPath, writeMode, ownerProcessId, now, now, active: true);
    }
}

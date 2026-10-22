namespace Ignixa.PackageManagement.Models;

/// <summary>
/// Result of importing a package to the database.
/// </summary>
public record PackageImportResult
{
    /// <summary>
    /// Package ID (e.g., "hl7.fhir.us.core").
    /// </summary>
    public required string PackageId { get; init; }

    /// <summary>
    /// Package version (e.g., "5.0.1").
    /// </summary>
    public required string PackageVersion { get; init; }

    /// <summary>
    /// Total number of resources extracted.
    /// </summary>
    public int TotalResources { get; init; }

    /// <summary>
    /// Number of new resources imported.
    /// </summary>
    public int ImportedResources { get; init; }

    /// <summary>
    /// Number of existing resources updated.
    /// </summary>
    public int UpdatedResources { get; init; }

    /// <summary>
    /// Time taken for the entire import operation.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Breakdown by resource type.
    /// </summary>
    public Dictionary<string, int> ResourcesByType { get; init; } = new();

    /// <summary>
    /// Packages loaded as part of this operation. Populated when the operation
    /// walked a dependency closure; <c>null</c> for single-package loads.
    /// Each entry is the canonical <c>id@version</c> followed by either no
    /// suffix or <c>(already loaded)</c> when the import reported no resources. The marker is
    /// count-based, so a newly imported package with no resources also carries it; prefer
    /// <see cref="ClosureMembers"/> for authoritative status.
    /// </summary>
    public IReadOnlyList<string>? LoadedPackages { get; init; }

    /// <summary>
    /// Human-readable skipped-package summary strings, including informational core-package entries.
    /// Entries are typically "<c>id@version (reason)</c>"; manifest failures use
    /// "<c>id@version dependencies (...)</c>". <c>null</c> for single-package loads or when nothing was skipped.
    /// Use <c>ClosureMembers[].IsBlocking</c> (or <c>LoadPackageResult.IsComplete</c>) to determine whether the load is incomplete.
    /// </summary>
    public IReadOnlyList<string>? SkippedPackages { get; init; }

    /// <summary>
    /// Typed per-package outcomes for a dependency-closure load; empty for single-package loads.
    /// Prefer this over the legacy <see cref="LoadedPackages"/> and <see cref="SkippedPackages"/> strings.
    /// </summary>
    public IReadOnlyList<PackageClosureMember> ClosureMembers { get; init; } = [];
}

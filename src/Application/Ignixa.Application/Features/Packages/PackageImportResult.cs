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
    /// suffix (newly imported) or <c>(already loaded)</c> for entries that
    /// short-circuited the import because the version was already present.
    /// </summary>
    public IReadOnlyList<string>? LoadedPackages { get; init; }

    /// <summary>
    /// Packages skipped due to load errors during dependency closure walk.
    /// Each entry is "<c>id@version (reason)</c>". Null for single-package loads.
    /// Non-null non-empty list indicates a partial load; callers should inspect and log.
    /// </summary>
    public IReadOnlyList<string>? SkippedPackages { get; init; }
}

using Medino;

namespace Ignixa.Application.Features.Admin;

/// <summary>
/// Command to load a FHIR package from the NPM registry into a specific tenant's database.
/// Set <paramref name="IncludeDependencies"/> to walk the declared dependency closure
/// (e.g. AU Core pulls AU Base, HL7 Terminology, UV Extensions in one call).
/// </summary>
public record LoadPackageCommand(
    string TenantId,
    string PackageId,
    string Version,
    bool IncludeDependencies = false) : IRequest<LoadPackageResult>;

/// <summary>
/// Result of loading a package.
/// </summary>
public record LoadPackageResult
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
    /// Number of resources imported.
    /// </summary>
    public int ImportedResources { get; init; }

    /// <summary>
    /// Time taken in milliseconds.
    /// </summary>
    public long DurationMilliseconds { get; init; }

    /// <summary>
    /// Breakdown by resource type.
    /// </summary>
    public Dictionary<string, int> ResourcesByType { get; init; } = new();

    /// <summary>
    /// Packages loaded as part of this operation. Non-null when the request used
    /// <see cref="LoadPackageCommand.IncludeDependencies"/>; each entry is the
    /// canonical <c>id@version</c> followed by either no suffix (newly imported)
    /// or <c>(already loaded)</c> / <c>(skipped: ...)</c> for entries that
    /// short-circuited the import.
    /// </summary>
    public IReadOnlyList<string>? LoadedPackages { get; init; }

    /// <summary>
    /// Packages skipped due to load errors during dependency closure walk.
    /// Each entry is "<c>id@version (reason)</c>". Null for single-package loads.
    /// Non-null non-empty list indicates a partial load.
    /// </summary>
    public IReadOnlyList<string>? SkippedPackages { get; init; }
}

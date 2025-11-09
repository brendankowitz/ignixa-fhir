using Medino;

namespace Ignixa.Application.Features.Admin;

/// <summary>
/// Command to load a FHIR package from the NPM registry into a specific tenant's database.
/// </summary>
public record LoadPackageCommand(string TenantId, string PackageId, string Version) : IRequest<LoadPackageResult>;

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
}

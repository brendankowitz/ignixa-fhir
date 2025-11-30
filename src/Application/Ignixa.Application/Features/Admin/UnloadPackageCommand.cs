using Medino;

namespace Ignixa.Application.Features.Admin;

/// <summary>
/// Command to unload (deactivate) a FHIR package from a specific tenant's database.
/// </summary>
public record UnloadPackageCommand(string TenantId, string PackageId, string Version) : IRequest<UnloadPackageResult>;

/// <summary>
/// Result of unloading a package.
/// </summary>
public record UnloadPackageResult
{
    /// <summary>
    /// Package ID.
    /// </summary>
    public required string PackageId { get; init; }

    /// <summary>
    /// Package version.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// Number of resources deactivated.
    /// </summary>
    public int ResourcesDeactivated { get; init; }
}

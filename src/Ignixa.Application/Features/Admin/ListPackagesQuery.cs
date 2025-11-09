using Medino;

namespace Ignixa.Application.Features.Admin;

/// <summary>
/// Query to list all loaded FHIR packages for a specific tenant.
/// </summary>
public record ListPackagesQuery(string TenantId) : IRequest<ListPackagesResult>;

/// <summary>
/// Result of listing packages.
/// </summary>
public record ListPackagesResult
{
    /// <summary>
    /// List of loaded packages.
    /// </summary>
    public required IReadOnlyList<PackageInfo> Packages { get; init; }
}

/// <summary>
/// Information about a loaded package.
/// </summary>
public record PackageInfo
{
    /// <summary>
    /// Package ID (e.g., "hl7.fhir.us.core").
    /// </summary>
    public required string PackageId { get; init; }

    /// <summary>
    /// Package version (e.g., "5.0.1").
    /// </summary>
    public required string Version { get; init; }
}

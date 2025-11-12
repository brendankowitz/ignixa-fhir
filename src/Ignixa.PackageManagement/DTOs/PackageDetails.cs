namespace Ignixa.PackageManagement.DTOs;

/// <summary>
/// Detailed information about a FHIR package.
/// </summary>
public record PackageDetails
{
    /// <summary>
    /// Package ID (e.g., "hl7.fhir.us.core").
    /// </summary>
    public required string PackageId { get; init; }

    /// <summary>
    /// Package description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Latest version tag.
    /// </summary>
    public string? LatestVersion { get; init; }

    /// <summary>
    /// All available versions.
    /// </summary>
    public required IReadOnlyList<PackageVersionInfo> Versions { get; init; }
}

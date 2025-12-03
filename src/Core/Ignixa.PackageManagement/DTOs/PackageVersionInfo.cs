namespace Ignixa.PackageManagement.DTOs;

/// <summary>
/// Information about a specific package version.
/// </summary>
public record PackageVersionInfo
{
    /// <summary>
    /// Version number (e.g., "6.1.0").
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// FHIR version this package targets (e.g., "R4", "Stu3").
    /// </summary>
    public string? FhirVersion { get; init; }

    /// <summary>
    /// Version-specific description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Download URL.
    /// </summary>
    public string? Url { get; init; }
}

namespace Ignixa.PackageManagement.DTOs;

/// <summary>
/// Represents a search result for a FHIR package.
/// </summary>
public record PackageSearchResult
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
    /// FHIR version(s) supported by this package.
    /// </summary>
    public string? FhirVersion { get; init; }

    /// <summary>
    /// Latest version available.
    /// </summary>
    public string? LatestVersion { get; init; }

    /// <summary>
    /// Search relevance score (0-100, higher is better match).
    /// </summary>
    public int RelevanceScore { get; init; }
}

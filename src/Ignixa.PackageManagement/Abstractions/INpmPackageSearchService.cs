namespace Ignixa.PackageManagement.Abstractions;

/// <summary>
/// Service for searching FHIR packages in the NPM registry.
/// Provides fuzzy search capabilities for package discovery and name resolution.
/// </summary>
public interface INpmPackageSearchService
{
    /// <summary>
    /// Searches for FHIR packages matching the query string.
    /// Performs fuzzy matching against package names and descriptions.
    /// </summary>
    /// <param name="query">Search query (e.g., "USCore", "us core", "hl7.fhir.us.core")</param>
    /// <param name="maxResults">Maximum number of results to return (default: 10)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of package search results ordered by relevance</returns>
    Task<IReadOnlyList<PackageSearchResult>> SearchPackagesAsync(
        string query,
        int maxResults = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets detailed information about a specific package, including all available versions.
    /// </summary>
    /// <param name="packageId">Package ID (e.g., "hl7.fhir.us.core")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Package details with versions, or null if not found</returns>
    Task<PackageDetails?> GetPackageDetailsAsync(
        string packageId,
        CancellationToken cancellationToken = default);
}

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
    /// FHIR version this package targets (e.g., "R4", "STU3").
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

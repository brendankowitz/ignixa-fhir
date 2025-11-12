using Ignixa.PackageManagement.DTOs;

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

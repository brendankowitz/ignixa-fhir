using System.Text.Json;
using Ignixa.Abstractions;
using Ignixa.PackageManagement.Abstractions;
using Ignixa.PackageManagement.DTOs;
using Microsoft.Extensions.Logging;

namespace Ignixa.PackageManagement.Infrastructure;

/// <summary>
/// Service for searching FHIR packages using the Simplifier.net FHIR Package API.
/// Uses server-side filtering with the /catalog?name= endpoint for better search results.
/// </summary>
public class NpmPackageSearchService : INpmPackageSearchService
{
    private readonly HttpClient _httpClient;
    private readonly NpmPackageLoaderOptions _options;
    private readonly ILogger<NpmPackageSearchService> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public NpmPackageSearchService(
        HttpClient httpClient,
        NpmPackageLoaderOptions? options,
        ILogger<NpmPackageSearchService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? new NpmPackageLoaderOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Searches for FHIR packages matching the query string.
    /// Uses server-side filtering via the /catalog?name= endpoint.
    /// </summary>
    public async Task<IReadOnlyList<PackageSearchResult>> SearchPackagesAsync(
        string query,
        int maxResults = 10,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Search query cannot be empty", nameof(query));
        }

        if (maxResults <= 0)
        {
            throw new ArgumentException("Max results must be greater than 0", nameof(maxResults));
        }

        _logger.LogDebug("Searching for packages with query: {Query}", query);

        // Step 1: Get catalog with server-side filtering
        var catalog = await GetCatalogAsync(query, cancellationToken);

        // Step 2: Score and filter matches
        var queryLower = query.ToUpperInvariant();
        var scoredResults = catalog
            .Select(entry => new
            {
                Entry = entry,
                Score = CalculateRelevanceScore(entry, queryLower)
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(maxResults)
            .ToList();

        if (scoredResults.Count == 0)
        {
            _logger.LogInformation("No packages found matching query: {Query}", query);
            return Array.Empty<PackageSearchResult>();
        }

        // Step 3: Fetch latest version for top results
        var results = new List<PackageSearchResult>();
        foreach (var scored in scoredResults)
        {
            var latestVersion = await TryGetLatestVersionAsync(scored.Entry.Name, cancellationToken);

            results.Add(new PackageSearchResult
            {
                PackageId = scored.Entry.Name,
                Description = scored.Entry.Description,
                FhirVersion = scored.Entry.FhirVersion,
                LatestVersion = latestVersion,
                RelevanceScore = scored.Score
            });
        }

        _logger.LogInformation("Found {Count} packages matching query: {Query}", results.Count, query);
        return results;
    }

    /// <summary>
    /// Gets detailed information about a specific package, including all available versions.
    /// </summary>
    public async Task<PackageDetails?> GetPackageDetailsAsync(
        string packageId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packageId))
        {
            throw new ArgumentException("Package ID cannot be empty", nameof(packageId));
        }

        _logger.LogDebug("Fetching details for package: {PackageId}", packageId);

        try
        {
            var url = $"{_options.RegistryUrl.TrimEnd('/')}/{packageId}";
            var response = await _httpClient.GetAsync(new Uri(url), cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogWarning("Package not found: {PackageId}", packageId);
                    return null;
                }

                response.EnsureSuccessStatusCode();
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var metadata = JsonSerializer.Deserialize<PackageMetadata>(json, JsonOptions);

            if (metadata == null)
            {
                _logger.LogWarning("Failed to deserialize package metadata for: {PackageId}", packageId);
                return null;
            }

            var versions = metadata.Versions?
                .Select(v => new PackageVersionInfo
                {
                    Version = v.Value.Version ?? v.Key,
                    FhirVersion = v.Value.FhirVersion,
                    Description = v.Value.Description,
                    Url = v.Value.Url
                })
                .OrderByDescending(v => v.Version)
                .ToList() ?? new List<PackageVersionInfo>();

            return new PackageDetails
            {
                PackageId = metadata.Name ?? packageId,
                Description = metadata.Description,
                LatestVersion = metadata.DistTags?.Latest,
                Versions = versions
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch package details for: {PackageId}", packageId);
            throw;
        }
    }

    /// <summary>
    /// Fetches the package catalog from the registry with optional server-side filtering.
    /// When using Simplifier.net API, the ?name= parameter enables server-side partial name matching.
    /// Uses caching to reduce HTTP requests (cache key includes query for filtered results).
    /// </summary>
    private async Task<CatalogEntry[]> GetCatalogAsync(string? nameFilter, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Fetching catalog from registry with filter: {Filter}", nameFilter ?? "(none)");

        try
        {
            var urlBuilder = new System.Text.StringBuilder($"{_options.RegistryUrl.TrimEnd('/')}/catalog");

            // Add name filter parameter if provided (Simplifier.net API supports this)
            if (!string.IsNullOrWhiteSpace(nameFilter))
            {
                urlBuilder.Append($"?name={Uri.EscapeDataString(nameFilter)}");
            }

            var url = urlBuilder.ToString();
            var response = await _httpClient.GetAsync(new Uri(url), cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var catalog = JsonSerializer.Deserialize<CatalogEntry[]>(json, JsonOptions) ?? Array.Empty<CatalogEntry>();

            _logger.LogInformation("Fetched catalog with {Count} packages", catalog.Length);
            return catalog;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch catalog");
            throw;
        }
    }

    /// <summary>
    /// Tries to get the latest version for a package.
    /// Returns null if fetch fails (non-critical operation).
    /// </summary>
    private async Task<string?> TryGetLatestVersionAsync(string packageId, CancellationToken cancellationToken)
    {
        try
        {
            var details = await GetPackageDetailsAsync(packageId, cancellationToken);
            return details?.LatestVersion;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch latest version for package: {PackageId}", packageId);
            return null;
        }
    }

    /// <summary>
    /// Calculates relevance score for a catalog entry against a search query.
    /// Since Simplifier.net API already filters by name, we only need simple scoring.
    /// Higher scores indicate better matches.
    /// </summary>
    private static int CalculateRelevanceScore(CatalogEntry entry, string queryLower)
    {
        var nameLower = entry.Name?.ToUpperInvariant() ?? string.Empty;
        var descriptionLower = entry.Description?.ToUpperInvariant() ?? string.Empty;

        var score = 0;

        // Exact match (highest priority)
        if (nameLower == queryLower)
        {
            score += 100;
        }
        // Starts with query
        else if (nameLower.StartsWith(queryLower, StringComparison.OrdinalIgnoreCase))
        {
            score += 80;
        }
        // Contains query anywhere
        else if (nameLower.Contains(queryLower, StringComparison.OrdinalIgnoreCase))
        {
            score += 40;
        }

        // Description matching (bonus points)
        if (descriptionLower.Contains(queryLower, StringComparison.OrdinalIgnoreCase))
        {
            score += 20;
        }

        return score;
    }
}

using System.Text.Json;
using Ignixa.PackageManagement.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ignixa.PackageManagement.Infrastructure;

/// <summary>
/// Service for searching FHIR packages in the NPM registry.
/// Uses fuzzy matching to help users find packages with partial or approximate names.
/// </summary>
public class NpmPackageSearchService : INpmPackageSearchService
{
    private readonly HttpClient _httpClient;
    private readonly NpmPackageLoaderOptions _options;
    private readonly ILogger<NpmPackageSearchService> _logger;

    // Cache for catalog entries to reduce HTTP calls
    private CatalogEntry[]? _cachedCatalog;
    private DateTime? _catalogCacheTime;
    private readonly TimeSpan _catalogCacheDuration = TimeSpan.FromMinutes(15);

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
    /// Performs fuzzy matching against package names and descriptions.
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

        // Step 1: Get catalog
        var catalog = await GetCatalogAsync(cancellationToken);

        // Step 2: Score and filter matches
        var queryLower = query.ToLowerInvariant();
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
            var response = await _httpClient.GetAsync(url, cancellationToken);

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
            var metadata = JsonSerializer.Deserialize<PackageMetadata>(json);

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
    /// Fetches the package catalog from the NPM registry.
    /// Uses caching to reduce HTTP requests.
    /// </summary>
    private async Task<CatalogEntry[]> GetCatalogAsync(CancellationToken cancellationToken)
    {
        // Check cache
        if (_cachedCatalog != null &&
            _catalogCacheTime.HasValue &&
            DateTime.UtcNow - _catalogCacheTime.Value < _catalogCacheDuration)
        {
            _logger.LogDebug("Using cached catalog");
            return _cachedCatalog;
        }

        _logger.LogDebug("Fetching catalog from registry");

        try
        {
            var url = $"{_options.RegistryUrl.TrimEnd('/')}/catalog";
            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var catalog = JsonSerializer.Deserialize<CatalogEntry[]>(json) ?? Array.Empty<CatalogEntry>();

            // Update cache
            _cachedCatalog = catalog;
            _catalogCacheTime = DateTime.UtcNow;

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
    /// Higher scores indicate better matches.
    /// </summary>
    private static int CalculateRelevanceScore(CatalogEntry entry, string queryLower)
    {
        var nameLower = entry.Name?.ToLowerInvariant() ?? string.Empty;
        var descriptionLower = entry.Description?.ToLowerInvariant() ?? string.Empty;

        var score = 0;

        // Exact match (highest priority)
        if (nameLower == queryLower)
        {
            score += 100;
        }
        // Starts with query
        else if (nameLower.StartsWith(queryLower))
        {
            score += 80;
        }
        // Contains query as word
        else if (nameLower.Contains($".{queryLower}.") || nameLower.Contains($".{queryLower}") || nameLower.Contains($"{queryLower}."))
        {
            score += 60;
        }
        // Contains query anywhere
        else if (nameLower.Contains(queryLower))
        {
            score += 40;
        }

        // Description matching (bonus points)
        if (descriptionLower.Contains(queryLower))
        {
            score += 20;
        }

        // Fuzzy matching for common abbreviations and variations
        score += CalculateFuzzyScore(nameLower, queryLower);

        return score;
    }

    /// <summary>
    /// Calculates fuzzy matching score for common patterns.
    /// Handles abbreviations like "uscore" -> "us.core" or "uscdi" variations.
    /// </summary>
    private static int CalculateFuzzyScore(string name, string query)
    {
        var score = 0;

        // Remove common separators for fuzzy comparison
        var nameNormalized = name.Replace(".", "").Replace("-", "").Replace("_", "");
        var queryNormalized = query.Replace(".", "").Replace("-", "").Replace("_", "");

        if (nameNormalized.Contains(queryNormalized))
        {
            score += 30;
        }

        // Handle common abbreviations
        if (query.Contains("uscore") && name.Contains("us.core"))
        {
            score += 50;
        }

        if (query.Contains("uscdi") && name.Contains("us.core"))
        {
            score += 30;
        }

        // Levenshtein distance for short queries (3+ chars)
        if (query.Length >= 3)
        {
            var distance = LevenshteinDistance(nameNormalized, queryNormalized);
            if (distance <= 3)
            {
                score += (10 - distance);
            }
        }

        return score;
    }

    /// <summary>
    /// Calculates Levenshtein distance between two strings.
    /// Returns the minimum number of single-character edits needed to transform one string into another.
    /// </summary>
    private static int LevenshteinDistance(string source, string target)
    {
        if (string.IsNullOrEmpty(source))
        {
            return target?.Length ?? 0;
        }

        if (string.IsNullOrEmpty(target))
        {
            return source.Length;
        }

        var sourceLength = source.Length;
        var targetLength = target.Length;
        var distance = new int[sourceLength + 1, targetLength + 1];

        for (var i = 0; i <= sourceLength; i++)
        {
            distance[i, 0] = i;
        }

        for (var j = 0; j <= targetLength; j++)
        {
            distance[0, j] = j;
        }

        for (var i = 1; i <= sourceLength; i++)
        {
            for (var j = 1; j <= targetLength; j++)
            {
                var cost = target[j - 1] == source[i - 1] ? 0 : 1;
                distance[i, j] = Math.Min(
                    Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1),
                    distance[i - 1, j - 1] + cost);
            }
        }

        return distance[sourceLength, targetLength];
    }

    // JSON deserialization models
    private class CatalogEntry
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? FhirVersion { get; set; }
    }

    private class PackageMetadata
    {
        public string? Name { get; set; }
        public string? Description { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("dist-tags")]
        public DistTags? DistTags { get; set; }

        public Dictionary<string, VersionMetadata>? Versions { get; set; }
    }

    private class DistTags
    {
        public string? Latest { get; set; }
    }

    private class VersionMetadata
    {
        public string? Version { get; set; }
        public string? Description { get; set; }
        public string? FhirVersion { get; set; }
        public string? Url { get; set; }
    }
}

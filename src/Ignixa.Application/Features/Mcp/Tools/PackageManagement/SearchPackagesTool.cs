// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel;
using ModelContextProtocol.Server;
using Ignixa.PackageManagement.Abstractions;

namespace Ignixa.Application.Features.Mcp.Tools.PackageManagement;

/// <summary>
/// MCP tool for searching available FHIR packages in the NPM registry.
/// Provides fuzzy matching to help users discover packages and resolve package names.
/// </summary>
[McpServerToolType]
public class SearchPackagesTool
{
    private readonly INpmPackageSearchService _searchService;

    public SearchPackagesTool(INpmPackageSearchService searchService)
    {
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
    }

    [McpServerTool(Name = "search_fhir_packages")]
    [Description(@"Search for available FHIR packages in the NPM registry using fuzzy matching.
Helps discover packages and resolve partial names to full package IDs.
Examples:
  - 'USCore' finds 'hl7.fhir.us.core'
  - 'us core' finds 'hl7.fhir.us.core'
  - 'ukcore' finds 'hl7.fhir.uk.core.r4'
Results include package ID, description, FHIR version, and latest version.
Use this before install_fhir_package to find the exact package ID and version.")]
    public async Task<SearchPackagesResultDto> SearchPackagesAsync(
        [Description("Search query (package name or partial name, e.g., 'USCore', 'us core', 'hl7.fhir.us')")]
        string query,

        [Description("Maximum number of results to return (default: 10, max: 50)")]
        int? maxResults = null,

        CancellationToken cancellationToken = default)
    {
        // Validate input
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Search query cannot be empty");
        }

        var limitedMaxResults = Math.Min(maxResults ?? 10, 50);

        // Search packages
        var results = await _searchService.SearchPackagesAsync(
            query,
            limitedMaxResults,
            cancellationToken);

        var resultDtos = results
            .Select(r => new PackageSearchResultDto
            {
                PackageId = r.PackageId,
                Description = r.Description,
                FhirVersion = r.FhirVersion,
                LatestVersion = r.LatestVersion,
                RelevanceScore = r.RelevanceScore
            })
            .ToList();

        return new SearchPackagesResultDto
        {
            Query = query,
            TotalResults = resultDtos.Count,
            Results = resultDtos
        };
    }

    [McpServerTool(Name = "get_fhir_package_details")]
    [Description(@"Get detailed information about a specific FHIR package, including all available versions.
Use this to see all versions before installing a specific one.
Returns package ID, description, latest version, and list of all available versions.
Example: packageId='hl7.fhir.us.core' returns versions like 6.1.0, 6.0.0, 5.0.1, etc.")]
    public async Task<PackageDetailsResultDto?> GetPackageDetailsAsync(
        [Description("Package ID (e.g., 'hl7.fhir.us.core')")]
        string packageId,

        CancellationToken cancellationToken = default)
    {
        // Validate input
        if (string.IsNullOrWhiteSpace(packageId))
        {
            throw new ArgumentException("Package ID cannot be empty");
        }

        // Get package details
        var details = await _searchService.GetPackageDetailsAsync(packageId, cancellationToken);

        if (details == null)
        {
            return null;
        }

        var versionDtos = details.Versions
            .Select(v => new PackageVersionInfoDto
            {
                Version = v.Version,
                FhirVersion = v.FhirVersion,
                Description = v.Description
            })
            .ToList();

        return new PackageDetailsResultDto
        {
            PackageId = details.PackageId,
            Description = details.Description,
            LatestVersion = details.LatestVersion,
            TotalVersions = versionDtos.Count,
            Versions = versionDtos
        };
    }
}

/// <summary>
/// Result of searching packages.
/// </summary>
public record SearchPackagesResultDto
{
    /// <summary>
    /// Original search query.
    /// </summary>
    public required string Query { get; init; }

    /// <summary>
    /// Total number of results found.
    /// </summary>
    public required int TotalResults { get; init; }

    /// <summary>
    /// Search results ordered by relevance.
    /// </summary>
    public required IReadOnlyList<PackageSearchResultDto> Results { get; init; }
}

/// <summary>
/// Search result for a FHIR package.
/// </summary>
public record PackageSearchResultDto
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
    /// FHIR version(s) supported.
    /// </summary>
    public string? FhirVersion { get; init; }

    /// <summary>
    /// Latest version available.
    /// </summary>
    public string? LatestVersion { get; init; }

    /// <summary>
    /// Search relevance score (higher is better match).
    /// </summary>
    public int RelevanceScore { get; init; }
}

/// <summary>
/// Detailed package information result.
/// </summary>
public record PackageDetailsResultDto
{
    /// <summary>
    /// Package ID.
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
    /// Total number of versions available.
    /// </summary>
    public required int TotalVersions { get; init; }

    /// <summary>
    /// List of all available versions.
    /// </summary>
    public required IReadOnlyList<PackageVersionInfoDto> Versions { get; init; }
}

/// <summary>
/// Package version information.
/// </summary>
public record PackageVersionInfoDto
{
    /// <summary>
    /// Version number.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// FHIR version this package targets.
    /// </summary>
    public string? FhirVersion { get; init; }

    /// <summary>
    /// Version-specific description.
    /// </summary>
    public string? Description { get; init; }
}

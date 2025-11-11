// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search.Models;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace Ignixa.Search.Definition;

/// <summary>
/// Resolves conflicts when multiple IGs define SearchParameters with the same code.
/// Implements deterministic resolution using:
/// 1. Explicit priority configuration (if provided)
/// 2. Semantic versioning (fallback - highest version wins)
/// 3. Alphabetical package ID (stable sort for equal versions)
/// </summary>
public class SearchParameterConflictResolver
{
    private readonly SearchParameterResolutionOptions _options;
    private readonly ILogger<SearchParameterConflictResolver> _logger;

    public SearchParameterConflictResolver(
        SearchParameterResolutionOptions options,
        ILogger<SearchParameterConflictResolver> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Resolves conflicts among multiple SearchParameters with the same code.
    /// Returns the winning parameter based on priority or semantic version.
    /// </summary>
    /// <param name="candidates">List of SearchParameters with the same code (from different IGs).</param>
    /// <param name="code">Search parameter code (for logging).</param>
    /// <param name="resourceType">Resource type (for logging).</param>
    /// <param name="packageMetadata">Metadata mapping (canonical URL -> package info).</param>
    /// <returns>The winning SearchParameter.</returns>
    public SearchParameterInfo ResolveConflict(
        IReadOnlyList<SearchParameterInfo> candidates,
        string code,
        string resourceType,
        IReadOnlyDictionary<string, PackageMetadata> packageMetadata)
    {
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException($"Cannot resolve conflict: no candidates provided for code '{code}'");
        }

        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        // Enrich candidates with package metadata
        var enrichedCandidates = candidates
            .Select(param => new EnrichedCandidate(param, GetPackageMetadata(param, packageMetadata)))
            .ToList();

        // Deduplicate by (Url, PackageId, PackageVersion) to prevent duplicate logging
        // This can happen when same base parameter appears multiple times in merge process
        enrichedCandidates = enrichedCandidates
            .GroupBy(c => new
            {
                Url = c.Parameter.Url?.ToString() ?? string.Empty,
                c.Metadata.PackageId,
                c.Metadata.PackageVersion
            })
            .Select(g => g.First())
            .ToList();

        // After deduplication, check if conflict still exists
        if (enrichedCandidates.Count == 1)
        {
            return enrichedCandidates[0].Parameter;
        }

        // Try explicit priority first
        if (_options.PackagePriorityOrder != null && _options.PackagePriorityOrder.Count > 0)
        {
            var winner = ResolveByPriority(enrichedCandidates, code, resourceType);
            if (winner != null)
            {
                return winner.Parameter;
            }
        }

        // Fallback to semantic versioning
        if (_options.UseSemanticVersioning)
        {
            var winner = ResolveBySemanticVersion(enrichedCandidates, code, resourceType);
            return winner.Parameter;
        }

        // Last resort: first in list (should never happen with proper config)
        _logger.LogWarning(
            "No resolution strategy available for SearchParameter '{Code}' on {ResourceType}. Using first candidate.",
            code,
            resourceType);

        return candidates[0];
    }

    /// <summary>
    /// Resolves conflict using explicit priority configuration.
    /// Returns null if no candidate has a configured priority.
    /// </summary>
    private EnrichedCandidate? ResolveByPriority(
        List<EnrichedCandidate> candidates,
        string code,
        string resourceType)
    {
        // Find candidates with explicit priority
        var prioritizedCandidates = candidates
            .Select(c => new
            {
                Candidate = c,
                Rank = _options.GetPriorityRank(c.Metadata.PackageId)
            })
            .Where(x => x.Rank != int.MaxValue)
            .OrderBy(x => x.Rank)
            .ToList();

        if (prioritizedCandidates.Count == 0)
        {
            return null;
        }

        var winner = prioritizedCandidates[0];

        if (_options.LogConflicts && candidates.Count > 1)
        {
            var conflictInfo = string.Join(", ", candidates.Select(c =>
                $"{c.Metadata.PackageId}#{c.Metadata.PackageVersion} (rank {_options.GetPriorityRank(c.Metadata.PackageId)})"));

            _logger.LogWarning(
                "SearchParameter '{Code}' for {ResourceType}: Conflict between [{Conflicts}]. " +
                "Winner: {WinnerPackage}#{WinnerVersion} (priority rank {WinnerRank})",
                code,
                resourceType,
                conflictInfo,
                winner.Candidate.Metadata.PackageId,
                winner.Candidate.Metadata.PackageVersion,
                winner.Rank);
        }

        return winner.Candidate;
    }

    /// <summary>
    /// Resolves conflict using semantic versioning (highest version wins).
    /// If versions are equal, uses alphabetical package ID for deterministic ordering.
    /// </summary>
    private EnrichedCandidate ResolveBySemanticVersion(
        List<EnrichedCandidate> candidates,
        string code,
        string resourceType)
    {
        // Sort by semantic version (descending), then by package ID (ascending) for stable sort
        var sorted = candidates
            .Select(c => new
            {
                Candidate = c,
                Version = TryParseSemanticVersion(c.Metadata.PackageVersion)
            })
            .OrderByDescending(x => x.Version ?? new SemanticVersion(0, 0, 0))
            .ThenBy(x => x.Candidate.Metadata.PackageId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var winner = sorted[0];

        if (_options.LogConflicts && candidates.Count > 1)
        {
            var conflictInfo = string.Join(", ", sorted.Select(s =>
                $"{s.Candidate.Metadata.PackageId}#{s.Candidate.Metadata.PackageVersion}"));

            _logger.LogWarning(
                "SearchParameter '{Code}' for {ResourceType}: Conflict between [{Conflicts}]. " +
                "Winner: {WinnerPackage}#{WinnerVersion} (semantic version resolution)",
                code,
                resourceType,
                conflictInfo,
                winner.Candidate.Metadata.PackageId,
                winner.Candidate.Metadata.PackageVersion);
        }

        return winner.Candidate;
    }

    /// <summary>
    /// Attempts to parse a semantic version string.
    /// Returns null if parsing fails (logs warning).
    /// </summary>
    private SemanticVersion? TryParseSemanticVersion(string versionString)
    {
        if (string.IsNullOrWhiteSpace(versionString))
        {
            return null;
        }

        if (SemanticVersion.TryParse(versionString, out var version))
        {
            return version;
        }

        _logger.LogDebug(
            "Failed to parse semantic version: {Version}. Using as-is for comparison.",
            versionString);

        return null;
    }

    /// <summary>
    /// Gets package metadata for a SearchParameter.
    /// Extracts from packageMetadata dictionary using canonical URL.
    /// </summary>
    private PackageMetadata GetPackageMetadata(
        SearchParameterInfo parameter,
        IReadOnlyDictionary<string, PackageMetadata> packageMetadata)
    {
        if (parameter.Url != null &&
            packageMetadata.TryGetValue(parameter.Url.ToString(), out var metadata))
        {
            return metadata;
        }

        // Fallback: unknown package
        return new PackageMetadata
        {
            PackageId = "unknown",
            PackageVersion = "0.0.0",
            LoadedDate = DateTimeOffset.MinValue
        };
    }

    /// <summary>
    /// Enriched candidate with package metadata for conflict resolution.
    /// </summary>
    private record EnrichedCandidate(SearchParameterInfo Parameter, PackageMetadata Metadata);
}

/// <summary>
/// Package metadata for a SearchParameter (from which IG it came).
/// </summary>
public class PackageMetadata
{
    public required string PackageId { get; set; }
    public required string PackageVersion { get; set; }
    public DateTimeOffset LoadedDate { get; set; }
}

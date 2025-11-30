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
    ///
    /// CRITICAL: All candidates MUST apply to the specified resourceType via their BaseResourceTypes.
    /// This ensures per-resource-type conflict resolution as required by FHIR semantics.
    /// </summary>
    /// <param name="candidates">List of SearchParameters with the same code (from different IGs), all applying to the same resource type.</param>
    /// <param name="code">Search parameter code (for logging).</param>
    /// <param name="resourceType">Resource type that all candidates MUST apply to.</param>
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

        // VALIDATION: Ensure all candidates apply to this resource type
        // This catches bugs in the grouping logic and ensures per-resource-type resolution semantics
        ValidateCandidatesApplyToResourceType(candidates, code, resourceType);

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
        SearchParameterInfo winnerParameter;
        if (_options.PackagePriorityOrder != null && _options.PackagePriorityOrder.Count > 0)
        {
            var winner = ResolveByPriority(enrichedCandidates, code, resourceType);
            if (winner != null)
            {
                winnerParameter = winner.Parameter;
                PopulateOverridesUrl(winnerParameter, enrichedCandidates);
                return winnerParameter;
            }
        }

        // Fallback to semantic versioning
        if (_options.UseSemanticVersioning)
        {
            var winner = ResolveBySemanticVersion(enrichedCandidates, code, resourceType);
            winnerParameter = winner.Parameter;
            PopulateOverridesUrl(winnerParameter, enrichedCandidates);
            return winnerParameter;
        }

        // Last resort: first in list (should never happen with proper config)
        _logger.LogWarning(
            "No resolution strategy available for SearchParameter '{Code}' on {ResourceType}. Using first candidate.",
            code,
            resourceType);

        winnerParameter = candidates[0];
        PopulateOverridesUrl(winnerParameter, enrichedCandidates);
        return winnerParameter;
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
                $"{c.Metadata.PackageId}#{c.Metadata.PackageVersion} (URL: {c.Parameter.Url}, rank {_options.GetPriorityRank(c.Metadata.PackageId)})"));

            // Debug level: Expected behavior when loading multi-IG configurations (e.g., US Core + base FHIR)
            _logger.LogDebug(
                "SearchParameter '{Code}' for {ResourceType}: Conflict between [{Conflicts}]. " +
                "Winner: {WinnerPackage}#{WinnerVersion} (URL: {WinnerUrl}, priority rank {WinnerRank}, resolution: explicit priority)",
                code,
                resourceType,
                conflictInfo,
                winner.Candidate.Metadata.PackageId,
                winner.Candidate.Metadata.PackageVersion,
                winner.Candidate.Parameter.Url,
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
                $"{s.Candidate.Metadata.PackageId}#{s.Candidate.Metadata.PackageVersion} (URL: {s.Candidate.Parameter.Url})"));

            // Debug level: Expected behavior when multiple versions of same parameter exist
            _logger.LogDebug(
                "SearchParameter '{Code}' for {ResourceType}: Conflict between [{Conflicts}]. " +
                "Winner: {WinnerPackage}#{WinnerVersion} (URL: {WinnerUrl}, resolution: semantic version {WinnerSemanticVersion})",
                code,
                resourceType,
                conflictInfo,
                winner.Candidate.Metadata.PackageId,
                winner.Candidate.Metadata.PackageVersion,
                winner.Candidate.Parameter.Url,
                winner.Version ?? new SemanticVersion(0, 0, 0));
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
    /// Validates that all candidates apply to the specified resource type.
    /// This ensures per-resource-type conflict resolution semantics per FHIR spec.
    ///
    /// IMPORTANT: This is a defensive check that catches bugs in grouping logic.
    /// All candidates passed to ResolveConflict should have been selected because they
    /// apply to the specified resourceType. If this validation fails, there's a bug
    /// in the caller's grouping logic (e.g., MergeAllSearchParameters).
    /// </summary>
    /// <remarks>
    /// FHIR Semantics: Search parameters are scoped to resource types via BaseResourceTypes.
    /// Example: "identifier" on Patient is a different parameter than "identifier" on Organization.
    /// Conflicts should be resolved independently per resource type.
    /// </remarks>
    private void ValidateCandidatesApplyToResourceType(
        IReadOnlyList<SearchParameterInfo> candidates,
        string code,
        string resourceType)
    {
        var invalidCandidates = candidates
            .Where(c => c.BaseResourceTypes == null ||
                       c.BaseResourceTypes.Count == 0 ||
                       !c.BaseResourceTypes.Contains(resourceType, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (invalidCandidates.Count > 0)
        {
            // This is a bug in the grouping logic - log it as an error
            var invalidDetails = string.Join(", ", invalidCandidates.Select(c =>
                $"URL:{c.Url} BaseResourceTypes:[{string.Join(",", c.BaseResourceTypes ?? Array.Empty<string>())}]"));

            _logger.LogError(
                "INTERNAL ERROR: SearchParameter '{Code}' for {ResourceType}: " +
                "{Count} candidates do not apply to this resource type. " +
                "Invalid candidates: [{InvalidDetails}]. " +
                "This indicates a bug in the grouping/merging logic.",
                code,
                resourceType,
                invalidCandidates.Count,
                invalidDetails);

            // Note: We continue anyway to avoid breaking resolution, but this is a serious bug indicator
        }
    }

    /// <summary>
    /// Populates the OverridesUrl property on the winner if it overrides a base FHIR parameter.
    /// This allows database syncing to alias package parameters to base parameter IDs.
    /// </summary>
    /// <param name="winner">The winning search parameter after conflict resolution.</param>
    /// <param name="allCandidates">All candidates that were considered (including the winner).</param>
    private void PopulateOverridesUrl(
        SearchParameterInfo winner,
        List<EnrichedCandidate> allCandidates)
    {
        if (winner == null || allCandidates.Count <= 1)
        {
            return;
        }

        // Find base FHIR parameter(s) among losers
        var baseParameter = allCandidates
            .Where(c => c.Parameter != winner)  // Exclude the winner itself
            .Where(c => IsBaseFhirParameter(c.Metadata))
            .Select(c => c.Parameter)
            .FirstOrDefault();

        if (baseParameter != null && baseParameter.Url != null && winner.Url != baseParameter.Url)
        {
            winner.OverridesUrl = baseParameter.Url;
            // Debug level: Expected behavior when IGs override base FHIR parameters
            _logger.LogDebug(
                "SearchParameter {WinnerUrl} overrides base parameter {BaseUrl} (code: {Code})",
                winner.Url,
                baseParameter.Url,
                winner.Code);
        }
    }

    /// <summary>
    /// Checks if a package is a base FHIR specification package (not an IG).
    /// </summary>
    private static bool IsBaseFhirParameter(PackageMetadata metadata)
    {
        // Base FHIR packages follow pattern: hl7.fhir.r{version}.core
        return metadata.PackageId.StartsWith("hl7.fhir.r", StringComparison.OrdinalIgnoreCase) &&
               metadata.PackageId.EndsWith(".core", StringComparison.OrdinalIgnoreCase);
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

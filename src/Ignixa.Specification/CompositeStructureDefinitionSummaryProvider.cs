// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Collections.Concurrent;
using Ignixa.Abstractions;
using Ignixa.Domain;
using Ignixa.Domain.Abstractions;
using Ignixa.Serialization;
using Microsoft.Extensions.Logging;

namespace Ignixa.Specification;

/// <summary>
/// Composite provider that combines base FHIR spec definitions with loaded Implementation Guide profiles.
/// Implements a fallback chain: loaded packages first, then base FHIR spec.
/// Per-tenant caching ensures different tenants can have different IGs loaded.
/// </summary>
public class CompositeStructureDefinitionSummaryProvider : IFhirSchemaProvider
{
    private readonly IStructureDefinitionSummaryProvider _baseProvider;
    private readonly IPackageResourceRepository _packageRepository;
    private readonly IPackageResourceProvider _packageResourceProvider;
    private readonly string? _fhirVersion;
    private readonly ILogger<CompositeStructureDefinitionSummaryProvider> _logger;

    // Per-tenant cache: ConcurrentDictionary<canonicalUrl, IStructureDefinitionSummary?>
    private readonly ConcurrentDictionary<string, IStructureDefinitionSummary?> _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositeStructureDefinitionSummaryProvider"/> class.
    /// </summary>
    /// <param name="baseProvider">The base FHIR specification provider (e.g., R4, R4B, R5).</param>
    /// <param name="packageRepository">Repository for querying loaded package resources.</param>
    /// <param name="packageResourceProvider">Adapter to convert package JSON to IStructureDefinitionSummary.</param>
    /// <param name="fhirVersion">Optional: FHIR version to filter package resources (e.g., "4.0.1").</param>
    /// <param name="logger">Logger instance.</param>
    public CompositeStructureDefinitionSummaryProvider(
        IStructureDefinitionSummaryProvider baseProvider,
        IPackageResourceRepository packageRepository,
        IPackageResourceProvider packageResourceProvider,
        string? fhirVersion,
        ILogger<CompositeStructureDefinitionSummaryProvider> logger)
    {
        _baseProvider = baseProvider;
        _packageRepository = packageRepository;
        _packageResourceProvider = packageResourceProvider;
        _fhirVersion = fhirVersion;
        _logger = logger;
        _cache = new ConcurrentDictionary<string, IStructureDefinitionSummary?>();

        if (!(_baseProvider is IFhirSchemaProvider))
        {
            _logger.LogWarning("Base provider does not implement IFhirSchemaProvider. Version info will be unavailable.");
        }
    }

    /// <summary>
    /// Provides a structure definition summary by canonical URL.
    /// Checks loaded packages first, then falls back to base FHIR spec.
    /// Results are cached per tenant.
    /// </summary>
    /// <param name="canonical">The canonical URL of the StructureDefinition.</param>
    /// <returns>The structure definition summary if found, null otherwise.</returns>
    public IStructureDefinitionSummary? Provide(string canonical)
    {
        // Check cache first (atomic)
        if (_cache.TryGetValue(canonical, out var cached))
        {
            return cached;
        }

        // Attempt to load from packages (Phase 1: synchronous using Task.Run)
        // TODO Phase 2: make this async with async Provide() method
        var packageSummary = TryGetFromPackages(canonical);
        if (packageSummary != null)
        {
            _cache[canonical] = packageSummary;
            _logger.LogDebug("Resolved {Canonical} from loaded packages (FHIR version: {FhirVersion})", canonical, _fhirVersion ?? "any");
            return packageSummary;
        }

        // Fall back to base spec provider
        var baseSummary = _baseProvider.Provide(canonical);
        _cache[canonical] = baseSummary; // Cache null results too

        if (baseSummary != null)
        {
            _logger.LogDebug("Resolved {Canonical} from base FHIR spec (FHIR version: {FhirVersion})", canonical, _fhirVersion ?? "any");
        }
        else
        {
            _logger.LogWarning("Could not resolve {Canonical} from packages or base spec (FHIR version: {FhirVersion})", canonical, _fhirVersion ?? "any");
        }

        return baseSummary;
    }

    /// <summary>
    /// Clears the per-tenant cache.
    /// Should be called when packages are loaded/unloaded for this tenant.
    /// </summary>
    public void ClearCache()
    {
        _cache.Clear();
        _logger.LogInformation("Cleared composite provider cache (FHIR version: {FhirVersion})", _fhirVersion ?? "any");
    }

    /// <summary>
    /// The FHIR specification version (e.g., R4, R5, STU3).
    /// Inherited from base provider - all resources come from the same FHIR version.
    /// </summary>
    public FhirSpecification Version
    {
        get
        {
            var baseProvider = _baseProvider as IFhirSchemaProvider;
            if (baseProvider != null)
            {
                return baseProvider.Version;
            }

            // Fallback: try to infer from FullVersion
            return FhirSpecification.R4; // Default fallback
        }
    }

    /// <summary>
    /// Full FHIR version string (e.g., "4.0.1", "5.0.0").
    /// Inherited from base provider.
    /// </summary>
    public string FullVersion
    {
        get
        {
            var baseProvider = _baseProvider as IFhirSchemaProvider;
            return baseProvider?.FullVersion ?? "unknown";
        }
    }

    /// <summary>
    /// Combined set of resource type names from both base spec and loaded packages.
    /// Merges base provider resource types with custom types from loaded IGs.
    /// </summary>
    public IReadOnlySet<string> ResourceTypeNames
    {
        get
        {
            var baseProvider = _baseProvider as IFhirSchemaProvider;
            if (baseProvider == null)
            {
                return new HashSet<string>();
            }

            // Start with base FHIR spec resource types
            var resourceTypes = new HashSet<string>(baseProvider.ResourceTypeNames, StringComparer.OrdinalIgnoreCase);

            // Phase 1: Empty set of custom types from packages
            // Phase 2: Query PackageResourceRepository for custom profile resource types
            // For now, we just return base spec types since PackageResourceProvider returns null

            _logger.LogDebug(
                "Resource type names resolved for FHIR {Version} + {PackageCount} packages",
                baseProvider.FullVersion,
                0); // TODO: count loaded packages

            return resourceTypes;
        }
    }

    private IStructureDefinitionSummary? TryGetFromPackages(string canonical)
    {
        // DESIGN NOTE: Phase 1 uses blocking call with Task.Run to bridge async/sync gap
        // Phase 2 will make Provide() async and handle CancellationToken properly
        try
        {
            // Use Task.Run to safely block on async operation
            var resources = Task.Run(async () =>
                await _packageRepository.GetStructureDefinitionsByCanonicalAsync(
                    canonical,
                    _fhirVersion,
                    CancellationToken.None)).GetAwaiter().GetResult();

            if (resources.Count == 0)
            {
                return null;
            }

            // Use the first (newest) version
            var packageResource = resources[0];

            // Convert JSON to IStructureDefinitionSummary
            var summary = _packageResourceProvider.ToStructureDefinitionSummary(
                packageResource.ResourceJson,
                packageResource.FhirVersion);

            if (summary == null)
            {
                _logger.LogWarning(
                    "Failed to parse StructureDefinition {Canonical} from package {PackageId}@{PackageVersion}",
                    canonical,
                    packageResource.PackageId,
                    packageResource.PackageVersion);
            }
            else
            {
                _logger.LogDebug(
                    "Loaded StructureDefinition {Canonical} from package {PackageId}@{PackageVersion}",
                    canonical,
                    packageResource.PackageId,
                    packageResource.PackageVersion);
            }

            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading {Canonical} from package repository (FHIR version: {FhirVersion})", canonical, _fhirVersion ?? "any");
            return null;
        }
    }
}

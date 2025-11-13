// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Collections.Concurrent;
using Ignixa.Abstractions;
using Ignixa.Domain.Abstractions;
using Ignixa.Specification;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Specification;

/// <summary>
/// Composite provider that combines base FHIR spec definitions with loaded Implementation Guide profiles.
/// Implements a fallback chain: loaded packages first, then base FHIR spec.
/// Per-tenant caching ensures different tenants can have different IGs loaded.
/// </summary>
public class CompositeStructureDefinitionSummaryProvider : IFhirSchemaProvider
{
    private readonly IFhirSchemaProvider _baseProvider;
    private readonly IPackageResourceRepository _packageRepository;
    private readonly IPackageResourceProvider _packageResourceProvider;
    private readonly string? _fhirVersion;
    private readonly ILogger<CompositeStructureDefinitionSummaryProvider> _logger;

    // Per-tenant cache: ConcurrentDictionary<canonicalUrl, IType?>
    private readonly ConcurrentDictionary<string, IType?> _cache;

    // Lazy cache for ResourceTypeNames to avoid repeated database queries
    // Non-readonly to allow recreation in ClearCache()
    private Lazy<IReadOnlySet<string>> _resourceTypeNamesCache;

    // Eager loading: all package StructureDefinitions loaded at startup
    private bool _isInitialized;
    private readonly ConcurrentDictionary<string, IType> _packageStructureDefinitions;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositeStructureDefinitionSummaryProvider"/> class.
    /// </summary>
    /// <param name="baseProvider">The base FHIR specification provider (e.g., R4, R4B, R5).</param>
    /// <param name="packageRepository">Repository for querying loaded package resources.</param>
    /// <param name="packageResourceProvider">Adapter to convert package JSON to IType.</param>
    /// <param name="fhirVersion">Optional: FHIR version to filter package resources (e.g., "4.0.1").</param>
    /// <param name="logger">Logger instance.</param>
    public CompositeStructureDefinitionSummaryProvider(
        IFhirSchemaProvider baseProvider,
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
        _cache = new ConcurrentDictionary<string, IType?>();
        _packageStructureDefinitions = new ConcurrentDictionary<string, IType>();

        // Initialize lazy cache for ResourceTypeNames
        _resourceTypeNamesCache = new Lazy<IReadOnlySet<string>>(ComputeResourceTypeNames, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// Initializes the provider by eagerly loading all package StructureDefinitions.
    /// Should be called once after construction during tenant preloading.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
        {
            _logger.LogDebug("Already initialized - skipping");
            return;
        }

        var startTime = DateTime.UtcNow;
        _logger.LogInformation(
            "Eagerly loading package StructureDefinitions for FHIR version {FhirVersion}",
            _fhirVersion ?? "any");

        try
        {
            // Load all StructureDefinitions from packages in one query
            var packageResources = await _packageRepository.GetAllStructureDefinitionsAsync(
                _fhirVersion,
                cancellationToken).ConfigureAwait(false);

            if (packageResources.Count == 0)
            {
                _logger.LogInformation(
                    "No package StructureDefinitions found for FHIR version {FhirVersion}",
                    _fhirVersion ?? "any");
                _isInitialized = true;
                return;
            }

            var successCount = 0;
            var failureCount = 0;
            var packageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var packageResource in packageResources)
            {
                try
                {
                    // TODO: Implement IPackageResourceProvider.ToTypeDefinition to convert package JSON to IType
                    // For now, this returns null until ToTypeDefinition() is fully implemented
                    var typeDefinition = _packageResourceProvider.ToTypeDefinition(
                        packageResource.ResourceJson,
                        packageResource.FhirVersion);

                    if (typeDefinition != null)
                    {
                        // Use canonical URL from PackageResource (already extracted from resource.url during package loading)
                        // This is the authoritative source - handles both base spec and IG canonical URLs correctly
                        var canonical = packageResource.Canonical;

                        if (!string.IsNullOrEmpty(canonical))
                        {
                            _packageStructureDefinitions.TryAdd(canonical, typeDefinition);
                            successCount++;

                            // Track package metadata
                            packageIds.Add($"{packageResource.PackageId}@{packageResource.PackageVersion}");

                            _logger.LogTrace(
                                "Loaded StructureDefinition {Canonical} (TypeName: {TypeName}) from package {PackageId}@{PackageVersion}",
                                canonical,
                                typeDefinition.Info.Name,
                                packageResource.PackageId,
                                packageResource.PackageVersion);
                        }
                        else
                        {
                            failureCount++;
                            _logger.LogWarning(
                                "StructureDefinition from package {PackageId}@{PackageVersion} (resource ID: {ResourceId}) has no canonical URL",
                                packageResource.PackageId,
                                packageResource.PackageVersion,
                                packageResource.ResourceId);
                        }
                    }
                    else
                    {
                        failureCount++;
                        _logger.LogWarning(
                            "Failed to parse StructureDefinition from package {PackageId}@{PackageVersion} (resource ID: {ResourceId})",
                            packageResource.PackageId,
                            packageResource.PackageVersion,
                            packageResource.ResourceId);
                    }
                }
                catch (Exception ex)
                {
                    failureCount++;
                    _logger.LogWarning(ex,
                        "Failed to parse StructureDefinition from package {PackageId}@{PackageVersion} (resource ID: {ResourceId})",
                        packageResource.PackageId,
                        packageResource.PackageVersion,
                        packageResource.ResourceId);
                }
            }

            _isInitialized = true;

            var elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogInformation(
                "Eagerly loaded {SuccessCount} StructureDefinitions from {PackageCount} packages in {ElapsedMs}ms (FHIR version: {FhirVersion}, {FailureCount} failures)",
                successCount,
                packageIds.Count,
                elapsedMs,
                _fhirVersion ?? "any",
                failureCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error eagerly loading package StructureDefinitions for FHIR version {FhirVersion}",
                _fhirVersion ?? "any");

            // Mark as initialized to avoid repeated failures
            _isInitialized = true;

            _logger.LogWarning(
                "Falling back to base spec StructureDefinitions only due to eager load error");
        }
    }

    /// <summary>
    /// Retrieves type metadata for the specified FHIR type.
    /// Checks pre-loaded packages first, then falls back to base FHIR spec.
    /// Results are cached per request to avoid repeated lookups.
    /// </summary>
    /// <param name="typeName">FHIR type name (e.g., "Patient", "HumanName", "string") or canonical URL.</param>
    /// <returns>Type metadata or null if type not found.</returns>
    public IType? GetTypeDefinition(string typeName)
    {
        // Check cache first (atomic)
        if (_cache.TryGetValue(typeName, out var cached))
        {
            return cached;
        }

        // Check pre-loaded package StructureDefinitions (eager loading)
        if (_isInitialized && _packageStructureDefinitions.TryGetValue(typeName, out var packageType))
        {
            _cache[typeName] = packageType;
            _logger.LogDebug("Resolved {TypeName} from loaded packages (FHIR version: {FhirVersion})", typeName, _fhirVersion ?? "any");
            return packageType;
        }

        // Fall back to base spec provider
        var baseType = _baseProvider.GetTypeDefinition(typeName);
        _cache[typeName] = baseType; // Cache null results too

        if (baseType == null)
        {
            _logger.LogWarning("Could not resolve {TypeName} from packages or base spec (FHIR version: {FhirVersion})", typeName, _fhirVersion ?? "any");
        }

        return baseType;
    }

    /// <summary>
    /// Checks if the specified type name is a valid FHIR type in this schema.
    /// </summary>
    /// <param name="typeName">FHIR type name to check.</param>
    /// <returns>True if the type is known in this schema; otherwise, false.</returns>
    public bool IsKnownType(string typeName)
    {
        // Check package types first
        if (_isInitialized && _packageStructureDefinitions.ContainsKey(typeName))
        {
            return true;
        }

        // Fall back to base spec provider
        return _baseProvider.IsKnownType(typeName);
    }

    /// <summary>
    /// Clears the per-tenant cache.
    /// Should be called when packages are loaded/unloaded for this tenant.
    /// Requires re-initialization via InitializeAsync() after clearing.
    /// </summary>
    public void ClearCache()
    {
        _cache.Clear();
        _packageStructureDefinitions.Clear();

        // Reset lazy cache for ResourceTypeNames to pick up newly loaded packages
        _resourceTypeNamesCache = new Lazy<IReadOnlySet<string>>(ComputeResourceTypeNames, LazyThreadSafetyMode.ExecutionAndPublication);

        // Reset initialization state to force reload
        _isInitialized = false;

        _logger.LogInformation("Cleared composite provider cache (FHIR version: {FhirVersion})", _fhirVersion ?? "any");
    }

    /// <summary>
    /// The FHIR specification version (e.g., R4, R5, Stu3).
    /// Inherited from base provider - all resources come from the same FHIR version.
    /// </summary>
    public FhirVersion Version => _baseProvider.Version;

    /// <summary>
    /// FHIR version for this schema (ISchema.Version).
    /// Converts FhirVersion to FhirVersion enum.
    /// </summary>
    Abstractions.FhirVersion ISchema.Version => Version switch
    {
        FhirVersion.Stu3 => Abstractions.FhirVersion.Stu3,
        FhirVersion.R4 => Abstractions.FhirVersion.R4,
        FhirVersion.R4B => Abstractions.FhirVersion.R4B,
        FhirVersion.R5 => Abstractions.FhirVersion.R5,
        FhirVersion.R6 => Abstractions.FhirVersion.R6,
        _ => Abstractions.FhirVersion.R4 // Default fallback
    };

    /// <summary>
    /// Full FHIR version string (e.g., "4.0.1", "5.0.0").
    /// Inherited from base provider.
    /// </summary>
    public string FullVersion => _baseProvider.FullVersion;

    /// <summary>
    /// Combined set of resource type names from both base spec and loaded packages.
    /// Merges base provider resource types with custom types from loaded IGs.
    /// Cached to avoid repeated database queries.
    /// </summary>
    public IReadOnlySet<string> ResourceTypeNames => _resourceTypeNamesCache.Value;

    /// <summary>
    /// Gets the reference metadata provider for this schema's FHIR version.
    /// Delegates to the base FHIR specification provider.
    /// </summary>
    public IReferenceMetadataProvider ReferenceMetadataProvider => _baseProvider.ReferenceMetadataProvider;

    /// <summary>
    /// Computes the combined resource type names from base spec and loaded packages.
    /// Called lazily on first access to ResourceTypeNames property.
    /// </summary>
    private IReadOnlySet<string> ComputeResourceTypeNames()
    {
        // Start with base FHIR spec resource types
        var resourceTypes = new HashSet<string>(_baseProvider.ResourceTypeNames, StringComparer.OrdinalIgnoreCase);

        // Add custom resource types from loaded packages
        try
        {
            // Query package resources for custom types (profiles, logical models, etc.)
            // These are StructureDefinitions with kind != "resource" or type that's not in base spec
            var packageResourceTypes = GetCustomResourceTypesFromPackages();
            foreach (var customType in packageResourceTypes)
            {
                resourceTypes.Add(customType);
            }

            _logger.LogDebug(
                "Resource type names resolved for FHIR {Version} + {PackageCount} custom types",
                _baseProvider.FullVersion,
                packageResourceTypes.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading custom resource types from packages");
            // Fallback to base spec types only
        }

        return resourceTypes;
    }

    /// <summary>
    /// Extracts custom resource types from loaded packages.
    /// Returns resource types referenced by:
    /// - StructureDefinitions with kind='logical' (custom logical models)
    /// - StructureDefinitions with type not in base spec (custom complex types)
    /// - ViewDefinition resources from SQL on FHIR packages (defines custom resources)
    /// </summary>
    private IReadOnlySet<string> GetCustomResourceTypesFromPackages()
    {
        var customTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            // Phase 2 enhancement: Query package resources for custom resource types
            // This extracts types from ViewDefinition resources (SQL on FHIR v2)
            // which explicitly declare which resource types they operate on.
            // Also extracts StructureDefinitions with custom kinds/types.
            //
            // Design: Uses blocking Task.Run to bridge async/sync gap (like TryGetFromPackages)
            // Phase 3: Will make this async when ResourceTypeNames property becomes async
            var packageTypes = Task.Run(async () =>
                await _packageRepository.GetCustomResourceTypesAsync(_fhirVersion, CancellationToken.None))
                .GetAwaiter()
                .GetResult();

            foreach (var type in packageTypes)
            {
                customTypes.Add(type);
            }

            if (customTypes.Count > 0)
            {
                _logger.LogDebug(
                    "Extracted {Count} custom resource types from packages (FHIR version: {FhirVersion})",
                    customTypes.Count,
                    _fhirVersion ?? "any");
            }

            return customTypes;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting custom resource types from packages");
            return new HashSet<string>();
        }
    }

}

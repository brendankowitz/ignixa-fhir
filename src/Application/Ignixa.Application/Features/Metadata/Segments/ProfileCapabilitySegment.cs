// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Ignixa.Application.Features.Metadata.Models;
using Ignixa.Application.Infrastructure.Caching;
using Ignixa.PackageManagement.Abstractions;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Serialization.Models;

namespace Ignixa.Application.Features.Metadata.Segments;

/// <summary>
/// Profile capability segment.
/// Populates supportedProfile for each resource type when Implementation Guides are loaded.
/// Changes when profiles are loaded/unloaded via $load-ig (Phase 11).
/// </summary>
/// <remarks>
/// Phase 11: Queries IImplementationGuideProvider for loaded packages and populates profiles.
/// Caches loaded package data per tenant to avoid duplicate database queries during capability statement generation.
/// </remarks>
public class ProfileCapabilitySegment : ICapabilitySegment
{
    private readonly ILogger<ProfileCapabilitySegment> _logger;
    private readonly IImplementationGuideProvider _implementationGuideProvider;
    private readonly IPackageResourceRepository _packageResourceRepository;
    private readonly ICapabilityCache _cache;

    public ProfileCapabilitySegment(
        ILogger<ProfileCapabilitySegment> logger,
        IImplementationGuideProvider implementationGuideProvider,
        IPackageResourceRepository packageResourceRepository,
        ICapabilityCache cache)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _implementationGuideProvider = implementationGuideProvider ?? throw new ArgumentNullException(nameof(implementationGuideProvider));
        _packageResourceRepository = packageResourceRepository ?? throw new ArgumentNullException(nameof(packageResourceRepository));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public string SegmentKey => "profiles";

    public int Priority => 40; // Execute after search parameters

    public async ValueTask ApplyAsync(
        CapabilityStatementJsonNode statement,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Applying profile capability segment for {FhirVersion}", context.FhirVersion);

        if (statement.Rest == null || statement.Rest.Count == 0)
        {
            _logger.LogWarning("No REST component found in capability statement - profiles will not be added");
            return;
        }

        var restComponent = statement.Rest[0];
        if (restComponent.Resource == null)
        {
            _logger.LogWarning("No resources found in REST component - profiles will not be added");
            return;
        }

        var tenantId = context.TenantId?.ToString() ?? "1"; // Default to tenant 1 for single-tenant mode

        try
        {
            // Load profile data from cache or database
            var profileData = await GetCachedProfileDataAsync(tenantId, cancellationToken);

            if (profileData.AllStructureDefinitions.Count == 0)
            {
                // No packages loaded - set empty profiles for all resources
                foreach (var resource in restComponent.Resource)
                {
                    resource.SetSupportedProfiles(new List<ReferenceOrCanonicalJsonNode>());
                }

                _logger.LogDebug("Profile capability segment applied (no packages loaded)");
                return;
            }

            // Group StructureDefinitions by the resource type they profile
            var profilesByResourceType = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var structureDefinition in profileData.AllStructureDefinitions)
            {
                // Extract the type this StructureDefinition profiles
                var resourceType = ExtractProfiledResourceType(structureDefinition.ResourceJson);

                if (!string.IsNullOrEmpty(resourceType))
                {
                    if (!profilesByResourceType.ContainsKey(resourceType))
                    {
                        profilesByResourceType[resourceType] = new List<string>();
                    }

                    profilesByResourceType[resourceType].Add(structureDefinition.Canonical);

                    _logger.LogTrace(
                        "Profile {Canonical} profiles resource type {ResourceType}",
                        structureDefinition.Canonical,
                        resourceType);
                }
            }

            // Apply profiles to each resource in the CapabilityStatement
            foreach (var resource in restComponent.Resource)
            {
                if (resource.Type != null && profilesByResourceType.TryGetValue(resource.Type, out var profiles))
                {
                    var supportedProfiles = profiles
                        .Select(canonical => new ReferenceOrCanonicalJsonNode { Reference = canonical })
                        .ToList();

                    resource.SetSupportedProfiles(supportedProfiles);

                    _logger.LogDebug(
                        "Added {Count} supported profiles to resource type {ResourceType}",
                        supportedProfiles.Count,
                        resource.Type);
                }
                else
                {
                    // No profiles found for this resource type
                    resource.SetSupportedProfiles(new List<ReferenceOrCanonicalJsonNode>());
                }
            }

            _logger.LogInformation(
                "Profile capability segment applied for {FhirVersion}. Added profiles for {Count} resource types",
                context.FhirVersion,
                profilesByResourceType.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error loading profiles for capability statement. Profiles will be empty.");

            // Set empty profiles on error to ensure capability statement is still valid
            foreach (var resource in restComponent.Resource)
            {
                resource.SetSupportedProfiles(new List<ReferenceOrCanonicalJsonNode>());
            }
        }
    }

    public async ValueTask<string> GetVersionHashAsync(
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        // Phase 11: Hash is SHA256 of all loaded profile canonical URLs
        var tenantId = context.TenantId?.ToString() ?? "1"; // Default to tenant 1 for single-tenant mode

        try
        {
            // Load profile data from cache or database
            var profileData = await GetCachedProfileDataAsync(tenantId, cancellationToken);

            if (profileData.AllStructureDefinitions.Count == 0)
            {
                const string emptyProfilesHash = "phase11-no-profiles";

                _logger.LogTrace(
                    "Computed profile version hash for {FhirVersion}: {Hash} (no packages loaded)",
                    context.FhirVersion,
                    emptyProfilesHash);

                return emptyProfilesHash;
            }

            // Return pre-computed hash from cached data
            _logger.LogTrace(
                "Computed profile version hash for {FhirVersion}: {Hash} ({Count} profiles)",
                context.FhirVersion,
                profileData.VersionHash,
                profileData.AllStructureDefinitions.Count);

            return profileData.VersionHash;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error computing profile version hash. Using error marker.");
            return "phase11-error";
        }
    }

    /// <summary>
    /// Gets cached profile data for a tenant, loading from database if not cached.
    /// Cache key format: "profiles:{tenantId}"
    /// Uses ICapabilityCache with 5-minute TTL to avoid repeated database queries during capability statement builds.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Profile data containing all StructureDefinitions and pre-computed version hash</returns>
    private async ValueTask<ProfileData> GetCachedProfileDataAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"profiles:{tenantId}";

        // Check cache first
        var cached = await _cache.GetAsync(cacheKey, cancellationToken);

        if (cached != null)
        {
            // Extract ProfileData from cached entry
            // We store ProfileData as the Statement property (repurposed for segment-level caching)
            if (cached.Statement.MutableNode.TryGetPropertyValue("_profileData", out var profileDataNode) && profileDataNode != null)
            {
                var cachedStructureDefinitions = JsonSerializer.Deserialize<List<PackageResource>>(
                    profileDataNode["structureDefinitions"]?.ToJsonString() ?? "[]") ?? new List<PackageResource>();

                var cachedVersionHash = profileDataNode["versionHash"]?.GetValue<string>() ?? "phase11-error";

                _logger.LogTrace(
                    "Cache hit for profiles data for tenant {TenantId} - {Count} StructureDefinitions",
                    tenantId,
                    cachedStructureDefinitions.Count);

                return new ProfileData(cachedStructureDefinitions, cachedVersionHash);
            }
        }

        // Cache miss - query database
        _logger.LogDebug("Cache miss for profiles data for tenant {TenantId} - querying database", tenantId);

        var loadedPackages = await _implementationGuideProvider.ListLoadedPackagesAsync(tenantId, cancellationToken);

        _logger.LogDebug("Found {Count} loaded packages for tenant {TenantId}", loadedPackages.Count, tenantId);

        if (loadedPackages.Count == 0)
        {
            var emptyData = new ProfileData(new List<PackageResource>(), "phase11-no-profiles");

            // Cache empty result with shorter TTL (1 minute) since packages might be loaded soon
            await CacheProfileDataAsync(cacheKey, emptyData, TimeSpan.FromMinutes(1), cancellationToken);

            return emptyData;
        }

        // Collect all StructureDefinitions from loaded packages
        var allStructureDefinitions = new List<PackageResource>();
        var allCanonicals = new List<string>();

        foreach (var (packageId, packageVersion) in loadedPackages)
        {
            _logger.LogTrace("Loading StructureDefinitions from package {PackageId}@{Version}", packageId, packageVersion);

            var packageResources = await _packageResourceRepository.ListPackageResourcesAsync(
                packageId,
                packageVersion,
                resourceType: "StructureDefinition",
                cancellationToken);

            allStructureDefinitions.AddRange(packageResources);
            allCanonicals.AddRange(packageResources.Select(pr => pr.Canonical));
        }

        _logger.LogDebug("Found {Count} StructureDefinitions across all loaded packages", allStructureDefinitions.Count);

        // Compute SHA256 hash for version tracking
        allCanonicals.Sort(StringComparer.Ordinal);
        var canonicalsString = string.Join("|", allCanonicals);
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalsString));
        var versionHash = Convert.ToHexString(hashBytes).ToUpperInvariant();

        var profileData = new ProfileData(allStructureDefinitions, versionHash);

        // Cache with 5-minute TTL
        await CacheProfileDataAsync(cacheKey, profileData, TimeSpan.FromMinutes(5), cancellationToken);

        return profileData;
    }

    /// <summary>
    /// Caches profile data using ICapabilityCache.
    /// Stores data in a repurposed CapabilityCacheEntry (using Statement.MutableNode to hold ProfileData).
    /// </summary>
    private async ValueTask CacheProfileDataAsync(
        string cacheKey,
        ProfileData profileData,
        TimeSpan expiration,
        CancellationToken cancellationToken)
    {
        // Create a minimal CapabilityStatementJsonNode to hold profile data
        // We repurpose the cache entry structure for segment-level caching
        var statement = new CapabilityStatementJsonNode();
        var dataNode = JsonSerializer.SerializeToNode(new
        {
            structureDefinitions = profileData.AllStructureDefinitions,
            versionHash = profileData.VersionHash,
        });

        statement.MutableNode["_profileData"] = dataNode;

        var cacheEntry = new CapabilityCacheEntry(
            statement,
            profileData.VersionHash,
            DateTimeOffset.UtcNow);

        await _cache.SetAsync(cacheKey, cacheEntry, expiration, cancellationToken);

        _logger.LogTrace(
            "Cached profile data for key {CacheKey} with {Count} StructureDefinitions (TTL: {Ttl})",
            cacheKey,
            profileData.AllStructureDefinitions.Count,
            expiration);
    }

    /// <summary>
    /// Extracts the resource type that a StructureDefinition profiles.
    /// Uses StructureDefinitionJsonNode to parse and access the "type" property.
    /// </summary>
    /// <param name="resourceJson">The StructureDefinition JSON</param>
    /// <returns>The resource type being profiled, or null if not found</returns>
    private string? ExtractProfiledResourceType(string resourceJson)
    {
        try
        {
            var structureDefinition = StructureDefinitionJsonNode.Parse(resourceJson, _logger);
            return structureDefinition?.Type;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse StructureDefinition JSON to extract type");
            return null;
        }
    }

    /// <summary>
    /// Internal data structure for caching profile information per tenant.
    /// Contains all StructureDefinitions and a pre-computed version hash.
    /// </summary>
    private record ProfileData(
        List<PackageResource> AllStructureDefinitions,
        string VersionHash);
}

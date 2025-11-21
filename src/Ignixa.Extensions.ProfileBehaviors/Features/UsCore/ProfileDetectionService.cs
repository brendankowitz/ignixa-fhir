// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ignixa.Extensions.ProfileBehaviors.Features.UsCore;

/// <summary>
/// Service to detect when specific FHIR profiles/IGs are loaded for a tenant.
/// Enables profile-specific behaviors to activate conditionally.
/// </summary>
public interface IProfileDetectionService
{
    /// <summary>
    /// Checks if a profile matching the pattern is active for the tenant.
    /// </summary>
    /// <param name="tenantId">The tenant ID.</param>
    /// <param name="profilePattern">Pattern to match (e.g., "hl7.fhir.us.core").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if profile is loaded.</returns>
    Task<bool> IsProfileActiveAsync(int tenantId, string profilePattern, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if US Core is active for the tenant.
    /// </summary>
    Task<bool> IsUSCoreActiveAsync(int tenantId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementation that queries the package repository to detect loaded profiles.
/// </summary>
public sealed class ProfileDetectionService : IProfileDetectionService
{
    private readonly IPackageResourceRepository _packageRepository;
    private readonly ILogger<ProfileDetectionService> _logger;

    // Cache results for performance (invalidated on package load via IPackageLoaded event)
    private readonly Dictionary<(int TenantId, string Pattern), bool> _cache = new();
    private readonly object _cacheLock = new();

    public ProfileDetectionService(
        IPackageResourceRepository packageRepository,
        ILogger<ProfileDetectionService> logger)
    {
        _packageRepository = packageRepository ?? throw new ArgumentNullException(nameof(packageRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> IsUSCoreActiveAsync(int tenantId, CancellationToken cancellationToken = default)
    {
        return await IsProfileActiveAsync(tenantId, "hl7.fhir.us.core", cancellationToken);
    }

    public async Task<bool> IsProfileActiveAsync(
        int tenantId,
        string profilePattern,
        CancellationToken cancellationToken = default)
    {
        // Check cache first
        lock (_cacheLock)
        {
            if (_cache.TryGetValue((tenantId, profilePattern), out var cached))
            {
                return cached;
            }
        }

        // Query repository for packages matching pattern
        // Note: IPackageResourceRepository doesn't have a ListPackages method yet
        // For now, we'll use a heuristic: try to load a known US Core StructureDefinition
        // and if it succeeds, US Core is loaded

        try
        {
            // Try to load a canonical US Core resource (e.g., us-core-patient)
            var testCanonical = GetTestCanonicalForPattern(profilePattern);
            if (testCanonical != null)
            {
                var testResource = await _packageRepository.GetPackageResourceAsync(
                    tenantId.ToString(),
                    testCanonical,
                    cancellationToken);

                var isActive = testResource != null;

                // Cache result
                lock (_cacheLock)
                {
                    _cache[(tenantId, profilePattern)] = isActive;
                }

                _logger.LogDebug(
                    "Profile detection: {Pattern} for tenant {TenantId} = {IsActive}",
                    profilePattern,
                    tenantId,
                    isActive);

                return isActive;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to detect profile {Pattern} for tenant {TenantId}",
                profilePattern,
                tenantId);
            return false;
        }
    }

    /// <summary>
    /// Invalidates the cache for a specific package (called when package is loaded/unloaded).
    /// </summary>
    public void InvalidateCache(int tenantId, string packageId)
    {
        lock (_cacheLock)
        {
            var keysToRemove = _cache.Keys.Where(k => k.TenantId == tenantId).ToList();
            foreach (var key in keysToRemove)
            {
                _cache.Remove(key);
            }
        }

        _logger.LogDebug(
            "Cache invalidated for tenant {TenantId} due to package {PackageId}",
            tenantId,
            packageId);
    }

    /// <summary>
    /// Gets a test canonical URL to check if a profile is loaded.
    /// </summary>
    private static string? GetTestCanonicalForPattern(string pattern)
    {
        return pattern.ToLowerInvariant() switch
        {
            "hl7.fhir.us.core" => "http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient",
            _ => null
        };
    }
}

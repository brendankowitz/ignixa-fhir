// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using EnsureThat;
using Ignixa.Domain;
using Ignixa.Serialization;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Definition;

/// <summary>
/// Version-aware wrapper for CompartmentDefinitionManager.
/// Caches one manager instance per FHIR version for thread-safe multi-version support.
/// </summary>
public sealed class VersionAwareCompartmentDefinitionManager : ICompartmentDefinitionManager, IDisposable
{
    private readonly ConcurrentDictionary<FhirSpecification, CompartmentDefinitionManager> _managerCache;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _disposed;

    public VersionAwareCompartmentDefinitionManager()
    {
        _managerCache = new ConcurrentDictionary<FhirSpecification, CompartmentDefinitionManager>();
    }

    /// <summary>
    /// Gets or creates the CompartmentDefinitionManager for the specified FHIR version.
    /// Thread-safe with double-check locking pattern.
    /// </summary>
    private async Task<CompartmentDefinitionManager> GetManagerAsync(FhirSpecification version, CancellationToken cancellationToken = default)
    {
        // Fast path: check cache
        if (_managerCache.TryGetValue(version, out var cachedManager))
        {
            return cachedManager;
        }

        // Slow path: create and initialize new manager
        await _initializationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Double-check after acquiring lock
            if (_managerCache.TryGetValue(version, out cachedManager))
            {
                return cachedManager;
            }

            // Create new manager with version-specific compartment definitions
            var manager = new CompartmentDefinitionManager(version);

            // Cache and return
            _managerCache.TryAdd(version, manager);
            return manager;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    // ICompartmentDefinitionManager implementation - delegates to version-specific manager
    // Note: These methods don't have version parameter, so they'll use R4 by default
    // Callers should use GetManagerForVersionAsync() directly for version-aware access

    public bool TryGetSearchParams(string resourceType, CompartmentType compartmentType, out HashSet<string> searchParams)
    {
        return GetManagerAsync(FhirSpecification.R4).GetAwaiter().GetResult()
            .TryGetSearchParams(resourceType, compartmentType, out searchParams);
    }

    public bool TryGetResourceTypes(CompartmentType compartmentType, out HashSet<string> resourceTypes)
    {
        return GetManagerAsync(FhirSpecification.R4).GetAwaiter().GetResult()
            .TryGetResourceTypes(compartmentType, out resourceTypes);
    }

    /// <summary>
    /// Gets the version-specific manager for explicit version-aware access.
    /// This is the preferred method for version-aware components.
    /// </summary>
    public Task<CompartmentDefinitionManager> GetManagerForVersionAsync(FhirSpecification version, CancellationToken cancellationToken = default)
    {
        return GetManagerAsync(version, cancellationToken);
    }

    /// <summary>
    /// Disposes the SemaphoreSlim used for thread synchronization.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _initializationLock?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

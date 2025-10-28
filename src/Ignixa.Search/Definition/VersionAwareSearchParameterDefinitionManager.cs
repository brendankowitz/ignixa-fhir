// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Ignixa.Domain;
using Ignixa.SourceNodeSerialization;
using Ignixa.Specification;
using Ignixa.Search.Models;
using Ignixa.SourceNodeSerialization.Abstractions;

namespace Ignixa.Search.Definition;

/// <summary>
/// Version-aware wrapper for SearchParameterDefinitionManager.
/// Caches one manager instance per FHIR version for thread-safe multi-version support.
/// </summary>
public sealed class VersionAwareSearchParameterDefinitionManager : ISearchParameterDefinitionManager, IDisposable
{
    private readonly FhirSchemaProviderResolver _providerResolver;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ConcurrentDictionary<FhirSpecification, SearchParameterDefinitionManager> _managerCache;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _disposed;

    public VersionAwareSearchParameterDefinitionManager(
        FhirSchemaProviderResolver providerResolver,
        ILoggerFactory loggerFactory)
    {
        EnsureArg.IsNotNull(providerResolver, nameof(providerResolver));
        EnsureArg.IsNotNull(loggerFactory, nameof(loggerFactory));

        _providerResolver = providerResolver;
        _loggerFactory = loggerFactory;
        _managerCache = new ConcurrentDictionary<FhirSpecification, SearchParameterDefinitionManager>();
    }

    /// <summary>
    /// Gets or creates the SearchParameterDefinitionManager for the specified FHIR version.
    /// Thread-safe with double-check locking pattern.
    /// </summary>
    private async Task<SearchParameterDefinitionManager> GetManagerAsync(FhirSpecification version, CancellationToken cancellationToken = default)
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

            // Create new manager with version-specific provider
            // Manager initializes synchronously in constructor with pre-generated search parameters
            var provider = _providerResolver(version);
            var logger = _loggerFactory.CreateLogger<SearchParameterDefinitionManager>();
            var manager = new SearchParameterDefinitionManager(provider, logger);

            // Cache and return
            _managerCache.TryAdd(version, manager);
            return manager;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    // ISearchParameterDefinitionManager implementation - delegates to version-specific manager
    // Note: These methods don't have version parameter, so they'll use R4 by default
    // Callers should use GetManagerForVersion() directly for version-aware access

    public IEnumerable<SearchParameterInfo> AllSearchParameters =>
        GetManagerAsync(FhirSpecification.R4).GetAwaiter().GetResult().AllSearchParameters;

    public IReadOnlyDictionary<string, string> SearchParameterHashMap =>
        GetManagerAsync(FhirSpecification.R4).GetAwaiter().GetResult().SearchParameterHashMap;

    public IEnumerable<SearchParameterInfo> GetSearchParameters(string resourceType)
    {
        return GetManagerAsync(FhirSpecification.R4).GetAwaiter().GetResult().GetSearchParameters(resourceType);
    }

    public SearchParameterInfo GetSearchParameter(string resourceType, string code)
    {
        return GetManagerAsync(FhirSpecification.R4).GetAwaiter().GetResult().GetSearchParameter(resourceType, code);
    }

    public bool TryGetSearchParameter(string resourceType, string code, out SearchParameterInfo searchParameter)
    {
        return GetManagerAsync(FhirSpecification.R4).GetAwaiter().GetResult().TryGetSearchParameter(resourceType, code, out searchParameter);
    }

    public SearchParameterInfo GetSearchParameter(Uri definitionUri)
    {
        return GetManagerAsync(FhirSpecification.R4).GetAwaiter().GetResult().GetSearchParameter(definitionUri);
    }

    public bool TryGetSearchParameter(Uri definitionUri, out SearchParameterInfo value)
    {
        return GetManagerAsync(FhirSpecification.R4).GetAwaiter().GetResult().TryGetSearchParameter(definitionUri, out value);
    }

    public string GetSearchParameterHashForResourceType(string resourceType)
    {
        return GetManagerAsync(FhirSpecification.R4).GetAwaiter().GetResult().GetSearchParameterHashForResourceType(resourceType);
    }

    public void UpdateSearchParameterHashMap(Dictionary<string, string> updatedSearchParamHashMap)
    {
        GetManagerAsync(FhirSpecification.R4).GetAwaiter().GetResult().UpdateSearchParameterHashMap(updatedSearchParamHashMap);
    }

    public void AddNewSearchParameters(IReadOnlyCollection<ITypedElement> searchParameters, bool calculateHash = true)
    {
        GetManagerAsync(FhirSpecification.R4).GetAwaiter().GetResult().AddNewSearchParameters(searchParameters, calculateHash);
    }

    public void DeleteSearchParameter(string url, bool calculateHash = true)
    {
        GetManagerAsync(FhirSpecification.R4).GetAwaiter().GetResult().DeleteSearchParameter(url, calculateHash);
    }

    public Task Start()
    {
        // Initialize R4 by default
        return GetManagerAsync(FhirSpecification.R4);
    }

    /// <summary>
    /// Gets the version-specific manager for explicit version-aware access.
    /// This is the preferred method for version-aware components.
    /// </summary>
    public Task<SearchParameterDefinitionManager> GetManagerForVersionAsync(FhirSpecification version, CancellationToken cancellationToken = default)
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

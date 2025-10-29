// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Ignixa.Domain;
using Ignixa.Specification;
using Ignixa.Search.Indexing;
using Ignixa.Search.Definition;
using Ignixa.Serialization;
using Ignixa.Specification.Generated;

namespace Ignixa.Search.Infrastructure;

/// <summary>
/// Provides version-specific FHIR context with caching.
/// Thread-safe singleton that creates and caches schema providers, search indexers, and search parameter definition managers per FHIR version.
/// </summary>
public sealed class FhirVersionContext : IFhirVersionContext, IDisposable
{
    private readonly ConcurrentDictionary<FhirSpecification, IFhirSchemaProvider> _schemaProviders = new();
    private readonly ConcurrentDictionary<FhirSpecification, ISearchIndexer> _searchIndexers = new();
    private readonly ConcurrentDictionary<FhirSpecification, ISearchParameterDefinitionManager> _searchParamManagers = new();
    private readonly ConcurrentDictionary<FhirSpecification, ICompartmentDefinitionManager> _compartmentManagers = new();
    private readonly SemaphoreSlim _indexerLock = new(1, 1);
    private readonly SemaphoreSlim _searchParamLock = new(1, 1);
    private readonly SemaphoreSlim _compartmentLock = new(1, 1);
    private readonly ILoggerFactory _loggerFactory;
    private bool _disposed;

    public FhirVersionContext(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    /// <inheritdoc/>
    public IFhirSchemaProvider GetSchemaProvider(FhirSpecification fhirVersion)
    {
        return _schemaProviders.GetOrAdd(fhirVersion, version =>
        {
            return version switch
            {
                FhirSpecification.Stu3 => new Stu3StructureDefinitionSummaryProvider(),
                FhirSpecification.R4 => new R4StructureDefinitionSummaryProvider(),
                FhirSpecification.R4B => new R4BStructureDefinitionSummaryProvider(),
                FhirSpecification.R5 => new R5StructureDefinitionSummaryProvider(),
                FhirSpecification.R6 => new R6StructureDefinitionSummaryProvider(),
                _ => throw new ArgumentException($"Unsupported FHIR version: {version}")
            };
        });
    }

    /// <inheritdoc/>
    public ISearchIndexer GetSearchIndexer(FhirSpecification fhirVersion)
    {
        // Fast path: check if already cached
        if (_searchIndexers.TryGetValue(fhirVersion, out var cachedIndexer))
        {
            return cachedIndexer;
        }

        // IMPORTANT: Call dependency methods BEFORE acquiring lock to avoid nested lock acquisition
        // This prevents potential deadlock where:
        // - Thread A: holds _indexerLock, waits for _searchParamLock (in GetSearchParameterDefinitionManager)
        // - Thread B: holds _searchParamLock, waits for _indexerLock
        var schemaProvider = GetSchemaProvider(fhirVersion);
        var searchParamManager = GetSearchParameterDefinitionManager(fhirVersion);

        // Slow path: create new indexer (synchronous factory with lock)
        _indexerLock.Wait();
        try
        {
            // Double-check after acquiring lock
            if (_searchIndexers.TryGetValue(fhirVersion, out cachedIndexer))
            {
                return cachedIndexer;
            }

            // Create new search indexer
            // Factory initializes synchronously using pre-generated search parameters
            var indexer = SearchIndexerFactory.CreateInstance(schemaProvider, _loggerFactory, searchParamManager);

            // Cache and return
            _searchIndexers.TryAdd(fhirVersion, indexer);
            return indexer;
        }
        finally
        {
            _indexerLock.Release();
        }
    }

    /// <inheritdoc/>
    public ISearchParameterDefinitionManager GetSearchParameterDefinitionManager(FhirSpecification fhirVersion)
    {
        // Fast path: check if already cached
        if (_searchParamManagers.TryGetValue(fhirVersion, out var cachedManager))
        {
            return cachedManager;
        }

        // Slow path: create new manager (synchronous with lock)
        _searchParamLock.Wait();
        try
        {
            // Double-check after acquiring lock
            if (_searchParamManagers.TryGetValue(fhirVersion, out cachedManager))
            {
                return cachedManager;
            }

            // Create new search parameter definition manager
            // Manager initializes synchronously using pre-generated search parameters
            var schemaProvider = GetSchemaProvider(fhirVersion);
            var logger = _loggerFactory.CreateLogger<SearchParameterDefinitionManager>();
            var manager = new SearchParameterDefinitionManager(schemaProvider, logger);

            // Cache and return
            _searchParamManagers.TryAdd(fhirVersion, manager);
            return manager;
        }
        finally
        {
            _searchParamLock.Release();
        }
    }

    /// <inheritdoc/>
    public ICompartmentDefinitionManager GetCompartmentDefinitionManager(FhirSpecification fhirVersion)
    {
        // Fast path: check if already cached
        if (_compartmentManagers.TryGetValue(fhirVersion, out var cachedManager))
        {
            return cachedManager;
        }

        // Slow path: create new manager (synchronous with lock)
        _compartmentLock.Wait();
        try
        {
            // Double-check after acquiring lock
            if (_compartmentManagers.TryGetValue(fhirVersion, out cachedManager))
            {
                return cachedManager;
            }

            // Create new compartment definition manager
            // Manager initializes synchronously using pre-generated compartment definitions
            var manager = new CompartmentDefinitionManager(fhirVersion);

            // Cache and return
            _compartmentManagers.TryAdd(fhirVersion, manager);
            return manager;
        }
        finally
        {
            _compartmentLock.Release();
        }
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

        _indexerLock?.Dispose();
        _searchParamLock?.Dispose();
        _compartmentLock?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

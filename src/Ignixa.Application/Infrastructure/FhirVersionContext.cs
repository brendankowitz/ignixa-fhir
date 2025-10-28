// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Ignixa.Domain;
using Ignixa.Specification;
using Ignixa.Search.Indexing;
using Ignixa.Serialization;
using Ignixa.Specification.Generated;

namespace Ignixa.Application.Infrastructure;

/// <summary>
/// Provides version-specific FHIR context with caching.
/// Thread-safe singleton that creates and caches schema providers and search indexers per FHIR version.
/// </summary>
public sealed class FhirVersionContext : IFhirVersionContext, IDisposable
{
    private readonly ConcurrentDictionary<FhirSpecification, IFhirSchemaProvider> _schemaProviders = new();
    private readonly ConcurrentDictionary<FhirSpecification, ISearchIndexer> _searchIndexers = new();
    private readonly SemaphoreSlim _indexerLock = new(1, 1);
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
            var schemaProvider = GetSchemaProvider(fhirVersion);
            var indexer = SearchIndexerFactory.CreateInstance(schemaProvider, _loggerFactory);

            // Cache and return
            _searchIndexers.TryAdd(fhirVersion, indexer);
            return indexer;
        }
        finally
        {
            _indexerLock.Release();
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
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

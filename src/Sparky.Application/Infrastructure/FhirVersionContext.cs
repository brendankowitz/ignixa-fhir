// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Sparky.Extensions;
using Sparky.Extensions.Schema;
using Sparky.Search.Indexing;
using Sparky.Specification.Schema;

namespace Sparky.Application.Infrastructure;

/// <summary>
/// Provides version-specific FHIR context with caching.
/// Thread-safe singleton that creates and caches schema providers and search indexers per FHIR version.
/// </summary>
public sealed class FhirVersionContext : IFhirVersionContext, IDisposable
{
    private readonly ConcurrentDictionary<string, IFhirSchemaProvider> _schemaProviders = new();
    private readonly ConcurrentDictionary<string, ISearchIndexer> _searchIndexers = new();
    private readonly SemaphoreSlim _indexerLock = new(1, 1);
    private readonly ILoggerFactory _loggerFactory;
    private bool _disposed;

    public FhirVersionContext(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    /// <inheritdoc/>
    public IFhirSchemaProvider GetSchemaProvider(string fhirVersion)
    {
        return _schemaProviders.GetOrAdd(fhirVersion, version =>
        {
            var fhirSpec = ParseFhirVersion(version);
            return new FhirJsonSchemaStructureDefinitionSummaryProvider(fhirSpec);
        });
    }

    /// <inheritdoc/>
    public async ValueTask<ISearchIndexer> GetSearchIndexerAsync(string fhirVersion, CancellationToken cancellationToken = default)
    {
        // Fast path: check if already cached
        if (_searchIndexers.TryGetValue(fhirVersion, out var cachedIndexer))
        {
            return cachedIndexer;
        }

        // Slow path: create new indexer (async factory requires lock)
        await _indexerLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (_searchIndexers.TryGetValue(fhirVersion, out cachedIndexer))
            {
                return cachedIndexer;
            }

            // Create new search indexer
            var schemaProvider = GetSchemaProvider(fhirVersion);
            var indexer = await SearchIndexerFactory.CreateInstance(schemaProvider, _loggerFactory);

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
    /// Parses FHIR version string to FhirSpecification enum.
    /// </summary>
    /// <param name="fhirVersion">FHIR version string (e.g., "4.0", "5.0", "3.0").</param>
    /// <returns>FhirSpecification enum value.</returns>
    private static FhirSpecification ParseFhirVersion(string fhirVersion)
    {
        return fhirVersion switch
        {
            "3.0" => FhirSpecification.Stu3,
            "4.0" => FhirSpecification.R4,
            "4.3" => FhirSpecification.R4B,
            "5.0" => FhirSpecification.R5,
            _ => FhirSpecification.R4 // Default to R4
        };
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
